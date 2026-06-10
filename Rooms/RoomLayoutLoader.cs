// ============================================================================
// ROOM LAYOUT LOADER
// Sorcery+ Remake - Per-room door JSON read/write
// ============================================================================
// Sibling of RoomContentLoader. Where content_<roomId>.json holds entities
// (items, enemies, wizards, blocked doors), layout_<roomId>.json holds the
// room's connectivity: its DOORS, with positions, IDs, opening direction,
// and target (room, door).
//
// Schema:
// {
//   "roomId": "chateau_1",
//   "doors": [
//     { "id": "chateau1_door_topleft",  "x": 0,   "y": 0,
//       "type": "RightOpening",
//       "targetRoom": "chateau_0", "targetDoor": "chateau0_door_topright" },
//     { "id": "chateau1_door_topright", "x": 296, "y": 0,
//       "type": "LeftOpening",
//       "targetRoom": "chateau_2", "targetDoor": "chateau2_door_topleft" }
//   ]
// }
//
// SorceryForge writes this file when the user authors doors in the editor.
// Game1 reads this file at room-load time to construct DoorComponents.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SorceryRemake.Rooms
{
    // ------------------------------------------------------------------------
    // JSON DTOs
    // ------------------------------------------------------------------------

    public class RoomLayoutJson
    {
        public string roomId { get; set; } = "";
        public List<DoorEntryJson> doors { get; set; } = new();
    }

    public class DoorEntryJson
    {
        public string id { get; set; } = "";
        public float x { get; set; }
        public float y { get; set; }
        public string type { get; set; } = "LeftOpening";   // "LeftOpening" | "RightOpening"
        public string targetRoom { get; set; } = "";
        public string targetDoor { get; set; } = "";
    }

    // ------------------------------------------------------------------------
    // LOADER / SAVER
    // ------------------------------------------------------------------------

    public static class RoomLayoutLoader
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static string GetPath(string roomId, string? dir = null) =>
            Path.Combine(dir ?? RoomContentLoader.DefaultDir, $"layout_{roomId}.json");

        /// <summary>
        /// Load the layout (doors) for a room. Returns null if the file
        /// doesn't exist — the caller treats that as "no doors yet".
        /// Malformed JSON throws so authoring errors surface immediately.
        /// </summary>
        public static RoomLayoutJson? TryLoad(string roomId, string? dir = null)
        {
            string path = GetPath(roomId, dir);
            if (!File.Exists(path)) return null;

            string text = File.ReadAllText(path);
            return JsonSerializer.Deserialize<RoomLayoutJson>(text, Options);
        }

        public static void Save(RoomLayoutJson layout, string? dir = null)
        {
            string path = GetPath(layout.roomId, dir);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string text = JsonSerializer.Serialize(layout, Options);
            File.WriteAllText(path, text);
        }
    }
}
