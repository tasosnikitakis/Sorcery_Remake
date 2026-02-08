"""
Analyze the door area of Spritesheet2.png with grid overlays
to find exact sprite boundaries.
"""

from PIL import Image, ImageDraw
import os

script_dir = os.path.dirname(os.path.abspath(__file__))
project_dir = os.path.dirname(script_dir)
spritesheet_path = os.path.join(project_dir, "Content", "Spritesheet2.png")
output_dir = os.path.join(project_dir, "assets", "images", "door_frames")
os.makedirs(output_dir, exist_ok=True)

img = Image.open(spritesheet_path)
print(f"Spritesheet size: {img.size}")

# Extract a wider area around the suspected door region
# Let's grab cols 23-30 (0-indexed 22-29) to have plenty of context
# X range: 22*24=528 to 30*24=720
# But let's also try different grid sizes

# First: extract a generous region and scale it up with grid lines
# Region: X=528 to 720, full height (192)
region_x1, region_y1 = 528, 0
region_x2, region_y2 = 720, 192
region = img.crop((region_x1, region_y1, region_x2, region_y2))

# Scale up 6x
scale = 6
big = region.resize((region.width * scale, region.height * scale), Image.NEAREST)
draw = ImageDraw.Draw(big)

# Draw 24px grid (scaled)
cell = 24
for x in range(0, region.width + 1, cell):
    sx = x * scale
    draw.line([(sx, 0), (sx, big.height)], fill=(255, 0, 0, 128), width=1)
for y in range(0, region.height + 1, cell):
    sy = y * scale
    draw.line([(0, sy), (big.width, sy)], fill=(255, 0, 0, 128), width=1)

# Also draw 16px grid in blue for comparison
for x in range(0, region.width + 1, 16):
    sx = x * scale
    draw.line([(sx, 0), (sx, big.height)], fill=(0, 0, 255, 128), width=1)
for y in range(0, region.height + 1, 16):
    sy = y * scale
    draw.line([(0, sy), (big.width, sy)], fill=(0, 0, 255, 128), width=1)

grid_path = os.path.join(output_dir, "door_area_grid_24_16.png")
big.save(grid_path)
print(f"Saved grid overlay (red=24px, blue=16px): {grid_path}")

# Also try with just the 24px grid, no 16px
big2 = region.resize((region.width * scale, region.height * scale), Image.NEAREST)
draw2 = ImageDraw.Draw(big2)
for x in range(0, region.width + 1, cell):
    sx = x * scale
    draw2.line([(sx, 0), (sx, big2.height)], fill=(255, 255, 0, 200), width=1)
for y in range(0, region.height + 1, cell):
    sy = y * scale
    draw2.line([(0, sy), (big2.width, sy)], fill=(255, 255, 0, 200), width=1)

# Add coordinate labels at top
for i, x in enumerate(range(0, region.width, cell)):
    col_num = (region_x1 + x) // cell + 1  # 1-indexed column
    # Just print to console
    print(f"  Grid col {i}: spritesheet col {col_num} (X={region_x1 + x})")

grid24_path = os.path.join(output_dir, "door_area_grid_24only.png")
big2.save(grid24_path)
print(f"Saved 24px grid overlay: {grid24_path}")

# Let's also look at pixel-level detail of where the door bars are
# The door sprites should have distinctive vertical bar patterns
# Let me scan the region for columns of repeated colored pixels

print("\n=== Scanning for door-like vertical patterns ===")
# Look for vertical stripes (same color in a column for multiple rows)
for x in range(region.width):
    col_pixels = [region.getpixel((x, y)) for y in range(region.height)]
    # Check if this column has long runs of the same color (>8 pixels)
    runs = []
    current_color = col_pixels[0]
    run_len = 1
    for y in range(1, len(col_pixels)):
        if col_pixels[y] == current_color:
            run_len += 1
        else:
            if run_len >= 8 and current_color != (0, 0, 0) and current_color != (0, 0, 0, 255):
                runs.append((current_color, run_len, y - run_len))
            current_color = col_pixels[y]
            run_len = 1
    if run_len >= 8 and current_color != (0, 0, 0) and current_color != (0, 0, 0, 255):
        runs.append((current_color, run_len, len(col_pixels) - run_len))

    if runs:
        abs_x = region_x1 + x
        for color, length, start_y in runs:
            if length >= 12:  # Only show long runs
                print(f"  X={abs_x} (region x={x}): {length}px run of {color[:3]} starting at Y={start_y}")

print("\nDone!")
