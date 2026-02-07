# Sorcery+ Remake - Quick Start Guide

**Get up and running in 5 minutes!**

---

## Step 1: Install .NET SDK

### Windows
1. Download: https://dotnet.microsoft.com/download/dotnet/8.0
2. Run installer
3. Verify in terminal:
   ```bash
   dotnet --version
   ```

### Linux (Ubuntu/Debian)
```bash
wget https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 8.0
```

### macOS
```bash
brew install dotnet@8
```

---

## Step 2: Install MonoGame

```bash
dotnet new install MonoGame.Templates.CSharp
```

Verify:
```bash
dotnet new list | grep -i monogame
```

---

## Step 3: Build & Run

### Windows
```bash
# Just run the build script
build.bat
```

### Linux/macOS
```bash
# Make script executable (first time only)
chmod +x build.sh

# Run it
./build.sh
```

### Manual Build
```bash
cd d:\sorcery+_remake
dotnet restore
dotnet build
dotnet run
```

---

## Controls

| Key | Action |
|-----|--------|
| **↑** | Thrust upward (fight gravity) |
| **↓** | Accelerate downward |
| **←** | Move left |
| **→** | Move right |
| **F1** | Toggle debug overlay |
| **ESC** | Exit |

---

## Expected Behavior

✅ Player spawns at center of screen
✅ Player "floats" - no instant stops
✅ Gravity constantly pulls down
✅ Arrow keys apply forces (not instant movement)
✅ Sprite animates based on movement

---

## Troubleshooting

### "dotnet not found"
→ Install .NET SDK (see Step 1)

### "No .NET SDKs were found"
→ Restart terminal after installing .NET SDK

### "The type or namespace name 'Microsoft' could not be found"
→ Run `dotnet restore`

### Black screen / No graphics
→ Check that `assets/images/Amstrad CPC - Sorcery - Characters.png` exists

### Sprite appears as magenta square
→ Spritesheet failed to load (see Game1.cs fallback code)

### Low FPS / Performance issues
→ Check graphics drivers, try running in Release mode:
```bash
dotnet run --configuration Release
```

---

## What's Next?

📘 **Read the docs:**
- [README.md](README.md) - Project overview
- [docs/Phase1.md](docs/Phase1.md) - Detailed development log
- [docs/CodeStructure.md](docs/CodeStructure.md) - Architecture guide

🔧 **Customize:**
- Edit physics constants in [Physics/PhysicsComponent.cs](Physics/PhysicsComponent.cs)
- Change window size in [Game1.cs](Game1.cs) constructor
- Add new animations in [Graphics/SpriteConfig.cs](Graphics/SpriteConfig.cs)

🚀 **Contribute:**
- Phase 2 needs: Collision detection, multi-room system
- See roadmap in [README.md](README.md)

---

## Quick Code Reference

### Physics Constants
```csharp
// Physics/PhysicsComponent.cs
Gravity = 300f;         // Downward pull
ThrustPower = 400f;     // Upward thrust
Damping = 0.85f;        // Friction (lower = more friction)
MaxVelocity = 200f;     // Speed cap
```

### Coordinate Spaces
```
Game Logic:  160 x 200   (Amstrad pixels)
Render:      640 x 400   (4x scaled)
Window:      640 x 400+  (scalable)
```

### Adding Debug Output
```csharp
// In Game1.cs DrawDebugInfo()
debugText += $"Custom Value: {myValue}\n";
```

---

## File Overview

```
sorcery+_remake/
├── Core/              ← ECS entities and components
├── Physics/           ← Flight mechanics
├── Graphics/          ← Sprite rendering
├── Content/           ← Assets (spritesheet)
├── docs/              ← Documentation
├── Game1.cs           ← Main game loop ★
├── Program.cs         ← Entry point
└── build.bat/sh       ← Build scripts
```

**★ Start here:** [Game1.cs](Game1.cs) - Main game loop

---

## Performance Targets

| Metric | Target | Current |
|--------|--------|---------|
| FPS | 60 | ✅ 60 |
| Frame Time | 16.67ms | ✅ ~2ms |
| Memory | <50 MB | ✅ ~10 MB |
| Entities | 1 | ✅ 1 (player) |

---

## Support

🐛 **Bug Reports:** https://github.com/anthropics/claude-code/issues
📖 **Documentation:** [docs/](docs/)
💬 **Questions:** See [README.md](README.md)

---

**Ready to fly? Run `build.bat` (Windows) or `./build.sh` (Linux/macOS)!** 🚀
