# 05 — Physics & Movement

The physics model is the single most important authentic-feel decision in the project. The original Sorcery+ used a deceptively simple **direct velocity** model — and that is what the remake replicates today.

## Where Physics Lives

| File | Role |
|------|------|
| [`Physics/PhysicsComponent.cs`](../Physics/PhysicsComponent.cs) | The live physics — direct velocity + tile collision + solid-rect collision + screen clamping |
| [`Core/PlayerController.cs`](../Core/PlayerController.cs) | Reads input, writes `Velocity` directly to the player's `PhysicsComponent` |
| [`Physics/DirectVelocityComponent.cs`](../Physics/DirectVelocityComponent.cs) | **Legacy / unused.** A simpler tilemap-less version preserved for reference. Do not wire this in. |
| [`Graphics/SpriteConfig.cs`](../Graphics/SpriteConfig.cs) | Per-enemy speeds (`GUARD_SPEED`, `MASK_SPEED`, etc.) and the velocity threshold for animation switching |

## The Model — Direct Velocity

This is **not** a force-based physics simulation. There are no accelerations, no friction, no integration of forces over time. Each frame:

1. Read input (or AI decision).
2. **Set velocity directly** based on what's pressed.
3. Position += velocity × dt.
4. Resolve collisions axis-separately (X, then Y).
5. Clamp to screen bounds.

```csharp
// PlayerController — instant set, no accumulation
if (Left  && !Right) targetX = -Speed;
else if (Right && !Left) targetX =  Speed;
else                    targetX =  0;

float vy = GravitySpeed;            // default: pulled down
if (Up)        vy = -Speed;          // thrust
else if (Down) vy =  Speed;          // dive

physics.Velocity = new Vector2(targetX, vy);
```

Releasing a key produces an **instant stop** in that axis. There is no glide. This feels twitchy by modern standards, but it is the original behavior and is essential for the feel of fighting gravity.

## Constants

Defined on `PhysicsComponent` (per-instance, not static):

| Constant | Default | Per-enemy override |
|----------|---------|---------------------|
| `Speed` | 140 px/s | `GUARD_SPEED=80`, `MASK_SPEED=150`, `BOAR_SPEED=150`, `EYE_SPEED=150`, `WRAITH_SPEED=220` |
| `GravitySpeed` | 160 px/s | Set to `0` for floating enemies (Mask, Boar, Eye, Wraith) |

**Why gravity > horizontal speed.** The original game made the wizard fall faster than he can fly sideways; pressing `Up` exactly cancels `GravitySpeed`, so flight feels like effort, not free movement. The slight gravity advantage (160 vs 140) is what makes the wizard feel weighty.

The player's `Speed` of 140 was chosen to feel right against the 8×8 tile grid in the available screenshot rooms; `GravitySpeed` of 160 keeps the wizard from "stalling" on platform edges.

## Why This Model Matters

The Sorcery+ rooms are designed around two facts that depend entirely on this physics:

1. **The wizard always falls if not actively thrusting.** Walking off a ledge is immediate, not a coyote-time grace window. This is enforced by [`PhysicsComponent.CheckOnGround`](../Physics/PhysicsComponent.cs#L241), which only reports "on ground" when the tile *directly under the wizard's center* is solid. Standing on a 1-tile-wide overhang doesn't qualify.

2. **The wizard must thread vertical shafts no wider than his sprite.** The 24-px sprite squeezes through a 24-px-wide gap because of the **2-px horizontal collision inset** (see [06_COLLISION.md](./06_COLLISION.md#the-2-pixel-horizontal-inset)).

If you switch to acceleration-based physics, both of these break: ledges become forgiving, and shafts stop working unless you re-tune the inset for momentum cases.

## Update Loop Detail

```
PhysicsComponent.Update(gameTime)
├── pos.X += vel.X * dt
├── ResolveHorizontalCollision(pos, ref vel)            ← tile collision X
├── ResolveSolidRectsHorizontal(pos, ref vel)           ← door / blocked-door X
├── pos.Y += vel.Y * dt
├── ResolveVerticalCollision(pos, ref vel)              ← tile collision Y
├── ResolveSolidRectsVertical(pos, ref vel)             ← door / blocked-door Y
├── IsOnGround = CheckOnGround(pos) || CheckOnGroundSolidRects(pos)
└── ClampToScreen(pos, ref vel)                         ← never leaves 320×144
```

Splitting movement into separate X and Y passes is the standard 2D-platformer trick that avoids "sticking on corners" — moving diagonally into a corner won't get the entity wedged. See [06_COLLISION.md](./06_COLLISION.md#separate-axis-resolution) for details.

## Tuning Knobs

Physical feel is controlled by exactly four knobs:

| Knob | Effect | Where |
|------|--------|-------|
| `PhysicsComponent.Speed` | Horizontal max speed (and Up-thrust speed for player) | Per-instance; player default = 140 |
| `PhysicsComponent.GravitySpeed` | Constant downward velocity when not thrusting | Per-instance; player default = 160 |
| `COLLISION_INSET_X` | Hitbox horizontal forgiveness | `Physics/PhysicsComponent.cs` (const, currently 2) |
| `ANIMATION_VELOCITY_THRESHOLD` | Speed at which walk animation kicks in | `Graphics/SpriteConfig.cs` (currently 10) |

**Don't change `HITBOX_WIDTH`/`HITBOX_HEIGHT`** without re-checking every door alignment, item-pickup overlap, blocked-door hitbox math, and enemy hitbox math. They are 24×24 across the codebase.

## Future: Authentic Flight Physics (Phase 4D)

The current model is "feel-correct enough" for the rooms shipped today, but `DEVELOPMENT_PHASES.md` Phase 4D plans to refine it to true momentum-flight (Up applies acceleration, releasing Up lets gravity gradually overtake — not the binary instant switch we have now). This is breaking and will require retuning all enemy speeds and re-balancing all rooms. Not currently scheduled.

## Rendering vs Physics

Physics runs at whatever framerate `Update` is called at — MonoGame defaults to 60 FPS fixed-step. Rendering and physics are coupled (no fixed-timestep separation). This is fine for the game's complexity but worth noting if you ever see behavior change at different framerates: it shouldn't, because we only use `dt`, but a non-fixed-step run could surface integration drift.

## Common Pitfalls

- **"Velocity is a struct."** `Vector2` is a value type. `_physics.Velocity.X = 0` looks reasonable but compiles to a no-op — you must reassign the whole `Vector2`. Done correctly throughout the codebase, but worth knowing when copying patterns.
- **Mutating velocity inside collision.** `ResolveHorizontalCollision` takes `ref Vector2 vel` so it can zero the relevant axis when the entity collides. If you write a new collision pass and forget the `ref`, the entity will keep momentum into a wall and embed itself.
- **Moving the player without setting `TileMap`.** A `PhysicsComponent` with no tilemap reference falls back to "no collision, just clamp to screen." If your enemy is going through walls, check that `physics.TileMap = _roomManager.CurrentTileMap` was set after spawn (and re-set after room transitions in `LoadRoomEnemies`).
