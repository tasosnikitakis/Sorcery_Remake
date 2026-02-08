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
using SorceryRemake.Tiles;
using SorceryRemake.Doors;
using SorceryRemake.Rooms;
using System;
using System.Collections.Generic;

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
        // Matches Python prototype dimensions EXACTLY
        // ====================================================================
        // Python settings.py:
        // BASE_GAME_AREA: 320x144, INFO_PANEL: 56px, SCALE: 3x
        // Result: 960x600 window (960x432 game + 168px info panel)
        // ====================================================================

        // Base dimensions (unscaled Amstrad coordinates)
        private const int BASE_GAME_WIDTH = 320;      // Python: BASE_GAME_AREA_WIDTH
        private const int BASE_GAME_HEIGHT = 144;     // Python: BASE_GAME_AREA_HEIGHT
        private const int BASE_INFO_PANEL_HEIGHT = 56; // Python: BASE_INFO_PANEL_HEIGHT

        // Scale factor
        private const int RENDER_SCALE = 3;           // Python: GLOBAL_SCALE_FACTOR = 3

        // Final window dimensions (scaled)
        private const int WINDOW_WIDTH = BASE_GAME_WIDTH * RENDER_SCALE;  // 320 * 3 = 960
        private const int GAME_AREA_HEIGHT = BASE_GAME_HEIGHT * RENDER_SCALE; // 144 * 3 = 432
        private const int INFO_PANEL_HEIGHT = BASE_INFO_PANEL_HEIGHT * RENDER_SCALE; // 56 * 3 = 168
        private const int WINDOW_HEIGHT = GAME_AREA_HEIGHT + INFO_PANEL_HEIGHT; // 432 + 168 = 600

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
        private Texture2D _tilesetTexture;
        private Texture2D _leftDoorTexture;
        private Texture2D _rightDoorTexture;

        // ====================================================================
        // ROOM MANAGER
        // ====================================================================

        private RoomManager _roomManager;

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

            // Create render target for game area (matches Python's game surface)
            _renderTarget = new RenderTarget2D(
                GraphicsDevice,
                BASE_GAME_WIDTH * RENDER_SCALE,  // 320 * 3 = 960
                BASE_GAME_HEIGHT * RENDER_SCALE  // 144 * 3 = 432
            );

            // ----------------------------------------------------------------
            // PHASE 2: Create player entity with ECS components
            // ----------------------------------------------------------------

            _player = new Entity("Player");

            // Start near top-center so player falls to floor
            _player.Position = new Vector2(148f, 16f);

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

            // Make black background transparent (color key transparency)
            MakeColorTransparent(_spriteSheet, Color.Black);

            // Add sprite component to player now that texture is loaded
            var sprite = new SpriteComponent(_spriteSheet, SpriteConfig.PLAYER_IDLE_FRONT[0]);
            _player.AddComponent(sprite);

            // ----------------------------------------------------------------
            // Load tileset, door spritesheet, and set up rooms (Phase 2C)
            // ----------------------------------------------------------------

            _tilesetTexture = Content.Load<Texture2D>("Tiles");

            _leftDoorTexture = Content.Load<Texture2D>("LeftDoorFrames");
            _rightDoorTexture = Content.Load<Texture2D>("RightDoorFrames");

            // Set up room manager
            _roomManager = new RoomManager();
            _roomManager.SetTextures(_tilesetTexture, _leftDoorTexture, _rightDoorTexture);
            RegisterTestRooms();
            _roomManager.LoadRoom("room_1");

            // Wire tilemap and door collision to physics
            var physics = _player.GetComponent<PhysicsComponent>();
            if (physics != null)
            {
                physics.TileMap = _roomManager.CurrentTileMap;
                UpdateDoorCollision(physics);
            }

            // Set player start position for room 1
            _player.Position = new Vector2(40f, 96f);

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

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // ----------------------------------------------------------------
            // Door transition logic (freezes all gameplay during animation)
            // ----------------------------------------------------------------

            if (_roomManager.IsGameFrozen)
            {
                var result = _roomManager.Update(dt);
                if (result.HasValue)
                {
                    // Animation done - execute room switch
                    Vector2 newPos = _roomManager.ExecuteTransition(PhysicsComponent.HITBOX_WIDTH);
                    _player.Position = newPos;

                    // Re-wire physics to new tilemap and door collision
                    var phys = _player.GetComponent<PhysicsComponent>();
                    if (phys != null)
                    {
                        phys.TileMap = _roomManager.CurrentTileMap;
                        phys.Velocity = Vector2.Zero;
                        UpdateDoorCollision(phys);
                    }
                }
                // Skip all other updates while frozen
            }
            else
            {
                // Normal gameplay
                _player.Update(gameTime);

                // Check door triggers
                _roomManager.CheckDoorTriggers(
                    _player.Position,
                    PhysicsComponent.HITBOX_WIDTH,
                    PhysicsComponent.HITBOX_HEIGHT
                );
            }

            base.Update(gameTime);
        }

        // ====================================================================
        // DRAW LOOP
        // Renders at monitor refresh rate with interpolation
        // ====================================================================

        protected override void Draw(GameTime gameTime)
        {
            // ----------------------------------------------------------------
            // STEP 1: Render to low-res render target (960x432 game area)
            // ----------------------------------------------------------------

            GraphicsDevice.SetRenderTarget(_renderTarget);
            GraphicsDevice.Clear(Color.Black);

            _spriteBatch.Begin(
                samplerState: SamplerState.PointClamp, // Pixel-perfect (no blur)
                sortMode: SpriteSortMode.Deferred
            );

            // Draw tilemap (background tiles)
            _roomManager.CurrentTileMap?.Draw(_spriteBatch, RENDER_SCALE);

            // Draw doors
            _roomManager.DrawDoors(_spriteBatch, RENDER_SCALE);

            // Draw player sprite (on top of tiles)
            DrawPlayer();

            _spriteBatch.End();

            // ----------------------------------------------------------------
            // STEP 2: Render to screen (game area + info panel)
            // ----------------------------------------------------------------

            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Clear(Color.Black);

            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);

            // Draw game area (top portion of window)
            _spriteBatch.Draw(
                _renderTarget,
                new Rectangle(0, 0, WINDOW_WIDTH, GAME_AREA_HEIGHT),
                Color.White
            );

            _spriteBatch.End();

            // ----------------------------------------------------------------
            // STEP 3: Draw info panel (bottom portion, matches Python)
            // ----------------------------------------------------------------

            DrawInfoPanel();

            // ----------------------------------------------------------------
            // STEP 4: Draw debug UI (on top of everything)
            // ----------------------------------------------------------------

            if (_showDebugInfo && _debugFont != null)
            {
                DrawDebugInfo(gameTime);
            }

            base.Draw(gameTime);
        }

        // ====================================================================
        // ROOM CREATION
        // ====================================================================

        /// <summary>
        /// Register test rooms for Phase 2C door testing.
        /// Room 1: Floor + platform, LEFT-opening door at bottom-right.
        /// Room 2: Floor only (empty), RIGHT-opening door at bottom-left.
        /// </summary>
        private void RegisterTestRooms()
        {
            _roomManager.RegisterRoom("room_1", () =>
            {
                var map = new TileMapComponent(_tilesetTexture, 40, 18);
                map.FillRect(0, 0, 40, 18, TileConfig.EMPTY);

                // Floor
                map.DrawHorizontalLine(0, 17, 40, TileConfig.FLOOR_TAN);

                // Platform in the middle
                map.DrawHorizontalLine(14, 12, 8, TileConfig.PLATFORM_LIGHT);

                _roomManager.SetTileMap(map);

                // Left-opening door at the right side of the room
                // Door is 24px tall, bottom at floor row 17: Y = (17*8) - 24 = 112
                // Placed at right edge: X = 320 - 24 = 296
                var door = new DoorComponent(DoorType.LeftOpening, new Vector2(296, 112));
                door.DoorId = "room1_door_right";
                door.TargetRoomId = "room_2";
                door.TargetDoorId = "room2_door_left";

                _roomManager.SetDoors(new List<DoorComponent> { door });
            });

            _roomManager.RegisterRoom("room_2", () =>
            {
                var map = new TileMapComponent(_tilesetTexture, 40, 18);
                map.FillRect(0, 0, 40, 18, TileConfig.EMPTY);

                // Floor only - empty room
                map.DrawHorizontalLine(0, 17, 40, TileConfig.FLOOR_BROWN);

                _roomManager.SetTileMap(map);

                // Right-opening door at the left side of the room
                // Placed at left edge: X = 0
                var door = new DoorComponent(DoorType.RightOpening, new Vector2(0, 112));
                door.DoorId = "room2_door_left";
                door.TargetRoomId = "room_1";
                door.TargetDoorId = "room1_door_right";

                _roomManager.SetDoors(new List<DoorComponent> { door });
            });
        }

        // ====================================================================
        // DOOR COLLISION WIRING
        // ====================================================================

        /// <summary>
        /// Update the physics solid rects from current room doors.
        /// Called after loading a room or executing a transition.
        /// </summary>
        private void UpdateDoorCollision(PhysicsComponent physics)
        {
            physics.SolidRects.Clear();
            foreach (var door in _roomManager.CurrentDoors)
            {
                physics.SolidRects.Add(new Rectangle(
                    (int)door.Position.X, (int)door.Position.Y,
                    DoorConfig.DOOR_WIDTH, DoorConfig.DOOR_HEIGHT
                ));
            }
        }

        // ====================================================================
        // RENDERING HELPERS
        // ====================================================================

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
        /// Draw info panel (HUD) at bottom of screen (matches Python's draw_info_panel).
        /// </summary>
        private void DrawInfoPanel()
        {
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);

            // Info panel background (dark blue, matches Python: rgb(0, 0, 139))
            var infoPanelRect = new Rectangle(
                0,
                GAME_AREA_HEIGHT,
                WINDOW_WIDTH,
                INFO_PANEL_HEIGHT
            );
            DrawFilledRectangle(_spriteBatch, infoPanelRect, new Color(0, 0, 139));

            // TODO: Add text rendering when font is available
            // Python displays:
            // - Location: "X: {player.col} Y: {player.row}"
            // - Carrying: item name or "Nothing"
            // - Energy: energy meter value
            // Text color: Yellow (255, 255, 0)

            _spriteBatch.End();
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
        /// Replace a specific color in a texture with transparent pixels (color key).
        /// Used to remove black backgrounds from sprites.
        /// </summary>
        private void MakeColorTransparent(Texture2D texture, Color colorToReplace)
        {
            // Get all pixel data from the texture
            Color[] data = new Color[texture.Width * texture.Height];
            texture.GetData(data);

            // Replace the specified color with transparent
            for (int i = 0; i < data.Length; i++)
            {
                // Check if pixel matches the color to replace (with small tolerance)
                if (data[i].R == colorToReplace.R &&
                    data[i].G == colorToReplace.G &&
                    data[i].B == colorToReplace.B)
                {
                    data[i] = Color.Transparent; // Make it transparent
                }
            }

            // Set the modified data back to the texture
            texture.SetData(data);
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
