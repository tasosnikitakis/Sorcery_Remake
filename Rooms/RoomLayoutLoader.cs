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
//   ],
//   "playerSpawn": { "x": 160, "y": 80 }        // OPTIONAL — see below
// }
//
// "playerSpawn" is where the player starts when the game begins (or restarts)
// in this room; it is the top-left of the 24x24 player, in room space. It is
// OPTIONAL and WRITTEN ONLY WHEN SET: a room that has never had a spawn
// authored serialises byte-for-byte as it always did, which is what keeps
// tools/RoundTrip green across this schema change. Absent means "use
// DefaultPlayerSpawn" — resolve it through GetPlayerSpawn rather than
// re-hardcoding (160, 80) at a call site.
//
// Door transitions do NOT consult playerSpawn: arriving through a door
// positions the player at that door (DoorComponent.GetArrivalPosition).
//
// SorceryForge writes this file when the user authors doors or a spawn in
// the editor. Game1 reads it at room-load time to construct DoorComponents,
// and at start / restart for the spawn.
//
// A room with no doors and no spawn has NO file — see the skip rule on Save
// below. Absent file and empty "doors" array mean the same thing to TryLoad.
// ============================================================================

using Microsoft.Xna.Framework;
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

        // Optional; null means "this room never had a spawn authored".
        // Declared LAST so the key, when present, appends after "doors" and
        // the existing key order of every already-written file is untouched.
        // Serialisation drops it while null (JsonIgnoreCondition.WhenWritingNull
        // in Options below), which is what makes the field free for the 8
        // layout files already in the repo.
        public PlayerSpawnJson? playerSpawn { get; set; }
    }

    public class PlayerSpawnJson
    {
        public float x { get; set; }
        public float y { get; set; }
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
        /// Where the player starts in a room that has no authored playerSpawn.
        /// </summary>
        // THE one definition of the fallback. It used to be written out at
        // three call sites (Game1's start, Game1.RestartGame, and the editor's
        // reachability flood-fill origin), which is how the editor could end up
        // validating from a position the game no longer used. Read it from
        // here; never retype the numbers.
        public static readonly Vector2 DefaultPlayerSpawn = new(160f, 80f);

        /// <summary>
        /// The spawn position for a room: its authored playerSpawn if the
        /// layout file has one, otherwise <see cref="DefaultPlayerSpawn"/>.
        /// </summary>
        public static Vector2 GetPlayerSpawn(string roomId, string? dir = null)
        {
            var spawn = TryLoad(roomId, dir)?.playerSpawn;
            return spawn == null ? DefaultPlayerSpawn : new Vector2(spawn.x, spawn.y);
        }

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

            // EMPTY, precisely: no doors AND no player spawn. RoomLayoutJson
            // carries three members today — roomId, doors and playerSpawn (see
            // the schema block at the top of this file) — and roomId
            // deliberately does NOT count: the writer always fills it in, so
            // its presence is never evidence that a human authored anything.
            //
            // playerSpawn is folded in exactly as the previous version of this
            // comment demanded of any future field holding real room data. Miss
            // it and a doorless room whose ONLY authored content is a spawn
            // gets no file — the spawn is silently dropped on save. The same
            // rule applies to whatever field comes next.
            bool isEmpty = layout.doors.Count == 0 && layout.playerSpawn == null;

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
