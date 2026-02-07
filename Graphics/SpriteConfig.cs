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
        // ENEMY SPRITES (For future phases)
        // Rows 3-5 of spritesheet
        // ====================================================================

        /// <summary>
        /// Example enemy sprite (to be defined later).
        /// </summary>
        public static readonly Rectangle ENEMY_GHOST = new Rectangle(0, 32, SPRITE_WIDTH, SPRITE_HEIGHT);

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

