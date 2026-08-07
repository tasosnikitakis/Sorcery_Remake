using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SorceryRemake.Core;
using SorceryRemake.Doors;

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

        // Kind == Door: the LOGICAL opening side, i.e. exactly what gets
        // written to layout JSON's "type" field and read back as DoorType.
        // Null for every other kind.
        //
        // NOT the texture's hinge side. The PNG names describe the hinge, so
        // a LeftOpening door renders RightDoorFrames.png (see the mirror
        // comment in EditorGame.BuildPalette and RoomManager.LoadRoom). Read
        // this field, never the Texture, when you need the side.
        //
        // Before this existed, the side was recovered by sniffing Label for
        // the substring "LeftOpening" — which made a display string load
        // bearing, so renaming a palette label silently broke door placement.
        public DoorType? DoorOpeningSide;

        // Section header to group under in the palette ("WEAPONS",
        // "KEY ITEMS", "ENEMIES", "OTHER"). The order is taken from
        // EditorGame.SectionOrder.
        public string Section = "OTHER";

        // Where this entry sits in the palette panel, filled by
        // EditorGame.LayoutPalette. This is the UNSCROLLED position — add the
        // palette scroll offset before drawing or hit-testing, which
        // EditorGame.PaletteRowRect does for both.
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
