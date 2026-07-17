# Phase 3B: Hooded Guard Enemy

## Overview

First enemy implementation for Sorcery+ Remake. The hooded guard is a ground-based patrol
enemy that horizontally follows the player on its current platform level.

## Spritesheet Analysis

**Source:** `Amstrad CPC - Sorcery - Characters.png`
**Row:** 2nd from top (Y=25), same 24x24 sprite size with 1px spacing as player.

| Frames | X Coordinates | Animation |
|--------|---------------|-----------|
| 0-3    | 0, 25, 50, 75 | Walk Left |
| 4-7    | 100, 125, 150, 175 | Direction Change (Idle) |
| 8-11   | 200, 225, 250, 275 | Walk Right |

**Initial frame:** One of the middle two frames (frame 5 or 6) when idle/spawned.

## Guard Behavior

### Movement Rules
- **Platform-locked:** Guard only moves horizontally on its spawn platform level.
- **Gravity-affected:** Falls until landing on a solid surface (uses existing PhysicsComponent).
- **Player-tracking:** Constantly moves toward player's X position.
- **Edge-aware:** Stops at platform edges; will NOT walk off into empty space.
- **Solid collision:** Collides with walls and solid tiles like the player does.
- **Speed:** Slower than the player (~80 px/s vs player's 200 px/s).

### Animation State Machine
```
                  player is left
    [Idle] ─────────────────────> [Walk Left]
      │                                │
      │  player is right               │ reaches edge OR
      └──────────────> [Walk Right]    │ close to player
                            │          │
                            └──────────> [Idle]
```

- **Idle:** Middle frames (4-7), 4-frame loop. Shown when player is very close or guard is at platform edge.
- **Walk Left:** Frames 0-3, 4-frame loop. Moving left toward player.
- **Walk Right:** Frames 8-11, 4-frame loop. Moving right toward player.

### Edge Detection Algorithm
Before moving in a direction, check if the tile below the guard's leading foot
(in the movement direction) is solid. If not, stop — the guard has reached the
edge of its platform.

```
Moving Right:
  Check tile at (guard.X + HITBOX_WIDTH, guard.Y + HITBOX_HEIGHT)
  If empty → stop, set idle

Moving Left:
  Check tile at (guard.X - 1, guard.Y + HITBOX_HEIGHT)
  If empty → stop, set idle
```

## Architecture

### New Files
- `Enemies/GuardController.cs` — AI controller component (follows PlayerController pattern)

### Modified Files
- `Graphics/SpriteConfig.cs` — Add guard animation frame definitions
- `Game1.cs` — Spawn guard entity in room_1 for testing

### Entity Structure
```
Guard Entity
├── PhysicsComponent (gravity + tile collision, Speed=80, GravitySpeed=120)
├── SpriteComponent (animated rendering from Characters.png)
└── GuardController (AI: follow player, edge detection, animation)
```

### GuardController Responsibilities
1. **Track player reference** — Needs player entity position each frame
2. **Determine direction** — Compare guard X to player X
3. **Edge detection** — Query tilemap for floor below next step
4. **Set velocity** — Horizontal only; vertical handled by PhysicsComponent gravity
5. **Update animation** — Switch between walk_left / idle / walk_right

## Test Setup (Room 1)

Spawn one guard on the floor of room_1:
- Position: `(160, 112)` — Center of room, on floor (row 17, Y = 17*8 - 24 = 112)
- The guard should immediately start walking toward the player
- Verify: animation plays correctly, guard stops at walls/edges, follows player

## Constants

| Constant | Value | Notes |
|----------|-------|-------|
| GUARD_SPEED | 80f | px/s, slower than player (200) |
| GUARD_HITBOX | 24x24 | Same as player |
| GUARD_ANIM_SPEED | 0.12f | Seconds per frame |
| GUARD_FOLLOW_THRESHOLD | 2f | Dead zone to prevent jitter |
