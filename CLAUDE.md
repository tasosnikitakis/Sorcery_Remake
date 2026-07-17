# CLAUDE.md — Sorcery+ Remake

> Operating manual for AI coding agents working in this repository.
> Read this first, every session. It tells you what the project is, the rules
> you may not break, how to add content, and where we are on the roadmap.

This is a faithful, pixel-perfect remake of **Sorcery+** (Virgin Games, Amstrad
CPC 6128, 1985), built in **C# 12 / .NET 8** on **MonoGame 3.8.1 (DesktopGL)**.
The design goal is *authenticity over modernization* with a *modern, data-driven
codebase*.

---

## 0. How this project is run (orchestration model)

Development is orchestrated: a lead agent decomposes work into small, scoped
tickets and dispatches coding agents to implement them one at a time. As a
coding agent your job is to complete **one ticket** to the definition of done
(§7) without breaking the invariants in §3.

- Do the smallest change that fully satisfies the ticket. Do not refactor
  adjacent code, rename things, or "improve" unrelated files unless the ticket
  says so.
- If the ticket is ambiguous or you discover it conflicts with an invariant,
  stop and surface the conflict rather than guessing.
- Keep `Game1.cs` shrinking, never growing (§3). New behavior goes in a new or
  existing subsystem file.

---

## 1. The canonical documentation

**`doc/` (lowercase, numbered `NN_`) is the single source of truth.** Read it
before touching a subsystem. Start with `doc/README.md`, which indexes:

| Topic | File |
|-------|------|
| What the game is / status | `doc/01_PROJECT_OVERVIEW.md` |
| How the code fits together | `doc/02_ARCHITECTURE.md` |
| Build, run, debug toggles | `doc/03_DEVELOPER_GUIDE.md` |
| Input / animation / loop | `doc/04_GAME_MECHANICS.md` |
| Flight physics + tuning | `doc/05_PHYSICS.md` |
| Collision (tile + pixel mask) | `doc/06_COLLISION.md` |
| Rooms, doors, transitions | `doc/07_WORLD_BUILDING.md` |
| Enemy AI | `doc/08_ENEMIES.md` |
| Items & combat matrix | `doc/09_ITEMS_AND_COMBAT.md` |
| Rendering (Mode-0 pipeline) | `doc/10_RENDERING.md` |
| Assets & content pipeline | `doc/11_ASSETS.md` |
| Roadmap (distilled) | `doc/12_ROADMAP.md` |
| Roadmap (long-form reasoning) | `DEVELOPMENT_PHASES.md` (repo root) |

**Doc hygiene rule:** if you change behavior, update the matching `doc/` file
*in the same commit*. Code examples in `doc/` must match the source.

> ⚠️ **Stale docs to ignore.** The root-level `README.md`, `STATUS.md`, and the
> `PHASE*_COMPLETE.md` / setup guides predate the current state and in places
> claim "Phase 1 Complete." They are historical. Trust `doc/` and this file.
> The root-level Python files (`main.py`, `player.py`, `spritesheet.py`,
> `settings.py`) are the abandoned original prototype — **not** part of the
> live game. Do not read them for behavior and do not extend them.

---

## 2. Design pillars (why decisions get made)

1. **Authenticity over modernization.** Physics constants, sprite frames,
   timings, and rules should match the 1985 original frame-for-frame. Do **not**
   add quality-of-life changes that alter gameplay (auto-aim, health regen,
   checkpoints, difficulty options) unless a ticket explicitly calls for it and
   cites the original.
2. **Data-driven content.** Rooms, items, enemies, and doors are defined in
   registries and JSON — adding content should be *one data entry*, not a code
   change. If adding a room forces you to edit `Game1.cs`, the pipeline is being
   bypassed; flag it.
3. **Thin `Game1`, fat subsystems.** `Game1.cs` is an orchestrator: init,
   update-dispatch, draw-dispatch. Domain logic lives in `Core/`, `Physics/`,
   `Enemies/`, `Rooms/`, etc.
4. **Preservation ethics.** Original game assets are extracted from legally
   owned copies for preservation/education. Never commit copyrighted ROM/disk
   binaries beyond what already exists in `extraction/`, and never add them to
   distributable build output.

---

## 3. Hard invariants — do not break these

These are the contracts the whole game rests on. A change that violates one is a
bug even if it compiles.

- **Rendering contract (Mode-0).** Game logic runs in a **320×144** game area
  plus a **320×56** info panel, rendered to an offscreen `RenderTarget2D` and
  stretched **3×** to a 960×600 window with `SamplerState.PointClamp`
  everywhere. No bilinear filtering, no sub-pixel blur. All gameplay coordinates
  are in *base* pixels; multiply by scale only at draw time. See
  `doc/10_RENDERING.md`.
- **Tile grid.** Tiles are **8×8**; a standard room is **40×18** tiles. Collision
  JSON in `assets/data/collision_<roomId>.json` matches this grid.
- **Player hitbox.** 24×24 px with a 2-px horizontal inset (edge-tolerant so the
  player squeezes through gaps). Do not change without a physics/collision
  ticket.
- **Fixed 60 FPS / fixed timestep.** Physics assumes 60 Hz. Keep time-based
  logic `dt`-scaled; do not hardcode per-frame constants that assume a different
  rate.
- **One-item inventory.** Picking up an item drops the currently carried one in
  its place. This is a core mechanic, not a limitation to "fix."
- **Weapon-enemy matrix.** Wrong weapon on an enemy = no kill and no weapon loss;
  correct weapon = kill and consume weapon. `ShootingStar` is the AOE exception.
  See `ItemSystem.CanKillEnemy` and `doc/09_ITEMS_AND_COMBAT.md`.
- **`Game1.cs` does not grow.** It is ~1,325 lines and must trend *down*. New
  systems = new files.
- **`WorldState` is the persistence boundary.** Anything that must survive a
  room transition (dead enemies, picked items, saved wizards, unlocked doors,
  carried item, and — once they exist — energy/lives/timer) lives in
  `Core/WorldState.cs`. It is the future save-file target; keep it
  serialization-friendly (plain data, no engine handles).

---

## 4. Architecture map (folder ownership)

| Folder | Owns |
|--------|------|
| `Core/` | ECS primitives (`Entity`, `IComponent`), `PlayerController`, `WorldState`, `ItemSystem`, `GameEntities` (runtime instance classes) |
| `Physics/` | `PhysicsComponent` (live: tile + pixel collision), `DirectVelocityComponent` (legacy/unused) |
| `Graphics/` | `SpriteComponent` (animation), `SpriteConfig` (frame coords, speeds, thresholds) |
| `Tiles/` | `TileMapComponent` (8×8 grid + optional pixel mask), `TileConfig` (tile IDs, `IsSolid`/`IsPlatform`) |
| `Doors/` | `DoorComponent` (state, alignment, animation), `DoorConfig` |
| `Enemies/` | One controller per enemy type (Guard, Mask, Boar, Eye, Wraith) — pure AI, no rendering |
| `Rooms/` | `RoomManager` (current room), `RoomManifest` (room catalogue), `RoomLayoutLoader` + `RoomContentLoader` (JSON), `RoomLoader`, `RoomData`, `RoomRegistry` |
| `assets/data/` | Per-room JSON: `collision_*.json`, `layout_*.json`, `content_*.json` |
| `assets/images/` | Spritesheets, room backgrounds, door frames |
| `Content/` | MGCB content pipeline (`Content.mgcb`), fonts |
| `SorceryForge/` | Standalone room/collision editor (separate `.csproj` in the solution) |
| `Game1.cs` | Orchestrator only |

**ECS rules:** one component instance per type per entity; components reach each
other via `Owner.GetComponent<T>()`; rendering is centralized in `Game1.Draw`
(most components no-op `Draw`). Full loop diagrams are in `doc/02_ARCHITECTURE.md`.

---

## 5. Adding content (the data-driven paths)

Follow the doc, don't invent a new mechanism:

- **New room** → `doc/07_WORLD_BUILDING.md`. Add a `RoomManifest` entry, author
  `collision_<id>.json` / `layout_<id>.json` / `content_<id>.json` in
  `assets/data/`, and use **SorceryForge** to draw collision/doors rather than
  counting pixels by hand. Backgrounds load at runtime — no `Content.mgcb` edit
  needed.
- **New enemy** → `doc/08_ENEMIES.md`. Add a controller in `Enemies/`, compose
  the entity (`PhysicsComponent` + `SpriteComponent` + your controller), and add
  its spritesheet + kill-matrix entry.
- **New item** → `doc/09_ITEMS_AND_COMBAT.md`. Extend the `ItemType` enum, call
  `ItemSystem.Register(...)` once in `Game1.LoadContent`, and extend
  `CanKillEnemy` / blocked-door rules only if it is a weapon / key.

If a "new content" task pushes you toward editing `Game1.cs` switch statements,
you are on the wrong path — the registries exist to prevent that.

---

## 6. Build, run, and verification

- **Target:** `net8.0`, MonoGame `3.8.1.303`. Two projects in
  `sorcery+_remake.sln`: the game (`SorceryRemake.csproj`) and the editor
  (`SorceryForge/SorceryForge.csproj`).
- **Local:** `dotnet restore && dotnet build && dotnet run`. Debug toggles:
  **F1** stats overlay, **F2** collision-mask overlay.
- ⚠️ **The cloud/web session container has no `dotnet` SDK installed and no
  display.** You often cannot compile or run the game here. Therefore:
  - Verify by **careful reading**: trace the update/draw loop, check you honored
    the invariants in §3, and re-read the relevant `doc/` file.
  - Keep changes small and self-consistent so a human (or a local-SDK session)
    can build and confirm.
  - **Never claim the game "runs" or "was tested" if you did not actually build
    and run it.** Say exactly what you verified (e.g. "compiles" only if you
    built it; otherwise "reviewed for correctness, not run").
  - If a ticket genuinely needs runtime verification, say so and hand it to a
    session that has the SDK.

---

## 7. Definition of done (per ticket)

A ticket is complete when **all** hold:

1. The requested behavior is implemented and matches the original where
   authenticity applies.
2. No invariant in §3 is violated; `Game1.cs` did not grow (or shrank).
3. New content went through the data-driven path (§5), not a `Game1` edit.
4. The matching `doc/` file is updated in the same commit if behavior changed.
5. Code matches the surrounding style: the existing heavy inline
   `// ===` header-comment convention, `SorceryRemake.*` namespaces, nullable
   reference types, `dt`-scaled time logic.
6. Commit message is clear and scoped (see §8). No stray formatting churn.

---

## 8. Git & workflow conventions

- **Branch:** develop on the feature branch you were assigned; create it from
  the latest default branch if it doesn't exist. Never push to `main` without
  explicit permission.
- **Commits:** small, descriptive, imperative mood. Historically commits are
  phase-tagged (e.g. `Phase 4B: Energy system and player death`). Keep that
  style when the work maps to a roadmap phase.
- **One ticket per branch/PR** unless told otherwise. Do not open a PR unless
  the human asks for one.
- Do not commit build output (`bin/`, `obj/`) or new binary ROM/disk assets.

---

## 9. Where we are & how we move forward

### Current state (ground truth, 2026)

- ✅ **Phase 4A complete** — `Game1.cs` refactored into `WorldState`,
  `ItemSystem`, `GameEntities`, `RoomRegistry`. (Tail remaining: `HudRenderer`
  and layout-registration are still inline in `Game1.cs`.)
- ✅ **Data-driven room pipeline + SorceryForge editor already built** — this is
  *ahead* of where `doc/12_ROADMAP.md` places Phase 5A tooling. Room
  layout/content/collision are JSON-authored via the editor.
- ✅ Working: ECS, flight physics (direct-velocity), tile + pixel-mask
  collision, door transitions, 5 enemy AI types, 5 wired items, weapon matrix,
  wizard rescue, blocked doors, shooting-star projectile, persistent world state.
- ❌ **No energy / health / death / lives** — the player is immortal. The game
  is currently a tech demo, not a game with stakes.
- ❌ No cauldrons, no crumbling-book timer, no title/menu/pause, no audio, no
  save/load.
- 📦 **Content gap:** ~**6 of ~75 rooms** exist (Chateau 0/1/2, Stonehenge,
  Wastelands, Tunnel Mouth). Only **5 of 14+** extracted items are wired.

### Recommended path forward (critical path)

The ordering below reflects *what breaks if you skip it*, and adjusts
`doc/12_ROADMAP.md` for the fact that the room pipeline is already built.

1. **Phase 4B — Energy, Death, Lives (HIGHEST PRIORITY).** This is the change
   that turns a tech demo into a game. 8-cell energy bar, contact damage gated by
   a ~1.5 s invincibility timer, death animation, 3 lives, game-over flow. Store
   `energy`/`lives` in `WorldState`. Everything downstream (cauldrons, timer,
   room balance) depends on this. *(See `DEVELOPMENT_PHASES.md` §4B.)*
2. **Phase 4A tail — extract `HudRenderer` + move layout registration out of
   `Game1.cs`.** Cheap, no gameplay dependency, unblocks a clean HUD. Can run in
   parallel with 4B.
3. **Phase 4C — sprite-based info panel HUD** (needs 4B data: energy, lives,
   wizard count, carried item).
4. **DECISION POINT — Phase 4D flight physics.** Decide *before* mass room
   authoring. Retuning to authentic momentum-flight after 70 rooms are balanced
   means re-balancing 70 rooms. Recommend deciding early: either commit to the
   authentic model now or explicitly defer and accept current feel as canon.
5. **Phase 5A — content scale-up.** The tooling exists; this is now an *authoring
   throughput* effort. Build the Chapter-1 room spine, connect the two isolated
   room chains, target 3–5 rooms per session toward the 47-room Chapter 1.
6. **Phase 5C / 5B — wire the remaining items, then cauldrons** (needs rooms +
   energy).
7. **Phase 6A — crumbling-book timer** (only meaningful once there are enough
   rooms to create tension).
8. **Phase 6B/6C — audio, title/menus/game-over.** Polish; audio sourcing is the
   biggest open risk (extract from disk vs. commission chiptune). Keep the
   `AudioManager` stub as the integration seam.
9. **Phase 7 — save/load.** Last. `WorldState` is already the serialization
   target; defer until its shape is stable.

### Known risks / open questions

- **Flight-physics decision (4D)** — see step 4. Highest-leverage timing call.
- **Audio sourcing (6B)** — original AY-3-8912 audio must be extracted or
  recreated; unresolved.
- **Doc reconciliation** — `doc/12_ROADMAP.md` still frames the room pipeline as
  future work; it should be updated to reflect that SorceryForge + JSON loaders
  exist. Root `README.md`/`STATUS.md` are stale and should eventually be pruned
  or redirected to `doc/`.
</content>
</invoke>
