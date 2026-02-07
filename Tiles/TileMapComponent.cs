// ============================================================================
// TILEMAP COMPONENT
// Sorcery+ Remake - Tile Grid Rendering
// ============================================================================
// Renders a grid of tiles for a room.
// Each room is typically 40x18 tiles (320x144 pixels at 8x8 tile size).
//
// COORDINATE SYSTEM:
// - Tiles are positioned in a grid (0-39 horizontal, 0-17 vertical)
// - Each tile is 8x8 pixels
// - Total room size: 320x144 pixels (matches Python prototype)
// ============================================================================

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SorceryRemake.Core;
using System;

namespace SorceryRemake.Tiles
{
    /// <summary>
    /// Component that renders a tilemap (grid of tiles).
    /// </summary>
    public class TileMapComponent : IComponent
    {
        // ====================================================================
        // COMPONENT INTERFACE
        // ====================================================================

        public Entity? Owner { get; set; }

        // ====================================================================
        // TILEMAP DATA
        // ====================================================================

        /// <summary>
        /// The tileset texture (64x64 pixels, 8x8 grid of 8x8 tiles).
        /// </summary>
        public Texture2D? TilesetTexture { get; set; }

        /// <summary>
        /// 2D array of tile IDs.
        /// [row, col] or [y, x] format.
        /// </summary>
        public int[,] Tiles { get; set; }

        /// <summary>
        /// Width of the tilemap in tiles.
        /// </summary>
        public int Width { get; private set; }

        /// <summary>
        /// Height of the tilemap in tiles.
        /// </summary>
        public int Height { get; private set; }

        // ====================================================================
        // CONSTRUCTOR
        // ====================================================================

        public TileMapComponent(Texture2D? tilesetTexture, int width, int height)
        {
            TilesetTexture = tilesetTexture;
            Width = width;
            Height = height;
            Tiles = new int[height, width];

            // Initialize with empty tiles
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Tiles[y, x] = TileConfig.EMPTY;
                }
            }
        }

        // ====================================================================
        // TILE ACCESS
        // ====================================================================

        /// <summary>
        /// Get the tile ID at a specific grid position.
        /// </summary>
        public int GetTile(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                return TileConfig.EMPTY;

            return Tiles[y, x];
        }

        /// <summary>
        /// Set the tile ID at a specific grid position.
        /// </summary>
        public void SetTile(int x, int y, int tileId)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
                return;

            Tiles[y, x] = tileId;
        }

        /// <summary>
        /// Get tile ID at a world pixel position.
        /// </summary>
        public int GetTileAtPosition(float worldX, float worldY)
        {
            int tileX = (int)(worldX / TileConfig.TILE_SIZE);
            int tileY = (int)(worldY / TileConfig.TILE_SIZE);
            return GetTile(tileX, tileY);
        }

        /// <summary>
        /// Load tile data from a 2D array.
        /// </summary>
        public void LoadTiles(int[,] tileData)
        {
            if (tileData.GetLength(0) != Height || tileData.GetLength(1) != Width)
            {
                throw new ArgumentException(
                    $"Tile data size mismatch. Expected {Height}x{Width}, got {tileData.GetLength(0)}x{tileData.GetLength(1)}"
                );
            }

            Tiles = tileData;
        }

        // ====================================================================
        // COLLISION HELPERS
        // ====================================================================

        /// <summary>
        /// Check if a tile at grid position is solid.
        /// </summary>
        public bool IsTileSolid(int x, int y)
        {
            int tileId = GetTile(x, y);
            return TileConfig.IsSolid(tileId);
        }

        /// <summary>
        /// Check if a tile at world position is solid.
        /// </summary>
        public bool IsPositionSolid(float worldX, float worldY)
        {
            int tileId = GetTileAtPosition(worldX, worldY);
            return TileConfig.IsSolid(tileId);
        }

        // ====================================================================
        // UPDATE
        // ====================================================================

        public void Update(GameTime gameTime)
        {
            // Tilemaps are static, no update logic needed
        }

        // ====================================================================
        // DRAW - RENDER TILEMAP
        // ====================================================================

        public void Draw(GameTime gameTime)
        {
            // Rendering is handled by the rendering system
            // This method is kept for interface compliance
        }

        /// <summary>
        /// Draw the tilemap using a provided SpriteBatch.
        /// Called by the rendering system.
        /// </summary>
        public void Draw(SpriteBatch spriteBatch, float scale)
        {
            if (TilesetTexture == null)
                return;

            // Draw each tile
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int tileId = Tiles[y, x];

                    // Skip empty tiles (optimization)
                    if (tileId == TileConfig.EMPTY)
                        continue;

                    // Get source rectangle from tileset
                    Rectangle sourceRect = TileConfig.GetTileSourceRect(tileId);

                    // Calculate destination position
                    // Tiles are at base 8x8 size, scaled up by render scale
                    Vector2 position = new Vector2(
                        x * TileConfig.TILE_SIZE * scale,
                        y * TileConfig.TILE_SIZE * scale
                    );

                    // Draw tile
                    spriteBatch.Draw(
                        texture: TilesetTexture,
                        position: position,
                        sourceRectangle: sourceRect,
                        color: Color.White,
                        rotation: 0f,
                        origin: Vector2.Zero,
                        scale: scale,
                        effects: SpriteEffects.None,
                        layerDepth: 0f
                    );
                }
            }
        }

        // ====================================================================
        // HELPER METHODS
        // ====================================================================

        /// <summary>
        /// Fill a rectangular area with a specific tile.
        /// </summary>
        public void FillRect(int startX, int startY, int width, int height, int tileId)
        {
            for (int y = startY; y < startY + height; y++)
            {
                for (int x = startX; x < startX + width; x++)
                {
                    SetTile(x, y, tileId);
                }
            }
        }

        /// <summary>
        /// Draw a horizontal line of tiles.
        /// </summary>
        public void DrawHorizontalLine(int startX, int y, int length, int tileId)
        {
            for (int x = startX; x < startX + length; x++)
            {
                SetTile(x, y, tileId);
            }
        }

        /// <summary>
        /// Draw a vertical line of tiles.
        /// </summary>
        public void DrawVerticalLine(int x, int startY, int length, int tileId)
        {
            for (int y = startY; y < startY + length; y++)
            {
                SetTile(x, y, tileId);
            }
        }

        /// <summary>
        /// Draw a rectangle outline.
        /// </summary>
        public void DrawRectOutline(int startX, int startY, int width, int height, int tileId)
        {
            // Top and bottom
            DrawHorizontalLine(startX, startY, width, tileId);
            DrawHorizontalLine(startX, startY + height - 1, width, tileId);

            // Left and right
            DrawVerticalLine(startX, startY, height, tileId);
            DrawVerticalLine(startX + width - 1, startY, height, tileId);
        }
    }
}
