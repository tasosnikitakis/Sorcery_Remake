// ============================================================================
// ENTITY CATALOG
// Sorcery+ Remake - Sprite facts for every item and enemy type
// ============================================================================
// This catalog is the single source of truth for entity sprite facts: which
// Content asset a type draws from, which source rectangle to use, what to
// call it in the UI, and which palette section it belongs to.
//
// To add an item:
//   1. Add the ItemType enum value (Core/ItemSystem.cs)
//   2. Add one row to Items below
//   3. Provide the PNG in Content/ plus its Content.mgcb block
//   ...nothing else. The game registers it with ItemSystem and SorceryForge
//   shows it in the palette, both by iterating this table.
//
// WHY THIS EXISTS
// These facts used to live in three places at once: Game1's registration
// calls, the editor's texture pre-load list, and the editor's hardcoded
// palette. Adding one item meant three coordinated edits, and forgetting the
// editor edit meant the item existed in the game but could not be placed in
// a room. Now the table is the edit and the three consumers follow it.
//
// SCOPE — what is NOT here
// Only the facts that reduce to table rows live here. Enemy BEHAVIOUR does
// not: Game1.SpawnEnemy wires per-type movement speed, gravity, door
// collision, and a distinct controller class per enemy, none of which is
// row-shaped. That switch stays where it is and carries a pointer back here
// so the two are read together. Weapon-vs-enemy rules likewise stay in
// ItemSystem.CanKillEnemy.
//
// This file is SHARED SOURCE — it compiles into both SorceryRemake and
// SorceryForge. It deliberately names assets as strings rather than holding
// Texture2D references: the two apps load content differently (the game
// through ItemSystem, the editor through its own black-keyed cache) and the
// catalog must not depend on either, nor on a GraphicsDevice existing.
// ============================================================================

using Microsoft.Xna.Framework;
using SorceryRemake.Graphics;
using System.Collections.Generic;

namespace SorceryRemake.Core
{
    /// <summary>
    /// Sprite facts for one <see cref="ItemType"/>. Get-only properties set
    /// once at construction — the catalog is read-only data, not state.
    /// </summary>
    public class ItemCatalogEntry
    {
        public ItemType Type { get; }
        public string Asset { get; }           // Content asset name, e.g. "SwordSheet"
        public Rectangle SourceRect { get; }   // frame within that sheet
        public string DisplayName { get; }     // UI label, e.g. "Ball & Chain"
        public string PaletteSection { get; }  // SorceryForge palette group

        public ItemCatalogEntry(ItemType type, string asset, Rectangle sourceRect,
                                string displayName, string paletteSection)
        {
            Type = type;
            Asset = asset;
            SourceRect = sourceRect;
            DisplayName = displayName;
            PaletteSection = paletteSection;
        }
    }

    /// <summary>
    /// Sprite facts for one <see cref="EnemyType"/>. SourceRect is the frame
    /// the enemy shows at rest — the same frame Game1.SpawnEnemy hands to the
    /// initial SpriteComponent, and the one the editor draws as its icon.
    /// Full animation strips stay in SpriteConfig; this points at frame 0.
    /// </summary>
    public class EnemyCatalogEntry
    {
        public EnemyType Type { get; }
        public string Asset { get; }
        public Rectangle SourceRect { get; }
        public string DisplayName { get; }
        public string PaletteSection { get; }

        public EnemyCatalogEntry(EnemyType type, string asset, Rectangle sourceRect,
                                 string displayName, string paletteSection)
        {
            Type = type;
            Asset = asset;
            SourceRect = sourceRect;
            DisplayName = displayName;
            PaletteSection = paletteSection;
        }
    }

    /// <summary>
    /// The catalog itself. List order is the editor's palette order within
    /// each section (SorceryForge.EditorGame.SectionOrder decides the order
    /// OF the sections), so reordering these rows reorders the palette.
    /// </summary>
    public static class EntityCatalog
    {
        // Palette section names. String constants rather than an enum because
        // SorceryForge's SectionOrder array is the thing that consumes them
        // and it is keyed by name; keeping them here means a new row can't
        // invent a section that has no header.
        public const string SectionWeapons = "WEAPONS";
        public const string SectionKeyItems = "KEY ITEMS";
        public const string SectionEnemies = "ENEMIES";

        /// <summary>
        /// Every item that has a sprite. ItemType.None is deliberately absent
        /// — it means "carrying nothing", not a thing that can be drawn or
        /// placed. Source rects come from SpriteConfig so frame geometry
        /// still has exactly one home.
        /// </summary>
        public static readonly List<ItemCatalogEntry> Items = new()
        {
            new(ItemType.Sword,        "SwordSheet",        SpriteConfig.SWORD_FRAME,          "Sword",         SectionWeapons),
            new(ItemType.BallAndChain, "BallandChainSheet", SpriteConfig.BALL_AND_CHAIN_FRAME, "Ball & Chain",  SectionWeapons),
            new(ItemType.Axe,          "AxeSheet",          SpriteConfig.AXE_FRAME,            "Axe",           SectionWeapons),
            new(ItemType.ShootingStar, "ShootingStarSheet", SpriteConfig.SHOOTING_STAR_FRAME,  "Shooting Star", SectionWeapons),
            new(ItemType.Lyre,         "LyreSheet",         SpriteConfig.LYRE_FRAME,           "Lyre",          SectionKeyItems),
        };

        /// <summary>
        /// Every enemy type. Sprite facts only — see the SCOPE note in this
        /// file's header for why behaviour is not table-driven.
        /// </summary>
        public static readonly List<EnemyCatalogEntry> Enemies = new()
        {
            new(EnemyType.Guard,  "GuardSheet",  SpriteConfig.GUARD_IDLE[0],  "Guard",  SectionEnemies),
            new(EnemyType.Mask,   "MaskSheet",   SpriteConfig.MASK_ANIM[0],   "Mask",   SectionEnemies),
            new(EnemyType.Boar,   "BoarSheet",   SpriteConfig.BOAR_ANIM[0],   "Boar",   SectionEnemies),
            new(EnemyType.Eye,    "EyeSheet",    SpriteConfig.EYE_ANIM[0],    "Eye",    SectionEnemies),
            new(EnemyType.Wraith, "WraithSheet", SpriteConfig.WRAITH_IDLE[0], "Wraith", SectionEnemies),
        };

        /// <summary>
        /// Look up an item row, or null when the type has no sprite (notably
        /// ItemType.None). Hand-rolled loop over five entries — a dictionary
        /// would cost more to build than these comparisons cost to run.
        /// </summary>
        public static ItemCatalogEntry? FindItem(ItemType type)
        {
            foreach (var e in Items) if (e.Type == type) return e;
            return null;
        }

        public static EnemyCatalogEntry? FindEnemy(EnemyType type)
        {
            foreach (var e in Enemies) if (e.Type == type) return e;
            return null;
        }
    }
}
