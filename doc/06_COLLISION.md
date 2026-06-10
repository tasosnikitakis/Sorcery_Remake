# 06 — Collision System

The remake supports **two independent collision approaches** that can co-exist in the same room:

1. **Tile-based collision** — 8×8 grid. Each tile is solid or empty (or a platform). Used by all rooms via the `collision_<roomId>.json` files.
2. **Pixel-perfect collision** — `bool[,]` mask the size of the background image (typically 320×144). Generated automatically when a room's background is set, by sampling non-black pixels and flood-filling from the bottom row.

The active live code path is **tile collision**; pixel-mask code exists in `PhysicsComponent` but is not currently invoked from `Update`. The pixel mask is always *generated* (so the F2 debug overlay works) but doesn't drive movement today. This is a deliberate choice — see [Tile vs. Pixel: When Each Wins](#tile-vs-pixel-when-each-wins).

## Hitboxes and Insets

The player and most enemies share these constants (`PhysicsComponent.cs`):

```
HITBOX_WIDTH  = 24
HITBOX_HEIGHT = 24
COLLISION_INSET_X      = 2     // 2 px on each side
COLLISION_INSET_TOP    = 0
COLLISION_INSET_BOTTOM = 0
```

The **sprite hitbox** is 24×24 (used for entity-to-entity overlaps: items, doors, enemy contact). The **collision box** for world-geometry checks is inset 2 px on the left and right, giving an effective 20×24 collision cross-section.

### The 2-Pixel Horizontal Inset

This is the single most important authenticity detail in collision. Without it, the wizard cannot fit through 24-pixel-wide vertical shafts (e.g., the central shaft in Chateau 1) because his sprite right edge clips a 1-pixel column of the wall corner.

Insetting the collision box 2 px horizontally is an 8-bit-era trick: render the full 24-px sprite, but only test the inner 20 px against world geometry. The original game used the same trick. See [`PhysicsComponent.GetCollisionBox`](../Physics/PhysicsComponent.cs#L388) and [`CheckOnGround`](../Physics/PhysicsComponent.cs#L241):

```csharp
// CheckOnGround uses CENTER ONLY for ground detection, not the full hitbox.
// This is what lets the wizard fall through a tile-aligned shaft the moment
// his center is no longer above floor — instead of "resting" on a far wall
// corner that his right edge happens to clip.
int centerCol = (int)(pos.X + HITBOX_WIDTH / 2) / TILE_SIZE;
return IsTileBlocking(centerCol, tileRow);
```

**Do not "improve" this** by checking left+right edges. That breaks shaft fall-through.

## Tile-Based Collision

### Tile IDs

`Tiles/TileConfig.cs` enumerates 64 tile IDs in 8 rows of 8. Categories:

| Range | Type | Behavior |
|-------|------|----------|
| 0–7 | Wall | Solid (`IsSolid` returns true) |
| 8–15 | Floor | Solid |
| 16–23 | Platform | `IsPlatform` returns true; treated as solid by current physics |
| 24–31 | Empty / Background | Passthrough |
| 32 / 33 / 34 | Ladder | `IsLadder` returns true (not currently used by physics) |
| 35, 37 | Hazard, Poison | `IsDeadly` returns true (not currently checked) |
| 36, 38, 39 | Decoration (water, warning, ice) | Passthrough |
| 40–63 | Decoration | Passthrough |

`IsTileBlocking(x, y)` — used by physics — returns `true` for both `IsSolid` and `IsPlatform`. So platforms are currently fully solid (no jump-through-from-below). When the platform-passthrough behavior is needed, `ResolveVerticalCollision` will need to distinguish `vel.Y > 0` (falling onto platform) vs `< 0` (jumping through).

### The Resolution Algorithm

For each axis independently:

```
1. pos.X += vel.X * dt
2. Compute the row range the entity spans vertically: [topTile, bottomTile]
3. If moving right:
     tileCol = (pos.X + HITBOX_WIDTH) / TILE_SIZE   // right edge tile
     for row in [topTile, bottomTile]:
       if IsTileBlocking(tileCol, row):
         pos.X = tileCol * TILE_SIZE - HITBOX_WIDTH   // snap left
         vel.X = 0
         break
   If moving left: mirror.
```

Vertical pass is identical with X/Y swapped, then `ground` is checked separately (center column only, see above).

### Separate-Axis Resolution

The reason physics moves on X first, resolves, *then* moves on Y is to avoid "corner sticking." If you moved diagonally in one step and resolved the combined velocity, the entity could get wedged into corners. By splitting:

- Diagonal into a wall corner: X resolves first, vel.X becomes 0; then Y still moves freely.
- This is a well-known pattern; standard reference: https://www.gamedeveloper.com/programming/the-guide-to-implementing-2d-platformers

## Solid Rectangles (Doors)

Doors and unlocked-blocked-doors don't fit the tile grid (they are 24×24 entities at arbitrary positions). They are added to `PhysicsComponent.SolidRects` — a `List<Rectangle>` — at room load time (`Game1.UpdateDoorCollision`) and rebuilt whenever a blocked door unlocks (`Game1.RebuildSolidRects`).

Rect resolution mirrors tile resolution:

```csharp
foreach (var rect in SolidRects):
  if !playerRect.Intersects(rect): continue
  if vel.X > 0: pos.X = rect.Left  - HITBOX_WIDTH; vel.X = 0
  else if vel.X < 0: pos.X = rect.Right;          vel.X = 0
  // recompute playerRect for next iteration
```

The recompute step inside the loop matters: a player can simultaneously collide with two stacked rects (rare but possible when blocked doors are adjacent). Recomputing means the second iteration sees the post-resolution position, not the pre-resolution one.

## Background Pixel Mask

Every room with a background image gets an automatically-generated `bool[,] PixelMask` on its `TileMapComponent`, built by [`TileMapComponent.BuildPixelMaskFromTexture`](../Tiles/TileMapComponent.cs#L71):

```
1. For every pixel: raw[x,y] = (R + G + B) > 10        // any non-near-black pixel
2. 8-connected flood fill from every pixel in the BOTTOM ROW that's `raw=true`
3. Output: only the floor-connected region of `raw` survives in `mask`
```

This is the trick that lets clouds, distant decoration, and sky details exist in the background image without becoming solid geometry. Anything not connected to the floor (via 8-connected adjacency through other near-black-or-not pixels) is treated as transparent.

The mask is currently used **only by the F2 debug overlay** in `Game1.DrawCollisionMaskOverlay`. The pixel-perfect resolution methods exist in `PhysicsComponent` (`ResolveHorizontalPixelCollision`, `CollidesAt`, etc.) but the live `Update` path uses tile collision only. The room's separate `collision_<id>.json` is the authoritative collision source.

### When Pixel Mask Becomes Live

Switching `Update` to pixel-mask collision is straightforward — replace the tile resolve calls with the existing pixel resolve calls. The reason this hasn't been done: tile JSON is faster to author for a level designer than pixel-perfect would be (tiles are 8×8, pixels are 1×1), and tile collision is faster at runtime. Pixel-mask collision is reserved for cases where tile resolution is too coarse for a specific feature.

## Tile vs. Pixel: When Each Wins

| Approach | Authoring | Runtime | Tunable | Best For |
|----------|-----------|---------|---------|----------|
| Tile JSON | 5–30 min/room manually, faster with future tooling | O(rows in hitbox) per axis per frame | Snap-to-tile only | Standard rooms with rectilinear geometry |
| Pixel mask | Free (auto from BG image) | O(pixels in collision box) per axis per frame | Pixel-perfect | Curved or organic geometry, sloped floors, decorative shapes |

Today every shipping room uses tile collision; the pixel mask is informational. The roadmap (Phase 5A) keeps tile collision as the authoring target.

## Authoring a Collision Grid

A collision JSON file (`assets/data/collision_<roomId>.json`) has shape:

```json
{
  "width":  40,
  "height": 18,
  "collision": [
    [0,0,0,...,0],   // row 0  (40 ints)
    [0,0,0,...,0],   // row 1
    ...
    [1,1,1,...,1]    // row 17
  ]
}
```

`0` = empty, `1` = solid (mapped to `WALL_DARK_GRAY` in `RoomLoader.LoadCollisionGrid`). Every row must have exactly `width` cells; mismatched dimensions throw at load time.

### Manual Workflow (current)

1. Open the room background PNG in an image editor.
2. Overlay a 40×18 grid (each cell 8×8 px).
3. For each cell, decide solid (`1`) or empty (`0`).
4. Hand-write the JSON (rows top to bottom).
5. Reference the file in the room builder via `RoomLoader.BuildCollisionTileMap`.

This takes 20–30 minutes per room. The DEVELOPMENT_PHASES Phase 5A roadmap proposes a tool that loads a PNG, overlays a clickable grid, and emits the JSON.

### Existing Files

```
assets/data/
├── collision_chateau0.json
├── collision_chateau1.json
├── collision_chateau2.json
├── collision_stonehenge.json
├── collision_tunnelmouth.json
└── collision_wastelands.json
```

Three of these have companion `_debug.png` files showing the painted-over collision overlay used during the original authoring pass.

## Debug Overlay (F2)

`Game1.DrawCollisionMaskOverlay`:

- Looks up `_roomManager.CurrentTileMap.PixelMask`.
- Builds a `Texture2D` once per mask change with semi-transparent red on every solid pixel.
- Caches by reference identity — when the mask reference changes (room transition), the overlay rebuilds.
- Renders at full render scale.

This shows the **pixel** mask (background-derived), not the **tile** mask. To visualize the JSON-driven tile collision instead, you would need to add a separate overlay that walks the tilemap and tints each `IsTileBlocking` cell. Not currently implemented.

## Ground Detection

`PhysicsComponent.IsOnGround` is set every frame via:

```
IsOnGround = CheckOnGround(pos) || CheckOnGroundSolidRects(pos)
```

- `CheckOnGround` — center-only column of the tile directly below the player's feet (see "The 2-Pixel Horizontal Inset" above for why center-only).
- `CheckOnGroundSolidRects` — feet rect (1 px tall, full hitbox width) intersected against `SolidRects` (doors).

Used by:

- `GuardController.Update` — guard only walks when on ground; otherwise applies pure gravity.
- (Future) Energy/death system, if "fall damage" is ever introduced.

## Edge Detection (Guards)

Guards have an additional `HasFloorAhead(direction)` check that samples the tile under the leading foot + 1 px ahead. If empty, the guard stops at the platform edge instead of walking off. This is per-controller, not part of `PhysicsComponent`.

## Common Pitfalls

- **"My new enemy walks through walls."** Check `physics.TileMap = _roomManager.CurrentTileMap` was set after spawn AND re-set in `LoadRoomEnemies` after a room transition (the snapshot keeps a stale reference).
- **"My new enemy clips into doors."** Check `UpdateDoorCollision(physics)` was called for that physics instance. Game1 only calls it on the player and on enemies during `SpawnEnemy`.
- **"Player gets stuck inside geometry."** Pixel collision has an "embedded" fallback (`ResolveHorizontalPixelCollision` allows movement out of solid geometry if the OLD position was already solid). Tile collision doesn't have this fallback because tile authoring shouldn't produce embedded states. If you see the player wedged, check the collision JSON — usually a stray `1` somewhere a `0` should be.
