// ============================================================================
// MASK CONTROLLER COMPONENT
// Sorcery+ Remake - Floating Mask Enemy AI
// ============================================================================
// The mask is a floating enemy that follows the player in both axes.
// It has no gravity, is solid (collides with all world geometry),
// and plays a single 4-frame animation continuously.
//
// MOVEMENT:
// - Floats toward the player at a steady speed (no gravity)
// - Blocked by solid tiles, platforms, walls, and doors
// - Slightly slower than the player
//
// ANIMATION:
// - Single 4-frame loop, plays at all times (idle or moving)
// ============================================================================

using Microsoft.Xna.Framework;
using SorceryRemake.Core;
using SorceryRemake.Physics;
using SorceryRemake.Graphics;

namespace SorceryRemake.Enemies
{
    public class MaskController : IComponent
    {
        // ====================================================================
        // COMPONENT INTERFACE
        // ====================================================================

        public Entity? Owner { get; set; }

        // ====================================================================
        // REFERENCES
        // ====================================================================

        private PhysicsComponent? _physics;
        private SpriteComponent? _sprite;
        private Entity _player;

        // ====================================================================
        // CONSTRUCTOR
        // ====================================================================

        public MaskController(Entity player)
        {
            _player = player;
        }

        // ====================================================================
        // INITIALIZATION
        // ====================================================================

        public void Initialize()
        {
            if (Owner == null) return;

            _physics = Owner.GetComponent<PhysicsComponent>();
            _sprite = Owner.GetComponent<SpriteComponent>();

            // Set the single looping animation
            if (_sprite != null)
            {
                _sprite.SetAnimation(
                    SpriteConfig.MASK_ANIM,
                    SpriteConfig.MASK_ANIMATION_SPEED,
                    loop: true
                );
            }
        }

        // ====================================================================
        // UPDATE - AI LOGIC
        // ====================================================================

        public void Update(GameTime gameTime)
        {
            if (Owner == null || _physics == null) return;

            // Re-cache sprite if needed
            if (_sprite == null)
            {
                _sprite = Owner.GetComponent<SpriteComponent>();
            }

            // Direction toward player (both axes)
            float dx = _player.Position.X - Owner.Position.X;
            float dy = _player.Position.Y - Owner.Position.Y;

            // Normalize direction vector for steady diagonal speed
            float dist = (float)System.Math.Sqrt(dx * dx + dy * dy);

            if (dist < 1f)
            {
                // Overlapping player - stop
                _physics.Velocity = Vector2.Zero;
                return;
            }

            float speed = SpriteConfig.MASK_SPEED;
            float vx = (dx / dist) * speed;
            float vy = (dy / dist) * speed;

            _physics.Velocity = new Vector2(vx, vy);
        }

        // ====================================================================
        // DRAW (no-op, rendering handled by SpriteComponent)
        // ====================================================================

        public void Draw(GameTime gameTime)
        {
        }
    }
}
