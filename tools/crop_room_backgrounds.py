"""
Crop Room Backgrounds from WinAPE Emulator Screenshots
======================================================
Extracts the 320x144 game area from Amstrad CPC screenshots captured in WinAPE.

WinAPE screenshot layout (1152x816):
- Full image includes CPC border (dark blue)
- Content area: 960x600 at position (96, 123) = 320x200 native at 3x scale
- Game area: top 432 rows of content = 144 native rows
- HUD area: bottom 168 rows of content = 56 native rows

Usage:
    python crop_room_backgrounds.py [--input-dir DIR] [--output-dir DIR] [--file FILE]

Examples:
    python crop_room_backgrounds.py
    python crop_room_backgrounds.py --file screenshot.png --name "Stonehenge"
"""

import os
import sys
import argparse
from pathlib import Path

try:
    from PIL import Image
    import numpy as np
except ImportError:
    print("ERROR: Requires Pillow and numpy. Install with: pip install Pillow numpy")
    sys.exit(1)

# WinAPE screenshot crop coordinates (verified by pixel analysis)
CONTENT_LEFT = 96
CONTENT_TOP = 123
CONTENT_RIGHT = 1056
GAME_AREA_BOTTOM = 555  # 123 + 432 (144 * 3)
HUD_TOP = 555
HUD_BOTTOM = 723  # 123 + 600 (200 * 3)

NATIVE_WIDTH = 320
NATIVE_HEIGHT = 144


def extract_room_name_from_hud(img):
    """Try to extract the room name from the HUD text area."""
    # The HUD contains text like "you are in the wastelands."
    # We can't easily OCR this, but we return None for manual naming
    return None


def crop_game_area(screenshot_path):
    """Crop the game area from a WinAPE screenshot."""
    img = Image.open(screenshot_path)

    # Verify expected dimensions
    if img.size != (1152, 816):
        print(f"  WARNING: Expected 1152x816, got {img.size[0]}x{img.size[1]}")
        print(f"  Attempting to auto-detect content area...")
        return auto_detect_and_crop(img)

    # Crop game area at 3x scale
    game_area_3x = img.crop((CONTENT_LEFT, CONTENT_TOP, CONTENT_RIGHT, GAME_AREA_BOTTOM))

    # Downscale to native resolution
    game_area_1x = game_area_3x.resize((NATIVE_WIDTH, NATIVE_HEIGHT), Image.NEAREST)

    return game_area_1x


def auto_detect_and_crop(img):
    """Auto-detect content area for non-standard screenshot sizes."""
    pixels = np.array(img)
    h, w = pixels.shape[:2]

    # Find content boundaries by looking for non-border pixels
    border_color = pixels[5, 5, :3]  # Sample corner for border color

    # Find top
    for y in range(h):
        row = pixels[y, :, :3]
        non_border = np.any(np.abs(row.astype(int) - border_color.astype(int)) > 30, axis=1)
        if np.any(non_border):
            top = y
            break

    # Find bottom
    for y in range(h - 1, 0, -1):
        row = pixels[y, :, :3]
        non_border = np.any(np.abs(row.astype(int) - border_color.astype(int)) > 30, axis=1)
        if np.any(non_border):
            bottom = y + 1
            break

    # Find left
    for x in range(w):
        col = pixels[:, x, :3]
        non_border = np.any(np.abs(col.astype(int) - border_color.astype(int)) > 30, axis=1)
        if np.any(non_border):
            left = x
            break

    # Find right
    for x in range(w - 1, 0, -1):
        col = pixels[:, x, :3]
        non_border = np.any(np.abs(col.astype(int) - border_color.astype(int)) > 30, axis=1)
        if np.any(non_border):
            right = x + 1
            break

    content_w = right - left
    content_h = bottom - top
    print(f"  Detected content area: ({left},{top}) to ({right},{bottom}) = {content_w}x{content_h}")

    # Calculate game area (top 72% of content = 144/200)
    game_h = int(content_h * 144 / 200)
    game_area = img.crop((left, top, right, top + game_h))

    # Resize to native
    game_area_1x = game_area.resize((NATIVE_WIDTH, NATIVE_HEIGHT), Image.NEAREST)
    return game_area_1x


def sanitize_name(name):
    """Convert a name to a safe filename component."""
    return name.replace(" ", "").replace("'", "").replace(".", "")


def process_screenshot(screenshot_path, output_dir, room_name=None):
    """Process a single screenshot."""
    filename = Path(screenshot_path).stem

    print(f"Processing: {filename}")

    # Crop game area
    game_area = crop_game_area(screenshot_path)

    if game_area is None:
        print(f"  ERROR: Failed to crop game area")
        return None

    # Determine output name
    if room_name:
        safe_name = sanitize_name(room_name)
    else:
        safe_name = filename

    output_path = os.path.join(output_dir, f"RoomBG_{safe_name}.png")
    game_area.save(output_path)
    print(f"  Saved: {output_path} ({game_area.size[0]}x{game_area.size[1]})")

    return output_path


def main():
    parser = argparse.ArgumentParser(description="Crop room backgrounds from WinAPE screenshots")
    parser.add_argument("--input-dir", default=None, help="Directory containing screenshots")
    parser.add_argument("--output-dir", default=None, help="Output directory for cropped backgrounds")
    parser.add_argument("--file", default=None, help="Process a single screenshot file")
    parser.add_argument("--name", default=None, help="Room name (used with --file)")
    args = parser.parse_args()

    script_dir = Path(__file__).parent
    project_dir = script_dir.parent

    # Defaults
    input_dir = args.input_dir or str(project_dir / "assets" / "images")
    output_dir = args.output_dir or str(project_dir / "Content")

    os.makedirs(output_dir, exist_ok=True)

    if args.file:
        # Process single file
        process_screenshot(args.file, output_dir, args.name)
    else:
        # Process all WinAPE screenshots in input directory
        import glob
        screenshots = sorted(glob.glob(os.path.join(input_dir, "sorcery-uk-*.png")))

        if not screenshots:
            print(f"No screenshots found in {input_dir}")
            print("Looking for files matching: sorcery-uk-*.png")
            return

        print(f"Found {len(screenshots)} screenshots in {input_dir}")
        print(f"Output directory: {output_dir}\n")

        for screenshot in screenshots:
            process_screenshot(screenshot, output_dir)

        print(f"\nDone! Processed {len(screenshots)} screenshots.")
        print("NOTE: Rename output files to RoomBG_{RoomName}.png for the Content pipeline.")


if __name__ == "__main__":
    main()
