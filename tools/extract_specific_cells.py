"""
Extract the specific cells the user identified for left door frames,
each at 10x scale for clear viewing.
Also extract with 2px padding trimmed on each side to see if that helps.
"""

from PIL import Image, ImageDraw
import os

script_dir = os.path.dirname(os.path.abspath(__file__))
project_dir = os.path.dirname(script_dir)
spritesheet_path = os.path.join(project_dir, "Content", "Spritesheet2.png")
output_dir = os.path.join(project_dir, "assets", "images", "door_frames")
os.makedirs(output_dir, exist_ok=True)

img = Image.open(spritesheet_path)
CELL = 24
SCALE = 10

# User specified: (row, col_in_section) where section = cols 25-32
# Col 1 of section = spritesheet col 25 (0-indexed: 24) -> X = 24*24 = 576
# Col 2 of section = spritesheet col 26 (0-indexed: 25) -> X = 25*24 = 600
# Rows are 1-indexed: Row 1 -> Y=0, Row 2 -> Y=24, etc.

frames = [
    ("Frame0_R2C26_closed",    600, 24),   # (2,2)
    ("Frame1_R1C26_opening1",  600,  0),   # (1,2)
    ("Frame2_R4C25_opening2",  576, 72),   # (4,1)
    ("Frame3_R3C25_open",      576, 48),   # (3,1)
]

# Create a side-by-side comparison at 10x
compare_w = len(frames) * (CELL * SCALE + 20) + 20
compare_h = CELL * SCALE + 40
compare = Image.new("RGB", (compare_w, compare_h), (30, 30, 30))
compare_draw = ImageDraw.Draw(compare)

for i, (name, x, y) in enumerate(frames):
    # Extract exact cell
    cell = img.crop((x, y, x + CELL, y + CELL))
    big = cell.resize((CELL * SCALE, CELL * SCALE), Image.NEAREST)

    # Save individual
    path = os.path.join(output_dir, f"{name}_10x.png")
    big.save(path)
    print(f"Saved: {name} from ({x},{y}) size {CELL}x{CELL}")

    # Place on comparison
    cx = 10 + i * (CELL * SCALE + 20)
    cy = 30
    compare.paste(big, (cx, cy))
    compare_draw.text((cx, 10), f"F{i}: ({x},{y})", fill=(255, 255, 0))

compare_path = os.path.join(output_dir, "left_door_frames_10x.png")
compare.save(compare_path)
print(f"\nSaved comparison: {compare_path}")

# Now also try extracting with different sizes to see if 24x24 is wrong
# Try 16x24, 20x24, and 24x24 from same starting positions
print("\n=== Testing different crop widths ===")
for width in [16, 20, 24, 28, 32]:
    row = Image.new("RGB", (width * SCALE * 4 + 50, 24 * SCALE + 30), (30, 30, 30))
    row_draw = ImageDraw.Draw(row)
    for i, (name, x, y) in enumerate(frames):
        cell = img.crop((x, y, x + width, y + CELL))
        big = cell.resize((width * SCALE, CELL * SCALE), Image.NEAREST)
        px = 10 + i * (width * SCALE + 10)
        row.paste(big, (px, 20))
    path = os.path.join(output_dir, f"width_test_{width}px.png")
    row.save(path)
    print(f"  Saved width {width}px test: {path}")

# Try shifting X by various offsets to find better alignment
print("\n=== Testing X offsets for Frame0 (R2,C26 area) ===")
# The bars at R2C26 (X=600) - let's try offsets from -12 to +12
offset_strip = Image.new("RGB", (25 * CELL * 3 + 20, CELL * 3 + 30), (30, 30, 30))
offset_draw = ImageDraw.Draw(offset_strip)
base_x, base_y = 600, 24
for i, offset in enumerate(range(-12, 13)):
    x = base_x + offset
    cell = img.crop((x, base_y, x + CELL, base_y + CELL))
    big = cell.resize((CELL * 3, CELL * 3), Image.NEAREST)
    px = 10 + i * (CELL * 3)
    offset_strip.paste(big, (px, 20))
    if offset % 4 == 0:
        offset_draw.text((px, 5), f"{offset:+d}", fill=(255, 255, 0))

offset_path = os.path.join(output_dir, "x_offset_test.png")
offset_strip.save(offset_path)
print(f"Saved X offset test: {offset_path}")
