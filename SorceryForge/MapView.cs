// ============================================================================
// MAP VIEW
// SorceryForge — the world map's zoom-and-pan transform
// ============================================================================
// EditorLayout owns the room canvas's view as static state, which is right for
// a thing there is exactly one of. The map is a second view onto a different
// coordinate space, and EDITOR_REVIEW item D calls for it as an INSTANCE
// rather than a second set of statics — so the two cannot leak into each other
// and Tab does not have to save and restore anything.
//
// Same shape as EditorLayout's transform, deliberately: wheel zoom anchored at
// the cursor, drag to pan, a fixed ladder of zoom levels. Two differences, both
// forced by what the map is:
//
//   - The ladder runs BELOW 1. A room box is 320x144 map units, and seventy-
//     five of them do not fit on a monitor at 1:1. Every level is a power of
//     two so a thumbnail is always an exact integer downscale of its
//     background — 320 -> 160, 80, 40, 20 — which is what keeps PointClamp
//     giving a readable picture instead of shimmer.
//   - Pan is unbounded-ish rather than clamped to a fixed 320x144. The board
//     grows as rooms are added and dragged, so panning is clamped to the
//     content's own bounds plus a margin, recomputed from whatever is on the
//     board now.
// ============================================================================

using Microsoft.Xna.Framework;
using System;

namespace SorceryForge
{
    public class MapView
    {
        /// <summary>Screen pixels per map unit. Powers of two; see the header.</summary>
        public static readonly float[] ZoomLevels = { 0.0625f, 0.125f, 0.25f, 0.5f, 1f };

        /// <summary>Index into ZoomLevels. 0.25 shows a room as an 80x36 thumbnail.</summary>
        public int ZoomIndex = 2;

        public float Scale => ZoomLevels[ZoomIndex];

        /// <summary>Map-unit point shown at the viewport's top-left corner.</summary>
        public Vector2 Pan;

        /// <summary>Screen rectangle the board is drawn into.</summary>
        public Rectangle Viewport;

        /// <summary>How far past the content the view may be panned, in map units.</summary>
        private const float PanMargin = 256f;

        public float VisibleWidth => Viewport.Width / Scale;
        public float VisibleHeight => Viewport.Height / Scale;

        public Vector2 ScreenToMap(Point screen) =>
            Pan + new Vector2((screen.X - Viewport.X) / Scale, (screen.Y - Viewport.Y) / Scale);

        /// <summary>A screen distance as a map distance — for drag deltas, which carry no origin.</summary>
        public Vector2 ScreenDeltaToMap(Point delta) =>
            new(delta.X / Scale, delta.Y / Scale);

        public Point MapToScreen(Vector2 map) =>
            new(Viewport.X + (int)MathF.Round((map.X - Pan.X) * Scale),
                Viewport.Y + (int)MathF.Round((map.Y - Pan.Y) * Scale));

        public Rectangle MapRectToScreen(Rectangle map)
        {
            // Both corners are mapped and subtracted rather than the origin
            // mapped and the size scaled: that keeps adjacent boxes' edges
            // agreeing at every zoom instead of drifting a pixel apart, the
            // same reason the crop overlay does it this way.
            var topLeft = MapToScreen(new Vector2(map.Left, map.Top));
            var bottomRight = MapToScreen(new Vector2(map.Right, map.Bottom));
            return new Rectangle(topLeft.X, topLeft.Y,
                                 Math.Max(1, bottomRight.X - topLeft.X),
                                 Math.Max(1, bottomRight.Y - topLeft.Y));
        }

        /// <summary>
        /// Zoom one level in (+1) or out (-1), keeping the map point under
        /// <paramref name="anchorScreen"/> where it is.
        /// </summary>
        public void StepZoom(int direction, Point anchorScreen, Rectangle contentBounds)
        {
            int next = Math.Clamp(ZoomIndex + direction, 0, ZoomLevels.Length - 1);
            if (next == ZoomIndex) return;

            Vector2 anchorMap = ScreenToMap(anchorScreen);
            ZoomIndex = next;
            Pan = anchorMap - new Vector2((anchorScreen.X - Viewport.X) / Scale,
                                          (anchorScreen.Y - Viewport.Y) / Scale);
            ClampPan(contentBounds);
        }

        /// <summary>
        /// Keep the board reachable: the view may run up to PanMargin past the
        /// content in any direction, and when the content is smaller than the
        /// viewport it simply sits centred.
        /// </summary>
        public void ClampPan(Rectangle contentBounds)
        {
            if (contentBounds.IsEmpty) return;
            Pan = new Vector2(
                ClampAxis(Pan.X, contentBounds.Left, contentBounds.Right, VisibleWidth),
                ClampAxis(Pan.Y, contentBounds.Top, contentBounds.Bottom, VisibleHeight));
        }

        private static float ClampAxis(float value, float contentMin, float contentMax, float visible)
        {
            float min = contentMin - PanMargin;
            float max = contentMax + PanMargin - visible;
            // Content narrower than the viewport: there is nothing to scroll,
            // so pin it centred rather than letting it slide about.
            if (max < min) return (contentMin + contentMax) / 2f - visible / 2f;
            return Math.Clamp(value, min, max);
        }

        /// <summary>
        /// Frame the whole board: the largest zoom level at which the content
        /// fits, centred. Used on the first entry into map mode, so the user
        /// arrives looking at the world rather than at one corner of it.
        /// </summary>
        public void FitTo(Rectangle contentBounds)
        {
            if (contentBounds.IsEmpty || Viewport.Width <= 0 || Viewport.Height <= 0) return;

            // Walk down from the largest level and take the first that fits;
            // if none does, the smallest is the best available.
            ZoomIndex = 0;
            for (int i = ZoomLevels.Length - 1; i >= 0; i--)
            {
                float s = ZoomLevels[i];
                if (contentBounds.Width * s <= Viewport.Width && contentBounds.Height * s <= Viewport.Height)
                {
                    ZoomIndex = i;
                    break;
                }
            }

            Pan = new Vector2(
                contentBounds.Left + contentBounds.Width / 2f - VisibleWidth / 2f,
                contentBounds.Top + contentBounds.Height / 2f - VisibleHeight / 2f);
            ClampPan(contentBounds);
        }

        /// <summary>Zoom as a percentage, for the status line.</summary>
        public int ZoomPercent => (int)MathF.Round(Scale * 100f);
    }
}
