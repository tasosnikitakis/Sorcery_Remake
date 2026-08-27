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

## Dependency policy

**The game has exactly two dependencies — MonoGame.Framework.DesktopGL and
MonoGame.Content.Builder.Task — and gains no more.** It ships a 1985 Amstrad
CPC game in 320×144; anything a third party would supply, it already has or
does not need. Check `SorceryRemake.csproj` before adding to it, and check the
built `deps.json` afterwards to be sure nothing arrived transitively.

**The editor has one further dependency: `ImGui.NET`, pinned at `1.91.6.1`**,
for chrome only. This is a deliberate, sanctioned exception, and the reasoning
is worth keeping because it is the test any future exception has to pass:

- The cost it removes is real and recurring. Every new widget — a text field
  for renaming an ID, a dropdown, a checkbox — was previously ~40 lines of
  hand-rolled layout, hit-testing and scroll handling, maintained in three
  places that had to agree. `docs/EDITOR_REVIEW.md` item 17 called the decision
  point exactly: adopt before building the list pickers, or commit to
  hand-rolled — *"mixing both later is the worst outcome"*.
- It is scoped to a tool, not to the shipped game. A bad day with ImGui costs
  the editor, never the player.
- It is pinned exactly. ImGui's C# surface is generated from cimgui and moves
  with it; a floating version is a build that breaks on its own.
- It is testable headlessly. Dear ImGui is pure CPU, which is what lets
  `tools/ChromeCheck` drive the real chrome with no desktop session.

MonoGame-flavoured ImGui packages were surveyed and all rejected — one ships
its assembly at the package root so referencing it compiles against nothing,
the rest pin ImGui.NET versions from 2020–2022 or depend on the wrong MonoGame
flavour. `SorceryForge/UI/ImGuiRenderer.cs`'s header names each and why.

## UI architecture — ImGui chrome, SpriteBatch canvas

The editor draws two kinds of thing and they do not mix:

| | Drawn by | Lives in |
|---|---|---|
| **Chrome** — menus, panels, modal overlays, status | Dear ImGui | `SorceryForge/UI/` |
| **Canvas** — the room, the map board, the crop image | SpriteBatch | `EditorGame.cs` |

The split is by coordinate space, not by taste. The canvas is a pixel-space
tool: integer power-of-two zoom, scissored passes, point sampling, a brush that
must land on the pixel under the cursor. Chrome is a widget space. **Do not add
a hand-rolled widget, and do not draw canvas content through ImGui.**

- `UI/ImGuiRenderer.cs` — the binding: font atlas, vertex path, input pump.
  **The only file under `UI/` that may touch `GraphicsDevice` or `Texture2D`.**
  Everything else is device-free so `tools/ChromeCheck` can compile and drive
  it; a panel that needs a texture takes ImGui's `IntPtr` handle instead.
- `UI/IChromeActions.cs` — the complete list of what the chrome may do. Panels
  cannot see `EditorGame`; they see this interface of verbs and a read-only
  `ChromeView` snapshot. **Logic goes on the logic side**: if a control needs a
  new effect, add a verb here and implement it in `EditorGame`, never inline in
  a panel.
- `UI/ChromeInputRouter.cs` — ImGui gets first refusal on the mouse
  (`WantCaptureMouse`), *except* that a gesture already running on the canvas,
  the board or the crop image keeps the mouse until it ends. Modal overlays
  read their cancel gestures raw and ungated.
- The chrome is built in **Update**, not Draw, so every state mutation stays in
  one place; `Draw` paints the recorded draw data after every SpriteBatch pass.

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

`SorceryForge/EditorGame.cs` (~3,900 lines, down from 4,709 when the chrome
moved to `UI/`) owns room load/save, canvas and map input, the pixel tools
(paint, erase, punch), the three validators and the import flow. It does not
draw a single widget.

## Per-room data model

Each room = up to three JSON files in `assets/data/` + a background PNG in
`Content/`:

| File | Contents | Written by |
|------|----------|-----------|
| `content_<roomId>.json` | items, enemies, wizards, blocked doors | SorceryForge (`RoomContentLoader`) |
| `layout_<roomId>.json` | doors: position, opening side, target room + door | SorceryForge (`RoomLayoutLoader`) |
| `collision_<roomId>.json` | solid-tile grid | SorceryForge paint mode / `tools/generate_collision_grid.py` |

Which rooms exist is defined in `assets/data/rooms.json` — the room
*registry* (`id`, `displayName`, `backgroundAsset`, `collisionFile`), loaded
and validated by `Rooms/RoomManifest.All` (shared source; both apps iterate
it). Array order is room order, including the editor's Prev/Next cycle.
A missing or malformed `rooms.json` is a fatal startup error in both apps by
design. Test rooms `room_1` / `room_2` are NOT in the registry — they're
registered programmatically in `Game1.RegisterTestRooms` and listed in
`RoomManifest.TestRoomIds`.

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
- Five headless harnesses guard the editor; run all five before hand-off and
  report the counts, which must not fall:
  `tools/RoundTrip` (save path), `tools/ImportCheck` (screenshot import),
  `tools/MapCheck` (world map), `tools/ChromeCheck` (chrome + input routing),
  `tools/EditCheck` (undo/redo commands + registry edits).
  None needs a desktop session — that is a property of the code they test, and
  a PR that breaks it has taken something away.
- Work on a branch named pr<N>-<slug>; the owner merges to main after
  the manual smoke pass. Restore tracked test artifacts AND check git
  status for new untracked files under assets/ and Content/ before merge.