using Microsoft.Xna.Framework;

namespace SorceryRemake.Doors
{
    public enum DoorType
    {
        LeftOpening,  // Triggered from the left side, opens left
        RightOpening  // Triggered from the right side, opens right
    }

    public static class DoorConfig
    {
        // Door size in game world (same as player: 24x24)
        public const int DOOR_WIDTH = 24;
        public const int DOOR_HEIGHT = 24;

        // Sprite source size (48x48 in spritesheet, scaled down to 24x24 when drawn)
        public const int SPRITE_WIDTH = 48;
        public const int SPRITE_HEIGHT = 48;

        // Animation timing
        public const float FRAME_DURATION = 0.15f; // seconds per frame
        public const int FRAME_COUNT = 4;

        // Frame source rectangles for LeftDoorFrames.png (192x48, 4 frames in a row)
        public static readonly Rectangle[] LEFT_DOOR_FRAMES = new Rectangle[]
        {
            new Rectangle(  0, 0, 48, 48),  // Frame 0: closed
            new Rectangle( 48, 0, 48, 48),  // Frame 1: slightly open
            new Rectangle( 96, 0, 48, 48),  // Frame 2: more open
            new Rectangle(144, 0, 48, 48),  // Frame 3: fully open
        };

        // Frame source rectangles for RightDoorFrames.png (192x48, mirrored left frames)
        public static readonly Rectangle[] RIGHT_DOOR_FRAMES = new Rectangle[]
        {
            new Rectangle(  0, 0, 48, 48),  // Frame 0: closed
            new Rectangle( 48, 0, 48, 48),  // Frame 1: slightly open
            new Rectangle( 96, 0, 48, 48),  // Frame 2: more open
            new Rectangle(144, 0, 48, 48),  // Frame 3: fully open
        };

        public static Rectangle GetClosedFrame(DoorType type)
        {
            return type == DoorType.LeftOpening
                ? LEFT_DOOR_FRAMES[0]
                : RIGHT_DOOR_FRAMES[0];
        }

        public static Rectangle[] GetFrames(DoorType type)
        {
            return type == DoorType.LeftOpening
                ? LEFT_DOOR_FRAMES
                : RIGHT_DOOR_FRAMES;
        }
    }
}
