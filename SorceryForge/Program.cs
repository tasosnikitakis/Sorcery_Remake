using System;

namespace SorceryForge
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            using var game = new EditorGame();
            game.Run();
        }
    }
}
