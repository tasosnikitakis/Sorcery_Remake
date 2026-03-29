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
using SorceryRemake.Enemies;
using System;
using System.IO;
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

        // Per-room enemy system
        private List<EnemyInstance> _roomEnemies = new List<EnemyInstance>();
        private HashSet<string> _deadEnemies = new HashSet<string>(); // permanently killed
        private Dictionary<string, List<EnemyInstance>> _savedRoomEnemies = new Dictionary<string, List<EnemyInstance>>();
        private int _spawnCounter = 0; // unique IDs for debug-spawned enemies
        private Random _rng = new Random();
        private KeyboardState _previousKeyState;

        // Item/inventory system
        private enum ItemType { None, Sword, BallAndChain, Axe, ShootingStar, Lyre }
        private ItemType _carriedItem = ItemType.None;
        private List<ItemInstance> _roomItems = new List<ItemInstance>();
        private HashSet<string> _pickedUpItems = new HashSet<string>(); // permanently picked up
        private List<Projectile> _projectiles = new List<Projectile>();

        // Captive wizard system
        private List<CaptiveWizard> _roomWizards = new List<CaptiveWizard>();
        private HashSet<string> _savedWizards = new HashSet<string>(); // permanently saved
        private int _savedWizardCount = 0;

        // Blocked door system
        private List<BlockedDoorInstance> _roomBlockedDoors = new List<BlockedDoorInstance>();
        private HashSet<string> _unlockedDoors = new HashSet<string>(); // permanently unlocked

        // ====================================================================
        // ASSETS
        // ====================================================================

        private Texture2D _spriteSheet;
        private Texture2D _tilesetTexture;
        private Texture2D _leftDoorTexture;
        private Texture2D _rightDoorTexture;
        private Texture2D _guardSheet;
        private Texture2D _maskSheet;
        private Texture2D _boarSheet;
        private Texture2D _eyeSheet;
        private Texture2D _wraithSheet;
        private Texture2D _deathSheet;
        private Texture2D _swordSheet;
        private Texture2D _ballAndChainSheet;
        private Texture2D _axeSheet;
        private Texture2D _shootingStarSheet;
        private Texture2D _pixelTexture; // 1x1 white texture for projectile rendering
        private Texture2D _captiveWizardSheet;
        private Texture2D _starSheet;
        private Texture2D _blockedDoorSheet;
        private Texture2D _lyreSheet;

        // Room background textures (screenshot-based rooms)
        private Texture2D _bgStonehenge;
        private Texture2D _bgWastelands;
        private Texture2D _bgTunnelMouth;

        // ====================================================================
        // ROOM MANAGER
        // ====================================================================

        private RoomManager _roomManager;

        // ====================================================================
        // DEBUG
        // ====================================================================

        private SpriteFont _debugFont;
        private bool _showDebugInfo = false;

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

            _guardSheet = Content.Load<Texture2D>("GuardSheet");
            MakeColorTransparent(_guardSheet, Color.Black);

            _maskSheet = Content.Load<Texture2D>("MaskSheet");
            MakeColorTransparent(_maskSheet, Color.Black);

            _boarSheet = Content.Load<Texture2D>("BoarSheet");
            MakeColorTransparent(_boarSheet, Color.Black);

            _eyeSheet = Content.Load<Texture2D>("EyeSheet");
            MakeColorTransparent(_eyeSheet, Color.Black);

            _wraithSheet = Content.Load<Texture2D>("WraithSheet");
            MakeColorTransparent(_wraithSheet, Color.Black);

            _deathSheet = Content.Load<Texture2D>("EnemyDeathSheet");
            MakeColorTransparent(_deathSheet, Color.Black);

            _swordSheet = Content.Load<Texture2D>("SwordSheet");
            MakeColorTransparent(_swordSheet, Color.Black);

            _ballAndChainSheet = Content.Load<Texture2D>("BallandChainSheet");
            MakeColorTransparent(_ballAndChainSheet, Color.Black);

            _axeSheet = Content.Load<Texture2D>("AxeSheet");
            MakeColorTransparent(_axeSheet, Color.Black);

            _shootingStarSheet = Content.Load<Texture2D>("ShootingStarSheet");
            MakeColorTransparent(_shootingStarSheet, Color.Black);

            // Create 1x1 white pixel texture for projectile rendering
            _pixelTexture = new Texture2D(GraphicsDevice, 1, 1);
            _pixelTexture.SetData(new[] { Color.White });

            _captiveWizardSheet = Content.Load<Texture2D>("CaptiveWizardSheet");
            MakeColorTransparent(_captiveWizardSheet, Color.Black);

            _starSheet = Content.Load<Texture2D>("StarSheet");
            MakeColorTransparent(_starSheet, Color.Black);

            _blockedDoorSheet = Content.Load<Texture2D>("BlockedDoorSheet");
            MakeColorTransparent(_blockedDoorSheet, Color.Black);

            _lyreSheet = Content.Load<Texture2D>("LyreSheet");
            MakeColorTransparent(_lyreSheet, Color.Black);

            // Load room background textures (available for background rooms)
            _bgStonehenge = Content.Load<Texture2D>("RoomBG_Stonehenge");
            _bgWastelands = Content.Load<Texture2D>("RoomBG_Wastelands");
            _bgTunnelMouth = Content.Load<Texture2D>("RoomBG_TunnelMouth");

            // Set up room manager
            _roomManager = new RoomManager();
            _roomManager.SetTextures(_tilesetTexture, _leftDoorTexture, _rightDoorTexture);
            RegisterTestRooms();
            RegisterBackgroundRooms();

            // Start with the two-room tile prototype
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

            // Spawn enemies, items, wizards, and blocked doors for the initial room
            SpawnRoomEnemies("room_1");
            SpawnRoomItems("room_1");
            SpawnRoomWizards("room_1");
            SpawnRoomBlockedDoors("room_1");

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

            // Restart game
            if (Keyboard.GetState().IsKeyDown(Keys.R))
            {
                RestartGame();
                return;
            }

            // ----------------------------------------------------------------
            // Toggle debug info
            // ----------------------------------------------------------------

            if (Keyboard.GetState().IsKeyDown(Keys.F1))
                _showDebugInfo = !_showDebugInfo;

            var currentKeyState = Keyboard.GetState();

            // ----------------------------------------------------------------
            // Debug spawn: keys 2-5 spawn floating enemies at player position
            // ----------------------------------------------------------------
            if (currentKeyState.IsKeyDown(Keys.D2) && !_previousKeyState.IsKeyDown(Keys.D2))
                SpawnEnemy($"spawned_mask_{_spawnCounter++}", "mask", FindRandomEmptyPosition());
            else if (currentKeyState.IsKeyDown(Keys.D3) && !_previousKeyState.IsKeyDown(Keys.D3))
                SpawnEnemy($"spawned_boar_{_spawnCounter++}", "boar", FindRandomEmptyPosition());
            else if (currentKeyState.IsKeyDown(Keys.D4) && !_previousKeyState.IsKeyDown(Keys.D4))
                SpawnEnemy($"spawned_eye_{_spawnCounter++}", "eye", FindRandomEmptyPosition());
            else if (currentKeyState.IsKeyDown(Keys.D5) && !_previousKeyState.IsKeyDown(Keys.D5))
                SpawnEnemy($"spawned_wraith_{_spawnCounter++}", "wraith", FindRandomEmptyPosition());

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            // ----------------------------------------------------------------
            // Door transition logic (freezes all gameplay during animation)
            // ----------------------------------------------------------------

            if (_roomManager.IsGameFrozen)
            {
                var result = _roomManager.Update(dt);
                if (result.HasValue)
                {
                    // Clear projectiles and save current room's enemies
                    _projectiles.Clear();
                    SaveRoomEnemies(_roomManager.CurrentRoomId);

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

                    // Restore or spawn enemies and items for the new room
                    LoadRoomEnemies(_roomManager.CurrentRoomId);
                    SpawnRoomItems(_roomManager.CurrentRoomId);
                    SpawnRoomWizards(_roomManager.CurrentRoomId);
                    SpawnRoomBlockedDoors(_roomManager.CurrentRoomId);
                }
                // Skip all other updates while frozen
            }
            else
            {
                // Normal gameplay

                // --- SPACE ACTIONS (before movement updates for snappy response) ---

                // Item pickup/swap: on press (not hold, to prevent rapid re-swapping)
                bool pickedUpItem = false;
                if (currentKeyState.IsKeyDown(Keys.Space) &&
                    !_previousKeyState.IsKeyDown(Keys.Space))
                {
                    for (int i = _roomItems.Count - 1; i >= 0; i--)
                    {
                        var item = _roomItems[i];
                        if (IsOverlapping(_player, item.Position))
                        {
                            ItemType pickedType = item.Type;
                            Vector2 dropPos = item.Position;

                            // Remove the picked-up item
                            _pickedUpItems.Add(item.Id);
                            _roomItems.RemoveAt(i);

                            // If carrying something, drop it where the picked item was
                            if (_carriedItem != ItemType.None)
                            {
                                string droppedId = $"dropped_{_spawnCounter++}";
                                Texture2D dropTex = GetItemTexture(_carriedItem);
                                Rectangle dropSrc = GetItemSourceRect(_carriedItem);
                                if (dropTex != null)
                                    _roomItems.Add(new ItemInstance(droppedId, _carriedItem, dropPos, dropTex, dropSrc));
                            }

                            _carriedItem = pickedType;
                            pickedUpItem = true;
                            break;
                        }
                    }
                }

                // Shooting Star: on press, fire 8 projectiles and consume
                // Skip if we just picked up an item on this frame
                if (!pickedUpItem &&
                    currentKeyState.IsKeyDown(Keys.Space) &&
                    !_previousKeyState.IsKeyDown(Keys.Space) &&
                    _carriedItem == ItemType.ShootingStar)
                {
                    FireShootingStar();
                    _carriedItem = ItemType.None;
                }

                // Kill enemy: while space is held + touching + correct melee weapon
                // Checks ALL overlapping enemies and kills the first one that matches
                if (currentKeyState.IsKeyDown(Keys.Space) && _carriedItem != ItemType.None)
                {
                    for (int i = _roomEnemies.Count - 1; i >= 0; i--)
                    {
                        var enemy = _roomEnemies[i];
                        if (enemy.IsDying) continue;

                        if (IsOverlapping(_player, enemy.Entity) &&
                            CanKillEnemy(enemy))
                        {
                            StartEnemyDeath(enemy);
                            break;
                        }
                    }
                }

                // --- MOVEMENT UPDATES ---

                _player.Update(gameTime);

                // Update all room enemies
                for (int i = _roomEnemies.Count - 1; i >= 0; i--)
                {
                    var enemy = _roomEnemies[i];

                    if (enemy.IsDying)
                    {
                        // Only tick the sprite animation, NOT the full entity
                        var sprite = enemy.Entity.GetComponent<SpriteComponent>();
                        if (sprite != null)
                        {
                            sprite.Update(gameTime);
                            if (!sprite.IsPlaying)
                            {
                                // Death animation finished — permanently kill
                                _deadEnemies.Add(enemy.Id);
                                _roomEnemies.RemoveAt(i);
                            }
                        }
                        continue;
                    }

                    enemy.Entity.Update(gameTime);
                }

                // Update blocked doors — unlock by touching while carrying correct item (no button needed)
                for (int i = _roomBlockedDoors.Count - 1; i >= 0; i--)
                {
                    var bd = _roomBlockedDoors[i];
                    var doorHitbox = bd.GetHitbox();
                    var playerRect = new Rectangle(
                        (int)_player.Position.X - 1, (int)_player.Position.Y - 1,
                        PhysicsComponent.HITBOX_WIDTH + 2, PhysicsComponent.HITBOX_HEIGHT + 2);

                    if (playerRect.Intersects(doorHitbox) &&
                        _carriedItem == bd.RequiredItem)
                    {
                        // Unlock: remove door, consume item, remove from physics
                        _unlockedDoors.Add(bd.Id);
                        _carriedItem = ItemType.None;
                        _roomBlockedDoors.RemoveAt(i);

                        // Re-wire physics solid rects without this door
                        RebuildSolidRects();
                    }
                }

                // Update captive wizards
                float wizSpeed = SpriteConfig.PROJECTILE_SPEED; // same as player speed
                for (int i = _roomWizards.Count - 1; i >= 0; i--)
                {
                    var wiz = _roomWizards[i];
                    wiz.UpdateAnimation(dt);

                    if (wiz.IsSaving)
                    {
                        // Fly upward
                        wiz.Position.Y -= wizSpeed * dt;

                        // Remove when off screen
                        if (wiz.Position.Y + SpriteConfig.ITEM_DISPLAY_SIZE < 0)
                        {
                            _roomWizards.RemoveAt(i);
                        }
                    }
                    else
                    {
                        // Check player touch
                        var wizRect = new Rectangle(
                            (int)wiz.Position.X, (int)wiz.Position.Y,
                            SpriteConfig.ITEM_DISPLAY_SIZE, SpriteConfig.ITEM_DISPLAY_SIZE);
                        var playerRect = new Rectangle(
                            (int)_player.Position.X - 1, (int)_player.Position.Y - 1,
                            PhysicsComponent.HITBOX_WIDTH + 2, PhysicsComponent.HITBOX_HEIGHT + 2);

                        if (wizRect.Intersects(playerRect))
                        {
                            // Transform to star and start flying up
                            wiz.IsSaving = true;
                            wiz.Texture = _starSheet;
                            wiz.AnimFrames = SpriteConfig.STAR_ANIM;
                            wiz.FrameTime = SpriteConfig.STAR_ANIMATION_SPEED;
                            wiz.CurrentFrame = 0;
                            wiz.FrameTimer = 0;

                            if (!wiz.CountedAsSaved)
                            {
                                _savedWizardCount++;
                                _savedWizards.Add(wiz.Id);
                                wiz.CountedAsSaved = true;
                            }
                        }
                    }
                }

                // Update projectiles
                UpdateProjectiles(dt);

                // Check door triggers
                _roomManager.CheckDoorTriggers(
                    _player.Position,
                    PhysicsComponent.HITBOX_WIDTH,
                    PhysicsComponent.HITBOX_HEIGHT
                );
            }

            _previousKeyState = currentKeyState;

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

            // Draw room background image (if any) - renders the screenshot-based room
            _roomManager.DrawBackground(_spriteBatch, RENDER_SCALE);

            // Draw tilemap (only if no background image, otherwise collision-only)
            if (!_roomManager.HasBackground)
                _roomManager.CurrentTileMap?.Draw(_spriteBatch, RENDER_SCALE);

            // Draw doors
            _roomManager.DrawDoors(_spriteBatch, RENDER_SCALE);

            // Draw blocked doors (48x48 source rendered at 24x24)
            foreach (var bd in _roomBlockedDoors)
            {
                Vector2 renderPos = bd.Position * RENDER_SCALE;
                int destSize = SpriteConfig.ITEM_DISPLAY_SIZE * RENDER_SCALE;
                var destRect = new Rectangle(
                    (int)renderPos.X, (int)renderPos.Y,
                    destSize, destSize);
                _spriteBatch.Draw(
                    bd.Texture,
                    destRect,
                    bd.SourceRect,
                    Color.White);
            }

            // Draw captive wizards (48x48 source rendered at 24x24)
            foreach (var wiz in _roomWizards)
            {
                Vector2 renderPos = wiz.Position * RENDER_SCALE;
                int destSize = SpriteConfig.ITEM_DISPLAY_SIZE * RENDER_SCALE;
                var destRect = new Rectangle(
                    (int)renderPos.X, (int)renderPos.Y,
                    destSize, destSize);
                _spriteBatch.Draw(
                    wiz.Texture,
                    destRect,
                    wiz.CurrentSourceRect,
                    Color.White);
            }

            // Draw room items (48x48 source rendered at 24x24)
            foreach (var item in _roomItems)
            {
                Vector2 renderPos = item.Position * RENDER_SCALE;
                int destSize = SpriteConfig.ITEM_DISPLAY_SIZE * RENDER_SCALE; // 24*3=72
                var destRect = new Rectangle(
                    (int)renderPos.X, (int)renderPos.Y,
                    destSize, destSize);
                _spriteBatch.Draw(
                    item.Texture,
                    destRect,
                    item.SourceRect,
                    Color.White);
            }

            // Draw player sprite (on top of tiles)
            DrawPlayer();

            // Draw all room enemies
            foreach (var enemy in _roomEnemies)
            {
                var sprite = enemy.Entity.GetComponent<SpriteComponent>();
                if (sprite == null || sprite.Texture == null) continue;

                if (enemy.IsDying)
                {
                    // Draw 48x48 death frame scaled down into a 24x24 destination
                    Vector2 renderPos = enemy.Entity.Position * RENDER_SCALE;
                    int destSize = PhysicsComponent.HITBOX_WIDTH * RENDER_SCALE; // 24*3=72
                    var destRect = new Rectangle(
                        (int)renderPos.X, (int)renderPos.Y,
                        destSize, destSize);
                    _spriteBatch.Draw(
                        sprite.Texture,
                        destRect,
                        sprite.SourceRectangle,
                        Color.White);
                }
                else
                {
                    Vector2 renderPos = enemy.Entity.Position * RENDER_SCALE;
                    sprite.Draw(_spriteBatch, renderPos, RENDER_SCALE);
                }
            }

            // Draw projectiles (1 pixel each, scaled to RENDER_SCALE)
            foreach (var proj in _projectiles)
            {
                var destRect = new Rectangle(
                    (int)(proj.Position.X * RENDER_SCALE),
                    (int)(proj.Position.Y * RENDER_SCALE),
                    RENDER_SCALE, RENDER_SCALE);
                _spriteBatch.Draw(_pixelTexture, destRect, proj.Color);
            }

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

                // Floor
                map.DrawHorizontalLine(0, 17, 40, TileConfig.FLOOR_BROWN);

                // Tunnel for captive wizard (right side, cols 28-39 = X 224-320)
                // Top wall: single tile row 8 (Y=64)
                map.DrawHorizontalLine(28, 8, 12, TileConfig.FLOOR_TAN);
                // Bottom wall: single tile row 12 (Y=96)
                // Gap = rows 9,10,11 = 24px (enough for player)
                map.DrawHorizontalLine(28, 12, 12, TileConfig.FLOOR_TAN);

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
        // BACKGROUND ROOM REGISTRATION
        // ====================================================================

        /// <summary>
        /// Register rooms that use screenshot backgrounds with collision-only tilemaps.
        /// Chain: Stonehenge <-> Wastelands <-> TunnelMouth
        /// </summary>
        private void RegisterBackgroundRooms()
        {
            string dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "data");

            // --- STONEHENGE ---
            _roomManager.RegisterRoom("stonehenge", () =>
            {
                // Background image
                _roomManager.SetBackground(_bgStonehenge);

                // Collision-only tilemap from auto-detected grid
                string jsonPath = Path.Combine(dataDir, "collision_stonehenge.json");
                var map = RoomLoader.BuildCollisionTileMap(_tilesetTexture, jsonPath);
                _roomManager.SetTileMap(map);

                // Right door -> Wastelands
                var doorRight = new DoorComponent(DoorType.LeftOpening, new Vector2(296, 112));
                doorRight.DoorId = "stonehenge_door_right";
                doorRight.TargetRoomId = "wastelands";
                doorRight.TargetDoorId = "wastelands_door_left";

                _roomManager.SetDoors(new List<DoorComponent> { doorRight });
            });

            // --- WASTELANDS ---
            _roomManager.RegisterRoom("wastelands", () =>
            {
                _roomManager.SetBackground(_bgWastelands);

                string jsonPath = Path.Combine(dataDir, "collision_wastelands.json");
                var map = RoomLoader.BuildCollisionTileMap(_tilesetTexture, jsonPath);
                _roomManager.SetTileMap(map);

                // Left door -> Stonehenge
                var doorLeft = new DoorComponent(DoorType.RightOpening, new Vector2(0, 112));
                doorLeft.DoorId = "wastelands_door_left";
                doorLeft.TargetRoomId = "stonehenge";
                doorLeft.TargetDoorId = "stonehenge_door_right";

                // Right door -> Tunnel Mouth
                var doorRight = new DoorComponent(DoorType.LeftOpening, new Vector2(296, 112));
                doorRight.DoorId = "wastelands_door_right";
                doorRight.TargetRoomId = "tunnelmouth";
                doorRight.TargetDoorId = "tunnelmouth_door_left";

                _roomManager.SetDoors(new List<DoorComponent> { doorLeft, doorRight });
            });

            // --- TUNNEL MOUTH ---
            _roomManager.RegisterRoom("tunnelmouth", () =>
            {
                _roomManager.SetBackground(_bgTunnelMouth);

                string jsonPath = Path.Combine(dataDir, "collision_tunnelmouth.json");
                var map = RoomLoader.BuildCollisionTileMap(_tilesetTexture, jsonPath);
                _roomManager.SetTileMap(map);

                // Left door -> Wastelands
                var doorLeft = new DoorComponent(DoorType.RightOpening, new Vector2(0, 96));
                doorLeft.DoorId = "tunnelmouth_door_left";
                doorLeft.TargetRoomId = "wastelands";
                doorLeft.TargetDoorId = "wastelands_door_right";

                _roomManager.SetDoors(new List<DoorComponent> { doorLeft });
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

        // ====================================================================
        // ENEMY MANAGEMENT
        // ====================================================================

        /// <summary>
        /// Tracks a placed item in the room.
        /// </summary>
        private class BlockedDoorInstance
        {
            public string Id;
            public Vector2 Position;
            public Texture2D Texture;
            public Rectangle SourceRect;
            public ItemType RequiredItem; // which item unlocks this door

            public BlockedDoorInstance(string id, Vector2 pos, Texture2D tex, Rectangle src, ItemType required)
            {
                Id = id;
                Position = pos;
                Texture = tex;
                SourceRect = src;
                RequiredItem = required;
            }

            /// <summary>
            /// Get the solid hitbox in world coordinates (centered 12px bar within 24x24).
            /// </summary>
            public Rectangle GetHitbox()
            {
                return new Rectangle(
                    (int)Position.X + SpriteConfig.BLOCKED_DOOR_HITBOX_OFFSET_X,
                    (int)Position.Y,
                    SpriteConfig.BLOCKED_DOOR_HITBOX_WIDTH,
                    SpriteConfig.BLOCKED_DOOR_HITBOX_HEIGHT);
            }
        }

        private class CaptiveWizard
        {
            public string Id;
            public Vector2 Position;
            public Texture2D Texture;
            public Rectangle[] AnimFrames;
            public float FrameTime;
            public int CurrentFrame;
            public float FrameTimer;
            public bool IsSaving; // true = transforming to star and flying up
            public bool CountedAsSaved; // true = already incremented saved count

            public CaptiveWizard(string id, Vector2 pos, Texture2D tex, Rectangle[] frames, float frameTime)
            {
                Id = id;
                Position = pos;
                Texture = tex;
                AnimFrames = frames;
                FrameTime = frameTime;
                CurrentFrame = 0;
                FrameTimer = 0;
                IsSaving = false;
                CountedAsSaved = false;
            }

            public Rectangle CurrentSourceRect => AnimFrames[CurrentFrame];

            public void UpdateAnimation(float dt)
            {
                FrameTimer += dt;
                if (FrameTimer >= FrameTime)
                {
                    FrameTimer -= FrameTime;
                    CurrentFrame++;
                    if (CurrentFrame >= AnimFrames.Length)
                        CurrentFrame = 0; // loop for idle, star will be removed before looping
                }
            }
        }

        private class Projectile
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public Color Color;

            public Projectile(Vector2 pos, Vector2 vel, Color color)
            {
                Position = pos;
                Velocity = vel;
                Color = color;
            }
        }

        private class ItemInstance
        {
            public string Id;
            public ItemType Type;
            public Vector2 Position;
            public Texture2D Texture;
            public Rectangle SourceRect;

            public ItemInstance(string id, ItemType type, Vector2 pos, Texture2D tex, Rectangle src)
            {
                Id = id;
                Type = type;
                Position = pos;
                Texture = tex;
                SourceRect = src;
            }
        }

        /// <summary>
        /// Tracks a spawned enemy with its ID and death state.
        /// </summary>
        private class EnemyInstance
        {
            public string Id;
            public Entity Entity;
            public bool IsDying;

            public EnemyInstance(string id, Entity entity)
            {
                Id = id;
                Entity = entity;
                IsDying = false;
            }
        }

        /// <summary>
        /// Save current room's living enemies for later restoration.
        /// </summary>
        private void SaveRoomEnemies(string roomId)
        {
            // Only save living (non-dying) enemies
            var toSave = new List<EnemyInstance>();
            foreach (var enemy in _roomEnemies)
            {
                if (!enemy.IsDying)
                {
                    // Freeze velocity
                    var physics = enemy.Entity.GetComponent<PhysicsComponent>();
                    if (physics != null)
                        physics.Velocity = Vector2.Zero;
                    toSave.Add(enemy);
                }
            }
            _savedRoomEnemies[roomId] = toSave;
            _roomEnemies.Clear();
        }

        /// <summary>
        /// Load enemies for a room: restore saved ones or spawn fresh.
        /// </summary>
        private void LoadRoomEnemies(string roomId)
        {
            if (_savedRoomEnemies.TryGetValue(roomId, out var saved))
            {
                _roomEnemies = saved;
                _savedRoomEnemies.Remove(roomId);

                // Re-wire physics to new tilemap and doors
                foreach (var enemy in _roomEnemies)
                {
                    var physics = enemy.Entity.GetComponent<PhysicsComponent>();
                    if (physics != null && physics.TileMap != null)
                    {
                        physics.TileMap = _roomManager.CurrentTileMap;
                        UpdateDoorCollision(physics);
                    }
                }
            }
            else
            {
                // First visit — spawn fresh
                SpawnRoomEnemies(roomId);
            }
        }

        /// <summary>
        /// Spawn enemies for a given room, skipping any that are permanently dead.
        /// </summary>
        private void SpawnRoomEnemies(string roomId)
        {
            _roomEnemies.Clear();

            switch (roomId)
            {
                case "room_1":
                    // No enemies for now (use keys 2-5 to spawn)
                    break;

                case "room_2":
                    // No enemies for now (use keys 2-5 to spawn)
                    break;
            }
        }

        /// <summary>
        /// Spawn a single enemy if not permanently dead.
        /// </summary>
        private void SpawnEnemy(string id, string type, Vector2 position)
        {
            if (_deadEnemies.Contains(id)) return;

            var entity = new Entity(id);
            entity.Position = position;

            var physics = new PhysicsComponent();

            switch (type)
            {
                case "guard":
                    physics.Speed = SpriteConfig.GUARD_SPEED;
                    physics.TileMap = _roomManager.CurrentTileMap;
                    entity.AddComponent(physics);
                    UpdateDoorCollision(physics);
                    entity.AddComponent(new SpriteComponent(_guardSheet, SpriteConfig.GUARD_IDLE[0]));
                    var guardCtrl = new GuardController(_player);
                    entity.AddComponent(guardCtrl);
                    guardCtrl.Initialize();
                    break;

                case "mask":
                    physics.Speed = SpriteConfig.MASK_SPEED;
                    physics.GravitySpeed = 0f;
                    physics.TileMap = _roomManager.CurrentTileMap;
                    entity.AddComponent(physics);
                    UpdateDoorCollision(physics);
                    entity.AddComponent(new SpriteComponent(_maskSheet, SpriteConfig.MASK_ANIM[0]));
                    var maskCtrl = new MaskController(_player);
                    entity.AddComponent(maskCtrl);
                    maskCtrl.Initialize();
                    break;

                case "eye":
                    physics.Speed = SpriteConfig.EYE_SPEED;
                    physics.GravitySpeed = 0f;
                    physics.TileMap = _roomManager.CurrentTileMap;
                    entity.AddComponent(physics);
                    UpdateDoorCollision(physics);
                    entity.AddComponent(new SpriteComponent(_eyeSheet, SpriteConfig.EYE_ANIM[0]));
                    var eyeCtrl = new EyeController(_player);
                    entity.AddComponent(eyeCtrl);
                    eyeCtrl.Initialize();
                    break;

                case "boar":
                    physics.Speed = SpriteConfig.BOAR_SPEED;
                    physics.GravitySpeed = 0f;
                    physics.TileMap = _roomManager.CurrentTileMap;
                    entity.AddComponent(physics);
                    UpdateDoorCollision(physics);
                    entity.AddComponent(new SpriteComponent(_boarSheet, SpriteConfig.BOAR_ANIM[0]));
                    var boarCtrl = new BoarController(_player);
                    entity.AddComponent(boarCtrl);
                    boarCtrl.Initialize();
                    break;

                case "wraith":
                    physics.Speed = SpriteConfig.WRAITH_SPEED;
                    physics.GravitySpeed = 0f;
                    // No TileMap - wraith passes through everything
                    entity.AddComponent(physics);
                    entity.AddComponent(new SpriteComponent(_wraithSheet, SpriteConfig.WRAITH_IDLE[0]));
                    var wraithCtrl = new WraithController(_player);
                    entity.AddComponent(wraithCtrl);
                    wraithCtrl.Initialize();
                    break;
            }

            _roomEnemies.Add(new EnemyInstance(id, entity));
        }

        /// <summary>
        /// Check if player hitbox overlaps with an enemy entity.
        /// </summary>
        private bool IsOverlapping(Entity a, Entity b)
        {
            // Expand by 1px so even edge-touching (pixel-adjacent) triggers
            var rectA = new Rectangle(
                (int)a.Position.X - 1, (int)a.Position.Y - 1,
                PhysicsComponent.HITBOX_WIDTH + 2, PhysicsComponent.HITBOX_HEIGHT + 2);
            var rectB = new Rectangle(
                (int)b.Position.X, (int)b.Position.Y,
                PhysicsComponent.HITBOX_WIDTH, PhysicsComponent.HITBOX_HEIGHT);
            return rectA.Intersects(rectB);
        }

        /// <summary>
        /// Begin the death animation for an enemy — stop movement, swap to death sprite.
        /// </summary>
        /// <summary>
        /// Fire 8 projectiles from the player's sprite edges in 8 directions.
        /// </summary>
        private void FireShootingStar()
        {
            float cx = _player.Position.X + PhysicsComponent.HITBOX_WIDTH / 2f;
            float cy = _player.Position.Y + PhysicsComponent.HITBOX_HEIGHT / 2f;
            float left = _player.Position.X;
            float right = _player.Position.X + PhysicsComponent.HITBOX_WIDTH;
            float top = _player.Position.Y;
            float bottom = _player.Position.Y + PhysicsComponent.HITBOX_HEIGHT;

            float speed = SpriteConfig.PROJECTILE_SPEED;
            float diag = speed * 0.7071f; // 1/sqrt(2)

            // Projectile colors (bright, visible)
            Color c = Color.Yellow;

            // 4 cardinal directions (from edge centers)
            _projectiles.Add(new Projectile(new Vector2(cx, top), new Vector2(0, -speed), c));       // Up
            _projectiles.Add(new Projectile(new Vector2(cx, bottom), new Vector2(0, speed), c));     // Down
            _projectiles.Add(new Projectile(new Vector2(left, cy), new Vector2(-speed, 0), c));      // Left
            _projectiles.Add(new Projectile(new Vector2(right, cy), new Vector2(speed, 0), c));      // Right

            // 4 diagonal directions (from corners)
            _projectiles.Add(new Projectile(new Vector2(left, top), new Vector2(-diag, -diag), c));      // Top-left
            _projectiles.Add(new Projectile(new Vector2(right, top), new Vector2(diag, -diag), c));      // Top-right
            _projectiles.Add(new Projectile(new Vector2(left, bottom), new Vector2(-diag, diag), c));    // Bottom-left
            _projectiles.Add(new Projectile(new Vector2(right, bottom), new Vector2(diag, diag), c));    // Bottom-right
        }

        /// <summary>
        /// Update projectile positions, check enemy hits, remove off-screen projectiles.
        /// </summary>
        private void UpdateProjectiles(float dt)
        {
            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                var proj = _projectiles[i];

                // Move
                proj.Position += proj.Velocity * dt;

                // Check off-screen
                if (proj.Position.X < 0 || proj.Position.X > BASE_GAME_WIDTH ||
                    proj.Position.Y < 0 || proj.Position.Y > BASE_GAME_HEIGHT)
                {
                    _projectiles.RemoveAt(i);
                    continue;
                }

                // Check enemy collision (1px projectile vs enemy hitbox)
                // Projectiles pass through enemies — kill on touch and continue
                for (int j = _roomEnemies.Count - 1; j >= 0; j--)
                {
                    var enemy = _roomEnemies[j];
                    if (enemy.IsDying) continue;

                    var enemyRect = new Rectangle(
                        (int)enemy.Entity.Position.X, (int)enemy.Entity.Position.Y,
                        PhysicsComponent.HITBOX_WIDTH, PhysicsComponent.HITBOX_HEIGHT);

                    if (enemyRect.Contains((int)proj.Position.X, (int)proj.Position.Y))
                    {
                        StartEnemyDeath(enemy);
                    }
                }
            }
        }

        private void StartEnemyDeath(EnemyInstance enemy)
        {
            enemy.IsDying = true;

            // Consume the weapon used to kill
            _carriedItem = ItemType.None;

            // Stop all movement
            var physics = enemy.Entity.GetComponent<PhysicsComponent>();
            if (physics != null)
                physics.Velocity = Vector2.Zero;

            // Swap sprite to death animation
            var sprite = enemy.Entity.GetComponent<SpriteComponent>();
            if (sprite != null)
            {
                sprite.Texture = _deathSheet;
                sprite.SetAnimation(
                    SpriteConfig.ENEMY_DEATH_ANIM,
                    SpriteConfig.DEATH_ANIMATION_SPEED,
                    loop: false
                );
            }
        }

        /// <summary>
        /// Spawn items for a given room, skipping any that are permanently picked up.
        /// </summary>
        /// <summary>
        /// Spawn captive wizards for a given room, skipping already saved ones.
        /// </summary>
        /// <summary>
        /// Spawn blocked doors for a room, skipping already unlocked ones.
        /// Also wires their hitboxes into physics.
        /// </summary>
        private void SpawnRoomBlockedDoors(string roomId)
        {
            _roomBlockedDoors.Clear();

            switch (roomId)
            {
                case "room_2":
                    // Iron door at left edge of tunnel (X=224, between rows 8 and 12)
                    // Door sits between top platform (row 8, Y=64) and bottom platform (row 12, Y=96)
                    // Door Y = top platform bottom edge = row 9 * 8 = 72
                    if (!_unlockedDoors.Contains("room_2_iron_door"))
                    {
                        _roomBlockedDoors.Add(new BlockedDoorInstance(
                            "room_2_iron_door",
                            new Vector2(216f, 72f),
                            _blockedDoorSheet,
                            SpriteConfig.BLOCKED_DOOR_FRAME,
                            ItemType.Lyre));
                    }
                    break;
            }

            // Wire blocked door hitboxes into player physics
            RebuildSolidRects();
        }

        /// <summary>
        /// Rebuild physics solid rects from room doors + blocked doors.
        /// </summary>
        private void RebuildSolidRects()
        {
            var physics = _player.GetComponent<PhysicsComponent>();
            if (physics == null) return;

            // Start with room transition doors
            physics.SolidRects.Clear();
            foreach (var door in _roomManager.CurrentDoors)
            {
                physics.SolidRects.Add(new Rectangle(
                    (int)door.Position.X, (int)door.Position.Y,
                    DoorConfig.DOOR_WIDTH, DoorConfig.DOOR_HEIGHT));
            }

            // Add blocked door hitboxes
            foreach (var bd in _roomBlockedDoors)
            {
                physics.SolidRects.Add(bd.GetHitbox());
            }
        }

        private void SpawnRoomWizards(string roomId)
        {
            _roomWizards.Clear();

            switch (roomId)
            {
                case "room_1":
                    // Wizard on top of platform
                    if (!_savedWizards.Contains("room_1_wizard"))
                    {
                        _roomWizards.Add(new CaptiveWizard(
                            "room_1_wizard",
                            new Vector2(160f, 72f),
                            _captiveWizardSheet,
                            SpriteConfig.CAPTIVE_WIZARD_ANIM,
                            SpriteConfig.CAPTIVE_WIZARD_ANIMATION_SPEED));
                    }
                    break;

                case "room_2":
                    // Wizard at right end of tunnel (inside, X=296, Y = row 9*8 = 72)
                    if (!_savedWizards.Contains("room_2_wizard"))
                    {
                        _roomWizards.Add(new CaptiveWizard(
                            "room_2_wizard",
                            new Vector2(296f, 72f),
                            _captiveWizardSheet,
                            SpriteConfig.CAPTIVE_WIZARD_ANIM,
                            SpriteConfig.CAPTIVE_WIZARD_ANIMATION_SPEED));
                    }
                    break;
            }
        }

        private void SpawnRoomItems(string roomId)
        {
            _roomItems.Clear();

            switch (roomId)
            {
                case "room_1":
                    // Sword on top of the platform (row 12, Y = 12*8 - 24 = 72)
                    SpawnItem("room_1_sword", ItemType.Sword, new Vector2(140f, 72f));
                    // Ball and chain on the floor
                    SpawnItem("room_1_ballchain", ItemType.BallAndChain, new Vector2(60f, 112f));
                    // Shooting star on the floor
                    SpawnItem("room_1_star", ItemType.ShootingStar, new Vector2(120f, 112f));
                    // Lyre on the floor
                    SpawnItem("room_1_lyre", ItemType.Lyre, new Vector2(240f, 112f));
                    break;

                case "room_2":
                    // Axe on the floor
                    SpawnItem("room_2_axe", ItemType.Axe, new Vector2(160f, 112f));
                    break;
            }
        }

        /// <summary>
        /// Spawn a single item if not already picked up.
        /// </summary>
        private void SpawnItem(string id, ItemType type, Vector2 position)
        {
            if (_pickedUpItems.Contains(id)) return;

            Texture2D tex = GetItemTexture(type);
            Rectangle src = GetItemSourceRect(type);
            if (tex != null)
                _roomItems.Add(new ItemInstance(id, type, position, tex, src));
        }

        /// <summary>
        /// Check if an enemy can be killed with the currently carried item.
        /// Every enemy type requires a specific weapon — no weapon = no kill.
        /// </summary>
        private bool CanKillEnemy(EnemyInstance enemy)
        {
            // Guard requires sword
            if (enemy.Id.Contains("guard"))
                return _carriedItem == ItemType.Sword;

            // Eye, mask, boar require ball and chain
            if (enemy.Id.Contains("eye") || enemy.Id.Contains("mask") || enemy.Id.Contains("boar"))
                return _carriedItem == ItemType.BallAndChain;

            // Wraith requires axe
            if (enemy.Id.Contains("wraith"))
                return _carriedItem == ItemType.Axe;

            return false;
        }

        /// <summary>
        /// Get the texture for an item type.
        /// </summary>
        private Texture2D GetItemTexture(ItemType type)
        {
            switch (type)
            {
                case ItemType.Sword: return _swordSheet;
                case ItemType.BallAndChain: return _ballAndChainSheet;
                case ItemType.Axe: return _axeSheet;
                case ItemType.ShootingStar: return _shootingStarSheet;
                case ItemType.Lyre: return _lyreSheet;
                default: return null;
            }
        }

        /// <summary>
        /// Get the source rectangle for an item type.
        /// </summary>
        private Rectangle GetItemSourceRect(ItemType type)
        {
            switch (type)
            {
                case ItemType.Sword: return SpriteConfig.SWORD_FRAME;
                case ItemType.BallAndChain: return SpriteConfig.BALL_AND_CHAIN_FRAME;
                case ItemType.Axe: return SpriteConfig.AXE_FRAME;
                case ItemType.ShootingStar: return SpriteConfig.SHOOTING_STAR_FRAME;
                case ItemType.Lyre: return SpriteConfig.LYRE_FRAME;
                default: return Rectangle.Empty;
            }
        }

        /// <summary>
        /// Check if player overlaps with an item position (24x24 hitbox).
        /// </summary>
        private bool IsOverlapping(Entity player, Vector2 itemPos)
        {
            var rectA = new Rectangle(
                (int)player.Position.X - 1, (int)player.Position.Y - 1,
                PhysicsComponent.HITBOX_WIDTH + 2, PhysicsComponent.HITBOX_HEIGHT + 2);
            var rectB = new Rectangle(
                (int)itemPos.X, (int)itemPos.Y,
                SpriteConfig.ITEM_DISPLAY_SIZE, SpriteConfig.ITEM_DISPLAY_SIZE);
            return rectA.Intersects(rectB);
        }

        /// <summary>
        /// Find a random position in the current room that doesn't overlap solid tiles or doors.
        /// </summary>
        private Vector2 FindRandomEmptyPosition()
        {
            var tileMap = _roomManager.CurrentTileMap;
            int hitW = PhysicsComponent.HITBOX_WIDTH;
            int hitH = PhysicsComponent.HITBOX_HEIGHT;

            for (int attempt = 0; attempt < 100; attempt++)
            {
                int x = _rng.Next(0, BASE_GAME_WIDTH - hitW);
                int y = _rng.Next(0, BASE_GAME_HEIGHT - hitH);

                // Check all four corners against solid tiles
                bool blocked = false;
                if (tileMap != null)
                {
                    int[] checkXs = { x, x + hitW - 1 };
                    int[] checkYs = { y, y + hitH - 1 };
                    foreach (int cx in checkXs)
                    {
                        foreach (int cy in checkYs)
                        {
                            int tx = cx / TileConfig.TILE_SIZE;
                            int ty = cy / TileConfig.TILE_SIZE;
                            if (tx >= 0 && tx < tileMap.Width && ty >= 0 && ty < tileMap.Height)
                            {
                                int tileId = tileMap.GetTile(tx, ty);
                                if (TileConfig.IsSolid(tileId) || TileConfig.IsPlatform(tileId))
                                {
                                    blocked = true;
                                    break;
                                }
                            }
                        }
                        if (blocked) break;
                    }
                }

                // Check against doors
                if (!blocked)
                {
                    var entityRect = new Rectangle(x, y, hitW, hitH);
                    foreach (var door in _roomManager.CurrentDoors)
                    {
                        var doorRect = new Rectangle(
                            (int)door.Position.X, (int)door.Position.Y,
                            DoorConfig.DOOR_WIDTH, DoorConfig.DOOR_HEIGHT);
                        if (entityRect.Intersects(doorRect))
                        {
                            blocked = true;
                            break;
                        }
                    }
                }

                if (!blocked)
                    return new Vector2(x, y);
            }

            // Fallback: center of room
            return new Vector2(BASE_GAME_WIDTH / 2f, BASE_GAME_HEIGHT / 2f);
        }

        /// <summary>
        /// Reset all game state to initial conditions.
        /// </summary>
        private void RestartGame()
        {
            // Clear all persistent state
            _deadEnemies.Clear();
            _pickedUpItems.Clear();
            _roomEnemies.Clear();
            _roomItems.Clear();
            _savedRoomEnemies.Clear();
            _projectiles.Clear();
            _roomWizards.Clear();
            _savedWizards.Clear();
            _savedWizardCount = 0;
            _roomBlockedDoors.Clear();
            _unlockedDoors.Clear();
            _carriedItem = ItemType.None;
            _spawnCounter = 0;

            // Reload initial room
            _roomManager.LoadRoom("room_1");

            // Reset player
            _player.Position = new Vector2(40f, 96f);
            var physics = _player.GetComponent<PhysicsComponent>();
            if (physics != null)
            {
                physics.TileMap = _roomManager.CurrentTileMap;
                physics.Velocity = Vector2.Zero;
                UpdateDoorCollision(physics);
            }

            // Spawn enemies, items, wizards, and blocked doors
            SpawnRoomEnemies("room_1");
            SpawnRoomItems("room_1");
            SpawnRoomWizards("room_1");
            SpawnRoomBlockedDoors("room_1");
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

            // Show carried item
            if (_debugFont != null)
            {
                string itemName = _carriedItem == ItemType.None ? "Nothing" : _carriedItem.ToString();
                _spriteBatch.DrawString(_debugFont, $"Carrying: {itemName}",
                    new Vector2(10, GAME_AREA_HEIGHT + 10), Color.Yellow);
                _spriteBatch.DrawString(_debugFont, $"Saved Wizards: {_savedWizardCount}",
                    new Vector2(10, GAME_AREA_HEIGHT + 30), Color.Yellow);
            }

            // Draw carried item icon
            if (_carriedItem != ItemType.None)
            {
                Texture2D itemTex = GetItemTexture(_carriedItem);
                Rectangle itemSrc = GetItemSourceRect(_carriedItem);
                if (itemTex != null)
                {
                    int iconSize = 48; // display size in info panel
                    var iconRect = new Rectangle(
                        WINDOW_WIDTH - iconSize - 20,
                        GAME_AREA_HEIGHT + (INFO_PANEL_HEIGHT - iconSize) / 2,
                        iconSize, iconSize);
                    _spriteBatch.Draw(itemTex, iconRect, itemSrc, Color.White);
                }
            }

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
