// ============================================================================
// DOOR VALIDATOR
// SorceryForge — diagnosing every door link across the whole world
// ============================================================================
// Door wiring is manual and two-way: a door names a targetRoom and a
// targetDoor, and the partner door has to point back. Nothing crashes when it
// doesn't — RoomManager.ExecuteTransition drops the player at the (160, 60)
// fallback and says nothing — so the only way to find a broken link is to look
// for it. That is what this does.
//
// WHY IT IS ITS OWN FILE. Two callers now need the same answers: the Doors
// button, which colours the current room's door outlines, and the world map,
// which colours every arrow on the board. Two copies of these rules would
// drift, and the failure mode of drift here is a map that says a link is fine
// when the button says it is broken. So the rules live here once and both
// callers ask.
//
// UI-FREE AND REGISTRY-FREE. It takes a list of room ids and a table of doors
// and returns a verdict per door — no RoomMeta lookups, no statics, no
// MonoGame beyond Vector2 inside DoorDef. tools/MapCheck therefore drives it
// with synthetic worlds (every failure shape, hand-built) as well as with the
// real one.
//
// THE FIVE VERDICTS
//   ok           the target room exists, holds the named door, and that door
//                points back here. The only fully-wired state.
//   ok-test      the target is room_1 / room_2, which are registered in code
//                by Game1.RegisterTestRooms and are not in the registry. The
//                link is real; its far side simply cannot be checked from here.
//   asymmetric   the partner door exists but targets somewhere else. Walking
//                through this door works; coming back does not land here.
//   orphan-door  the target room exists but has no door with that id.
//   orphan-room  no such room.
// ============================================================================

using System;
using System.Collections.Generic;
using SorceryRemake.Rooms;

namespace SorceryForge
{
    /// <summary>Per-door verdicts plus the tallies the status line reports.</summary>
    public class DoorReport
    {
        /// <summary>Door id → one of the five verdicts above.</summary>
        public readonly Dictionary<string, string> Status = new();

        public int Ok;             // includes ok-test, which the counters treat as fine
        public int OrphanRoom;
        public int OrphanDoor;
        public int Asymmetric;

        public int Bad => OrphanRoom + OrphanDoor + Asymmetric;
        public int Total => Ok + Bad;
    }

    public static class DoorValidator
    {
        /// <summary>
        /// Snapshot of "which doors each room has": the saved layout for every
        /// room, with the CURRENT room's in-memory placements substituted for
        /// its saved doors.
        /// </summary>
        // The substitution is the point. Authoring a door and immediately
        // asking whether it links up has to work before the file is written,
        // or the validators are useless during the one activity they exist to
        // support. Every other room reads from disk because that is genuinely
        // its state.
        public static Dictionary<string, List<DoorDef>> BuildDoorTable(
            IReadOnlyList<RoomMeta> rooms, string currentRoomId, IReadOnlyList<Placement> currentPlacements)
        {
            var table = new Dictionary<string, List<DoorDef>>(StringComparer.Ordinal);
            foreach (var room in rooms)
                table[room.RoomId] = new List<DoorDef>(room.Doors);

            var live = new List<DoorDef>();
            foreach (var p in currentPlacements)
            {
                if (p.Kind != PlacementKind.Door) continue;
                live.Add(new DoorDef(p.Id, p.Position, p.DoorOpeningSide,
                                     p.DoorTargetRoomId, p.DoorTargetDoorId));
            }
            table[currentRoomId] = live;
            return table;
        }

        /// <summary>Room ids in registry order — the order every report walks.</summary>
        public static List<string> RoomIdsOf(IReadOnlyList<RoomMeta> rooms)
        {
            var ids = new List<string>(rooms.Count);
            foreach (var room in rooms) ids.Add(room.RoomId);
            return ids;
        }

        /// <summary>
        /// Diagnose every door in every room. <paramref name="roomIds"/> is
        /// both the set of rooms that exist and the order results are counted
        /// in; <paramref name="doorsByRoom"/> is what BuildDoorTable produced.
        /// </summary>
        public static DoorReport Validate(IReadOnlyList<string> roomIds,
                                          IReadOnlyDictionary<string, List<DoorDef>> doorsByRoom)
        {
            var report = new DoorReport();

            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in roomIds) known.Add(id);

            foreach (string roomId in roomIds)
            {
                if (!doorsByRoom.TryGetValue(roomId, out var doors)) continue;

                foreach (var door in doors)
                {
                    // Test rooms first: they are real rooms that simply are not
                    // in the registry, so the room-exists check below would call
                    // a working link an orphan.
                    if (RoomManifest.TestRoomIds.Contains(door.TargetRoomId))
                    {
                        report.Status[door.DoorId] = "ok-test";
                        report.Ok++;
                        continue;
                    }

                    if (!known.Contains(door.TargetRoomId))
                    {
                        report.Status[door.DoorId] = "orphan-room";
                        report.OrphanRoom++;
                        continue;
                    }

                    var back = FindDoor(doorsByRoom, door.TargetRoomId, door.TargetDoorId);
                    if (back == null)
                    {
                        report.Status[door.DoorId] = "orphan-door";
                        report.OrphanDoor++;
                        continue;
                    }

                    // The partner should name this room AND this door. Naming
                    // only the room is still asymmetric: the player comes back
                    // through a different door and lands somewhere else.
                    if (back.TargetRoomId != roomId || back.TargetDoorId != door.DoorId)
                    {
                        report.Status[door.DoorId] = "asymmetric";
                        report.Asymmetric++;
                        continue;
                    }

                    report.Status[door.DoorId] = "ok";
                    report.Ok++;
                }
            }

            return report;
        }

        /// <summary>The door with this id in this room, or null.</summary>
        public static DoorDef? FindDoor(IReadOnlyDictionary<string, List<DoorDef>> doorsByRoom,
                                        string roomId, string doorId)
        {
            if (!doorsByRoom.TryGetValue(roomId, out var doors)) return null;
            foreach (var d in doors) if (d.DoorId == doorId) return d;
            return null;
        }
    }
}
