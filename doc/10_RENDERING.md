# 10 — Rendering & Graphics

The remake's rendering goal is *crisp pixel art at any window size*. This document explains the two-pass render-target pipeline, the sprite component, animation frame definitions, and the info-panel layout.

## Render Targets, Scales, and Window Sizes

Three coordinate systems coexist:

| Space | Size | Notes |
|-------|------|-------|
| **Game space** | 320 × 144 | All gameplay logic runs here. Tile = 8 px, sprite = 24 px. |
| **Render-target space** | 960 × 432 (game area) | Game space × `RENDER_SCALE` (3). Drawn into an offscreen `RenderTarget2D`. |
| **Window space** | 960 × 600 (game 432 + panel 168) | What the user sees. Game area is the render target stretched to the back buffer; info panel is drawn directly. |

Constants from `Game1.cs`:

```csharp
private const int BASE_GAME_WIDTH       = 320;
private const int BASE_GAME_HEIGHT      = 144;
private const int BASE_INFO_PANEL_HEIGHT = 56;
private const int RENDER_SCALE          = 3;
private const int WINDOW_WIDTH          = BASE_GAME_WIDTH * RENDER_SCALE;             // 960
private const int GAME_AREA_HEIGHT      = BASE_GAME_HEIGHT * RENDER_SCALE;            // 432
private const int INFO_PANEL_HEIGHT     = BASE_INFO_PANEL_HEIGHT * RENDER_SCALE;      // 168
private const int WINDOW_HEIGHT         = GAME_AREA_HEIGHT + INFO_PANEL_HEIGHT;       // 600
```

## The Draw Pipeline

```
Draw(gameTime):
  1. SetRenderTarget(_renderTarget)        ← offscreen, 960×432
     SpriteBatch.Begin(PointClamp)
       background → tilemap → doors → blocked doors → wizards → items → player → enemies → projectiles → debug overlay
     SpriteBatch.End()

  2. SetRenderTarget(null)                 ← back to back buffer
     SpriteBatch.Begin(PointClamp)
       Draw(_renderTarget, dest=Rectangle(0,0,WINDOW_WIDTH,GAME_AREA_HEIGHT), color=White)
     SpriteBatch.End()

  3. DrawInfoPanel()                       ← directly into back buffer at y=GAME_AREA_HEIGHT
     SpriteBatch.Begin(PointClamp)
       blue panel rectangle, text strings, item icon
     SpriteBatch.End()

  4. if (F1 toggled) DrawDebugInfo()       ← directly into back buffer
     SpriteBatch.Begin()
       text overlay
     SpriteBatch.End()
```

Why two passes:

- The render target ensures pixel-art crispness. We draw at 3× source resolution; stretching that to the back buffer with `SamplerState.PointClamp` preserves sharp edges.
- It also keeps the info panel and debug overlays in their own coordinate space (back-buffer space, with no implicit scale). Mouse hover handling and any UI math stays simple.

`SamplerState.PointClamp` is non-negotiable — using `LinearClamp` would blur every sprite and break the retro aesthetic.

## Sprite Component

`Graphics/SpriteComponent.cs` is the universal animated-sprite wrapper. Every entity that displays a sprite uses one.

### Anatomy

```csharp
public Texture2D? Texture;
public Rectangle SourceRectangle;
public Rectangle[]? AnimationFrames;
public int CurrentFrame;
public float FrameTime;
public bool IsLooping;
public bool IsPlaying;
public bool FlipHorizontal;
public bool FlipVertical;
```

### Animation Update

Each frame:

```
if !IsPlaying or AnimationFrames == null: return
_frameTimer += dt
if _frameTimer >= FrameTime:
   _frameTimer -= FrameTime
   CurrentFrame++
   if CurrentFrame >= frames.Length:
       if IsLooping: CurrentFrame = 0
       else:         CurrentFrame = last; IsPlaying = false
   SourceRectangle = AnimationFrames[CurrentFrame]
```

### `SetAnimation`

`SetAnimation(frames, frameTime, loop)` resets to frame 0, sets timing, picks the first frame, and starts playing. It's called every time the visual state changes (e.g., player switching from idle to walk_left).

### `Draw(SpriteBatch, renderPosition, scale)`

```csharp
SpriteEffects effects = SpriteEffects.None;
if (FlipHorizontal) effects |= SpriteEffects.FlipHorizontally;
if (FlipVertical)   effects |= SpriteEffects.FlipVertically;

spriteBatch.Draw(
    texture: Texture,
    position: renderPosition,        // already × RENDER_SCALE
    sourceRectangle: SourceRectangle,
    color: Tint,
    rotation: Owner.Rotation,
    origin: Vector2.Zero,            // top-left, for pixel-perfect positioning
    scale: scale,                    // RENDER_SCALE
    effects: effects,
    layerDepth: 0f);
```

The origin is **top-left**, not center. This matters for any code computing render positions — `entity.Position * RENDER_SCALE` is the top-left corner of the sprite, not its center.

## Frame Coordinates — `SpriteConfig`

`Graphics/SpriteConfig.cs` is the single source of truth for every sprite frame in the game. It defines:

- Sprite dimensions (`SPRITE_WIDTH=24`, `SPRITE_HEIGHT=24`)
- Per-character animation arrays (`PLAYER_IDLE_FRONT`, `PLAYER_WALK_LEFT`, `PLAYER_WALK_RIGHT`, plus aliases for legacy names)
- Per-enemy animation arrays (`GUARD_*`, `MASK_ANIM`, `BOAR_ANIM`, `EYE_ANIM`, `WRAITH_*`)
- Death animation (`ENEMY_DEATH_ANIM`)
- Item frames (single-frame rectangles for each weapon/item)
- Wizard / star animations (vertical 4-frame strips)
- Animation speeds (per-character `*_ANIMATION_SPEED` constants)
- Movement speeds (`MASK_SPEED`, `BOAR_SPEED`, etc.)
- Velocity threshold for animation switching (`ANIMATION_VELOCITY_THRESHOLD = 10`)

### Frame Spacing

Most spritesheets use **24-px frames with 1-px gaps** (so frames are 25 px apart). `SpriteConfig` hand-codes coordinates:

```csharp
new Rectangle(0,   75, 24, 24),  // frame 0
new Rectangle(25,  75, 24, 24),  // frame 1
new Rectangle(50,  75, 24, 24),  // frame 2
new Rectangle(75,  75, 24, 24),  // frame 3
```

Some sprites have non-standard sizes:

- **Boar:** 22 × 24, with 1-px Y offsets per frame to crop a green stray pixel row. `BOAR_ANIM` has irregular Y values.
- **Eye:** 24 × 17 (shorter sprite).
- **Wraith:** 24 × 23, with 1-px Y offset on certain frames.
- **Items, doors:** 48 × 48 source, scaled down to 24 × 24 game-space.
- **Death anim:** 48 × 48 vertical strip (4 frames stacked).
- **Captive wizard, star:** 48 × 48 vertical strips, but cycled in opposite directions (wizard bottom-up, star top-down).

When extracting a new sprite, **the layout convention matters more than the sheet size** — pick whichever 1-row or 1-column strip makes the math simplest.

## Color Keying — Black to Transparent

The original assets use solid black `(0, 0, 0)` as the transparent key. The remake converts this on load via `Game1.MakeColorTransparent`:

```csharp
Color[] data = new Color[texture.Width * texture.Height];
texture.GetData(data);

for (int i = 0; i < data.Length; i++)
{
    if (data[i].R == 0 && data[i].G == 0 && data[i].B == 0)
        data[i] = Color.Transparent;
}

texture.SetData(data);
```

Every loaded sprite sheet is run through this helper (`LoadAndTransparent`). Backgrounds (which are *meant* to have black pixels for sky) are NOT processed this way.

This is faster and simpler than authoring PNGs with explicit alpha. The trade-off: if a sprite legitimately needs near-black color, you have to keep it just-not-pure-black (e.g., `(1, 1, 1)`).

## The Info Panel

A 320×56 (rendered 960×168) bar below the game area. Currently text-based:

```
Top-left:    "You are in: <RoomDisplayName>"   (yellow, debug font)
Middle-left: "Carrying: <itemName>"            (yellow)
Bottom-left: "Saved Wizards: <N>"              (yellow)
Right side:  48×48 icon of carried item (or empty)
```

Background is `Color(0, 0, 139)` (dark blue) drawn as a `_pixelTexture` rectangle.

Phase 4C will replace this with a sprite-based panel matching the original (energy bar, lives indicator, wizard count graphic, item icon at original layout). The text version is intentionally placeholder.

## Debug Font

`Content/DebugFont.spritefont` is loaded into `_debugFont` in `LoadContent`. It's used for:

- Info panel text
- F1 debug overlay

If the font fails to load, both panels silently degrade — text is skipped but the game continues. The debug font is a MonoGame `SpriteFont` (XML descriptor); regenerating it requires the MonoGame Pipeline tool.

## Background Rendering

Two paths:

### Background image rooms

```csharp
RoomManager.DrawBackground(spriteBatch, scale=3):
  if CurrentBackground != null:
    spriteBatch.Draw(CurrentBackground,
        dest: Rectangle(0, 0, 320*3, 144*3),
        Color.White)
```

The background image is stretched to exactly fill the game area. Since backgrounds are authored at 320×144 native, the 3× stretch is integer-clean.

### Tile-rendered rooms

```csharp
TileMapComponent.Draw(spriteBatch, scale=3):
  for each row, col:
    if tile == EMPTY: continue
    sourceRect = TileConfig.GetTileSourceRect(tileId)
    pos = (col*8*scale, row*8*scale)
    spriteBatch.Draw(TilesetTexture, pos, sourceRect, ..., scale=3, ...)
```

The tile renderer skips `EMPTY` tiles as an optimization (and to avoid ghost background tiles).

When a room has both a background and a tile map (common — the JSON collision creates an invisible tilemap), the background is drawn but `RoomManager.HasBackground` returns true, so the tilemap visuals are skipped. The tilemap exists only for collision.

## The Pixel Texture

`Game1._pixelTexture` is a `Texture2D(GraphicsDevice, 1, 1)` initialized to `Color.White`. Used for:

- Drawing the info panel background rectangle.
- Drawing projectiles (single-pixel * RENDER_SCALE = 3×3 squares).

Created once in `LoadContent`. Earlier versions of the codebase allocated a fresh 1×1 texture *every frame* in `DrawFilledRectangle` — a real leak that was fixed during Phase 4A.

## Render Order Within the Game Area

Order matters because `SpriteSortMode.Deferred` is used (not `BackToFront` or `FrontToBack`). The order of `_spriteBatch.Draw` calls IS the layer order:

```
1. Background image (or tilemap)
2. Doors
3. Blocked doors
4. Captive wizards
5. Room items
6. Player
7. Enemies (incl. dying death animation)
8. Projectiles
9. Collision mask debug overlay (F2)
```

If you add a new visible thing, decide where in this stack it should appear. Player vs. items: the player is drawn AFTER items, so picking up an item (which puts the player on top of it briefly) shows the player in front. If you want a foreground decorative layer, it should be inserted between projectiles and the F2 overlay.

## F2 Overlay (Debug)

`DrawCollisionMaskOverlay` creates a once-per-mask `Texture2D` from the `bool[,]` pixel mask, with semi-transparent red on solid pixels. The texture is cached and only rebuilt when the mask reference changes (room transitions). Drawn at `RENDER_SCALE` to align with the game world.

This is **pixel mask only** — it does NOT visualize the JSON tile collision. To see tile collision instead, you'd need a separate overlay that walks `TileMap.Tiles` and tints each `IsTileBlocking` cell. Not currently implemented.

## Performance Notes

- **No texture batching.** Each `Draw` call may issue a separate batch. MonoGame's `SpriteBatch` consolidates within a `Begin/End` block, but switching textures (player → enemy → item) does cause flushes. With ~10–20 sprites per room, this is invisibly fast.
- **No z-ordering.** Layer depth is always `0f`. Order is purely call-order.
- **No culling.** Tiles outside the screen are still skipped via the `EMPTY` check, but other sprites are always drawn. Since the world is one screen and sprites are tiny, no culling is needed.
- **The collision overlay rebuild** (F2) does an O(width × height) pass to color the texture. It's gated by reference identity on the mask, so it only happens once per room — no per-frame cost.
