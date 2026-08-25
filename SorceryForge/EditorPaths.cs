using System;
using System.IO;

namespace SorceryForge
{
    /// <summary>
    /// Resolves on-disk paths the editor needs. The editor's bin/ output
    /// receives copies of the assets at build time, but the editor SAVES
    /// JSON files into the repo's source tree so the main game (and git)
    /// see the changes.
    /// </summary>
    public static class EditorPaths
    {
        /// <summary>Repository root (directory containing SorceryRemake.csproj).</summary>
        public static string RepoRoot { get; } = FindRepoRoot();

        /// <summary>Source-tree assets/data folder. Saves go here.</summary>
        public static string RepoAssetsDataDir => Path.Combine(RepoRoot, "assets", "data");

        /// <summary>Source-tree Content folder (raw PNGs).</summary>
        public static string RepoContentDir => Path.Combine(RepoRoot, "Content");

        /// <summary>
        /// Drop folder the screenshot import scans. Source captures land here;
        /// the import reads them and writes a PNG into Content/. Its image
        /// files are gitignored — they are inputs, never repository content.
        /// </summary>
        public static string RepoImportDir => Path.Combine(RepoRoot, "assets", "import");

        /// <summary>
        /// Personal workspace state for the editor — crop presets today, more
        /// later. Gitignored, and deliberately NOT under assets/data.
        /// </summary>
        // assets/data is the world: everything in it is a shared decision, and
        // a merge conflict there is a real conflict about the game. What lives
        // here is one person's convenience — which rectangle their emulator
        // happens to put the playfield at — and it must never be able to gate
        // someone else's clone. Separate folder, separate gitignore line, no
        // loader in the game reads it. See SorceryForge/EditorSettings.cs.
        public static string RepoSettingsDir => Path.Combine(RepoRoot, ".sorceryforge");

        private static string FindRepoRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
            {
                if (File.Exists(Path.Combine(dir, "SorceryRemake.csproj")))
                    return dir;
                var parent = Directory.GetParent(dir)?.FullName;
                if (parent == null || parent == dir) break;
                dir = parent;
            }
            // Fall back to the executable's directory if we can't find the repo.
            return AppDomain.CurrentDomain.BaseDirectory;
        }
    }
}
