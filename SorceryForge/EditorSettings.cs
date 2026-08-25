// ============================================================================
// EDITOR SETTINGS
// SorceryForge — .sorceryforge/settings.json, one person's working preferences
// ============================================================================
// PERSONAL WORKSPACE STATE, AND NOTHING ELSE. Everything the editor writes into
// assets/data is a shared decision about the game — where a door goes, which
// rooms exist — and a conflict there is a real conflict two people have to
// talk about. This file is the other kind: which rectangle YOUR emulator puts
// the playfield at, and later whatever else turns out to be personal. It sits
// at the repo root in .sorceryforge/ and it is GITIGNORED, deliberately, so it
// can never gate a clone or collide in a merge. Deleting it costs one re-frame.
//
// (The world map's positions went the other way — those are a shared picture
// of the world's shape and live in assets/data/worldmap.json. The distinction
// is "would another person want this?", not "is it convenient?".)
//
// BORN-EMPTY DISCIPLINE, the same rule WorldMapFile and the room loaders
// follow, and it is easy to get backwards: "don't CREATE an empty file" is not
// "don't WRITE one".
//
//   - nothing stored and no file yet   -> write nothing, so a fresh clone
//                                         that never crops stays clean
//   - nothing stored but a file EXISTS -> write it anyway, because emptying
//                                         it was a deliberate act
//
// UNKNOWN KEYS ARE PRESERVED. This file is expected to grow — a later PR will
// put other preferences beside cropPresets — and an older build must not eat a
// newer build's settings. Every top-level member this version does not
// recognise is kept as compact JSON and re-emitted verbatim. (worldmap.json
// went the opposite way, dropping unknown room ids, for the opposite reason:
// its content is a set of deliberate acts about rooms that exist, and stale
// entries there are silt. Here the unknown key IS the point.)
//
// FORMAT — house style, so a diff reads:
//   {
//     "cropPresets": {
//       "384x270":   { "x": 32, "y": 41, "w": 320, "h": 144 },
//       "1920x1080": { "x": 96, "y": 32, "w": 960, "h": 432 }
//     }
//   }
// Keys in ordinal order, columns aligned. Load -> save with no change is
// byte-identical for any file this writer produced; tools/ImportCheck asserts
// it, the way MapCheck does for worldmap.json. (A HAND-written file is
// re-formatted on the first save, which is the same deal every other JSON
// writer in this tree offers.)
// ============================================================================

using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace SorceryForge
{
    public class EditorSettings
    {
        public const string DirName = ".sorceryforge";
        public const string FileName = "settings.json";

        /// <summary>The one member this version understands.</summary>
        private const string CropPresetsKey = "cropPresets";

        // Crop rectangles, keyed by "<width>x<height>" of the SOURCE they were
        // cut from. Ordinal comparison and an ordinal sort on write, so the
        // file's line order is stable across saves and a new preset is a
        // one-line diff.
        private readonly Dictionary<string, Rectangle> _cropPresets = new(StringComparer.Ordinal);

        // Top-level members this version does not recognise, as compact JSON
        // text. Kept as text rather than as JsonElement on purpose: the
        // JsonDocument they came from is disposed at the end of Load, and a
        // JsonElement outliving its document is a use-after-free waiting to
        // happen.
        private readonly Dictionary<string, string> _unknown = new(StringComparer.Ordinal);

        /// <summary>How many unknown members are being carried through.</summary>
        public int UnknownMemberCount => _unknown.Count;

        /// <summary>How many crop presets are stored.</summary>
        public int CropPresetCount => _cropPresets.Count;

        public static string GetPath(string? dir = null) =>
            Path.Combine(dir ?? EditorPaths.RepoSettingsDir, FileName);

        // ====================================================================
        // CROP PRESETS
        // ====================================================================

        /// <summary>The key a source of these dimensions is stored under.</summary>
        public static string CropKey(int srcW, int srcH) =>
            string.Create(CultureInfo.InvariantCulture, $"{srcW}x{srcH}");

        /// <summary>
        /// The stored rectangle for a source this size, or null if there is
        /// none. Null is the value ImageImport.ResolveCropRect wants, which is
        /// why this is a nullable return rather than a Try pattern.
        /// </summary>
        public Rectangle? CropPreset(int srcW, int srcH) =>
            _cropPresets.TryGetValue(CropKey(srcW, srcH), out var rect) ? rect : null;

        /// <summary>
        /// Remember this rectangle for sources of this size. Last-used wins:
        /// the newest confirmed crop is always the one that comes back.
        /// </summary>
        public void SetCropPreset(int srcW, int srcH, Rectangle rect) =>
            _cropPresets[CropKey(srcW, srcH)] = rect;

        // ====================================================================
        // LOAD
        // ====================================================================

        /// <summary>
        /// The settings on disk. An absent file is not an error — it is the
        /// normal state until someone confirms their first crop.
        /// </summary>
        // A malformed file is not fatal either, for WorldMapFile's reason: this
        // is a convenience, and refusing to start the editor over one would be
        // absurd. The caller reports and carries on with defaults. Note the
        // whole file is dropped in that case rather than partially applied —
        // half-read settings are harder to reason about than none.
        public static EditorSettings Load(string? dir, out string? error)
        {
            error = null;
            var settings = new EditorSettings();

            string path = GetPath(dir);
            if (!File.Exists(path)) return settings;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
                {
                    // Same reader settings as every other JSON file in the
                    // tree: hand-editable, so a comment or a trailing comma in
                    // it must not cost the user their presets.
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });

                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    throw new FormatException("the root of the file is not a JSON object");

                foreach (var member in doc.RootElement.EnumerateObject())
                {
                    if (member.NameEquals(CropPresetsKey) && member.Value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var entry in member.Value.EnumerateObject())
                            if (TryReadRect(entry.Value, out var rect))
                                settings._cropPresets[entry.Name] = rect;
                        continue;
                    }

                    // Anything else — including a cropPresets that is not an
                    // object, which a future schema might legitimately make it
                    // — is carried through untouched rather than dropped.
                    settings._unknown[member.Name] = JsonSerializer.Serialize(member.Value);
                }
            }
            catch (Exception ex)
            {
                error = $"{DirName}/{FileName} is unreadable ({ex.Message}) — defaults are in use.";
                return new EditorSettings();
            }
            return settings;
        }

        /// <summary>A { x, y, w, h } object of whole numbers, or nothing.</summary>
        // Strict on purpose. A malformed entry is skipped rather than
        // guessed at, because a half-read rectangle would open the crop step
        // somewhere the user did not put it and look like the editor lost
        // their framing.
        private static bool TryReadRect(JsonElement element, out Rectangle rect)
        {
            rect = Rectangle.Empty;
            if (element.ValueKind != JsonValueKind.Object) return false;
            if (!TryReadInt(element, "x", out int x)) return false;
            if (!TryReadInt(element, "y", out int y)) return false;
            if (!TryReadInt(element, "w", out int w)) return false;
            if (!TryReadInt(element, "h", out int h)) return false;
            rect = new Rectangle(x, y, w, h);
            return true;
        }

        private static bool TryReadInt(JsonElement obj, string name, out int value)
        {
            value = 0;
            return obj.TryGetProperty(name, out var property)
                && property.ValueKind == JsonValueKind.Number
                && property.TryGetInt32(out value);
        }

        // ====================================================================
        // SAVE
        // ====================================================================

        /// <summary>
        /// Write the file. Returns true if a file was written, false if the
        /// write was deliberately skipped (nothing to store, and no file to
        /// update).
        /// </summary>
        public bool Save(string? dir = null)
        {
            string path = GetPath(dir);
            if (_cropPresets.Count == 0 && _unknown.Count == 0 && !File.Exists(path)) return false;

            var members = new List<string>();
            if (_cropPresets.Count > 0) members.Add(RenderCropPresets());

            var unknownNames = new List<string>(_unknown.Keys);
            unknownNames.Sort(StringComparer.Ordinal);
            foreach (string name in unknownNames)
                members.Add($"  {Quote(name)}: {_unknown[name]}");

            string nl = Environment.NewLine;   // CRLF, matching every other JSON writer in the tree
            var sb = new StringBuilder();
            sb.Append('{').Append(nl);
            for (int i = 0; i < members.Count; i++)
                sb.Append(members[i]).Append(i < members.Count - 1 ? "," : "").Append(nl);
            sb.Append('}').Append(nl);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, sb.ToString());
            return true;
        }

        /// <summary>The cropPresets member, one preset per line, columns aligned.</summary>
        // Column widths are computed across the whole table — the quoted key
        // and each of the four numbers — the way RoomManifest.Save and
        // WorldMapFile.Save do it, so adding a preset is a one-line diff
        // instead of a re-alignment of the whole block. (A preset wider or
        // taller than every existing one re-pads that column once; stable
        // afterwards. Same deal rooms.json offers for a long room id.)
        private string RenderCropPresets()
        {
            var keys = new List<string>(_cropPresets.Keys);
            keys.Sort(StringComparer.Ordinal);

            int keyWidth = 0, xw = 0, yw = 0, ww = 0, hw = 0;
            foreach (string key in keys)
            {
                var r = _cropPresets[key];
                keyWidth = Math.Max(keyWidth, Quote(key).Length + 1);   // + the colon
                xw = Math.Max(xw, Num(r.X).Length);
                yw = Math.Max(yw, Num(r.Y).Length);
                ww = Math.Max(ww, Num(r.Width).Length);
                hw = Math.Max(hw, Num(r.Height).Length);
            }

            string nl = Environment.NewLine;
            var sb = new StringBuilder();
            sb.Append("  ").Append(Quote(CropPresetsKey)).Append(": {").Append(nl);
            for (int i = 0; i < keys.Count; i++)
            {
                var r = _cropPresets[keys[i]];
                sb.Append("    ").Append((Quote(keys[i]) + ":").PadRight(keyWidth + 1))
                  .Append(" { \"x\": ").Append(Num(r.X).PadLeft(xw))
                  .Append(", \"y\": ").Append(Num(r.Y).PadLeft(yw))
                  .Append(", \"w\": ").Append(Num(r.Width).PadLeft(ww))
                  .Append(", \"h\": ").Append(Num(r.Height).PadLeft(hw))
                  .Append(" }").Append(i < keys.Count - 1 ? "," : "").Append(nl);
            }
            sb.Append("  }");
            return sb.ToString();
        }

        /// <summary>Delete the file, if there is one. Returns true if it went.</summary>
        public static bool Delete(string? dir = null)
        {
            string path = GetPath(dir);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }

        private static string Quote(string value) => JsonSerializer.Serialize(value ?? "");

        // Invariant culture, always: a machine set to a locale with a
        // different digit grouping would otherwise write JSON no parser
        // accepts. Same guard WorldMapFile.Number carries.
        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
