// ============================================================================
// CHROMECHECK — SORCERYFORGE CHROME / INPUT-ROUTING HARNESS
// Sorcery+ Remake
// ============================================================================
// THE INVARIANT IT GUARDS
//
//   "Dear ImGui gets first refusal on every frame's mouse. The canvas, the map
//    board and the crop image see that frame's mouse only when ImGui declines
//    it — EXCEPT that a gesture which started on one of those surfaces keeps
//    the mouse until it ends, wherever the cursor wanders."
//
//   That sentence is the whole of UI/ChromeInputRouter.cs, and it is the
//   single thing a chrome migration is most likely to get wrong. It is also
//   invisible in a screenshot: the editor looks perfect right up until the
//   wheel zooms the canvas when you meant to scroll the palette, or a drag
//   released over the inspector leaves an entity welded to the cursor because
//   the release was never seen.
//
// WHY THIS CAN RUN HEADLESS
//
//   Dear ImGui is pure CPU. It builds its font atlas with stb_truetype, lays
//   out its windows, decides what the mouse is over, and records draw lists —
//   all in ordinary memory. A renderer only paints what it produced. So this
//   harness creates a real ImGui context against the real pinned cimgui,
//   feeds it synthetic input, and asks it the same questions the editor asks,
//   with no window, no GraphicsDevice and no desktop session.
//
//   The design rule that keeps this possible: every chrome file under
//   SorceryForge/UI/ is device-free EXCEPT ImGuiRenderer.cs. If a panel ever
//   needs a Texture2D, it takes the IntPtr handle ImGui uses instead.
//
// WHAT IT CANNOT COVER
//
//   Pixels. Whether the font is legible, whether the menu bar looks right, and
//   whether the thing is pleasant to use are the owner's smoke test. Also the
//   real driver: this proves ImGui's answer, not that the window manager put
//   the cursor where ImGui was told it was. Run the editor with --imgui-probe
//   for that half.
//
// SECTIONS
//
//   1 capture     which screen regions ImGui claims, at two window sizes
//   2 override    a gesture that began on the canvas survives crossing a panel
//   3 ownership   a gesture that began on a panel does NOT leak to the canvas
//   4 wheel       the notch goes to exactly one consumer, decided by region
//   5 keyboard    editor keybinds keep firing over every band and under a held
//                 widget; an OPEN MENU holds them, so Escape closes the menu
//                 instead of quitting the editor
//   6 menus       what the board disables, and the four documented exceptions
//   7 titles      the room title and the board title, verbatim, and which
//                 '*' means which unsaved thing
//   8 status      every fragment of the status line's right-hand group, in
//                 order, including the three unsaved markers
//   9 palette     the real PalettePanel, driven with synthetic clicks: the row
//                 you click is the row you saw, scrolled or not
//  10 inspector   every field reaches its own named verb, on its own placement;
//                 a collapsed section registers nothing
//  11 pickers     candidate rows, the quantize toggle, and the crop step's two
//                 buttons - including that the crop IMAGE is left to the canvas
//  12 modality    with a modal up, nothing behind it answers a click
//  13 undo menu   Edit > Undo / Redo, greyed by their stacks and by the board
//  14 pickers     the inspector's filterable dropdowns: the list, the filter,
//                 what the pick becomes, and what the chrome does behind one
//  15 text input  a focused filter box holds the editor's keys — the first
//                 time the WantTextInput rule has had anything to gate
//
// HOW TO RUN
//
//   dotnet build tools/ChromeCheck/ChromeCheck.csproj
//   dotnet run   --project tools/ChromeCheck/ChromeCheck.csproj
//
//   Exit 0 = every check passed. Exit 1 = failures (listed inline as FAIL).
//   Exit 2 = could not run (bad arguments, ImGui context could not start).
//
// SAFETY
//
//   Writes nothing, anywhere. It does not even read assets/data.
// ============================================================================

using ImGuiNET;
using Microsoft.Xna.Framework;
using SorceryForge;
using SorceryForge.UI;
using SorceryRemake.Core;
using System;
using System.Collections.Generic;
using NVector2 = System.Numerics.Vector2;

namespace SorceryRemake.Tools.ChromeCheck
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

            Console.WriteLine("ChromeCheck — SorceryForge chrome / input-routing harness");
            Console.WriteLine();

            Harness harness;
            try
            {
                harness = new Harness();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("could not start an ImGui context: " + ex.Message);
                Console.Error.WriteLine("the native cimgui library must be resolvable — check that the");
                Console.Error.WriteLine("ImGui.NET package restored its runtimes/<rid>/native payload.");
                return 2;
            }

            CheckCapture(harness);
            CheckGestureOverride(harness);
            CheckChromeOwnership(harness);
            CheckWheel(harness);
            CheckKeyboard(harness);
            CheckMenuEnablement();
            CheckTitles();
            CheckStatusLine();
            CheckPalette(harness);
            CheckInspector(harness);
            CheckPickers(harness);
            CheckModality(harness);
            CheckUndoMenu();
            CheckInspectorPickers(harness);
            CheckTextInputKeyboard(harness);

            Console.WriteLine();
            Console.WriteLine($"  {_checks} checks, {_failures} failure(s)");
            Console.WriteLine();
            Console.WriteLine(_failures == 0
                ? "  CHROME HOLDS: ImGui has first refusal, the canvas keeps what it started,\n" +
                  "  one wheel notch reaches exactly one consumer, and every menu item, title\n" +
                  "  and status fragment says what it always said."
                : "  CHROME BROKEN — see the FAIL lines above.");

            return _failures == 0 ? 0 : 1;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("usage: dotnet run --project tools/ChromeCheck/ChromeCheck.csproj");
            Console.WriteLine();
            Console.WriteLine("  Drives the real Dear ImGui with synthetic input and asserts the");
            Console.WriteLine("  editor's input-routing rules. Writes nothing.");
            Console.WriteLine();
            Console.WriteLine("exit 0 = all checks pass; 1 = failures; 2 = could not run.");
        }

        // ====================================================================
        // 1. CAPTURE — which regions ImGui claims
        // ====================================================================

        private static void CheckCapture(Harness h)
        {
            Section("1. CAPTURE — ImGui claims the chrome bands and nothing else");

            foreach (var (w, hgt) in new[] { (1280, 720), (900, 500) })
            {
                h.Resize(w, hgt);
                Console.WriteLine($"    -- window {w}x{hgt}, canvas {EditorLayout.CanvasRect}");

                AssertCapture(h, "canvas centre",
                    Centre(EditorLayout.CanvasRect), expectCapture: false);
                AssertCapture(h, "palette panel",
                    Centre(EditorLayout.PaletteRect), expectCapture: true);
                AssertCapture(h, "inspector panel",
                    Centre(EditorLayout.InspectorRect), expectCapture: true);
                AssertCapture(h, "top bar",
                    Centre(EditorLayout.TopBarRect), expectCapture: true);
                AssertCapture(h, "status bar",
                    Centre(EditorLayout.StatusBarRect), expectCapture: true);

                // The margin between a panel and the canvas belongs to neither.
                // It is dead space in the old chrome too; what matters is that
                // it does not silently become canvas.
                var justLeftOfCanvas = new NVector2(
                    EditorLayout.CanvasRect.Left - 4, Centre(EditorLayout.CanvasRect).Y);
                h.MoveTo(justLeftOfCanvas);
                h.Settle();
                Assert("canvas margin is not the canvas",
                    !EditorLayout.IsInsideCanvas(
                        new Microsoft.Xna.Framework.Point((int)justLeftOfCanvas.X, (int)justLeftOfCanvas.Y)));
            }

            // Every corner of the canvas, not just its centre: an off-by-one in
            // the window rectangles would show up at the edges first.
            h.Resize(1280, 720);
            var c = EditorLayout.CanvasRect;
            AssertCapture(h, "canvas top-left pixel", new NVector2(c.Left + 1, c.Top + 1), false);
            AssertCapture(h, "canvas bottom-right pixel", new NVector2(c.Right - 2, c.Bottom - 2), false);
        }

        private static void AssertCapture(Harness h, string label, NVector2 at, bool expectCapture)
        {
            h.MoveTo(at);
            h.Settle();
            bool captured = h.Router.ImGuiWantsMouse;
            Assert($"{label} -> ImGui {(expectCapture ? "captures" : "declines")}",
                captured == expectCapture,
                $"WantCaptureMouse={captured} at ({at.X:0}, {at.Y:0})");
            Assert($"{label} -> mouse {(expectCapture ? "stops at chrome" : "reaches the world")}",
                h.Router.MouseReachesWorld == !expectCapture);
        }

        // ====================================================================
        // 2. OVERRIDE — a canvas gesture survives crossing a panel
        // ====================================================================
        // This is the rule that keeps the out-of-canvas move release working:
        // dragging a placement toward a room edge routinely takes the cursor
        // onto the inspector before the button comes up, and that release ends
        // the move and fires auto-punch. Gate it on ImGui alone and the release
        // is never seen.
        // ====================================================================

        private static void CheckGestureOverride(Harness h)
        {
            Section("2. OVERRIDE — a gesture begun on the canvas keeps the mouse");

            h.Resize(1280, 720);

            // Press on the canvas.
            h.MoveTo(Centre(EditorLayout.CanvasRect));
            h.Settle();
            h.SetLeft(true);
            h.Frame();
            Assert("press on the canvas reaches the world", h.Router.MouseReachesWorld);

            // The editor now has a move in flight. Drag onto the inspector.
            h.Router.WorldGestureInProgress = true;
            h.MoveTo(Centre(EditorLayout.InspectorRect));
            h.Frame();
            Assert("dragging onto the inspector still reaches the world",
                h.Router.MouseReachesWorld,
                $"WantCaptureMouse={h.Router.ImGuiWantsMouse}");

            // Release there. THIS is the frame the old out-of-canvas branch
            // caught, and the one a naive gate would eat.
            h.SetLeft(false);
            h.Frame();
            Assert("the release over the inspector reaches the world",
                h.Router.MouseReachesWorld);

            // The editor clears its own flag on that release; the router then
            // hands the panel back.
            h.Router.WorldGestureInProgress = false;
            h.Frame();
            h.Settle();
            Assert("once the gesture ends the inspector takes the mouse back",
                !h.Router.MouseReachesWorld,
                $"WantCaptureMouse={h.Router.ImGuiWantsMouse}");

            // Same shape for the other three world gestures. The router does
            // not know which is running, only that one is — which is why
            // EditorGame.WorldGestureInProgress is the single place that
            // decides, from the flags that already exist.
            foreach (var gesture in new[] { "erase stroke", "map room drag", "crop box drag" })
            {
                h.Router.WorldGestureInProgress = true;
                h.MoveTo(Centre(EditorLayout.PaletteRect));
                h.Frame();
                Assert($"{gesture} continues over the palette", h.Router.MouseReachesWorld);
                h.Router.WorldGestureInProgress = false;
            }

            // The middle button, driven for real rather than simulated by the
            // flag: a pan started on the canvas is the gesture most likely to
            // leave it, because panning a zoomed room is how you reach a room
            // edge in the first place.
            h.MoveTo(Centre(EditorLayout.CanvasRect));
            h.Settle();
            h.SetMiddle(true);
            h.Frame();
            h.Router.WorldGestureInProgress = true;      // EditorGame sets _panning here
            h.MoveTo(Centre(EditorLayout.InspectorRect));
            h.Frame();
            Assert("middle-drag pan continues over the inspector", h.Router.MouseReachesWorld);
            h.SetMiddle(false);
            h.Frame();
            h.Router.WorldGestureInProgress = false;     // EditorGame clears _panning here
            h.Settle();
            Assert("  and the inspector takes the mouse back on release",
                !h.Router.MouseReachesWorld);
        }

        // ====================================================================
        // 3. OWNERSHIP — a chrome gesture does NOT leak onto the canvas
        // ====================================================================

        private static void CheckChromeOwnership(Harness h)
        {
            Section("3. OWNERSHIP — a gesture begun on a panel stays with the panel");

            h.Resize(1280, 720);
            h.Router.WorldGestureInProgress = false;

            h.MoveTo(Centre(EditorLayout.PaletteRect));
            h.Settle();
            h.SetLeft(true);
            h.Frame();
            Assert("press on the palette does not reach the world", !h.Router.MouseReachesWorld);

            // Drag out over the canvas with the button still down — the shape
            // of dragging an ImGui scrollbar past the panel's edge. Nothing
            // here may paint, erase or place.
            h.MoveTo(Centre(EditorLayout.CanvasRect));
            h.Frame();
            Assert("dragging out over the canvas still does not reach the world",
                !h.Router.MouseReachesWorld,
                $"WantCaptureMouse={h.Router.ImGuiWantsMouse}");

            h.SetLeft(false);
            h.Frame();
            h.Settle();
            Assert("after the release the canvas is live again", h.Router.MouseReachesWorld);

            // WHY THE MODAL HANDLERS BYPASS THE ROUTER.
            // A right-click anywhere cancels the New Room picker, the Import
            // picker and the crop step. Over a picker panel — which is exactly
            // where the cursor is while reading the list — ImGui captures the
            // mouse, so a cancel routed through the router would never fire.
            // Hence EditorGame's three modal handlers read the raw mouse
            // ungated: "consumes every input" has to keep meaning every input.
            h.MoveTo(Centre(EditorLayout.PaletteRect));
            h.Settle();
            h.SetRight(true);
            h.Frame();
            Assert("right-click over chrome IS captured by ImGui",
                h.Router.ImGuiWantsMouse,
                "so the picker cancel must not consult the router");
            h.SetRight(false);
            h.Frame();
            h.Settle();
        }

        // ====================================================================
        // 4. WHEEL — one notch, one consumer
        // ====================================================================
        // The hand-rolled chrome had three independent wheel consumers in room
        // mode (inspector scroll, palette scroll, canvas zoom), each region-
        // testing its own rectangle, with the rectangles maintained in three
        // different places. That is the arrangement that shipped a palette
        // whose scroll and whose hit-testing disagreed. Now the region test is
        // ImGui's own hover, computed from the windows actually on screen.
        // ====================================================================

        private static void CheckWheel(Harness h)
        {
            Section("4. WHEEL — the notch reaches exactly one consumer");

            h.Resize(1280, 720);
            h.Router.WorldGestureInProgress = false;

            h.MoveTo(Centre(EditorLayout.CanvasRect));
            h.Settle();
            h.Wheel(-1f);
            h.Frame();
            Assert("wheel over the canvas is left to the canvas (zoom)",
                h.Router.MouseReachesWorld && !h.Router.ImGuiWantsMouse);

            h.MoveTo(Centre(EditorLayout.PaletteRect));
            h.Settle();
            float before = h.PaletteScroll;
            h.Wheel(-1f);
            h.Frame();
            h.Frame();
            Assert("wheel over the palette is taken by ImGui",
                !h.Router.MouseReachesWorld);
            Assert("  and actually scrolls the palette",
                h.PaletteScroll > before,
                $"{before} -> {h.PaletteScroll}");

            // Back to the canvas: the palette must not keep eating notches
            // just because it was the last thing scrolled.
            h.MoveTo(Centre(EditorLayout.CanvasRect));
            h.Settle();
            float held = h.PaletteScroll;
            h.Wheel(-1f);
            h.Frame();
            h.Frame();
            Assert("wheel over the canvas does not also scroll the palette",
                Math.Abs(h.PaletteScroll - held) < 0.01f,
                $"{held} -> {h.PaletteScroll}");
            Assert("  and the canvas still gets it", h.Router.MouseReachesWorld);
        }

        // ====================================================================
        // 5. KEYBOARD — the editor's keybinds keep firing
        // ====================================================================

        private static void CheckKeyboard(Harness h)
        {
            Section("5. KEYBOARD — chrome does not steal the editor's keys");

            h.Resize(1280, 720);
            h.Router.WorldGestureInProgress = false;

            foreach (var (label, at) in new[]
            {
                ("canvas", Centre(EditorLayout.CanvasRect)),
                ("palette", Centre(EditorLayout.PaletteRect)),
                ("inspector", Centre(EditorLayout.InspectorRect)),
                ("top bar", Centre(EditorLayout.TopBarRect)),
                ("status bar", Centre(EditorLayout.StatusBarRect)),
            })
            {
                h.MoveTo(at);
                h.Settle();
                Assert($"idle over the {label}: keys reach the editor",
                    h.Router.KeyboardReachesEditor,
                    $"WantCaptureKeyboard={h.Router.ImGuiWantsKeyboard}");
            }

            // With keyboard navigation deliberately off and no text field in
            // the chrome, the ONLY thing that raises WantCaptureKeyboard is an
            // ImGui widget being actively held. Documented here rather than
            // asserted either way: what matters is that merely hovering chrome
            // never costs the editor a keypress, which is the case above.
            h.MoveTo(Centre(EditorLayout.PaletteRect));
            h.Settle();
            // A MOUSE BUTTON HELD ON CHROME must not cost the editor its keys.
            //
            // HOLD FOR SEVERAL FRAMES, and read the flag on the last of them.
            // ImGui's ActiveId — and with it io.WantCaptureKeyboard — is still
            // clear on the frame of the press and only goes true from the NEXT
            // one, so a probe that presses and asserts immediately reports a
            // clean result whatever the rule is. This assertion passed for a
            // while against a router that was in fact killing every keybind.
            var actions = new RecordingActions();
            var state = BuildPaletteState();
            h.SetBandsModal(false);
            h.DrivePalette(actions, state);

            foreach (var (label, at) in new[]
            {
                ("a palette row", new NVector2(140f, EditorLayout.PaletteRect.Y + 30f + 22f + 4f + 22f)),
                // Empty space counts: a window takes its own MoveId on a press
                // even when it is NoMove, so this raises ActiveId exactly as a
                // widget does.
                ("the palette's title strip", new NVector2(140f, EditorLayout.PaletteRect.Y + 10f)),
                ("empty status bar", Centre(EditorLayout.StatusBarRect)),
                ("empty top bar", new NVector2(EditorLayout.WindowWidth - 400f, EditorLayout.TopBarRect.Y + 40f)),
            })
            {
                h.MoveTo(at);
                h.Settle();
                h.SetLeft(true);
                h.Frame(); h.Frame(); h.Frame();
                Assert($"holding {label} does NOT cost the editor its keys",
                    h.Router.KeyboardReachesEditor,
                    $"WantCaptureKeyboard={h.Router.ImGuiWantsKeyboard} " +
                    $"WantTextInput={h.Router.ImGuiWantsTextInput}");
                h.SetLeft(false);
                h.Frame();
                h.Settle();
            }

            Assert("after releasing, keys reach the editor again", h.Router.KeyboardReachesEditor);

            // AN OPEN MENU IS THE DANGEROUS CASE. Escape in room view arms the
            // discard guard, and on a clean room it quits outright — so if a
            // popup swallowed neither the key nor the click, "open the File
            // menu, change your mind, press Escape" would close the editor.
            // Keyboard navigation is deliberately off, so ImGui's own
            // Escape-closes-a-popup path cannot be assumed. Measured, not
            // assumed:
            h.DriveMenuBar(actions, state, new ChromeView { RoomId = "chateau_0", RoomDisplayName = "X" });
            h.ClickAt(new NVector2(22f, EditorLayout.TopBarRect.Y + 10f));
            bool opened = Harness.AnyPopupOpen;
            Assert("clicking File opens a menu popup", opened);

            if (opened)
            {
                Assert("an open menu holds the keyboard, so Escape cannot reach Exit",
                    !h.Router.KeyboardReachesEditor,
                    $"WantCaptureKeyboard={h.Router.ImGuiWantsKeyboard}");

                h.TapKey(ImGuiKey.Escape);
                h.Settle();
                Console.WriteLine($"      (after Escape, a popup is {(Harness.AnyPopupOpen ? "still open" : "closed")})");
            }

            // Click away to leave the menus in a known state for later sections.
            h.ClickAt(Centre(EditorLayout.CanvasRect));
            h.Settle();
        }

        // ====================================================================
        // 6. MENU ENABLEMENT — the map-mode rule, item by item
        // ====================================================================
        // Every one of the twelve top-bar buttons was inert while the board was
        // up, because Update returns before HandleButtons runs and DrawButton
        // greyed them to say so. The menus preserve that item for item, with
        // exactly four departures, each written down here so a later PR cannot
        // widen the set by accident.
        // ====================================================================

        private static void CheckMenuEnablement()
        {
            Section("6. MENUS — what the board disables, and the four exceptions");

            var room = new ChromeView { MapMode = false };
            var map = new ChromeView { MapMode = true };

            // The rule.
            Assert("room actions are live in room view", MenuBar.CanActOnRoom(room));
            Assert("room actions are dead on the board", !MenuBar.CanActOnRoom(map));
            Assert("Save Room is live in room view", MenuBar.CanSaveRoom(room));
            Assert("Save Room is dead on the board", !MenuBar.CanSaveRoom(map));
            Assert("Exit is live in room view", MenuBar.CanExit(room));
            Assert("Exit is dead on the board (Esc returns to the room there)",
                !MenuBar.CanExit(map));
            Assert("Fullscreen is live in room view", MenuBar.CanToggleFullscreen(room));
            Assert("Fullscreen is dead on the board (F11 is not read there)",
                !MenuBar.CanToggleFullscreen(map));

            // The exceptions, each justified by a keyboard path that already
            // works from the board.
            Assert("EXCEPTION Save Map Arrangement is live ONLY on the board",
                MenuBar.CanSaveMap(map) && !MenuBar.CanSaveMap(room));
            Assert("EXCEPTION New Room / Import are live in BOTH (N and I already are)",
                MenuBar.CanOpenPickers(room) && MenuBar.CanOpenPickers(map));
            Assert("EXCEPTION World Map is live in BOTH (Tab already is)",
                MenuBar.CanToggleMap(room) && MenuBar.CanToggleMap(map));
        }

        // ====================================================================
        // 7. TITLES — the room title and the board title, verbatim
        // ====================================================================

        private static void CheckTitles()
        {
            Section("7. TITLES — exact strings, and which '*' means what");

            var state = new EditorState();
            var view = new ChromeView
            {
                MapMode = false,
                RoomDisplayName = "Chateau Entrance",
                RoomId = "chateau_0",
                MapRoomCount = 12,
            };

            // Two spaces before the parenthesis, carried over from the old
            // form[0]. The unsaved marker is NOT baked into this string: it is
            // drawn as its own glyph in its own colour, so that "unsaved" is
            // something the eye finds rather than punctuation.
            AssertText("room title", MenuBar.TitleText(state, view),
                "Room: Chateau Entrance  (chateau_0)");

            state.PlacementsDirty = true;
            AssertText("room title is unchanged by a dirty room",
                MenuBar.TitleText(state, view),
                "Room: Chateau Entrance  (chateau_0)");
            state.PlacementsDirty = false;

            view.MapMode = true;
            AssertText("board title", MenuBar.TitleText(state, view),
                "WORLD MAP — 12 rooms   |   Tab or Esc: back to chateau_0");

            // The board's "*" is MapDirty alone. A dirty ROOM must not mark the
            // board, and a dirty board must not mark the room — three dirty
            // readouts, three different rules, and conflating any two of them
            // is the mistake this asserts against.
            state.MapDirty = true;
            AssertText("board title marks an unsaved arrangement",
                MenuBar.TitleText(state, view),
                "WORLD MAP * — 12 rooms   |   Tab or Esc: back to chateau_0");

            state.MapDirty = false;
            state.PlacementsDirty = true;
            state.CollisionDirty = true;
            state.BackgroundDirty = true;
            AssertText("a dirty ROOM does not mark the board title",
                MenuBar.TitleText(state, view),
                "WORLD MAP — 12 rooms   |   Tab or Esc: back to chateau_0");
        }

        // ====================================================================
        // 8. STATUS LINE — every fragment, in order
        // ====================================================================
        // The one always-visible readout of what is unsaved. Its fragments are
        // conditional on exactly one piece of state each, in a fixed order, so
        // a missing fragment means a specific thing — which is only true if
        // nothing ever reorders them.
        // ====================================================================

        private static void CheckStatusLine()
        {
            Section("8. STATUS LINE — fragments, order, and the three markers");

            var state = new EditorState();
            var room = new ChromeView { MapMode = false, Zoom = 1 };

            AssertText("clean room, Place mode", StatusBar.ViewInfo(state, room),
                "Zoom 1x | Tab: map");

            room.Zoom = 4;
            state.Mode = EditorMode.Erase;
            state.BrushSize = 12;
            AssertText("Brush appears in Erase mode only", StatusBar.ViewInfo(state, room),
                "Zoom 4x | Brush 12px | Tab: map");

            state.Mode = EditorMode.Paint;
            AssertText("  and not in Paint mode", StatusBar.ViewInfo(state, room),
                "Zoom 4x | Tab: map");
            state.Mode = EditorMode.Place;

            // room* is the marker that closes the saturation problem: the old
            // chrome's only always-on sign of unsaved work was a "*" drawn in
            // the top bar ONLY IF it fitted a gap between two button banks —
            // 36 px at a default 1280 px window.
            room.RoomDirty = true;
            AssertText("room* marks any of the three room flags",
                StatusBar.ViewInfo(state, room), "Zoom 4x | room* | Tab: map");

            state.BackgroundDirty = true;
            AssertText("PNG* joins it for background pixels specifically",
                StatusBar.ViewInfo(state, room), "Zoom 4x | room* | PNG* | Tab: map");

            state.MapDirty = true;
            AssertText("map* is shown from ROOM mode too",
                StatusBar.ViewInfo(state, room),
                "Zoom 4x | room* | PNG* | map* | Tab: map");

            // Map mode: the board's own zoom, map* ALONE, then the persistent
            // hints — which live here precisely because the transient message
            // on the left is overwritten by every drag and every zoom.
            //
            // room* and PNG* must NOT appear on the board. They are about a
            // room you are not looking at and cannot save from there (Ctrl+S
            // means the arrangement in that mode), and this is exactly how the
            // line read before the migration.
            var map = new ChromeView { MapMode = true, MapZoomPercent = 25, RoomDirty = true };
            AssertText("board, everything unsaved: map* alone", StatusBar.ViewInfo(state, map),
                "Map 25% | map* | N: new | I: import | Tab/Esc: room");

            state.MapDirty = false;
            AssertText("  a dirty ROOM alone leaves the board's line unmarked",
                StatusBar.ViewInfo(state, map),
                "Map 25% | N: new | I: import | Tab/Esc: room");
            state.MapDirty = true;

            state.BackgroundDirty = false;
            state.MapDirty = false;
            map.RoomDirty = false;
            AssertText("board, nothing unsaved", StatusBar.ViewInfo(state, map),
                "Map 25% | N: new | I: import | Tab/Esc: room");
        }

        // ====================================================================
        // 9. PALETTE — what you click is what you saw
        // ====================================================================
        // The hand-rolled palette kept its row rectangles in one place, its
        // scroll offset in another, and its viewport rectangle in four. The
        // failure mode was those copies disagreeing: a scrolled palette that
        // hands you the entry above the one you clicked. Here the panel is
        // driven for real, with a synthetic entry set, and the click is
        // followed all the way to the IChromeActions verb it produces.
        // ====================================================================

        private static void CheckPalette(Harness h)
        {
            Section("9. PALETTE — the row you click is the row you get");

            h.Resize(1280, 720);
            var state = BuildPaletteState();
            var actions = new RecordingActions();
            h.DrivePalette(actions, state);

            // Row geometry, from PalettePanel's own constants. Asserting
            // against them is the point: they are the contract the old layout
            // arithmetic implemented four times.
            const float titleH = 30f, headerH = 22f, rowH = 44f, rowGap = 4f, sectionGap = 6f;
            float listTop = EditorLayout.PaletteRect.Y + titleH;
            float firstRow = listTop + headerH + rowGap;

            // Entry 0 of section 1.
            h.ClickAt(new NVector2(140f, firstRow + rowH / 2f));
            AssertPicked("first row picks the first entry", actions, "Sword");

            // Entry 1 of section 1: one row plus one gap further down.
            actions.Reset();
            h.ClickAt(new NVector2(140f, firstRow + rowH + rowGap + rowH / 2f));
            AssertPicked("second row picks the second entry", actions, "Ball & Chain");

            // The section header between two sections is not a row and is not
            // clickable — it never was, and it must not become a
            // CollapsingHeader on the way through ImGui.
            actions.Reset();
            float section2Header = firstRow + 3f * (rowH + rowGap) + sectionGap;
            h.ClickAt(new NVector2(140f, section2Header + headerH / 2f));
            Assert("a section header is not clickable", actions.Picked == null,
                actions.Picked ?? "");

            // The first entry of the SECOND section, immediately below it.
            actions.Reset();
            h.ClickAt(new NVector2(140f, section2Header + headerH + rowGap + rowH / 2f));
            AssertPicked("  and the row below it is the next section's first entry",
                actions, "Guard");

            // The 30 px title strip is outside the list. It used to scroll the
            // list; it never picked an entry, and it still must not.
            actions.Reset();
            h.ClickAt(new NVector2(140f, EditorLayout.PaletteRect.Y + titleH / 2f));
            Assert("the PALETTE title strip picks nothing", actions.Picked == null,
                actions.Picked ?? "");

            // Scrolled to the bottom, the last entry is at the bottom of the
            // list and a click there gets it. THIS is the assertion the old
            // chrome could not make: drawing and hit-testing are now the same
            // call, so they cannot disagree by a scroll offset.
            actions.Reset();
            h.ScrollPaletteToBottom(actions, state);
            float listBottom = EditorLayout.PaletteRect.Bottom - 8f;
            h.ClickAt(new NVector2(140f, listBottom - rowH / 2f));
            AssertPicked("scrolled to the bottom, the bottom row is the last entry",
                actions, "Player Spawn");

            // Same click position, unscrolled, is a different entry — i.e. the
            // scroll really moved the rows under the cursor rather than moving
            // only the paint.
            actions.Reset();
            h.ResetPaletteScroll(actions, state);
            h.ClickAt(new NVector2(140f, listBottom - rowH / 2f));
            Assert("  and unscrolled that same point is NOT the last entry",
                actions.Picked != "Player Spawn", actions.Picked ?? "(nothing)");

            // Outside Place mode the palette dims and ignores clicks — the
            // dimming is what advertises the gate, so the gate has to be real.
            foreach (var mode in new[] { EditorMode.Paint, EditorMode.Erase })
            {
                actions.Reset();
                state.Mode = mode;
                h.DrivePalette(actions, state);
                h.ClickAt(new NVector2(140f, firstRow + rowH / 2f));
                Assert($"{mode} mode: the palette ignores clicks", actions.Picked == null,
                    actions.Picked ?? "");
                AssertText($"  and says so in its title", PalettePanel.Title(mode),
                    mode == EditorMode.Paint ? "PALETTE (paint mode)" : "PALETTE (erase mode)");
            }

            state.Mode = EditorMode.Place;
            AssertText("Place mode title", PalettePanel.Title(EditorMode.Place), "PALETTE");
        }

        /// <summary>
        /// A synthetic palette with the real shape: sections in SectionOrder,
        /// entries in insertion order, and the META spawn entry last. No
        /// textures — the panel draws through ImGui handles, so it never
        /// touches a Texture2D, which is the whole reason this runs here.
        /// </summary>
        // Deliberately LONGER than the panel. The palette overflowing is the
        // condition the whole scroll machinery exists for, and the state the
        // hand-rolled version's rectangles disagreed in. It is also where the
        // real palette is heading: EDITOR_REVIEW item 5's full item set is
        // already extracted in Content/.
        private static EditorState BuildPaletteState()
        {
            var state = new EditorState { Mode = EditorMode.Place };
            void Add(string label, string section) =>
                state.Palette.Add(new PaletteEntry(label, PlacementKind.Item, null!, default)
                { Section = section });

            // The '&' is not incidental: "Ball & Chain" is a real entry label,
            // and a widget system that treated '&' as a mnemonic escape would
            // silently render it as "Ball  Chain".
            Add("Sword", "WEAPONS");
            Add("Ball & Chain", "WEAPONS");
            Add("Axe", "WEAPONS");

            Add("Guard", "ENEMIES");
            for (int i = 1; i < 16; i++) Add($"Enemy {i}", "ENEMIES");

            state.Palette.Add(new PaletteEntry("Player Spawn", PlacementKind.Item, null!, default)
            { Section = "META", IsPlayerSpawn = true });
            return state;
        }

        // ====================================================================
        // 10. INSPECTOR — every field reaches its own verb
        // ====================================================================
        // The old inspector's editable fields carried their whole side-effect
        // set in a lambda inside a Draw method. They are named verbs now, and
        // this follows a click on each one all the way to the verb it fires and
        // the placement it fires it on.
        //
        // It also pins down the two things about the header that a rewrite is
        // most likely to split apart: one click both SELECTS and TOGGLES, and a
        // collapsed section registers no field clicks at all.
        // ====================================================================

        private static void CheckInspector(Harness h)
        {
            Section("10. INSPECTOR — the field you click is the verb that fires");

            h.Resize(1280, 720);
            var actions = new RecordingActions();
            var state = new EditorState();

            var door = new Placement("chateau_0_door_1", PlacementKind.Door, new Vector2(0, 40))
            { DoorOpeningSide = "LeftOpening", DoorTargetRoomId = "chateau_1", DoorTargetDoorId = "" };
            state.Placements.Add(door);
            h.DriveInspector(actions, state);

            // Header geometry, from InspectorPanel's own constants.
            const float titleH = 32f, headerH = 40f, headerGap = 2f;
            const float labelH = 16f, innerGap = 2f, valueH = 22f, rowGap = 4f;
            const float rowH = labelH + innerGap + valueH + rowGap;   // 44
            float x = EditorLayout.InspectorRect.X + 40f;
            float headerTop = EditorLayout.InspectorRect.Y + titleH;
            float bodyTop = headerTop + headerH + headerGap;

            // A row's clickable half is the VALUE BOX, not the label above it.
            float ValueY(int row) => bodyTop + row * rowH + labelH + innerGap + valueH / 2f;

            h.ClickAt(new NVector2(x, headerTop + headerH / 2f));
            AssertCall("clicking the header selects AND toggles, in one verb",
                actions, nameof(IChromeActions.SelectAndToggleSection), door.Id);

            // Row 0 is Pos, and it is read-only: no hit region at all.
            actions.Reset();
            h.ClickAt(new NVector2(x, ValueY(0)));
            Assert("the Pos row is read-only and fires nothing", actions.Calls.Count == 0,
                actions.Only);

            actions.Reset();
            h.ClickAt(new NVector2(x, ValueY(1)));
            AssertCall("Opens cycles the door's side", actions,
                nameof(IChromeActions.CycleDoorOpeningSide), door.Id);

            // Room and Door are PICKERS since PR 7b: the click opens a popup
            // and fires no verb of its own. Which verb the popup's rows reach
            // is section 14's business; what belongs here is that the click
            // lands on the right ROW, and that a row that opens a list does not
            // also quietly change something on the way.
            actions.Reset();
            h.ClickAt(new NVector2(x, ValueY(2)));
            Assert("Room opens a picker rather than firing a verb",
                actions.Calls.Count == 0, actions.Only);
            Assert("  and a picker popup is up", Harness.AnyPopupOpen);
            h.CloseAnyPopup();

            actions.Reset();
            h.ClickAt(new NVector2(x, ValueY(3)));
            Assert("Door opens a picker rather than firing a verb",
                actions.Calls.Count == 0, actions.Only);
            Assert("  and a picker popup is up", Harness.AnyPopupOpen);
            h.CloseAnyPopup();

            actions.Reset();
            h.ClickAt(new NVector2(x, ValueY(4)));
            AssertCall("Background punches under this placement", actions,
                nameof(IChromeActions.PunchBackground), door.Id);

            // Collapsed, the body is not drawn and none of its rows can be
            // clicked — the old panel achieved this by not calling
            // DrawSectionBody, which is what registered the zones.
            actions.Reset();
            state.CollapsedPlacementIds.Add(door.Id);
            h.DriveInspector(actions, state);
            h.ClickAt(new NVector2(x, ValueY(1)));
            Assert("a collapsed section registers no field clicks", actions.Calls.Count == 0,
                actions.Only);
            state.CollapsedPlacementIds.Clear();

            // The right verb on the right entity when there is more than one.
            actions.Reset();
            var blocked = new Placement("chateau_0_blockeddoor_2", PlacementKind.BlockedDoor,
                                        new Vector2(80, 40)) { RequiredItem = ItemType.Lyre };
            state.Placements.Add(blocked);
            h.DriveInspector(actions, state);

            // Door section: header + 5 rows. Then the blocked door's header.
            float secondHeaderTop = bodyTop + 5f * rowH + 6f;
            h.ClickAt(new NVector2(x, secondHeaderTop + headerH / 2f));
            AssertCall("the second section's header names the SECOND placement",
                actions, nameof(IChromeActions.SelectAndToggleSection), blocked.Id);

            actions.Reset();
            float blockedBody = secondHeaderTop + headerH + headerGap;
            h.ClickAt(new NVector2(x, blockedBody + rowH + labelH + innerGap + valueH / 2f));
            Assert("  and its Needs row opens a picker of its own",
                actions.Calls.Count == 0 && Harness.AnyPopupOpen, actions.Only);
            h.CloseAnyPopup();

            // TWO PLACEMENTS OF THE SAME KIND. Both draw a row labelled
            // "Opens", and an ImGui id is the label plus the enclosing id
            // stack — so if the placement's id does not scope the whole
            // section, the two rows fuse into one widget and clicking the
            // second reports on the first. Nothing about that is visible on
            // screen: both rows draw, both highlight, one of them lies.
            actions.Reset();
            state.Placements.Clear();
            state.CollapsedPlacementIds.Clear();
            var doorA = new Placement("chateau_0_door_1", PlacementKind.Door, new Vector2(0, 40))
            { DoorOpeningSide = "LeftOpening" };
            var doorB = new Placement("chateau_0_door_2", PlacementKind.Door, new Vector2(296, 40))
            { DoorOpeningSide = "RightOpening" };
            state.Placements.Add(doorA);
            state.Placements.Add(doorB);
            h.DriveInspector(actions, state);

            // Door A: header, 2 px, five rows, 6 px. Then door B's header, 2 px,
            // and its own body — whose row 1 is "Opens", the row that collides.
            float doorBBody = bodyTop + 5f * rowH + 6f + headerH + headerGap;
            h.ClickAt(new NVector2(x, doorBBody + rowH + labelH + innerGap + valueH / 2f));
            AssertCall("two doors: the SECOND door's Opens row is the second door's",
                actions, nameof(IChromeActions.CycleDoorOpeningSide), doorB.Id);

            actions.Reset();
            h.ClickAt(new NVector2(x, ValueY(1)));
            AssertCall("  and the first door's Opens row is still the first door's",
                actions, nameof(IChromeActions.CycleDoorOpeningSide), doorA.Id);

            // Labels the panel owns outright.
            AssertText("kind label for a door", InspectorPanel.KindShortLabel(door), "Door");
            AssertText("kind label for a blocked door",
                InspectorPanel.KindShortLabel(blocked), "BlockedDoor");
            AssertText("kind label for a wizard",
                InspectorPanel.KindShortLabel(
                    new Placement("w", PlacementKind.Wizard, Vector2.Zero)), "Wizard");
        }

        // ====================================================================
        // 11. PICKERS — rows, cancels, and the toggle
        // ====================================================================

        private static void CheckPickers(Harness h)
        {
            Section("11. PICKERS — a row is its candidate, and Cancel is a cancel");

            h.Resize(1280, 720);
            var actions = new RecordingActions();

            var view = new ChromeView
            {
                NewRoomOpen = true,
                NewRoomCandidates = new List<RoomCandidate>
                {
                    Candidate("RoomBG_Chateau3", "chateau_3", "Chateau 3", true),
                    Candidate("RoomBG_Taken", "", "", false),
                    Candidate("RoomBG_Chateau4", "chateau_4", "Chateau 4", true),
                },
                ImportCandidates = new List<ImportCandidate>(),
            };
            h.DrivePickers(actions, view);

            // Panel geometry: centred, 660x540 at this window size, 12 px top
            // padding, two title lines, a 6 px gap, then the list.
            var panel = CentredPanel(660, 540);
            float rowTop = panel.Y + 12f + ImGui.GetTextLineHeight() * 2f
                           + ImGui.GetStyle().ItemSpacing.Y * 2f + 6f;
            float rowX = panel.X + 60f;

            h.ClickAt(new NVector2(rowX, rowTop + 23f));
            AssertPicked("the first row is the first candidate", actions, "chateau_3");

            // An unusable candidate is drawn but has no hit region — the red
            // tint has to be trustworthy.
            actions.Reset();
            h.ClickAt(new NVector2(rowX, rowTop + 50f + 23f));
            Assert("an unavailable row cannot be clicked", actions.Calls.Count == 0, actions.Only);

            actions.Reset();
            h.ClickAt(new NVector2(rowX, rowTop + 100f + 23f));
            AssertPicked("  and the row below it is still the right candidate", actions, "chateau_4");

            // Cancel, bottom right of the panel.
            actions.Reset();
            h.ClickAt(new NVector2(panel.Right - 64f, panel.Bottom - 12f - ImGui.GetFrameHeight() / 2f));
            AssertCall("Cancel cancels the New Room picker", actions,
                nameof(IChromeActions.CancelNewRoomPicker));

            // The import picker's quantize toggle: a whole row, so the click
            // target is obvious.
            actions.Reset();
            view.NewRoomOpen = false;
            view.ImportOpen = true;
            view.ImportQuantize = true;
            view.ImportDir = @"D:\Sorcery_Remake\assets\import";
            h.DrivePickers(actions, view);

            var importPanel = CentredPanel(760, 560);
            float toggleY = importPanel.Y + 12f + ImGui.GetTextLineHeight() * 2f
                            + ImGui.GetStyle().ItemSpacing.Y * 2f + 6f + 14f;
            h.ClickAt(new NVector2(importPanel.X + 200f, toggleY));
            AssertCall("the quantize row toggles quantize", actions,
                nameof(IChromeActions.ToggleImportQuantize));

            // The crop step's two buttons, in the footer strip.
            actions.Reset();
            view.ImportOpen = false;
            view.CropOpen = true;
            view.CropFileName = "Chateau3.jpg";
            view.CropRoomId = "chateau_3";
            view.CropDisplayName = "Chateau 3";
            view.CropPresetNote = "built-in framing";
            view.CropSourceWidth = 384;
            view.CropSourceHeight = 270;
            view.CropRect = new Rectangle(32, 41, 320, 144);
            h.DrivePickers(actions, view);

            float footerY = EditorLayout.StatusBarRect.Y + EditorLayout.StatusBarRect.Height / 2f;
            h.ClickAt(new NVector2(EditorLayout.WindowWidth - 158f, footerY));
            AssertCall("the crop footer's Cancel cancels", actions, nameof(IChromeActions.CancelCrop));

            actions.Reset();
            h.ClickAt(new NVector2(EditorLayout.WindowWidth - 50f, footerY));
            AssertCall("  and Confirm confirms", actions, nameof(IChromeActions.ConfirmCrop));

            // ONE CLICK MODEL PER MODAL. The rows and the quantize toggle were
            // put back on the PRESS edge to match every old click zone; a
            // Cancel button left on ImGui's release-inside default would mean a
            // single panel answering two different questions about when a click
            // happens. Pressing and NOT releasing must already have acted.
            actions.Reset();
            h.MoveTo(new NVector2(EditorLayout.WindowWidth - 158f, footerY));
            h.Settle();
            h.SetLeft(true);
            h.Frame();
            AssertCall("the crop footer's Cancel fires on the PRESS, not the release",
                actions, nameof(IChromeActions.CancelCrop));
            h.SetLeft(false);
            h.Frame();
            h.Settle();

            // The crop's IMAGE area is not chrome: ImGui must decline it, so
            // that dragging the selection box still reaches EditorGame.
            h.MoveTo(Centre(EditorLayout.CanvasRect));
            h.Settle();
            Assert("the crop image area is left to the canvas",
                h.Router.MouseReachesWorld,
                $"WantCaptureMouse={h.Router.ImGuiWantsMouse}");
        }

        // CanCreate is derived from Problem, not settable — which is the right
        // shape and worth noticing: a candidate cannot claim to be usable
        // without also saying why it is not.
        // ====================================================================
        // 12. MODALITY — the bands go inert while a modal owns the editor
        // ====================================================================
        // The old chrome got this for free: Update returned before HandleButtons,
        // HandlePaletteInput or HandleInspectorClicks ever ran, so with a picker
        // up the whole chrome was dead. An ImGui window does NOT stop
        // hit-testing because another window is drawn over part of the screen —
        // and the centred picker covers the canvas, not the palette at x 0..280.
        // Without an explicit NoInputs the palette stayed live behind the dim,
        // and a click on it would dirty a room the user could not see.
        //
        // A running batch import counts too, and shows no overlay of its own.
        // ====================================================================

        private static void CheckModality(Harness h)
        {
            Section("12. MODALITY — nothing behind a modal answers a click");

            h.Resize(1280, 720);
            var actions = new RecordingActions();
            var state = BuildPaletteState();

            const float titleH = 30f, headerH = 22f, rowH = 44f, rowGap = 4f;
            float firstRow = EditorLayout.PaletteRect.Y + titleH + headerH + rowGap;
            var paletteRow = new NVector2(140f, firstRow + rowH / 2f);

            // Baseline: with no modal, the palette answers.
            h.SetBandsModal(false);
            h.DrivePalette(actions, state);
            h.ClickAt(paletteRow);
            AssertPicked("no modal: the palette answers", actions, "Sword");

            // With one, it does not — and ImGui does not even consider itself
            // hovered there, which is what lets the crop step reach its image
            // through the same dim.
            actions.Reset();
            h.SetBandsModal(true);
            h.DrivePalette(actions, state);
            h.ClickAt(paletteRow);
            Assert("modal up: the palette answers nothing", actions.Calls.Count == 0,
                actions.Only);

            // Same for the inspector.
            actions.Reset();
            var inspectorState = new EditorState();
            inspectorState.Placements.Add(
                new Placement("chateau_0_door_1", PlacementKind.Door, new Vector2(0, 40)));
            h.DriveInspector(actions, inspectorState);
            h.ClickAt(new NVector2(EditorLayout.InspectorRect.X + 40f,
                                   EditorLayout.InspectorRect.Y + 32f + 20f));
            Assert("modal up: the inspector answers nothing", actions.Calls.Count == 0,
                actions.Only);

            // And the menu bar — File > Save Room from behind a "modal" would
            // write the room the picker is about to replace.
            actions.Reset();
            h.ClickAt(new NVector2(24f, EditorLayout.TopBarRect.Y + 10f));
            h.ClickAt(new NVector2(24f, EditorLayout.TopBarRect.Y + 40f));
            Assert("modal up: the menu bar answers nothing", actions.Calls.Count == 0,
                actions.Only);

            // Released again.
            actions.Reset();
            h.SetBandsModal(false);
            h.DrivePalette(actions, state);
            h.ClickAt(paletteRow);
            AssertPicked("modal closed: the palette answers again", actions, "Sword");
        }

        // ====================================================================
        // 13. UNDO MENU — Edit > Undo / Redo, and when they are dead
        // ====================================================================
        // PR 7b. The two items are the only ones in the menus whose enablement
        // changes moment to moment, which makes them the two most likely to end
        // up always-on — and an always-on Undo that sometimes does nothing is
        // how an author learns to distrust the whole menu.
        //
        // Asserted through the named predicates, as section 6 does and for the
        // same reason: the rule is one table, in one place, and a condition
        // scattered through the menu bodies could not be tested at all.
        // ====================================================================

        private static void CheckUndoMenu()
        {
            Section("13. UNDO MENU — Edit > Undo / Redo, greyed when their stack is empty");

            var empty = new ChromeView { MapMode = false, CanUndo = false, CanRedo = false };
            var loaded = new ChromeView { MapMode = false, CanUndo = true, CanRedo = true };
            var board = new ChromeView { MapMode = true, CanUndo = true, CanRedo = true };

            Assert("Undo is dead with an empty stack", !MenuBar.CanUndo(empty));
            Assert("Undo is live once something has been done", MenuBar.CanUndo(loaded));
            Assert("Redo is dead with an empty redo stack", !MenuBar.CanRedo(empty));
            Assert("Redo is live once something has been undone", MenuBar.CanRedo(loaded));

            // The board's rule, which is the one that would be forgotten: Ctrl+Z
            // is read in HandleKeyboardShortcuts, and map mode returns before
            // that ever runs. A live menu item there would be the only way to
            // undo from the board, i.e. a second entry point into a stack whose
            // room is not the one on screen.
            Assert("Undo is dead on the board even with a full stack", !MenuBar.CanUndo(board));
            Assert("Redo is dead on the board even with a full redo stack", !MenuBar.CanRedo(board));

            // Half-states: one stack full, the other empty. The two items are
            // independent, and a shared condition would show up here.
            var undoOnly = new ChromeView { CanUndo = true, CanRedo = false };
            var redoOnly = new ChromeView { CanUndo = false, CanRedo = true };
            Assert("with only undo available, Redo stays dead",
                MenuBar.CanUndo(undoOnly) && !MenuBar.CanRedo(undoOnly));
            Assert("with only redo available, Undo stays dead",
                !MenuBar.CanUndo(redoOnly) && MenuBar.CanRedo(redoOnly));
        }

        // ====================================================================
        // 14. PICKERS — type to narrow, Enter takes the top hit
        // ====================================================================
        // EDITOR_REVIEW item 10, and the first REAL TEXT INPUT this editor has
        // ever had. Two families of thing are asserted here, and they fail
        // differently:
        //
        //   the widget      the list is what the logic side offered, typing
        //                   narrows it, Enter takes the top hit, a click takes
        //                   the row under the cursor, and "(none)" arrives as
        //                   the empty string the schema actually stores
        //
        //   the modality    what the rest of the chrome does while a picker is
        //                   open. PR 7a had to say this out loud for the modal
        //                   pickers (the bands take NoInputs); an ImGui popup
        //                   is supposed to handle it itself. Supposed to is not
        //                   a thing to ship, so it is MEASURED here.
        // ====================================================================

        private static readonly List<string> PickerRooms = new()
        {
            "chateau_0", "chateau_1", "chateau_2", "near_chateau",
            "inside_chateau", "stonehenge", "wastelands_1", "forest_1",
        };

        private static readonly List<ItemType> PickerItems = new()
        {
            ItemType.Sword, ItemType.BallAndChain, ItemType.Axe,
            ItemType.ShootingStar, ItemType.Lyre,
        };

        private static readonly Dictionary<string, IReadOnlyList<string>> PickerDoors = new()
        {
            ["chateau_1"] = new List<string> { "chateau_1_door_left", "chateau_1_door_topright" },
            ["chateau_2"] = new List<string> { "chateau_2_door_a" },
        };

        private static IReadOnlyList<string> DoorsOf(string roomId) =>
            PickerDoors.TryGetValue(roomId, out var doors) ? doors : new List<string>();

        private static void CheckInspectorPickers(Harness h)
        {
            Section("14. PICKERS — the list, the filter, and what the pick becomes");

            // ---- the narrowing rule, on its own -----------------------------
            //
            // Asserted apart from the widget because it is a rule rather than a
            // hit-test, and because SUBSTRING-not-prefix is the part a rewrite
            // would get wrong: door ids carry their room as a prefix, so a
            // prefix match would make "topright" find nothing — which is
            // exactly the half of the id an author remembers.
            var hits = new List<string>();
            FilterPopup.Narrow(PickerRooms, "", hits);
            Assert("an empty filter matches everything", hits.Count == PickerRooms.Count,
                hits.Count.ToString());

            FilterPopup.Narrow(PickerRooms, "chateau", hits);
            Assert("a filter narrows to what contains it", hits.Count == 5, hits.Count.ToString());
            Assert("  including matches that are not a PREFIX",
                hits.Contains("near_chateau") && hits.Contains("inside_chateau"));

            FilterPopup.Narrow(PickerRooms, "STONE", hits);
            Assert("the filter ignores case", hits.Count == 1 && hits[0] == "stonehenge");

            FilterPopup.Narrow(PickerRooms, "zzz", hits);
            Assert("a filter that matches nothing narrows to nothing", hits.Count == 0);

            FilterPopup.Narrow(null, "x", hits);
            Assert("a null option list narrows to nothing rather than throwing", hits.Count == 0);

            // ---- the widget --------------------------------------------------

            h.Resize(1280, 720);
            var actions = new RecordingActions();
            var state = new EditorState();
            var door = new Placement("chateau_0_door_1", PlacementKind.Door, new Vector2(0, 40))
            {
                DoorOpeningSide = "LeftOpening",
                DoorTargetRoomId = "chateau_1",
                DoorTargetDoorId = "",
            };
            state.Placements.Add(door);

            h.SetBandsModal(false);
            h.SetPickerLists(PickerRooms, PickerItems, DoorsOf);
            h.DriveInspector(actions, state);

            const float titleH = 32f, headerH = 40f, headerGap = 2f;
            const float labelH = 16f, innerGap = 2f, valueH = 22f, rowGap = 4f;
            const float rowH = labelH + innerGap + valueH + rowGap;
            float x = EditorLayout.InspectorRect.X + 40f;
            float bodyTop = EditorLayout.InspectorRect.Y + titleH + headerH + headerGap;
            float ValueY(int row) => bodyTop + row * rowH + labelH + innerGap + valueH / 2f;

            float roomRowY = ValueY(2);    // Pos, Opens, Room, Door, Background
            float doorRowY = ValueY(3);

            // ENTER TAKES THE TOP HIT. Three characters and a keypress is the
            // whole interaction the cycle-button could not offer at any number
            // of rooms.
            actions.Reset();
            h.ClickAt(new NVector2(x, roomRowY));
            Assert("clicking Room opens a picker", Harness.AnyPopupOpen);
            h.Settle();
            h.TypeText("stone");
            h.TapKey(ImGuiKey.Enter);
            h.Settle();
            AssertCall("typing 'stone' and pressing Enter picks stonehenge", actions,
                nameof(IChromeActions.SetDoorTargetRoom), door.Id);
            AssertValue("  and the value that arrives is the room id", actions, "stonehenge");
            Assert("  and picking closes the popup", !Harness.AnyPopupOpen);

            // A CLICK ON A ROW takes that row. The row is FOUND by hovering
            // rather than computed — see Harness.FindPopupLastRowY. Narrowed to
            // one option first, so "the last row" is the only row.
            actions.Reset();
            h.ClickAt(new NVector2(x, roomRowY));
            h.Settle();
            h.TypeText("wastelands");
            float rowY = h.FindPopupLastRowY(x, roomRowY + 4f, roomRowY + 200f);
            Assert("the picker's list row can be found under the cursor", rowY > 0f,
                rowY.ToString("0"));
            if (rowY > 0f)
            {
                h.ClickAt(new NVector2(x, rowY));
                h.Settle();
                AssertCall("clicking the row picks it", actions,
                    nameof(IChromeActions.SetDoorTargetRoom), door.Id);
                AssertValue("  and it is the row that was showing", actions, "wastelands_1");
            }
            h.CloseAnyPopup();

            // "(none)" IS A REAL ENTRY, and it arrives as the empty string the
            // schema stores. A door with no target is what an unfinished room
            // looks like; the cycle had an empty entry for the same reason.
            actions.Reset();
            h.ClickAt(new NVector2(x, roomRowY));
            h.Settle();
            h.TypeText("(none)");
            h.TapKey(ImGuiKey.Enter);
            h.Settle();
            AssertCall("picking (none) still reaches the verb", actions,
                nameof(IChromeActions.SetDoorTargetRoom), door.Id);
            AssertValue("  as the EMPTY STRING, not the label", actions, "");
            h.CloseAnyPopup();

            // THE DOOR LIST FOLLOWS THE ROOM ALREADY CHOSEN. The placement's
            // target room is chateau_1, so its two doors are what the Door
            // picker offers — and chateau_2's door is not.
            actions.Reset();
            h.ClickAt(new NVector2(x, doorRowY));
            Assert("clicking Door opens a picker", Harness.AnyPopupOpen);
            h.Settle();
            h.TypeText("topright");
            h.TapKey(ImGuiKey.Enter);
            h.Settle();
            AssertCall("the Door picker offers the TARGET room's doors", actions,
                nameof(IChromeActions.SetDoorTargetDoor), door.Id);
            AssertValue("  found by the half of the id an author remembers",
                actions, "chateau_1_door_topright");
            h.CloseAnyPopup();

            actions.Reset();
            h.ClickAt(new NVector2(x, doorRowY));
            h.Settle();
            h.TypeText("chateau_2");
            h.TapKey(ImGuiKey.Enter);
            h.Settle();
            Assert("  and NOT another room's doors", actions.Calls.Count == 0, actions.Only);
            h.CloseAnyPopup();

            // THE ITEM PICKER, on a blocked door. Typed all the way through:
            // the panel offers ItemType values and the verb receives one.
            actions.Reset();
            state.Placements.Clear();
            var blocked = new Placement("chateau_0_blockeddoor_2", PlacementKind.BlockedDoor,
                                        new Vector2(80, 40)) { RequiredItem = ItemType.Lyre };
            state.Placements.Add(blocked);
            h.DriveInspector(actions, state);

            float needsRowY = ValueY(1);   // Pos, Needs, Background
            h.ClickAt(new NVector2(x, needsRowY));
            Assert("clicking Needs opens a picker", Harness.AnyPopupOpen);
            h.Settle();
            h.TypeText("axe");
            h.TapKey(ImGuiKey.Enter);
            h.Settle();
            AssertCall("the item picker reaches the blocked door's verb", actions,
                nameof(IChromeActions.SetBlockedDoorRequiredItem), blocked.Id);
            AssertValue("  with the item it named", actions, "Axe");
            h.CloseAnyPopup();

            // None is NOT offered — the cycle could never reach it either, and
            // a blocked door requiring nothing is broken data.
            actions.Reset();
            h.ClickAt(new NVector2(x, needsRowY));
            h.Settle();
            h.TypeText("None");
            h.TapKey(ImGuiKey.Enter);
            h.Settle();
            Assert("the item picker does not offer None", actions.Calls.Count == 0, actions.Only);
            h.CloseAnyPopup();

            // ---- MODALITY, measured rather than assumed ---------------------
            //
            // PR 7a had to make the bands NoInputs by hand for the three modal
            // overlays, because an ImGui WINDOW does not stop hit-testing
            // because something is drawn over the middle of the screen. An
            // ImGui POPUP is documented to be different: while one is open,
            // ImGui reports other windows' content as not hoverable, so their
            // widgets never see a click. The whole of the inspector picker's
            // modality rests on that, so it is asserted here rather than read
            // out of the library's source.
            actions.Reset();
            var paletteState = BuildPaletteState();
            h.DrivePalette(actions, paletteState);
            h.DriveInspector(actions, state);
            var paletteRow = new NVector2(140f, EditorLayout.PaletteRect.Y + 30f + 22f + 4f + 22f);

            h.ClickAt(paletteRow);
            AssertPicked("with no popup, the palette answers a click", actions, "Sword");

            actions.Reset();
            h.ClickAt(new NVector2(x, needsRowY));
            Assert("a picker popup is open over the panels", Harness.AnyPopupOpen);
            actions.Reset();
            h.ClickAt(paletteRow);
            Assert("with a picker open, the palette does NOT answer that click",
                actions.Picked == null, actions.Picked ?? "(nothing)");
            Assert("  and the click closed the popup instead", !Harness.AnyPopupOpen);

            // ...and the chrome is live again the moment it is closed. A
            // modality that does not lift is worse than one that never applied.
            actions.Reset();
            h.ClickAt(paletteRow);
            AssertPicked("popup closed: the palette answers again", actions, "Sword");
        }

        // ====================================================================
        // 15. TEXT INPUT — the keyboard rule, now that it is live
        // ====================================================================
        // PR 7a built ChromeInputRouter.KeyboardReachesEditor on io.WantTextInput
        // rather than io.WantCaptureKeyboard, for a chrome that had NO TEXT
        // FIELD AT ALL. Every assertion about it until now has been about the
        // false branch. This is the section where the rule finally does
        // something, and it is asserted the way 7a's own lesson demands:
        //
        //   ACROSS FRAMES. Both flags are latched during NewFrame from what the
        //   PREVIOUS frame's widgets asked for, so a probe that presses and
        //   reads on the same frame reports a clean result whatever the rule
        //   is. That is how a broken keyboard rule once shipped inside a
        //   PASSING assertion in this very file.
        //
        // The editor keybinds this protects — P, Delete, [ and ], N, I, A — all
        // pass through the single gate KeyboardReachesEditor, in
        // HandleKeyboardShortcuts and HandleMapInput. The gate is what is
        // assertable headlessly; that each key is behind it is one `if` in
        // EditorGame and is in the owner's smoke pass.
        // ====================================================================

        private static void CheckTextInputKeyboard(Harness h)
        {
            Section("15. TEXT INPUT — a focused filter box holds the editor's keys");

            // ---- the rule itself, as a truth table --------------------------
            //
            // Driven directly rather than through ImGui, and that is the point.
            // Every text field this editor has TODAY lives inside a popup, so
            // the popup term alone covers every case the widget-driven
            // assertions below can reach — which was measured: deleting the
            // WantTextInput term entirely left all of them passing. A term with
            // no test is a term someone deletes.
            //
            // It stops being redundant with the room-rename field, which is a
            // plain band widget with no popup over it. Pinning the table now
            // means the term is guarded before the widget that needs it lands.
            var rule = new ChromeInputRouter();

            rule.Sample(false, false, false, false);
            Assert("neither term set: the keys are the editor's", rule.KeyboardReachesEditor);

            rule.Sample(false, false, false, true);
            Assert("a TEXT FIELD alone holds them", !rule.KeyboardReachesEditor);

            rule.Sample(false, false, true, false);
            Assert("an open POPUP alone holds them", !rule.KeyboardReachesEditor);

            rule.Sample(false, false, true, true);
            Assert("both together hold them", !rule.KeyboardReachesEditor);

            // 7a'S TRAP, pinned as a negative. io.WantCaptureKeyboard is true
            // whenever ImGui has an ActiveId — which includes a mouse button
            // merely HELD on empty chrome, because a window takes its own
            // MoveId even when it is NoMove. Building the rule on it killed
            // every editor keybind for as long as a button was down anywhere on
            // the bands.
            rule.Sample(false, true, false, false);
            Assert("but WantCaptureKeyboard alone does NOT — it is not the rule",
                rule.KeyboardReachesEditor);

            h.Resize(1280, 720);
            var actions = new RecordingActions();
            var state = new EditorState();
            var door = new Placement("chateau_0_door_1", PlacementKind.Door, new Vector2(0, 40))
            { DoorTargetRoomId = "chateau_1" };
            state.Placements.Add(door);

            h.SetBandsModal(false);
            h.SetPickerLists(PickerRooms, PickerItems, DoorsOf);
            h.DriveInspector(actions, state);

            const float titleH = 32f, headerH = 40f, headerGap = 2f;
            const float labelH = 16f, innerGap = 2f, valueH = 22f, rowGap = 4f;
            const float rowH = labelH + innerGap + valueH + rowGap;
            float x = EditorLayout.InspectorRect.X + 40f;
            float bodyTop = EditorLayout.InspectorRect.Y + titleH + headerH + headerGap;
            float roomRowY = bodyTop + 2 * rowH + labelH + innerGap + valueH / 2f;

            // Baseline: nothing open, keys are the editor's.
            h.MoveTo(Centre(EditorLayout.CanvasRect));
            h.Settle();
            Assert("with nothing open, keys reach the editor", h.Router.KeyboardReachesEditor);

            h.ClickAt(new NVector2(x, roomRowY));
            h.Settle();

            // HELD FOR SEVERAL FRAMES, and read on the last of them.
            h.Frame(); h.Frame(); h.Frame();
            Assert("the filter box takes text input", h.Router.ImGuiWantsTextInput,
                $"WantTextInput={h.Router.ImGuiWantsTextInput} popup={Harness.AnyPopupOpen}");
            Assert("  so P / Delete / brackets / N / I / A cannot reach the editor",
                !h.Router.KeyboardReachesEditor);

            // Typing keeps it that way. A rule that held for one frame after
            // focus and then let a keystroke through would be worse than none.
            h.TypeText("chateau");
            h.Frame(); h.Frame(); h.Frame();
            Assert("and it still holds them while the author is typing",
                !h.Router.KeyboardReachesEditor && h.Router.ImGuiWantsTextInput);

            // ESCAPE, STEP ONE: the field loses focus, the popup stays.
            h.TapKey(ImGuiKey.Escape);
            h.Settle();
            Assert("Esc clears the field's focus FIRST", !h.Router.ImGuiWantsTextInput,
                $"WantTextInput={h.Router.ImGuiWantsTextInput}");
            Assert("  leaving the popup open", Harness.AnyPopupOpen);
            Assert("  and the keys still held, by the popup rather than the field",
                !h.Router.KeyboardReachesEditor);

            // ESCAPE, STEP TWO: the popup closes. Neither press reached the
            // editor's Escape — which on a clean room is Exit.
            h.TapKey(ImGuiKey.Escape);
            h.Settle();
            Assert("a second Esc closes the popup", !Harness.AnyPopupOpen);
            Assert("  and only THEN do the keys come back", h.Router.KeyboardReachesEditor);

            // The same, ended by a pick rather than by Escape: the keys must
            // come back that way too, or the editor is deaf after every edit.
            h.ClickAt(new NVector2(x, roomRowY));
            h.Settle();
            h.Frame(); h.Frame();
            Assert("reopening the picker takes the keys again", !h.Router.KeyboardReachesEditor);
            h.TypeText("stone");
            h.TapKey(ImGuiKey.Enter);
            h.Settle();
            Assert("picking with Enter closes the popup", !Harness.AnyPopupOpen);
            Assert("  and hands the keyboard back to the editor",
                h.Router.KeyboardReachesEditor);

            // And a click somewhere harmless leaves the world in a known state
            // for anything that runs after this section.
            h.MoveTo(Centre(EditorLayout.CanvasRect));
            h.Settle();
        }

        private static RoomCandidate Candidate(string asset, string roomId, string display, bool ok) =>
            new()
            {
                BackgroundAsset = asset,
                RoomId = roomId,
                DisplayName = display,
                Problem = ok ? null : "already claimed by a room",
            };

        private static Rectangle CentredPanel(int maxW, int maxH)
        {
            int w = Math.Min(maxW, EditorLayout.WindowWidth - 80);
            int hh = Math.Min(maxH, EditorLayout.WindowHeight - 120);
            return new Rectangle((EditorLayout.WindowWidth - w) / 2,
                                 (EditorLayout.WindowHeight - hh) / 2, w, hh);
        }

        private static void AssertCall(string label, RecordingActions actions,
                                       string expectedVerb, string? expectedTarget = null)
        {
            _checks++;
            bool ok = actions.Calls.Count == 1 && actions.Calls[0] == expectedVerb
                      && (expectedTarget == null ||
                          (actions.Targets.Count == 1 && actions.Targets[0] == expectedTarget));
            if (!ok) _failures++;
            Console.WriteLine($"    {(ok ? "ok  " : "FAIL")} {label}");
            if (!ok)
            {
                Console.WriteLine($"           expected: {expectedVerb}" +
                                  (expectedTarget == null ? "" : $" on {expectedTarget}"));
                Console.WriteLine($"           actual  : {actions.Only}" +
                                  (actions.Targets.Count == 0 ? "" : $" on {string.Join(",", actions.Targets)}"));
            }
        }

        /// <summary>The value a setter verb was handed — a room id, a door id, an item.</summary>
        private static void AssertValue(string label, RecordingActions actions, string expected)
        {
            _checks++;
            bool ok = actions.Values.Count == 1 && actions.Values[0] == expected;
            if (!ok) _failures++;
            Console.WriteLine($"    {(ok ? "ok  " : "FAIL")} {label}");
            if (!ok)
                Console.WriteLine($"           expected '{expected}', got " +
                    (actions.Values.Count == 0 ? "(nothing)" : $"'{string.Join(",", actions.Values)}'"));
        }

        private static void AssertPicked(string label, RecordingActions actions, string expected)
        {
            _checks++;
            bool ok = actions.Picked == expected;
            if (!ok) _failures++;
            Console.WriteLine($"    {(ok ? "ok  " : "FAIL")} {label}");
            if (!ok) Console.WriteLine($"           expected '{expected}', got '{actions.Picked ?? "(nothing)"}'");
        }

        /// <summary>
        /// Records which verb the chrome invoked. Every method is a no-op that
        /// remembers it was called — which is exactly what an IChromeActions
        /// implementation is allowed to be, and the reason the interface exists.
        /// </summary>
        private sealed class RecordingActions : IChromeActions
        {
            public string? Picked;
            public readonly List<string> Calls = new();

            /// <summary>The entity id each placement-targeted verb was given.</summary>
            public readonly List<string> Targets = new();

            /// <summary>The VALUE each setter verb was given — a room id, a door id, an item.</summary>
            // New in PR 7b. A cycle verb carried no value: "advance this door's
            // target room" was the whole message. A picker's verb carries the
            // chosen value, so "the row you clicked is the value that arrives"
            // is now a question worth being able to ask.
            public readonly List<string> Values = new();

            public void Reset() { Picked = null; Calls.Clear(); Targets.Clear(); Values.Clear(); }

            public string Only => Calls.Count == 1 ? Calls[0] : $"[{Calls.Count}: {string.Join(",", Calls)}]";

            public void BeginPaletteDrag(PaletteEntry entry)
            {
                Picked = entry.Label;
                Calls.Add("BeginPaletteDrag");
            }

            private void Hit(string verb, Placement p) { Calls.Add(verb); Targets.Add(p.Id); }

            public void SelectAndToggleSection(Placement p) => Hit(nameof(SelectAndToggleSection), p);
            public void CycleDoorOpeningSide(Placement p) => Hit(nameof(CycleDoorOpeningSide), p);
            public void PunchBackground(Placement p) => Hit(nameof(PunchBackground), p);

            private void Set(string verb, Placement p, string value)
            {
                Hit(verb, p);
                Values.Add(value);
            }

            public void SetDoorTargetRoom(Placement p, string roomId) =>
                Set(nameof(SetDoorTargetRoom), p, roomId);

            public void SetDoorTargetDoor(Placement p, string doorId) =>
                Set(nameof(SetDoorTargetDoor), p, doorId);

            public void SetBlockedDoorRequiredItem(Placement p, ItemType item) =>
                Set(nameof(SetBlockedDoorRequiredItem), p, item.ToString());

            public void CreateRoom(RoomCandidate candidate)
            {
                Picked = candidate.RoomId;
                Calls.Add(nameof(CreateRoom));
            }

            public void RunImport(ImportCandidate candidate)
            {
                Picked = candidate.FileName;
                Calls.Add(nameof(RunImport));
            }

            public void CancelNewRoomPicker() => Calls.Add(nameof(CancelNewRoomPicker));
            public void CancelImportPicker() => Calls.Add(nameof(CancelImportPicker));
            public void ToggleImportQuantize() => Calls.Add(nameof(ToggleImportQuantize));
            public void ConfirmCrop() => Calls.Add(nameof(ConfirmCrop));
            public void CancelCrop() => Calls.Add(nameof(CancelCrop));

            public void Undo() => Calls.Add(nameof(Undo));
            public void Redo() => Calls.Add(nameof(Redo));

            public void CyclePrevRoom() => Calls.Add(nameof(CyclePrevRoom));
            public void CycleNextRoom() => Calls.Add(nameof(CycleNextRoom));
            public void SaveCurrentRoom() => Calls.Add(nameof(SaveCurrentRoom));
            public void SaveWorldMap() => Calls.Add(nameof(SaveWorldMap));
            public void ExitEditor() => Calls.Add(nameof(ExitEditor));
            public void SetMode(EditorMode mode) => Calls.Add(nameof(SetMode));
            public void ToggleSnap() => Calls.Add(nameof(ToggleSnap));
            public void ToggleAutoPunch() => Calls.Add(nameof(ToggleAutoPunch));
            public void ToggleFullscreen() => Calls.Add(nameof(ToggleFullscreen));
            public void ToggleMapMode() => Calls.Add(nameof(ToggleMapMode));
            public void ValidateReachability() => Calls.Add(nameof(ValidateReachability));
            public void ValidateDoors() => Calls.Add(nameof(ValidateDoors));
            public void AnalyzePuzzle() => Calls.Add(nameof(AnalyzePuzzle));
            public void OpenNewRoomPicker() => Calls.Add(nameof(OpenNewRoomPicker));
            public void OpenImportPicker() => Calls.Add(nameof(OpenImportPicker));
        }

        private static void AssertText(string label, string actual, string expected)
        {
            _checks++;
            bool ok = actual == expected;
            if (!ok) _failures++;
            Console.WriteLine($"    {(ok ? "ok  " : "FAIL")} {label}");
            if (!ok)
            {
                Console.WriteLine($"           expected: {expected}");
                Console.WriteLine($"           actual  : {actual}");
            }
        }

        // ====================================================================
        // HARNESS
        // ====================================================================

        /// <summary>
        /// A real ImGui context driven by synthetic input, with stand-in
        /// windows pinned to the editor's own EditorLayout rectangles.
        /// </summary>
        // The windows are stand-ins rather than the editor's real panels only
        // in what they CONTAIN. Where they are is not a stand-in: it comes from
        // the same EditorLayout the canvas measures itself against, so a
        // rectangle that moves moves for both.
        private sealed class Harness
        {
            public readonly ChromeInputRouter Router = new();

            private NVector2 _mouse;
            private bool _left, _right, _middle;
            private float _wheel;
            private readonly Dictionary<ImGuiKey, bool> _keys = new();

            public float PaletteScroll { get; private set; }

            public Harness()
            {
                ImGui.CreateContext();
                ImGui.StyleColorsDark();

                var io = ImGui.GetIO();
                io.ConfigFlags &= ~ImGuiConfigFlags.NavEnableKeyboard;

                // No imgui.ini, for the same reason ImGuiRenderer disables it,
                // and one more: this harness promises to write nothing, and
                // ImGui saves window settings on a timer driven by DeltaTime —
                // which a few hundred synthetic frames sails straight past.
                unsafe { io.NativePtr->IniFilename = null; }
                io.DisplaySize = new NVector2(1280, 720);
                io.DisplayFramebufferScale = new NVector2(1f, 1f);
                io.DeltaTime = 1f / 60f;

                // NewFrame asserts the atlas is built. Building it is CPU work;
                // the texture id is a token this harness never dereferences.
                io.Fonts.GetTexDataAsRGBA32(out IntPtr _, out int _, out int _, out int _);
                io.Fonts.SetTexID(new IntPtr(1));

                Resize(1280, 720);
            }

            public void Resize(int w, int hgt)
            {
                EditorLayout.Recalculate(w, hgt);
                ImGui.GetIO().DisplaySize = new NVector2(w, hgt);
                Settle();
            }

            public void MoveTo(NVector2 p) => _mouse = p;

            public void SetLeft(bool down) => _left = down;

            /// <summary>Hold or release a key, as the editor's own pump would.</summary>
            public void SetKey(ImGuiKey key, bool down) => _keys[key] = down;

            /// <summary>A press and release of one key, one frame each.</summary>
            public void TapKey(ImGuiKey key)
            {
                SetKey(key, true);
                Frame();
                SetKey(key, false);
                Frame();
            }

            /// <summary>
            /// Type text into whatever ImGui widget has the keyboard, the way
            /// ImGuiRenderer's TextInput handler does.
            /// </summary>
            // Queued and flushed inside Frame(), not pushed here: ImGui reads
            // io.InputQueueCharacters during NewFrame, so a character added
            // after NewFrame would be seen a frame late — or, if the frame in
            // between closed the widget, never.
            public void TypeText(string text)
            {
                foreach (char c in text) _pendingChars.Add(c);
                Frame();
                Frame();
            }

            private readonly List<char> _pendingChars = new();

            /// <summary>True while ImGui has any popup — a menu or a picker — open.</summary>
            public static bool AnyPopupOpen =>
                ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel);

            /// <summary>
            /// Leave whatever popup is up, so the next assertion starts clean.
            /// </summary>
            // SETTLE FIRST, then tap until it goes. A picker's first Escape
            // only defocuses its filter box — that two-step is section 15's
            // subject, and this is a tool rather than the thing under test, so
            // it loops instead of asserting a count.
            //
            // The settle is not decoration. ImGui's InputText ignores keys on
            // the frame its item was JUST activated, and the filter box is
            // activated by a focus request that resolves a frame after the
            // popup opens — so an Escape sent immediately after the opening
            // click is swallowed and the popup never closes. Measured, after a
            // section that opened a picker and then found the NEXT click going
            // to the still-open popup instead of the row it aimed at.
            public void CloseAnyPopup()
            {
                if (!AnyPopupOpen) return;
                Settle();
                for (int i = 0; i < 3 && AnyPopupOpen; i++)
                {
                    TapKey(ImGuiKey.Escape);
                    Settle();
                }
            }

            public void SetRight(bool down) => _right = down;

            public void SetMiddle(bool down) => _middle = down;

            public void Wheel(float notches) => _wheel = notches;

            /// <summary>
            /// Run frames until the answer is stable. ImGui decides what the
            /// mouse is over during NewFrame using the window rectangles the
            /// PREVIOUS frame produced, so a window that has just appeared or
            /// just moved does not capture on its first frame. The editor never
            /// notices — its windows exist and are static from frame one — but
            /// a harness that resizes between assertions would.
            /// </summary>
            public void Settle() { Frame(); Frame(); }

            public void Frame()
            {
                var io = ImGui.GetIO();
                io.DeltaTime = 1f / 60f;

                io.AddMousePosEvent(_mouse.X, _mouse.Y);
                io.AddMouseButtonEvent(0, _left);
                io.AddMouseButtonEvent(1, _right);
                io.AddMouseButtonEvent(2, _middle);
                if (_wheel != 0f) { io.AddMouseWheelEvent(0f, _wheel); _wheel = 0f; }
                foreach (var pair in _keys) io.AddKeyEvent(pair.Key, pair.Value);
                for (int i = 0; i < _pendingChars.Count; i++) io.AddInputCharacter(_pendingChars[i]);
                _pendingChars.Clear();

                ImGui.NewFrame();
                Router.Sample(io.WantCaptureMouse, io.WantCaptureKeyboard, AnyPopupOpen, io.WantTextInput);

                BuildChrome();

                ImGui.Render();
            }

            private static ImGuiWindowFlags PanelFlags =>
                ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus;

            // When these are set, the frame builds the EDITOR'S OWN palette
            // panel instead of the stand-in. Sections 1-5 want a stand-in they
            // can make overflow on demand; section 9 wants the real thing.
            private IChromeActions? _paletteActions;
            private EditorState? _paletteState;

            private IChromeActions? _inspectorActions;
            private EditorState? _inspectorState;

            // The three option lists the inspector's pickers read out of
            // ChromeView. Kept beside the band view rather than folded into it
            // so that section 12's modality drive (SetBandsModal) and section
            // 14's picker drive can be set independently — a picker open behind
            // a modal is a real combination and both flags have to reach the
            // panel at once.
            private IReadOnlyList<string>? _pickerRoomIds;
            private IReadOnlyList<ItemType>? _pickerItems;
            private Func<string, IReadOnlyList<string>>? _pickerDoorIds;
            private IChromeActions? _pickerActions;
            private ChromeView _pickerView;
            private bool _pickersOn;

            /// <summary>What the BANDS are told each frame — chiefly ModalOpen.</summary>
            private ChromeView _bandView;
            private IChromeActions? _menuActions;
            private EditorState? _menuState;
            private ChromeView _menuView;

            /// <summary>Switch the top band to the real MenuBar.</summary>
            public void DriveMenuBar(IChromeActions actions, EditorState state, ChromeView view)
            {
                _menuActions = actions;
                _menuState = state;
                _menuView = view;
                Settle();
            }

            /// <summary>Put the bands into (or out of) their modal-inert state.</summary>
            public void SetBandsModal(bool modal)
            {
                _bandView = new ChromeView { ModalOpen = modal };
                Settle();
            }

            /// <summary>Switch the palette band to the real PalettePanel.</summary>
            public void DrivePalette(IChromeActions actions, EditorState state)
            {
                _paletteActions = actions;
                _paletteState = state;
                Settle();
            }

            /// <summary>Switch the inspector band to the real InspectorPanel.</summary>
            public void DriveInspector(IChromeActions actions, EditorState state)
            {
                _inspectorActions = actions;
                _inspectorState = state;
                Settle();
            }

            /// <summary>Fill the three lists the inspector's pickers offer.</summary>
            public void SetPickerLists(IReadOnlyList<string> roomIds,
                                       IReadOnlyList<ItemType> items,
                                       Func<string, IReadOnlyList<string>> doorIds)
            {
                _pickerRoomIds = roomIds;
                _pickerItems = items;
                _pickerDoorIds = doorIds;
                Settle();
            }

            /// <summary>Draw the real Pickers over whatever else is up.</summary>
            public void DrivePickers(IChromeActions actions, ChromeView view)
            {
                _pickerActions = actions;
                _pickerView = view;
                _pickersOn = true;
                Settle();
            }

            /// <summary>
            /// A full click at a point: hover, settle, press, release. ImGui's
            /// buttons fire on RELEASE while still hovered, so a press alone
            /// proves nothing.
            /// </summary>
            public void ClickAt(NVector2 p)
            {
                MoveTo(p);
                Settle();
                SetLeft(true);
                Frame();
                SetLeft(false);
                Frame();
            }

            /// <summary>True while the cursor is over some hoverable ImGui item.</summary>
            // Read AFTER a frame, so it reports that frame's answer. Used to
            // FIND a popup's rows rather than to compute where they must be:
            // an open popup is positioned by ImGui against the mouse and
            // clamped to the viewport, and its internal spacing comes from the
            // style — so hard-coding a row's y would be asserting arithmetic
            // this harness had to reproduce, which is exactly the class of
            // duplicated-rectangle bug the ImGui migration deleted.
            public static bool AnyItemHovered => ImGui.IsAnyItemHovered();

            /// <summary>
            /// Walk down column <paramref name="x"/> and return a point inside
            /// the LAST hoverable thing found — which, in a filter popup
            /// narrowed to a single option, is that option's row.
            /// </summary>
            // The bottom of a popup's hoverable column is the bottom of its
            // list; the padding below it hits nothing. So with the list
            // narrowed to ONE row, four pixels above that boundary is inside
            // that row, whatever the style's padding and spacing happen to be.
            //
            // Counting hover RUNS instead — filter box, then row — was tried
            // and does not work: ImGui registers the child window itself as an
            // item, so the filter box and the list are one continuous hoverable
            // column with no gap to count.
            public float FindPopupLastRowY(float x, float fromY, float toY)
            {
                float lastHovered = -1f;
                for (float y = fromY; y <= toY; y += 2f)
                {
                    MoveTo(new NVector2(x, y));
                    Frame();
                    if (AnyItemHovered) lastHovered = y;
                }
                return lastHovered < 0f ? -1f : lastHovered - 4f;
            }

            /// <summary>Wheel the palette list as far down as it goes.</summary>
            public void ScrollPaletteToBottom(IChromeActions actions, EditorState state)
            {
                DrivePalette(actions, state);
                MoveTo(new NVector2(140f, EditorLayout.PaletteRect.Y + 200f));
                Settle();
                for (int i = 0; i < 40; i++) { Wheel(-1f); Frame(); }
                Settle();
            }

            public void ResetPaletteScroll(IChromeActions actions, EditorState state)
            {
                DrivePalette(actions, state);
                MoveTo(new NVector2(140f, EditorLayout.PaletteRect.Y + 200f));
                Settle();
                for (int i = 0; i < 40; i++) { Wheel(1f); Frame(); }
                Settle();
            }

            private void BuildChrome()
            {
                if (_menuActions != null && _menuState != null)
                    MenuBar.Draw(_menuActions, _menuState, _menuView);
                else
                    Panel("##topbar", EditorLayout.TopBarRect, () => ImGui.TextUnformatted("menu"));

                if (_paletteActions != null && _paletteState != null)
                {
                    PalettePanel.Draw(_paletteActions, _paletteState, _bandView);
                }
                else
                {
                    Panel("##palette", EditorLayout.PaletteRect, () =>
                    {
                        // Deliberately taller than the panel: the wheel section
                        // needs something that can actually scroll.
                        for (int i = 0; i < 60; i++) ImGui.TextUnformatted($"entry {i}");
                        PaletteScroll = ImGui.GetScrollY();
                    });
                }

                if (_inspectorActions != null && _inspectorState != null)
                {
                    // The band view plus whatever picker lists have been set:
                    // the modality flag and the option lists are independent,
                    // and both have to reach the panel in the same struct.
                    var inspectorView = _bandView;
                    inspectorView.TargetRoomIds = _pickerRoomIds;
                    inspectorView.RequiredItems = _pickerItems;
                    inspectorView.DoorIdsForRoom = _pickerDoorIds;
                    InspectorPanel.Draw(_inspectorActions, _inspectorState, inspectorView);
                }
                else
                {
                    Panel("##inspector", EditorLayout.InspectorRect, () => ImGui.TextUnformatted("inspector"));
                }

                Panel("##status", EditorLayout.StatusBarRect, () => ImGui.TextUnformatted("status"));

                if (_pickersOn && _pickerActions != null) Pickers.Draw(_pickerActions, _pickerView);
            }

            private static void Panel(string id, Microsoft.Xna.Framework.Rectangle r, Action body)
            {
                ImGui.SetNextWindowPos(new NVector2(r.X, r.Y), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new NVector2(r.Width, r.Height), ImGuiCond.Always);
                if (ImGui.Begin(id, PanelFlags)) body();
                ImGui.End();
            }
        }

        // ====================================================================
        // OUTPUT
        // ====================================================================

        private static NVector2 Centre(Microsoft.Xna.Framework.Rectangle r) =>
            new(r.X + r.Width / 2f, r.Y + r.Height / 2f);

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
    }
}
