// ============================================================================
// NEW ROOM FLOW
// SorceryForge — creating a room from an unused background PNG
// ============================================================================
// The editor is where rooms are born (EDITOR_REVIEW item 7 stage 2). Creating
// one used to mean three hand edits — a C# manifest entry, a Content.mgcb
// block, and a collision file — with no feedback until the game crashed on the
// one you forgot. This does all three from one click.
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
        //     -> split into words at each internal
        //        capital and at a trailing digit run -> ["Chateau", "3"]
        //     -> display name = words joined by spaces -> "Chateau 3"
        //     -> room id      = words lowercased,
        //        joined by underscores               -> "chateau_3"
        //
        // One decomposition, two renderings — the id is always the display name
        // in snake_case, which is the room-id convention doc/07 documents
        // ("snake_case with the area prefix": forest_1, chateau_0, stonehenge).
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
        /// Split PascalCase-with-trailing-digits into words: "OutsideChateau"
        /// → ["Outside", "Chateau"], "Chateau3" → ["Chateau", "3"].
        /// </summary>
        private static List<string> SplitWords(string name)
        {
            var words = new List<string>();
            if (string.IsNullOrEmpty(name)) return words;

            int start = 0;
            for (int i = 1; i < name.Length; i++)
            {
                // A word starts at an uppercase letter following a non-upper
                // character, or at the first digit of a run following a letter.
                // Consecutive capitals stay together so an acronym isn't
                // shredded into single letters.
                bool capBoundary = char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]);
                bool digitBoundary = char.IsDigit(name[i]) && !char.IsDigit(name[i - 1]);
                if (!capBoundary && !digitBoundary) continue;
                words.Add(name.Substring(start, i - start));
                start = i;
            }
            words.Add(name.Substring(start));
            return words;
        }

        // ====================================================================
        // CANDIDATES
        // ====================================================================

        /// <summary>
        /// Every RoomBG_*.png in the content directory that no registry room
        /// already uses, in filename order, each with its derived id / name and
        /// any reason it can't be created.
        /// </summary>
        public static List<RoomCandidate> FindCandidates(string contentDir, IReadOnlyList<RoomManifest> registry)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var takenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in registry)
            {
                used.Add(r.BackgroundAsset);
                takenIds.Add(r.RoomId);
            }

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

                string id = DeriveRoomId(asset);
                var candidate = new RoomCandidate
                {
                    BackgroundAsset = asset,
                    RoomId = id,
                    DisplayName = DeriveDisplayName(asset),
                };

                if (string.IsNullOrWhiteSpace(id))
                    candidate.Problem = "filename yields an empty room id";
                else if (RoomManifest.TestRoomIds.Contains(id))
                    candidate.Problem = $"'{id}' is reserved for the programmatic test rooms";
                else if (takenIds.Contains(id))
                    candidate.Problem = $"room id '{id}' already exists";
                else if (!derivedSoFar.Add(id))
                    candidate.Problem = $"another candidate already derives '{id}'";

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
        /// NOT reload the registry or load the room — the caller owns that, in
        /// that order (RoomManifest.Reload → RoomMeta.RebuildAll → LoadRoom).
        /// </summary>
        public static CreateResult Create(RoomCandidate candidate, string contentDir, string dataDir)
        {
            var result = new CreateResult();

            if (!candidate.CanCreate)
            {
                result.Message = $"Cannot create {candidate.BackgroundAsset}: {candidate.Problem}";
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
                var rooms = new List<RoomManifest>(RoomManifest.All)
                {
                    new RoomManifest(candidate.RoomId, candidate.DisplayName,
                                     candidate.BackgroundAsset, collisionName),
                };
                RoomManifest.Save(rooms, Path.Combine(dataDir, "rooms.json"));
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
