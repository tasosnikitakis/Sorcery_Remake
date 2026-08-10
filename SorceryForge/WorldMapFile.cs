// ============================================================================
// WORLD MAP FILE
// SorceryForge — assets/data/worldmap.json, where a dragged room stays put
// ============================================================================
// The board's auto-placement is a good default and nothing more. The world has
// a shape in the owner's head — chateau up here, the wastelands chain running
// off to the side — and the map earns its keep only if that shape can be
// arranged once and still be there tomorrow. This is the file that remembers.
//
// EDITOR-ONLY, AND A NEW FILE. Nothing in the game reads it, no existing
// schema changes, and it names no entity: it is a set of positions keyed by
// room id, which is the least it can be and still do the job. It is also
// invisible to tools/RoundTrip by construction — that harness seeds and sweeps
// content_* and layout_* only — and tools/MapCheck asserts that rather than
// leaving it to be assumed.
//
// BORN-EMPTY DISCIPLINE, the 3b rule, for the 3b reason. The file is created
// only once the user has actually dragged something. Auto-placed positions are
// never written, because writing them would freeze today's BFS output into the
// repository: add a door next week and every room would stay where the old
// layout put it, with no way to tell which positions were decisions and which
// were defaults. What is in the file is exactly the set of deliberate acts.
//
//   - a room in the file uses its stored position
//   - a room absent from it is auto-placed, every time
//   - delete the file and the whole board goes back to auto-placement
//
// UNKNOWN IDS ARE DROPPED, NOT KEPT. A room renamed or removed leaves a
// position behind that can never apply to anything; it is ignored on load and
// gone on the next save. The alternative — preserving unknown keys — sounds
// tidier and is worse: it accumulates silt nobody can attribute, in a file
// whose whole content is meant to be deliberate acts.
//
// FORMAT — house style, so a diff reads:
//   {
//     "rooms": {
//       "chateau_0":  { "x": 0,   "y": 0   },
//       "near_chateau": { "x": 448, "y": 216 }
//     }
//   }
// One room per line, in REGISTRY order (not drag order, not hash order), keys
// column-aligned. Load -> save with no change is byte-identical; MapCheck
// asserts it, the same way RoundTrip asserts it for rooms.json.
// ============================================================================

using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace SorceryForge
{
    // ------------------------------------------------------------------------
    // JSON DTOs — lowercase names match the on-disk schema. Don't rename.
    // ------------------------------------------------------------------------

    public class WorldMapJson
    {
        public Dictionary<string, MapPositionJson> rooms { get; set; } = new();
    }

    public class MapPositionJson
    {
        public float x { get; set; }
        public float y { get; set; }
    }

    public static class WorldMapFile
    {
        public const string FileName = "worldmap.json";

        public static string GetPath(string? dir = null) =>
            Path.Combine(dir ?? EditorPaths.RepoAssetsDataDir, FileName);

        private static readonly JsonSerializerOptions Options = new()
        {
            // Same reader settings as the room registry: the file is
            // hand-editable, and a comment or a trailing comma in it should not
            // cost the user their arrangement.
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        // ====================================================================
        // LOAD
        // ====================================================================

        /// <summary>
        /// Stored positions, keyed by room id. Empty when there is no file —
        /// which is the normal state until someone drags something, and means
        /// exactly "auto-place everything".
        /// </summary>
        // A malformed file is NOT fatal here, unlike rooms.json. Losing an
        // arrangement is annoying; refusing to start the editor over a cosmetic
        // file would be absurd. The board falls back to auto-placement and the
        // caller reports it.
        public static Dictionary<string, Vector2> Load(string? dir, out string? error)
        {
            error = null;
            var result = new Dictionary<string, Vector2>(StringComparer.Ordinal);

            string path = GetPath(dir);
            if (!File.Exists(path)) return result;

            try
            {
                var data = JsonSerializer.Deserialize<WorldMapJson>(File.ReadAllText(path), Options);
                if (data?.rooms == null) return result;
                foreach (var pair in data.rooms)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null) continue;
                    result[pair.Key] = new Vector2(pair.Value.x, pair.Value.y);
                }
            }
            catch (Exception ex)
            {
                error = $"{FileName} is unreadable ({ex.Message}) — the board is auto-placed.";
                result.Clear();
            }
            return result;
        }

        // ====================================================================
        // SAVE
        // ====================================================================

        /// <summary>
        /// Write the positions of the rooms that have one, in the order given.
        /// Returns true if a file was written, false if the write was
        /// deliberately skipped.
        /// </summary>
        // Same asymmetry the room loaders live by, and it is easy to get
        // backwards: "don't CREATE an empty file" is not "don't WRITE one".
        // Nothing arranged and no file yet -> write nothing, so an untouched
        // map never adds a file to the repo. Nothing arranged but a file
        // EXISTS -> write it empty anyway, because that is a user who dragged
        // rooms back to auto-placement and their reset has to persist.
        //
        // `rooms` is walked in the order given — the caller passes registry
        // order — so the file's line order is stable across saves and a diff
        // shows only what moved.
        public static bool Save(IReadOnlyList<MapRoom> rooms, string? dir = null)
        {
            string path = GetPath(dir);

            var stored = new List<(string id, Vector2 pos)>();
            foreach (var room in rooms)
                if (room.Arranged) stored.Add((room.RoomId, room.Position));

            if (stored.Count == 0 && !File.Exists(path)) return false;

            // Column widths, matching RoomManifest.Save's approach: the quoted
            // key plus its colon, padded to the longest, so the x/y columns
            // line up and a moved room is a one-line diff.
            int keyWidth = 0;
            foreach (var (id, _) in stored)
                keyWidth = Math.Max(keyWidth, Quote(id).Length + 1);

            var sb = new StringBuilder();
            string nl = Environment.NewLine;   // CRLF, matching every other JSON writer in the tree
            sb.Append('{').Append(nl);
            sb.Append("  \"rooms\": {").Append(nl);

            for (int i = 0; i < stored.Count; i++)
            {
                var (id, pos) = stored[i];
                sb.Append("    ").Append((Quote(id) + ":").PadRight(keyWidth + 1))
                  .Append(" { \"x\": ").Append(Number(pos.X))
                  .Append(", \"y\": ").Append(Number(pos.Y))
                  .Append(" }").Append(i < stored.Count - 1 ? "," : "").Append(nl);
            }

            sb.Append("  }").Append(nl);
            sb.Append('}').Append(nl);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, sb.ToString());
            return true;
        }

        /// <summary>Delete the file, if there is one. Returns true if it went.</summary>
        public static bool Delete(string? dir = null)
        {
            string path = GetPath(dir);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }

        private static string Quote(string value) => JsonSerializer.Serialize(value ?? "");

        /// <summary>
        /// A position as the shortest exact decimal — "448" not "448.0".
        /// </summary>
        // Positions are whole map units in practice (dragging quantises to
        // them), and round-tripping has to be byte-stable, so this uses the
        // round-trip format and the invariant culture rather than anything
        // locale-dependent. A machine set to a comma decimal separator would
        // otherwise write JSON no parser accepts.
        private static string Number(float value) =>
            value == MathF.Round(value)
                ? ((long)MathF.Round(value)).ToString(CultureInfo.InvariantCulture)
                : value.ToString("R", CultureInfo.InvariantCulture);
    }
}
