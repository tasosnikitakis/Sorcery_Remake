using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SorceryRemake.Core;

namespace SorceryForge
{
    /// <summary>
    /// A single icon in the palette panel. Drag-source. Defines what to
    /// place when the user picks it up and drops it on the canvas.
    /// </summary>
    public class PaletteEntry
    {
        public string Label;
        public PlacementKind Kind;
        public ItemType ItemType;       // Kind == Item or BlockedDoor (req item)
        public EnemyType EnemyType;     // Kind == Enemy
        public Texture2D Texture;
        public Rectangle SourceRect;

        // Section header to group under in the palette ("WEAPONS",
        // "KEY ITEMS", "ENEMIES", "OTHER"). The order is taken from
        // EditorGame.SectionOrder.
        public string Section = "OTHER";

        // Where this entry sits in the palette panel (filled by PaletteLayout).
        public Rectangle ScreenBounds;

        public PaletteEntry(string label, PlacementKind kind, Texture2D tex, Rectangle src)
        {
            Label = label;
            Kind = kind;
            Texture = tex;
            SourceRect = src;
        }
    }
}
