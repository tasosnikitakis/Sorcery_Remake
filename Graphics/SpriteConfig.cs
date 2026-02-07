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
        // Row 1 of spritesheet - Yellow wizard (main player)
        // Matches Python prototype animation names and structure
        // ====================================================================

        /// <summary>
        /// IDLE_FRONT animation (Python prototype name).
        /// Front-facing idle/hovering animation.
        /// Yellow wizard at top of spritesheet.
        /// 4 frames for smooth idle bob.
        /// </summary>
        public static readonly Rectangle[] PLAYER_IDLE_FRONT = new Rectangle[]
        {
            new Rectangle(0, 0, SPRITE_WIDTH, SPRITE_HEIGHT),    // Frame 0
            new Rectangle(24, 0, SPRITE_WIDTH, SPRITE_HEIGHT),   // Frame 1
            new Rectangle(48, 0, SPRITE_WIDTH, SPRITE_HEIGHT),   // Frame 2
            new Rectangle(72, 0, SPRITE_WIDTH, SPRITE_HEIGHT),   // Frame 3
        };

        /// <summary>
        /// WALK_RIGHT animation (Python prototype name).
        /// Flying/walking right animation.
        /// 4 frames for smooth horizontal movement.
        /// </summary>
        public static readonly Rectangle[] PLAYER_WALK_RIGHT = new Rectangle[]
        {
            new Rectangle(96, 0, SPRITE_WIDTH, SPRITE_HEIGHT),   // Frame 0
            new Rectangle(120, 0, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 1
            new Rectangle(144, 0, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 2
            new Rectangle(168, 0, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 3
        };

        /// <summary>
        /// WALK_LEFT animation (Python prototype name).
        /// Flying/walking left animation.
        /// Uses WALK_RIGHT frames with horizontal flip (FlipHorizontal = true).
        /// </summary>
        public static readonly Rectangle[] PLAYER_WALK_LEFT = PLAYER_WALK_RIGHT;

        /// <summary>
        /// Flying up (thrusting) animation.
        /// For Mode 0 flight physics when pressing Up.
        /// </summary>
        public static readonly Rectangle[] PLAYER_FLYING_UP = new Rectangle[]
        {
            new Rectangle(192, 0, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 0
            new Rectangle(216, 0, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 1
        };

        /// <summary>
        /// Falling down animation.
        /// Triggered when falling fast (velocity.Y > threshold).
        /// </summary>
        public static readonly Rectangle[] PLAYER_FALLING = new Rectangle[]
        {
            new Rectangle(240, 0, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 0
            new Rectangle(264, 0, SPRITE_WIDTH, SPRITE_HEIGHT),  // Frame 1
        };

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

