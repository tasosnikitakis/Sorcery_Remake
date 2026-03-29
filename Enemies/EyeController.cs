// ============================================================================
// EYE CONTROLLER COMPONENT
// Sorcery+ Remake - Floating Eye Enemy AI
// ============================================================================
// The eye is a floating enemy identical in behavior to the mask/boar:
// follows the player in both axes, no gravity, solid collision,
// single 4-frame animation looping continuously.
// ============================================================================

using Microsoft.Xna.Framework;
using SorceryRemake.Core;
using SorceryRemake.Physics;
using SorceryRemake.Graphics;

namespace SorceryRemake.Enemies
{
    public class EyeController : IComponent
    {
        public Entity? Owner { get; set; }

        private PhysicsComponent? _physics;
        private SpriteComponent? _sprite;
        private Entity _player;

        public EyeController(Entity player)
        {
            _player = player;
        }

        public void Initialize()
        {
            if (Owner == null) return;

            _physics = Owner.GetComponent<PhysicsComponent>();
            _sprite = Owner.GetComponent<SpriteComponent>();

            if (_sprite != null)
            {
                _sprite.SetAnimation(
                    SpriteConfig.EYE_ANIM,
                    SpriteConfig.EYE_ANIMATION_SPEED,
                    loop: true
                );
            }
        }

        public void Update(GameTime gameTime)
        {
            if (Owner == null || _physics == null) return;

            if (_sprite == null)
                _sprite = Owner.GetComponent<SpriteComponent>();

            float dx = _player.Position.X - Owner.Position.X;
            float dy = _player.Position.Y - Owner.Position.Y;

            float dist = (float)System.Math.Sqrt(dx * dx + dy * dy);

            if (dist < 1f)
            {
                _physics.Velocity = Vector2.Zero;
                return;
            }

            float speed = SpriteConfig.EYE_SPEED;
            _physics.Velocity = new Vector2((dx / dist) * speed, (dy / dist) * speed);
        }

        public void Draw(GameTime gameTime)
        {
        }
    }
}
