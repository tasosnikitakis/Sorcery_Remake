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
| **New Room** | a 320×144 PNG already in `Content/` | [The short way](#the-short-way-sorceryforges-new-room-button) |
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
2. In SorceryForge, click **Import**. The picker lists every image in the
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

The selection opens at the largest size that fits, centred, which is one nudge
away from right for the usual case of one room plus a border. Scaling a crop
that isn't a near-integer factor loses whole columns and wobbles the spacing by
a pixel — that is unavoidable and still better than a filter, and the CPC
quantize cleans up what it leaves. Capture at an exact multiple of 320×144 when
you can.

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
then move), and then everything [New Room](#the-short-way-sorceryforges-new-room-button)
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

### The short way: SorceryForge's **New Room** button

1. Put a 320×144 PNG in `Content/`, named `RoomBG_<Name>.png`.
2. In SorceryForge, click **New Room**. The picker lists every `RoomBG_*.png` that no room in `rooms.json` has claimed.
3. Pick it. The room is created and opened.

That writes all three things step 1–2 below describe by hand: the `#begin` block in `Content/Content.mgcb`, an all-empty `collision_<id>.json`, and the appended `rooms.json` entry. It does **not** create `content_<id>.json` or `layout_<id>.json` — those appear the first time you save something real into the room (see [Per-Room JSON Files](#per-room-json-files--when-they-exist)).

**The id and name come from the filename** — the editor has no text field, and the flow is designed so it never needs one:

| PNG | Room ID | Display Name |
|-----|---------|--------------|
| `RoomBG_Chateau3.png` | `chateau_3` | `Chateau 3` |
| `RoomBG_NearChateau.png` | `near_chateau` | `Near Chateau` |
| `RoomBG_Stonehenge.png` | `stonehenge` | `Stonehenge` |

The rule (`SorceryForge/NewRoomFlow.cs`): strip `RoomBG_` and `.png`, split at each internal capital and at a trailing digit run, then join the words with spaces for the display name and with underscores, lowercased, for the id. **Rename the file to change the room's id** — there is no rename-after-the-fact. A derived id that collides with an existing room or with a reserved test-room id (`room_1` / `room_2`) is listed in the picker but greyed out with the reason.

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

### 5. Author content

Place items / enemies / wizards / blocked doors in SorceryForge and save; that writes `assets/data/content_forest_1.json`, which `RoomRegistry.GetContent` prefers over any hardcoded entry. (`RoomRegistry.Initialize`'s C# entries remain only as the fallback for the test rooms, which have no JSON.)

### 6. Connect the matching door on the partner room

If `forest_1`'s right door targets `forest_2`, then `forest_2` must have a door with id `forest2_door_left` whose `TargetRoomId="forest_1"` and `TargetDoorId="forest1_door_right"`.

There is no automatic two-way door wiring; mismatched IDs silently fail (the transition lands in the target room but at fallback position `(160, 60)`).

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

## RoomData (DTO, future use)

`Rooms/RoomData.cs` defines a fuller DTO (`Width`, `Height`, `Tiles`, `PlayerSpawn`, `Exits`, `BackgroundColor`, `BackgroundTextureName`, `CollisionGrid`) that is *not currently used* by the live system. (Its `PlayerSpawn` is unrelated to the live one: that ships as `playerSpawn` in `layout_<id>.json` — see [Player Entry Position](#player-entry-position).) It exists as the target shape for a future serializable-rooms / map-editor pipeline (Phase 5A). Today the live shape is the builder lambda + `RoomContent`; `RoomData` is a sketch.

## Per-Room JSON Files — When They Exist

A room's `content_<roomId>.json` and `layout_<roomId>.json` are **optional**. An absent file and a file with all-empty arrays mean exactly the same thing to the loaders (`RoomContentLoader.TryLoad` / `RoomLayoutLoader.TryLoad` both return "nothing here"), so the save path prefers absence:

- **Empty room, no file yet → nothing is written.** Opening a content-free or doorless room in SorceryForge and pressing Ctrl+S must not add a no-op file to the repo. The status line says so rather than claiming a save.
- **Empty room, file already exists → the file IS rewritten.** This is the case where the author just deleted everything in the room. Skipping the write would silently discard the deletion and leave the old entities/doors live in the game. Emptiness alone never suppresses a write; only emptiness *plus* absence does.

"Empty" is defined per file at the check site in each loader's `Save`: content = no items, enemies, wizards or blocked doors; layout = no doors **and no `playerSpawn`**. `roomId` doesn't count — the writer always fills it in. Any future DTO field that carries authored room data has to be folded into those predicates, or a room whose only content is that field never gets a file. `playerSpawn` is the worked example: a doorless room with just a spawn *is* non-empty and does get a file, and `tools/RoundTrip`'s self-test pins that case.

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

## Removing or Renaming a Room

Don't, except as a one-shot operation:

- All doors in *other* rooms targeting the removed room must be updated.
- All `WorldState` sets reference IDs that now point at nothing — entries become benign noise.
- A renamed room with the same content is fine (update both the layout registration and the content key).
