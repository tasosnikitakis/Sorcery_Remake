// ============================================================================
// STATUS BAR
// SorceryForge — the bottom band: what just happened, and what is unsaved
// ============================================================================
// SHAPE, unchanged from the band it replaces: the transient status message on
// the left, a fixed view-info group flush right, and the message truncated —
// never the group — when the two would collide. That priority is the point.
// The message is a sentence about the last thing you did and can be re-read by
// doing it again; the group is state you cannot recover by any other means.
//
// THE MARKER GROUP now carries three markers rather than two:
//
//   room*   any of PlacementsDirty / CollisionDirty / BackgroundDirty — the
//           exact condition that makes the discard guard block a room switch,
//           and the condition the toolbar's room title marks with its "*"
//   PNG*    BackgroundDirty specifically: erase strokes and punches that Save
//           will write back to Content/RoomBG_*.png, which is the one save
//           that also needs a content rebuild before the GAME sees it
//   map*    MapDirty: a board arrangement dragged but not yet written
//
// room* is the addition, and it is why the unsaved warning can no longer be
// lost. The old chrome's only always-on sign of unsaved work was a "*" in the
// top bar's room title, drawn only if it fitted a gap between two button banks
// — a gap that was 36 px at a default 1280 px window. Here the group is placed
// by measuring itself against the window's right edge and nothing competes
// with it for room.
//
// The three deliberately overlap rather than partition. PNG* implies room*,
// and that is not redundancy: they name different saves. Ctrl+S writes both,
// but only the PNG needs `dotnet build` afterwards, and only the map needs you
// to be looking at the map.
// ============================================================================

using ImGuiNET;
using System.Text;
using NVector2 = System.Numerics.Vector2;

namespace SorceryForge.UI
{
    public static class StatusBar
    {
        public static void Draw(EditorState state, in ChromeView view)
        {
            if (ChromeTheme.BeginPanel("##sf_status", EditorLayout.StatusBarRect))
            {
                string info = ViewInfo(state, view);

                // The group first, measured, so the message below knows how
                // much room it has. ImGui clips the message at the window edge
                // anyway; the explicit wrap position stops it from being drawn
                // UNDER the group, which clipping alone would allow.
                float infoWidth = ImGui.CalcTextSize(info).X;
                float messageRoom = ImGui.GetWindowWidth() - infoWidth - 24f;

                ImGui.AlignTextToFramePadding();
                if (messageRoom > 16f)
                {
                    ImGui.PushTextWrapPos(messageRoom);
                    ImGui.TextColored(ChromeTheme.Status, state.Status ?? "");
                    ImGui.PopTextWrapPos();
                    ImGui.SameLine();
                }

                ChromeTheme.CursorToRightOf(info);
                ImGui.TextColored(ChromeTheme.ViewInfo, info);
            }
            ChromeTheme.EndPanel();
        }

        /// <summary>
        /// The right-hand group. Fragment order is fixed and each fragment is
        /// conditional on exactly one piece of state, so the line reads the
        /// same way every time and a missing fragment means a specific thing.
        /// </summary>
        // Pure — no ImGui call — so tools/ChromeCheck can assert every form of
        // it. This string is the editor's only always-visible readout of what
        // is unsaved, which makes "did a fragment quietly stop appearing" a
        // question worth being able to answer without a screenshot.
        internal static string ViewInfo(EditorState state, in ChromeView view)
        {
            var s = new StringBuilder();

            if (view.MapMode)
            {
                s.Append("Map ").Append(view.MapZoomPercent).Append('%');
                AppendMarkers(s, state, view);
                // Persistent hints belong here rather than in the transient
                // message on the left, which any drag or zoom overwrites.
                s.Append(" | N: new | I: import | Tab/Esc: room");
                return s.ToString();
            }

            s.Append("Zoom ").Append(view.Zoom).Append('x');
            if (state.Mode == EditorMode.Erase)
                s.Append(" | Brush ").Append(state.BrushSize).Append("px");
            AppendMarkers(s, state, view);
            s.Append(" | Tab: map");
            return s.ToString();
        }

        // Shown from BOTH modes, all three of them. An unsaved room is a thing
        // quitting would lose whether or not you are looking at it, and the
        // same goes for the board — which is exactly why the exit guard is the
        // one caller that passes includeMap.
        private static void AppendMarkers(StringBuilder s, EditorState state, in ChromeView view)
        {
            if (view.RoomDirty) s.Append(" | room*");
            if (state.BackgroundDirty) s.Append(" | PNG*");
            if (state.MapDirty) s.Append(" | map*");
        }
    }
}
