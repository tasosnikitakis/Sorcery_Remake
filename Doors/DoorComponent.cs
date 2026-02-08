using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SorceryRemake.Doors
{
    public enum DoorState
    {
        Closed,   // Idle, waiting for player
        Opening,  // Animation playing, game frozen
        Open      // Animation done, ready to transition
    }

    /// <summary>
    /// A door that transports the player to another room.
    /// Triggered when the player is fully aligned with the door's active side.
    /// </summary>
    public class DoorComponent
    {
        // Door properties
        public DoorType Type { get; }
        public Vector2 Position { get; }
        public DoorState State { get; private set; }

        // Destination
        public string TargetRoomId { get; set; } = "";
        public string TargetDoorId { get; set; } = "";
        public string DoorId { get; set; } = "";

        // Animation state
        private int _currentFrame;
        private float _frameTimer;
        private readonly Rectangle[] _frames;

        // Texture reference (set externally)
        public Texture2D? Texture { get; set; }

        public DoorComponent(DoorType type, Vector2 position)
        {
            Type = type;
            Position = position;
            State = DoorState.Closed;
            _currentFrame = 0;
            _frameTimer = 0f;
            _frames = DoorConfig.GetFrames(type);
        }

        /// <summary>
        /// Check if the player is aligned with this door's active side.
        /// Both player and door are 24x24, so Y must match directly.
        /// </summary>
        public bool IsPlayerAligned(Vector2 playerPos, int playerWidth, int playerHeight)
        {
            if (State != DoorState.Closed) return false;

            // Y must match (both are 24px tall, standing on same floor)
            float yDiff = System.Math.Abs(playerPos.Y - Position.Y);
            if (yDiff > 2f) return false;

            if (Type == DoorType.LeftOpening)
            {
                // Player approaches from the left: player's right edge at door's left edge
                float playerRight = playerPos.X + playerWidth;
                float doorLeft = Position.X;
                return System.Math.Abs(playerRight - doorLeft) < 3f;
            }
            else
            {
                // Player approaches from the right: player's left edge at door's right edge
                float playerLeft = playerPos.X;
                float doorRight = Position.X + DoorConfig.DOOR_WIDTH;
                return System.Math.Abs(playerLeft - doorRight) < 3f;
            }
        }

        public void StartOpening()
        {
            if (State != DoorState.Closed) return;
            State = DoorState.Opening;
            _currentFrame = 0;
            _frameTimer = 0f;
        }

        public bool Update(float deltaTime)
        {
            if (State != DoorState.Opening) return false;

            _frameTimer += deltaTime;
            if (_frameTimer >= DoorConfig.FRAME_DURATION)
            {
                _frameTimer -= DoorConfig.FRAME_DURATION;
                _currentFrame++;

                if (_currentFrame >= DoorConfig.FRAME_COUNT)
                {
                    State = DoorState.Open;
                    _currentFrame = DoorConfig.FRAME_COUNT - 1;
                    return true;
                }
            }
            return false;
        }

        public void Reset()
        {
            State = DoorState.Closed;
            _currentFrame = 0;
            _frameTimer = 0f;
        }

        /// <summary>
        /// Player arrives on the TRIGGER side, offset 5px to avoid re-trigger.
        /// </summary>
        public Vector2 GetArrivalPosition(int playerWidth)
        {
            if (Type == DoorType.LeftOpening)
            {
                return new Vector2(Position.X - playerWidth - 5, Position.Y);
            }
            else
            {
                return new Vector2(Position.X + DoorConfig.DOOR_WIDTH + 5, Position.Y);
            }
        }

        public Rectangle GetCurrentSourceRect()
        {
            return _frames[_currentFrame];
        }

        /// <summary>
        /// Draw the door. Source is 48x48 in spritesheet, rendered at 24x24 game size.
        /// </summary>
        public void Draw(SpriteBatch spriteBatch, float scale)
        {
            if (Texture == null) return;

            Vector2 renderPos = Position * scale;
            Rectangle sourceRect = GetCurrentSourceRect();

            // Destination rect: 24x24 game size * render scale
            Rectangle destRect = new Rectangle(
                (int)renderPos.X, (int)renderPos.Y,
                (int)(DoorConfig.DOOR_WIDTH * scale),
                (int)(DoorConfig.DOOR_HEIGHT * scale)
            );

            spriteBatch.Draw(Texture, destRect, sourceRect, Color.White);
        }
    }
}
