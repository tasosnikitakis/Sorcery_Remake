# 03 — Developer Guide

Everything a contributor needs to build, run, and inspect the game.

## Prerequisites

- **.NET SDK 8.0** — https://dotnet.microsoft.com/download/dotnet/8.0
  - Verify: `dotnet --version` should print `8.x`.
- **MonoGame 3.8.1.303** — pulled automatically as a NuGet package, no separate install required.
- (Optional, only when adding content) **MonoGame Content Builder** — invoked by the `MonoGameContentReference` in the `.csproj` during `dotnet build`.

The project targets `net8.0`, has nullable reference types enabled, and uses `RollForward=Major` so newer minor 8.x SDKs work transparently.

## Build & Run

From the repo root:

```bash
dotnet restore        # one-off, on first checkout or after package changes
dotnet build          # compiles + runs MGCB content pipeline
dotnet run            # launches the game
```

Two convenience scripts also exist:

- `build.bat` — Windows
- `build.sh` — macOS / Linux

The game launches in a 960×600 window (320×144 game area + 320×56 info panel, all scaled 3×).

## Player Controls

| Key | Action |
|-----|--------|
| **Arrow keys** | Move (Left/Right/Up/Down) — Up thrust counters constant gravity |
| **Space (tap on item)** | Pick up the item the player is touching |
| **Space (held while touching enemy)** | Use carried weapon — kills enemy if matrix allows; consumes the weapon |
| **Space (carrying Shooting Star)** | Fire 8-direction projectile burst from player center; consumes the Shooting Star |
| **R** | Restart the game (reset world state, return to chateau_0) |
| **Esc** | Exit |

The Shooting Star is a one-shot AOE: it always fires when you tap Space while carrying it, regardless of enemy proximity, and the weapon is consumed.

## Debug Controls

| Key | Action |
|-----|--------|
| **F1** | Toggle debug overlay (FPS, room id, player position/velocity, enemy count, control hints) |
| **F2** | Toggle collision-mask overlay (semi-transparent red on every solid pixel of the current room) |
| **D2** | Spawn a Mask enemy at a random empty tile |
| **D3** | Spawn a Boar enemy at a random empty tile |
| **D4** | Spawn an Eye enemy at a random empty tile |
| **D5** | Spawn a Wraith enemy at a random empty tile |

Spawned enemies are tagged `spawned_<type>_<n>` so `WorldState.DeadEnemies` tracks them like any other. They are NOT persisted across room transitions in `WorldState.SavedRoomEnemies` because they were never part of `RoomRegistry`.

## Project Layout (developer view)

See [02_ARCHITECTURE.md](./02_ARCHITECTURE.md#file-ownership-map) for a complete folder ownership map. The fastest mental model:

- Want to change movement feel? `Physics/PhysicsComponent.cs` + `Core/PlayerController.cs`
- Want to add a room? `Game1.RegisterChateauRooms` (layout) + `Rooms/RoomRegistry.cs` (content) + `assets/data/collision_<id>.json`
- Want to add an enemy type? New file in `Enemies/` + extend `EnemyType` enum + extend `ItemSystem.CanKillEnemy` + extend the spawn switch in `Game1.SpawnEnemy`
- Want to change a sprite frame? `Graphics/SpriteConfig.cs`

## Common Tasks

### Adding a Room

See [07_WORLD_BUILDING.md](./07_WORLD_BUILDING.md#adding-a-new-room) for the canonical workflow.

### Tuning Physics

See [05_PHYSICS.md](./05_PHYSICS.md#tuning-knobs). The dials are `Speed` and `GravitySpeed` on `PhysicsComponent`, plus `COLLISION_INSET_X` for hitbox forgiveness.

### Adding an Item

See [09_ITEMS_AND_COMBAT.md](./09_ITEMS_AND_COMBAT.md#adding-a-new-item).

### Generating a Collision JSON

See [06_COLLISION.md](./06_COLLISION.md#authoring-a-collision-grid).

## Where Things Print

- **Stdout** — nothing routine. Asset-load failures fall through `try/catch` and use a magenta-pixel placeholder texture; nothing is logged.
- **Debug overlay (F1)** — runtime stats overlaid on the game.
- **Window title** — defaults to "MonoGame", not customized.

If you need to log, the conventional path is `System.Diagnostics.Debug.WriteLine`, which surfaces in the IDE Debug Output and in `dotnet run` console.

## Asset Pipeline

`Content/Content.mgcb` is the MonoGame Content Builder file: every asset built into the binary lives there. `dotnet build` invokes MGCB via the `MonoGame.Content.Builder.Task` package.

The runtime has a fallback path for the player spritesheet: if `Content.Load<Texture2D>("Characters")` fails, it tries `assets/images/Amstrad CPC - Sorcery - Characters.png` directly via `Texture2D.FromStream`. If that also fails, a 16×16 magenta placeholder is generated. Other textures have no fallback — missing them throws and crashes load.

Per-room collision JSON files live in `assets/data/` and are copied to the output directory by the `<Content>` block in `SorceryRemake.csproj` (not by MGCB). They are read at runtime via `File.ReadAllText` (`RoomLoader.LoadCollisionGrid`).

See [11_ASSETS.md](./11_ASSETS.md) for the complete asset inventory.

## Diagnostics

### "Player can't fit through a 24-pixel shaft"

Check `PhysicsComponent.COLLISION_INSET_X` (currently `2`). The collision box is inset 2px on each side from the 24-px sprite, giving 20-px effective width — see [06_COLLISION.md](./06_COLLISION.md#the-2-pixel-horizontal-inset).

### "Player walks through clouds in Stonehenge"

By design. The pixel mask is built via `BuildPixelMaskFromTexture` with a flood-fill from the bottom row, so only ground-connected pixels are solid. Floating decorations are passable. See [06_COLLISION.md](./06_COLLISION.md#background-pixel-mask).

### "Wraith goes through walls"

Also by design. `WraithController` doesn't sample the tilemap — its physics has `GravitySpeed = 0` and the wraith's `PhysicsComponent` has no `TileMap` set on spawn. The wraith is bounded only by `ClampToScreen`.

### "Enemies disappeared after I went next door and came back"

They didn't — `SaveRoomEnemies` snapshotted them into `WorldState.SavedRoomEnemies` and `LoadRoomEnemies` restores them on return. If you get a "no enemies on return" symptom, check that `enemy.IsDying` wasn't set, since dying enemies are dropped from the snapshot.

### "Black room with the player floating"

A room background failed to load and there's no tilemap visuals, but a collision tilemap was set. This is normal during development of new background-image rooms — the background field of the room builder is null but the collision JSON loaded fine. Press F2 to confirm collision is present.

## Build Outputs

- `bin/Debug/net8.0/SorceryRemake.exe` — Windows binary
- `bin/Debug/net8.0/Content/` — built `.xnb` assets
- `bin/Debug/net8.0/assets/` — copied collision JSONs

The `obj/` directory holds intermediate MSBuild artifacts; safe to delete.
