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
            if (Owner == null || _physics == null)
                return;

            // Re-cache sprite component if needed (it may be added after Initialize)
            if (_sprite == null)
            {
                _sprite = Owner.GetComponent<SpriteComponent>();
                if (_sprite == null) return; // Still null, can't continue

                // Set initial animation now that sprite component exists
                _sprite.SetAnimation(
                    SpriteConfig.PLAYER_IDLE_FRONT,
                    SpriteConfig.PLAYER_IDLE_ANIMATION_SPEED,
                    loop: true
                );
            }

            // Update input state
            _previousKeyState = _currentKeyState;
            _currentKeyState = Keyboard.GetState();

            // ----------------------------------------------------------------
            // PROCESS MOVEMENT INPUT - DIRECT VELOCITY (Python style)
            // Matches Python's handle_input_and_movement() EXACTLY
            // ----------------------------------------------------------------

            // HORIZONTAL VELOCITY (Left/Right)
            bool movingLeft = _currentKeyState.IsKeyDown(Keys.Left);
            bool movingRight = _currentKeyState.IsKeyDown(Keys.Right);

            float targetHorizontalVelocity = 0;
            if (movingLeft && !movingRight)
            {
                targetHorizontalVelocity = -_physics.Speed; // -500 px/s
            }
            else if (movingRight && !movingLeft)
            {
                targetHorizontalVelocity = _physics.Speed; // +500 px/s
            }
            // else: targetHorizontalVelocity = 0 (instant stop!)

            // VERTICAL VELOCITY (Up/Down/Gravity)
            bool pressingUp = _currentKeyState.IsKeyDown(Keys.Up);
            bool pressingDown = _currentKeyState.IsKeyDown(Keys.Down);

            float currentVerticalVelocity = _physics.GravitySpeed; // Default: +300 px/s (fall)

            if (pressingUp)
            {
                currentVerticalVelocity = -_physics.Speed; // -500 px/s (fly up)
            }
            else if (pressingDown)
            {
                currentVerticalVelocity = _physics.Speed; // +500 px/s (fly down fast)
            }

            // Special case: On ground and no vertical input = stop falling
            if (_physics.IsOnGround && !pressingUp && !pressingDown)
            {
                currentVerticalVelocity = 0;
            }

            // DIRECT ASSIGNMENT (create new Vector2 since it's a value type)
            _physics.Velocity = new Vector2(targetHorizontalVelocity, currentVerticalVelocity);

            // ----------------------------------------------------------------
            // UPDATE ANIMATION STATE (SIMPLIFIED - Idle always cycles)
            // ----------------------------------------------------------------

            // Determine target animation based on horizontal movement only
            string targetAnimation = "idle_front"; // Default: always idle
            bool flipSprite = false;

            // Only change animation if moving horizontally
            if (targetHorizontalVelocity < -10f) // Moving left
            {
                targetAnimation = "walk_left";
                flipSprite = false; // walk_left has its own frames
            }
            else if (targetHorizontalVelocity > 10f) // Moving right
            {
                targetAnimation = "walk_right";
                flipSprite = false;
            }

            // Apply the animation if it changed
            if (targetAnimation == "idle_front" && _sprite.AnimationFrames != SpriteConfig.PLAYER_IDLE_FRONT)
            {
                _sprite.SetAnimation(
                    SpriteConfig.PLAYER_IDLE_FRONT,
                    SpriteConfig.PLAYER_IDLE_ANIMATION_SPEED,
                    loop: true
                );
            }
            else if (targetAnimation == "walk_left" && _sprite.AnimationFrames != SpriteConfig.PLAYER_WALK_LEFT)
            {
                _sprite.SetAnimation(
                    SpriteConfig.PLAYER_WALK_LEFT,
                    SpriteConfig.PLAYER_WALK_ANIMATION_SPEED,
                    loop: true
                );
            }
            else if (targetAnimation == "walk_right" && _sprite.AnimationFrames != SpriteConfig.PLAYER_WALK_RIGHT)
            {
                _sprite.SetAnimation(
                    SpriteConfig.PLAYER_WALK_RIGHT,
                    SpriteConfig.PLAYER_WALK_ANIMATION_SPEED,
                    loop: true
                );
            }

            _sprite.FlipHorizontal = flipSprite;
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
                    // Python: "walk_left" (separate left-facing frames, NO flip)
                    _sprite.SetAnimation(
                        SpriteConfig.PLAYER_WALK_LEFT,
                        SpriteConfig.PLAYER_WALK_ANIMATION_SPEED,
                        loop: true
                    );
                    _sprite.FlipHorizontal = false; // NO flip - separate frames
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
