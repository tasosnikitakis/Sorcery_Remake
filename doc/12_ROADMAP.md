# 12 — Roadmap

This is a distilled view of the planned development phases. The authoritative long-form is [`DEVELOPMENT_PHASES.md`](../DEVELOPMENT_PHASES.md) at the repository root — read that for the full reasoning behind each phase. This file is a quick-reference index that survives roadmap rewrites.

## Phases at a Glance

| Phase | Name | Status | Blocks |
|-------|------|--------|--------|
| **4A** | Refactor Game1.cs into subsystems | ✅ Largely done | Everything else |
| **4B** | Energy system + player death + lives | ⏳ Next | Room balance, fail state |
| **4C** | Sprite-based HUD (info panel) | ⏳ Pending 4B | Visual polish |
| **4D** | Authentic momentum-flight physics | ⏳ Optional | Room design feel |
| **5A** | Room authoring pipeline + batch rooms | ⏳ Pending 4 | Content scale |
| **5B** | Cauldron healing/poison system | ⏳ Pending 5A | Difficulty curve |
| **5C** | Full item set integration | ⏳ Pending 4A,5A | Puzzle rooms |
| **6A** | Crumbling-book global timer | ⏳ Pending 5A | Win/loss tension |
| **6B** | Audio (music + SFX) | ⏳ Pending | Polish |
| **6C** | Title screen, menus, game over | ⏳ Pending | Ship condition |
| **7** | Save/load system | ⏳ Last | Convenience |

## Where We Are Today

Phase 4A is mostly complete:

- ✅ `WorldState.cs` — extracted, holds all persistent state
- ✅ `ItemSystem.cs` — extracted, owns item textures + kill matrix
- ✅ `GameEntities.cs` — extracted, holds runtime instance types (`EnemyInstance`, `ItemInstance`, etc.)
- ✅ `RoomRegistry.cs` — extracted, data-driven content per room
- ✅ The `DrawFilledRectangle` per-frame allocation leak is fixed (uses `_pixelTexture`)
- ⏳ `HudRenderer.cs` — not yet extracted; info-panel + debug overlay are still inline in `Game1.cs`
- ⏳ Layout registration (`RegisterChateauRooms` etc.) is still in `Game1.cs`; should ideally move into `RoomRegistry` or a new `RoomLayouts.cs`

`Game1.cs` is approximately 1,300 lines (down from ~1,732 pre-refactor). Target post-refactor: 400–500 lines.

## Immediate Next Step (per DEVELOPMENT_PHASES.md)

**Phase 4B — Energy & Death.** The game today has no fail state. Touching enemies is cosmetic; the player is immortal. Adding the energy/health system unlocks meaningful enemy encounters and is a prerequisite for cauldrons (Phase 5B) and the timer (Phase 6A).

Mechanics summary:

- 8-cell energy bar
- Contact with any enemy = -1 energy, gated by 1.5 s invincibility timer
- Energy = 0 → death animation → respawn at room entry (3 lives) or game over
- 3 lives, depleted lives = game over
- Visual flash during invincibility window
- HUD shows energy (8 cells), lives indicator

`WorldState` already exists as the data home. `HudRenderer` (extracted in 4C) becomes the display home. Timeline estimate from the source roadmap: 2–3 days.

## Phase Dependencies

```
4A ─┬─→ 4B ─→ 4C ─→ 4D
    │   │
    │   └─→ 5B (needs energy)
    │   └─→ 6A (needs death flow)
    │
    └─→ 5A ─→ 5B (needs rooms)
        │
        └─→ 5C (needs rooms)
        │
        └─→ 6A (needs enough rooms for timer to matter)

7 (save/load) ─ depends on stable WorldState shape, gated until everything else freezes
```

## What Could Slip

- **Audio (Phase 6B).** Sourcing authentic AY-3-8912 chiptune is uncertain. Two paths: extract from original disk image (specialist task), or commission new compositions. The `AudioManager.cs` stub mentioned in the source roadmap is a no-op placeholder so audio integration won't require a cross-cutting refactor when it's ready.
- **Phase 4D flight physics.** Optional re-tune to true momentum-flight. Breaks all current room balance; only worth doing once or never.
- **Pixel-perfect collision swap.** The pixel-mask path exists in `PhysicsComponent` but isn't live. Promoting it from "F2 overlay only" to "live collision" is one method-call swap, but tile collision is faster and easier to author. The pixel path stays a fallback option.

## Cross-References

- Architecture detail: [02_ARCHITECTURE.md](./02_ARCHITECTURE.md)
- Why physics is direct-velocity (and what 4D would change): [05_PHYSICS.md](./05_PHYSICS.md#future-authentic-flight-physics-phase-4d)
- Room authoring bottleneck (5A motivation): [07_WORLD_BUILDING.md](./07_WORLD_BUILDING.md#adding-a-new-room) and [06_COLLISION.md](./06_COLLISION.md#authoring-a-collision-grid)
- Item set status (5C): [11_ASSETS.md](./11_ASSETS.md#asset-inventory) shows which items are extracted-but-not-wired
- Enemy gaps (4B contact damage): [08_ENEMIES.md](./08_ENEMIES.md#whats-missing-vs-original)
