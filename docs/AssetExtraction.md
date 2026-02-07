# Asset Extraction Pipeline Guide
**Sorcery+ Remake - Binary Asset Extraction from .DSK Images**

---

## Overview

This document provides a walkthrough for implementing the asset extraction pipeline described in Design Document Section 2.2. The goal is to extract graphics, tiles, and audio from the original Amstrad CPC `.DSK` disk image files.

---

## Phase 1: Prerequisites

### Required Tools

1. **Python 3.8+** - For extraction scripts
2. **AmstradDSKExplorer** - Python library for parsing .DSK files
   ```bash
   pip install amstrad-disk-tools
   ```

3. **Pillow (PIL)** - Python image library
   ```bash
   pip install Pillow
   ```

4. **NumPy** - For pixel data manipulation
   ```bash
   pip install numpy
   ```

### Required Source Files

- **Sorcery+.dsk** - Original Amstrad CPC 6128 disk image
  - Available from: [Legal source needed - personal backup or authorized archive]
  - SHA-256 hash: [To be determined for verification]

---

## Phase 2: Understanding Amstrad CPC File Structure

### AMSDOS File System

The Amstrad uses a custom file system with:
- **Header**: 128-byte metadata block (type, load address, length)
- **Headerless Files**: Raw binary data with no metadata

Sorcery+ uses **headerless files** extensively, requiring manual identification.

### Disk Layout Analysis

1. **Mount the .DSK file** using an emulator (OpenCPC, Arnold)
2. **Catalog the disk**:
   ```
   |DIR
   ```
   Expected files:
   - `SORCERY.BIN` - Main executable
   - `SPRITES.DAT` - Graphics data (headerless)
   - `TILES.DAT` - Background tiles (headerless)
   - `MUSIC.YM` - AY-3-8912 music data

3. **Locate binary offsets** using a hex editor (HxD, 010 Editor)

---

## Phase 3: Graphics Extraction (Mode 0 Decoding)

### Mode 0 Pixel Format

Amstrad CPC Mode 0 has complex pixel storage:
- **Resolution**: 160x200 pixels
- **Colors**: 16 simultaneous from 27-color palette
- **Bit Layout**: Pixels are **interleaved** across 4 bytes

#### Interleaving Pattern

Each pixel requires 4 bits (16 colors = 2^4). These bits are scattered:

```
Byte 0: [P1.b3] [P0.b3] [P1.b1] [P0.b1] [P1.b2] [P0.b2] [P1.b0] [P0.b0]
Byte 1: [P3.b3] [P2.b3] [P3.b1] [P2.b1] [P3.b2] [P2.b2] [P3.b0] [P2.b0]
...
```

Where `P0` = Pixel 0, `b0` = bit 0 of color index.

### De-interleaving Algorithm (Python)

```python
def decode_mode0_sprite(data, width, height):
    """
    Decode Amstrad Mode 0 sprite data to RGBA image.

    Args:
        data: bytes - Raw binary sprite data
        width: int - Sprite width in pixels (must be multiple of 2)
        height: int - Sprite height in pixels

    Returns:
        PIL.Image - Decoded RGBA image
    """
    from PIL import Image
    import numpy as np

    # Amstrad CPC 27-color palette (Hardware colors &00-&1A)
    # This is a standard palette - may need adjustment per game
    CPC_PALETTE = [
        (0x00, 0x00, 0x00),  # &00 - Black
        (0x00, 0x00, 0x80),  # &01 - Blue
        (0x00, 0x00, 0xFF),  # &02 - Bright Blue
        (0x80, 0x00, 0x00),  # &03 - Red
        (0x80, 0x00, 0x80),  # &04 - Magenta
        (0x80, 0x00, 0xFF),  # &05 - Mauve
        (0xFF, 0x00, 0x00),  # &06 - Bright Red
        (0xFF, 0x00, 0x80),  # &07 - Purple
        (0xFF, 0x00, 0xFF),  # &08 - Bright Magenta
        (0x00, 0x80, 0x00),  # &09 - Green
        (0x00, 0x80, 0x80),  # &0A - Cyan
        (0x00, 0x80, 0xFF),  # &0B - Sky Blue
        (0x80, 0x80, 0x00),  # &0C - Yellow
        (0x80, 0x80, 0x80),  # &0D - White
        (0x80, 0x80, 0xFF),  # &0E - Pastel Blue
        (0xFF, 0x80, 0x00),  # &0F - Orange
        (0xFF, 0x80, 0x80),  # &10 - Pink
        (0xFF, 0x80, 0xFF),  # &11 - Pastel Magenta
        (0x00, 0xFF, 0x00),  # &12 - Bright Green
        (0x00, 0xFF, 0x80),  # &13 - Sea Green
        (0x00, 0xFF, 0xFF),  # &14 - Bright Cyan
        (0x80, 0xFF, 0x00),  # &15 - Lime
        (0x80, 0xFF, 0x80),  # &16 - Pastel Green
        (0x80, 0xFF, 0xFF),  # &17 - Pastel Cyan
        (0xFF, 0xFF, 0x00),  # &18 - Bright Yellow
        (0xFF, 0xFF, 0x80),  # &19 - Pastel Yellow
        (0xFF, 0xFF, 0xFF),  # &1A - Bright White
    ]

    pixels = np.zeros((height, width), dtype=np.uint8)

    byte_idx = 0
    for y in range(height):
        for x in range(0, width, 2):  # Process 2 pixels at a time
            if byte_idx >= len(data):
                break

            byte_val = data[byte_idx]
            byte_idx += 1

            # Extract 2 pixels from interleaved byte
            # (Simplified - actual interleaving is more complex)
            pixel0 = ((byte_val & 0x80) >> 7) | \
                     ((byte_val & 0x20) >> 4) | \
                     ((byte_val & 0x08) >> 1) | \
                     ((byte_val & 0x02) << 2)

            pixel1 = ((byte_val & 0x40) >> 6) | \
                     ((byte_val & 0x10) >> 3) | \
                     ((byte_val & 0x04) >> 0) | \
                     ((byte_val & 0x01) << 3)

            pixels[y, x] = pixel0
            pixels[y, x + 1] = pixel1

    # Map palette indices to RGB
    img = Image.new('RGB', (width, height))
    for y in range(height):
        for x in range(width):
            color_idx = pixels[y, x] % len(CPC_PALETTE)
            img.putpixel((x, y), CPC_PALETTE[color_idx])

    return img
```

**Note:** The above is a **simplified** de-interleaving. Full Mode 0 decoding requires analyzing the actual memory layout used by Sorcery+. Use the OpenCPC emulator with a memory dump to verify the pattern.

---

## Phase 4: Extraction Workflow

### Step-by-Step Process

1. **Extract .DSK to Raw Files**
   ```python
   from amstrad_disk_tools import DiskImage

   disk = DiskImage.load('Sorcery+.dsk')
   files = disk.list_files()

   for file in files:
       data = disk.read_file(file.name)
       with open(f'extracted/{file.name}', 'wb') as f:
           f.write(data)
   ```

2. **Identify Sprite Data Boundaries**
   - Open `SPRITES.DAT` in hex editor
   - Locate repeating patterns (sprite data is organized sequentially)
   - Estimate sprite size (likely 16x16 pixels = 128 bytes in Mode 0)

3. **Batch Extract Sprites**
   ```python
   SPRITE_WIDTH = 16
   SPRITE_HEIGHT = 16
   SPRITE_SIZE = 128  # Bytes per sprite (Mode 0)

   with open('extracted/SPRITES.DAT', 'rb') as f:
       sprite_data = f.read()

   sprite_count = len(sprite_data) // SPRITE_SIZE

   for i in range(sprite_count):
       offset = i * SPRITE_SIZE
       sprite_bytes = sprite_data[offset:offset + SPRITE_SIZE]

       img = decode_mode0_sprite(sprite_bytes, SPRITE_WIDTH, SPRITE_HEIGHT)
       img.save(f'sprites/sprite_{i:03d}.png')
   ```

4. **Assemble Spritesheet**
   ```python
   from PIL import Image

   # Arrange sprites in a grid (e.g., 16 sprites per row)
   SPRITES_PER_ROW = 16
   ROW_COUNT = (sprite_count + SPRITES_PER_ROW - 1) // SPRITES_PER_ROW

   sheet = Image.new('RGB',
                     (SPRITE_WIDTH * SPRITES_PER_ROW,
                      SPRITE_HEIGHT * ROW_COUNT))

   for i in range(sprite_count):
       sprite = Image.open(f'sprites/sprite_{i:03d}.png')
       x = (i % SPRITES_PER_ROW) * SPRITE_WIDTH
       y = (i // SPRITES_PER_ROW) * SPRITE_HEIGHT
       sheet.paste(sprite, (x, y))

   sheet.save('assets/images/Sorcery-Spritesheet.png')
   ```

5. **Generate SpriteConfig.cs**
   ```python
   # Auto-generate C# sprite definitions
   with open('Graphics/SpriteConfig.cs', 'w') as f:
       f.write('// Auto-generated from asset extraction\n')
       f.write('public static class SpriteConfig {\n')

       for i in range(sprite_count):
           x = (i % SPRITES_PER_ROW) * SPRITE_WIDTH
           y = (i // SPRITES_PER_ROW) * SPRITE_HEIGHT

           f.write(f'    public static readonly Rectangle SPRITE_{i} = ')
           f.write(f'new Rectangle({x}, {y}, {SPRITE_WIDTH}, {SPRITE_HEIGHT});\n')

       f.write('}\n')
   ```

---

## Phase 5: Audio Extraction (AY-3-8912 PSG)

### YM File Format

The AY-3-8912 sound chip is best emulated by extracting raw register data:

1. **Record YM Stream** using an emulator
   - Use OpenCPC with YM recording enabled
   - Play the game and record music
   - Export to `.YM` file format

2. **Integrate YM Player**
   - Use a C# YM player library (e.g., `AyumiSharp`)
   - Load `.YM` file into MonoGame audio system
   - Play back with authentic PSG emulation

**Alternative (Quick & Dirty):**
- Export to WAV using an Amstrad audio plugin
- Import WAV into MonoGame (loses authenticity but simpler)

---

## Phase 6: Verification

### Quality Checks

✅ **Sprite Visual Comparison**
- Run extracted sprites side-by-side with emulator screenshot
- Verify colors match (palette accuracy)
- Check for artifacts (incorrect de-interleaving)

✅ **Coordinate Verification**
- Place sprites in test room
- Compare positioning to original game (use emulator memory viewer)

✅ **Animation Frame Order**
- Play animations in sequence
- Match timing to original (12.5 FPS for most animations)

---

## Troubleshooting

### Common Issues

| Problem | Cause | Solution |
|---------|-------|----------|
| Colors wrong | Incorrect palette mapping | Verify palette indices against hardware manual |
| Sprites garbled | Wrong de-interleaving | Check byte order, test with known sprite |
| Missing sprites | Incorrect file boundaries | Dump full memory in emulator to find data |
| Aspect ratio wrong | Mode 0 pixel width not handled | Sprites are 2:1 aspect (wide pixels) |

---

## Future Automation

**Goal:** One-click extraction pipeline

```bash
python extract_assets.py --dsk Sorcery+.dsk --output assets/
```

**Script Workflow:**
1. Parse .DSK file system
2. Identify sprite/tile data by signature
3. Auto-detect sprite dimensions
4. De-interleave Mode 0 graphics
5. Map to hardware palette
6. Generate spritesheet + C# config
7. Extract YM music data
8. Validate output

**Status:** ⏳ To be implemented in Phase 2

---

## Legal Notice

⚠️ **Important:** Only extract assets from legally obtained copies of Sorcery+ that you personally own. This extraction is for preservation, education, and personal use only. Do not distribute extracted assets without proper authorization from copyright holders.

---

## References

- [Amstrad CPC Graphics Modes](http://www.cpcwiki.eu/index.php/Video_modes)
- [Mode 0 Pixel Layout](http://www.grimware.org/doku.php/documentations/devices/gatearray)
- [AY-3-8912 Sound Chip](http://www.cpcwiki.eu/index.php/PSG)
- [AMSDOS File System](http://www.cpcwiki.eu/index.php/AMSDOS)
- [YM File Format Specification](http://leonard.oxg.free.fr/ymformat.html)

---

**Next Steps:**
1. Acquire legal .DSK image
2. Implement Python extraction script
3. Verify extracted sprites against emulator
4. Auto-generate SpriteConfig.cs from extraction
5. Replace prototype spritesheet with extracted assets
