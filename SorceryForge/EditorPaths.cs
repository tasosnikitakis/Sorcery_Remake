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
