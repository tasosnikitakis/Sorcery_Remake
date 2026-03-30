// ============================================================================
// ROOM REGISTRY
// Sorcery+ Remake - Data-Driven Room Content
// ============================================================================
// Replaces the switch-statement spawning in Game1.cs with a registry of
// per-room content definitions. Adding a new room means adding one entry
// here — no other files need changing.
//
// Room LAYOUT (tiles, doors, backgrounds) is still registered via
// RoomManager.RegisterRoom() in Game1. This registry handles only the
// CONTENT that populates rooms: enemies, items, wizards, blocked doors.
// ============================================================================

using Microsoft.Xna.Framework;
using SorceryRemake.Core;
using System.Collections.Generic;

namespace SorceryRemake.Rooms
{
    /// <summary>
    /// Spawn definition for an enemy in a room.
    /// </summary>
    public struct EnemySpawn
    {
        public string Id;
        public EnemyType Type;
        public Vector2 Position;

        public EnemySpawn(string id, EnemyType type, Vector2 position)
        {
            Id = id;
            Type = type;
            Position = position;
        }
    }

    /// <summary>
    /// Spawn definition for an item in a room.
    /// </summary>
    public struct ItemSpawn
    {
        public string Id;
        public ItemType Type;
        public Vector2 Position;

        public ItemSpawn(string id, ItemType type, Vector2 position)
        {
            Id = id;
            Type = type;
            Position = position;
        }
    }

    /// <summary>
    /// Spawn definition for a captive wizard in a room.
    /// </summary>
    public struct WizardSpawn
    {
        public string Id;
        public Vector2 Position;

        public WizardSpawn(string id, Vector2 position)
        {
            Id = id;
            Position = position;
        }
    }

    /// <summary>
    /// Spawn definition for a blocked door in a room.
    /// </summary>
    public struct BlockedDoorSpawn
    {
        public string Id;
        public Vector2 Position;
        public ItemType RequiredItem;

        public BlockedDoorSpawn(string id, Vector2 position, ItemType requiredItem)
        {
            Id = id;
            Position = position;
            RequiredItem = requiredItem;
        }
    }

    /// <summary>
    /// All spawnable content for a single room.
    /// </summary>
    public class RoomContent
    {
        public List<EnemySpawn> Enemies { get; } = new List<EnemySpawn>();
        public List<ItemSpawn> Items { get; } = new List<ItemSpawn>();
        public List<WizardSpawn> Wizards { get; } = new List<WizardSpawn>();
        public List<BlockedDoorSpawn> BlockedDoors { get; } = new List<BlockedDoorSpawn>();
    }

    /// <summary>
    /// Central registry of room content. Adding rooms 6-75 means adding entries here.
    /// </summary>
    public static class RoomRegistry
    {
        private static readonly Dictionary<string, RoomContent> _rooms = new();

        /// <summary>
        /// Get content for a room (returns empty content if not registered).
        /// </summary>
        public static RoomContent GetContent(string roomId)
        {
            return _rooms.TryGetValue(roomId, out var content) ? content : new RoomContent();
        }

        /// <summary>
        /// Register all room content. Called once at startup.
        /// </summary>
        public static void Initialize()
        {
            _rooms.Clear();

            // ================================================================
            // ROOM 1 — Test room with platform
            // ================================================================
            var room1 = new RoomContent();
            // Items on platform and floor
            room1.Items.Add(new ItemSpawn("room_1_sword", ItemType.Sword, new Vector2(140f, 72f)));
            room1.Items.Add(new ItemSpawn("room_1_ballchain", ItemType.BallAndChain, new Vector2(60f, 112f)));
            room1.Items.Add(new ItemSpawn("room_1_star", ItemType.ShootingStar, new Vector2(120f, 112f)));
            room1.Items.Add(new ItemSpawn("room_1_lyre", ItemType.Lyre, new Vector2(240f, 112f)));
            // Wizard on top of platform
            room1.Wizards.Add(new WizardSpawn("room_1_wizard", new Vector2(160f, 72f)));
            _rooms["room_1"] = room1;

            // ================================================================
            // ROOM 2 — Tunnel with blocked door
            // ================================================================
            var room2 = new RoomContent();
            // Axe on the floor
            room2.Items.Add(new ItemSpawn("room_2_axe", ItemType.Axe, new Vector2(160f, 112f)));
            // Wizard at right end of tunnel
            room2.Wizards.Add(new WizardSpawn("room_2_wizard", new Vector2(296f, 72f)));
            // Iron door blocking tunnel entrance
            room2.BlockedDoors.Add(new BlockedDoorSpawn("room_2_iron_door", new Vector2(216f, 72f), ItemType.Lyre));
            _rooms["room_2"] = room2;

            // ================================================================
            // STONEHENGE / WASTELANDS / TUNNELMOUTH
            // (No content yet — background-only rooms)
            // ================================================================
        }
    }
}
