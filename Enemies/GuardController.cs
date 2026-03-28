// ============================================================================
// GUARD CONTROLLER COMPONENT
// Sorcery+ Remake - Hooded Guard AI and Animation
// ============================================================================
// The hooded guard is a ground-based enemy that horizontally follows the
// player on its current platform. It respects gravity, tile collision,
// and platform edges (will not walk off into empty space).
//
// ANIMATION:
// - Walk Left:  Frames 0-3  (row 2, X=0..75)
// - Idle:       Frames 4-7  (row 2, X=100..175) - direction change
// - Walk Right: Frames 8-11 (row 2, X=200..275)
// ============================================================================

using Microsoft.Xna.Framework;
using SorceryRemake.Core;
using SorceryRemake.Physics;
using SorceryRemake.Graphics;
using SorceryRemake.Tiles;

namespace SorceryRemake.Enemies
{
    public class GuardController : IComponent
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
        // ANIMATION STATE
        // ====================================================================

        private enum GuardAnimState
        {
            Idle,
            WalkLeft,
            WalkRight
        }

        private GuardAnimState _currentAnimState = GuardAnimState.Idle;

        // ====================================================================
        // CONSTRUCTOR
        // ====================================================================

        public GuardController(Entity player)
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

            // Start with idle animation (middle frames)
            if (_sprite != null)
            {
                _sprite.SetAnimation(
                    SpriteConfig.GUARD_IDLE,
                    SpriteConfig.GUARD_ANIMATION_SPEED,
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
                if (_sprite == null) return;
            }

            // Only move when on the ground
            if (!_physics.IsOnGround)
            {
                _physics.Velocity = new Vector2(0, _physics.GravitySpeed);
                return;
            }

            // Determine direction to player
            float dx = _player.Position.X - Owner.Position.X;
            float absDx = System.Math.Abs(dx);

            // Dead zone to prevent jitter
            if (absDx < SpriteConfig.GUARD_FOLLOW_THRESHOLD)
            {
                // Close enough - idle
                _physics.Velocity = new Vector2(0, _physics.GravitySpeed);
                SetAnimState(GuardAnimState.Idle);
                return;
            }

            // Determine movement direction
            float moveDir = dx > 0 ? 1f : -1f;

            // Edge detection: check if there's floor ahead
            if (!HasFloorAhead(moveDir))
            {
                // Platform edge - stop
                _physics.Velocity = new Vector2(0, _physics.GravitySpeed);
                SetAnimState(GuardAnimState.Idle);
                return;
            }

            // Move toward player
            float speed = SpriteConfig.GUARD_SPEED;
            _physics.Velocity = new Vector2(moveDir * speed, _physics.GravitySpeed);

            // Update animation
            if (moveDir < 0)
                SetAnimState(GuardAnimState.WalkLeft);
            else
                SetAnimState(GuardAnimState.WalkRight);
        }

        // ====================================================================
        // EDGE DETECTION
        // ====================================================================

        /// <summary>
        /// Check if there is solid ground ahead of the guard in the given direction.
        /// Prevents the guard from walking off platform edges.
        /// </summary>
        private bool HasFloorAhead(float direction)
        {
            if (_physics?.TileMap == null || Owner == null) return false;

            var tileMap = _physics.TileMap;

            // Check the tile below the guard's leading foot
            float checkX;
            if (direction > 0)
            {
                // Moving right: check below right edge + 1 pixel ahead
                checkX = Owner.Position.X + PhysicsComponent.HITBOX_WIDTH;
            }
            else
            {
                // Moving left: check below left edge - 1 pixel ahead
                checkX = Owner.Position.X - 1;
            }

            // Check the tile row just below the guard's feet
            float checkY = Owner.Position.Y + PhysicsComponent.HITBOX_HEIGHT;

            int tileX = (int)(checkX / TileConfig.TILE_SIZE);
            int tileY = (int)(checkY / TileConfig.TILE_SIZE);

            // Bounds check
            if (tileX < 0 || tileX >= tileMap.Width || tileY < 0 || tileY >= tileMap.Height)
                return false;

            int tileId = tileMap.GetTile(tileX, tileY);
            return TileConfig.IsSolid(tileId) || TileConfig.IsPlatform(tileId);
        }

        // ====================================================================
        // ANIMATION STATE MANAGEMENT
        // ====================================================================

        private void SetAnimState(GuardAnimState newState)
        {
            if (_sprite == null || _currentAnimState == newState) return;

            _currentAnimState = newState;

            switch (newState)
            {
                case GuardAnimState.Idle:
                    _sprite.SetAnimation(
                        SpriteConfig.GUARD_IDLE,
                        SpriteConfig.GUARD_ANIMATION_SPEED,
                        loop: true
                    );
                    break;

                case GuardAnimState.WalkLeft:
                    _sprite.SetAnimation(
                        SpriteConfig.GUARD_WALK_LEFT,
                        SpriteConfig.GUARD_ANIMATION_SPEED,
                        loop: true
                    );
                    break;

                case GuardAnimState.WalkRight:
                    _sprite.SetAnimation(
                        SpriteConfig.GUARD_WALK_RIGHT,
                        SpriteConfig.GUARD_ANIMATION_SPEED,
                        loop: true
                    );
                    break;
            }
        }

        // ====================================================================
        // DRAW (no-op, rendering handled by SpriteComponent)
        // ====================================================================

        public void Draw(GameTime gameTime)
        {
        }
    }
}
