// ============================================================================
// ROOM PROPERTIES
// SorceryForge — editing what rooms.json says about a room
// ============================================================================
// One property is editable, and the boundary is the interesting part.
//
// DISPLAY NAME: yes. It is cosmetic — the editor's room title, the game's HUD,
// the world map's labels — and nothing keys off it. Until PR 7b the only way to
// change one was to hand-edit rooms.json, because the room's name is DERIVED
// from its background PNG's filename at creation time and there was no text
// field anywhere in the editor to change it afterwards. "Rename the file, get a
// different room" is a fine rule for creating a room and a bad one for fixing a
// typo in a room that already has content in it.
//
// ROOM ID: NO, and not as a simplification. An id is:
//
//   a persistence key    WorldState remembers PickedUpItems / DeadEnemies /
//                        SavedWizards / UnlockedDoors as sets of ENTITY ids,
//                        and every entity id is built from its room's id. A
//                        rename orphans every one of them — silently, since a
//                        set lookup that misses just means "not picked up yet"
//   three file names     content_<id>.json, layout_<id>.json, collision_<id>.json
//   a cross-room link    every door in every OTHER room that targets this one
//                        names it in its targetRoom, and the validator's
//                        orphan-room verdict is the only thing that would
//                        notice
//   a map key            worldmap.json keys stored positions by room id
//
// Doing it properly means a migration — rewrite three files, rewrite every
// referring door, rewrite the map, and decide what happens to a save file. That
// is a tool, not a text field, and a text field that did a third of it would be
// worse than none. doc/07 says so where an author would look for it.
//
// UI-FREE, like NewRoomFlow: filesystem and string work with the directory
// passed in, so tools/EditCheck can drive it against a scratch copy instead of
// only by clicking. EditorGame owns the inspector's ROOM block and calls
// Rename once.
// ============================================================================

using SorceryRemake.Rooms;
using System;
using System.Collections.Generic;
using System.IO;

namespace SorceryForge
{
    public static class RoomProperties
    {
        /// <summary>
        /// Long enough for every shipped name with room to spare, short enough
        /// that the top bar's title and the map board's labels stay readable.
        /// </summary>
        public const int MaxDisplayNameLength = 48;

        /// <summary>
        /// Why <paramref name="name"/> cannot be a room's display name, or null
        /// if it can.
        /// </summary>
        // EMPTY IS THE ONE THAT MATTERS, and it is refused HERE rather than
        // left to the writer, because the writer would serialise it happily and
        // the LOADER would then substitute the room id for it
        // (RoomManifest.LoadAll's "displayName is cosmetic; fall back to the id"
        // branch). So an empty rename would look like it worked and would
        // quietly rename the room to its own id — a change nobody asked for, in
        // a file nobody was watching.
        //
        // Newlines and tabs are refused for a duller reason: rooms.json is
        // written one entry per line, and a name containing a newline would be
        // escaped into the JSON correctly and then read back as a name with a
        // \n in it, which every label in two applications would render as a box.
        public static string? CheckDisplayName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "a room's display name cannot be empty";

            if (name.Length > MaxDisplayNameLength)
                return $"a display name may be at most {MaxDisplayNameLength} characters";

            foreach (char c in name)
                if (char.IsControl(c))
                    return "a display name cannot contain control characters";

            return null;
        }

        /// <summary>What Rename actually did, for the status line.</summary>
        public class RenameResult
        {
            public bool Ok;
            public string Message = "";

            /// <summary>False when the name was already what was asked for.</summary>
            public bool Changed;
        }

        /// <summary>
        /// Rewrite one room's displayName in the rooms.json inside
        /// <paramref name="dataDir"/>, leaving every other field and the array
        /// order exactly as they were.
        /// </summary>
        // READS THE REGISTRY FRESH, from the directory it is about to write, for
        // the same reason NewRoomFlow.Create does: the cached RoomManifest.All
        // is a snapshot, and rewriting a whole file from a snapshot discards
        // anything that reached the file since. Here that would mean a rename
        // silently deleting a room somebody had just created.
        //
        // The write goes through RoomManifest.Save, which re-emits the header
        // comment verbatim and re-aligns the columns — the alternative,
        // JsonSerializer.Serialize, would drop the header and take the ordering
        // rule with it. tools/EditCheck pins both.
        public static RenameResult Rename(string roomId, string displayName, string dataDir)
        {
            var result = new RenameResult();

            string? problem = CheckDisplayName(displayName);
            if (problem != null)
            {
                result.Message = $"Rename refused: {problem}.";
                return result;
            }

            string path = Path.Combine(dataDir, "rooms.json");

            try
            {
                var registry = RoomManifest.LoadFrom(path);

                var rewritten = new List<RoomManifest>(registry.Count);
                string? oldName = null;
                foreach (var r in registry)
                {
                    if (r.RoomId != roomId) { rewritten.Add(r); continue; }
                    oldName = r.DisplayName;
                    rewritten.Add(new RoomManifest(r.RoomId, displayName,
                                                   r.BackgroundAsset, r.CollisionFile));
                }

                if (oldName == null)
                {
                    // Only reachable if the registry changed under the editor,
                    // which is exactly the case the fresh read exists to notice.
                    result.Message = $"Rename failed: '{roomId}' is not in {path}.";
                    return result;
                }

                if (oldName == displayName)
                {
                    // Not an error, and deliberately not a write: the field
                    // reports every deactivation, including the one after
                    // Escape has reverted the text, and a no-op rename must not
                    // touch the file's timestamp or the git status.
                    result.Ok = true;
                    result.Changed = false;
                    result.Message = $"{roomId} is already called \"{displayName}\".";
                    return result;
                }

                RoomManifest.Save(rewritten, path);

                result.Ok = true;
                result.Changed = true;
                result.Message = $"Renamed {roomId}: \"{oldName}\" -> \"{displayName}\" (rooms.json).";
            }
            catch (Exception ex)
            {
                result.Message = $"Rename failed: {ex.Message}";
            }
            return result;
        }
    }
}
