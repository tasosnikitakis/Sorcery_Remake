// ============================================================================
// EDITCHECK — SORCERYFORGE LOGIC-LAYER HARNESS
// Sorcery+ Remake
// ============================================================================
// THE INVARIANT IT GUARDS
//
//   "Every editor action is a command, and every command's Undo is the exact
//    inverse of its Do."
//
//   That sentence is the whole of SorceryForge/EditorCommands.cs, and it is
//   the single thing a unified undo stack is most likely to get wrong. It is
//   also INVISIBLE. An undo that restores four of a placement's five fields
//   does not crash, does not draw anything odd, and does not report anything;
//   it leaves the room in a state the author never authored, and the first
//   sign of it is a git diff nobody can explain three commits later.
//
//   So the property is asserted directly, one command class at a time:
//
//       Do(); Undo();          leaves the state it found
//       Do(); Undo(); Do();    leaves the state Do() alone would have
//
//   and the state being compared is the WHOLE of what a command may touch —
//   the placement list and its order, every placement's editable fields, the
//   player spawn, the collision grid and the background pixels — canonicalised
//   into one string, so that a field added to Placement without being added to
//   PlacementFields shows up here rather than in someone's afternoon.
//
// WHY THIS CAN RUN HEADLESS
//
//   Because none of it is drawn. EditorCommands.cs and UndoStack.cs name no
//   Texture2D, no GraphicsDevice and no SpriteBatch; the one part of undo that
//   genuinely moves pixels reaches them through IBackgroundTarget, which this
//   harness implements over a bare Color[]. That is the same design rule the
//   chrome follows under UI/, and it buys the same thing: the logic can be
//   exercised exhaustively without a desktop session.
//
// WHAT IT CANNOT COVER
//
//   Whether Ctrl+Z is BOUND to the stack, and whether the editor closes an
//   in-progress drag before popping it. Those live in EditorGame, which needs
//   a window. tools/ChromeCheck covers the menu's half (Edit > Undo/Redo
//   enablement); the keyboard half is in the owner's smoke pass.
//
// SECTIONS
//
//   1 commands    every command class, through the round-trip property
//   2 stack       redo cleared by a new edit, the depth cap, clear, labels
//   3 registry    rooms.json rename: header preserved, one field changed
//
// HOW TO RUN
//
//   dotnet build tools/EditCheck/EditCheck.csproj
//   dotnet run   --project tools/EditCheck/EditCheck.csproj
//
//   Exit 0 = every check passed. Exit 1 = failures (listed inline as FAIL).
//
// SAFETY
//
//   Writes nothing into the repository. Section 3 works on a scratch copy of
//   rooms.json under the system temp directory and never touches assets/data.
// ============================================================================

using Microsoft.Xna.Framework;
using SorceryForge;
using SorceryRemake.Core;
using SorceryRemake.Rooms;
using SorceryRemake.Tiles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SorceryRemake.Tools.EditCheck
{
    internal static class Program
    {
        private static int _failures;
        private static int _checks;

        private static int Main(string[] args)
        {
            foreach (var arg in args)
            {
                if (arg is "-h" or "--help") { PrintUsage(); return 0; }
                Console.Error.WriteLine($"unknown argument: {arg}");
                PrintUsage();
                return 2;
            }

            Console.WriteLine("EditCheck — SorceryForge undo/redo and registry-edit harness");
            Console.WriteLine();

            CheckCommands();
            CheckStack();
            CheckRegistryEdits();

            Console.WriteLine();
            Console.WriteLine($"  {_checks} checks, {_failures} failure(s)");
            Console.WriteLine();
            Console.WriteLine(_failures == 0
                ? "  UNDO HOLDS: every command's Undo is the inverse of its Do, a new edit\n" +
                  "  clears the redo stack, the depth cap evicts the oldest entry, and a\n" +
                  "  rename rewrites one field of rooms.json and nothing else."
                : "  UNDO BROKEN — see the FAIL lines above.");

            return _failures == 0 ? 0 : 1;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("usage: dotnet run --project tools/EditCheck/EditCheck.csproj");
            Console.WriteLine();
            Console.WriteLine("  Drives SorceryForge's undo/redo commands and the rooms.json");
            Console.WriteLine("  writer. Touches nothing under assets/.");
            Console.WriteLine();
            Console.WriteLine("exit 0 = all checks pass; 1 = failures; 2 = could not run.");
        }

        // ====================================================================
        // 1. COMMANDS — the round-trip property, one class at a time
        // ====================================================================
        // Each block builds a world, snapshots it, runs the command forward,
        // asserts something actually changed (a command that does nothing would
        // otherwise pass every round-trip test ever written), runs it back, and
        // asserts the world is bit-for-bit what it was. Then forward again, to
        // catch a Do that is only correct the first time — the classic shape of
        // that bug is a command that consumes state it captured at Do.
        // ====================================================================

        private static void CheckCommands()
        {
            Section("1. COMMANDS — Undo is the inverse of Do, class by class");

            RoundTrip("AddPlacement", w =>
            {
                var fresh = new Placement("chateau_0_sword_9", PlacementKind.Item, new Vector2(40, 40))
                { ItemType = ItemType.Sword };
                return new AddPlacementCommand(fresh, 1);
            });

            RoundTrip("DeletePlacement", w =>
            {
                var victim = w.State.Placements[1];
                return new DeletePlacementCommand(victim, 1);
            });

            RoundTrip("MovePlacement", w =>
                new MovePlacementCommand(w.State.Placements[0],
                    w.State.Placements[0].Position, new Vector2(200, 100)));

            // ONE ROUND-TRIP PER FIELD, and that is not padding. The first
            // version of this section changed the target room and the target
            // door together, which is the real inspector edit — and it could
            // not see an ApplyTo that had FORGOTTEN to write DoorTargetDoorId
            // at all. Because the room change blanks the door id anyway, a
            // command that never wrote that field left the world looking
            // exactly as a correct one would, in both directions. Every field
            // therefore gets a change of its own, to a DIFFERENT non-empty
            // value, so that not writing it is visible.
            RoundTrip("SetPlacementField (opening side)", w =>
            {
                var before = PlacementFields.From(w.Door);
                var after = before;
                after.DoorOpeningSide = "RightOpening";
                return new SetPlacementFieldCommand(w.Door, before, after, "opening side");
            });

            RoundTrip("SetPlacementField (target door alone)", w =>
            {
                var before = PlacementFields.From(w.Door);
                var after = before;
                after.DoorTargetDoorId = "chateau_1_door_right";
                return new SetPlacementFieldCommand(w.Door, before, after, "target door");
            });

            RoundTrip("SetPlacementField (target room alone)", w =>
            {
                var before = PlacementFields.From(w.Door);
                var after = before;
                after.DoorTargetRoomId = "chateau_2";
                return new SetPlacementFieldCommand(w.Door, before, after, "target room");
            });

            RoundTrip("SetPlacementField (door retarget blanks the door id)", w =>
            {
                var door = w.Door;
                var before = PlacementFields.From(door);
                var after = before;
                after.DoorTargetRoomId = "chateau_2";
                after.DoorTargetDoorId = "";
                return new SetPlacementFieldCommand(door, before, after, "target room");
            });

            RoundTrip("SetPlacementField (blocked door's required item)", w =>
            {
                var blocked = w.Blocked;
                var before = PlacementFields.From(blocked);
                var after = before;
                after.RequiredItem = ItemType.Axe;
                return new SetPlacementFieldCommand(blocked, before, after, "required item");
            });

            // PlacementFields.Equals is what stops a no-op picked value from
            // filling the stack, so it has to notice a change in EVERY field it
            // carries — a comparison that forgot one would let that field's
            // edits through as "nothing changed" and silently stop being
            // undoable.
            var probe = PlacementFields.From(NewWorld().Door);
            AssertFieldDetected("opening side", probe,
                (ref PlacementFields f) => f.DoorOpeningSide = "RightOpening");
            AssertFieldDetected("target room", probe,
                (ref PlacementFields f) => f.DoorTargetRoomId = "chateau_7");
            AssertFieldDetected("target door", probe,
                (ref PlacementFields f) => f.DoorTargetDoorId = "chateau_7_door_a");
            AssertFieldDetected("required item", probe,
                (ref PlacementFields f) => f.RequiredItem = ItemType.ShootingStar);

            RoundTrip("SetPlayerSpawn (move)", w =>
                SetPlayerSpawnCommand.Move(w.State.PlayerSpawn!.Value, new Vector2(64, 96)));

            RoundTrip("SetPlayerSpawn (clear)", w =>
                SetPlayerSpawnCommand.Clear(w.State.PlayerSpawn!.Value));

            RoundTrip("SetPlayerSpawn (set, from a room that had none)", w =>
            {
                w.State.PlayerSpawn = null;
                return SetPlayerSpawnCommand.Set(null, new Vector2(24, 32));
            });

            RoundTrip("PaintTiles (a drag over three cells)", w => new PaintTilesCommand(new[]
            {
                new TileEdit(2, 3, TileConfig.EMPTY, TileConfig.WALL_DARK_GRAY),
                new TileEdit(3, 3, TileConfig.EMPTY, TileConfig.WALL_DARK_GRAY),
                // A cell that was already solid being cleared: the drag's other
                // direction, in the same command.
                new TileEdit(5, 5, TileConfig.WALL_DARK_GRAY, TileConfig.EMPTY),
            }));

            RoundTrip("BackgroundEdit (an erase stroke)", w =>
            {
                var after = (Color[])w.Background.BackgroundPixels!.Clone();
                for (int i = 0; i < 200; i++) after[1000 + i] = Color.Transparent;
                return BackgroundEditCommand.FromDiff(
                    w.Background.BackgroundPixels!, after,
                    w.Background.BackgroundWidth, w.Background.BackgroundHeight, "background stroke")!;
            });

            RoundTrip("Composite (a drop with auto-punch behind it)", w =>
            {
                var fresh = new Placement("chateau_0_door_9", PlacementKind.Door, new Vector2(0, 112));
                var add = new AddPlacementCommand(fresh, w.State.Placements.Count);

                var after = (Color[])w.Background.BackgroundPixels!.Clone();
                for (int y = 112; y < 136; y++)
                for (int x = 0; x < 24; x++)
                    after[y * w.Background.BackgroundWidth + x] = Color.Transparent;
                var punch = BackgroundEditCommand.FromDiff(
                    w.Background.BackgroundPixels!, after,
                    w.Background.BackgroundWidth, w.Background.BackgroundHeight, "punch")!;

                return new CompositeCommand(add.Label, add, punch);
            });

            // ---- what a command must NOT do ---------------------------------

            Section("1b. COMMANDS — the guards, and the no-op rule");

            var identical = new Color[64];
            Assert("FromDiff on two identical images records nothing",
                BackgroundEditCommand.FromDiff(identical, (Color[])identical.Clone(), 8, 8, "x") == null);

            var oneChanged = (Color[])identical.Clone();
            oneChanged[3 * 8 + 5] = Color.Red;
            var tight = BackgroundEditCommand.FromDiff(identical, oneChanged, 8, 8, "x");
            Assert("FromDiff on one changed pixel records a command", tight != null);
            if (tight != null)
            {
                // The point of storing a region: one changed pixel must not
                // cost a whole image. Proved by applying it to a DIFFERENT
                // image and watching exactly one pixel move.
                var canvas = new Color[64];
                for (int i = 0; i < canvas.Length; i++) canvas[i] = Color.Blue;
                var bg = new FakeBackground(canvas, 8, 8);
                var ctx = new EditorCommandContext(new EditorState(), bg);
                tight.Do(ctx);

                int changed = 0;
                for (int i = 0; i < canvas.Length; i++) if (canvas[i] != Color.Blue) changed++;
                Assert("  and touches exactly the one pixel that differed", changed == 1,
                    $"{changed} pixels moved");
                Assert("  which is the pixel it was told about", canvas[3 * 8 + 5] == Color.Red);

                // AND SAYS SO. Writing the array is only half the job: in the
                // editor those pixels are mirrored into a Texture2D, and
                // BackgroundPixelsChanged is the only thing that pushes them.
                // A command that wrote the array and stayed silent would pass
                // every other assertion in this file and leave the author
                // looking at a background that did not change when they pressed
                // Ctrl+Z.
                Assert("  and tells the target its pixels moved", bg.PushCount == 1,
                    bg.PushCount.ToString());
                tight.Undo(ctx);
                Assert("  on the way back as well", bg.PushCount == 2, bg.PushCount.ToString());
                // To the value the COMMAND recorded, which is the source
                // image's, not this canvas's — a command carries its own
                // before-state and does not consult what it is applied to.
                Assert("  and the undo wrote the recorded before-value",
                    canvas[3 * 8 + 5] == identical[3 * 8 + 5],
                    canvas[3 * 8 + 5].ToString());
            }

            // A room with no editable PNG has null pixels. The stack is cleared
            // on every room switch so this should be unreachable — which is
            // exactly why it is asserted rather than assumed.
            var blind = new EditorCommandContext(new EditorState(), new FakeBackground(null, 0, 0));
            var strokeCommand = BackgroundEditCommand.FromDiff(identical, oneChanged, 8, 8, "x")!;
            bool threw = false;
            try { strokeCommand.Do(blind); strokeCommand.Undo(blind); }
            catch (Exception ex) { threw = true; Console.WriteLine("           " + ex.Message); }
            Assert("a background command over a room with no PNG is a quiet no-op", !threw);

            threw = false;
            var noGrid = new EditorCommandContext(new EditorState(), new FakeBackground(null, 0, 0));
            var paint = new PaintTilesCommand(new[] { new TileEdit(0, 0, 0, 1) });
            try { paint.Do(noGrid); paint.Undo(noGrid); }
            catch (Exception ex) { threw = true; Console.WriteLine("           " + ex.Message); }
            Assert("a paint command over a room with no collision grid is a quiet no-op", !threw);

            // Out-of-range cells are skipped rather than thrown on: a hand-
            // edited collision file can be any size the loader accepts.
            threw = false;
            var small = new EditorState { CollisionMap = new TileMapComponent(null, 4, 4) };
            var outside = new EditorCommandContext(small, new FakeBackground(null, 0, 0));
            try { new PaintTilesCommand(new[] { new TileEdit(99, 99, 0, 1) }).Do(outside); }
            catch (Exception ex) { threw = true; Console.WriteLine("           " + ex.Message); }
            Assert("a paint command skips cells outside the grid", !threw);

            // ---- the composite's ordering -----------------------------------
            //
            // Undo must run the parts BACKWARDS. With two commands writing the
            // same cell, forward order and reverse order produce different
            // answers, which is what makes this assertable rather than a
            // matter of taste.
            var order = new EditorState { CollisionMap = new TileMapComponent(null, 4, 4) };
            var orderCtx = new EditorCommandContext(order, new FakeBackground(null, 0, 0));
            var first = new PaintTilesCommand(new[] { new TileEdit(0, 0, 0, 1) });
            var second = new PaintTilesCommand(new[] { new TileEdit(0, 0, 1, 2) });
            var pair = new CompositeCommand("pair", first, second);
            pair.Do(orderCtx);
            Assert("a composite runs its parts forward", order.CollisionMap!.GetTile(0, 0) == 2,
                order.CollisionMap.GetTile(0, 0).ToString());
            pair.Undo(orderCtx);
            Assert("  and backwards on undo, so the pair inverts",
                order.CollisionMap.GetTile(0, 0) == 0, order.CollisionMap.GetTile(0, 0).ToString());

            // ---- selection ----------------------------------------------------
            //
            // Not decoration. A command that removes a placement must not leave
            // SelectedPlacement pointing at it — Delete and the canvas outline
            // both act on that reference, and an object no longer in the room's
            // list is one the author cannot see and cannot reach.
            var sel = NewWorld();
            var target = sel.State.Placements[0];
            sel.State.SelectedPlacement = target;
            new DeletePlacementCommand(target, 0).Do(sel.Context);
            Assert("deleting the selected placement clears the selection",
                sel.State.SelectedPlacement == null);

            sel = NewWorld();
            var added = new Placement("chateau_0_axe_9", PlacementKind.Item, Vector2.Zero);
            var addCmd = new AddPlacementCommand(added, 0);
            addCmd.Do(sel.Context);
            Assert("adding a placement selects it", ReferenceEquals(sel.State.SelectedPlacement, added));
            addCmd.Undo(sel.Context);
            Assert("  and undoing the add clears the selection again",
                sel.State.SelectedPlacement == null);

            // ---- list ORDER ---------------------------------------------------
            //
            // content_<room>.json is written by walking Placements in order, so
            // a redo that appended instead of re-inserting would reorder the
            // file for everything added after it — a diff nobody asked for, and
            // the exact churn tools/RoundTrip exists to catch. The round-trip
            // property alone cannot see this: a command that consistently
            // appends is consistently wrong, and consistency is all that
            // property measures. So the index is pinned directly.
            var order2 = NewWorld();
            var inserted = new Placement("chateau_0_lyre_9", PlacementKind.Item, Vector2.Zero);
            var insertCmd = new AddPlacementCommand(inserted, 1);
            insertCmd.Do(order2.Context);
            Assert("an add lands at the index it was given",
                ReferenceEquals(order2.State.Placements[1], inserted),
                order2.State.Placements.IndexOf(inserted).ToString());
            insertCmd.Undo(order2.Context);
            insertCmd.Do(order2.Context);
            Assert("  and a redo puts it back at that SAME index",
                ReferenceEquals(order2.State.Placements[1], inserted),
                order2.State.Placements.IndexOf(inserted).ToString());

            var order3 = NewWorld();
            var middle = order3.State.Placements[1];
            var delCmd = new DeletePlacementCommand(middle, 1);
            delCmd.Do(order3.Context);
            delCmd.Undo(order3.Context);
            Assert("undoing a delete puts the placement back where it was, not on the end",
                ReferenceEquals(order3.State.Placements[1], middle),
                order3.State.Placements.IndexOf(middle).ToString());

            sel = NewWorld();
            sel.State.CollapsedPlacementIds.Add(sel.Door.Id);
            var fieldsBefore = PlacementFields.From(sel.Door);
            var fieldsAfter = fieldsBefore;
            fieldsAfter.DoorOpeningSide = "RightOpening";
            new SetPlacementFieldCommand(sel.Door, fieldsBefore, fieldsAfter, "opening side").Do(sel.Context);
            Assert("a field change expands the section it happened in",
                !sel.State.IsCollapsed(sel.Door.Id));
        }

        /// <summary>
        /// Change one field of a PlacementFields and assert the equality test
        /// notices — and that applying it back to a placement carries it.
        /// </summary>
        private delegate void FieldChange(ref PlacementFields fields);

        private static void AssertFieldDetected(string name, PlacementFields baseline, FieldChange change)
        {
            var changed = baseline;
            change(ref changed);

            Assert($"PlacementFields sees a change of {name}", !changed.Equals(baseline));

            // ...and ApplyTo carries it. Equality and application are two
            // separate omissions, and a field can be missing from either.
            var p = new Placement("probe", PlacementKind.Door, Vector2.Zero);
            baseline.ApplyTo(p);
            changed.ApplyTo(p);
            Assert($"  and ApplyTo writes {name} through to the placement",
                PlacementFields.From(p).Equals(changed));
        }

        /// <summary>
        /// Run one command through the property that defines it, over a world
        /// with something of every kind in it.
        /// </summary>
        // The state is compared as a canonical STRING covering everything a
        // command may touch. A structural comparison field by field would have
        // to be extended by hand every time Placement grows one; this one only
        // has to have the new field added to Shape(), and until it does the
        // round-trip check is silently narrower — which is why Shape() is the
        // first thing to look at if a command starts losing something.
        private static void RoundTrip(string label, Func<World, IEditorCommand> build)
        {
            var w = NewWorld();
            var command = build(w);

            string before = Shape(w);
            command.Do(w.Context);
            string afterDo = Shape(w);

            Assert($"{label}: Do changes something", afterDo != before);

            command.Undo(w.Context);
            AssertShape($"{label}: Do then Undo restores the world", Shape(w), before);

            command.Do(w.Context);
            AssertShape($"{label}: and redoing lands where Do landed", Shape(w), afterDo);

            // The conservative dirty rule: a command dirties what it touches, in
            // BOTH directions. Undoing back to the last-saved state leaves the
            // room marked dirty, which costs one redundant save; the opposite
            // error loses work silently, which is what the discard guard exists
            // to prevent.
            var clean = NewWorld();
            var c2 = build(clean);
            clean.State.PlacementsDirty = false;
            clean.State.CollisionDirty = false;
            clean.State.BackgroundDirty = false;
            c2.Do(clean.Context);
            bool dirtiedForward = clean.State.PlacementsDirty || clean.State.CollisionDirty
                                  || clean.State.BackgroundDirty;
            clean.State.PlacementsDirty = false;
            clean.State.CollisionDirty = false;
            clean.State.BackgroundDirty = false;
            c2.Undo(clean.Context);
            bool dirtiedBackward = clean.State.PlacementsDirty || clean.State.CollisionDirty
                                   || clean.State.BackgroundDirty;
            Assert($"{label}: dirties the room in both directions",
                dirtiedForward && dirtiedBackward,
                $"forward={dirtiedForward} backward={dirtiedBackward}");
        }

        // ====================================================================
        // 2. STACK — the four rules
        // ====================================================================

        private static void CheckStack()
        {
            Section("2. STACK — redo is cleared by a new edit, and the cap evicts the oldest");

            var w = NewWorld();
            var stack = new UndoStack();

            Assert("a fresh stack can neither undo nor redo", !stack.CanUndo && !stack.CanRedo);
            Assert("  and Undo on it returns nothing rather than throwing",
                stack.Undo(w.Context) == null);
            Assert("  as does Redo", stack.Redo(w.Context) == null);

            var move = new MovePlacementCommand(w.State.Placements[0],
                w.State.Placements[0].Position, new Vector2(8, 8));
            stack.Execute(move, w.Context);
            Assert("Execute runs the command", w.State.Placements[0].Position == new Vector2(8, 8));
            Assert("  and records it", stack.CanUndo && stack.UndoDepth == 1);
            Assert("  naming what it was", stack.NextUndoLabel == move.Label, stack.NextUndoLabel ?? "(null)");

            string label = stack.Undo(w.Context) ?? "";
            Assert("Undo reports the label it popped", label == move.Label, label);
            Assert("  and the entry moved to the redo side", !stack.CanUndo && stack.CanRedo);
            Assert("  which names it too", stack.NextRedoLabel == move.Label, stack.NextRedoLabel ?? "(null)");

            stack.Redo(w.Context);
            Assert("Redo puts it back on the undo side", stack.CanUndo && !stack.CanRedo);
            Assert("  and re-applies the edit", w.State.Placements[0].Position == new Vector2(8, 8));

            // RULE 1. The future a new edit invalidates.
            stack.Undo(w.Context);
            Assert("with something undone, redo is available", stack.CanRedo);
            stack.Execute(new MovePlacementCommand(w.State.Placements[0],
                w.State.Placements[0].Position, new Vector2(16, 16)), w.Context);
            Assert("a NEW edit clears the redo stack", !stack.CanRedo);

            // Redoing several in a row must stay possible: Redo pushes straight
            // onto the undo list rather than going through the path that clears
            // the redo list it is walking.
            var chain = NewWorld();
            var chainStack = new UndoStack();
            for (int i = 1; i <= 3; i++)
                chainStack.Execute(new MovePlacementCommand(chain.State.Placements[0],
                    chain.State.Placements[0].Position, new Vector2(i * 10, 0)), chain.Context);
            for (int i = 0; i < 3; i++) chainStack.Undo(chain.Context);
            Assert("three undos leave three redos", chainStack.RedoDepth == 3,
                chainStack.RedoDepth.ToString());
            for (int i = 0; i < 3; i++) chainStack.Redo(chain.Context);
            Assert("  and all three can be redone in a row", chainStack.UndoDepth == 3
                && chain.State.Placements[0].Position == new Vector2(30, 0));

            // RULE 2. The cap, and WHICH end it evicts.
            var deep = NewWorld();
            var deepStack = new UndoStack();
            int overflow = UndoStack.MaxDepth + 6;
            for (int i = 1; i <= overflow; i++)
                deepStack.Execute(new MovePlacementCommand(deep.State.Placements[0],
                    deep.State.Placements[0].Position, new Vector2(i, 0)), deep.Context);

            Assert($"the stack caps at {UndoStack.MaxDepth}", deepStack.UndoDepth == UndoStack.MaxDepth,
                deepStack.UndoDepth.ToString());

            for (int i = 0; i < UndoStack.MaxDepth; i++) deepStack.Undo(deep.Context);
            Assert("  and undoing it dry empties it", !deepStack.CanUndo);
            // The OLDEST entries went, so the position lands where the seventh
            // edit left it — not at the original. This is what "evict the
            // oldest" costs, and it is the honest half of the cap.
            Assert("  evicting the OLDEST, so the earliest edits are gone for good",
                deep.State.Placements[0].Position == new Vector2(overflow - UndoStack.MaxDepth, 0),
                deep.State.Placements[0].Position.ToString());

            // RULE 3. A room switch clears both halves. The editor calls this
            // from LoadRoom; what is assertable here is that it clears BOTH.
            var cleared = new UndoStack();
            cleared.Execute(new MovePlacementCommand(w.State.Placements[0],
                w.State.Placements[0].Position, new Vector2(1, 1)), w.Context);
            cleared.Undo(w.Context);
            Assert("before a clear there is history on both sides",
                cleared.CanRedo && !cleared.CanUndo);
            cleared.Execute(new MovePlacementCommand(w.State.Placements[0],
                w.State.Placements[0].Position, new Vector2(2, 2)), w.Context);
            cleared.Clear();
            Assert("Clear empties BOTH stacks", !cleared.CanUndo && !cleared.CanRedo);

            // PushApplied records without running. The gestures that use it —
            // a drag, a brush stroke, a paint swipe — have already written
            // their effect by the time the button comes up.
            var applied = NewWorld();
            var appliedStack = new UndoStack();
            var start = applied.State.Placements[0].Position;
            appliedStack.PushApplied(new MovePlacementCommand(applied.State.Placements[0],
                start, new Vector2(99, 99)));
            Assert("PushApplied does NOT run the command",
                applied.State.Placements[0].Position == start);
            Assert("  but records it", appliedStack.CanUndo);
            appliedStack.Undo(applied.Context);
            Assert("  so undo still reaches the position it recorded",
                applied.State.Placements[0].Position == start);
        }

        // ====================================================================
        // 3. REGISTRY EDITS — rename, through the writer that keeps the header
        // ====================================================================
        // The display-name rename (PR 7b commit 3) goes through
        // RoomManifest.Save, which re-emits the hand-written header verbatim
        // and re-aligns the columns. The thing worth pinning is that a rename
        // changes ONE field of ONE row and nothing else in the file — because
        // the alternative, JsonSerializer.Serialize, would silently drop the
        // header comment and take the ordering rule with it.
        //
        // Room ID rename is explicitly out of scope and has no code path; see
        // doc/07. Nothing here tests one, because there is nothing to test.
        // ====================================================================

        private static void CheckRegistryEdits()
        {
            Section("3. REGISTRY — a display-name rename rewrites one field");

            string scratch = Path.Combine(Path.GetTempPath(), "sorcery-editcheck");
            Directory.CreateDirectory(scratch);
            string path = Path.Combine(scratch, "rooms.json");

            string liveText;
            List<RoomManifest> live;
            try
            {
                liveText = File.ReadAllText(RoomManifest.RoomsJsonPath);
                live = RoomManifest.All;
            }
            catch (Exception ex)
            {
                Assert("the live registry could be read", false, ex.Message);
                return;
            }

            // A no-op write first: the baseline this section's diffs are read
            // against, and the same byte-identical property tools/RoundTrip
            // asserts for the live file.
            RoomManifest.Save(live, path);
            string baseline = File.ReadAllText(path);
            Assert("saving the registry unchanged reproduces the live file byte for byte",
                baseline == liveText,
                $"{baseline.Length} vs {liveText.Length} bytes");

            // The rename, exactly as EditorGame.SetRoomDisplayName performs it:
            // the same list, in the same order, with one entry replaced.
            const string newName = "Chateau Zero Renamed";
            var renamed = new List<RoomManifest>(live.Count);
            string targetId = live[0].RoomId;
            string oldName = live[0].DisplayName;
            foreach (var r in live)
                renamed.Add(r.RoomId == targetId
                    ? new RoomManifest(r.RoomId, newName, r.BackgroundAsset, r.CollisionFile)
                    : r);

            RoomManifest.Save(renamed, path);
            string after = File.ReadAllText(path);

            // Compared against the LIVE FILE, not against the baseline this
            // section wrote. Both of those come out of the same writer, so a
            // writer that emitted no header at all would produce two identical
            // empty headers and pass — which is exactly what the first version
            // of this check did when the header emission was deliberately
            // broken to test it.
            Assert("the header comment survived the rename verbatim",
                HeaderOf(after) == HeaderOf(liveText),
                $"{HeaderOf(after).Length} vs {HeaderOf(liveText).Length} chars");
            Assert("  and there is a header there to survive",
                HeaderOf(after).Contains("ROOM REGISTRY", StringComparison.Ordinal));
            Assert("the new display name is in the file",
                after.Contains($"\"displayName\": \"{newName}\"", StringComparison.Ordinal));
            Assert("the old one is gone",
                !after.Contains($"\"displayName\": \"{oldName}\"", StringComparison.Ordinal));
            Assert("the room id is untouched",
                after.Contains($"\"id\": \"{targetId}\"", StringComparison.Ordinal));
            Assert("the row count did not change",
                EntryRows(after) == EntryRows(baseline),
                $"{EntryRows(baseline)} -> {EntryRows(after)}");

            // Column alignment: the writer pads every token to the longest one,
            // so a LONGER name re-pads the whole displayName column and every
            // row's line changes. That is expected and is why this asserts the
            // parse rather than the bytes — what must not change is the DATA.
            var reread = ReadRegistry(path, out string? problem);
            if (reread == null)
            {
                Assert("the renamed file parses back", false, problem ?? "unknown");
            }
            else
            {
                Assert("the renamed file parses back", true);
                bool sameShape = reread.Count == live.Count;
                for (int i = 0; sameShape && i < reread.Count; i++)
                    sameShape = reread[i].RoomId == live[i].RoomId
                             && reread[i].BackgroundAsset == live[i].BackgroundAsset
                             && reread[i].CollisionFile == live[i].CollisionFile;
                Assert("  with every id, background and collision file unchanged", sameShape);

                int nameChanges = 0;
                for (int i = 0; i < reread.Count && i < live.Count; i++)
                    if (reread[i].DisplayName != live[i].DisplayName) nameChanges++;
                Assert("  and exactly one display name different", nameChanges == 1,
                    nameChanges.ToString());
            }

            // Round-trip: saving the renamed registry again is byte-identical,
            // so a rename does not leave the file drifting toward a new shape
            // on every subsequent write.
            if (reread != null)
            {
                RoomManifest.Save(reread, path);
                Assert("re-saving the renamed registry is byte-identical",
                    File.ReadAllText(path) == after);
            }

            CheckDisplayNameRules();
            CheckRenameFlow(scratch, live);

            Console.WriteLine();
            Console.WriteLine($"  scratch left at {scratch}");
        }

        // ---- what a display name may be -------------------------------------

        private static void CheckDisplayNameRules()
        {
            Console.WriteLine();
            Console.WriteLine("    what a display name may be");

            Assert("an ordinary name is accepted",
                RoomProperties.CheckDisplayName("Chateau Zero") == null,
                RoomProperties.CheckDisplayName("Chateau Zero") ?? "");

            // THE ONE THAT MATTERS. RoomManifest.LoadAll treats displayName as
            // cosmetic and falls back to the ID when it is blank — so an empty
            // rename would be written, read back as the room's id, and look
            // like it had worked. Refused up front instead.
            Assert("an empty name is refused", RoomProperties.CheckDisplayName("") != null);
            Assert("  and so is whitespace, for the same reason",
                RoomProperties.CheckDisplayName("   ") != null);
            Assert("  and null", RoomProperties.CheckDisplayName(null) != null);

            Assert($"a name longer than {RoomProperties.MaxDisplayNameLength} is refused",
                RoomProperties.CheckDisplayName(new string('x', RoomProperties.MaxDisplayNameLength + 1)) != null);
            Assert("  and one exactly that long is not",
                RoomProperties.CheckDisplayName(new string('x', RoomProperties.MaxDisplayNameLength)) == null);

            // rooms.json is written one entry per line. A newline would be
            // escaped into the JSON correctly and read back as a name with a
            // \n in it, which every label in two applications renders as a box.
            Assert("a name containing a newline is refused",
                RoomProperties.CheckDisplayName("Chateau\n0") != null);
            Assert("  and a tab", RoomProperties.CheckDisplayName("Chateau\t0") != null);
        }

        // ---- the rename, end to end, against a scratch registry -------------

        private static void CheckRenameFlow(string scratch, IReadOnlyList<RoomManifest> live)
        {
            Console.WriteLine();
            Console.WriteLine("    the rename flow");

            // Its own directory: Rename READS the registry it is about to
            // write, so the two must be the same file, and this section rewrites
            // it several times.
            string dir = Path.Combine(scratch, "rename");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "rooms.json");
            RoomManifest.Save(live, path);

            string firstId = live[0].RoomId;
            string secondId = live.Count > 1 ? live[1].RoomId : live[0].RoomId;

            var refused = RoomProperties.Rename(firstId, "  ", dir);
            Assert("Rename refuses an empty name", !refused.Ok, refused.Message);
            Assert("  and writes nothing for it",
                File.ReadAllText(path) == RegistryText(live), "the file changed");

            var missing = RoomProperties.Rename("no_such_room", "Whatever", dir);
            Assert("Rename refuses a room the registry does not hold", !missing.Ok, missing.Message);

            var noop = RoomProperties.Rename(firstId, live[0].DisplayName, dir);
            Assert("renaming to the name it already has succeeds", noop.Ok, noop.Message);
            Assert("  and reports that nothing changed", !noop.Changed);

            var first = RoomProperties.Rename(firstId, "Alpha Room", dir);
            Assert("a real rename succeeds", first.Ok, first.Message);
            Assert("  and reports that something changed", first.Changed);

            // THE FRESH READ, which is the whole reason Rename does not use the
            // cached RoomManifest.All. Two renames in a row, with no reload
            // between them: if the second had started from a snapshot taken
            // before the first, it would rewrite the whole file from that
            // snapshot and the first rename would simply be gone.
            var second = RoomProperties.Rename(secondId, "Beta Room", dir);
            Assert("a second rename succeeds", second.Ok, second.Message);

            var final = ReadRegistry(path, out string? problem);
            if (final == null)
            {
                Assert("the twice-renamed file parses back", false, problem ?? "unknown");
                return;
            }

            Assert("the twice-renamed file parses back", true);
            Assert("  the SECOND rename is in it", NameOf(final, secondId) == "Beta Room",
                NameOf(final, secondId) ?? "(missing)");
            Assert("  and so is the FIRST — no snapshot overwrote it",
                NameOf(final, firstId) == "Alpha Room", NameOf(final, firstId) ?? "(missing)");
            Assert("  with the room count unchanged", final.Count == live.Count,
                $"{live.Count} -> {final.Count}");

            // Order is room order — the editor's Prev/Next cycle walks it — so
            // a rename must not reorder anything.
            bool sameOrder = final.Count == live.Count;
            for (int i = 0; sameOrder && i < final.Count; i++)
                sameOrder = final[i].RoomId == live[i].RoomId;
            Assert("  and the array order untouched", sameOrder);
        }

        private static string? NameOf(IReadOnlyList<RoomManifest> registry, string roomId)
        {
            foreach (var r in registry) if (r.RoomId == roomId) return r.DisplayName;
            return null;
        }

        /// <summary>What the writer would produce for this registry.</summary>
        private static string RegistryText(IReadOnlyList<RoomManifest> registry)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "sorcery-editcheck-cmp.json");
            RoomManifest.Save(registry, tmp);
            return File.ReadAllText(tmp);
        }

        private static List<RoomManifest>? ReadRegistry(string path, out string? problem)
        {
            problem = null;
            try
            {
                // Parsed through the DTOs the loader uses, not through
                // RoomManifest.All — that reads the LIVE path and is cached.
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                };
                var data = System.Text.Json.JsonSerializer.Deserialize<RoomsJson>(
                    File.ReadAllText(path), options);
                if (data == null) { problem = "deserialised to null"; return null; }

                var list = new List<RoomManifest>(data.rooms.Count);
                foreach (var e in data.rooms)
                    list.Add(new RoomManifest(e.id, e.displayName, e.backgroundAsset, e.collisionFile ?? ""));
                return list;
            }
            catch (Exception ex)
            {
                problem = ex.Message;
                return null;
            }
        }

        /// <summary>Everything before the opening brace — the hand-written header block.</summary>
        private static string HeaderOf(string text)
        {
            int brace = text.IndexOf('{');
            return brace < 0 ? text : text.Substring(0, brace);
        }

        private static int EntryRows(string text)
        {
            int n = 0, i = 0;
            const string needle = "{ \"id\": ";
            while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        // ====================================================================
        // THE WORLD UNDER TEST
        // ====================================================================

        /// <summary>
        /// A room with one of everything a command can touch: four placements
        /// (including a door and a blocked door), a player spawn, a collision
        /// grid with some solid cells in it, and a background image.
        /// </summary>
        private sealed class World
        {
            public EditorState State = null!;
            public FakeBackground Background = null!;
            public EditorCommandContext Context = null!;

            public Placement Door = null!;
            public Placement Blocked = null!;
        }

        private static World NewWorld()
        {
            var state = new EditorState();

            state.Placements.Add(new Placement("chateau_0_sword_1", PlacementKind.Item, new Vector2(40, 100))
            { ItemType = ItemType.Sword });
            state.Placements.Add(new Placement("chateau_0_guard_2", PlacementKind.Enemy, new Vector2(120, 100))
            { EnemyType = default });

            var blocked = new Placement("chateau_0_blockeddoor_3", PlacementKind.BlockedDoor, new Vector2(160, 112))
            { RequiredItem = ItemType.Lyre };
            state.Placements.Add(blocked);

            var door = new Placement("chateau_0_door_4", PlacementKind.Door, new Vector2(296, 112))
            {
                DoorOpeningSide = "LeftOpening",
                DoorTargetRoomId = "chateau_1",
                DoorTargetDoorId = "chateau_1_door_left",
            };
            state.Placements.Add(door);

            state.PlayerSpawn = new Vector2(160, 80);

            // 40x18 at 8 px per tile — the room shape doc/07 documents.
            var map = new TileMapComponent(null, 40, 18);
            for (int x = 0; x < 40; x++) map.SetTile(x, 17, TileConfig.WALL_DARK_GRAY);
            map.SetTile(5, 5, TileConfig.WALL_DARK_GRAY);
            state.CollisionMap = map;

            // 320x144, filled with something non-uniform so a region blit that
            // wrote the wrong row would show up in the shape string.
            var pixels = new Color[320 * 144];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color(i % 251, (i / 320) % 253, 128, 255);

            var bg = new FakeBackground(pixels, 320, 144);
            return new World
            {
                State = state,
                Background = bg,
                Context = new EditorCommandContext(state, bg),
                Door = door,
                Blocked = blocked,
            };
        }

        /// <summary>
        /// EVERYTHING a command may touch, canonicalised. Two worlds with the
        /// same shape string are indistinguishable to the editor and to the
        /// save path.
        /// </summary>
        // Dirty flags are deliberately NOT in here: they are asserted
        // separately, because undo sets them on purpose and comparing them
        // would make every round-trip fail for the right reason at the wrong
        // moment.
        //
        // Selection is not in here either — it is a way of looking at the room,
        // not part of it, and the commands move it on purpose (asserted
        // directly in section 1b instead).
        private static string Shape(World w)
        {
            var sb = new StringBuilder();

            // List ORDER included: it is the order the save path writes, and a
            // redo that appended instead of re-inserting would reorder the file.
            foreach (var p in w.State.Placements)
                sb.Append(p.Id).Append('|').Append(p.Kind).Append('|')
                  .Append((int)p.Position.X).Append(',').Append((int)p.Position.Y).Append('|')
                  .Append(p.ItemType).Append('|').Append(p.EnemyType).Append('|')
                  .Append(p.RequiredItem).Append('|').Append(p.DoorOpeningSide).Append('|')
                  .Append(p.DoorTargetRoomId).Append('|').Append(p.DoorTargetDoorId).Append('\n');

            sb.Append("spawn=").Append(w.State.PlayerSpawn?.ToString() ?? "(none)").Append('\n');

            var map = w.State.CollisionMap;
            if (map != null)
            {
                sb.Append("tiles=");
                for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                    sb.Append(map.GetTile(x, y));
                sb.Append('\n');
            }

            var px = w.Background.BackgroundPixels;
            if (px != null)
            {
                // A checksum rather than every pixel: 46,080 colours would make
                // a failure unreadable, and any single changed pixel moves it.
                ulong sum = 1469598103934665603UL;
                for (int i = 0; i < px.Length; i++)
                {
                    sum ^= px[i].PackedValue;
                    sum *= 1099511628211UL;
                }
                sb.Append("pixels=").Append(sum.ToString("x16")).Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>The IBackgroundTarget an undo command sees, over a bare array.</summary>
        private sealed class FakeBackground : IBackgroundTarget
        {
            public Color[]? BackgroundPixels { get; }
            public int BackgroundWidth { get; }
            public int BackgroundHeight { get; }

            /// <summary>How many times a command said "these pixels changed".</summary>
            public int PushCount { get; private set; }

            public FakeBackground(Color[]? pixels, int width, int height)
            {
                BackgroundPixels = pixels;
                BackgroundWidth = width;
                BackgroundHeight = height;
            }

            public void BackgroundPixelsChanged() => PushCount++;
        }

        // ====================================================================
        // OUTPUT
        // ====================================================================

        private static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine($"  {title}");
        }

        private static void Assert(string label, bool ok, string? detail = null)
        {
            _checks++;
            if (!ok) _failures++;
            string suffix = ok || string.IsNullOrEmpty(detail) ? "" : $"   [{detail}]";
            Console.WriteLine($"    {(ok ? "ok  " : "FAIL")} {label}{suffix}");
        }

        private static void AssertShape(string label, string actual, string expected)
        {
            _checks++;
            bool ok = actual == expected;
            if (!ok) _failures++;
            Console.WriteLine($"    {(ok ? "ok  " : "FAIL")} {label}");
            if (ok) return;

            // Name the first line that differs. A whole-shape dump would be
            // hundreds of characters of identical text with one hidden change.
            string[] a = actual.Split('\n'), e = expected.Split('\n');
            for (int i = 0; i < Math.Max(a.Length, e.Length); i++)
            {
                string la = i < a.Length ? a[i] : "(missing)";
                string le = i < e.Length ? e[i] : "(missing)";
                if (la == le) continue;
                Console.WriteLine($"           expected: {le}");
                Console.WriteLine($"           actual  : {la}");
                break;
            }
        }
    }
}
