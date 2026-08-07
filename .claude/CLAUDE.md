# CLAUDE.md — Sorcery+ Remake

Faithful remake of the Amstrad CPC classic **Sorcery+** (1985) in C# /
MonoGame 3.8 / .NET 8, plus **SorceryForge**, a room editor that shares the
game's source. Dev environment: Windows / PowerShell.

## Build & run

```powershell
dotnet build SorceryRemake.csproj                 # the game
dotnet build SorceryForge/SorceryForge.csproj     # the editor
dotnet run --project SorceryRemake.csproj
dotnet run --project SorceryForge/SorceryForge.csproj
```

Both projects MUST build clean before any commit. Background PNG changes
need a rebuild before the game sees them (content pipeline → XNB); the
editor reads raw PNGs directly and sees changes immediately.

## Architecture — read this before touching anything shared

**SorceryForge compiles the game's source files directly** via
`<Compile Include>` globs in `SorceryForge/SorceryForge.csproj`
(`Core/`, `Graphics/`, `Tiles/`, `Rooms/`, `Doors/`, `Physics/` — excluding
`Core/PlayerController.cs` and `Enemies/`). Any edit to those folders
affects BOTH binaries. Keep shared-file changes additive and
backwards-compatible; put editor-only logic in `SorceryForge/`.

The game itself is a small ECS: entities hold components
(`PhysicsComponent`, `SpriteComponent`, controllers). `Game1.cs` (~1,300
lines, mid-refactor per Phase 4A) still owns room layout registration and
HUD drawing.

## Per-room data model

Each room = up to three JSON files in `assets/data/` + a background PNG in
`Content/`:

| File | Contents | Written by |
|------|----------|-----------|
| `content_<roomId>.json` | items, enemies, wizards, blocked doors | SorceryForge (`RoomContentLoader`) |
| `layout_<roomId>.json` | doors: position, opening side, target room + door | SorceryForge (`RoomLayoutLoader`) |
| `collision_<roomId>.json` | solid-tile grid | SorceryForge paint mode / `tools/generate_collision_grid.py` |

Which rooms exist is defined in `Rooms/RoomManifest.All` (compiled C#; both
apps iterate it). Test rooms `room_1` / `room_2` are NOT in the manifest —
they're registered programmatically in `Game1.RegisterTestRooms`.

The editor flattens content + doors into one `List<Placement>`
(`SorceryForge/EditorState.cs`); `ToRoomContent()` / `ToRoomLayoutJson()`
split them back on save. Saves go to the repo source tree (path resolution:
`SorceryForge/EditorPaths.cs` walks up to find `SorceryRemake.csproj`).

## Critical invariants

- **Entity IDs are persistence keys.** `WorldState` tracks
  `PickedUpItems` / `DeadEnemies` / `SavedWizards` / `UnlockedDoors` as
  sets of entity IDs. IDs follow `<roomid>_<type>_<n>` and must stay unique
  and stable. Never rename or reformat IDs casually.
- **Door wiring is manual and two-way.** A door names a `targetRoom` +
  `targetDoor`; the partner door must point back. Broken links don't crash —
  the player silently lands at fallback `(160, 60)`.
- **Door texture swap quirk:** PNG names describe hinge side, not opening
  side. `DoorType.LeftOpening` renders `RightDoorFrames.png` and vice versa
  (see `RoomManager.LoadRoom` and the mirror comment in
  `EditorGame.BuildPalette`). Do not "fix" this.
- **Black = transparent for sprites.** Sprite sheets get
  black-keyed on load (`MakeColorTransparent`). Room backgrounds are
  opaque; erased background pixels are (0,0,0,0), which renders black
  in-game.
- **Rooms are 320×144** game pixels, 8-px tiles (40×18), 24×24 entities.
  Player spawn is currently hardcoded `(160, 80)` in `Game1` (and mirrored
  in the editor's reachability validator).

## Code style

- Heavy explanatory comments — banner headers per file/section, "why" notes
  on non-obvious decisions. Match this; don't strip comments.
- Hand-rolled loops over LINQ in per-frame paths.
- Nullable reference types enabled; keep annotations honest.
- JSON DTOs use lowercase property names matching the on-disk schema
  (`roomId`, `doors`, `targetRoom`) — don't rename.

## Documentation index (`doc/`)

Numbered docs 01–12 are current and authoritative; the older `docs/` folder
(Phase 1 logs) is historical. Most useful:

- `doc/07_WORLD_BUILDING.md` — rooms, doors, IDs, state persistence.
  **Required reading for any room/editor work.**
- `doc/06_COLLISION.md` — collision grid schema + authoring.
- `doc/02_ARCHITECTURE.md` — ECS + subsystem layout.
`doc/12_ROADMAP.md` — phase plan; `docs/EDITOR_REVIEW.md` — current
editor work plan and PR sequence (the one current file in the otherwise
historical docs/ folder).

## Current focus

Editor (SorceryForge) hardening and features, per `docs/EDITOR_REVIEW.md`:
P0 bug fixes → punch-out tool → data-driven palette → rooms-as-data →
screenshot (JPEG) import → world map view. Game-side, the next phase is 4B
(energy/death) per `doc/12_ROADMAP.md`.

## Working agreements for agents

- One concern per commit; both builds green per commit.
- After editor changes: manual smoke pass — cycle all rooms, place / move /
  delete each placement kind, save, reload, check `assets/data/` diffs are
  minimal and ordering-stable.
- Never edit generated/`bin`/`obj` content; editor saves must land in the
  source tree via `EditorPaths`.
- When a task touches JSON schemas, update the schema comment blocks in the
  matching loader (`RoomLayoutLoader.cs` / `RoomContentLoader.cs`) and
  `doc/07` in the same PR.
- Any PR touching the save path, loaders, or room registration must run
  tools/RoundTrip against main before hand-off and report the result.
- Work on a branch named pr<N>-<slug>; the owner merges to main after
  the manual smoke pass. Restore tracked test artifacts AND check git
  status for new untracked files under assets/ and Content/ before merge.