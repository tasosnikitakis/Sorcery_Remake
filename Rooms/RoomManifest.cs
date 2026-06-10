// ============================================================================
// ROOM MANIFEST
// Sorcery+ Remake - Static catalogue of background-image rooms
// ============================================================================
// This is the single source of truth for which rooms exist, what their
// display names are, and which background / collision asset they use. The
// game registers rooms by iterating this list; the editor reads it to
// populate its room picker.
//
// Adding a new room is one entry here. Door connections live separately in
// assets/data/layout_<roomId>.json (see RoomLayoutLoader) — that file is
// authored in SorceryForge and edited live, while the manifest changes
// rarely (only when a wholly new background/room ID enters the world).
//
// Test rooms (room_1, room_2 — programmatic tilemaps with no background)
// are NOT in this manifest; they remain registered directly in Game1 via
// RegisterTestRooms.
// ============================================================================

using System.Collections.Generic;

namespace SorceryRemake.Rooms
{
    public class RoomManifest
    {
        public string RoomId { get; }
        public string DisplayName { get; }
        public string BackgroundAsset { get; }   // Content asset name, e.g. "RoomBG_Chateau0"
        public string CollisionFile { get; }     // Filename in assets/data, e.g. "collision_chateau0.json"

        public RoomManifest(string roomId, string displayName, string backgroundAsset, string collisionFile)
        {
            RoomId = roomId;
            DisplayName = displayName;
            BackgroundAsset = backgroundAsset;
            CollisionFile = collisionFile;
        }

        /// <summary>
        /// All shipped background-image rooms. The order here is the editor's
        /// cycle order (Prev/Next buttons walk this list).
        /// </summary>
        public static readonly List<RoomManifest> All = new()
        {
            // Existing chateau set
            new("chateau_0",       "Chateau 0",       "RoomBG_Chateau0",       "collision_chateau0.json"),
            new("chateau_1",       "Chateau 1",       "RoomBG_Chateau1",       "collision_chateau1.json"),
            new("chateau_2",       "Chateau 2",       "RoomBG_Chateau2",       "collision_chateau2.json"),

            // Stonehenge / wastelands chain
            new("stonehenge",      "Stonehenge",      "RoomBG_Stonehenge",     "collision_stonehenge.json"),
            new("wastelands",      "Wastelands",      "RoomBG_Wastelands",     "collision_wastelands.json"),
            new("tunnelmouth",     "Tunnel Mouth",    "RoomBG_TunnelMouth",    "collision_tunnelmouth.json"),

            // New screenshot rooms (door connections authored in SorceryForge)
            new("near_chateau",    "Near Chateau",    "RoomBG_NearChateau",    "collision_near_chateau.json"),
            new("inside_chateau",  "Inside Chateau",  "RoomBG_InsideChateau",  "collision_inside_chateau.json"),
            new("outside_chateau", "Outside Chateau", "RoomBG_OutsideChateau", "collision_outside_chateau.json"),
        };

        public static RoomManifest? Find(string roomId)
        {
            foreach (var m in All) if (m.RoomId == roomId) return m;
            return null;
        }
    }
}
