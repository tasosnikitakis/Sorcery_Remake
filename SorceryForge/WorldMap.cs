// ============================================================================
// WORLD MAP
// SorceryForge — the board: where each room sits, and what links to what
// ============================================================================
// EDITOR_REVIEW item D. Prev/Next cycling works at nine rooms and does not
// work at the target seventy-five; the map is what replaces it. Rooms are
// boxes carrying their own background as a thumbnail, door links are arrows
// between them, and the arrangement is something you can drag into a shape
// that matches how the world actually feels.
//
// UI-FREE ON PURPOSE, like NewRoomFlow and ImageImport before it. Nothing here
// knows about a screen: positions and arrow endpoints come out in MAP UNITS,
// and MapView turns those into pixels. That is what lets tools/MapCheck assert
// the layout is deterministic and the arrows land on the right rooms without a
// desktop session — the parts a human has to look at are then genuinely only
// the parts that need looking at.
//
// MAP UNITS ARE ROOM PIXELS. A room box is exactly 320x144 units, so a
// position in worldmap.json reads in the same numbers as everything else in
// this project, and "two rooms one room-width apart" is a number you can see.
// ============================================================================

using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace SorceryForge
{
    /// <summary>Which edge of a room box a door sits on.</summary>
    public enum DoorSide { Left, Right, Top, Bottom }

    /// <summary>One room's box on the board.</summary>
    public class MapRoom
    {
        public string RoomId = "";
        public string DisplayName = "";
        public string BackgroundAsset = "";

        /// <summary>Top-left of the box, in map units.</summary>
        public Vector2 Position;

        /// <summary>
        /// True when this room was placed BY HAND — loaded from worldmap.json,
        /// or just dragged. Exactly the set WorldMapFile writes back; an
        /// auto-placed room is never recorded.
        /// </summary>
        public bool Arranged;

        public Rectangle Box =>
            new((int)Position.X, (int)Position.Y, WorldMap.RoomWidth, WorldMap.RoomHeight);
    }

    /// <summary>
    /// One line to draw. A correctly-wired pair of doors produces a single
    /// edge with an arrowhead at both ends; everything else produces one
    /// directed arrow per door.
    /// </summary>
    public class MapEdge
    {
        public string FromRoomId = "";
        public string ToRoomId = "";        // "" when the target is off the board
        public string FromDoorId = "";
        public string ToDoorId = "";

        /// <summary>One of DoorValidator's five verdicts.</summary>
        public string Status = "";

        /// <summary>Arrowheads at both ends: the pair is wired correctly.</summary>
        public bool BothWays;

        /// <summary>
        /// The far end is not a room box — a link to a programmatic test room,
        /// to a room that does not exist, or back into the same room. Drawn as
        /// a short spur off the source door rather than a line to nowhere.
        /// </summary>
        public bool Stub;

        public Vector2 From;
        public Vector2 To;
    }

    public static class WorldMap
    {
        // A box is one room, at one map unit per room pixel.
        public const int RoomWidth = 320;
        public const int RoomHeight = 144;

        // Gaps between auto-placed boxes. Horizontal is the wider of the two
        // because that is where the arrows run — a BFS column boundary is
        // exactly where links cross.
        public const int ColumnGap = 128;
        public const int RowGap = 72;

        public const int ColumnPitch = RoomWidth + ColumnGap;
        public const int RowPitch = RoomHeight + RowGap;

        /// <summary>Length of the spur drawn for a link with no box at the far end.</summary>
        public const int StubLength = 64;

        // ====================================================================
        // AUTO-PLACEMENT
        // ====================================================================
        // BFS layers from the first registry room: column = distance in door
        // links, row = the order the room was first reached. Rooms no chain of
        // doors reaches get a trailing column of their own.
        //
        // DETERMINISM RULE, and it is a rule rather than an accident: the same
        // registry and the same layout files must always produce the same
        // board, or every session rearranges the world under the user and the
        // arrangement they learned is worthless. Two things enforce it —
        //   1. rooms are seeded and enqueued in REGISTRY ORDER, never in
        //      dictionary order, so nothing depends on hashing;
        //   2. adjacency is symmetric (see below), so it cannot depend on which
        //      end of a link happened to be visited first.
        // tools/MapCheck asserts a repeat run is identical.
        //
        // ADJACENCY IS UNDIRECTED even though doors are directed. A one-way
        // drop still makes two rooms neighbours in the world, and a room whose
        // only link is INTO the start room is plainly adjacent to it — treating
        // links as directed would exile it to the unreached column and put it
        // nowhere near the room it connects to. The map is about where things
        // are; the arrows carry the direction.
        // ====================================================================

        /// <summary>
        /// Fill in every room's Position: from <paramref name="stored"/> when
        /// it has one (FromFile), otherwise from the BFS layout. Mutates the
        /// list in place, in registry order.
        /// </summary>
        // `stored` may be null, which means "nothing has been arranged by hand"
        // — every room is auto-placed. That is also exactly what an absent
        // worldmap.json means, so the no-file case needs no special path.
        public static void PlaceRooms(List<MapRoom> rooms,
                                      IReadOnlyDictionary<string, Vector2>? stored,
                                      IReadOnlyDictionary<string, List<DoorDef>> doorsByRoom)
        {
            if (rooms.Count == 0) return;

            var auto = AutoPlace(rooms, doorsByRoom);
            foreach (var room in rooms)
            {
                if (stored != null && stored.TryGetValue(room.RoomId, out var saved))
                {
                    room.Position = saved;
                    room.Arranged = true;
                }
                else
                {
                    room.Position = auto[room.RoomId];
                    room.Arranged = false;
                }
            }
        }

        /// <summary>
        /// The BFS layout for every room, whether or not it will be used. Room
        /// id → top-left in map units.
        /// </summary>
        // Computed for ALL rooms even when some have stored positions, so that
        // dragging one room never shifts the auto-placed ones around it. The
        // auto layout is a fixed backdrop; the file is an overlay on it.
        public static Dictionary<string, Vector2> AutoPlace(
            IReadOnlyList<MapRoom> rooms, IReadOnlyDictionary<string, List<DoorDef>> doorsByRoom)
        {
            var result = new Dictionary<string, Vector2>(StringComparer.Ordinal);
            if (rooms.Count == 0) return result;

            var order = new List<string>(rooms.Count);
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in rooms) { order.Add(r.RoomId); known.Add(r.RoomId); }

            var neighbours = BuildAdjacency(order, known, doorsByRoom);

            var depth = new Dictionary<string, int>(StringComparer.Ordinal);
            var columns = new List<List<string>>();

            // Seeded from the FIRST registry room — chateau_0 today, and
            // whatever the registry's first entry is tomorrow. The registry's
            // array order is documented as room order; this follows it rather
            // than hardcoding an id that a future reordering would strand.
            var queue = new Queue<string>();
            queue.Enqueue(order[0]);
            depth[order[0]] = 0;

            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                int d = depth[id];
                while (columns.Count <= d) columns.Add(new List<string>());
                columns[d].Add(id);

                foreach (string next in neighbours[id])
                {
                    if (depth.ContainsKey(next)) continue;
                    depth[next] = d + 1;
                    queue.Enqueue(next);
                }
            }

            // Anything no chain of doors reaches — a room authored but not yet
            // wired up, which during a 75-room build-out is most of them for a
            // while — gets a column past the end, in registry order.
            //
            // Known characteristic, not a bug: a whole disconnected COMPONENT
            // lands here as a flat stack rather than being laid out on its own
            // BFS. Today that is the stonehenge/wastelands/tunnelmouth chain
            // plus the two unwired chateau rooms; mid-build-out it could be
            // most of the world. Dragging is the answer — a room moved by hand
            // keeps its place — and laying each component out separately would
            // be the obvious improvement if that stops being enough.
            var unreached = new List<string>();
            foreach (string id in order) if (!depth.ContainsKey(id)) unreached.Add(id);
            if (unreached.Count > 0) columns.Add(unreached);

            for (int col = 0; col < columns.Count; col++)
            for (int row = 0; row < columns[col].Count; row++)
                result[columns[col][row]] = new Vector2(col * ColumnPitch, row * RowPitch);

            return result;
        }

        /// <summary>
        /// Room id → its neighbours, in registry order, deduplicated. A door in
        /// either direction makes two rooms neighbours; links to rooms outside
        /// the registry (test rooms, typos) are not adjacency.
        /// </summary>
        private static Dictionary<string, List<string>> BuildAdjacency(
            IReadOnlyList<string> order, HashSet<string> known,
            IReadOnlyDictionary<string, List<DoorDef>> doorsByRoom)
        {
            var sets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (string id in order) sets[id] = new HashSet<string>(StringComparer.Ordinal);

            foreach (string id in order)
            {
                if (!doorsByRoom.TryGetValue(id, out var doors)) continue;
                foreach (var door in doors)
                {
                    string target = door.TargetRoomId;
                    if (string.IsNullOrEmpty(target) || target == id) continue;
                    if (!known.Contains(target)) continue;
                    sets[id].Add(target);
                    sets[target].Add(id);      // undirected, per the rule above
                }
            }

            // Materialised in registry order rather than set order: a HashSet
            // enumerates in insertion order today and is not contracted to,
            // and the determinism rule above must not rest on that.
            var lists = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string id in order)
            {
                var list = new List<string>();
                foreach (string other in order) if (sets[id].Contains(other)) list.Add(other);
                lists[id] = list;
            }
            return lists;
        }

        // ====================================================================
        // EDGES
        // ====================================================================

        /// <summary>
        /// One drawable edge per link. A correctly-wired pair collapses to a
        /// single double-headed line; everything else stays a directed arrow so
        /// the defect is visible as a direction.
        /// </summary>
        // Statuses come from DoorValidator — this does not re-derive them, so
        // an arrow can never disagree with the Doors button about the same
        // door. It only decides where the line goes.
        public static List<MapEdge> BuildEdges(IReadOnlyList<MapRoom> rooms,
                                               IReadOnlyDictionary<string, List<DoorDef>> doorsByRoom,
                                               IReadOnlyDictionary<string, string> doorStatus)
        {
            var boxes = new Dictionary<string, MapRoom>(StringComparer.Ordinal);
            foreach (var r in rooms) boxes[r.RoomId] = r;

            var edges = new List<MapEdge>();
            var collapsed = new HashSet<string>(StringComparer.Ordinal);

            foreach (var room in rooms)
            {
                if (!doorsByRoom.TryGetValue(room.RoomId, out var doors)) continue;

                foreach (var door in doors)
                {
                    string status = doorStatus.TryGetValue(door.DoorId, out var s) ? s : "orphan-room";
                    Vector2 from = AnchorFor(room.Box, door.Position);

                    // A wired pair is one line. Keyed by the two door ids
                    // sorted, so whichever end is walked first claims it and
                    // the other skips — the reason this is keyed on doors and
                    // not on rooms is that two rooms can be joined by several
                    // door pairs, and each deserves its own line.
                    if (status == "ok")
                    {
                        string key = string.CompareOrdinal(door.DoorId, door.TargetDoorId) <= 0
                            ? door.DoorId + " " + door.TargetDoorId
                            : door.TargetDoorId + " " + door.DoorId;
                        if (!collapsed.Add(key)) continue;
                    }

                    var edge = new MapEdge
                    {
                        FromRoomId = room.RoomId,
                        FromDoorId = door.DoorId,
                        ToDoorId = door.TargetDoorId,
                        Status = status,
                        BothWays = status == "ok",
                        From = from,
                    };

                    // A self-link has no second box to reach, and a link to a
                    // test room or a nonexistent one has no box at all. All
                    // three become a spur pointing out of the source door: it
                    // says "this door leads somewhere not on this board", which
                    // is the true and useful statement.
                    bool sameRoom = door.TargetRoomId == room.RoomId;
                    if (sameRoom || !boxes.TryGetValue(door.TargetRoomId, out var targetRoom))
                    {
                        edge.Stub = true;
                        edge.BothWays = false;
                        edge.ToRoomId = sameRoom ? room.RoomId : "";
                        edge.To = from + Outward(SideFor(door.Position)) * StubLength;
                        edges.Add(edge);
                        continue;
                    }

                    edge.ToRoomId = targetRoom.RoomId;

                    // Land on the partner door when there is one, so a room
                    // with several doors shows which of them the link uses.
                    // orphan-door has no partner, so aim at the nearest point
                    // on the target box instead.
                    var back = DoorValidator.FindDoor(doorsByRoom, door.TargetRoomId, door.TargetDoorId);
                    edge.To = back != null
                        ? AnchorFor(targetRoom.Box, back.Position)
                        : NearestPointOn(targetRoom.Box, from);

                    edges.Add(edge);
                }
            }

            return edges;
        }

        // ====================================================================
        // GEOMETRY
        // ====================================================================

        /// <summary>
        /// Which edge of the room a door belongs to: the nearest one.
        /// </summary>
        // Nearest-edge rather than a table of conventions, because the
        // conventions are not universal — side doors sit at y=112, which is
        // near the bottom, and a rule that checked y first would call them
        // bottom doors. Distance settles it: a door at (0, 112) is 0 from the
        // left edge and 8 from the bottom. Ties go left/right, since a door in
        // a corner is a side door in every room shipped so far.
        public static DoorSide SideFor(Vector2 doorPos)
        {
            const int size = 24;
            float left = doorPos.X;
            float right = RoomWidth - (doorPos.X + size);
            float top = doorPos.Y;
            float bottom = RoomHeight - (doorPos.Y + size);

            float best = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
            if (left <= best) return DoorSide.Left;
            if (right <= best) return DoorSide.Right;
            if (top <= best) return DoorSide.Top;
            return DoorSide.Bottom;
        }

        /// <summary>Unit vector pointing out of the room through that side.</summary>
        public static Vector2 Outward(DoorSide side) => side switch
        {
            DoorSide.Left => new Vector2(-1, 0),
            DoorSide.Right => new Vector2(1, 0),
            DoorSide.Top => new Vector2(0, -1),
            _ => new Vector2(0, 1),
        };

        /// <summary>
        /// Where a door's arrow meets its room box: on the door's own edge, at
        /// the door's own position along that edge.
        /// </summary>
        // Using the door's real position rather than the edge midpoint is what
        // makes a room with a top-left and a top-right door readable — two
        // arrows leaving the same edge from where the doors actually are.
        public static Vector2 AnchorFor(Rectangle box, Vector2 doorPos)
        {
            const int size = 24;
            float cx = Math.Clamp(doorPos.X + size / 2f, 0, RoomWidth);
            float cy = Math.Clamp(doorPos.Y + size / 2f, 0, RoomHeight);

            return SideFor(doorPos) switch
            {
                DoorSide.Left => new Vector2(box.Left, box.Top + cy),
                DoorSide.Right => new Vector2(box.Right, box.Top + cy),
                DoorSide.Top => new Vector2(box.Left + cx, box.Top),
                _ => new Vector2(box.Left + cx, box.Bottom),
            };
        }

        /// <summary>Closest point on a box's outline to an outside point.</summary>
        public static Vector2 NearestPointOn(Rectangle box, Vector2 from) =>
            new(Math.Clamp(from.X, box.Left, box.Right),
                Math.Clamp(from.Y, box.Top, box.Bottom));

        /// <summary>Bounding box of every room box, in map units. Empty for no rooms.</summary>
        public static Rectangle ContentBounds(IReadOnlyList<MapRoom> rooms)
        {
            if (rooms.Count == 0) return Rectangle.Empty;

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var r in rooms)
            {
                var b = r.Box;
                if (b.Left < minX) minX = b.Left;
                if (b.Top < minY) minY = b.Top;
                if (b.Right > maxX) maxX = b.Right;
                if (b.Bottom > maxY) maxY = b.Bottom;
            }
            return new Rectangle(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>The room whose box contains this map point, topmost-last-wins, or null.</summary>
        public static MapRoom? RoomAt(IReadOnlyList<MapRoom> rooms, Vector2 mapPoint)
        {
            // Backwards, so the room drawn last (on top, when boxes have been
            // dragged over each other) is the one a click picks up.
            for (int i = rooms.Count - 1; i >= 0; i--)
                if (rooms[i].Box.Contains((int)mapPoint.X, (int)mapPoint.Y)) return rooms[i];
            return null;
        }
    }
}
