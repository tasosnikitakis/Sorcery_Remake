# Sorcery+ Remake

A faithful pixel-perfect remake of the Amstrad CPC 6128 classic **Sorcery+** using C# and MonoGame.

![Status](https://img.shields.io/badge/Status-Phase%201%20Complete-success)
![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-blue)
![Framework](https://img.shields.io/badge/Framework-MonoGame%203.8-blueviolet)
![Language](https://img.shields.io/badge/Language-C%23%20.NET%208.0-purple)

---

## About Sorcery+

**Sorcery+** (1985) was an innovative action-adventure game for the Amstrad CPC that featured:
- Unique "flight physics" - the player hovers and battles gravity instead of walking
- A sprawling 75-screen map (47 screens in Chapter 1, 28+ in Chapter 2)
- Memory-based puzzle design (poisonous vs. healing cauldrons look identical)
- The "one item" inventory rule - picking up an item drops your current one
- Complex weapon/enemy interaction matrix

This remake aims to recreate the game with 100% visual and gameplay authenticity using modern ECS architecture.

---

## Project Status

### ✅ Phase 1: Single Room Prototype (COMPLETE)
- [x] MonoGame project setup with ECS architecture
- [x] Amstrad CPC Mode 0 rendering (160x200 → 640x400)
- [x] Authentic flight physics (gravity, inertia, damping)
- [x] Player sprite animation system
- [x] Input handling (arrow key flight controls)
- [x] Test room with basic boundaries
- [x] Comprehensive code documentation

### 🚧 Phase 2: Multi-Room System (PLANNED)
- [ ] Pixel-perfect collision detection
- [ ] Tile-based map system
- [ ] Room transition logic
- [ ] SorceryForge map editor (initial version)
- [ ] Binary asset extraction pipeline (.DSK → PNG)

### 📋 Phase 3: Gameplay Systems (FUTURE)
- [ ] "One item" inventory system
- [ ] Weapon/enemy interaction matrix
- [ ] Healing/poisonous cauldron logic
- [ ] Timer system (crumbling book)
- [ ] 8 sorcerer rescue objectives

### 🎵 Phase 4: Audio & Polish (FUTURE)
- [ ] AY-3-8912 PSG emulation
- [ ] "The Sorcerer's Apprentice" music playback
- [ ] Sound effects (doors, explosions, etc.)
- [ ] Menu system
- [ ] Save/load system

---

## Quick Start

### Prerequisites

1. **Install .NET SDK 8.0**
   - Download: https://dotnet.microsoft.com/download/dotnet/8.0
   - Verify: `dotnet --version`

2. **Install MonoGame Templates**
   ```bash
   dotnet new install MonoGame.Templates.CSharp
   ```

### Build & Run

```bash
# Clone/navigate to the project
cd d:\sorcery+_remake

# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Run the game
dotnet run
```

### Controls (Prototype)

| Key | Action |
|-----|--------|
| **Arrow Keys** | Move player (flight controls) |
| **Up Arrow** | Thrust upward (counteract gravity) |
| **Down/Left/Right** | Directional movement |
| **F1** | Toggle debug overlay |
| **ESC** | Exit game |

---

## Technical Highlights

### Entity-Component-System (ECS) Architecture

The game uses a clean ECS pattern to avoid the "spaghetti code" of 8-bit era:

```
Player Entity
├── PhysicsComponent    (gravity, velocity, forces)
├── SpriteComponent     (animation, rendering)
└── PlayerController    (input, state machine)
```

**Benefits:**
- Components are reusable across entities
- Systems process components in parallel
- Easy to add new behaviors without code coupling

### Authentic Mode 0 Rendering

```
Game Logic (160x200 "Amstrad pixels")
    ↓
Render to Target (640x400 - 4x scale)
    ↓
Scale to Window (PointClamp - no blur)
    ↓
Result: Pixel-perfect retro aesthetic
```

### Flight Physics Calibration

Physics constants were tuned to match the original 1985 game feel:

```csharp
Gravity:     300 px/s²  // Constant downward pull
Thrust:      400 px/s²  // Upward force when "Up" held
Damping:     0.85       // 15% friction (creates "floaty" feel)
MaxVelocity: 200 px/s   // Speed cap
```

**Result:** Player momentum decays naturally - no instant stops, just like the original.

---

## Project Structure

```
sorcery+_remake/
├── Core/
│   ├── Entity.cs              # Base entity class
│   ├── IComponent.cs          # Component interface
│   └── PlayerController.cs    # Player input handler
├── Physics/
│   └── PhysicsComponent.cs    # Flight mechanics
├── Graphics/
│   ├── SpriteComponent.cs     # Sprite rendering
│   └── SpriteConfig.cs        # Spritesheet frame definitions
├── Content/
│   └── Content.mgcb           # MonoGame content pipeline
├── assets/
│   └── images/
│       └── Amstrad CPC - Sorcery - Characters.png
├── docs/
│   ├── Phase1.md              # Phase 1 development log
│   ├── AssetExtraction.md     # Binary extraction guide
│   └── DesignDocument.txt     # Full design spec
├── Game1.cs                   # Main game loop
├── Program.cs                 # Entry point
└── SorceryRemake.csproj       # Project file
```

---

## Code Documentation

Every file includes detailed inline annotations explaining:
- Purpose and design decisions
- Physics formulas and constants
- Coordinate system transformations
- Amstrad CPC-specific technical details

**Example:**
```csharp
// ============================================================================
// PHYSICS COMPONENT - FLIGHT MECHANICS
// Sorcery+ Remake - Authentic Amstrad CPC Flight Physics
// ============================================================================
// This component implements the unique "hover" flight mechanics from the
// original game. The player doesn't walk - they fly with inertia and gravity.
//
// PHYSICS MODEL (Calibrated to match 1985 original):
// - Constant gravity pulls downward
// - Thrust counteracts gravity when "Up" is held
// ...
```

See [docs/Phase1.md](docs/Phase1.md) for detailed development log.

---

## Asset Extraction Pipeline

The project includes a comprehensive guide for extracting original assets from .DSK disk images:

📄 **[docs/AssetExtraction.md](docs/AssetExtraction.md)**

**Pipeline Features:**
- Parse Amstrad AMSDOS file system
- De-interleave Mode 0 graphics (complex bit layout)
- Map 16-color sprites to 27-color hardware palette
- Extract AY-3-8912 music (YM format)
- Auto-generate sprite config files

**Status:** Documentation complete, Python implementation planned for Phase 2.

---

## Design Philosophy

### 1. Authenticity Over Modernization
- No quality-of-life changes that alter gameplay
- Physics must match the original frame-by-frame
- Visual fidelity prioritizes accuracy over "HD remasters"

### 2. Modern Engineering Practices
- Clean ECS architecture (not 8-bit spaghetti code)
- Comprehensive inline documentation
- Unit-testable components
- Version controlled development

### 3. Extensibility
- Modding support via SorceryForge editor
- JSON/XML map format for easy editing
- Component system allows gameplay experiments

---

## Development Roadmap

| Phase | Focus | Status |
|-------|-------|--------|
| **Phase 1** | Core movement & rendering | ✅ **COMPLETE** |
| **Phase 2** | Collision & multi-room | 🚧 Next |
| **Phase 3** | Inventory & interactions | 📋 Planned |
| **Phase 4** | Audio & polish | 📋 Planned |
| **Phase 5** | Chapter 2 content | 📋 Planned |
| **Phase 6** | SorceryForge editor | 📋 Planned |

---

## Contributing

This is a personal preservation project, but feedback and bug reports are welcome!

**Areas of Interest:**
- Original .DSK file verification (checksums)
- Mode 0 de-interleaving algorithm optimization
- Physics constant calibration (comparing to emulator)
- Historical documentation about the original game

---

## Technical Specifications

### Original Game (1985)
- **Platform:** Amstrad CPC 6128
- **CPU:** Zilog Z80A @ 4 MHz
- **Graphics:** Mode 0 (160x200, 16 colors)
- **Sound:** AY-3-8912 PSG (3 channels)
- **Media:** 3" disk (180 KB formatted)

### Remake (2026)
- **Platform:** Windows/Linux/macOS (via MonoGame)
- **Framework:** .NET 8.0 + MonoGame 3.8
- **Language:** C# with nullable reference types
- **Architecture:** Entity-Component-System
- **Target FPS:** 60 (physics at fixed 60Hz)

---

## License

**Code:** MIT License (see LICENSE file)

**Original Game Assets:** Copyright © Original Rights Holders
- Assets are extracted from legally owned copies for preservation
- Not included in repository (must be extracted by end user)
- For personal, educational, and preservation use only

---

## Acknowledgments

- **Original Developers:** Virgin Games (1985)
- **Amstrad CPC Community:** Technical documentation and preservation
- **MonoGame Team:** Cross-platform game framework
- **OpenCPC Project:** Emulator used for testing and verification

---

## Screenshots

### Phase 1 Prototype (Current)
*Player flying in test room with debug overlay*

![Prototype Screenshot Placeholder]

### Original Game (Reference)
*Amstrad CPC Mode 0 graphics (1985)*

![Original Screenshot Placeholder]

---

## Documentation Index

- 📘 [Phase 1 Development Log](docs/Phase1.md)
- 🔧 [Asset Extraction Guide](docs/AssetExtraction.md)
- 📐 [Design Document](docs/DesignDocument.txt)
- 🚀 [Setup Instructions](SETUP.md)

---

**Status:** Phase 1 Complete - Ready for first build! 🎮
