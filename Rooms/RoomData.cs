// ============================================================================
// ROOM DATA
// Sorcery+ Remake - Room Definition Structure
// ============================================================================
// Defines the data structure for a room.
// Rooms contain tile layout, spawn points, exits, and metadata.
//
// ROOM DIMENSIONS:
// - Standard room: 40x18 tiles (320x144 pixels)
// - Matches Python prototype dimensions
// ============================================================================

using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace SorceryRemake.Rooms
{
    /// <summary>
    /// Data for a single room in the game.
    /// </summary>
    public class RoomData
    {
        // ====================================================================
        // ROOM METADATA
        // ====================================================================

        /// <summary>
        /// Unique room identifier (e.g., "room_01", "test_room").
        /// </summary>
        public string RoomId { get; set; } = "";

        /// <summary>
        /// Display name of the room (optional).
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Room description (optional, for debugging/editor).
        /// </summary>
        public string Description { get; set; } = "";

        // ====================================================================
        // ROOM DIMENSIONS
        // ====================================================================

        /// <summary>
        /// Width of the room in tiles.
        /// Standard: 40 tiles (320 pixels).
        /// </summary>
        public int Width { get; set; } = 40;

        /// <summary>
        /// Height of the room in tiles.
        /// Standard: 18 tiles (144 pixels).
        /// </summary>
        public int Height { get; set; } = 18;

        // ====================================================================
        // TILE LAYOUT
        // ====================================================================

        /// <summary>
        /// 2D array of tile IDs [row, col] or [y, x].
        /// Each value corresponds to a tile ID from TileConfig.
        /// </summary>
        public int[,]? Tiles { get; set; }

        // ====================================================================
        // SPAWN POINTS
        // ====================================================================

        /// <summary>
        /// Player spawn position in world pixels.
        /// </summary>
        public Vector2 PlayerSpawn { get; set; } = new Vector2(160, 120);

        /// <summary>
        /// Enemy spawn points (positions where enemies appear).
        /// </summary>
        public List<Vector2> EnemySpawns { get; set; } = new List<Vector2>();

        /// <summary>
        /// Item spawn points (positions for pickups).
        /// </summary>
        public List<ItemSpawnData> ItemSpawns { get; set; } = new List<ItemSpawnData>();

        // ====================================================================
        // ROOM EXITS
        /// ====================================================================

        /// <summary>
        /// Exit points that lead to other rooms.
        /// </summary>
        public List<RoomExit> Exits { get; set; } = new List<RoomExit>();

        // ====================================================================
        // BACKGROUND
        // ====================================================================

        /// <summary>
        /// Background color for the room (optional).
        /// Default: black (0, 0, 0).
        /// </summary>
        public Color BackgroundColor { get; set; } = Color.Black;

        // ====================================================================
        // BACKGROUND IMAGE
        // ====================================================================

        /// <summary>
        /// Name of the background texture asset (loaded via Content pipeline).
        /// If set, this texture is drawn as the room background before any tiles.
        /// Example: "RoomBG_Stonehenge" (without file extension).
        /// </summary>
        public string? BackgroundTextureName { get; set; }

        /// <summary>
        /// Collision grid for background-image rooms (40x18).
        /// Each cell: 0=empty/air, 1=solid (blocks movement).
        /// Used when the room relies on a background image rather than tile-based rendering.
        /// </summary>
        public byte[,]? CollisionGrid { get; set; }
    }

    // ========================================================================
    // SUPPORTING DATA STRUCTURES
    // ========================================================================

    /// <summary>
    /// Defines an item spawn point in a room.
    /// </summary>
    public class ItemSpawnData
    {
        /// <summary>
        /// Position in world pixels.
        /// </summary>
        public Vector2 Position { get; set; }

        /// <summary>
        /// Item type ID (will be defined in Phase 3A).
        /// </summary>
        public string ItemType { get; set; } = "";

        /// <summary>
        /// Should the item respawn after being collected?
        /// </summary>
        public bool Respawns { get; set; } = false;
    }

    /// <summary>
    /// Defines an exit point that leads to another room.
    /// </summary>
    public class RoomExit
    {
        /// <summary>
        /// Trigger area for the exit (in world pixels).
        /// Player must overlap this rectangle to trigger the exit.
        /// </summary>
        public Rectangle TriggerArea { get; set; }

        /// <summary>
        /// ID of the room to transition to.
        /// </summary>
        public string TargetRoomId { get; set; } = "";

        /// <summary>
        /// Spawn position in the target room.
        /// </summary>
        public Vector2 TargetSpawnPosition { get; set; }

        /// <summary>
        /// Exit type (door, edge, teleporter, etc.).
        /// </summary>
        public ExitType Type { get; set; } = ExitType.Edge;
    }

    /// <summary>
    /// Type of room exit.
    /// </summary>
    public enum ExitType
    {
        Edge,        // Walk off edge of screen
        Door,        // Door that requires key
        Teleporter,  // Instant teleport
        Ladder,      // Climb to next room
        Fall         // Fall through floor
    }
}
