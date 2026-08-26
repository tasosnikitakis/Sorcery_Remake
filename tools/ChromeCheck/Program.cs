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

            Console.WriteLine();
            Console.WriteLine($"  {_checks} checks, {_failures} failure(s)");
            Console.WriteLine();
            Console.WriteLine(_failures == 0
                ? "  ROUTING HOLDS: ImGui has first refusal, the canvas keeps what it started,\n" +
                  "  and one wheel notch reaches exactly one consumer."
                : "  ROUTING BROKEN — see the FAIL lines above.");

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

            private void BuildChrome()
            {
                Panel("##topbar", EditorLayout.TopBarRect, () => ImGui.TextUnformatted("menu"));

                Panel("##palette", EditorLayout.PaletteRect, () =>
                {
                    // Deliberately taller than the panel: the wheel section
                    // needs something that can actually scroll.
                    for (int i = 0; i < 60; i++) ImGui.TextUnformatted($"entry {i}");
                    PaletteScroll = ImGui.GetScrollY();
                });

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
