# 01 — Project Overview

## What is Sorcery+?

**Sorcery+** (Virgin Games, 1985) is an Amstrad CPC action-adventure that pioneered several mechanics still rare in 8-bit games:

- **Flight physics** — the player hovers and constantly fights gravity rather than walking.
- **75-room interconnected map** — 47 rooms in Chapter 1, 28+ in Chapter 2.
- **Memory-based puzzles** — healing and poisonous cauldrons look identical; the player has to remember which is which.
- **One-item inventory** — picking up a new item drops your current one in its place.
- **Weapon-enemy effectiveness matrix** — different weapons defeat different enemy types; the wrong weapon is harmless.
- **Crumbling-book timer** — a global countdown that ends the game whether or not the player has died.

## What is the Remake?

A faithful, pixel-perfect rebuild of the original using modern engineering practices. The goal is **authenticity over modernization**: physics constants, sprite frames, and gameplay rules should match the 1985 release frame-for-frame, while the *codebase* uses ECS, version control, and testable components.

The remake is currently in active development. The codebase has just completed **Phase 4A** (refactoring `Game1.cs` into modular subsystems) and is moving toward energy/health systems, room authoring tooling, and content scale-up.

## Current Implementation Status

### Working (in main branch)

- ECS scaffolding (`Entity`, `IComponent`, component registry per entity)
- Direct-velocity flight physics with gravity (`PhysicsComponent`)
- Tile-based collision (8×8 grid) with separate-axis resolution and edge-tolerant hitbox
- Pixel-mask collision generated from background images (with floor flood-fill so floating clouds are passable)
- Multi-room system with door transitions and animation
- Background-image rooms (Stonehenge, Wastelands, Tunnel Mouth, Chateau 0/1/2)
- Five enemy AI types: Guard (ground-following), Mask/Boar/Eye (floating chasers), Wraith (no-clip chaser)
- Item system with five items (Sword, Ball-and-Chain, Axe, Shooting Star, Lyre)
- Weapon-enemy effectiveness matrix
- Captive wizard rescue with star-transform animation
- Blocked doors that require specific items to unlock
- Shooting Star projectile (8-direction radial burst from player center)
- Persistent world state across rooms (dead enemies, picked-up items, saved wizards, unlocked doors)
- Debug overlays (F1: stats, F2: collision mask)

### Not Yet Implemented

- Energy / health system (player is currently immortal)
- Lives system and game-over flow
- Cauldron healing/poison mechanic
- Crumbling-book global timer
- Title screen / menus / pause
- Audio (music + SFX)
- Save/load
- Most of the 75-room map (5 rooms exist; 70+ remain)
- The full item set (14+ sprites are extracted but only 5 are wired up)

See [12_ROADMAP.md](./12_ROADMAP.md) for the planned phasing.

## Design Principles

1. **Authenticity over modernization.** No quality-of-life changes that alter gameplay. Physics, sprite timings, and rules should match the original frame-by-frame.
2. **Modern engineering.** Clean ECS, comprehensive inline annotations, Git-tracked, testable components.
3. **Data-driven content.** Rooms, items, and enemies are defined in registries and JSON — adding new content should be one entry, not a code change.
4. **Extensibility.** The architecture leaves room for a future `SorceryForge` map editor and modding pipeline.

## Original vs. Remake — Technical Snapshot

| | Original (1985) | Remake (2026) |
|---|---|---|
| Platform | Amstrad CPC 6128 | Windows / Linux / macOS |
| CPU | Zilog Z80A @ 4 MHz | Modern x86_64 / ARM64 |
| Graphics | Mode 0, 160×200, 16 colors | 320×144 (game) + 56-px panel, scaled 3× |
| Sound | AY-3-8912 PSG (3 channels) | MonoGame audio (planned) |
| Media | 3″ disk, 180 KB | .NET 8 binary, ~tens of MB w/ assets |

## Repository Layout (selected)

```
Sorcery_Remake/
├── Core/              # ECS base, world state, item system, player controller, game entities
├── Physics/           # PhysicsComponent (tile + pixel collision), DirectVelocityComponent (legacy)
├── Graphics/          # SpriteComponent, SpriteConfig (frame coords)
├── Tiles/             # TileMapComponent, TileConfig (tile IDs/properties)
├── Doors/             # DoorComponent, DoorConfig
├── Enemies/           # GuardController, MaskController, BoarController, EyeController, WraithController
├── Rooms/             # RoomManager, RoomLoader (JSON), RoomData, RoomRegistry
├── Content/           # Spritesheets, fonts, MGCB content pipeline
├── assets/
│   ├── data/          # Per-room collision_*.json
│   └── images/        # Original spritesheet
├── doc/               # ⬅ You are here
├── docs/              # Legacy Phase-1 notes
├── Game1.cs           # Main game class (orchestrator)
├── Program.cs         # Entry point
└── SorceryRemake.csproj
```

See [02_ARCHITECTURE.md](./02_ARCHITECTURE.md) for what each folder owns.
