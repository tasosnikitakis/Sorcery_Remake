// ============================================================================
// IMPORTCHECK — SORCERYFORGE SCREENSHOT-IMPORT REGRESSION HARNESS
// Sorcery+ Remake
// ============================================================================
// THE INVARIANTS IT GUARDS
//
//   "A screenshot dropped into assets/import/ becomes a 320x144 PNG whose
//    pixels are the source's own pixels — point-sampled, never blended, and
//    (with the toggle on) snapped to the 27 Amstrad CPC hardware colours —
//    registered as a room by exactly the code the New Room button runs."
//
//   Every clause of that is checkable without a screen, because the import is
//   deliberately split: SorceryForge/ImageImport.cs and NewRoomFlow.cs do the
//   resampling, the quantizing, the naming and the three creation writes in
//   plain Color[] and filesystem code, and EditorGame does nothing but decode
//   at one end (Texture2D.FromStream) and encode at the other (SaveAsPng).
//   This harness compiles those two files — the editor's own, not a copy — and
//   drives them directly.
//
// WHAT IT CANNOT COVER
//
//   The two MonoGame calls at the ends. Decoding a real JPEG and encoding a
//   PNG both need a GraphicsDevice, which needs a desktop session. Those, and
//   the visual question of whether quantized output looks right on a real
//   screenshot, are the owner's smoke test. Everything between them is here.
//
// SECTIONS
//
//   1 palette     27 colours, and they contain every triple in the project's
//                 own CPC decoder (extraction/convert_cpc_graphics.py)
//   2 quantize    nearest-colour behaviour, including on the exact levels the
//                 shipped room backgrounds actually use
//   3 resample    point sampling is every-Nth-pixel, with no blending
//   4 pipeline    a synthesised 640x288 source end to end, toggle both ways
//   5 naming      the filename rule and the size classifier
//   5a derivation idempotence and no-doubled-underscore, the properties that
//                 make the id-collision check trustworthy (PR 5b regression)
//   6 candidates  a scratch import folder: which files are offered, and the
//                 exact reason each refused one is refused
//   7 creation    Create against a scratch tree: collision grid, an
//                 append-only .mgcb edit, one new rooms.json row
//   8 headers     PNG and JPEG dimension reading, including a real repo PNG
//   9 crop        the aspect-locked selection algebra and the fit transform,
//                 then the pixels an awkward-scale crop actually cuts
//   10 presets    the built-in 384x270 calibration, stored-beats-built-in
//                 resolution, .sorceryforge/settings.json's contract, and the
//                 aspect lock over every reachable selection state
//   11 batch      which files "Import All" takes and which it names as skips,
//                 the region each one cuts, and the summary line
//
// HOW TO RUN
//
//   dotnet build tools/ImportCheck/ImportCheck.csproj
//   dotnet run   --project tools/ImportCheck/ImportCheck.csproj
//   dotnet run   --project tools/ImportCheck/ImportCheck.csproj -- --out <dir>
//   dotnet run   --project tools/ImportCheck/ImportCheck.csproj -- --probe <img>
//
//   Exit 0 = every check passed. Exit 1 = failures (listed inline as FAIL).
//   Exit 2 = could not run (bad arguments, unsafe --out, repo root not found,
//            unreadable or invalid assets/data/rooms.json).
//
//   --probe prints the dimensions TryReadImageSize reads from any one file,
//   which is how you check the header reader against a real-world capture
//   (an emulator screenshot, a phone photo, an EXIF-laden export) without
//   adding it to the repo.
//
// SAFETY
//
//   Nothing outside the scratch directory is ever written. assets/data,
//   Content/ and assets/import/ are read only — Content/Content.mgcb and
//   assets/data/rooms.json are COPIED into the scratch tree and the copies are
//   what Create edits. AssertScratchSafe refuses a scratch path that is, holds,
//   or sits inside the repo, and the pre-run clean deletes only files it
//   recognises rather than recursing through a stranger's folder.
// ============================================================================

using Microsoft.Xna.Framework;
using SorceryForge;
using SorceryRemake.Rooms;
using SorceryRemake.Tiles;
using System;
using System.Collections.Generic;
using System.IO;

namespace SorceryRemake.Tools.ImportCheck
{
    internal static class Program
    {
        private const string DefaultScratchName = "sorcery-importcheck";

        private static int _failures;
        private static int _checks;

        private static int Main(string[] args)
        {
            string? outArg = null;
            string? probeArg = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--out":
                        if (i + 1 >= args.Length) { Console.Error.WriteLine("--out needs a directory"); return 2; }
                        outArg = args[++i];
                        break;
                    case "--probe":
                        if (i + 1 >= args.Length) { Console.Error.WriteLine("--probe needs a file"); return 2; }
                        probeArg = args[++i];
                        break;
                    case "-h":
                    case "--help":
                        PrintUsage();
                        return 0;
                    default:
                        Console.Error.WriteLine($"unknown argument: {args[i]}");
                        PrintUsage();
                        return 2;
                }
            }

            if (probeArg != null) return Probe(probeArg);

            string repoRoot = EditorPaths.RepoRoot;
            string scratch = Path.GetFullPath(outArg ?? Path.Combine(Path.GetTempPath(), DefaultScratchName));

            // A repo root that fell back to the executable's own directory
            // means EditorPaths' walk-up failed — refuse rather than testing
            // against an empty world and declaring success. Same guard, same
            // reason, as tools/RoundTrip.
            if (!File.Exists(Path.Combine(repoRoot, "SorceryRemake.csproj")))
            {
                Console.Error.WriteLine($"could not locate the repo root (got '{repoRoot}').");
                Console.Error.WriteLine("run this from inside the source tree, not a published build.");
                return 2;
            }

            Console.WriteLine("ImportCheck — SorceryForge screenshot-import regression harness");
            Console.WriteLine($"  repo    : {repoRoot}");
            Console.WriteLine($"  scratch : {scratch}");

            string scratchImport = Path.Combine(scratch, "import");
            string scratchContent = Path.Combine(scratch, "Content");
            string scratchData = Path.Combine(scratch, "data");
            string scratchSettings = Path.Combine(scratch, "settings");

            try
            {
                // First touch of the registry, inside the guard: RoomManifest.All
                // is a Lazy<T> that throws our own message for a missing or
                // malformed rooms.json, and that message should end the run
                // cleanly rather than as an unhandled stack trace.
                Console.WriteLine($"  rooms   : {RoomManifest.All.Count} (RoomManifest.All)");
                Console.WriteLine();

                AssertScratchSafe(scratch, repoRoot);
                CleanScratch(scratch);
                Directory.CreateDirectory(scratchImport);
                Directory.CreateDirectory(scratchContent);
                Directory.CreateDirectory(scratchData);
                Directory.CreateDirectory(scratchSettings);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("refusing to run: " + ex.Message);
                return 2;
            }

            CheckPalette();
            CheckQuantize();
            CheckResample();
            CheckPipeline();
            CheckNamingAndSizes();
            CheckCandidates(scratchImport, scratchContent);
            CheckCreation(scratchContent, scratchData);
            CheckImageHeaders(scratch, repoRoot);
            CheckCrop();
            CheckCropPresets(scratchSettings, repoRoot);
            CheckBatch(scratchContent, scratchData);

            Console.WriteLine();
            Console.WriteLine($"  {_checks} checks, {_failures} failure(s)");
            Console.WriteLine();
            Console.WriteLine(_failures == 0
                ? "  IMPORT PIPELINE HOLDS: point-sampled, unblended, in-palette output;\n" +
                  "  naming and registration go through New Room's own code."
                : "  IMPORT PIPELINE BROKEN — see the FAIL lines above.");
            Console.WriteLine($"\n  scratch left at {scratch}");

            return _failures == 0 ? 0 : 1;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("usage: dotnet run --project tools/ImportCheck/ImportCheck.csproj -- [--out <dir>] [--probe <image>]");
            Console.WriteLine();
            Console.WriteLine("  --out <dir>      scratch directory to work in (cleared and rebuilt each run).");
            Console.WriteLine($"                   default: %TEMP%\\{DefaultScratchName}");
            Console.WriteLine("  --probe <image>  print the dimensions ImageImport.TryReadImageSize reads");
            Console.WriteLine("                   from one file, then exit. Use it to check the header");
            Console.WriteLine("                   reader against a real capture without committing it.");
            Console.WriteLine();
            Console.WriteLine("exit 0 = all checks pass; 1 = failures; 2 = could not run.");
        }

        private static int Probe(string path)
        {
            if (!File.Exists(path)) { Console.Error.WriteLine($"no such file: {path}"); return 2; }
            bool ok = ImageImport.TryReadImageSize(path, out int w, out int h);
            if (!ok) { Console.WriteLine($"{path}: header not readable as PNG or JPEG"); return 1; }
            int n = ImageImport.ExactMultiple(w, h);
            Console.WriteLine($"{path}: {w}x{h}" +
                (n > 0 ? $" — exactly {n}x the 320x144 room, imports directly"
                       : " — no direct import: " + ImageImport.ExpectedSizeMessage(w, h)));
            return 0;
        }

        // ====================================================================
        // 1. PALETTE
        // ====================================================================

        // The 16 Mode 0 colours from extraction/convert_cpc_graphics.py's
        // CPC_PALETTE, copied here verbatim. This is the cross-check the PR
        // asked for, made machine-readable: if either table is edited so that
        // one stops containing the other, the assertion below says so instead
        // of the two drifting apart unnoticed.
        private static readonly (byte r, byte g, byte b)[] Mode0PaletteFromExtractionScript =
        {
            (0, 0, 0), (0, 0, 128), (0, 0, 255),
            (128, 0, 0), (128, 0, 128), (128, 0, 255),
            (255, 0, 0), (255, 0, 128), (255, 0, 255),
            (0, 128, 0), (0, 128, 128), (0, 128, 255),
            (128, 128, 0), (128, 128, 128), (128, 128, 255),
            (255, 128, 0),
        };

        private static void CheckPalette()
        {
            Section("1. PALETTE — the 27 CPC hardware colours");

            Assert("27 colours (3 levels ^ 3 channels)", ImageImport.CpcPalette.Length == 27);

            var seen = new HashSet<uint>();
            bool distinct = true, levelsOnly = true, opaque = true;
            foreach (var c in ImageImport.CpcPalette)
            {
                if (!seen.Add(c.PackedValue)) distinct = false;
                if (!IsLevel(c.R) || !IsLevel(c.G) || !IsLevel(c.B)) levelsOnly = false;
                if (c.A != 255) opaque = false;
            }
            Assert("all 27 are distinct", distinct);
            Assert("every channel is one of CpcLevels", levelsOnly);
            Assert("every entry is opaque", opaque);

            bool containsMode0 = true;
            foreach (var (r, g, b) in Mode0PaletteFromExtractionScript)
                if (!PaletteHas(r, g, b)) containsMode0 = false;
            Assert("contains all 16 Mode 0 colours from extraction/convert_cpc_graphics.py", containsMode0);

            // The containment above is what makes the quantizer a no-op on
            // anything decoded by that script: an already-in-palette pixel is
            // its own nearest neighbour.
            bool fixedPoint = true;
            foreach (var (r, g, b) in Mode0PaletteFromExtractionScript)
            {
                var one = new[] { new Color(r, g, b) };
                ImageImport.QuantizeToCpc(one);
                if (one[0] != new Color(r, g, b)) fixedPoint = false;
            }
            Assert("quantize leaves every Mode 0 colour exactly where it was", fixedPoint);
        }

        private static bool IsLevel(byte v)
        {
            foreach (byte l in ImageImport.CpcLevels) if (l == v) return true;
            return false;
        }

        private static bool PaletteHas(byte r, byte g, byte b)
        {
            foreach (var c in ImageImport.CpcPalette)
                if (c.R == r && c.G == g && c.B == b) return true;
            return false;
        }

        // ====================================================================
        // 2. QUANTIZE
        // ====================================================================

        private static void CheckQuantize()
        {
            Section("2. QUANTIZE — nearest of the 27, no dithering");

            AssertSnap("JPEG-ish near-miss snaps back", (3, 126, 250), (0, 128, 255));
            AssertSnap("above the 128/255 midpoint goes up", (200, 200, 200), (255, 255, 255));
            AssertSnap("below the 0/128 midpoint goes down", (60, 60, 60), (0, 0, 0));

            // A dead tie: 64 is equidistant from 0 and 128. The scan keeps the
            // first strictly-better candidate, and the palette is built lowest
            // level first, so ties resolve downward. Pinned because it is the
            // one case where "nearest" does not decide on its own.
            AssertSnap("a tie resolves to the lower level", (64, 64, 64), (0, 0, 0));

            // The levels the shipped backgrounds are actually in — see the long
            // comment on ImageImport.CpcLevels. Pinned so the size of the shift
            // is a stated fact rather than a surprise on the smoke test.
            AssertSnap("chateau-set emulator levels move by 5 or less", (123, 125, 251), (128, 128, 255));
            AssertSnap("stonehenge-set emulator levels move a lot more", (99, 101, 207), (128, 128, 255));

            // Alpha: holes normalise to (0,0,0,0) exactly as LoadRoom does;
            // any other alpha survives while RGB snaps.
            var hole = new[] { new Color(200, 30, 90, 0) };
            ImageImport.QuantizeToCpc(hole);
            Assert("a fully transparent pixel becomes (0,0,0,0)", hole[0] == Color.Transparent);

            var semi = new[] { new Color((byte)10, (byte)10, (byte)10, (byte)128) };
            ImageImport.QuantizeToCpc(semi);
            Assert("partial alpha is preserved while RGB snaps",
                semi[0].R == 0 && semi[0].G == 0 && semi[0].B == 0 && semi[0].A == 128);

            // A broad sweep: whatever goes in, what comes out is in the set.
            var sweep = new Color[4096];
            for (int i = 0; i < sweep.Length; i++)
                sweep[i] = new Color((byte)(i * 7), (byte)(i * 13), (byte)(i * 29));
            ImageImport.QuantizeToCpc(sweep);
            Assert("4096 arbitrary colours all land in the 27-colour set", AllInPalette(sweep));
        }

        private static void AssertSnap(string label, (byte r, byte g, byte b) input, (byte r, byte g, byte b) expected)
        {
            var px = new[] { new Color(input.r, input.g, input.b) };
            ImageImport.QuantizeToCpc(px);
            var want = new Color(expected.r, expected.g, expected.b);
            Assert($"{label}: ({input.r},{input.g},{input.b}) -> ({expected.r},{expected.g},{expected.b})",
                px[0] == want, $"got ({px[0].R},{px[0].G},{px[0].B})");
        }

        private static bool AllInPalette(Color[] pixels)
        {
            foreach (var c in pixels) if (!PaletteHas(c.R, c.G, c.B)) return false;
            return true;
        }

        // ====================================================================
        // 3 & 4. RESAMPLE AND THE WHOLE PIPELINE
        // ====================================================================
        // The sources are built so that a blend is impossible to mistake for a
        // sample: the pixels point sampling must keep carry a "signal" colour
        // derived from their destination coordinates, and every other pixel
        // carries loud junk. If the output equals the signal everywhere, then
        // every output pixel came from exactly one source pixel, and from the
        // right one.
        // ====================================================================

        private static Color Signal(int dx, int dy) =>
            new((byte)dx, (byte)dy, (byte)((dx * 7 + dy * 13) & 0xFF));

        private static Color Junk(int sx, int sy) =>
            new((byte)(255 - sx), (byte)(255 - sy), (byte)200);

        /// <summary>A source of exactly N x (320x144) carrying the signal at every Nth pixel.</summary>
        private static Color[] BuildMultipleSource(int n, out int srcW, out int srcH)
        {
            srcW = ImageImport.RoomWidth * n;
            srcH = ImageImport.RoomHeight * n;
            var src = new Color[srcW * srcH];
            for (int y = 0; y < srcH; y++)
            for (int x = 0; x < srcW; x++)
                src[y * srcW + x] = (x % n == 0 && y % n == 0)
                    ? Signal(x / n, y / n)
                    : Junk(x, y);
            return src;
        }

        private static void CheckResample()
        {
            Section("3. RESAMPLE — every Nth pixel, never a blend");

            foreach (int n in new[] { 1, 2, 3, 4 })
            {
                var src = BuildMultipleSource(n, out int w, out int h);
                var dst = ImageImport.PointSample(src, w, h, ImageImport.WholeImage(w, h),
                                                  ImageImport.RoomWidth, ImageImport.RoomHeight);
                Assert($"{w}x{h} ({n}x) -> 320x144 keeps exactly the sampled pixels",
                    MatchesSignal(dst));
            }

            // Guard rails. PointSample writes straight into an array indexed
            // from the region, so a region outside the source has to be refused
            // rather than trusted — that is the failure mode a misread header
            // would otherwise turn into an out-of-range crash.
            var small = new Color[8 * 8];
            Assert("a region outside the source is refused",
                Throws(() => ImageImport.PointSample(small, 8, 8, new Rectangle(4, 4, 8, 8), 4, 4)));
            Assert("a zero-size region is refused",
                Throws(() => ImageImport.PointSample(small, 8, 8, new Rectangle(0, 0, 0, 8), 4, 4)));
            Assert("an undersized pixel array is refused",
                Throws(() => ImageImport.PointSample(small, 16, 16, new Rectangle(0, 0, 16, 16), 4, 4)));
        }

        private static void CheckPipeline()
        {
            Section("4. PIPELINE — a synthesised 640x288 source, end to end");

            var src = BuildMultipleSource(2, out int w, out int h);
            var whole = ImageImport.WholeImage(w, h);

            // Toggle OFF: a pure pass-through of the sampled source values.
            var raw = ImageImport.BuildRoomBackground(src, w, h, whole, quantize: false);
            Assert("output is 320x144", raw.Length == ImageImport.RoomWidth * ImageImport.RoomHeight);
            Assert("quantize OFF gives the exact point-sampled source values", MatchesSignal(raw));

            // Toggle ON: every pixel in the palette, and each one the nearest
            // neighbour of the value the OFF run produced — so quantizing is
            // provably the only difference between the two runs.
            var snapped = ImageImport.BuildRoomBackground(src, w, h, whole, quantize: true);
            Assert("quantize ON gives 320x144 too", snapped.Length == raw.Length);
            Assert("quantize ON puts every pixel in the 27-colour set", AllInPalette(snapped));

            var expected = (Color[])raw.Clone();
            ImageImport.QuantizeToCpc(expected);
            Assert("quantize ON differs from OFF by exactly the snap", SequenceEqual(snapped, expected));

            // The sampled values sweep R and G across their whole byte range,
            // so this run really did exercise colours far from the palette
            // rather than a handful of near-misses.
            int moved = 0;
            for (int i = 0; i < raw.Length; i++) if (raw[i] != snapped[i]) moved++;
            Assert("the run was a real workout (most pixels were off-palette)",
                moved > raw.Length / 2, $"{moved} of {raw.Length} pixels moved");
        }

        private static bool MatchesSignal(Color[] dst)
        {
            for (int dy = 0; dy < ImageImport.RoomHeight; dy++)
            for (int dx = 0; dx < ImageImport.RoomWidth; dx++)
                if (dst[dy * ImageImport.RoomWidth + dx] != Signal(dx, dy)) return false;
            return true;
        }

        // ====================================================================
        // 5. NAMING AND SIZES
        // ====================================================================

        private static void CheckNamingAndSizes()
        {
            Section("5. NAMING AND SIZES");

            AssertLegal("Chateau3", true);
            AssertLegal("near_chateau", true);
            AssertLegal("Room-7", true);
            AssertLegal("chateau 3", false);          // space
            AssertLegal("Château3", false);      // accent
            AssertLegal("shot.final", false);         // dot
            AssertLegal("", false);

            AssertMultiple(320, 144, 1);
            AssertMultiple(640, 288, 2);
            AssertMultiple(960, 432, 3);
            AssertMultiple(700, 500, 0);
            AssertMultiple(640, 144, 0);              // only one axis scaled
            AssertMultiple(480, 216, 0);              // 1.5x is not an integer multiple
            AssertMultiple(0, 0, 0);

            // Derivation parity: the import must produce the identical names
            // New Room would, because it hands the same candidate to the same
            // Create. Asserted against NewRoomFlow directly, not a copy.
            string asset = NewRoomFlow.AssetNameFor("Chateau3");
            Assert("AssetNameFor prefixes RoomBG_", asset == "RoomBG_Chateau3", asset);
            Assert("id derivation matches New Room's",
                NewRoomFlow.DeriveRoomId(asset) == "chateau_3", NewRoomFlow.DeriveRoomId(asset));
            Assert("display name derivation matches New Room's",
                NewRoomFlow.DeriveDisplayName(asset) == "Chateau 3", NewRoomFlow.DeriveDisplayName(asset));

            CheckIdDerivation();
        }

        // ====================================================================
        // 5a. ID DERIVATION
        // ====================================================================
        // PR 5b, from the owner's live smoke: chateau_1.png derived the id
        // chateau__1. The split treated the name's own '_' as a word boundary
        // and then rejoined the words WITH an underscore, so the separator was
        // counted twice. It mangled the id, and — much worse — it walked
        // straight past the collision check, because "chateau_1 is taken" is
        // true and "chateau__1 is taken" is not. A near-duplicate room could be
        // created beside a shipped one.
        //
        // The fix is a property, not a special case, so this section asserts
        // the property:
        //
        //     DeriveRoomId(DeriveRoomId(x)) == DeriveRoomId(x)
        //     DeriveRoomId(x) never contains "__"
        //
        // Idempotence is exactly what the collision check needs to be
        // trustworthy: the id that gets tested has to be the id that gets
        // created. Both consumers of the rule — the Import picker and the New
        // Room picker — call this one function, so fixing it here fixes both.
        // ====================================================================

        // Names chosen to cover every shape the rule can meet: PascalCase,
        // trailing digits, already-snake_case, hyphens, acronyms, doubled and
        // edge separators, digits-only, and the empty string.
        private static readonly string[] DerivationCorpus =
        {
            "Chateau3", "Chateau_1", "chateau_1", "chateau_10", "Chateau",
            "NearChateau", "near_chateau", "OutsideChateau", "TunnelMouth",
            "Stonehenge", "stonehenge", "Room-7", "near-chateau", "NEARChateau",
            "HUD2", "a", "A1", "9", "shot_2_of_3", "a__b", "_leading",
            "trailing_", "--", "_", "__", "",
            "RoomBG_Chateau3",                       // the prefixed form
        };

        private static void CheckIdDerivation()
        {
            Section("5a. ID DERIVATION — idempotent, and no doubled underscore");

            // ---- the regression, stated as itself ----
            // chateau_1.png is the file the owner actually dropped in.
            string mangled = NewRoomFlow.DeriveRoomId(NewRoomFlow.AssetNameFor("chateau_1"));
            Assert("chateau_1.png derives chateau_1 (was chateau__1)", mangled == "chateau_1", mangled);
            Assert("  and is therefore refused as a registry collision",
                NewRoomFlow.CheckRoomId(mangled, NewRoomFlow.TakenRoomIds(RoomManifest.All)) != null,
                NewRoomFlow.CheckRoomId(mangled, NewRoomFlow.TakenRoomIds(RoomManifest.All)) ?? "accepted!");

            AssertDerives("Chateau_1", "chateau_1", "Chateau 1");
            AssertDerives("Chateau3", "chateau_3", "Chateau 3");
            AssertDerives("NearChateau", "near_chateau", "Near Chateau");
            AssertDerives("near_chateau", "near_chateau", "near chateau");
            AssertDerives("near-chateau", "near_chateau", "near chateau");
            AssertDerives("shot_2_of_3", "shot_2_of_3", "shot 2 of 3");
            AssertDerives("_leading", "leading", "leading");
            AssertDerives("a__b", "a_b", "a b");
            AssertDerives("__", "", "");

            // ---- the properties, over the whole corpus ----
            bool idempotent = true, noDouble = true;
            string? idemFail = null, doubleFail = null;
            foreach (string name in DerivationCorpus)
            {
                string once = NewRoomFlow.DeriveRoomId(name);
                string twice = NewRoomFlow.DeriveRoomId(once);
                if (once != twice) { idempotent = false; idemFail ??= $"{name} -> {once} -> {twice}"; }
                if (once.Contains("__", StringComparison.Ordinal))
                { noDouble = false; doubleFail ??= $"{name} -> {once}"; }
            }
            Assert($"derive(derive(x)) == derive(x) for all {DerivationCorpus.Length} corpus names",
                idempotent, idemFail);
            Assert("no derived id contains \"__\"", noDouble, doubleFail);

            // A derived id must also survive being used as a base name again —
            // that is the path a user takes when they rename a rejected file to
            // the id the picker showed them.
            bool stableAsBaseName = true;
            string? stableFail = null;
            foreach (string name in DerivationCorpus)
            {
                string id = NewRoomFlow.DeriveRoomId(name);
                if (id.Length == 0) continue;
                string viaAsset = NewRoomFlow.DeriveRoomId(NewRoomFlow.AssetNameFor(id));
                if (viaAsset != id) { stableAsBaseName = false; stableFail ??= $"{id} -> {viaAsset}"; }
            }
            Assert("re-importing under the derived id yields that same id", stableAsBaseName, stableFail);

            // ---- the nine shipped rooms ----
            // The rule's own comment claims it reproduces every shipped display
            // name and every shipped id but tunnelmouth's. That claim guards
            // the live registry against a derivation change, so assert it
            // rather than leaving it as prose.
            int idMatches = 0, nameMatches = 0;
            var idDiverged = new List<string>();
            foreach (var room in RoomManifest.All)
            {
                if (NewRoomFlow.DeriveRoomId(room.BackgroundAsset) == room.RoomId) idMatches++;
                else idDiverged.Add($"{room.BackgroundAsset} -> {NewRoomFlow.DeriveRoomId(room.BackgroundAsset)} != {room.RoomId}");
                if (NewRoomFlow.DeriveDisplayName(room.BackgroundAsset) == room.DisplayName) nameMatches++;
            }
            Assert("every shipped background re-derives its own display name",
                nameMatches == RoomManifest.All.Count, $"{nameMatches}/{RoomManifest.All.Count}");
            Assert("every shipped background re-derives its own id, except tunnelmouth",
                idDiverged.Count == 1 && idDiverged[0].Contains("tunnel_mouth", StringComparison.Ordinal),
                idDiverged.Count == 0 ? "none diverged" : string.Join("; ", idDiverged));
        }

        private static void AssertDerives(string baseName, string expectId, string expectName)
        {
            string asset = NewRoomFlow.AssetNameFor(baseName);
            string id = NewRoomFlow.DeriveRoomId(asset);
            string name = NewRoomFlow.DeriveDisplayName(asset);
            Assert($"\"{baseName}\" -> id \"{expectId}\"", id == expectId, id);
            Assert($"\"{baseName}\" -> name \"{expectName}\"", name == expectName, name);
        }

        private static void AssertLegal(string baseName, bool expected) =>
            Assert($"base name \"{baseName}\" is {(expected ? "legal" : "refused")}",
                ImageImport.IsLegalBaseName(baseName) == expected);

        private static void AssertMultiple(int w, int h, int expected)
        {
            int got = ImageImport.ExactMultiple(w, h);
            Assert($"{w}x{h} -> multiple {expected}", got == expected, $"got {got}");
        }

        // ====================================================================
        // 6. CANDIDATES
        // ====================================================================
        // A scratch import folder holding one file per outcome. The registry is
        // the REAL one — "chateau_0 is taken" and "room_1 is reserved" are only
        // worth asserting against the ids the project actually ships.
        // ====================================================================

        private static void CheckCandidates(string importDir, string contentDir)
        {
            Section("6. CANDIDATES — what the picker offers, and why it refuses the rest");

            WritePngStub(Path.Combine(importDir, "Chateau3.png"), 320, 144);   // loses to the .jpg below
            WriteJpegStub(Path.Combine(importDir, "Chateau3.jpg"), 640, 288);  // the one importable file
            WritePngStub(Path.Combine(importDir, "Bad Name.png"), 320, 144);
            WritePngStub(Path.Combine(importDir, "Chateau0.png"), 320, 144);
            WritePngStub(Path.Combine(importDir, "Room1.png"), 320, 144);
            WritePngStub(Path.Combine(importDir, "Odd.png"), 700, 500);
            WritePngStub(Path.Combine(importDir, "Tiny.png"), 100, 50);
            WritePngStub(Path.Combine(importDir, "Ghost.png"), 320, 144);
            // PR 5b: the file from the owner's live smoke. Before the
            // derivation fix this one was OFFERED, because it derived
            // chateau__1 and nothing owns that id — the collision bypass.
            WritePngStub(Path.Combine(importDir, "chateau_1.png"), 384, 270);
            File.WriteAllText(Path.Combine(importDir, "NotAnImage.png"), "this is not an image");
            File.WriteAllText(Path.Combine(importDir, "notes.txt"), "ignored: wrong extension");

            // The target this scratch Content/ already holds, so Ghost.png has
            // somewhere to collide with.
            WritePngStub(Path.Combine(contentDir, "RoomBG_Ghost.png"), 320, 144);

            var found = ImageImport.FindCandidates(importDir, contentDir, RoomManifest.All);

            Assert("only image extensions are listed (notes.txt ignored)", found.Count == 10, $"{found.Count} listed");

            var chateau3 = Find(found, "Chateau3.jpg");
            Assert("Chateau3.jpg is importable", chateau3 != null && chateau3.CanCreate,
                chateau3?.Problem ?? "not listed");
            if (chateau3 != null)
            {
                Assert("  -> RoomBG_Chateau3", chateau3.BackgroundAsset == "RoomBG_Chateau3", chateau3.BackgroundAsset);
                Assert("  -> room id chateau_3", chateau3.RoomId == "chateau_3", chateau3.RoomId);
                Assert("  -> display name \"Chateau 3\"", chateau3.DisplayName == "Chateau 3", chateau3.DisplayName);
                Assert("  -> 640x288 read as a 2x source", chateau3.Multiple == 2, chateau3.SizeLabel);
            }

            // An awkward size is not a refusal — it routes to the crop step.
            var odd = Find(found, "Odd.png");
            Assert("Odd.png (700x500) is importable via the crop step",
                odd != null && odd.CanCreate && odd.NeedsCrop, odd?.Problem ?? "not listed");
            var exact = Find(found, "Chateau3.jpg");
            Assert("an exact multiple does NOT route to the crop step",
                exact != null && !exact.NeedsCrop);

            AssertProblemMentions(found, "Bad Name.png", "rename the file");
            // An illegal name derives nothing at all — there is no asset name,
            // so no id. The batch summary relies on that empty id to fall back
            // to the filename, which is the only handle such a file has.
            var badName = Find(found, "Bad Name.png");
            Assert("  and derives no id to report it under",
                badName != null && badName.RoomId.Length == 0 && badName.BackgroundAsset.Length == 0,
                badName?.RoomId ?? "not listed");
            AssertProblemMentions(found, "Chateau0.png", "already exists");
            AssertProblemMentions(found, "Room1.png", "reserved");
            AssertProblemMentions(found, "Tiny.png", "smaller than a 320x144 room");
            AssertProblemMentions(found, "Ghost.png", "already exists in Content/");
            AssertProblemMentions(found, "NotAnImage.png", "could not read the image size");
            AssertProblemMentions(found, "Chateau3.png", "already derives");
            AssertProblemMentions(found, "chateau_1.png", "room id 'chateau_1' already exists");

            // Ordering is the filename order the picker shows, so the file that
            // wins a duplicate derivation is predictable rather than whichever
            // the filesystem happened to hand back first.
            Assert("listed in filename order", IsSortedByFileName(found));
        }

        private static ImportCandidate? Find(List<ImportCandidate> list, string fileName)
        {
            foreach (var c in list)
                if (string.Equals(c.FileName, fileName, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        private static void AssertProblemMentions(List<ImportCandidate> list, string fileName, string fragment)
        {
            var c = Find(list, fileName);
            bool ok = c != null && c.Problem != null
                      && c.Problem.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
            Assert($"{fileName} refused: \"...{fragment}...\"", ok, c?.Problem ?? "not listed");
        }

        private static bool IsSortedByFileName(List<ImportCandidate> list)
        {
            for (int i = 1; i < list.Count; i++)
                if (StringComparer.OrdinalIgnoreCase.Compare(list[i - 1].SourcePath, list[i].SourcePath) > 0)
                    return false;
            return true;
        }

        // ====================================================================
        // 7. CREATION
        // ====================================================================
        // NewRoomFlow.Create against a scratch copy of Content.mgcb and an
        // empty data directory. This is the New Room path — the point of the
        // section is that the import reaches it unchanged.
        // ====================================================================

        private static void CheckCreation(string contentDir, string dataDir)
        {
            Section("7. CREATION — collision grid, .mgcb append, one rooms.json row");

            string realMgcb = Path.Combine(EditorPaths.RepoContentDir, "Content.mgcb");
            string scratchMgcb = Path.Combine(contentDir, "Content.mgcb");
            File.Copy(realMgcb, scratchMgcb, overwrite: true);
            string mgcbBefore = File.ReadAllText(scratchMgcb);

            string realRooms = RoomManifest.RoomsJsonPath;
            string scratchRooms = Path.Combine(dataDir, "rooms.json");
            string roomsBefore = File.ReadAllText(realRooms);

            var candidate = NewRoomFlow.MakeCandidate(NewRoomFlow.AssetNameFor("Chateau3"));
            var result = NewRoomFlow.Create(candidate, contentDir, dataDir);
            Assert("Create succeeds", result.Ok, result.Message);

            // -- collision grid --
            string collisionPath = Path.Combine(dataDir, "collision_chateau_3.json");
            Assert("wrote collision_chateau_3.json", File.Exists(collisionPath));
            if (File.Exists(collisionPath))
            {
                int[,] grid = RoomLoader.LoadCollisionGrid(collisionPath);
                bool empty = true;
                for (int y = 0; y < grid.GetLength(0); y++)
                for (int x = 0; x < grid.GetLength(1); x++)
                    if (grid[y, x] != TileConfig.EMPTY) empty = false;
                Assert("  40x18 and completely empty",
                    grid.GetLength(1) == 40 && grid.GetLength(0) == 18 && empty,
                    $"{grid.GetLength(1)}x{grid.GetLength(0)}");
            }

            // -- .mgcb --
            string mgcbAfter = File.ReadAllText(scratchMgcb);
            Assert("Content.mgcb edit is append-only", mgcbAfter.StartsWith(mgcbBefore, StringComparison.Ordinal));
            Assert("  exactly one #begin for the new asset",
                Occurrences(mgcbAfter, "#begin RoomBG_Chateau3.png") == 1);
            Assert("  exactly one /build for the new asset",
                Occurrences(mgcbAfter, "/build:RoomBG_Chateau3.png") == 1);
            Assert("  no other asset's block was touched",
                Occurrences(mgcbAfter, "#begin ") == Occurrences(mgcbBefore, "#begin ") + 1);

            bool secondEdit = NewRoomFlow.EnsureMgcbBlock(contentDir, "RoomBG_Chateau3");
            Assert("  a second EnsureMgcbBlock is a no-op",
                !secondEdit && File.ReadAllText(scratchMgcb) == mgcbAfter);

            // -- rooms.json --
            Assert("wrote rooms.json", File.Exists(scratchRooms));
            if (File.Exists(scratchRooms))
            {
                string roomsAfter = File.ReadAllText(scratchRooms);
                Assert("  gained exactly one entry row",
                    EntryRows(roomsAfter) == EntryRows(roomsBefore) + 1,
                    $"{EntryRows(roomsBefore)} -> {EntryRows(roomsAfter)}");
                Assert("  the row names the derived id",
                    roomsAfter.Contains("\"id\": \"chateau_3\"", StringComparison.Ordinal));
                Assert("  the row names the background asset",
                    roomsAfter.Contains("\"backgroundAsset\": \"RoomBG_Chateau3\"", StringComparison.Ordinal));
                Assert("  the row names the collision file",
                    roomsAfter.Contains("\"collisionFile\": \"collision_chateau_3.json\"", StringComparison.Ordinal));
                Assert("  the header comment survived verbatim",
                    HeaderOf(roomsAfter) == HeaderOf(roomsBefore));
                // chateau_3 is shorter than every existing id, display name and
                // asset name, so nothing re-pads: the diff really is one line.
                Assert("  every pre-existing row is unchanged (no column re-padding)",
                    AllOldRowsSurvive(roomsBefore, roomsAfter));
            }

            // -- refusal --
            var refused = NewRoomFlow.MakeCandidate(NewRoomFlow.AssetNameFor("Room1"));
            refused.Problem = NewRoomFlow.CheckRoomId(refused.RoomId, NewRoomFlow.TakenRoomIds(RoomManifest.All));
            var refusedResult = NewRoomFlow.Create(refused, contentDir, dataDir);
            Assert("Create refuses a candidate carrying a Problem", !refusedResult.Ok, refusedResult.Message);
            Assert("  and wrote nothing for it",
                !File.Exists(Path.Combine(dataDir, "collision_room_1.json"))
                && Occurrences(File.ReadAllText(scratchMgcb), "#begin RoomBG_Room1.png") == 0);
        }

        private static int Occurrences(string haystack, string needle)
        {
            int n = 0, i = 0;
            while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        /// <summary>Number of `{ "id": ... }` rows in a rooms.json.</summary>
        private static int EntryRows(string text) => Occurrences(text, "{ \"id\": ");

        /// <summary>Everything before the opening brace — the hand-written header block.</summary>
        private static string HeaderOf(string text)
        {
            int brace = text.IndexOf("\n{", StringComparison.Ordinal);
            return brace < 0 ? text : text.Substring(0, brace);
        }

        private static bool AllOldRowsSurvive(string before, string after)
        {
            foreach (string line in before.Replace("\r\n", "\n").Split('\n'))
            {
                string trimmed = line.Trim();
                if (!trimmed.StartsWith("{ \"id\": ", StringComparison.Ordinal)) continue;
                // The last row of the old file had no trailing comma and now
                // does; compare without it.
                string bare = trimmed.TrimEnd(',');
                if (!after.Contains(bare, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        // ====================================================================
        // 9. CROP
        // ====================================================================
        // Two halves. The rectangle algebra — aspect lock, minimum, clamping,
        // the wheel step, the fit transform — is checked case by case. Then the
        // thing that actually matters: that the pixels which come out of a crop
        // are pixels that went in, at the positions the box said.
        //
        // The source for that is built so every pixel carries its own
        // coordinates as a colour. Decoding an output pixel therefore says
        // where it came from, independently of the sampling code — a blended
        // pixel decodes to coordinates that are wrong or nonexistent, and there
        // is nowhere for a filter to hide.
        // ====================================================================

        private static Color Coded(int x, int y) =>
            new((byte)(x & 0xFF), (byte)(y & 0xFF), (byte)(((x >> 8) << 4) | (y >> 8)));

        private static bool DecodeCoded(Color c, out int x, out int y)
        {
            x = c.R | ((c.B >> 4) << 8);
            y = c.G | ((c.B & 0x0F) << 8);
            return true;
        }

        private static Color[] BuildCodedSource(int srcW, int srcH)
        {
            var src = new Color[srcW * srcH];
            for (int y = 0; y < srcH; y++)
            for (int x = 0; x < srcW; x++)
                src[y * srcW + x] = Coded(x, y);
            return src;
        }

        private static void CheckCrop()
        {
            Section("9. CROP — aspect-locked selection, and the pixels it cuts");

            // ---- rectangle algebra ----
            Assert("320 wide -> 144 tall (the room's own aspect)", ImageImport.CropHeightFor(320) == 144);
            Assert("640 wide -> 288 tall", ImageImport.CropHeightFor(640) == 288);
            Assert("700 wide -> 315 tall", ImageImport.CropHeightFor(700) == 315,
                ImageImport.CropHeightFor(700).ToString());

            Assert("a source at least one room in both axes can be cropped", ImageImport.CanCrop(320, 144));
            Assert("a too-narrow source cannot", !ImageImport.CanCrop(319, 500));
            Assert("a too-short source cannot", !ImageImport.CanCrop(700, 143));

            Assert("700x500: the widest selection is the full width",
                ImageImport.MaxCropWidth(700, 500) == 700, ImageImport.MaxCropWidth(700, 500).ToString());
            Assert("1920x1080: the widest selection is the full width",
                ImageImport.MaxCropWidth(1920, 1080) == 1920, ImageImport.MaxCropWidth(1920, 1080).ToString());
            Assert("400x200: height is the limit, not width",
                ImageImport.MaxCropWidth(400, 200) == 400, ImageImport.MaxCropWidth(400, 200).ToString());
            Assert("2000x200: height is the limit",
                ImageImport.CropHeightFor(ImageImport.MaxCropWidth(2000, 200)) <= 200);

            var def = ImageImport.DefaultCropRect(700, 500);
            Assert("the default selection is as large as fits, centred",
                def == new Rectangle(0, 92, 700, 315), def.ToString());

            var clamped = ImageImport.ClampCropRect(new Rectangle(-50, -50, 10, 10), 700, 500);
            Assert("an undersized, out-of-bounds selection clamps to the minimum at the origin",
                clamped == new Rectangle(0, 0, 320, 144), clamped.ToString());

            var oversized = ImageImport.ClampCropRect(new Rectangle(600, 400, 5000, 5000), 700, 500);
            Assert("an oversized selection clamps to the source and stays inside it",
                oversized.Width <= 700 && oversized.Height <= 500
                && oversized.Right <= 700 && oversized.Bottom <= 500, oversized.ToString());

            bool aspectHeld = true;
            var walk = ImageImport.DefaultCropRect(1920, 1080);
            for (int i = 0; i < 60; i++)
            {
                walk = ImageImport.StepCropWidth(walk, +1, 1920, 1080);
                if (walk.Height != ImageImport.CropHeightFor(walk.Width)) aspectHeld = false;
                if (walk.Width < ImageImport.RoomWidth) aspectHeld = false;
                if (walk.Right > 1920 || walk.Bottom > 1080 || walk.X < 0 || walk.Y < 0) aspectHeld = false;
            }
            Assert("60 wheel notches in: aspect, minimum and bounds all still hold",
                aspectHeld && walk.Width == ImageImport.RoomWidth, walk.ToString());

            for (int i = 0; i < 60; i++) walk = ImageImport.StepCropWidth(walk, -1, 1920, 1080);
            Assert("and 60 back out reaches the maximum again",
                walk.Width == ImageImport.MaxCropWidth(1920, 1080), walk.ToString());

            // The wheel resizes about the selection's centre, so a notch in and
            // a notch out returns to (near enough) where it started rather than
            // walking the box across the image.
            var mid = ImageImport.ClampCropRect(new Rectangle(100, 40, 500, 225), 700, 500);
            var there = ImageImport.StepCropWidth(mid, +1, 700, 500);
            var back = ImageImport.StepCropWidth(there, -1, 700, 500);
            Assert("in-then-out keeps the selection centred where it was",
                Math.Abs((back.X + back.Width / 2) - (mid.X + mid.Width / 2)) <= 1
                && Math.Abs((back.Y + back.Height / 2) - (mid.Y + mid.Height / 2)) <= 1,
                $"{mid} -> {there} -> {back}");

            // ---- the fit transform ----
            var area = new Rectangle(100, 50, 960, 432);
            var fit = ImageImport.FitInside(700, 500, area);
            Assert("the fitted image stays inside the area",
                area.Contains(fit), fit.ToString());
            Assert("the fitted image keeps the source's aspect",
                Math.Abs(fit.Width / (float)fit.Height - 700f / 500f) < 0.01f, fit.ToString());
            Assert("the fitted image is centred in the area",
                Math.Abs((fit.X + fit.Width / 2) - (area.X + area.Width / 2)) <= 1
                && Math.Abs((fit.Y + fit.Height / 2) - (area.Y + area.Height / 2)) <= 1, fit.ToString());

            var corner = ImageImport.ScreenToSource(new Point(fit.X, fit.Y), fit, 700, 500);
            Assert("the fitted top-left maps back to source (0, 0)", corner == Point.Zero, corner.ToString());
            var far = ImageImport.ScreenToSource(new Point(fit.Right + 500, fit.Bottom + 500), fit, 700, 500);
            Assert("a point past the image clamps to the last source pixel",
                far == new Point(699, 499), far.ToString());

            var whole = ImageImport.SourceRectToScreen(new Rectangle(0, 0, 700, 500), fit, 700, 500);
            Assert("the whole source maps back onto the whole fitted rectangle",
                whole == fit, $"{whole} vs {fit}");

            // ---- the pixels ----
            CheckCropPixels();
        }

        private static void CheckCropPixels()
        {
            const int srcW = 700, srcH = 500;
            var src = BuildCodedSource(srcW, srcH);

            // An exact 2x region inside an awkward source: the answer is
            // knowable in closed form, so assert it exactly.
            var exact = new Rectangle(40, 30, 640, 288);
            var dst = ImageImport.PointSample(src, srcW, srcH, exact,
                                              ImageImport.RoomWidth, ImageImport.RoomHeight);
            bool exactOk = true;
            for (int dy = 0; dy < ImageImport.RoomHeight && exactOk; dy++)
            for (int dx = 0; dx < ImageImport.RoomWidth && exactOk; dx++)
                if (dst[dy * ImageImport.RoomWidth + dx] != Coded(40 + dx * 2, 30 + dy * 2)) exactOk = false;
            Assert("a 640x288 region of a 700x500 source cuts exactly those pixels", exactOk);

            // An awkward region — 700x315 down to 320x144, a scale of 2.1875.
            // Nothing here assumes the sampling formula: every output pixel is
            // decoded back to the source coordinates it came from, and those
            // are what get checked.
            var awkward = ImageImport.DefaultCropRect(srcW, srcH);
            var odd = ImageImport.PointSample(src, srcW, srcH, awkward,
                                              ImageImport.RoomWidth, ImageImport.RoomHeight);

            bool insideRegion = true, monotone = true;
            var seenX = new HashSet<int>();
            var seenY = new HashSet<int>();
            int prevRowY = -1;
            for (int dy = 0; dy < ImageImport.RoomHeight; dy++)
            {
                int prevX = -1;
                for (int dx = 0; dx < ImageImport.RoomWidth; dx++)
                {
                    DecodeCoded(odd[dy * ImageImport.RoomWidth + dx], out int sx, out int sy);
                    if (sx < awkward.Left || sx >= awkward.Right
                        || sy < awkward.Top || sy >= awkward.Bottom) insideRegion = false;
                    if (sx < prevX) monotone = false;
                    prevX = sx;
                    if (dy == 0) seenX.Add(sx);
                    if (dx == 0)
                    {
                        if (sy < prevRowY) monotone = false;
                        prevRowY = sy;
                        seenY.Add(sy);
                    }
                }
            }
            Assert("an awkward 2.19x crop takes every pixel from inside the selection", insideRegion);
            Assert("  and sweeps it left-to-right, top-to-bottom without doubling back", monotone);
            Assert("  and reaches 320 distinct columns and 144 distinct rows",
                seenX.Count == ImageImport.RoomWidth && seenY.Count == ImageImport.RoomHeight,
                $"{seenX.Count} columns, {seenY.Count} rows");
            Assert("  its first pixel is the selection's own top-left corner",
                odd[0] == Coded(awkward.X, awkward.Y));
            Assert("  every output value is a real source colour (nothing was blended)",
                AllAreSourcePixels(odd, srcW, srcH));

            // A crop that is exactly one room is the identity.
            var oneRoom = ImageImport.ClampCropRect(new Rectangle(11, 7, 320, 144), srcW, srcH);
            var identity = ImageImport.PointSample(src, srcW, srcH, oneRoom,
                                                   ImageImport.RoomWidth, ImageImport.RoomHeight);
            bool identityOk = true;
            for (int dy = 0; dy < ImageImport.RoomHeight && identityOk; dy++)
            for (int dx = 0; dx < ImageImport.RoomWidth && identityOk; dx++)
                if (identity[dy * ImageImport.RoomWidth + dx] != Coded(11 + dx, 7 + dy)) identityOk = false;
            Assert("a 320x144 selection is copied out pixel for pixel", identityOk);

            // And the quantize toggle composes with the crop the same way it
            // composes with a whole-image import.
            var quantized = ImageImport.BuildRoomBackground(src, srcW, srcH, awkward, quantize: true);
            var expected = (Color[])odd.Clone();
            ImageImport.QuantizeToCpc(expected);
            Assert("quantize applies to a cropped import identically", SequenceEqual(quantized, expected));
        }

        private static bool AllAreSourcePixels(Color[] pixels, int srcW, int srcH)
        {
            foreach (var c in pixels)
            {
                DecodeCoded(c, out int x, out int y);
                if (x < 0 || y < 0 || x >= srcW || y >= srcH) return false;
                if (Coded(x, y) != c) return false;   // the colour must round-trip
            }
            return true;
        }

        // ====================================================================
        // 10. CROP PRESETS
        // ====================================================================
        // PR 5b. Every source in a batch of emulator captures is framed
        // identically, so the rectangle that was right for the first is right
        // for all of them. Presets are that rectangle, remembered against the
        // SOURCE DIMENSIONS it was cut from, in .sorceryforge/settings.json.
        //
        // Three things are worth proving headlessly and none of them need a
        // screen: the resolution order (stored beats built-in beats
        // largest-that-fits), the file's contract (born empty, byte-stable,
        // unknown members preserved), and — the cheap one that pins the whole
        // crop mechanism — that no reachable selection can violate the aspect
        // lock, including the ones a preset introduces.
        // ====================================================================

        private static void CheckCropPresets(string settingsDir, string repoRoot)
        {
            Section("10. CROP PRESETS — the built-in, the file, and the aspect lock");

            CheckBuiltInPreset();
            CheckPresetResolution();
            CheckSettingsFile(settingsDir);
            CheckSettingsAreIgnored(repoRoot);
            CheckAspectLockHolds();
        }

        // ---- the shipped 384x270 calibration --------------------------------

        private static void CheckBuiltInPreset()
        {
            bool has = ImageImport.TryBuiltInCropPreset(
                ImageImport.CpcFrameWidth, ImageImport.CpcFrameHeight, out var builtIn);
            Assert("384x270 has a built-in preset", has);
            Assert("  and it is (32, 41, 320, 144) — the owner's live calibration",
                builtIn == new Rectangle(32, 41, ImageImport.RoomWidth, ImageImport.RoomHeight),
                builtIn.ToString());

            // x is the one number with a second, independent derivation: the
            // CPC's horizontal border is (384 - 320) / 2 either side. y is
            // measured only — the room is a 144-line slice of a 200-line
            // screen, so there is no arithmetic to check it against.
            Assert("  x is the CPC's own border arithmetic, (384 - 320) / 2",
                builtIn.X == (ImageImport.CpcFrameWidth - ImageImport.RoomWidth) / 2, builtIn.X.ToString());
            Assert("  it is a 1:1 cut — exactly one room, no rescale",
                builtIn.Width == ImageImport.RoomWidth && builtIn.Height == ImageImport.RoomHeight);
            Assert("  and it lies wholly inside a 384x270 frame",
                builtIn.Right <= ImageImport.CpcFrameWidth && builtIn.Bottom <= ImageImport.CpcFrameHeight);

            // Exactly one size is built in. A near miss must NOT match: a
            // 385x270 capture is a different emulator setting and its playfield
            // is somewhere else.
            Assert("no other size has one (383x270)", !ImageImport.TryBuiltInCropPreset(383, 270, out _));
            Assert("no other size has one (384x271)", !ImageImport.TryBuiltInCropPreset(384, 271, out _));
            Assert("no other size has one (1920x1080)", !ImageImport.TryBuiltInCropPreset(1920, 1080, out _));
            Assert("384x270 is not an exact multiple, so it does reach the crop step",
                ImageImport.ExactMultiple(ImageImport.CpcFrameWidth, ImageImport.CpcFrameHeight) == 0);
            Assert("  and it is big enough to crop",
                ImageImport.CanCrop(ImageImport.CpcFrameWidth, ImageImport.CpcFrameHeight));
        }

        // ---- stored beats built-in beats largest-that-fits ------------------

        private static void CheckPresetResolution()
        {
            const int w = ImageImport.CpcFrameWidth, h = ImageImport.CpcFrameHeight;

            var fromBuiltIn = ImageImport.ResolveCropRect(w, h, null, out var builtInOrigin);
            Assert("with nothing stored, a 384x270 source opens on the built-in",
                builtInOrigin == ImageImport.CropPresetOrigin.BuiltIn
                && fromBuiltIn == ImageImport.CpcFrameCrop, $"{builtInOrigin} {fromBuiltIn}");

            var mine = new Rectangle(30, 44, 320, 144);
            var fromStored = ImageImport.ResolveCropRect(w, h, mine, out var storedOrigin);
            Assert("a stored preset overrides the built-in",
                storedOrigin == ImageImport.CropPresetOrigin.Stored && fromStored == mine,
                $"{storedOrigin} {fromStored}");

            var noPreset = ImageImport.ResolveCropRect(700, 500, null, out var noneOrigin);
            Assert("a size with neither falls back to the largest box that fits",
                noneOrigin == ImageImport.CropPresetOrigin.None
                && noPreset == ImageImport.DefaultCropRect(700, 500), $"{noneOrigin} {noPreset}");

            // A hand-edited settings file is the reason ResolveCropRect clamps
            // rather than trusts. Nonsense must cost a badly placed box the
            // user can see and move, never an out-of-range region reaching
            // PointSample.
            var junk = ImageImport.ResolveCropRect(700, 500, new Rectangle(-900, -900, 99999, 3), out _);
            Assert("a nonsense stored rect is clamped back into shape",
                junk == ImageImport.ClampCropRect(junk, 700, 500)
                && junk.X >= 0 && junk.Y >= 0 && junk.Right <= 700 && junk.Bottom <= 500,
                junk.ToString());
            Assert("  including its aspect and its floor",
                junk.Height == ImageImport.CropHeightFor(junk.Width)
                && junk.Width >= ImageImport.RoomWidth, junk.ToString());

            // Eligibility for the batch import (section 11) is exactly "opens
            // already framed", so it is defined off the same two sources.
            Assert("HasCropPreset: true for 384x270 with nothing stored",
                ImageImport.HasCropPreset(w, h, null));
            Assert("HasCropPreset: true for any size with something stored",
                ImageImport.HasCropPreset(700, 500, mine));
            Assert("HasCropPreset: false for a size with neither",
                !ImageImport.HasCropPreset(700, 500, null));

            // The status-bar line names which of the three it was, because
            // "why is the box here?" is the first question the overlay raises.
            Assert("the stored line names the source size",
                ImageImport.DescribeCropPreset(ImageImport.CropPresetOrigin.Stored, w, h)
                    == "preset from last 384x270 crop",
                ImageImport.DescribeCropPreset(ImageImport.CropPresetOrigin.Stored, w, h));
            Assert("the built-in line says so",
                ImageImport.DescribeCropPreset(ImageImport.CropPresetOrigin.BuiltIn, w, h)
                    .Contains("built-in", StringComparison.Ordinal));
            Assert("and the no-preset line does not claim one",
                !ImageImport.DescribeCropPreset(ImageImport.CropPresetOrigin.None, 700, 500)
                    .Contains("preset from", StringComparison.Ordinal));
        }

        // ---- .sorceryforge/settings.json ------------------------------------

        private static void CheckSettingsFile(string dir)
        {
            string path = EditorSettings.GetPath(dir);

            // Born empty. A clone whose owner never crops anything must not
            // gain a file — the same rule content_*.json and worldmap.json live
            // by, for the same reason.
            var fresh = new EditorSettings();
            Assert("nothing stored and no file yet: nothing is written", !fresh.Save(dir));
            Assert("  so the folder stays clean", !File.Exists(path));

            fresh.SetCropPreset(384, 270, new Rectangle(32, 41, 320, 144));
            fresh.SetCropPreset(1920, 1080, new Rectangle(96, 32, 960, 432));
            Assert("a confirmed crop writes the file", fresh.Save(dir));
            Assert("  and it is there now", File.Exists(path));

            string first = File.ReadAllText(path);
            var reloaded = EditorSettings.Load(dir, out string? loadError);
            Assert("it loads back with no error", loadError == null, loadError);
            Assert("  both presets survived", reloaded.CropPresetCount == 2, reloaded.CropPresetCount.ToString());
            Assert("  384x270 came back exactly",
                reloaded.CropPreset(384, 270) == new Rectangle(32, 41, 320, 144),
                reloaded.CropPreset(384, 270)?.ToString() ?? "missing");
            Assert("  and a size with no preset returns nothing",
                reloaded.CropPreset(700, 500) == null);

            reloaded.Save(dir);
            Assert("load -> save with no change is byte-identical",
                File.ReadAllText(path) == first);

            // Last-used wins: the preset is a memory of the most recent
            // decision, not a first-one-sticks rule.
            reloaded.SetCropPreset(384, 270, new Rectangle(31, 40, 320, 144));
            reloaded.Save(dir);
            var again = EditorSettings.Load(dir, out _);
            Assert("re-cropping a size replaces its preset",
                again.CropPreset(384, 270) == new Rectangle(31, 40, 320, 144),
                again.CropPreset(384, 270)?.ToString() ?? "missing");
            Assert("  and leaves the other size alone",
                again.CropPreset(1920, 1080) == new Rectangle(96, 32, 960, 432));

            // Unknown members. The file will grow other settings; an older
            // build must not eat a newer one's.
            string withFuture =
                "{" + Environment.NewLine +
                "  \"cropPresets\": {" + Environment.NewLine +
                "    \"384x270\": { \"x\": 32, \"y\": 41, \"w\": 320, \"h\": 144 }" + Environment.NewLine +
                "  }," + Environment.NewLine +
                "  \"futureThing\": { \"a\": 1, \"b\": [1, 2, 3] }," + Environment.NewLine +
                "  \"aScalar\": 7" + Environment.NewLine +
                "}" + Environment.NewLine;
            File.WriteAllText(path, withFuture);

            var future = EditorSettings.Load(dir, out string? futureError);
            Assert("a file with unrecognised members loads", futureError == null, futureError);
            Assert("  its two unknown members are carried", future.UnknownMemberCount == 2,
                future.UnknownMemberCount.ToString());
            Assert("  and its preset was still read", future.CropPreset(384, 270) != null);

            future.SetCropPreset(700, 500, new Rectangle(0, 92, 700, 315));
            future.Save(dir);
            string rewritten = File.ReadAllText(path);
            Assert("  saving keeps the unknown object",
                rewritten.Contains("\"futureThing\"", StringComparison.Ordinal)
                && rewritten.Contains("[1,2,3]", StringComparison.Ordinal), rewritten);
            Assert("  and the unknown scalar",
                rewritten.Contains("\"aScalar\": 7", StringComparison.Ordinal), rewritten);
            Assert("  and the new preset went in beside them",
                rewritten.Contains("\"700x500\"", StringComparison.Ordinal), rewritten);

            EditorSettings.Load(dir, out _).Save(dir);
            Assert("  a file carrying unknown members still round-trips byte-identically",
                File.ReadAllText(path) == rewritten);

            // A file that EXISTS is written even when there is nothing left to
            // store — emptying it was a deliberate act. (Not the same rule as
            // "don't create an empty file"; this is the other half of it.)
            Assert("an existing file is rewritten even with nothing to store",
                new EditorSettings().Save(dir));
            Assert("  and what is left parses as an empty settings object",
                EditorSettings.Load(dir, out _).CropPresetCount == 0);

            // Malformed input is reported, never fatal, and never partially
            // applied.
            File.WriteAllText(path, "{ this is not json");
            var broken = EditorSettings.Load(dir, out string? brokenError);
            Assert("a malformed file reports and falls back to defaults",
                brokenError != null && broken.CropPresetCount == 0 && broken.UnknownMemberCount == 0,
                brokenError ?? "no error reported");

            File.WriteAllText(path, "[1, 2, 3]" + Environment.NewLine);
            EditorSettings.Load(dir, out string? arrayError);
            Assert("a JSON array where an object belongs is reported too",
                arrayError != null, arrayError ?? "no error reported");

            // One bad entry is skipped; the rest of the file still applies.
            File.WriteAllText(path,
                "{ \"cropPresets\": { \"384x270\": { \"x\": 32, \"y\": 41 }, " +
                "\"700x500\": { \"x\": 0, \"y\": 92, \"w\": 700, \"h\": 315 } } }");
            var partial = EditorSettings.Load(dir, out string? partialError);
            Assert("an entry missing w/h is skipped, not guessed at",
                partialError == null && partial.CropPreset(384, 270) == null,
                partialError ?? partial.CropPreset(384, 270)?.ToString() ?? "");
            Assert("  and its well-formed neighbour still loads",
                partial.CropPreset(700, 500) == new Rectangle(0, 92, 700, 315));

            Assert("deleting the file leaves nothing behind", EditorSettings.Delete(dir));
            Assert("  and a fresh load is empty and errorless",
                EditorSettings.Load(dir, out string? goneError).CropPresetCount == 0 && goneError == null);
        }

        /// <summary>
        /// The settings file must never be committable. The whole "personal
        /// state cannot gate collaboration" claim rests on one .gitignore line,
        /// so assert the line rather than trusting it.
        /// </summary>
        private static void CheckSettingsAreIgnored(string repoRoot)
        {
            string gitignore = Path.Combine(repoRoot, ".gitignore");
            string text = File.Exists(gitignore) ? File.ReadAllText(gitignore) : "";
            Assert($".gitignore covers {EditorSettings.DirName}/",
                text.Contains(EditorSettings.DirName + "/", StringComparison.Ordinal),
                File.Exists(gitignore) ? "no matching line" : "no .gitignore");

            // And it really is at the repo root, which is what that line
            // matches — not beside the editor's bin/ output.
            string real = Path.GetFullPath(EditorSettings.GetPath(null));
            string expected = Path.GetFullPath(
                Path.Combine(repoRoot, EditorSettings.DirName, EditorSettings.FileName));
            Assert("  and that is where the editor would write it",
                string.Equals(real, expected, StringComparison.OrdinalIgnoreCase), real);
        }

        // ---- the aspect lock, over every reachable selection ----------------
        // "20:9 exactly" is not literally achievable at integer sizes — a
        // 384-wide box would need to be 172.8 tall — so the property the
        // mechanism actually guarantees, and the one worth pinning, is:
        //
        //   Height == CropHeightFor(Width)     the lock itself, exact: the
        //                                      height is always DERIVED, never
        //                                      a stale value carried along
        //   |W*9 - H*20| <= 10                 which is the same as saying the
        //                                      height is within half a pixel of
        //                                      true 20:9 — the rounding
        //                                      CropHeightFor does and no more
        //   Width >= 320                       the floor
        //   inside the source                  the bounds
        //
        // Reachable means: whatever the crop overlay can produce. That is the
        // opening rectangle (all three preset origins), plus any number of
        // wheel notches, plus any drag, in any order.

        private static bool IsValidSelection(Rectangle r, int srcW, int srcH, out string why)
        {
            if (r.Height != ImageImport.CropHeightFor(r.Width))
            { why = $"{r}: height is not CropHeightFor({r.Width}) = {ImageImport.CropHeightFor(r.Width)}"; return false; }
            if (Math.Abs(r.Width * 9 - r.Height * 20) > 10)
            { why = $"{r}: off true 20:9 by more than half a pixel"; return false; }
            if (r.Width < ImageImport.RoomWidth)
            { why = $"{r}: narrower than one room"; return false; }
            if (r.X < 0 || r.Y < 0 || r.Right > srcW || r.Bottom > srcH)
            { why = $"{r}: outside the {srcW}x{srcH} source"; return false; }
            why = "";
            return true;
        }

        // Sizes a real capture actually arrives at, plus the degenerate ends of
        // the range. All are croppable (>= one room both ways); a smaller
        // source never reaches the overlay at all, because RunImport refuses it.
        private static readonly (int w, int h)[] CroppableSizes =
        {
            (384, 270), (320, 144), (321, 145), (700, 500), (768, 540),
            (1920, 1080), (1024, 768), (2000, 200), (400, 200), (4000, 3000),
            (320, 2000), (1280, 145),
        };

        private static void CheckAspectLockHolds()
        {
            // Deterministic, seeded: a harness that fails only sometimes is
            // worse than one that does not test this at all.
            var rng = new Random(20250825);
            int states = 0;
            bool ok = true;
            string? firstFailure = null;

            foreach (var (srcW, srcH) in CroppableSizes)
            {
                var starts = new List<Rectangle>
                {
                    ImageImport.DefaultCropRect(srcW, srcH),
                    ImageImport.ResolveCropRect(srcW, srcH, null, out _),
                    ImageImport.ResolveCropRect(srcW, srcH, new Rectangle(32, 41, 320, 144), out _),
                    ImageImport.ResolveCropRect(srcW, srcH, new Rectangle(-9999, -9999, 1, 1), out _),
                    ImageImport.ResolveCropRect(srcW, srcH, new Rectangle(5, 5, 999999, 999999), out _),
                    ImageImport.ClampCropRect(new Rectangle(0, 0, 0, 0), srcW, srcH),
                    ImageImport.ClampCropRect(new Rectangle(srcW, srcH, srcW * 3, srcH * 3), srcW, srcH),
                };

                foreach (var start in starts)
                {
                    var r = start;
                    if (!IsValidSelection(r, srcW, srcH, out string why))
                    { ok = false; firstFailure ??= $"{srcW}x{srcH} opening {why}"; }
                    states++;

                    // 120 mixed gestures: wheel in, wheel out, and drags of
                    // arbitrary size — the overlay's whole vocabulary, in the
                    // arbitrary order a user produces it in.
                    for (int step = 0; step < 120; step++)
                    {
                        switch (rng.Next(3))
                        {
                            case 0:
                                r = ImageImport.StepCropWidth(r, +1, srcW, srcH);
                                break;
                            case 1:
                                r = ImageImport.StepCropWidth(r, -1, srcW, srcH);
                                break;
                            default:
                                r = ImageImport.ClampCropRect(
                                    new Rectangle(r.X + rng.Next(-srcW, srcW + 1),
                                                  r.Y + rng.Next(-srcH, srcH + 1),
                                                  r.Width, r.Height),
                                    srcW, srcH);
                                break;
                        }

                        if (!IsValidSelection(r, srcW, srcH, out string stepWhy))
                        { ok = false; firstFailure ??= $"{srcW}x{srcH} step {step}: {stepWhy}"; }
                        states++;

                        // And the point of all of it: whatever the selection
                        // is, sampling it must not throw. This is the clause
                        // that turns the algebra into a promise about pixels.
                        if (r.Width <= 0 || r.Height <= 0 || r.Right > srcW || r.Bottom > srcH)
                        { ok = false; firstFailure ??= $"{srcW}x{srcH} step {step}: unsamplable {r}"; }
                    }
                }
            }

            Assert($"every one of {states} reachable selections holds the aspect lock, floor and bounds",
                ok, firstFailure);

            // The lock stated the other way round, on the function itself: the
            // height for a width is the rounded 20:9 height and nothing else.
            bool derived = true;
            string? derivedFail = null;
            for (int w = ImageImport.RoomWidth; w <= 4000; w++)
            {
                int h = ImageImport.CropHeightFor(w);
                if (h != (int)Math.Round(w * 9.0 / 20.0, MidpointRounding.AwayFromZero))
                { derived = false; derivedFail ??= $"{w} -> {h}"; break; }
            }
            Assert("CropHeightFor is the rounded 20:9 height at every width 320..4000",
                derived, derivedFail);
        }

        // ====================================================================
        // 11. BATCH IMPORT
        // ====================================================================
        // PR 5b. "Import All" is a loop over the functions the sections above
        // already prove — FindCandidates for the checks, BuildRoomBackground
        // for the pixels, NewRoomFlow.Create for the registration. What is new,
        // and what this section covers, is the two pieces of judgement wrapped
        // round that loop:
        //
        //   the PARTITION  which files go in with no decision, and the exact
        //                  reason each of the others is named as a skip
        //   the SUMMARY    what the status bar says afterwards
        //
        // Both are plain data in and a string or a list out, so both are fully
        // checkable here. The decode and the encode are the owner's smoke test,
        // as they are for every other route into the import.
        // ====================================================================

        /// <summary>An ImportCandidate as FindCandidates would have built it.</summary>
        // Synthesised rather than written to disk and scanned: section 6
        // already proves FindCandidates fills these fields correctly, and what
        // is under test here is what PlanBatch does with them. Deriving the
        // names through NewRoomFlow keeps the labels honest anyway.
        private static ImportCandidate Synth(string fileName, int w, int h, string? problem = null)
        {
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            var candidate = new ImportCandidate
            {
                SourcePath = fileName,
                FileName = fileName,
                BaseName = baseName,
                SourceWidth = w,
                SourceHeight = h,
                Multiple = ImageImport.ExactMultiple(w, h),
                Problem = problem,
            };

            // FindCandidates stops at an illegal base name and derives NOTHING
            // from it — there is no asset name, so no id and no display name.
            // Mirrored here because that empty id is exactly what makes the
            // batch summary fall back to the filename, and a fixture that
            // derived one anyway would test a state the editor cannot produce.
            if (!ImageImport.IsLegalBaseName(baseName)) return candidate;

            var derived = NewRoomFlow.MakeCandidate(NewRoomFlow.AssetNameFor(baseName));
            candidate.BackgroundAsset = derived.BackgroundAsset;
            candidate.RoomId = derived.RoomId;
            candidate.DisplayName = derived.DisplayName;
            return candidate;
        }

        private static void CheckBatch(string contentDir, string dataDir)
        {
            Section("11. BATCH — the eligibility partition, the skips, the summary");

            CheckBatchRegion();
            CheckBatchPartition();
            CheckBatchSummary();
            CheckBatchNeedsAReload(contentDir, dataDir);
        }

        // ---- which region a batch would cut ---------------------------------

        private static void CheckBatchRegion()
        {
            var none = (Rectangle?)null;

            Assert("320x144 batches as the whole image",
                ImageImport.BatchRegionFor(320, 144, none, out _) == new Rectangle(0, 0, 320, 144));
            Assert("640x288 batches as the whole image (2x, no crop step)",
                ImageImport.BatchRegionFor(640, 288, none, out _) == new Rectangle(0, 0, 640, 288));

            var frame = ImageImport.BatchRegionFor(384, 270, none, out var frameOrigin);
            Assert("384x270 batches on the built-in preset",
                frame == ImageImport.CpcFrameCrop
                && frameOrigin == ImageImport.CropPresetOrigin.BuiltIn, frame?.ToString() ?? "null");

            Assert("700x500 with no preset does not batch — it needs a decision",
                ImageImport.BatchRegionFor(700, 500, none, out _) == null);

            var mine = new Rectangle(10, 20, 640, 288);
            var stored = ImageImport.BatchRegionFor(700, 500, mine, out var storedOrigin);
            Assert("  and with one stored, it does",
                stored == mine && storedOrigin == ImageImport.CropPresetOrigin.Stored,
                stored?.ToString() ?? "null");

            // Branch-order parity with the single import. RunImport tests
            // ExactMultiple FIRST and only then reaches the crop step, so a
            // preset stored against an exact-multiple size must not divert the
            // batch into cropping what the click would have taken whole.
            var multipleWithPreset = ImageImport.BatchRegionFor(640, 288, new Rectangle(0, 0, 320, 144), out var mOrigin);
            Assert("an exact multiple ignores a preset for its size, as the single import does",
                multipleWithPreset == new Rectangle(0, 0, 640, 288)
                && mOrigin == ImageImport.CropPresetOrigin.None,
                multipleWithPreset?.ToString() ?? "null");

            Assert("a source smaller than a room never batches",
                ImageImport.BatchRegionFor(100, 50, none, out _) == null);
            Assert("  not even with a preset stored against that size",
                ImageImport.BatchRegionFor(100, 50, new Rectangle(0, 0, 320, 144), out _) == null);
            Assert("an unreadable size never batches",
                ImageImport.BatchRegionFor(0, 0, none, out _) == null);
        }

        // ---- the partition ---------------------------------------------------

        private static void CheckBatchPartition()
        {
            var candidates = new List<ImportCandidate>
            {
                Synth("FrameA.png", 384, 270),                       // built-in preset
                Synth("FrameB.png", 384, 270),                       // built-in preset
                Synth("Double.png", 640, 288),                       // exact 2x
                Synth("Exact.png", 320, 144),                        // exact 1x
                Synth("Odd.png", 700, 500),                          // no preset yet
                Synth("Taken.png", 384, 270, "room id 'taken' already exists"),
                Synth("Bad Name.png", 384, 270, "rename the file — the name may hold only letters..."),
                Synth("Headless.png", 0, 0),                         // no Problem, unreadable size
            };

            var plan = ImageImport.PlanBatch(candidates, (w, h) => null);

            Assert("four files import with no decision", plan.Eligible.Count == 4,
                $"{plan.Eligible.Count}: {string.Join(", ", EligibleNames(plan))}");
            Assert("  and four are named as skips", plan.Skipped.Count == 4,
                $"{plan.Skipped.Count}: {string.Join(", ", SkipLabels(plan))}");
            Assert("  so the batch is offered", plan.Offered);
            Assert("every candidate lands on exactly one side",
                plan.Eligible.Count + plan.Skipped.Count == candidates.Count);

            var frameA = FindEntry(plan, "FrameA.png");
            Assert("FrameA.png carries the built-in crop",
                frameA != null && frameA.Region == ImageImport.CpcFrameCrop
                && frameA.Origin == ImageImport.CropPresetOrigin.BuiltIn,
                frameA?.Region.ToString() ?? "not eligible");

            var doubled = FindEntry(plan, "Double.png");
            Assert("Double.png carries the whole 640x288 image",
                doubled != null && doubled.Region == new Rectangle(0, 0, 640, 288),
                doubled?.Region.ToString() ?? "not eligible");

            // The picker's own refusals are restated verbatim, so the summary
            // and the greyed-out row cannot say different things.
            AssertSkipMentions(plan, "taken", "already exists");
            AssertSkipMentions(plan, "Bad Name.png", "rename the file");
            AssertSkipMentions(plan, "odd", "no crop preset yet");
            AssertSkipMentions(plan, "odd", "import one of these on its own first");
            AssertSkipMentions(plan, "headless", "size could not be read");

            // The label is the room id where there is one, because that is what
            // would have been created — but a file refused for its NAME never
            // got an id, and there the filename is the only handle.
            Assert("a skip is labelled by its room id where there is one",
                SkipLabels(plan).Contains("taken"), string.Join(", ", SkipLabels(plan)));
            Assert("  and by its filename where the name itself was the problem",
                SkipLabels(plan).Contains("Bad Name.png"), string.Join(", ", SkipLabels(plan)));

            // Storing a preset for the awkward size moves exactly one file
            // across the line — which is the workflow the skip reason
            // recommends, so it had better be true.
            var withPreset = ImageImport.PlanBatch(candidates,
                (w, h) => w == 700 && h == 500 ? new Rectangle(0, 92, 700, 315) : null);
            Assert("storing a 700x500 preset makes Odd.png eligible",
                withPreset.Eligible.Count == 5 && withPreset.Skipped.Count == 3,
                $"{withPreset.Eligible.Count} eligible, {withPreset.Skipped.Count} skipped");
            var odd = FindEntry(withPreset, "Odd.png");
            Assert("  cutting the stored rectangle",
                odd != null && odd.Region == new Rectangle(0, 92, 700, 315)
                && odd.Origin == ImageImport.CropPresetOrigin.Stored,
                odd?.Region.ToString() ?? "not eligible");

            // One ready file is not a batch: pressing A would be a slower way
            // to click the row already in front of you.
            var lonely = ImageImport.PlanBatch(
                new List<ImportCandidate> { Synth("FrameA.png", 384, 270), Synth("Odd.png", 700, 500) },
                (w, h) => null);
            Assert($"one eligible file does not offer a batch (MinBatchSize {ImageImport.MinBatchSize})",
                lonely.Eligible.Count == 1 && !lonely.Offered);

            var empty = ImageImport.PlanBatch(new List<ImportCandidate>(), (w, h) => null);
            Assert("an empty folder plans an empty batch",
                !empty.Offered && empty.Eligible.Count == 0 && empty.Skipped.Count == 0);
        }

        private static List<string> EligibleNames(ImageImport.BatchPlan plan)
        {
            var names = new List<string>();
            foreach (var e in plan.Eligible) names.Add(e.Candidate.FileName);
            return names;
        }

        private static List<string> SkipLabels(ImageImport.BatchPlan plan)
        {
            var labels = new List<string>();
            foreach (var s in plan.Skipped) labels.Add(s.Label);
            return labels;
        }

        private static ImageImport.BatchEntry? FindEntry(ImageImport.BatchPlan plan, string fileName)
        {
            foreach (var e in plan.Eligible)
                if (string.Equals(e.Candidate.FileName, fileName, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        private static void AssertSkipMentions(ImageImport.BatchPlan plan, string label, string fragment)
        {
            foreach (var s in plan.Skipped)
            {
                if (!string.Equals(s.Label, label, StringComparison.OrdinalIgnoreCase)) continue;
                Assert($"{label} skipped: \"...{fragment}...\"",
                    s.Reason.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0, s.Reason);
                return;
            }
            Assert($"{label} skipped: \"...{fragment}...\"", false, "not in the skip list");
        }

        // ---- the summary -----------------------------------------------------

        private static void CheckBatchSummary()
        {
            var nothing = new List<ImageImport.BatchSkip>();
            string clean = ImageImport.SummariseBatch(4, nothing, aborted: false);
            Assert("a clean run counts what went in",
                clean.StartsWith("Import All: imported 4, skipped 0.", StringComparison.Ordinal), clean);
            Assert("  and says the game needs a rebuild to see them",
                clean.Contains("Rebuild the game", StringComparison.Ordinal), clean);

            string nothingAtAll = ImageImport.SummariseBatch(0, nothing, aborted: false);
            Assert("a run that imported nothing does not ask for a rebuild",
                !nothingAtAll.Contains("Rebuild the game", StringComparison.Ordinal), nothingAtAll);

            var two = new List<ImageImport.BatchSkip>
            {
                new("chateau_4", "room id 'chateau_4' already exists"),
                new("Odd.png", "700x500 has no crop preset yet"),
            };
            string listed = ImageImport.SummariseBatch(3, two, aborted: false);
            Assert("skips are named with their reasons",
                listed.Contains("chateau_4 (room id 'chateau_4' already exists)", StringComparison.Ordinal)
                && listed.Contains("Odd.png (700x500 has no crop preset yet)", StringComparison.Ordinal), listed);

            // The status bar is one line, so a long tail is capped — but it is
            // COUNTED, never dropped silently. "and 3 more" is the difference
            // between a cap and a lie.
            var many = new List<ImageImport.BatchSkip>();
            for (int i = 0; i < ImageImport.BatchSummaryListLimit + 3; i++)
                many.Add(new ImageImport.BatchSkip($"room_{i}", "reason"));
            string capped = ImageImport.SummariseBatch(1, many, aborted: false);
            Assert($"a long skip list stops after {ImageImport.BatchSummaryListLimit}",
                Occurrences(capped, "(reason)") == ImageImport.BatchSummaryListLimit, capped);
            Assert("  and says how many it did not name",
                capped.Contains("and 3 more", StringComparison.Ordinal), capped);
            Assert("  while the total count is still exact",
                capped.Contains($"skipped {many.Count}", StringComparison.Ordinal), capped);

            string stopped = ImageImport.SummariseBatch(2, two, aborted: true);
            Assert("a stopped run says so rather than reading as a clean finish",
                stopped.StartsWith("Import All stopped:", StringComparison.Ordinal), stopped);
        }

        // ---- why the loop must reload the registry ---------------------------

        private static void CheckBatchNeedsAReload(string contentDir, string dataDir)
        {
            // NewRoomFlow.Create builds the new registry from RoomManifest.All,
            // which is a cached Lazy. Two Creates without a Reload between them
            // therefore both start from the SAME nine rooms, and the second
            // write silently drops the first room. The editor's batch avoids
            // this only because it goes through CreateAndOpenRoom, which
            // reloads after every file.
            //
            // That is a real hazard hiding behind a convenience method, so it
            // is asserted rather than trusted: if Create ever stops depending
            // on the cache, this check says so and the batch can be simplified.
            string scratchRooms = Path.Combine(dataDir, "rooms.json");
            if (!File.Exists(scratchRooms))
            {
                Assert("section 7 left a scratch rooms.json to work from", false, "missing");
                return;
            }

            // Section 7 already created chateau_3 here.
            Assert("the scratch registry currently holds section 7's room",
                File.ReadAllText(scratchRooms).Contains("\"id\": \"chateau_3\"", StringComparison.Ordinal));

            var second = NewRoomFlow.MakeCandidate(NewRoomFlow.AssetNameFor("Chateau4"));
            var result = NewRoomFlow.Create(second, contentDir, dataDir);
            Assert("a second Create succeeds on its own terms", result.Ok, result.Message);

            string after = File.ReadAllText(scratchRooms);
            Assert("  but without a registry reload it drops the first room",
                after.Contains("\"id\": \"chateau_4\"", StringComparison.Ordinal)
                && !after.Contains("\"id\": \"chateau_3\"", StringComparison.Ordinal),
                "Create no longer reads the cached registry — the batch's reload may be redundant now");
            Assert("  which is exactly why the batch goes through CreateAndOpenRoom",
                EntryRows(after) == RoomManifest.All.Count + 1, EntryRows(after).ToString());
        }

        // ====================================================================
        // 8. IMAGE HEADERS
        // ====================================================================

        private static void CheckImageHeaders(string scratch, string repoRoot)
        {
            Section("8. IMAGE HEADERS — dimensions without decoding");

            // A real PNG from the repo. The one file in this harness that is
            // not synthetic, and the only proof the reader agrees with an
            // actual encoder.
            string realPng = Path.Combine(repoRoot, "Content", "RoomBG_Chateau0.png");
            if (File.Exists(realPng))
            {
                bool ok = ImageImport.TryReadImageSize(realPng, out int w, out int h);
                Assert("a real repo PNG reads as 320x144", ok && w == 320 && h == 144, $"{w}x{h}");
            }
            else
            {
                Assert("Content/RoomBG_Chateau0.png is present to read", false, realPng);
            }

            string dir = Path.Combine(scratch, "headers");
            Directory.CreateDirectory(dir);

            string png = Path.Combine(dir, "stub.png");
            WritePngStub(png, 1234, 567);
            AssertSize("a synthesised PNG header", png, 1234, 567);

            string jpg = Path.Combine(dir, "stub.jpg");
            WriteJpegStub(jpg, 700, 500);
            AssertSize("a JPEG with APP0/APP1/DQT before the frame", jpg, 700, 500);

            // The APP1 in the stub above carries a decoy SOI + SOF0 claiming
            // 8x8 — an EXIF thumbnail, in other words. Skipping segments by
            // their declared length is what stops that being read as the frame.
            bool decoyRejected = ImageImport.TryReadImageSize(jpg, out int dw, out int dh)
                                 && dw == 700 && dh == 500;
            Assert("  an embedded EXIF thumbnail is not mistaken for the frame", decoyRejected, $"{dw}x{dh}");

            string prog = Path.Combine(dir, "progressive.jpg");
            WriteJpegStub(prog, 960, 432, sofMarker: 0xC2);
            AssertSize("a progressive JPEG (SOF2)", prog, 960, 432);

            string garbage = Path.Combine(dir, "garbage.png");
            File.WriteAllText(garbage, "not an image at all, just some text");
            Assert("garbage is refused", !ImageImport.TryReadImageSize(garbage, out _, out _));

            string truncated = Path.Combine(dir, "truncated.jpg");
            File.WriteAllBytes(truncated, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 });
            Assert("a truncated JPEG is refused", !ImageImport.TryReadImageSize(truncated, out _, out _));

            Assert("a missing file is refused",
                !ImageImport.TryReadImageSize(Path.Combine(dir, "nope.png"), out _, out _));
        }

        private static void AssertSize(string label, string path, int expectW, int expectH)
        {
            bool ok = ImageImport.TryReadImageSize(path, out int w, out int h);
            Assert($"{label} reads as {expectW}x{expectH}", ok && w == expectW && h == expectH, $"{w}x{h}");
        }

        // ====================================================================
        // SYNTHETIC IMAGE STUBS
        // ====================================================================
        // Header-only files. TryReadImageSize never decodes, so a valid header
        // followed by nothing is all it takes to test it — and it means the
        // harness needs no image encoder and no committed binary fixtures.
        // ====================================================================

        private static void WritePngStub(string path, int width, int height)
        {
            var bytes = new List<byte>();
            bytes.AddRange(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });  // signature
            bytes.AddRange(BigEndian32(13));                                                 // IHDR length
            bytes.AddRange(new byte[] { (byte)'I', (byte)'H', (byte)'D', (byte)'R' });
            bytes.AddRange(BigEndian32(width));
            bytes.AddRange(BigEndian32(height));
            bytes.AddRange(new byte[] { 8, 6, 0, 0, 0 });                                    // depth, colour type, ...
            File.WriteAllBytes(path, bytes.ToArray());
        }

        private static void WriteJpegStub(string path, int width, int height, byte sofMarker = 0xC0)
        {
            var bytes = new List<byte>();
            bytes.AddRange(new byte[] { 0xFF, 0xD8 });                        // SOI

            // APP0 / JFIF
            bytes.AddRange(new byte[] { 0xFF, 0xE0, 0x00, 0x10 });
            bytes.AddRange(new byte[] { (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0 });
            bytes.AddRange(new byte[] { 1, 1, 0, 0, 1, 0, 1, 0, 0 });

            // APP1 holding a decoy: a complete little SOI + SOF0 claiming 8x8,
            // which is what an EXIF thumbnail looks like from the outside. A
            // reader that scanned for the next 0xFFC0 instead of skipping by
            // length would read 8x8 and be wrong.
            var decoy = new List<byte> { 0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x11, 8 };
            decoy.AddRange(BigEndian16(8));
            decoy.AddRange(BigEndian16(8));
            decoy.AddRange(new byte[] { 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1 });
            bytes.AddRange(new byte[] { 0xFF, 0xE1 });
            bytes.AddRange(BigEndian16(decoy.Count + 2));
            bytes.AddRange(decoy);

            // DQT — one more length-skipped segment before the real frame.
            bytes.AddRange(new byte[] { 0xFF, 0xDB });
            bytes.AddRange(BigEndian16(67));
            bytes.Add(0);
            for (int i = 0; i < 64; i++) bytes.Add(16);

            // Fill bytes are legal ahead of a marker; the reader must eat them.
            bytes.AddRange(new byte[] { 0xFF, 0xFF, sofMarker });
            bytes.AddRange(BigEndian16(17));
            bytes.Add(8);                                                     // sample precision
            bytes.AddRange(BigEndian16(height));
            bytes.AddRange(BigEndian16(width));
            bytes.AddRange(new byte[] { 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1 });

            File.WriteAllBytes(path, bytes.ToArray());
        }

        private static byte[] BigEndian32(int v) =>
            new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

        private static byte[] BigEndian16(int v) => new[] { (byte)(v >> 8), (byte)v };

        // ====================================================================
        // PLUMBING
        // ====================================================================

        private static void Section(string title)
        {
            Console.WriteLine($"  {title}");
        }

        private static void Assert(string label, bool ok, string? detail = null)
        {
            _checks++;
            if (!ok) _failures++;
            string suffix = ok || string.IsNullOrEmpty(detail) ? "" : $"   [{detail}]";
            Console.WriteLine($"    {(ok ? "ok  " : "FAIL")} {label}{suffix}");
        }

        private static bool Throws(Action action)
        {
            try { action(); return false; }
            catch (ArgumentException) { return true; }
        }

        private static bool SequenceEqual(Color[] a, Color[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // ====================================================================
        // SCRATCH DIRECTORY — SAFETY AND SETUP
        // ====================================================================
        // Same contract as tools/RoundTrip: the harness must not disturb the
        // tree it is measuring. The scratch path may not be, contain, or sit
        // inside the repository, and the clean refuses anything it did not
        // write rather than recursing.
        // ====================================================================

        private static void AssertScratchSafe(string scratch, string repoRoot)
        {
            string s = Canonical(scratch);
            string root = Canonical(repoRoot);

            if (s == root || IsUnder(s, root) || IsUnder(root, s))
                throw new InvalidOperationException(
                    $"'{scratch}' is, sits inside, or contains the repository root '{repoRoot}'. " +
                    "The harness must never write into the source tree.");
        }

        // The scratch tree this harness builds is import/ + Content/ + data/ +
        // headers/, all of them ours. Anything else means the directory belongs
        // to someone: refuse rather than delete it.
        private static readonly string[] OwnedSubdirectories = { "import", "Content", "data", "headers", "settings" };

        private static void CleanScratch(string scratch)
        {
            if (!Directory.Exists(scratch)) { Directory.CreateDirectory(scratch); return; }

            var stray = new List<string>();
            foreach (string path in Directory.GetFileSystemEntries(scratch))
            {
                string name = Path.GetFileName(path);
                bool ours = Directory.Exists(path) && Array.IndexOf(OwnedSubdirectories, name) >= 0;
                if (!ours) stray.Add(name);
            }

            if (stray.Count > 0)
                throw new InvalidOperationException(
                    $"'{scratch}' holds entries this harness did not write ({string.Join(", ", stray)}). " +
                    "Point --out at an empty or harness-owned directory.");

            foreach (string name in OwnedSubdirectories)
            {
                string path = Path.Combine(scratch, name);
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            }
        }

        private static string Canonical(string path) =>
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        private static bool IsUnder(string child, string parent) =>
            child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
