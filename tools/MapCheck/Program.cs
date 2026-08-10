// ============================================================================
// MAPCHECK — SORCERYFORGE WORLD-MAP REGRESSION HARNESS
// Sorcery+ Remake
// ============================================================================
// THE INVARIANTS IT GUARDS
//
//   "The board is the same every time it is built from the same world; every
//    door in every room becomes exactly one arrow; and every arrow starts and
//    ends on the rooms and the doors it claims to, wearing the verdict the
//    Doors button would give it."
//
//   Determinism is the one worth stating twice. The map's whole value is that
//   a user learns where things are — a layout that reshuffles between sessions
//   is worse than no layout, because it teaches something false. So the BFS is
//   seeded and enqueued in registry order and its adjacency is symmetric, and
//   this harness runs it twice and demands the same answer.
//
// WHAT IT CANNOT COVER
//
//   Drawing. Thumbnails, arrowheads, hover, and whether the thing is pleasant
//   to use are the owner's smoke test. Everything the pixels are computed FROM
//   is here, which is why WorldMap, DoorValidator and MapView contain no
//   Texture2D and no GraphicsDevice — keep it that way and this stays possible.
//
// SECTIONS
//
//   1 verdicts    DoorValidator against hand-built worlds holding each of the
//                 five outcomes, then against the real one
//   2 layout      BFS columns on a synthetic graph; on the real registry the
//                 columns are re-derived independently and compared; repeat
//                 runs are identical; no two boxes overlap
//   3 geometry    which edge a door sits on, where its arrow meets the box,
//                 content bounds, hit-testing
//   4 arrows      every door becomes exactly one arrow; endpoints land on the
//                 right boxes; statuses match the validator's, door for door
//   5 view        MapView's transform round-trips, anchors its zoom, clamps
//                 its pan, and frames the board
//   6 file        worldmap.json: born empty, only hand-arranged rooms in it,
//                 byte-identical round trip, invisible to RoundTrip
//
// HOW TO RUN
//
//   dotnet build tools/MapCheck/MapCheck.csproj
//   dotnet run   --project tools/MapCheck/MapCheck.csproj
//   dotnet run   --project tools/MapCheck/MapCheck.csproj -- --out <dir>
//   dotnet run   --project tools/MapCheck/MapCheck.csproj -- --board
//
//   Exit 0 = every check passed. Exit 1 = failures (listed inline as FAIL).
//   Exit 2 = could not run (bad arguments, unsafe --out, repo root not found,
//            unreadable or invalid assets/data/rooms.json).
//
//   --board prints the computed board — every room's column, row and position,
//   and every arrow with its verdict. Read it when a map looks wrong on screen
//   and you need to know whether the picture or the data is at fault.
//
// SAFETY
//
//   assets/data and Content/ are READ ONLY: opened, parsed, left as they were.
//   The one thing this harness writes is a scratch worldmap.json, inside a
//   scratch directory validated before anything touches it — the repository's
//   own arrangement file is never opened for writing.
// ============================================================================

using Microsoft.Xna.Framework;
using SorceryForge;
using SorceryRemake.Rooms;
using System;
using System.Collections.Generic;
using System.IO;

namespace SorceryRemake.Tools.MapCheck
{
    internal static class Program
    {
        private static int _failures;
        private static int _checks;

        private const string DefaultScratchName = "sorcery-mapcheck";

        private static int Main(string[] args)
        {
            bool dumpBoard = false;
            string? outArg = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--board":
                        dumpBoard = true;
                        break;
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
            if (!File.Exists(Path.Combine(repoRoot, "SorceryRemake.csproj")))
            {
                Console.Error.WriteLine($"could not locate the repo root (got '{repoRoot}').");
                Console.Error.WriteLine("run this from inside the source tree, not a published build.");
                return 2;
            }

            string scratch = Path.GetFullPath(outArg ?? Path.Combine(Path.GetTempPath(), DefaultScratchName));

            Console.WriteLine("MapCheck — SorceryForge world-map regression harness");
            Console.WriteLine($"  repo  : {repoRoot}");
            Console.WriteLine($"  scratch: {scratch}");

            // Same contract as the other two harnesses: the scratch directory
            // may not be, contain, or sit inside the repository, so a mistyped
            // --out cannot land a worldmap.json in assets/data.
            string s = Path.TrimEndingDirectorySeparator(scratch);
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoRoot));
            if (string.Equals(s, root, StringComparison.OrdinalIgnoreCase)
                || s.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || root.StartsWith(s + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine(
                    $"refusing to run: '{scratch}' is, sits inside, or contains the repository root '{repoRoot}'.");
                return 2;
            }

            List<RoomMeta> rooms;
            try
            {
                // First touch of the registry, guarded: RoomManifest.All is a
                // Lazy<T> that throws our own message for a missing or
                // malformed rooms.json, and that should end the run cleanly.
                rooms = RoomMeta.All;
                Console.WriteLine($"  rooms : {rooms.Count} (RoomMeta.All)");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("refusing to run: " + ex.Message);
                return 2;
            }

            var doorsByRoom = RealDoorTable(rooms);
            var report = DoorValidator.Validate(DoorValidator.RoomIdsOf(rooms), doorsByRoom);
            var board = BuildRealBoard(rooms, doorsByRoom);
            var edges = WorldMap.BuildEdges(board, doorsByRoom, report.Status);

            if (dumpBoard) { DumpBoard(board, edges, report); return 0; }

            CheckVerdicts(rooms, doorsByRoom, report);
            CheckLayout(rooms, doorsByRoom, board);
            CheckGeometry(board);
            CheckArrows(board, doorsByRoom, report, edges);
            CheckView(board);
            CheckFile(board, scratch);

            Console.WriteLine();
            Console.WriteLine($"  {_checks} checks, {_failures} failure(s)");
            Console.WriteLine();
            Console.WriteLine(_failures == 0
                ? "  WORLD MAP HOLDS: the board is deterministic, every door is one arrow,\n" +
                  "  and every arrow wears the verdict the Doors button would give it."
                : "  WORLD MAP BROKEN — see the FAIL lines above.");

            return _failures == 0 ? 0 : 1;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("usage: dotnet run --project tools/MapCheck/MapCheck.csproj -- [--out <dir>] [--board]");
            Console.WriteLine();
            Console.WriteLine("  --out <dir>  scratch directory for the worldmap.json checks.");
            Console.WriteLine($"               default: %TEMP%\\{DefaultScratchName}");
            Console.WriteLine("  --board      print the computed board (rooms with their columns and");
            Console.WriteLine("               positions, arrows with their verdicts) and exit.");
            Console.WriteLine();
            Console.WriteLine("exit 0 = all checks pass; 1 = failures; 2 = could not run.");
            Console.WriteLine();
            Console.WriteLine("assets/data and Content/ are read only; only the scratch dir is written.");
        }

        // ====================================================================
        // THE REAL WORLD
        // ====================================================================

        /// <summary>
        /// The door table for the world as it is ON DISK — no editor, so no
        /// current room and no unsaved placements to overlay.
        /// </summary>
        private static Dictionary<string, List<DoorDef>> RealDoorTable(IReadOnlyList<RoomMeta> rooms)
        {
            var table = new Dictionary<string, List<DoorDef>>(StringComparer.Ordinal);
            foreach (var room in rooms) table[room.RoomId] = new List<DoorDef>(room.Doors);
            return table;
        }

        private static List<MapRoom> BuildRealBoard(IReadOnlyList<RoomMeta> rooms,
                                                    IReadOnlyDictionary<string, List<DoorDef>> doorsByRoom)
        {
            var board = new List<MapRoom>();
            foreach (var meta in rooms)
                board.Add(new MapRoom
                {
                    RoomId = meta.RoomId,
                    DisplayName = meta.DisplayName,
                    BackgroundAsset = meta.BackgroundAsset ?? "",
                });
            WorldMap.PlaceRooms(board, null, doorsByRoom);
            return board;
        }

        private static void DumpBoard(List<MapRoom> board, List<MapEdge> edges, DoorReport report)
        {
            Console.WriteLine("  ROOMS");
            foreach (var r in board)
                Console.WriteLine($"    {r.RoomId.PadRight(18)} col {(int)r.Position.X / WorldMap.ColumnPitch}" +
                                  $"  row {(int)r.Position.Y / WorldMap.RowPitch}" +
                                  $"  at ({(int)r.Position.X}, {(int)r.Position.Y})" +
                                  $"  {(r.Arranged ? "[stored]" : "[auto]")}");

            Console.WriteLine();
            Console.WriteLine("  ARROWS");
            foreach (var e in edges)
                Console.WriteLine($"    {e.Status.PadRight(12)} {(e.BothWays ? "<->" : " ->")} " +
                                  $"{e.FromRoomId}.{e.FromDoorId} -> " +
                                  $"{(e.Stub ? "(off board)" : e.ToRoomId + "." + e.ToDoorId)}" +
                                  $"   ({(int)e.From.X},{(int)e.From.Y}) -> ({(int)e.To.X},{(int)e.To.Y})");

            Console.WriteLine();
            Console.WriteLine($"  {report.Total} door links: ok {report.Ok}, asymmetric {report.Asymmetric}, " +
                              $"orphan-door {report.OrphanDoor}, orphan-room {report.OrphanRoom}");
        }

        // ====================================================================
        // 1. VERDICTS
        // ====================================================================
        // Hand-built worlds, one per outcome, so each rule is pinned on its own
        // rather than inferred from whatever the real data happens to contain.
        // ====================================================================

        private static DoorDef Door(string id, string targetRoom, string targetDoor, int x = 0, int y = 112) =>
            new(id, new Vector2(x, y), "RightOpening", targetRoom, targetDoor);

        private static void CheckVerdicts(IReadOnlyList<RoomMeta> rooms,
                                          Dictionary<string, List<DoorDef>> realTable,
                                          DoorReport realReport)
        {
            Section("1. VERDICTS — one hand-built world per outcome");

            var world = new Dictionary<string, List<DoorDef>>
            {
                ["a"] = new()
                {
                    Door("a_ok",       "b", "b_ok"),          // wired both ways
                    Door("a_asym",     "b", "b_elsewhere"),   // partner points away
                    Door("a_no_door",  "b", "b_missing"),     // room exists, door doesn't
                    Door("a_no_room",  "nowhere", "x"),       // no such room
                    Door("a_test",     "room_1", "whatever"), // programmatic test room
                },
                ["b"] = new()
                {
                    Door("b_ok",        "a", "a_ok"),
                    Door("b_elsewhere", "b", "b_ok"),
                },
            };
            var ids = new List<string> { "a", "b" };
            var r = DoorValidator.Validate(ids, world);

            AssertVerdict(r, "a_ok", "ok");
            AssertVerdict(r, "b_ok", "ok");
            AssertVerdict(r, "a_asym", "asymmetric");
            AssertVerdict(r, "a_no_door", "orphan-door");
            AssertVerdict(r, "a_no_room", "orphan-room");
            AssertVerdict(r, "a_test", "ok-test");

            // b_elsewhere is asymmetric too: it targets b_ok, and b_ok targets
            // a_ok rather than back at it. Seven doors, three of them fine.
            AssertVerdict(r, "b_elsewhere", "asymmetric");
            Assert("tallies add up", r.Total == 7 && r.Ok == 3 && r.Asymmetric == 2
                                     && r.OrphanDoor == 1 && r.OrphanRoom == 1,
                $"total {r.Total} ok {r.Ok} asym {r.Asymmetric} od {r.OrphanDoor} or {r.OrphanRoom}");
            // Only two doors are fully wired; the third "ok" is the test-room
            // link, which is tallied as fine rather than as broken.
            Assert("ok-test is counted as ok, not as broken", r.Ok == 3 && r.Bad == 4);

            // A partner that names the right ROOM but the wrong DOOR is still
            // asymmetric: the player comes back through a different door and
            // lands somewhere else in the room.
            var nearMiss = new Dictionary<string, List<DoorDef>>
            {
                ["a"] = new() { Door("a1", "b", "b1") },
                ["b"] = new() { Door("b1", "a", "a2"), Door("a2", "a", "a1") },
            };
            var nm = DoorValidator.Validate(new List<string> { "a", "b" }, nearMiss);
            AssertVerdict(nm, "a1", "asymmetric");

            // ---- the real world ----
            // Cross-checked against the layout FILES rather than against
            // RoomMeta, so a bug in the loading path shows up as a count
            // mismatch instead of being invisible to both sides.
            int doorsOnDisk = 0;
            foreach (var room in rooms)
            {
                var layout = RoomLayoutLoader.TryLoad(room.RoomId, EditorPaths.RepoAssetsDataDir);
                if (layout != null) doorsOnDisk += layout.doors.Count;
            }
            Assert($"every door in the layout files got a verdict ({doorsOnDisk} on disk)",
                realReport.Total == doorsOnDisk, $"validator saw {realReport.Total}");
            Assert("no door was judged twice", realReport.Status.Count == realReport.Total,
                $"{realReport.Status.Count} verdicts for {realReport.Total} doors");

            Console.WriteLine($"      (real world: ok {realReport.Ok}, asymmetric {realReport.Asymmetric}, " +
                              $"orphan-door {realReport.OrphanDoor}, orphan-room {realReport.OrphanRoom})");
        }

        private static void AssertVerdict(DoorReport report, string doorId, string expected)
        {
            string got = report.Status.TryGetValue(doorId, out var s) ? s : "(none)";
            Assert($"{doorId} -> {expected}", got == expected, got);
        }

        // ====================================================================
        // 2. LAYOUT
        // ====================================================================

        private static MapRoom Node(string id) => new() { RoomId = id, DisplayName = id, BackgroundAsset = "" };

        private static void CheckLayout(IReadOnlyList<RoomMeta> rooms,
                                        Dictionary<string, List<DoorDef>> doorsByRoom,
                                        List<MapRoom> board)
        {
            Section("2. LAYOUT — BFS columns, deterministic, non-overlapping");

            // ---- a synthetic chain, plus an island ----
            var chain = new List<MapRoom> { Node("a"), Node("b"), Node("c"), Node("d"), Node("island") };
            var chainDoors = new Dictionary<string, List<DoorDef>>
            {
                ["a"] = new() { Door("a1", "b", "b1") },
                ["b"] = new() { Door("b1", "a", "a1"), Door("b2", "c", "c1") },
                ["c"] = new() { Door("c1", "b", "b2"), Door("c2", "d", "d1") },
                ["d"] = new() { Door("d1", "c", "c2") },
                ["island"] = new(),
            };
            WorldMap.PlaceRooms(chain, null, chainDoors);
            AssertColumn(chain, "a", 0);
            AssertColumn(chain, "b", 1);
            AssertColumn(chain, "c", 2);
            AssertColumn(chain, "d", 3);
            Assert("an unreached room lands in the trailing column",
                ColumnOf(chain, "island") == 4, ColumnOf(chain, "island").ToString());

            // A link that exists only in ONE direction still makes the two
            // rooms neighbours: the map is about where things are, and the
            // arrow carries the direction.
            var oneWay = new List<MapRoom> { Node("start"), Node("drop") };
            var oneWayDoors = new Dictionary<string, List<DoorDef>>
            {
                ["start"] = new(),
                ["drop"] = new() { Door("d1", "start", "s1") },   // only drop -> start
            };
            WorldMap.PlaceRooms(oneWay, null, oneWayDoors);
            Assert("a one-way link still places its room next door",
                ColumnOf(oneWay, "drop") == 1, ColumnOf(oneWay, "drop").ToString());

            // Two rooms at the same depth share a column and stack in rows.
            var fork = new List<MapRoom> { Node("hub"), Node("left"), Node("right") };
            var forkDoors = new Dictionary<string, List<DoorDef>>
            {
                ["hub"] = new() { Door("h1", "left", "l1"), Door("h2", "right", "r1") },
                ["left"] = new() { Door("l1", "hub", "h1") },
                ["right"] = new() { Door("r1", "hub", "h2") },
            };
            WorldMap.PlaceRooms(fork, null, forkDoors);
            Assert("siblings share a column",
                ColumnOf(fork, "left") == 1 && ColumnOf(fork, "right") == 1);
            Assert("and stack in different rows",
                RowOf(fork, "left") != RowOf(fork, "right"));

            // ---- the real registry ----
            // Columns re-derived here by repeated relaxation over an adjacency
            // built straight from the layout files — a different algorithm over
            // differently-obtained data, so agreement means something.
            var expected = ShortestHops(rooms, doorsByRoom);
            bool columnsMatch = true;
            foreach (var room in board)
            {
                int col = ColumnOf(board, room.RoomId);
                if (expected.TryGetValue(room.RoomId, out int hops))
                {
                    if (col != hops) { columnsMatch = false; Console.WriteLine($"      {room.RoomId}: col {col}, hops {hops}"); }
                }
                else if (col <= MaxValue(expected))
                {
                    columnsMatch = false;
                    Console.WriteLine($"      {room.RoomId}: unreachable but placed in column {col}");
                }
            }
            Assert("every real room's column is its distance in door links from the first room",
                columnsMatch);

            // ---- determinism ----
            var again = BuildRealBoard(rooms, doorsByRoom);
            bool identical = again.Count == board.Count;
            for (int i = 0; identical && i < board.Count; i++)
                if (again[i].RoomId != board[i].RoomId || again[i].Position != board[i].Position)
                    identical = false;
            Assert("building the board twice gives the identical layout", identical);

            // ---- no overlaps ----
            bool overlap = false;
            for (int i = 0; i < board.Count && !overlap; i++)
            for (int j = i + 1; j < board.Count && !overlap; j++)
                if (board[i].Box.Intersects(board[j].Box))
                {
                    overlap = true;
                    Console.WriteLine($"      {board[i].RoomId} overlaps {board[j].RoomId}");
                }
            Assert("no two auto-placed boxes overlap", !overlap);

            // ---- a room added to the registry ----
            // The map's N / I entry points create rooms, and a fresh room has
            // no doors, so it is unreachable and joins the trailing column.
            // What matters is that its arrival does not MOVE anything: an
            // arrangement the user has learned must survive adding to it.
            var grown = BuildRealBoard(rooms, doorsByRoom);
            var grownDoors = new Dictionary<string, List<DoorDef>>(doorsByRoom, StringComparer.Ordinal);
            grown.Add(Node("brand_new_room"));
            grownDoors["brand_new_room"] = new List<DoorDef>();
            WorldMap.PlaceRooms(grown, null, grownDoors);

            bool nothingMoved = true;
            for (int i = 0; i < board.Count; i++)
                if (grown[i].RoomId != board[i].RoomId || grown[i].Position != board[i].Position)
                    nothingMoved = false;
            Assert("adding a room moves none of the existing ones", nothingMoved);

            var newcomer = grown[^1];
            bool newcomerClear = true;
            for (int i = 0; i < board.Count; i++)
                if (grown[i].Box.Intersects(newcomer.Box)) newcomerClear = false;
            Assert("  and the newcomer gets a clear spot of its own", newcomerClear,
                newcomer.Position.ToString());

            // ---- stored positions win, and only where given ----
            var mixed = BuildRealBoard(rooms, doorsByRoom);
            var stored = new Dictionary<string, Vector2> { [mixed[0].RoomId] = new Vector2(-1000, -500) };
            WorldMap.PlaceRooms(mixed, stored, doorsByRoom);
            Assert("a stored position overrides auto-placement",
                mixed[0].Position == new Vector2(-1000, -500) && mixed[0].Arranged);
            Assert("and leaves every other room exactly where it was",
                mixed.Count == board.Count && SamePositionsExceptFirst(board, mixed));
        }

        private static bool SamePositionsExceptFirst(List<MapRoom> a, List<MapRoom> b)
        {
            for (int i = 1; i < a.Count; i++)
                if (a[i].RoomId != b[i].RoomId || a[i].Position != b[i].Position || b[i].Arranged) return false;
            return true;
        }

        private static int MaxValue(Dictionary<string, int> d)
        {
            int max = -1;
            foreach (var v in d.Values) if (v > max) max = v;
            return max;
        }

        /// <summary>
        /// Distance in door links from the first registry room, computed by
        /// relaxation rather than by BFS — deliberately not the same algorithm
        /// WorldMap uses.
        /// </summary>
        private static Dictionary<string, int> ShortestHops(IReadOnlyList<RoomMeta> rooms,
                                                            IReadOnlyDictionary<string, List<DoorDef>> doorsByRoom)
        {
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in rooms) known.Add(r.RoomId);

            var pairs = new List<(string a, string b)>();
            foreach (var room in rooms)
            {
                if (!doorsByRoom.TryGetValue(room.RoomId, out var doors)) continue;
                foreach (var d in doors)
                {
                    if (d.TargetRoomId == room.RoomId) continue;
                    if (!known.Contains(d.TargetRoomId)) continue;
                    pairs.Add((room.RoomId, d.TargetRoomId));
                }
            }

            var dist = new Dictionary<string, int>(StringComparer.Ordinal) { [rooms[0].RoomId] = 0 };
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var (a, b) in pairs)
                {
                    if (Relax(dist, a, b)) changed = true;
                    if (Relax(dist, b, a)) changed = true;   // undirected, per WorldMap's rule
                }
            }
            return dist;
        }

        private static bool Relax(Dictionary<string, int> dist, string from, string to)
        {
            if (!dist.TryGetValue(from, out int d)) return false;
            if (dist.TryGetValue(to, out int existing) && existing <= d + 1) return false;
            dist[to] = d + 1;
            return true;
        }

        private static int ColumnOf(List<MapRoom> board, string roomId)
        {
            foreach (var r in board) if (r.RoomId == roomId) return (int)r.Position.X / WorldMap.ColumnPitch;
            return -1;
        }

        private static int RowOf(List<MapRoom> board, string roomId)
        {
            foreach (var r in board) if (r.RoomId == roomId) return (int)r.Position.Y / WorldMap.RowPitch;
            return -1;
        }

        private static void AssertColumn(List<MapRoom> board, string roomId, int column) =>
            Assert($"{roomId} sits in column {column}", ColumnOf(board, roomId) == column,
                ColumnOf(board, roomId).ToString());

        // ====================================================================
        // 3. GEOMETRY
        // ====================================================================

        private static void CheckGeometry(List<MapRoom> board)
        {
            Section("3. GEOMETRY — door sides, anchors, bounds, hit-testing");

            // The three conventions doc/07 documents, plus the case that makes
            // a naive rule wrong: a side door sits at y=112, near the BOTTOM,
            // and must still read as a side door.
            AssertSide("left-edge door (0, 112)", new Vector2(0, 112), DoorSide.Left);
            AssertSide("right-edge door (296, 112)", new Vector2(296, 112), DoorSide.Right);
            AssertSide("top door (148, 0)", new Vector2(148, 0), DoorSide.Top);
            AssertSide("bottom door (148, 120)", new Vector2(148, 120), DoorSide.Bottom);
            AssertSide("a corner door is a side door", new Vector2(0, 0), DoorSide.Left);

            var box = new Rectangle(1000, 500, WorldMap.RoomWidth, WorldMap.RoomHeight);

            var left = WorldMap.AnchorFor(box, new Vector2(0, 112));
            Assert("a left door anchors on the box's left edge, at the door's height",
                left == new Vector2(box.Left, box.Top + 124), left.ToString());

            var right = WorldMap.AnchorFor(box, new Vector2(296, 112));
            Assert("a right door anchors on the right edge",
                right == new Vector2(box.Right, box.Top + 124), right.ToString());

            var top = WorldMap.AnchorFor(box, new Vector2(148, 0));
            Assert("a top door anchors on the top edge, at the door's x",
                top == new Vector2(box.Left + 160, box.Top), top.ToString());

            // Two doors on the same edge must anchor at different points, or a
            // room with a top-left and a top-right door draws both arrows from
            // the same place and the map stops telling you which is which.
            var topLeft = WorldMap.AnchorFor(box, new Vector2(24, 0));
            var topRight = WorldMap.AnchorFor(box, new Vector2(272, 0));
            Assert("two doors on one edge anchor apart", topLeft != topRight);

            Assert("every real anchor lies on its own box's outline", AllAnchorsOnOutline(board));

            var bounds = WorldMap.ContentBounds(board);
            bool covers = true;
            foreach (var r in board) if (!bounds.Contains(r.Box)) covers = false;
            Assert("content bounds cover every box", covers, bounds.ToString());
            Assert("content bounds of nothing is empty", WorldMap.ContentBounds(new List<MapRoom>()).IsEmpty);

            var first = board[0];
            var inside = new Vector2(first.Box.Center.X, first.Box.Center.Y);
            Assert("hit-testing finds the room under a point",
                WorldMap.RoomAt(board, inside)?.RoomId == first.RoomId);
            Assert("and finds nothing in the gap between columns",
                WorldMap.RoomAt(board, new Vector2(first.Box.Right + WorldMap.ColumnGap / 2f,
                                                   first.Box.Center.Y)) == null);
        }

        private static bool AllAnchorsOnOutline(List<MapRoom> board)
        {
            foreach (var room in board)
            {
                var b = room.Box;
                foreach (var pos in new[] { new Vector2(0, 112), new Vector2(296, 112),
                                            new Vector2(148, 0), new Vector2(148, 120) })
                {
                    var a = WorldMap.AnchorFor(b, pos);
                    bool onVertical = (a.X == b.Left || a.X == b.Right) && a.Y >= b.Top && a.Y <= b.Bottom;
                    bool onHorizontal = (a.Y == b.Top || a.Y == b.Bottom) && a.X >= b.Left && a.X <= b.Right;
                    if (!onVertical && !onHorizontal) return false;
                }
            }
            return true;
        }

        private static void AssertSide(string label, Vector2 pos, DoorSide expected)
        {
            var got = WorldMap.SideFor(pos);
            Assert($"{label} -> {expected}", got == expected, got.ToString());
        }

        // ====================================================================
        // 4. ARROWS
        // ====================================================================

        private static void CheckArrows(List<MapRoom> board,
                                        Dictionary<string, List<DoorDef>> doorsByRoom,
                                        DoorReport report,
                                        List<MapEdge> edges)
        {
            Section("4. ARROWS — one per link, on the right rooms, with the right verdict");

            var boxes = new Dictionary<string, MapRoom>(StringComparer.Ordinal);
            foreach (var r in board) boxes[r.RoomId] = r;

            // Every door in the world is accounted for exactly once: either as
            // some arrow's source, or as the far end of a collapsed pair.
            var covered = new HashSet<string>(StringComparer.Ordinal);
            bool doubleCounted = false;
            foreach (var e in edges)
            {
                if (!covered.Add(e.FromDoorId)) doubleCounted = true;
                if (e.BothWays && !covered.Add(e.ToDoorId)) doubleCounted = true;
            }
            Assert("no door appears on two arrows", !doubleCounted);

            var allDoors = new HashSet<string>(StringComparer.Ordinal);
            foreach (var room in board)
                if (doorsByRoom.TryGetValue(room.RoomId, out var doors))
                    foreach (var d in doors) allDoors.Add(d.DoorId);
            Assert($"every one of the {allDoors.Count} doors is on an arrow",
                covered.SetEquals(allDoors), $"{covered.Count} covered");

            // A wired pair is ONE line; everything else is one arrow per door.
            int okPairs = 0, others = 0;
            var seenPairs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var room in board)
            {
                if (!doorsByRoom.TryGetValue(room.RoomId, out var doors)) continue;
                foreach (var d in doors)
                {
                    string status = report.Status[d.DoorId];
                    if (status == "ok")
                    {
                        string key = string.CompareOrdinal(d.DoorId, d.TargetDoorId) <= 0
                            ? d.DoorId + " " + d.TargetDoorId : d.TargetDoorId + " " + d.DoorId;
                        if (seenPairs.Add(key)) okPairs++;
                    }
                    else others++;
                }
            }
            Assert($"arrow count is {okPairs} collapsed pairs + {others} singles",
                edges.Count == okPairs + others, $"{edges.Count} arrows");

            bool statusMatches = true, endpointsRight = true, headsRight = true;
            foreach (var e in edges)
            {
                if (report.Status[e.FromDoorId] != e.Status) statusMatches = false;

                // The source end always sits on its own room's outline.
                var fromBox = boxes[e.FromRoomId].Box;
                if (!OnOutline(fromBox, e.From)) endpointsRight = false;

                // The far end sits on the target's outline, unless the arrow is
                // a stub — which is exactly the set of links with no box to
                // reach: test rooms, missing rooms, and self-links.
                if (e.Stub)
                {
                    bool offBoard = e.ToRoomId == "" || e.ToRoomId == e.FromRoomId;
                    if (!offBoard) endpointsRight = false;
                }
                else
                {
                    if (!boxes.TryGetValue(e.ToRoomId, out var toRoom) || !OnOutline(toRoom.Box, e.To))
                        endpointsRight = false;
                }

                // Two arrowheads iff the pair is correctly wired.
                if (e.BothWays != (e.Status == "ok")) headsRight = false;
            }
            Assert("every arrow's colour comes from its own door's verdict", statusMatches);
            Assert("every arrow starts and ends where it says", endpointsRight);
            Assert("double-headed arrows are exactly the correctly-wired pairs", headsRight);

            // ---- the failure shapes, hand-built ----
            var nodes = new List<MapRoom> { Node("a"), Node("b") };
            var world = new Dictionary<string, List<DoorDef>>
            {
                ["a"] = new()
                {
                    Door("a_ok", "b", "b_ok", 296, 112),
                    Door("a_missing_door", "b", "nope", 0, 112),
                    Door("a_missing_room", "ghost", "x", 148, 0),
                    Door("a_test", "room_1", "x", 148, 120),
                    Door("a_self", "a", "a_ok", 24, 0),
                },
                ["b"] = new() { Door("b_ok", "a", "a_ok", 0, 112) },
            };
            WorldMap.PlaceRooms(nodes, null, world);
            var rep = DoorValidator.Validate(new List<string> { "a", "b" }, world);
            var built = WorldMap.BuildEdges(nodes, world, rep.Status);

            Assert("a wired pair collapses to one double-headed arrow",
                CountEdges(built, "ok") == 1 && FindEdge(built, "a_ok")!.BothWays);
            Assert("an orphan-door arrow still reaches the target room",
                FindEdge(built, "a_missing_door") is { Stub: false, ToRoomId: "b" });
            Assert("a missing room becomes a stub off the source door",
                FindEdge(built, "a_missing_room") is { Stub: true, ToRoomId: "" });
            Assert("a test-room link becomes a stub too (there is no box for it)",
                FindEdge(built, "a_test") is { Stub: true, Status: "ok-test" });
            Assert("a self-link becomes a stub rather than a line to itself",
                FindEdge(built, "a_self") is { Stub: true, ToRoomId: "a" });

            var stub = FindEdge(built, "a_missing_room")!;
            Assert("a stub points outward from its door's edge",
                stub.To.Y < stub.From.Y && Math.Abs(stub.To.X - stub.From.X) < 0.001f,
                $"{stub.From} -> {stub.To}");
        }

        private static bool OnOutline(Rectangle box, Vector2 p)
        {
            bool vertical = (Math.Abs(p.X - box.Left) < 0.001f || Math.Abs(p.X - box.Right) < 0.001f)
                            && p.Y >= box.Top - 0.001f && p.Y <= box.Bottom + 0.001f;
            bool horizontal = (Math.Abs(p.Y - box.Top) < 0.001f || Math.Abs(p.Y - box.Bottom) < 0.001f)
                              && p.X >= box.Left - 0.001f && p.X <= box.Right + 0.001f;
            return vertical || horizontal;
        }

        private static int CountEdges(List<MapEdge> edges, string status)
        {
            int n = 0;
            foreach (var e in edges) if (e.Status == status) n++;
            return n;
        }

        private static MapEdge? FindEdge(List<MapEdge> edges, string fromDoorId)
        {
            foreach (var e in edges) if (e.FromDoorId == fromDoorId) return e;
            return null;
        }

        // ====================================================================
        // 5. VIEW
        // ====================================================================

        private static void CheckView(List<MapRoom> board)
        {
            Section("5. VIEW — transform, anchored zoom, pan clamp, framing");

            var bounds = WorldMap.ContentBounds(board);
            var view = new MapView { Viewport = new Rectangle(0, 56, 1280, 632) };

            view.FitTo(bounds);
            Assert("framing picks a zoom at which the whole board fits",
                bounds.Width * view.Scale <= view.Viewport.Width + 1
                && bounds.Height * view.Scale <= view.Viewport.Height + 1,
                $"{view.ZoomPercent}% of {bounds.Width}x{bounds.Height}");
            Assert("and centres it",
                Math.Abs((view.Pan.X + view.VisibleWidth / 2) - (bounds.Left + bounds.Width / 2f)) < 1.5f,
                view.Pan.ToString());

            // Screen and map coordinates must agree in both directions, or the
            // box a click lands on is not the box that was drawn there.
            bool roundTrips = true;
            for (int i = 0; i < MapView.ZoomLevels.Length; i++)
            {
                view.ZoomIndex = i;
                view.Pan = new Vector2(137, -84);
                foreach (var screen in new[] { new Point(0, 56), new Point(640, 300), new Point(1279, 687) })
                {
                    var back = view.MapToScreen(view.ScreenToMap(screen));
                    if (Math.Abs(back.X - screen.X) > 1 || Math.Abs(back.Y - screen.Y) > 1) roundTrips = false;
                }
            }
            Assert("screen -> map -> screen returns to within a pixel at every zoom", roundTrips);

            // Wheel zoom must keep the point under the cursor still, which is
            // the whole reason it is anchored rather than centred.
            bool anchored = true;
            var anchor = new Point(900, 400);
            for (int i = 0; i < MapView.ZoomLevels.Length - 1; i++)
            {
                view.ZoomIndex = i;
                view.Pan = new Vector2(0, 0);
                var before = view.ScreenToMap(anchor);
                view.StepZoom(+1, anchor, new Rectangle(-100000, -100000, 200000, 200000));
                var after = view.ScreenToMap(anchor);
                if ((before - after).Length() > 1.5f) anchored = false;
            }
            Assert("zooming keeps the map point under the cursor", anchored);

            view.ZoomIndex = MapView.ZoomLevels.Length - 1;
            view.Pan = new Vector2(999999, 999999);
            view.ClampPan(bounds);
            Assert("panning far past the board is clamped back to it",
                view.Pan.X < bounds.Right + 512 && view.Pan.Y < bounds.Bottom + 512, view.Pan.ToString());

            // Content smaller than the viewport has nothing to scroll; it must
            // pin centred rather than slide around.
            var tiny = new Rectangle(0, 0, 100, 50);
            view.ZoomIndex = 0;
            view.Pan = new Vector2(5000, 5000);
            view.ClampPan(tiny);
            var pinned = view.Pan;
            view.Pan = new Vector2(-5000, -5000);
            view.ClampPan(tiny);
            Assert("a board smaller than the viewport pins centred", view.Pan == pinned, view.Pan.ToString());

            Assert("a box's drawn width is its map width times the scale",
                view.MapRectToScreen(new Rectangle(0, 0, WorldMap.RoomWidth, WorldMap.RoomHeight)).Width
                    == (int)(WorldMap.RoomWidth * view.Scale));
        }

        // ====================================================================
        // 6. THE FILE
        // ====================================================================
        // assets/data/worldmap.json, exercised in a scratch directory. The real
        // one is never touched: only the arrangement is at stake, but this
        // harness's contract is that it writes nothing into the repository.
        // ====================================================================

        private static void CheckFile(List<MapRoom> board, string scratch)
        {
            Section("6. THE FILE — worldmap.json, born empty and stable");

            Directory.CreateDirectory(scratch);
            string path = WorldMapFile.GetPath(scratch);
            if (File.Exists(path)) File.Delete(path);

            // ---- born-empty discipline ----
            // Nothing arranged and no file yet: write nothing, so an untouched
            // map never adds a file to the repository. This is the 3b rule.
            var untouched = CloneBoard(board);
            Assert("an untouched board writes no file",
                !WorldMapFile.Save(untouched, scratch) && !File.Exists(path));

            // ---- only arranged rooms are recorded ----
            var arranged = CloneBoard(board);
            arranged[0].Arranged = true;
            arranged[0].Position = new Vector2(64, -32);
            arranged[2].Arranged = true;
            arranged[2].Position = new Vector2(1000, 2000);

            Assert("dragging two rooms and saving writes the file",
                WorldMapFile.Save(arranged, scratch) && File.Exists(path));

            var loaded = WorldMapFile.Load(scratch, out string? loadError);
            Assert("  it loads back with no error", loadError == null, loadError);
            Assert("  holding exactly the two dragged rooms", loaded.Count == 2, $"{loaded.Count} entries");
            Assert("  at exactly the positions they were dragged to",
                loaded.TryGetValue(arranged[0].RoomId, out var p0) && p0 == new Vector2(64, -32)
                && loaded.TryGetValue(arranged[2].RoomId, out var p2) && p2 == new Vector2(1000, 2000));
            Assert("  and no auto-placed room in sight",
                !loaded.ContainsKey(arranged[1].RoomId));

            // ---- byte-identical round trip ----
            // Same property RoundTrip's self-test pins for rooms.json, for the
            // same reason: a save that reformats buries the one line that
            // changed in whole-file noise.
            string first = File.ReadAllText(path);
            var reboard = CloneBoard(board);
            WorldMap.PlaceRooms(reboard, loaded, new Dictionary<string, List<DoorDef>>());
            WorldMapFile.Save(reboard, scratch);
            Assert("load -> save is byte-identical", File.ReadAllText(path) == first);

            // ---- registry order, not drag order ----
            var reversed = CloneBoard(board);
            reversed.Reverse();
            WorldMap.PlaceRooms(reversed, loaded, new Dictionary<string, List<DoorDef>>());
            WorldMapFile.Save(reversed, scratch);
            string reversedText = File.ReadAllText(path);
            Assert("line order follows the list given, so registry order is stable",
                reversedText != first && LineCount(reversedText) == LineCount(first));
            // Put it back the right way round for the checks that follow.
            WorldMapFile.Save(reboard, scratch);

            // ---- unknown ids ----
            File.WriteAllText(path,
                "{\r\n  \"rooms\": {\r\n" +
                "    \"" + board[0].RoomId + "\": { \"x\": 10, \"y\": 20 },\r\n" +
                "    \"a_room_that_was_deleted\": { \"x\": 1, \"y\": 2 }\r\n" +
                "  }\r\n}\r\n");
            var withGhost = WorldMapFile.Load(scratch, out _);
            Assert("an unknown room id survives the raw load", withGhost.Count == 2);

            // ...and is dropped the moment it meets the registry, then gone on
            // the next save. The editor does this filtering in LoadMapPositions;
            // the file layer stays dumb on purpose, so a hand-edited file is
            // never silently rewritten just by being read.
            var filtered = new Dictionary<string, Vector2>(StringComparer.Ordinal);
            foreach (var pair in withGhost) if (RoomMeta.Find(pair.Key) != null) filtered[pair.Key] = pair.Value;
            Assert("  filtering against the registry drops it", filtered.Count == 1);

            var afterGhost = CloneBoard(board);
            WorldMap.PlaceRooms(afterGhost, filtered, new Dictionary<string, List<DoorDef>>());
            WorldMapFile.Save(afterGhost, scratch);
            Assert("  and the next save no longer mentions it",
                !File.ReadAllText(path).Contains("a_room_that_was_deleted", StringComparison.Ordinal));

            // ---- a reset persists ----
            // Nothing arranged but a file EXISTS: that is a user who dragged
            // every room back, and it must be written, not skipped. Getting
            // this backwards is how a deletion silently fails to stick.
            var reset = CloneBoard(board);
            Assert("clearing every arrangement still writes (the reset persists)",
                WorldMapFile.Save(reset, scratch) && File.Exists(path));
            Assert("  and the file is now empty of rooms",
                WorldMapFile.Load(scratch, out _).Count == 0);

            // ---- deleting the file resets the board ----
            Assert("deleting the file leaves nothing stored", WorldMapFile.Delete(scratch)
                && WorldMapFile.Load(scratch, out _).Count == 0);
            var afterDelete = CloneBoard(board);
            WorldMap.PlaceRooms(afterDelete, WorldMapFile.Load(scratch, out _), RealDoorTableFor(afterDelete));
            bool backToAuto = true;
            for (int i = 0; i < board.Count; i++)
                if (afterDelete[i].Position != board[i].Position || afterDelete[i].Arranged) backToAuto = false;
            Assert("  and the board is back to auto-placement", backToAuto);

            // ---- a broken file costs the arrangement, not the editor ----
            File.WriteAllText(path, "{ this is not json");
            var broken = WorldMapFile.Load(scratch, out string? brokenError);
            Assert("an unreadable file reports and falls back to auto-placement",
                broken.Count == 0 && brokenError != null, brokenError ?? "no error reported");
            File.Delete(path);

            // ---- invisible to RoundTrip ----
            // tools/RoundTrip seeds and sweeps content_* and layout_* only, and
            // flags any OTHER file its save path creates in the scratch copy.
            // worldmap.json is written by neither loader, so it can never
            // appear there — but "can never" is worth asserting rather than
            // assuming, because the name is the whole reason.
            Assert("worldmap.json is outside RoundTrip's seed/sweep prefixes",
                !WorldMapFile.FileName.StartsWith("content_", StringComparison.OrdinalIgnoreCase)
                && !WorldMapFile.FileName.StartsWith("layout_", StringComparison.OrdinalIgnoreCase));
            Assert("  and no room id could ever derive that name",
                RoomMeta.Find("worldmap") == null
                && !File.Exists(RoomContentLoader.GetPath("worldmap", scratch)));
        }

        private static List<MapRoom> CloneBoard(List<MapRoom> board)
        {
            var copy = new List<MapRoom>(board.Count);
            foreach (var r in board)
                copy.Add(new MapRoom
                {
                    RoomId = r.RoomId,
                    DisplayName = r.DisplayName,
                    BackgroundAsset = r.BackgroundAsset,
                    Position = r.Position,
                    Arranged = r.Arranged,
                });
            return copy;
        }

        private static Dictionary<string, List<DoorDef>> RealDoorTableFor(List<MapRoom> board)
        {
            var table = new Dictionary<string, List<DoorDef>>(StringComparer.Ordinal);
            foreach (var r in board)
            {
                var meta = RoomMeta.Find(r.RoomId);
                table[r.RoomId] = meta != null ? new List<DoorDef>(meta.Doors) : new List<DoorDef>();
            }
            return table;
        }

        private static int LineCount(string text) => text.Replace("\r\n", "\n").Split('\n').Length;

        // ====================================================================
        // PLUMBING
        // ====================================================================

        private static void Section(string title) => Console.WriteLine($"  {title}");

        private static void Assert(string label, bool ok, string? detail = null)
        {
            _checks++;
            if (!ok) _failures++;
            string suffix = ok || string.IsNullOrEmpty(detail) ? "" : $"   [{detail}]";
            Console.WriteLine($"    {(ok ? "ok  " : "FAIL")} {label}{suffix}");
        }
    }
}
