# Phase 2B Complete: Tile Collision Detection

**Date**: February 8, 2026
**Commit**: 4955b6c
**Status**: COMPLETE

---

## What We Built

### Tile Collision System
All collision logic lives in [PhysicsComponent.cs](Physics/PhysicsComponent.cs). The approach uses **separate-axis resolution** (move X, resolve, move Y, resolve) which prevents the player from sticking to corners.

**Key method**: `IsTileBlocking(tileX, tileY)` - single check that treats both Solid and Platform tiles as fully solid world geometry. This means all world-building blocks behave identically: they block the player from every direction (top, bottom, left, right).

### Collision Flow (per frame)
1. Apply X velocity, resolve horizontal collisions
2. Apply Y velocity, resolve vertical collisions
3. Check ground (tile directly below feet)
4. Clamp to screen boundaries

### Ground Detection
`CheckOnGround()` checks the tile row immediately below the player's feet. When grounded and no vertical input is pressed, vertical velocity is set to zero (player rests on surface).

### Test Room Layout
- **40x18 tiles** (320x144 pixels) - matches Python prototype
- **Floor**: row 17 (full width, 1 tile thick)
- **Platform**: row 12, columns 16-23 (8 tiles wide, centered, 5 rows above floor)
- Player spawns at top-center and falls to the floor

---

## Files Changed

| File | Change |
|------|--------|
| [PhysicsComponent.cs](Physics/PhysicsComponent.cs) | Added tile collision, `IsTileBlocking()`, `ResolveHorizontalCollision()`, `ResolveVerticalCollision()`, `CheckOnGround()`, `TileMap` reference |
| [Game1.cs](Game1.cs) | Simplified test room (floor + platform), wired tilemap to physics, updated spawn position |

---

## How Collision Works

```
Player hitbox: 24x24 pixels (3x3 tiles)

Horizontal check:
  Moving right -> check tiles at right edge of hitbox
  Moving left  -> check tiles at left edge of hitbox
  If blocking tile found -> push player out, zero X velocity

Vertical check:
  Falling down -> check tiles at bottom edge of hitbox
  Moving up    -> check tiles at top edge of hitbox
  If blocking tile found -> push player out, zero Y velocity

Ground check:
  Check tile row at (player.Y + 24) / 8
  If blocking tile exists -> IsOnGround = true
```

---

## Design Decision: Unified Solid Geometry

The user requested that all world-building tiles block from every direction. There is no distinction between "solid walls" and "platforms" for collision purposes. Both `TileType.Solid` and `TileType.Platform` are treated identically by `IsTileBlocking()`. This simplifies world construction - any blocking tile placed anywhere behaves consistently.

---

## Next Phase: 2C - Multi-Room Navigation

### Goal
Load rooms from data files, transition between rooms, and manage a connected world of multiple rooms.

### Tasks

#### 1. Room Loading from JSON
- Create `RoomLoader.cs` to read room definitions from JSON files
- Each JSON file defines: tile grid, player spawn, exits, metadata
- Load rooms on demand when player enters

**Example room JSON format:**
```json
{
  "roomId": "room_01",
  "name": "Starting Chamber",
  "width": 40,
  "height": 18,
  "tiles": [
    [24,24,24, ... ],
    [24,24,24, ... ],
    ...
  ],
  "playerSpawn": { "x": 148, "y": 16 },
  "exits": [
    {
      "type": "edge",
      "side": "right",
      "targetRoom": "room_02",
      "targetSpawn": { "x": 8, "y": 96 }
    }
  ]
}
```

#### 2. Room Manager
- Create `RoomManager.cs` to handle active room state
- Load/unload rooms as player transitions
- Track which room the player is currently in
- Manage room exit triggers

#### 3. Room Transitions
- Detect when player reaches an exit (screen edge, door tile)
- Load the target room
- Place player at the correct entry position
- Instant transition (matching original Amstrad behavior - no scrolling)

#### 4. Build 3-5 Connected Rooms
- Design a small test area with multiple connected rooms
- Test edge-based transitions (walk off right side -> appear on left of next room)
- Verify player position is correct after each transition

### Files to Create
```
Rooms/RoomLoader.cs      - JSON deserialization
Rooms/RoomManager.cs     - Active room management and transitions
Content/Rooms/            - JSON room data files
```

### Success Criteria
- [ ] Rooms load from JSON files
- [ ] Player walks off screen edge and appears in next room
- [ ] 3-5 rooms connected and navigable
- [ ] Room transitions feel instant (no loading delay)
- [ ] Player spawn position correct after each transition

---

## Progress Summary

| Phase | Status | Description |
|-------|--------|-------------|
| 1     | Complete | ECS, physics, animation, player controller, rendering |
| 2A    | Complete | Tile system, tilemap rendering, room data structure |
| 2B    | Complete | Tile collision, solid geometry, ground detection |
| **2C**| **Next** | **Room loading, transitions, multi-room navigation** |
| 3A    | Pending  | Inventory system |
| 3B    | Pending  | Enemy AI |
| 3C    | Pending  | Energy, cauldrons, timer |
| 4     | Pending  | Audio, UI, content |

**Overall: ~25% complete**
