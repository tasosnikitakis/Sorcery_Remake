using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SorceryForge.UI;
using SorceryRemake.Core;
using SorceryRemake.Doors;
using SorceryRemake.Graphics;
using SorceryRemake.Rooms;
using SorceryRemake.Tiles;
using System;
using System.Collections.Generic;
using System.IO;
using NVector2 = System.Numerics.Vector2;

namespace SorceryForge
{
    /// <summary>
    /// SorceryForge — the level editor. Renders the same room a player
    /// would see (background + collision + placed entities) and lets the
    /// designer drag entities from a palette onto the canvas. Saves to
    /// assets/data/content_&lt;roomId&gt;.json which the main game picks up
    /// next time it loads the room.
    /// </summary>
    // IBackgroundTarget is how an undo command reaches the room's pixels
    // without ever naming a Texture2D — see the header of EditorCommands.cs.
    public class EditorGame : Game, IChromeActions, IBackgroundTarget
    {
        private readonly GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch = null!;
        private SpriteFont? _font;

        // 1x1 white texture, used to draw filled rectangles for UI chrome.
        private Texture2D _pixel = null!;

        // 24x24 generated marker for the player spawn — an outline plus a
        // cross, drawn in SpawnColor. Generated rather than authored because
        // the spawn has no game sprite: it is an editor concept only.
        private Texture2D _spawnMarker = null!;

        // The spawn's colour, deliberately unused by every other overlay:
        // red is "unreachable"/"orphan door", yellow is selection/puzzle/paint,
        // green is "door ok", white-on-black is the erase cursor. Magenta is
        // free, and unmistakable over the CPC-palette backgrounds.
        private static readonly Color SpawnColor = new(255, 80, 255);

        // Texture cache keyed by Content asset name (avoids reloading).
        private readonly Dictionary<string, Texture2D> _textures = new();

        // Editor model and palette descriptors for each placeable kind.
        private readonly EditorState _state = new();

        // ---- CHROME (Dear ImGui) -------------------------------------------
        // Every menu, panel, overlay and status line the editor draws. The
        // canvas, the map board and the crop image stay SpriteBatch — they are
        // pixel-space tools and ImGui has nothing to offer them. See the UI
        // architecture section of .claude/CLAUDE.md for where the line runs.
        private ImGuiRenderer _imgui = null!;
        private readonly ChromeInputRouter _router = new();

        /// <summary>
        /// Debug flag (--imgui-probe on the command line): draws a small window
        /// reporting the routing decision for the current frame. Off by
        /// default; it exists so the input-capture behaviour can be watched on
        /// a real desktop, which is the one thing tools/ChromeCheck cannot do.
        /// </summary>
        public static bool ImGuiProbe;

        // Per-room cached background and collision overlay (cleared on switch).
        private Texture2D? _currentBackground;

        // Background pixel-edit state (Erase mode). _bgPixels mirrors the
        // texture's data; _bgOriginal is the state at load / last save (the
        // right-drag "restore" brush copies from it). Both are null when the
        // room's raw PNG wasn't found — then the background is display-only
        // (XNB fallback) and Erase mode is disabled for the room.
        private Color[]? _bgPixels;
        private Color[]? _bgOriginal;
        private bool _ownsBackground;                      // true when _currentBackground came from FromStream (we must Dispose it); false for ContentManager-owned XNB
        private bool _strokeActive;
        private bool _strokeChanged;                       // any pixel changed this stroke (no-op strokes drop their command)
        private Color[]? _strokeBefore;                    // full image as it was when the stroke opened; diffed at EndStroke, never retained
        private Point _lastStamp;                          // room px, previous stamp centre
        private (int zoom, int panX, int panY) _strokeView; // view at last stamp — a view jump must not Bresenham across it

        // ---- UNDO / REDO ----------------------------------------------------
        // EDITOR_REVIEW item 11. One stack for every kind of edit — placements,
        // inspector fields, the spawn, painted tiles and background pixels —
        // reached by Ctrl+Z and Ctrl+Y through exactly one path. The commands
        // and the rules live beside EditorState, device-free, so
        // tools/EditCheck can drive all of them headlessly; this class supplies
        // the pixels they act on and closes any running gesture first.
        private readonly UndoStack _undo = new();
        private EditorCommandContext _cmd = null!;         // built in the constructor, below

        // Collision cells changed by the paint drag currently under the mouse.
        // Committed as ONE PaintTilesCommand when the buttons come up, for the
        // same reason a placement drag is recorded once at release: a swipe
        // across twenty tiles is one action, not twenty.
        private readonly List<TileEdit> _paintStroke = new();

        // Where the placement / spawn being dragged started. A move is recorded
        // at RELEASE as from -> to, so the from has to be captured at the press.
        private Vector2 _moveFrom;
        private Vector2 _spawnMoveFrom;

        // Middle-mouse panning of the zoomed canvas.
        private bool _panning;
        private Point _panStartMouse;
        private Point _panStartPan;

        // Scissor rasterizer for clipping canvas content while zoomed.
        private static readonly RasterizerState ScissorOn = new() { ScissorTestEnable = true };

        // Input edge-detection.
        private MouseState _mouseNow, _mousePrev;
        private KeyboardState _keysNow, _keysPrev;

        // Fullscreen state. "Borderless" because exclusive-fullscreen with
        // MonoGame DesktopGL is fussier and worse for editor use.
        private bool _isFullscreen;
        private int _windowedW = EditorLayout.WindowWidth;
        private int _windowedH = EditorLayout.WindowHeight;
        private bool _resizingGuard;

        public EditorGame()
        {
            _graphics = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = EditorLayout.WindowWidth,
                PreferredBackBufferHeight = EditorLayout.WindowHeight,
            };
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
            Window.Title = "SorceryForge — Room Editor";
            Window.AllowUserResizing = true;
            Window.ClientSizeChanged += OnClientSizeChanged;

            // The context every command is handed. Built once: it is two
            // references, and rebuilding it per call would be the only way for
            // a command to be handed a different EditorState than the chrome is
            // rendering.
            _cmd = new EditorCommandContext(_state, this);
        }

        // ====================================================================
        // IBackgroundTarget — the pixels an undo command is allowed to touch
        // ====================================================================
        // Explicitly implemented, so none of this widens EditorGame's own
        // surface: these four members exist for EditorCommands.cs and nothing
        // else calls them.

        Color[]? IBackgroundTarget.BackgroundPixels => _bgPixels;

        int IBackgroundTarget.BackgroundWidth => _currentBackground?.Width ?? 0;

        int IBackgroundTarget.BackgroundHeight => _currentBackground?.Height ?? 0;

        void IBackgroundTarget.BackgroundPixelsChanged()
        {
            // The one line in the undo path that needs a GraphicsDevice, which
            // is exactly why it is on this side of the interface.
            if (_currentBackground != null && _bgPixels != null)
                _currentBackground.SetData(_bgPixels);
        }

        private void OnClientSizeChanged(object? sender, EventArgs e)
        {
            // Re-entrancy guard: ApplyChanges below can fire this same event.
            if (_resizingGuard) return;
            _resizingGuard = true;
            try
            {
                int w = Window.ClientBounds.Width;
                int h = Window.ClientBounds.Height;
                if (w < 200 || h < 200) return;

                _graphics.PreferredBackBufferWidth = w;
                _graphics.PreferredBackBufferHeight = h;
                _graphics.ApplyChanges();

                EditorLayout.Recalculate(w, h);
            }
            finally { _resizingGuard = false; }
        }

        /// <summary>
        /// Ask for a fullscreen toggle. It is APPLIED after the ImGui frame
        /// closes, never during it.
        /// </summary>
        // ToggleFullscreen resizes the back buffer and re-runs
        // EditorLayout.Recalculate. Both callers — F11 in
        // HandleKeyboardShortcuts, and View > Fullscreen in the menu — run
        // INSIDE the open ImGui frame, where io.DisplaySize was already sampled
        // from the old back buffer. Applying it there leaves every panel built
        // afterwards positioned in the new window space while the renderer's
        // projection still describes the old one: one frame of chrome drawn at
        // the wrong scale, which on a 1280 -> 2560 transition means a menu bar
        // twice its height and a status bar off the bottom of the screen.
        //
        // A plain window drag-resize does not need this — SDL dispatches
        // ClientSizeChanged between ticks, not inside the frame.
        private bool _fullscreenTogglePending;

        private void RequestFullscreenToggle() => _fullscreenTogglePending = true;

        private void ApplyPendingFullscreenToggle()
        {
            if (!_fullscreenTogglePending) return;
            _fullscreenTogglePending = false;
            ToggleFullscreen();
        }

        private void ToggleFullscreen()
        {
            _resizingGuard = true;
            try
            {
                var dm = GraphicsDevice.Adapter.CurrentDisplayMode;

                if (!_isFullscreen)
                {
                    // Cache the windowed size for restore.
                    _windowedW = _graphics.PreferredBackBufferWidth;
                    _windowedH = _graphics.PreferredBackBufferHeight;

                    Window.IsBorderless = true;
                    Window.Position = Point.Zero;
                    _graphics.PreferredBackBufferWidth = dm.Width;
                    _graphics.PreferredBackBufferHeight = dm.Height;
                    _graphics.ApplyChanges();
                    EditorLayout.Recalculate(dm.Width, dm.Height);
                }
                else
                {
                    // ORDER MATTERS on Windows/SDL: shrink the back buffer
                    // FIRST, then clear IsBorderless. If you remove the
                    // border while the window is still display-sized, SDL
                    // paints the title bar onto a window that covers the
                    // full screen and the OS never composes the chrome
                    // correctly — the user sees a borderless window with
                    // no visible controls.
                    _graphics.PreferredBackBufferWidth = _windowedW;
                    _graphics.PreferredBackBufferHeight = _windowedH;
                    _graphics.ApplyChanges();

                    Window.IsBorderless = false;

                    // SDL can leave AllowUserResizing in an odd state after
                    // a borderless round-trip; reapply to guarantee drag-
                    // resize works again.
                    Window.AllowUserResizing = true;

                    // Re-centre the restored window on the primary display.
                    Window.Position = new Point(
                        Math.Max(0, (dm.Width  - _windowedW) / 2),
                        Math.Max(0, (dm.Height - _windowedH) / 2));

                    EditorLayout.Recalculate(_windowedW, _windowedH);
                }

                _isFullscreen = !_isFullscreen;
                _state.Status = _isFullscreen
                    ? "Borderless fullscreen — F11 to exit."
                    : "Windowed.";
            }
            finally { _resizingGuard = false; }
        }

        // ====================================================================
        // CONTENT LOAD
        // ====================================================================

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);

            // Before anything that might want to register a texture with it.
            _imgui = new ImGuiRenderer(this);

            _pixel = new Texture2D(GraphicsDevice, 1, 1);
            _pixel.SetData(new[] { Color.White });

            _spawnMarker = BuildSpawnMarkerTexture();

            try
            {
                _font = Content.Load<SpriteFont>("DebugFont");
                // DebugFont was authored for the main game's HUD and only
                // covers a basic ASCII range. Without a default character,
                // MeasureString/DrawString throw on any unknown glyph.
                _font.DefaultCharacter = '?';
            }
            catch { _font = null; }

            // Pre-load every spritesheet the editor draws. Each LoadAndCache
            // call also makes black transparent so item icons render cleanly
            // on the canvas (matches Game1.LoadAndTransparent).
            //
            // Item and enemy sheets come from the shared catalog, so a new
            // entity type is loadable here the moment its row exists — no edit
            // to this method. LoadAndCache is idempotent (it returns the
            // cached texture), so rows sharing one sheet cost a dictionary
            // hit, not a second load.
            LoadAndCache("Tiles");
            foreach (var item in EntityCatalog.Items) LoadAndCache(item.Asset);
            foreach (var enemy in EntityCatalog.Enemies) LoadAndCache(enemy.Asset);

            // Sheets with no catalog row: these belong to placement kinds that
            // aren't type-parameterised (one wizard, one blocked door, two
            // doors), so there's nothing to tabulate.
            LoadAndCache("CaptiveWizardSheet");
            LoadAndCache("BlockedDoorSheet");
            LoadAndCache("LeftDoorFrames");   // 4-frame strip, frame 0 = closed
            LoadAndCache("RightDoorFrames");

            // Personal workspace state — crop presets today. Read once, here,
            // rather than on each use: it is a few dozen bytes, and a re-read
            // per import would let a half-written file break a crop mid-flow.
            // A load problem is reported, never fatal (EditorSettings says why).
            _settings = EditorSettings.Load(null, out string? settingsError);

            BuildPalette();
            LoadRoom(_state.CurrentRoomIndex);

            // After LoadRoom, which sets its own status line.
            if (settingsError != null) _state.Status = settingsError;
        }

        // ====================================================================
        // EDITOR SETTINGS
        // ====================================================================
        // Loaded in LoadContent, written by the acts that change it. See
        // SorceryForge/EditorSettings.cs for what belongs in here and what
        // belongs in assets/data instead.
        // ====================================================================

        private EditorSettings _settings = new();

        /// <summary>
        /// Persist the settings. Returns a fragment to append to the status
        /// line, or "" when the write was clean.
        /// </summary>
        // Never throws to the caller and never aborts what it was called from:
        // failing to remember a crop preset must not cost the user the import
        // they were actually doing.
        private string SaveEditorSettings()
        {
            try
            {
                _settings.Save();
                return "";
            }
            catch (Exception ex)
            {
                return $" ({EditorSettings.DirName}/{EditorSettings.FileName} not written — {ex.Message})";
            }
        }

        private Texture2D LoadAndCache(string asset)
        {
            if (_textures.TryGetValue(asset, out var cached)) return cached;
            var tex = Content.Load<Texture2D>(asset);
            MakeColorTransparent(tex, Color.Black);
            _textures[asset] = tex;
            return tex;
        }

        /// <summary>
        /// The 24x24 player-spawn marker: a 1-px outline plus a centred cross,
        /// everything else transparent. Used for the palette icon, the drag
        /// ghost, and (drawn again as scaled outlines) the canvas overlay, so
        /// all three read as the same object.
        /// </summary>
        private Texture2D BuildSpawnMarkerTexture()
        {
            const int n = 24;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                bool edge  = x == 0 || y == 0 || x == n - 1 || y == n - 1;
                // Two-pixel-wide cross through the centre, stopping short of
                // the outline so the shape doesn't read as a filled square.
                bool cross = (x >= n / 2 - 1 && x <= n / 2 && y >= 3 && y <= n - 4)
                          || (y >= n / 2 - 1 && y <= n / 2 && x >= 3 && x <= n - 4);
                px[y * n + x] = edge || cross ? SpawnColor : Color.Transparent;
            }
            var tex = new Texture2D(GraphicsDevice, n, n);
            tex.SetData(px);
            return tex;
        }

        private static void MakeColorTransparent(Texture2D tex, Color color)
        {
            var data = new Color[tex.Width * tex.Height];
            tex.GetData(data);
            for (int i = 0; i < data.Length; i++)
                if (data[i].R == color.R && data[i].G == color.G && data[i].B == color.B)
                    data[i] = Color.Transparent;
            tex.SetData(data);
        }

        // ====================================================================
        // PALETTE & BUTTONS
        // ====================================================================

        // The palette's SECTION ORDER moved to UI/PalettePanel, which is the
        // only thing that ever read it. Entries still carry their section name
        // as a string, so tagging one here is unchanged.

        private void BuildPalette()
        {
            // -- ITEMS & ENEMIES --------------------------------------------
            // Straight from the shared catalog (Core/EntityCatalog): label,
            // sheet, source rect and section all come from the row, so adding
            // an item type puts it in the palette with no edit here. Row order
            // within a section is palette order; SectionOrder below decides
            // the order of the sections themselves.
            foreach (var item in EntityCatalog.Items)
                _state.Palette.Add(Tag(
                    MakeItem(item.DisplayName, item.Type, item.Asset, item.SourceRect),
                    item.PaletteSection));

            foreach (var enemy in EntityCatalog.Enemies)
                _state.Palette.Add(Tag(
                    MakeEnemy(enemy.DisplayName, enemy.Type, enemy.Asset, enemy.SourceRect),
                    enemy.PaletteSection));

            // -- DOORS ------------------------------------------------------
            // Two entries — pick the opening side that matches where you're
            // dropping. By naming convention:
            //   - LeftOpening goes on the RIGHT edge of a room (X=296).
            //   - RightOpening goes on the LEFT edge of a room (X=0).
            // For top/bottom doors, type is informational; arrival math is
            // the same. Use the inspector to retarget after dropping.
            //
            // Texture swap: the PNG file names describe HINGE SIDE, not
            // opening side. RoomManager.LoadRoom maps DoorType.LeftOpening
            // to _rightDoorTexture (RightDoorFrames.png) and vice versa.
            // We mirror that swap here so the editor preview matches what
            // the game actually renders for each DoorType.
            // DoorOpeningSide carries the logical side; the Label is display
            // text only. Note how the two disagree on purpose — the entry
            // whose side is LeftOpening holds the RightDoorFrames texture.
            var leftDoor = new PaletteEntry(
                "Door (LeftOpening)", PlacementKind.Door,
                _textures["RightDoorFrames"],   // LeftOpening uses RightDoorFrames in-game
                new Rectangle(0, 0, 48, 48))    // frame 0 = closed
            { DoorOpeningSide = DoorType.LeftOpening };
            _state.Palette.Add(Tag(leftDoor, "DOORS"));

            var rightDoor = new PaletteEntry(
                "Door (RightOpening)", PlacementKind.Door,
                _textures["LeftDoorFrames"],    // RightOpening uses LeftDoorFrames in-game
                new Rectangle(0, 0, 48, 48))
            { DoorOpeningSide = DoorType.RightOpening };
            _state.Palette.Add(Tag(rightDoor, "DOORS"));

            // -- OTHER ------------------------------------------------------
            var wizardEntry = new PaletteEntry(
                "Wizard", PlacementKind.Wizard,
                _textures["CaptiveWizardSheet"],
                new Rectangle(0, 0, 48, 48));
            _state.Palette.Add(Tag(wizardEntry, "OTHER"));

            // Blocked door — for the MVP we ship a single Lyre-locked variant.
            var blockedDoorEntry = new PaletteEntry(
                "Blocked Door (Lyre)", PlacementKind.BlockedDoor,
                _textures["BlockedDoorSheet"],
                SpriteConfig.BLOCKED_DOOR_FRAME)
            { ItemType = ItemType.Lyre };
            _state.Palette.Add(Tag(blockedDoorEntry, "OTHER"));

            // -- META -------------------------------------------------------
            // Room-level markers, not entities. The spawn entry carries
            // PlacementKind.Item purely because PaletteEntry demands some
            // Kind; IsPlayerSpawn is what every code path actually branches
            // on, and it is checked before Kind everywhere it matters.
            var spawnEntry = new PaletteEntry(
                "Player Spawn", PlacementKind.Item,
                _spawnMarker,
                new Rectangle(0, 0, 24, 24))
            { IsPlayerSpawn = true };
            _state.Palette.Add(Tag(spawnEntry, "META"));

            BindPaletteIcons();
        }

        /// <summary>
        /// Give every palette entry an ImGui texture handle and the UV corners
        /// of its source rect, so the panel can draw real game sprites without
        /// ever seeing a Texture2D.
        /// </summary>
        // One handle per SHEET, not per entry: five weapons share nothing but
        // the five enemies each have their own strip, and a door's two entries
        // point at two different files. Registering the same texture twice
        // would work but would make the id-to-texture map lie about how many
        // textures there are.
        private void BindPaletteIcons()
        {
            var handles = new Dictionary<Texture2D, IntPtr>();

            foreach (var entry in _state.Palette)
            {
                if (!handles.TryGetValue(entry.Texture, out var id))
                {
                    id = _imgui.BindTexture(entry.Texture);
                    handles[entry.Texture] = id;
                }
                entry.ImGuiTextureId = id;

                // Corner to corner, so a non-square source (every enemy frame)
                // stretches into the square icon box exactly as SpriteBatch
                // stretched it. Deliberate; do not letterbox.
                float w = entry.Texture.Width, h = entry.Texture.Height;
                entry.IconUv0 = new NVector2(entry.SourceRect.Left / w, entry.SourceRect.Top / h);
                entry.IconUv1 = new NVector2(entry.SourceRect.Right / w, entry.SourceRect.Bottom / h);
            }
        }

        private static PaletteEntry Tag(PaletteEntry e, string section) { e.Section = section; return e; }

        // Source rects now arrive from the catalog rather than being assumed
        // to be the full 48x48 sheet — the values are the same for today's
        // items, but an item whose sheet holds several frames will need its
        // own rect and this stops that being a second edit site.
        private PaletteEntry MakeItem(string label, ItemType type, string asset, Rectangle src) =>
            new(label, PlacementKind.Item, _textures[asset], src)
            { ItemType = type };

        private PaletteEntry MakeEnemy(string label, EnemyType type, string asset, Rectangle src) =>
            new(label, PlacementKind.Enemy, _textures[asset], src)
            { EnemyType = type };

        /// <summary>
        /// Enter a cursor mode. The toolbar selects one of the three directly;
        /// nothing cycles them any more.
        /// </summary>
        // Was ToggleMode, which cycled Place -> Paint -> Erase and rewrote its
        // own button's label in the same breath. The label is gone with the
        // button; what is left is the part that was always logic — cancelling
        // whatever the outgoing mode had in flight, and saying what the new one
        // does. Setting the mode you are already in is allowed and re-states
        // the status line, which is harmless and occasionally useful.
        private void SetMode(EditorMode mode)
        {
            _state.Mode = mode;

            // Switching out of Place mode cancels in-progress drag/move;
            // switching out of Erase mode closes any open brush stroke.
            if (_state.Mode != EditorMode.Place)
            {
                _state.Dragging = null;
                _state.IsMovingSelection = false;
            }
            if (_state.Mode != EditorMode.Erase) EndStroke();
            // ...and switching out of Paint closes the paint drag, so its
            // command is recorded rather than left open to merge with the next
            // one the mode is re-entered for.
            if (_state.Mode != EditorMode.Paint) EndPaintStroke();
            _state.Status = _state.Mode switch
            {
                EditorMode.Paint => "Paint mode: left-click adds solid, right-click clears.",
                EditorMode.Erase => _bgPixels == null
                    ? "Erase mode: this room has no editable background PNG."
                    : "Erase: left-drag erases, right-drag restores. [ ] brush, wheel zoom, mid-drag pan, Ctrl+Z undo.",
                _ => "Place mode: drag from palette, click to select/move.",
            };
        }

        // ====================================================================
        // ROOM LOAD / SAVE
        // ====================================================================

        private void LoadRoom(int index)
        {
            _state.CurrentRoomIndex = index;
            var meta = _state.CurrentRoom;

            // Background (null for non-bg rooms; we just show grey).
            // Prefer the raw PNG in the repo Content folder: it's the file
            // Erase-mode Save writes, so loading it means the editor always
            // shows its own edits (the XNB only refreshes on the next
            // content build), and FromStream gives straight (non-premultiplied)
            // alpha which is what SaveAsPng expects to round-trip losslessly.
            // FromStream textures are ours to free; XNB textures belong to
            // the shared ContentManager cache and must never be disposed.
            if (_ownsBackground) _currentBackground?.Dispose();
            _ownsBackground = false;
            _currentBackground = null;
            _bgPixels = null;
            _bgOriginal = null;
            _strokeActive = false;
            _strokeChanged = false;
            _strokeBefore = null;
            _paintStroke.Clear();

            // UNDO HISTORY IS PER-ROOM, and this is where it ends. Below, this
            // method REBUILDS Placements from disk — every Placement object in
            // the working set is replaced — and the commands hold references to
            // those objects. Carrying the stack across would mean a Ctrl+Z that
            // writes to an object no longer in any room's list: no crash, no
            // visible effect, and the edit the user thought they took back
            // still there. See the header of UndoStack.cs, and doc/07.
            _undo.Clear();

            _discardArmed = false;
            _state.BackgroundDirty = false;
            EditorLayout.ResetView();

            if (meta.BackgroundAsset != null)
            {
                string pngPath = Path.Combine(EditorPaths.RepoContentDir, meta.BackgroundAsset + ".png");
                if (File.Exists(pngPath))
                {
                    Texture2D? tex = null;
                    try
                    {
                        using var fs = File.OpenRead(pngPath);
                        tex = Texture2D.FromStream(GraphicsDevice, fs);
                        var px = new Color[tex.Width * tex.Height];
                        tex.GetData(px);
                        // Normalise fully-transparent pixels to (0,0,0,0):
                        // earlier tooling kept RGB under alpha-0 holes, which
                        // would bleed additively under premultiplied blending.
                        for (int i = 0; i < px.Length; i++)
                            if (px[i].A == 0) px[i] = Color.Transparent;
                        tex.SetData(px);
                        _currentBackground = tex;
                        _ownsBackground = true;
                        _bgPixels = px;
                        _bgOriginal = (Color[])px.Clone();
                    }
                    catch
                    {
                        tex?.Dispose();
                        _currentBackground = null;
                        _bgPixels = null;
                        _bgOriginal = null;
                    }
                }
                if (_currentBackground == null)
                {
                    try { _currentBackground = Content.Load<Texture2D>(meta.BackgroundAsset); }
                    catch { _currentBackground = null; }
                }
            }

            // Collision overlay (read-only). The same JSON the game uses.
            _state.CollisionMap = null;
            if (meta.CollisionJsonName != null)
            {
                string path = Path.Combine(EditorPaths.RepoAssetsDataDir, meta.CollisionJsonName);
                if (File.Exists(path))
                {
                    try { _state.CollisionMap = RoomLoader.BuildCollisionTileMap(_textures["Tiles"], path); }
                    catch { _state.CollisionMap = null; }
                }
            }

            // Content (items / enemies / wizards / blocked doors) and doors
            // are read from two separate JSON files. Re-read both on every
            // room change so external edits (or hot-reload while the editor
            // is open) appear immediately.
            var content = RoomContentLoader.TryLoad(meta.RoomId, EditorPaths.RepoAssetsDataDir);
            meta.ReloadLayoutFromDisk();
            _state.LoadFromRoomContent(content ?? new RoomContent(), meta.Doors, meta.PlayerSpawn);
            _state.NextIdCounter = _state.Placements.Count + 1;

            // Clear collision-edit, placement-edit and validation state on
            // every load — the working set was just rebuilt from disk, so
            // nothing in it is unsaved any more.
            _state.CollisionDirty = false;
            _state.PlacementsDirty = false;
            _state.UnreachableIds.Clear();
            _state.HasValidated = false;

            _state.Status = $"Loaded {meta.DisplayName} ({_state.Placements.Count} entities)";
        }

        private void SaveCurrentRoom()
        {
            var meta = _state.CurrentRoom;
            try
            {
                // The status line names exactly the files that hit the disk.
                // Both loaders decline to CREATE a file for a room that is
                // empty and has none yet (they still rewrite an existing one,
                // so deletions persist), so "saved" cannot be assumed — it is
                // built from what each Save actually reports back.
                var saved = new List<string>();

                // 1. Content (items, enemies, wizards, blocked doors).
                if (RoomContentLoader.Save(meta.RoomId, _state.ToRoomContent(), EditorPaths.RepoAssetsDataDir))
                    saved.Add($"content_{meta.RoomId}.json");

                // 2. Layout (doors + player spawn).
                var layout = _state.ToRoomLayoutJson(meta.RoomId);
                if (RoomLayoutLoader.Save(layout, EditorPaths.RepoAssetsDataDir))
                    saved.Add($"layout_{meta.RoomId}.json");

                // Placements and the spawn live entirely in those two files,
                // so they're durable now — clear the flag here (same pattern
                // as the collision / background flags below, each cleared
                // right after the write that persists it).
                _state.PlacementsDirty = false;

                // 3. Collision grid (only when Paint mode produced changes).
                if (_state.CollisionDirty && _state.CollisionMap != null && meta.CollisionJsonName != null)
                {
                    string path = Path.Combine(EditorPaths.RepoAssetsDataDir, meta.CollisionJsonName);
                    RoomLoader.SaveCollisionGrid(path, _state.CollisionMap);
                    _state.CollisionDirty = false;
                    saved.Add(meta.CollisionJsonName);
                }

                // 4. Background PNG (only when Erase mode touched pixels).
                //    Written to the repo source Content folder — the GAME's
                //    XNB refreshes on the next content build (dotnet build).
                //    Write-to-temp + move keeps the source asset intact if
                //    the encode fails mid-way.
                if (_state.BackgroundDirty && _bgPixels != null && _currentBackground != null
                    && meta.BackgroundAsset != null)
                {
                    string pngPath = Path.Combine(EditorPaths.RepoContentDir, meta.BackgroundAsset + ".png");
                    WriteTextureAsPng(_currentBackground, pngPath);
                    // The file the map's thumbnail cache read just changed
                    // under it — drop it so the board shows the erased version
                    // rather than the one from before the stroke.
                    InvalidateMapThumbnail(meta.BackgroundAsset);
                    // The restore brush now restores to this saved state.
                    _bgOriginal = (Color[])_bgPixels.Clone();
                    _state.BackgroundDirty = false;
                    saved.Add($"{meta.BackgroundAsset}.png (rebuild for game)");
                }

                // Refresh the in-memory door / spawn cache from the file we
                // just wrote so DoorMarkers / Validate Doors see the new state.
                meta.ReloadLayoutFromDisk();

                _discardArmed = false;   // a save always disarms the discard guard
                _state.Status = saved.Count > 0
                    ? "Saved " + string.Join(" + ", saved)
                    : $"Nothing to save — {meta.DisplayName} is empty, so no files were created";
            }
            catch (Exception ex)
            {
                _state.Status = "Save failed: " + ex.Message;
            }
        }

        /// <summary>
        /// Write a texture out as a PNG atomically: encode into
        /// &lt;path&gt;.tmp, then move that over the target. A failed or
        /// half-finished encode can then never destroy the file it was
        /// replacing.
        /// </summary>
        // Shared by the Erase-mode background save and the screenshot import.
        // Both are overwriting an asset in Content/ that the user has no other
        // copy of, so both get the same guarantee from the same six lines.
        private static void WriteTextureAsPng(Texture2D texture, string path)
        {
            string tmpPath = path + ".tmp";
            try
            {
                using (var fs = File.Create(tmpPath))
                    texture.SaveAsPng(fs, texture.Width, texture.Height);
                File.Move(tmpPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(tmpPath)) File.Delete(tmpPath);
            }
        }

        // ====================================================================
        // NEW ROOM — background picker overlay
        // ====================================================================
        // A modal list of every unused RoomBG_*.png. State lives here rather
        // than on EditorState because it is transient view plumbing (like
        // _discardArmed), not room data — nothing here survives a room switch
        // or reaches disk. The list itself is drawn by UI/Pickers; what is left
        // in this file is the flow — what a pick means, and what cancels it.
        //
        // Zero typing: the room id and display name come from the filename
        // (NewRoomFlow's derivation rule). The editor has no text field, and
        // the picker is designed so it never needs one.
        // ====================================================================

        private bool _newRoomOpen;
        private List<RoomCandidate> _newRoomCandidates = new();

        private void OpenNewRoomPicker()
        {
            // Creating a room LOADS it, which discards unsaved edits in the
            // current one. The guard therefore runs here, at the click that
            // opens the modal, not at the click that picks a background: its
            // "repeat the action to discard" warning belongs in the status bar
            // the user is looking at, not behind an overlay. Nothing can dirty
            // the room while the picker is open — it swallows all input.
            if (!ConfirmDiscardUnsavedEdits()) return;

            _newRoomCandidates = NewRoomFlow.FindCandidates(EditorPaths.RepoContentDir, RoomManifest.All);
            _newRoomOpen = true;

            int usable = 0;
            foreach (var c in _newRoomCandidates) if (c.CanCreate) usable++;
            _state.Status = usable > 0
                ? $"New Room: pick a background ({usable} unused). Esc or right-click cancels."
                : "New Room: no unused RoomBG_*.png in Content/ — import a screenshot first.";
        }

        private void CloseNewRoomPicker(string status)
        {
            _newRoomOpen = false;
            _state.Status = status;
        }

        /// <summary>
        /// Modal input. Runs instead of every other handler while the picker is
        /// open, so a stray click can't reach the canvas underneath.
        /// </summary>
        private void HandleNewRoomPicker()
        {
            // Cancel only. The rows, the Cancel button and the list's scroll
            // are UI/Pickers' now — ImGui hit-tests them in the same call that
            // draws them. What stays here is the pair of gestures that must
            // keep working with the cursor anywhere on screen, including over
            // the panel itself, which is where ImGui would otherwise claim the
            // mouse: Escape, and right-click.
            if (Pressed(Keys.Escape) || RightClicked())
                CloseNewRoomPicker("New Room cancelled.");
        }

        /// <summary>
        /// Write the room's files, refresh both caches, and open it. The order
        /// is load-bearing: RoomManifest caches the registry in a Lazy and
        /// RoomMeta.All is derived from that, so reloading them the other way
        /// round would rebuild the catalogue from the stale registry.
        /// </summary>
        // THE one path by which a room comes into existence. New Room calls it
        // with a candidate found from an unused PNG; the screenshot import
        // calls it after writing that PNG itself. Everything a new room needs
        // — collision grid, .mgcb block, registry entry, both caches, the load
        // — happens here and nowhere else, so the two entry points cannot
        // produce differently-registered rooms.
        private NewRoomFlow.CreateResult CreateAndOpenRoom(RoomCandidate candidate)
        {
            var result = NewRoomFlow.Create(candidate, EditorPaths.RepoContentDir, EditorPaths.RepoAssetsDataDir);
            if (!result.Ok) return result;

            RoomManifest.Reload();
            RoomMeta.RebuildAll();

            // By id, not by "the last one": Save writes the array in the order
            // it was given, but looking the room up by the key we just wrote
            // can't be wrong.
            int index = 0;
            for (int i = 0; i < RoomMeta.All.Count; i++)
                if (RoomMeta.All[i].RoomId == candidate.RoomId) { index = i; break; }

            // Creating a room from the world map lands you IN it, in the room
            // editor — the point of making a room is to author it, and the
            // alternative (a new empty box appearing on a board) would leave
            // the user to hunt for it. It joins the board on the next Tab,
            // auto-placed like any unwired room. The arrangement is untouched:
            // it lives in _mapStored, which nothing here goes near.
            _mapMode = false;

            LoadRoom(index);
            _state.Status = result.Message;   // after LoadRoom, which sets its own
            return result;
        }

        private void CreateRoom(RoomCandidate candidate)
        {
            // Closed up front, on both outcomes: the picker was modal, and
            // whichever way this goes the status line it leaves behind is the
            // thing the user needs to read.
            _newRoomOpen = false;

            var result = CreateAndOpenRoom(candidate);
            if (!result.Ok) _state.Status = result.Message;
        }

        // ====================================================================
        // IMPORT SCREENSHOT — source picker overlay
        // ====================================================================
        // EDITOR_REVIEW item A. Scans assets/import/ and turns the file you
        // pick into Content/RoomBG_<Name>.png, then hands it to the very same
        // CreateAndOpenRoom that New Room uses. One click from "a screenshot
        // sitting in a folder" to "the editor is in that room, editing it".
        //
        // Same widget pattern as the New Room picker above — modal list, click
        // zones populated in Draw and consumed in Update, transient state kept
        // here rather than on EditorState. Same zero-typing rule too: the
        // source filename decides the asset name, the room id and the display
        // name (ImageImport's header spells the rule out).
        //
        // The one control it adds is the CPC quantize toggle. See
        // ImageImport.QuantizeToCpc for what it does and why it defaults ON.
        // ====================================================================

        private bool _importOpen;
        private List<ImportCandidate> _importCandidates = new();

        // Session preference, not room data: it survives closing and reopening
        // the picker, because importing a set of screenshots is one decision
        // made once, not one per file. ON by default — the sources are captures
        // of a CPC game, so snapping to the hardware palette is the answer
        // nearly always.
        private bool _importQuantize = true;

        // How the current candidate list partitions for a batch import.
        // Computed once when the picker opens — nothing that can change the
        // answer (the file list, the stored presets) is reachable without
        // closing it — and read by the footer hint every frame.
        private ImageImport.BatchPlan _importPlan = new();

        private void OpenImportPicker()
        {
            // Importing LOADS the new room, discarding unsaved edits in the
            // current one — so the guard runs here, at the click that opens the
            // modal, exactly as it does for New Room. Its "repeat the action to
            // discard" warning belongs in the status bar the user is looking
            // at, not behind an overlay.
            if (!ConfirmDiscardUnsavedEdits()) return;

            // Created rather than merely read: the folder ships in the repo,
            // but a fresh clone with an aggressive clean, or a user who deleted
            // it, should get a working "put your files here" instruction rather
            // than an empty list and no explanation.
            try { Directory.CreateDirectory(EditorPaths.RepoImportDir); }
            catch (Exception ex)
            {
                _state.Status = $"Import: cannot use {EditorPaths.RepoImportDir} — {ex.Message}";
                return;
            }

            _importCandidates = ImageImport.FindCandidates(
                EditorPaths.RepoImportDir, EditorPaths.RepoContentDir, RoomManifest.All);
            _importPlan = ImageImport.PlanBatch(_importCandidates, _settings.CropPreset);
            _importOpen = true;

            int usable = 0;
            foreach (var c in _importCandidates) if (c.CanCreate) usable++;
            _state.Status = _importCandidates.Count == 0
                ? "Import: assets/import/ holds no .jpg/.jpeg/.png — drop a screenshot in and click Import again."
                : $"Import: pick a screenshot ({usable} of {_importCandidates.Count} importable)" +
                  (_importPlan.Offered ? $", or A to import all {_importPlan.Eligible.Count}" : "") +
                  ". Esc or right-click cancels.";
        }

        private void CloseImportPicker(string status)
        {
            _importOpen = false;
            _importPlan = new ImageImport.BatchPlan();
            _state.Status = status;
        }

        /// <summary>
        /// Modal input, mirroring HandleNewRoomPicker: while the picker is open
        /// it runs instead of every other handler, so a stray click can't reach
        /// the canvas underneath.
        /// </summary>
        private void HandleImportPicker()
        {
            if (Pressed(Keys.Escape) || RightClicked())
            {
                CloseImportPicker("Import cancelled.");
                return;
            }

            // Import All. Safe to press when nothing qualifies — it says so and
            // leaves the picker up — so it needs no guard beyond its own.
            //
            // A KEY, NOT A BUTTON, still. It belongs to the picker — it is only
            // meaningful while looking at the list it acts on — and the footer
            // hint advertises it whenever it is available.
            if (Pressed(Keys.A)) StartBatchImport();

            // The rows, the quantize toggle and the Cancel button are
            // UI/Pickers' now. Escape and right-click stay here, ungated, so
            // they keep working with the cursor over the panel itself.
        }

        private void ToggleImportQuantize()
        {
            _importQuantize = !_importQuantize;
            _state.Status = _importQuantize
                ? "Import: CPC quantize ON — pixels snap to the 27 hardware colours (kills JPEG noise)."
                : "Import: CPC quantize OFF — source colours pass through untouched.";
        }

        /// <summary>
        /// Decode a source file into straight-alpha pixels. This and
        /// FinishImport are the only two places in the import path that touch
        /// MonoGame; everything between them is ImageImport's plain Color[]
        /// arithmetic, which is why the interesting half is headlessly testable.
        /// </summary>
        private bool TryDecodeImportSource(ImportCandidate candidate,
                                           out Color[] pixels, out int width, out int height)
        {
            pixels = Array.Empty<Color>();
            width = height = 0;
            try
            {
                // FromStream is what makes "no new NuGet dependency" true: the
                // JPEG decoder is already inside MonoGame (bundled
                // StbImageSharp), and it hands back straight (non-premultiplied)
                // alpha — the same form LoadRoom relies on and SaveAsPng
                // round-trips losslessly.
                using var fs = File.OpenRead(candidate.SourcePath);
                using var tex = Texture2D.FromStream(GraphicsDevice, fs);
                width = tex.Width;
                height = tex.Height;
                pixels = new Color[width * height];
                tex.GetData(pixels);
                return true;
            }
            catch (Exception ex)
            {
                _state.Status = $"Import: could not decode {candidate.FileName} — {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Act on a picked source. A size that is already 320x144 or an exact
        /// multiple goes straight through; anything else opens the crop step.
        /// </summary>
        private void RunImport(ImportCandidate candidate)
        {
            _importOpen = false;

            if (!TryDecodeImportSource(candidate, out var src, out int w, out int h)) return;

            // The picker classified this file from its HEADER. Everything below
            // branches on what the DECODER actually produced, so a header we
            // misread costs a different (correct) branch rather than reaching
            // PointSample as an out-of-range region.
            if (ImageImport.ExactMultiple(w, h) > 0)
            {
                var pixels = ImageImport.BuildRoomBackground(
                    src, w, h, ImageImport.WholeImage(w, h), _importQuantize);
                FinishImport(candidate, pixels);
                return;
            }

            if (!ImageImport.CanCrop(w, h))
            {
                _state.Status = $"Import: {candidate.FileName} decoded as {w}x{h}, smaller than a " +
                                $"{ImageImport.RoomWidth}x{ImageImport.RoomHeight} room — nothing to crop.";
                return;
            }

            OpenCrop(candidate, src, w, h);
        }

        /// <summary>
        /// Write the finished 320x144 pixels to Content/ and register the room.
        /// The tail of every import route.
        /// </summary>
        // Returns rather than setting the status line, because there are now
        // two callers that want the outcome for different reasons: the single
        // import shows the message, and the batch collects it as one row of a
        // summary. The WORK is identical for both — one PNG write, one
        // CreateAndOpenRoom — which is what "the batch is a loop over the
        // existing functions" has to mean to be worth saying.
        private bool TryFinishImport(ImportCandidate candidate, Color[] pixels, out string message)
        {
            string pngPath = Path.Combine(EditorPaths.RepoContentDir, candidate.BackgroundAsset + ".png");
            try
            {
                using var tex = new Texture2D(GraphicsDevice, ImageImport.RoomWidth, ImageImport.RoomHeight);
                tex.SetData(pixels);
                WriteTextureAsPng(tex, pngPath);
            }
            catch (Exception ex)
            {
                message = $"Import: writing {candidate.BackgroundAsset}.png failed — {ex.Message}";
                return false;
            }

            // PNG first, registration second. Should the registration fail, the
            // leftover is an unused background in Content/ — which is exactly
            // what the New Room picker lists, so the user can finish the job
            // from there. The other order would put a room in rooms.json
            // naming an asset that isn't on disk, which the game refuses to
            // start with.
            var result = CreateAndOpenRoom(candidate);
            string mode = _importQuantize ? "CPC" : "raw";
            if (result.Ok)
            {
                message = $"Imported {candidate.FileName} [{mode}] -> {candidate.BackgroundAsset}.png. {result.Message}";
                return true;
            }

            message = $"Wrote {candidate.BackgroundAsset}.png but registration failed: {result.Message} " +
                      "The PNG is now an unused background — New Room can finish it.";
            return false;
        }

        private void FinishImport(ImportCandidate candidate, Color[] pixels)
        {
            TryFinishImport(candidate, pixels, out string message);
            _state.Status = message;
        }

        // ====================================================================
        // IMPORT — BATCH ("IMPORT ALL")
        // ====================================================================
        // With a preset in hand, a whole folder of identically framed captures
        // needs no decisions: A in the picker imports every file that would
        // have gone straight through, skipping and naming the rest.
        //
        // A KEY, NOT A BUTTON. The top bar is full (the same reason Tab opens
        // the map and N/I open the pickers), and this belongs to the picker
        // anyway — it is only meaningful while looking at the list it acts on.
        // The footer hint line advertises it whenever it is available, and says
        // nothing when it is not, so it can't look broken.
        //
        // ONE FILE PER FRAME, not a loop. Seventy-five decode-resample-encode
        // cycles inside one Update is several frozen seconds with no way to
        // tell a slow batch from a hung editor. A per-frame step costs a small
        // state machine and buys a status line that actually counts up, and an
        // Esc that actually stops it.
        //
        // The partition, the skip reasons and the summary all live in
        // ImageImport, where tools/ImportCheck drives them; what is here is the
        // decode, the encode and the pacing.
        // ====================================================================

        private bool _batchRunning;
        private List<ImageImport.BatchEntry> _batchQueue = new();
        private readonly List<ImageImport.BatchSkip> _batchSkips = new();
        private int _batchIndex;
        private int _batchImported;
        private int _batchTotal;

        private void StartBatchImport()
        {
            // Re-planned here rather than reusing _importPlan. It is one call
            // on a keypress, and it means the batch acts on the state it is
            // actually started from — not on whatever was true when the picker
            // opened, however hard that is to arrange today.
            var plan = ImageImport.PlanBatch(_importCandidates, _settings.CropPreset);
            if (!plan.Offered)
            {
                // Not an error — just nothing to do. Say which of the two
                // reasons it is, because "no preset yet" has an obvious fix and
                // "everything here is refused" does not.
                _state.Status = plan.Eligible.Count == 1
                    ? "Import All needs at least two ready files — click the one that is ready."
                    : "Import All: nothing here imports without a decision. Import one of each " +
                      "size on its own first; that stores its crop preset and the rest follow.";
                return;
            }

            _importOpen = false;

            _batchQueue = plan.Eligible;
            _batchSkips.Clear();
            _batchSkips.AddRange(plan.Skipped);
            _batchIndex = 0;
            _batchImported = 0;
            _batchTotal = plan.Eligible.Count;
            _batchRunning = true;

            string mode = _importQuantize ? "CPC" : "raw";
            _state.Status = $"Import All: {_batchTotal} file(s) [{mode}], {_batchSkips.Count} skipped. " +
                            "Esc stops after the file in progress.";
        }

        /// <summary>
        /// Modal input plus exactly one file's work. Runs instead of every
        /// other handler while a batch is going.
        /// </summary>
        private void StepBatchImport()
        {
            // Esc stops after the current file rather than mid-write: a batch
            // is a sequence of complete, independent creations, and there is no
            // half-done room to unwind. What has been imported stays imported.
            if (Pressed(Keys.Escape) || RightClicked())
            {
                FinishBatchImport(aborted: true);
                return;
            }

            if (_batchIndex >= _batchQueue.Count)
            {
                FinishBatchImport(aborted: false);
                return;
            }

            var entry = _batchQueue[_batchIndex++];
            var candidate = entry.Candidate;
            string label = string.IsNullOrEmpty(candidate.RoomId) ? candidate.FileName : candidate.RoomId;

            if (TryDecodeImportSource(candidate, out var src, out int w, out int h))
            {
                // Re-decided against the DECODED size, through the same
                // function that built the plan from the header size. A header
                // we misread costs a named skip here, not a bad room.
                var region = ImageImport.BatchRegionFor(w, h, _settings.CropPreset(w, h), out _);
                if (region == null)
                {
                    _batchSkips.Add(new ImageImport.BatchSkip(label,
                        $"decoded as {w}x{h}, which has no preset — import it on its own"));
                }
                else
                {
                    var pixels = ImageImport.BuildRoomBackground(src, w, h, region.Value, _importQuantize);
                    if (TryFinishImport(candidate, pixels, out string message)) _batchImported++;
                    else _batchSkips.Add(new ImageImport.BatchSkip(label, message));
                }
            }
            else
            {
                // TryDecodeImportSource put its own reason in the status line,
                // which the progress line below is about to overwrite — so take
                // it as this file's skip reason before that happens.
                _batchSkips.Add(new ImageImport.BatchSkip(label, _state.Status));
            }

            // Last, over whatever TryFinishImport's CreateAndOpenRoom left
            // behind: while a batch is running, the count is the thing to read.
            _state.Status = $"Import All: {_batchIndex}/{_batchTotal} — {candidate.FileName} " +
                            $"({_batchImported} in, {_batchSkips.Count} skipped). Esc stops.";
        }

        private void FinishBatchImport(bool aborted)
        {
            // A stop leaves the queue's tail unattempted. Those are not
            // failures and must not be reported as skips — say plainly how many
            // were never reached.
            int unreached = Math.Max(0, _batchQueue.Count - _batchIndex);

            _batchRunning = false;
            _batchQueue = new List<ImageImport.BatchEntry>();

            string summary = ImageImport.SummariseBatch(_batchImported, _batchSkips, aborted);
            if (aborted && unreached > 0) summary += $" {unreached} not attempted.";
            _state.Status = summary;
        }


        // ====================================================================
        // IMPORT — CROP OVERLAY
        // ====================================================================
        // A real emulator capture has a border round it and whatever scale the
        // screenshot key produced, so "320x144 or an exact multiple" covers
        // very few of them. Picking one of the others opens this: the whole
        // source fitted to the canvas area with a fixed 20:9 box over it, drag
        // to move, wheel to resize, Enter to cut.
        //
        // An overlay state, not a mode. It is modal in exactly the way the two
        // pickers are — it consumes every input while open, and everything it
        // owns is torn down when it closes. Nothing has been written to disk at
        // this point, so cancelling really does leave no trace; that is also
        // why the discard guard is not involved here but at the click that
        // opened the Import picker, several steps earlier.
        //
        // All the arithmetic — the fit transform, both mappings, the aspect
        // lock, the clamping, the final sample — is in ImageImport, where
        // tools/ImportCheck can drive it. What lives here is the gesture.
        // ====================================================================

        private bool _cropOpen;
        private ImportCandidate? _cropCandidate;
        private Texture2D? _cropTexture;                       // ours to dispose
        private Color[] _cropPixels = Array.Empty<Color>();
        private int _cropSrcW, _cropSrcH;
        private Rectangle _cropRect;                           // in SOURCE pixels
        private bool _cropDragging;
        private Point _cropDragStartMouse;
        private Point _cropDragStartOrigin;                    // rect top-left at drag start

        // Where the box was when the overlay opened. Shown, not enforced —
        // the moment the user drags or wheels it, the label is stale in the
        // only way that matters (it still says where they started from).
        private ImageImport.CropPresetOrigin _cropPresetOrigin;

        private void OpenCrop(ImportCandidate candidate, Color[] pixels, int srcW, int srcH)
        {
            _cropCandidate = candidate;
            _cropPixels = pixels;
            _cropSrcW = srcW;
            _cropSrcH = srcH;
            // Pre-placed from the preset for this source size when there is
            // one, so the common case — a folder of identically framed
            // captures — is one glance and Enter. Still shown either way:
            // nothing is cut without the user seeing the box first.
            _cropRect = ImageImport.ResolveCropRect(
                srcW, srcH, _settings.CropPreset(srcW, srcH), out _cropPresetOrigin);
            _cropDragging = false;

            // Built back up from the decoded pixels rather than kept from the
            // decode, so what is on screen is provably the same array the crop
            // will sample — not a second decode that could differ.
            try
            {
                _cropTexture?.Dispose();
                _cropTexture = new Texture2D(GraphicsDevice, srcW, srcH);
                _cropTexture.SetData(pixels);
            }
            catch (Exception ex)
            {
                CloseCrop($"Import: cannot preview {candidate.FileName} at {srcW}x{srcH} — {ex.Message}");
                return;
            }

            _cropOpen = true;
            _state.Status = $"Crop {candidate.FileName} ({srcW}x{srcH}) — " +
                            $"{ImageImport.DescribeCropPreset(_cropPresetOrigin, srcW, srcH)}. " +
                            "Drag to move, wheel to resize, Enter confirms, Esc cancels.";
        }

        private void CloseCrop(string? status)
        {
            _cropOpen = false;
            _cropTexture?.Dispose();
            _cropTexture = null;
            _cropPixels = Array.Empty<Color>();
            _cropCandidate = null;
            _cropDragging = false;
            if (status != null) _state.Status = status;
        }

        /// <summary>
        /// Cut the selection to 320x144, quantize if the picker's toggle says
        /// so, and hand it to the same FinishImport the direct path uses.
        /// </summary>
        private void ConfirmCrop()
        {
            if (_cropCandidate == null) { CloseCrop("Import cancelled."); return; }

            // Everything CloseCrop is about to throw away, captured first.
            var candidate = _cropCandidate;
            var pixels = ImageImport.BuildRoomBackground(
                _cropPixels, _cropSrcW, _cropSrcH, _cropRect, _importQuantize);
            var rect = _cropRect;
            int srcW = _cropSrcW, srcH = _cropSrcH;

            // Remember the framing against the source's dimensions, before
            // anything can fail: this is the act the preset records, and it is
            // worth keeping even if the import that follows goes wrong. Last
            // confirmed crop of a size wins, including over the built-in.
            _settings.SetCropPreset(srcW, srcH, rect);
            string settingsNote = SaveEditorSettings();

            CloseCrop(null);
            FinishImport(candidate, pixels);

            // FinishImport owns the status line; append what was cut, since the
            // crop is the part of this import the user made a decision about.
            _state.Status += $" Cropped {rect.Width}x{rect.Height} at ({rect.X}, {rect.Y}) — " +
                             $"remembered for {srcW}x{srcH} sources.{settingsNote}";
        }

        /// <summary>Modal input for the crop step.</summary>
        private void HandleCropOverlay()
        {
            // A modal with nothing to show would swallow every input forever.
            // Nothing can currently reach this state — OpenCrop only sets
            // _cropOpen after the preview texture exists — but the cost of
            // being sure is one line, and the cost of being wrong is a wedged
            // editor with unsaved work in it.
            if (_cropTexture == null || _cropCandidate == null)
            {
                CloseCrop("Import cancelled — the crop preview was lost.");
                return;
            }

            if (Pressed(Keys.Escape) || RightClicked())
            {
                CloseCrop("Import cancelled — nothing was written.");
                return;
            }

            if (Pressed(Keys.Enter))
            {
                ConfirmCrop();
                return;
            }

            // The fitted image is a world surface: it answers the wheel and the
            // drag only when ImGui has declined them, so the header and footer
            // strips drawn over it cannot also resize or move the selection.
            // Escape / Enter / right-click above stay ungated — they are the
            // modal's cancel and confirm, and must work over its own chrome.
            bool worldMouse = _router.MouseReachesWorld;

            int wheel = _mouseNow.ScrollWheelValue - _mousePrev.ScrollWheelValue;
            if (wheel != 0 && worldMouse)
            {
                _cropRect = ImageImport.StepCropWidth(_cropRect, Math.Sign(wheel), _cropSrcW, _cropSrcH);
                _state.Status = CropSelectionSummary();
            }

            var mouse = new Point(_mouseNow.X, _mouseNow.Y);
            var fit = CropFitRect;

            // The Confirm and Cancel buttons used to be hit-tested here, first,
            // so that a click on one could never also start a drag. The router
            // does that now: both live in ImGui strips, and hovering either
            // makes ImGui claim the mouse, which is what clears worldMouse.
            if (LeftClicked())
            {
                if (worldMouse && fit.Contains(mouse))
                {
                    _cropDragging = true;
                    _cropDragStartMouse = mouse;
                    _cropDragStartOrigin = new Point(_cropRect.X, _cropRect.Y);
                }
            }

            if (!LeftHeld()) _cropDragging = false;

            if (_cropDragging)
            {
                // Measured from the gesture's START, not frame to frame: a
                // per-frame delta rounds to source pixels every frame and the
                // rounding error accumulates, so a slow drag out and back does
                // not return the box to where it began.
                var delta = ImageImport.ScreenDeltaToSource(
                    new Point(mouse.X - _cropDragStartMouse.X, mouse.Y - _cropDragStartMouse.Y),
                    fit, _cropSrcW, _cropSrcH);
                _cropRect = ImageImport.ClampCropRect(
                    new Rectangle(_cropDragStartOrigin.X + delta.X, _cropDragStartOrigin.Y + delta.Y,
                                  _cropRect.Width, _cropRect.Height),
                    _cropSrcW, _cropSrcH);
                _state.Status = CropSelectionSummary();
            }
        }

        private string CropSelectionSummary()
        {
            float scale = _cropRect.Width / (float)ImageImport.RoomWidth;
            return $"Crop {_cropRect.Width}x{_cropRect.Height} at ({_cropRect.X}, {_cropRect.Y}) " +
                   $"-> {ImageImport.RoomWidth}x{ImageImport.RoomHeight} ({scale:0.00}x down). " +
                   "Enter confirms, Esc cancels.";
        }

        /// <summary>
        /// Where the source image is drawn: the canvas area, inset a little so
        /// the selection's outline at a room-edge crop is still visible rather
        /// than flush with the panel beside it.
        /// </summary>
        private static Rectangle CropArea => InflateRect(EditorLayout.CanvasRect, -8);

        private Rectangle CropFitRect => ImageImport.FitInside(_cropSrcW, _cropSrcH, CropArea);

        /// <summary>
        /// The crop step, drawn in its own two passes (see the call site in
        /// Draw): the source image with LINEAR filtering, then everything else
        /// with the PointClamp the rest of the editor uses.
        /// </summary>
        private void DrawCropOverlay()
        {
            if (!_cropOpen || _cropTexture == null || _cropCandidate == null) return;

            var fit = CropFitRect;

            // -- pass A: the source image ------------------------------------
            // Linear, uniquely in this editor. The image is being shown at an
            // arbitrary fractional scale, and point sampling at (say) 0.43x
            // drops more than half the rows — which turns a screenshot into
            // something you cannot recognise a room in, and recognising the
            // room is the entire job of this screen. The pixels this affects
            // are the PREVIEW only; the crop itself is point-sampled.
            _spriteBatch.Begin(samplerState: SamplerState.LinearClamp);
            FillRect(new Rectangle(0, 0, EditorLayout.WindowWidth, EditorLayout.WindowHeight),
                     new Color(0, 0, 0, 210));
            _spriteBatch.Draw(_cropTexture, fit, Color.White);
            _spriteBatch.End();

            // -- pass B: chrome ----------------------------------------------
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);

            var sel = ImageImport.SourceRectToScreen(_cropRect, fit, _cropSrcW, _cropSrcH);

            // Darken everything outside the selection, in four bands, so the
            // eye reads the bright part as "this is the room". Widths are
            // floored at zero: at a punishingly small window the selection can
            // round out to a pixel wider than the band arithmetic expects, and
            // a negative-width rect is not something SpriteBatch should be
            // handed.
            var shade = new Color(0, 0, 0, 150);
            FillRect(new Rectangle(fit.X, fit.Y, fit.Width, Math.Max(0, sel.Y - fit.Y)), shade);
            FillRect(new Rectangle(fit.X, sel.Bottom, fit.Width, Math.Max(0, fit.Bottom - sel.Bottom)), shade);
            FillRect(new Rectangle(fit.X, sel.Y, Math.Max(0, sel.X - fit.X), sel.Height), shade);
            FillRect(new Rectangle(sel.Right, sel.Y, Math.Max(0, fit.Right - sel.Right), sel.Height), shade);

            DrawRectOutline(InflateRect(sel, 1), Color.Black);
            DrawRectOutline(sel, Color.White);
            DrawCropCornerTicks(sel);
            DrawRectOutline(fit, new Color(90, 100, 130));

            // The header and footer strips that used to follow are UI/Pickers'
            // now. What is left here is the pixel-space half of the crop step —
            // the fitted image, the shading, the selection and its ticks — and
            // that is the half ImGui has nothing to offer.

            _spriteBatch.End();
        }

        /// <summary>Short bars at the selection's corners — the box reads as a handle-less crop frame.</summary>
        private void DrawCropCornerTicks(Rectangle sel)
        {
            int t = Math.Max(6, Math.Min(20, sel.Width / 12));
            var c = Color.White;
            FillRect(new Rectangle(sel.Left, sel.Top, t, 3), c);
            FillRect(new Rectangle(sel.Left, sel.Top, 3, t), c);
            FillRect(new Rectangle(sel.Right - t, sel.Top, t, 3), c);
            FillRect(new Rectangle(sel.Right - 3, sel.Top, 3, t), c);
            FillRect(new Rectangle(sel.Left, sel.Bottom - 3, t, 3), c);
            FillRect(new Rectangle(sel.Left, sel.Bottom - t, 3, t), c);
            FillRect(new Rectangle(sel.Right - t, sel.Bottom - 3, t, 3), c);
            FillRect(new Rectangle(sel.Right - 3, sel.Bottom - t, 3, t), c);
        }


        // ====================================================================
        // WORLD MAP MODE
        // ====================================================================
        // EDITOR_REVIEW item D. Every registry room as a box carrying its own
        // background, door links as arrows between them, coloured by the same
        // verdicts the Doors button reports. Tab in, Tab or Esc out, click a
        // room to open it.
        //
        // It is a MODE, not an overlay: while it is up, nothing of the room
        // editor runs. No palette, no canvas, no paint or punch, none of the
        // room keyboard shortcuts — HandleMapInput is the whole of Update. The
        // current room stays loaded behind it untouched, which is why entering
        // the map needs no discard guard and why clicking a room does.
        //
        // The board is drawn across the full window between the top bar and the
        // status bar, over where the palette and inspector would be: at
        // seventy-five rooms, space is the scarce thing, and neither panel has
        // anything to say about a world.
        // ====================================================================

        private bool _mapMode;
        private readonly MapView _mapView = new();
        private readonly List<MapRoom> _mapRooms = new();
        private List<MapEdge> _mapEdges = new();
        private Rectangle _mapContentBounds;
        private DoorReport _mapDoors = new();

        // The first entry frames the whole board; later entries keep whatever
        // view the user left behind, because re-framing every time would undo
        // the deliberate act of zooming in on a corner of the world.
        private bool _mapFramed;

        // Thumbnails, keyed by background asset. A NULL value is cached too and
        // means "tried, and there is no readable PNG" — without that, a missing
        // background would be re-opened from disk every single frame.
        //
        // Its own cache, deliberately not _textures: those go through
        // LoadAndCache, which black-keys them for sprite transparency, and
        // black-keying a room background would punch holes straight through the
        // picture. Nor does it touch _currentBackground — the room editor's
        // pixel-edit state is not something a thumbnail may disturb.
        private readonly Dictionary<string, Texture2D?> _mapThumbs = new();

        // Click-versus-drag. A press remembers where it started and what was
        // under it; travel past MapClickSlop turns it into a drag and cancels
        // the click, so a shaky hand cannot open a room the user was scrolling
        // past. A drag that began on a room moves that room; anywhere else it
        // pans the board.
        private const int MapClickSlop = 4;
        private bool _mapLeftDown, _mapMidDown;
        private Point _mapPressScreen;
        private Vector2 _mapPressPan;
        private Vector2 _mapPressRoomPos;
        private bool _mapPressMoved;
        private MapRoom? _mapPressRoom;

        // Hand-arranged positions, keyed by room id: the working copy of
        // assets/data/worldmap.json. Read once per session on the first map
        // entry, updated on every drag release, written by Ctrl+S in map mode.
        // A room absent from here is auto-placed, every time — which is also
        // what an absent file means, so "no arrangement yet" needs no special
        // case anywhere.
        private readonly Dictionary<string, Vector2> _mapStored = new(StringComparer.Ordinal);
        private bool _mapPositionsLoaded;

        /// <summary>Everything between the top bar and the status bar.</summary>
        private static Rectangle MapBoardRect => new(
            0, EditorLayout.TopBarHeight, EditorLayout.WindowWidth,
            Math.Max(0, EditorLayout.WindowHeight - EditorLayout.TopBarHeight - EditorLayout.StatusBarHeight));

        private void ToggleMapMode()
        {
            if (_mapMode) LeaveMapMode();
            else EnterMapMode();
        }

        private void EnterMapMode()
        {
            _mapMode = true;
            _mapLeftDown = _mapMidDown = _mapPressMoved = false;
            _mapPressRoom = null;

            LoadMapPositions();
            RebuildMapBoard();

            _mapView.Viewport = MapBoardRect;
            if (!_mapFramed) { _mapView.FitTo(_mapContentBounds); _mapFramed = true; }
            else _mapView.ClampPan(_mapContentBounds);

            _state.Status = MapStatusLine();
        }

        private void LeaveMapMode()
        {
            _mapMode = false;
            _mapLeftDown = _mapMidDown = false;
            _state.Status = $"Room view — {_state.CurrentRoom.DisplayName}. Tab returns to the map.";
        }

        /// <summary>
        /// Recompute the board: validate the world's doors, lay the rooms out,
        /// build the arrows. Runs on every entry, so a room created or a door
        /// rewired since last time is on the map when you come back.
        /// </summary>
        private void RebuildMapBoard()
        {
            // The verdicts colour the arrows, and they come from the shared
            // validator — so an arrow can never say a link is fine while the
            // Doors button says it is broken. It also fills _lastDoorTable,
            // which is the table the arrows are then built from: same doors,
            // same answers, one source.
            _mapDoors = RunDoorValidation();

            _mapRooms.Clear();
            foreach (var meta in RoomMeta.All)
                _mapRooms.Add(new MapRoom
                {
                    RoomId = meta.RoomId,
                    DisplayName = meta.DisplayName,
                    BackgroundAsset = meta.BackgroundAsset ?? "",
                });

            // Hand-arranged rooms take their stored position; everything else
            // is auto-placed by the deterministic BFS layout. Rebuilding is
            // therefore safe at any moment — it never moves a room the user
            // put somewhere.
            WorldMap.PlaceRooms(_mapRooms, _mapStored, _lastDoorTable);

            _mapEdges = WorldMap.BuildEdges(_mapRooms, _lastDoorTable, _state.DoorStatus);
            _mapContentBounds = WorldMap.ContentBounds(_mapRooms);
        }

        /// <summary>
        /// Read worldmap.json once per session, on the first entry into the
        /// map. Re-reading on later entries would throw away drags the user
        /// has made but not yet saved.
        /// </summary>
        private void LoadMapPositions()
        {
            if (_mapPositionsLoaded) return;
            _mapPositionsLoaded = true;

            var stored = WorldMapFile.Load(EditorPaths.RepoAssetsDataDir, out string? error);
            _mapStored.Clear();

            // Positions for rooms that no longer exist are dropped here and
            // gone from the file on the next save. Keeping them would silt up a
            // file whose entire content is meant to be deliberate acts, with
            // entries nobody can attribute to anything.
            int dropped = 0;
            foreach (var pair in stored)
            {
                if (RoomMeta.Find(pair.Key) != null) _mapStored[pair.Key] = pair.Value;
                else dropped++;
            }

            _state.MapDirty = false;
            _mapLoadNote = error ?? (dropped > 0
                ? $" ({dropped} position(s) for rooms that no longer exist will be dropped on save)"
                : null);
        }

        // Carried from the load so the first map status line can mention a
        // problem with the file without the load having to own the status bar.
        private string? _mapLoadNote;

        /// <summary>
        /// Write the arrangement to assets/data/worldmap.json. Ctrl+S in map
        /// mode; room-mode Ctrl+S is untouched and still saves the room.
        /// </summary>
        // Born-empty discipline, the same rule the room loaders follow and for
        // the same reason: nothing arranged and no file yet writes nothing, so
        // an untouched map never adds a file to the repository. Nothing
        // arranged but a file EXISTS still writes — that is a user who dragged
        // every room back to auto-placement, and their reset has to persist.
        private void SaveWorldMap()
        {
            try
            {
                bool wrote = WorldMapFile.Save(_mapRooms, EditorPaths.RepoAssetsDataDir);
                _state.MapDirty = false;
                _discardArmed = false;

                int arranged = 0;
                foreach (var room in _mapRooms) if (room.Arranged) arranged++;

                _state.Status = wrote
                    ? $"Saved {WorldMapFile.FileName} — {arranged} room(s) arranged by hand, " +
                      $"{_mapRooms.Count - arranged} auto-placed."
                    : $"Nothing to save — no room has been dragged, so no {WorldMapFile.FileName} was created.";
            }
            catch (Exception ex)
            {
                _state.Status = $"Saving {WorldMapFile.FileName} failed: {ex.Message}";
            }
        }

        private string MapStatusLine()
        {
            string doors = _mapDoors.Bad == 0
                ? $"{_mapDoors.Total} door links, all clean"
                : $"{_mapDoors.Bad} of {_mapDoors.Total} door links broken";
            string note = _mapLoadNote ?? "";
            _mapLoadNote = null;   // said once, on the entry that read the file
            return $"World map: {_mapRooms.Count} rooms, {doors}. " +
                   $"Click a room to open it, drag to arrange, Ctrl+S saves the arrangement.{note}";
        }

        // --------------------------------------------------------------------
        // MAP INPUT
        // --------------------------------------------------------------------

        private void HandleMapInput()
        {
            // Re-read every frame: the window can be resized while the map is
            // up, and a stale viewport would put the click zones somewhere
            // other than the boxes.
            _mapView.Viewport = MapBoardRect;

            bool keys = _router.KeyboardReachesEditor;

            // Esc returns to the room view. It deliberately does NOT exit the
            // editor here — the exit-with-autosave path lives in room view,
            // where the unsaved work it protects actually is.
            if (keys && Pressed(Keys.Escape)) { LeaveMapMode(); return; }

            // Ctrl+S in map mode saves the ARRANGEMENT. Room Ctrl+S is
            // untouched and still saves the room: two modes, two things to
            // save, one key that means "persist what is in front of you".
            bool ctrl = _keysNow.IsKeyDown(Keys.LeftControl) || _keysNow.IsKeyDown(Keys.RightControl);
            if (keys && ctrl && Pressed(Keys.S)) { SaveWorldMap(); return; }

            // N and I open the New Room and Import pickers — the SAME overlays
            // the File menu opens, invoked from here because the map is where
            // "the world is missing a room" is a thing you notice. They remain
            // MAP-MODE-ONLY keys, exactly as before this PR: room mode reaches
            // both through the menu and never had these bindings.
            //
            // Their discard guards are untouched and still concern the CURRENT
            // ROOM's unsaved edits, which exist just as much while the map is
            // up — creating a room loads it, and loading replaces them.
            if (keys && !ctrl && Pressed(Keys.N)) { OpenNewRoomPicker(); return; }
            if (keys && !ctrl && Pressed(Keys.I)) { OpenImportPicker(); return; }

            var mouse = new Point(_mouseNow.X, _mouseNow.Y);

            // The board is a world surface, so it answers the mouse only when
            // ImGui has declined it — the board runs under the menu bar and the
            // status bar, and a click on either must not also grab a room.
            // A drag already under way overrides that; see ChromeInputRouter.
            bool overBoard = MapBoardRect.Contains(mouse) && _router.MouseReachesWorld;

            int wheel = _mouseNow.ScrollWheelValue - _mousePrev.ScrollWheelValue;
            if (wheel != 0 && overBoard)
            {
                _mapView.StepZoom(Math.Sign(wheel), mouse, _mapContentBounds);
                _state.Status = MapStatusLine();
            }

            // Arrow keys nudge the view, matching room mode's arrow panning.
            // One room-width per press, so it moves by something meaningful at
            // any zoom rather than by a screen pixel.
            int nudge = WorldMap.RoomWidth / 2;
            if (keys && Pressed(Keys.Left))  PanMap(-nudge, 0);
            if (keys && Pressed(Keys.Right)) PanMap(nudge, 0);
            if (keys && Pressed(Keys.Up))    PanMap(0, -nudge);
            if (keys && Pressed(Keys.Down))  PanMap(0, nudge);

            HandleMapDrag(mouse, overBoard);
            PumpMapThumbnails();
        }

        private void PanMap(int dx, int dy)
        {
            _mapView.Pan += new Vector2(dx, dy);
            _mapView.ClampPan(_mapContentBounds);
        }

        /// <summary>
        /// Middle-drag pans. Left-press arms a click; travel past the slop
        /// turns it into a drag — of the room under the press, or of the board
        /// if there wasn't one — and a release inside the slop over a room
        /// opens that room.
        /// </summary>
        private void HandleMapDrag(Point mouse, bool overBoard)
        {
            bool midDown = _mouseNow.MiddleButton == ButtonState.Pressed;
            bool midWas = _mousePrev.MiddleButton == ButtonState.Pressed;

            if (midDown && !midWas && overBoard) StartMapPress(mouse, out _mapMidDown);
            if (!midDown) _mapMidDown = false;

            if (LeftClicked() && overBoard)
            {
                StartMapPress(mouse, out _mapLeftDown);
                _mapPressRoom = WorldMap.RoomAt(_mapRooms, _mapView.ScreenToMap(mouse));
                if (_mapPressRoom != null) _mapPressRoomPos = _mapPressRoom.Position;
            }

            if (!(_mapLeftDown || _mapMidDown))
            {
                if (LeftReleased()) _mapPressRoom = null;
                return;
            }

            int travelX = mouse.X - _mapPressScreen.X;
            int travelY = mouse.Y - _mapPressScreen.Y;
            if (Math.Abs(travelX) > MapClickSlop || Math.Abs(travelY) > MapClickSlop)
                _mapPressMoved = true;

            // Everything below offsets from the state recorded AT PRESS rather
            // than accumulating per-frame deltas: a per-frame delta rounds to
            // map units every frame and the error piles up, so a slow drag out
            // and back would not return to where it began.
            var delta = _mapView.ScreenDeltaToMap(new Point(travelX, travelY));

            if (_mapLeftDown && _mapPressMoved && _mapPressRoom != null)
            {
                // Dragging a room. Rounded to whole map units so the number
                // that reaches worldmap.json is one a human can read, and so a
                // drag at 6% zoom doesn't write sixteen-decimal noise.
                _mapPressRoom.Position = new Vector2(
                    MathF.Round(_mapPressRoomPos.X + delta.X),
                    MathF.Round(_mapPressRoomPos.Y + delta.Y));
                _mapContentBounds = WorldMap.ContentBounds(_mapRooms);
            }
            else if (_mapMidDown || _mapPressMoved)
            {
                _mapView.Pan = _mapPressPan - delta;
                _mapView.ClampPan(_mapContentBounds);
            }

            if (_mapLeftDown && LeftReleased())
            {
                _mapLeftDown = false;
                if (_mapPressMoved && _mapPressRoom != null) CommitRoomDrag(_mapPressRoom);
                else if (!_mapPressMoved && _mapPressRoom != null) OpenRoomFromMap(_mapPressRoom);
                _mapPressRoom = null;
            }
        }

        /// <summary>
        /// A finished room drag: record the position and mark the arrangement
        /// unsaved.
        /// </summary>
        // Committed on RELEASE, not per frame. A drag abandoned by leaving map
        // mode mid-gesture therefore records nothing, and the next entry's
        // RebuildMapBoard puts the room back where the file (or the auto
        // layout) says it belongs.
        private void CommitRoomDrag(MapRoom room)
        {
            room.Arranged = true;
            _mapStored[room.RoomId] = room.Position;
            _state.MapDirty = true;
            _discardArmed = false;         // new edits re-arm the discard guard

            // The arrows are anchored to the boxes, so they have to be rebuilt
            // around the room's new position. Cheap — it is arithmetic over the
            // door table already in hand, not a reload.
            _mapEdges = WorldMap.BuildEdges(_mapRooms, _lastDoorTable, _state.DoorStatus);
            _mapContentBounds = WorldMap.ContentBounds(_mapRooms);

            _state.Status = $"Moved {room.RoomId} to ({(int)room.Position.X}, {(int)room.Position.Y}). " +
                            $"Ctrl+S saves the arrangement to {WorldMapFile.FileName}.";
        }

        private void StartMapPress(Point mouse, out bool flag)
        {
            flag = true;
            _mapPressScreen = mouse;
            _mapPressPan = _mapView.Pan;
            _mapPressMoved = false;
        }

        /// <summary>
        /// Switch to the room view and load the clicked room — through the same
        /// discard guard Prev/Next uses.
        /// </summary>
        // PR 1's guarantee is that no path silently throws away unsaved work,
        // and a click on the map is now one of the paths that loads a room. It
        // gets the identical treatment: the first click warns and stays on the
        // map, the second goes through.
        private void OpenRoomFromMap(MapRoom room)
        {
            if (!ConfirmDiscardUnsavedEdits()) return;

            int index = -1;
            for (int i = 0; i < RoomMeta.All.Count; i++)
                if (RoomMeta.All[i].RoomId == room.RoomId) { index = i; break; }

            if (index < 0)
            {
                // The registry was reloaded under the board (a room created and
                // the map not rebuilt). Rebuilding is the honest recovery.
                RebuildMapBoard();
                _state.Status = $"Map: '{room.RoomId}' is no longer in the registry — board refreshed.";
                return;
            }

            _mapMode = false;
            LoadRoom(index);
            _state.Status = $"Opened {room.DisplayName} ({room.RoomId}) from the map. Tab returns to it.";
        }

        // --------------------------------------------------------------------
        // THUMBNAILS
        // --------------------------------------------------------------------

        /// <summary>
        /// Load at most a couple of on-screen thumbnails per frame, from
        /// Update rather than Draw.
        /// </summary>
        // Two reasons it is here and not in the draw loop: creating textures
        // in the middle of an open SpriteBatch is the sort of thing that works
        // until it doesn't, and seventy-five decodes in the frame the map opens
        // would be a visible stall. Off-screen rooms are never loaded at all —
        // pan to a room and it appears.
        private void PumpMapThumbnails()
        {
            const int budgetPerFrame = 2;
            int loaded = 0;

            foreach (var room in _mapRooms)
            {
                if (loaded >= budgetPerFrame) return;
                if (string.IsNullOrEmpty(room.BackgroundAsset)) continue;
                if (_mapThumbs.ContainsKey(room.BackgroundAsset)) continue;
                if (!_mapView.MapRectToScreen(room.Box).Intersects(MapBoardRect)) continue;

                LoadMapThumbnail(room.BackgroundAsset);
                loaded++;
            }
        }

        private void LoadMapThumbnail(string asset)
        {
            Texture2D? tex = null;
            string path = Path.Combine(EditorPaths.RepoContentDir, asset + ".png");
            if (File.Exists(path))
            {
                try
                {
                    using var fs = File.OpenRead(path);
                    tex = Texture2D.FromStream(GraphicsDevice, fs);
                }
                catch { tex = null; }
            }
            // Cached even when null: "there is no readable PNG here" is an
            // answer, and re-asking it sixty times a second is not.
            _mapThumbs[asset] = tex;
        }

        /// <summary>
        /// Drop a cached thumbnail so the next map entry re-reads it. Called
        /// after Erase-mode saves the room's PNG, which is the one way the file
        /// changes while the editor is running.
        /// </summary>
        private void InvalidateMapThumbnail(string asset)
        {
            if (!_mapThumbs.TryGetValue(asset, out var tex)) return;
            tex?.Dispose();
            _mapThumbs.Remove(asset);
        }

        // --------------------------------------------------------------------
        // MAP DRAW
        // --------------------------------------------------------------------

        private void DrawMapMode()
        {
            _mapView.Viewport = MapBoardRect;

            // The board and nothing else. The menu bar above it and the status
            // bar below it are ImGui windows now, painted after every
            // SpriteBatch pass — so the scissor here does the same job it
            // always did (cut a box panned half off the board at the band's
            // edge) and the bands themselves are no longer this method's
            // business.
            GraphicsDevice.ScissorRectangle = MapBoardRect;
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp, rasterizerState: ScissorOn);
            DrawMapBoard();
            _spriteBatch.End();
        }

        private void DrawMapBoard()
        {
            FillRect(MapBoardRect, new Color(20, 22, 28));

            // Arrows first: a box then covers the stub of any arrow that starts
            // underneath it, and the lines read as passing behind the rooms.
            foreach (var edge in _mapEdges) DrawMapEdge(edge);
            foreach (var room in _mapRooms) DrawMapRoomBox(room);
        }

        private void DrawMapRoomBox(MapRoom room)
        {
            var dest = _mapView.MapRectToScreen(room.Box);
            if (!dest.Intersects(MapBoardRect)) return;

            var thumb = string.IsNullOrEmpty(room.BackgroundAsset) ? null
                      : _mapThumbs.TryGetValue(room.BackgroundAsset, out var t) ? t : null;

            if (thumb != null)
            {
                _spriteBatch.Draw(thumb, dest, Color.White);
            }
            else
            {
                // Either not loaded yet or there is no readable PNG. Both look
                // the same on purpose: an empty slate that says "a room is
                // here" without pretending to know what it looks like.
                FillRect(dest, new Color(44, 48, 60));
            }

            bool isCurrent = room.RoomId == _state.CurrentRoom.RoomId;
            bool hover = dest.Contains(_mouseNow.X, _mouseNow.Y)
                         && MapBoardRect.Contains(_mouseNow.X, _mouseNow.Y);

            Color border = isCurrent ? new Color(255, 220, 60)
                         : hover     ? new Color(200, 215, 255)
                                     : new Color(90, 100, 130);
            DrawRectOutline(InflateRect(dest, 1), border);
            if (isCurrent) DrawRectOutline(InflateRect(dest, 2), border);

            // The label is the room ID — the persistence key, the thing door
            // targets name, and what you actually need when wiring a world.
            // Skipped when the box is too small to read one, rather than
            // stacking unreadable text at every zoom level.
            if (dest.Width < 56) return;
            string label = TruncateText(room.RoomId, dest.Width);
            var size = MeasureText(label);
            DrawText(label,
                new Vector2(dest.X + (dest.Width - size.X) / 2f, dest.Bottom + 3),
                isCurrent ? new Color(255, 235, 150) : new Color(185, 195, 215));
        }

        private void DrawMapEdge(MapEdge edge)
        {
            var a = _mapView.MapToScreen(edge.From).ToVector2();
            var b = _mapView.MapToScreen(edge.To).ToVector2();

            // Cheap cull: skip lines whose bounding box misses the board.
            var bounds = new Rectangle(
                (int)Math.Min(a.X, b.X) - 8, (int)Math.Min(a.Y, b.Y) - 8,
                (int)Math.Abs(a.X - b.X) + 16, (int)Math.Abs(a.Y - b.Y) + 16);
            if (!bounds.Intersects(MapBoardRect)) return;

            Color color = StatusColor(edge.Status);
            DrawLine(a, b, color);

            float head = Math.Clamp(10f * _mapView.Scale * 4f, 5f, 12f);
            DrawArrowHead(b, b - a, color, head);
            // A correctly-wired pair is ONE line with a head at each end: the
            // link is bidirectional and drawing it twice would say nothing
            // extra while doubling the clutter at seventy-five rooms.
            if (edge.BothWays) DrawArrowHead(a, a - b, color, head);
        }

        /// <summary>A 1-px line between two screen points, drawn from the UI pixel.</summary>
        // The editor had no line primitive before the map — everything else is
        // axis-aligned rectangles. This is the standard rotate-and-stretch of a
        // 1x1 texture; the (0, 0.5) origin centres the stroke on the endpoints
        // so a line and its arrowheads meet exactly.
        private void DrawLine(Vector2 from, Vector2 to, Color color, float thickness = 1f)
        {
            Vector2 delta = to - from;
            float length = delta.Length();
            if (length < 0.5f) return;
            _spriteBatch.Draw(_pixel, from, null, color,
                              MathF.Atan2(delta.Y, delta.X), new Vector2(0f, 0.5f),
                              new Vector2(length, thickness), SpriteEffects.None, 0f);
        }

        /// <summary>Two short strokes at <paramref name="tip"/>, opening back along <paramref name="incoming"/>.</summary>
        private void DrawArrowHead(Vector2 tip, Vector2 incoming, Color color, float size)
        {
            float length = incoming.Length();
            if (length < 0.5f) return;
            Vector2 dir = incoming / length;

            // +/- 30 degrees off the incoming direction: narrow enough to read
            // as an arrow at a 20-pixel-wide thumbnail, wide enough to see.
            const float spread = MathF.PI / 6f;
            float baseAngle = MathF.Atan2(dir.Y, dir.X);
            DrawLine(tip, tip + Radial(baseAngle + spread, size), color);
            DrawLine(tip, tip + Radial(baseAngle - spread, size), color);
        }

        private static Vector2 Radial(float angle, float length) =>
            new(MathF.Cos(angle) * length, MathF.Sin(angle) * length);

        // Armed by the first destructive attempt (room switch, exit) while
        // background pixel, collision, or placement edits are unsaved; the
        // second attempt goes through. Disarmed by saving or by further
        // editing.
        private bool _discardArmed;

        /// <summary>
        /// Arm-then-allow guard in front of anything that discards unsaved
        /// work. <paramref name="includeMap"/> adds the world-map arrangement
        /// to what counts as unsaved.
        /// </summary>
        // Only the EXIT path passes it. The three room flags are working state
        // that LoadRoom replaces, so every room-loading caller must consult
        // them; the map arrangement lives in EditorGame and survives room
        // switches, room creation and imports untouched. Quitting is the one
        // action that loses it — so quitting is the one caller that asks. A
        // guard that blocks Prev/Next over something Prev/Next cannot destroy
        // (and that room-mode Ctrl+S cannot clear, since that saves the room)
        // would train the user to double-tap through every warning, which is
        // exactly how the class of bug PR 1 fixed gets back in.
        private bool ConfirmDiscardUnsavedEdits(bool includeMap = false)
        {
            bool roomDirty = _state.BackgroundDirty || _state.CollisionDirty || _state.PlacementsDirty;
            bool mapDirty = includeMap && _state.MapDirty;
            if (!roomDirty && !mapDirty) return true;
            if (_discardArmed) { _discardArmed = false; return true; }

            _discardArmed = true;
            _state.Status = roomDirty
                ? "Unsaved edits! Save (Ctrl+S), or repeat the action to discard."
                : "Unsaved map arrangement! Tab to the map and Ctrl+S, or repeat the action to discard.";
            return false;
        }

        /// <summary>
        /// Window-X / Alt+F4 cannot be cancelled in MonoGame DesktopGL, and
        /// double-Escape deliberately skips saving — so as a last-resort
        /// safety net, unsaved background pixels are flushed to a sidecar
        /// .autosave.png beside the asset (never the asset itself). Delete
        /// it if unwanted; rename over the original to recover.
        /// </summary>
        protected override void OnExiting(object sender, EventArgs args)
        {
            if (_state.BackgroundDirty && _bgPixels != null && _currentBackground != null
                && _state.CurrentRoom.BackgroundAsset != null)
            {
                try
                {
                    string path = Path.Combine(EditorPaths.RepoContentDir,
                        _state.CurrentRoom.BackgroundAsset + ".autosave.png");
                    using var fs = File.Create(path);
                    _currentBackground.SaveAsPng(fs, _currentBackground.Width, _currentBackground.Height);
                }
                catch { /* best effort — exit must not be blocked */ }
            }
            base.OnExiting(sender, args);
        }

        /// <summary>
        /// Free what the chrome owns. The ImGui renderer holds a font texture,
        /// two dynamic GPU buffers and an event handler on the window; the
        /// window outlives this call on some platforms, so the handler in
        /// particular has to come off deliberately.
        /// </summary>
        protected override void UnloadContent()
        {
            _imgui?.Dispose();
            base.UnloadContent();
        }

        private void CyclePrevRoom()
        {
            if (!ConfirmDiscardUnsavedEdits()) return;
            int n = RoomMeta.All.Count;
            LoadRoom((_state.CurrentRoomIndex - 1 + n) % n);
        }

        private void CycleNextRoom()
        {
            if (!ConfirmDiscardUnsavedEdits()) return;
            int n = RoomMeta.All.Count;
            LoadRoom((_state.CurrentRoomIndex + 1) % n);
        }

        private void ToggleSnap()
        {
            _state.SnapEnabled = !_state.SnapEnabled;
        }

        /// <summary>
        /// Auto-punch toggle. OFF by default: the explicit punch (P key /
        /// inspector row) is the primary workflow because it lets you align
        /// first and cut second. Turn this ON when stamping doors in bulk onto
        /// a screenshot room, where every drop wants its footprint cleared.
        /// </summary>
        private void ToggleAutoPunch()
        {
            _state.AutoPunch = !_state.AutoPunch;
            _state.Status = _state.AutoPunch
                ? "Auto-punch ON: drops and moves clear the background under the placement."
                : "Auto-punch OFF: use P (or the inspector row) to punch explicitly.";
        }

        // ====================================================================
        // UNDO / REDO
        // ====================================================================
        // EDITOR_REVIEW item 11. The stack, the commands and the four rules
        // they follow live in UndoStack.cs and EditorCommands.cs, device-free
        // and driven headlessly by tools/EditCheck. What is HERE is the two
        // ways an edit gets recorded, and the two entry points Ctrl+Z / Ctrl+Y
        // reach.
        //
        // MAP ARRANGEMENT IS OUT OF SCOPE, deliberately. Dragging a room on the
        // board is not per-room working state — it survives every room switch,
        // which is the event that clears this stack — and it has its own Ctrl+S
        // in its own mode. Folding it in would mean a stack whose entries some
        // clears apply to and some do not. Recorded in doc/07.
        // ====================================================================

        /// <summary>Run a discrete edit AS a command, and record it.</summary>
        // The preferred form: the command's Do IS the edit, so redo cannot
        // drift from what the original click did.
        private void ExecuteCommand(IEditorCommand command)
        {
            _undo.Execute(command, _cmd);
            _discardArmed = false;   // an edit re-arms the discard guard
        }

        /// <summary>Record an edit that has already happened.</summary>
        // For the gestures that are incremental by nature — a drag, a brush
        // stroke, a paint swipe — where the "after" is only known once the
        // button comes up. See UndoStack.PushApplied.
        private void RecordCommand(IEditorCommand command)
        {
            _undo.PushApplied(command);
            _discardArmed = false;
        }

        /// <summary>
        /// Close anything the mouse is still in the middle of, so a half-done
        /// gesture cannot merge into the entry undo is about to pop.
        /// </summary>
        // The generalisation of a rule the old background-only Ctrl+Z already
        // had ("close any in-progress stroke first"). It now has three kinds of
        // gesture to close, and forgetting one of them would mean an undo that
        // pops a command while the mouse is still writing to the same state.
        private void CloseOpenGestures()
        {
            EndStroke();
            EndPaintStroke();
            EndPlacementDrag();
        }

        /// <summary>Ctrl+Z. One path for every kind of edit.</summary>
        private void UndoLastEdit()
        {
            CloseOpenGestures();
            string? label = _undo.Undo(_cmd);
            _discardArmed = false;
            _state.Status = label == null
                ? "Nothing to undo."
                : $"Undid: {label} ({_undo.UndoDepth} more, Ctrl+Y redoes)";
        }

        /// <summary>Ctrl+Y (and Ctrl+Shift+Z).</summary>
        private void RedoLastEdit()
        {
            CloseOpenGestures();
            string? label = _undo.Redo(_cmd);
            _discardArmed = false;
            _state.Status = label == null
                ? "Nothing to redo."
                : $"Redid: {label} ({_undo.RedoDepth} more)";
        }

        // ====================================================================
        // UPDATE — INPUT
        // ====================================================================

        protected override void Update(GameTime gameTime)
        {
            _keysPrev = _keysNow;
            _keysNow = Keyboard.GetState();
            _mousePrev = _mouseNow;
            _mouseNow = Mouse.GetState();

            // ---- the ImGui frame opens here ---------------------------------
            // The chrome is built inside Update, not Draw, so that every editor
            // state mutation still happens in one place. A menu item's callback
            // IS a mutation — it loads rooms, arms the discard guard, writes
            // files — and half the editor's writes drifting into the render
            // pass is exactly the kind of split this PR exists to prevent.
            // ImGuiRenderer's header spells out the frame shape.
            _imgui.BeginFrame(gameTime);

            // Read the capture verdict immediately: it is computed during
            // NewFrame and is what decides, below, whether the canvas and the
            // map board see this frame's mouse at all.
            var io = ImGui.GetIO();
            _router.WorldGestureInProgress = WorldGestureInProgress();
            _router.Sample(io.WantCaptureMouse, io.WantCaptureKeyboard,
                ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel),
                io.WantTextInput);

            UpdateEditor();

            // After the editor's own handling, so the chrome renders the state
            // this frame just produced — the status line especially.
            BuildChrome();
            _imgui.EndFrame();

            // After the frame closes: a back-buffer resize mid-frame would
            // leave the chrome drawn against a stale projection for one frame.
            ApplyPendingFullscreenToggle();

            base.Update(gameTime);
        }

        /// <summary>
        /// True while a gesture that STARTED on a world surface (room canvas,
        /// map board, crop image) is still running. Such a gesture keeps the
        /// mouse until it ends, wherever the cursor wanders — see the header of
        /// UI/ChromeInputRouter.cs for why that override is not optional.
        /// </summary>
        private bool WorldGestureInProgress()
        {
            if (_cropOpen) return _cropDragging;
            if (_mapMode) return _mapLeftDown || _mapMidDown;
            // _paintStroke joins the list for the same reason _strokeActive is
            // on it: a paint drag now has an END, and the command it records is
            // committed by the release. Drag toward the room edge and the
            // cursor routinely lands on a panel before the button comes up —
            // gate that release on ImGui alone and the stroke stays open, and
            // the next one merges into it.
            return _state.IsMovingSelection || _state.IsMovingSpawn || _panning
                || _strokeActive || _paintStroke.Count > 0;
        }

        /// <summary>Everything Update did before the chrome moved to ImGui.</summary>
        // Split out so the early returns below end the EDITOR's frame without
        // also skipping the chrome: a modal picker still has to be drawn, and
        // the status bar still has to say what just happened.
        private void UpdateEditor()
        {
            // The New Room and Import pickers are modal: while one is open it
            // consumes every input, so a click meant for a candidate row can't
            // fall through to the canvas behind it and Escape closes the picker
            // rather than the editor. At most one is ever open — the top-bar
            // buttons that open them are themselves unreachable from here.
            // A running batch outranks everything, pickers included: it is
            // writing files, and one file's worth of work happens per frame.
            // Its own handler owns Escape (stop after the current file).
            //
            // NONE of the three modal handlers is gated on the ImGui router.
            // They were already defined as consuming every input, and their
            // cancel gestures (Escape, right-click) have to keep working over
            // the panel itself — which is the one place the cursor is bound to
            // be. ImGui capture cannot be allowed to change what "every input"
            // means; the router gates the WORLD surfaces (canvas, map board,
            // crop image), and nothing else.
            if (_batchRunning)
            {
                StepBatchImport();
                return;
            }

            if (_newRoomOpen)
            {
                HandleNewRoomPicker();
                return;
            }

            if (_importOpen)
            {
                HandleImportPicker();
                return;
            }

            // The crop step is the same kind of modal, reached from the import
            // picker rather than from a button. Nothing has been written to
            // disk while it is open, so its Escape is a plain cancel.
            if (_cropOpen)
            {
                HandleCropOverlay();
                return;
            }

            // Tab flips between the room editor and the world map, from either
            // side. Handled before both, and returning immediately, so the
            // press that changed mode is not also read by the mode it landed
            // in.
            if (_router.KeyboardReachesEditor && Pressed(Keys.Tab))
            {
                ToggleMapMode();
                return;
            }

            // Map mode suspends room editing entirely: no palette, no canvas,
            // no paint, no punch, no room keyboard shortcuts. Its own handler
            // is the only thing that runs.
            if (_mapMode)
            {
                HandleMapInput();
                return;
            }

            // Escape exits, but never silently throws away unsaved edits:
            // the first press arms the discard guard (status bar warning),
            // the second confirms. (In map mode Escape returns to the room
            // view instead — see HandleMapInput. The exit path is reachable
            // from room view exactly as it always was.)
            //
            // includeMap: quitting is the one action that loses an unsaved
            // board arrangement, so it is the one that has to ask about it.
            if (_router.KeyboardReachesEditor
                && Pressed(Keys.Escape) && ConfirmDiscardUnsavedEdits(includeMap: true)) Exit();

            // Right-click cancels a palette drag from ANYWHERE — over the
            // palette, over the inspector, over the canvas margin. UNGATED, for
            // the same reason the modal pickers' cancels are: the cursor is
            // most often over a chrome panel when you change your mind, and
            // ImGui captures the mouse there. Routing it would quietly shrink
            // "anywhere" to "over the canvas", which is exactly where you are
            // least likely to be.
            if (_state.Mode == EditorMode.Place && _state.Dragging != null && RightClicked())
            {
                _state.Dragging = null;
                _state.Status = "Drag cancelled.";
            }

            // Inspector clicks used to be tested here and, on a hit, to swallow
            // the frame's canvas handling so that clicking a cycle button could
            // not also deselect the entity it was editing. The router does that
            // now, and does it better: the inspector is an ImGui window, so a
            // click anywhere in it makes ImGui claim the mouse and this branch
            // never runs at all — no hand-maintained priority list, and no
            // one-frame-stale rectangles to test against.
            if (_router.MouseReachesWorld)
            {
                HandleCanvasView();
                HandleCanvasInput();
            }
            if (_router.KeyboardReachesEditor) HandleKeyboardShortcuts();
        }

        private bool LeftClicked() =>
            _mouseNow.LeftButton == ButtonState.Pressed &&
            _mousePrev.LeftButton == ButtonState.Released;

        private bool RightClicked() =>
            _mouseNow.RightButton == ButtonState.Pressed &&
            _mousePrev.RightButton == ButtonState.Released;

        private bool LeftHeld() => _mouseNow.LeftButton == ButtonState.Pressed;

        private bool LeftReleased() =>
            _mouseNow.LeftButton == ButtonState.Released &&
            _mousePrev.LeftButton == ButtonState.Pressed;


        /// <summary>
        /// Canvas view navigation, active in every mode: mouse-wheel zooms
        /// in/out anchored at the cursor; middle-drag pans while zoomed.
        /// </summary>
        private void HandleCanvasView()
        {
            var pt = new Point(_mouseNow.X, _mouseNow.Y);

            // Wheel zoom. Two things keep this from fighting the panels for a
            // notch: the canvas containment test below, and — since the panels
            // became ImGui windows — the router, which never lets this method
            // run at all on a frame ImGui claimed the mouse.
            if (EditorLayout.IsInsideCanvas(pt))
            {
                int delta = _mouseNow.ScrollWheelValue - _mousePrev.ScrollWheelValue;
                if (delta != 0)
                {
                    EditorLayout.StepZoom(Math.Sign(delta), pt);
                    // Zooming mid-pan: rebase the drag gesture on the
                    // post-zoom view, or the pan branch below would
                    // overwrite the anchored pan with stale start state
                    // rescaled by the new EffScale.
                    if (_panning)
                    {
                        _panStartMouse = pt;
                        _panStartPan = new Point(EditorLayout.PanX, EditorLayout.PanY);
                    }
                    _state.Status = $"Zoom {EditorLayout.Zoom}x";
                }
            }

            // Middle-drag pan.
            bool midDown = _mouseNow.MiddleButton == ButtonState.Pressed;
            bool midWas  = _mousePrev.MiddleButton == ButtonState.Pressed;
            if (midDown && !midWas && EditorLayout.IsInsideCanvas(pt))
            {
                _panning = true;
                _panStartMouse = pt;
                _panStartPan = new Point(EditorLayout.PanX, EditorLayout.PanY);
            }
            if (!midDown) _panning = false;
            if (_panning)
            {
                int dx = (int)Math.Round((_panStartMouse.X - pt.X) / (float)EditorLayout.EffScale);
                int dy = (int)Math.Round((_panStartMouse.Y - pt.Y) / (float)EditorLayout.EffScale);
                EditorLayout.SetPan(_panStartPan.X + dx, _panStartPan.Y + dy);
            }
        }

        private void HandleCanvasInput()
        {
            var screenPt = new Point(_mouseNow.X, _mouseNow.Y);

            if (_state.Mode == EditorMode.Paint)
            {
                HandlePaintInput(screenPt);
                return;
            }

            if (_state.Mode == EditorMode.Erase)
            {
                HandleEraseInput(screenPt);
                return;
            }

            // The right-click drag-cancel used to live here. It moved up into
            // UpdateEditor, ungated by the router: it has always worked from
            // anywhere on screen, and anywhere on screen is now mostly ImGui.

            if (!EditorLayout.IsInsideCanvas(screenPt))
            {
                // Outside the canvas: end any in-progress move. Dragging a
                // placement to a room edge routinely takes the cursor past the
                // canvas before the button comes up, so auto-punch has to fire
                // here too — otherwise whether the move got punched would
                // depend on where the pointer happened to be on release.
                if (LeftReleased() && _state.IsMovingSelection)
                {
                    _state.IsMovingSelection = false;
                    if (_state.SelectedPlacement != null) CommitMove(_state.SelectedPlacement);
                }
                // Same for the spawn marker — it has no footprint to punch,
                // so recording the move is all there is to do.
                if (LeftReleased() && _state.IsMovingSpawn)
                {
                    _state.IsMovingSpawn = false;
                    CommitSpawnMove();
                }
                return;
            }

            Vector2 game = EditorLayout.ScreenToGame(screenPt);

            // Drop a palette drag onto the canvas. The ghost previews the
            // 24x24 sprite centred on the cursor, so anchor the drop the
            // same way: offset to the top-left before snapping.
            if (LeftClicked() && _state.Dragging != null)
            {
                DropDraggingAt(SnapIfNeeded(game - new Vector2(12f, 12f)));
                return;
            }

            // Start a move on an existing placement.
            if (LeftClicked())
            {
                _state.SelectedPlacement = HitTestPlacements(game);
                if (_state.SelectedPlacement != null)
                {
                    _state.SpawnSelected = false;
                    _state.IsMovingSpawn = false;
                    _state.IsMovingSelection = true;
                    _state.MoveOffset = _state.SelectedPlacement.Position - game;
                    // Captured at the press, because the release is the only
                    // moment that knows where the drag ended — and by then the
                    // original position has been overwritten sixty times.
                    _moveFrom = _state.SelectedPlacement.Position;
                    // Expand the inspector section for the freshly-selected
                    // placement so its attributes are visible immediately.
                    _state.Expand(_state.SelectedPlacement.Id);
                    _state.Status = $"Moving {_state.SelectedPlacement.DisplayName} ({_state.SelectedPlacement.Id})";
                }
                // Placements win the click; the spawn marker is only tested
                // when none was hit. A spawn buried under an entity is
                // therefore not grabbable — but it is never stranded either,
                // because dropping the palette's Player Spawn entry again
                // MOVES the existing point. An entity buried under the spawn
                // would have no such escape hatch, which is why the priority
                // runs this way round and not the other.
                else if (HitTestSpawn(game))
                {
                    _state.SpawnSelected = true;
                    _state.IsMovingSpawn = true;
                    _state.SpawnMoveOffset = _state.PlayerSpawn!.Value - game;
                    _spawnMoveFrom = _state.PlayerSpawn.Value;
                    _state.Status = $"Moving player spawn ({(int)_state.PlayerSpawn.Value.X}, {(int)_state.PlayerSpawn.Value.Y}) — Delete clears it.";
                }
                else
                {
                    _state.SpawnSelected = false;
                    _state.Status = "No placement under cursor.";
                }
                return;
            }

            // Continue a spawn-marker move while the mouse is held. Checked
            // before the placement branch below because the two are mutually
            // exclusive and this one is the cheaper test.
            if (LeftHeld() && _state.IsMovingSpawn && _state.PlayerSpawn.HasValue)
            {
                Vector2 before = _state.PlayerSpawn.Value;
                Vector2 moved = ClampPointToRoom(SnapIfNeeded(game + _state.SpawnMoveOffset));
                _state.PlayerSpawn = moved;
                if (moved != before)
                {
                    _state.HasValidated = false;   // the flood-fill origin moved
                    _state.PlacementsDirty = true;
                    _discardArmed = false;         // new edits re-arm the discard guard
                }
                return;
            }

            // Continue a move while the mouse is held.
            if (LeftHeld() && _state.IsMovingSelection && _state.SelectedPlacement != null)
            {
                Vector2 before = _state.SelectedPlacement.Position;
                Vector2 newPos = game + _state.MoveOffset;
                _state.SelectedPlacement.Position = SnapIfNeeded(newPos);
                ClampToRoom(_state.SelectedPlacement);
                _state.HasValidated = false;
                // Only an actual displacement counts as an edit. This branch
                // also runs while the button is merely held down after a
                // select-click, and marking dirty there would arm the discard
                // guard for a click that changed nothing.
                if (_state.SelectedPlacement.Position != before)
                {
                    _state.PlacementsDirty = true;
                    _discardArmed = false;  // new edits re-arm the discard guard
                }
                return;
            }

            if (LeftReleased() && _state.IsMovingSpawn)
            {
                _state.IsMovingSpawn = false;
                CommitSpawnMove();
                if (_state.PlayerSpawn.HasValue)
                    _state.Status = $"Player spawn at ({(int)_state.PlayerSpawn.Value.X}, {(int)_state.PlayerSpawn.Value.Y})";
                return;
            }

            if (LeftReleased() && _state.IsMovingSelection)
            {
                _state.IsMovingSelection = false;
                if (_state.SelectedPlacement != null)
                {
                    _state.Status = $"Placed at ({(int)_state.SelectedPlacement.Position.X}, {(int)_state.SelectedPlacement.Position.Y})";
                    CommitMove(_state.SelectedPlacement);
                }
            }
        }

        /// <summary>
        /// A finished placement drag: record it as one command, together with
        /// the auto-punch it triggers, and leave nothing behind if the
        /// placement did not actually move.
        /// </summary>
        // Auto-punch re-cuts at the FINAL position once the drag ends (not
        // per-frame while dragging, which would smear a trench along the whole
        // path). The hole left at the drop position stays: the background there
        // was due for clearing anyway, and cutting more than needed is
        // harmless. If it isn't, Erase mode's right-drag restores from the last
        // saved state.
        //
        // Both halves go into ONE composite, so one release costs one Ctrl+Z.
        private void CommitMove(Placement p)
        {
            bool moved = p.Position != _moveFrom;
            IEditorCommand? punch = moved && _state.AutoPunch ? PunchBackgroundCore(p) : null;

            if (!moved)
            {
                // A click that selected without dragging. Nothing changed, so
                // nothing is recorded — the same rule that keeps a no-op brush
                // stroke and an already-clear punch off the stack.
                return;
            }

            var move = new MovePlacementCommand(p, _moveFrom, p.Position);
            RecordCommand(punch == null
                ? move
                : new CompositeCommand(move.Label, move, punch));
        }

        /// <summary>A finished spawn-marker drag.</summary>
        private void CommitSpawnMove()
        {
            if (!_state.PlayerSpawn.HasValue) return;
            if (_state.PlayerSpawn.Value == _spawnMoveFrom) return;
            RecordCommand(SetPlayerSpawnCommand.Move(_spawnMoveFrom, _state.PlayerSpawn.Value));
        }

        // --- PAINT MODE ----------------------------------------------------

        /// <summary>
        /// In paint mode, left-click sets the tile under the cursor solid;
        /// right-click clears it. Holding either button continues the brush
        /// across the grid (so you can drag-paint walls and floors).
        /// </summary>
        private void HandlePaintInput(Point screenPt)
        {
            if (_state.CollisionMap == null) return;

            bool drawSolid = _mouseNow.LeftButton == ButtonState.Pressed;
            bool drawEmpty = _mouseNow.RightButton == ButtonState.Pressed;

            // Both buttons up ends the drag, wherever the cursor is — so the
            // command is committed even when the release happens off the canvas
            // or over a panel. This runs BEFORE the inside-canvas test for that
            // reason: gating it on the canvas would leave a stroke open until
            // the next one started and fold the two together.
            if (!drawSolid && !drawEmpty) { EndPaintStroke(); return; }

            if (!EditorLayout.IsInsideCanvas(screenPt)) return;

            Vector2 game = EditorLayout.ScreenToGame(screenPt);
            int tx = (int)(game.X / TileConfig.TILE_SIZE);
            int ty = (int)(game.Y / TileConfig.TILE_SIZE);
            if (tx < 0 || ty < 0 || tx >= _state.CollisionMap.Width || ty >= _state.CollisionMap.Height)
                return;

            int desired = drawSolid ? TileConfig.WALL_DARK_GRAY : TileConfig.EMPTY;
            int had = _state.CollisionMap.GetTile(tx, ty);
            if (had == desired) return;

            // Only cells that actually changed join the stroke, which is what
            // makes re-crossing a cell free and a no-op drag record nothing.
            _paintStroke.Add(new TileEdit(tx, ty, had, desired));

            _state.CollisionMap.SetTile(tx, ty, desired);
            _state.CollisionDirty = true;
            _discardArmed = false;         // new edits re-arm the discard guard
            _state.HasValidated = false;   // collision changed, old result is stale
            _state.Status = drawSolid
                ? $"Paint solid at tile ({tx}, {ty})"
                : $"Erase tile ({tx}, {ty})";
        }

        // --- ERASE MODE (background pixel brush) ----------------------------

        /// <summary>
        /// GIMP-style background eraser. Left-drag clears pixels to
        /// transparent; right-drag paints them back from the last-saved
        /// state. The brush is a BrushSize-square stamp, stamped along the
        /// cursor's path (Bresenham) so fast drags leave no gaps. One undo
        /// snapshot is pushed per stroke (Ctrl+Z).
        /// </summary>
        private void HandleEraseInput(Point screenPt)
        {
            if (_bgPixels == null || _bgOriginal == null || _currentBackground == null)
                return;

            bool erase   = _mouseNow.LeftButton == ButtonState.Pressed;
            bool restore = !erase && _mouseNow.RightButton == ButtonState.Pressed;

            if (!erase && !restore) { EndStroke(); return; }

            // A stroke must START inside the canvas, but may continue past
            // its edge (stamps clamp to the image bounds).
            if (!_strokeActive && !EditorLayout.IsInsideCanvas(screenPt)) return;

            Vector2 game = EditorLayout.ScreenToGame(screenPt);
            var here = new Point((int)Math.Floor(game.X), (int)Math.Floor(game.Y));
            var view = (EditorLayout.Zoom, EditorLayout.PanX, EditorLayout.PanY);

            if (!_strokeActive)
            {
                // A transient full-image clone, NOT a retained snapshot: it is
                // diffed against the finished stroke in EndStroke and the
                // command keeps only the rectangle that changed.
                _strokeBefore = (Color[])_bgPixels.Clone();
                _strokeActive = true;
                _strokeChanged = false;
                _discardArmed = false;   // new edits re-arm the discard guard
                _lastStamp = here;
                _strokeView = view;
            }

            // A view change (wheel zoom, middle-drag/arrow pan) moves the
            // under-cursor room point without the user dragging — never
            // Bresenham across that jump, and don't stamp while panning.
            if (_panning || view != _strokeView)
            {
                _strokeView = view;
                _lastStamp = here;
                return;
            }

            bool changed = StampLine(_lastStamp, here, erase);
            _lastStamp = here;

            if (changed)
            {
                _strokeChanged = true;
                _currentBackground.SetData(_bgPixels);
                _state.BackgroundDirty = true;
                _state.Status =
                    $"{(erase ? "Erasing" : "Restoring")} at ({here.X}, {here.Y}) — brush {_state.BrushSize}px, Ctrl+Z undo, Save writes PNG.";
            }
        }

        /// <summary>
        /// Close the current stroke and record it as ONE undo command. A stroke
        /// that changed nothing records nothing, so no-op clicks can't evict
        /// real history off the end of the stack.
        /// </summary>
        // The no-op rule is now enforced twice over, and both are worth having:
        // _strokeChanged says "no stamp reported a write", and
        // BackgroundEditCommand.FromDiff returns null when the two images are
        // identical regardless. The second catches the case the first cannot —
        // a stroke that erased pixels and then restored exactly those pixels
        // from the right-drag brush before letting go.
        private void EndStroke()
        {
            if (_strokeActive && _strokeChanged && _strokeBefore != null
                && _bgPixels != null && _currentBackground != null)
            {
                var command = BackgroundEditCommand.FromDiff(
                    _strokeBefore, _bgPixels,
                    _currentBackground.Width, _currentBackground.Height,
                    "background stroke");
                if (command != null) RecordCommand(command);
            }

            _strokeActive = false;
            _strokeChanged = false;
            _strokeBefore = null;   // never retained past the stroke that took it
        }

        /// <summary>
        /// Close the paint drag under the mouse and record every cell it
        /// changed as ONE command. A drag that changed nothing records nothing.
        /// </summary>
        private void EndPaintStroke()
        {
            if (_paintStroke.Count > 0) RecordCommand(new PaintTilesCommand(_paintStroke));
            _paintStroke.Clear();
        }

        /// <summary>
        /// End a placement or spawn drag without recording anything.
        /// </summary>
        // Called only from CloseOpenGestures, i.e. from undo/redo. The normal
        // end of a drag is the release handler in HandleCanvasInput, which
        // records the move; this is the abnormal one, where the user pressed
        // Ctrl+Z with the button still down. Recording a half-finished drag
        // there would push a command the user never completed, on top of the
        // one they are asking to take back.
        private void EndPlacementDrag()
        {
            _state.IsMovingSelection = false;
            _state.IsMovingSpawn = false;
        }

        /// <summary>Stamp the brush along the line from..to (Bresenham).</summary>
        private bool StampLine(Point from, Point to, bool erase)
        {
            bool changed = false;
            int dx = Math.Abs(to.X - from.X), sx = from.X < to.X ? 1 : -1;
            int dy = -Math.Abs(to.Y - from.Y), sy = from.Y < to.Y ? 1 : -1;
            int err = dx + dy;
            int x = from.X, y = from.Y;
            while (true)
            {
                changed |= StampBrush(x, y, erase);
                if (x == to.X && y == to.Y) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x += sx; }
                if (e2 <= dx) { err += dx; y += sy; }
            }
            return changed;
        }

        /// <summary>
        /// Apply the square brush centred on room pixel (cx, cy). Erase sets
        /// (0,0,0,0); restore copies from _bgOriginal. Returns true if any
        /// pixel changed.
        /// </summary>
        private bool StampBrush(int cx, int cy, bool erase)
        {
            int size = _state.BrushSize;
            int x0 = cx - size / 2, y0 = cy - size / 2;
            int w = _currentBackground!.Width, h = _currentBackground.Height;
            bool changed = false;
            for (int y = Math.Max(0, y0); y < Math.Min(h, y0 + size); y++)
            for (int x = Math.Max(0, x0); x < Math.Min(w, x0 + size); x++)
            {
                int i = y * w + x;
                Color want = erase ? Color.Transparent : _bgOriginal![i];
                if (_bgPixels![i] != want) { _bgPixels[i] = want; changed = true; }
            }
            return changed;
        }

        // --- PUNCH-OUT (clear the background under a placement) -------------

        /// <summary>
        /// The P key and the inspector's Background row: punch, and record it
        /// as its own undo entry.
        /// </summary>
        private void PunchBackground(Placement p)
        {
            var command = PunchBackgroundCore(p);
            if (command != null) RecordCommand(command);
        }

        /// <summary>
        /// Clear the background pixels under the given placement's 24x24
        /// footprint to transparent, and return the command that describes it —
        /// WITHOUT recording it. Returns null when nothing was punched.
        ///
        /// Why this exists: rooms are built from screenshots of the original
        /// game, which still contain its baked-in artwork (doors especially).
        /// Once a real entity is placed over such a spot, those pixels would
        /// bleed through the entity's animation frames — so they get cut out.
        /// Transparent renders as black in-game, which is what we want here.
        /// </summary>
        // It returns the command rather than pushing it because AUTO-PUNCH runs
        // as part of another action: dropping a placement, or ending a move.
        // Those callers compose the punch with their own command so that one
        // click costs one Ctrl+Z — otherwise the first undo would fill the hole
        // back in and leave the door standing in it, which is a state the user
        // never asked for and cannot reach any other way.
        private IEditorCommand? PunchBackgroundCore(Placement p)
        {
            // Same guard HandleEraseInput uses: no raw PNG behind this room
            // means there are no pixels we're allowed to edit (the XNB
            // fallback is display-only and Save would have nothing to write).
            if (_bgPixels == null || _bgOriginal == null || _currentBackground == null)
            {
                _state.Status = "Punch: this room has no editable background PNG.";
                return null;
            }

            // A punch is its own one-shot "stroke". Closing any open erase
            // stroke first means the punch can never be folded into that
            // stroke's undo entry — one Ctrl+Z per user action, always.
            EndStroke();

            // Clamp the footprint to the texture. ClampToRoom should already
            // keep every placement inside a 320x144 room, but the punch writes
            // straight into the pixel array — it must never index out of range
            // (e.g. a hand-edited JSON position, or an undersized PNG).
            int texW = _currentBackground.Width;
            int texH = _currentBackground.Height;
            Rectangle b = p.Bounds;
            int x0 = Math.Max(0, b.Left);
            int y0 = Math.Max(0, b.Top);
            int x1 = Math.Min(texW, b.Right);
            int y1 = Math.Min(texH, b.Bottom);
            if (x0 >= x1 || y0 >= y1)
            {
                _state.Status = $"Punch: {p.DisplayName} lies outside the background image.";
                return null;
            }

            // Pre-scan. Punching an already-clear rect changes nothing, and a
            // no-op action must not push a command — that would evict real
            // history off the end of the UndoStack.MaxDepth ring (same rule
            // EndStroke applies to no-op brush strokes).
            bool anyOpaque = false;
            for (int y = y0; y < y1 && !anyOpaque; y++)
            for (int x = x0; x < x1 && !anyOpaque; x++)
                if (_bgPixels[y * texW + x] != Color.Transparent) anyOpaque = true;

            if (!anyOpaque)
            {
                _state.Status = $"Punch: nothing to punch — background under {p.DisplayName} is already clear.";
                return null;
            }

            // Transient, like the erase stroke's: the clone is diffed below and
            // the command keeps only the 24x24 rectangle. Going through the
            // same FromDiff as the stroke rather than slicing the known rect
            // here is deliberate — one construction path for background
            // history means one place for tools/EditCheck to hold to account.
            var before = (Color[])_bgPixels.Clone();

            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                _bgPixels[y * texW + x] = Color.Transparent;

            _currentBackground.SetData(_bgPixels);
            _state.BackgroundDirty = true;
            _discardArmed = false;         // new edits re-arm the discard guard
            _state.Status = $"Punched background under {p.DisplayName} at ({(int)p.Position.X}, {(int)p.Position.Y}) — Ctrl+Z undoes, Save writes PNG.";

            return BackgroundEditCommand.FromDiff(before, _bgPixels, texW, texH,
                                                  $"punch under {p.Id}");
        }

        // --- REACHABILITY VALIDATOR ----------------------------------------

        /// <summary>
        /// Flood-fills from the room's player spawn over an 8-pixel grid where
        /// each cell is "ok" iff a 24x24 player hitbox at that position doesn't
        /// overlap any solid tile. Any placement whose center sits in an
        /// unreachable cell is flagged.
        ///
        /// The origin is the room's own spawn when it has one — read from
        /// EditorState, so an unsaved spawn edit is validated from where it
        /// now is — otherwise RoomLayoutLoader.DefaultPlayerSpawn, the same
        /// constant Game1 falls back to. This used to be a hardcoded
        /// (160, 80) that could silently disagree with the game.
        ///
        /// Approximations: ignores the 2 px horizontal collision inset
        /// (uses the full 24x24 hitbox) and treats SolidRects (doors)
        /// as passable since the editor doesn't model them.
        /// </summary>
        private void ValidateReachability()
        {
            _state.UnreachableIds.Clear();

            // Duplicate placement IDs make every ID-keyed result ambiguous
            // (UnreachableIds is a set, so two entities sharing an ID would be
            // flagged or cleared together) and they corrupt the game's
            // WorldState persistence. GenerateId can no longer mint one, but a
            // hand-edited content JSON still can — refuse to validate until
            // it's fixed rather than report against ambiguous keys.
            var seenIds = new HashSet<string>();
            foreach (var p in _state.Placements)
            {
                if (seenIds.Add(p.Id)) continue;
                _state.HasValidated = false;
                _state.Status = $"Validate: DUPLICATE ID '{p.Id}' — fix content JSON before validating.";
                return;
            }

            _state.HasValidated = true;

            if (_state.CollisionMap == null)
            {
                _state.Status = "Validate: no collision data — all placements assumed reachable.";
                return;
            }

            const int step = 8;                           // cell size on the reachability grid
            int cellsX = (EditorLayout.RoomWidth - 24) / step + 1;   // 38
            int cellsY = (EditorLayout.RoomHeight - 24) / step + 1;  // 16

            bool[,] ok = new bool[cellsX, cellsY];
            for (int cy = 0; cy < cellsY; cy++)
            for (int cx = 0; cx < cellsX; cx++)
                ok[cx, cy] = PlayerFitsAt(cx * step, cy * step);

            // Flood-fill from the cell closest to the spawn position.
            Vector2 spawn = _state.PlayerSpawn ?? RoomLayoutLoader.DefaultPlayerSpawn;
            int sx = Math.Clamp((int)spawn.X / step, 0, cellsX - 1);
            int sy = Math.Clamp((int)spawn.Y / step, 0, cellsY - 1);

            // If spawn is in solid geometry, walk outward to find a fit.
            if (!ok[sx, sy])
            {
                bool found = false;
                for (int radius = 1; radius < Math.Max(cellsX, cellsY) && !found; radius++)
                {
                    for (int dy = -radius; dy <= radius && !found; dy++)
                    for (int dx = -radius; dx <= radius && !found; dx++)
                    {
                        int nx = sx + dx, ny = sy + dy;
                        if (nx < 0 || ny < 0 || nx >= cellsX || ny >= cellsY) continue;
                        if (ok[nx, ny]) { sx = nx; sy = ny; found = true; }
                    }
                }
                if (!found)
                {
                    _state.Status =
                        $"Validate: spawn ({(int)spawn.X}, {(int)spawn.Y}) is fully blocked — fix collision.";
                    foreach (var p in _state.Placements) _state.UnreachableIds.Add(p.Id);
                    return;
                }
            }

            bool[,] reach = new bool[cellsX, cellsY];
            var queue = new Queue<(int x, int y)>();
            reach[sx, sy] = true;
            queue.Enqueue((sx, sy));

            int[] dxs = { -1, 1, 0, 0, -1, -1, 1, 1 };
            int[] dys = { 0, 0, -1, 1, -1, 1, -1, 1 };
            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                for (int i = 0; i < dxs.Length; i++)
                {
                    int nx = cx + dxs[i], ny = cy + dys[i];
                    if (nx < 0 || ny < 0 || nx >= cellsX || ny >= cellsY) continue;
                    if (reach[nx, ny] || !ok[nx, ny]) continue;
                    reach[nx, ny] = true;
                    queue.Enqueue((nx, ny));
                }
            }

            // Mark each placement as reachable if any cell that overlaps its
            // 24x24 hitbox is reachable (lenient — placements at cell
            // boundaries shouldn't false-flag).
            foreach (var p in _state.Placements)
            {
                bool any = false;
                int x0 = Math.Clamp((int)p.Position.X / step, 0, cellsX - 1);
                int y0 = Math.Clamp((int)p.Position.Y / step, 0, cellsY - 1);
                int x1 = Math.Clamp(((int)p.Position.X + 23) / step, 0, cellsX - 1);
                int y1 = Math.Clamp(((int)p.Position.Y + 23) / step, 0, cellsY - 1);
                for (int yy = y0; yy <= y1 && !any; yy++)
                for (int xx = x0; xx <= x1 && !any; xx++)
                    if (reach[xx, yy]) any = true;

                if (!any) _state.UnreachableIds.Add(p.Id);
            }

            int bad = _state.UnreachableIds.Count;
            string origin = $"({(int)spawn.X}, {(int)spawn.Y})"
                          + (_state.PlayerSpawn.HasValue ? "" : " default");
            _state.Status = bad == 0
                ? $"Validate: all {_state.Placements.Count} placements reachable from spawn {origin}."
                : $"Validate: {bad} of {_state.Placements.Count} placements UNREACHABLE from spawn {origin} — see red borders.";
        }

        // --- PUZZLE ANALYZER ----------------------------------------------

        /// <summary>
        /// Cross-room sanity check (one-button operation): walks the door
        /// graph from chateau_0, aggregates everything in the JSON content
        /// files of reachable rooms, then flags missing-counter weapons,
        /// missing keys, and stranded wizards. Result drives the yellow
        /// borders on canvas — only placements in the CURRENT room are
        /// rendered, but issues across the whole world are summarised in
        /// the status bar.
        ///
        /// The current room is passed in from memory rather than read back
        /// from disk, so unsaved edits are analysed too — matching what
        /// ValidateDoors already does with its doorsByRoom overlay.
        /// </summary>
        private void AnalyzePuzzle()
        {
            var doors = new List<DoorDef>();
            foreach (var p in _state.Placements)
                if (p.Kind == PlacementKind.Door)
                    doors.Add(new DoorDef(p.Id, p.Position, p.DoorOpeningSide,
                                          p.DoorTargetRoomId, p.DoorTargetDoorId));

            var report = PuzzleAnalyzer.Analyze(
                _state.CurrentRoom.RoomId, _state.ToRoomContent(), doors);
            _state.LastPuzzleReport = report;
            _state.PuzzleProblemIds.Clear();
            foreach (var issue in report.Issues)
                _state.PuzzleProblemIds.Add(issue.PlacementId);

            int reach = report.ReachableRoomIds.Count;
            int total = RoomMeta.All.Count;
            int issues = report.Issues.Count;

            if (issues == 0)
            {
                _state.Status =
                    $"Puzzle: OK. {reach}/{total} rooms reachable, " +
                    $"{report.ItemCount} items, {report.EnemyCount} enemies, " +
                    $"{report.WizardCount} wizards, {report.BlockedDoorCount} blocked doors.";
            }
            else
            {
                // Show the first issue inline; the rest can be discovered by
                // cycling rooms (yellow markers appear on flagged placements).
                string first = report.Issues[0].Reason;
                _state.Status =
                    $"Puzzle: {issues} issue{(issues == 1 ? "" : "s")} " +
                    $"({reach}/{total} reachable). First: {first}";
            }
        }

        // --- DOOR-LINK VALIDATOR -------------------------------------------

        /// <summary>
        /// Walk every door across every room and diagnose its target link.
        /// Mirrors what RoomManager.ExecuteTransition does at runtime: it
        /// looks up TargetRoomId, then within that room looks up
        /// TargetDoorId. If either lookup fails, the player lands at the
        /// (160, 60) fallback — silent in-game, but a real authoring bug.
        /// </summary>
        // The rules themselves moved to DoorValidator when the world map needed
        // the same answers for its arrows. This is now the button: run the
        // shared check, publish the verdicts the canvas overlays read, report.
        private void ValidateDoors()
        {
            var report = RunDoorValidation();

            int bad = report.Bad;
            if (bad == 0)
            {
                _state.Status = $"Doors: all {report.Ok} doors link cleanly across {RoomMeta.All.Count} rooms.";
            }
            else
            {
                _state.Status =
                    $"Doors: {bad} broken " +
                    $"(orphan-room={report.OrphanRoom}, orphan-door={report.OrphanDoor}, asymmetric={report.Asymmetric}). " +
                    $"Red markers on canvas show local room's issues.";
            }
        }

        /// <summary>
        /// Validate every door in the world and publish the verdicts into
        /// EditorState, without touching the status line. The map calls this on
        /// entry; the Doors button calls it and then reports.
        /// </summary>
        // The door table overlays the current room's unsaved placements, so the
        // map's arrows and the canvas's outlines both reflect doors that have
        // been authored but not yet written — which is the whole reason the
        // overlay exists.
        private DoorReport RunDoorValidation()
        {
            var doorsByRoom = DoorValidator.BuildDoorTable(
                RoomMeta.All, _state.CurrentRoom.RoomId, _state.Placements);
            var report = DoorValidator.Validate(DoorValidator.RoomIdsOf(RoomMeta.All), doorsByRoom);

            _state.DoorStatus.Clear();
            foreach (var pair in report.Status) _state.DoorStatus[pair.Key] = pair.Value;
            _state.HasValidatedDoors = true;

            _lastDoorTable = doorsByRoom;
            return report;
        }

        // Kept from the last validation so the map can build its arrows from
        // exactly the table the verdicts were computed against, rather than
        // rebuilding one that might disagree.
        private Dictionary<string, List<DoorDef>> _lastDoorTable = new();

        /// <summary>True iff a 24x24 hitbox at (px, py) doesn't overlap any solid tile.</summary>
        private bool PlayerFitsAt(int px, int py)
        {
            if (_state.CollisionMap == null) return true;
            int tileSize = TileConfig.TILE_SIZE;
            int x0 = px / tileSize;
            int y0 = py / tileSize;
            int x1 = (px + 23) / tileSize;
            int y1 = (py + 23) / tileSize;
            for (int ty = y0; ty <= y1; ty++)
            for (int tx = x0; tx <= x1; tx++)
                if (_state.CollisionMap.IsTileSolid(tx, ty)) return false;
            return true;
        }

        private bool Pressed(Keys key) =>
            _keysNow.IsKeyDown(key) && !_keysPrev.IsKeyDown(key);

        private void HandleKeyboardShortcuts()
        {
            bool ctrl  = _keysNow.IsKeyDown(Keys.LeftControl) || _keysNow.IsKeyDown(Keys.RightControl);
            bool shift = _keysNow.IsKeyDown(Keys.LeftShift)   || _keysNow.IsKeyDown(Keys.RightShift);

            // Delete the selected placement, or clear the selected spawn.
            if (Pressed(Keys.Delete))
            {
                if (_state.SelectedPlacement != null)
                {
                    var doomed = _state.SelectedPlacement;
                    // The index goes into the command so undo puts it back
                    // where it was, not on the end of the list.
                    ExecuteCommand(new DeletePlacementCommand(doomed, _state.Placements.IndexOf(doomed)));
                    _state.Status = $"Deleted {doomed.DisplayName} — Ctrl+Z undoes.";
                }
                else if (_state.SpawnSelected && _state.PlayerSpawn.HasValue)
                {
                    // Back to null, not to (160, 80): the next save then omits
                    // the "playerSpawn" key entirely and the room falls back to
                    // RoomLayoutLoader.DefaultPlayerSpawn in game.
                    ExecuteCommand(SetPlayerSpawnCommand.Clear(_state.PlayerSpawn.Value));
                    _state.Status = "Cleared the player spawn — this room falls back to (160, 80). Ctrl+Z undoes; Save to persist.";
                }
            }

            // Ctrl+S → save.
            if (ctrl && Pressed(Keys.S)) SaveCurrentRoom();

            // Ctrl+Z → undo; Ctrl+Y and Ctrl+Shift+Z → redo. One path for every
            // kind of edit, which is the whole of EDITOR_REVIEW item 11.
            //
            // REDO IS TESTED FIRST, and the undo branch excludes Shift. Both
            // halves are required: without the order, Ctrl+Shift+Z would fall
            // into the undo branch; without the exclusion, it would do both.
            if (ctrl && shift && Pressed(Keys.Z)) RedoLastEdit();
            else if (ctrl && Pressed(Keys.Y)) RedoLastEdit();
            else if (ctrl && !shift && Pressed(Keys.Z)) UndoLastEdit();

            // P → punch the background out from under the selected placement.
            // Place mode only: that's the mode where a placement can be
            // selected at all, and the align-then-cut workflow lives there.
            if (Pressed(Keys.P))
            {
                if (_state.Mode == EditorMode.Place && _state.SelectedPlacement != null)
                    PunchBackground(_state.SelectedPlacement);
                else
                    _state.Status = "Punch: select a placement in Place mode first.";
            }

            // [ / ] → brush size down/up (Shift = steps of 4).
            int step = shift ? 4 : 1;
            if (Pressed(Keys.OemOpenBrackets))  SetBrushSize(_state.BrushSize - step);
            if (Pressed(Keys.OemCloseBrackets)) SetBrushSize(_state.BrushSize + step);

            // Arrow keys → pan the zoomed view in 8-px nudges.
            if (Pressed(Keys.Left))  EditorLayout.SetPan(EditorLayout.PanX - 8, EditorLayout.PanY);
            if (Pressed(Keys.Right)) EditorLayout.SetPan(EditorLayout.PanX + 8, EditorLayout.PanY);
            if (Pressed(Keys.Up))    EditorLayout.SetPan(EditorLayout.PanX, EditorLayout.PanY - 8);
            if (Pressed(Keys.Down))  EditorLayout.SetPan(EditorLayout.PanX, EditorLayout.PanY + 8);

            // Page-up/down → cycle rooms.
            if (Pressed(Keys.PageUp)) CyclePrevRoom();
            if (Pressed(Keys.PageDown)) CycleNextRoom();

            // F11 → borderless fullscreen toggle.
            if (Pressed(Keys.F11)) RequestFullscreenToggle();
        }

        private void SetBrushSize(int size)
        {
            _state.BrushSize = Math.Clamp(size, 1, 32);
            _state.Status = $"Brush {_state.BrushSize}px";
        }

        private void DropDraggingAt(Vector2 gamePos)
        {
            var entry = _state.Dragging!;
            string roomId = _state.CurrentRoom.RoomId;

            // The spawn marker is not an entity: no ID is minted, nothing is
            // added to Placements, and nothing reaches content JSON. Dropping
            // it when the room already has a spawn MOVES that spawn — there is
            // exactly one per room, by construction rather than by validation.
            if (entry.IsPlayerSpawn)
            {
                Vector2? before = _state.PlayerSpawn;
                Vector2 after = ClampPointToRoom(gamePos);
                _state.Dragging = null;

                // Set or Move by whether the room already had a spawn — one
                // state transition, two labels, because "set player spawn" and
                // "move player spawn" are what the author did.
                ExecuteCommand(before.HasValue
                    ? SetPlayerSpawnCommand.Move(before.Value, after)
                    : SetPlayerSpawnCommand.Set(before, after));

                _state.Status =
                    $"Player spawn set to ({(int)after.X}, {(int)after.Y}) — " +
                    "drag to move, Delete to clear, Ctrl+Z undoes, Ctrl+S writes layout JSON.";
                return;
            }

            // Door entries carry their logical opening side as a typed field.
            // Placement stores it as the string that goes straight into
            // layout JSON's "type", and DoorType's member names ARE that
            // schema's vocabulary ("LeftOpening" / "RightOpening"), so
            // ToString() is the conversion — no mapping table to drift.
            //
            // Non-door kinds keep the "LeftOpening" default the field has
            // always had; it is inert for them (ToRoomLayoutJson only writes
            // Door placements).
            string openingSide = entry.Kind == PlacementKind.Door && entry.DoorOpeningSide.HasValue
                ? entry.DoorOpeningSide.Value.ToString()
                : "LeftOpening";

            var placement = new Placement(
                _state.GenerateId(roomId, entry.Kind, entry.ItemType, entry.EnemyType),
                entry.Kind,
                gamePos)
            {
                ItemType = entry.Kind == PlacementKind.Item ? entry.ItemType : ItemType.None,
                EnemyType = entry.Kind == PlacementKind.Enemy ? entry.EnemyType : default,
                RequiredItem = entry.Kind == PlacementKind.BlockedDoor ? entry.ItemType : ItemType.None,
                DoorOpeningSide = openingSide,
            };

            ClampToRoom(placement);
            _state.Dragging = null;

            // The add itself, through its own command — appended, which is
            // where the list has always grown, and where an undo/redo cycle
            // puts it back so the saved file's order never shifts.
            var add = new AddPlacementCommand(placement, _state.Placements.Count);
            add.Do(_cmd);
            _state.Status = $"Placed {placement.DisplayName} at ({(int)placement.Position.X}, {(int)placement.Position.Y})";

            // Auto-punch runs AFTER the clamp, so the hole matches the position
            // the placement actually ended up at. It overwrites the status line
            // with its own report — that's deliberate; the punch is the part
            // that touched the PNG and therefore the part worth reporting.
            //
            // Composed with the add rather than pushed beside it: ONE click
            // must cost ONE Ctrl+Z. Undoing them separately would leave a hole
            // with the door still standing in it — a state the author never
            // asked for and cannot reach any other way.
            IEditorCommand? punch = _state.AutoPunch ? PunchBackgroundCore(placement) : null;
            RecordCommand(punch == null
                ? add
                : new CompositeCommand(add.Label, add, punch));
        }

        private Placement? HitTestPlacements(Vector2 gamePos)
        {
            // Iterate in reverse so most-recently-placed (drawn on top) wins.
            for (int i = _state.Placements.Count - 1; i >= 0; i--)
            {
                if (_state.Placements[i].Bounds.Contains((int)gamePos.X, (int)gamePos.Y))
                    return _state.Placements[i];
            }
            return null;
        }

        private Vector2 SnapIfNeeded(Vector2 gamePos)
        {
            if (!_state.SnapEnabled) return gamePos;
            const int s = TileConfig.TILE_SIZE;
            return new Vector2((float)Math.Round(gamePos.X / s) * s,
                               (float)Math.Round(gamePos.Y / s) * s);
        }

        private static void ClampToRoom(Placement p) => p.Position = ClampPointToRoom(p.Position);

        /// <summary>
        /// Keep a 24x24 top-left corner inside the room. Shared by placements
        /// and the spawn marker, which is the same size.
        /// </summary>
        private static Vector2 ClampPointToRoom(Vector2 pos) =>
            new(Math.Clamp(pos.X, 0f, EditorLayout.RoomWidth - 24),
                Math.Clamp(pos.Y, 0f, EditorLayout.RoomHeight - 24));

        /// <summary>True when the room has a spawn and gamePos is inside its 24x24 marker.</summary>
        private bool HitTestSpawn(Vector2 gamePos)
        {
            if (!_state.PlayerSpawn.HasValue) return false;
            var b = new Rectangle((int)_state.PlayerSpawn.Value.X, (int)_state.PlayerSpawn.Value.Y, 24, 24);
            return b.Contains((int)gamePos.X, (int)gamePos.Y);
        }

        // ====================================================================
        // CHROME
        // ====================================================================
        // The single hook where every ImGui window is built. Called from
        // Update, after the editor's own input handling, so the chrome always
        // renders the state this frame just produced.
        //
        // Everything in here may only CALL the editor's logic methods — the
        // same ones the hand-rolled buttons called. Nothing decides anything.
        // ====================================================================

        private void BuildChrome()
        {
            // Rebuilt between panels rather than once for the frame: a menu
            // item's callback runs DURING MenuBar.Draw and can load a room,
            // leave map mode or clear a dirty flag. Handing the status bar a
            // snapshot taken before that would show the previous room's state
            // for one frame — a marker that flickers after a save is exactly
            // the kind of thing nobody can reproduce on demand.
            MenuBar.Draw(this, _state, Snapshot());

            // The board takes the palette's and the inspector's space while it
            // is up: at seventy-five rooms the scarce thing is width, and
            // neither panel has anything to say about a world.
            if (!_mapMode)
            {
                PalettePanel.Draw(this, _state, Snapshot());
                InspectorPanel.Draw(this, _state, Snapshot());
            }

            StatusBar.Draw(_state, Snapshot());

            // Over the panels, and over the board: at most one of the three is
            // ever open, and while one is, EditorGame's Update returns before
            // anything underneath sees input.
            Pickers.Draw(this, Snapshot());

            // Last, and into the FOREGROUND list, so the carried entry floats
            // over every panel. Suppressed while a modal is up or the board is
            // showing — the room editor is not the live surface then, and a
            // ghost hovering over a picker reads as a glitch.
            if (!_mapMode && !_newRoomOpen && !_importOpen && !_cropOpen) DrawDragGhost();

            if (ImGuiProbe) DrawRoutingProbe();
        }

        /// <summary>
        /// The read-only view state the chrome renders from. Everything here
        /// lives on EditorGame rather than EditorState; the panels get it by
        /// value and cannot write it back.
        /// </summary>
        private ChromeView Snapshot() => new()
        {
            MapMode = _mapMode,
            IsFullscreen = _isFullscreen,
            RoomDisplayName = _state.CurrentRoom.DisplayName,
            RoomId = _state.CurrentRoom.RoomId,
            // The same three flags ConfirmDiscardUnsavedEdits consults, and
            // deliberately not MapDirty — see EditorState's comment on why the
            // board's unsaved state is not the room's.
            RoomDirty = _state.PlacementsDirty || _state.CollisionDirty || _state.BackgroundDirty,
            Zoom = EditorLayout.Zoom,
            CanUndo = _undo.CanUndo,
            CanRedo = _undo.CanRedo,

            // The inspector's three pickers. TargetDoorIds is a FUNCTION
            // because the answer depends on the room chosen in the row above
            // it, which changes while the panel is on screen; the other two are
            // lists because they do not.
            TargetRoomIds = TargetRoomIds(),
            RequiredItems = RequiredItems(),
            DoorIdsForRoom = TargetDoorIdsFor,
            MapRoomCount = _mapRooms.Count,
            MapZoomPercent = _mapView.ZoomPercent,

            // Any modal owning the editor makes the three bands NoInputs. A
            // running batch counts and shows no overlay of its own: it is
            // writing files one per frame, and a click that loaded a different
            // room underneath it would be genuinely destructive.
            ModalOpen = _batchRunning || _newRoomOpen || _importOpen || _cropOpen,

            NewRoomOpen = _newRoomOpen,
            NewRoomCandidates = _newRoomCandidates,

            ImportOpen = _importOpen,
            ImportCandidates = _importCandidates,
            ImportDir = EditorPaths.RepoImportDir,
            ImportQuantize = _importQuantize,
            // Read from the plan computed when the picker opened: nothing that
            // can change eligibility is reachable without closing it, and
            // re-planning per frame would allocate a list per file per frame
            // for an unchanging answer.
            ImportBatchOffered = _importPlan.Offered,
            ImportBatchCount = _importPlan.Eligible.Count,

            CropOpen = _cropOpen,
            CropFileName = _cropCandidate?.FileName ?? "",
            CropRoomId = _cropCandidate?.RoomId ?? "",
            CropDisplayName = _cropCandidate?.DisplayName ?? "",
            CropPresetNote = _cropOpen
                ? ImageImport.DescribeCropPreset(_cropPresetOrigin, _cropSrcW, _cropSrcH)
                : "",
            CropSourceWidth = _cropSrcW,
            CropSourceHeight = _cropSrcH,
            CropRect = _cropRect,
        };

        /// <summary>
        /// --imgui-probe: a small window reporting the frame's routing verdict.
        /// </summary>
        // tools/ChromeCheck proves the routing rules headlessly against the
        // real ImGui, which is the important half. This is the other half: the
        // one thing a headless harness cannot do is tell you that the window
        // you are hovering is the window ImGui thinks you are hovering, on a
        // real driver, at a real DPI.
        private void DrawRoutingProbe()
        {
            var io = ImGui.GetIO();
            ImGui.SetNextWindowPos(new NVector2(EditorLayout.WindowWidth - 300f, 80f), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new NVector2(290f, 150f), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Input routing probe"))
            {
                ImGui.TextUnformatted($"WantCaptureMouse    {io.WantCaptureMouse}");
                ImGui.TextUnformatted($"WantCaptureKeyboard {io.WantCaptureKeyboard}");
                ImGui.Separator();
                ImGui.TextUnformatted($"world gesture       {_router.WorldGestureInProgress}");
                ImGui.TextUnformatted($"mouse -> world      {_router.MouseReachesWorld}");
                ImGui.TextUnformatted($"keys  -> editor     {_router.KeyboardReachesEditor}");
                ImGui.Separator();
                ImGui.TextUnformatted($"mouse {io.MousePos.X:0}, {io.MousePos.Y:0}");
            }
            ImGui.End();
        }

        // ====================================================================
        // DRAW
        // ====================================================================

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(new Color(36, 38, 46));

            if (_mapMode) DrawMapMode();
            else DrawRoomMode();

            // The pickers sit above whichever mode is underneath: they are
            // reachable from the top bar in room view, and (once the map has
            // its own entry points) from the board too. Only one is ever open.
            // The crop step owns its own two passes — the source image wants
            // LINEAR filtering (it is shown at an arbitrary fractional scale,
            // where point sampling drops whole rows and makes a screenshot
            // unrecognisable), its chrome wants the PointClamp everything else
            // uses. No-op unless the crop step is open.
            DrawCropOverlay();

            // LAST, over every SpriteBatch pass. The chrome sits on top of the
            // canvas, the map board and the crop image — which is the same
            // stacking the hand-rolled panels had, since they were drawn after
            // the canvas too. ImGuiRenderer saves and restores every device
            // state it touches, so the next frame's first SpriteBatch.Begin
            // finds the device exactly as it left it.
            _imgui.RenderDrawData();

            base.Draw(gameTime);
        }

        private void DrawRoomMode()
        {
            // Pass 1: the canvas frame. The panels around it are ImGui windows
            // now, painted after every SpriteBatch pass.
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            FillRect(EditorLayout.CanvasRect, Color.Black);
            DrawRectOutline(InflateRect(EditorLayout.CanvasRect, 2), new Color(120, 130, 160));
            _spriteBatch.End();

            // Pass 2: canvas content, scissor-clipped to the canvas rect so
            // zoomed-in drawing never spills over the surrounding panels.
            GraphicsDevice.ScissorRectangle = EditorLayout.CanvasRect;
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp, rasterizerState: ScissorOn);
            DrawCanvasContent();
            _spriteBatch.End();

            // Pass 2b: outline overlays. Their borders inflate up to 3 px
            // beyond placements that sit flush at a room edge (doors by
            // convention do), so they get a slightly looser scissor —
            // still clipped, so off-view outlines can't reach the panels.
            GraphicsDevice.ScissorRectangle = InflateRect(EditorLayout.CanvasRect, 3);
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp, rasterizerState: ScissorOn);
            DrawCanvasOverlays();
            _spriteBatch.End();

            // Pass 3: overlays that intentionally draw outside the canvas —
            // door labels live in the canvas margin. The side panels that used
            // to be drawn here are ImGui windows now.
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            DrawDoorLabels();
            _spriteBatch.End();
        }

        // -- Canvas: background → collision overlay → placements → selection.

        private void DrawCanvasContent()
        {
            if (_currentBackground != null)
            {
                // Source rect = the visible (zoomed/panned) region of the room.
                var src = new Rectangle(EditorLayout.PanX, EditorLayout.PanY,
                                        EditorLayout.VisibleWidth, EditorLayout.VisibleHeight);
                _spriteBatch.Draw(_currentBackground, EditorLayout.CanvasRect, src, Color.White);
            }

            DrawCollisionOverlay();
            DrawPaintCursor();
            DrawPlacements();
        }

        // Outline overlays — drawn in pass 2b under a 3px-looser scissor so
        // borders around room-edge placements aren't shaved off.
        private void DrawCanvasOverlays()
        {
            DrawDoorMarkerOutlines();
            DrawPlayerSpawnMarker();
            DrawUnreachableWarnings();
            DrawPuzzleWarnings();
            DrawSelectionHighlight();
            DrawEraseCursor();
        }

        /// <summary>
        /// The room's player spawn: the generated 24x24 marker texture drawn
        /// at the spawn position, plus a doubled outline so it stays legible
        /// over busy backgrounds at 1x zoom. Nothing draws when the room has
        /// no spawn — absence is the common case and must look like absence,
        /// not like a marker parked at the fallback position.
        /// </summary>
        private void DrawPlayerSpawnMarker()
        {
            if (!_state.PlayerSpawn.HasValue) return;

            var v = _state.PlayerSpawn.Value;
            var dest = EditorLayout.GameRectToScreen(new Rectangle((int)v.X, (int)v.Y, 24, 24));
            // White tint: the texture already carries SpawnColor in its pixels,
            // and tinting magenta by magenta would darken the green channel.
            _spriteBatch.Draw(_spawnMarker, dest, null, Color.White);
            DrawRectOutline(InflateRect(dest, 1), SpawnColor);
            DrawRectOutline(InflateRect(dest, 2), SpawnColor);
        }

        /// <summary>
        /// Brush preview in Erase mode: the exact pixels the next stamp
        /// would cover, double-outlined so it reads on any background.
        /// </summary>
        private void DrawEraseCursor()
        {
            if (_state.Mode != EditorMode.Erase || _bgPixels == null) return;
            var pt = new Point(_mouseNow.X, _mouseNow.Y);
            if (!EditorLayout.IsInsideCanvas(pt)) return;

            Vector2 game = EditorLayout.ScreenToGame(pt);
            int size = _state.BrushSize;
            int bx = (int)Math.Floor(game.X) - size / 2;
            int by = (int)Math.Floor(game.Y) - size / 2;
            var dest = EditorLayout.GameRectToScreen(new Rectangle(bx, by, size, size));
            DrawRectOutline(InflateRect(dest, 1), Color.Black);
            DrawRectOutline(dest, Color.White);
        }

        private void DrawPuzzleWarnings()
        {
            if (_state.LastPuzzleReport == null || _state.PuzzleProblemIds.Count == 0) return;

            // Yellow border for puzzle issues — distinct from the red border
            // the reachability validator uses, so they don't get confused
            // when both validators have been run.
            var warn = new Color(255, 220, 60);
            foreach (var p in _state.Placements)
            {
                if (!_state.PuzzleProblemIds.Contains(p.Id)) continue;
                var dest = EditorLayout.GameRectToScreen(p.Bounds);
                DrawRectOutline(InflateRect(dest, 1), warn);
                DrawRectOutline(InflateRect(dest, 2), warn);
            }
        }

        /// <summary>The one colour language for door verdicts.</summary>
        // Shared by the room canvas's door outlines and the world map's arrows,
        // so the same link is the same colour wherever it is drawn.
        private static Color StatusColor(string status) => status switch
        {
            "ok"          => new Color( 80, 230, 110),
            // Dimmer green: the target is a programmatic test room, so the
            // link is accepted but its far side is unverified.
            "ok-test"     => new Color( 80, 180, 110),
            "asymmetric"  => new Color(255, 200,  60),
            _             => new Color(255,  60,  60),  // orphan-*
        };

        private Color DoorStatusColor(Placement door) =>
            _state.HasValidatedDoors && _state.DoorStatus.TryGetValue(door.Id, out var status)
                ? StatusColor(status)
                : new Color(180, 180, 200);

        /// <summary>
        /// Outlines every door placement in the current room with its
        /// validation status colour. Iterates _state.Placements so authored-
        /// but-not-yet-saved doors show their overlay too. (Drawn in the
        /// scissored canvas pass; the labels are drawn separately in the
        /// margin pass.)
        /// </summary>
        private void DrawDoorMarkerOutlines()
        {
            foreach (var door in _state.Placements)
            {
                if (door.Kind != PlacementKind.Door) continue;
                var rect = EditorLayout.GameRectToScreen(
                    new Rectangle((int)door.Position.X, (int)door.Position.Y, 24, 24));
                var color = DoorStatusColor(door);
                DrawRectOutline(InflateRect(rect, 1), color);
                DrawRectOutline(InflateRect(rect, 2), color);
            }
        }

        /// <summary>
        /// Render each door's target-room label OUTSIDE the canvas in the
        /// surrounding margin, so it never overlaps placements.
        ///   - Top doors  (y near 0)               → label above canvas
        ///   - Bottom doors (y near RoomHeight-24) → label below canvas
        ///   - Mid-height doors → tucked above the door inside the canvas
        /// Doors scrolled out of the zoomed view get no label.
        /// </summary>
        private void DrawDoorLabels()
        {
            foreach (var door in _state.Placements)
            {
                if (door.Kind != PlacementKind.Door) continue;

                var rect = EditorLayout.GameRectToScreen(
                    new Rectangle((int)door.Position.X, (int)door.Position.Y, 24, 24));
                if (!rect.Intersects(EditorLayout.CanvasRect)) continue;

                string targetLabel = string.IsNullOrEmpty(door.DoorTargetRoomId)
                    ? "(no target)"
                    : door.DoorTargetRoomId;
                var size = MeasureText(targetLabel);
                bool isTopDoor    = door.Position.Y < 12;
                bool isBottomDoor = door.Position.Y + 24 > EditorLayout.RoomHeight - 12;

                int labelY;
                if (isTopDoor)
                    labelY = EditorLayout.CanvasY - (int)size.Y - 4;
                else if (isBottomDoor)
                    labelY = EditorLayout.CanvasY + EditorLayout.CanvasHeight + 4;
                else
                    // Clamp into the canvas: a mid-height door partially
                    // scrolled off the top of a zoomed view must not push
                    // its label up into the top-bar buttons.
                    labelY = Math.Max(rect.Top - (int)size.Y - 4, EditorLayout.CanvasY + 2);

                int labelX = rect.X + (rect.Width - (int)size.X) / 2;
                // Clamp horizontally so labels near the canvas edges don't
                // run off the screen on a narrow window.
                int minX = EditorLayout.CanvasX;
                int maxX = EditorLayout.CanvasX + EditorLayout.CanvasWidth - (int)size.X;
                labelX = Math.Clamp(labelX, minX, maxX);

                DrawText(targetLabel,
                    new Vector2(labelX, labelY),
                    new Color(220, 220, 240));
            }
        }

        private void DrawPaintCursor()
        {
            if (_state.Mode != EditorMode.Paint) return;
            var pt = new Point(_mouseNow.X, _mouseNow.Y);
            if (!EditorLayout.IsInsideCanvas(pt)) return;

            Vector2 game = EditorLayout.ScreenToGame(pt);
            int t = TileConfig.TILE_SIZE;
            int tx = (int)(game.X / t);
            int ty = (int)(game.Y / t);

            // Yellow outline at the hovered tile so the brush position is obvious.
            var dest = EditorLayout.GameRectToScreen(new Rectangle(tx * t, ty * t, t, t));
            DrawRectOutline(dest, new Color(255, 220, 60));
        }

        private void DrawUnreachableWarnings()
        {
            if (!_state.HasValidated || _state.UnreachableIds.Count == 0) return;

            var bad = new Color(255, 50, 50);
            foreach (var p in _state.Placements)
            {
                if (!_state.UnreachableIds.Contains(p.Id)) continue;
                var dest = EditorLayout.GameRectToScreen(p.Bounds);
                DrawRectOutline(InflateRect(dest, 1), bad);
                DrawRectOutline(InflateRect(dest, 2), bad);
                DrawRectOutline(InflateRect(dest, 3), bad);
            }
        }

        private void DrawCollisionOverlay()
        {
            if (_state.CollisionMap == null) return;
            // Hidden in Erase mode: the red tint would obscure exactly the
            // background pixels the user is trying to inspect and clean.
            if (_state.Mode == EditorMode.Erase) return;

            var tint = new Color(255, 60, 60, 90);
            int t = TileConfig.TILE_SIZE;

            for (int ty = 0; ty < _state.CollisionMap.Height; ty++)
            {
                for (int tx = 0; tx < _state.CollisionMap.Width; tx++)
                {
                    if (!_state.CollisionMap.IsTileSolid(tx, ty)) continue;
                    var dest = EditorLayout.GameRectToScreen(new Rectangle(tx * t, ty * t, t, t));
                    if (!dest.Intersects(EditorLayout.CanvasRect)) continue;
                    FillRect(dest, tint);
                }
            }
        }

        private void DrawPlacements()
        {
            // In Erase mode entities are ghosted so they don't hide the
            // background pixels under the brush (doors especially sit
            // exactly where baked-in door remnants need cleaning).
            Color tint = _state.Mode == EditorMode.Erase ? Color.White * 0.25f : Color.White;
            foreach (var p in _state.Placements)
            {
                var entry = FindPaletteFor(p);
                if (entry == null) continue;
                var dest = EditorLayout.GameRectToScreen(p.Bounds);
                _spriteBatch.Draw(entry.Texture, dest, entry.SourceRect, tint);
            }
        }

        private void DrawSelectionHighlight()
        {
            // Selection is the same yellow whether a placement or the spawn
            // marker holds it — one visual language for "this is what Delete
            // and drag act on". The two are mutually exclusive by construction.
            Rectangle? bounds = null;
            if (_state.SelectedPlacement != null)
                bounds = _state.SelectedPlacement.Bounds;
            else if (_state.SpawnSelected && _state.PlayerSpawn.HasValue)
                bounds = new Rectangle((int)_state.PlayerSpawn.Value.X,
                                       (int)_state.PlayerSpawn.Value.Y, 24, 24);

            if (bounds == null) return;
            var dest = EditorLayout.GameRectToScreen(bounds.Value);
            DrawRectOutline(InflateRect(dest, 2), new Color(255, 220, 60));
        }

        /// <summary>
        /// The cursor's "carried" entry during a palette drag, drawn into
        /// ImGui's FOREGROUND draw list so it stays on top of every panel.
        /// </summary>
        // It used to be the last SpriteBatch draw of the frame, which put it
        // over the hand-rolled panels. ImGui now paints after every SpriteBatch
        // pass, so a SpriteBatch ghost would slide UNDER the palette — the one
        // panel you are guaranteed to be dragging away from. The foreground
        // draw list is above even that, and above the modal pickers, which is
        // right: the ghost is cursor feedback, not content.
        //
        // Built from the palette entry's ImGui handle and precomputed UVs, so
        // the door ghost shows the same deliberately-mismatched texture the
        // canvas will render.
        private void DrawDragGhost()
        {
            if (_state.Dragging == null) return;

            var screen = new Point(_mouseNow.X, _mouseNow.Y);
            // Over the canvas the ghost matches the drop size at the current
            // zoom; elsewhere it stays at base scale so a 16x-zoomed ghost
            // doesn't blanket the palette and inspector panels.
            int scale = EditorLayout.IsInsideCanvas(screen) ? EditorLayout.EffScale : EditorLayout.CanvasScale;
            int size = 24 * scale;

            var min = new NVector2(screen.X - size / 2f, screen.Y - size / 2f);
            var max = new NVector2(min.X + size, min.Y + size);
            ImGui.GetForegroundDrawList().AddImage(
                _state.Dragging.ImGuiTextureId, min, max,
                _state.Dragging.IconUv0, _state.Dragging.IconUv1,
                ChromeTheme.Packed(255, 255, 255, 180));
        }

        // ====================================================================
        // INSPECTOR EDITS
        // ====================================================================
        // Each of these was a lambda inside DrawInspector, carrying its full
        // side-effect set inline in a render method. Extracted verbatim — same
        // writes, same order, same omissions — so that "what happens when I
        // retarget a door" is answerable without reading a renderer.
        //
        // Note what they all leave ALONE. None writes _state.Status: the status
        // bar keeps whatever it had, which is how these have always behaved.
        //
        // TWO THINGS CHANGED IN PR 7b, both consequences of these edits joining
        // the undo stack:
        //
        //   They now clear HasValidated as well as HasValidatedDoors. That is
        //   MarkPlacementsChanged's doing, and it is the conservative reading:
        //   one flag-clearing rule for every placement edit, at the cost of
        //   re-running a validator that would have given the same answer.
        //
        //   They now SELECT the placement and EXPAND its section. An action
        //   that can be replayed by Ctrl+Z has to be visible when it is
        //   replayed — undoing a door retarget inside a collapsed section is an
        //   undo you cannot confirm — and a rule that applied only on the undo
        //   half would make Do and Undo asymmetric, which is precisely the
        //   defect the round-trip property in tools/EditCheck exists to catch.
        //   So it applies to both, and clicking a field selects its placement.
        // ====================================================================

        /// <summary>Section header click: select the placement AND toggle its collapse.</summary>
        // Exactly two side effects, and they cannot be separated: the canvas
        // outline follows SelectedPlacement, so a header that only toggled
        // would leave the canvas pointing at a different entity than the panel.
        // It deliberately does NOT clear SpawnSelected — unlike a canvas click
        // — because DrawSelectionHighlight prefers SelectedPlacement anyway.
        private void SelectAndToggleSection(Placement p)
        {
            _state.SelectedPlacement = p;
            _state.ToggleCollapse(p.Id);
        }

        /// <summary>
        /// Apply one inspector change as one undoable command, or do nothing
        /// when the change is a no-op.
        /// </summary>
        // THE ONE PATH every inspector field takes, so that "one applied change
        // = one Ctrl+Z" is a property of the code rather than a habit each
        // field has to remember. The no-op check is what keeps a picker that
        // re-selects the value already showing from filling the stack with
        // entries that undo nothing — the same rule an already-clear punch and
        // a no-op brush stroke follow.
        //
        // The dirty flags are NOT set here: SetPlacementFieldCommand sets them
        // in both directions, which is what makes undo dirty the room too.
        private void ApplyPlacementFields(Placement p, in PlacementFields before,
                                          in PlacementFields after, string what)
        {
            if (after.Equals(before)) return;
            ExecuteCommand(new SetPlacementFieldCommand(p, before, after, what));
        }

        private void CycleDoorOpeningSide(Placement p)
        {
            var before = PlacementFields.From(p);
            var after = before;
            after.DoorOpeningSide = before.DoorOpeningSide == "LeftOpening" ? "RightOpening" : "LeftOpening";
            ApplyPlacementFields(p, before, after, "opening side");
        }

        /// <summary>Point a door at a room — and blank the target door with it.</summary>
        // The blanking is load-bearing, not tidiness: a door id is only
        // meaningful inside one room, so carrying the old one across a room
        // change would leave a link that validates as orphan-door and reads
        // like a typo. Both writes are ONE command, so undoing the room change
        // restores the door id with it — a per-field command would undo half of
        // this and leave exactly the broken link the blanking exists to avoid.
        //
        // It stays on THIS side rather than in the picker for the same reason:
        // a panel that had to remember to blank the door is a panel that will
        // one day forget, and forgetting is silent.
        private void SetDoorTargetRoom(Placement p, string roomId)
        {
            var before = PlacementFields.From(p);
            var after = before;
            after.DoorTargetRoomId = roomId;
            after.DoorTargetDoorId = "";
            ApplyPlacementFields(p, before, after, "target room");
        }

        private void SetDoorTargetDoor(Placement p, string doorId)
        {
            var before = PlacementFields.From(p);
            var after = before;
            after.DoorTargetDoorId = doorId;
            ApplyPlacementFields(p, before, after, "target door");
        }

        private void SetBlockedDoorRequiredItem(Placement p, ItemType item)
        {
            var before = PlacementFields.From(p);
            var after = before;
            after.RequiredItem = item;
            ApplyPlacementFields(p, before, after, "required item");
        }

        // ====================================================================
        // PICKER OPTION LISTS
        // ====================================================================
        // What the three filterable dropdowns offer. Built here, on the logic
        // side, and handed to the chrome through ChromeView — the panels can
        // read them and cannot compute them, which is the same rule that keeps
        // "what happens when I retarget a door" out of a renderer.
        //
        // Each is rebuilt into a reused list rather than allocated per frame.
        // Snapshot() runs several times a frame (see BuildChrome), and a fresh
        // List<string> of every room per call is a per-frame allocation for an
        // answer that changes only when a room is created.
        // ====================================================================

        private readonly List<string> _targetRoomIds = new();
        private readonly List<ItemType> _requiredItems = new();
        private readonly List<string> _targetDoorIds = new();

        /// <summary>Every registry room, in registry order. Test rooms excluded.</summary>
        // The exclusion is the standing decision, carried over from the cycle
        // this replaces — which walked RoomMeta.All, and RoomMeta.All is built
        // from the registry, so room_1 and room_2 were never offered there
        // either. They are dev scaffolding registered in Game1.RegisterTestRooms
        // and the door validator has a whole verdict ("ok-test") for
        // hand-edited data that points at one; offering them in an AUTHORING
        // list would make that verdict something the editor produces rather
        // than something it tolerates.
        private IReadOnlyList<string> TargetRoomIds()
        {
            _targetRoomIds.Clear();
            foreach (var r in RoomMeta.All) _targetRoomIds.Add(r.RoomId);
            return _targetRoomIds;
        }

        /// <summary>The item catalog a blocked door can require. None excluded.</summary>
        // The same set the cycle reached, expressed as a list instead of as a
        // "next value" walk: it skipped None, so a blocked door could never be
        // cycled back to requiring nothing, and the picker keeps that.
        private IReadOnlyList<ItemType> RequiredItems()
        {
            _requiredItems.Clear();
            foreach (ItemType t in Enum.GetValues(typeof(ItemType)))
                if (t != ItemType.None) _requiredItems.Add(t);
            return _requiredItems;
        }

        /// <summary>
        /// The door ids of one room: its saved doors, plus this room's unsaved
        /// ones when the named room IS the room being edited.
        /// </summary>
        // The unsaved half is what lets a door be wired to another door in the
        // same room before either has been written — self-linking a room is how
        // a two-door corridor gets built, and the cycle had the same rule.
        private IReadOnlyList<string> TargetDoorIdsFor(string roomId)
        {
            _targetDoorIds.Clear();

            var room = RoomMeta.Find(roomId);
            if (room != null)
                foreach (var d in room.Doors) _targetDoorIds.Add(d.DoorId);

            if (roomId == _state.CurrentRoom.RoomId)
            {
                foreach (var p in _state.Placements)
                    if (p.Kind == PlacementKind.Door && !_targetDoorIds.Contains(p.Id))
                        _targetDoorIds.Add(p.Id);
            }
            return _targetDoorIds;
        }

        // ====================================================================
        // HELPERS — drawing, text
        // ====================================================================

        private PaletteEntry? FindPaletteFor(Placement p)
        {
            foreach (var entry in _state.Palette)
            {
                // The spawn entry is never a placement's sprite. Without this
                // it would match an Item placement whose type is None — which
                // a content JSON hand-written with "type": "None" produces —
                // and that entity would draw as a player-spawn marker. Such a
                // placement matched no entry before the META section existed
                // and must keep matching none.
                if (entry.IsPlayerSpawn) continue;

                if (entry.Kind != p.Kind) continue;
                if (p.Kind == PlacementKind.Item && entry.ItemType != p.ItemType) continue;
                if (p.Kind == PlacementKind.Enemy && entry.EnemyType != p.EnemyType) continue;
                if (p.Kind == PlacementKind.BlockedDoor && entry.ItemType != p.RequiredItem) continue;
                if (p.Kind == PlacementKind.Door)
                {
                    // Match on the typed side, not the label. A placement
                    // whose JSON "type" is unparseable (hand-edited file)
                    // matches no entry and so isn't drawn — same outcome the
                    // old label sniffing produced, just not by accident.
                    if (entry.DoorOpeningSide == null) continue;
                    if (entry.DoorOpeningSide.Value.ToString() != p.DoorOpeningSide) continue;
                }
                return entry;
            }
            return null;
        }

        private void FillRect(Rectangle r, Color c) =>
            _spriteBatch.Draw(_pixel, r, c);

        private void DrawRectOutline(Rectangle r, Color c)
        {
            FillRect(new Rectangle(r.X, r.Y, r.Width, 1), c);
            FillRect(new Rectangle(r.X, r.Bottom - 1, r.Width, 1), c);
            FillRect(new Rectangle(r.X, r.Y, 1, r.Height), c);
            FillRect(new Rectangle(r.Right - 1, r.Y, 1, r.Height), c);
        }

        private static Rectangle InflateRect(Rectangle r, int n) =>
            new(r.X - n, r.Y - n, r.Width + 2 * n, r.Height + 2 * n);

        private void DrawText(string text, Vector2 pos, Color color)
        {
            if (_font != null) _spriteBatch.DrawString(_font, text, pos, color);
        }

        private Vector2 MeasureText(string text) =>
            _font != null ? _font.MeasureString(text) : Vector2.Zero;

        /// <summary>
        /// Trim a string with a three-dot ellipsis until it fits the given
        /// pixel width, measured with the SpriteFont.
        /// </summary>
        // The chrome has its own copy of this rule in ChromeTheme.Truncate,
        // measured with ImGui's font. Two copies because there are genuinely
        // two fonts: this one serves the map board's room labels, which are
        // canvas-side and drawn by SpriteBatch. Neither can measure for the
        // other.
        private string TruncateText(string text, float maxPx)
        {
            if (_font == null || string.IsNullOrEmpty(text)) return text;
            if (_font.MeasureString(text).X <= maxPx) return text;

            const string ell = "...";
            string trimmed = text;
            while (trimmed.Length > 0 && _font.MeasureString(trimmed + ell).X > maxPx)
                trimmed = trimmed.Substring(0, trimmed.Length - 1);
            return trimmed + ell;
        }

        // ====================================================================
        // ICHROMEACTIONS — the complete list of what the chrome may do
        // ====================================================================
        // Implemented EXPLICITLY, so none of it widens EditorGame's own
        // surface: these members are callable only through the interface, and
        // the interface is the only thing the panels under UI/ can see. The
        // block is deliberately mechanical — every line is a forward to a
        // method that already existed and is already commented where it lives.
        //
        // That is the whole guarantee. A panel cannot set a dirty flag, cannot
        // touch EditorState, cannot reach the canvas. If a new chrome control
        // needs a new effect, a verb has to appear here and in
        // UI/IChromeActions.cs, which is a diff a reviewer sees.
        // ====================================================================

        void IChromeActions.CyclePrevRoom() => CyclePrevRoom();
        void IChromeActions.CycleNextRoom() => CycleNextRoom();
        void IChromeActions.SaveCurrentRoom() => SaveCurrentRoom();
        void IChromeActions.SaveWorldMap() => SaveWorldMap();

        /// <summary>File &gt; Exit. Identical to Escape in room view.</summary>
        // Not Exit() directly: the guard is the point. The first invocation on
        // a dirty room arms it and warns in the status bar; the second gets
        // through. includeMap because quitting is the one action that loses an
        // unsaved board arrangement.
        void IChromeActions.ExitEditor()
        {
            if (ConfirmDiscardUnsavedEdits(includeMap: true)) Exit();
        }

        void IChromeActions.Undo() => UndoLastEdit();
        void IChromeActions.Redo() => RedoLastEdit();

        void IChromeActions.SetMode(EditorMode mode) => SetMode(mode);
        void IChromeActions.ToggleSnap() => ToggleSnap();
        void IChromeActions.ToggleAutoPunch() => ToggleAutoPunch();
        void IChromeActions.ToggleFullscreen() => RequestFullscreenToggle();
        void IChromeActions.ToggleMapMode() => ToggleMapMode();

        void IChromeActions.ValidateReachability() => ValidateReachability();
        void IChromeActions.ValidateDoors() => ValidateDoors();
        void IChromeActions.AnalyzePuzzle() => AnalyzePuzzle();

        /// <summary>Palette click: the cursor picks the entry up.</summary>
        // Exactly the four writes HandlePaletteInput made, and no others. In
        // particular it does NOT clear SpawnSelected or IsMovingSpawn (a
        // selected spawn keeps its outline while you carry an entry), and it
        // does NOT disarm the discard guard — picking something up is not an
        // edit. Picking up a second entry simply replaces the first.
        void IChromeActions.BeginPaletteDrag(PaletteEntry entry)
        {
            _state.Dragging = entry;
            _state.SelectedPlacement = null;
            _state.IsMovingSelection = false;
            _state.Status = $"Dragging: {entry.Label}. Click on canvas to drop, right-click to cancel.";
        }

        void IChromeActions.SelectAndToggleSection(Placement p) => SelectAndToggleSection(p);
        void IChromeActions.CycleDoorOpeningSide(Placement p) => CycleDoorOpeningSide(p);
        void IChromeActions.SetDoorTargetRoom(Placement p, string roomId) => SetDoorTargetRoom(p, roomId);
        void IChromeActions.SetDoorTargetDoor(Placement p, string doorId) => SetDoorTargetDoor(p, doorId);
        void IChromeActions.SetBlockedDoorRequiredItem(Placement p, ItemType item) =>
            SetBlockedDoorRequiredItem(p, item);
        void IChromeActions.PunchBackground(Placement p) => PunchBackground(p);

        void IChromeActions.OpenNewRoomPicker() => OpenNewRoomPicker();
        void IChromeActions.OpenImportPicker() => OpenImportPicker();
        void IChromeActions.CreateRoom(RoomCandidate candidate) => CreateRoom(candidate);
        void IChromeActions.CancelNewRoomPicker() => CloseNewRoomPicker("New Room cancelled.");
        void IChromeActions.RunImport(ImportCandidate candidate) => RunImport(candidate);
        void IChromeActions.CancelImportPicker() => CloseImportPicker("Import cancelled.");
        void IChromeActions.ToggleImportQuantize() => ToggleImportQuantize();
        void IChromeActions.ConfirmCrop() => ConfirmCrop();

        // The same message the Escape and right-click paths leave, so the
        // reassurance that nothing reached the disk does not depend on which
        // way you backed out.
        void IChromeActions.CancelCrop() => CloseCrop("Import cancelled — nothing was written.");
    }
}
