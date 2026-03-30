// ============================================================================
// ITEM SYSTEM
// Sorcery+ Remake - Item Types, Textures, and Combat Rules
// ============================================================================
// Centralizes:
// - ItemType enum (public, used across the codebase)
// - Texture and source-rect lookup per item type
// - Weapon-enemy effectiveness matrix (CanKillEnemy)
//
// Adding a new item type:
// 1. Add to ItemType enum
// 2. Register its texture in Game1.LoadContent via Register()
// 3. Done — no other files need changing
// ============================================================================

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SorceryRemake.Graphics;
using System.Collections.Generic;

namespace SorceryRemake.Core
{
    /// <summary>
    /// All item types in the game.
    /// </summary>
    public enum ItemType
    {
        None,
        Sword,
        BallAndChain,
        Axe,
        ShootingStar,
        Lyre
    }

    /// <summary>
    /// Manages item textures, source rects, and weapon-enemy rules.
    /// </summary>
    public class ItemSystem
    {
        private readonly Dictionary<ItemType, Texture2D> _textures = new();
        private readonly Dictionary<ItemType, Rectangle> _sourceRects = new();

        /// <summary>
        /// Register a texture and source rectangle for an item type.
        /// Called during LoadContent for each item.
        /// </summary>
        public void Register(ItemType type, Texture2D texture, Rectangle sourceRect)
        {
            _textures[type] = texture;
            _sourceRects[type] = sourceRect;
        }

        public Texture2D? GetTexture(ItemType type)
        {
            return _textures.TryGetValue(type, out var tex) ? tex : null;
        }

        public Rectangle GetSourceRect(ItemType type)
        {
            return _sourceRects.TryGetValue(type, out var rect) ? rect : Rectangle.Empty;
        }

        /// <summary>
        /// Weapon-enemy effectiveness matrix.
        /// Returns true if the carried item can kill this enemy type.
        /// </summary>
        public static bool CanKillEnemy(EnemyType enemyType, ItemType weapon)
        {
            return enemyType switch
            {
                // Guard requires sword
                EnemyType.Guard => weapon == ItemType.Sword,
                // Eye, mask, boar require ball and chain
                EnemyType.Eye => weapon == ItemType.BallAndChain,
                EnemyType.Mask => weapon == ItemType.BallAndChain,
                EnemyType.Boar => weapon == ItemType.BallAndChain,
                // Wraith requires axe
                EnemyType.Wraith => weapon == ItemType.Axe,
                _ => false
            };
        }
    }
}
