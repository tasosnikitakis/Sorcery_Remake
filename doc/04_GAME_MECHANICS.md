# 04 — Game Mechanics

This document describes the rules of play: how the player interacts with the world, what each input does, and what gameplay states currently exist.

## The Core Loop

A player session today consists of:

1. Spawn at the chateau-0 entry position (`Vector2(160, 80)`).
2. Hover and fly across rooms, fighting constant gravity.
3. Pick up items by overlapping them and pressing **Space**. Picking up a new item drops the current one *at the picked-up item's position*.
4. Use weapons against enemies by being adjacent and holding **Space** while carrying a weapon that matches the enemy type. Wrong weapon = no effect; correct weapon = enemy dies, weapon is consumed.
5. Rescue captive wizards by overlapping them. They transform into a star, fly upward, and disappear off-screen. The saved-wizard count increments.
6. Open blocked doors by carrying their `RequiredItem` and overlapping the door. The door unlocks permanently and the item is consumed.
7. Trigger room transitions by walking flush against a door's active edge. The door animates open, the game freezes, and the player teleports to the matching door in the target room.

The eventual loop adds: energy/health, lives, cauldron healing, the crumbling-book global timer, and a win condition (rescue all wizards before the timer expires). None of these exist yet — the game today has no fail state.

## Input

Player input is read in two places, both each frame:

1. `Game1.Update` — global keys (Esc, R, F1, F2, debug spawn keys D2-D5, Space for pickup/weapon use)
2. `PlayerController.Update` — movement (arrows)

Movement is handled by `PlayerController.Update` writing directly to `PhysicsComponent.Velocity`:

```csharp
// HORIZONTAL
if (Left  && !Right) targetX = -Speed;
else if (Right && !Left) targetX =  Speed;
else targetX = 0;             // instant stop, no momentum

// VERTICAL — gravity always applies
float currentVerticalVelocity = _physics.GravitySpeed;
if (pressingUp)        currentVerticalVelocity = -_physics.Speed;
else if (pressingDown) currentVerticalVelocity =  _physics.Speed;

_physics.Velocity = new Vector2(targetX, currentVerticalVelocity);
```

This is **direct velocity assignment** — there is no acceleration, no inertia, no damping. It matches the original CPC behavior exactly. See [05_PHYSICS.md](./05_PHYSICS.md) for why.

The crucial detail: gravity is *always* the default vertical velocity. Pressing Up replaces it with `-Speed`. The wizard cannot stand still in mid-air; he is always being pulled downward unless actively thrusting.

## Player Animation State Machine

`PlayerController` chooses one of three animations every frame, based on horizontal velocity only:

| State | Trigger | Frames | Speed |
|-------|---------|--------|-------|
| `idle_front` | `vx ≈ 0` (no horizontal motion) | 4 frames at row Y=75, x=100..175 | 0.117 s/frame |
| `walk_left` | `vx < -10` | 4 frames at row Y=75, x=0..75 | 0.10 s/frame |
| `walk_right` | `vx > 10` | 4 frames at row Y=75, x=200..275 | 0.10 s/frame |

Vertical motion has no dedicated animation — when flying up or falling, the player still shows `idle_front` (matching the Python prototype and original game). `PLAYER_FLYING_UP` and `PLAYER_FALLING` are aliased to `PLAYER_IDLE_FRONT` in `SpriteConfig`.

The `walk_left` frames are NOT a horizontally-flipped `walk_right`; the sheet contains separate left-facing artwork. This is why `FlipHorizontal` is always `false`.

## Item Pickup

When the player presses Space:

1. Iterate `_roomItems`. If any item's display rectangle (24×24 at its position) overlaps the player's hitbox + 1-px margin:
2. Mark the item ID in `WorldState.PickedUpItems` (so it never re-spawns even after leaving and returning).
3. Remove it from `_roomItems`.
4. If the player was already carrying something, create a new `ItemInstance` at the picked-up item's position carrying the *previously held* item with id `dropped_<spawnCounter++>`. The dropped item's ID is unique per session and is NOT in `PickedUpItems`, so it persists in the room until explicitly picked up again.
5. Set `WorldState.CarriedItem` to the new pickup type.

The "one item" rule is therefore strict: you can't accumulate items. The dropped item swap means rooms become a permanent puzzle — a Sword left in room 5 is still in room 5 when you return.

## Weapon Use

Two distinct paths share the **Space** key:

- **Tap (just-pressed)** → first try `TryPickupItem`; if it succeeds, the rest of this frame's Space-handling is skipped.
- **Tap, no pickup, carrying ShootingStar** → fire 8 projectiles radially from player center, consume the ShootingStar.
- **Held + carrying any weapon + overlapping enemy whose type matches `CanKillEnemy`** → enemy enters `IsDying`, weapon is consumed (`CarriedItem = None`).

The `IsOverlapping(Entity, Entity)` check uses a 1-px margin so enemies don't have to perfectly coincide with the player.

## Captive Wizard Rescue

`CaptiveWizard` instances animate a 4-frame loop (`CAPTIVE_WIZARD_ANIM`) bottom-to-top while idle. When the player overlaps the wizard's 24×24 display rectangle:

1. `wiz.IsSaving = true`
2. The texture and animation switch to `_starSheet` / `STAR_ANIM` (top-to-bottom 4 frames).
3. The wizard moves upward at `PROJECTILE_SPEED` (200 px/s) until it goes off the top of the screen, then despawns.
4. If `!wiz.CountedAsSaved`, increment `WorldState.SavedWizardCount` and add the ID to `SavedWizards`. The flag prevents double-counting if a save is interrupted somehow.

The wizard is stored in `_roomWizards` per-room runtime list, but `WorldState.SavedWizards` is the persistent record — saved wizards do not respawn even when re-entering the room.

## Blocked Door Unlock

`BlockedDoorInstance` has a `RequiredItem` (e.g., `ItemType.Lyre` for the iron door in room_2). Each frame:

1. Build the door's hitbox (an 8-px-wide vertical bar centered in the 24×24 sprite area).
2. If the player's hitbox + 1-px margin intersects, AND `WorldState.CarriedItem == bd.RequiredItem`:
   - Add door ID to `WorldState.UnlockedDoors`.
   - Clear `CarriedItem` (the key is consumed).
   - Remove from `_roomBlockedDoors`.
   - Call `RebuildSolidRects` so the player's `PhysicsComponent.SolidRects` no longer includes the door.

Re-entering the room won't restore the door; `SpawnRoomContent` skips any blocked door already in `UnlockedDoors`.

## Room Transitions

Doors (`DoorComponent`) come in two types:

- `LeftOpening` — player approaches from the left; aligned when `playerRight ≈ doorLeft`
- `RightOpening` — player approaches from the right; aligned when `playerLeft ≈ doorRight`

Each frame, `RoomManager.CheckDoorTriggers` iterates the current doors and asks each: "is the player aligned with my active edge?" Alignment requires:

- Y position within 2 px of the door's Y
- The relevant edge within 3 px of the door's edge

When aligned, the door starts opening and `TransitionState` becomes `DoorOpening`. The game enters a frozen state — `Update` no longer runs gameplay, only door animation. After 4 frames × 0.15 s = 0.6 s of animation, the transition fires:

1. `SaveRoomEnemies(currentRoomId)` — snapshot non-dying enemies into `WorldState.SavedRoomEnemies`
2. `RoomManager.ExecuteTransition` — `LoadRoom(targetRoom)`, find the matching door by `TargetDoorId`, return its arrival position
3. Player teleports to the arrival position (which is offset 5 px outside the door so the player doesn't immediately re-trigger it)
4. Player velocity zeroed
5. `LoadRoomEnemies(newRoomId)` — restore snapshot if any
6. `SpawnRoomContent(newRoomId)` — spawn fresh items/wizards/doors, plus enemies if first visit

See [07_WORLD_BUILDING.md](./07_WORLD_BUILDING.md) for door layout conventions.

## Restart

Pressing **R** at any time:

1. `WorldState.Reset()` — clear all hashsets, reset counters
2. Clear all per-room runtime lists
3. `RoomManager.LoadRoom("chateau_0")` — back to the start
4. Reset player position, velocity, tilemap reference
5. `SpawnRoomContent("chateau_0")` — fresh content

There is no "are you sure" prompt; R is unconditional.

## Coordinate System

- **Game world:** 320×144 in base pixels (Amstrad-equivalent). All gameplay logic uses these units.
- **Tile grid:** 40×18, each tile 8×8 px. So `tileX = pixelX / 8`.
- **Render scale:** 3×. Multiplying by `RENDER_SCALE` is the only place game-space converts to render-space.
- **Window:** 960×600 (320×3 wide, 144×3 game area + 56×3 info panel).

Y-axis grows downward (standard 2D / MonoGame convention). The origin is top-left.

## What This Document Does NOT Cover

- Detailed flight physics math → [05_PHYSICS.md](./05_PHYSICS.md)
- Collision resolution and pixel masks → [06_COLLISION.md](./06_COLLISION.md)
- Per-enemy AI behavior → [08_ENEMIES.md](./08_ENEMIES.md)
- Weapon-enemy effectiveness matrix → [09_ITEMS_AND_COMBAT.md](./09_ITEMS_AND_COMBAT.md)
- Future mechanics (energy, lives, cauldrons, timer) → [12_ROADMAP.md](./12_ROADMAP.md)
