# 02 — Architecture

The codebase is organized around a lightweight Entity-Component-System (ECS) and a small set of singleton subsystems owned by `Game1`. The goal of the Phase-4A refactor was to keep `Game1.cs` thin (init, update dispatch, draw dispatch) and push *every* domain concern into a dedicated file.

## Top-Level Diagram

```
                         ┌──────────────────┐
                         │     Program      │  Main() — bootstrap
                         └────────┬─────────┘
                                  │
                         ┌────────▼─────────┐
                         │      Game1       │  MonoGame Game subclass
                         └────────┬─────────┘
                                  │ holds
        ┌────────────┬────────────┼────────────┬────────────┐
        ▼            ▼            ▼            ▼            ▼
  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐
  │WorldState│ │ItemSystem│ │RoomMgr   │ │ Player   │ │ Per-room │
  │          │ │          │ │ + tiles  │ │ Entity   │ │ runtime  │
  │persistent│ │textures  │ │ + doors  │ │ (ECS)    │ │ lists    │
  │  state   │ │+ kill mtx│ │ + bgs    │ │          │ │          │
  └──────────┘ └──────────┘ └──────────┘ └──────────┘ └──────────┘
```

Per-room runtime lists held by `Game1`: `_roomEnemies`, `_roomItems`, `_roomWizards`, `_roomBlockedDoors`, `_projectiles`. These are cleared and re-populated on every room transition; their durability is owned by `WorldState`.

## ECS Pattern

Every dynamic actor in the world is an `Entity` (`Core/Entity.cs`) with a bag of `IComponent` instances:

```csharp
var player = new Entity("Player");
player.AddComponent(new PhysicsComponent());
player.AddComponent(new PlayerController());
player.AddComponent(new SpriteComponent(sheet, frame0));
```

Rules:

- One component instance per type per entity (enforced by `AddComponent`).
- Components access each other through `Owner.GetComponent<T>()`.
- `Entity.Update(GameTime)` walks all components and calls `Update`. Same for `Draw` (most components no-op `Draw` because rendering is centralized in `Game1.Draw`).
- The `IComponent` interface is intentionally minimal: `Owner`, `Update`, `Draw`. No event bus, no signals.

### Enemy Composition Example

```csharp
var entity = new Entity(id);
entity.AddComponent(new PhysicsComponent { Speed = ..., GravitySpeed = ... });
entity.AddComponent(new SpriteComponent(sheet, idleFrame));
entity.AddComponent(new GuardController(playerRef));
```

The `GuardController` is the brain; it reads player position and writes velocity to the `PhysicsComponent`, which is then resolved by collision the next tick. See [08_ENEMIES.md](./08_ENEMIES.md) for the per-controller breakdown.

## Subsystems Owned by `Game1`

### `WorldState` — `Core/WorldState.cs`

The persistent layer. Survives every room transition; reset only on `RestartGame()`.

- `HashSet<string>` of dead-enemy / picked-item / saved-wizard / unlocked-door IDs
- `Dictionary<string, List<EnemyInstance>>` snapshots per room (so you can leave and return)
- `CarriedItem`, `SavedWizardCount`, `SpawnCounter`

This is the eventual save-file target. JSON-serializing `WorldState` is a one-liner (Phase 7).

### `ItemSystem` — `Core/ItemSystem.cs`

- `ItemType` enum (`None, Sword, BallAndChain, Axe, ShootingStar, Lyre`)
- `Register(type, texture, sourceRect)` populates lookup dictionaries during `LoadContent`
- `GetTexture` / `GetSourceRect` for any rendering site
- Static `CanKillEnemy(enemyType, weapon)` — the weapon-enemy effectiveness matrix

Adding an item: extend the enum, call `Register` once in `Game1.LoadContent`. No other files change. (When the item is also a weapon or key, also extend `CanKillEnemy` or `BlockedDoorSpawn`.)

### `RoomManager` — `Rooms/RoomManager.cs`

Owns the *current room only*: tilemap, doors, background, transition state.

- `RegisterRoom(id, builder, displayName?)` — stores a builder lambda; called once per room at startup
- `LoadRoom(id)` — runs the builder, populates `CurrentTileMap` / `CurrentDoors` / `CurrentBackground`
- `CheckDoorTriggers(playerPos, w, h)` — every-frame door collision check
- `Update(dt)` — drives door animation; returns `(targetRoom, targetDoor)` when ready
- `ExecuteTransition(playerWidth)` — loads target room, returns spawn position at the matching door

When a background is set, the manager auto-builds a pixel-perfect collision mask via `TileMapComponent.BuildPixelMaskFromTexture` (see [06_COLLISION.md](./06_COLLISION.md)).

### `RoomRegistry` — `Rooms/RoomRegistry.cs`

A static dictionary mapping `roomId` → `RoomContent` (enemies, items, wizards, blocked doors). This is **separate** from layout registration in `RoomManager`:

- `RoomManager.RegisterRoom` — *layout* (background, collision, doors)
- `RoomRegistry.Initialize` — *content* (what spawns where)

Adding content to a room is one entry in `RoomRegistry.Initialize`.

## File Ownership Map

| Folder | Owns |
|--------|------|
| `Core/` | ECS primitives (`Entity`, `IComponent`), `PlayerController`, `WorldState`, `ItemSystem`, `GameEntities` (runtime instance classes) |
| `Physics/` | `PhysicsComponent` (the live one), `DirectVelocityComponent` (legacy/unused) |
| `Graphics/` | `SpriteComponent` (animated rendering), `SpriteConfig` (frame coordinates, speeds, thresholds) |
| `Tiles/` | `TileMapComponent` (8×8 grid + optional pixel mask), `TileConfig` (tile IDs and `IsSolid`/`IsPlatform` predicates) |
| `Doors/` | `DoorComponent` (state, alignment check, animation), `DoorConfig` (constants, frame rectangles) |
| `Enemies/` | One controller per enemy type — pure AI, no rendering |
| `Rooms/` | `RoomManager`, `RoomLoader` (JSON), `RoomData` (DTO, used by future tooling), `RoomRegistry` (content) |
| `Content/` | Spritesheets, fonts, MGCB content pipeline definition |
| `assets/data/` | Per-room collision JSON (`collision_<roomId>.json`) |
| `assets/images/` | Original Amstrad spritesheet (runtime fallback path) |

## Update Loop (per frame)

```
Game1.Update(gameTime)
├── Read input, F1/F2 toggles, debug spawn keys (D2..D5)
├── if RoomManager.IsGameFrozen:
│   └── UpdateDoorTransition(dt, gameTime)
│       ├── RoomManager.Update(dt)            ← drives door anim
│       └── if (transition ready):
│           ├── SaveRoomEnemies(currentRoomId)
│           ├── ExecuteTransition()           ← swap room
│           ├── LoadRoomEnemies(newRoomId)
│           └── SpawnRoomContent(newRoomId)
└── else:
    └── UpdateGameplay(dt, gameTime, keys)
        ├── TryPickupItem (Space)
        ├── FireShootingStar (Space + carrying ShootingStar)
        ├── Melee kill check (Space held + carrying weapon + overlap + matrix)
        ├── _player.Update()                  ← PlayerController writes velocity → PhysicsComponent moves & collides
        ├── UpdateEnemies()                   ← per-enemy controller + physics
        ├── UpdateBlockedDoors()              ← unlock if carrying RequiredItem
        ├── UpdateWizards(dt)                 ← anim + rescue check + saved-wizard fly-up
        ├── UpdateProjectiles(dt)             ← move + bounds + per-enemy hit
        └── RoomManager.CheckDoorTriggers()   ← may freeze game for transition
```

## Draw Loop (per frame)

```
Game1.Draw(gameTime)
├── 1. Render game area to RenderTarget2D (320*3 × 144*3, point-clamp):
│   ├── RoomManager.DrawBackground(scale=3)         ← background image if set
│   ├── if (no background) RoomManager.CurrentTileMap.Draw(scale=3)
│   ├── RoomManager.DrawDoors(scale=3)
│   ├── Blocked doors
│   ├── Captive wizards (animated)
│   ├── Room items
│   ├── Player sprite
│   ├── Enemies (death anim or normal)
│   ├── Projectiles
│   └── if (F2) DrawCollisionMaskOverlay()
├── 2. Stretch render target to back buffer (game area only)
├── 3. DrawInfoPanel (room name, carried item, saved wizards, item icon)
└── 4. if (F1) DrawDebugInfo (FPS, room id, position, velocity, controls)
```

The double-pass (offscreen render target → back buffer) is the standard way to keep pixel art crisp at non-integer scales. `SamplerState.PointClamp` everywhere.

## Phase-4A Refactor — What Moved Where

Before Phase 4A, `Game1.cs` was ~1,732 lines and held the item enum, all enemy types, all spawn switches, the full kill matrix, and the per-room state. Phase 4A (the current state) extracted:

- `WorldState.cs` — persistent state
- `ItemSystem.cs` — items + kill matrix
- `GameEntities.cs` — `EnemyInstance`, `ItemInstance`, `CaptiveWizard`, `BlockedDoorInstance`, `Projectile`
- `RoomRegistry.cs` — per-room content as data, not switch statements

`Game1.cs` is now ~1,300 lines and shrinking. Layout registration (`RegisterTestRooms`, `RegisterBackgroundRooms`, `RegisterChateauRooms`) still lives there but is the next extraction target.
