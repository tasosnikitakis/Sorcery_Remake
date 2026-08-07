using Microsoft.Xna.Framework;
using SorceryRemake.Core;
using SorceryRemake.Rooms;
using SorceryRemake.Tiles;
using System.Collections.Generic;

namespace SorceryForge
{
    /// <summary>What the cursor does when clicked on the canvas.</summary>
    public enum EditorMode
    {
        Place,   // drag entities from the palette, move/delete on canvas
        Paint,   // paint collision tiles directly into the collision grid
        Erase,   // erase background pixels to transparent (GIMP-style brush)
    }

    /// <summary>
    /// All mutable editor state lives here. The EditorGame Update/Draw
    /// methods read and write fields on this object. Keeping the model
    /// out of the MonoGame Game subclass makes it easier to grow features
    /// (undo, multi-select, etc.) without touching frame plumbing.
    /// </summary>
    public class EditorState
    {
        // Place-vs-paint cursor mode (toggled from the top bar).
        public EditorMode Mode = EditorMode.Place;

        // Tracks unsaved collision-grid changes so Save knows whether to
        // rewrite collision_<roomId>.json. Cleared on save and on room load.
        public bool CollisionDirty = false;

        // Tracks unsaved background pixel edits (Erase mode) so Save knows
        // whether to rewrite Content/RoomBG_<x>.png. Cleared on save/load.
        public bool BackgroundDirty = false;

        // Tracks unsaved placement edits (add/move/delete/inspector changes)
        // so room switches and exit can't silently discard them.
        public bool PlacementsDirty = false;

        // Erase-mode brush: side length of the square stamp, in room pixels.
        public int BrushSize = 4;

        // Set of placement IDs the validator marked as unreachable from the
        // hardcoded player spawn. Cleared on every Validate run.
        public readonly HashSet<string> UnreachableIds = new();

        // True iff Validate has been run since the last edit. When false, the
        // unreachable warnings are stale and we hide them.
        public bool HasValidated = false;

        // Door validation. Cleared and refilled by ValidateDoors. Each entry
        // maps a door ID (in any room) to its diagnosis: "ok", "ok-test"
        // (target is a programmatic test room — accepted, far side not
        // verifiable), "orphan-room" (target room doesn't exist),
        // "orphan-door" (target room exists but lacks the named door), or
        // "asymmetric" (target door points somewhere other than back here).
        // Empty until ValidateDoors runs.
        public readonly Dictionary<string, string> DoorStatus = new();
        public bool HasValidatedDoors = false;

        // Puzzle analyzer output. Last cross-room solve report. The set is
        // a per-placement-id index so the canvas renderer can flag flagged
        // entries quickly without walking the full Issues list each frame.
        public PuzzleReport? LastPuzzleReport;
        public readonly HashSet<string> PuzzleProblemIds = new();

        // Right-side inspector panel. Default behaviour: all sections start
        // EXPANDED; collapsed IDs are tracked here. Scroll position is the
        // pixel offset to subtract from each section's Y when rendering.
        public readonly HashSet<string> CollapsedPlacementIds = new();
        public float InspectorScrollY = 0f;

        public bool IsCollapsed(string placementId) =>
            CollapsedPlacementIds.Contains(placementId);

        public void ToggleCollapse(string placementId)
        {
            if (!CollapsedPlacementIds.Remove(placementId))
                CollapsedPlacementIds.Add(placementId);
        }

        public void Expand(string placementId) =>
            CollapsedPlacementIds.Remove(placementId);

        // Left-side palette panel. Same scroll model as the inspector above:
        // a pixel offset subtracted from each row's laid-out Y. Zero until the
        // entry list outgrows the panel, which it will once Phase 5C's full
        // item set lands.
        public float PaletteScrollY = 0f;
        // Available rooms (from RoomMeta.All) and which one is currently loaded.
        public int CurrentRoomIndex = 0;
        public RoomMeta CurrentRoom => RoomMeta.All[CurrentRoomIndex];

        // Working set for the current room: every entity is a Placement.
        public readonly List<Placement> Placements = new();

        // Read-only view of the room's collision data (rendered as overlay).
        public TileMapComponent? CollisionMap;

        // Palette: the catalog of things you can drop onto the canvas.
        public readonly List<PaletteEntry> Palette = new();

        // Drag-from-palette: when non-null, the cursor "carries" this entry.
        // First click on canvas drops it. Right-click cancels.
        public PaletteEntry? Dragging;

        // Click-and-drag of an existing placement. When non-null, mouse moves
        // update SelectedPlacement.Position.
        public Placement? SelectedPlacement;
        public bool IsMovingSelection;
        public Vector2 MoveOffset;          // cursor → top-left offset at drag start

        // Snap-to-tile toggle (8x8 grid). Off by default — current rooms place
        // items at sub-tile coords like (140,72), and forcing snap would round
        // them to multiples of 8 on first move.
        public bool SnapEnabled = false;

        // Auto-punch: when true, dropping a placement (and finishing a move)
        // clears the background under its footprint. Default OFF — explicit
        // punch (P key / inspector) is the primary workflow; auto is for bulk
        // door placement over screenshot rooms.
        public bool AutoPunch = false;

        // ID counter used when generating fresh placement IDs.
        public int NextIdCounter = 1;

        // Status line at the bottom of the screen.
        public string Status = "Ready";

        // Convert the working-set list to RoomContent for saving (skips Door
        // placements — those go to layout JSON via ToRoomLayoutJson).
        public RoomContent ToRoomContent()
        {
            var content = new RoomContent();
            foreach (var p in Placements)
            {
                switch (p.Kind)
                {
                    case PlacementKind.Item:
                        content.Items.Add(new ItemSpawn(p.Id, p.ItemType, p.Position));
                        break;
                    case PlacementKind.Enemy:
                        content.Enemies.Add(new EnemySpawn(p.Id, p.EnemyType, p.Position));
                        break;
                    case PlacementKind.Wizard:
                        content.Wizards.Add(new WizardSpawn(p.Id, p.Position));
                        break;
                    case PlacementKind.BlockedDoor:
                        content.BlockedDoors.Add(new BlockedDoorSpawn(p.Id, p.Position, p.RequiredItem));
                        break;
                }
            }
            return content;
        }

        // Convert Door placements to a RoomLayoutJson for saving.
        public RoomLayoutJson ToRoomLayoutJson(string roomId)
        {
            var layout = new RoomLayoutJson { roomId = roomId };
            foreach (var p in Placements)
            {
                if (p.Kind != PlacementKind.Door) continue;
                layout.doors.Add(new DoorEntryJson
                {
                    id = p.Id,
                    x = p.Position.X,
                    y = p.Position.Y,
                    type = p.DoorOpeningSide,
                    targetRoom = p.DoorTargetRoomId,
                    targetDoor = p.DoorTargetDoorId,
                });
            }
            return layout;
        }

        // Build the working-set list from RoomContent + the room's DoorDefs
        // (loading a room). Both content and layout JSON contribute.
        public void LoadFromRoomContent(RoomContent content, List<DoorDef> doors)
        {
            Placements.Clear();
            SelectedPlacement = null;
            IsMovingSelection = false;
            Dragging = null;

            foreach (var s in content.Items)
                Placements.Add(new Placement(s.Id, PlacementKind.Item, s.Position) { ItemType = s.Type });

            foreach (var s in content.Enemies)
                Placements.Add(new Placement(s.Id, PlacementKind.Enemy, s.Position) { EnemyType = s.Type });

            foreach (var s in content.Wizards)
                Placements.Add(new Placement(s.Id, PlacementKind.Wizard, s.Position));

            foreach (var s in content.BlockedDoors)
                Placements.Add(new Placement(s.Id, PlacementKind.BlockedDoor, s.Position) { RequiredItem = s.RequiredItem });

            foreach (var d in doors)
                Placements.Add(new Placement(d.DoorId, PlacementKind.Door, d.Position)
                {
                    DoorOpeningSide = d.OpeningSide,
                    DoorTargetRoomId = d.TargetRoomId,
                    DoorTargetDoorId = d.TargetDoorId,
                });
        }

        // Generate an ID following the project convention (<roomId>_<kind>_<n>).
        //
        // The counter alone is NOT enough: LoadRoom seeds it from
        // Placements.Count, so deleting an entity and adding another can
        // re-issue a suffix that's still in use (two chateau_0_sword_3).
        // Placement IDs are the game's WorldState persistence keys
        // (PickedUpItems / DeadEnemies / SavedWizards / UnlockedDoors), so a
        // collision would make one entity's state silently apply to another.
        // Spin the counter until the candidate is genuinely free — the ID
        // format stays exactly as documented, we just skip taken numbers.
        public string GenerateId(string roomId, PlacementKind kind, ItemType itemType = ItemType.None, EnemyType enemyType = default)
        {
            string suffix = kind switch
            {
                PlacementKind.Item => itemType.ToString().ToLower(),
                PlacementKind.Enemy => enemyType.ToString().ToLower(),
                PlacementKind.Wizard => "wizard",
                PlacementKind.BlockedDoor => "blockeddoor",
                PlacementKind.Door => "door",
                _ => "entity"
            };

            string candidate;
            do
            {
                candidate = $"{roomId}_{suffix}_{NextIdCounter++}";
            }
            while (HasPlacementId(candidate));
            return candidate;
        }

        private bool HasPlacementId(string id)
        {
            foreach (var p in Placements) if (p.Id == id) return true;
            return false;
        }
    }
}
