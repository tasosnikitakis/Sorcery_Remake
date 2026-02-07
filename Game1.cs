// ============================================================================
// MAIN GAME CLASS
// Sorcery+ Remake - MonoGame Entry Point
// ============================================================================
// This is the core game loop that manages:
// - Window initialization with correct Mode 0 aspect ratio
// - Asset loading (spritesheet)
// - Player entity creation with ECS components
// - Update/Draw loops with fixed timestep physics
//
// RENDERING STRATEGY:
// - Internal logic: 160x200 "Amstrad pixels"
// - Render target: 640x400 (4x scale for modern displays)
// - Final output: Scaled to window with letterboxing
// ============================================================================

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SorceryRemake.Core;
using SorceryRemake.Physics;
using SorceryRemake.Graphics;
using System;

namespace SorceryRemake
{
    public class Game1 : Game
    {
        // ====================================================================
        // GRAPHICS DEVICES
        // ====================================================================

        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;

        // ====================================================================
        // RENDERING CONSTANTS
        // Mode 0 dimensions and scaling factors
        // ====================================================================

        private const int AMSTRAD_WIDTH = 160;   // Original horizontal resolution
        private const int AMSTRAD_HEIGHT = 200;  // Original vertical resolution
        private const int RENDER_SCALE = 4;      // 4x scale = 640x400 render target
        private const int WINDOW_WIDTH = 640;    // Default window size
        private const int WINDOW_HEIGHT = 400;

        // ====================================================================
        // RENDER TARGET
        // We render to a low-res target then scale up for pixel-perfect look
        // ====================================================================

        private RenderTarget2D _renderTarget;

        // ====================================================================
        // GAME ENTITIES
        // ====================================================================

        private Entity _player;

        // ====================================================================
        // ASSETS
        // ====================================================================

        private Texture2D _spriteSheet;

        // ====================================================================
        // DEBUG
        // ====================================================================

        private SpriteFont _debugFont;
        private bool _showDebugInfo = true;

        // ====================================================================
        // CONSTRUCTOR
        // ====================================================================

        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;

            // Configure window for Mode 0 aspect ratio
            _graphics.PreferredBackBufferWidth = WINDOW_WIDTH;
            _graphics.PreferredBackBufferHeight = WINDOW_HEIGHT;
        }

        // ====================================================================
        // INITIALIZATION
        // ====================================================================

        protected override void Initialize()
        {
            // ----------------------------------------------------------------
            // PHASE 1: Initialize rendering system
            // ----------------------------------------------------------------

            // Create render target at 4x Amstrad resolution
            _renderTarget = new RenderTarget2D(
                GraphicsDevice,
                AMSTRAD_WIDTH * RENDER_SCALE,
                AMSTRAD_HEIGHT * RENDER_SCALE
            );

            // ----------------------------------------------------------------
            // PHASE 2: Create player entity with ECS components
            // ----------------------------------------------------------------

            _player = new Entity("Player");

            // Set starting position (center of screen in Amstrad coordinates)
            _player.Position = new Vector2(AMSTRAD_WIDTH / 2f, AMSTRAD_HEIGHT / 2f);

            // Add physics component for flight mechanics
            var physics = new PhysicsComponent();
            _player.AddComponent(physics);

            // Add player controller for input handling
            var controller = new PlayerController();
            _player.AddComponent(controller);

            base.Initialize();

            // Initialize player controller after all components are added
            controller.Initialize();
        }

        // ====================================================================
        // CONTENT LOADING
        // ====================================================================

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);

            // ----------------------------------------------------------------
            // Load spritesheet from assets
            // ----------------------------------------------------------------

            try
            {
                // Try to load the spritesheet
                // Note: This requires the PNG to be added to Content.mgcb
                _spriteSheet = Content.Load<Texture2D>("Characters");
            }
            catch (Exception)
            {
                // If content pipeline isn't set up, load directly from file
                // This is a fallback for initial testing
                try
                {
                    using (var stream = System.IO.File.OpenRead(
                        @"assets\images\Amstrad CPC - Sorcery - Characters.png"))
                    {
                        _spriteSheet = Texture2D.FromStream(GraphicsDevice, stream);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load spritesheet: {ex.Message}");
                    // Create a placeholder texture
                    _spriteSheet = new Texture2D(GraphicsDevice, 16, 16);
                    Color[] data = new Color[16 * 16];
                    for (int i = 0; i < data.Length; i++)
                        data[i] = Color.Magenta; // Bright magenta = missing texture
                    _spriteSheet.SetData(data);
                }
            }

            // Add sprite component to player now that texture is loaded
            var sprite = new SpriteComponent(_spriteSheet, SpriteConfig.PLAYER_IDLE[0]);
            _player.AddComponent(sprite);

            // Try to load debug font
            try
            {
                _debugFont = Content.Load<SpriteFont>("DebugFont");
            }
            catch
            {
                _debugFont = null; // No debug text if font is missing
            }
        }

        // ====================================================================
        // UPDATE LOOP
        // Called at fixed 60 FPS
        // ====================================================================

        protected override void Update(GameTime gameTime)
        {
            // ----------------------------------------------------------------
            // Global input: Exit game
            // ----------------------------------------------------------------

            if (Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();

            // ----------------------------------------------------------------
            // Toggle debug info
            // ----------------------------------------------------------------

            if (Keyboard.GetState().IsKeyDown(Keys.F1))
                _showDebugInfo = !_showDebugInfo;

            // ----------------------------------------------------------------
            // Update player entity (this updates all components)
            // ----------------------------------------------------------------

            _player.Update(gameTime);

            base.Update(gameTime);
        }

        // ====================================================================
        // DRAW LOOP
        // Renders at monitor refresh rate with interpolation
        // ====================================================================

        protected override void Draw(GameTime gameTime)
        {
            // ----------------------------------------------------------------
            // STEP 1: Render to low-res render target (640x400)
            // ----------------------------------------------------------------

            GraphicsDevice.SetRenderTarget(_renderTarget);
            GraphicsDevice.Clear(Color.Black);

            _spriteBatch.Begin(
                samplerState: SamplerState.PointClamp, // Pixel-perfect (no blur)
                sortMode: SpriteSortMode.Deferred
            );

            // Draw the test room (simple background for Phase 1)
            DrawTestRoom();

            // Draw player sprite
            DrawPlayer();

            _spriteBatch.End();

            // ----------------------------------------------------------------
            // STEP 2: Render the target to the screen (scaled up)
            // ----------------------------------------------------------------

            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Clear(Color.Black);

            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);

            _spriteBatch.Draw(
                _renderTarget,
                new Rectangle(0, 0, WINDOW_WIDTH, WINDOW_HEIGHT),
                Color.White
            );

            _spriteBatch.End();

            // ----------------------------------------------------------------
            // STEP 3: Draw debug UI (on top of everything)
            // ----------------------------------------------------------------

            if (_showDebugInfo && _debugFont != null)
            {
                DrawDebugInfo(gameTime);
            }

            base.Draw(gameTime);
        }

        // ====================================================================
        // RENDERING HELPERS
        // ====================================================================

        /// <summary>
        /// Draw a simple test room (placeholder for Phase 1).
        /// </summary>
        private void DrawTestRoom()
        {
            // Create a simple room with walls
            // For Phase 1, we'll just draw colored rectangles

            // Floor (green)
            var floorRect = new Rectangle(
                0,
                (int)(180 * RENDER_SCALE),
                AMSTRAD_WIDTH * RENDER_SCALE,
                AMSTRAD_HEIGHT * RENDER_SCALE
            );
            DrawFilledRectangle(_spriteBatch, floorRect, new Color(0, 128, 0));

            // Left wall (dark gray)
            var leftWall = new Rectangle(0, 0, 10 * RENDER_SCALE, AMSTRAD_HEIGHT * RENDER_SCALE);
            DrawFilledRectangle(_spriteBatch, leftWall, new Color(64, 64, 64));

            // Right wall (dark gray)
            var rightWall = new Rectangle(
                (AMSTRAD_WIDTH - 10) * RENDER_SCALE,
                0,
                10 * RENDER_SCALE,
                AMSTRAD_HEIGHT * RENDER_SCALE
            );
            DrawFilledRectangle(_spriteBatch, rightWall, new Color(64, 64, 64));

            // Ceiling (dark blue)
            var ceiling = new Rectangle(0, 0, AMSTRAD_WIDTH * RENDER_SCALE, 10 * RENDER_SCALE);
            DrawFilledRectangle(_spriteBatch, ceiling, new Color(0, 0, 128));
        }

        /// <summary>
        /// Draw the player sprite at the correct scaled position.
        /// </summary>
        private void DrawPlayer()
        {
            var sprite = _player.GetComponent<SpriteComponent>();
            if (sprite == null) return;

            // Convert Amstrad coordinates to render target coordinates
            Vector2 renderPos = _player.Position * RENDER_SCALE;

            sprite.Draw(_spriteBatch, renderPos, RENDER_SCALE);
        }

        /// <summary>
        /// Draw debug information overlay.
        /// </summary>
        private void DrawDebugInfo(GameTime gameTime)
        {
            if (_debugFont == null) return;

            _spriteBatch.Begin();

            var physics = _player.GetComponent<PhysicsComponent>();

            string debugText = $"SORCERY+ REMAKE - PHASE 1 PROTOTYPE\n" +
                               $"FPS: {1.0 / gameTime.ElapsedGameTime.TotalSeconds:F0}\n" +
                               $"Position: ({_player.Position.X:F1}, {_player.Position.Y:F1})\n" +
                               $"Velocity: ({physics?.Velocity.X:F1}, {physics?.Velocity.Y:F1})\n" +
                               $"\n" +
                               $"Controls:\n" +
                               $"  Arrow Keys - Move\n" +
                               $"  Up - Thrust (fight gravity)\n" +
                               $"  F1 - Toggle debug info\n" +
                               $"  ESC - Exit";

            _spriteBatch.DrawString(_debugFont, debugText, new Vector2(10, 10), Color.Yellow);

            _spriteBatch.End();
        }

        /// <summary>
        /// Helper to draw filled rectangles (for test room).
        /// </summary>
        private void DrawFilledRectangle(SpriteBatch spriteBatch, Rectangle rect, Color color)
        {
            // Create a 1x1 white texture if we don't have one
            Texture2D pixel = new Texture2D(GraphicsDevice, 1, 1);
            pixel.SetData(new[] { Color.White });

            spriteBatch.Draw(pixel, rect, color);
        }
    }
}
