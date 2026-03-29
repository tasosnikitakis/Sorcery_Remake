// ============================================================================
// WRAITH CONTROLLER COMPONENT
// Sorcery+ Remake - Wraith Enemy AI
// ============================================================================
// The wraith follows the player in both axes like the mask/boar but uses
// directional animations like the guard (walk left/idle/walk right).
// It passes through ALL world geometry (no collision) and is slightly
// faster than the player. Only bounded by the game area.
// ============================================================================

using Microsoft.Xna.Framework;
using SorceryRemake.Core;
using SorceryRemake.Physics;
using SorceryRemake.Graphics;

namespace SorceryRemake.Enemies
{
    public class WraithController : IComponent
    {
        public Entity? Owner { get; set; }

        private PhysicsComponent? _physics;
        private SpriteComponent? _sprite;
        private Entity _player;
        private bool _movingLeft;
        private bool _movingRight;

        public WraithController(Entity player)
        {
            _player = player;
        }

        public void Initialize()
        {
            if (Owner == null) return;

            _physics = Owner.GetComponent<PhysicsComponent>();
            _sprite = Owner.GetComponent<SpriteComponent>();

            // Start with idle animation
            if (_sprite != null)
            {
                _sprite.SetAnimation(
                    SpriteConfig.WRAITH_IDLE,
                    SpriteConfig.WRAITH_ANIMATION_SPEED,
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

            if (dist < SpriteConfig.WRAITH_FOLLOW_THRESHOLD)
            {
                _physics.Velocity = Vector2.Zero;
                UpdateAnimation(0);
                return;
            }

            float speed = SpriteConfig.WRAITH_SPEED;
            float vx = (dx / dist) * speed;
            float vy = (dy / dist) * speed;

            _physics.Velocity = new Vector2(vx, vy);
            UpdateAnimation(vx);
        }

        private void UpdateAnimation(float vx)
        {
            if (_sprite == null) return;

            if (vx < -SpriteConfig.ANIMATION_VELOCITY_THRESHOLD)
            {
                if (!_movingLeft)
                {
                    _sprite.SetAnimation(
                        SpriteConfig.WRAITH_WALK_LEFT,
                        SpriteConfig.WRAITH_ANIMATION_SPEED,
                        loop: true
                    );
                    _movingLeft = true;
                    _movingRight = false;
                }
            }
            else if (vx > SpriteConfig.ANIMATION_VELOCITY_THRESHOLD)
            {
                if (!_movingRight)
                {
                    _sprite.SetAnimation(
                        SpriteConfig.WRAITH_WALK_RIGHT,
                        SpriteConfig.WRAITH_ANIMATION_SPEED,
                        loop: true
                    );
                    _movingRight = true;
                    _movingLeft = false;
                }
            }
            else
            {
                if (_movingLeft || _movingRight)
                {
                    _sprite.SetAnimation(
                        SpriteConfig.WRAITH_IDLE,
                        SpriteConfig.WRAITH_ANIMATION_SPEED,
                        loop: true
                    );
                    _movingLeft = false;
                    _movingRight = false;
                }
            }
        }

        public void Draw(GameTime gameTime)
        {
        }
    }
}
