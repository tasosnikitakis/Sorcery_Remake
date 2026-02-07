// ============================================================================
// PHYSICS COMPONENT - FLIGHT MECHANICS
// Sorcery+ Remake - Authentic Amstrad CPC Flight Physics
// ============================================================================
// This component implements the unique "hover" flight mechanics from the
// original game. The player doesn't walk - they fly with inertia and gravity.
//
// PHYSICS MODEL (Calibrated to match 1985 original):
// - Constant gravity pulls downward
// - Thrust counteracts gravity when "Up" is held
// - Damping (friction) creates the characteristic "floating" feel
// - No instant stops - momentum must decay naturally
// ============================================================================

using Microsoft.Xna.Framework;
using SorceryRemake.Core;

namespace SorceryRemake.Physics
{
    /// <summary>
    /// Handles realistic flight physics with gravity, thrust, and inertia.
    /// Calibrated to match the original Amstrad CPC feel.
    /// </summary>
    public class PhysicsComponent : IComponent
    {
        // ====================================================================
        // COMPONENT INTERFACE
        // ====================================================================

        public Entity? Owner { get; set; }

        // ====================================================================
        // PHYSICS CONSTANTS (Tuned to match original game)
        // All values are in "Amstrad pixels" per second
        // ====================================================================

        /// <summary>
        /// Downward acceleration in pixels/second²
        /// Original game: ~300 px/s² (estimated from gameplay)
        /// </summary>
        public float Gravity { get; set; } = 300f;

        /// <summary>
        /// Upward thrust when "Up" key is held (pixels/second²)
        /// Must be slightly stronger than gravity to allow hovering
        /// </summary>
        public float ThrustPower { get; set; } = 400f;

        /// <summary>
        /// Lateral acceleration from left/right input (pixels/second²)
        /// </summary>
        public float LateralAcceleration { get; set; } = 350f;

        /// <summary>
        /// Damping factor applied each frame (friction)
        /// 0.85 means velocity is reduced to 85% each frame
        /// This creates the "floaty" feel of the original
        /// </summary>
        public float Damping { get; set; } = 0.85f;

        /// <summary>
        /// Maximum velocity in any direction (pixels/second)
        /// Prevents runaway speeds in tight corridors
        /// </summary>
        public float MaxVelocity { get; set; } = 200f;

        // ====================================================================
        // PHYSICS STATE
        // ====================================================================

        /// <summary>
        /// Current velocity vector (pixels/second)
        /// </summary>
        public Vector2 Velocity { get; set; }

        /// <summary>
        /// Accumulated acceleration this frame (pixels/second²)
        /// Reset each frame after being applied to velocity
        /// </summary>
        private Vector2 _acceleration;

        /// <summary>
        /// Is the entity grounded? (For future ground-walking enemies)
        /// </summary>
        public bool IsGrounded { get; set; }

        // ====================================================================
        // CONSTRUCTOR
        // ====================================================================

        public PhysicsComponent()
        {
            Velocity = Vector2.Zero;
            _acceleration = Vector2.Zero;
            IsGrounded = false;
        }

        // ====================================================================
        // PUBLIC API - FORCE APPLICATION
        // ====================================================================

        /// <summary>
        /// Apply a force (acceleration) to the entity.
        /// Multiple forces can be applied per frame; they accumulate.
        /// </summary>
        public void ApplyForce(Vector2 force)
        {
            _acceleration += force;
        }

        /// <summary>
        /// Apply thrust in a specific direction.
        /// Used for player input (up/down/left/right).
        /// </summary>
        public void ApplyThrust(Vector2 direction, float power)
        {
            if (direction.LengthSquared() > 0)
            {
                direction.Normalize();
                _acceleration += direction * power;
            }
        }

        // ====================================================================
        // UPDATE LOOP - PHYSICS INTEGRATION
        // ====================================================================

        public void Update(GameTime gameTime)
        {
            if (Owner == null) return;

            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // ----------------------------------------------------------------
            // STEP 1: Apply constant gravity (always pulling down)
            // ----------------------------------------------------------------
            _acceleration.Y += Gravity;

            // ----------------------------------------------------------------
            // STEP 2: Integrate acceleration into velocity
            // ----------------------------------------------------------------
            Velocity += _acceleration * deltaTime;

            // ----------------------------------------------------------------
            // STEP 3: Apply damping (friction/air resistance)
            // This creates the signature "floaty" movement
            // ----------------------------------------------------------------
            Velocity *= Damping;

            // ----------------------------------------------------------------
            // STEP 4: Clamp velocity to maximum speed
            // Prevents uncontrollable speeds in open areas
            // ----------------------------------------------------------------
            if (Velocity.LengthSquared() > MaxVelocity * MaxVelocity)
            {
                Velocity = Vector2.Normalize(Velocity) * MaxVelocity;
            }

            // ----------------------------------------------------------------
            // STEP 5: Integrate velocity into position
            // Position is in "Amstrad pixel" coordinates (160x200 space)
            // ----------------------------------------------------------------
            Owner.Position += Velocity * deltaTime;

            // ----------------------------------------------------------------
            // STEP 6: Reset acceleration for next frame
            // ----------------------------------------------------------------
            _acceleration = Vector2.Zero;

            // ----------------------------------------------------------------
            // DEBUG: Clamp to screen bounds (temporary for prototype)
            // TODO: Replace with proper collision detection
            // ----------------------------------------------------------------
            Owner.Position = new Vector2(
                MathHelper.Clamp(Owner.Position.X, 0, 160),
                MathHelper.Clamp(Owner.Position.Y, 0, 200)
            );
        }

        public void Draw(GameTime gameTime)
        {
            // Physics components don't render anything
        }
    }
}
