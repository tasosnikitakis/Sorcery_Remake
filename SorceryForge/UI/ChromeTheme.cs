// ============================================================================
// CHROME THEME
// SorceryForge — the colours and window shape every chrome panel shares
// ============================================================================
// The hand-rolled chrome carried its palette inline: `new Color(24, 26, 32)`
// for the top bar, `new Color(28, 30, 38)` for the two side panels, amber
// `(255, 220, 110)` for every section header and every modal title, and
// `(200, 200, 220)` for status text. Those values are kept here rather than
// dropped, because they are the editor's look and because half of them are
// SHARED with the canvas overlays, which are still SpriteBatch and still use
// the MonoGame Color literals. A door verdict that is amber on the canvas and
// a different amber in the inspector would read as two different states.
//
// ImGui works in Vector4 with 0..1 channels, so each value appears once, here,
// converted once. StatusColor's canvas-side twin lives in EditorGame and is
// deliberately not moved: the canvas is not chrome.
//
// WINDOW SHAPE. Every fixed panel is pinned to an EditorLayout rectangle and
// stripped of the decoration a floating window would carry. NoSavedSettings
// matters even with the ini file disabled — it stops ImGui allocating settings
// state per window for something that is repositioned every frame anyway.
// ============================================================================

using ImGuiNET;
using Microsoft.Xna.Framework;
using System;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace SorceryForge.UI
{
    public static class ChromeTheme
    {
        // ---- The editor's palette, verbatim from the chrome it replaces ----

        /// <summary>Section headers, modal titles, selection, paint cursor.</summary>
        public static readonly NVector4 Amber = Rgb(255, 220, 110);

        /// <summary>Panel titles and other quiet labels.</summary>
        public static readonly NVector4 Muted = Rgb(180, 180, 200);

        /// <summary>Dimmer still: hints, secondary lines in modal rows.</summary>
        public static readonly NVector4 Dim = Rgb(150, 160, 185);

        /// <summary>The status line's own text.</summary>
        public static readonly NVector4 Status = Rgb(200, 200, 220);

        /// <summary>Right-hand view info in the status bar.</summary>
        public static readonly NVector4 ViewInfo = Rgb(150, 170, 200);

        /// <summary>Values and IDs inside inspector rows.</summary>
        public static readonly NVector4 Value = Rgb(180, 200, 230);

        /// <summary>A row that cannot be used, and the reason beside it.</summary>
        public static readonly NVector4 Unavailable = Rgb(255, 140, 140);

        /// <summary>Anything unsaved. The one colour the `*` markers wear.</summary>
        public static readonly NVector4 Dirty = Rgb(255, 190, 90);

        public static readonly NVector4 White = Rgb(255, 255, 255);

        // ---- Window shape --------------------------------------------------

        /// <summary>
        /// A panel pinned to a fixed screen rectangle: no title bar, no move,
        /// no resize, no collapse, and never raised over the modal overlays.
        /// </summary>
        public const ImGuiWindowFlags PanelFlags =
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus |
            ImGuiWindowFlags.NoNavFocus;

        /// <summary>
        /// Open a panel pinned to <paramref name="r"/>. Returns false when the
        /// window is fully clipped, in which case the caller must still call
        /// <see cref="EndPanel"/> — ImGui's Begin/End are unconditional pairs.
        /// </summary>
        public static bool BeginPanel(string id, Rectangle r, ImGuiWindowFlags extra = ImGuiWindowFlags.None)
        {
            ImGui.SetNextWindowPos(new NVector2(r.X, r.Y), ImGuiCond.Always);
            ImGui.SetNextWindowSize(new NVector2(Math.Max(1, r.Width), Math.Max(1, r.Height)), ImGuiCond.Always);
            return ImGui.Begin(id, PanelFlags | extra);
        }

        public static void EndPanel() => ImGui.End();

        // ---- Helpers -------------------------------------------------------

        /// <summary>
        /// Move the cursor so that <paramref name="text"/> ends
        /// <paramref name="rightPad"/> px from the current window's right edge.
        /// </summary>
        // Right-alignment is how the status bar's view info and the toolbar's
        // room title stay put at every window width, in place of the old
        // chrome's gap arithmetic between two button banks — which is what made
        // the room title vanish below about 1244 px.
        public static void CursorToRightOf(string text, float rightPad = 8f)
        {
            float width = ImGui.CalcTextSize(text).X;
            float x = ImGui.GetWindowWidth() - width - rightPad;
            ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), x));
        }

        /// <summary>
        /// Pack a colour the way ImGui's draw lists want it: IM_COL32, R in the
        /// low byte. The same order MonoGame's Color.PackedValue uses, which is
        /// why the renderer can hand ImGui's vertex buffer straight to a
        /// MonoGame VertexElementFormat.Color without transcoding.
        /// </summary>
        public static uint Packed(int r, int g, int b, int a = 255) =>
            (uint)((a << 24) | (b << 16) | (g << 8) | r);

        /// <summary>
        /// Install the editor's look: no rounding anywhere, a 1-px border, and
        /// the panel/field colours the hand-rolled chrome used. Called once.
        /// </summary>
        // Set on the style rather than pushed per window: these are what the
        // editor looks like, not a local override, and a push/pop per panel is
        // a pair that eventually gets unbalanced.
        public static void Install()
        {
            var style = ImGui.GetStyle();

            // Square everything. The old chrome drew rectangles with a 1x1
            // texture and had no rounding to offer; matching it keeps the
            // canvas overlays and the panels looking like one program.
            style.WindowRounding = 0f;
            style.ChildRounding = 0f;
            style.FrameRounding = 0f;
            style.PopupRounding = 0f;
            style.ScrollbarRounding = 0f;
            style.GrabRounding = 0f;
            style.TabRounding = 0f;

            style.WindowBorderSize = 1f;
            style.ChildBorderSize = 0f;
            style.FrameBorderSize = 1f;
            style.ScrollbarSize = 10f;

            Set(ImGuiCol.WindowBg, 28, 30, 38);
            Set(ImGuiCol.ChildBg, 28, 30, 38);
            Set(ImGuiCol.PopupBg, 30, 33, 42);
            Set(ImGuiCol.Border, 60, 64, 78);
            Set(ImGuiCol.MenuBarBg, 24, 26, 32);      // the old top bar's fill
            Set(ImGuiCol.Text, 230, 230, 240);
            Set(ImGuiCol.TextDisabled, 120, 125, 140); // the old inert-button label

            // Buttons and fields reuse the value-box colours from the
            // inspector's rows, so a menu, a toolbar button and an inspector
            // field are recognisably the same control.
            Set(ImGuiCol.Button, 50, 55, 70);
            Set(ImGuiCol.ButtonHovered, 70, 78, 100);
            Set(ImGuiCol.ButtonActive, 80, 90, 130);
            Set(ImGuiCol.FrameBg, 40, 46, 60);
            Set(ImGuiCol.FrameBgHovered, 60, 75, 110);
            Set(ImGuiCol.FrameBgActive, 70, 85, 120);
            Set(ImGuiCol.Header, 70, 90, 130);         // the selected inspector header
            Set(ImGuiCol.HeaderHovered, 60, 75, 110);
            Set(ImGuiCol.HeaderActive, 80, 90, 130);
            Set(ImGuiCol.ScrollbarBg, 40, 44, 56);
            Set(ImGuiCol.ScrollbarGrab, 120, 130, 160);
            Set(ImGuiCol.ScrollbarGrabHovered, 140, 150, 180);
            Set(ImGuiCol.ScrollbarGrabActive, 160, 170, 200);
            Set(ImGuiCol.CheckMark, 120, 230, 140);    // the import toggle's tick
            Set(ImGuiCol.Separator, 60, 64, 78);
            Set(ImGuiCol.ModalWindowDimBg, 0, 0, 0, 170);  // the pickers' dim, exactly
        }

        private static void Set(ImGuiCol idx, int r, int g, int b, int a = 255) =>
            ImGui.GetStyle().Colors[(int)idx] = new NVector4(r / 255f, g / 255f, b / 255f, a / 255f);

        private static NVector4 Rgb(int r, int g, int b) =>
            new(r / 255f, g / 255f, b / 255f, 1f);
    }
}
