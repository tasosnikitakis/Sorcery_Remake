"""
Generate Collision Grids from Room Background Images
=====================================================
Analyzes 320x144 room background PNGs and auto-detects collision areas.

Algorithm:
- Divides image into 40x18 grid of 8x8 pixel cells
- All-black cell (all pixels RGB < threshold) = empty (0)
- Any non-black pixel = solid (1)

Output:
- JSON collision grid per room (40x18 array, 0=empty, 1=solid)
- Visual debug overlay PNG (red tint = solid, green tint = empty)

Usage:
    python generate_collision_grid.py [--input FILE] [--output-dir DIR] [--threshold 10]
    python generate_collision_grid.py --all
"""

import os
import sys
import json
import argparse
from pathlib import Path

try:
    from PIL import Image, ImageDraw
    import numpy as np
except ImportError:
    print("ERROR: Requires Pillow and numpy. Install with: pip install Pillow numpy")
    sys.exit(1)

TILE_SIZE = 8
GRID_COLS = 40
GRID_ROWS = 18
EXPECTED_WIDTH = 320
EXPECTED_HEIGHT = 144


def generate_collision_grid(image_path, black_threshold=10):
    """
    Generate a collision grid from a room background image.

    Args:
        image_path: Path to a 320x144 room background PNG
        black_threshold: Pixels with all RGB values below this are considered black/empty

    Returns:
        2D list (18 rows x 40 cols) of 0 (empty) or 1 (solid)
    """
    img = Image.open(image_path)

    if img.size != (EXPECTED_WIDTH, EXPECTED_HEIGHT):
        print(f"  WARNING: Expected {EXPECTED_WIDTH}x{EXPECTED_HEIGHT}, got {img.size[0]}x{img.size[1]}")

    pixels = np.array(img.convert("RGB"))
    grid = []

    for row in range(GRID_ROWS):
        grid_row = []
        for col in range(GRID_COLS):
            # Extract 8x8 tile
            y_start = row * TILE_SIZE
            x_start = col * TILE_SIZE
            tile = pixels[y_start:y_start + TILE_SIZE, x_start:x_start + TILE_SIZE]

            # Check if all pixels are black (below threshold)
            is_all_black = np.all(tile[:, :, :3] < black_threshold)

            grid_row.append(0 if is_all_black else 1)
        grid.append(grid_row)

    return grid


def create_debug_overlay(image_path, grid, output_path):
    """Create a visual debug image showing the collision overlay."""
    img = Image.open(image_path).convert("RGBA")
    overlay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)

    for row in range(GRID_ROWS):
        for col in range(GRID_COLS):
            x = col * TILE_SIZE
            y = row * TILE_SIZE

            if grid[row][col] == 1:
                # Solid: red tint
                draw.rectangle([x, y, x + TILE_SIZE - 1, y + TILE_SIZE - 1],
                               fill=(255, 0, 0, 80))
            # Empty cells get no overlay (transparent)

    # Draw grid lines
    for row in range(GRID_ROWS + 1):
        y = row * TILE_SIZE
        draw.line([(0, y), (EXPECTED_WIDTH, y)], fill=(255, 255, 255, 40))
    for col in range(GRID_COLS + 1):
        x = col * TILE_SIZE
        draw.line([(x, 0), (x, EXPECTED_HEIGHT)], fill=(255, 255, 255, 40))

    # Composite
    result = Image.alpha_composite(img, overlay)
    result.save(output_path)


def save_collision_json(grid, room_id, output_path):
    """Save collision grid as JSON."""
    data = {
        "roomId": room_id,
        "width": GRID_COLS,
        "height": GRID_ROWS,
        "tileSize": TILE_SIZE,
        "collision": grid,
        "_legend": {
            "0": "empty (air/passthrough)",
            "1": "solid (blocks movement)"
        },
        "_note": "Auto-generated from room background. Edit manually to fix incorrect detections."
    }

    with open(output_path, "w") as f:
        json.dump(data, f, indent=2)


def print_collision_ascii(grid):
    """Print a compact ASCII representation of the collision grid."""
    for row in grid:
        line = ""
        for cell in row:
            line += "#" if cell == 1 else "."
        print(f"  {line}")


def process_room(image_path, output_dir, room_id=None, threshold=10):
    """Process a single room background image."""
    filename = Path(image_path).stem

    if room_id is None:
        # Extract room name from filename (e.g., RoomBG_Stonehenge -> stonehenge)
        room_id = filename.replace("RoomBG_", "").lower()

    print(f"\nProcessing: {filename} (room: {room_id})")

    # Generate collision grid
    grid = generate_collision_grid(image_path, threshold)

    # Count stats
    total = GRID_ROWS * GRID_COLS
    solid = sum(sum(row) for row in grid)
    empty = total - solid
    print(f"  Grid: {GRID_COLS}x{GRID_ROWS} = {total} cells")
    print(f"  Solid: {solid} ({100 * solid / total:.1f}%)")
    print(f"  Empty: {empty} ({100 * empty / total:.1f}%)")

    # Print ASCII preview
    print(f"  Collision map:")
    print_collision_ascii(grid)

    # Save JSON
    json_path = os.path.join(output_dir, f"collision_{room_id}.json")
    save_collision_json(grid, room_id, json_path)
    print(f"  JSON: {json_path}")

    # Save debug overlay
    debug_path = os.path.join(output_dir, f"collision_{room_id}_debug.png")
    create_debug_overlay(image_path, grid, debug_path)
    print(f"  Debug: {debug_path}")

    return grid


def main():
    parser = argparse.ArgumentParser(description="Generate collision grids from room backgrounds")
    parser.add_argument("--input", default=None, help="Single room background image to process")
    parser.add_argument("--output-dir", default=None, help="Output directory for collision data")
    parser.add_argument("--threshold", type=int, default=10,
                        help="Black threshold (pixels below this are empty, default: 10)")
    parser.add_argument("--all", action="store_true",
                        help="Process all RoomBG_*.png files in Content/")
    parser.add_argument("--room-id", default=None, help="Room ID (used with --input)")
    args = parser.parse_args()

    script_dir = Path(__file__).parent
    project_dir = script_dir.parent

    output_dir = args.output_dir or str(project_dir / "assets" / "data")
    os.makedirs(output_dir, exist_ok=True)

    if args.input:
        process_room(args.input, output_dir, args.room_id, args.threshold)
    elif args.all:
        import glob
        content_dir = project_dir / "Content"
        backgrounds = sorted(glob.glob(str(content_dir / "RoomBG_*.png")))

        if not backgrounds:
            print(f"No RoomBG_*.png files found in {content_dir}")
            return

        print(f"Found {len(backgrounds)} room backgrounds")
        for bg in backgrounds:
            process_room(bg, output_dir, threshold=args.threshold)

        print(f"\nDone! Processed {len(backgrounds)} rooms.")
    else:
        parser.print_help()
        print("\nUse --all to process all room backgrounds, or --input for a single file.")


if __name__ == "__main__":
    main()
