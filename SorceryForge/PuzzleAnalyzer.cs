using SorceryRemake.Core;
using SorceryRemake.Rooms;
using System.Collections.Generic;

namespace SorceryForge
{
    /// <summary>
    /// One issue surfaced by the puzzle solver. RoomId/PlacementId let the
    /// editor highlight the offender on canvas; Reason is a short human
    /// description for the status bar.
    /// </summary>
    public class PuzzleIssue
    {
        public string RoomId;
        public string PlacementId;
        public string Reason;

        public PuzzleIssue(string roomId, string placementId, string reason)
        {
            RoomId = roomId;
            PlacementId = placementId;
            Reason = reason;
        }
    }

    public class PuzzleReport
    {
        public HashSet<string> ReachableRoomIds = new();
        public List<string> UnreachableRoomIds = new();
        public List<PuzzleIssue> Issues = new();

        public int ItemCount;
        public int EnemyCount;
        public int WizardCount;
        public int BlockedDoorCount;
    }

    /// <summary>
    /// Cross-room sanity check for the current placements. Walks the door
    /// graph from the player's start room (chateau_0), aggregates every
    /// item, enemy, blocked door, and wizard in reachable rooms, then asks:
    ///
    ///   1. Every enemy in a reachable room — is at least one weapon that
    ///      kills it placed somewhere in the reachable world? (Shooting Star
    ///      counts for everything since it AOEs any enemy.)
    ///   2. Every blocked door in a reachable room — is its required key
    ///      item placed somewhere in the reachable world?
    ///   3. Every wizard in an UNREACHABLE room — flagged as unreachable.
    ///
    /// Limitations:
    ///   - Doesn't model the one-item rule's ordering. So "the only Axe is
    ///     behind the only Wraith" reads as solvable.
    ///   - Doesn't model Shooting Star consumption (each shot kills one
    ///     enemy, but we treat its presence as covering all enemies).
    ///   - Treats blocked doors as wall-only (not graph edges), matching
    ///     how Game1 implements them.
    ///   - Reads JSON content via RoomContentLoader; rooms with no
    ///     content_*.json contribute nothing (treated as empty).
    /// </summary>
    public static class PuzzleAnalyzer
    {
        public const string StartRoom = "chateau_0";

        /// <summary>
        /// Run the analysis. Every room is read from its JSON files EXCEPT
        /// the one named by <paramref name="overrideRoomId"/>, which is taken
        /// from the supplied in-memory content / doors instead.
        ///
        /// That override is what makes the Puzzle button honour unsaved edits
        /// in the room currently open in the editor — otherwise it reports on
        /// the last-saved world and silently contradicts what's on screen.
        /// ValidateDoors already overlays the current room the same way.
        /// Passing no arguments analyses purely what's on disk.
        /// </summary>
        public static PuzzleReport Analyze(
            string? overrideRoomId = null,
            RoomContent? overrideContent = null,
            List<DoorDef>? overrideDoors = null)
        {
            var report = new PuzzleReport();

            // ---------- 1. Reachable rooms via door BFS ------------------
            var visited = new HashSet<string>();
            var queue = new Queue<string>();
            if (RoomMeta.Find(StartRoom) != null)
            {
                visited.Add(StartRoom);
                queue.Enqueue(StartRoom);
            }
            while (queue.Count > 0)
            {
                string rid = queue.Dequeue();
                var meta = RoomMeta.Find(rid);
                if (meta == null) continue;

                // Expand the overridden room through its unsaved doors, so a
                // just-authored connection counts towards reachability.
                var doors = (rid == overrideRoomId && overrideDoors != null)
                    ? overrideDoors
                    : meta.Doors;

                foreach (var d in doors)
                {
                    if (RoomMeta.Find(d.TargetRoomId) == null) continue;
                    if (visited.Add(d.TargetRoomId))
                        queue.Enqueue(d.TargetRoomId);
                }
            }
            report.ReachableRoomIds = visited;
            foreach (var m in RoomMeta.All)
                if (!visited.Contains(m.RoomId))
                    report.UnreachableRoomIds.Add(m.RoomId);

            // ---------- 2. Aggregate placements in reachable rooms ----------
            var allItems = new HashSet<ItemType>();
            var enemies = new List<(string room, string id, EnemyType type)>();
            var blocked = new List<(string room, string id, ItemType req)>();

            foreach (var rid in visited)
            {
                var content = (rid == overrideRoomId && overrideContent != null)
                    ? overrideContent
                    : RoomContentLoader.TryLoad(rid);
                if (content == null) continue;

                foreach (var i in content.Items)        { allItems.Add(i.Type);                      report.ItemCount++; }
                foreach (var e in content.Enemies)      { enemies.Add((rid, e.Id, e.Type));          report.EnemyCount++; }
                foreach (var b in content.BlockedDoors) { blocked.Add((rid, b.Id, b.RequiredItem));  report.BlockedDoorCount++; }
                foreach (var _ in content.Wizards)      { report.WizardCount++; }
            }

            // ---------- 3. Enemy weapon coverage --------------------------
            bool hasAOE = allItems.Contains(ItemType.ShootingStar);
            foreach (var (rid, id, type) in enemies)
            {
                if (hasAOE) continue;
                bool covered = false;
                foreach (var item in allItems)
                {
                    if (ItemSystem.CanKillEnemy(type, item)) { covered = true; break; }
                }
                if (!covered)
                    report.Issues.Add(new PuzzleIssue(rid, id,
                        $"{type} placed but no counter weapon in reachable rooms"));
            }

            // ---------- 4. Blocked-door key coverage ----------------------
            foreach (var (rid, id, req) in blocked)
            {
                if (!allItems.Contains(req))
                    report.Issues.Add(new PuzzleIssue(rid, id,
                        $"Blocked door needs {req} but none placed in reachable rooms"));
            }

            // ---------- 5. Wizards stranded in unreachable rooms ----------
            foreach (var rid in report.UnreachableRoomIds)
            {
                var content = (rid == overrideRoomId && overrideContent != null)
                    ? overrideContent
                    : RoomContentLoader.TryLoad(rid);
                if (content == null) continue;
                foreach (var w in content.Wizards)
                    report.Issues.Add(new PuzzleIssue(rid, w.Id,
                        $"Wizard in unreachable room {rid}"));
            }

            return report;
        }
    }
}
