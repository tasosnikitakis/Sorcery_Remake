# 07 — World Building

This document describes how the world is constructed: rooms, doors, transitions, and the registry pattern that turns "adding a new room" from a multi-file edit into a one-line entry.

## Vocabulary

- **Room** — a single 320×144 screen. The original game has ~75 rooms; the remake currently ships with 8.
- **Layout** — the static structure of a room: background image, collision grid, doors, dimensions. Defined in `Game1.RegisterChateauRooms`/`RegisterBackgroundRooms`/`RegisterTestRooms`.
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
| `chateau_0` | Background image (start) | door_topright ↔ `chateau_1` | `RoomBG_Chateau0.png` |
| `chateau_1` | Background image | door_topleft ↔ `chateau_0`, door_topright ↔ `chateau_2` | `RoomBG_Chateau1.png` |
| `chateau_2` | Background image | door_topleft ↔ `chateau_1` | `RoomBG_Chateau2.png` |

The chateau chain is the new player's entry sequence (game starts in `chateau_0`). The stonehenge / wastelands / tunnelmouth chain is the older background-room set.

## The Two-Phase Room System

### Phase 1: Layout — `RoomManager.RegisterRoom`

Layout is registered once at startup via `Game1.RegisterChateauRooms` (and similar). Each room is a builder lambda:

```csharp
_roomManager.RegisterRoom("chateau_1", () =>
{
    _roomManager.SetBackground(_bgChateau1);
    string jsonPath = Path.Combine(dataDir, "collision_chateau1.json");
    _roomManager.SetTileMap(RoomLoader.BuildCollisionTileMap(_tilesetTexture, jsonPath));

    var doorLeft = new DoorComponent(DoorType.RightOpening, new Vector2(0, 0));
    doorLeft.DoorId = "chateau1_door_topleft";
    doorLeft.TargetRoomId = "chateau_0";
    doorLeft.TargetDoorId = "chateau0_door_topright";

    var doorRight = new DoorComponent(DoorType.LeftOpening, new Vector2(296, 0));
    doorRight.DoorId = "chateau1_door_topright";
    doorRight.TargetRoomId = "chateau_2";
    doorRight.TargetDoorId = "chateau2_door_topleft";

    _roomManager.SetDoors(new List<DoorComponent> { doorLeft, doorRight });
}, displayName: "Chateau 1");
```

The builder lambda is *not* run at registration. It's stored, then run on first `LoadRoom(id)` and again on every subsequent return-to-room.

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

The full workflow for a new background-image room:

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

### 2. Author the collision JSON

Create `assets/data/collision_forest_1.json` matching the [JSON schema](./06_COLLISION.md#authoring-a-collision-grid).

### 3. Add a load + cached field in `Game1`

Add `private Texture2D _bgForest1;` to the room-backgrounds field block, and load it in `LoadContent`:

```csharp
_bgForest1 = Content.Load<Texture2D>("RoomBG_Forest1");
```

(Phase 5A roadmap removes this step by caching backgrounds dynamically. Today it's still per-asset.)

### 4. Register layout

In a `Register*Rooms()` method (or a new one for a new room set):

```csharp
_roomManager.RegisterRoom("forest_1", () =>
{
    _roomManager.SetBackground(_bgForest1);
    string jsonPath = Path.Combine(dataDir, "collision_forest_1.json");
    _roomManager.SetTileMap(RoomLoader.BuildCollisionTileMap(_tilesetTexture, jsonPath));

    var doorRight = new DoorComponent(DoorType.LeftOpening, new Vector2(296, 112));
    doorRight.DoorId = "forest1_door_right";
    doorRight.TargetRoomId = "forest_2";
    doorRight.TargetDoorId = "forest2_door_left";
    _roomManager.SetDoors(new List<DoorComponent> { doorRight });
}, displayName: "Forest 1");
```

### 5. Register content

In `RoomRegistry.Initialize`:

```csharp
var forest1 = new RoomContent();
forest1.Enemies.Add(new EnemySpawn("forest1_guard_1", EnemyType.Guard, new Vector2(120, 104)));
forest1.Items.Add(new ItemSpawn("forest1_sword", ItemType.Sword, new Vector2(200, 104)));
forest1.Wizards.Add(new WizardSpawn("forest1_wizard", new Vector2(260, 80)));
_rooms["forest_1"] = forest1;
```

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

The game starts the player in `chateau_0` at `Vector2(160, 80)` (set in `Game1.LoadContent` and `RestartGame`). This is hard-coded; rooms don't carry a "default spawn position" today. (`RoomData.PlayerSpawn` exists in `Rooms/RoomData.cs` for a future room-DTO design but isn't currently wired in.)

## Per-Room State Persistence

`WorldState` keeps four sets, all keyed by entity ID:

- `DeadEnemies` — once dead, stays dead
- `PickedUpItems` — once picked up, the original spawn never reappears (but dropped items use new IDs and *do* persist in their drop room)
- `SavedWizards` — once rescued, never respawns
- `UnlockedDoors` — blocked doors that have been opened stay open

Plus `SavedRoomEnemies` — a snapshot map of `roomId → List<EnemyInstance>` used so enemies retain their position when you leave a room and come back. This is the only state that is *room-scoped* rather than entity-scoped.

The persistence model is what makes rooms feel like a coherent world rather than independent levels: dropping a Sword in room 5, going to room 6, and coming back finds the Sword exactly where you left it.

## RoomData (DTO, future use)

`Rooms/RoomData.cs` defines a fuller DTO (`Width`, `Height`, `Tiles`, `PlayerSpawn`, `Exits`, `BackgroundColor`, `BackgroundTextureName`, `CollisionGrid`) that is *not currently used* by the live system. It exists as the target shape for a future serializable-rooms / map-editor pipeline (Phase 5A). Today the live shape is the builder lambda + `RoomContent`; `RoomData` is a sketch.

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
