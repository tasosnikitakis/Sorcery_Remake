// ============================================================================
// EDITOR COMMANDS
// SorceryForge — one undoable action, in one object
// ============================================================================
// EDITOR_REVIEW item 11. Before this file, Ctrl+Z undid exactly one thing: a
// background brush stroke. Everything else an author does in a session —
// dropping an entity, dragging it two pixels, retargeting a door, painting a
// wall, setting the player spawn — was permanent the moment it happened. That
// is not a missing convenience; it is what makes an editor frightening to use,
// because the only way back from a wrong nudge is to remember where the thing
// used to be.
//
// THE SHAPE. Every action is an object that knows how to do itself and how to
// take itself back:
//
//     IEditorCommand { Label; Do(ctx); Undo(ctx); }
//
// and one stack of them (UndoStack.cs). Ctrl+Z walks it backwards, Ctrl+Y
// walks it forwards, and neither knows what kind of action it is holding. That
// is the whole point of the pattern here: the alternative — a switch over
// "what was the last thing" — is a switch that grows a case per feature and
// silently omits the one somebody forgot.
//
// DEVICE-FREE, DELIBERATELY. Nothing in this file touches Texture2D,
// GraphicsDevice or SpriteBatch; the one thing that genuinely needs pixels
// reaches them through IBackgroundTarget, which EditorGame implements. That is
// the same rule the chrome follows under UI/, and for the same reason: it is
// what lets tools/EditCheck compile these commands and drive every one of them
// with no window and no desktop session. An undo stack is exactly the kind of
// thing that must be exhaustively tested, because its failures are silent —
// a command whose Undo is not the inverse of its Do does not crash, it quietly
// corrupts the room you are editing.
//
// THE PROPERTY EVERY COMMAND MUST HOLD, and that tools/EditCheck asserts one
// class at a time:
//
//     Do(); Undo();        leaves the state it found
//     Do(); Undo(); Do();  leaves the state Do() alone would have
//
// BACKGROUND EDITS STORE A REGION, NOT AN IMAGE. The chrome this replaces kept
// a full 320x144 Color[] per stroke — 180 KB each, forty deep. Doubling that
// for redo at a 64-deep cap would be 23 MB of mostly-identical pixels. So a
// background command stores the CHANGED BOUNDING RECTANGLE and its before/after
// pixels only: a punch is 24x24 (2 KB), a small erase stroke is smaller still,
// and the pathological case — a stroke across the whole image — costs exactly
// what the old scheme cost for every stroke. The transient full clone taken
// while a stroke is in progress is not retained.
//
// WHAT IS NOT UNDOABLE, and why each is a decision rather than an omission:
//
//   The world-map arrangement. Dragging a room on the board sets MapDirty and
//   is written by its own Ctrl+S; it is not per-room working state and it
//   survives every room switch, so it does not belong on a stack that is
//   cleared by one. Noted in doc/07 as out of scope.
//
//   Saving. Ctrl+S is not an edit; undoing "the file was written" would have to
//   mean rewriting the previous file, which is a version-control feature.
//
//   Room creation and screenshot import. Both write files and register a room
//   before loading it, and loading a room clears the stack by design.
//
//   Selection and collapse. Both are ways of looking at the room, not changes
//   to it. Commands DO move the selection — see below — but only as a
//   consequence of the edit they are undoing.
//
// COMMANDS MOVE THE SELECTION ON PURPOSE. Undoing a move that scrolled off the
// far side of a zoomed canvas, with no outline to show you what moved, is an
// undo you cannot verify. So a command that acts on a placement selects it, and
// a command that removes one clears the selection if it was pointing there —
// which also stops Delete and the canvas outline from referring to a placement
// that is no longer in the room's list.
// ============================================================================

using Microsoft.Xna.Framework;
using SorceryRemake.Core;
using System;
using System.Collections.Generic;

namespace SorceryForge
{
    // ========================================================================
    // THE BACKGROUND SURFACE
    // ========================================================================

    /// <summary>
    /// The room's editable background pixels, as an undo command sees them.
    /// Implemented by EditorGame; a harness implements it over a bare array.
    /// </summary>
    // This interface is the ONLY reason a background command can live in a
    // device-free file. EditorGame's pixels are mirrored into a Texture2D, and
    // a Texture2D cannot exist without a GraphicsDevice — so the command writes
    // into the array it is handed and says "these changed", and whoever owns
    // the texture decides what that means.
    //
    // BackgroundPixels is null for a room with no editable PNG (the XNB
    // fallback). A command must survive that rather than assume it away: the
    // stack is cleared on every room switch, so it should be unreachable, and
    // "should be unreachable" is not a guarantee to index an array on.
    public interface IBackgroundTarget
    {
        /// <summary>The live working pixels, or null when the room has no editable PNG.</summary>
        Color[]? BackgroundPixels { get; }

        int BackgroundWidth { get; }
        int BackgroundHeight { get; }

        /// <summary>Called after a command has written into BackgroundPixels.</summary>
        void BackgroundPixelsChanged();
    }

    /// <summary>Everything a command is allowed to reach.</summary>
    // Two references and nothing else. A command cannot see EditorGame, for the
    // same reason a chrome panel cannot: the list of what an undoable action may
    // touch should be readable in one place, and it is this one.
    public sealed class EditorCommandContext
    {
        public readonly EditorState State;
        public readonly IBackgroundTarget Background;

        public EditorCommandContext(EditorState state, IBackgroundTarget background)
        {
            State = state;
            Background = background;
        }
    }

    // ========================================================================
    // THE INTERFACE
    // ========================================================================

    public interface IEditorCommand
    {
        /// <summary>
        /// What this action was, in the words the status line uses:
        /// "Undid: move chateau_0_sword_2".
        /// </summary>
        // A phrase, not a sentence, and it names the ENTITY rather than the
        // kind. "move chateau_0_sword_2" tells you which of the room's four
        // swords came back; "moved an item" does not.
        string Label { get; }

        void Do(EditorCommandContext ctx);
        void Undo(EditorCommandContext ctx);
    }

    // ========================================================================
    // COMPOSITE — one user action that changed two things
    // ========================================================================

    /// <summary>
    /// Several commands that undo and redo as one. Undo runs the parts in
    /// reverse, which is what makes the composite itself invertible.
    /// </summary>
    // Auto-punch is the reason this exists. With it on, ONE click drops a door
    // AND cuts a hole in the background under it; a stack that recorded two
    // commands would need two Ctrl+Z presses to take back one click, and the
    // first of them would leave a hole with a door still standing in it. The
    // same applies to a move-release, which re-punches at the new position.
    public sealed class CompositeCommand : IEditorCommand
    {
        private readonly IEditorCommand[] _parts;

        public string Label { get; }

        public CompositeCommand(string label, params IEditorCommand[] parts)
        {
            Label = label;
            _parts = parts;
        }

        public void Do(EditorCommandContext ctx)
        {
            for (int i = 0; i < _parts.Length; i++) _parts[i].Do(ctx);
        }

        public void Undo(EditorCommandContext ctx)
        {
            for (int i = _parts.Length - 1; i >= 0; i--) _parts[i].Undo(ctx);
        }
    }

    // ========================================================================
    // PLACEMENTS — add, delete, move
    // ========================================================================

    /// <summary>
    /// A placement dropped from the palette. Undo removes it; redo puts it back
    /// at the SAME index, so the room's list order — which is the order the
    /// save path writes — is unchanged by an undo/redo cycle.
    /// </summary>
    // The index matters more than it looks. content_<room>.json is written by
    // walking Placements in order, so re-appending an undone placement at the
    // end would reorder the file for anything that was added after it. That is
    // a diff nobody asked for, and tools/RoundTrip exists to catch exactly this
    // class of gratuitous churn.
    public sealed class AddPlacementCommand : IEditorCommand
    {
        private readonly Placement _placement;
        private readonly int _index;

        public string Label => $"place {_placement.Id}";

        public AddPlacementCommand(Placement placement, int index)
        {
            _placement = placement;
            _index = index;
        }

        public void Do(EditorCommandContext ctx)
        {
            var list = ctx.State.Placements;
            list.Insert(Math.Clamp(_index, 0, list.Count), _placement);
            ctx.State.SelectedPlacement = _placement;
            ctx.State.IsMovingSelection = false;
            ctx.State.Expand(_placement.Id);
            MarkPlacementsChanged(ctx);
        }

        public void Undo(EditorCommandContext ctx)
        {
            ctx.State.Placements.Remove(_placement);
            if (ReferenceEquals(ctx.State.SelectedPlacement, _placement))
            {
                ctx.State.SelectedPlacement = null;
                ctx.State.IsMovingSelection = false;
            }
            MarkPlacementsChanged(ctx);
        }

        /// <summary>
        /// The three flags every placement edit invalidates, in one place.
        /// </summary>
        // Conservative on purpose: a command always dirties what it touches,
        // in BOTH directions. Undoing back to the last-saved state leaves the
        // room marked dirty, which costs one redundant save and never loses an
        // edit — the opposite error loses work silently, which is the entire
        // failure mode the discard guard was built for.
        internal static void MarkPlacementsChanged(EditorCommandContext ctx)
        {
            ctx.State.PlacementsDirty = true;
            ctx.State.HasValidated = false;
            ctx.State.HasValidatedDoors = false;
        }
    }

    /// <summary>
    /// Delete of a selected placement. The exact inverse of the add above, and
    /// written out rather than expressed as one — a command that means the
    /// opposite thing should say the opposite thing in its Label.
    /// </summary>
    public sealed class DeletePlacementCommand : IEditorCommand
    {
        private readonly Placement _placement;
        private readonly int _index;

        public string Label => $"delete {_placement.Id}";

        public DeletePlacementCommand(Placement placement, int index)
        {
            _placement = placement;
            _index = index;
        }

        public void Do(EditorCommandContext ctx)
        {
            ctx.State.Placements.Remove(_placement);
            if (ReferenceEquals(ctx.State.SelectedPlacement, _placement))
            {
                ctx.State.SelectedPlacement = null;
                ctx.State.IsMovingSelection = false;
            }
            AddPlacementCommand.MarkPlacementsChanged(ctx);
        }

        public void Undo(EditorCommandContext ctx)
        {
            var list = ctx.State.Placements;
            list.Insert(Math.Clamp(_index, 0, list.Count), _placement);
            ctx.State.SelectedPlacement = _placement;
            ctx.State.IsMovingSelection = false;
            ctx.State.Expand(_placement.Id);
            AddPlacementCommand.MarkPlacementsChanged(ctx);
        }
    }

    /// <summary>
    /// One drag of one placement, recorded at RELEASE as old position → new.
    /// </summary>
    // Recorded once, at the end, and not per frame: a drag across the canvas
    // produces a new position every frame the mouse moves, and sixty commands
    // for one gesture would fill a 64-deep stack with a single nudge. "One
    // Ctrl+Z per user action" is the rule the punch already followed, and this
    // is the same rule applied to the mouse.
    public sealed class MovePlacementCommand : IEditorCommand
    {
        private readonly Placement _placement;
        private readonly Vector2 _from, _to;

        public string Label => $"move {_placement.Id}";

        public MovePlacementCommand(Placement placement, Vector2 from, Vector2 to)
        {
            _placement = placement;
            _from = from;
            _to = to;
        }

        public void Do(EditorCommandContext ctx) => Apply(ctx, _to);
        public void Undo(EditorCommandContext ctx) => Apply(ctx, _from);

        private void Apply(EditorCommandContext ctx, Vector2 pos)
        {
            _placement.Position = pos;
            // Select what moved. An undo you cannot see is an undo you press
            // twice — and on a zoomed canvas the entity may not even be on
            // screen without the outline to look for.
            ctx.State.SelectedPlacement = _placement;
            ctx.State.IsMovingSelection = false;
            AddPlacementCommand.MarkPlacementsChanged(ctx);
        }
    }

    // ========================================================================
    // PLACEMENT FIELDS — the inspector's editable values
    // ========================================================================

    /// <summary>
    /// Every field of a Placement the inspector can change, captured together.
    /// </summary>
    // ONE struct rather than a command per field, and that is not laziness. A
    // single applied change can write TWO fields: choosing a door's target room
    // also blanks its target door, because a door id is only meaningful inside
    // one room. A per-field command would record one of those two writes and
    // undo half the change — which is worse than not undoing it, because the
    // result is a link that validates as orphan-door and reads like a typo.
    //
    // Position is deliberately NOT here: it is moved by dragging, and
    // MovePlacementCommand records that with its own label. Id is not here
    // either — ids are persistence keys and nothing in the editor renames one.
    public struct PlacementFields : IEquatable<PlacementFields>
    {
        public string DoorOpeningSide;
        public string DoorTargetRoomId;
        public string DoorTargetDoorId;
        public ItemType RequiredItem;

        public static PlacementFields From(Placement p) => new()
        {
            DoorOpeningSide = p.DoorOpeningSide,
            DoorTargetRoomId = p.DoorTargetRoomId,
            DoorTargetDoorId = p.DoorTargetDoorId,
            RequiredItem = p.RequiredItem,
        };

        public readonly void ApplyTo(Placement p)
        {
            p.DoorOpeningSide = DoorOpeningSide;
            p.DoorTargetRoomId = DoorTargetRoomId;
            p.DoorTargetDoorId = DoorTargetDoorId;
            p.RequiredItem = RequiredItem;
        }

        public readonly bool Equals(PlacementFields other) =>
            DoorOpeningSide == other.DoorOpeningSide &&
            DoorTargetRoomId == other.DoorTargetRoomId &&
            DoorTargetDoorId == other.DoorTargetDoorId &&
            RequiredItem == other.RequiredItem;

        public readonly override bool Equals(object? obj) => obj is PlacementFields f && Equals(f);

        public readonly override int GetHashCode() =>
            HashCode.Combine(DoorOpeningSide, DoorTargetRoomId, DoorTargetDoorId, RequiredItem);
    }

    /// <summary>
    /// One applied inspector change: the placement's editable fields before and
    /// after. One command per applied change, never per keystroke.
    /// </summary>
    public sealed class SetPlacementFieldCommand : IEditorCommand
    {
        private readonly Placement _placement;
        private readonly PlacementFields _before, _after;
        private readonly string _what;

        public string Label => $"{_what} on {_placement.Id}";

        public SetPlacementFieldCommand(Placement placement, PlacementFields before,
                                       PlacementFields after, string what)
        {
            _placement = placement;
            _before = before;
            _after = after;
            _what = what;
        }

        public void Do(EditorCommandContext ctx) => Apply(ctx, _after);
        public void Undo(EditorCommandContext ctx) => Apply(ctx, _before);

        private void Apply(EditorCommandContext ctx, in PlacementFields fields)
        {
            fields.ApplyTo(_placement);
            // Select AND expand: the row that changed is inside a section that
            // may well be collapsed, and an undo whose effect is hidden behind
            // a '+' is an undo the user cannot confirm.
            ctx.State.SelectedPlacement = _placement;
            ctx.State.Expand(_placement.Id);
            AddPlacementCommand.MarkPlacementsChanged(ctx);
        }
    }

    // ========================================================================
    // PLAYER SPAWN
    // ========================================================================

    /// <summary>
    /// The room's single player-start point moving, appearing or being cleared.
    /// </summary>
    // ONE class, three factories. Set / Move / Clear are three verbs over one
    // state transition — Vector2? to Vector2? — and three classes would be
    // three copies of the same two assignments differing only in a string. The
    // factories keep the CALL SITES reading as three distinct actions, which is
    // what the status line and the undo label are about.
    public sealed class SetPlayerSpawnCommand : IEditorCommand
    {
        private readonly Vector2? _before, _after;

        public string Label { get; }

        private SetPlayerSpawnCommand(Vector2? before, Vector2? after, string label)
        {
            _before = before;
            _after = after;
            Label = label;
        }

        /// <summary>Dropping the palette's spawn entry into a room that had none.</summary>
        public static SetPlayerSpawnCommand Set(Vector2? before, Vector2 after) =>
            new(before, after, "set player spawn");

        /// <summary>Dragging the spawn marker, or dropping the entry into a room that had one.</summary>
        public static SetPlayerSpawnCommand Move(Vector2 before, Vector2 after) =>
            new(before, after, "move player spawn");

        /// <summary>Delete with the marker selected: back to null, not to the default.</summary>
        public static SetPlayerSpawnCommand Clear(Vector2 before) =>
            new(before, null, "clear player spawn");

        public void Do(EditorCommandContext ctx) => Apply(ctx, _after);
        public void Undo(EditorCommandContext ctx) => Apply(ctx, _before);

        private static void Apply(EditorCommandContext ctx, Vector2? spawn)
        {
            ctx.State.PlayerSpawn = spawn;
            // The spawn and a placement are never both selected — the canvas
            // enforces that, and so does this.
            ctx.State.SelectedPlacement = null;
            ctx.State.IsMovingSelection = false;
            ctx.State.SpawnSelected = spawn.HasValue;
            ctx.State.IsMovingSpawn = false;

            ctx.State.PlacementsDirty = true;   // the spawn rides the layout write
            ctx.State.HasValidated = false;     // the flood-fill origin moved
        }
    }

    // ========================================================================
    // COLLISION TILES
    // ========================================================================

    /// <summary>One cell of the collision grid, with the value on each side of the edit.</summary>
    public readonly struct TileEdit
    {
        public readonly int X, Y, Before, After;

        public TileEdit(int x, int y, int before, int after)
        {
            X = x; Y = y; Before = before; After = after;
        }
    }

    /// <summary>
    /// One paint drag: every cell it changed, with before and after values.
    /// </summary>
    // Per DRAG, not per cell. Painting a wall is one gesture that sets twenty
    // tiles, and twenty undo entries for one swipe is an undo stack you stop
    // using. Cells that the drag re-crossed are recorded once — the editor only
    // appends a cell when its value actually changes, so a second visit to a
    // cell already at the target value contributes nothing.
    public sealed class PaintTilesCommand : IEditorCommand
    {
        private readonly TileEdit[] _edits;

        public string Label => _edits.Length == 1
            ? $"paint 1 tile"
            : $"paint {_edits.Length} tiles";

        public PaintTilesCommand(IReadOnlyList<TileEdit> edits)
        {
            _edits = new TileEdit[edits.Count];
            for (int i = 0; i < edits.Count; i++) _edits[i] = edits[i];
        }

        public void Do(EditorCommandContext ctx) => Apply(ctx, forward: true);
        public void Undo(EditorCommandContext ctx) => Apply(ctx, forward: false);

        private void Apply(EditorCommandContext ctx, bool forward)
        {
            var map = ctx.State.CollisionMap;
            // Null only if the room was reloaded under the stack, which the
            // room-switch clear rules out. Guarded anyway; see the note on
            // IBackgroundTarget.
            if (map == null) return;

            for (int i = 0; i < _edits.Length; i++)
            {
                var e = _edits[i];
                if (e.X < 0 || e.Y < 0 || e.X >= map.Width || e.Y >= map.Height) continue;
                map.SetTile(e.X, e.Y, forward ? e.After : e.Before);
            }

            ctx.State.CollisionDirty = true;
            ctx.State.HasValidated = false;   // geometry changed, old result is stale
        }
    }

    // ========================================================================
    // BACKGROUND PIXELS
    // ========================================================================

    /// <summary>
    /// One background pixel edit — an erase/restore stroke, or a punch — as the
    /// changed rectangle plus its before and after pixels.
    /// </summary>
    // A REGION, not two images. See the file header for the arithmetic; the
    // short version is that a punch is 24x24 and the old scheme charged 320x144
    // for it. The rectangle is the tight bounding box of the pixels that
    // actually differ, so a stroke that changed nothing produces no command at
    // all (FromDiff returns null) — which is the same rule the old EndStroke
    // applied when it dropped the snapshot of a no-op stroke.
    public sealed class BackgroundEditCommand : IEditorCommand
    {
        private readonly Rectangle _region;
        private readonly Color[] _before, _after;

        public string Label { get; }

        private BackgroundEditCommand(Rectangle region, Color[] before, Color[] after, string label)
        {
            _region = region;
            _before = before;
            _after = after;
            Label = label;
        }

        /// <summary>
        /// Build a command from two full-image snapshots, or null when they are
        /// identical. Both arrays must be <paramref name="width"/> ×
        /// <paramref name="height"/>.
        /// </summary>
        // The full-image scan is the transient cost, not the retained one: it
        // runs once per completed stroke over 46,080 pixels and keeps only the
        // rectangle it found.
        public static BackgroundEditCommand? FromDiff(Color[] before, Color[] after,
                                                      int width, int height, string label)
        {
            if (before.Length != width * height || after.Length != width * height) return null;

            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    if (before[row + x] == after[row + x]) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < 0) return null;   // nothing changed

            var region = new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
            return new BackgroundEditCommand(region,
                Slice(before, width, region), Slice(after, width, region), label);
        }

        private static Color[] Slice(Color[] full, int width, Rectangle r)
        {
            var cut = new Color[r.Width * r.Height];
            for (int y = 0; y < r.Height; y++)
                Array.Copy(full, (r.Y + y) * width + r.X, cut, y * r.Width, r.Width);
            return cut;
        }

        public void Do(EditorCommandContext ctx) => Blit(ctx, _after);
        public void Undo(EditorCommandContext ctx) => Blit(ctx, _before);

        private void Blit(EditorCommandContext ctx, Color[] source)
        {
            var px = ctx.Background.BackgroundPixels;
            if (px == null) return;

            int w = ctx.Background.BackgroundWidth;
            int h = ctx.Background.BackgroundHeight;
            // A room whose PNG changed size under the stack cannot be reached —
            // the stack is cleared on every room load — but a bounds check is
            // cheaper than the corruption it prevents.
            if (w <= 0 || h <= 0 || _region.Right > w || _region.Bottom > h) return;

            for (int y = 0; y < _region.Height; y++)
                Array.Copy(source, y * _region.Width,
                           px, (_region.Y + y) * w + _region.X, _region.Width);

            ctx.Background.BackgroundPixelsChanged();
            ctx.State.BackgroundDirty = true;
        }
    }
}
