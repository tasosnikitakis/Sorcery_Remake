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
        /// Draw the door. Source is 48x48 in spritesheet, rendered at 24x24
        /// game size.
        ///
        /// Background masking: rooms whose backgrounds contain a baked-in
        /// drawn door (extracted from original-game screenshots) would let
        /// that BG visual bleed through during the open/close animation.
        /// To hide it, we first draw the union of every animation frame at
        /// the same destination tinted Color.Black. SpriteBatch tints
        /// multiply RGB but preserve alpha, so the sprite's transparent
        /// pixels stay transparent — only the panel area becomes black.
        /// The arch / wall / decoration surrounding the door in the BG is
        /// untouched. Then the actual current frame is drawn on top.
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

            // Silhouette mask: union of all frames' opacity, painted black.
            // The sprite's own alpha channel acts as the mask shape so we
            // never blacken a pixel the artist intended to be transparent.
            foreach (var frame in _frames)
                spriteBatch.Draw(Texture, destRect, frame, Color.Black);

            spriteBatch.Draw(Texture, destRect, sourceRect, Color.White);
        }
    }
}
