// ============================================================================
// PHYSICS COMPONENT - DIRECT VELOCITY + TILE COLLISION
// Sorcery+ Remake - Matches Python Prototype
// ============================================================================
// Direct velocity assignment (CPC authentic):
// - Press arrow key -> instant velocity (no acceleration)
// - Release key -> instant stop (no momentum)
// - Idle -> constant downward velocity (gravity)
//
// Phase 2B: Added tile-based collision detection
// - Separate axis collision (X then Y) prevents sticking
// - Player stands on solid tiles and platforms
// - IsOnGround set when tile exists below feet
// ============================================================================

using Microsoft.Xna.Framework;
using SorceryRemake.Core;
using SorceryRemake.Tiles;
using System;
using System.Collections.Generic;

namespace SorceryRemake.Physics
{
    public class PhysicsComponent : IComponent
    {
        // ====================================================================
        // COMPONENT INTERFACE
        // ====================================================================

        public Entity? Owner { get; set; }

        // ====================================================================
        // PHYSICS CONSTANTS
        // ====================================================================

        public float Speed { get; set; } = 200f;
        public float GravitySpeed { get; set; } = 120f;

        // ====================================================================
        // COLLISION CONSTANTS
        // ====================================================================

        /// <summary>
        /// Player hitbox size in pixels.
        /// </summary>
        public const int HITBOX_WIDTH = 24;
        public const int HITBOX_HEIGHT = 24;

        // ====================================================================
        // PHYSICS STATE
        // ====================================================================

        public Vector2 Velocity { get; set; }
        public bool IsOnGround { get; set; }

        // ====================================================================
        // TILEMAP REFERENCE (set by Game1 after creation)
        // ====================================================================

        public TileMapComponent? TileMap { get; set; }

        /// <summary>
        /// Solid rectangles for non-tile collision (doors, etc.).
        /// Updated by Game1 when rooms change.
        /// </summary>
        public List<Rectangle> SolidRects { get; set; } = new();

        // ====================================================================
        // CONSTRUCTOR
        // ====================================================================

        public PhysicsComponent()
        {
            Velocity = Vector2.Zero;
            IsOnGround = false;
        }

        // ====================================================================
        // UPDATE - MOVE + COLLIDE
        // ====================================================================

        public void Update(GameTime gameTime)
        {
            if (Owner == null) return;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            Vector2 pos = Owner.Position;
            Vector2 vel = Velocity;

            if (TileMap != null)
            {
                // SEPARATE AXIS COLLISION:
                // Move X first, resolve, then move Y, resolve.

                // --- X AXIS ---
                pos.X += vel.X * dt;
                pos = ResolveHorizontalCollision(pos, ref vel);
                pos = ResolveSolidRectsHorizontal(pos, ref vel);

                // --- Y AXIS ---
                pos.Y += vel.Y * dt;
                pos = ResolveVerticalCollision(pos, ref vel);
                pos = ResolveSolidRectsVertical(pos, ref vel);

                // --- GROUND CHECK ---
                IsOnGround = CheckOnGround(pos) || CheckOnGroundSolidRects(pos);
            }
            else
            {
                // No tilemap: just move with screen bounds (legacy)
                pos += vel * dt;

                if (pos.Y > 144 - HITBOX_HEIGHT)
                {
                    pos.Y = 144 - HITBOX_HEIGHT;
                    vel.Y = 0;
                    IsOnGround = true;
                }
                else
                {
                    IsOnGround = false;
                }
            }

            // Screen edge boundaries (always enforced)
            pos = ClampToScreen(pos, ref vel);

            Owner.Position = pos;
            Velocity = vel;
        }

        // ====================================================================
        // HORIZONTAL COLLISION
        // ====================================================================

        /// <summary>
        /// Check if a tile blocks movement (solid or platform - all treated as solid world geometry).
        /// </summary>
        private bool IsTileBlocking(int tileX, int tileY)
        {
            int tileId = TileMap!.GetTile(tileX, tileY);
            return TileConfig.IsSolid(tileId) || TileConfig.IsPlatform(tileId);
        }

        private Vector2 ResolveHorizontalCollision(Vector2 pos, ref Vector2 vel)
        {
            int topTile = (int)(pos.Y / TileConfig.TILE_SIZE);
            int bottomTile = (int)((pos.Y + HITBOX_HEIGHT - 1) / TileConfig.TILE_SIZE);

            if (vel.X > 0)
            {
                // Moving right: check right edge
                int rightEdge = (int)(pos.X + HITBOX_WIDTH);
                int tileCol = rightEdge / TileConfig.TILE_SIZE;

                for (int row = topTile; row <= bottomTile; row++)
                {
                    if (IsTileBlocking(tileCol, row))
                    {
                        pos.X = tileCol * TileConfig.TILE_SIZE - HITBOX_WIDTH;
                        vel.X = 0;
                        break;
                    }
                }
            }
            else if (vel.X < 0)
            {
                // Moving left: check left edge
                int leftEdge = (int)(pos.X - 1);
                if (leftEdge < 0) return pos;
                int tileCol = leftEdge / TileConfig.TILE_SIZE;

                for (int row = topTile; row <= bottomTile; row++)
                {
                    if (IsTileBlocking(tileCol, row))
                    {
                        pos.X = (tileCol + 1) * TileConfig.TILE_SIZE;
                        vel.X = 0;
                        break;
                    }
                }
            }

            return pos;
        }

        // ====================================================================
        // VERTICAL COLLISION
        // ====================================================================

        private Vector2 ResolveVerticalCollision(Vector2 pos, ref Vector2 vel)
        {
            int leftTile = (int)(pos.X / TileConfig.TILE_SIZE);
            int rightTile = (int)((pos.X + HITBOX_WIDTH - 1) / TileConfig.TILE_SIZE);

            if (vel.Y > 0)
            {
                // Falling down: check bottom edge
                int bottomEdge = (int)(pos.Y + HITBOX_HEIGHT);
                int tileRow = bottomEdge / TileConfig.TILE_SIZE;

                for (int col = leftTile; col <= rightTile; col++)
                {
                    if (IsTileBlocking(col, tileRow))
                    {
                        pos.Y = tileRow * TileConfig.TILE_SIZE - HITBOX_HEIGHT;
                        vel.Y = 0;
                        break;
                    }
                }
            }
            else if (vel.Y < 0)
            {
                // Moving up: check top edge
                int topEdge = (int)(pos.Y);
                int tileRow = topEdge / TileConfig.TILE_SIZE;

                for (int col = leftTile; col <= rightTile; col++)
                {
                    if (IsTileBlocking(col, tileRow))
                    {
                        pos.Y = (tileRow + 1) * TileConfig.TILE_SIZE;
                        vel.Y = 0;
                        break;
                    }
                }
            }

            return pos;
        }

        // ====================================================================
        // GROUND CHECK
        // ====================================================================

        private bool CheckOnGround(Vector2 pos)
        {
            // Check tile row just below feet
            int checkY = (int)(pos.Y + HITBOX_HEIGHT);
            int tileRow = checkY / TileConfig.TILE_SIZE;

            int leftTile = (int)(pos.X / TileConfig.TILE_SIZE);
            int rightTile = (int)((pos.X + HITBOX_WIDTH - 1) / TileConfig.TILE_SIZE);

            for (int col = leftTile; col <= rightTile; col++)
            {
                if (IsTileBlocking(col, tileRow))
                    return true;
            }

            return false;
        }

        // ====================================================================
        // SOLID RECT COLLISION (doors, etc.)
        // ====================================================================

        private Vector2 ResolveSolidRectsHorizontal(Vector2 pos, ref Vector2 vel)
        {
            Rectangle playerRect = new Rectangle((int)pos.X, (int)pos.Y, HITBOX_WIDTH, HITBOX_HEIGHT);

            foreach (var rect in SolidRects)
            {
                if (!playerRect.Intersects(rect)) continue;

                if (vel.X > 0)
                {
                    pos.X = rect.Left - HITBOX_WIDTH;
                    vel.X = 0;
                }
                else if (vel.X < 0)
                {
                    pos.X = rect.Right;
                    vel.X = 0;
                }
                playerRect = new Rectangle((int)pos.X, (int)pos.Y, HITBOX_WIDTH, HITBOX_HEIGHT);
            }

            return pos;
        }

        private Vector2 ResolveSolidRectsVertical(Vector2 pos, ref Vector2 vel)
        {
            Rectangle playerRect = new Rectangle((int)pos.X, (int)pos.Y, HITBOX_WIDTH, HITBOX_HEIGHT);

            foreach (var rect in SolidRects)
            {
                if (!playerRect.Intersects(rect)) continue;

                if (vel.Y > 0)
                {
                    pos.Y = rect.Top - HITBOX_HEIGHT;
                    vel.Y = 0;
                }
                else if (vel.Y < 0)
                {
                    pos.Y = rect.Bottom;
                    vel.Y = 0;
                }
                playerRect = new Rectangle((int)pos.X, (int)pos.Y, HITBOX_WIDTH, HITBOX_HEIGHT);
            }

            return pos;
        }

        private bool CheckOnGroundSolidRects(Vector2 pos)
        {
            Rectangle feetCheck = new Rectangle((int)pos.X, (int)(pos.Y + HITBOX_HEIGHT), HITBOX_WIDTH, 1);
            foreach (var rect in SolidRects)
            {
                if (feetCheck.Intersects(rect))
                    return true;
            }
            return false;
        }

        // ====================================================================
        // SCREEN BOUNDS
        // ====================================================================

        private Vector2 ClampToScreen(Vector2 pos, ref Vector2 vel)
        {
            if (pos.X < 0)
            {
                pos.X = 0;
                vel.X = 0;
            }
            else if (pos.X > 320 - HITBOX_WIDTH)
            {
                pos.X = 320 - HITBOX_WIDTH;
                vel.X = 0;
            }

            if (pos.Y < 0)
            {
                pos.Y = 0;
                vel.Y = 0;
            }
            else if (pos.Y > 144 - HITBOX_HEIGHT)
            {
                pos.Y = 144 - HITBOX_HEIGHT;
                vel.Y = 0;
            }

            return pos;
        }

        public void Draw(GameTime gameTime)
        {
            // Physics components don't render
        }
    }
}
