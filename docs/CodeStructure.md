# Code Structure & Architecture Guide
**Sorcery+ Remake - Developer Reference**

---

## Overview

This document explains the codebase architecture, design patterns, and how components interact. It serves as a reference for understanding and extending the game engine.

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────┐
│                       Game1.cs                          │
│                    (Main Game Loop)                     │
│                                                         │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐ │
│  │ Initialize() │→│  Update()    │→│   Draw()     │ │
│  └──────────────┘  └──────────────┘  └──────────────┘ │
└────────────┬────────────────────────────────┬───────────┘
             │                                │
             ▼                                ▼
    ┌────────────────┐              ┌─────────────────┐
    │  Entity System │              │ Render Pipeline │
    └────────────────┘              └─────────────────┘
             │                                │
        ┌────┴────┐                     ┌─────┴─────┐
        ▼         ▼                     ▼           ▼
   ┌────────┐ ┌─────────┐      ┌────────────┐ ┌────────┐
   │ Player │ │ Enemies │      │ RenderTgt  │ │ Sprite │
   │ Entity │ │ (Phase3)│      │ (640x400)  │ │ Batch  │
   └────────┘ └─────────┘      └────────────┘ └────────┘
        │
   ┌────┴─────┬─────────┬─────────┐
   ▼          ▼         ▼         ▼
┌──────┐ ┌────────┐ ┌────────┐ ┌────────┐
│Player│ │Physics │ │Sprite  │ │Input   │
│Ctrl  │ │Comp    │ │Comp    │ │System  │
└──────┘ └────────┘ └────────┘ └────────┘
```

---

## Entity-Component-System (ECS) Pattern

### Core Concept

Instead of inheritance hierarchies (Player extends Character extends GameObject), we use **composition**:

```
Entity = Container
Components = Data + Behavior
Systems = Logic that processes components
```

### Benefits

| Traditional OOP | ECS Approach |
|----------------|--------------|
| Rigid inheritance trees | Flexible composition |
| Behavior buried in classes | Behavior in reusable components |
| Hard to add features | Add component = add feature |
| Tightly coupled | Loosely coupled |

### Example: Creating the Player

```csharp
// Create an empty entity
Entity player = new Entity("Player");

// Add components to give it capabilities
player.AddComponent(new PhysicsComponent());     // Can move
player.AddComponent(new SpriteComponent(...));   // Can be rendered
player.AddComponent(new PlayerController());     // Can be controlled

// Later: Add new feature without changing existing code
player.AddComponent(new InventoryComponent());   // Can hold items
```

---

## Component Details

### 1. Entity (Core/Entity.cs)

**Purpose:** Container for components, holds transform data.

**Key Properties:**
```csharp
Vector2 Position   // In Amstrad coordinates (0-160, 0-200)
Vector2 Scale      // Usually (1, 1)
float Rotation     // Radians
Dictionary<Type, IComponent> _components
```

**Key Methods:**
```csharp
AddComponent<T>(T component)
GetComponent<T>() → T?
HasComponent<T>() → bool
Update(GameTime) → calls component.Update()
```

**Design Note:** Position is in "Amstrad pixel" space (160x200). Rendering code scales this to screen space.

---

### 2. PhysicsComponent (Physics/PhysicsComponent.cs)

**Purpose:** Handles flight mechanics (gravity, thrust, inertia).

**Key Properties:**
```csharp
Vector2 Velocity       // Current speed (pixels/second)
float Gravity          // Downward acceleration (300 px/s²)
float ThrustPower      // Upward thrust (400 px/s²)
float Damping          // Friction factor (0.85 = 15% loss per frame)
float MaxVelocity      // Speed cap (200 px/s)
```

**Update Loop:**
```csharp
1. Apply gravity → acceleration.Y += Gravity
2. Apply input forces → ApplyForce(thrust)
3. Integrate acceleration → velocity += acceleration * deltaTime
4. Apply damping → velocity *= Damping
5. Clamp velocity → velocity = clamp(velocity, MaxVelocity)
6. Update position → position += velocity * deltaTime
7. Reset acceleration
```

**Tuning Notes:**
- `Gravity` and `ThrustPower` are antagonistic forces
- `Damping` creates the "floaty" feel (lower = more friction)
- Adjust `MaxVelocity` to control tight corridor navigation

---

### 3. SpriteComponent (Graphics/SpriteComponent.cs)

**Purpose:** Renders animated sprites from a spritesheet.

**Key Properties:**
```csharp
Texture2D Texture           // The spritesheet
Rectangle SourceRectangle   // Current frame's position on sheet
Rectangle[] AnimationFrames // Array of frame rectangles
int CurrentFrame            // Animation playback state
float FrameTime             // Seconds per frame
bool FlipHorizontal         // For left/right facing
```

**Animation System:**
```csharp
// Set an animation
sprite.SetAnimation(
    frames: SpriteConfig.PLAYER_IDLE,
    frameTime: 0.12f,
    loop: true
);

// Every frame:
frameTimer += deltaTime
if (frameTimer >= FrameTime) {
    CurrentFrame++
    if (CurrentFrame >= frames.Length) {
        CurrentFrame = loop ? 0 : frames.Length - 1
    }
}
```

**Coordinate Note:** `SourceRectangle` is in spritesheet pixel space, not Amstrad space.

---

### 4. PlayerController (Core/PlayerController.cs)

**Purpose:** Translates keyboard input into forces and animation changes.

**Architecture:**
```csharp
Input → Determine Forces → Apply to Physics
  ↓
Velocity Analysis → Determine Animation State → Update Sprite
```

**State Machine:**
```
Input State + Velocity → Animation State

Examples:
- Up key pressed + velocity.Y < 0     → FlyingUp
- Left key pressed + velocity.X < -10 → FlyingLeft
- No input + |velocity| < 10          → Idle
- velocity.Y > 50                     → Falling
```

**Key Method:**
```csharp
PlayerAnimState DetermineAnimationState(
    Vector2 inputDirection,
    bool isThrusting,
    Vector2 velocity
) {
    // Priority system:
    if (isThrusting && velocity.Y < 0) return FlyingUp;
    if (velocity.Y > 50) return Falling;
    if (inputDirection.X != 0) return FlyingLeft/Right;
    return Idle;
}
```

---

## Rendering Pipeline

### Coordinate Spaces

The game uses **three different coordinate systems**:

| Space | Range | Purpose |
|-------|-------|---------|
| **Amstrad Space** | 0-160 x 0-200 | Game logic, physics |
| **Render Target Space** | 0-640 x 0-400 | Internal rendering (4x scale) |
| **Window Space** | 0-640 x 0-400+ | Final output (scalable) |

### Transformation Chain

```
Game Logic (Amstrad)
  ↓ (× RENDER_SCALE = 4)
Render Target (640x400)
  ↓ (SpriteBatch.Draw to window)
Window (scaled with PointClamp)
  ↓
Display
```

### Why This Approach?

1. **Physics Accuracy:** Logic in original coordinate space (160x200)
2. **Pixel Perfect:** PointClamp sampling prevents blur
3. **Scalability:** Can easily change window size without affecting game logic

### Code Example (Game1.cs Draw)

```csharp
// Step 1: Render to low-res target
GraphicsDevice.SetRenderTarget(_renderTarget);
_spriteBatch.Begin(samplerState: SamplerState.PointClamp);

// Convert Amstrad position to render target position
Vector2 renderPos = player.Position * RENDER_SCALE;
sprite.Draw(_spriteBatch, renderPos, scale: RENDER_SCALE);

_spriteBatch.End();

// Step 2: Render target to screen
GraphicsDevice.SetRenderTarget(null);
_spriteBatch.Begin(samplerState: SamplerState.PointClamp);
_spriteBatch.Draw(_renderTarget, destinationRectangle: windowRect);
_spriteBatch.End();
```

---

## Data Flow Diagrams

### Update Cycle (60 FPS)

```
Game1.Update(gameTime)
    │
    ├→ Keyboard.GetState() → currentKeyState
    │
    ├→ player.Update(gameTime)
    │   │
    │   ├→ PlayerController.Update()
    │   │   │
    │   │   ├→ Read arrow keys → inputDirection
    │   │   ├→ ApplyThrust() to PhysicsComponent
    │   │   └→ Update animation state
    │   │
    │   ├→ PhysicsComponent.Update()
    │   │   │
    │   │   ├→ Apply gravity
    │   │   ├→ Integrate forces → velocity
    │   │   ├→ Apply damping
    │   │   └→ Update position
    │   │
    │   └→ SpriteComponent.Update()
    │       └→ Advance animation frame
    │
    └→ (Future: Update enemies, items, etc.)
```

### Draw Cycle (Variable FPS)

```
Game1.Draw(gameTime)
    │
    ├→ SetRenderTarget(_renderTarget)
    ├→ DrawTestRoom()
    │   └→ Draw colored rectangles (placeholder)
    │
    ├→ DrawPlayer()
    │   ├→ Get player.Position (Amstrad space)
    │   ├→ renderPos = position * RENDER_SCALE
    │   └→ sprite.Draw(spriteBatch, renderPos)
    │
    ├→ SetRenderTarget(null)
    ├→ Draw _renderTarget to window
    │
    └→ DrawDebugInfo() (if enabled)
        └→ Draw position, velocity, FPS
```

---

## Extension Points

### Adding a New Entity Type (e.g., Enemy)

```csharp
// 1. Create entity
Entity ghost = new Entity("Ghost");

// 2. Add components
ghost.AddComponent(new PhysicsComponent {
    Gravity = 0,  // Ghosts don't fall
    Damping = 0.95  // Less friction than player
});

ghost.AddComponent(new SpriteComponent(
    spriteSheet,
    SpriteConfig.ENEMY_GHOST
));

ghost.AddComponent(new AIComponent {
    Behavior = AIBehavior.SeekPlayer
});

// 3. Add to entity list in Game1.cs
_entities.Add(ghost);

// 4. Update/Draw in game loop
foreach (var entity in _entities) {
    entity.Update(gameTime);
    entity.Draw(gameTime);
}
```

### Adding a New Component Type

```csharp
// 1. Implement IComponent
public class InventoryComponent : IComponent {
    public Entity? Owner { get; set; }
    public Item? HeldItem { get; set; }

    public void Update(GameTime gameTime) {
        // Inventory logic
    }

    public void Draw(GameTime gameTime) {
        // Render held item sprite
    }
}

// 2. Add to player
player.AddComponent(new InventoryComponent());

// 3. Access from other components
var inventory = player.GetComponent<InventoryComponent>();
if (inventory != null) {
    inventory.HeldItem = new Item("Key");
}
```

---

## Performance Considerations

### Current Performance (Phase 1)

- **Entities:** 1 (player)
- **Draw Calls:** ~5 (room + player + debug)
- **FPS:** 60 (stable)
- **Memory:** ~10 MB (minimal)

### Scalability Targets (Future Phases)

| Phase | Entities | Draw Calls | Expected FPS |
|-------|----------|------------|--------------|
| Phase 1 | 1 | 5 | 60 |
| Phase 2 | 10-20 | 50 | 60 |
| Phase 3 | 50-100 | 200 | 60 |
| Phase 4 | 200+ | 500 | 60 |

### Optimization Strategies (Future)

1. **Object Pooling:** Reuse entities instead of create/destroy
2. **Spatial Hashing:** Only update entities near player
3. **Sprite Batching:** Group draw calls by texture
4. **Frustum Culling:** Don't render off-screen entities

---

## Testing Strategy

### Manual Testing (Phase 1)

1. **Physics Verification**
   - Hold Up arrow → player should hover at constant height
   - Release Up → player should fall with damping
   - Press Left/Right → should accelerate gradually (not instant)

2. **Animation Verification**
   - Idle → should play gentle hover animation
   - Move left → should flip sprite horizontally
   - Move up → should play thrust animation

3. **Coordinate Verification**
   - Player at (80, 100) Amstrad space
   - Should appear at center of 640x400 render target
   - Should appear at center of window

### Unit Testing (Future)

```csharp
[Test]
public void PhysicsComponent_ApplyGravity_IncreasesVelocityY() {
    var physics = new PhysicsComponent { Gravity = 300 };
    var entity = new Entity();
    physics.Owner = entity;

    physics.Update(new GameTime {
        ElapsedGameTime = TimeSpan.FromSeconds(1.0/60.0)
    });

    Assert.Greater(physics.Velocity.Y, 0); // Should be falling
}
```

---

## Common Patterns & Idioms

### Component Communication

**❌ Bad (Tight Coupling):**
```csharp
public class PlayerController {
    private PhysicsComponent _physics; // Hard reference
}
```

**✅ Good (Loose Coupling):**
```csharp
public class PlayerController {
    private PhysicsComponent? _physics;

    public void Initialize() {
        _physics = Owner?.GetComponent<PhysicsComponent>();
    }
}
```

### Null Safety

**Enabled:** C# nullable reference types (`#nullable enable`)

**Pattern:**
```csharp
var physics = Owner?.GetComponent<PhysicsComponent>();
if (physics != null) {
    physics.ApplyForce(force);
}
// OR
physics?.ApplyForce(force); // Null-conditional
```

### Configuration Constants

**❌ Bad (Magic Numbers):**
```csharp
velocity *= 0.85;
```

**✅ Good (Named Constants):**
```csharp
public float Damping { get; set; } = 0.85f; // 15% friction
velocity *= Damping;
```

---

## File Naming Conventions

| Type | Naming | Example |
|------|--------|---------|
| Entity classes | PascalCase | `Entity.cs` |
| Components | PascalCase + Component | `PhysicsComponent.cs` |
| Interfaces | I + PascalCase | `IComponent.cs` |
| Static configs | PascalCase + Config | `SpriteConfig.cs` |
| Game systems | PascalCase | `Game1.cs` |

---

## Future Architecture Additions

### Phase 2: Systems

```csharp
// System = Logic that processes specific components
public class RenderSystem {
    public void Render(SpriteBatch batch, List<Entity> entities) {
        foreach (var entity in entities) {
            var sprite = entity.GetComponent<SpriteComponent>();
            sprite?.Draw(batch, entity.Position);
        }
    }
}
```

### Phase 3: Events

```csharp
// Event bus for cross-system communication
public class EventBus {
    public event Action<Entity, Item>? OnItemPickedUp;

    public void RaiseItemPickup(Entity player, Item item) {
        OnItemPickedUp?.Invoke(player, item);
    }
}
```

---

## References

- **ECS Pattern:** [Game Programming Patterns - Component](http://gameprogrammingpatterns.com/component.html)
- **MonoGame Docs:** https://docs.monogame.net/
- **C# Nullable Types:** https://learn.microsoft.com/en-us/dotnet/csharp/nullable-references

---

**Last Updated:** Phase 1 Complete (February 7, 2026)
