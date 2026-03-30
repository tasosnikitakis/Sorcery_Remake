// ============================================================================
// MAIN GAME CLASS
// Sorcery+ Remake - MonoGame Entry Point
// ============================================================================
// Phase 4A refactored version. Game1 is now a thin orchestrator:
// - Initialization and asset loading
// - Update loop dispatching to subsystems
// - Draw loop dispatching to renderers
//
// Extracted systems:
// - WorldState: persistent game state (Core/WorldState.cs)
// - ItemSystem: item types, textures, combat rules (Core/ItemSystem.cs)
// - GameEntities: EnemyInstance, ItemInstance, etc. (Core/GameEntities.cs)
// - RoomRegistry: per-room content data (Rooms/RoomRegistry.cs)
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
        // RENDERING CONSTANTS
        // ====================================================================

        private const int BASE_GAME_WIDTH = 320;
        private const int BASE_GAME_HEIGHT = 144;
        private const int BASE_INFO_PANEL_HEIGHT = 56;
        private const int RENDER_SCALE = 3;
        private const int WINDOW_WIDTH = BASE_GAME_WIDTH * RENDER_SCALE;
        private const int GAME_AREA_HEIGHT = BASE_GAME_HEIGHT * RENDER_SCALE;
        private const int INFO_PANEL_HEIGHT = BASE_INFO_PANEL_HEIGHT * RENDER_SCALE;
        private const int WINDOW_HEIGHT = GAME_AREA_HEIGHT + INFO_PANEL_HEIGHT;

        // ====================================================================
        // GRAPHICS
        // ====================================================================

        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;
        private RenderTarget2D _renderTarget;

        // ====================================================================
        // SUBSYSTEMS
        // ====================================================================

        private WorldState _worldState = new WorldState();
        private ItemSystem _itemSystem = new ItemSystem();
        private RoomManager _roomManager;

        // ====================================================================
        // PLAYER
        // ====================================================================

        private Entity _player;

        // ====================================================================
        // RUNTIME ROOM STATE (current room only)
        // ====================================================================

        private List<EnemyInstance> _roomEnemies = new List<EnemyInstance>();
        private List<ItemInstance> _roomItems = new List<ItemInstance>();
        private List<CaptiveWizard> _roomWizards = new List<CaptiveWizard>();
        private List<BlockedDoorInstance> _roomBlockedDoors = new List<BlockedDoorInstance>();
        private List<Projectile> _projectiles = new List<Projectile>();

        // ====================================================================
        // INPUT
        // ====================================================================

        private KeyboardState _previousKeyState;
        private Random _rng = new Random();

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
        private Texture2D _captiveWizardSheet;
        private Texture2D _starSheet;
        private Texture2D _blockedDoorSheet;
        private Texture2D _pixelTexture;

        // Room backgrounds
        private Texture2D _bgStonehenge;
        private Texture2D _bgWastelands;
        private Texture2D _bgTunnelMouth;

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
            _graphics.PreferredBackBufferWidth = WINDOW_WIDTH;
            _graphics.PreferredBackBufferHeight = WINDOW_HEIGHT;
        }

        // ====================================================================
        // INITIALIZATION
        // ====================================================================

        protected override void Initialize()
        {
            _renderTarget = new RenderTarget2D(
                GraphicsDevice,
                BASE_GAME_WIDTH * RENDER_SCALE,
                BASE_GAME_HEIGHT * RENDER_SCALE
            );

            _player = new Entity("Player");
            _player.Position = new Vector2(148f, 16f);

            var physics = new PhysicsComponent();
            _player.AddComponent(physics);

            var controller = new PlayerController();
            _player.AddComponent(controller);

            base.Initialize();

            controller.Initialize();
        }

        // ====================================================================
        // CONTENT LOADING
        // ====================================================================

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);

            // --- Player spritesheet ---
            try
            {
                _spriteSheet = Content.Load<Texture2D>("Characters");
            }
            catch (Exception)
            {
                try
                {
                    using (var stream = System.IO.File.OpenRead(
                        @"assets\images\Amstrad CPC - Sorcery - Characters.png"))
                    {
                        _spriteSheet = Texture2D.FromStream(GraphicsDevice, stream);
                    }
                }
                catch (Exception)
                {
                    _spriteSheet = new Texture2D(GraphicsDevice, 16, 16);
                    Color[] data = new Color[16 * 16];
                    for (int i = 0; i < data.Length; i++)
                        data[i] = Color.Magenta;
                    _spriteSheet.SetData(data);
                }
            }

            MakeColorTransparent(_spriteSheet, Color.Black);
            _player.AddComponent(new SpriteComponent(_spriteSheet, SpriteConfig.PLAYER_IDLE_FRONT[0]));

            // --- Tileset & doors ---
            _tilesetTexture = Content.Load<Texture2D>("Tiles");
            _leftDoorTexture = Content.Load<Texture2D>("LeftDoorFrames");
            _rightDoorTexture = Content.Load<Texture2D>("RightDoorFrames");

            // --- Enemy sheets ---
            _guardSheet = LoadAndTransparent("GuardSheet");
            _maskSheet = LoadAndTransparent("MaskSheet");
            _boarSheet = LoadAndTransparent("BoarSheet");
            _eyeSheet = LoadAndTransparent("EyeSheet");
            _wraithSheet = LoadAndTransparent("WraithSheet");
            _deathSheet = LoadAndTransparent("EnemyDeathSheet");

            // --- Item sheets (register with ItemSystem) ---
            var swordSheet = LoadAndTransparent("SwordSheet");
            _itemSystem.Register(ItemType.Sword, swordSheet, SpriteConfig.SWORD_FRAME);

            var ballAndChainSheet = LoadAndTransparent("BallandChainSheet");
            _itemSystem.Register(ItemType.BallAndChain, ballAndChainSheet, SpriteConfig.BALL_AND_CHAIN_FRAME);

            var axeSheet = LoadAndTransparent("AxeSheet");
            _itemSystem.Register(ItemType.Axe, axeSheet, SpriteConfig.AXE_FRAME);

            var shootingStarSheet = LoadAndTransparent("ShootingStarSheet");
            _itemSystem.Register(ItemType.ShootingStar, shootingStarSheet, SpriteConfig.SHOOTING_STAR_FRAME);

            var lyreSheet = LoadAndTransparent("LyreSheet");
            _itemSystem.Register(ItemType.Lyre, lyreSheet, SpriteConfig.LYRE_FRAME);

            // --- Wizard & blocked door sheets ---
            _captiveWizardSheet = LoadAndTransparent("CaptiveWizardSheet");
            _starSheet = LoadAndTransparent("StarSheet");
            _blockedDoorSheet = LoadAndTransparent("BlockedDoorSheet");

            // --- 1x1 pixel texture for projectiles & rectangles ---
            _pixelTexture = new Texture2D(GraphicsDevice, 1, 1);
            _pixelTexture.SetData(new[] { Color.White });

            // --- Room backgrounds ---
            _bgStonehenge = Content.Load<Texture2D>("RoomBG_Stonehenge");
            _bgWastelands = Content.Load<Texture2D>("RoomBG_Wastelands");
            _bgTunnelMouth = Content.Load<Texture2D>("RoomBG_TunnelMouth");

            // --- Room system ---
            _roomManager = new RoomManager();
            _roomManager.SetTextures(_tilesetTexture, _leftDoorTexture, _rightDoorTexture);
            RegisterTestRooms();
            RegisterBackgroundRooms();

            // --- Room content registry ---
            RoomRegistry.Initialize();

            // --- Start game ---
            _roomManager.LoadRoom("room_1");
            var phys = _player.GetComponent<PhysicsComponent>();
            if (phys != null)
            {
                phys.TileMap = _roomManager.CurrentTileMap;
                UpdateDoorCollision(phys);
            }
            _player.Position = new Vector2(40f, 96f);
            SpawnRoomContent("room_1");

            // --- Debug font ---
            try { _debugFont = Content.Load<SpriteFont>("DebugFont"); }
            catch { _debugFont = null; }
        }

        /// <summary>
        /// Load a texture and make black transparent. Shorthand for repeated pattern.
        /// </summary>
        private Texture2D LoadAndTransparent(string assetName)
        {
            var tex = Content.Load<Texture2D>(assetName);
            MakeColorTransparent(tex, Color.Black);
            return tex;
        }

        // ====================================================================
        // UPDATE LOOP
        // ====================================================================

        protected override void Update(GameTime gameTime)
        {
            if (Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();

            if (Keyboard.GetState().IsKeyDown(Keys.R))
            {
                RestartGame();
                return;
            }

            if (Keyboard.GetState().IsKeyDown(Keys.F1))
                _showDebugInfo = !_showDebugInfo;

            var currentKeyState = Keyboard.GetState();

            // Debug spawn: keys 2-5
            if (currentKeyState.IsKeyDown(Keys.D2) && !_previousKeyState.IsKeyDown(Keys.D2))
                SpawnEnemy($"spawned_mask_{_worldState.SpawnCounter++}", EnemyType.Mask, FindRandomEmptyPosition());
            else if (currentKeyState.IsKeyDown(Keys.D3) && !_previousKeyState.IsKeyDown(Keys.D3))
                SpawnEnemy($"spawned_boar_{_worldState.SpawnCounter++}", EnemyType.Boar, FindRandomEmptyPosition());
            else if (currentKeyState.IsKeyDown(Keys.D4) && !_previousKeyState.IsKeyDown(Keys.D4))
                SpawnEnemy($"spawned_eye_{_worldState.SpawnCounter++}", EnemyType.Eye, FindRandomEmptyPosition());
            else if (currentKeyState.IsKeyDown(Keys.D5) && !_previousKeyState.IsKeyDown(Keys.D5))
                SpawnEnemy($"spawned_wraith_{_worldState.SpawnCounter++}", EnemyType.Wraith, FindRandomEmptyPosition());

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;

            if (_roomManager.IsGameFrozen)
            {
                UpdateDoorTransition(dt, gameTime);
            }
            else
            {
                UpdateGameplay(dt, gameTime, currentKeyState);
            }

            _previousKeyState = currentKeyState;
            base.Update(gameTime);
        }

        private void UpdateDoorTransition(float dt, GameTime gameTime)
        {
            var result = _roomManager.Update(dt);
            if (result.HasValue)
            {
                _projectiles.Clear();
                SaveRoomEnemies(_roomManager.CurrentRoomId);

                Vector2 newPos = _roomManager.ExecuteTransition(PhysicsComponent.HITBOX_WIDTH);
                _player.Position = newPos;

                var phys = _player.GetComponent<PhysicsComponent>();
                if (phys != null)
                {
                    phys.TileMap = _roomManager.CurrentTileMap;
                    phys.Velocity = Vector2.Zero;
                    UpdateDoorCollision(phys);
                }

                LoadRoomEnemies(_roomManager.CurrentRoomId);
                SpawnRoomContent(_roomManager.CurrentRoomId);
            }
        }

        private void UpdateGameplay(float dt, GameTime gameTime, KeyboardState currentKeyState)
        {
            // --- Item pickup / weapon use ---
            bool pickedUpItem = false;
            if (currentKeyState.IsKeyDown(Keys.Space) && !_previousKeyState.IsKeyDown(Keys.Space))
            {
                pickedUpItem = TryPickupItem();
            }

            // Shooting Star: fire and consume
            if (!pickedUpItem &&
                currentKeyState.IsKeyDown(Keys.Space) &&
                !_previousKeyState.IsKeyDown(Keys.Space) &&
                _worldState.CarriedItem == ItemType.ShootingStar)
            {
                FireShootingStar();
                _worldState.CarriedItem = ItemType.None;
            }

            // Melee kill: space held + touching + correct weapon
            if (currentKeyState.IsKeyDown(Keys.Space) && _worldState.CarriedItem != ItemType.None)
            {
                for (int i = _roomEnemies.Count - 1; i >= 0; i--)
                {
                    var enemy = _roomEnemies[i];
                    if (enemy.IsDying) continue;
                    if (IsOverlapping(_player, enemy.Entity) &&
                        ItemSystem.CanKillEnemy(enemy.Type, _worldState.CarriedItem))
                    {
                        StartEnemyDeath(enemy);
                        break;
                    }
                }
            }

            // --- Entity updates ---
            _player.Update(gameTime);
            UpdateEnemies(gameTime);
            UpdateBlockedDoors();
            UpdateWizards(dt);
            UpdateProjectiles(dt);

            _roomManager.CheckDoorTriggers(
                _player.Position,
                PhysicsComponent.HITBOX_WIDTH,
                PhysicsComponent.HITBOX_HEIGHT
            );
        }

        /// <summary>
        /// Try to pick up an item near the player. Returns true if picked up.
        /// </summary>
        private bool TryPickupItem()
        {
            for (int i = _roomItems.Count - 1; i >= 0; i--)
            {
                var item = _roomItems[i];
                if (IsOverlapping(_player, item.Position))
                {
                    ItemType pickedType = item.Type;
                    Vector2 dropPos = item.Position;

                    _worldState.PickedUpItems.Add(item.Id);
                    _roomItems.RemoveAt(i);

                    // Drop current item where picked item was
                    if (_worldState.CarriedItem != ItemType.None)
                    {
                        string droppedId = $"dropped_{_worldState.SpawnCounter++}";
                        Texture2D dropTex = _itemSystem.GetTexture(_worldState.CarriedItem);
                        Rectangle dropSrc = _itemSystem.GetSourceRect(_worldState.CarriedItem);
                        if (dropTex != null)
                            _roomItems.Add(new ItemInstance(droppedId, _worldState.CarriedItem, dropPos, dropTex, dropSrc));
                    }

                    _worldState.CarriedItem = pickedType;
                    return true;
                }
            }
            return false;
        }

        private void UpdateEnemies(GameTime gameTime)
        {
            for (int i = _roomEnemies.Count - 1; i >= 0; i--)
            {
                var enemy = _roomEnemies[i];

                if (enemy.IsDying)
                {
                    var sprite = enemy.Entity.GetComponent<SpriteComponent>();
                    if (sprite != null)
                    {
                        sprite.Update(gameTime);
                        if (!sprite.IsPlaying)
                        {
                            _worldState.DeadEnemies.Add(enemy.Id);
                            _roomEnemies.RemoveAt(i);
                        }
                    }
                    continue;
                }

                enemy.Entity.Update(gameTime);
            }
        }

        private void UpdateBlockedDoors()
        {
            for (int i = _roomBlockedDoors.Count - 1; i >= 0; i--)
            {
                var bd = _roomBlockedDoors[i];
                var doorHitbox = bd.GetHitbox();
                var playerRect = new Rectangle(
                    (int)_player.Position.X - 1, (int)_player.Position.Y - 1,
                    PhysicsComponent.HITBOX_WIDTH + 2, PhysicsComponent.HITBOX_HEIGHT + 2);

                if (playerRect.Intersects(doorHitbox) &&
                    _worldState.CarriedItem == bd.RequiredItem)
                {
                    _worldState.UnlockedDoors.Add(bd.Id);
                    _worldState.CarriedItem = ItemType.None;
                    _roomBlockedDoors.RemoveAt(i);
                    RebuildSolidRects();
                }
            }
        }

        private void UpdateWizards(float dt)
        {
            float wizSpeed = SpriteConfig.PROJECTILE_SPEED;
            for (int i = _roomWizards.Count - 1; i >= 0; i--)
            {
                var wiz = _roomWizards[i];
                wiz.UpdateAnimation(dt);

                if (wiz.IsSaving)
                {
                    wiz.Position.Y -= wizSpeed * dt;
                    if (wiz.Position.Y + SpriteConfig.ITEM_DISPLAY_SIZE < 0)
                        _roomWizards.RemoveAt(i);
                }
                else
                {
                    var wizRect = new Rectangle(
                        (int)wiz.Position.X, (int)wiz.Position.Y,
                        SpriteConfig.ITEM_DISPLAY_SIZE, SpriteConfig.ITEM_DISPLAY_SIZE);
                    var playerRect = new Rectangle(
                        (int)_player.Position.X - 1, (int)_player.Position.Y - 1,
                        PhysicsComponent.HITBOX_WIDTH + 2, PhysicsComponent.HITBOX_HEIGHT + 2);

                    if (wizRect.Intersects(playerRect))
                    {
                        wiz.IsSaving = true;
                        wiz.Texture = _starSheet;
                        wiz.AnimFrames = SpriteConfig.STAR_ANIM;
                        wiz.FrameTime = SpriteConfig.STAR_ANIMATION_SPEED;
                        wiz.CurrentFrame = 0;
                        wiz.FrameTimer = 0;

                        if (!wiz.CountedAsSaved)
                        {
                            _worldState.SavedWizardCount++;
                            _worldState.SavedWizards.Add(wiz.Id);
                            wiz.CountedAsSaved = true;
                        }
                    }
                }
            }
        }

        // ====================================================================
        // DRAW LOOP
        // ====================================================================

        protected override void Draw(GameTime gameTime)
        {
            // Step 1: Render game area to render target
            GraphicsDevice.SetRenderTarget(_renderTarget);
            GraphicsDevice.Clear(Color.Black);

            _spriteBatch.Begin(samplerState: SamplerState.PointClamp, sortMode: SpriteSortMode.Deferred);

            _roomManager.DrawBackground(_spriteBatch, RENDER_SCALE);
            if (!_roomManager.HasBackground)
                _roomManager.CurrentTileMap?.Draw(_spriteBatch, RENDER_SCALE);
            _roomManager.DrawDoors(_spriteBatch, RENDER_SCALE);

            // Blocked doors
            foreach (var bd in _roomBlockedDoors)
            {
                Vector2 renderPos = bd.Position * RENDER_SCALE;
                int destSize = SpriteConfig.ITEM_DISPLAY_SIZE * RENDER_SCALE;
                _spriteBatch.Draw(bd.Texture,
                    new Rectangle((int)renderPos.X, (int)renderPos.Y, destSize, destSize),
                    bd.SourceRect, Color.White);
            }

            // Captive wizards
            foreach (var wiz in _roomWizards)
            {
                Vector2 renderPos = wiz.Position * RENDER_SCALE;
                int destSize = SpriteConfig.ITEM_DISPLAY_SIZE * RENDER_SCALE;
                _spriteBatch.Draw(wiz.Texture,
                    new Rectangle((int)renderPos.X, (int)renderPos.Y, destSize, destSize),
                    wiz.CurrentSourceRect, Color.White);
            }

            // Room items
            foreach (var item in _roomItems)
            {
                Vector2 renderPos = item.Position * RENDER_SCALE;
                int destSize = SpriteConfig.ITEM_DISPLAY_SIZE * RENDER_SCALE;
                _spriteBatch.Draw(item.Texture,
                    new Rectangle((int)renderPos.X, (int)renderPos.Y, destSize, destSize),
                    item.SourceRect, Color.White);
            }

            // Player
            DrawPlayer();

            // Enemies
            foreach (var enemy in _roomEnemies)
            {
                var sprite = enemy.Entity.GetComponent<SpriteComponent>();
                if (sprite == null || sprite.Texture == null) continue;

                if (enemy.IsDying)
                {
                    Vector2 renderPos = enemy.Entity.Position * RENDER_SCALE;
                    int destSize = PhysicsComponent.HITBOX_WIDTH * RENDER_SCALE;
                    _spriteBatch.Draw(sprite.Texture,
                        new Rectangle((int)renderPos.X, (int)renderPos.Y, destSize, destSize),
                        sprite.SourceRectangle, Color.White);
                }
                else
                {
                    sprite.Draw(_spriteBatch, enemy.Entity.Position * RENDER_SCALE, RENDER_SCALE);
                }
            }

            // Projectiles
            foreach (var proj in _projectiles)
            {
                _spriteBatch.Draw(_pixelTexture,
                    new Rectangle((int)(proj.Position.X * RENDER_SCALE), (int)(proj.Position.Y * RENDER_SCALE),
                        RENDER_SCALE, RENDER_SCALE),
                    proj.Color);
            }

            _spriteBatch.End();

            // Step 2: Render to screen
            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Clear(Color.Black);

            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            _spriteBatch.Draw(_renderTarget,
                new Rectangle(0, 0, WINDOW_WIDTH, GAME_AREA_HEIGHT), Color.White);
            _spriteBatch.End();

            // Step 3: Info panel
            DrawInfoPanel();

            // Step 4: Debug overlay
            if (_showDebugInfo && _debugFont != null)
                DrawDebugInfo(gameTime);

            base.Draw(gameTime);
        }

        // ====================================================================
        // ROOM LAYOUT REGISTRATION
        // ====================================================================

        private void RegisterTestRooms()
        {
            _roomManager.RegisterRoom("room_1", () =>
            {
                var map = new TileMapComponent(_tilesetTexture, 40, 18);
                map.FillRect(0, 0, 40, 18, TileConfig.EMPTY);
                map.DrawHorizontalLine(0, 17, 40, TileConfig.FLOOR_TAN);
                map.DrawHorizontalLine(14, 12, 8, TileConfig.PLATFORM_LIGHT);
                _roomManager.SetTileMap(map);

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
                map.DrawHorizontalLine(0, 17, 40, TileConfig.FLOOR_BROWN);
                map.DrawHorizontalLine(28, 8, 12, TileConfig.FLOOR_TAN);
                map.DrawHorizontalLine(28, 12, 12, TileConfig.FLOOR_TAN);
                _roomManager.SetTileMap(map);

                var door = new DoorComponent(DoorType.RightOpening, new Vector2(0, 112));
                door.DoorId = "room2_door_left";
                door.TargetRoomId = "room_1";
                door.TargetDoorId = "room1_door_right";
                _roomManager.SetDoors(new List<DoorComponent> { door });
            });
        }

        private void RegisterBackgroundRooms()
        {
            string dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "data");

            _roomManager.RegisterRoom("stonehenge", () =>
            {
                _roomManager.SetBackground(_bgStonehenge);
                string jsonPath = Path.Combine(dataDir, "collision_stonehenge.json");
                _roomManager.SetTileMap(RoomLoader.BuildCollisionTileMap(_tilesetTexture, jsonPath));

                var doorRight = new DoorComponent(DoorType.LeftOpening, new Vector2(296, 112));
                doorRight.DoorId = "stonehenge_door_right";
                doorRight.TargetRoomId = "wastelands";
                doorRight.TargetDoorId = "wastelands_door_left";
                _roomManager.SetDoors(new List<DoorComponent> { doorRight });
            });

            _roomManager.RegisterRoom("wastelands", () =>
            {
                _roomManager.SetBackground(_bgWastelands);
                string jsonPath = Path.Combine(dataDir, "collision_wastelands.json");
                _roomManager.SetTileMap(RoomLoader.BuildCollisionTileMap(_tilesetTexture, jsonPath));

                var doorLeft = new DoorComponent(DoorType.RightOpening, new Vector2(0, 112));
                doorLeft.DoorId = "wastelands_door_left";
                doorLeft.TargetRoomId = "stonehenge";
                doorLeft.TargetDoorId = "stonehenge_door_right";

                var doorRight = new DoorComponent(DoorType.LeftOpening, new Vector2(296, 112));
                doorRight.DoorId = "wastelands_door_right";
                doorRight.TargetRoomId = "tunnelmouth";
                doorRight.TargetDoorId = "tunnelmouth_door_left";

                _roomManager.SetDoors(new List<DoorComponent> { doorLeft, doorRight });
            });

            _roomManager.RegisterRoom("tunnelmouth", () =>
            {
                _roomManager.SetBackground(_bgTunnelMouth);
                string jsonPath = Path.Combine(dataDir, "collision_tunnelmouth.json");
                _roomManager.SetTileMap(RoomLoader.BuildCollisionTileMap(_tilesetTexture, jsonPath));

                var doorLeft = new DoorComponent(DoorType.RightOpening, new Vector2(0, 96));
                doorLeft.DoorId = "tunnelmouth_door_left";
                doorLeft.TargetRoomId = "wastelands";
                doorLeft.TargetDoorId = "wastelands_door_right";
                _roomManager.SetDoors(new List<DoorComponent> { doorLeft });
            });
        }

        // ====================================================================
        // ROOM CONTENT SPAWNING (data-driven via RoomRegistry)
        // ====================================================================

        /// <summary>
        /// Spawn all content for a room from the registry, skipping already-collected/killed/unlocked.
        /// </summary>
        private void SpawnRoomContent(string roomId)
        {
            var content = RoomRegistry.GetContent(roomId);

            // Items
            _roomItems.Clear();
            foreach (var spawn in content.Items)
            {
                if (_worldState.PickedUpItems.Contains(spawn.Id)) continue;
                var tex = _itemSystem.GetTexture(spawn.Type);
                var src = _itemSystem.GetSourceRect(spawn.Type);
                if (tex != null)
                    _roomItems.Add(new ItemInstance(spawn.Id, spawn.Type, spawn.Position, tex, src));
            }

            // Wizards
            _roomWizards.Clear();
            foreach (var spawn in content.Wizards)
            {
                if (_worldState.SavedWizards.Contains(spawn.Id)) continue;
                _roomWizards.Add(new CaptiveWizard(
                    spawn.Id, spawn.Position,
                    _captiveWizardSheet,
                    SpriteConfig.CAPTIVE_WIZARD_ANIM,
                    SpriteConfig.CAPTIVE_WIZARD_ANIMATION_SPEED));
            }

            // Blocked doors
            _roomBlockedDoors.Clear();
            foreach (var spawn in content.BlockedDoors)
            {
                if (_worldState.UnlockedDoors.Contains(spawn.Id)) continue;
                _roomBlockedDoors.Add(new BlockedDoorInstance(
                    spawn.Id, spawn.Position,
                    _blockedDoorSheet, SpriteConfig.BLOCKED_DOOR_FRAME,
                    spawn.RequiredItem));
            }
            RebuildSolidRects();

            // Enemies (only on first visit — restored visits use SaveRoomEnemies)
            if (!_worldState.SavedRoomEnemies.ContainsKey(roomId))
            {
                _roomEnemies.Clear();
                foreach (var spawn in content.Enemies)
                    SpawnEnemy(spawn.Id, spawn.Type, spawn.Position);
            }
        }

        // ====================================================================
        // ENEMY MANAGEMENT
        // ====================================================================

        private void SpawnEnemy(string id, EnemyType type, Vector2 position)
        {
            if (_worldState.DeadEnemies.Contains(id)) return;

            var entity = new Entity(id);
            entity.Position = position;
            var physics = new PhysicsComponent();

            switch (type)
            {
                case EnemyType.Guard:
                    physics.Speed = SpriteConfig.GUARD_SPEED;
                    physics.TileMap = _roomManager.CurrentTileMap;
                    entity.AddComponent(physics);
                    UpdateDoorCollision(physics);
                    entity.AddComponent(new SpriteComponent(_guardSheet, SpriteConfig.GUARD_IDLE[0]));
                    var guardCtrl = new GuardController(_player);
                    entity.AddComponent(guardCtrl);
                    guardCtrl.Initialize();
                    break;

                case EnemyType.Mask:
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

                case EnemyType.Eye:
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

                case EnemyType.Boar:
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

                case EnemyType.Wraith:
                    physics.Speed = SpriteConfig.WRAITH_SPEED;
                    physics.GravitySpeed = 0f;
                    entity.AddComponent(physics);
                    entity.AddComponent(new SpriteComponent(_wraithSheet, SpriteConfig.WRAITH_IDLE[0]));
                    var wraithCtrl = new WraithController(_player);
                    entity.AddComponent(wraithCtrl);
                    wraithCtrl.Initialize();
                    break;
            }

            _roomEnemies.Add(new EnemyInstance(id, type, entity));
        }

        private void SaveRoomEnemies(string roomId)
        {
            var toSave = new List<EnemyInstance>();
            foreach (var enemy in _roomEnemies)
            {
                if (!enemy.IsDying)
                {
                    var physics = enemy.Entity.GetComponent<PhysicsComponent>();
                    if (physics != null)
                        physics.Velocity = Vector2.Zero;
                    toSave.Add(enemy);
                }
            }
            _worldState.SavedRoomEnemies[roomId] = toSave;
            _roomEnemies.Clear();
        }

        private void LoadRoomEnemies(string roomId)
        {
            if (_worldState.SavedRoomEnemies.TryGetValue(roomId, out var saved))
            {
                _roomEnemies = saved;
                _worldState.SavedRoomEnemies.Remove(roomId);

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
                // First visit — enemies spawned by SpawnRoomContent
            }
        }

        private void StartEnemyDeath(EnemyInstance enemy)
        {
            enemy.IsDying = true;
            _worldState.CarriedItem = ItemType.None;

            var physics = enemy.Entity.GetComponent<PhysicsComponent>();
            if (physics != null)
                physics.Velocity = Vector2.Zero;

            var sprite = enemy.Entity.GetComponent<SpriteComponent>();
            if (sprite != null)
            {
                sprite.Texture = _deathSheet;
                sprite.SetAnimation(SpriteConfig.ENEMY_DEATH_ANIM, SpriteConfig.DEATH_ANIMATION_SPEED, loop: false);
            }
        }

        // ====================================================================
        // PROJECTILES
        // ====================================================================

        private void FireShootingStar()
        {
            float cx = _player.Position.X + PhysicsComponent.HITBOX_WIDTH / 2f;
            float cy = _player.Position.Y + PhysicsComponent.HITBOX_HEIGHT / 2f;
            float left = _player.Position.X;
            float right = _player.Position.X + PhysicsComponent.HITBOX_WIDTH;
            float top = _player.Position.Y;
            float bottom = _player.Position.Y + PhysicsComponent.HITBOX_HEIGHT;

            float speed = SpriteConfig.PROJECTILE_SPEED;
            float diag = speed * 0.7071f;
            Color c = Color.Yellow;

            _projectiles.Add(new Projectile(new Vector2(cx, top), new Vector2(0, -speed), c));
            _projectiles.Add(new Projectile(new Vector2(cx, bottom), new Vector2(0, speed), c));
            _projectiles.Add(new Projectile(new Vector2(left, cy), new Vector2(-speed, 0), c));
            _projectiles.Add(new Projectile(new Vector2(right, cy), new Vector2(speed, 0), c));
            _projectiles.Add(new Projectile(new Vector2(left, top), new Vector2(-diag, -diag), c));
            _projectiles.Add(new Projectile(new Vector2(right, top), new Vector2(diag, -diag), c));
            _projectiles.Add(new Projectile(new Vector2(left, bottom), new Vector2(-diag, diag), c));
            _projectiles.Add(new Projectile(new Vector2(right, bottom), new Vector2(diag, diag), c));
        }

        private void UpdateProjectiles(float dt)
        {
            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                var proj = _projectiles[i];
                proj.Position += proj.Velocity * dt;

                if (proj.Position.X < 0 || proj.Position.X > BASE_GAME_WIDTH ||
                    proj.Position.Y < 0 || proj.Position.Y > BASE_GAME_HEIGHT)
                {
                    _projectiles.RemoveAt(i);
                    continue;
                }

                for (int j = _roomEnemies.Count - 1; j >= 0; j--)
                {
                    var enemy = _roomEnemies[j];
                    if (enemy.IsDying) continue;

                    var enemyRect = new Rectangle(
                        (int)enemy.Entity.Position.X, (int)enemy.Entity.Position.Y,
                        PhysicsComponent.HITBOX_WIDTH, PhysicsComponent.HITBOX_HEIGHT);

                    if (enemyRect.Contains((int)proj.Position.X, (int)proj.Position.Y))
                        StartEnemyDeath(enemy);
                }
            }
        }

        // ====================================================================
        // COLLISION & PHYSICS HELPERS
        // ====================================================================

        private void UpdateDoorCollision(PhysicsComponent physics)
        {
            physics.SolidRects.Clear();
            foreach (var door in _roomManager.CurrentDoors)
            {
                physics.SolidRects.Add(new Rectangle(
                    (int)door.Position.X, (int)door.Position.Y,
                    DoorConfig.DOOR_WIDTH, DoorConfig.DOOR_HEIGHT));
            }
        }

        private void RebuildSolidRects()
        {
            var physics = _player.GetComponent<PhysicsComponent>();
            if (physics == null) return;

            physics.SolidRects.Clear();
            foreach (var door in _roomManager.CurrentDoors)
            {
                physics.SolidRects.Add(new Rectangle(
                    (int)door.Position.X, (int)door.Position.Y,
                    DoorConfig.DOOR_WIDTH, DoorConfig.DOOR_HEIGHT));
            }
            foreach (var bd in _roomBlockedDoors)
                physics.SolidRects.Add(bd.GetHitbox());
        }

        private bool IsOverlapping(Entity a, Entity b)
        {
            var rectA = new Rectangle(
                (int)a.Position.X - 1, (int)a.Position.Y - 1,
                PhysicsComponent.HITBOX_WIDTH + 2, PhysicsComponent.HITBOX_HEIGHT + 2);
            var rectB = new Rectangle(
                (int)b.Position.X, (int)b.Position.Y,
                PhysicsComponent.HITBOX_WIDTH, PhysicsComponent.HITBOX_HEIGHT);
            return rectA.Intersects(rectB);
        }

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

        private Vector2 FindRandomEmptyPosition()
        {
            var tileMap = _roomManager.CurrentTileMap;
            int hitW = PhysicsComponent.HITBOX_WIDTH;
            int hitH = PhysicsComponent.HITBOX_HEIGHT;

            for (int attempt = 0; attempt < 100; attempt++)
            {
                int x = _rng.Next(0, BASE_GAME_WIDTH - hitW);
                int y = _rng.Next(0, BASE_GAME_HEIGHT - hitH);

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

            return new Vector2(BASE_GAME_WIDTH / 2f, BASE_GAME_HEIGHT / 2f);
        }

        // ====================================================================
        // GAME STATE
        // ====================================================================

        private void RestartGame()
        {
            _worldState.Reset();
            _roomEnemies.Clear();
            _roomItems.Clear();
            _roomWizards.Clear();
            _roomBlockedDoors.Clear();
            _projectiles.Clear();

            _roomManager.LoadRoom("room_1");
            _player.Position = new Vector2(40f, 96f);

            var physics = _player.GetComponent<PhysicsComponent>();
            if (physics != null)
            {
                physics.TileMap = _roomManager.CurrentTileMap;
                physics.Velocity = Vector2.Zero;
                UpdateDoorCollision(physics);
            }

            SpawnRoomContent("room_1");
        }

        // ====================================================================
        // RENDERING HELPERS
        // ====================================================================

        private void DrawPlayer()
        {
            var sprite = _player.GetComponent<SpriteComponent>();
            if (sprite == null) return;
            sprite.Draw(_spriteBatch, _player.Position * RENDER_SCALE, RENDER_SCALE);
        }

        private void DrawInfoPanel()
        {
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);

            // Info panel background (dark blue)
            _spriteBatch.Draw(_pixelTexture,
                new Rectangle(0, GAME_AREA_HEIGHT, WINDOW_WIDTH, INFO_PANEL_HEIGHT),
                new Color(0, 0, 139));

            if (_debugFont != null)
            {
                string itemName = _worldState.CarriedItem == ItemType.None ? "Nothing" : _worldState.CarriedItem.ToString();
                _spriteBatch.DrawString(_debugFont, $"Carrying: {itemName}",
                    new Vector2(10, GAME_AREA_HEIGHT + 10), Color.Yellow);
                _spriteBatch.DrawString(_debugFont, $"Saved Wizards: {_worldState.SavedWizardCount}",
                    new Vector2(10, GAME_AREA_HEIGHT + 30), Color.Yellow);
            }

            if (_worldState.CarriedItem != ItemType.None)
            {
                Texture2D itemTex = _itemSystem.GetTexture(_worldState.CarriedItem);
                Rectangle itemSrc = _itemSystem.GetSourceRect(_worldState.CarriedItem);
                if (itemTex != null)
                {
                    int iconSize = 48;
                    _spriteBatch.Draw(itemTex,
                        new Rectangle(
                            WINDOW_WIDTH - iconSize - 20,
                            GAME_AREA_HEIGHT + (INFO_PANEL_HEIGHT - iconSize) / 2,
                            iconSize, iconSize),
                        itemSrc, Color.White);
                }
            }

            _spriteBatch.End();
        }

        private void DrawDebugInfo(GameTime gameTime)
        {
            if (_debugFont == null) return;

            _spriteBatch.Begin();
            var physics = _player.GetComponent<PhysicsComponent>();

            string debugText = $"SORCERY+ REMAKE - PHASE 4A\n" +
                               $"FPS: {1.0 / gameTime.ElapsedGameTime.TotalSeconds:F0}\n" +
                               $"Room: {_roomManager.CurrentRoomId}\n" +
                               $"Position: ({_player.Position.X:F1}, {_player.Position.Y:F1})\n" +
                               $"Velocity: ({physics?.Velocity.X:F1}, {physics?.Velocity.Y:F1})\n" +
                               $"Enemies: {_roomEnemies.Count}\n" +
                               $"\nControls:\n" +
                               $"  Arrow Keys - Move\n" +
                               $"  Space - Use item\n" +
                               $"  2-5 - Spawn enemies\n" +
                               $"  R - Restart\n" +
                               $"  F1 - Toggle debug\n" +
                               $"  ESC - Exit";

            _spriteBatch.DrawString(_debugFont, debugText, new Vector2(10, 10), Color.Yellow);
            _spriteBatch.End();
        }

        // ====================================================================
        // UTILITY
        // ====================================================================

        private void MakeColorTransparent(Texture2D texture, Color colorToReplace)
        {
            Color[] data = new Color[texture.Width * texture.Height];
            texture.GetData(data);

            for (int i = 0; i < data.Length; i++)
            {
                if (data[i].R == colorToReplace.R &&
                    data[i].G == colorToReplace.G &&
                    data[i].B == colorToReplace.B)
                {
                    data[i] = Color.Transparent;
                }
            }

            texture.SetData(data);
        }
    }
}
