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

        private static NVector4 Rgb(int r, int g, int b) =>
            new(r / 255f, g / 255f, b / 255f, 1f);
    }
}
