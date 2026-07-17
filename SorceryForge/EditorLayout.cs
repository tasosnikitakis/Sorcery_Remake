using Microsoft.Xna.Framework;
using System;

namespace SorceryForge
{
    /// <summary>
    /// Screen layout. Recalculates on every window resize / fullscreen
    /// toggle so the canvas takes whatever space remains after the fixed
    /// top bar, palette, and status bar. Canvas always renders at the
    /// largest integer multiple of 320x144 that fits — pixel-art crispness
    /// at any window size.
    /// </summary>
    public static class EditorLayout
    {
        // Game-space room size (constant — the engine doesn't grow rooms).
        public const int RoomWidth = 320;
        public const int RoomHeight = 144;

        // Fixed UI band sizes.
        public const int TopBarHeight = 56;
        public const int StatusBarHeight = 32;
        public const int PaletteWidth = 280;
        public const int InspectorWidth = 300;
        public const int CanvasMargin = 20;

        // Current window size (mutated by Recalculate).
        public static int WindowWidth { get; private set; } = 1280;
        public static int WindowHeight { get; private set; } = 720;

        // Computed every Recalculate.
        public static int CanvasScale { get; private set; } = 3;
        public static Rectangle CanvasRect { get; private set; }
        public static Rectangle TopBarRect { get; private set; }
        public static Rectangle PaletteRect { get; private set; }
        public static Rectangle InspectorRect { get; private set; }
        public static Rectangle StatusBarRect { get; private set; }

        // Convenience accessors used throughout the editor.
        public static int CanvasX => CanvasRect.X;
        public static int CanvasY => CanvasRect.Y;
        public static int CanvasWidth => CanvasRect.Width;
        public static int CanvasHeight => CanvasRect.Height;
        public static int PaletteX => PaletteRect.X;
        public static int PaletteY => PaletteRect.Y;
        public static int PaletteHeight => PaletteRect.Height;
        public static int TopBarY => TopBarRect.Y;
        public static int StatusBarY => StatusBarRect.Y;

        static EditorLayout() => Recalculate(WindowWidth, WindowHeight);

        public static void Recalculate(int width, int height)
        {
            // Floors so the editor doesn't break when the user shrinks the
            // window below the fixed-band totals (palette + inspector + bars).
            WindowWidth  = Math.Max(900, width);
            WindowHeight = Math.Max(500, height);

            int availableW = WindowWidth - PaletteWidth - InspectorWidth - CanvasMargin * 2;
            int availableH = WindowHeight - TopBarHeight - StatusBarHeight - CanvasMargin * 2;

            // Largest integer scale that fits both dimensions; 1 is the floor.
            int scaleX = Math.Max(1, availableW / RoomWidth);
            int scaleY = Math.Max(1, availableH / RoomHeight);
            CanvasScale = Math.Min(scaleX, scaleY);

            int canvasW = RoomWidth * CanvasScale;
            int canvasH = RoomHeight * CanvasScale;
            int canvasX = PaletteWidth + CanvasMargin + (availableW - canvasW) / 2;
            int canvasY = TopBarHeight + CanvasMargin + (availableH - canvasH) / 2;

            int barH = WindowHeight - TopBarHeight - StatusBarHeight;
            CanvasRect    = new Rectangle(canvasX, canvasY, canvasW, canvasH);
            TopBarRect    = new Rectangle(0, 0, WindowWidth, TopBarHeight);
            PaletteRect   = new Rectangle(0, TopBarHeight, PaletteWidth, barH);
            InspectorRect = new Rectangle(WindowWidth - InspectorWidth, TopBarHeight, InspectorWidth, barH);
            StatusBarRect = new Rectangle(0, WindowHeight - StatusBarHeight, WindowWidth, StatusBarHeight);
        }

        // --- View transform (zoom & pan) --------------------------------
        // Zoom multiplies the base CanvasScale; (PanX, PanY) is the room
        // pixel shown at the canvas's top-left corner. Zoom levels are
        // powers of two so the visible region (RoomWidth/Zoom by
        // RoomHeight/Zoom) always divides 320x144 exactly and background
        // pixels stay integer-crisp.
        public static readonly int[] ZoomLevels = { 1, 2, 4, 8, 16 };
        public static int Zoom { get; private set; } = 1;
        public static int PanX { get; private set; }
        public static int PanY { get; private set; }

        public static int EffScale => CanvasScale * Zoom;
        public static int VisibleWidth => RoomWidth / Zoom;
        public static int VisibleHeight => RoomHeight / Zoom;

        public static void ResetView() { Zoom = 1; PanX = 0; PanY = 0; }

        public static void SetPan(int x, int y)
        {
            PanX = Math.Clamp(x, 0, RoomWidth - VisibleWidth);
            PanY = Math.Clamp(y, 0, RoomHeight - VisibleHeight);
        }

        /// <summary>
        /// Zoom one level in (+1) or out (-1), keeping the room point under
        /// anchorScreen stationary on screen (GIMP-style wheel zoom).
        /// </summary>
        public static void StepZoom(int direction, Point anchorScreen)
        {
            int idx = Array.IndexOf(ZoomLevels, Zoom);
            int next = Math.Clamp(idx + direction, 0, ZoomLevels.Length - 1);
            if (next == idx) return;

            Vector2 anchorGame = ScreenToGame(anchorScreen);
            Zoom = ZoomLevels[next];
            SetPan(
                (int)Math.Round(anchorGame.X - (anchorScreen.X - CanvasRect.X) / (float)EffScale),
                (int)Math.Round(anchorGame.Y - (anchorScreen.Y - CanvasRect.Y) / (float)EffScale));
        }

        public static Vector2 ScreenToGame(Point screen) =>
            new(PanX + (screen.X - CanvasRect.X) / (float)EffScale,
                PanY + (screen.Y - CanvasRect.Y) / (float)EffScale);

        public static Point GameToScreen(Vector2 game) =>
            new(CanvasRect.X + (int)((game.X - PanX) * EffScale),
                CanvasRect.Y + (int)((game.Y - PanY) * EffScale));

        public static Rectangle GameRectToScreen(Rectangle game) =>
            new(CanvasRect.X + (game.X - PanX) * EffScale,
                CanvasRect.Y + (game.Y - PanY) * EffScale,
                game.Width * EffScale,
                game.Height * EffScale);

        public static bool IsInsideCanvas(Point screen) => CanvasRect.Contains(screen);
    }
}
