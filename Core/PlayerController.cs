// ============================================================================
// PLAYER CONTROLLER COMPONENT
// Sorcery+ Remake - Player Input and Animation Control
// ============================================================================
// Handles keyboard input and translates it into physics forces and
// animation state changes.
//
// ORIGINAL CONTROLS:
// - Arrow Keys: Movement (Up = Thrust, Down/Left/Right = Directional)
// - The player constantly fights gravity with the Up key
// - No "jump" button - it's pure flight physics
// ============================================================================

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using SorceryRemake.Core;
using SorceryRemake.Physics;
using SorceryRemake.Graphics;

namespace SorceryRemake.Core
{
    /// <summary>
    /// Handles player input and controls the player entity.
    /// </summary>
    public class PlayerController : IComponent
    {
        // ====================================================================
        // COMPONENT INTERFACE
        // ====================================================================

        public Entity? Owner { get; set; }

        // ====================================================================
        // COMPONENT REFERENCES
        // Cached for performance
        // ====================================================================

        private PhysicsComponent? _physics;
        private SpriteComponent? _sprite;

        // ====================================================================
        // INPUT STATE
        // ====================================================================

        private KeyboardState _previousKeyState;
        private KeyboardState _currentKeyState;

        // ====================================================================
        // ANIMATION STATE TRACKING
        // ====================================================================

        private enum PlayerAnimState
        {
            Idle,
            FlyingLeft,
            FlyingRight,
            FlyingUp,
            Falling
        }

        private PlayerAnimState _currentAnimState = PlayerAnimState.Idle;

        // ====================================================================
        // INITIALIZATION
        // ====================================================================

        public void Initialize()
        {
            if (Owner == null) return;

            // Cache component references
            _physics = Owner.GetComponent<PhysicsComponent>();
            _sprite = Owner.GetComponent<SpriteComponent>();

            // Set initial animation to idle_front (Python prototype name)
            if (_sprite != null)
            {
                _sprite.SetAnimation(
                    SpriteConfig.PLAYER_IDLE_FRONT,
                    SpriteConfig.PLAYER_IDLE_ANIMATION_SPEED,
                    loop: true
                );
            }
        }

        // ====================================================================
        // UPDATE - INPUT PROCESSING
        // ====================================================================

        public void Update(GameTime gameTime)
        {
            if (Owner == null || _physics == null || _sprite == null)
                return;

            // Update input state
            _previousKeyState = _currentKeyState;
            _currentKeyState = Keyboard.GetState();

            // ----------------------------------------------------------------
            // PROCESS MOVEMENT INPUT
            // Apply forces based on key presses
            // ----------------------------------------------------------------

            Vector2 inputDirection = Vector2.Zero;
            bool isThrusting = false;

            // Up arrow: Thrust upward (counteract gravity)
            if (_currentKeyState.IsKeyDown(Keys.Up))
            {
                _physics.ApplyThrust(new Vector2(0, -1), _physics.ThrustPower);
                isThrusting = true;
            }

            // Down arrow: Accelerate downward (complement gravity)
            if (_currentKeyState.IsKeyDown(Keys.Down))
            {
                _physics.ApplyThrust(new Vector2(0, 1), _physics.LateralAcceleration);
            }

            // Left arrow: Move left
            if (_currentKeyState.IsKeyDown(Keys.Left))
            {
                _physics.ApplyThrust(new Vector2(-1, 0), _physics.LateralAcceleration);
                inputDirection.X = -1;
            }

            // Right arrow: Move right
            if (_currentKeyState.IsKeyDown(Keys.Right))
            {
                _physics.ApplyThrust(new Vector2(1, 0), _physics.LateralAcceleration);
                inputDirection.X = 1;
            }

            // ----------------------------------------------------------------
            // UPDATE ANIMATION STATE
            // Choose appropriate animation based on movement
            // ----------------------------------------------------------------

            PlayerAnimState newAnimState = DetermineAnimationState(
                inputDirection,
                isThrusting,
                _physics.Velocity
            );

            // Only change animation if state has changed
            if (newAnimState != _currentAnimState)
            {
                _currentAnimState = newAnimState;
                ApplyAnimationState(newAnimState);
            }
        }

        // ====================================================================
        // ANIMATION STATE LOGIC
        // ====================================================================

        /// <summary>
        /// Determine which animation should play based on input and velocity.
        /// Matches Python prototype logic from player.py update_animation().
        /// </summary>
        private PlayerAnimState DetermineAnimationState(
            Vector2 inputDirection,
            bool isThrusting,
            Vector2 velocity)
        {
            // Use velocity threshold from SpriteConfig (matches Python prototype)
            float threshold = SpriteConfig.ANIMATION_VELOCITY_THRESHOLD;

            // Python logic (simplified):
            // vel_x > threshold  → walk_right
            // vel_x < -threshold → walk_left
            // vel_y != 0 or else → idle_front

            // Priority 1: Horizontal movement (matches Python prototype)
            if (velocity.X > threshold)
            {
                return PlayerAnimState.FlyingRight; // walk_right
            }
            else if (velocity.X < -threshold)
            {
                return PlayerAnimState.FlyingLeft; // walk_left
            }

            // Priority 2: Vertical movement (optional enhancement)
            // Python showed idle_front for vertical; we add flying_up/falling
            if (isThrusting && velocity.Y < -threshold)
            {
                return PlayerAnimState.FlyingUp;
            }
            else if (velocity.Y > threshold * 3) // Falling fast
            {
                return PlayerAnimState.Falling;
            }

            // Default: Idle/hovering (idle_front in Python)
            return PlayerAnimState.Idle;
        }

        /// <summary>
        /// Apply the correct animation frames for the current state.
        /// Uses animation names from Python prototype (idle_front, walk_right, walk_left).
        /// </summary>
        private void ApplyAnimationState(PlayerAnimState state)
        {
            if (_sprite == null) return;

            switch (state)
            {
                case PlayerAnimState.Idle:
                    // Python: "idle_front"
                    _sprite.SetAnimation(
                        SpriteConfig.PLAYER_IDLE_FRONT,
                        SpriteConfig.PLAYER_IDLE_ANIMATION_SPEED,
                        loop: true
                    );
                    _sprite.FlipHorizontal = false;
                    break;

                case PlayerAnimState.FlyingLeft:
                    // Python: "walk_left" (uses walk_right frames with flip)
                    _sprite.SetAnimation(
                        SpriteConfig.PLAYER_WALK_LEFT,
                        SpriteConfig.PLAYER_WALK_ANIMATION_SPEED,
                        loop: true
                    );
                    _sprite.FlipHorizontal = true; // Flip sprite to face left
                    break;

                case PlayerAnimState.FlyingRight:
                    // Python: "walk_right"
                    _sprite.SetAnimation(
                        SpriteConfig.PLAYER_WALK_RIGHT,
                        SpriteConfig.PLAYER_WALK_ANIMATION_SPEED,
                        loop: true
                    );
                    _sprite.FlipHorizontal = false;
                    break;

                case PlayerAnimState.FlyingUp:
                    // Enhanced animation (not in Python prototype)
                    _sprite.SetAnimation(
                        SpriteConfig.PLAYER_FLYING_UP,
                        SpriteConfig.PLAYER_THRUST_ANIMATION_SPEED,
                        loop: true
                    );
                    _sprite.FlipHorizontal = false;
                    break;

                case PlayerAnimState.Falling:
                    // Enhanced animation (not in Python prototype)
                    _sprite.SetAnimation(
                        SpriteConfig.PLAYER_FALLING,
                        SpriteConfig.PLAYER_WALK_ANIMATION_SPEED,
                        loop: true
                    );
                    _sprite.FlipHorizontal = false;
                    break;
            }
        }

        // ====================================================================
        // HELPER METHODS
        // ====================================================================

        /// <summary>
        /// Check if a key was just pressed this frame.
        /// </summary>
        private bool IsKeyPressed(Keys key)
        {
            return _currentKeyState.IsKeyDown(key) && _previousKeyState.IsKeyUp(key);
        }

        /// <summary>
        /// Check if a key was just released this frame.
        /// </summary>
        private bool IsKeyReleased(Keys key)
        {
            return _currentKeyState.IsKeyUp(key) && _previousKeyState.IsKeyDown(key);
        }

        public void Draw(GameTime gameTime)
        {
            // PlayerController doesn't render anything
        }
    }
}
