using Microsoft.Xna.Framework;
using SorceryRemake.Rooms;
using System.Collections.Generic;

namespace SorceryForge
{
    /// <summary>
    /// One door belonging to a specific room. Mirrors the on-disk
    /// DoorEntryJson but with strongly-typed fields the editor can mutate
    /// in memory before flushing back to layout_&lt;roomId&gt;.json.
    /// </summary>
    public class DoorDef
    {
        public string DoorId;
        public string TargetRoomId;
        public string TargetDoorId;
        public Vector2 Position;          // 24x24 sprite top-left, in game space
        public string OpeningSide;        // "LeftOpening" or "RightOpening"

        public DoorDef(string doorId, Vector2 pos, string openingSide,
                       string targetRoomId, string targetDoorId)
        {
            DoorId = doorId;
            Position = pos;
            OpeningSide = openingSide;
            TargetRoomId = targetRoomId;
            TargetDoorId = targetDoorId;
        }
    }

    /// <summary>
    /// Editor view of a room. Wraps the main project's RoomManifest (the
    /// catalogue of which rooms exist) and adds an in-memory list of
    /// DoorDef instances loaded from layout_&lt;roomId&gt;.json.
    ///
    /// Doors live in JSON; this object is the editor's working set. When
    /// the user saves a room, we serialise Doors back to layout JSON.
    /// When LoadRoom changes the active room, we re-read the JSON.
    /// </summary>
    public class RoomMeta
    {
        public RoomManifest Manifest { get; }
        public List<DoorDef> Doors { get; private set; } = new();

        /// <summary>
        /// The room's authored player spawn, or null when the layout file
        /// carries none (the game then falls back to
        /// RoomLayoutLoader.DefaultPlayerSpawn). Null and "the default"
        /// are deliberately distinguishable: only the former serialises
        /// back to a file with no "playerSpawn" key.
        /// </summary>
        public Vector2? PlayerSpawn { get; private set; }

        public string RoomId => Manifest.RoomId;
        public string DisplayName => Manifest.DisplayName;
        public string BackgroundAsset => Manifest.BackgroundAsset;
        public string CollisionJsonName => Manifest.CollisionFile;

        public RoomMeta(RoomManifest manifest) { Manifest = manifest; }

        /// <summary>(Re)load doors and spawn from layout_&lt;roomId&gt;.json.</summary>
        public void ReloadLayoutFromDisk()
        {
            (Doors, PlayerSpawn) = LoadLayoutFor(RoomId);
        }

        /// <summary>
        /// Read one room's layout from disk. Used both at startup (to populate
        /// every RoomMeta in All) and after a save in the editor to refresh
        /// in-memory state.
        /// </summary>
        // Doors and spawn come back together from ONE file read on purpose:
        // two separate loaders would parse layout_&lt;id&gt;.json twice and, worse,
        // could drift out of step if only one of them were called.
        public static (List<DoorDef> Doors, Vector2? PlayerSpawn) LoadLayoutFor(string roomId)
        {
            var result = new List<DoorDef>();
            var layout = RoomLayoutLoader.TryLoad(roomId, EditorPaths.RepoAssetsDataDir);
            if (layout == null) return (result, null);
            foreach (var d in layout.doors)
            {
                result.Add(new DoorDef(
                    d.id,
                    new Vector2(d.x, d.y),
                    d.type,
                    d.targetRoom,
                    d.targetDoor));
            }
            Vector2? spawn = layout.playerSpawn == null
                ? null
                : new Vector2(layout.playerSpawn.x, layout.playerSpawn.y);
            return (result, spawn);
        }

        // --------------------------------------------------------------------
        // STATIC CATALOGUE
        // --------------------------------------------------------------------
        // Built once at startup from the main project's RoomManifest.All.
        // Doors and spawns are loaded from layout_*.json at the same time.
        // Adding a new shipping room is therefore a single edit to
        // RoomManifest.All — both projects pick it up.
        // --------------------------------------------------------------------

        public static readonly List<RoomMeta> All = BuildAll();

        private static List<RoomMeta> BuildAll()
        {
            var list = new List<RoomMeta>();
            foreach (var manifest in RoomManifest.All)
            {
                var meta = new RoomMeta(manifest);
                meta.ReloadLayoutFromDisk();
                list.Add(meta);
            }
            return list;
        }

        public static RoomMeta? Find(string roomId)
        {
            foreach (var r in All) if (r.RoomId == roomId) return r;
            return null;
        }
    }
}
