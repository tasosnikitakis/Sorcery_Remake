#!/usr/bin/env python3
"""
Convert Amstrad CPC Character/Tile data to PNG
Character sets are typically 8x8 tiles, 8 bytes per character
"""

from PIL import Image
import sys
import os

# Amstrad CPC Mode 0 Palette
CPC_PALETTE = [
    (0, 0, 0),         # 0: Black
    (0, 0, 128),       # 1: Blue
    (0, 0, 255),       # 2: Bright Blue
    (128, 0, 0),       # 3: Red
    (128, 0, 128),     # 4: Magenta
    (128, 0, 255),     # 5: Mauve
    (255, 0, 0),       # 6: Bright Red
    (255, 0, 128),     # 7: Purple
    (255, 0, 255),     # 8: Bright Magenta
    (0, 128, 0),       # 9: Green
    (0, 128, 128),     # 10: Cyan
    (0, 128, 255),     # 11: Sky Blue
    (128, 128, 0),     # 12: Yellow
    (128, 128, 128),   # 13: White (Gray)
    (128, 128, 255),   # 14: Pastel Blue
    (255, 128, 0),     # 15: Orange
]

def decode_mode0_byte(byte_val):
    """Decode a Mode 0 byte into 2 pixels"""
    pixel0 = ((byte_val & 0x80) >> 4) | ((byte_val & 0x20) >> 3) | \
             ((byte_val & 0x08) >> 2) | ((byte_val & 0x02) >> 1)
    pixel1 = ((byte_val & 0x40) >> 3) | ((byte_val & 0x10) >> 2) | \
             ((byte_val & 0x04) >> 1) | (byte_val & 0x01)
    return pixel0, pixel1

def convert_charset_to_png(input_file, output_file, tile_width=8, tile_height=8, tiles_per_row=16):
    """
    Convert CPC character set to PNG tileset

    Args:
        input_file: Path to character set .BIN file
        output_file: Path to output .PNG file
        tile_width: Width of each tile in pixels (default 8)
        tile_height: Height of each tile in pixels (default 8)
        tiles_per_row: Number of tiles per row in output (default 16)
    """
    with open(input_file, 'rb') as f:
        data = f.read()

    # Calculate number of tiles
    # Each 8x8 tile = 4 bytes per line * 8 lines = 32 bytes (Mode 0: 4 pixels per byte)
    bytes_per_tile_line = tile_width // 2  # Mode 0: 2 pixels per byte
    bytes_per_tile = bytes_per_tile_line * tile_height
    num_tiles = len(data) // bytes_per_tile

    print(f"Converting {input_file}")
    print(f"  File size: {len(data)} bytes")
    print(f"  Tile size: {tile_width}x{tile_height}")
    print(f"  Bytes per tile: {bytes_per_tile}")
    print(f"  Number of tiles: {num_tiles}")

    # Calculate output image dimensions
    num_rows = (num_tiles + tiles_per_row - 1) // tiles_per_row
    img_width = tiles_per_row * tile_width
    img_height = num_rows * tile_height

    print(f"  Output dimensions: {img_width}x{img_height} ({tiles_per_row} tiles per row)")

    # Create image
    img = Image.new('RGB', (img_width, img_height), color=(0, 0, 0))
    pixels = img.load()

    # Process each tile
    for tile_idx in range(num_tiles):
        tile_offset = tile_idx * bytes_per_tile

        # Calculate tile position in output image
        tile_x = (tile_idx % tiles_per_row) * tile_width
        tile_y = (tile_idx // tiles_per_row) * tile_height

        # Decode tile pixels
        for y in range(tile_height):
            line_offset = tile_offset + y * bytes_per_tile_line

            for x in range(0, tile_width, 2):
                byte_offset = line_offset + x // 2

                if byte_offset >= len(data):
                    break

                byte_val = data[byte_offset]
                pixel0, pixel1 = decode_mode0_byte(byte_val)

                # Clamp to palette
                pixel0 = pixel0 % len(CPC_PALETTE)
                pixel1 = pixel1 % len(CPC_PALETTE)

                # Set pixels in output image
                if tile_x + x < img_width and tile_y + y < img_height:
                    pixels[tile_x + x, tile_y + y] = CPC_PALETTE[pixel0]
                if tile_x + x + 1 < img_width and tile_y + y < img_height:
                    pixels[tile_x + x + 1, tile_y + y] = CPC_PALETTE[pixel1]

    # Save PNG
    img.save(output_file)
    print(f"  Saved to: {output_file}")
    print()

    return num_tiles

def main():
    """Convert character set files to tilesets"""
    script_dir = os.path.dirname(os.path.abspath(__file__))
    raw_dir = os.path.join(script_dir, 'raw')
    png_dir = os.path.join(script_dir, 'png')

    os.makedirs(png_dir, exist_ok=True)

    print("=" * 60)
    print("CPC Character Set / Tile Converter")
    print("=" * 60)
    print()

    # Convert character sets (tiles)
    files_to_convert = ['CONSET1.BIN', 'CONSET2.BIN', 'CONALP.BIN']

    for filename in files_to_convert:
        input_path = os.path.join(raw_dir, filename)
        output_filename = os.path.splitext(filename)[0].lower() + '_tiles.png'
        output_path = os.path.join(png_dir, output_filename)

        if os.path.exists(input_path):
            try:
                convert_charset_to_png(input_path, output_path)
            except Exception as e:
                print(f"ERROR converting {filename}: {e}")
                import traceback
                traceback.print_exc()
                print()
        else:
            print(f"SKIPPED: {filename} (not found)")
            print()

    print("=" * 60)
    print("Conversion complete!")
    print("=" * 60)

if __name__ == '__main__':
    main()
