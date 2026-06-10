# Sorcery+ Remake — Documentation

A comprehensive technical reference for the Sorcery+ Remake project: a faithful pixel-perfect rebuild of the 1985 Amstrad CPC action-adventure classic, built in C# on MonoGame 3.8 / .NET 8.

This `doc/` folder is the canonical source of truth for the architecture, gameplay systems, world-building pipeline, and development workflow. The legacy `docs/` folder (lowercase) contains older Phase 1 notes and asset-extraction guides that predate the current ECS refactor; refer to this folder first.

---

## Reading Order

If you are new to the project, read the documents in this order:

1. **[Project Overview](./01_PROJECT_OVERVIEW.md)** — what this game is, what's done, what's next
2. **[Architecture](./02_ARCHITECTURE.md)** — how Game1, ECS, and subsystems fit together
3. **[Developer Guide](./03_DEVELOPER_GUIDE.md)** — build, run, controls, debug toggles
4. **[Game Mechanics](./04_GAME_MECHANICS.md)** — input, animation, gameplay loop
5. **[Physics & Movement](./05_PHYSICS.md)** — flight model, gravity, velocity assignment
6. **[Collision System](./06_COLLISION.md)** — tile vs pixel-mask collision, inset hitbox
7. **[World Building](./07_WORLD_BUILDING.md)** — rooms, doors, transitions, registry pattern
8. **[Enemy AI](./08_ENEMIES.md)** — guard, mask, boar, eye, wraith behaviors
9. **[Items & Combat](./09_ITEMS_AND_COMBAT.md)** — inventory rules, weapon matrix, projectiles
10. **[Rendering & Graphics](./10_RENDERING.md)** — Mode-0 pipeline, render target, sprite component
11. **[Assets & Content Pipeline](./11_ASSETS.md)** — spritesheets, content.mgcb, runtime fallbacks
12. **[Roadmap](./12_ROADMAP.md)** — future phases distilled from `DEVELOPMENT_PHASES.md`

---

## Quick Reference

| Topic | File |
|-------|------|
| Run the game | [03_DEVELOPER_GUIDE.md](./03_DEVELOPER_GUIDE.md#build--run) |
| Add a new room | [07_WORLD_BUILDING.md](./07_WORLD_BUILDING.md#adding-a-new-room) |
| Add a new enemy | [08_ENEMIES.md](./08_ENEMIES.md#adding-a-new-enemy) |
| Add a new item | [09_ITEMS_AND_COMBAT.md](./09_ITEMS_AND_COMBAT.md#adding-a-new-item) |
| Tune flight feel | [05_PHYSICS.md](./05_PHYSICS.md#tuning-knobs) |
| Build collision JSON | [06_COLLISION.md](./06_COLLISION.md#authoring-a-collision-grid) |
| Debug toggles (F1/F2) | [03_DEVELOPER_GUIDE.md](./03_DEVELOPER_GUIDE.md#debug-controls) |

---

## Project at a Glance

| Attribute | Value |
|-----------|-------|
| Language | C# 12 / .NET 8 |
| Framework | MonoGame 3.8.1.303 (DesktopGL) |
| Architecture | Entity-Component-System (ECS) |
| Base resolution | 320 × 144 game area + 320 × 56 info panel |
| Render scale | 3× (window: 960 × 600) |
| Tile size | 8 × 8 pixels |
| Standard room | 40 × 18 tiles |
| Player hitbox | 24 × 24 pixels (with 2-px horizontal inset) |
| Target framerate | 60 FPS |
| Original game | Sorcery+ — Virgin Games, Amstrad CPC 6128, 1985 |

---

## Contributing

This is a personal preservation project. When updating documentation:

- Keep file names prefixed with `NN_` so the reading order is preserved alphabetically.
- Treat `DEVELOPMENT_PHASES.md` (in repo root) as the long-form roadmap; this folder cross-references it but should not duplicate it.
- Code examples in docs should match the actual source — if behavior changes, update the doc in the same commit.
