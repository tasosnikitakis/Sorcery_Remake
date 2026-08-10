// ============================================================================
// ROUNDTRIP — SORCERYFORGE SAVE-PATH REGRESSION HARNESS
// Sorcery+ Remake
// ============================================================================
// THE INVARIANT IT GUARDS
//
//   "Load every manifest room in SorceryForge, save each one untouched:
//    `git diff` shows nothing and `git status` shows nothing new."
//
//   That property is what makes an editor diff reviewable. If an untouched
//   save reformats a file or creates a new one, real edits drown in noise and
//   nobody can tell a data change from a serialiser change. This harness is
//   the machine-checkable form of that sentence, so any PR touching the save
//   path, the loaders, or room registration can prove it still holds — without
//   a human clicking through nine rooms.
//
// WHAT IT DOES
//
//   1. Copies every content_*.json / layout_*.json out of assets/data into a
//      scratch directory. That copy IS the model of the working tree, and
//      seeding it matters: the loaders' "don't CREATE an empty file" rule
//      turns on whether the TARGET file already exists, so a harness saving
//      into an empty directory would take different branches than the editor
//      and prove nothing.
//   2. For every room in Rooms/RoomManifest.All, performs the same load->save
//      round trip the editor performs on open-then-Ctrl+S-without-touching:
//
//        RoomContentLoader.TryLoad + RoomMeta.LoadDoorsFor        (LoadRoom)
//          -> EditorState.LoadFromRoomContent                     (LoadRoom)
//          -> EditorState.ToRoomContent / ToRoomLayoutJson        (SaveCurrentRoom)
//          -> RoomContentLoader.Save / RoomLayoutLoader.Save      (SaveCurrentRoom)
//
//      ...with the writes landing in the scratch copy, never in assets/data.
//   3. Diffs the scratch copy against assets/data — the stand-in for
//      `git diff` (content changes) plus `git status` (new files).
//
//   Before all that it runs a SELF-TEST of the empty-file rule against
//   synthetic room IDs, so the harness proves the rule directly instead of
//   only observing its effects. See RunSelfTest.
//
// VERDICTS
//
//   identical / eol-only  file came back the same. eol-only means line
//                         endings differ, which git normalises away here
//                         (core.autocrlf=true) — reported, not a violation.
//   changed               an existing file came back different.  VIOLATION
//   extra                 a file was created that assets/data lacks. This is
//                         the born-empty-file failure.                VIOLATION
//   not-rewritten         a file that EXISTS was not written. The rule is
//                         "don't CREATE empty files", never "don't WRITE
//                         them" — an emptied room must still be flushed or
//                         the user's deletions vanish.                VIOLATION
//   missing               a file that exists in assets/data is gone from the
//                         scratch copy afterwards.                    VIOLATION
//   skipped               no file before, none after: the correct no-op for a
//                         room that is genuinely empty.
//
// WHAT IT DOES NOT COVER
//
//   SaveCurrentRoom steps 3 and 4 (collision grid, background PNG) are gated
//   behind the CollisionDirty / BackgroundDirty flags, which an untouched
//   load-then-save never sets. Out of scope by construction. If a future
//   change made either fire unconditionally, the extra-file sweep would catch
//   the collision JSON appearing in the scratch directory.
//
// HOW TO RUN
//
//   dotnet build tools/RoundTrip/RoundTrip.csproj
//   dotnet run   --project tools/RoundTrip/RoundTrip.csproj
//   dotnet run   --project tools/RoundTrip/RoundTrip.csproj -- --out <dir>
//
//   Exit 0 = invariant holds. Exit 1 = violations (listed above the summary).
//   Exit 2 = could not run (bad arguments, unsafe --out, repo root not found,
//            unreadable or invalid assets/data/rooms.json).
//   The scratch directory is left in place afterwards for manual diffing.
//
// HOW TO COMPARE AGAINST main
//
//   Prove this branch's save path produces byte-identical output to main's:
//
//     git checkout main
//     dotnet run --project tools/RoundTrip/RoundTrip.csproj -- --out %TEMP%\rt-main
//     git checkout -
//     dotnet run --project tools/RoundTrip/RoundTrip.csproj -- --out %TEMP%\rt-head
//     git diff --no-index %TEMP%\rt-main %TEMP%\rt-head
//
//   An empty diff means nothing about the written bytes changed. Note the two
//   runs also seed from their own checkout of assets/data, so a data-only
//   commit shows up here too — read the diff, don't just look at the exit
//   code. (Commit or stash your work before switching branches.)
//
// SAFETY
//
//   The harness NEVER writes into the repository's assets/data. The scratch
//   directory is validated before anything is written (AssertScratchSafe) and
//   the pre-run clean only ever deletes *.json — pointed at a directory
//   holding anything else, it refuses rather than deleting a stranger's work.
// ============================================================================

using Microsoft.Xna.Framework;
using SorceryForge;
using SorceryRemake.Core;
using SorceryRemake.Rooms;
using System;
using System.Collections.Generic;
using System.IO;

namespace SorceryRemake.Tools.RoundTrip
{
    internal enum Verdict
    {
        Identical,      // byte-for-byte match against assets/data
        EolOnly,        // same content, different line endings (git normalises)
        Changed,        // real content difference               -> violation
        Extra,          // created; assets/data has no such file -> violation
        NotRewritten,   // exists, but the save declined to write it -> violation
        Missing,        // exists in assets/data, gone afterwards -> violation
        SkippedOk,      // no file before, none after: correct no-op
    }

    internal class FileResult
    {
        public string RoomId = "";
        public string Kind = "";        // "content" | "layout"
        public string FileName = "";
        public Verdict Verdict;
        public string Detail = "";

        public bool IsViolation =>
            Verdict == Verdict.Changed || Verdict == Verdict.Extra ||
            Verdict == Verdict.NotRewritten || Verdict == Verdict.Missing;
    }

    internal static class Program
    {
        private const string DefaultScratchName = "sorcery-roundtrip";

        // Synthetic room IDs used only by the self-test. Never in the manifest,
        // never on disk in assets/data.
        private const string SelfTestNewRoom = "__roundtrip_never_saved__";
        private const string SelfTestOldRoom = "__roundtrip_emptied__";

        private static int Main(string[] args)
        {
            string? outArg = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--out":
                        if (i + 1 >= args.Length) { Console.Error.WriteLine("--out needs a directory"); return 2; }
                        outArg = args[++i];
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

            string repoRoot = EditorPaths.RepoRoot;
            string sourceDir = EditorPaths.RepoAssetsDataDir;
            string scratch = Path.GetFullPath(outArg ?? Path.Combine(Path.GetTempPath(), DefaultScratchName));

            // A repo root that fell back to the executable's own directory
            // means EditorPaths' walk-up failed — refuse, rather than silently
            // round-tripping an empty world and declaring success.
            if (!File.Exists(Path.Combine(repoRoot, "SorceryRemake.csproj")))
            {
                Console.Error.WriteLine($"could not locate the repo root (got '{repoRoot}').");
                Console.Error.WriteLine("run this from inside the source tree, not a published build.");
                return 2;
            }

            Console.WriteLine("RoundTrip — SorceryForge save-path regression harness");
            Console.WriteLine($"  repo    : {repoRoot}");
            Console.WriteLine($"  source  : {sourceDir}");
            Console.WriteLine($"  scratch : {scratch}");

            int selfTestFailures;
            var seeded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // FIRST touch of the registry, and it belongs inside the guard.
                // RoomManifest.All is a Lazy<T> that throws our own
                // InvalidOperationException for a missing / malformed / invalid
                // rooms.json. Read outside the try and a broken registry ends
                // the run with an unhandled stack trace; read here and it exits
                // 2 with the documented "refusing to run: <reason>" message,
                // like every other could-not-run case.
                Console.WriteLine($"  rooms   : {RoomManifest.All.Count} (RoomManifest.All)");
                Console.WriteLine();

                AssertScratchSafe(scratch, repoRoot, sourceDir);

                CleanScratch(scratch);
                selfTestFailures = RunSelfTest(scratch);

                CleanScratch(scratch);
                seeded = SeedScratch(scratch, sourceDir);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("refusing to run: " + ex.Message);
                return 2;
            }

            Console.WriteLine($"  seeded {seeded.Count} file(s) from assets/data into the scratch copy");
            Console.WriteLine();

            var results = new List<FileResult>();
            foreach (var manifest in RoomManifest.All)
                results.AddRange(RoundTripRoom(manifest.RoomId, sourceDir, scratch));

            results.AddRange(SweepForStrays(results, seeded, scratch));

            Report(results, selfTestFailures, scratch);

            int violations = selfTestFailures;
            foreach (var r in results) if (r.IsViolation) violations++;
            return violations == 0 ? 0 : 1;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("usage: dotnet run --project tools/RoundTrip/RoundTrip.csproj -- [--out <dir>]");
            Console.WriteLine();
            Console.WriteLine("  --out <dir>  scratch directory to work in (cleared and re-seeded each run).");
            Console.WriteLine($"               default: %TEMP%\\{DefaultScratchName}");
            Console.WriteLine();
            Console.WriteLine("exit 0 = round-trip invariant holds; 1 = violations found; 2 = could not run");
            Console.WriteLine("        (bad arguments, unsafe --out, repo root not found, bad rooms.json).");
        }

        // ====================================================================
        // SELF-TEST — the empty-file rule, stated as assertions
        // ====================================================================
        // The main sweep can only observe the rule's effects on real data.
        // These cases pin the rule itself, including the asymmetry that is
        // easy to get backwards: "don't CREATE an empty file" is NOT the same
        // as "don't WRITE an empty file". Getting it backwards means a user
        // who deletes every entity in a room and saves loses the deletion.
        //
        // The last case pins the OTHER half of the rule — what "empty" means.
        // Every field of a layout/content DTO that holds authored room data
        // has to count, or a room whose only content is that field never gets
        // a file. Add a field to either DTO, add a case here.
        //
        // Runs against synthetic room IDs inside the scratch directory, so it
        // touches no real data and needs none.
        // ====================================================================

        private static int RunSelfTest(string scratch)
        {
            int failures = 0;
            Console.WriteLine("  SELF-TEST — empty-file rule");

            // ---- content ----
            string newContent = RoomContentLoader.GetPath(SelfTestNewRoom, scratch);
            string oldContent = RoomContentLoader.GetPath(SelfTestOldRoom, scratch);

            // 1. empty + no file yet -> nothing created.
            failures += Assert("content: empty room with no file creates nothing",
                RoomContentLoader.Save(SelfTestNewRoom, new RoomContent(), scratch) == false
                && !File.Exists(newContent));

            // 2. non-empty -> file written (baseline for case 3).
            var populated = new RoomContent();
            populated.Items.Add(new ItemSpawn($"{SelfTestOldRoom}_sword_1", ItemType.Sword, new Vector2(8, 8)));
            failures += Assert("content: non-empty room writes a file",
                RoomContentLoader.Save(SelfTestOldRoom, populated, scratch) == true
                && File.Exists(oldContent));

            // 3. THE ASYMMETRY: empty + file exists -> written anyway, and the
            //    entity really is gone from disk afterwards.
            bool wroteEmpty = RoomContentLoader.Save(SelfTestOldRoom, new RoomContent(), scratch);
            var reread = RoomContentLoader.TryLoad(SelfTestOldRoom, scratch);
            failures += Assert("content: emptied room STILL writes (deletion persists)",
                wroteEmpty && File.Exists(oldContent) && reread != null && reread.Items.Count == 0);

            // ---- layout ----
            string newLayout = RoomLayoutLoader.GetPath(SelfTestNewRoom, scratch);
            string oldLayout = RoomLayoutLoader.GetPath(SelfTestOldRoom, scratch);

            failures += Assert("layout: doorless room with no file creates nothing",
                RoomLayoutLoader.Save(new RoomLayoutJson { roomId = SelfTestNewRoom }, scratch) == false
                && !File.Exists(newLayout));

            var withDoor = new RoomLayoutJson { roomId = SelfTestOldRoom };
            withDoor.doors.Add(new DoorEntryJson
            {
                id = $"{SelfTestOldRoom}_door_1",
                x = 0,
                y = 112,
                type = "RightOpening",
                targetRoom = "chateau_0",
                targetDoor = "chateau0_door_topright",
            });
            failures += Assert("layout: room with a door writes a file",
                RoomLayoutLoader.Save(withDoor, scratch) == true && File.Exists(oldLayout));

            bool wroteEmptyLayout = RoomLayoutLoader.Save(new RoomLayoutJson { roomId = SelfTestOldRoom }, scratch);
            var rereadLayout = RoomLayoutLoader.TryLoad(SelfTestOldRoom, scratch);
            failures += Assert("layout: emptied room STILL writes (deletion persists)",
                wroteEmptyLayout && File.Exists(oldLayout)
                && rereadLayout != null && rereadLayout.doors.Count == 0);

            // 4. "Empty" means no doors AND no spawn. A doorless room whose
            //    only authored content is a player spawn is NOT empty, and
            //    must get a file — miss this and the spawn is silently
            //    dropped the moment it is saved into a room with no doors.
            //    File.Delete first so this tests the CREATE branch, which is
            //    the one that would refuse.
            File.Delete(newLayout);
            var spawnOnly = new RoomLayoutJson
            {
                roomId = SelfTestNewRoom,
                playerSpawn = new PlayerSpawnJson { x = 32, y = 64 },
            };
            bool wroteSpawnOnly = RoomLayoutLoader.Save(spawnOnly, scratch);
            var rereadSpawn = RoomLayoutLoader.TryLoad(SelfTestNewRoom, scratch);
            failures += Assert("layout: doorless room with only a spawn DOES write",
                wroteSpawnOnly && File.Exists(newLayout)
                && rereadSpawn?.playerSpawn != null
                && rereadSpawn.playerSpawn.x == 32 && rereadSpawn.playerSpawn.y == 64);

            Console.WriteLine();
            return failures;
        }

        private static int Assert(string label, bool ok)
        {
            Console.WriteLine($"    {(ok ? "ok  " : "FAIL")} {label}");
            return ok ? 0 : 1;
        }

        // ====================================================================
        // THE ROUND TRIP
        // ====================================================================
        // Deliberately mirrors EditorGame.LoadRoom / EditorGame.SaveCurrentRoom
        // step for step. If either of those changes shape, change this too —
        // a harness that tests a parallel implementation tests nothing.
        // ====================================================================

        private static List<FileResult> RoundTripRoom(string roomId, string sourceDir, string scratch)
        {
            var results = new List<FileResult>();

            // --- LoadRoom: content + doors + spawn from disk into the working set ---
            var content = RoomContentLoader.TryLoad(roomId, sourceDir);
            var (doors, spawn) = RoomMeta.LoadLayoutFor(roomId);

            var state = new EditorState();
            state.LoadFromRoomContent(content ?? new RoomContent(), doors, spawn);

            // --- SaveCurrentRoom step 1: content ---
            string contentName = Path.GetFileName(RoomContentLoader.GetPath(roomId, scratch));
            bool wroteContent = RoomContentLoader.Save(roomId, state.ToRoomContent(), scratch);
            results.Add(Classify(roomId, "content", contentName, wroteContent, sourceDir, scratch));

            // --- SaveCurrentRoom step 2: layout (doors) ---
            var layout = state.ToRoomLayoutJson(roomId);
            string layoutName = Path.GetFileName(RoomLayoutLoader.GetPath(roomId, scratch));
            bool wroteLayout = RoomLayoutLoader.Save(layout, scratch);
            results.Add(Classify(roomId, "layout", layoutName, wroteLayout, sourceDir, scratch));

            return results;
        }

        // ====================================================================
        // CLASSIFICATION
        // ====================================================================

        private static FileResult Classify(string roomId, string kind, string fileName,
                                           bool wrote, string sourceDir, string scratch)
        {
            var r = new FileResult { RoomId = roomId, Kind = kind, FileName = fileName };

            string srcPath = Path.Combine(sourceDir, fileName);
            string outPath = Path.Combine(scratch, fileName);
            bool srcExists = File.Exists(srcPath);
            bool outExists = File.Exists(outPath);

            if (!srcExists)
            {
                if (!outExists)
                {
                    // Nothing before, nothing after — the correct no-op. Hold
                    // Save to its word while we're here.
                    r.Verdict = Verdict.SkippedOk;
                    if (wrote) { r.Verdict = Verdict.Missing; r.Detail = "Save() claimed a write but produced no file"; }
                    return r;
                }
                r.Verdict = Verdict.Extra;
                r.Detail = "created a file assets/data does not have";
                return r;
            }

            // From here the file EXISTS in assets/data.
            if (!wrote)
            {
                // The seeded copy is still sitting there untouched, so a byte
                // comparison would pass and hide this. Emptiness alone must
                // never suppress a write: if it did, deleting every entity in
                // a room and saving would leave the old file live on disk.
                r.Verdict = Verdict.NotRewritten;
                r.Detail = "file exists but Save() skipped it — a room emptied by the user would not persist";
                return r;
            }

            if (!outExists)
            {
                r.Verdict = Verdict.Missing;
                r.Detail = "file vanished from the scratch copy";
                return r;
            }

            byte[] a = File.ReadAllBytes(srcPath);
            byte[] b = File.ReadAllBytes(outPath);
            if (BytesEqual(a, b)) { r.Verdict = Verdict.Identical; return r; }

            string sa = File.ReadAllText(srcPath);
            string sb = File.ReadAllText(outPath);
            if (NormaliseEol(sa) == NormaliseEol(sb))
            {
                // git here runs core.autocrlf=true, so this is invisible to
                // `git diff` — worth reporting, not a violation.
                r.Verdict = Verdict.EolOnly;
                r.Detail = "line endings only";
                return r;
            }

            r.Verdict = Verdict.Changed;
            r.Detail = FirstDifference(sa, sb);
            return r;
        }

        /// <summary>
        /// Anything in the scratch directory that was neither seeded nor
        /// already accounted for by a room result. Catches files created for
        /// rooms outside the manifest, and any file kind the round trip is
        /// not supposed to touch at all (a collision JSON, say).
        /// </summary>
        private static List<FileResult> SweepForStrays(List<FileResult> results,
                                                       HashSet<string> seeded, string scratch)
        {
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in results) known.Add(r.FileName);

            var strays = new List<FileResult>();
            foreach (string path in Directory.GetFiles(scratch))
            {
                string name = Path.GetFileName(path);
                if (known.Contains(name) || seeded.Contains(name)) continue;
                strays.Add(new FileResult
                {
                    RoomId = "(unexpected)",
                    Kind = "-",
                    FileName = name,
                    Verdict = Verdict.Extra,
                    Detail = "written by the save path but not seeded and not a manifest room's file",
                });
            }
            return strays;
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static string NormaliseEol(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n");

        /// <summary>First line where the two files diverge.</summary>
        private static string FirstDifference(string expected, string actual)
        {
            string[] ea = NormaliseEol(expected).Split('\n');
            string[] aa = NormaliseEol(actual).Split('\n');
            int n = Math.Min(ea.Length, aa.Length);
            for (int i = 0; i < n; i++)
            {
                if (ea[i] == aa[i]) continue;
                return $"line {i + 1}: on disk [{ea[i]}] vs saved [{aa[i]}]";
            }
            return $"line count: on disk {ea.Length} vs saved {aa.Length}";
        }

        // ====================================================================
        // REPORT
        // ====================================================================

        private static void Report(List<FileResult> results, int selfTestFailures, string scratch)
        {
            int identical = 0, eol = 0, skipped = 0;
            var violations = new List<FileResult>();

            foreach (var r in results)
            {
                if (r.IsViolation) { violations.Add(r); continue; }
                switch (r.Verdict)
                {
                    case Verdict.Identical: identical++; break;
                    case Verdict.EolOnly: eol++; break;
                    case Verdict.SkippedOk: skipped++; break;
                }
            }

            Console.WriteLine("  ROUND TRIP — one line per room");
            foreach (var manifest in RoomManifest.All)
            {
                string line = "    " + manifest.RoomId.PadRight(18);
                foreach (var kind in new[] { "content", "layout" })
                {
                    string cell = "-";
                    foreach (var r in results)
                        if (r.RoomId == manifest.RoomId && r.Kind == kind) { cell = Describe(r.Verdict); break; }
                    line += $"{kind} {cell.PadRight(16)}";
                }
                Console.WriteLine(line);
            }

            Console.WriteLine();
            Console.WriteLine($"  identical {identical}   eol-only {eol}   correctly-skipped {skipped}" +
                              $"   self-test failures {selfTestFailures}   VIOLATIONS {violations.Count}");

            if (violations.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  VIOLATIONS");
                foreach (var v in violations)
                    Console.WriteLine($"    [{Describe(v.Verdict).ToUpperInvariant()}] {v.FileName} — {v.Detail}");
            }

            Console.WriteLine();
            Console.WriteLine(violations.Count == 0 && selfTestFailures == 0
                ? "  ROUND-TRIP INVARIANT HOLDS: loading and saving every manifest room untouched\n" +
                  "  leaves every existing file byte-identical and creates no new one."
                : "  ROUND-TRIP INVARIANT VIOLATED — see above.");

            Console.WriteLine($"\n  scratch left at {scratch}");
        }

        private static string Describe(Verdict v) => v switch
        {
            Verdict.Identical => "identical",
            Verdict.EolOnly => "eol-only",
            Verdict.Changed => "changed",
            Verdict.Extra => "extra",
            Verdict.NotRewritten => "not-rewritten",
            Verdict.Missing => "missing",
            Verdict.SkippedOk => "skipped",
            _ => "?"
        };

        // ====================================================================
        // SCRATCH DIRECTORY — SAFETY AND SETUP
        // ====================================================================
        // The whole point of the harness is that it does not disturb the tree
        // it is measuring. Two guards:
        //   1. AssertScratchSafe — the destination may not be, contain, or sit
        //      inside assets/data, and may not be or contain the repo root. A
        //      typo'd --out cannot overwrite real room data.
        //   2. CleanScratch — deletes only *.json. Pointed at a directory
        //      holding anything else (or any subdirectory), it refuses rather
        //      than recursively deleting a stranger's folder.
        // ====================================================================

        private static void AssertScratchSafe(string scratch, string repoRoot, string sourceDir)
        {
            string s = Canonical(scratch);
            string root = Canonical(repoRoot);
            string data = Canonical(sourceDir);

            if (s == data || IsUnder(s, data) || IsUnder(data, s))
                throw new InvalidOperationException(
                    $"'{scratch}' is (or overlaps) the repository's assets/data. " +
                    "The harness must never write real room data.");

            if (s == root || IsUnder(root, s))
                throw new InvalidOperationException(
                    $"'{scratch}' is (or contains) the repository root '{repoRoot}'.");
        }

        private static void CleanScratch(string scratch)
        {
            if (!Directory.Exists(scratch)) { Directory.CreateDirectory(scratch); return; }

            var stray = new List<string>();
            foreach (string path in Directory.GetFileSystemEntries(scratch))
            {
                string name = Path.GetFileName(path);
                bool ours = File.Exists(path) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                if (!ours) stray.Add(name);
            }

            if (stray.Count > 0)
                throw new InvalidOperationException(
                    $"'{scratch}' holds entries this harness did not write ({string.Join(", ", stray)}). " +
                    "Point --out at an empty or harness-owned directory.");

            foreach (string path in Directory.GetFiles(scratch)) File.Delete(path);
        }

        /// <summary>
        /// Copy the room JSON the editor reads and writes into the scratch
        /// directory. Collision JSON is deliberately NOT seeded: the round trip
        /// must not touch it, so if it ever appears there the stray sweep says
        /// so. Returns the set of seeded file names.
        /// </summary>
        private static HashSet<string> SeedScratch(string scratch, string sourceDir)
        {
            var seeded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in Directory.GetFiles(sourceDir, "*.json"))
            {
                string name = Path.GetFileName(path);
                if (!name.StartsWith("content_", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("layout_", StringComparison.OrdinalIgnoreCase))
                    continue;
                File.Copy(path, Path.Combine(scratch, name), overwrite: true);
                seeded.Add(name);
            }
            return seeded;
        }

        private static string Canonical(string path) =>
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        private static bool IsUnder(string child, string parent) =>
            child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
