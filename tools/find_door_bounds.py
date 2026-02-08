"""
Find actual sprite boundaries in the door area by scanning for non-black pixels.
This identifies where each door sprite actually sits regardless of grid alignment.
"""

from PIL import Image
import os

script_dir = os.path.dirname(os.path.abspath(__file__))
project_dir = os.path.dirname(script_dir)
spritesheet_path = os.path.join(project_dir, "Content", "Spritesheet2.png")
output_dir = os.path.join(project_dir, "assets", "images", "door_frames")
os.makedirs(output_dir, exist_ok=True)

img = Image.open(spritesheet_path)

def is_black(pixel):
    """Check if pixel is black (background)."""
    if len(pixel) == 4:
        return pixel[0] == 0 and pixel[1] == 0 and pixel[2] == 0
    return pixel[0] == 0 and pixel[1] == 0 and pixel[2] == 0

# Scan the area from X=520 to X=700, full height
# For each row of pixels, find non-black spans
# This will reveal sprite widths and positions

print("=== Horizontal non-black spans per pixel row ===")
print("Looking for vertical bar patterns (door sprites) in X=520-700\n")

# For each 24-pixel band (row), find the sprite bounding boxes
for band_row in range(8):
    y_start = band_row * 24
    y_end = y_start + 24

    print(f"\n--- Row {band_row+1} (Y={y_start}-{y_end-1}) ---")

    # Find the overall non-black bounding box for this row band in X=520-700
    min_x = 700
    max_x = 520
    min_y = y_end
    max_y = y_start

    for y in range(y_start, y_end):
        for x in range(520, 700):
            px = img.getpixel((x, y))
            if not is_black(px):
                min_x = min(min_x, x)
                max_x = max(max_x, x)
                min_y = min(min_y, y)
                max_y = max(max_y, y)

    if min_x <= max_x:
        print(f"  Overall non-black bounds: X=[{min_x}, {max_x}] Y=[{min_y}, {max_y}]")
        print(f"  Width: {max_x - min_x + 1}, Height: {max_y - min_y + 1}")

    # Now find individual sprites by looking for black vertical gaps
    # Scan each X column and check if it's entirely black in this band
    col_has_pixels = []
    for x in range(520, 700):
        has_pixel = False
        for y in range(y_start, y_end):
            if not is_black(img.getpixel((x, y))):
                has_pixel = True
                break
        col_has_pixels.append((x, has_pixel))

    # Find contiguous non-black spans
    spans = []
    span_start = None
    for x, has in col_has_pixels:
        if has and span_start is None:
            span_start = x
        elif not has and span_start is not None:
            spans.append((span_start, x - 1))
            span_start = None
    if span_start is not None:
        spans.append((span_start, col_has_pixels[-1][0]))

    for sx, ex in spans:
        w = ex - sx + 1
        print(f"  Sprite span: X=[{sx}, {ex}] width={w}px")

        # Find exact Y bounds for this span
        spy_min = y_end
        spy_max = y_start
        for y in range(y_start, y_end):
            for x in range(sx, ex + 1):
                if not is_black(img.getpixel((x, y))):
                    spy_min = min(spy_min, y)
                    spy_max = max(spy_max, y)
                    break
        print(f"    Y bounds: [{spy_min}, {spy_max}] height={spy_max - spy_min + 1}px")

print("\n\n=== Now extracting wider view for manual inspection ===")

# Extract rows 1-4 from X=528 to X=672 at 8x scale
for band_row in range(8):
    y_start = band_row * 24
    strip = img.crop((528, y_start, 672, y_start + 24))
    scale = 8
    big = strip.resize((strip.width * scale, strip.height * scale), Image.NEAREST)
    path = os.path.join(output_dir, f"row{band_row+1}_strip_8x.png")
    big.save(path)
    print(f"Saved row {band_row+1} strip: {path}")
