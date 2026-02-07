// ============================================================================
// TILE CONFIGURATION
// Sorcery+ Remake - Tile Definitions and Properties
// ============================================================================
// Defines all tile types, their IDs, and gameplay properties.
// Tiles are 8x8 pixels arranged in an 8x8 grid (64 total tiles).
//
// TILE PROPERTIES:
// - Solid: Player collides with these (walls, floors)
// - Passthrough: Player can walk through (air, background)
// - Platform: Player stands on top but can jump through from below
// - Deadly: Touching kills player (spikes, hazards)
// - Ladder: Player can climb
// ============================================================================

using Microsoft.Xna.Framework;

namespace SorceryRemake.Tiles
{
    /// <summary>
    /// Tile behavior types for collision detection.
    /// </summary>
    public enum TileType
    {
        Empty,       // Air, passthrough
        Solid,       // Solid wall/floor
        Platform,    // Can stand on top, can jump through from below
        Ladder,      // Climbable
        Deadly,      // Kills player on contact
        Decoration   // Visual only, no collision
    }

    /// <summary>
    /// Configuration and properties for all tiles in the game.
    /// </summary>
    public static class TileConfig
    {
        // ====================================================================
        // TILE DIMENSIONS
        // ====================================================================

        /// <summary>
        /// Tile size in game pixels (8x8 at 1x scale).
        /// </summary>
        public const int TILE_SIZE = 8;

        /// <summary>
        /// Number of tiles per row in the tileset image.
        /// </summary>
        public const int TILES_PER_ROW = 8;

        /// <summary>
        /// Number of tiles per column in the tileset image.
        /// </summary>
        public const int TILES_PER_COL = 8;

        /// <summary>
        /// Total number of tiles in the tileset.
        /// </summary>
        public const int TOTAL_TILES = TILES_PER_ROW * TILES_PER_COL; // 64

        // ====================================================================
        // TILE ID CONSTANTS
        // Organized by row (0-7) in the tileset
        // ====================================================================

        // ROW 0: SOLID WALLS (0-7)
        public const int WALL_DARK_GRAY = 0;
        public const int WALL_MEDIUM_GRAY = 1;
        public const int WALL_LIGHT_GRAY = 2;
        public const int WALL_BROWN_BRICK = 3;
        public const int WALL_LIGHT_BROWN = 4;
        public const int WALL_BLUE_GRAY = 5;
        public const int WALL_RED_BROWN = 6;
        public const int WALL_GREEN_GRAY = 7;

        // ROW 1: FLOOR TILES (8-15)
        public const int FLOOR_TAN = 8;
        public const int FLOOR_BROWN = 9;
        public const int FLOOR_LIGHT_TAN = 10;
        public const int FLOOR_DARK_BROWN = 11;
        public const int FLOOR_GRAY_STONE = 12;
        public const int FLOOR_LIGHT_STONE = 13;
        public const int FLOOR_DARK_WOOD = 14;
        public const int FLOOR_LIGHT_WOOD = 15;

        // ROW 2: PLATFORMS (16-23)
        public const int PLATFORM_LIGHT = 16;
        public const int PLATFORM_MEDIUM = 17;
        public const int PLATFORM_DARK = 18;
        public const int PLATFORM_YELLOW = 19;
        public const int PLATFORM_GREEN = 20;
        public const int PLATFORM_PURPLE = 21;
        public const int PLATFORM_OLIVE = 22;
        public const int PLATFORM_KHAKI = 23;

        // ROW 3: BACKGROUND/AIR (24-31)
        public const int EMPTY = 24;
        public const int BG_DARK = 25;
        public const int BG_BLUE = 26;
        public const int BG_RED = 27;
        public const int BG_GREEN = 28;
        public const int BG_YELLOW = 29;
        public const int BG_PURPLE = 30;
        public const int BG_CYAN = 31;

        // ROW 4: LADDERS AND HAZARDS (32-39)
        public const int LADDER_YELLOW = 32;
        public const int LADDER_VARIANT = 33;
        public const int LADDER_GREEN = 34;
        public const int HAZARD_RED = 35;
        public const int WATER_BLUE = 36;
        public const int POISON_PURPLE = 37;
        public const int WARNING_YELLOW = 38;
        public const int ICE_CYAN = 39;

        // ROW 5: DECORATIVE TILES (40-47)
        public const int DECO_BRICK = 40;
        public const int DECO_LIGHT_BRICK = 41;
        public const int DECO_DARK_BRICK = 42;
        public const int DECO_STONE = 43;
        public const int DECO_COBBLESTONE = 44;
        public const int DECO_ROUGH_STONE = 45;
        public const int DECO_SMOOTH_STONE = 46;
        public const int DECO_MOSSY_STONE = 47;

        // ROW 6: COLORED DECORATIONS (48-55)
        public const int DECO_RED = 48;
        public const int DECO_GREEN = 49;
        public const int DECO_BLUE = 50;
        public const int DECO_YELLOW = 51;
        public const int DECO_MAGENTA = 52;
        public const int DECO_CYAN = 53;
        public const int DECO_ORANGE = 54;
        public const int DECO_LIME = 55;

        // ROW 7: SPECIAL/RESERVED (56-63)
        public const int SPECIAL_MAGENTA = 56;
        public const int SPECIAL_MID_GRAY = 57;
        public const int SPECIAL_LIGHT_GRAY = 58;
        public const int SPECIAL_VERY_LIGHT_GRAY = 59;
        public const int SPECIAL_VERY_DARK_GRAY = 60;
        public const int SPECIAL_WHITE = 61;
        public const int SPECIAL_OFF_WHITE = 62;
        public const int SPECIAL_SILVER = 63;

        // ====================================================================
        // TILE PROPERTIES
        // ====================================================================

        /// <summary>
        /// Get the tile type (solid/passthrough/etc.) for a given tile ID.
        /// </summary>
        public static TileType GetTileType(int tileId)
        {
            // ROW 0: Solid walls (0-7)
            if (tileId >= 0 && tileId <= 7)
                return TileType.Solid;

            // ROW 1: Solid floors (8-15)
            if (tileId >= 8 && tileId <= 15)
                return TileType.Solid;

            // ROW 2: Platforms (16-23)
            if (tileId >= 16 && tileId <= 23)
                return TileType.Platform;

            // ROW 3: Background/Empty (24-31)
            if (tileId >= 24 && tileId <= 31)
                return TileType.Empty;

            // ROW 4: Ladders and hazards (32-39)
            if (tileId == LADDER_YELLOW || tileId == LADDER_VARIANT || tileId == LADDER_GREEN)
                return TileType.Ladder;
            if (tileId == HAZARD_RED || tileId == POISON_PURPLE)
                return TileType.Deadly;
            if (tileId == WATER_BLUE || tileId == WARNING_YELLOW || tileId == ICE_CYAN)
                return TileType.Decoration; // For now

            // ROW 5-7: Decorations (40-63)
            if (tileId >= 40)
                return TileType.Decoration;

            // Default: treat as decoration
            return TileType.Decoration;
        }

        /// <summary>
        /// Check if a tile is solid (blocks player movement).
        /// </summary>
        public static bool IsSolid(int tileId)
        {
            var type = GetTileType(tileId);
            return type == TileType.Solid;
        }

        /// <summary>
        /// Check if a tile is a platform (player can stand on, but jump through).
        /// </summary>
        public static bool IsPlatform(int tileId)
        {
            return GetTileType(tileId) == TileType.Platform;
        }

        /// <summary>
        /// Check if a tile is deadly (kills player on contact).
        /// </summary>
        public static bool IsDeadly(int tileId)
        {
            return GetTileType(tileId) == TileType.Deadly;
        }

        /// <summary>
        /// Check if a tile is a ladder (player can climb).
        /// </summary>
        public static bool IsLadder(int tileId)
        {
            return GetTileType(tileId) == TileType.Ladder;
        }

        /// <summary>
        /// Check if a tile is empty/passthrough.
        /// </summary>
        public static bool IsEmpty(int tileId)
        {
            var type = GetTileType(tileId);
            return type == TileType.Empty || type == TileType.Decoration;
        }

        // ====================================================================
        // TILE SOURCE RECTANGLE CALCULATION
        // ====================================================================

        /// <summary>
        /// Get the source rectangle in the tileset for a given tile ID.
        /// </summary>
        public static Rectangle GetTileSourceRect(int tileId)
        {
            int row = tileId / TILES_PER_ROW;
            int col = tileId % TILES_PER_ROW;

            return new Rectangle(
                col * TILE_SIZE,
                row * TILE_SIZE,
                TILE_SIZE,
                TILE_SIZE
            );
        }
    }
}
