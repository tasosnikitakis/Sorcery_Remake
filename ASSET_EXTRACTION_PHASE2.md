# Asset Extraction Guide - Phase 2A (Tile Graphics)
**For**: Sorcery+ Remake - Room Building
**Source**: Original Sorcery.dsk (Amstrad CPC 6128)
**Last Updated**: February 7, 2026

---

## 🎯 What Assets Do We Need?

### For Phase 2A: Tile-Based Rooms

We need the following graphics from the original game:

#### ✅ Already Have
- **Character Spritesheet** (24x24 sprites)
  - Player wizard animations
  - Enemy sprites
  - File: `assets/images/Amstrad CPC - Sorcery - Characters.png`

#### ❌ Still Need
1. **Tile Graphics** (8x8 tiles)
   - Floor tiles
   - Wall tiles (solid blocks)
   - Platform tiles (walkable)
   - Decoration tiles (non-solid)
   - Hazard tiles (spikes, etc.)

2. **Room Backgrounds** (Optional)
   - Background graphics for atmosphere
   - Sky/ceiling patterns

3. **Object Sprites** (For Phase 3)
   - Items (keys, weapons, potions)
   - Doors and exits
   - Cauldrons (healing/poison)
   - Bone piles (enemy spawners)

---

## 🔍 Option 1: Online Resources (EASIEST)

Before extracting from the .dsk file, check if assets are already available online.

### Recommended Sources

#### 1. The Spriters Resource
**URL**: https://www.spriters-resource.com/

Search for:
- "Amstrad CPC Sorcery"
- "Sorcery tiles"
- "Sorcery sprites"

**If found**: Download PNG files directly, skip extraction!

#### 2. SMS Power! (Sega Master System version)
**URL**: https://www.smspower.org/

The game was ported to SMS - graphics might be similar and easier to extract.

#### 3. VGMaps.com
**URL**: https://vgmaps.com/

May have room maps that show tile layouts.

#### 4. Archive.org
**URL**: https://archive.org/

Search for "Sorcery Amstrad CPC" - may have sprite sheets uploaded by community.

---

## 🔧 Option 2: Extract from Sorcery.dsk (MANUAL)

If assets aren't available online, extract from the original disk image.

### Prerequisites

#### Required Tools

1. **CPCDiskXP** (Free)
   - **Download**: http://www.cpcwiki.eu/index.php/CPCDiskXP
   - **Purpose**: Browse and extract files from .dsk images
   - **Platform**: Windows (works with Wine on Linux/Mac)

2. **ConvImgCPC** (Free)
   - **Download**: http://www.cpcwiki.eu/index.php/ConvImgCPC
   - **Purpose**: Convert CPC graphics to PNG
   - **Platform**: Windows

3. **Alternative: ManageDSK** (Cross-platform)
   - **Download**: https://github.com/pulkomandy/cpctools
   - **Purpose**: Command-line .dsk manipulation
   - **Platform**: Windows, Linux, macOS

---

## 📋 Step-by-Step Extraction Process

### Step 1: Open the Disk Image

Using **CPCDiskXP**:

1. Launch CPCDiskXP
2. File → Open → Select `Sorcery.dsk`
3. You'll see a list of files on the disk

**Expected Files**:
- `SORCERY` or `GAME` (game executable)
- Graphics data files (might be named `GFX`, `TILES`, `SCREEN$`, etc.)
- Data files (might contain room layouts)

### Step 2: Identify Graphics Files

Graphics files might have extensions like:
- `.BIN` (binary data)
- `.SCR` (screen data)
- `.GFX` (graphics)
- Or no extension at all

**Tip**: Graphics files are usually:
- Larger than text files (several KB)
- Binary data (not readable as text)
- Named something obvious like `TILES` or `GFX`

### Step 3: Extract Graphics Files

1. **Select the graphics file** in CPCDiskXP
2. **Right-click → Extract**
3. **Save to**: `d:\sorcery+_remake\extraction\raw\`

Repeat for all graphics-related files.

### Step 4: Convert CPC Graphics to PNG

Amstrad CPC graphics are stored in a special format (Mode 0, Mode 1, or Mode 2).

#### Using ConvImgCPC:

```bash
# Basic conversion
ConvImgCPC.exe -mode 0 -in raw/TILES.BIN -out tiles.png

# If tiles are 8x8
ConvImgCPC.exe -mode 0 -width 8 -height 8 -in raw/TILES.BIN -out tiles.png

# Batch convert all files
for %f in (raw\*.BIN) do ConvImgCPC.exe -mode 0 -in %f -out %~nf.png
```

**Mode 0**: 160x200, 16 colors (most likely for Sorcery)
**Mode 1**: 320x200, 4 colors
**Mode 2**: 640x200, 2 colors

**Try Mode 0 first** - Sorcery used Mode 0.

---

## 🎨 Option 3: Use an Emulator (VISUAL METHOD)

If conversion tools don't work, use an emulator to capture screenshots.

### Using WinAPE Emulator

1. **Download WinAPE** (Best CPC emulator)
   - **URL**: http://www.winape.net/
   - **Platform**: Windows

2. **Load the Game**
   - File → Drive A: → Insert → Select `Sorcery.dsk`
   - Type `RUN"SORCERY` (or `CAT` to see file names)
   - Press Enter

3. **Capture Tile Graphics**
   - Play through the game to see different rooms
   - Press `Shift+F2` to take screenshots
   - Screenshots saved to `WinAPE/Screenshots/`

4. **Extract Tiles from Screenshots**
   - Open screenshots in image editor (GIMP, Photoshop, etc.)
   - Manually crop out 8x8 tile sections
   - Save as individual PNG files

**This method is tedious but guaranteed to work!**

---

## 📦 What We Actually Need for Phase 2A

### Minimum Viable Assets

To start Phase 2A, we only need a **basic tileset**:

#### Essential Tiles (8x8 pixels each)
1. **Solid Block** (gray/stone) - For walls/platforms
2. **Floor Tile** (different texture than block)
3. **Empty/Air** (transparent) - For background
4. **Decoration** (optional) - Visual variety

#### Recommended Starting Set
- 1 solid wall tile
- 1 platform tile
- 1 background tile
- That's it! We can expand later.

### Alternative: Create Placeholder Tiles

**If extraction is difficult**, we can temporarily use **colored rectangles**:

```
Solid Wall: Dark gray 8x8 square
Platform: Brown 8x8 square
Background: Black (transparent)
```

Then replace with real assets later.

---

## 🗂️ Organizing Extracted Assets

### Folder Structure

```
d:\sorcery+_remake\
├── assets\
│   ├── images\
│   │   ├── Amstrad CPC - Sorcery - Characters.png  (✅ Already have)
│   │   ├── Tiles.png                               (❌ Need to extract)
│   │   ├── Items.png                               (❌ Need for Phase 3)
│   │   └── Objects.png                             (❌ Need for Phase 3)
│   └── raw\                                         (Original .dsk files)
│       └── Sorcery.dsk
├── extraction\                                      (Temp folder for extraction)
│   ├── raw\                                         (Extracted .BIN files)
│   └── converted\                                   (Converted PNG files)
└── Content\
    └── Tiles.png                                    (Copy final PNG here)
```

### Tile Sheet Format

Organize tiles in a grid for easy access:

```
+---+---+---+---+---+---+---+---+
| 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |  Row 0: Wall tiles
+---+---+---+---+---+---+---+---+
| 8 | 9 | 10| 11| 12| 13| 14| 15|  Row 1: Floor tiles
+---+---+---+---+---+---+---+---+
| 16| 17| 18| 19| 20| 21| 22| 23|  Row 2: Platform tiles
+---+---+---+---+---+---+---+---+
```

**Size**: 64x64 pixels (8x8 tiles, each 8x8 pixels)
**Format**: PNG with transparency
**Color**: Original CPC palette (16 colors)

---

## 🎨 Amstrad CPC Color Palette

The original game used **Mode 0** with these colors:

```
Color 0:  Black       (0, 0, 0)
Color 1:  Blue        (0, 0, 128)
Color 2:  Bright Blue (0, 0, 255)
Color 3:  Red         (128, 0, 0)
Color 4:  Magenta     (128, 0, 128)
Color 5:  Mauve       (128, 0, 255)
Color 6:  Bright Red  (255, 0, 0)
Color 7:  Purple      (255, 0, 128)
Color 8:  Bright Magenta (255, 0, 255)
Color 9:  Green       (0, 128, 0)
Color 10: Cyan        (0, 128, 128)
Color 11: Sky Blue    (0, 128, 255)
Color 12: Yellow      (128, 128, 0)
Color 13: White       (128, 128, 128)
Color 14: Pastel Blue (128, 128, 255)
Color 15: Orange      (255, 128, 0)
Color 16: Pink        (255, 128, 128)
Color 17: Pastel Magenta (255, 128, 255)
Color 18: Bright Green (0, 255, 0)
Color 19: Sea Green   (0, 255, 128)
Color 20: Bright Cyan (0, 255, 255)
Color 21: Lime        (128, 255, 0)
Color 22: Pastel Green (128, 255, 128)
Color 23: Pastel Cyan (128, 255, 255)
Color 24: Bright Yellow (255, 255, 0)
Color 25: Pastel Yellow (255, 255, 128)
Color 26: Bright White (255, 255, 255)
```

Stick to these colors for authenticity!

---

## ⚡ Quick Start: Use Placeholder Tiles

Don't want to extract assets right now? **Start with placeholders**:

### Create Basic Tileset in Paint/GIMP

1. **Create new image**: 64x64 pixels, transparent background
2. **Draw 8x8 tiles**:
   - Row 0: Gray squares (walls)
   - Row 1: Brown squares (floors)
   - Row 2: Dark gray squares (platforms)
3. **Save as**: `assets/images/Tiles_Placeholder.png`
4. **Use in code** while developing
5. **Replace later** with real extracted tiles

**Advantage**: Start Phase 2A immediately, extract real tiles later!

---

## 🧪 Testing Extracted Tiles

### Verify Tile Extraction Success

1. **Open tile sheet in image viewer**
   - Tiles should be 8x8 pixels each
   - Should show distinct patterns (walls, floors, etc.)
   - Colors should match CPC palette

2. **Load in game**
   - Add to Content pipeline
   - Render a few tiles on screen
   - Verify they look correct at 3x scale (24x24)

3. **Build test room**
   - Use 2-3 different tile types
   - Create simple platform layout
   - Test if tiles align properly

---

## 📚 Additional Resources

### Amstrad CPC Technical Info
- **CPC Wiki**: http://www.cpcwiki.eu/
- **CPC Graphics Modes**: http://www.cpcwiki.eu/index.php/Video_modes
- **Mode 0 Format**: http://www.cpcwiki.eu/index.php/Standard_Palette

### Extraction Tools
- **CPCDiskXP**: http://www.cpcwiki.eu/index.php/CPCDiskXP
- **ConvImgCPC**: http://www.cpcwiki.eu/index.php/ConvImgCPC
- **CPCEC Emulator**: https://github.com/cpcitor/cpcec (Cross-platform)

### Community Help
- **CPC Wiki Forums**: http://www.cpcwiki.eu/forum/
- **Vintage Computing Forums**: Many retro enthusiasts willing to help!

---

## 🎯 Recommended Approach for Phase 2A

### Option A: Quick Start (Recommended)
1. ✅ **Use placeholder colored tiles** (2 hours)
2. ✅ **Build tile system and test rooms** (1-2 days)
3. ✅ **Extract real tiles later** when system works (1 day)
4. ✅ **Replace placeholders** with authentic graphics (1 hour)

**Total Time**: 3-4 days, working system on Day 1

### Option B: Authentic First
1. ❌ **Spend 1-2 days extracting tiles** from .dsk
2. ❌ **Then start building tile system**
3. ❌ **Risk getting stuck on extraction** before coding

**Total Time**: 3-5 days, no working system until Day 3+

### My Recommendation

**Start with Option A**: Get the tile system working with placeholders, then extract real tiles and drop them in. This way:
- ✅ You make progress immediately
- ✅ Tile system works even if extraction is hard
- ✅ Can replace tiles anytime without code changes
- ✅ Less frustrating development experience

---

## 🚀 Next Steps After Extraction

Once you have tiles (real or placeholder):

1. **Add to Content Pipeline**
   - Copy `Tiles.png` to `Content/` folder
   - Add to `Content.mgcb` (like we did with Characters.png)

2. **Create TileConfig.cs**
   - Define tile IDs (0 = wall, 1 = floor, etc.)
   - Map tile IDs to spritesheet positions
   - Set collision properties (solid/passthrough)

3. **Create TileComponent.cs**
   - Render tiles at correct positions
   - Scale 3x (8x8 → 24x24)

4. **Build First Test Room**
   - Create simple room layout
   - Test rendering
   - Verify collision (Phase 2B)

---

## ✅ Checklist Before Starting Phase 2A

- [ ] Decide: Placeholder tiles or real extraction?
- [ ] If placeholders: Create basic 64x64 tileset PNG
- [ ] If extraction: Download CPCDiskXP or WinAPE
- [ ] Extract/create Tiles.png
- [ ] Verify tiles are 8x8 pixels each
- [ ] Add Tiles.png to Content folder
- [ ] Ready to start coding TileComponent!

---

**Bottom Line**: You CAN extract from Sorcery.dsk, but I recommend **starting with placeholder tiles** to keep momentum. Extract authentic tiles later when the system works!

Would you like to:
1. **Start with placeholders** → Proceed to Phase 2A immediately ✅
2. **Extract real tiles first** → I'll guide you through .dsk extraction 🔧
3. **Search online first** → Look for pre-extracted sprites 🔍

Your choice! 🎮

---

*Generated: February 7, 2026*
*Next: Choose extraction method, then begin Phase 2A*
