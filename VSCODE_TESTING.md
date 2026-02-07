# VS Code Testing Guide
**Sorcery+ Remake - Run, Debug, and Test in VS Code**

---

## Quick Start (3 Ways to Run)

### Method 1: Press F5 (Fastest)
```
1. Press F5
2. Game builds and launches automatically
3. Debug tools available (breakpoints, watch, etc.)
```

### Method 2: VS Code Terminal
```bash
# Open terminal in VS Code (Ctrl + `)
dotnet run
```

### Method 3: Run & Debug Panel
```
1. Click "Run and Debug" icon in sidebar (Ctrl+Shift+D)
2. Select "🎮 Run Game (Debug)"
3. Click green play button
```

---

## Debugging Features

### Setting Breakpoints

1. **Click in the gutter** (left of line numbers) to add a red dot
2. **Run with F5**
3. **Game pauses** when that line executes

**Example: Debug player movement**
```csharp
// Core/PlayerController.cs, line ~110
public void Update(GameTime gameTime)
{
    if (Owner == null || _physics == null) return;

    _currentKeyState = Keyboard.GetState(); // ← Put breakpoint here

    // ... rest of code
}
```

### Watch Variables

When paused at a breakpoint:
1. **Hover over variables** to see their values
2. **Add to Watch panel** (right-click variable → "Add to Watch")
3. **Inspect complex objects** (expand properties)

**Useful variables to watch:**
- `_physics.Velocity` - Current player speed
- `_player.Position` - Player coordinates
- `_currentAnimState` - Current animation
- `gameTime.ElapsedGameTime.TotalSeconds` - Frame delta

### Debug Console

While debugging, use the **Debug Console** to execute code:
```csharp
// Check player position
_player.Position

// Change gravity on the fly
_physics.Gravity = 500f

// Force an animation
_sprite.SetAnimation(SpriteConfig.PLAYER_FLYING_UP, 0.1f, true)
```

---

## Build Configurations

### Debug Build (Default)
```bash
# Slower, includes debug symbols
dotnet build --configuration Debug
dotnet run --configuration Debug
```

**Use for:**
- ✅ Development
- ✅ Debugging with breakpoints
- ✅ Testing new features

### Release Build (Optimized)
```bash
# Faster, optimized code
dotnet build --configuration Release
dotnet run --configuration Release
```

**Use for:**
- ✅ Performance testing
- ✅ Sharing builds with others
- ✅ Final testing before release

**To run Release in VS Code:**
1. Open Run & Debug panel (Ctrl+Shift+D)
2. Select "🚀 Run Game (Release)"
3. Press F5

---

## Testing Workflows

### Workflow 1: Rapid Iteration (Hot Reload)

MonoGame doesn't support full hot reload, but you can minimize rebuild time:

```bash
# In VS Code terminal
dotnet watch run
```

**Benefits:**
- Auto-rebuilds on file save
- Faster than manual rebuild
- Good for tweaking constants

**Limitations:**
- Still requires game restart
- No true "live" editing

### Workflow 2: Test-Driven Development

Create a test project (future enhancement):

```bash
dotnet new xunit -n SorceryRemake.Tests
dotnet add SorceryRemake.Tests reference SorceryRemake.csproj
```

**Example test:**
```csharp
[Fact]
public void PhysicsComponent_ApplyGravity_IncreasesVelocityY()
{
    var physics = new PhysicsComponent { Gravity = 300 };
    var entity = new Entity();
    physics.Owner = entity;

    physics.Update(new GameTime { ElapsedGameTime = TimeSpan.FromSeconds(1.0/60.0) });

    Assert.True(physics.Velocity.Y > 0); // Should be falling
}
```

### Workflow 3: Animation Testing

**Quick animation test loop:**

1. **Modify sprite config:**
   ```csharp
   // Graphics/SpriteConfig.cs
   public const float PLAYER_WALK_ANIMATION_SPEED = 0.05f; // Make faster
   ```

2. **Save file** (Ctrl+S)

3. **Restart game** (Ctrl+Shift+F5 in debug mode)

4. **Test animation** with arrow keys

5. **Adjust** and repeat

---

## Keyboard Shortcuts

### VS Code Essentials

| Shortcut | Action |
|----------|--------|
| **F5** | Start debugging |
| **Ctrl+F5** | Run without debugging (faster) |
| **Shift+F5** | Stop debugging |
| **Ctrl+Shift+F5** | Restart debugging |
| **F9** | Toggle breakpoint |
| **F10** | Step over (next line) |
| **F11** | Step into (enter function) |
| **Shift+F11** | Step out (exit function) |

### Game Controls (While Running)

| Key | Action |
|-----|--------|
| **Arrow Keys** | Move player |
| **Up** | Thrust (fight gravity) |
| **F1** | Toggle debug overlay (in-game) |
| **ESC** | Exit game |

### Terminal Shortcuts

| Shortcut | Action |
|----------|--------|
| **Ctrl+`** | Open/close terminal |
| **Ctrl+Shift+`** | New terminal |
| **Ctrl+C** | Stop running process |

---

## Performance Profiling

### Built-in Debug Info

The game already shows FPS and basic stats:
- Press **F1** in-game to toggle debug overlay

**Current debug info shows:**
- FPS (frames per second)
- Player position (Amstrad coordinates)
- Player velocity (X, Y)

### Add Custom Debug Info

Edit `Game1.cs` DrawDebugInfo():

```csharp
private void DrawDebugInfo(GameTime gameTime)
{
    if (_debugFont == null) return;

    _spriteBatch.Begin();

    var physics = _player.GetComponent<PhysicsComponent>();
    var sprite = _player.GetComponent<SpriteComponent>();

    string debugText = $"SORCERY+ REMAKE - DEBUG INFO\n" +
                       $"FPS: {1.0 / gameTime.ElapsedGameTime.TotalSeconds:F0}\n" +
                       $"Position: ({_player.Position.X:F1}, {_player.Position.Y:F1})\n" +
                       $"Velocity: ({physics?.Velocity.X:F1}, {physics?.Velocity.Y:F1})\n" +
                       $"Animation: {sprite?.CurrentFrame ?? 0}/{sprite?.AnimationFrames?.Length ?? 0}\n" + // NEW
                       $"Damping: {physics?.Damping:F2}\n" +  // NEW
                       $"Gravity: {physics?.Gravity:F0} px/s²\n";  // NEW

    _spriteBatch.DrawString(_debugFont, debugText, new Vector2(10, 10), Color.Yellow);
    _spriteBatch.End();
}
```

### Measure Frame Time

Add to Update():

```csharp
private double _totalUpdateTime = 0;
private int _updateCount = 0;

protected override void Update(GameTime gameTime)
{
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();

    // ... existing update code ...

    stopwatch.Stop();
    _totalUpdateTime += stopwatch.Elapsed.TotalMilliseconds;
    _updateCount++;

    if (_updateCount % 60 == 0) // Log every 60 frames
    {
        Console.WriteLine($"Avg update time: {_totalUpdateTime / _updateCount:F2}ms");
    }

    base.Update(gameTime);
}
```

---

## Common Testing Scenarios

### Test 1: Physics Tuning

**Goal:** Find the perfect gravity value

```csharp
// Physics/PhysicsComponent.cs
public float Gravity { get; set; } = 300f; // ← Change this

// Try different values:
// - 200f: Lighter gravity (slower fall)
// - 300f: Current (balanced)
// - 400f: Heavy gravity (faster fall)
```

**Test:**
1. Change value
2. Save (Ctrl+S)
3. Restart (Ctrl+Shift+F5)
4. Let player fall and observe
5. Adjust and repeat

### Test 2: Animation Speed

**Goal:** Find the right animation timing

```csharp
// Graphics/SpriteConfig.cs
public const float PLAYER_WALK_ANIMATION_SPEED = 0.1f; // ← Adjust

// Try:
// - 0.05f: Very fast (cartoonish)
// - 0.1f: Current (balanced)
// - 0.2f: Slow (cinematic)
```

**Test:**
1. Change value
2. Save + Restart
3. Move left/right and observe walk cycle
4. Find sweet spot

### Test 3: Sprite Frame Positions

**Goal:** Verify sprites are aligned correctly

```csharp
// Graphics/SpriteConfig.cs
public static readonly Rectangle[] PLAYER_IDLE_FRONT = new Rectangle[]
{
    new Rectangle(0, 0, SPRITE_WIDTH, SPRITE_HEIGHT),    // ← Check X values
    new Rectangle(24, 0, SPRITE_WIDTH, SPRITE_HEIGHT),
    // ...
};
```

**Test:**
1. Run game
2. Watch idle animation
3. If sprites are glitchy or show wrong frames:
   - X positions are wrong (adjust by 24px increments)
   - Sprite size is wrong (should be 24x24)
4. Fix and retest

### Test 4: Velocity Threshold

**Goal:** Make walk animation trigger at the right speed

```csharp
// Graphics/SpriteConfig.cs
public const float ANIMATION_VELOCITY_THRESHOLD = 10f; // ← Tune this
```

**Test:**
1. Run game
2. Tap arrow key briefly (slow movement)
   - Should animation trigger? (If threshold too low, it will)
3. Hold arrow key (fast movement)
   - Should always trigger
4. Find threshold where walk only plays during intentional movement

---

## Output Locations

### Build Output
```
bin/Debug/net8.0/SorceryRemake.dll
bin/Debug/net8.0/SorceryRemake.exe (Windows)
```

### Console Output
- **VS Code Debug Console**: Shows Console.WriteLine()
- **Terminal**: Shows dotnet build/run messages

### Logs
Add logging:
```csharp
// In any class
System.Diagnostics.Debug.WriteLine($"Player position: {Position}");
```

View in **Debug Console** (Ctrl+Shift+Y)

---

## Troubleshooting

### Game won't start

**Check terminal output:**
```bash
dotnet build
# Look for errors
```

**Common issues:**
- Missing semicolon → Syntax error
- Typo in class name → Build error
- Missing MonoGame reference → NuGet restore needed

**Fix:**
```bash
dotnet restore
dotnet build
```

### Breakpoints not working

**Cause:** Running in Release mode (optimized)

**Fix:**
1. Use Debug configuration
2. Check launch.json uses "Debug" configuration

### Game runs but black screen

**Possible causes:**
1. Spritesheet failed to load
2. Rendering code has exception

**Debug:**
```csharp
// Game1.cs LoadContent()
try {
    _spriteSheet = Content.Load<Texture2D>("Characters");
    Console.WriteLine("✅ Spritesheet loaded");
} catch (Exception ex) {
    Console.WriteLine($"❌ Failed: {ex.Message}");
}
```

### Slow performance in Debug

**This is normal!** Debug builds are slower.

**Fix:**
- Use Release build for performance testing
- Or disable "Just My Code" in debugger settings

---

## VS Code Extensions (Recommended)

### Essential
- **C# Dev Kit** - IntelliSense, debugging
- **C#** - Language support

### Nice to Have
- **GitLens** - Advanced git features
- **Error Lens** - Inline error display
- **Better Comments** - Color-coded comments

**Install:**
1. Ctrl+Shift+X (Extensions panel)
2. Search extension name
3. Click "Install"

---

## Quick Reference Card

### Before Testing
```bash
✅ dotnet restore  # First time only
✅ dotnet build    # Verify it compiles
```

### During Testing
```
✅ F5              # Start with debugger
✅ Ctrl+F5         # Start without debugger (faster)
✅ F9              # Add breakpoint
✅ F1 in-game      # Toggle debug overlay
```

### After Changes
```
✅ Ctrl+S          # Save file
✅ Ctrl+Shift+F5   # Restart game
```

### Performance Check
```
✅ Toggle F1       # Check FPS
✅ Ctrl+F5         # Run Release build
```

---

**Happy testing!** 🎮🐛

**Next:** See [GITHUB_SETUP.md](GITHUB_SETUP.md) to push your code!
