using Microsoft.Xna.Framework;
using SorceryRemake.Core;

namespace SorceryForge
{
    /// <summary>
    /// What kind of game-object a placement represents. The Subtype field
    /// holds the matching enum value (ItemType / EnemyType / RequiredItem)
    /// or is unused for Wizard.
    /// </summary>
    public enum PlacementKind
    {
        Item,
        Enemy,
        Wizard,
        BlockedDoor,
        Door,
    }

    /// <summary>
    /// A single placed entity in a room. The editor's working set is just a
    /// <c>List&lt;Placement&gt;</c>; saving rebuilds RoomContent from it.
    /// </summary>
    public class Placement
    {
        public string Id;
        public PlacementKind Kind;

        // Only one of these is meaningful per Kind:
        public ItemType ItemType;        // Kind == Item
        public EnemyType EnemyType;      // Kind == Enemy
        public ItemType RequiredItem;    // Kind == BlockedDoor (Lyre, Key, ...)

        // Door fields (Kind == Door):
        public string DoorOpeningSide = "LeftOpening";   // "LeftOpening" | "RightOpening"
        public string DoorTargetRoomId = "";
        public string DoorTargetDoorId = "";

        public Vector2 Position;         // top-left, in 320x144 game space

        public Placement(string id, PlacementKind kind, Vector2 pos)
        {
            Id = id;
            Kind = kind;
            Position = pos;
        }

        /// <summary>Hitbox in game space — every placement renders 24x24.</summary>
        public Rectangle Bounds => new((int)Position.X, (int)Position.Y, 24, 24);

        public string DisplayName => Kind switch
        {
            PlacementKind.Item => ItemType.ToString(),
            PlacementKind.Enemy => EnemyType.ToString(),
            PlacementKind.Wizard => "Wizard",
            PlacementKind.BlockedDoor => $"BlockedDoor[{RequiredItem}]",
            PlacementKind.Door => $"Door({DoorOpeningSide})",
            _ => "?"
        };
    }
}
