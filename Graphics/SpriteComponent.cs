// ============================================================================
// SPRITE COMPONENT - ANIMATED SPRITE RENDERING
// Sorcery+ Remake - Mode 0 Sprite System
// ============================================================================
// Handles sprite rendering with animation support.
// Sprites are extracted from the original Amstrad CPC spritesheet.
//
// COORDINATE SYSTEM:
// - Internal game logic: 160x200 "Amstrad pixels"
// - Rendering: Scaled 4x to 640x400 for modern displays
// - Aspect ratio: 2:1 (wide pixels) maintained automatically
// ============================================================================

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SorceryRemake.Core;
using System;

namespace SorceryRemake.Graphics
{
    /// <summary>
    /// Component that renders an animated sprite from a spritesheet.
    /// </summary>
    public class SpriteComponent : IComponent
    {
        // ====================================================================
        // COMPONENT INTERFACE
        // ====================================================================

        public Entity? Owner { get; set; }

        // ====================================================================
        // SPRITE DATA
        // ====================================================================

        /// <summary>
        /// The spritesheet texture containing all animation frames.
        /// </summary>
        public Texture2D? Texture { get; set; }

        /// <summary>
        /// Source rectangle on the spritesheet for current frame.
        /// Coordinates are in original spritesheet space.
        /// </summary>
        public Rectangle SourceRectangle { get; set; }

        /// <summary>
        /// Tint color applied to the sprite (default: white = no tint).
        /// </summary>
        public Color Tint { get; set; } = Color.White;

        /// <summary>
        /// Sprite origin point for rotation and positioning.
        /// Default: center of the sprite.
        /// </summary>
        public Vector2 Origin { get; set; }

        // ====================================================================
        // ANIMATION STATE
        // ====================================================================

        /// <summary>
        /// Array of source rectangles for each animation frame.
        /// </summary>
        public Rectangle[]? AnimationFrames { get; set; }

        /// <summary>
        /// Current frame index in the animation sequence.
        /// </summary>
        public int CurrentFrame { get; set; }

        /// <summary>
        /// Time per frame in seconds.
        /// Default: 0.1s = 10 FPS animation
        /// </summary>
        public float FrameTime { get; set; } = 0.1f;

        /// <summary>
        /// Accumulated time since last frame change.
        /// </summary>
        private float _frameTimer;

        /// <summary>
        /// Should the animation loop?
        /// </summary>
        public bool IsLooping { get; set; } = true;

        /// <summary>
        /// Is the animation currently playing?
        /// </summary>
        public bool IsPlaying { get; set; } = true;

        // ====================================================================
        // RENDERING SETTINGS
        // ====================================================================

        /// <summary>
        /// Flip the sprite horizontally (for left/right facing).
        /// </summary>
        public bool FlipHorizontal { get; set; }

        /// <summary>
        /// Flip the sprite vertically.
        /// </summary>
        public bool FlipVertical { get; set; }

        // ====================================================================
        // CONSTRUCTOR
        // ====================================================================

        public SpriteComponent(Texture2D? texture, Rectangle sourceRect)
        {
            Texture = texture;
            SourceRectangle = sourceRect;
            Origin = Vector2.Zero; // Top-left origin for pixel-perfect positioning
            CurrentFrame = 0;
            _frameTimer = 0f;
        }

        // ====================================================================
        // ANIMATION CONTROL
        // ====================================================================

        /// <summary>
        /// Set up an animation sequence from an array of frames.
        /// </summary>
        public void SetAnimation(Rectangle[] frames, float frameTime = 0.1f, bool loop = true)
        {
            AnimationFrames = frames;
            FrameTime = frameTime;
            IsLooping = loop;
            CurrentFrame = 0;
            _frameTimer = 0f;
            IsPlaying = true;

            if (frames.Length > 0)
            {
                SourceRectangle = frames[0];
                Origin = Vector2.Zero; // Top-left origin for pixel-perfect positioning
            }
        }

        /// <summary>
        /// Play the animation from the beginning.
        /// </summary>
        public void Play()
        {
            IsPlaying = true;
            CurrentFrame = 0;
            _frameTimer = 0f;
        }

        /// <summary>
        /// Stop the animation.
        /// </summary>
        public void Stop()
        {
            IsPlaying = false;
        }

        // ====================================================================
        // UPDATE - ANIMATION LOGIC
        // ====================================================================

        public void Update(GameTime gameTime)
        {
            if (!IsPlaying || AnimationFrames == null || AnimationFrames.Length == 0)
                return;

            float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _frameTimer += deltaTime;

            // Advance to next frame when timer exceeds frame time
            if (_frameTimer >= FrameTime)
            {
                _frameTimer -= FrameTime;
                CurrentFrame++;

                // Handle looping or stopping at end
                if (CurrentFrame >= AnimationFrames.Length)
                {
                    if (IsLooping)
                    {
                        CurrentFrame = 0;
                    }
                    else
                    {
                        CurrentFrame = AnimationFrames.Length - 1;
                        IsPlaying = false;
                    }
                }

                // Update source rectangle to current frame
                SourceRectangle = AnimationFrames[CurrentFrame];
            }
        }

        // ====================================================================
        // DRAW - RENDER SPRITE
        // ====================================================================

        public void Draw(GameTime gameTime)
        {
            // Drawing is handled by the rendering system
            // This method is kept for interface compliance
        }

        /// <summary>
        /// Draw the sprite using a provided SpriteBatch.
        /// Called by the rendering system.
        /// </summary>
        public void Draw(SpriteBatch spriteBatch, Vector2 renderPosition, float scale)
        {
            if (Texture == null || Owner == null)
                return;

            SpriteEffects effects = SpriteEffects.None;
            if (FlipHorizontal) effects |= SpriteEffects.FlipHorizontally;
            if (FlipVertical) effects |= SpriteEffects.FlipVertically;

            spriteBatch.Draw(
                texture: Texture,
                position: renderPosition,
                sourceRectangle: SourceRectangle,
                color: Tint,
                rotation: Owner.Rotation,
                origin: Origin,
                scale: scale,
                effects: effects,
                layerDepth: 0f
            );
        }
    }
}
