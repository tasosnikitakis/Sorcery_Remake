// ============================================================================
// NEW ROOM FLOW
// SorceryForge — creating a room from an unused background PNG
// ============================================================================
// The editor is where rooms are born (EDITOR_REVIEW item 7 stage 2). Creating
// one used to mean three hand edits — a C# manifest entry, a Content.mgcb
// block, and a collision file — with no feedback until the game crashed on the
// one you forgot. This does all three from one click.
//
// SHARED WITH THE SCREENSHOT IMPORT. ImageImport (EDITOR_REVIEW item A / PR 5)
// produces the PNG this flow expects to already exist, then hands the very
// same RoomCandidate to the very same Create. Name derivation, the id checks
// and the three writes all live here and only here, so an imported room and a
// hand-dropped one cannot end up registered differently. The pieces the import
// reuses are marked "shared with ImageImport" below.
//
// ZERO TYPING BY DESIGN. The editor has no text-input widget (building one is
// the PR 7 / ImGui decision), so nothing here asks for a name: the room id and
// display name are DERIVED from the background PNG's filename, and the PNG is
// picked from a list. Rename the file, get a different room.
//
// UI-FREE ON PURPOSE. Everything here is filesystem + string work with the
// directories passed in, so it can be exercised headlessly against scratch
// copies instead of only by clicking. EditorGame owns the picker overlay and
// calls Create once.
//
// WHAT CREATE WRITES
//   assets/data/collision_<id>.json   all-empty 40x18 grid
//   Content/Content.mgcb              a #begin block, if the asset lacks one
//   assets/data/rooms.json            one appended entry, via RoomManifest.Save
//
// and deliberately NOT content_<id>.json or layout_<id>.json: those appear the
// first time the user saves something real into the room. A born-empty file is
// exactly what tools/RoundTrip guards against. The collision file is different
// — it is created here because this IS the explicit user-initiated creation,
// not a save side-effect, and paint mode needs a grid to paint into.
// ============================================================================

using SorceryRemake.Rooms;
using SorceryRemake.Tiles;
using System;
using System.Collections.Generic;
using System.IO;

namespace SorceryForge
{
    /// <summary>
    /// One background PNG in Content/ that no registry room uses yet, with the
    /// room id and display name derived from its filename.
    /// </summary>
    // Also the base of ImageImport.ImportCandidate, which adds the source file
    // and its dimensions. Inheritance rather than a wrapper so Create() takes
    // an imported candidate unchanged — the import literally runs New Room's
    // creation code, it does not mirror it.
    public class RoomCandidate
    {
        public string BackgroundAsset = "";   // "RoomBG_Chateau3" (no extension)
        public string RoomId = "";            // "chateau_3"
        public string DisplayName = "";       // "Chateau 3"

        /// <summary>
        /// Non-null when this candidate cannot be created — the derived id
        /// collides with an existing room, a reserved test-room id, or another
        /// candidate. Shown in the picker; Create refuses it.
        /// </summary>
        public string? Problem;

        public bool CanCreate => Problem == null;
    }

    public static class NewRoomFlow
    {
        // Rooms are 320x144 at 8 px per tile. Same literals Game1's test rooms
        // use; there is no shared constant outside EditorLayout, which the
        // headless harness deliberately doesn't compile.
        private const int GridWidth = 320 / TileConfig.TILE_SIZE;   // 40
        private const int GridHeight = 144 / TileConfig.TILE_SIZE;  // 18

        private const string AssetPrefix = "RoomBG_";

        // Erase mode writes <asset>.autosave.png beside the asset on an
        // uncancellable exit. Those match RoomBG_*.png and are recovery
        // sidecars, not rooms.
        private const string AutosaveMarker = ".autosave";

        // ====================================================================
        // NAME DERIVATION
        // ====================================================================
        // The rule, worked from the filename outward:
        //
        //   RoomBG_Chateau3.png
        //     -> strip "RoomBG_" and ".png"        -> "Chateau3"
        //     -> split into words at each separator
        //        ('_' / '-'), at each internal
        //        capital, and at a trailing digit run -> ["Chateau", "3"]
        //     -> display name = words joined by spaces -> "Chateau 3"
        //     -> room id      = words lowercased,
        //        joined by underscores               -> "chateau_3"
        //
        // One decomposition, two renderings — the id is always the display name
        // in snake_case, which is the room-id convention doc/07 documents
        // ("snake_case with the area prefix": forest_1, chateau_0, stonehenge).
        //
        // SEPARATORS ARE CONSUMED, NOT CARRIED (PR 5b, and it was a real bug).
        // The split used to look only for capital and digit boundaries, so a
        // name that was ALREADY snake_case kept its separator inside the word
        // in front of it: "chateau_1" split to ["chateau_", "1"] and rejoined
        // as "chateau__1". Two consequences, the second serious:
        //
        //   - mangled ids with a doubled underscore in them, and
        //   - a COLLISION BYPASS. chateau_1 is a shipped room, so a capture
        //     named chateau_1.png must be refused — but the id actually derived
        //     was chateau__1, which collides with nothing, so the check passed
        //     and the editor happily built a near-duplicate room beside it.
        //
        // Hence the property this rule now guarantees, and that
        // tools/ImportCheck pins:
        //
        //     DeriveRoomId(DeriveRoomId(x)) == DeriveRoomId(x)      (idempotent)
        //     DeriveRoomId(x) never contains "__"
        //
        // Idempotence is what makes the collision check trustworthy: feeding a
        // derived id back through the rule has to land on the same string, or
        // "is this id taken?" is being asked about a different id than the one
        // that would be created. It holds because a derived id is lowercase —
        // so StripPrefix, which matches "RoomBG_" ordinally, can never fire on
        // one — and because '_' is now a separator rather than a word
        // character, so a second pass re-splits exactly where the first joined.
        //
        // Checked against all nine shipped rooms: it reproduces every display
        // name exactly, and every room id except tunnelmouth, which this rule
        // would derive as tunnel_mouth. That room is the outlier — its three
        // multi-word siblings ARE snake_case (near_chateau, inside_chateau,
        // outside_chateau) — and nothing re-derives ids for existing rooms, so
        // the divergence is inert. Room ids are persistence keys; never rename
        // an existing one to match this rule.
        // ====================================================================

        /// <summary>"RoomBG_Chateau3" → "chateau_3".</summary>
        public static string DeriveRoomId(string backgroundAsset) =>
            string.Join("_", SplitWords(StripPrefix(backgroundAsset))).ToLowerInvariant();

        /// <summary>"RoomBG_Chateau3" → "Chateau 3".</summary>
        public static string DeriveDisplayName(string backgroundAsset) =>
            string.Join(" ", SplitWords(StripPrefix(backgroundAsset)));

        private static string StripPrefix(string asset) =>
            asset.StartsWith(AssetPrefix, StringComparison.Ordinal)
                ? asset.Substring(AssetPrefix.Length)
                : asset;

        /// <summary>
        /// The two characters that already mean "word boundary" in a base name.
        /// </summary>
        // These are exactly the two non-alphanumerics IsLegalBaseName admits,
        // so between them and the case/digit boundaries below there is no
        // punctuation left that a word could end up carrying.
        private static bool IsSeparator(char c) => c == '_' || c == '-';

        /// <summary>
        /// Split a base name into words: "OutsideChateau" → ["Outside",
        /// "Chateau"], "Chateau3" → ["Chateau", "3"], "chateau_1" →
        /// ["chateau", "1"], "near-chateau" → ["near", "chateau"].
        /// </summary>
        // Two passes rather than one predicate, because the two kinds of
        // boundary behave differently and conflating them is what produced the
        // "chateau__1" bug: a separator is CONSUMED (it is not part of either
        // neighbour), while a capital or digit boundary is a seam BETWEEN two
        // characters that are both kept.
        //
        // Empty runs — a leading, trailing or doubled separator — contribute no
        // word at all. That is what keeps "__" out of the joined id no matter
        // what the file was called, and it is why "___.png" yields an empty id
        // that CheckRoomId then refuses by name.
        private static List<string> SplitWords(string name)
        {
            var words = new List<string>();
            if (string.IsNullOrEmpty(name)) return words;

            int runStart = 0;
            for (int i = 0; i <= name.Length; i++)
            {
                if (i < name.Length && !IsSeparator(name[i])) continue;
                AddCasedWords(name, runStart, i, words);
                runStart = i + 1;
            }
            return words;
        }

        /// <summary>
        /// Split name[start, end) — a run with no separators in it — at its
        /// internal capitals and digit runs, appending the words found.
        /// </summary>
        private static void AddCasedWords(string name, int start, int end, List<string> words)
        {
            if (end <= start) return;   // empty run: a doubled or edge separator

            int wordStart = start;
            for (int i = start + 1; i < end; i++)
            {
                // A word starts at an uppercase letter following a non-upper
                // character, or at the first digit of a run following a letter.
                // Consecutive capitals stay together so an acronym isn't
                // shredded into single letters.
                bool capBoundary = char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]);
                bool digitBoundary = char.IsDigit(name[i]) && !char.IsDigit(name[i - 1]);
                if (!capBoundary && !digitBoundary) continue;
                words.Add(name.Substring(wordStart, i - wordStart));
                wordStart = i;
            }
            words.Add(name.Substring(wordStart, end - wordStart));
        }

        // ====================================================================
        // CANDIDATES
        // ====================================================================

        /// <summary>
        /// The Content asset name for a room whose background image is called
        /// <paramref name="baseName"/>: "Chateau3" → "RoomBG_Chateau3".
        /// </summary>
        // Shared with ImageImport, which builds the target PNG's path from it.
        public static string AssetNameFor(string baseName) => AssetPrefix + baseName;

        /// <summary>Room ids already claimed by the registry.</summary>
        // Shared with ImageImport: one definition of "taken", so a screenshot
        // named after an existing room is rejected by exactly the rule that
        // rejects a background PNG named after one.
        public static HashSet<string> TakenRoomIds(IReadOnlyList<RoomManifest> registry)
        {
            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in registry) taken.Add(r.RoomId);
            return taken;
        }

        /// <summary>A candidate with its id and display name derived from the asset name.</summary>
        // Shared with ImageImport. Problem is left null — the caller decides
        // which checks apply (CheckRoomId always; the import adds its own).
        public static RoomCandidate MakeCandidate(string backgroundAsset) => new()
        {
            BackgroundAsset = backgroundAsset,
            RoomId = DeriveRoomId(backgroundAsset),
            DisplayName = DeriveDisplayName(backgroundAsset),
        };

        /// <summary>
        /// Why this derived room id cannot become a room, or null if it can.
        /// </summary>
        // Shared with ImageImport. The three ways a derived id is unusable —
        // empty, reserved, already registered — are stated once, here, so the
        // two creation entry points can never disagree about what is legal.
        public static string? CheckRoomId(string roomId, ICollection<string> takenRoomIds)
        {
            if (string.IsNullOrWhiteSpace(roomId))
                return "filename yields an empty room id";
            if (RoomManifest.TestRoomIds.Contains(roomId))
                return $"'{roomId}' is reserved for the programmatic test rooms";
            if (takenRoomIds.Contains(roomId))
                return $"room id '{roomId}' already exists";
            return null;
        }

        /// <summary>
        /// Every RoomBG_*.png in the content directory that no registry room
        /// already uses, in filename order, each with its derived id / name and
        /// any reason it can't be created.
        /// </summary>
        public static List<RoomCandidate> FindCandidates(string contentDir, IReadOnlyList<RoomManifest> registry)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in registry) used.Add(r.BackgroundAsset);
            var takenIds = TakenRoomIds(registry);

            var result = new List<RoomCandidate>();
            if (!Directory.Exists(contentDir)) return result;

            var derivedSoFar = new HashSet<string>(StringComparer.Ordinal);
            var files = Directory.GetFiles(contentDir, AssetPrefix + "*.png");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            foreach (string file in files)
            {
                string asset = Path.GetFileNameWithoutExtension(file);
                if (asset.IndexOf(AutosaveMarker, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (used.Contains(asset)) continue;

                var candidate = MakeCandidate(asset);

                // Short-circuits deliberately: an id that already failed one of
                // the shared checks is NOT recorded in derivedSoFar, so a later
                // file deriving the same id reports that same first reason
                // rather than the misleading "another candidate derives it".
                candidate.Problem = CheckRoomId(candidate.RoomId, takenIds);
                if (candidate.Problem == null && !derivedSoFar.Add(candidate.RoomId))
                    candidate.Problem = $"another candidate already derives '{candidate.RoomId}'";

                result.Add(candidate);
            }
            return result;
        }

        // ====================================================================
        // CREATION
        // ====================================================================

        /// <summary>What Create actually did, for the status line.</summary>
        public class CreateResult
        {
            public bool Ok;
            public string Message = "";
            public readonly List<string> Wrote = new();
        }

        /// <summary>
        /// Create the room: collision grid, .mgcb block, registry entry. Does
        /// NOT reload the cached registry or load the room — the caller owns
        /// that, in that order (RoomManifest.Reload → RoomMeta.RebuildAll →
        /// LoadRoom).
        /// </summary>
        // Nor does it write the background PNG: New Room's already exists (it
        // is what the candidate was found from), and the screenshot import
        // writes its own before calling here. Keeping the PNG out means this
        // method needs no GraphicsDevice and stays headlessly exercisable.
        //
        // ---------------------------------------------------------------------
        // IT READS THE REGISTRY FRESH, FROM dataDir. PR 7b; PR 5b left this as a
        // documented hazard and tools/ImportCheck pinned it as one.
        //
        // The hazard: this method used to build the new registry from the CACHED
        // RoomManifest.All. Two Creates without a RoomManifest.Reload between
        // them therefore both started from the same snapshot, and the second
        // write silently dropped the room the first had added. Nothing failed.
        // Nothing warned. A room simply was not there any more.
        //
        // The editor never hit it, because CreateAndOpenRoom reloads after every
        // file — but that is a caller remembering something, and a caller
        // remembering something is the shape of every bug this comment block
        // exists to describe. So the method no longer depends on being called
        // correctly: it reads the registry it is about to rewrite, from the
        // directory it is about to rewrite it in, at the moment it rewrites it.
        //
        // It also re-checks the id against that fresh registry. A candidate is
        // computed when a picker OPENS; between then and the click, a batch
        // import may have created a room deriving the same id. The check that
        // matters is the one against the file being written.
        // ---------------------------------------------------------------------
        public static CreateResult Create(RoomCandidate candidate, string contentDir, string dataDir)
        {
            var result = new CreateResult();

            if (!candidate.CanCreate)
            {
                result.Message = $"Cannot create {candidate.BackgroundAsset}: {candidate.Problem}";
                return result;
            }

            string roomsPath = Path.Combine(dataDir, "rooms.json");
            List<RoomManifest> registry;
            try
            {
                registry = RoomManifest.LoadFrom(roomsPath);
            }
            catch (Exception ex)
            {
                // Before ANY write. A registry that cannot be read is a registry
                // that cannot be appended to, and leaving a collision file and
                // an .mgcb block behind for a room that will never exist is
                // litter this method is in a position to avoid.
                result.Message = $"Create failed: {ex.Message}";
                return result;
            }

            string? fresh = CheckRoomId(candidate.RoomId, TakenRoomIds(registry));
            if (fresh != null)
            {
                result.Message = $"Cannot create {candidate.BackgroundAsset}: {fresh}";
                return result;
            }

            try
            {
                // Order matters for failure survivability. The registry entry
                // goes LAST, because it is what makes the room exist: fail
                // before it and the leftovers are an orphan collision file and
                // an unused .mgcb block, both harmless and both reused on the
                // next attempt. Fail after it and rooms.json would name assets
                // that aren't there.

                // 1. Collision grid — all empty. Never clobber an existing file
                //    (a leftover from a room that was removed still holds real
                //    painted geometry, and the user asked to create a room, not
                //    to erase one).
                string collisionName = $"collision_{candidate.RoomId}.json";
                string collisionPath = Path.Combine(dataDir, collisionName);
                if (!File.Exists(collisionPath))
                {
                    RoomLoader.SaveCollisionGrid(collisionPath,
                        new TileMapComponent(null, GridWidth, GridHeight));
                    result.Wrote.Add(collisionName);
                }

                // 2. Content pipeline block, so the GAME can load the XNB. The
                //    editor reads the raw PNG and doesn't need it; the game
                //    crashes at startup without it (LoadRoomBackgrounds is
                //    eager on purpose).
                if (EnsureMgcbBlock(contentDir, candidate.BackgroundAsset))
                    result.Wrote.Add("Content.mgcb");

                // 3. Registry entry, appended last in the array — array order
                //    is room order, and appending is the only always-safe edit.
                //    Onto the registry READ FROM DISK above, not onto the
                //    cached one; see the block on Create.
                registry.Add(new RoomManifest(candidate.RoomId, candidate.DisplayName,
                                              candidate.BackgroundAsset, collisionName));
                RoomManifest.Save(registry, roomsPath);
                result.Wrote.Add("rooms.json");

                result.Ok = true;
                result.Message =
                    $"Created {candidate.DisplayName} ({candidate.RoomId}) — wrote {string.Join(" + ", result.Wrote)}. " +
                    "Rebuild the game (dotnet build) for it to see the background.";
            }
            catch (Exception ex)
            {
                result.Message = $"Create failed: {ex.Message}";
            }
            return result;
        }

        // ====================================================================
        // CONTENT.MGCB
        // ====================================================================

        /// <summary>
        /// Append a #begin block for the asset if the file has none. Returns
        /// true if the file was modified.
        /// </summary>
        // Line-based string append, NOT a parse-and-rewrite: .mgcb is a
        // hand-maintained file with its own section banners, and reformatting
        // it would bury this one-block change in a whole-file diff. The block
        // shape is copied verbatim from the existing RoomBG_* entries.
        public static bool EnsureMgcbBlock(string contentDir, string asset)
        {
            string path = Path.Combine(contentDir, "Content.mgcb");
            if (!File.Exists(path))
                throw new FileNotFoundException($"Content pipeline file not found: '{path}'.", path);

            string text = File.ReadAllText(path);
            string begin = $"#begin {asset}.png";
            if (text.Contains(begin, StringComparison.Ordinal)) return false;

            // Match the file's own line endings rather than the platform's —
            // this file is CRLF in the repo and a mixed-ending append shows up
            // as a whole-file diff under some editors.
            string nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            if (text.Length > 0 && !text.EndsWith(nl, StringComparison.Ordinal)) text += nl;

            string block = string.Join(nl, new[]
            {
                "",
                begin,
                "/importer:TextureImporter",
                "/processor:TextureProcessor",
                "/processorParam:ColorKeyColor=255,0,255,255",
                "/processorParam:ColorKeyEnabled=False",
                "/processorParam:GenerateMipmaps=False",
                "/processorParam:PremultiplyAlpha=True",
                "/processorParam:ResizeToPowerOfTwo=False",
                "/processorParam:MakeSquare=False",
                "/processorParam:TextureFormat=Color",
                $"/build:{asset}.png",
                "",
            });

            File.WriteAllText(path, text + block);
            return true;
        }
    }
}
