# First Run Checklist
**Sorcery+ Remake - Getting Started**

---

## ✅ Phase 1 Setup Complete!

Your project is ready to run. Here's what's been set up:

### 📦 Git Repository
- ✅ First commit created: `92e278d`
- ✅ 38 files committed (6,088 lines of code)
- ✅ .gitignore configured
- ⏳ **Ready to push to GitHub**

### 🎮 VS Code Configuration
- ✅ Debug configuration (`.vscode/launch.json`)
- ✅ Build tasks (`.vscode/tasks.json`)
- ✅ Press **F5** to run!

### 📚 Documentation
- ✅ [GITHUB_SETUP.md](GITHUB_SETUP.md) - Push to GitHub guide
- ✅ [VSCODE_TESTING.md](VSCODE_TESTING.md) - Testing guide
- ✅ [QUICKSTART.md](QUICKSTART.md) - 5-minute setup
- ✅ [README.md](README.md) - Project overview

---

## 🚀 First Run Steps

### Step 1: Restart VS Code

Since you just installed .NET, VS Code needs to be restarted to detect it:

```
1. Close VS Code completely (File → Exit)
2. Reopen VS Code
3. Open this folder (d:\sorcery+_remake)
```

### Step 2: Verify .NET Installation

In VS Code terminal (Ctrl + \`):

```bash
dotnet --version
```

**Expected output:**
```
10.0.0  # Or similar (you installed .NET 10)
```

**If it shows "command not found":**
- Restart your computer
- Or add .NET to PATH manually

### Step 3: Restore NuGet Packages

```bash
dotnet restore
```

**Expected output:**
```
Determining projects to restore...
Restored [project path]
```

### Step 4: Build the Project

```bash
dotnet build
```

**Expected output:**
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

**If you get errors:**
- Check the error message
- Most likely: Missing MonoGame templates
- Fix: `dotnet new install MonoGame.Templates.CSharp`

### Step 5: Run the Game!

**Option A: Press F5** (Easiest)
- Just press F5 in VS Code
- Game builds and launches automatically

**Option B: Terminal**
```bash
dotnet run
```

**Option C: Build script**
```bash
# Windows
build.bat

# Linux/macOS
./build.sh
```

---

## 🎯 What to Expect

When the game runs, you should see:

### Visual
- ✅ 640x400 window (black background)
- ✅ Simple test room (green floor, gray walls, blue ceiling)
- ✅ Player wizard sprite at center
- ✅ Yellow debug text in top-left (FPS, position, velocity)

### Controls
| Key | Action |
|-----|--------|
| **Arrow Keys** | Move player |
| **Up** | Thrust upward (fight gravity) |
| **F1** | Toggle debug overlay |
| **ESC** | Exit game |

### Expected Behavior
- Player starts at center of screen
- Gravity pulls player down
- Arrow keys apply forces (not instant movement)
- Player "floats" when you stop pressing keys (momentum decays)
- Sprite animates:
  - Idle when stationary
  - Walk right when moving right
  - Walk left when moving left (flipped sprite)

---

## 🐛 Troubleshooting

### .NET Not Found

**After installing .NET 10:**
1. Restart VS Code
2. Restart terminal in VS Code
3. Restart computer if needed

**To verify installation:**
```bash
# Windows
where dotnet

# Linux/macOS
which dotnet
```

### MonoGame Templates Missing

```bash
dotnet new install MonoGame.Templates.CSharp
```

### Spritesheet Not Loading

**Symptom:** Player appears as a magenta square

**Check:**
```bash
# Verify spritesheet exists
ls "assets/images/Amstrad CPC - Sorcery - Characters.png"
```

**Fix:** Spritesheet should already be in the assets folder. If missing, the game will use a fallback colored square.

### Build Errors

**Error:** `The type or namespace name 'Microsoft' could not be found`

**Fix:**
```bash
dotnet restore
dotnet build
```

**Error:** `Project file is incomplete`

**Fix:** The .csproj file should be correct. If you see this, check [SorceryRemake.csproj](SorceryRemake.csproj)

### Game Window Doesn't Open

**Check console output for errors:**
- Look in VS Code Debug Console (Ctrl+Shift+Y)
- Or Terminal output

**Common causes:**
- Graphics driver issue
- Missing OpenGL support
- Try running in Release mode: `dotnet run --configuration Release`

---

## 📊 Testing Checklist

After first successful run, test these:

### ✅ Movement Physics
- [ ] Player falls when idle (gravity works)
- [ ] Pressing Up makes player rise (thrust works)
- [ ] Player doesn't stop instantly (damping works)
- [ ] Arrow keys move player smoothly

### ✅ Animations
- [ ] Idle animation plays at start (gentle bob)
- [ ] Walk right animation plays when moving right
- [ ] Walk left animation plays when moving left
- [ ] Walk left sprite is flipped horizontally
- [ ] Animations loop smoothly

### ✅ Debug Info
- [ ] F1 toggles debug text on/off
- [ ] FPS shows ~60 (or your monitor refresh rate)
- [ ] Position updates as player moves
- [ ] Velocity shows current speed

### ✅ Boundaries
- [ ] Player can't go below screen bottom
- [ ] Player can't go past left/right edges
- [ ] Player can't go above screen top

---

## 🎨 First Tweaks to Try

Once the game runs, try these quick tests:

### 1. Change Gravity

```csharp
// Physics/PhysicsComponent.cs, line ~41
public float Gravity { get; set; } = 500f; // Changed from 300
```

**Save → Restart (Ctrl+Shift+F5) → Feel the difference!**

### 2. Change Animation Speed

```csharp
// Graphics/SpriteConfig.cs, line ~137
public const float PLAYER_IDLE_ANIMATION_SPEED = 0.05f; // Changed from 0.117
```

**Save → Restart → Watch faster animation!**

### 3. Change Player Color

```csharp
// Graphics/SpriteConfig.cs
// Use green wizard instead of yellow (Row 2 of spritesheet)
public static readonly Rectangle[] PLAYER_IDLE_FRONT = PLAYER_IDLE_FRONT_GREEN;
```

### 4. Change Window Size

```csharp
// Game1.cs, line ~67
private const int WINDOW_WIDTH = 960;   // Changed from 640
private const int WINDOW_HEIGHT = 600;  // Changed from 400
```

---

## 📝 Next Steps (After First Run)

### Immediate
1. **Push to GitHub** - See [GITHUB_SETUP.md](GITHUB_SETUP.md)
2. **Test all animations** - See [VSCODE_TESTING.md](VSCODE_TESTING.md)
3. **Verify sprite positions** - See [docs/AnimationReference.md](docs/AnimationReference.md)

### Short Term (Phase 1 Refinement)
- [ ] Fine-tune physics constants (gravity, damping)
- [ ] Verify animation frame positions are correct
- [ ] Adjust velocity threshold if needed
- [ ] Add any missing debug info

### Long Term (Phase 2+)
- [ ] Pixel-perfect collision detection
- [ ] Multi-room system
- [ ] Tile-based map editor
- [ ] Info panel UI (energy, items, timer)

---

## 🆘 Getting Help

### Documentation Files
- **[README.md](README.md)** - Project overview
- **[QUICKSTART.md](QUICKSTART.md)** - 5-minute setup
- **[docs/Phase1.md](docs/Phase1.md)** - Development log
- **[docs/PythonToCSharpMigration.md](docs/PythonToCSharpMigration.md)** - Python comparison

### If Stuck
1. Check error messages in terminal
2. Review [VSCODE_TESTING.md](VSCODE_TESTING.md) troubleshooting section
3. Verify .NET is installed: `dotnet --version`
4. Try clean rebuild:
   ```bash
   dotnet clean
   dotnet restore
   dotnet build
   ```

---

## 🎉 Success Criteria

You've successfully completed Phase 1 when:

✅ Game window opens (640x400)
✅ Player sprite visible and animated
✅ Arrow keys control movement
✅ Physics feels "floaty" (damping working)
✅ Debug overlay shows FPS and position
✅ No crashes or errors

**Once you confirm all of the above, you're ready for Phase 2!**

---

## Current Status

```
✅ Phase 1 Code: Complete
✅ Git Commit: Created
✅ Documentation: Complete
✅ VS Code Config: Ready
⏳ .NET SDK: Installed (restart VS Code)
⏳ First Run: Pending
⏳ GitHub Push: Pending
```

---

**Let's run the game!** 🎮

**Start:** Restart VS Code → Press F5 → Enjoy!
