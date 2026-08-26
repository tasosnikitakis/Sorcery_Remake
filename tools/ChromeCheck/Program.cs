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
//   5 keyboard    editor keybinds keep firing; no chrome text field steals them
//   6 menus       what the board disables, and the four documented exceptions
//   7 titles      the room title and the board title, verbatim, and which
//                 '*' means which unsaved thing
//   8 status      every fragment of the status line's right-hand group, in
//                 order, including the three unsaved markers
//   9 palette     the real PalettePanel, driven with synthetic clicks: the row
//                 you click is the row you saw, scrolled or not
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
using SorceryForge;
using SorceryForge.UI;
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
            h.SetLeft(true);
            h.Frame();
            Console.WriteLine("      (holding a chrome widget: WantCaptureKeyboard=" +
                              $"{h.Router.ImGuiWantsKeyboard} — keys " +
                              (h.Router.KeyboardReachesEditor ? "still reach the editor" : "are held by the chrome") + ")");
            h.SetLeft(false);
            h.Frame();
            h.Settle();
            Assert("after releasing, keys reach the editor again", h.Router.KeyboardReachesEditor);
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

            // Map mode: the board's own zoom, the markers, then the persistent
            // hints — which live here precisely because the transient message
            // on the left is overwritten by every drag and every zoom.
            var map = new ChromeView { MapMode = true, MapZoomPercent = 25, RoomDirty = true };
            AssertText("board, everything unsaved", StatusBar.ViewInfo(state, map),
                "Map 25% | room* | PNG* | map* | N: new | I: import | Tab/Esc: room");

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

            public void Reset() { Picked = null; Calls.Clear(); }

            public void BeginPaletteDrag(PaletteEntry entry)
            {
                Picked = entry.Label;
                Calls.Add("BeginPaletteDrag");
            }

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

            public float PaletteScroll { get; private set; }

            public Harness()
            {
                ImGui.CreateContext();
                ImGui.StyleColorsDark();

                var io = ImGui.GetIO();
                io.ConfigFlags &= ~ImGuiConfigFlags.NavEnableKeyboard;
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

                ImGui.NewFrame();
                Router.Sample(io.WantCaptureMouse, io.WantCaptureKeyboard);

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

            /// <summary>Switch the palette band to the real PalettePanel.</summary>
            public void DrivePalette(IChromeActions actions, EditorState state)
            {
                _paletteActions = actions;
                _paletteState = state;
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
                Panel("##topbar", EditorLayout.TopBarRect, () => ImGui.TextUnformatted("menu"));

                if (_paletteActions != null && _paletteState != null)
                {
                    PalettePanel.Draw(_paletteActions, _paletteState);
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

                Panel("##inspector", EditorLayout.InspectorRect, () => ImGui.TextUnformatted("inspector"));
                Panel("##status", EditorLayout.StatusBarRect, () => ImGui.TextUnformatted("status"));
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
