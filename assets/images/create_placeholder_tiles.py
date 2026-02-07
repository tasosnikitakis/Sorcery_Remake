#!/usr/bin/env python3
"""
Create placeholder tileset for Sorcery+ Remake Phase 2A
8x8 pixel tiles arranged in a grid
"""

from PIL import Image, ImageDraw

# Tile size
TILE_SIZE = 8
TILES_PER_ROW = 8
TILES_PER_COL = 8

# Output image size
IMG_WIDTH = TILES_PER_ROW * TILE_SIZE
IMG_HEIGHT = TILES_PER_COL * TILE_SIZE

# Create image
img = Image.new('RGB', (IMG_WIDTH, IMG_HEIGHT), color=(0, 0, 0))
draw = ImageDraw.Draw(img)

# Define tile colors (matching Amstrad CPC aesthetic)
tiles = {
    # Row 0: Solid walls
    0: (64, 64, 64),      # Dark gray solid wall
    1: (80, 80, 80),      # Medium gray wall
    2: (96, 96, 96),      # Light gray wall
    3: (70, 50, 30),      # Brown brick wall
    4: (90, 70, 50),      # Light brown wall
    5: (60, 60, 80),      # Blue-gray wall
    6: (80, 60, 60),      # Red-brown wall
    7: (50, 70, 50),      # Green-gray wall

    # Row 1: Floor tiles (walkable platforms)
    8: (100, 80, 60),     # Tan floor
    9: (90, 70, 50),      # Brown floor
    10: (110, 90, 70),    # Light tan floor
    11: (80, 60, 40),     # Dark brown floor
    12: (70, 70, 70),     # Gray stone floor
    13: (85, 85, 85),     # Light stone floor
    14: (60, 50, 40),     # Dark wood floor
    15: (95, 75, 55),     # Light wood floor

    # Row 2: Platforms (semi-solid)
    16: (120, 100, 80),   # Light platform
    17: (100, 80, 60),    # Medium platform
    18: (80, 60, 40),     # Dark platform
    19: (90, 90, 70),     # Yellow platform
    20: (70, 90, 70),     # Green platform
    21: (90, 70, 90),     # Purple platform
    22: (90, 80, 70),     # Olive platform
    23: (80, 80, 60),     # Khaki platform

    # Row 3: Background/decoration (non-solid)
    24: (0, 0, 0),        # Empty/air (black)
    25: (20, 20, 20),     # Dark background
    26: (30, 30, 40),     # Blue background
    27: (40, 30, 30),     # Red background
    28: (30, 40, 30),     # Green background
    29: (40, 40, 30),     # Yellow background
    30: (50, 40, 50),     # Purple background
    31: (40, 50, 50),     # Cyan background

    # Row 4: Ladders and special tiles
    32: (100, 90, 50),    # Ladder (yellow-brown)
    33: (110, 100, 60),   # Ladder variant
    34: (0, 80, 0),       # Green ladder
    35: (80, 0, 0),       # Red hazard
    36: (0, 0, 80),       # Blue water
    37: (100, 0, 100),    # Purple poison
    38: (128, 128, 0),    # Yellow warning
    39: (0, 128, 128),    # Cyan ice

    # Row 5: Decorative tiles
    40: (70, 60, 50),     # Brick pattern
    41: (85, 75, 65),     # Light brick
    42: (60, 55, 45),     # Dark brick
    43: (80, 80, 60),     # Stone pattern
    44: (65, 65, 55),     # Cobblestone
    45: (75, 70, 60),     # Rough stone
    46: (90, 85, 75),     # Smooth stone
    47: (70, 75, 70),     # Mossy stone

    # Row 6: More decorative
    48: (100, 50, 50),    # Red decoration
    49: (50, 100, 50),    # Green decoration
    50: (50, 50, 100),    # Blue decoration
    51: (100, 100, 50),   # Yellow decoration
    52: (100, 50, 100),   # Magenta decoration
    53: (50, 100, 100),   # Cyan decoration
    54: (128, 64, 0),     # Orange decoration
    55: (64, 128, 64),    # Lime decoration

    # Row 7: Special/reserved
    56: (255, 0, 255),    # Magenta (transparency marker)
    57: (128, 128, 128),  # Mid gray
    58: (160, 160, 160),  # Light gray
    59: (192, 192, 192),  # Very light gray
    60: (32, 32, 32),     # Very dark gray
    61: (255, 255, 255),  # White (rare)
    62: (200, 200, 200),  # Off-white
    63: (150, 150, 150),  # Silver
}

# Draw each tile
for tile_id, color in tiles.items():
    row = tile_id // TILES_PER_ROW
    col = tile_id % TILES_PER_ROW

    x = col * TILE_SIZE
    y = row * TILE_SIZE

    # Fill tile with color
    draw.rectangle([x, y, x + TILE_SIZE - 1, y + TILE_SIZE - 1], fill=color)

    # Add subtle border for visibility (optional)
    if tile_id != 24:  # Don't border the empty tile
        border_color = (
            max(0, color[0] - 20),
            max(0, color[1] - 20),
            max(0, color[2] - 20)
        )
        # Draw 1-pixel border
        draw.rectangle([x, y, x + TILE_SIZE - 1, y + TILE_SIZE - 1],
                      outline=border_color, width=1)

# Save
output_path = 'Tiles_Placeholder.png'
img.save(output_path)

print("=" * 60)
print("Placeholder Tileset Created!")
print("=" * 60)
print(f"File: {output_path}")
print(f"Size: {IMG_WIDTH}x{IMG_HEIGHT} pixels")
print(f"Tile size: {TILE_SIZE}x{TILE_SIZE} pixels")
print(f"Total tiles: {TILES_PER_ROW}x{TILES_PER_COL} = {TILES_PER_ROW * TILES_PER_COL}")
print()
print("Tile Map:")
print("  Row 0 (0-7):   Solid walls (gray, brown, various)")
print("  Row 1 (8-15):  Floor tiles (tan, brown, stone)")
print("  Row 2 (16-23): Platforms (light, various colors)")
print("  Row 3 (24-31): Background/air (black = empty)")
print("  Row 4 (32-39): Ladders and hazards")
print("  Row 5 (40-47): Decorative (brick, stone patterns)")
print("  Row 6 (48-55): Colored decorations")
print("  Row 7 (56-63): Special/reserved")
print()
print("Key tiles to use in first test room:")
print("  Tile 0:  Solid wall (dark gray)")
print("  Tile 8:  Floor (tan)")
print("  Tile 16: Platform (light)")
print("  Tile 24: Empty/air (black)")
print("=" * 60)
