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
//
// A room with no doors has NO file — see the skip rule on Save below.
// Absent file and empty "doors" array mean the same thing to TryLoad.
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

        /// <summary>
        /// Write layout_&lt;roomId&gt;.json. Returns true if the file was
        /// written, false if the write was deliberately skipped (see below) —
        /// callers use the result to keep their "saved X" feedback honest.
        /// </summary>
        public static bool Save(RoomLayoutJson layout, string? dir = null)
        {
            string path = GetPath(layout.roomId, dir);

            // EMPTY, precisely: no doors. RoomLayoutJson carries exactly two
            // members today — roomId and doors (see the schema block at the
            // top of this file) — and roomId deliberately does NOT count:
            // the writer always fills it in, so its presence is never
            // evidence that a human authored anything. Be conservative if
            // this DTO ever grows a field holding real room data (spawn
            // points, camera hints, ...): fold it in here, or a room whose
            // only content is that new field would never get a file.
            bool isEmpty = layout.doors.Count == 0;

            // The rule is "don't CREATE an empty file", never "don't WRITE
            // an empty file", and the asymmetry matters in both directions:
            //
            //   file absent + empty  -> skip. Otherwise merely opening a
            //     doorless room in SorceryForge and hitting Ctrl+S adds a
            //     no-op file to the repo, and an untouched save stops being a
            //     no-op (see tools/RoundTrip).
            //   file present + empty -> WRITE. The user just deleted every
            //     door in the room; refusing the write would silently discard
            //     that deletion and leave the old doors live in the game,
            //     which is far worse than a redundant file. Emptiness alone
            //     must never suppress a write.
            if (isEmpty && !File.Exists(path)) return false;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string text = JsonSerializer.Serialize(layout, Options);
            File.WriteAllText(path, text);
            return true;
        }
    }
}
