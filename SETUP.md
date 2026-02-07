# Sorcery+ Remake - Setup Guide

## Prerequisites Installation

### 1. Install .NET SDK 8.0
Download and install from: https://dotnet.microsoft.com/download/dotnet/8.0

Verify installation:
```bash
dotnet --version
```

### 2. Install MonoGame Templates
```bash
dotnet new install MonoGame.Templates.CSharp
```

### 3. Verify MonoGame Installation
```bash
dotnet new list | grep -i monogame
```

You should see:
- MonoGame Cross-Platform Desktop Application (mgdesktopgl)
- MonoGame Windows Desktop Application (mgwindowsdx)

## Project Creation (Manual Setup Complete)

The project has been manually created with the following structure:

```
sorcery+_remake/
├── SorceryRemake/              # Main game project
│   ├── Core/                   # ECS and core systems
│   ├── Physics/                # Flight physics implementation
│   ├── Graphics/               # Rendering and sprite management
│   ├── Content/                # MonoGame content pipeline
│   └── Game1.cs                # Main game entry point
├── assets/                     # Source assets
│   └── images/                 # Spritesheets
├── docs/                       # Documentation
│   └── Phase1.md               # Phase 1 progress
└── tools/                      # Asset extraction tools (future)
```

## Building the Project (After .NET Installation)

### Option 1: Create MonoGame Project (Recommended)
```bash
cd d:\sorcery+_remake
dotnet new mgdesktopgl -n SorceryRemake
```

Then copy the pre-created code files into the generated project.

### Option 2: Use Pre-Created Project Files
The code files have been created for you. After installing .NET SDK:

```bash
cd d:\sorcery+_remake\SorceryRemake
dotnet restore
dotnet build
dotnet run
```

## Next Steps

1. Install .NET SDK 8.0
2. Install MonoGame templates
3. Run the build commands above
4. The prototype will display a single room with the player character
5. Test flight physics with arrow keys

## Controls (Prototype)

- **Arrow Keys**: Move player
- **Up Arrow**: Thrust (counteracts gravity)
- **ESC**: Exit

## Technical Notes

### Mode 0 Graphics (160x200)
The original Amstrad CPC Mode 0 uses 160x200 resolution with wide pixels. The remake:
- Renders internally at 160x200
- Scales to 640x400 (4x) for modern displays
- Maintains 2:1 aspect ratio for pixel authenticity

### Physics Constants
Based on original game feel:
- Gravity: 300 pixels/second²
- Thrust: 400 pixels/second²
- Damping: 0.85 (15% friction per frame)
- Max velocity: 200 pixels/second
