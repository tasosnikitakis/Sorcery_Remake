# Sorcery.dsk Extraction Walkthrough
**Step-by-Step Guide for Extracting Tile Graphics**
**Tools**: CPCDiskXP, ConvImgCPC, Image Editor
**Time**: 1-2 hours

---

## 📋 Prerequisites Checklist

Before starting, verify you have:

- [ ] **Sorcery.dsk** file (original game disk image)
- [ ] **CPCDiskXP** installed (http://www.cpcwiki.eu/index.php/CPCDiskXP)
- [ ] **ConvImgCPC** downloaded (http://www.cpcwiki.eu/index.php/ConvImgCPC)
- [ ] **Image editor** (Paint, GIMP, Photoshop, or Paint.NET)
- [ ] **Extraction folder** created at `d:\sorcery+_remake\extraction\`

---

## 🗂️ Step 1: Set Up Folder Structure

Create the following folders:

```bash
d:\sorcery+_remake\
├── extraction\
│   ├── raw\              # Put Sorcery.dsk here
│   ├── bin\              # Extracted .BIN files go here
│   └── png\              # Converted PNG files go here
└── assets\
    └── images\
        └── extracted\    # Final organized tiles go here
```

**In Command Prompt or PowerShell**:
```powershell
cd d:\sorcery+_remake
mkdir extraction\raw
mkdir extraction\bin
mkdir extraction\png
mkdir assets\images\extracted
```

**Copy Sorcery.dsk to `extraction\raw\`**

---

## 🔍 Step 2: Open the Disk Image in CPCDiskXP

### Launch CPCDiskXP

1. **Run CPCDiskXP.exe**
2. **File → Open** (or press Ctrl+O)
3. **Navigate to**: `d:\sorcery+_remake\extraction\raw\`
4. **Select**: `Sorcery.dsk`
5. **Click Open**

### What You Should See

CPCDiskXP will display a **file list** from the disk. Look for files like:

```
SORCERY      (game executable)
SCREEN$      (screen data - likely contains graphics)
GFX          (graphics data)
TILES        (tile graphics)
DATA         (game data)
```

**Note**: File names vary by game. Look for:
- Files with **no extension** or **.BIN** extension
- Files that are **larger** than text files (2-16 KB)
- Files with names suggesting graphics (GFX, SCREEN, TILES, SPRITE)

---

## 📦 Step 3: Identify Graphics Files

### Method 1: File Size Analysis

Graphics files are typically **2-16 KB**:
- Small files (< 1 KB) = Code or data
- Medium files (2-16 KB) = **Graphics** ← Look for these
- Large files (> 16 KB) = Game code

### Method 2: File Name Hints

Look for files named:
- `SCREEN$` or `SCREEN` (screen data)
- `GFX` or `GRAPH` (graphics)
- `TILES` (tile data)
- `SPRITE` (sprite data)
- `CHARS` (character graphics)

### Method 3: Extract and Test

**When in doubt, extract everything** and test each file!

---

## 💾 Step 4: Extract Graphics Files

### For Each Suspected Graphics File:

1. **Click the file** in CPCDiskXP
2. **Right-click → Extract** (or File → Extract)
3. **Save to**: `d:\sorcery+_remake\extraction\bin\`
4. **Keep the original filename** (e.g., `SCREEN$`, `GFX`, etc.)

### Recommended Files to Extract

Extract ALL files between 2-16 KB, especially:
- Anything with a graphics-related name
- Files around 2 KB, 4 KB, 8 KB, or 16 KB (common sizes for CPC graphics)

**Example**:
```
SCREEN$ → extraction\bin\SCREEN$
GFX     → extraction\bin\GFX
TILES   → extraction\bin\TILES
```

---

## 🖼️ Step 5: Convert to PNG with ConvImgCPC

### Understanding Amstrad CPC Graphics Modes

Sorcery likely uses **Mode 0**:
- Resolution: 160x200 pixels
- Colors: 16 colors
- Most common for games

### Basic Conversion Command

Open **Command Prompt** in `d:\sorcery+_remake\extraction\`:

```bash
cd d:\sorcery+_remake\extraction

# Convert a file (replace SCREEN$ with actual filename)
ConvImgCPC.exe -mode 0 -in bin\SCREEN$ -out png\screen.png
```

### If That Doesn't Work, Try Different Parameters

```bash
# Try specifying palette
ConvImgCPC.exe -mode 0 -pal -in bin\SCREEN$ -out png\screen.png

# Try Mode 1 (if Mode 0 looks wrong)
ConvImgCPC.exe -mode 1 -in bin\SCREEN$ -out png\screen.png

# Specify width and height (for tile data)
ConvImgCPC.exe -mode 0 -width 8 -height 8 -in bin\TILES -out png\tiles.png
```

### Batch Convert All Files

```bash
# Convert all .BIN files at once
for %f in (bin\*) do ConvImgCPC.exe -mode 0 -in %f -out png\%~nf.png
```

---

## 🔍 Step 6: Examine the Extracted Graphics

### Open Each PNG in an Image Viewer

Navigate to `extraction\png\` and open each image.

### What to Look For

#### ✅ Good Signs (Correct Extraction)
- **Recognizable patterns** (walls, floors, tiles)
- **Distinct colors** (16-color CPC palette)
- **Repeating tile patterns** (8x8 or 16x16 grids)
- **Game screen layout** (if it's a full screen capture)

#### ❌ Bad Signs (Wrong Settings)
- **Random noise** (garbled pixels)
- **Incorrect colors** (too many colors or grayscale)
- **Stretched/squashed** (wrong aspect ratio)
- **Empty or black** (wrong file or offset)

### Common Issues and Fixes

**Issue**: Image looks garbled
**Fix**: Try different mode (`-mode 1` or `-mode 2`)

**Issue**: Image is shifted/offset
**Fix**: Graphics might have a header, try:
```bash
ConvImgCPC.exe -mode 0 -offset 128 -in bin\FILE -out png\file.png
```

**Issue**: Image is empty/black
**Fix**: File might not be graphics, try next file

---

## 🎨 Step 7: Identify Tile Graphics

### Look for These Patterns

Once you have PNGs, identify which file contains **tiles**:

#### Tiles Look Like:
- **Small repeated patterns** (8x8 or 16x16 pixels each)
- **Grid layout** (multiple tiles arranged in rows/columns)
- **Solid blocks, platforms, walls**
- **Background patterns**

#### Tiles Don't Look Like:
- Large sprites (characters, enemies) - these are 24x24
- Full screen layouts - these are complete rooms
- Text or UI elements

### Expected Tile Types

In Sorcery, you should find:
1. **Solid wall tiles** (gray/brown blocks)
2. **Platform tiles** (walkable surfaces)
3. **Background tiles** (decorative, non-solid)
4. **Ladder tiles** (if game has ladders)
5. **Hazard tiles** (spikes, deadly surfaces)

---

## ✂️ Step 8: Organize Tiles into a Tileset

### Option A: Tiles Already Organized

**If the extracted PNG already shows tiles in a grid**:

1. **Open in image editor**
2. **Verify tile size** (should be 8x8 pixels each)
3. **Count tiles**: How many rows/columns?
4. **Save as**: `assets\images\extracted\Tiles_Original.png`
5. **Done!** ✅

### Option B: Tiles Are Scattered

**If tiles are mixed with other graphics**:

1. **Open in image editor** (GIMP recommended for precision)
2. **Create new image**: 128x128 pixels (for 16x16 tile grid)
3. **Copy each 8x8 tile** to the new image in a grid:
   - Row 0: Wall tiles (solid blocks)
   - Row 1: Platform tiles
   - Row 2: Background tiles
   - etc.
4. **Save as**: `assets\images\extracted\Tiles_Organized.png`

### Tile Organization Format

Organize tiles like this:

```
+----+----+----+----+----+----+----+----+
|  0 |  1 |  2 |  3 |  4 |  5 |  6 |  7 |  Row 0: Walls
+----+----+----+----+----+----+----+----+
|  8 |  9 | 10 | 11 | 12 | 13 | 14 | 15 |  Row 1: Floors
+----+----+----+----+----+----+----+----+
| 16 | 17 | 18 | 19 | 20 | 21 | 22 | 23 |  Row 2: Platforms
+----+----+----+----+----+----+----+----+
| 24 | 25 | 26 | 27 | 28 | 29 | 30 | 31 |  Row 3: Decorations
+----+----+----+----+----+----+----+----+
```

**Size**: Each tile is 8x8 pixels
**Grid**: 8 tiles wide, N tiles tall (adjust as needed)
**Format**: PNG with transparency

---

## 🎮 Step 9: Alternative - Use WinAPE Emulator

**If ConvImgCPC doesn't work**, use the emulator method:

### Download WinAPE
- **URL**: http://www.winape.net/
- **Install and run**

### Load the Game
1. **File → Drive A: → Insert**
2. **Select**: `Sorcery.dsk`
3. **Type in emulator**: `CAT` (press Enter to see files)
4. **Type**: `RUN"SORCERY"` (or the game executable name)
5. **Press Enter**

### Capture Screenshots
1. **Play the game** until you see rooms with tiles
2. **Press Shift+F2** to take screenshot
3. **Screenshots saved to**: `WinAPE\Screenshots\`
4. **Open in image editor** and crop out individual tiles

**This method is slower but guaranteed to work!**

---

## ✅ Step 10: Verify Extraction Success

### Checklist

- [ ] You have at least one PNG with tile graphics
- [ ] Tiles are 8x8 pixels each
- [ ] Colors match CPC palette (16 colors)
- [ ] You can identify walls, floors, platforms
- [ ] Image is organized in a grid (or you organized it)

### Test the Tileset

Before using in game:

1. **Open in image editor**
2. **Zoom to 800%** to see individual pixels
3. **Count tile size**: Should be exactly 8x8 pixels
4. **Check transparency**: Background should be transparent or black
5. **Verify colors**: Should look authentic to Amstrad CPC

---

## 📂 Step 11: Prepare for Phase 2A

### Copy Final Tileset to Content Folder

```bash
# Copy organized tileset
copy assets\images\extracted\Tiles_Organized.png Content\Tiles.png
```

### Update Content.mgcb

Add the tileset to the MonoGame Content Pipeline:

1. **Open**: `Content\Content.mgcb` in text editor
2. **Add** (copy the Characters.png entry and modify):

```
#begin Tiles.png
/importer:TextureImporter
/processor:TextureProcessor
/processorParam:ColorKeyColor=255,0,255,255
/processorParam:ColorKeyEnabled=True
/processorParam:GenerateMipmaps=False
/processorParam:PremultiplyAlpha=True
/processorParam:ResizeToPowerOfTwo=False
/processorParam:MakeSquare=False
/processorParam:TextureFormat=Color
/build:Tiles.png
```

3. **Save** Content.mgcb

### Verify in Game

Quick test to verify tiles load:

```csharp
// In Game1.cs LoadContent(), after loading character spritesheet:
var testTiles = Content.Load<Texture2D>("Tiles");
System.Diagnostics.Debug.WriteLine($"Tiles loaded: {testTiles.Width}x{testTiles.Height}");
```

**Build and run** - check debug output for confirmation.

---

## 🚨 Troubleshooting Common Issues

### Issue: ConvImgCPC Not Found
**Solution**: Make sure ConvImgCPC.exe is in your PATH or use full path:
```bash
C:\Path\To\ConvImgCPC.exe -mode 0 -in bin\SCREEN$ -out png\screen.png
```

### Issue: Graphics Look Wrong
**Try these settings**:
```bash
# Different modes
-mode 0    # 160x200, 16 colors (most likely)
-mode 1    # 320x200, 4 colors
-mode 2    # 640x200, 2 colors

# With palette
-pal

# With offset (skip header)
-offset 128
-offset 256
```

### Issue: File Not Opening
**Solution**: File might be compressed or encrypted. Try:
1. Extract ALL files from .dsk
2. Look for files with `.GZ` or `.Z` extension (compressed)
3. Use 7-Zip to decompress if needed

### Issue: Can't Find Tiles
**Solution**: Try the WinAPE screenshot method instead (Step 9)

---

## 📊 What to Expect

### Successful Extraction Looks Like:

**Files Extracted**:
```
extraction\bin\
├── SCREEN$     (8 KB)
├── GFX         (4 KB)
└── TILES       (2 KB)

extraction\png\
├── screen.png  (shows game screen or tiles)
├── gfx.png     (shows graphics data)
└── tiles.png   (shows tile grid) ← THIS ONE!
```

**Tiles.png Contains**:
- Grid of 8x8 pixel tiles
- 16-color CPC palette
- Walls, floors, platforms visible
- Ready to use in Phase 2A

---

## 🎯 Success Criteria

You're ready for Phase 2A when:

✅ You have a PNG with tile graphics (8x8 each)
✅ Tiles are organized in a grid
✅ You can identify walls, floors, platforms
✅ File is copied to `Content\Tiles.png`
✅ Content.mgcb updated
✅ Game loads the tileset without errors

---

## 📞 If You Get Stuck

### Post Your Results

If extraction isn't working, provide:
1. **Screenshot of CPCDiskXP** showing file list
2. **File sizes** of extracted .BIN files
3. **Screenshot of converted PNG** (even if it looks wrong)
4. **ConvImgCPC command** you used

I'll help diagnose the issue!

### Fallback Plan

If extraction is too difficult:
1. **Use WinAPE screenshot method** (slower but works)
2. **Search online** for pre-extracted Sorcery tiles
3. **Use placeholder tiles** and extract real ones later

**Don't let extraction block Phase 2A development!**

---

## 🎉 Next Steps After Extraction

Once you have `Content\Tiles.png`:

1. **Create TileConfig.cs** - Define tile IDs and properties
2. **Create TileComponent.cs** - Render tiles
3. **Build test room** - Use tiles to create a simple room
4. **Test collision** - Verify player can stand on platforms

**Phase 2A begins!** 🚀

---

*Good luck with extraction! Report back with your results!*
