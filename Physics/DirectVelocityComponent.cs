// ============================================================================
// DIRECT VELOCITY COMPONENT - AMSTRAD CPC AUTHENTIC PHYSICS
// Sorcery+ Remake - Matches Python Prototype EXACTLY
// ============================================================================
// The original Amstrad CPC game did NOT use force-based physics.
// It used DIRECT VELOCITY ASSIGNMENT:
// - Press key → instant velocity (no acceleration)
// - Release key → instant stop (no damping)
// - Idle → constant downward velocity (gravity)
//
// This matches the Python prototype's handle_input_and_movement() method.
// ============================================================================

using Microsoft.Xna.Framework;
using SorceryRemake.Core;

namespace SorceryRemake.Physics
{
    /// <summary>
    /// Direct velocity physics matching the original Amstrad CPC behavior.
    /// No forces, no acceleration, no damping - just instant velocity changes.
    /// </summary>
    public class DirectVelocityComponent : IComponent
    {
        // ====================================================================
        // COMPONENT INTERFACE
        // ====================================================================

        public Entity? Owner { get; set; }

        // ====================================================================
        // PHYSICS CONSTANTS (From Python settings.py)
        // ====================================================================

        /// <summary>
        /// Movement speed when arrow keys are pressed (pixels/second).
        /// Python: PLAYER_SPEED_PPS = 500
        /// Applies to: Left, Right, Up, Down keys
        /// </summary>
        public float Speed { get; set; } = 500f;

        /// <summary>
        /// Gravity speed when idle (pixels/second downward).
        /// Python: PLAYER_GRAVITY_PPS = 300
        /// Applied when no vertical key is pressed.
        /// </summary>
        public float GravitySpeed { get; set; } = 300f;

        // ====================================================================
        // PHYSICS STATE
        // ====================================================================

        /// <summary>
        /// Current velocity (pixels/second).
        /// Set DIRECTLY by input, no integration.
        /// </summary>
        public Vector2 Velocity { get; set; }

        /// <summary>
        /// Is the player on the ground?
        /// (For future platform collision - Phase 2)
        /// </summary>
        public bool IsOnGround { get; set; }

        // ====================================================================
        // CONSTRUCTOR
        // ====================================================================

        public DirectVelocityComponent()
        {
            Velocity = Vector2.Zero;
            IsOnGround = false;
        }

        // ====================================================================
        // PUBLIC API
        // ====================================================================

        /// <summary>
        /// Set horizontal velocity directly (no acceleration).
        /// </summary>
        public void SetHorizontalVelocity(float velocityX)
        {
            Velocity = new Vector2(velocityX, Velocity.Y);
        }

        /// <summary>
        /// Set vertical velocity directly (no acceleration).
        /// </summary>
        public void SetVerticalVelocity(float velocityY)
        {
            Velocity = new Vector2(Velocity.X, velocityY);
        }

        // ====================================================================
        // UPDATE - POSITION INTEGRATION
        // ====================================================================

        public void Update(GameTime gameTime)
        {
            if (Owner == null) return;

            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // ----------------------------------------------------------------
            // STEP 1: Integrate velocity into position
            // Velocity is set DIRECTLY by PlayerController, not by forces
            // ----------------------------------------------------------------
            Owner.Position += Velocity * deltaTime;

            // ----------------------------------------------------------------
            // STEP 2: Apply screen boundaries (simple clamping for Phase 1)
            // Python: apply_screen_boundaries() in player.py
            // ----------------------------------------------------------------
            Vector2 pos = Owner.Position;
            Vector2 vel = Velocity;

            // Horizontal bounds (0 to WINDOW_WIDTH)
            // Note: WINDOW_WIDTH = 960 in Python (320 * 3)
            // We use 320 base units in our coordinate space
            if (pos.X < 0)
            {
                pos.X = 0;
                vel.X = 0;
            }
            else if (pos.X > 320 - 24) // 320 - sprite_width
            {
                pos.X = 320 - 24;
                vel.X = 0;
            }

            // Vertical bounds (0 to GAME_AREA_HEIGHT)
            // Python: GAME_AREA_HEIGHT = 432 (144 * 3) in scaled space
            // In base units: 144
            if (pos.Y < 0)
            {
                pos.Y = 0;
                vel.Y = 0;
            }
            else if (pos.Y > 144 - 24) // 144 - sprite_height
            {
                pos.Y = 144 - 24;
                vel.Y = 0;
                IsOnGround = true;
            }
            else
            {
                IsOnGround = false;
            }

            // Apply modified values back
            Owner.Position = pos;
            Velocity = vel;
        }

        public void Draw(GameTime gameTime)
        {
            // Physics components don't render
        }
    }
}
