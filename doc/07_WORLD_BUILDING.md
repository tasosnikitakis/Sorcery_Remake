# 07 — World Building

This document describes how the world is constructed: rooms, doors, transitions, and the registry pattern that turns "adding a new room" from a multi-file edit into a one-line entry.

## Vocabulary

- **Room** — a single 320×144 screen. The original game has ~75 rooms; the remake currently ships with 9 registry rooms plus 2 programmatic test rooms.
- **Registry** — which rooms exist at all: `assets/data/rooms.json`, loaded by `Rooms/RoomManifest.All`. Four facts per room (`id`, `displayName`, `backgroundAsset`, `collisionFile`) and nothing else.
- **Layout** — the static structure of a room: background image, collision grid, doors, player spawn, dimensions. Assembled by `Game1.RegisterRoomsFromManifest` from the registry entry plus that room's `collision_<id>.json` and `layout_<id>.json`. (Test rooms are the exception: `Game1.RegisterTestRooms` builds theirs in code.)
- **Content** — what spawns *in* a room: enemies, items, captive wizards, blocked doors. Defined in `Rooms/RoomRegistry.cs`.
- **Door** — a 24×24 entity that, when aligned with the player, triggers a transition to a target room and target door.
- **Transition** — the frozen-game animation between rooms. Door opens (4 frames × 0.15 s), player teleports, target room loads.

The split between *layout* and *content* is intentional: rooms shipped with no enemies (the chateau set) need only layout; rooms with rich content (room_1, room_2 test rooms) need both. The registry pattern means content is data, not code.

## Currently Shipped Rooms

| Room ID | Type | Connections | Background |
|---------|------|-------------|-----------|
| `room_1` | Test (tile-rendered) | door_right ↔ `room_2` | none, drawn from tiles |
| `room_2` | Test (tile-rendered) | door_left ↔ `room_1` | none, drawn from tiles |
| `stonehenge` | Background image | door_right ↔ `wastelands` | `RoomBG_Stonehenge.png` |
| `wastelands` | Background image | door_left ↔ `stonehenge`, door_right ↔ `tunnelmouth` | `RoomBG_Wastelands.png` |
| `tunnelmouth` | Background image | door_left ↔ `wastelands` | `RoomBG_TunnelMouth.png` |
| `chateau_0` | Background image (start) | door_topright ↔ `chateau_1`, door_2 ↔ `near_chateau` | `RoomBG_Chateau0.png` |
| `chateau_1` | Background image | door_topleft ↔ `chateau_0`, door_topright ↔ `chateau_2` | `RoomBG_Chateau1.png` |
| `chateau_2` | Background image | door_topleft ↔ `chateau_1` | `RoomBG_Chateau2.png` |
| `near_chateau` | Background image | door_1 ↔ `chateau_0` | `RoomBG_NearChateau.png` |
| `inside_chateau` | Background image | none yet | `RoomBG_InsideChateau.png` |
| `outside_chateau` | Background image | none yet | `RoomBG_OutsideChateau.png` |

The chateau chain is the new player's entry sequence (game starts in `chateau_0`). The stonehenge / wastelands / tunnelmouth chain is the older background-room set. The three screenshot-derived rooms came later and have their connections authored entirely in the editor.

`assets/data/rooms.json` is the authority here, in that array order; this table is a convenience copy and will drift.

## The Two-Phase Room System

### Phase 1: Layout — `RoomManager.RegisterRoom`

Layout is registered once at startup by `Game1.RegisterRoomsFromManifest`, which walks the registry and gives every room the same builder lambda — background, collision, doors, all read from that room's data:

```csharp
foreach (var manifest in RoomManifest.All)
{
    var captured = manifest;  // the lambda runs on every room load, not now
    _roomManager.RegisterRoom(captured.RoomId, () =>
    {
        _roomManager.SetBackground(_roomBackgrounds[captured.BackgroundAsset]);

        string colPath = Path.Combine(dataDir, captured.CollisionFile);
        if (File.Exists(colPath))
            _roomManager.SetTileMap(RoomLoader.BuildCollisionTileMap(_tilesetTexture, colPath));

        _roomManager.SetDoors(BuildDoorsForRoom(captured.RoomId, dataDir));
    }, displayName: captured.DisplayName);
}
```

Backgrounds live in `Game1._roomBackgrounds`, a `Dictionary<string, Texture2D>` keyed by `BackgroundAsset` and filled by `LoadRoomBackgrounds()` during `LoadContent` — one `Content.Load` per registry room, eagerly, so an asset named in `rooms.json` that the pipeline never built crashes at startup naming the room and the asset rather than showing a blank screen mid-session. There is no per-room texture field.

The builder lambda is *not* run at registration. It's stored, then run on first `LoadRoom(id)` and again on every subsequent return-to-room. (`Game1.RegisterTestRooms` still writes its two lambdas by hand — test rooms draw from tiles and have no background asset.)

### Phase 2: Content — `RoomRegistry.Initialize`

Content is registered once at startup via `RoomRegistry.Initialize()` (called from `Game1.LoadContent`):

```csharp
var room1 = new RoomContent();
room1.Items.Add(new ItemSpawn("room_1_sword", ItemType.Sword, new Vector2(140f, 72f)));
room1.Items.Add(new ItemSpawn("room_1_ballchain", ItemType.BallAndChain, new Vector2(60f, 112f)));
room1.Items.Add(new ItemSpawn("room_1_star",     ItemType.ShootingStar, new Vector2(120f, 112f)));
room1.Items.Add(new ItemSpawn("room_1_lyre",     ItemType.Lyre,         new Vector2(240f, 112f)));
room1.Wizards.Add(new WizardSpawn("room_1_wizard", new Vector2(160f, 72f)));
_rooms["room_1"] = room1;
```

`SpawnRoomContent(roomId)` reads from this registry on every room load and skips entries already in `WorldState` (picked up, killed, saved, unlocked).

## Adding a New Room

Three routes, in the order you will actually reach for them:

| Route | Start from | Section |
|-------|-----------|---------|
| **Import** | a screenshot of the original game | [Importing a screenshot room](#importing-a-screenshot-room) |
| **New Room** | a 320×144 PNG already in `Content/` | [The short way](#the-short-way-sorceryforges-new-room-menu-item) |
| By hand | nothing | [The long way](#the-long-way-by-hand) |

Import is New Room with one step in front of it: it produces the
`Content/RoomBG_<Name>.png` that New Room would otherwise need you to have, then
runs New Room's own creation code on it.

### Importing a screenshot room

The rooms of Sorcery+ are being rebuilt from screenshots of the original, so
this is the normal way a room is born.

1. Drop the capture into **`assets/import/`** as `<Name>.jpg`, `.jpeg` or
   `.png`. (Its images are gitignored — they are inputs, not repository
   content. See [`assets/import/README.md`](../assets/import/README.md).)
2. In SorceryForge, choose **File → Import Screenshot…** (or press **I** on the
   world map). The picker lists every image in the
   folder with its size and the room it would become.
3. Leave **CPC quantize** on unless the source isn't a capture of the original
   game.
4. Click the file. If the source isn't 320×144 or an exact multiple, a crop
   step opens first — drag the 20:9 selection, wheel to resize, `Enter` to
   confirm, `Esc` to back out.

The editor is then sitting in the new room with the screenshot behind it.

**The name of the file is the name of the room** — the same derivation New
Room uses, so `Chateau3.jpg` → `Content/RoomBG_Chateau3.png` → id `chateau_3` →
display name "Chateau 3". PascalCase reads best. The name may hold only
letters, digits, `_` and `-`; anything else is listed greyed out asking you to
rename the file, because the editor has no text field to offer instead.

**Sizes.** 320×144 is used as-is. An exact multiple (640×288, 960×432, …) is
downscaled by taking every Nth pixel, which for a scaled-up capture of a
320×144 screen returns the original screen bit for bit. Any other size goes
through the crop step, whose output is point-sampled to 320×144 the same way.
Nothing is ever filtered or blended — a filter would invent colours that aren't
in the palette and blur exactly the hard edges the punch-out tool needs.

**The crop step**, for the sizes real emulator captures actually come in.
Picking such a file shows the whole source fitted to the canvas area with a
20:9 selection box over it (marked `[crop]` in the picker, so it is never a
surprise):

| | |
|---|---|
| drag inside the image | move the selection |
| wheel | resize it — aspect stays locked at 20:9, floor 320×144 source pixels, always inside the image |
| `Enter` / **Confirm** | cut the selection to 320×144 and carry on into the import |
| `Esc` / right-click / **Cancel** | back out; nothing has been written at this point, so there is no trace to clean up |

Scaling a crop that isn't a near-integer factor loses whole columns and wobbles
the spacing by a pixel — that is unavoidable and still better than a filter, and
the CPC quantize cleans up what it leaves. Capture at an exact multiple of
320×144 when you can.

#### Crop presets — where the box starts

Every capture from one emulator is framed identically, so the rectangle that was
right for the first one is right for all of them. The crop step remembers it,
keyed by the **source's dimensions** and nothing else (not the filename — sizes
are what actually determine where the playfield sits, and they survive renaming):

| The box opens at | When | Header says |
|---|---|---|
| your last confirmed crop of a source this size | you have cropped one before | `preset from last 384×270 crop` |
| the built-in 384×270 calibration | first 384×270 source, nothing stored | `built-in 384×270 preset (CPC full frame)` |
| the largest 20:9 box that fits, centred | any other size | `no preset — largest 20:9 box that fits` |

A preset is a **starting position, not a decision**: the overlay still opens,
still draws the box, still waits for `Enter`. One glance confirms it, a nudge
fixes it — and the nudged rectangle becomes the new preset, last-used wins.

**The built-in** is `(32, 41, 320, 144)` on a source of exactly 384×270 — the
CPC's 320×200 Mode 0 screen plus its hardware border, which is what this
project's captures are. `x = 32` is also exactly `(384 − 320) / 2`, the CPC's own
horizontal border arithmetic; `y = 41` is measured, because the 144-line room is
a slice of the 200-line screen and there is no arithmetic to check it against.
The result is a 1:1 cut — one room, pixel for pixel, no rescale at all. It is a
default and nothing more: confirm one crop of a 384×270 source and your
rectangle replaces it from then on.

**Where presets live.** `.sorceryforge/settings.json` at the repo root, and it
is **gitignored** — this is personal workspace state (which rectangle *your*
emulator puts the playfield at), not a shared decision about the world the way
`assets/data` is, and it must never be able to gate someone else's clone.
Deleting it costs one re-frame. The file is born empty (nothing is written until
you confirm your first crop), round-trips byte-identically, and preserves
members it doesn't recognise, because it will grow other settings later. See
`SorceryForge/EditorSettings.cs`.

#### Importing a whole folder — `A`

Once a size has a preset, files of that size need no decision at all: the crop
is known, the quantize toggle is already set, and the filename rule does the
rest. When **two or more** files in the picker are in that state, the footer
hint reads `A imports all N ready file(s)` and `A` runs them.

A file is **ready** when the picker has no complaint about it *and* either it is
an exact multiple of 320×144 (no crop step at all) or its source size has a
preset. Everything else is **skipped and named** — never forced through. The
interesting skip is `<W>×<H> has no crop preset yet`, whose fix is in the
message: import one file of that size on its own, which stores its crop, and
the rest become ready.

Import All is a loop over the same functions a single click runs — the same
candidate checks, the same `BuildRoomBackground`, the same `NewRoomFlow.Create`.
It lowers no bar. One file is imported per frame, so the status bar counts up
(`Import All: 12/47 — Chateau12.png (11 in, 1 skipped)`) and `Esc` stops after
the current file; what has been imported stays imported. The finish line reads
`imported N, skipped M: <id> (<reason>); …`, listing the first five skips and
then `and K more` — capped because the status bar is one line, counted because a
silent cap would read as "everything went in".

The new rooms join the [world map](#world-map) on the next `Tab`,
auto-placed like any unwired room. The game needs one content rebuild for all of
them.

**CPC quantize** (default on) snaps every pixel to the nearest of the 27
Amstrad CPC hardware colours — three levels per RGB channel, taken from
`extraction/convert_cpc_graphics.py`, the project's own Mode 0 decoder. JPEG's
compression turns a flat block of one colour into a cloud of near-misses:
invisible on screen, ruinous to edit, because Erase and Punch cut hard
rectangles and a hard cut through noise leaves a ragged seam. Snapping restores
real flats. Turn it off for art that isn't a capture of the original.

> The shipped backgrounds are **not** in that palette: `RoomBG_Chateau*` and the
> three `*Chateau` rooms use levels 0/123/255 (green 0/125/251), and
> `RoomBG_{Stonehenge,TunnelMouth,Wastelands}` use 0/99/206 (green 0/101/207) —
> two different emulator palettes. Quantizing moves the first set by ≤5 per
> channel (invisible) and the second by up to 49 (not invisible). Which levels
> your own captures carry depends on the emulator, so compare a quantized and
> an unquantized import of one real screenshot before doing the other 74.

**What it writes.** `Content/RoomBG_<Name>.png` (atomically: encode to `.tmp`,
then move), and then everything [New Room](#the-short-way-sorceryforges-new-room-menu-item)
writes — the `#begin` block, `collision_<id>.json`, the `rooms.json` row. The
game needs a content rebuild (`dotnet build SorceryRemake.csproj`) before it
can see the background; the editor reads the raw PNG immediately.

**What it does not do.** It never moves, deletes or modifies the source file —
re-import as often as you like. What it *refuses* is overwriting a background
that already exists in `Content/`, since you may have erased or punched pixels
out of it since; to genuinely redo an import, delete that PNG (and its
`rooms.json` row, if the room got registered) first. And if the PNG is written
but registration then fails, the leftover is an unused background — which is
exactly what the New Room picker lists, so you can finish the job from there.

Everything except the decode and the encode — the resampling, the quantizer,
the crop maths, the filename rule, the three creation writes — is exercised
headlessly by [`tools/ImportCheck`](../tools/ImportCheck/README.md). That is
only possible because `SorceryForge/ImageImport.cs` and `NewRoomFlow.cs` hold
no `Texture2D` and no `GraphicsDevice`; keep it that way.

### The short way: SorceryForge's **New Room** menu item

1. Put a 320×144 PNG in `Content/`, named `RoomBG_<Name>.png`.
2. In SorceryForge, choose **File > New Room…** (or press **N** on the world map). The picker lists every `RoomBG_*.png` that no room in `rooms.json` has claimed.
3. Pick it. The room is created and opened.

That writes all three things step 1–2 below describe by hand: the `#begin` block in `Content/Content.mgcb`, an all-empty `collision_<id>.json`, and the appended `rooms.json` entry. It does **not** create `content_<id>.json` or `layout_<id>.json` — those appear the first time you save something real into the room (see [Per-Room JSON Files](#per-room-json-files--when-they-exist)).

**The id and name are derived from the filename.** The display name can be changed afterwards in the inspector's ROOM block (see [Renaming a Room's Display Name](#renaming-a-rooms-display-name)); the id cannot, and the filename is the only chance to choose it:

| PNG | Room ID | Display Name |
|-----|---------|--------------|
| `RoomBG_Chateau3.png` | `chateau_3` | `Chateau 3` |
| `RoomBG_NearChateau.png` | `near_chateau` | `Near Chateau` |
| `RoomBG_Stonehenge.png` | `stonehenge` | `Stonehenge` |

The rule (`SorceryForge/NewRoomFlow.cs`): strip `RoomBG_` and `.png`, split into words at each separator (`_` or `-`, which are consumed), at each internal capital and at a trailing digit run, then join the words with spaces for the display name and with underscores, lowercased, for the id. **Rename the file to change the room's id** — there is no rename-after-the-fact. A derived id that collides with an existing room or with a reserved test-room id (`room_1` / `room_2`) is listed in the picker but greyed out with the reason.

`NewRoomFlow.Create` reads `rooms.json` **from the data directory it is about to write**, not from the cached `RoomManifest.All`, and re-checks the derived id against it. Before PR 7b it appended to the cached snapshot, so two creations without a `RoomManifest.Reload()` between them both started from the same list and the second write silently dropped the first room. The editor never hit it because `CreateAndOpenRoom` reloads after every file — which was a caller having to remember something. `tools/ImportCheck` now pins the fix rather than the hazard.

A name that is *already* snake_case derives itself: `chateau_1` → `chateau_1`, `near-chateau` → `near_chateau`. That idempotence is what makes the collision check trustworthy — the id being tested has to be the id that would be created. Until PR 5b it did not hold: the separator was counted as a boundary *and* kept, so `chateau_1` derived `chateau__1`, which collides with nothing and so was cheerfully created beside the shipped `chateau_1`. `tools/ImportCheck` section 5a now pins both `derive(derive(x)) == derive(x)` and "no derived id contains `__`".

One shipped room diverges from this rule: `tunnelmouth` would derive as `tunnel_mouth`. Its three multi-word siblings (`near_chateau`, `inside_chateau`, `outside_chateau`) *are* snake_case, so it's the outlier. Nothing re-derives ids for existing rooms, so the divergence is inert — and room ids are persistence keys, so don't "fix" it.

The game needs a **content rebuild** (`dotnet build SorceryRemake.csproj`) before it can load the new background; the editor reads the raw PNG and shows it immediately.

If the picker is empty, every background is already claimed — the path for a room that has no PNG yet is [Import](#importing-a-screenshot-room), which writes one.

### The long way: by hand

Still supported, and what the button automates. Steps 1, 2 and 3 are the ones New Room performs.

### 1. Author the background image

A 320×144 PNG. Drop into `Content/` and add to `Content/Content.mgcb`:

```
#begin RoomBG_Forest1.png
/importer:TextureImporter
/processor:TextureProcessor
/processorParam:ColorKeyEnabled=False
/build:RoomBG_Forest1.png
#end
```

(The simplest pattern is to copy an existing block from the .mgcb file.)

### 2. Register the room in `assets/data/rooms.json`

The room *registry* is data, not code. Append one entry to the `rooms` array:

```json
{ "id": "forest_1", "displayName": "Forest 1", "backgroundAsset": "RoomBG_Forest1", "collisionFile": "collision_forest_1.json" }
```

| Field | Meaning |
|-------|---------|
| `id` | Room ID. A persistence key — `WorldState` remembers entity IDs built from it, so never rename casually. |
| `displayName` | Shown in the editor's room picker and the game's HUD. Falls back to `id` if omitted. |
| `backgroundAsset` | Content pipeline asset name from step 1. Required. |
| `collisionFile` | File name inside `assets/data`. May be `""` until the geometry is painted. |

**Array order is room order** — the editor's Prev/Next buttons cycle rooms in exactly this sequence, and every "for each room" loop walks it. Appending is always safe; reordering re-orders the editor.

`Rooms/RoomManifest.cs` loads and validates this file once per process (it is shared source, so the game, SorceryForge and `tools/RoundTrip` all read the same file). Validation is deliberately fatal: a missing file, malformed JSON, a duplicate `id`, an `id` reserved for a test room, or an entry with no `backgroundAsset` throws at startup with the path and problem named, rather than booting into a silently empty world. The file may carry `//` comments — the loader reads with `JsonCommentHandling.Skip`.

Test rooms `room_1` / `room_2` are deliberately absent from the registry; they are built programmatically in `Game1.RegisterTestRooms` and listed in `RoomManifest.TestRoomIds` so validators can distinguish them from a typo'd room ID. **Those two ids are reserved**: `RegisterTestRooms` runs first, so a registry entry reusing one would lose the `RoomManager` slot and its background, collision and doors would silently never load. `RoomManifest.LoadAll` rejects it rather than shipping a room that looks registered and isn't.

#### rooms.json is also machine-written

`RoomManifest.Save` writes it — that is how New Room appends an entry. Two properties are load-bearing and guarded by `tools/RoundTrip`'s self-test:

- **The header comment survives a write.** `JsonSerializer.Serialize` would drop it (comments aren't part of the object model), taking the array-order rule with it. The writer re-emits it textually from the `RoomsJsonHeader` constant in `RoomManifest.cs` — so **edit the header there, not in the file**, or the next New Room overwrites your edit.
- **Load → save with no changes is byte-identical.** Entries stay one-per-line and column-aligned, so a New Room diff is one added line. (Adding a room with an id longer than every existing one re-pads that column across all rows — a one-time whole-file diff, stable afterwards.)

The registry is cached in a `Lazy<T>`. `RoomManifest.Reload()` drops that cache and is **editor-only**: the game registers its rooms and loads its background textures once at startup, so a mid-session reload would produce entries the `RoomManager` and texture dictionary know nothing about. SorceryForge calls `Reload()` then `RoomMeta.RebuildAll()`, in that order, after writing the file.

That is the whole code-side registration. `Game1.RegisterRoomsFromManifest` iterates the registry at startup and builds each room's loader lambda — background, collision tilemap, and doors — from the entry plus the room's own JSON files. There is no per-room C# to write.

### 3. Author the collision grid

Paint it in SorceryForge, or hand-author `assets/data/collision_forest_1.json` against the [JSON schema](./06_COLLISION.md#authoring-a-collision-grid). A room with no collision file is a valid (if floorless) state — the game skips the tilemap step.

### 4. Author doors

Place them in SorceryForge and save; that writes `assets/data/layout_forest_1.json`:

```json
{
  "roomId": "forest_1",
  "doors": [
    { "id": "forest1_door_right", "x": 296, "y": 112, "type": "LeftOpening",
      "targetRoom": "forest_2", "targetDoor": "forest2_door_left" }
  ],
  "playerSpawn": { "x": 160, "y": 80 }
}
```

`BuildDoorsForRoom` turns each door entry into a `DoorComponent` at room-load time. `playerSpawn` is optional — see [Player Entry Position](#player-entry-position).

#### Wiring a door in the inspector

Select the door on the canvas and use the inspector's fields:

| Field | What it does |
|---|---|
| `Opens` | flips `LeftOpening` / `RightOpening` — one click, because there are only two values |
| `Room` | **click to open a filterable list** of every registry room. Type to narrow, `Enter` takes the top hit, `Esc` closes. |
| `Door` | the same list, of the doors belonging to whichever room `Room` names — plus this room's own **unsaved** doors when it targets itself, so a two-door corridor can be wired before either end is written |
| `Needs` | (blocked doors) the same list, of item types |

Filtering is a **substring** match, case-insensitive: typing `topright` finds `chateau1_door_topright`, which a prefix match would not — and the second half of the id is the half anyone remembers. `(none)` is a real entry in the `Room` and `Door` lists and stores the empty string; a door with no target is what an unfinished room looks like.

**Choosing a room blanks the door.** A door id is only meaningful inside one room, so carrying the old one across would leave a link that validates as `orphan-door` and reads like a typo. Both writes are a single undo entry, so `Ctrl+Z` restores the pair.

**Test rooms are not offered.** `room_1` and `room_2` are dev scaffolding registered in `Game1.RegisterTestRooms`. The door validator tolerates a hand-edited target pointing at one (verdict `ok-test`), but the editor will not author one.

### 5. Author content

Place items / enemies / wizards / blocked doors in SorceryForge and save; that writes `assets/data/content_forest_1.json`, which `RoomRegistry.GetContent` prefers over any hardcoded entry. (`RoomRegistry.Initialize`'s C# entries remain only as the fallback for the test rooms, which have no JSON.)

### 6. Connect the matching door on the partner room

If `forest_1`'s right door targets `forest_2`, then `forest_2` must have a door with id `forest2_door_left` whose `TargetRoomId="forest_1"` and `TargetDoorId="forest1_door_right"`.

There is no automatic two-way door wiring; mismatched IDs silently fail (the transition lands in the target room but at fallback position `(160, 60)`).

## World Map

**Tab** in SorceryForge flips between the room editor and the world map: every
registry room as a box carrying its own background, door links as arrows
between them. At nine rooms Prev/Next cycling is fine; at the target
seventy-five it is not, and the map is what replaces it.

Tab is the primary way in and out; **View → World Map** does the same thing and
displays the shortcut, and the keybind is also shown at the right-hand end of
the status bar in both modes.

| In map mode | |
|---|---|
| **Tab** or **Esc** | back to the room editor. Esc here does *not* quit — the exit path lives in room view, where the unsaved work it protects is. |
| **wheel** | zoom, anchored at the cursor. Five levels, `6%` to `100%`; each is a power of two so a thumbnail is always an exact integer downscale of its background. |
| **middle-drag**, or **left-drag on empty space** | pan |
| **arrow keys** | pan by half a room |
| **click a room** | open it in the room editor |
| **drag a room** | move it; its position persists (see below) |
| **Ctrl+S** | save the arrangement to `assets/data/worldmap.json` |
| **N** | open the [New Room](#the-short-way-sorceryforges-new-room-menu-item) picker (map mode only; room mode uses **File > New Room…**) |
| **I** | open the [Import](#importing-a-screenshot-room) picker (map mode only; room mode uses **File > Import Screenshot…**) |

**N** and **I** open the same overlays the File menu does, with the same
discard guards — those concern the *current room's* unsaved edits, which exist
just as much while the map is up, because creating a room loads it. Creating a
room lands you **in it, in the room editor**: the point of making a room is to
author it. It joins the board on the next Tab, auto-placed like any unwired
room, and adding it moves nothing that was already there. Cancelling either
picker returns you to the map exactly as you left it.

Map mode suspends room editing completely: no palette, no inspector, no canvas,
no paint or punch, and every menu item that acts on a room is disabled because
none of them means anything against a board. Four stay live — the ones whose
keyboard path already worked from the board: **New Room**, **Import
Screenshot**, **World Map** itself, and **Save Map Arrangement**, which is what
Ctrl+S writes here. The room you were editing stays loaded behind it,
untouched — which is why *entering* the map needs no discard guard and why
*clicking a room* gets the same one Prev/Next uses. Unsaved edits warn on the
first click and go through on the second; they are never discarded silently.

### How the board is arranged

Rooms nobody has moved are placed automatically: **column = distance in door
links from the first registry room** (`chateau_0`), row = order reached within
that column. Rooms no chain of doors reaches get a trailing column of their
own.

Two rules make it deterministic, and they are rules rather than accidents — a
layout that reshuffles between sessions is *worse* than no layout, because it
teaches you something false:

- rooms are seeded and enqueued in **registry order**, never dictionary order;
- **adjacency is undirected** even though doors are not. A one-way drop still
  makes two rooms neighbours, and a room whose only link points *into* the
  start room is plainly next to it. The map says where things are; the arrows
  carry the direction.

A whole disconnected *component* currently lands in the trailing column as a
flat stack rather than getting its own layout — today that is the
stonehenge/wastelands/tunnelmouth chain plus the two unwired chateau rooms.
Dragging is the answer: a room moved by hand stays where you put it.

### `assets/data/worldmap.json`

Drag a room and it stops being auto-placed. **Ctrl+S in map mode** writes the
arrangement:

```json
{
  "rooms": {
    "chateau_0":    { "x": 0,   "y": 0   },
    "near_chateau": { "x": 448, "y": 216 }
  }
}
```

Editor-only — nothing in the game reads it, and it is the map's own file rather
than a change to any existing schema. Positions are in **map units, which are
room pixels**: a box is 320×144, so "one room-width apart" is a number you can
read.

**Only rooms you actually dragged are in it.** Auto-placed positions are never
written, because writing them would freeze today's BFS output into the
repository — add a door next week and every room would stay where the old
layout put it, with no way to tell which positions were decisions and which
were defaults. What is in the file is exactly the set of deliberate acts:

- a room **in** the file uses its stored position;
- a room **absent** from it is auto-placed, every time;
- **delete the file** and the whole board goes back to auto-placement.

Same born-empty discipline as `content_*.json` / `layout_*.json`, and the same
asymmetry — which is easy to get backwards. Nothing dragged and no file yet
writes **nothing**, so an untouched map never adds a file to the repo. Nothing
dragged but the file **exists** writes it anyway, because that is a user who
dragged every room back to auto-placement and their reset has to persist.

A position for a room that no longer exists (renamed, removed) is ignored on
load and gone on the next save. Preserving unknown keys sounds tidier and is
worse: it silts up a file whose whole content is meant to be attributable.

The writer follows house style — one room per line, keys column-aligned, in
registry order, and load → save with no change is byte-identical, so a moved
room is a one-line diff. An unreadable file costs you the arrangement and
nothing else: it is reported in the status bar and the board falls back to
auto-placement, unlike `rooms.json`, where a parse failure is deliberately
fatal.

`tools/RoundTrip` never sees this file — it seeds and sweeps `content_*` and
`layout_*` only — and both `tools/MapCheck` and an end-to-end RoundTrip run
with a `worldmap.json` present confirm it.

**Unsaved arrangements** show as `map*` in the status bar (in *both* modes) and
as a `*` on the map's title. Quitting is the only thing that discards one, so
quitting is the only action that warns about it: room switches, room creation
and imports all leave the arrangement alone, and a guard that fired on those
would be a warning nobody believes. There is no autosave sidecar for it — a
surprise file appearing in `assets/data` would be worse than losing a drag.

### Arrows

Built from the same door data and the same verdicts as the **Doors** button —
`SorceryForge/DoorValidator.cs`, which both call — so an arrow can never say a
link is fine while the button says it is broken. Validation runs on entering
the map, and it includes the current room's *unsaved* doors, so a link you just
authored is on the board before you save it.

| Colour | Verdict | |
|---|---|---|
| green | `ok` | wired both ways — drawn as **one** line with an arrowhead at each end |
| dim green | `ok-test` | targets `room_1` / `room_2`, which are registered in code and are not on the board |
| yellow | `asymmetric` | the partner door exists but points elsewhere: one-way |
| red | `orphan-door` | the target room exists, that door does not |
| red | `orphan-room` | no such room |

Each arrow leaves its room at the **door's own position on the door's own
edge**, so a room with a top-left and a top-right door shows two arrows leaving
from where those doors actually are. A link with no box at the far end — a test
room, a room that does not exist, or a door pointing back into its own room —
is drawn as a short spur pointing out of the source door, which is the true
statement: *this door leads somewhere not on this board*.

Thumbnails are the raw `Content/RoomBG_*.png` files, loaded lazily as boxes come
into view and cached for the session. A room with no readable PNG gets a plain
slate. Saving background pixel edits drops that room's cached thumbnail, so the
board shows the erased version next time.

### Checking the board without a screen

`tools/MapCheck` computes the whole board headlessly and asserts it: the layout
is deterministic, every door becomes exactly one arrow, and every arrow lands
on the rooms and doors it claims to. `--board` prints the computed layout and
arrow list, which is how you tell a wrong *picture* from wrong *data*. See
[`tools/MapCheck/README.md`](../tools/MapCheck/README.md).

## Door System

### Door Types

```csharp
public enum DoorType
{
    LeftOpening,   // Visually opens to the LEFT — player approaches from the LEFT
    RightOpening,  // Visually opens to the RIGHT — player approaches from the RIGHT
}
```

Naming convention: a `LeftOpening` door is on the **right edge** of a room, because the player approaches from inside the room (i.e., from the left). A `RightOpening` door is on the **left edge**.

### Door Position

Door position is the top-left corner of the 24×24 sprite. Common positions:

- Right-edge door: `Vector2(296, 112)` — 320 - 24 = 296 (flush against right wall), Y=112 places it on the floor (top at 112, bottom at 136 = 18 - 1 tile up from screen bottom).
- Left-edge door: `Vector2(0, 112)` — flush against left wall.
- Chateau top doors: Y=0 — flush against the top edge (player flies up into them).

### Alignment Check (`DoorComponent.IsPlayerAligned`)

A door is "alignable" only when:

- `State == Closed` (transitions in progress lock out other doors)
- Player Y is within 2 px of door Y (both are 24 tall, so they need to be on the same horizontal row)
- The relevant edge (player.right for LeftOpening, player.left for RightOpening) is within 3 px of the door's matching edge

The 2-px Y tolerance and 3-px X tolerance are forgiving enough that "fly into the door at any angle" works, but tight enough that you don't accidentally trigger doors while flying past nearby.

### Animation

`DoorConfig`:

- `FRAME_COUNT = 4`
- `FRAME_DURATION = 0.15` seconds
- Total animation: 0.6 seconds

The door spritesheet is 192×48 (4 frames of 48×48), rendered at 24×24 game-space size. Both `LeftDoorFrames.png` and `RightDoorFrames.png` are 4-frame strips.

### Transition Sequence

When a door's animation completes (`Update` returns `true`):

1. `RoomManager.State = TransitionReady`
2. Next `Game1.UpdateDoorTransition` call sees `IsGameFrozen` still true but `Update` returns `(targetRoomId, targetDoorId)`.
3. `Game1` invokes `SaveRoomEnemies(currentRoomId)` then `_roomManager.ExecuteTransition(playerWidth)`.
4. `ExecuteTransition` calls `LoadRoom(targetRoomId)` (re-runs the builder lambda), then iterates the new room's doors looking for one whose `DoorId == targetDoorId`. Returns `door.GetArrivalPosition(playerWidth)`.
5. `GetArrivalPosition` for a `LeftOpening` door returns `Position - (playerWidth + 5, 0)` — i.e., 5 px outside the door so the player doesn't immediately re-trigger.

If the target door isn't found, the player lands at fallback `(160, 60)`.

## Player Entry Position

A room may carry its own player spawn, as an optional `playerSpawn` object in `layout_<id>.json`:

```json
"playerSpawn": { "x": 160, "y": 80 }
```

It is the **top-left of the 24×24 player**, in room space — same convention as door and entity positions. There is at most one per room.

**Where it applies.** Only to *starting* in a room: `Game1.LoadContent`'s initial spawn and `Game1.RestartGame`, both of which start in `chateau_0`. Walking through a door does **not** consult it — a door transition positions the player at the target door via `DoorComponent.GetArrivalPosition` (see [Transition Sequence](#transition-sequence)).

**When it's absent** — which is the case for every room shipped today — the game uses `RoomLayoutLoader.DefaultPlayerSpawn`, i.e. `(160, 80)`. That constant is the single definition of the fallback; SorceryForge's reachability validator reads the same field, so the editor can no longer validate from a position the game doesn't use. Resolve a room's spawn with `RoomLayoutLoader.GetPlayerSpawn(roomId)` rather than retyping the numbers.

**Authoring it.** In SorceryForge, drag **Player Spawn** from the palette's **META** section onto the canvas. It renders as a magenta 24×24 outline with a cross — a colour no other overlay uses. Dropping it again *moves* the existing spawn (there is never a second one); drag it like a placement to fine-position; select it and press `Delete` to clear it. Clearing sets the field back to *absent*, not to `(160, 80)`: the next save drops the `playerSpawn` key entirely and the room falls back to the default.

The marker is deliberately **not** a `Placement`. It has no entity ID, never appears in `content_<id>.json`, and takes no part in the puzzle or reachability entity sets. It is held on `EditorState.PlayerSpawn` and persisted through the layout write, covered by the same `PlacementsDirty` flag as placements — so a room switch or exit with an unsaved spawn hits the discard guard.

Because a placement wins a canvas click over the spawn marker, a spawn sitting underneath an entity is not draggable; drop the palette entry again to relocate it.

## Per-Room State Persistence

`WorldState` keeps four sets, all keyed by entity ID:

- `DeadEnemies` — once dead, stays dead
- `PickedUpItems` — once picked up, the original spawn never reappears (but dropped items use new IDs and *do* persist in their drop room)
- `SavedWizards` — once rescued, never respawns
- `UnlockedDoors` — blocked doors that have been opened stay open

Plus `SavedRoomEnemies` — a snapshot map of `roomId → List<EnemyInstance>` used so enemies retain their position when you leave a room and come back. This is the only state that is *room-scoped* rather than entity-scoped.

The persistence model is what makes rooms feel like a coherent world rather than independent levels: dropping a Sword in room 5, going to room 6, and coming back finds the Sword exactly where you left it.

## Undo and Redo in SorceryForge

`Ctrl+Z` takes back the last edit of **any** kind; `Ctrl+Y` (and `Ctrl+Shift+Z`) puts it back. Both are also in **Edit > Undo / Redo**, greyed when their stack is empty. The stack holds 64 entries and evicts the oldest.

One user action costs one `Ctrl+Z`. Every editor action is a command object (`SorceryForge/EditorCommands.cs`) that knows how to do itself and how to take itself back:

| Command | Recorded when |
|---|---|
| `AddPlacementCommand` | a palette entry is dropped on the canvas |
| `DeletePlacementCommand` | `Delete` with a placement selected |
| `MovePlacementCommand` | a placement drag is **released** — one command per drag, not per frame |
| `SetPlacementFieldCommand` | one applied inspector change (all of a placement's editable fields, together) |
| `SetPlayerSpawnCommand` | the spawn is set, moved or cleared |
| `PaintTilesCommand` | a paint drag **ends** — every cell it changed, in one command |
| `BackgroundEditCommand` | an erase/restore stroke ends, or a punch happens |
| `CompositeCommand` | a drop or move that also auto-punched: both halves, one entry |

The status line names what happened — `Undid: move chateau_0_sword_2 (3 more, Ctrl+Y redoes)`.

**Undo history is per room, and switching rooms clears it — both halves.** This is a correctness decision, not a simplification. `LoadRoom` rebuilds `Placements` from disk: every `Placement` object in the working set is replaced by a new instance, and the commands hold *references* to those objects. A command surviving the switch would, on `Ctrl+Z`, write to an object that is no longer in any room's list — no crash, no visible effect, and the edit the author thought they took back still there. Keying commands by entity ID instead would not help; it would only change the failure to "the ID now names a different object with different fields."

**Undo and redo always mark the room dirty**, in both directions. Undoing back to exactly the last-saved state still shows `room*`. That is deliberate and conservative: the cost is one redundant save, and the opposite error loses work silently — which is the whole reason the discard guard exists.

**The world-map arrangement is out of scope.** Dragging a room on the board sets `map*` and is written by its own `Ctrl+S` in map mode; it is not per-room working state, and it survives every room switch — the event that clears this stack. Folding it in would mean a stack some clears applied to and some did not. Undo on the board is disabled for the same reason `F11` is: `Ctrl+Z` is read in `HandleKeyboardShortcuts`, which map mode never reaches.

Every command is driven headlessly by `tools/EditCheck`, through the property that defines it: `Do(); Undo();` leaves the state it found, and `Do(); Undo(); Do();` leaves the state `Do()` alone would have.

## RoomData (DTO, future use)

`Rooms/RoomData.cs` defines a fuller DTO (`Width`, `Height`, `Tiles`, `PlayerSpawn`, `Exits`, `BackgroundColor`, `BackgroundTextureName`, `CollisionGrid`) that is *not currently used* by the live system. (Its `PlayerSpawn` is unrelated to the live one: that ships as `playerSpawn` in `layout_<id>.json` — see [Player Entry Position](#player-entry-position).) It exists as the target shape for a future serializable-rooms / map-editor pipeline (Phase 5A). Today the live shape is the builder lambda + `RoomContent`; `RoomData` is a sketch.

## Per-Room JSON Files — When They Exist

A room's `content_<roomId>.json` and `layout_<roomId>.json` are **optional**. An absent file and a file with all-empty arrays mean exactly the same thing to the loaders (`RoomContentLoader.TryLoad` / `RoomLayoutLoader.TryLoad` both return "nothing here"), so the save path prefers absence:

- **Empty room, no file yet → nothing is written.** Opening a content-free or doorless room in SorceryForge and pressing Ctrl+S must not add a no-op file to the repo. The status line says so rather than claiming a save.
- **Empty room, file already exists → the file IS rewritten.** This is the case where the author just deleted everything in the room. Skipping the write would silently discard the deletion and leave the old entities/doors live in the game. Emptiness alone never suppresses a write; only emptiness *plus* absence does.

"Empty" is defined per file at the check site in each loader's `Save`: content = no items, enemies, wizards or blocked doors; layout = no doors **and no `playerSpawn`**. `roomId` doesn't count — the writer always fills it in. Any future DTO field that carries authored room data has to be folded into those predicates, or a room whose only content is that field never gets a file. `playerSpawn` is the worked example: a doorless room with just a spawn *is* non-empty and does get a file, and `tools/RoundTrip`'s self-test pins that case.

**What `Ctrl+S` reports.** The status line names exactly the parts that reached the disk, and never a part that did not:

```
Saved chateau_0: content + layout + PNG — rebuild (dotnet build) for the game to see the PNG.
```

`content` and `layout` appear only when the loader actually wrote (see the two rules above); `collision` only when Paint mode changed a tile; `PNG` only when Erase or a punch touched background pixels. The rebuild note rides on `PNG` alone, because the background is the only part of a save the *game* cannot see until the content pipeline runs again — this is what the PR 4b smoke pass caught, where a save quietly rewrote an asset in `Content/` and said nothing about it.

Together with a stable serializer this gives the repo a checkable invariant: **load every manifest room in the editor, save each untouched, and both `git diff` and `git status` stay clean.** `tools/RoundTrip` runs exactly that headlessly and is the regression test for any change to the save path, the loaders, or room registration.

## Layout Conventions

These are project conventions, not framework requirements:

- Standard room: 320×144 (40 tiles wide, 18 tiles tall).
- Floor at Y=136 (tile row 17) — bottom 1 tile is solid.
- Side doors at Y=112 (tile row 14) — top of a 24-px-tall door sits on the floor.
- Top doors at Y=0 (tile row 0) — for vertical chateau-style flight.
- Door X=0 (left edge) or X=296 (right edge, 320-24).
- Room IDs use snake_case with the area prefix (`forest_1`, `chateau_0`, `stonehenge`).
- Door IDs follow `<roomId>_door_<location>` (e.g., `chateau_1_door_topright`).
- Item / enemy / wizard IDs follow `<roomid>_<type>_<n>` so they stay unique without a counter.

## Renaming a Room's Display Name

The inspector's **ROOM** block (top of the right panel, always there) has a text field for the room's `displayName`. Type, press `Enter` or click away, and it writes through `RoomManifest.Save` to `assets/data/rooms.json` — header comment preserved, array order preserved, one field changed. `Esc` reverts. An empty name is refused: `RoomManifest.LoadAll` substitutes the room id for a blank `displayName`, so writing one would look like it had worked and quietly rename the room to its own id.

Renaming writes the file immediately, so it is **not on the undo stack** — the same boundary `Ctrl+S` sits on. It does **not** reload the room, so unsaved placement edits survive it.

**The room ID is not editable, and this is not an oversight.** An id is:

- a **persistence key** — `WorldState` remembers `PickedUpItems` / `DeadEnemies` / `SavedWizards` / `UnlockedDoors` as sets of *entity* ids, and every entity id is built from its room's id. A rename orphans every one of them, silently, because a set lookup that misses just means "not picked up yet";
- **three file names** — `content_<id>.json`, `layout_<id>.json`, `collision_<id>.json`;
- a **cross-room link** — every door in every *other* room that targets this one names it in `targetRoom`, and the validator's `orphan-room` verdict is the only thing that would notice;
- a **map key** — `worldmap.json` stores board positions by room id.

Doing it properly means a migration: rewrite three files, rewrite every referring door, rewrite the map, and decide what happens to an existing save. That is a tool, not a text field, and a text field that did a third of it would be worse than none. To change an id today, see [Removing or Renaming a Room](#removing-or-renaming-a-room) below and do it as a one-shot operation by hand.

## Removing or Renaming a Room

Don't, except as a one-shot operation:

- All doors in *other* rooms targeting the removed room must be updated.
- All `WorldState` sets reference IDs that now point at nothing — entries become benign noise.
- A renamed room with the same content is fine (update both the layout registration and the content key).
