// ============================================================================
// ROOM MANIFEST
// Sorcery+ Remake - The catalogue of which rooms exist
// ============================================================================
// This is the single source of truth for which rooms exist, what their
// display names are, and which background / collision asset they use. The
// game registers rooms by iterating this list; the editor reads it to
// populate its room picker.
//
// The list is DATA, not code: it is read from assets/data/rooms.json (see
// LoadAll below). Adding a room is one entry in that file — no rebuild of
// either binary, no C# edit. Array order in the JSON is room order; the
// editor's Prev/Next buttons walk it in exactly that sequence.
//
// Door connections live separately in assets/data/layout_<roomId>.json (see
// RoomLayoutLoader), entities in content_<roomId>.json (RoomContentLoader),
// and solid tiles in the collision file each entry names. rooms.json is the
// registry and nothing more.
//
// Test rooms (room_1, room_2 — programmatic tilemaps with no background)
// are NOT in the registry; they remain registered directly in Game1 via
// RegisterTestRooms, and are listed in TestRoomIds below so validators can
// tell "deliberately not a manifest room" from "typo'd room id".
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SorceryRemake.Rooms
{
    // ------------------------------------------------------------------------
    // JSON DTOs — lowercase names match the on-disk schema. Don't rename.
    // ------------------------------------------------------------------------

    public class RoomsJson
    {
        public List<RoomEntryJson> rooms { get; set; } = new();
    }

    public class RoomEntryJson
    {
        public string id { get; set; } = "";
        public string displayName { get; set; } = "";
        public string backgroundAsset { get; set; } = "";
        public string collisionFile { get; set; } = "";
    }

    // ------------------------------------------------------------------------
    // MANIFEST
    // ------------------------------------------------------------------------

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

        // --------------------------------------------------------------------
        // REGISTRY LOADING
        // --------------------------------------------------------------------
        // Parsed once on first access and cached for the process lifetime.
        // Lazy<T> (rather than a static constructor) is deliberate: it rethrows
        // the ORIGINAL exception on every access, so a bad rooms.json surfaces
        // as our own message instead of a TypeInitializationException wrapper
        // that buries it one level down.
        //
        // Path resolution reuses RoomContentLoader.DefaultDir — the same
        // walk-up-to-the-repo-root logic every other assets/data loader uses,
        // so the game, SorceryForge and tools/RoundTrip all read the one file
        // in the source tree during development, and the copy next to the
        // executable in a published build.
        // --------------------------------------------------------------------

        /// <summary>Absolute path of the room registry file.</summary>
        public static string RoomsJsonPath => Path.Combine(RoomContentLoader.DefaultDir, "rooms.json");

        /// <summary>
        /// Rooms that exist in the game but are registered programmatically
        /// (Game1.RegisterTestRooms) rather than via this manifest. Validators
        /// treat door targets in this set as existing-but-unverifiable.
        /// </summary>
        // Declared ABOVE _all deliberately: LoadAll rejects registry entries
        // that reuse one of these ids, so the set has to be initialised before
        // any code path that can run LoadAll. Static field initialisers run in
        // textual order, and while today's Lazy<T> only ever calls LoadAll long
        // after class init, keeping the declaration first means that stays true
        // regardless of how the registry is triggered later.
        public static readonly HashSet<string> TestRoomIds = new() { "room_1", "room_2" };

        /// <summary>
        /// All shipped background-image rooms, in rooms.json array order.
        /// That order is the editor's cycle order (Prev/Next walk this list).
        /// </summary>
        public static List<RoomManifest> All => _all.Value;

        private static readonly Lazy<List<RoomManifest>> _all = new(LoadAll);

        private static readonly JsonSerializerOptions Options = new()
        {
            // rooms.json carries a hand-written header comment explaining the
            // ordering rule. Trailing commas are allowed for the same reason:
            // this file is edited by humans as often as by tools.
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>
        /// Read and validate assets/data/rooms.json. Every failure here is
        /// FATAL and loud. A silently-empty room list would "work" — the game
        /// would boot into a world with no rooms and the editor would show an
        /// empty picker — which is far harder to diagnose than a crash naming
        /// the file and the problem.
        /// </summary>
        private static List<RoomManifest> LoadAll()
        {
            string path = RoomsJsonPath;

            if (!File.Exists(path))
                throw new InvalidOperationException(
                    $"Room registry not found: '{path}'. assets/data/rooms.json defines which " +
                    "rooms exist; the game and SorceryForge cannot start without it.");

            RoomsJson? data;
            try
            {
                data = JsonSerializer.Deserialize<RoomsJson>(File.ReadAllText(path), Options);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Room registry '{path}' is not valid JSON: {ex.Message}", ex);
            }

            if (data == null || data.rooms == null || data.rooms.Count == 0)
                throw new InvalidOperationException(
                    $"Room registry '{path}' lists no rooms. Expected {{ \"rooms\": [ ... ] }} " +
                    "with at least one entry.");

            var list = new List<RoomManifest>(data.rooms.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < data.rooms.Count; i++)
            {
                var e = data.rooms[i];

                if (string.IsNullOrWhiteSpace(e.id))
                    throw new InvalidOperationException(
                        $"Room registry '{path}': entry {i} has no \"id\".");

                // Room ids are persistence keys — WorldState remembers entity
                // ids built from them — so a duplicate is never harmless.
                if (!seen.Add(e.id))
                    throw new InvalidOperationException(
                        $"Room registry '{path}': duplicate room id '{e.id}' (entry {i}). " +
                        "Room ids are persistence keys and must be unique.");

                // The test rooms are built in code (Game1.RegisterTestRooms)
                // and registered under these ids before the manifest rooms
                // are. A registry entry reusing one would be shadowed — the
                // programmatic room wins the RoomManager slot — so the entry's
                // background, collision and doors would silently do nothing.
                // Refuse rather than ship a room that looks registered and
                // isn't.
                if (TestRoomIds.Contains(e.id))
                    throw new InvalidOperationException(
                        $"Room registry '{path}': room id '{e.id}' (entry {i}) is reserved for the " +
                        "programmatic test rooms registered by Game1.RegisterTestRooms. A registry " +
                        "entry with this id would be shadowed by the test room and never load. " +
                        "Rename the room.");

                if (string.IsNullOrWhiteSpace(e.backgroundAsset))
                    throw new InvalidOperationException(
                        $"Room registry '{path}': room '{e.id}' has no \"backgroundAsset\". " +
                        "Name the Content pipeline asset, e.g. \"RoomBG_Chateau0\".");

                // displayName is cosmetic; fall back to the id rather than
                // failing, so a half-authored entry still shows up somewhere.
                string displayName = string.IsNullOrWhiteSpace(e.displayName) ? e.id : e.displayName;

                // collisionFile is genuinely optional — a room whose geometry
                // has not been painted yet has none, and the game skips it.
                list.Add(new RoomManifest(e.id, displayName, e.backgroundAsset, e.collisionFile ?? ""));
            }

            return list;
        }

        public static RoomManifest? Find(string roomId)
        {
            foreach (var m in All) if (m.RoomId == roomId) return m;
            return null;
        }
    }
}
