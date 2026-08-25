using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SorceryRemake.Core;
using SorceryRemake.Doors;
using SorceryRemake.Graphics;
using SorceryRemake.Rooms;
using SorceryRemake.Tiles;
using System;
using System.Collections.Generic;
using System.IO;

namespace SorceryForge
{
    /// <summary>
    /// SorceryForge — the level editor. Renders the same room a player
    /// would see (background + collision + placed entities) and lets the
    /// designer drag entities from a palette onto the canvas. Saves to
    /// assets/data/content_&lt;roomId&gt;.json which the main game picks up
    /// next time it loads the room.
    /// </summary>
    public class EditorGame : Game
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
        private readonly List<UiButton> _buttons = new();

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
        private readonly List<Color[]> _bgUndo = new();   // per-stroke snapshots, oldest first
        private const int MaxUndo = 40;
        private bool _strokeActive;
        private bool _strokeChanged;                       // any pixel changed this stroke (no-op strokes drop their snapshot)
        private Point _lastStamp;                          // room px, previous stamp centre
        private (int zoom, int panX, int panY) _strokeView; // view at last stamp — a view jump must not Bresenham across it

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
                LayoutPalette();
                RelayoutButtons();
            }
            finally { _resizingGuard = false; }
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
                LayoutPalette();
                RelayoutButtons();
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
            BuildButtons();
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

        // Section names in display order. Add a section here and every entry
        // tagged with that name will appear under its header.
        // META sits last: it holds room-level markers rather than entities, and
        // appending it leaves every existing section exactly where the muscle
        // memory of anyone who has authored a room expects to find it.
        private static readonly string[] SectionOrder =
            { "WEAPONS", "KEY ITEMS", "ENEMIES", "DOORS", "OTHER", "META" };

        // Computed by LayoutPalette: the rectangle for each section header.
        private readonly List<(string name, Rectangle bounds)> _sectionHeaders = new();

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

            LayoutPalette();
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

        // ---- Palette scroll geometry --------------------------------------
        //
        // The panel's first PaletteTitleHeight pixels hold the "PALETTE"
        // title and never move; everything below scrolls. Layout, the scissor
        // rect, wheel clamping and click hit-testing all measure against
        // PaletteViewportRect, so they cannot disagree about where a row is —
        // which is exactly the off-by-the-scroll-offset bug that makes a
        // scrolled palette hand you the wrong entry.
        private const int PaletteTitleHeight = 30;
        private const int PaletteBottomInset = 8;

        // Total laid-out height of headers + entries (set by LayoutPalette).
        private int _paletteContentHeight;

        private static Rectangle PaletteViewportRect
        {
            get
            {
                int top = EditorLayout.PaletteY + PaletteTitleHeight;
                int bottom = EditorLayout.PaletteRect.Bottom - PaletteBottomInset;
                return new Rectangle(EditorLayout.PaletteX, top,
                                     EditorLayout.PaletteWidth, Math.Max(0, bottom - top));
            }
        }

        /// <summary>Where a laid-out palette row actually sits once scrolled.</summary>
        private Rectangle PaletteRowRect(Rectangle layoutBounds) =>
            new(layoutBounds.X, layoutBounds.Y - (int)_state.PaletteScrollY,
                layoutBounds.Width, layoutBounds.Height);

        /// <summary>True when any part of a scrolled row falls in the viewport.</summary>
        private static bool PaletteRowVisible(Rectangle scrolled)
        {
            var vp = PaletteViewportRect;
            return scrolled.Bottom > vp.Top && scrolled.Top < vp.Bottom;
        }

        /// <summary>
        /// Assign every section header and entry its UNSCROLLED position, and
        /// record the total content height. The scroll offset is applied at
        /// draw and hit-test time via PaletteRowRect — baking it in here would
        /// mean re-running layout on every wheel notch.
        /// </summary>
        private void LayoutPalette()
        {
            const int entryHeight = 44;
            const int headerHeight = 22;
            const int padding = 8;
            int x = EditorLayout.PaletteX + padding;
            int y = EditorLayout.PaletteY + PaletteTitleHeight;
            int w = EditorLayout.PaletteWidth - padding * 2;
            int contentTop = y;

            _sectionHeaders.Clear();

            foreach (var section in SectionOrder)
            {
                // Skip empty sections so headers never appear with no entries.
                bool any = false;
                foreach (var p in _state.Palette) if (p.Section == section) { any = true; break; }
                if (!any) continue;

                _sectionHeaders.Add((section, new Rectangle(x, y, w, headerHeight)));
                y += headerHeight + 4;

                foreach (var p in _state.Palette)
                {
                    if (p.Section != section) continue;
                    p.ScreenBounds = new Rectangle(x, y, w, entryHeight);
                    y += entryHeight + 4;
                }

                y += 6;  // spacer between sections
            }

            _paletteContentHeight = y - contentTop;

            // Re-clamp here as well as on wheel input: shrinking the window
            // grows the viewport-vs-content gap, and the scroll must come back
            // into range immediately, not on the next time the cursor happens
            // to pass over the palette.
            ClampPaletteScroll();
        }

        private void ClampPaletteScroll()
        {
            float maxScroll = Math.Max(0, _paletteContentHeight - PaletteViewportRect.Height);
            if (_state.PaletteScrollY < 0)         _state.PaletteScrollY = 0;
            if (_state.PaletteScrollY > maxScroll) _state.PaletteScrollY = maxScroll;
        }

        // Indices into _buttons for the labels we update at runtime, and for
        // the ones RelayoutButtons places outside their list order.
        private int _btnSnapIdx, _btnModeIdx, _btnPunchIdx, _btnNewRoomIdx, _btnImportIdx;

        // Where the "left bank" of buttons ends and the "right bank" begins,
        // in screen X. Recomputed each RelayoutButtons() — used to center
        // the room title between them.
        private int _leftBankRight, _rightBankLeft;

        private void BuildButtons()
        {
            // Use placeholder bounds; RelayoutButtons sets actual coords.
            _buttons.Add(new UiButton("< Prev", default, CyclePrevRoom));
            _buttons.Add(new UiButton("Next >", default, CycleNextRoom));

            _btnModeIdx = _buttons.Count;
            _buttons.Add(new UiButton("Mode: Place", default, ToggleMode));

            _buttons.Add(new UiButton("Validate", default, ValidateReachability));
            _buttons.Add(new UiButton("Doors", default, ValidateDoors));
            _buttons.Add(new UiButton("Puzzle", default, AnalyzePuzzle));

            _btnSnapIdx = _buttons.Count;
            _buttons.Add(new UiButton("Snap: OFF", default, ToggleSnap));

            _buttons.Add(new UiButton("Save", default, SaveCurrentRoom));

            _buttons.Add(new UiButton("Full (F11)", default, ToggleFullscreen));

            // Appended last so the hardcoded indices above keep their meaning;
            // RelayoutButtons decides where it actually sits on screen.
            _btnPunchIdx = _buttons.Count;
            _buttons.Add(new UiButton("Punch: OFF", default, ToggleAutoPunch));

            _btnNewRoomIdx = _buttons.Count;
            _buttons.Add(new UiButton("New Room", default, OpenNewRoomPicker));

            _btnImportIdx = _buttons.Count;
            _buttons.Add(new UiButton("Import", default, OpenImportPicker));

            RelayoutButtons();
        }

        /// <summary>
        /// Position buttons against the current window width. Left-anchored
        /// buttons start from x=8; right-anchored buttons stack inward from
        /// the right edge. Called from BuildButtons (initial), resize, and
        /// fullscreen toggle.
        /// </summary>
        private void RelayoutButtons()
        {
            int by = 12;
            int bh = EditorLayout.TopBarHeight - 24;
            int W = EditorLayout.WindowWidth;

            // Left bank: Prev | Next | New Room | Import | Mode
            // New Room and Import sit with the room-navigation buttons rather
            // than in the right-hand tool bank — they are how you get to a
            // room, not tools you use inside one. Import sits beside New Room
            // because it is the same act with one more step: it produces the
            // background PNG that New Room would otherwise need you to have.
            _buttons[0].Bounds = new Rectangle(8,   by,  80, bh);
            _buttons[1].Bounds = new Rectangle(96,  by,  80, bh);
            _buttons[_btnNewRoomIdx].Bounds = new Rectangle(186, by, 110, bh);
            _buttons[_btnImportIdx].Bounds  = new Rectangle(302, by,  86, bh);
            _buttons[2].Bounds = new Rectangle(394, by, 130, bh);
            _leftBankRight = 394 + 130;

            // Right bank (right-to-left):
            // Save | Snap | Punch | Puzzle | Doors | Validate | Fullscreen
            //
            // The Fullscreen button carries the short "Full (F11)" label: the
            // bank grew by a button and the room title only draws when it fits
            // in the gap left over between the two banks, so 50 px of slack
            // matters more here than the longer word does.
            int rx = W - 8;
            int saveW = 90, snapW = 110, punchW = 100, puzzW = 80, doorsW = 80, valW = 110, fsW = 100;
            _buttons[7].Bounds            = new Rectangle(rx - saveW,  by, saveW,  bh);   rx -= saveW  + 6;
            _buttons[6].Bounds            = new Rectangle(rx - snapW,  by, snapW,  bh);   rx -= snapW  + 6;
            _buttons[_btnPunchIdx].Bounds = new Rectangle(rx - punchW, by, punchW, bh);   rx -= punchW + 6;
            _buttons[5].Bounds            = new Rectangle(rx - puzzW,  by, puzzW,  bh);   rx -= puzzW  + 6;
            _buttons[4].Bounds            = new Rectangle(rx - doorsW, by, doorsW, bh);   rx -= doorsW + 6;
            _buttons[3].Bounds            = new Rectangle(rx - valW,   by, valW,   bh);   rx -= valW   + 6;
            _buttons[8].Bounds            = new Rectangle(rx - fsW,    by, fsW,    bh);   rx -= fsW    + 6;
            _rightBankLeft = rx;
        }

        private void ToggleMode()
        {
            _state.Mode = _state.Mode switch
            {
                EditorMode.Place => EditorMode.Paint,
                EditorMode.Paint => EditorMode.Erase,
                _                => EditorMode.Place,
            };
            _buttons[_btnModeIdx].Label = $"Mode: {_state.Mode}";

            // Switching out of Place mode cancels in-progress drag/move;
            // switching out of Erase mode closes any open brush stroke.
            if (_state.Mode != EditorMode.Place)
            {
                _state.Dragging = null;
                _state.IsMovingSelection = false;
            }
            if (_state.Mode != EditorMode.Erase) EndStroke();
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
            _bgUndo.Clear();
            _strokeActive = false;
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
        // A modal list of every unused RoomBG_*.png, built on the same
        // populate-in-Draw / consume-in-Update click-zone pattern the
        // inspector uses. State lives here rather than on EditorState because
        // it is transient view plumbing (like _inspectorButtons and
        // _discardArmed), not room data — nothing here survives a room switch
        // or reaches disk.
        //
        // Zero typing: the room id and display name come from the filename
        // (NewRoomFlow's derivation rule). The editor has no text field, and
        // the picker is designed so it never needs one.
        // ====================================================================

        private bool _newRoomOpen;
        private List<RoomCandidate> _newRoomCandidates = new();
        private float _newRoomScrollY;
        private int _newRoomContentHeight;
        private readonly List<(Rectangle bounds, Action action)> _newRoomButtons = new();

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
            _newRoomScrollY = 0f;
            _newRoomButtons.Clear();
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
            _newRoomButtons.Clear();
            _state.Status = status;
        }

        /// <summary>
        /// Modal input. Runs instead of every other handler while the picker is
        /// open, so a stray click can't reach the canvas underneath.
        /// </summary>
        private void HandleNewRoomPicker()
        {
            if (Pressed(Keys.Escape) || RightClicked())
            {
                CloseNewRoomPicker("New Room cancelled.");
                return;
            }

            int delta = _mouseNow.ScrollWheelValue - _mousePrev.ScrollWheelValue;
            if (delta != 0) _newRoomScrollY -= delta * 0.25f;
            float maxScroll = Math.Max(0, _newRoomContentHeight - NewRoomListRect.Height);
            _newRoomScrollY = Math.Clamp(_newRoomScrollY, 0, maxScroll);

            if (!LeftClicked()) return;
            var p = new Point(_mouseNow.X, _mouseNow.Y);
            for (int i = _newRoomButtons.Count - 1; i >= 0; i--)
            {
                if (_newRoomButtons[i].bounds.Contains(p))
                {
                    _newRoomButtons[i].action();
                    return;
                }
            }
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
            _newRoomButtons.Clear();

            var result = CreateAndOpenRoom(candidate);
            if (!result.Ok) _state.Status = result.Message;
        }

        // Panel geometry. Computed from the window rather than stored so a
        // resize while the picker is open can't leave the click zones and the
        // drawn rows disagreeing.
        private static Rectangle NewRoomPanelRect
        {
            get
            {
                int w = Math.Min(660, EditorLayout.WindowWidth - 80);
                int h = Math.Min(540, EditorLayout.WindowHeight - 120);
                return new Rectangle((EditorLayout.WindowWidth - w) / 2,
                                     (EditorLayout.WindowHeight - h) / 2, w, h);
            }
        }

        private const int NewRoomTitleHeight = 56;
        private const int NewRoomFooterHeight = 44;

        private static Rectangle NewRoomListRect
        {
            get
            {
                var p = NewRoomPanelRect;
                return new Rectangle(p.X + 10, p.Y + NewRoomTitleHeight,
                                     p.Width - 20,
                                     Math.Max(0, p.Height - NewRoomTitleHeight - NewRoomFooterHeight));
            }
        }

        /// <summary>
        /// The picker: a dimmed screen, a panel, one row per candidate. Rows
        /// register their click zones here (consumed by HandleNewRoomPicker on
        /// the next frame — the inspector's pattern).
        /// </summary>
        private void DrawNewRoomPicker()
        {
            if (!_newRoomOpen) return;

            _newRoomButtons.Clear();

            // Dim everything behind the modal so it reads as blocking input,
            // which it does.
            FillRect(new Rectangle(0, 0, EditorLayout.WindowWidth, EditorLayout.WindowHeight),
                     new Color(0, 0, 0, 170));

            var panel = NewRoomPanelRect;
            FillRect(panel, new Color(30, 33, 42));
            DrawRectOutline(panel, new Color(120, 130, 160));

            DrawText("NEW ROOM — pick an unused background",
                new Vector2(panel.X + 14, panel.Y + 12), new Color(255, 220, 110));
            DrawText("Content/RoomBG_*.png not already claimed by a room in rooms.json",
                new Vector2(panel.X + 14, panel.Y + 32), new Color(150, 160, 185));

            var list = NewRoomListRect;
            int rowH = 46;
            int y = list.Y - (int)_newRoomScrollY;
            _newRoomContentHeight = _newRoomCandidates.Count * (rowH + 4);

            if (_newRoomCandidates.Count == 0)
            {
                // The intended path for a room that has no PNG yet is the
                // screenshot import (EDITOR_REVIEW item A / PR 5), so say so
                // rather than leaving an empty box.
                DrawText("No unused RoomBG_*.png in Content/.",
                    new Vector2(list.X + 6, list.Y + 8), new Color(230, 230, 240));
                DrawText("Every background is already claimed by a room.",
                    new Vector2(list.X + 6, list.Y + 30), new Color(180, 185, 200));
                DrawText("To add a room from a screenshot, use Import instead —",
                    new Vector2(list.X + 6, list.Y + 58), new Color(150, 160, 185));
                DrawText("it writes the RoomBG_*.png this picker lists.",
                    new Vector2(list.X + 6, list.Y + 78), new Color(150, 160, 185));
            }

            foreach (var candidate in _newRoomCandidates)
            {
                var row = new Rectangle(list.X, y, list.Width, rowH);
                y += rowH + 4;

                // Cull rows scrolled out of the list viewport — and skip their
                // click zones with them, so an invisible row can't be clicked.
                if (row.Bottom <= list.Top || row.Top >= list.Bottom) continue;

                var captured = candidate;
                bool hover = captured.CanCreate && row.Contains(_mouseNow.X, _mouseNow.Y)
                             && list.Contains(_mouseNow.X, _mouseNow.Y);

                Color bg = !captured.CanCreate ? new Color(46, 36, 40)
                         : hover               ? new Color(60, 75, 110)
                                               : new Color(40, 46, 60);
                FillRect(row, bg);
                DrawRectOutline(row, new Color(90, 100, 130));

                DrawText(TruncateText(captured.BackgroundAsset + ".png", row.Width - 20),
                    new Vector2(row.X + 8, row.Y + 5),
                    captured.CanCreate ? Color.White : new Color(190, 150, 150));

                string sub = captured.CanCreate
                    ? $"-> {captured.RoomId}   \"{captured.DisplayName}\""
                    : $"unavailable: {captured.Problem}";
                DrawText(TruncateText(sub, row.Width - 20),
                    new Vector2(row.X + 8, row.Y + 25),
                    captured.CanCreate ? new Color(180, 200, 230) : new Color(255, 140, 140));

                if (captured.CanCreate)
                    _newRoomButtons.Add((row, () => CreateRoom(captured)));
            }

            // Footer: Cancel. Escape and right-click do the same thing; the
            // button is here because a modal with no visible way out is a
            // usability trap.
            var cancel = new Rectangle(panel.Right - 110, panel.Bottom - NewRoomFooterHeight + 6, 100, 28);
            bool cancelHover = cancel.Contains(_mouseNow.X, _mouseNow.Y);
            FillRect(cancel, cancelHover ? new Color(70, 78, 100) : new Color(50, 55, 70));
            DrawRectOutline(cancel, new Color(110, 120, 150));
            var cz = MeasureText("Cancel");
            DrawText("Cancel",
                new Vector2(cancel.X + (cancel.Width - cz.X) / 2, cancel.Y + (cancel.Height - cz.Y) / 2),
                Color.White);
            _newRoomButtons.Add((cancel, () => CloseNewRoomPicker("New Room cancelled.")));

            DrawText("Esc / right-click cancels",
                new Vector2(panel.X + 14, panel.Bottom - NewRoomFooterHeight + 12),
                new Color(150, 160, 185));
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
        private float _importScrollY;
        private int _importContentHeight;
        private readonly List<(Rectangle bounds, Action action)> _importButtons = new();

        // Session preference, not room data: it survives closing and reopening
        // the picker, because importing a set of screenshots is one decision
        // made once, not one per file. ON by default — the sources are captures
        // of a CPC game, so snapping to the hardware palette is the answer
        // nearly always.
        private bool _importQuantize = true;

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
            _importScrollY = 0f;
            _importButtons.Clear();
            _importOpen = true;

            int usable = 0;
            foreach (var c in _importCandidates) if (c.CanCreate) usable++;
            _state.Status = _importCandidates.Count == 0
                ? "Import: assets/import/ holds no .jpg/.jpeg/.png — drop a screenshot in and click Import again."
                : $"Import: pick a screenshot ({usable} of {_importCandidates.Count} importable). Esc or right-click cancels.";
        }

        private void CloseImportPicker(string status)
        {
            _importOpen = false;
            _importButtons.Clear();
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

            int delta = _mouseNow.ScrollWheelValue - _mousePrev.ScrollWheelValue;
            if (delta != 0) _importScrollY -= delta * 0.25f;
            float maxScroll = Math.Max(0, _importContentHeight - ImportListRect.Height);
            _importScrollY = Math.Clamp(_importScrollY, 0, maxScroll);

            if (!LeftClicked()) return;
            var p = new Point(_mouseNow.X, _mouseNow.Y);
            for (int i = _importButtons.Count - 1; i >= 0; i--)
            {
                if (_importButtons[i].bounds.Contains(p))
                {
                    _importButtons[i].action();
                    return;
                }
            }
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
            _importButtons.Clear();

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
        private void FinishImport(ImportCandidate candidate, Color[] pixels)
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
                _state.Status = $"Import: writing {candidate.BackgroundAsset}.png failed — {ex.Message}";
                return;
            }

            // PNG first, registration second. Should the registration fail, the
            // leftover is an unused background in Content/ — which is exactly
            // what the New Room picker lists, so the user can finish the job
            // from there. The other order would put a room in rooms.json
            // naming an asset that isn't on disk, which the game refuses to
            // start with.
            var result = CreateAndOpenRoom(candidate);
            string mode = _importQuantize ? "CPC" : "raw";
            _state.Status = result.Ok
                ? $"Imported {candidate.FileName} [{mode}] -> {candidate.BackgroundAsset}.png. {result.Message}"
                : $"Wrote {candidate.BackgroundAsset}.png but registration failed: {result.Message} " +
                  "The PNG is now an unused background — New Room can finish it.";
        }

        // Panel geometry. Computed from the window rather than stored, so a
        // resize while the picker is open can't leave the click zones and the
        // drawn rows disagreeing. Wider than the New Room panel because each
        // row carries a filename, a size and a derived room id.
        private static Rectangle ImportPanelRect
        {
            get
            {
                int w = Math.Min(760, EditorLayout.WindowWidth - 80);
                int h = Math.Min(560, EditorLayout.WindowHeight - 120);
                return new Rectangle((EditorLayout.WindowWidth - w) / 2,
                                     (EditorLayout.WindowHeight - h) / 2, w, h);
            }
        }

        private const int ImportTitleHeight = 56;
        private const int ImportToggleHeight = 36;
        private const int ImportFooterHeight = 44;

        private static Rectangle ImportToggleRect
        {
            get
            {
                var p = ImportPanelRect;
                return new Rectangle(p.X + 10, p.Y + ImportTitleHeight, p.Width - 20, 28);
            }
        }

        private static Rectangle ImportListRect
        {
            get
            {
                var p = ImportPanelRect;
                return new Rectangle(p.X + 10, p.Y + ImportTitleHeight + ImportToggleHeight,
                                     p.Width - 20,
                                     Math.Max(0, p.Height - ImportTitleHeight - ImportToggleHeight - ImportFooterHeight));
            }
        }

        /// <summary>
        /// The picker: a dimmed screen, a panel, the quantize toggle, one row
        /// per file in assets/import/. Rows register their click zones here and
        /// HandleImportPicker consumes them next frame — the inspector's
        /// pattern, and the New Room picker's.
        /// </summary>
        private void DrawImportPicker()
        {
            if (!_importOpen) return;

            _importButtons.Clear();

            FillRect(new Rectangle(0, 0, EditorLayout.WindowWidth, EditorLayout.WindowHeight),
                     new Color(0, 0, 0, 170));

            var panel = ImportPanelRect;
            FillRect(panel, new Color(30, 33, 42));
            DrawRectOutline(panel, new Color(120, 130, 160));

            DrawText("IMPORT SCREENSHOT — pick a file from assets/import/",
                new Vector2(panel.X + 14, panel.Y + 12), new Color(255, 220, 110));
            DrawText(TruncateText(EditorPaths.RepoImportDir, panel.Width - 28),
                new Vector2(panel.X + 14, panel.Y + 32), new Color(150, 160, 185));

            DrawImportQuantizeToggle();

            var list = ImportListRect;
            int rowH = 46;
            int y = list.Y - (int)_importScrollY;
            _importContentHeight = _importCandidates.Count * (rowH + 4);

            if (_importCandidates.Count == 0)
            {
                DrawText("Nothing to import.",
                    new Vector2(list.X + 6, list.Y + 8), new Color(230, 230, 240));
                DrawText("Drop a .jpg / .jpeg / .png screenshot into assets/import/",
                    new Vector2(list.X + 6, list.Y + 34), new Color(180, 185, 200));
                DrawText("and click Import again. The file name becomes the room:",
                    new Vector2(list.X + 6, list.Y + 54), new Color(180, 185, 200));
                DrawText("Chateau3.jpg  ->  RoomBG_Chateau3.png  ->  chateau_3",
                    new Vector2(list.X + 6, list.Y + 80), new Color(150, 160, 185));
            }

            foreach (var candidate in _importCandidates)
            {
                var row = new Rectangle(list.X, y, list.Width, rowH);
                y += rowH + 4;

                // Cull rows scrolled out of the list viewport — and skip their
                // click zones with them, so an invisible row can't be clicked.
                if (row.Bottom <= list.Top || row.Top >= list.Bottom) continue;

                var captured = candidate;
                bool hover = captured.CanCreate && row.Contains(_mouseNow.X, _mouseNow.Y)
                             && list.Contains(_mouseNow.X, _mouseNow.Y);

                Color bg = !captured.CanCreate ? new Color(46, 36, 40)
                         : hover               ? new Color(60, 75, 110)
                                               : new Color(40, 46, 60);
                FillRect(row, bg);
                DrawRectOutline(row, new Color(90, 100, 130));

                DrawText(TruncateText($"{captured.FileName}   {captured.SizeLabel}", row.Width - 20),
                    new Vector2(row.X + 8, row.Y + 5),
                    captured.CanCreate ? Color.White : new Color(190, 150, 150));

                // "[crop]" marks the sources that open the crop step instead of
                // importing on the click, so that is never a surprise.
                string sub = captured.CanCreate
                    ? (captured.NeedsCrop ? "[crop] " : "") +
                      $"-> {captured.BackgroundAsset}.png   ->   {captured.RoomId}   \"{captured.DisplayName}\""
                    : $"unavailable: {captured.Problem}";
                DrawText(TruncateText(sub, row.Width - 20),
                    new Vector2(row.X + 8, row.Y + 25),
                    captured.CanCreate ? new Color(180, 200, 230) : new Color(255, 140, 140));

                if (captured.CanCreate)
                    _importButtons.Add((row, () => RunImport(captured)));
            }

            var cancel = new Rectangle(panel.Right - 110, panel.Bottom - ImportFooterHeight + 6, 100, 28);
            bool cancelHover = cancel.Contains(_mouseNow.X, _mouseNow.Y);
            FillRect(cancel, cancelHover ? new Color(70, 78, 100) : new Color(50, 55, 70));
            DrawRectOutline(cancel, new Color(110, 120, 150));
            var cz = MeasureText("Cancel");
            DrawText("Cancel",
                new Vector2(cancel.X + (cancel.Width - cz.X) / 2, cancel.Y + (cancel.Height - cz.Y) / 2),
                Color.White);
            _importButtons.Add((cancel, () => CloseImportPicker("Import cancelled.")));

            DrawText("Esc / right-click cancels   |   sources are never modified or deleted",
                new Vector2(panel.X + 14, panel.Bottom - ImportFooterHeight + 12),
                new Color(150, 160, 185));
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
        private readonly List<(Rectangle bounds, Action action)> _cropButtons = new();

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
            _cropButtons.Clear();

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
            _cropButtons.Clear();
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

            int wheel = _mouseNow.ScrollWheelValue - _mousePrev.ScrollWheelValue;
            if (wheel != 0)
            {
                _cropRect = ImageImport.StepCropWidth(_cropRect, Math.Sign(wheel), _cropSrcW, _cropSrcH);
                _state.Status = CropSelectionSummary();
            }

            var mouse = new Point(_mouseNow.X, _mouseNow.Y);
            var fit = CropFitRect;

            // Buttons first: the footer sits outside the image, but checking it
            // first means a Confirm click can never also start a drag.
            if (LeftClicked())
            {
                for (int i = _cropButtons.Count - 1; i >= 0; i--)
                {
                    if (_cropButtons[i].bounds.Contains(mouse))
                    {
                        _cropButtons[i].action();
                        return;
                    }
                }

                if (fit.Contains(mouse))
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

            _cropButtons.Clear();
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

            // Header strip over the top bar: what is being cropped, and into what.
            var header = new Rectangle(0, 0, EditorLayout.WindowWidth, EditorLayout.TopBarHeight);
            FillRect(header, new Color(24, 26, 32));
            DrawRectOutline(header, new Color(120, 130, 160));
            DrawText(TruncateText(
                    $"CROP  {_cropCandidate.FileName}  ({_cropSrcW}x{_cropSrcH})  ->  " +
                    $"{_cropCandidate.RoomId}   \"{_cropCandidate.DisplayName}\"",
                    header.Width - 28),
                new Vector2(14, 8), new Color(255, 220, 110));
            float scale = _cropRect.Width / (float)ImageImport.RoomWidth;
            DrawText(TruncateText(
                    $"selection {_cropRect.Width}x{_cropRect.Height} at ({_cropRect.X}, {_cropRect.Y})  ->  " +
                    $"{ImageImport.RoomWidth}x{ImageImport.RoomHeight} ({scale:0.00}x down)   |   " +
                    $"CPC quantize {(_importQuantize ? "ON" : "OFF")}   |   " +
                    // Where the box STARTED. Left unchanged as the user drags,
                    // because that is what it is claiming — not "this is the
                    // preset", but "this is what you were handed".
                    ImageImport.DescribeCropPreset(_cropPresetOrigin, _cropSrcW, _cropSrcH),
                    header.Width - 28),
                new Vector2(14, 30), new Color(160, 175, 200));

            // Footer strip over the status bar: the controls, and the two
            // buttons. Escape and Enter do the same as the buttons; the buttons
            // exist because a modal with no visible way out is a usability trap.
            var footer = new Rectangle(0, EditorLayout.WindowHeight - EditorLayout.StatusBarHeight,
                                       EditorLayout.WindowWidth, EditorLayout.StatusBarHeight);
            FillRect(footer, new Color(20, 22, 28));
            DrawRectOutline(footer, new Color(120, 130, 160));
            DrawText("drag to move   |   wheel resizes (20:9 locked)   |   Enter confirms   |   Esc / right-click cancels",
                new Vector2(8, footer.Y + 8), new Color(200, 200, 220));

            int by = footer.Y + 3;
            int bh = EditorLayout.StatusBarHeight - 6;
            var confirm = new Rectangle(EditorLayout.WindowWidth - 108, by, 100, bh);
            var cancel = new Rectangle(EditorLayout.WindowWidth - 216, by, 100, bh);
            DrawCropButton(confirm, "Confirm", new Color(60, 100, 70), ConfirmCrop);
            DrawCropButton(cancel, "Cancel", new Color(60, 55, 70),
                () => CloseCrop("Import cancelled — nothing was written."));

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

        private void DrawCropButton(Rectangle bounds, string label, Color baseColor, Action action)
        {
            bool hover = bounds.Contains(_mouseNow.X, _mouseNow.Y);
            FillRect(bounds, hover
                ? new Color(baseColor.R + 25, baseColor.G + 25, baseColor.B + 30)
                : baseColor);
            DrawRectOutline(bounds, new Color(120, 135, 165));
            var sz = MeasureText(label);
            DrawText(label,
                new Vector2(bounds.X + (bounds.Width - sz.X) / 2, bounds.Y + (bounds.Height - sz.Y) / 2),
                Color.White);
            _cropButtons.Add((bounds, action));
        }

        /// <summary>
        /// The CPC quantize checkbox. A whole row rather than a small box, so
        /// the click target is obvious and the "why" fits beside it.
        /// </summary>
        private void DrawImportQuantizeToggle()
        {
            var rect = ImportToggleRect;
            bool hover = rect.Contains(_mouseNow.X, _mouseNow.Y);
            FillRect(rect, hover ? new Color(60, 75, 110) : new Color(40, 46, 60));
            DrawRectOutline(rect, new Color(90, 100, 130));

            var box = new Rectangle(rect.X + 7, rect.Y + 7, 14, 14);
            DrawRectOutline(box, new Color(170, 180, 205));
            if (_importQuantize)
                FillRect(new Rectangle(box.X + 3, box.Y + 3, 8, 8), new Color(120, 230, 140));

            DrawText(TruncateText(
                    _importQuantize
                        ? "CPC quantize ON — snap to the 27 hardware colours (removes JPEG noise)"
                        : "CPC quantize OFF — source colours pass through untouched",
                    rect.Width - 40),
                new Vector2(rect.X + 30, rect.Y + 5),
                _importQuantize ? new Color(210, 230, 210) : new Color(200, 200, 215));

            _importButtons.Add((rect, ToggleImportQuantize));
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

            // Esc returns to the room view. It deliberately does NOT exit the
            // editor here — the exit-with-autosave path lives in room view,
            // where the unsaved work it protects actually is.
            if (Pressed(Keys.Escape)) { LeaveMapMode(); return; }

            // Ctrl+S in map mode saves the ARRANGEMENT. Room Ctrl+S is
            // untouched and still saves the room: two modes, two things to
            // save, one key that means "persist what is in front of you".
            bool ctrl = _keysNow.IsKeyDown(Keys.LeftControl) || _keysNow.IsKeyDown(Keys.RightControl);
            if (ctrl && Pressed(Keys.S)) { SaveWorldMap(); return; }

            // N and I open the New Room and Import pickers — the SAME overlays
            // the top bar opens, invoked from here because the map is where
            // "the world is missing a room" is a thing you notice. Keys rather
            // than buttons for the same reason Tab is: the bar is full.
            //
            // Their discard guards are untouched and still concern the CURRENT
            // ROOM's unsaved edits, which exist just as much while the map is
            // up — creating a room loads it, and loading replaces them.
            if (!ctrl && Pressed(Keys.N)) { OpenNewRoomPicker(); return; }
            if (!ctrl && Pressed(Keys.I)) { OpenImportPicker(); return; }

            var mouse = new Point(_mouseNow.X, _mouseNow.Y);
            bool overBoard = MapBoardRect.Contains(mouse);

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
            if (Pressed(Keys.Left))  PanMap(-nudge, 0);
            if (Pressed(Keys.Right)) PanMap(nudge, 0);
            if (Pressed(Keys.Up))    PanMap(0, -nudge);
            if (Pressed(Keys.Down))  PanMap(0, nudge);

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

            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            DrawTopBar();
            _spriteBatch.End();

            // Scissored, so a box panned half off the board is cut at the edge
            // instead of painting over the top bar.
            GraphicsDevice.ScissorRectangle = MapBoardRect;
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp, rasterizerState: ScissorOn);
            DrawMapBoard();
            _spriteBatch.End();

            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            DrawStatusBar();
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
            _buttons[_btnSnapIdx].Label = _state.SnapEnabled ? "Snap: 8px" : "Snap: OFF";
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
            _buttons[_btnPunchIdx].Label = _state.AutoPunch ? "Punch: ON" : "Punch: OFF";
            _state.Status = _state.AutoPunch
                ? "Auto-punch ON: drops and moves clear the background under the placement."
                : "Auto-punch OFF: use P (or the inspector row) to punch explicitly.";
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

            // The New Room and Import pickers are modal: while one is open it
            // consumes every input, so a click meant for a candidate row can't
            // fall through to the canvas behind it and Escape closes the picker
            // rather than the editor. At most one is ever open — the top-bar
            // buttons that open them are themselves unreachable from here.
            if (_newRoomOpen)
            {
                HandleNewRoomPicker();
                base.Update(gameTime);
                return;
            }

            if (_importOpen)
            {
                HandleImportPicker();
                base.Update(gameTime);
                return;
            }

            // The crop step is the same kind of modal, reached from the import
            // picker rather than from a button. Nothing has been written to
            // disk while it is open, so its Escape is a plain cancel.
            if (_cropOpen)
            {
                HandleCropOverlay();
                base.Update(gameTime);
                return;
            }

            // Tab flips between the room editor and the world map, from either
            // side. Handled before both, and returning immediately, so the
            // press that changed mode is not also read by the mode it landed
            // in. Tab rather than a button because the top bar is full — its
            // restructure is a later PR, and until then the map is a keybind
            // advertised in the status line.
            if (Pressed(Keys.Tab))
            {
                ToggleMapMode();
                base.Update(gameTime);
                return;
            }

            // Map mode suspends room editing entirely: no palette, no canvas,
            // no paint, no punch, no room keyboard shortcuts. Its own handler
            // is the only thing that runs.
            if (_mapMode)
            {
                HandleMapInput();
                base.Update(gameTime);
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
            if (Pressed(Keys.Escape) && ConfirmDiscardUnsavedEdits(includeMap: true)) Exit();

            HandleButtons();
            HandleInspectorScroll();
            // Before HandlePaletteInput below, so a wheel notch and the click
            // that follows it in the same frame agree on the scroll offset.
            HandlePaletteScroll();
            // Inspector buttons take priority over the canvas: clicking a
            // cycle button shouldn't deselect the entity it's editing.
            if (HandleInspectorClicks()) { /* swallowed */ }
            else
            {
                HandleCanvasView();
                HandleCanvasInput();
                HandlePaletteInput();
            }
            HandleKeyboardShortcuts();

            base.Update(gameTime);
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

        private void HandleButtons()
        {
            if (!LeftClicked()) return;
            var p = new Point(_mouseNow.X, _mouseNow.Y);
            foreach (var b in _buttons)
            {
                if (b.Bounds.Contains(p))
                {
                    b.OnClick();
                    return;
                }
            }
        }

        /// <summary>
        /// Mouse wheel over the inspector pane scrolls its content. The
        /// content height is computed during DrawInspector — we clamp the
        /// scroll position into [0, contentHeight - viewportHeight] each
        /// frame so resizing the window can't leave the scroll out of range.
        /// </summary>
        private void HandleInspectorScroll()
        {
            if (!EditorLayout.InspectorRect.Contains(_mouseNow.X, _mouseNow.Y)) return;

            int delta = _mouseNow.ScrollWheelValue - _mousePrev.ScrollWheelValue;
            if (delta != 0)
            {
                // SDL/MonoGame reports ~120 per notch; convert to ~30 px.
                _state.InspectorScrollY -= delta * 0.25f;
            }

            int viewportH = EditorLayout.InspectorRect.Height - 40;
            float maxScroll = Math.Max(0, _inspectorContentHeight - viewportH);
            if (_state.InspectorScrollY < 0)         _state.InspectorScrollY = 0;
            if (_state.InspectorScrollY > maxScroll) _state.InspectorScrollY = maxScroll;
        }

        /// <summary>
        /// Returns true if a left-click landed on an inspector cycle button
        /// (and the click was consumed). Iterated in reverse so the most
        /// recently-drawn button wins on overlap (defensive).
        /// </summary>
        private bool HandleInspectorClicks()
        {
            if (!LeftClicked()) return false;
            var p = new Point(_mouseNow.X, _mouseNow.Y);
            for (int i = _inspectorButtons.Count - 1; i >= 0; i--)
            {
                if (_inspectorButtons[i].bounds.Contains(p))
                {
                    _inspectorButtons[i].action();
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Mouse wheel over the palette pane scrolls its entry list. Same
        /// shape as HandleInspectorScroll — and the two can't fight, because
        /// each claims its own screen region, as does the canvas's wheel zoom
        /// (HandleCanvasView only acts inside CanvasRect).
        /// </summary>
        private void HandlePaletteScroll()
        {
            if (!EditorLayout.PaletteRect.Contains(_mouseNow.X, _mouseNow.Y)) return;

            int delta = _mouseNow.ScrollWheelValue - _mousePrev.ScrollWheelValue;
            if (delta != 0)
            {
                // SDL/MonoGame reports ~120 per notch; convert to ~30 px.
                _state.PaletteScrollY -= delta * 0.25f;
            }

            ClampPaletteScroll();
        }

        private void HandlePaletteInput()
        {
            // Palette is interactive only in Place mode.
            if (_state.Mode != EditorMode.Place) return;
            if (!LeftClicked()) return;

            var p = new Point(_mouseNow.X, _mouseNow.Y);

            // Only visible pixels are clickable. The scissor in Draw clips
            // rows to the viewport, so a row scrolled up under the title or
            // down past the panel's bottom edge isn't on screen and must not
            // answer clicks either — hence the viewport test, and hit-testing
            // the SCROLLED rect rather than the laid-out one.
            if (!PaletteViewportRect.Contains(p)) return;

            foreach (var entry in _state.Palette)
            {
                if (PaletteRowRect(entry.ScreenBounds).Contains(p))
                {
                    _state.Dragging = entry;
                    _state.SelectedPlacement = null;
                    _state.IsMovingSelection = false;
                    _state.Status = $"Dragging: {entry.Label}. Click on canvas to drop, right-click to cancel.";
                    return;
                }
            }
        }

        /// <summary>
        /// Canvas view navigation, active in every mode: mouse-wheel zooms
        /// in/out anchored at the cursor; middle-drag pans while zoomed.
        /// </summary>
        private void HandleCanvasView()
        {
            var pt = new Point(_mouseNow.X, _mouseNow.Y);

            // Wheel zoom (only when the cursor is over the canvas — the
            // inspector consumes its own wheel events by region).
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

            // Right-click cancels palette drag.
            if (RightClicked() && _state.Dragging != null)
            {
                _state.Dragging = null;
                _state.Status = "Drag cancelled.";
                return;
            }

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
                    if (_state.AutoPunch && _state.SelectedPlacement != null)
                        PunchBackground(_state.SelectedPlacement);
                }
                // Same for the spawn marker — it has no footprint to punch,
                // so ending the drag is all there is to do.
                if (LeftReleased()) _state.IsMovingSpawn = false;
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

                    // Auto-punch re-cuts at the final position once the drag
                    // ends (not per-frame while dragging, which would smear a
                    // trench along the whole path). The hole left at the drop
                    // position stays: the background there was due for clearing
                    // anyway, and cutting more than needed is harmless. If it
                    // isn't, Erase mode's right-drag restores from the last
                    // saved state.
                    if (_state.AutoPunch) PunchBackground(_state.SelectedPlacement);
                }
            }
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
            if (!EditorLayout.IsInsideCanvas(screenPt)) return;

            bool drawSolid = _mouseNow.LeftButton == ButtonState.Pressed;
            bool drawEmpty = _mouseNow.RightButton == ButtonState.Pressed;
            if (!drawSolid && !drawEmpty) return;

            Vector2 game = EditorLayout.ScreenToGame(screenPt);
            int tx = (int)(game.X / TileConfig.TILE_SIZE);
            int ty = (int)(game.Y / TileConfig.TILE_SIZE);
            if (tx < 0 || ty < 0 || tx >= _state.CollisionMap.Width || ty >= _state.CollisionMap.Height)
                return;

            int desired = drawSolid ? TileConfig.WALL_DARK_GRAY : TileConfig.EMPTY;
            if (_state.CollisionMap.GetTile(tx, ty) == desired) return;

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
                if (_bgUndo.Count >= MaxUndo) _bgUndo.RemoveAt(0);
                _bgUndo.Add((Color[])_bgPixels.Clone());
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
        /// Close the current stroke. A stroke that changed nothing drops its
        /// undo snapshot so no-op clicks can't evict real history.
        /// </summary>
        private void EndStroke()
        {
            if (_strokeActive && !_strokeChanged && _bgUndo.Count > 0)
                _bgUndo.RemoveAt(_bgUndo.Count - 1);
            _strokeActive = false;
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
        /// Clear the background pixels under the given placement's 24x24
        /// footprint to transparent. One undo snapshot per punch (Ctrl+Z works
        /// like an erase stroke). No-op (with status explanation) when the room
        /// has no editable background PNG, and snapshot-free when every pixel
        /// in the rect is already transparent.
        ///
        /// Why this exists: rooms are built from screenshots of the original
        /// game, which still contain its baked-in artwork (doors especially).
        /// Once a real entity is placed over such a spot, those pixels would
        /// bleed through the entity's animation frames — so they get cut out.
        /// Transparent renders as black in-game, which is what we want here.
        /// </summary>
        private void PunchBackground(Placement p)
        {
            // Same guard HandleEraseInput uses: no raw PNG behind this room
            // means there are no pixels we're allowed to edit (the XNB
            // fallback is display-only and Save would have nothing to write).
            if (_bgPixels == null || _bgOriginal == null || _currentBackground == null)
            {
                _state.Status = "Punch: this room has no editable background PNG.";
                return;
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
                return;
            }

            // Pre-scan. Punching an already-clear rect changes nothing, and a
            // no-op action must not push a snapshot — that would evict real
            // history off the end of the MaxUndo ring (same rule EndStroke
            // applies to no-op brush strokes).
            bool anyOpaque = false;
            for (int y = y0; y < y1 && !anyOpaque; y++)
            for (int x = x0; x < x1 && !anyOpaque; x++)
                if (_bgPixels[y * texW + x] != Color.Transparent) anyOpaque = true;

            if (!anyOpaque)
            {
                _state.Status = $"Punch: nothing to punch — background under {p.DisplayName} is already clear.";
                return;
            }

            if (_bgUndo.Count >= MaxUndo) _bgUndo.RemoveAt(0);
            _bgUndo.Add((Color[])_bgPixels.Clone());

            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                _bgPixels[y * texW + x] = Color.Transparent;

            _currentBackground.SetData(_bgPixels);
            _state.BackgroundDirty = true;
            _discardArmed = false;         // new edits re-arm the discard guard
            _state.Status = $"Punched background under {p.DisplayName} at ({(int)p.Position.X}, {(int)p.Position.Y}) — Ctrl+Z undoes, Save writes PNG.";
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
                    _state.Placements.Remove(_state.SelectedPlacement);
                    _state.Status = $"Deleted {_state.SelectedPlacement.DisplayName}";
                    _state.SelectedPlacement = null;
                    _state.PlacementsDirty = true;
                    _discardArmed = false;   // new edits re-arm the discard guard
                }
                else if (_state.SpawnSelected && _state.PlayerSpawn.HasValue)
                {
                    // Back to null, not to (160, 80): the next save then omits
                    // the "playerSpawn" key entirely and the room falls back to
                    // RoomLayoutLoader.DefaultPlayerSpawn in game.
                    _state.PlayerSpawn = null;
                    _state.SpawnSelected = false;
                    _state.IsMovingSpawn = false;
                    _state.HasValidated = false;
                    _state.PlacementsDirty = true;
                    _discardArmed = false;
                    _state.Status = "Cleared the player spawn — this room falls back to (160, 80). Save to persist.";
                }
            }

            // Ctrl+S → save.
            if (ctrl && Pressed(Keys.S)) SaveCurrentRoom();

            // Ctrl+Z → undo the last erase/restore stroke (background only).
            if (ctrl && Pressed(Keys.Z) && _bgUndo.Count > 0 &&
                _bgPixels != null && _currentBackground != null)
            {
                // Close any in-progress stroke first: a still-held drag then
                // starts a fresh stroke with a fresh snapshot next frame,
                // instead of silently merging into the popped history entry.
                EndStroke();
                if (_bgUndo.Count > 0)
                {
                    var snap = _bgUndo[^1];
                    _bgUndo.RemoveAt(_bgUndo.Count - 1);
                    bool differs = !snap.AsSpan().SequenceEqual(_bgPixels);
                    Array.Copy(snap, _bgPixels, snap.Length);
                    _currentBackground.SetData(_bgPixels);
                    if (differs) _state.BackgroundDirty = true;
                    _discardArmed = false;
                    _state.Status = $"Undid background stroke ({_bgUndo.Count} more in history).";
                }
            }

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
            if (Pressed(Keys.F11)) ToggleFullscreen();
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
                _state.PlayerSpawn = ClampPointToRoom(gamePos);
                _state.SelectedPlacement = null;
                _state.IsMovingSelection = false;
                _state.SpawnSelected = true;
                _state.Dragging = null;
                _state.HasValidated = false;    // the flood-fill origin moved
                _state.PlacementsDirty = true;
                _discardArmed = false;          // new edits re-arm the discard guard
                _state.Status =
                    $"Player spawn set to ({(int)_state.PlayerSpawn.Value.X}, {(int)_state.PlayerSpawn.Value.Y}) — " +
                    "drag to move, Delete to clear, Ctrl+S writes layout JSON.";
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
            _state.Placements.Add(placement);
            _state.SelectedPlacement = placement;
            _state.Expand(placement.Id);
            _state.Dragging = null;
            _state.HasValidated = false;
            _state.HasValidatedDoors = false;
            _state.PlacementsDirty = true;
            _discardArmed = false;         // new edits re-arm the discard guard
            _state.Status = $"Placed {placement.DisplayName} at ({(int)placement.Position.X}, {(int)placement.Position.Y})";

            // Auto-punch runs AFTER the clamp, so the hole matches the position
            // the placement actually ended up at. It overwrites the status line
            // with its own report — that's deliberate; the punch is the part
            // that touched the PNG and therefore the part worth reporting.
            if (_state.AutoPunch) PunchBackground(placement);
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
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            DrawNewRoomPicker();
            DrawImportPicker();
            _spriteBatch.End();

            // The crop step owns its own two passes — the source image wants
            // LINEAR filtering (it is shown at an arbitrary fractional scale,
            // where point sampling drops whole rows and makes a screenshot
            // unrecognisable), its chrome wants the PointClamp everything else
            // uses. No-op unless the crop step is open.
            DrawCropOverlay();

            base.Draw(gameTime);
        }

        private void DrawRoomMode()
        {
            // Pass 1: UI chrome and the canvas frame.
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            DrawTopBar();
            DrawPaletteChrome();
            FillRect(EditorLayout.CanvasRect, Color.Black);
            DrawRectOutline(InflateRect(EditorLayout.CanvasRect, 2), new Color(120, 130, 160));
            _spriteBatch.End();

            // Pass 1b: the palette's scrollable body, scissored to its
            // viewport. Culling alone isn't enough — a 44 px row straddling
            // the viewport's top edge would otherwise paint up over the
            // "PALETTE" title and into the top bar.
            GraphicsDevice.ScissorRectangle = PaletteViewportRect;
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp, rasterizerState: ScissorOn);
            DrawPaletteEntries();
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

            // Pass 3: overlays that intentionally draw outside the canvas
            // (door labels live in the canvas margin) and the side panels.
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            DrawDoorLabels();
            DrawInspector();
            DrawStatusBar();
            DrawDragGhost();
            _spriteBatch.End();
        }

        // -- Top bar with room cycle, room name, save, snap toggle ----------

        private void DrawTopBar()
        {
            FillRect(EditorLayout.TopBarRect, new Color(24, 26, 32));
            DrawRectOutline(EditorLayout.TopBarRect, new Color(60, 64, 78));

            foreach (var b in _buttons) DrawButton(b);

            // Map mode suspends every one of those buttons — they all act on
            // the room being edited, and none of them means anything against a
            // board. DrawButton greys them; here the centre says what mode this
            // is and how to leave, in place of the room title.
            if (_mapMode)
            {
                // Same "*" language the room title uses for unsaved work — here
                // it means the board has been arranged and not yet written.
                string mapTitle = $"WORLD MAP{(_state.MapDirty ? " *" : "")} — {_mapRooms.Count} rooms" +
                                  $"   |   Tab or Esc: back to {_state.CurrentRoom.RoomId}";
                var mapSize = MeasureText(mapTitle);
                int mapGap = _rightBankLeft - _leftBankRight;
                if (mapGap >= mapSize.X + 16)
                    DrawText(mapTitle,
                        new Vector2(_leftBankRight + (mapGap - mapSize.X) / 2f,
                                    (EditorLayout.TopBarHeight - mapSize.Y) / 2f),
                        new Color(255, 220, 110));
                return;
            }

            // Centre the room title in the empty stretch between the left and
            // right button banks (computed in RelayoutButtons), and draw the
            // longest form that fits in it.
            //
            // Trailing "*" whenever ANY edit is unsaved (placements, collision
            // grid, or background pixels) — the same condition that makes
            // ConfirmDiscardUnsavedEdits block a room switch or exit.
            //
            // The shorter forms exist because that "*" is the only always-on
            // sign of unsaved work, and the gap is not guaranteed: the top bar
            // gained the Import button, which pushed the left bank right by
            // ~90 px, and the full title stops fitting a little under the
            // default 1280 px window. Degrading to the bare room id and then to
            // the marker alone keeps the warning visible where an all-or-
            // nothing title would silently drop it. (Narrower still, the two
            // banks themselves overlap — that is a pre-existing limit of the
            // fixed top bar, now reached at ~1244 px rather than ~1152.)
            bool dirty = _state.PlacementsDirty || _state.CollisionDirty || _state.BackgroundDirty;
            string mark = dirty ? " *" : "";
            string[] forms =
            {
                $"Room: {_state.CurrentRoom.DisplayName}  ({_state.CurrentRoom.RoomId}){mark}",
                _state.CurrentRoom.RoomId + mark,
                dirty ? "*" : "",
            };

            int gap = _rightBankLeft - _leftBankRight;
            foreach (string title in forms)
            {
                if (title.Length == 0) continue;
                var size = MeasureText(title);
                if (gap < size.X + 16) continue;
                float tx = _leftBankRight + (gap - size.X) / 2f;
                float ty = (EditorLayout.TopBarHeight - size.Y) / 2f;
                DrawText(title, new Vector2(tx, ty), Color.White);
                break;
            }
        }

        // -- Palette panel: icon + label per entry --------------------------

        /// <summary>
        /// The parts of the palette panel that don't scroll: the panel itself,
        /// its title, and the scrollbar hint. Drawn unscissored in pass 1.
        /// </summary>
        private void DrawPaletteChrome()
        {
            FillRect(EditorLayout.PaletteRect, new Color(28, 30, 38));
            DrawRectOutline(EditorLayout.PaletteRect, new Color(60, 64, 78));

            string header = _state.Mode switch
            {
                EditorMode.Paint => "PALETTE (paint mode)",
                EditorMode.Erase => "PALETTE (erase mode)",
                _ => "PALETTE",
            };
            DrawText(header, new Vector2(EditorLayout.PaletteX + 8, EditorLayout.PaletteY + 8), new Color(180, 180, 200));

            // Right-edge scrollbar hint when the entries overflow, matching
            // the inspector's. It sits in the 6 px gutter outside the entry
            // column, so drawing it here (before the entries) can't hide it.
            var vp = PaletteViewportRect;
            if (_paletteContentHeight > vp.Height && vp.Height > 0)
            {
                int trackX = EditorLayout.PaletteRect.Right - 6;
                FillRect(new Rectangle(trackX, vp.Top, 4, vp.Height), new Color(40, 44, 56));
                float ratio = (float)vp.Height / _paletteContentHeight;
                int thumbH = Math.Max(20, (int)(vp.Height * ratio));
                int thumbY = vp.Top + (int)((vp.Height - thumbH) *
                    (_state.PaletteScrollY / Math.Max(1, _paletteContentHeight - vp.Height)));
                FillRect(new Rectangle(trackX, thumbY, 4, thumbH), new Color(120, 130, 160));
            }
        }

        /// <summary>
        /// The scrolling body: section headers and entries. Drawn inside the
        /// scissored pass, so partially-visible rows are cut at the viewport
        /// edge rather than popping in and out whole.
        /// </summary>
        private void DrawPaletteEntries()
        {
            // Section headers — drawn before entries so entries can paint
            // their hover/selected backgrounds on top.
            foreach (var (name, bounds) in _sectionHeaders)
            {
                var rect = PaletteRowRect(bounds);
                if (!PaletteRowVisible(rect)) continue;
                FillRect(rect, new Color(45, 50, 65));
                DrawRectOutline(rect, new Color(80, 90, 120));
                DrawText(name, new Vector2(rect.X + 8, rect.Y + 4), new Color(255, 220, 110));
            }

            // Outside Place mode, the entity palette is non-interactive —
            // render its entries dimmed so it's visually obvious why clicks
            // are ignored. Sprites are drawn through a tinted alpha overlay.
            bool dim = _state.Mode != EditorMode.Place;
            var viewport = PaletteViewportRect;

            foreach (var entry in _state.Palette)
            {
                var rect = PaletteRowRect(entry.ScreenBounds);
                if (!PaletteRowVisible(rect)) continue;

                // Hover uses the same two tests HandlePaletteInput uses, so
                // the highlight always marks the entry a click would pick up.
                bool isHover = !dim
                    && rect.Contains(_mouseNow.X, _mouseNow.Y)
                    && viewport.Contains(_mouseNow.X, _mouseNow.Y);
                bool isActive = ReferenceEquals(_state.Dragging, entry);

                Color bg = isActive ? new Color(80, 90, 130)
                       : isHover    ? new Color(50, 55, 70)
                                    : new Color(38, 42, 52);
                FillRect(rect, bg);
                DrawRectOutline(rect, new Color(70, 74, 88));

                Color iconTint = dim ? new Color(255, 255, 255, 90) : Color.White;
                var iconRect = new Rectangle(rect.X + 6, rect.Y + 6, 32, 32);
                _spriteBatch.Draw(entry.Texture, iconRect, entry.SourceRect, iconTint);

                Color labelColor = dim ? new Color(140, 140, 150) : Color.White;
                DrawText(entry.Label,
                    new Vector2(rect.X + 46, rect.Y + 14),
                    labelColor);
            }
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

        private void DrawDragGhost()
        {
            if (_state.Dragging == null) return;
            var screen = new Point(_mouseNow.X, _mouseNow.Y);
            // Over the canvas the ghost matches the drop size at the current
            // zoom; elsewhere it stays at base scale so a 16x-zoomed ghost
            // doesn't blanket the palette and inspector panels.
            int scale = EditorLayout.IsInsideCanvas(screen) ? EditorLayout.EffScale : EditorLayout.CanvasScale;
            int size = 24 * scale;
            var dest = new Rectangle(screen.X - size / 2, screen.Y - size / 2, size, size);
            _spriteBatch.Draw(_state.Dragging.Texture, dest, _state.Dragging.SourceRect, new Color(255, 255, 255, 180));
        }

        // -- Door inspector -------------------------------------------------

        // Click-zones populated by DrawInspector each frame, consumed by
        // HandleInspectorClicks. Each entry is a screen rectangle and the
        // action to run on click (toggle a section, cycle a field value).
        private readonly List<(Rectangle bounds, Action action)> _inspectorButtons = new();

        // Total height of inspector content (computed during DrawInspector)
        // — used by HandleInspectorScroll to clamp scrolling.
        private int _inspectorContentHeight;

        // -- Right-side persistent inspector --------------------------------
        //
        // Replaces the old bottom-of-palette modal. Shows every Placement in
        // the current room as a collapsible section. Each section's body
        // contains read-only attributes plus cycle-buttons for editable ones
        // (door target room / target door / opening side, blocked-door key).
        //
        // Mouse-wheel scrolls when the cursor is over the inspector area;
        // clicks on section headers toggle expand/collapse and select the
        // placement (so the canvas highlights it).

        private void DrawInspector()
        {
            _inspectorButtons.Clear();

            var rect = EditorLayout.InspectorRect;
            FillRect(rect, new Color(28, 30, 38));
            DrawRectOutline(rect, new Color(60, 64, 78));

            int titleY = rect.Y + 8;
            DrawText("INSPECTOR", new Vector2(rect.X + 8, titleY), new Color(180, 180, 200));
            DrawText($"{_state.Placements.Count} entities",
                new Vector2(rect.X + rect.Width - 110, titleY), new Color(140, 150, 170));

            int contentX = rect.X + 8;
            int contentW = rect.Width - 16;
            int viewportTop = rect.Y + 32;
            int viewportBottom = rect.Bottom - 8;

            // Y cursor in screen space — scroll offset is subtracted so the
            // user can scroll past content that exceeds the viewport.
            int currentY = viewportTop - (int)_state.InspectorScrollY;
            int contentStartY = currentY;

            if (_state.Placements.Count == 0)
            {
                DrawText("(empty room — drag from the palette)",
                    new Vector2(contentX, currentY + 8), new Color(140, 150, 170));
            }

            foreach (var placement in _state.Placements)
            {
                // Closure capture: each lambda needs a stable reference to
                // the placement it was built for.
                var captured = placement;
                bool collapsed = _state.IsCollapsed(captured.Id);
                bool selected  = ReferenceEquals(_state.SelectedPlacement, captured);

                // Two-line section header: line 1 = chevron + kind, line 2 =
                // (truncated) full ID. The two-line shape keeps the header
                // narrow enough that IDs like `chateau1_door_topright` don't
                // overflow the inspector width at the DebugFont's pixel size.
                const int headerLine1H = 22;
                const int headerLine2H = 18;
                int headerH = headerLine1H + headerLine2H;
                var headerRect = new Rectangle(contentX, currentY, contentW, headerH);

                // Only render and register a click zone when the header sits
                // inside the visible viewport — otherwise scrolled-away rows
                // would be clickable.
                if (headerRect.Bottom > viewportTop && headerRect.Top < viewportBottom)
                {
                    Color headerBg = selected ? new Color(70, 90, 130)
                                              : new Color(45, 50, 65);
                    Color headerBorder = selected ? new Color(255, 220,  60)
                                                  : new Color(80, 90, 120);
                    FillRect(headerRect, headerBg);
                    DrawRectOutline(headerRect, headerBorder);

                    string chevron = collapsed ? "+" : "-";
                    DrawText($"{chevron}  {KindShortLabel(captured)}",
                        new Vector2(headerRect.X + 6, headerRect.Y + 3),
                        Color.White);

                    string idLine = TruncateText(captured.Id, headerRect.Width - 24);
                    DrawText(idLine,
                        new Vector2(headerRect.X + 18, headerRect.Y + headerLine1H),
                        new Color(180, 200, 230));

                    _inspectorButtons.Add((headerRect, () =>
                    {
                        _state.SelectedPlacement = captured;
                        _state.ToggleCollapse(captured.Id);
                    }));
                }
                currentY += headerH + 2;

                if (!collapsed)
                {
                    int bodyHeight = DrawSectionBody(captured, contentX + 10, currentY,
                                                     contentW - 20, viewportTop, viewportBottom);
                    currentY += bodyHeight + 6;
                }
            }

            _inspectorContentHeight = currentY - contentStartY;

            // Right-edge scrollbar hint when content overflows.
            int viewportH = viewportBottom - viewportTop;
            if (_inspectorContentHeight > viewportH)
            {
                int trackX = rect.Right - 6;
                int trackY = viewportTop;
                FillRect(new Rectangle(trackX, trackY, 4, viewportH), new Color(40, 44, 56));
                float ratio = (float)viewportH / _inspectorContentHeight;
                int thumbH = Math.Max(20, (int)(viewportH * ratio));
                int thumbY = trackY + (int)((viewportH - thumbH) * (_state.InspectorScrollY / Math.Max(1, _inspectorContentHeight - viewportH)));
                FillRect(new Rectangle(trackX, thumbY, 4, thumbH), new Color(120, 130, 160));
            }
        }

        private static string KindShortLabel(Placement p) => p.Kind switch
        {
            PlacementKind.Item        => "Item",
            PlacementKind.Enemy       => "Enemy",
            PlacementKind.Wizard      => "Wizard",
            PlacementKind.BlockedDoor => "BlockedDoor",
            PlacementKind.Door        => "Door",
            _ => "?",
        };

        /// <summary>
        /// Draw a section's expanded body. Returns the total pixel height
        /// consumed (so the caller can advance currentY). Each row also
        /// registers a click zone in _inspectorButtons when interactive.
        /// </summary>
        private int DrawSectionBody(Placement p, int x, int y, int w, int viewportTop, int viewportBottom)
        {
            int row = 0;
            int rowGap = 4;

            // Position is read-only; drag the placement on the canvas to move.
            row += DrawInspectorRow(x, y + row, w, "Pos",
                $"({(int)p.Position.X}, {(int)p.Position.Y})",
                null, viewportTop, viewportBottom) + rowGap;

            switch (p.Kind)
            {
                case PlacementKind.Item:
                    row += DrawInspectorRow(x, y + row, w, "Type",
                        p.ItemType.ToString(), null, viewportTop, viewportBottom) + rowGap;
                    break;

                case PlacementKind.Enemy:
                    row += DrawInspectorRow(x, y + row, w, "Type",
                        p.EnemyType.ToString(), null, viewportTop, viewportBottom) + rowGap;
                    break;

                case PlacementKind.Wizard:
                    // Wizards have no extra attributes today — just position.
                    break;

                case PlacementKind.BlockedDoor:
                    var capturedBd = p;
                    row += DrawInspectorRow(x, y + row, w, "Needs",
                        capturedBd.RequiredItem.ToString(),
                        () =>
                        {
                            capturedBd.RequiredItem = NextItemType(capturedBd.RequiredItem);
                            _state.HasValidatedDoors = false;
                            _state.PlacementsDirty = true;
                            _discardArmed = false;
                        },
                        viewportTop, viewportBottom) + rowGap;
                    break;

                case PlacementKind.Door:
                    var capturedD = p;
                    row += DrawInspectorRow(x, y + row, w, "Opens",
                        capturedD.DoorOpeningSide,
                        () =>
                        {
                            capturedD.DoorOpeningSide = capturedD.DoorOpeningSide == "LeftOpening"
                                ? "RightOpening" : "LeftOpening";
                            _state.HasValidatedDoors = false;
                            _state.PlacementsDirty = true;
                            _discardArmed = false;
                        },
                        viewportTop, viewportBottom) + rowGap;

                    row += DrawInspectorRow(x, y + row, w, "Room",
                        string.IsNullOrEmpty(capturedD.DoorTargetRoomId) ? "(none)" : capturedD.DoorTargetRoomId,
                        () =>
                        {
                            capturedD.DoorTargetRoomId = NextRoomId(capturedD.DoorTargetRoomId);
                            capturedD.DoorTargetDoorId = "";
                            _state.HasValidatedDoors = false;
                            _state.PlacementsDirty = true;
                            _discardArmed = false;
                        },
                        viewportTop, viewportBottom) + rowGap;

                    row += DrawInspectorRow(x, y + row, w, "Door",
                        string.IsNullOrEmpty(capturedD.DoorTargetDoorId) ? "(none)" : capturedD.DoorTargetDoorId,
                        () =>
                        {
                            capturedD.DoorTargetDoorId = NextTargetDoorId(capturedD.DoorTargetRoomId, capturedD.DoorTargetDoorId);
                            _state.HasValidatedDoors = false;
                            _state.PlacementsDirty = true;
                            _discardArmed = false;
                        },
                        viewportTop, viewportBottom) + rowGap;
                    break;
            }

            // Punch-out is generic — every kind gets the row. A wizard standing
            // on the original game's baked-in artwork needs its footprint cut
            // out just as much as a door does. Same closure-capture rule as the
            // rows above: the lambda outlives this frame's local `p`.
            var capturedP = p;
            row += DrawInspectorRow(x, y + row, w, "Background",
                "Punch (clear 24x24)",
                () => PunchBackground(capturedP),
                viewportTop, viewportBottom) + rowGap;

            return row;
        }

        /// <summary>
        /// One inspector field rendered as TWO lines: a small label on top,
        /// a full-row-width value box below. The two-line shape gives long
        /// values (e.g. door IDs like `chateau1_door_topright`) the entire
        /// row width so they never collide with the label.
        /// </summary>
        private int DrawInspectorRow(int x, int y, int w,
                                     string label, string value,
                                     Action? onClick,
                                     int viewportTop, int viewportBottom)
        {
            const int labelH = 16;
            const int valueH = 22;
            const int innerGap = 2;
            int totalH = labelH + valueH + innerGap;

            // Row entirely outside the viewport — skip drawing AND skip
            // click registration.
            if (y + totalH <= viewportTop || y >= viewportBottom) return totalH;

            // -- Line 1: label -----------------------------------------------
            DrawText(label, new Vector2(x, y), new Color(190, 190, 210));

            // -- Line 2: value box (full width) ------------------------------
            var valueRect = new Rectangle(x, y + labelH + innerGap, w, valueH);
            if (onClick != null)
            {
                bool hover = valueRect.Contains(_mouseNow.X, _mouseNow.Y);
                FillRect(valueRect, hover ? new Color(60, 75, 110) : new Color(40, 46, 60));
                DrawRectOutline(valueRect, new Color(90, 100, 130));
                _inspectorButtons.Add((valueRect, onClick));
            }
            else
            {
                // Read-only fields get a flatter, borderless background so
                // it's visually obvious you can't click them.
                FillRect(valueRect, new Color(34, 38, 50));
            }

            // Truncate text that would overflow the box width.
            string display = TruncateText(value, valueRect.Width - 12);
            DrawText(display, new Vector2(valueRect.X + 6, valueRect.Y + 4), Color.White);
            return totalH;
        }

        /// <summary>
        /// Trim a string with ellipsis until it fits the given pixel width.
        /// Uses MeasureText, so it respects whatever the loaded font measures.
        /// </summary>
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

        private static ItemType NextItemType(ItemType current)
        {
            // Cycle through the known ItemType enum values, skipping None.
            var values = (ItemType[])Enum.GetValues(typeof(ItemType));
            int idx = Array.IndexOf(values, current);
            for (int step = 1; step <= values.Length; step++)
            {
                var next = values[(idx + step) % values.Length];
                if (next != ItemType.None) return next;
            }
            return current;
        }

        private static string NextRoomId(string current)
        {
            // Cycle through every room id (plus an empty entry at the start).
            var ids = new List<string> { "" };
            foreach (var r in RoomMeta.All) ids.Add(r.RoomId);
            int idx = ids.IndexOf(current);
            return ids[(idx + 1) % ids.Count];
        }

        private string NextTargetDoorId(string roomId, string current)
        {
            // Door IDs to cycle through: those that exist in the named room
            // (saved RoomMeta + unsaved current-room placements). Plus an
            // "" entry so you can cycle back to "no target".
            var ids = new List<string> { "" };
            var room = RoomMeta.Find(roomId);
            if (room != null)
            {
                foreach (var d in room.Doors) ids.Add(d.DoorId);
            }
            // If the target room IS the current room, also include unsaved
            // doors so you can self-link before saving.
            if (roomId == _state.CurrentRoom.RoomId)
            {
                foreach (var p in _state.Placements)
                    if (p.Kind == PlacementKind.Door && !ids.Contains(p.Id))
                        ids.Add(p.Id);
            }
            int idx = ids.IndexOf(current);
            return ids[(idx + 1) % ids.Count];
        }

        // -- Status bar -----------------------------------------------------

        private void DrawStatusBar()
        {
            FillRect(EditorLayout.StatusBarRect, new Color(20, 22, 28));
            DrawRectOutline(EditorLayout.StatusBarRect, new Color(60, 64, 78));

            // Right-aligned view info and the mode keybind. The keybind is the
            // whole discoverability story for map mode: the top bar is full, so
            // Tab is advertised here instead of on a button.
            string view;
            if (_mapMode)
            {
                view = $"Map {_mapView.ZoomPercent}%";
                if (_state.MapDirty) view += " | map*";
                // Persistent hints belong here rather than in the transient
                // left-hand status text, which any drag or zoom overwrites.
                view += " | N: new | I: import | Tab/Esc: room";
            }
            else
            {
                view = $"Zoom {EditorLayout.Zoom}x";
                if (_state.Mode == EditorMode.Erase) view += $" | Brush {_state.BrushSize}px";
                if (_state.BackgroundDirty) view += " | PNG*";
                // Shown from ROOM mode too: an unsaved arrangement is a thing
                // the user has that quitting would lose, and the room title's
                // "*" means this room's edits — it would be a lie to fold the
                // map's state into it.
                if (_state.MapDirty) view += " | map*";
                view += " | Tab: map";
            }
            var viewSize = MeasureText(view);
            float viewX = EditorLayout.WindowWidth - viewSize.X - 8;
            DrawText(view, new Vector2(viewX, EditorLayout.StatusBarY + 10), new Color(150, 170, 200));

            // Truncate status text if it would run into the view info.
            string status = _state.Status ?? "";
            float maxX = viewX - 16;
            while (status.Length > 0 && 8 + MeasureText(status).X > maxX)
                status = status[..^1];
            DrawText(status, new Vector2(8, EditorLayout.StatusBarY + 10), new Color(200, 200, 220));
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

        private void DrawButton(UiButton b)
        {
            // In map mode every top-bar button is inert (HandleButtons never
            // runs), so it is drawn inert: no hover response, dimmed label. A
            // button that looks live and does nothing is worse than no button.
            if (_mapMode)
            {
                FillRect(b.Bounds, new Color(38, 41, 50));
                DrawRectOutline(b.Bounds, new Color(70, 76, 92));
                var dimSize = MeasureText(b.Label);
                DrawText(b.Label,
                    new Vector2(b.Bounds.X + (b.Bounds.Width - dimSize.X) / 2,
                                b.Bounds.Y + (b.Bounds.Height - dimSize.Y) / 2),
                    new Color(120, 125, 140));
                return;
            }

            bool hover = b.Bounds.Contains(_mouseNow.X, _mouseNow.Y);
            FillRect(b.Bounds, hover ? new Color(70, 78, 100) : new Color(50, 55, 70));
            DrawRectOutline(b.Bounds, new Color(110, 120, 150));
            var sz = MeasureText(b.Label);
            DrawText(b.Label,
                new Vector2(b.Bounds.X + (b.Bounds.Width - sz.X) / 2,
                            b.Bounds.Y + (b.Bounds.Height - sz.Y) / 2),
                Color.White);
        }
    }

    // ------------------------------------------------------------------------
    // TINY UI BUTTON
    // ------------------------------------------------------------------------

    public class UiButton
    {
        public string Label;
        public Rectangle Bounds;
        public Action OnClick;

        public UiButton(string label, Rectangle bounds, Action onClick)
        {
            Label = label;
            Bounds = bounds;
            OnClick = onClick;
        }
    }
}
