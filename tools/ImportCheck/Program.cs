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
//   6 candidates  a scratch import folder: which files are offered, and the
//                 exact reason each refused one is refused
//   7 creation    Create against a scratch tree: collision grid, an
//                 append-only .mgcb edit, one new rooms.json row
//   8 headers     PNG and JPEG dimension reading, including a real repo PNG
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
            WritePngStub(Path.Combine(importDir, "Ghost.png"), 320, 144);
            File.WriteAllText(Path.Combine(importDir, "NotAnImage.png"), "this is not an image");
            File.WriteAllText(Path.Combine(importDir, "notes.txt"), "ignored: wrong extension");

            // The target this scratch Content/ already holds, so Ghost.png has
            // somewhere to collide with.
            WritePngStub(Path.Combine(contentDir, "RoomBG_Ghost.png"), 320, 144);

            var found = ImageImport.FindCandidates(importDir, contentDir, RoomManifest.All);

            Assert("only image extensions are listed (notes.txt ignored)", found.Count == 8, $"{found.Count} listed");

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

            AssertProblemMentions(found, "Bad Name.png", "rename the file");
            AssertProblemMentions(found, "Chateau0.png", "already exists");
            AssertProblemMentions(found, "Room1.png", "reserved");
            AssertProblemMentions(found, "Odd.png", "not 320x144 or an exact multiple");
            AssertProblemMentions(found, "Ghost.png", "already exists in Content/");
            AssertProblemMentions(found, "NotAnImage.png", "could not read the image size");
            AssertProblemMentions(found, "Chateau3.png", "already derives");

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
        private static readonly string[] OwnedSubdirectories = { "import", "Content", "data", "headers" };

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
