// ============================================================================
// ROOM CONTENT LOADER
// Sorcery+ Remake - JSON read/write for per-room content
// ============================================================================
// Bridges the editor (SorceryForge) and the runtime (Game1 → RoomRegistry).
// One file per room: assets/data/content_<roomId>.json. When present, it
// overrides whatever a hardcoded RoomRegistry entry would have produced.
//
// File schema:
// {
//   "roomId": "chateau_1",
//   "items":        [ { "id":"...", "type":"Sword",        "x": 140, "y": 72 }, ... ],
//   "enemies":      [ { "id":"...", "type":"Guard",        "x":  60, "y":112 }, ... ],
//   "wizards":      [ { "id":"...",                        "x": 200, "y": 80 }, ... ],
//   "blockedDoors": [ { "id":"...", "requiredItem":"Lyre", "x": 216, "y": 72 }, ... ]
// }
// ============================================================================

using Microsoft.Xna.Framework;
using SorceryRemake.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SorceryRemake.Rooms
{
    // ------------------------------------------------------------------------
    // JSON DTOs
    // ------------------------------------------------------------------------

    public class RoomContentJson
    {
        public string roomId { get; set; } = "";
        public List<ItemEntryJson> items { get; set; } = new();
        public List<EnemyEntryJson> enemies { get; set; } = new();
        public List<WizardEntryJson> wizards { get; set; } = new();
        public List<BlockedDoorEntryJson> blockedDoors { get; set; } = new();
    }

    public class ItemEntryJson
    {
        public string id { get; set; } = "";
        public string type { get; set; } = "";
        public float x { get; set; }
        public float y { get; set; }
    }

    public class EnemyEntryJson
    {
        public string id { get; set; } = "";
        public string type { get; set; } = "";
        public float x { get; set; }
        public float y { get; set; }
    }

    public class WizardEntryJson
    {
        public string id { get; set; } = "";
        public float x { get; set; }
        public float y { get; set; }
    }

    public class BlockedDoorEntryJson
    {
        public string id { get; set; } = "";
        public string requiredItem { get; set; } = "";
        public float x { get; set; }
        public float y { get; set; }
    }

    // ------------------------------------------------------------------------
    // LOADER / SAVER
    // ------------------------------------------------------------------------

    public static class RoomContentLoader
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>
        /// Default directory for content JSON. In a dev tree (we walk up
        /// from the executable looking for SorceryRemake.csproj), prefer
        /// the repo's source-tree assets/data — this is where SorceryForge
        /// saves, so the running game sees editor saves immediately and
        /// the FileSystemWatcher in Game1 can hot-reload them. Outside a
        /// dev tree (e.g. a published build) we fall back to the path
        /// alongside the executable.
        /// </summary>
        public static string DefaultDir => _resolvedDefaultDir.Value;

        private static readonly Lazy<string> _resolvedDefaultDir = new(() =>
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
            {
                if (File.Exists(Path.Combine(dir, "SorceryRemake.csproj")))
                    return Path.Combine(dir, "assets", "data");
                var parent = Directory.GetParent(dir)?.FullName;
                if (parent == null || parent == dir) break;
                dir = parent;
            }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "data");
        });

        public static string GetPath(string roomId, string? dir = null) =>
            Path.Combine(dir ?? DefaultDir, $"content_{roomId}.json");

        /// <summary>
        /// Load JSON content for a room. Returns null when the file doesn't exist.
        /// Malformed files throw so authoring errors surface immediately.
        /// </summary>
        public static RoomContent? TryLoad(string roomId, string? dir = null)
        {
            string path = GetPath(roomId, dir);
            if (!File.Exists(path)) return null;

            string text = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<RoomContentJson>(text, Options);
            return data == null ? null : ToRoomContent(data);
        }

        public static void Save(string roomId, RoomContent content, string? dir = null)
        {
            string path = GetPath(roomId, dir);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var dto = FromRoomContent(roomId, content);
            string text = JsonSerializer.Serialize(dto, Options);
            File.WriteAllText(path, text);
        }

        // --------------------------------------------------------------------
        // DTO ↔ RoomContent conversion
        // --------------------------------------------------------------------

        public static RoomContent ToRoomContent(RoomContentJson data)
        {
            var result = new RoomContent();

            foreach (var e in data.items)
                if (Enum.TryParse<ItemType>(e.type, ignoreCase: true, out var t))
                    result.Items.Add(new ItemSpawn(e.id, t, new Vector2(e.x, e.y)));

            foreach (var e in data.enemies)
                if (Enum.TryParse<EnemyType>(e.type, ignoreCase: true, out var t))
                    result.Enemies.Add(new EnemySpawn(e.id, t, new Vector2(e.x, e.y)));

            foreach (var e in data.wizards)
                result.Wizards.Add(new WizardSpawn(e.id, new Vector2(e.x, e.y)));

            foreach (var e in data.blockedDoors)
                if (Enum.TryParse<ItemType>(e.requiredItem, ignoreCase: true, out var req))
                    result.BlockedDoors.Add(new BlockedDoorSpawn(e.id, new Vector2(e.x, e.y), req));

            return result;
        }

        public static RoomContentJson FromRoomContent(string roomId, RoomContent content)
        {
            var dto = new RoomContentJson { roomId = roomId };

            foreach (var s in content.Items)
                dto.items.Add(new ItemEntryJson { id = s.Id, type = s.Type.ToString(), x = s.Position.X, y = s.Position.Y });

            foreach (var s in content.Enemies)
                dto.enemies.Add(new EnemyEntryJson { id = s.Id, type = s.Type.ToString(), x = s.Position.X, y = s.Position.Y });

            foreach (var s in content.Wizards)
                dto.wizards.Add(new WizardEntryJson { id = s.Id, x = s.Position.X, y = s.Position.Y });

            foreach (var s in content.BlockedDoors)
                dto.blockedDoors.Add(new BlockedDoorEntryJson
                {
                    id = s.Id,
                    requiredItem = s.RequiredItem.ToString(),
                    x = s.Position.X,
                    y = s.Position.Y
                });

            return dto;
        }
    }
}
