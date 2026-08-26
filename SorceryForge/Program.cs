using System;

namespace SorceryForge
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // --imgui-probe: draw the input-routing readout (EditorGame.
            // DrawRoutingProbe). A debug switch, not a feature — the editor has
            // no other command-line surface and should not grow one casually.
            foreach (var arg in args)
                if (arg == "--imgui-probe") EditorGame.ImGuiProbe = true;

            using var game = new EditorGame();
            game.Run();
        }
    }
}
