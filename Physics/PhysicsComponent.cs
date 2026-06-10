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

        public float Speed { get; set; } = 140f;

        // Gravity is intentionally a bit faster than horizontal Speed — matches the
        // original Sorcery+ game where the wizard always falls if not actively
        // thrusting up, and falls slightly faster than he can fly sideways.
        public float GravitySpeed { get; set; } = 160f;

        // ====================================================================
        // COLLISION CONSTANTS
        // ====================================================================

        /// <summary>
        /// Player hitbox size in pixels (full sprite — used for entity interactions
        /// like door triggers, item pickup, enemy contact).
        /// </summary>
        public const int HITBOX_WIDTH = 24;
        public const int HITBOX_HEIGHT = 24;

        /// <summary>
        /// Horizontal inset of the collision box relative to the sprite.
        /// The collision box is (HITBOX_WIDTH - 2*COLLISION_INSET_X) wide, centered in the sprite.
        /// This 8-bit-era trick gives the player tolerance when fitting through gaps exactly
        /// as wide as the sprite (e.g. the 24-pixel central shaft in Chateau 1).
        /// </summary>
        public const int COLLISION_INSET_X = 2;
        public const int COLLISION_INSET_TOP = 0;
        public const int COLLISION_INSET_BOTTOM = 0;

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
                // Tile-based collision (8x8 grid, snap-to-tile resolution).
                // The pixel mask on TileMap is kept only for the F2 debug overlay.

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
                // No tilemap: just move with screen bounds only
                pos += vel * dt;
                IsOnGround = false;
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
            // Center-only ground check: the player is "on ground" only if the tile
            // directly under his CENTER is solid. This matches original Sorcery+
            // behaviour where walking off an edge causes an immediate fall — and
            // crucially, it lets the wizard fall through tile-aligned shafts that
            // are exactly his sprite width (he no longer "rests" on a shaft's far
            // wall corner just because his right edge clips one solid tile column).
            int checkY = (int)(pos.Y + HITBOX_HEIGHT);
            int tileRow = checkY / TileConfig.TILE_SIZE;
            int centerCol = (int)(pos.X + HITBOX_WIDTH / 2) / TileConfig.TILE_SIZE;
            return IsTileBlocking(centerCol, tileRow);
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

        // ====================================================================
        // PIXEL-PERFECT COLLISION (when PixelMask is set on TileMap)
        // ====================================================================

        /// <summary>
        /// True if the pixel at (x, y) is solid in the current room's pixel mask.
        /// Out-of-bounds pixels are treated as non-solid (screen edges handled by ClampToScreen).
        /// </summary>
        private bool IsPixelSolid(int x, int y)
        {
            var mask = TileMap?.PixelMask;
            if (mask == null) return false;
            if (x < 0 || y < 0) return false;
            if (x >= mask.GetLength(0) || y >= mask.GetLength(1)) return false;
            return mask[x, y];
        }

        /// <summary>
        /// Check if the vertical strip at column `col` between [yTop, yBottom] overlaps any solid pixel.
        /// </summary>
        private bool AnySolidInColumn(int col, int yTop, int yBottom)
        {
            for (int y = yTop; y <= yBottom; y++)
                if (IsPixelSolid(col, y)) return true;
            return false;
        }

        /// <summary>
        /// Check if the horizontal strip at row `row` between [xLeft, xRight] overlaps any solid pixel.
        /// </summary>
        private bool AnySolidInRow(int row, int xLeft, int xRight)
        {
            for (int x = xLeft; x <= xRight; x++)
                if (IsPixelSolid(x, row)) return true;
            return false;
        }

        // Collision box (inset from sprite hitbox) — the rectangle we test against scenery pixels.
        // Returns (left, top, right, bottom) in absolute coordinates.
        private (int l, int t, int r, int b) GetCollisionBox(Vector2 pos)
        {
            int l = (int)pos.X + COLLISION_INSET_X;
            int r = (int)pos.X + HITBOX_WIDTH - 1 - COLLISION_INSET_X;
            int t = (int)pos.Y + COLLISION_INSET_TOP;
            int b = (int)pos.Y + HITBOX_HEIGHT - 1 - COLLISION_INSET_BOTTOM;
            return (l, t, r, b);
        }

        /// <summary>
        /// True if any pixel inside the collision box at `pos` overlaps a solid mask pixel.
        /// </summary>
        private bool CollidesAt(Vector2 pos)
        {
            var (l, t, r, b) = GetCollisionBox(pos);
            for (int y = t; y <= b; y++)
                for (int x = l; x <= r; x++)
                    if (IsPixelSolid(x, y)) return true;
            return false;
        }

        private Vector2 ResolveHorizontalPixelCollision(Vector2 pos, float oldX, ref Vector2 vel)
        {
            if (vel.X == 0) return pos;

            var (l, t, r, b) = GetCollisionBox(pos);

            if (vel.X > 0)
            {
                // Moving right: check right-edge column of the collision box.
                if (!AnySolidInColumn(r, t, b)) return pos;

                // If the OLD position's collision box right edge was already in a solid
                // column, the player is already embedded — let them move freely this frame
                // so they can escape.
                int oldR = (int)oldX + HITBOX_WIDTH - 1 - COLLISION_INSET_X;
                if (AnySolidInColumn(oldR, t, b)) return pos;

                // Walk back to the last clear column, but never past the old position.
                int clearR = r;
                while (clearR > oldR && AnySolidInColumn(clearR, t, b))
                    clearR -= 1;
                pos.X = clearR - (HITBOX_WIDTH - 1 - COLLISION_INSET_X);
                vel.X = 0;
            }
            else
            {
                // Moving left: check left-edge column.
                if (!AnySolidInColumn(l, t, b)) return pos;

                int oldL = (int)oldX + COLLISION_INSET_X;
                if (AnySolidInColumn(oldL, t, b)) return pos;

                int clearL = l;
                while (clearL < oldL && AnySolidInColumn(clearL, t, b))
                    clearL += 1;
                pos.X = clearL - COLLISION_INSET_X;
                vel.X = 0;
            }

            return pos;
        }

        private Vector2 ResolveVerticalPixelCollision(Vector2 pos, float oldY, ref Vector2 vel)
        {
            if (vel.Y == 0) return pos;

            var (l, t, r, b) = GetCollisionBox(pos);

            if (vel.Y > 0)
            {
                // Falling: check bottom-edge row.
                if (!AnySolidInRow(b, l, r)) return pos;

                int oldB = (int)oldY + HITBOX_HEIGHT - 1 - COLLISION_INSET_BOTTOM;
                if (AnySolidInRow(oldB, l, r)) return pos;

                int clearB = b;
                while (clearB > oldB && AnySolidInRow(clearB, l, r))
                    clearB -= 1;
                pos.Y = clearB - (HITBOX_HEIGHT - 1 - COLLISION_INSET_BOTTOM);
                vel.Y = 0;
            }
            else
            {
                // Rising: check top-edge row.
                if (!AnySolidInRow(t, l, r)) return pos;

                int oldT = (int)oldY + COLLISION_INSET_TOP;
                if (AnySolidInRow(oldT, l, r)) return pos;

                int clearT = t;
                while (clearT < oldT && AnySolidInRow(clearT, l, r))
                    clearT += 1;
                pos.Y = clearT - COLLISION_INSET_TOP;
                vel.Y = 0;
            }

            return pos;
        }

        private bool CheckOnGroundPixel(Vector2 pos)
        {
            // Ground = any solid pixel directly below the collision box's bottom row.
            var (l, t, r, b) = GetCollisionBox(pos);
            return AnySolidInRow(b + 1, l, r);
        }

        public void Draw(GameTime gameTime)
        {
            // Physics components don't render
        }
    }
}
