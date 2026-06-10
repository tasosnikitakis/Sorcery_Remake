# 08 — Enemy AI

The remake currently ships **five enemy types** with three distinct behavioral archetypes. This document is the per-controller reference plus the shared spawn/lifecycle plumbing.

## Enemy Archetypes

| Type | Archetype | Gravity | Tilemap collision | Door collision | Edge detection | Speed |
|------|-----------|---------|-------------------|---------------|----------------|-------|
| **Guard** | Ground-walker | Yes (160 px/s default — `Speed` overridden to 80) | Yes | Yes | Yes (won't walk off platforms) | 80 px/s |
| **Mask** | Floating chaser | No (`GravitySpeed=0`) | Yes | Yes | No | 150 px/s |
| **Boar** | Floating chaser | No | Yes | Yes | No | 150 px/s |
| **Eye** | Floating chaser | No | Yes | Yes | No | 150 px/s |
| **Wraith** | No-clip chaser | No | **No** (no `TileMap` set) | No | No | 220 px/s (fastest) |

Mask, Boar, and Eye are mechanically identical floating chasers — same AI, same speed, same threshold, only the sprite sheet and frame layout differ. The *original* game distinguished them by size and weak-spot, which the remake replicates by changing only the visuals; behavior parity is intentional.

The Guard is the only enemy that walks. The Wraith is the only enemy that ignores world geometry.

## Per-Type Reference

### Guard (`Enemies/GuardController.cs`)

The hooded guard patrols the floor and follows the player horizontally on the same platform.

**Update logic:**

```
1. If !IsOnGround:
     velocity = (0, GravitySpeed)        // pure gravity, no AI input
     return
2. dx = player.X - guard.X
3. If |dx| < GUARD_FOLLOW_THRESHOLD (2 px):  // dead zone
     velocity = (0, GravitySpeed); idle anim; return
4. Determine direction (sign of dx)
5. If !HasFloorAhead(direction):
     velocity = (0, GravitySpeed); idle anim; return  // platform edge stop
6. velocity = (direction * GUARD_SPEED, GravitySpeed)
7. animation = walk_left or walk_right
```

**Edge detection** (`HasFloorAhead`):

- Looks at the tile under the leading foot + 1 px ahead in the move direction.
- Returns true only if that tile is solid or a platform.
- Prevents the guard from walking off a ledge while the player is mid-air below.

**Animation:**

- 12 frames at row Y=0 in `GuardSheet.png`, 25 px apart (24 + 1 spacing).
- Frames 0–3: walk_left
- Frames 4–7: idle / direction change
- Frames 8–11: walk_right
- Speed: 0.12 s/frame for all states.

**Counters:** Sword (`ItemSystem.CanKillEnemy(Guard, Sword)` → true).

### Mask (`Enemies/MaskController.cs`)

A floating chaser that pursues the player in both axes, normalizing the direction vector for steady diagonal speed.

**Update logic:**

```
dx = player.X - mask.X
dy = player.Y - mask.Y
dist = sqrt(dx*dx + dy*dy)
if dist < 1: velocity = 0; return    // overlap stop
velocity = (dx/dist, dy/dist) * MASK_SPEED
```

That's the entire AI. The mask doesn't pathfind — it goes straight at you and is blocked by tile/door collision. In rooms with internal walls, the mask "presses against" walls at constant velocity; this is intentional and matches the original.

**Animation:** 4-frame loop at row Y=0 in `MaskSheet.png`. Plays continuously regardless of state.

**Counters:** Ball-and-Chain.

### Boar (`Enemies/BoarController.cs`)

Behavior identical to Mask. The only differences:

- Sprite sheet (`BoarSheet.png`) — 22-px-wide frames (2 px thinner than the standard 24).
- Per-frame Y offset for the green-line crop on top: frame 0 = 0, frame 1 = 1, frame 2 = 2, frame 3 = 1.
- Source rectangle list is slightly wonky-looking but produces a clean visual loop. See `SpriteConfig.BOAR_ANIM`.

**Counters:** Ball-and-Chain.

### Eye (`Enemies/EyeController.cs`)

Behavior identical to Mask and Boar. Visual differences:

- 24×17 frames (shorter than the standard 24×24).
- 4-frame horizontal strip in `EyeSheet.png`.

**Counters:** Ball-and-Chain.

### Wraith (`Enemies/WraithController.cs`)

A no-clip chaser. Differences from the floating chasers:

- **No tilemap collision.** The wraith's `PhysicsComponent` is created without a `TileMap` reference, so it falls through the "just clamp to screen" path in `PhysicsComponent.Update`. It can pass through walls, doors, blocked doors, and platforms.
- **Fastest enemy.** `WRAITH_SPEED = 220` px/s vs player 140. The wraith *will* catch you in open space.
- **Directional animations.** Unlike the floating chasers' single loop, the wraith uses a guard-style 3-state animation set (walk_left / idle / walk_right) keyed on horizontal velocity vs `ANIMATION_VELOCITY_THRESHOLD`.
- **Follow threshold:** 2 px (`WRAITH_FOLLOW_THRESHOLD`) — within 2 px of the player, it stops and idles.

The wraith is the only enemy you cannot evade with terrain. The only counter is the Axe.

**Counters:** Axe.

## The Weapon-Enemy Matrix

Defined as a static method in `ItemSystem`:

```csharp
public static bool CanKillEnemy(EnemyType enemyType, ItemType weapon)
{
    return enemyType switch
    {
        EnemyType.Guard  => weapon == ItemType.Sword,
        EnemyType.Eye    => weapon == ItemType.BallAndChain,
        EnemyType.Mask   => weapon == ItemType.BallAndChain,
        EnemyType.Boar   => weapon == ItemType.BallAndChain,
        EnemyType.Wraith => weapon == ItemType.Axe,
        _ => false
    };
}
```

**Wrong weapon = no effect, no penalty.** The weapon stays in your inventory; the enemy is unfazed; nothing happens. This is the original game's behavior and is the foundation of the puzzle: knowing which weapon to bring matters.

The **Shooting Star** is an AOE exception: it kills any enemy it touches, regardless of type. See [09_ITEMS_AND_COMBAT.md](./09_ITEMS_AND_COMBAT.md#shooting-star).

## Enemy Lifecycle

### Spawn

`Game1.SpawnEnemy(id, type, position)` is the universal spawn entry. It:

1. Skips if `WorldState.DeadEnemies.Contains(id)` — once dead, never respawns.
2. Creates an `Entity`.
3. Constructs a `PhysicsComponent` with type-specific `Speed`, `GravitySpeed`, and tile-map reference.
4. Adds the type-specific controller (e.g., `GuardController`).
5. Calls `controller.Initialize()` (which sets the initial sprite animation).
6. Adds an `EnemyInstance(id, type, entity)` to `_roomEnemies`.

The spawn switch in `Game1.SpawnEnemy` is one of the largest remaining blocks in `Game1.cs`. Each new enemy type adds a `case`. A future refactor should move enemy construction into per-type factory methods or an `EnemyFactory`.

### Death

When `Space + correct weapon + overlapping`, `Game1.StartEnemyDeath(enemy)`:

1. `enemy.IsDying = true`.
2. `WorldState.CarriedItem = ItemType.None` — the weapon is consumed.
3. Velocity zeroed.
4. Sprite texture swapped to `_deathSheet`.
5. Animation set to `ENEMY_DEATH_ANIM` (4 frames × 0.12 s = 0.48 s, non-looping).

While `IsDying`, the enemy:

- Doesn't update its AI controller (the `UpdateEnemies` loop bypasses it).
- Plays the death animation.
- When animation finishes (`!sprite.IsPlaying`), the enemy is added to `WorldState.DeadEnemies` and removed from `_roomEnemies`.

Death is permanent — the enemy ID is recorded so it never spawns again on this run.

### Room Transition Persistence

When the player leaves a room, `Game1.SaveRoomEnemies(roomId)`:

1. Iterates `_roomEnemies`.
2. Filters out `IsDying` enemies (they don't get re-saved; they finish dying off-screen if you weren't looking).
3. Zeros each saved enemy's velocity.
4. Stores the list in `WorldState.SavedRoomEnemies[roomId]`.
5. Clears `_roomEnemies`.

When the player re-enters, `Game1.LoadRoomEnemies(roomId)`:

- If `SavedRoomEnemies` has an entry, restore it and remove the snapshot.
- Re-set each enemy's `physics.TileMap` to the new (current) `_roomManager.CurrentTileMap`.
- Re-call `UpdateDoorCollision(physics)` so `SolidRects` matches the new room.

If there's no saved snapshot, it's a first visit — `SpawnRoomContent` will spawn fresh from `RoomRegistry`.

## Adding a New Enemy

The current path is a six-step edit (the cleanest possible target after Phase 4A — but still spread across files):

### 1. Extend the `EnemyType` enum

`Core/GameEntities.cs`:

```csharp
public enum EnemyType
{
    Guard, Mask, Boar, Eye, Wraith,
    Bat,    // ← new
}
```

### 2. Add the controller file

`Enemies/BatController.cs` — implement `IComponent`, model behavior after one of the existing controllers. If it's another floating chaser, copy `MaskController` and rename. If it's a new archetype, write fresh.

### 3. Extend SpriteConfig

`Graphics/SpriteConfig.cs` — add frame rectangles, animation speed, and movement speed:

```csharp
public static readonly Rectangle[] BAT_ANIM = new Rectangle[] { ... };
public const float BAT_ANIMATION_SPEED = 0.10f;
public const float BAT_SPEED = 180f;
```

### 4. Load the sprite sheet

`Game1.LoadContent`:

```csharp
_batSheet = LoadAndTransparent("BatSheet");
```

…and add a private field for `_batSheet`. (Plus add the asset to `Content/Content.mgcb`.)

### 5. Add a spawn case

`Game1.SpawnEnemy`:

```csharp
case EnemyType.Bat:
    physics.Speed = SpriteConfig.BAT_SPEED;
    physics.GravitySpeed = 0f;
    physics.TileMap = _roomManager.CurrentTileMap;
    entity.AddComponent(physics);
    UpdateDoorCollision(physics);
    entity.AddComponent(new SpriteComponent(_batSheet, SpriteConfig.BAT_ANIM[0]));
    var batCtrl = new BatController(_player);
    entity.AddComponent(batCtrl);
    batCtrl.Initialize();
    break;
```

### 6. Extend the kill matrix

`Core/ItemSystem.cs` `CanKillEnemy`:

```csharp
EnemyType.Bat => weapon == ItemType.Wand,   // pick the right counter
```

### Optional: Debug spawn key

`Game1.Update`:

```csharp
else if (currentKeyState.IsKeyDown(Keys.D6) && !_previousKeyState.IsKeyDown(Keys.D6))
    SpawnEnemy($"spawned_bat_{_worldState.SpawnCounter++}", EnemyType.Bat, FindRandomEmptyPosition());
```

## Movement Behavior Cheatsheet

When tuning a new enemy, decide:

| Question | Answer determines |
|----------|-------------------|
| Does it walk on floors? | Set `GravitySpeed > 0` and add ground/edge detection |
| Does it float through air? | Set `GravitySpeed = 0` |
| Does it pass through walls? | Don't set `physics.TileMap` (and don't call `UpdateDoorCollision`) |
| Does it follow X only / Y only / both? | Choose between guard-style (X) and mask-style (X+Y) update |
| Does it have directional sprites? | Either guard-style 3-state, or wraith-style 3-state, or single-loop |
| What weapon kills it? | Add to `CanKillEnemy` |

There is currently no pathfinding system — every enemy goes straight at the player and is blocked by terrain. This matches the original game's complexity and is part of the puzzle (lure enemies into chokepoints, bait them off platforms, etc.).

## What's Missing (vs. original)

- **Damage on contact.** Today, touching an enemy does nothing. Phase 4B (energy system) will add 1-point damage per "contact event" with a 1.5-second invincibility timer.
- **Variable enemy stats per room.** All Guards move at 80 px/s everywhere. The original may have had per-instance speed/aggression variation.
- **Spawn animation.** Enemies pop into existence at room load. The original may have had a "wraith fades in" effect.
- **Group AI.** Enemies don't coordinate. There's no flocking, no leader-follow, no pack behavior.

These belong to future phases; see [12_ROADMAP.md](./12_ROADMAP.md).
