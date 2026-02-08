"""
Extract door frames from Spritesheet2.png and save as individual PNGs.
Also creates a combined door spritesheet for easy loading.

Spritesheet2.png: 1152x192 pixels
Assumed grid: 48 cols x 8 rows at 24x24 per cell

Left-opening door frames (user specified from section_cols_25_to_32):
  Frame 0 (closed):       row 2, col 2 of section = col 26 overall
  Frame 1 (slightly open): row 1, col 2 of section = col 26 overall
  Frame 2 (more open):    row 4, col 1 of section = col 25 overall
  Frame 3 (fully open):   row 3, col 1 of section = col 25 overall

Grid coords (0-indexed): col 24=col25, col 25=col26
"""

from PIL import Image
import os

# Open spritesheet
script_dir = os.path.dirname(os.path.abspath(__file__))
project_dir = os.path.dirname(script_dir)
spritesheet_path = os.path.join(project_dir, "Content", "Spritesheet2.png")
output_dir = os.path.join(project_dir, "assets", "images", "door_frames")

os.makedirs(output_dir, exist_ok=True)

img = Image.open(spritesheet_path)
print(f"Spritesheet size: {img.size}")  # Should be 1152x192

CELL_W = 24
CELL_H = 24

# Left door frames - user specified coordinates (1-indexed row,col within section_cols_25_to_32)
# Section starts at col 25 (0-indexed: col 24), so:
# col 1 of section = overall col index 24 -> X = 24*24 = 576
# col 2 of section = overall col index 25 -> X = 25*24 = 600
# row 1 = Y=0, row 2 = Y=24, row 3 = Y=48, row 4 = Y=72

left_door_frames = [
    ("left_frame0_closed",    600, 24),   # (2,2): row 2, col 2 of section
    ("left_frame1_opening1",  600,  0),   # (1,2): row 1, col 2 of section
    ("left_frame2_opening2",  576, 72),   # (4,1): row 4, col 1 of section
    ("left_frame3_open",      576, 48),   # (3,1): row 3, col 1 of section
]

# Also extract the surrounding area to see what's around these frames
print("\n=== Extracting left door frames ===")
for name, x, y in left_door_frames:
    # Extract exact 24x24 cell
    frame = img.crop((x, y, x + CELL_W, y + CELL_H))
    path = os.path.join(output_dir, f"{name}.png")
    frame.save(path)
    print(f"  {name}: ({x}, {y}) -> {path}")

    # Also save a larger context view (with 8px padding around)
    pad = 8
    cx = max(0, x - pad)
    cy = max(0, y - pad)
    cx2 = min(img.width, x + CELL_W + pad)
    cy2 = min(img.height, y + CELL_H + pad)
    context = img.crop((cx, cy, cx2, cy2))
    context_path = os.path.join(output_dir, f"{name}_context.png")
    context.save(context_path)

# Now let's also look at the area around cols 25-27 more carefully
# Extract a strip of cols 24-27 (0-indexed), all 8 rows
strip_x = 24 * CELL_W  # col 25 (0-indexed 24) = 576
strip_w = 4 * CELL_W   # 4 columns wide = 96
strip = img.crop((strip_x, 0, strip_x + strip_w, img.height))
strip_path = os.path.join(output_dir, "cols_25_to_28_strip.png")
strip.save(strip_path)
print(f"\nSaved strip of cols 25-28: {strip_path}")

# Create a combined door frames sheet (4 frames in a row, 24x24 each = 96x24)
combined = Image.new("RGBA", (4 * CELL_W, CELL_H))
for i, (name, x, y) in enumerate(left_door_frames):
    frame = img.crop((x, y, x + CELL_W, y + CELL_H))
    combined.paste(frame, (i * CELL_W, 0))

combined_path = os.path.join(output_dir, "left_door_combined.png")
combined.save(combined_path)
print(f"Saved combined left door sheet: {combined_path}")

# Scale up 4x for easier visual inspection
scale = 4
combined_big = combined.resize((combined.width * scale, combined.height * scale), Image.NEAREST)
combined_big_path = os.path.join(output_dir, "left_door_combined_4x.png")
combined_big.save(combined_big_path)
print(f"Saved 4x scaled preview: {combined_big_path}")

print("\nDone! Check the extracted frames to see if they look correct.")
print("If they contain wrong pixels, we'll adjust the crop coordinates.")
