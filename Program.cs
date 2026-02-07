// ============================================================================
// PROGRAM ENTRY POINT
// Sorcery+ Remake - Application Bootstrap
// ============================================================================

using System;

namespace SorceryRemake
{
    public static class Program
    {
        [STAThread]
        static void Main()
        {
            using (var game = new Game1())
            {
                game.Run();
            }
        }
    }
}
