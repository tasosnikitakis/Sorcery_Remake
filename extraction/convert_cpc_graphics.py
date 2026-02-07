#!/usr/bin/env python3
"""
Convert Amstrad CPC Mode 0 graphics to PNG
Based on CPC Mode 0 format: 160x200, 16 colors
"""

from PIL import Image
import sys
import os

# Amstrad CPC Mode 0 Palette (Hardware colors)
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
    """
    Decode a Mode 0 byte into 2 pixels (4 bits per pixel)
    Mode 0 uses a complex bit interleaving pattern
    """
    # Mode 0 bit pattern for 2 pixels:
    # Byte: b7 b6 b5 b4 b3 b2 b1 b0
    # Pixel 0: b7 b5 b3 b1 (bits 7,5,3,1)
    # Pixel 1: b6 b4 b2 b0 (bits 6,4,2,0)

    pixel0 = ((byte_val & 0x80) >> 4) | ((byte_val & 0x20) >> 3) | \
             ((byte_val & 0x08) >> 2) | ((byte_val & 0x02) >> 1)
    pixel1 = ((byte_val & 0x40) >> 3) | ((byte_val & 0x10) >> 2) | \
             ((byte_val & 0x04) >> 1) | (byte_val & 0x01)

    return pixel0, pixel1

def convert_cpc_to_png(input_file, output_file, width=160, height=None, offset=0):
    """
    Convert CPC Mode 0 graphics file to PNG

    Args:
        input_file: Path to .BIN file
        output_file: Path to output .PNG file
        width: Width in pixels (default 160 for Mode 0)
        height: Height in pixels (auto-calculated if None)
        offset: Bytes to skip at start (for headers)
    """
    # Read binary data
    with open(input_file, 'rb') as f:
        data = f.read()[offset:]  # Skip header if specified

    # Calculate dimensions
    # Each byte = 2 pixels in Mode 0
    # Each line = width/2 bytes
    bytes_per_line = width // 2

    if height is None:
        # Auto-calculate height from file size
        height = len(data) // bytes_per_line
        if height == 0:
            height = 200  # Default CPC screen height

    print(f"Converting {input_file}")
    print(f"  File size: {len(data)} bytes (offset: {offset})")
    print(f"  Dimensions: {width}x{height}")
    print(f"  Bytes per line: {bytes_per_line}")

    # Create image
    img = Image.new('RGB', (width, height))
    pixels = img.load()

    # Decode pixels
    byte_index = 0
    for y in range(height):
        for x in range(0, width, 2):
            if byte_index >= len(data):
                break

            byte_val = data[byte_index]
            pixel0, pixel1 = decode_mode0_byte(byte_val)

            # Clamp color indices to palette range
            pixel0 = pixel0 % len(CPC_PALETTE)
            pixel1 = pixel1 % len(CPC_PALETTE)

            # Set pixels
            if x < width:
                pixels[x, y] = CPC_PALETTE[pixel0]
            if x + 1 < width:
                pixels[x + 1, y] = CPC_PALETTE[pixel1]

            byte_index += 1

    # Save PNG
    img.save(output_file)
    print(f"  Saved to: {output_file}")
    print()

def main():
    """Convert all .BIN files in raw folder to PNG"""
    script_dir = os.path.dirname(os.path.abspath(__file__))
    raw_dir = os.path.join(script_dir, 'raw')
    png_dir = os.path.join(script_dir, 'png')

    # Create png directory if it doesn't exist
    os.makedirs(png_dir, exist_ok=True)

    # Files to convert: filename -> list of (width, height, offset, suffix)
    # We'll try multiple parameter combinations for each file
    files_to_convert = {
        'CONSET1.BIN': [
            (160, None, 0, ''),           # Standard Mode 0
            (160, None, 128, '_off128'),  # Skip 128-byte header
            (128, None, 0, '_w128'),      # Try width 128
            (80, None, 0, '_w80'),        # Try width 80
            (256, None, 0, '_w256'),      # Try width 256
        ],
        'CONSET2.BIN': [
            (160, None, 0, ''),
            (160, None, 128, '_off128'),
            (128, None, 0, '_w128'),
            (80, None, 0, '_w80'),
        ],
        'CONALP.BIN': [
            (160, None, 0, ''),
            (128, None, 0, '_w128'),
        ],
        'TITLEP.BIN': [
            (160, None, 0, ''),
            (160, None, 128, '_off128'),
        ],
        'SPRITES1.BIN': [
            (160, None, 0, ''),
            (160, None, 128, '_off128'),
        ],
        'SPRITES2.BIN': [
            (160, None, 0, ''),
        ],
    }

    print("=" * 60)
    print("CPC Mode 0 Graphics Converter")
    print("=" * 60)
    print()

    for filename, param_sets in files_to_convert.items():
        input_path = os.path.join(raw_dir, filename)

        if os.path.exists(input_path):
            for width, height, offset, suffix in param_sets:
                base_name = os.path.splitext(filename)[0].lower()
                output_filename = base_name + suffix + '.png'
                output_path = os.path.join(png_dir, output_filename)

                try:
                    convert_cpc_to_png(input_path, output_path, width, height, offset)
                except Exception as e:
                    print(f"ERROR converting {filename} with params (w={width}, off={offset}): {e}")
                    print()
        else:
            print(f"SKIPPED: {filename} (not found)")
            print()

    print("=" * 60)
    print("Conversion complete! Check the png/ folder for results.")
    print("=" * 60)

if __name__ == '__main__':
    main()
