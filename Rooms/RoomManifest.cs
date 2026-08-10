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
using System.Text;
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

        // Not readonly: Reload() below replaces the Lazy so the editor can
        // re-read the file after appending a room.
        private static Lazy<List<RoomManifest>> _all = new(LoadAll);

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

        // --------------------------------------------------------------------
        // REGISTRY RELOADING (editor only)
        // --------------------------------------------------------------------

        /// <summary>
        /// Throw away the cached registry so the next <see cref="All"/> re-reads
        /// rooms.json. Call after writing the file — SorceryForge's New Room
        /// flow does, and nothing else should.
        /// </summary>
        // The GAME never calls this. Its rooms are registered once in
        // Game1.RegisterRoomsFromManifest (loader lambdas captured per entry)
        // and its backgrounds loaded once in LoadRoomBackgrounds, so a
        // mid-session reload would produce a registry the RoomManager and the
        // texture dictionary know nothing about — worse than a restart.
        //
        // Not thread-safe: the assignment races any concurrent All reader. The
        // editor is single-threaded and calls this between frames, which is the
        // only supported use.
        public static void Reload() => _all = new Lazy<List<RoomManifest>>(LoadAll);

        // --------------------------------------------------------------------
        // REGISTRY WRITING
        // --------------------------------------------------------------------
        // rooms.json is hand-edited as often as it is written, so the writer's
        // job is not just "valid JSON" — it is "the same file a human would
        // have typed". Two consequences:
        //
        //   1. The header comment survives. JsonSerializer.Serialize would
        //      silently drop it (comments are not part of the object model),
        //      taking the ordering rule with it — the one thing a reader of
        //      this file most needs to know. It is re-emitted textually from
        //      RoomsJsonHeader below.
        //   2. Entries stay one-per-line and column-aligned, exactly as the
        //      file was hand-authored, so a New Room diff is one added line.
        //
        // Load -> Save with no changes is byte-identical; tools/RoundTrip's
        // self-test asserts it on every run. If you edit RoomsJsonHeader, the
        // file and the constant disagree until someone saves the registry
        // once — that assertion is what tells you.
        // --------------------------------------------------------------------

        /// <summary>
        /// The header block re-emitted verbatim at the top of every write.
        /// Must stay identical to the header in assets/data/rooms.json.
        /// </summary>
        private static readonly string[] RoomsJsonHeader =
        {
            "// ============================================================================",
            "// ROOM REGISTRY — which rooms exist",
            "// ============================================================================",
            "// Read by Rooms/RoomManifest.cs (shared source: the game, SorceryForge and",
            "// tools/RoundTrip all load this one file). Registry facts ONLY — a room's",
            "// doors live in layout_<id>.json, its entities in content_<id>.json, and its",
            "// solid tiles in the collision file named below.",
            "//",
            "// ARRAY ORDER IS ROOM ORDER. It is the editor's Prev/Next cycle order and the",
            "// order every \"for each room\" loop walks. This array was seeded verbatim from",
            "// the compiled list that used to live in RoomManifest.All, so the order here",
            "// IS that historical order — chateau set, then the stonehenge/wastelands",
            "// chain, then the screenshot-derived rooms. Reordering entries reorders the",
            "// editor's room cycle; appending is always safe.",
            "//",
            "// Fields:",
            "//   id              persistence key. Entity IDs are built from it and",
            "//                   WorldState remembers them, so never rename casually.",
            "//                   room_1 / room_2 are reserved (see below) and rejected.",
            "//   displayName     shown in the editor's room picker and the game's HUD.",
            "//   backgroundAsset Content pipeline asset name (no extension). Must have a",
            "//                   matching #begin block in Content/Content.mgcb.",
            "//   collisionFile   file name inside assets/data. May be \"\" for a room with",
            "//                   no painted geometry yet.",
            "//",
            "// Test rooms room_1 / room_2 are deliberately absent — they are built",
            "// programmatically in Game1.RegisterTestRooms and listed in",
            "// RoomManifest.TestRoomIds.",
            "//",
            "// THIS FILE IS ALSO MACHINE-WRITTEN. SorceryForge's New Room button appends",
            "// an entry through RoomManifest.Save, which re-emits this header verbatim",
            "// from the RoomsJsonHeader constant and re-aligns the columns below. Edit it",
            "// by hand freely — but a hand-edited header is overwritten by the next New",
            "// Room, so header changes belong in RoomManifest.cs.",
            "//",
            "// Comments are legal here: the loader reads with JsonCommentHandling.Skip.",
            "// ============================================================================",
        };

        /// <summary>
        /// Write the registry back to rooms.json (default: the live path).
        /// Array order is written exactly as given — it is room order.
        /// </summary>
        public static void Save(IReadOnlyList<RoomManifest> rooms, string? path = null)
        {
            path ??= RoomsJsonPath;

            // Column widths. A field's token is its quoted value plus the
            // trailing comma, and every token is padded to the longest one plus
            // a single separating space — precisely the alignment the file was
            // hand-authored with, so normalising it moved no column. Hence the
            // +2: one for the comma, one for that space.
            //
            // The last field on a line is never padded; trailing whitespace
            // would come back as diff noise the first time an editor stripped
            // it.
            int idW = 0, nameW = 0, bgW = 0;
            foreach (var r in rooms)
            {
                idW   = Math.Max(idW,   Quote(r.RoomId).Length + 2);
                nameW = Math.Max(nameW, Quote(r.DisplayName).Length + 2);
                bgW   = Math.Max(bgW,   Quote(r.BackgroundAsset).Length + 2);
            }

            var sb = new StringBuilder();
            string nl = Environment.NewLine;   // CRLF here, matching every other JSON writer in the tree
            foreach (string line in RoomsJsonHeader) sb.Append(line).Append(nl);
            sb.Append('{').Append(nl);
            sb.Append("  \"rooms\": [").Append(nl);

            for (int i = 0; i < rooms.Count; i++)
            {
                var r = rooms[i];
                sb.Append("    { \"id\": ").Append((Quote(r.RoomId) + ",").PadRight(idW))
                  .Append("\"displayName\": ").Append((Quote(r.DisplayName) + ",").PadRight(nameW))
                  .Append("\"backgroundAsset\": ").Append((Quote(r.BackgroundAsset) + ",").PadRight(bgW))
                  .Append("\"collisionFile\": ").Append(Quote(r.CollisionFile))
                  .Append(" }").Append(i < rooms.Count - 1 ? "," : "").Append(nl);
            }

            sb.Append("  ]").Append(nl);
            sb.Append('}').Append(nl);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, sb.ToString());
        }

        /// <summary>JSON string literal for a value — escaping included.</summary>
        private static string Quote(string value) => JsonSerializer.Serialize(value ?? "");
    }
}
