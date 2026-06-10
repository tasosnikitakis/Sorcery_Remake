using System.IO;
using System.Text;
using System.Text.Json;
using SorceryRemake.Tiles;

namespace SorceryRemake.Rooms
{
    /// <summary>
    /// Loads room data from JSON files.
    /// </summary>
    public static class RoomLoader
    {
        /// <summary>
        /// Load a collision grid from a JSON file and build a collision-only TileMapComponent.
        /// The tilemap is NOT drawn visually - it only provides collision data.
        /// Cells with value 1 are mapped to a solid tile; cells with 0 are empty.
        /// </summary>
        public static int[,] LoadCollisionGrid(string jsonPath)
        {
            string json = File.ReadAllText(jsonPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            int width = root.GetProperty("width").GetInt32();
            int height = root.GetProperty("height").GetInt32();
            var collisionArray = root.GetProperty("collision");

            int[,] grid = new int[height, width];

            int row = 0;
            foreach (var rowElement in collisionArray.EnumerateArray())
            {
                int col = 0;
                foreach (var cell in rowElement.EnumerateArray())
                {
                    int value = cell.GetInt32();
                    // Map: 0 = empty, 1 = solid wall
                    grid[row, col] = value == 1 ? TileConfig.WALL_DARK_GRAY : TileConfig.EMPTY;
                    col++;
                }
                row++;
            }

            return grid;
        }

        /// <summary>
        /// Build a TileMapComponent from a collision grid loaded from JSON.
        /// This tilemap is collision-only (not rendered visually).
        /// </summary>
        public static TileMapComponent BuildCollisionTileMap(
            Microsoft.Xna.Framework.Graphics.Texture2D tilesetTexture,
            string jsonPath)
        {
            int[,] grid = LoadCollisionGrid(jsonPath);
            int height = grid.GetLength(0);
            int width = grid.GetLength(1);

            var map = new TileMapComponent(tilesetTexture, width, height);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    map.SetTile(x, y, grid[y, x]);
                }
            }

            return map;
        }

        /// <summary>
        /// Write a collision grid to JSON in the same shape the loader reads.
        /// Each cell becomes 0 (empty) or 1 (solid) based on TileConfig.IsSolid;
        /// platforms, ladders, decorations all collapse to 0/1 since the live
        /// collision system is currently binary.
        /// </summary>
        public static void SaveCollisionGrid(string jsonPath, TileMapComponent map)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"collision\": [\n");

            for (int y = 0; y < map.Height; y++)
            {
                sb.Append("    [");
                for (int x = 0; x < map.Width; x++)
                {
                    int v = TileConfig.IsSolid(map.GetTile(x, y)) ? 1 : 0;
                    sb.Append(v);
                    if (x < map.Width - 1) sb.Append(", ");
                }
                sb.Append(']');
                if (y < map.Height - 1) sb.Append(',');
                sb.Append('\n');
            }

            sb.Append("  ],\n");
            sb.Append($"  \"width\": {map.Width},\n");
            sb.Append($"  \"height\": {map.Height}\n");
            sb.Append("}\n");

            Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
            File.WriteAllText(jsonPath, sb.ToString());
        }
    }
}
