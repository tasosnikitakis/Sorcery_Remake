// ============================================================================
// SPRITE CONFIGURATION
// Sorcery+ Remake - Spritesheet Frame Definitions
// ============================================================================
// This file defines the exact pixel coordinates of each sprite in the
// original Amstrad CPC spritesheet. Extracted manually from:
// "Amstrad CPC - Sorcery - Characters.png"
//
// SPRITE LAYOUT ANALYSIS:
// - Player wizard: Top row, multiple animation frames
// - Color variants: Second row (palette swaps)
// - Enemies: Rows 3-5
//
// Each sprite is 16x16 pixels in the original sheet.
// ============================================================================

using Microsoft.Xna.Framework;

namespace SorceryRemake.Graphics
{
    /// <summary>
    /// Static definitions for all sprite frame coordinates.
    /// </summary>
    public static class SpriteConfig
    {
        // ====================================================================
        // SPRITE DIMENSIONS
        // Native sprite size on the spritesheet (from Python prototype)
        // ====================================================================

        /// <summary>
        /// Native sprite width on spritesheet.
        /// Python prototype used 24x24 pixels.
        /// </summary>
        public const int SPRITE_WIDTH = 24;

        /// <summary>
        /// Native sprite height on spritesheet.
        /// Python prototype used 24x24 pixels.
        /// </summary>
        public const int SPRITE_HEIGHT = 24;

        // ====================================================================
        // PLAYER WIZARD ANIMATIONS
        // Row 4 of spritesheet (Y=75) - Pink/Magenta wizard (PLAYER CHARACTER)
        // Matches Python prototype EXACTLY
        // ====================================================================
        // Python coordinates from main.py:
        // "walk_left":  { "x": 0,   "y": 75, "count": 4, "spacing": 1}
        // "idle_front": { "x": 100, "y": 75, "count": 4, "spacing": 1}
        // "walk_right": { "x": 200, "y": 75, "count": 4, "spacing": 1}
        //
        // With 1px spacing, frames are 25 pixels apart (24 width + 1 spacing)
        // ====================================================================

        /// <summary>
        /// WALK_LEFT animation - First 4 sprites on row 4.
        /// Separate left-facing frames (NOT flipped).
        /// Python: x=0, y=75, count=4, spacing=1
        /// </summary>
        public static readonly Rectangle[] PLAYER_WALK_LEFT = new Rectangle[]
        {
            new Rectangle(0, 75, SPRITE_WIDTH, SPRITE_HEIGHT),   // Frame 0: X=0
            new Rectangle(25, 75, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 1: X=25 (24+1)
            new Rectangle(50, 75, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 2: X=50 (24+1+24+1)
            new Rectangle(75, 75, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 3: X=75
        };

        /// <summary>
        /// IDLE_FRONT animation - Middle 4 sprites on row 4.
        /// Front-facing idle/hovering animation.
        /// Python: x=100, y=75, count=4, spacing=1
        /// </summary>
        public static readonly Rectangle[] PLAYER_IDLE_FRONT = new Rectangle[]
        {
            new Rectangle(100, 75, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 0: X=100
            new Rectangle(125, 75, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 1: X=125
            new Rectangle(150, 75, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 2: X=150
            new Rectangle(175, 75, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 3: X=175
        };

        /// <summary>
        /// WALK_RIGHT animation - Last 4 sprites on row 4.
        /// Right-facing walking animation.
        /// Python: x=200, y=75, count=4, spacing=1
        /// </summary>
        public static readonly Rectangle[] PLAYER_WALK_RIGHT = new Rectangle[]
        {
            new Rectangle(200, 75, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 0: X=200
            new Rectangle(225, 75, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 1: X=225
            new Rectangle(250, 75, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 2: X=250
            new Rectangle(275, 75, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 3: X=275
        };

        // ====================================================================
        // NOTE: Flying_up and Falling animations don't exist on row 4
        // Python prototype only used walk_left, idle_front, and walk_right
        // Vertical movement in Python just showed idle_front
        // ====================================================================

        /// <summary>
        /// Flying up - Use IDLE for now (matches Python behavior).
        /// No specific flying_up animation on row 4.
        /// </summary>
        public static readonly Rectangle[] PLAYER_FLYING_UP = PLAYER_IDLE_FRONT;

        /// <summary>
        /// Falling - Use IDLE for now (matches Python behavior).
        /// No specific falling animation on row 4.
        /// </summary>
        public static readonly Rectangle[] PLAYER_FALLING = PLAYER_IDLE_FRONT;

        // ====================================================================
        // LEGACY ANIMATION ALIASES (For backward compatibility)
        // ====================================================================

        /// <summary>
        /// Alias for PLAYER_IDLE_FRONT (original name).
        /// </summary>
        public static readonly Rectangle[] PLAYER_IDLE = PLAYER_IDLE_FRONT;

        /// <summary>
        /// Alias for PLAYER_WALK_RIGHT (original name).
        /// </summary>
        public static readonly Rectangle[] PLAYER_FLYING_RIGHT = PLAYER_WALK_RIGHT;

        // ====================================================================
        // HOODED GUARD ANIMATIONS
        // Dedicated spritesheet: GuardSheet.png (extracted from Characters.png row 2)
        // Green top-line cropped, 12 frames at Y=0 with 1px spacing
        // 4 walk left, 4 direction change/idle, 4 walk right
        // ====================================================================

        /// <summary>
        /// Guard WALK_LEFT animation - First 4 frames.
        /// </summary>
        public static readonly Rectangle[] GUARD_WALK_LEFT = new Rectangle[]
        {
            new Rectangle(0, 0, SPRITE_WIDTH, SPRITE_HEIGHT),   // Frame 0
            new Rectangle(25, 0, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 1
            new Rectangle(50, 0, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 2
            new Rectangle(75, 0, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 3
        };

        /// <summary>
        /// Guard IDLE animation - Middle 4 frames.
        /// Shown when guard is idle or changing direction.
        /// </summary>
        public static readonly Rectangle[] GUARD_IDLE = new Rectangle[]
        {
            new Rectangle(100, 0, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 0
            new Rectangle(125, 0, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 1
            new Rectangle(150, 0, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 2
            new Rectangle(175, 0, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 3
        };

        /// <summary>
        /// Guard WALK_RIGHT animation - Last 4 frames.
        /// </summary>
        public static readonly Rectangle[] GUARD_WALK_RIGHT = new Rectangle[]
        {
            new Rectangle(200, 0, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 0
            new Rectangle(225, 0, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 1
            new Rectangle(250, 0, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 2
            new Rectangle(275, 0, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 3
        };

        /// <summary>
        /// Guard animation speed (seconds per frame).
        /// </summary>
        public const float GUARD_ANIMATION_SPEED = 0.12f;

        // ====================================================================
        // MASK ENEMY ANIMATIONS
        // Dedicated spritesheet: MaskSheet.png (extracted from Characters.png row 1)
        // 4 frames at Y=0 with 1px spacing, single looping animation
        // ====================================================================

        /// <summary>
        /// Mask animation - 4 frames, loops continuously regardless of state.
        /// </summary>
        public static readonly Rectangle[] MASK_ANIM = new Rectangle[]
        {
            new Rectangle(0, 0, SPRITE_WIDTH, SPRITE_HEIGHT),   // Frame 0
            new Rectangle(25, 0, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 1
            new Rectangle(50, 0, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 2
            new Rectangle(75, 0, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 3
        };

        /// <summary>
        /// Mask animation speed (seconds per frame).
        /// </summary>
        public const float MASK_ANIMATION_SPEED = 0.12f;

        /// <summary>
        /// Mask movement speed (pixels/second). Slightly slower than player (200).
        /// </summary>
        public const float MASK_SPEED = 150f;

        // ====================================================================
        // WILD BOAR ENEMY ANIMATIONS
        // Dedicated spritesheet: BoarSheet.png (extracted from Characters.png row 1, frames 5-8)
        // 4 frames at Y=0 with 1px spacing, single looping animation
        // Same floating behavior as mask
        // ====================================================================

        /// <summary>
        /// Boar sprite width (22px, 2px thinner than standard 24px).
        /// </summary>
        public const int BOAR_SPRITE_WIDTH = 22;

        /// <summary>
        /// Boar animation - 4 frames of 22x24, loops continuously regardless of state.
        /// BoarSheet.png layout: 22px frames with 1px gaps (22+1+22+1+22+1+22 = 91px wide).
        /// Green line cropped: frame 1=0, frame 2=1px, frame 3=2px, frame 4=1px from top.
        /// </summary>
        public static readonly Rectangle[] BOAR_ANIM = new Rectangle[]
        {
            new Rectangle(0, 0, BOAR_SPRITE_WIDTH, SPRITE_HEIGHT),    // Frame 0
            new Rectangle(23, 1, BOAR_SPRITE_WIDTH, SPRITE_HEIGHT - 1),  // Frame 1 (1px top crop)
            new Rectangle(46, 2, BOAR_SPRITE_WIDTH, SPRITE_HEIGHT - 2),  // Frame 2 (2px top crop)
            new Rectangle(69, 1, BOAR_SPRITE_WIDTH, SPRITE_HEIGHT - 1),  // Frame 3 (1px top crop)
        };

        /// <summary>
        /// Boar animation speed (seconds per frame).
        /// </summary>
        public const float BOAR_ANIMATION_SPEED = 0.12f;

        /// <summary>
        /// Boar movement speed (pixels/second). Slightly slower than player (200).
        /// </summary>
        public const float BOAR_SPEED = 150f;

        // ====================================================================
        // EYE ENEMY ANIMATIONS
        // Dedicated spritesheet: EyeSheet.png (extracted from Characters.png row 1, last 4 frames)
        // 4 frames at Y=0 with 1px spacing, 24x17 per frame, single looping animation
        // Same floating behavior as mask/boar
        // ====================================================================

        /// <summary>
        /// Eye sprite height (17px, shorter than standard 24px).
        /// </summary>
        public const int EYE_SPRITE_HEIGHT = 17;

        /// <summary>
        /// Eye animation - 4 frames of 24x17, loops continuously regardless of state.
        /// EyeSheet.png layout: 24px frames with 1px gaps (24+1+24+1+24+1+24 = 99px wide).
        /// </summary>
        public static readonly Rectangle[] EYE_ANIM = new Rectangle[]
        {
            new Rectangle(0, 0, SPRITE_WIDTH, EYE_SPRITE_HEIGHT),    // Frame 0
            new Rectangle(25, 0, SPRITE_WIDTH, EYE_SPRITE_HEIGHT),   // Frame 1
            new Rectangle(50, 0, SPRITE_WIDTH, EYE_SPRITE_HEIGHT),   // Frame 2
            new Rectangle(75, 0, SPRITE_WIDTH, EYE_SPRITE_HEIGHT),   // Frame 3
        };

        /// <summary>
        /// Eye animation speed (seconds per frame).
        /// </summary>
        public const float EYE_ANIMATION_SPEED = 0.12f;

        /// <summary>
        /// Eye movement speed (pixels/second). Slightly slower than player (200).
        /// </summary>
        public const float EYE_SPEED = 150f;

        // ====================================================================
        // WRAITH ENEMY ANIMATIONS
        // Dedicated spritesheet: WraithSheet.png (extracted from Characters.png)
        // 12 frames at Y=0 with 1px spacing, 24x23 per frame
        // Same pattern as guard: 4 walk left, 4 idle, 4 walk right
        // Frames 1,5,6,7,8,9 (1-indexed) need 1px top crop
        // Wraith passes through all world geometry, slightly faster than player
        // ====================================================================

        /// <summary>
        /// Wraith sprite height (23px).
        /// </summary>
        public const int WRAITH_SPRITE_HEIGHT = 23;

        /// <summary>
        /// Wraith WALK_LEFT animation - First 4 frames.
        /// Frame 1 (index 0) has 1px top crop.
        /// </summary>
        public static readonly Rectangle[] WRAITH_WALK_LEFT = new Rectangle[]
        {
            new Rectangle(0, 1, SPRITE_WIDTH, WRAITH_SPRITE_HEIGHT - 1),    // Frame 0 (1px crop)
            new Rectangle(25, 0, SPRITE_WIDTH, WRAITH_SPRITE_HEIGHT),       // Frame 1
            new Rectangle(50, 0, SPRITE_WIDTH, WRAITH_SPRITE_HEIGHT),       // Frame 2
            new Rectangle(75, 0, SPRITE_WIDTH, WRAITH_SPRITE_HEIGHT),       // Frame 3
        };

        /// <summary>
        /// Wraith IDLE animation - Middle 4 frames.
        /// Frames 5,6,7,8 (1-indexed, index 4-7) all have 1px top crop.
        /// </summary>
        public static readonly Rectangle[] WRAITH_IDLE = new Rectangle[]
        {
            new Rectangle(100, 1, SPRITE_WIDTH, WRAITH_SPRITE_HEIGHT - 1),  // Frame 4 (1px crop)
            new Rectangle(125, 1, SPRITE_WIDTH, WRAITH_SPRITE_HEIGHT - 1),  // Frame 5 (1px crop)
            new Rectangle(150, 1, SPRITE_WIDTH, WRAITH_SPRITE_HEIGHT - 1),  // Frame 6 (1px crop)
            new Rectangle(175, 1, SPRITE_WIDTH, WRAITH_SPRITE_HEIGHT - 1),  // Frame 7 (1px crop)
        };

        /// <summary>
        /// Wraith WALK_RIGHT animation - Last 4 frames.
        /// Frame 9 (1-indexed, index 8) has 1px top crop.
        /// </summary>
        public static readonly Rectangle[] WRAITH_WALK_RIGHT = new Rectangle[]
        {
            new Rectangle(200, 1, SPRITE_WIDTH, WRAITH_SPRITE_HEIGHT - 1),  // Frame 8 (1px crop)
            new Rectangle(225, 0, SPRITE_WIDTH, WRAITH_SPRITE_HEIGHT),      // Frame 9
            new Rectangle(250, 0, SPRITE_WIDTH, WRAITH_SPRITE_HEIGHT),      // Frame 10
            new Rectangle(275, 0, SPRITE_WIDTH, WRAITH_SPRITE_HEIGHT),      // Frame 11
        };

        /// <summary>
        /// Wraith animation speed (seconds per frame).
        /// </summary>
        public const float WRAITH_ANIMATION_SPEED = 0.12f;

        /// <summary>
        /// Wraith movement speed (pixels/second). Slightly faster than player (200).
        /// </summary>
        public const float WRAITH_SPEED = 220f;

        /// <summary>
        /// Dead zone threshold to prevent wraith jitter when near player.
        /// </summary>
        public const float WRAITH_FOLLOW_THRESHOLD = 2f;

        // ====================================================================
        // ENEMY DEATH ANIMATION
        // Dedicated spritesheet: EnemyDeathSheet.png
        // 4 frames of 48x48, stacked vertically, plays once top to bottom
        // ====================================================================

        /// <summary>
        /// Death animation frame size.
        /// </summary>
        public const int DEATH_FRAME_WIDTH = 48;
        public const int DEATH_FRAME_HEIGHT = 48;

        /// <summary>
        /// Death animation - 4 frames of 48x48, vertical strip, plays once.
        /// </summary>
        public static readonly Rectangle[] ENEMY_DEATH_ANIM = new Rectangle[]
        {
            new Rectangle(0, 0, DEATH_FRAME_WIDTH, DEATH_FRAME_HEIGHT),    // Frame 0
            new Rectangle(0, 48, DEATH_FRAME_WIDTH, DEATH_FRAME_HEIGHT),   // Frame 1
            new Rectangle(0, 96, DEATH_FRAME_WIDTH, DEATH_FRAME_HEIGHT),   // Frame 2
            new Rectangle(0, 144, DEATH_FRAME_WIDTH, DEATH_FRAME_HEIGHT),  // Frame 3
        };

        /// <summary>
        /// Death animation speed (seconds per frame).
        /// </summary>
        public const float DEATH_ANIMATION_SPEED = 0.12f;

        // ====================================================================
        // ITEM SPRITES
        // Items are 48x48 source, rendered at 24x24 in the game world
        // ====================================================================

        /// <summary>
        /// Item source frame size (48x48 on spritesheet).
        /// </summary>
        public const int ITEM_SOURCE_SIZE = 48;

        /// <summary>
        /// Item display size in game world (24x24).
        /// </summary>
        public const int ITEM_DISPLAY_SIZE = 24;

        /// <summary>
        /// Sword sprite frame (full 48x48 sheet).
        /// </summary>
        public static readonly Rectangle SWORD_FRAME = new Rectangle(0, 0, ITEM_SOURCE_SIZE, ITEM_SOURCE_SIZE);

        /// <summary>
        /// Ball and Chain sprite frame (full 48x48 sheet).
        /// </summary>
        public static readonly Rectangle BALL_AND_CHAIN_FRAME = new Rectangle(0, 0, ITEM_SOURCE_SIZE, ITEM_SOURCE_SIZE);

        /// <summary>
        /// Axe sprite frame (full 48x48 sheet).
        /// </summary>
        public static readonly Rectangle AXE_FRAME = new Rectangle(0, 0, ITEM_SOURCE_SIZE, ITEM_SOURCE_SIZE);

        /// <summary>
        /// Shooting Star sprite frame (full 48x48 sheet).
        /// </summary>
        public static readonly Rectangle SHOOTING_STAR_FRAME = new Rectangle(0, 0, ITEM_SOURCE_SIZE, ITEM_SOURCE_SIZE);

        /// <summary>
        /// Projectile speed (pixels/second). Same as player speed.
        /// </summary>
        public const float PROJECTILE_SPEED = 200f;

        // ====================================================================
        // CAPTIVE WIZARD
        // CaptiveWizardSheet.png: 48x192, 4 frames of 48x48 vertical
        // Cycles bottom to top continuously. Rendered at 24x24.
        // ====================================================================

        /// <summary>
        /// Captive wizard animation - 4 frames of 48x48, bottom to top.
        /// </summary>
        public static readonly Rectangle[] CAPTIVE_WIZARD_ANIM = new Rectangle[]
        {
            new Rectangle(0, 144, ITEM_SOURCE_SIZE, ITEM_SOURCE_SIZE), // Frame 3 (bottom)
            new Rectangle(0, 96, ITEM_SOURCE_SIZE, ITEM_SOURCE_SIZE),  // Frame 2
            new Rectangle(0, 48, ITEM_SOURCE_SIZE, ITEM_SOURCE_SIZE),  // Frame 1
            new Rectangle(0, 0, ITEM_SOURCE_SIZE, ITEM_SOURCE_SIZE),   // Frame 0 (top)
        };

        public const float CAPTIVE_WIZARD_ANIMATION_SPEED = 0.15f;

        // ====================================================================
        // STAR (saved wizard transformation)
        // StarSheet.png: 48x192, 4 frames of 48x48 vertical
        // Plays top to bottom once, then wizard flies upward.
        // ====================================================================

        /// <summary>
        /// Star animation - 4 frames of 48x48, top to bottom.
        /// </summary>
        public static readonly Rectangle[] STAR_ANIM = new Rectangle[]
        {
            new Rectangle(0, 0, ITEM_SOURCE_SIZE, ITEM_SOURCE_SIZE),   // Frame 0 (top)
            new Rectangle(0, 48, ITEM_SOURCE_SIZE, ITEM_SOURCE_SIZE),  // Frame 1
            new Rectangle(0, 96, ITEM_SOURCE_SIZE, ITEM_SOURCE_SIZE),  // Frame 2
            new Rectangle(0, 144, ITEM_SOURCE_SIZE, ITEM_SOURCE_SIZE), // Frame 3 (bottom)
        };

        public const float STAR_ANIMATION_SPEED = 0.12f;

        /// <summary>
        /// Guard movement speed (pixels/second). Slower than player.
        /// </summary>
        public const float GUARD_SPEED = 80f;

        /// <summary>
        /// Dead zone threshold to prevent guard jitter when near player.
        /// </summary>
        public const float GUARD_FOLLOW_THRESHOLD = 2f;

        // ====================================================================
        // ANIMATION TIMING CONSTANTS
        // Based on Python prototype settings
        // ====================================================================

        /// <summary>
        /// Idle animation speed (seconds per frame).
        /// Python: PLAYER_ANIMATION_TICKS_PER_FRAME = 7
        /// At 60 FPS: 7 ticks / 60 = 0.117 seconds per frame
        /// </summary>
        public const float PLAYER_IDLE_ANIMATION_SPEED = 0.117f;

        /// <summary>
        /// Walk/fly animation speed (seconds per frame).
        /// Slightly faster than idle for dynamic movement.
        /// </summary>
        public const float PLAYER_WALK_ANIMATION_SPEED = 0.1f;

        /// <summary>
        /// Thrust/fall animation speed (seconds per frame).
        /// Fastest animation for intense actions.
        /// </summary>
        public const float PLAYER_THRUST_ANIMATION_SPEED = 0.08f;

        /// <summary>
        /// Default animation speed for player (backwards compatibility).
        /// </summary>
        public const float PLAYER_ANIMATION_SPEED = PLAYER_WALK_ANIMATION_SPEED;

        // ====================================================================
        // VELOCITY THRESHOLD (from Python prototype)
        // ====================================================================

        /// <summary>
        /// Minimum velocity (pixels/second) to trigger walk animation.
        /// Python: PLAYER_ANIMATION_VELOCITY_THRESHOLD = 0.1 * GLOBAL_SCALE_FACTOR
        ///         = 0.1 * 3 = 0.3 pixels per frame
        /// At 60 FPS: 0.3 * 60 = 18 pixels/second
        ///
        /// Adjusted for 160x200 Mode 0:
        /// Python used 320x144 scaled 3x (960x600 window)
        /// We use 160x200 scaled 4x (640x400 window)
        /// Threshold is halved to match the smaller coordinate space.
        /// </summary>
        public const float ANIMATION_VELOCITY_THRESHOLD = 10f; // pixels/second
    }

    // ========================================================================
    // PYTHON TO C# MAPPING REFERENCE
    // ========================================================================
    // This section documents the mapping from the Python prototype.
    //
    // Python Settings (settings.py):
    // - PLAYER_SPRITE_WIDTH = 24
    // - PLAYER_SPRITE_HEIGHT = 24
    // - PLAYER_ANIMATION_TICKS_PER_FRAME = 7
    // - PLAYER_ANIMATION_VELOCITY_THRESHOLD = 0.1 * GLOBAL_SCALE_FACTOR (= 0.3)
    // - PLAYER_SPEED_PPS = 500  (pixels per second)
    // - PLAYER_GRAVITY_PPS = 300 (pixels per second)
    //
    // Python Animation Names:
    // - "idle_front" → PLAYER_IDLE_FRONT
    // - "walk_right" → PLAYER_WALK_RIGHT
    // - "walk_left"  → PLAYER_WALK_LEFT (FlipHorizontal = true)
    //
    // Animation State Logic (from player.py update_animation):
    // - vel_x > threshold  → walk_right
    // - vel_x < -threshold → walk_left
    // - vel_y != 0         → idle_front (for vertical movement)
    // - else               → idle_front (default)
    //
    // NOTE: The Python version used a simpler animation system for vertical
    // movement (just showed idle_front). Our C# version adds FLYING_UP and
    // FALLING animations for better visual feedback in flight physics.
    // ========================================================================
}

