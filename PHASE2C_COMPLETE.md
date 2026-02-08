# Phase 2C Complete: Door-Based Room Transitions

**Date**: February 8, 2026
**Status**: COMPLETE

---

## What We Built

### Door System
A complete door-based room transition system where doors are solid objects that block the player, trigger an opening animation when the player aligns with the active side, and transport the player to a destination room/door.

### Door Components

**DoorConfig** ([Doors/DoorConfig.cs](Doors/DoorConfig.cs))
- Door game-world size: 24x24 pixels (same as player)
- Door sprite source size: 48x48 pixels (scaled down when drawn)
- Two door types: `LeftOpening` (triggered from left) and `RightOpening` (triggered from right)
- 4-frame animation at 0.15s per frame
- Source rectangles point to dedicated spritesheet PNGs (192x48, 4 frames in a row)

**DoorComponent** ([Doors/DoorComponent.cs](Doors/DoorComponent.cs))
- State machine: Closed -> Opening -> Open
- Trigger detection: player edge must align with door edge (within 3px), Y must match (within 2px)
- Arrival position: player spawns on trigger side, offset 5px to prevent re-trigger
- Draw method uses destination rectangle to scale 48x48 source to 24x24 game size

**RoomManager** ([Rooms/RoomManager.cs](Rooms/RoomManager.cs))
- Manages room registry, current room state, and transitions
- Room builders registered by ID, loaded on demand
- Transition flow: detect trigger -> freeze game -> play animation -> load target room -> place player
- Separate left/right door textures with swapped assignment (LeftOpening uses right spritesheet, RightOpening uses left)

### Door Sprite Extraction
Each door frame is a 2x2 block of 24x24 cells (48x48 total) from Spritesheet2.png:
- Frame 0 (closed): R3C25+R3C26 / R4C25+R4C26
- Frame 1 (opening): R1C25+R1C26 / R2C25+R2C26
- Frame 2 (more open): R7C23+R7C24 / R8C23+R8C24
- Frame 3 (fully open): R5C23+R5C24 / R6C23+R6C24

Build script: [tools/build_door_spritesheet.py](tools/build_door_spritesheet.py)
- Extracts frames from Spritesheet2.png
- Creates LeftDoorFrames.png (original) and RightDoorFrames.png (horizontally mirrored)

### Door Solidity
Doors are solid objects that the player cannot walk through. This is implemented via `SolidRects` in PhysicsComponent - a list of rectangles that use separate-axis collision resolution (same approach as tile collision).

### Test Rooms
- **Room 1**: Floor + platform + LeftOpening door at right edge (296, 112)
- **Room 2**: Floor only + RightOpening door at left edge (0, 112)
- Player can walk between rooms via doors

---

## Transition Flow

```
1. Player walks toward door
2. Door is solid -> player stops at door edge (natural trigger position)
3. IsPlayerAligned() detects alignment
4. Game freezes (IsGameFrozen = true)
5. Door opening animation plays (4 frames, ~0.6s)
6. Animation completes -> TransitionReady state
7. Target room loaded, old room replaced
8. Player placed at destination door's arrival position (trigger side + 5px offset)
9. Game unfreezes, player resumes control
```

---

## Files Changed

| File | Change |
|------|--------|
| [Game1.cs](Game1.cs) | Room builder registration, door collision wiring, transition handling, separate door texture loading |
| [Physics/PhysicsComponent.cs](Physics/PhysicsComponent.cs) | Added `SolidRects` collision system (`ResolveSolidRectsHorizontal`, `ResolveSolidRectsVertical`, `CheckOnGroundSolidRects`) |
| [Content/Content.mgcb](Content/Content.mgcb) | Added Spritesheet2.png, LeftDoorFrames.png, RightDoorFrames.png |

## Files Created

| File | Purpose |
|------|---------|
| [Doors/DoorConfig.cs](Doors/DoorConfig.cs) | Door types, sizes, animation constants, frame rectangles |
| [Doors/DoorComponent.cs](Doors/DoorComponent.cs) | Door entity with trigger detection, animation, drawing |
| [Rooms/RoomManager.cs](Rooms/RoomManager.cs) | Room registry, loading, transitions, door management |
| [Content/LeftDoorFrames.png](Content/LeftDoorFrames.png) | Left-opening door animation spritesheet (192x48) |
| [Content/RightDoorFrames.png](Content/RightDoorFrames.png) | Right-opening door animation spritesheet (192x48) |
| [Content/Spritesheet2.png](Content/Spritesheet2.png) | Second spritesheet (source for door frames) |
| [tools/build_door_spritesheet.py](tools/build_door_spritesheet.py) | Script to extract door frames from Spritesheet2.png |

---

## Progress Summary

| Phase | Status | Description |
|-------|--------|-------------|
| 1     | Complete | ECS, physics, animation, player controller, rendering |
| 2A    | Complete | Tile system, tilemap rendering, room data structure |
| 2B    | Complete | Tile collision, solid geometry, ground detection |
| **2C**| **Complete** | **Door system, room transitions, solid doors, door animation** |
| 2D    | Next | Room data from JSON files, build connected world |
| 3A    | Pending | Inventory system |
| 3B    | Pending | Enemy AI |
| 3C    | Pending | Energy, cauldrons, timer |
| 4     | Pending | Audio, UI, content |

**Overall: ~30% complete**

---

## Next Phase: 2D - Room Data & Connected World

### Goal
Move room definitions from hardcoded builders to JSON data files, and build a connected world of multiple rooms using the actual Sorcery+ room layouts.

### Tasks

#### 1. Room Data Format
- Define JSON schema for room definitions (tile grid, doors, metadata)
- Create `RoomLoader.cs` to deserialize room JSON files
- Support tile indices that map to the tileset

#### 2. Extract Actual Room Layouts
- Map the original Sorcery+ rooms from the Amstrad CPC data
- Convert tile data to the JSON format
- Identify door placements and connections between rooms

#### 3. Build Connected Room Graph
- Define all room-to-room connections via doors
- Ensure bidirectional consistency (Room A door -> Room B, Room B door -> Room A)
- Test navigation through multiple connected rooms

#### 4. Room Loading System
- Load rooms from JSON on demand
- Unload previous room data when transitioning
- Validate room data on load (correct dimensions, valid tile indices)

### Success Criteria
- [ ] Rooms defined in JSON data files (not hardcoded)
- [ ] At least 5-10 rooms connected via doors
- [ ] Player can navigate between all connected rooms
- [ ] Room transitions work correctly in all directions
- [ ] Room data matches original Sorcery+ layouts
