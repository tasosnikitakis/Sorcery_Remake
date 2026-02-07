# Animation Reference Guide
**Sorcery+ Remake - Sprite Frame Mapping**

---

## Spritesheet Layout

**File:** `assets/images/Amstrad CPC - Sorcery - Characters.png`

```
┌────────────────────────────────────────────────────────────────────────┐
│ Row 1 (Y=0): Player - Yellow Wizard                                    │
│ ┌───┬───┬───┬───┬───┬───┬───┬───┬───┬───┬───┬───┐                    │
│ │ 0 │ 1 │ 2 │ 3 │ 4 │ 5 │ 6 │ 7 │ 8 │ 9 │10 │11 │                    │
│ └───┴───┴───┴───┴───┴───┴───┴───┴───┴───┴───┴───┘                    │
│ └─── idle_front ──┘ └─── walk_right ────────┘ └─ flying_up/falling ─┘│
│   (4 frames)          (4 frames)                  (2+2 frames)        │
├────────────────────────────────────────────────────────────────────────┤
│ Row 2 (Y=24): Player - Green Wizard (palette swap)                    │
│ Row 3 (Y=48): Player - Purple Wizard (palette swap)                   │
│ Row 4 (Y=72): Player - Pink Wizard (palette swap)                     │
│ Row 5+ (Y=96+): Enemies and other sprites                             │
└────────────────────────────────────────────────────────────────────────┘
```

**Each sprite cell = 24x24 pixels**

---

## Animation Definitions

### 1. idle_front (Python: "idle_front")

**Purpose:** Default idle/hovering animation when stationary

**Frame Count:** 4
**Animation Speed:** 0.117 seconds per frame (slower, gentle bob)
**Loop:** Yes

| Frame | X | Y | W | H | Description |
|-------|---|---|---|---|-------------|
| 0 | 0 | 0 | 24 | 24 | Standing/neutral |
| 1 | 24 | 0 | 24 | 24 | Slight bob up |
| 2 | 48 | 0 | 24 | 24 | Peak hover |
| 3 | 72 | 0 | 24 | 24 | Bob down |

**C# Code:**
```csharp
SpriteConfig.PLAYER_IDLE_FRONT
```

**Trigger Conditions:**
- Velocity.X < threshold AND
- Velocity.Y < threshold * 3 AND
- Not thrusting

---

### 2. walk_right (Python: "walk_right")

**Purpose:** Flying/walking to the right

**Frame Count:** 4
**Animation Speed:** 0.1 seconds per frame
**Loop:** Yes
**Flip:** No (faces right naturally)

| Frame | X | Y | W | H | Description |
|-------|---|---|---|---|-------------|
| 0 | 96 | 0 | 24 | 24 | Step 1 |
| 1 | 120 | 0 | 24 | 24 | Step 2 |
| 2 | 144 | 0 | 24 | 24 | Step 3 |
| 3 | 168 | 0 | 24 | 24 | Step 4 |

**C# Code:**
```csharp
SpriteConfig.PLAYER_WALK_RIGHT
```

**Trigger Conditions:**
- Velocity.X > ANIMATION_VELOCITY_THRESHOLD (10 px/s)

---

### 3. walk_left (Python: "walk_left")

**Purpose:** Flying/walking to the left

**Frame Count:** 4 (same as walk_right)
**Animation Speed:** 0.1 seconds per frame
**Loop:** Yes
**Flip:** **Yes (FlipHorizontal = true)**

| Frame | X | Y | W | H | Description |
|-------|---|---|---|---|-------------|
| 0 | 96 | 0 | 24 | 24 | Step 1 (flipped) |
| 1 | 120 | 0 | 24 | 24 | Step 2 (flipped) |
| 2 | 144 | 0 | 24 | 24 | Step 3 (flipped) |
| 3 | 168 | 0 | 24 | 24 | Step 4 (flipped) |

**C# Code:**
```csharp
SpriteConfig.PLAYER_WALK_LEFT  // Same frames as walk_right
sprite.FlipHorizontal = true;   // But flipped horizontally
```

**Trigger Conditions:**
- Velocity.X < -ANIMATION_VELOCITY_THRESHOLD (-10 px/s)

---

### 4. flying_up (C# Enhancement)

**Purpose:** Thrusting upward (fighting gravity)

**Frame Count:** 2
**Animation Speed:** 0.08 seconds per frame (fast)
**Loop:** Yes
**Flip:** No

| Frame | X | Y | W | H | Description |
|-------|---|---|---|---|-------------|
| 0 | 192 | 0 | 24 | 24 | Thrust pose 1 |
| 1 | 216 | 0 | 24 | 24 | Thrust pose 2 |

**C# Code:**
```csharp
SpriteConfig.PLAYER_FLYING_UP
```

**Trigger Conditions:**
- Up key pressed AND
- Velocity.Y < -threshold

**Note:** This animation was NOT in the Python prototype (it used idle_front for vertical movement). This is a C# enhancement for better visual feedback.

---

### 5. falling (C# Enhancement)

**Purpose:** Falling fast due to gravity

**Frame Count:** 2
**Animation Speed:** 0.1 seconds per frame
**Loop:** Yes
**Flip:** No

| Frame | X | Y | W | H | Description |
|-------|---|---|---|---|-------------|
| 0 | 240 | 0 | 24 | 24 | Fall pose 1 |
| 1 | 264 | 0 | 24 | 24 | Fall pose 2 |

**C# Code:**
```csharp
SpriteConfig.PLAYER_FALLING
```

**Trigger Conditions:**
- Velocity.Y > threshold * 3 (30 px/s downward)

**Note:** This animation was NOT in the Python prototype. This is a C# enhancement.

---

## Animation State Machine

```
                        ┌─────────────┐
                        │    Idle     │ ← Default state
                        │ (idle_front)│
                        └──────┬──────┘
                               │
                ┌──────────────┼──────────────┐
                │              │              │
        Vel.X > 10       Vel.Y < -10    Vel.Y > 30
                │              │              │
                ▼              ▼              ▼
        ┌──────────┐   ┌──────────┐   ┌──────────┐
        │WalkRight │   │ FlyingUp │   │ Falling  │
        └──────────┘   └──────────┘   └──────────┘
                │
        Vel.X < -10
                │
                ▼
        ┌──────────┐
        │ WalkLeft │
        │ (flipped)│
        └──────────┘
```

**Priority Order:**
1. Horizontal movement (walk_right / walk_left)
2. Vertical movement (flying_up / falling)
3. Default (idle_front)

---

## Testing Checklist

When you run the game, verify these animations:

### ✅ idle_front
- [ ] Plays when player spawns
- [ ] 4 frames cycle smoothly
- [ ] Gentle bobbing motion
- [ ] Slower animation speed (noticeable pause between frames)

### ✅ walk_right
- [ ] Triggers when pressing Right arrow
- [ ] 4 frames cycle smoothly
- [ ] Sprite faces right
- [ ] Smooth walking/flying motion

### ✅ walk_left
- [ ] Triggers when pressing Left arrow
- [ ] Uses same 4 frames as walk_right
- [ ] **Sprite is horizontally flipped** (facing left)
- [ ] Smooth walking/flying motion

### ✅ flying_up (Optional - C# enhancement)
- [ ] Triggers when pressing Up arrow and moving upward
- [ ] 2 frames alternate quickly
- [ ] Suggests upward thrust

### ✅ falling (Optional - C# enhancement)
- [ ] Triggers when falling fast (release Up arrow)
- [ ] 2 frames alternate
- [ ] Suggests downward motion

---

## Common Issues & Fixes

### Issue: All sprites appear as magenta squares
**Cause:** Spritesheet failed to load
**Fix:**
1. Verify `assets/images/Amstrad CPC - Sorcery - Characters.png` exists
2. Check MonoGame content pipeline setup
3. See fallback code in `Game1.cs` LoadContent()

### Issue: Sprites are too small/large
**Cause:** Incorrect scaling factor
**Fix:**
- Sprites are 24x24 native
- Scaled 4x = 96x96 on screen (for 640x400 window)
- Check `RENDER_SCALE = 4` in `Game1.cs`

### Issue: walk_left faces the wrong direction
**Cause:** FlipHorizontal not applied
**Fix:**
- Check `PlayerController.cs` ApplyAnimationState()
- Ensure `sprite.FlipHorizontal = true` for FlyingLeft state

### Issue: Animations don't play (frozen on one frame)
**Cause:** Animation update not being called
**Fix:**
- Check `SpriteComponent.Update()` is being called
- Verify `IsPlaying = true`
- Check `FrameTime` is > 0

### Issue: Animations too fast/slow
**Cause:** Incorrect frame timing
**Fix:**
- Idle: 0.117s per frame (slowest)
- Walk: 0.1s per frame (medium)
- Thrust: 0.08s per frame (fastest)
- Adjust in `SpriteConfig.cs`

---

## Velocity Threshold Tuning

The velocity threshold determines how fast the player must move before the walk animation triggers.

**Current:** `ANIMATION_VELOCITY_THRESHOLD = 10f` (10 pixels/second)

### If animations trigger too easily:
```csharp
// In SpriteConfig.cs
public const float ANIMATION_VELOCITY_THRESHOLD = 15f; // Increase threshold
```

### If animations don't trigger enough:
```csharp
// In SpriteConfig.cs
public const float ANIMATION_VELOCITY_THRESHOLD = 5f; // Decrease threshold
```

### Python Prototype Comparison:
- Python: 18 px/s (in 320x144 space)
- C#: 10 px/s (in 160x200 space)
- Ratio: ~0.56x (adjusted for smaller coordinate system)

---

## Sprite Coordinates Quick Reference

**Copy-paste for manual verification:**

```
idle_front:  [0,0] [24,0] [48,0] [72,0]
walk_right:  [96,0] [120,0] [144,0] [168,0]
walk_left:   [96,0] [120,0] [144,0] [168,0] + flip
flying_up:   [192,0] [216,0]
falling:     [240,0] [264,0]

All frames: W=24, H=24
```

---

## Debug Commands

### Toggle Debug Overlay
**Key:** F1

Shows:
- Current position (Amstrad coordinates)
- Current velocity (X, Y)
- Current FPS
- Current animation state (would need to add this)

### Recommended Debug Addition

Add this to `Game1.cs` DrawDebugInfo():

```csharp
var controller = _player.GetComponent<PlayerController>();
debugText += $"Animation: {controller?.CurrentAnimationName ?? "None"}\n";
```

---

**Last Updated:** February 7, 2026
**Verified Against:** Python prototype settings.py and player.py
