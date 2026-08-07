using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SorceryRemake.Core;
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

            BuildPalette();
            BuildButtons();
            LoadRoom(_state.CurrentRoomIndex);
        }

        private Texture2D LoadAndCache(string asset)
        {
            if (_textures.TryGetValue(asset, out var cached)) return cached;
            var tex = Content.Load<Texture2D>(asset);
            MakeColorTransparent(tex, Color.Black);
            _textures[asset] = tex;
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
        private static readonly string[] SectionOrder =
            { "WEAPONS", "KEY ITEMS", "ENEMIES", "DOORS", "OTHER" };

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
            var leftDoor = new PaletteEntry(
                "Door (LeftOpening)", PlacementKind.Door,
                _textures["RightDoorFrames"],   // LeftOpening uses RightDoorFrames in-game
                new Rectangle(0, 0, 48, 48))    // frame 0 = closed
            { };
            _state.Palette.Add(Tag(leftDoor, "DOORS"));

            var rightDoor = new PaletteEntry(
                "Door (RightOpening)", PlacementKind.Door,
                _textures["LeftDoorFrames"],    // RightOpening uses LeftDoorFrames in-game
                new Rectangle(0, 0, 48, 48))
            { };
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

        private void LayoutPalette()
        {
            const int entryHeight = 44;
            const int headerHeight = 22;
            const int padding = 8;
            int x = EditorLayout.PaletteX + padding;
            int y = EditorLayout.PaletteY + padding + 22;   // 22 = "PALETTE" title line
            int w = EditorLayout.PaletteWidth - padding * 2;

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
        }

        // Indices into _buttons for the labels we update at runtime.
        private int _btnSnapIdx, _btnModeIdx, _btnPunchIdx;

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

            // Left bank: Prev | Next | Mode
            _buttons[0].Bounds = new Rectangle(8,   by,  80, bh);
            _buttons[1].Bounds = new Rectangle(96,  by,  80, bh);
            _buttons[2].Bounds = new Rectangle(186, by, 130, bh);
            _leftBankRight = 186 + 130;

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
            meta.ReloadDoorsFromDisk();
            _state.LoadFromRoomContent(content ?? new RoomContent(), meta.Doors);
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
                // 1. Content (items, enemies, wizards, blocked doors).
                RoomContentLoader.Save(meta.RoomId, _state.ToRoomContent(), EditorPaths.RepoAssetsDataDir);
                string saved = $"content_{meta.RoomId}.json";

                // 2. Layout (doors).
                var layout = _state.ToRoomLayoutJson(meta.RoomId);
                RoomLayoutLoader.Save(layout, EditorPaths.RepoAssetsDataDir);
                saved += $" + layout_{meta.RoomId}.json";

                // Placements live entirely in those two files, so they're
                // durable now — clear the flag here (same pattern as the
                // collision / background flags below, each cleared right
                // after the write that persists it).
                _state.PlacementsDirty = false;

                // 3. Collision grid (only when Paint mode produced changes).
                if (_state.CollisionDirty && _state.CollisionMap != null && meta.CollisionJsonName != null)
                {
                    string path = Path.Combine(EditorPaths.RepoAssetsDataDir, meta.CollisionJsonName);
                    RoomLoader.SaveCollisionGrid(path, _state.CollisionMap);
                    _state.CollisionDirty = false;
                    saved += $" + {meta.CollisionJsonName}";
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
                    string tmpPath = pngPath + ".tmp";
                    try
                    {
                        using (var fs = File.Create(tmpPath))
                            _currentBackground.SaveAsPng(fs, _currentBackground.Width, _currentBackground.Height);
                        File.Move(tmpPath, pngPath, overwrite: true);
                    }
                    finally
                    {
                        if (File.Exists(tmpPath)) File.Delete(tmpPath);
                    }
                    // The restore brush now restores to this saved state.
                    _bgOriginal = (Color[])_bgPixels.Clone();
                    _state.BackgroundDirty = false;
                    saved += $" + {meta.BackgroundAsset}.png (rebuild for game)";
                }

                // Refresh in-memory door cache from the file we just wrote
                // so DoorMarkers / Validate Doors see the new state.
                meta.ReloadDoorsFromDisk();

                _discardArmed = false;   // a save always disarms the discard guard
                _state.Status = "Saved " + saved;
            }
            catch (Exception ex)
            {
                _state.Status = "Save failed: " + ex.Message;
            }
        }

        // Armed by the first destructive attempt (room switch, exit) while
        // background pixel, collision, or placement edits are unsaved; the
        // second attempt goes through. Disarmed by saving or by further
        // editing.
        private bool _discardArmed;

        private bool ConfirmDiscardUnsavedEdits()
        {
            if (!_state.BackgroundDirty && !_state.CollisionDirty && !_state.PlacementsDirty) return true;
            if (_discardArmed) { _discardArmed = false; return true; }
            _discardArmed = true;
            _state.Status = "Unsaved edits! Save (Ctrl+S), or repeat the action to discard.";
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

            // Escape exits, but never silently throws away unsaved edits:
            // the first press arms the discard guard (status bar warning),
            // the second confirms.
            if (Pressed(Keys.Escape) && ConfirmDiscardUnsavedEdits()) Exit();

            HandleButtons();
            HandleInspectorScroll();
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

        private void HandlePaletteInput()
        {
            // Palette is interactive only in Place mode.
            if (_state.Mode != EditorMode.Place) return;
            if (!LeftClicked()) return;

            var p = new Point(_mouseNow.X, _mouseNow.Y);
            foreach (var entry in _state.Palette)
            {
                if (entry.ScreenBounds.Contains(p))
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
                    _state.IsMovingSelection = true;
                    _state.MoveOffset = _state.SelectedPlacement.Position - game;
                    // Expand the inspector section for the freshly-selected
                    // placement so its attributes are visible immediately.
                    _state.Expand(_state.SelectedPlacement.Id);
                    _state.Status = $"Moving {_state.SelectedPlacement.DisplayName} ({_state.SelectedPlacement.Id})";
                }
                else
                {
                    _state.Status = "No placement under cursor.";
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
        /// Flood-fills from the player's hardcoded spawn (160, 80) over an
        /// 8-pixel grid where each cell is "ok" iff a 24x24 player hitbox at
        /// that position doesn't overlap any solid tile. Any placement
        /// whose center sits in an unreachable cell is flagged.
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
            const int spawnX = 160, spawnY = 80;          // matches Game1 default
            int sx = Math.Clamp(spawnX / step, 0, cellsX - 1);
            int sy = Math.Clamp(spawnY / step, 0, cellsY - 1);

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
                    _state.Status = "Validate: spawn location is fully blocked — fix collision.";
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
            _state.Status = bad == 0
                ? $"Validate: all {_state.Placements.Count} placements reachable from spawn (160, 80)."
                : $"Validate: {bad} of {_state.Placements.Count} placements UNREACHABLE — see red borders.";
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
        private void ValidateDoors()
        {
            _state.DoorStatus.Clear();
            int orphanRoom = 0, orphanDoor = 0, asymmetric = 0, ok = 0;

            // Build a snapshot of "doors per room" — using Placements for the
            // current room (so unsaved authored doors are validated too) and
            // saved RoomMeta.Doors for every other room.
            var doorsByRoom = new Dictionary<string, List<DoorDef>>();
            foreach (var room in RoomMeta.All)
                doorsByRoom[room.RoomId] = new List<DoorDef>(room.Doors);

            string currentRoomId = _state.CurrentRoom.RoomId;
            doorsByRoom[currentRoomId] = new List<DoorDef>();
            foreach (var p in _state.Placements)
            {
                if (p.Kind != PlacementKind.Door) continue;
                doorsByRoom[currentRoomId].Add(new DoorDef(
                    p.Id, p.Position, p.DoorOpeningSide, p.DoorTargetRoomId, p.DoorTargetDoorId));
            }

            foreach (var room in RoomMeta.All)
            {
                foreach (var door in doorsByRoom[room.RoomId])
                {
                    // The test rooms are registered in Game1.RegisterTestRooms,
                    // not in RoomManifest, so RoomMeta.Find can't see them and
                    // a door targeting one would read as orphan-room. The link
                    // is real — we just can't verify the far side from here.
                    if (RoomManifest.TestRoomIds.Contains(door.TargetRoomId))
                    {
                        _state.DoorStatus[door.DoorId] = "ok-test";
                        ok++;
                        continue;
                    }

                    var target = RoomMeta.Find(door.TargetRoomId);
                    if (target == null)
                    {
                        _state.DoorStatus[door.DoorId] = "orphan-room";
                        orphanRoom++;
                        continue;
                    }

                    DoorDef? back = null;
                    foreach (var d in doorsByRoom[target.RoomId])
                        if (d.DoorId == door.TargetDoorId) { back = d; break; }

                    if (back == null)
                    {
                        _state.DoorStatus[door.DoorId] = "orphan-door";
                        orphanDoor++;
                        continue;
                    }

                    // The back-door should target this door. If not, the link
                    // is one-way — works walking through this door, but a
                    // player coming the other way ends up somewhere else.
                    if (back.TargetRoomId != room.RoomId || back.TargetDoorId != door.DoorId)
                    {
                        _state.DoorStatus[door.DoorId] = "asymmetric";
                        asymmetric++;
                        continue;
                    }

                    _state.DoorStatus[door.DoorId] = "ok";
                    ok++;
                }
            }

            _state.HasValidatedDoors = true;
            int bad = orphanRoom + orphanDoor + asymmetric;
            if (bad == 0)
            {
                _state.Status = $"Doors: all {ok} doors link cleanly across {RoomMeta.All.Count} rooms.";
            }
            else
            {
                _state.Status =
                    $"Doors: {bad} broken " +
                    $"(orphan-room={orphanRoom}, orphan-door={orphanDoor}, asymmetric={asymmetric}). " +
                    $"Red markers on canvas show local room's issues.";
            }
        }

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

            // Delete the selected placement.
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

            // Door entries store the opening side in the entry's label —
            // we read it back via the icon texture to pick the correct
            // opening side. The two palette entries differ only in this.
            string openingSide = entry.Kind == PlacementKind.Door
                ? (entry.Label.Contains("LeftOpening") ? "LeftOpening" : "RightOpening")
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

        private static void ClampToRoom(Placement p)
        {
            float maxX = EditorLayout.RoomWidth - 24;
            float maxY = EditorLayout.RoomHeight - 24;
            p.Position = new Vector2(
                Math.Clamp(p.Position.X, 0f, maxX),
                Math.Clamp(p.Position.Y, 0f, maxY));
        }

        // ====================================================================
        // DRAW
        // ====================================================================

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(new Color(36, 38, 46));

            // Pass 1: UI chrome and the canvas frame.
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            DrawTopBar();
            DrawPalette();
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

            // Pass 3: overlays that intentionally draw outside the canvas
            // (door labels live in the canvas margin) and the side panels.
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            DrawDoorLabels();
            DrawInspector();
            DrawStatusBar();
            DrawDragGhost();
            _spriteBatch.End();

            base.Draw(gameTime);
        }

        // -- Top bar with room cycle, room name, save, snap toggle ----------

        private void DrawTopBar()
        {
            FillRect(EditorLayout.TopBarRect, new Color(24, 26, 32));
            DrawRectOutline(EditorLayout.TopBarRect, new Color(60, 64, 78));

            foreach (var b in _buttons) DrawButton(b);

            // Centre the room title in the empty stretch between the left
            // and right button banks (computed in RelayoutButtons). Skip
            // drawing when there's no room — falls off the side instead of
            // overlapping a button.
            // Trailing "*" whenever ANY edit is unsaved (placements, collision
            // grid, or background pixels) — the same condition that makes
            // ConfirmDiscardUnsavedEdits block a room switch or exit.
            bool dirty = _state.PlacementsDirty || _state.CollisionDirty || _state.BackgroundDirty;
            string title = $"Room: {_state.CurrentRoom.DisplayName}  ({_state.CurrentRoom.RoomId})"
                         + (dirty ? " *" : "");
            var size = MeasureText(title);
            int gap = _rightBankLeft - _leftBankRight;
            if (gap >= size.X + 16)
            {
                float tx = _leftBankRight + (gap - size.X) / 2f;
                float ty = (EditorLayout.TopBarHeight - size.Y) / 2f;
                DrawText(title, new Vector2(tx, ty), Color.White);
            }
        }

        // -- Palette panel: icon + label per entry --------------------------

        private void DrawPalette()
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

            // Section headers — drawn before entries so entries can paint
            // their hover/selected backgrounds on top.
            foreach (var (name, bounds) in _sectionHeaders)
            {
                FillRect(bounds, new Color(45, 50, 65));
                DrawRectOutline(bounds, new Color(80, 90, 120));
                DrawText(name, new Vector2(bounds.X + 8, bounds.Y + 4), new Color(255, 220, 110));
            }

            // Outside Place mode, the entity palette is non-interactive —
            // render its entries dimmed so it's visually obvious why clicks
            // are ignored. Sprites are drawn through a tinted alpha overlay.
            bool dim = _state.Mode != EditorMode.Place;

            foreach (var entry in _state.Palette)
            {
                bool isHover = !dim && entry.ScreenBounds.Contains(_mouseNow.X, _mouseNow.Y);
                bool isActive = ReferenceEquals(_state.Dragging, entry);

                Color bg = isActive ? new Color(80, 90, 130)
                       : isHover    ? new Color(50, 55, 70)
                                    : new Color(38, 42, 52);
                FillRect(entry.ScreenBounds, bg);
                DrawRectOutline(entry.ScreenBounds, new Color(70, 74, 88));

                Color iconTint = dim ? new Color(255, 255, 255, 90) : Color.White;
                var iconRect = new Rectangle(
                    entry.ScreenBounds.X + 6, entry.ScreenBounds.Y + 6, 32, 32);
                _spriteBatch.Draw(entry.Texture, iconRect, entry.SourceRect, iconTint);

                Color labelColor = dim ? new Color(140, 140, 150) : Color.White;
                DrawText(entry.Label,
                    new Vector2(entry.ScreenBounds.X + 46, entry.ScreenBounds.Y + 14),
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
            DrawUnreachableWarnings();
            DrawPuzzleWarnings();
            DrawSelectionHighlight();
            DrawEraseCursor();
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

        private Color DoorStatusColor(Placement door)
        {
            if (_state.HasValidatedDoors && _state.DoorStatus.TryGetValue(door.Id, out var status))
            {
                return status switch
                {
                    "ok"          => new Color( 80, 230, 110),
                    // Dimmer green: the target is a programmatic test room, so
                    // the link is accepted but its far side is unverified.
                    "ok-test"     => new Color( 80, 180, 110),
                    "asymmetric"  => new Color(255, 200,  60),
                    _             => new Color(255,  60,  60),  // orphan-*
                };
            }
            return new Color(180, 180, 200);
        }

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
            if (_state.SelectedPlacement == null) return;
            var dest = EditorLayout.GameRectToScreen(_state.SelectedPlacement.Bounds);
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

            // Right-aligned view info: zoom level always, brush size in
            // Erase mode, unsaved-pixels marker while the PNG is dirty.
            string view = $"Zoom {EditorLayout.Zoom}x";
            if (_state.Mode == EditorMode.Erase) view += $" | Brush {_state.BrushSize}px";
            if (_state.BackgroundDirty) view += " | PNG*";
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
                if (entry.Kind != p.Kind) continue;
                if (p.Kind == PlacementKind.Item && entry.ItemType != p.ItemType) continue;
                if (p.Kind == PlacementKind.Enemy && entry.EnemyType != p.EnemyType) continue;
                if (p.Kind == PlacementKind.BlockedDoor && entry.ItemType != p.RequiredItem) continue;
                if (p.Kind == PlacementKind.Door)
                {
                    // Two door entries differ by label; match the one whose
                    // label encodes the placement's opening side.
                    if (!entry.Label.Contains(p.DoorOpeningSide)) continue;
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
