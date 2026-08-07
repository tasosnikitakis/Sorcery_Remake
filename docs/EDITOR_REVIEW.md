# SorceryForge Editor — Review & Improvement Plan

Reviewed: `SorceryForge/` (EditorGame.cs 2,192 lines, EditorState, EditorLayout,
RoomMeta, PuzzleAnalyzer, Placement, PaletteEntry) plus the shared `Rooms/`
pipeline it depends on. Date: 2026-08-07.

## What's already good (don't break these)

- **Model/view split.** `EditorState` holds all mutable state; `EditorGame` is
  frame plumbing. Keep new features on this pattern.
- **Shared source via `<Compile Include>` globs.** The editor compiles the
  game's own `Rooms/`, `Core/`, `Tiles/` files — data types can't drift.
- **Three validators** (reachability flood-fill, door-link symmetry, cross-room
  puzzle solver) with distinct on-canvas colour languages.
- **Erase-mode robustness**: per-stroke undo with no-op snapshot dropping,
  view-jump Bresenham guard, premultiplied-alpha normalisation, atomic
  tmp+move PNG save, `.autosave.png` on exit, double-confirm discard guard.
- **Integer power-of-two zoom** anchored at the cursor; scissored render passes.

---

## P0 — Bugs (fix before anything else)

### 1. Placement edits are silently discarded on room switch / exit
`ConfirmDiscardUnsavedEdits()` checks only `BackgroundDirty` and
`CollisionDirty`. Adding, moving, deleting, or retargeting a **placement**
(items, enemies, wizards, doors) sets no dirty flag, so PageUp/PageDown,
Prev/Next, or Escape throws that work away with no warning. This is the
most likely way to lose real authoring work today.

**Fix:** add `PlacementsDirty` to `EditorState`; set it in `DropDraggingAt`,
the move-drag branch of `HandleCanvasInput`, `Delete` handling, and every
inspector cycle-button lambda. Include it in `ConfirmDiscardUnsavedEdits`
and clear it in `LoadRoom`/`SaveCurrentRoom`. Show a `*` in the top-bar room
title when any dirty flag is set.

### 2. `NextIdCounter` can generate duplicate IDs
`LoadRoom` sets `NextIdCounter = Placements.Count + 1`. Delete one placement,
add a new one, and the counter can re-issue an existing suffix (e.g. a second
`chateau_0_sword_3`). IDs key `WorldState` persistence (`PickedUpItems`,
`DeadEnemies`, …), so duplicates corrupt save-state semantics.

**Fix:** in `GenerateId`, loop the counter until the candidate ID isn't in
`Placements`. Cheap and bulletproof. Additionally add a duplicate-ID check to
the Validate pass (duplicates can also arrive via hand-edited JSON).

### 3. PuzzleAnalyzer analyses stale data
`PuzzleAnalyzer.Analyze()` reads `content_*.json` from disk, so unsaved edits
in the current room are invisible to it. `ValidateDoors` already overlays the
current room's in-memory placements — the puzzle button doesn't, which makes
the two buttons behave inconsistently and will confuse anyone iterating.

**Fix:** pass the current room's in-memory `RoomContent`
(`_state.ToRoomContent()`) into `Analyze()` and use it in place of the loaded
JSON for that one room. Alternative (worse UX): auto-save before analysing.

### 4. Doors targeting `room_1` / `room_2` false-flag as orphan-room
Test rooms aren't in `RoomManifest.All`, so `RoomMeta.Find` fails and any door
targeting them reads as broken. Either add them to the manifest (flagged as
test rooms) or treat a known-test-room target as "ok (test)".

---

## P1 — Scale blockers (required before Phase 5A's 75-room push)

### 5. Data-driven palette (and the full item set)
`BuildPalette` hardcodes 5 items + 5 enemies; `ItemType` has only 5 values,
yet `Content/` already holds extracted sheets for Key, Wand, Chalice, Bottle,
Bag, Coat, Cup, Moon, Parchment, Book, etc. When Phase 5C lands, every new
item currently needs edits in `LoadAndCache`, `BuildPalette`, and (implicitly)
`FindPaletteFor`.

**Fix:** one static table — `(ItemType, assetName, sourceRect, section)` —
living in the **shared** source (e.g. next to `ItemSystem`) so game and editor
both consume it. `BuildPalette` and `Game1`'s item registration iterate the
same table. `LoadAndCache` iterates the table's distinct assets.

### 6. Kill the label-string matching for doors
`DropDraggingAt` recovers the opening side via
`entry.Label.Contains("LeftOpening")`, and `FindPaletteFor` matches door
placements to palette entries the same way. Renaming a label breaks placement.
Add a `DoorOpeningSide` field to `PaletteEntry` and match on it.

### 7. Rooms as data: `rooms.json` manifest + "New Room" in-editor
`RoomManifest.All` is a compiled C# list, so creating a room means editing
code, adding a `.mgcb` block, and adding a `Game1` texture field — exactly the
bottleneck `doc/07` and the Phase 5A roadmap call out. The editor should be
where rooms are born.

**Fix, staged:**
1. Move the manifest to `assets/data/rooms.json` (id, displayName,
   backgroundAsset, collisionFile). `RoomManifest.All` loads it; both apps
   pick it up unchanged elsewhere.
2. Editor "New Room" button: prompts for an id, copies/points at a background
   PNG, writes an empty `collision_<id>.json` (all-empty grid) and
   `layout_<id>.json`, appends to `rooms.json`, and loads it.
3. In the game, replace the per-room `_bgChateau1`-style cached fields with a
   `Dictionary<string, Texture2D>` keyed by `BackgroundAsset` (the roadmap's
   Phase 5A note already wants this).

### 8. Per-room player spawn marker
The spawn `(160, 80)` is hardcoded in `Game1` **and** duplicated inside
`ValidateReachability`. `RoomData.PlayerSpawn` already sketches the right
shape. Add `playerSpawn` to `layout_<id>.json`, render a draggable spawn
marker in the editor (a distinct palette entry or a fixed overlay), and make
the validator flood-fill from it. Game falls back to `(160, 80)` when absent.

### 9. Palette scrolling
With the full item set the palette (44 px per entry + headers) will overflow
`PaletteRect` and entries become unclickable. Reuse the inspector's
scroll/viewport-culling pattern (`HandleInspectorScroll` +
clip-and-skip-click-zones) — it's already proven. Alternative: a 2-column
icon grid halves the height.

### 10. Better pickers than cycle-buttons
Cycling `TargetRoomId` one click at a time is fine for 9 rooms and unusable
for 75. Minimum viable fix: clicking the value box opens a scrollable list
overlay (same widget style as the palette) instead of cycling. Same for
`TargetDoorId` and blocked-door `RequiredItem`. This is the single biggest
inspector usability item.

---

## P1.5 — Owner-requested features (added 2026-08-07)

These come from the project owner and slot between the scale blockers and the
productivity items. Items A–C form the "screenshot-to-room" pipeline; item D
is the world map.

### A. Screenshot import (JPEG → room background)
The owner has JPEG screenshots of the original game's rooms with items,
doors, and enemies in their correct positions. These must become room
backgrounds.

**Do NOT add runtime JPEG loading/saving.** JPEG has no alpha channel, and
both Erase mode and the punch-out feature (item C) write transparency back
via `SaveAsPng` — a JPEG round-trip would silently break them. Instead build
an **Import Screenshot** flow (top-bar button or part of New Room):

1. Accept `.jpg` / `.jpeg` / `.png` via a file path (or a drop folder such as
   `assets/import/` scanned on demand — MonoGame has no native file dialog;
   a scanned drop-folder with an on-screen picker list is the pragmatic
   choice, matching the editor's hand-rolled UI).
2. If dimensions ≠ 320×144, offer nearest-neighbour resize (and/or a simple
   crop-rect step). This replaces the manual
   `tools/crop_room_backgrounds.py` workflow.
3. **Optional but recommended:** quantize to the 27-colour Amstrad CPC
   hardware palette. Original-game screenshots contain only these colours;
   quantizing removes JPEG DCT noise entirely, which makes erase/punch edges
   pixel-clean. The palette is already known to the extraction tooling
   (`extraction/convert_cpc_graphics.py`).
4. Write `Content/RoomBG_<Name>.png`, add the `.mgcb` block (string-append —
   the file format is line-based), and register the room (via `rooms.json`
   once item 7 lands; until then, print the manual steps to the status bar).

After import the room is a normal PNG-backed room: Erase mode, punch-out,
and atomic saves all work unchanged.

### B. Pixel deletion on screenshots — ALREADY EXISTS, verified
Erase mode covers this: square brush (`[`/`]` size, Shift for ×4), Bresenham
stroke stamping, right-drag restores from last-saved state, one undo snapshot
per stroke (Ctrl+Z, 40 deep), save is atomic tmp+move, exit writes
`.autosave.png`. No work needed beyond making imported JPEGs PNG-backed
(item A). Note: erasing sets pixels to transparent (0,0,0,0), which renders
as black in-game — functionally identical to "revert to black".

### C. Punch-out: clear background under a placed item
When a game item (especially a door) is placed over a screenshot that still
shows the original's baked-in artwork at that spot, the background pixels
under the item's 24×24 footprint must be cleared to transparent so they never
bleed through the item's animation frames.

**Design decision (agreed with owner intent, refined for safety):** an
automatic punch on the drop click would leave a mispositioned hole if the
user then nudges the item to fine-align it — and for doors, fine alignment
is the whole point. Implement both:

- **Explicit punch (primary):** with a placement selected, `P` key and an
  inspector button "Punch background" clear the 24×24 rect under
  `SelectedPlacement.Bounds` to transparent. This is the
  align-first-then-cut workflow.
- **Auto-punch toggle (secondary):** a top-bar toggle `Punch: ON/OFF`
  (default OFF). When ON, `DropDraggingAt` punches at the drop position,
  and a move-release re-punches at the new position (the old hole stays —
  acceptable, since the background there was due for clearing anyway; if
  not, right-drag restore fixes it).

Implementation notes:
- Reuse the stroke-undo machinery: push a `_bgUndo` snapshot before each
  punch (a punch is a one-shot "stroke"), set `BackgroundDirty`, call
  `_currentBackground.SetData`. Ctrl+Z and Save then work for free.
- Guard: punch is a no-op with a status-bar explanation when
  `_bgPixels == null` (room has no editable PNG — same guard Erase uses).
- Punch the full 24×24 rect (not sprite-shaped) — matches the door use-case
  and is what the owner asked for.

### D. World map view (promoted from P3 item 16)
A separate editor mode/tab showing every room as a scaled-down thumbnail of
its background, with arrows connecting linked doors — a mind-map of the game
world. Requirements from the owner:

- Rooms are **draggable**, and their positions **persist** — store in
  `assets/data/worldmap.json` (`{ "rooms": { "<roomId>": {"x":…, "y":…} } }`).
  Rooms without a stored position get auto-placed (simple grid or BFS layers
  from `chateau_0`).
- The board is **scrollable and zoomable** — reuse the `EditorLayout`
  view-transform pattern (wheel zoom anchored at cursor, middle-drag pan),
  but as an instance ("MapView") rather than the static room-canvas state.
- **Arrows between doors:** build edges from `RoomMeta.All[*].Doors`. Anchor
  each arrow at the door's side of the room box (left edge / right edge /
  top, derived from door position). Colour edges by door-validation status
  (green ok / yellow asymmetric / red orphan) — the validator output already
  exists.
- **Click a room → open it in the room editor** (switch mode + `LoadRoom`).
  Use a click-vs-drag threshold (~4 px) so dragging doesn't open rooms.
- **Add rooms from the map** — this invokes the New Room / Import Screenshot
  flow (items 7 and A), so those must land first.
- Entry: a top-bar button or Tab key toggles Room ↔ Map mode. Detailed UI/UX
  polish is explicitly deferred; ship functional first.

Thumbnails: draw the already-loaded background textures scaled (PointClamp
keeps them readable); rooms whose texture isn't loaded yet can lazy-load via
the same `FromStream` path `LoadRoom` uses, cached in a map-view dictionary.

---

## P2 — Productivity (high value, do after P1)

### 11. Unified undo stack
Undo currently covers only background strokes. Introduce a small command
pattern in `EditorState` — `IEditorCommand { Do(); Undo(); }` with
`AddPlacement`, `DeletePlacement`, `MovePlacement` (record start/end position
at drag end), `PaintTiles` (record changed cells per drag), and wrap the
existing bg-stroke snapshots as a command. One Ctrl+Z path for everything.
This also gives you Ctrl+Y for free.

### 12. "Play from here"
Add a Play button that saves, then launches the game with
`--room <id> --pos <x,y>` (small `Program.cs`/`Game1` change to parse args
and override the start room/spawn). This collapses the edit → rebuild →
navigate-to-room loop into one click and will pay for itself within a day of
room authoring. Bonus: in DEBUG builds, have the game load room background
PNGs via `FromStream` like the editor does, so background edits don't need a
content rebuild.

### 13. Duplicate, nudge, and multi-place
- `Ctrl+D`: duplicate the selected placement with a fresh ID, offset 8 px.
- Arrow keys nudge the selected placement by 1 px (Shift = 8) **when a
  placement is selected**, panning otherwise — resolves the current
  arrows-always-pan conflict.
- Shift-click drop: keep `Dragging` active after a drop for stamping several
  of the same entity.

### 14. Validation as ambient state, not buttons
Door validation is cheap — run it automatically after every door edit and on
save, and show a persistent issue count in the status bar (e.g.
`Doors: 2 broken | Puzzle: stale`). Keep the buttons for the expensive
reachability/puzzle passes, but stale results should be visibly labelled
stale (the flags exist; surface them).

### 15. One-way doors shouldn't be errors forever
`asymmetric` is treated as a defect, but the original Sorcery+ has
intentional one-way drops. Add an `"oneWay": true` field to `DoorEntryJson`
that downgrades asymmetric to informational for that door.

---

## P3 — Bigger swings (worth planning, not urgent)

### 16. World map view
`PuzzleAnalyzer` already computes the door graph. A map mode (Tab key):
rooms as boxes laid out by force-direction or a stored grid position, edges
coloured by door status, unreachable rooms greyed, click-to-open. At 75
rooms this becomes the primary navigation surface; Prev/Next cycling stops
scaling around 15.

### 17. Consider ImGui.NET for the chrome
The hand-rolled UI is well done, but every new widget (text input for
renaming IDs, dropdowns, checkboxes) costs real code. `MonoGame.ImGuiNet`
would give text fields, combos, and docking for free while keeping the
canvas rendering exactly as-is. Decision point: adopt it before building the
list-picker overlays in item 10, or commit to hand-rolled and accept the
cost. Either is defensible; mixing both later is the worst outcome.

### 18. Split EditorGame.cs
At 2,192 lines it's approaching the Game1 problem the main project just
refactored away. Natural seams, as partial classes or separate types:
`EditorGame.Input.cs`, `EditorGame.Draw.cs`, `Inspector.cs`,
`BackgroundEraser.cs` (stroke/undo/stamp logic is fully self-contained),
`Validators.cs`. Do this before item 11 lands more code in the file.

---

## Suggested execution order for agents

1. **PR 1 (bugs):** P0 items 1–4. Small, independent, testable.
2. **PR 2 (punch-out):** item C. Small, self-contained (reuses stroke-undo),
   immediately useful for cleaning the rooms that already exist.
3. **PR 3 (palette):** items 5, 6, 9 together — shared item table, door
   fields, palette scroll.
4. **PR 4 (rooms-as-data):** items 7, 8 — rooms.json, New Room, spawn marker,
   background dictionary in Game1.
5. **PR 5 (screenshot import):** item A — builds on PR 4's rooms.json so an
   imported screenshot registers itself end-to-end. Include the CPC-palette
   quantize option.
6. **PR 6 (world map):** item D — needs PR 4 (add-room flow) and benefits
   from PR 5 (importing screenshots directly from the map).
7. **PR 7 (pickers + undo):** items 10, 11 — decide on item 17 (ImGui) first.
8. **PR 8 (playtest loop):** items 12, 13, 14.

Dependency chain for the new features: PR 4 → PR 5 → PR 6. PR 2 (punch-out)
has no dependencies and can land any time after PR 1.

Each PR should run both `dotnet build` targets (game + editor) and a manual
smoke pass: load every room, place/move/delete each kind, save, reload,
confirm JSON diffs are minimal (stable ordering already holds — keep it).
