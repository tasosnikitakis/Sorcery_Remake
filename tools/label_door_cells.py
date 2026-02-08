"""
Extract each 24x24 cell from the door area and create a labeled contact sheet.
This helps visually identify which cells contain door frames.
"""

from PIL import Image, ImageDraw, ImageFont
import os

script_dir = os.path.dirname(os.path.abspath(__file__))
project_dir = os.path.dirname(script_dir)
spritesheet_path = os.path.join(project_dir, "Content", "Spritesheet2.png")
output_dir = os.path.join(project_dir, "assets", "images", "door_frames")
os.makedirs(output_dir, exist_ok=True)

img = Image.open(spritesheet_path)
CELL = 24

# Extract cols 23-30 (1-indexed), rows 1-8
# 0-indexed: cols 22-29, rows 0-7
start_col = 22  # col 23 in 1-indexed
end_col = 30     # col 30 in 1-indexed (exclusive)
num_cols = end_col - start_col
num_rows = 8

# Scale each cell 6x, add 2px border and label
SCALE = 6
BORDER = 2
LABEL_H = 16
cell_display_w = CELL * SCALE + BORDER * 2
cell_display_h = CELL * SCALE + BORDER * 2 + LABEL_H

sheet_w = num_cols * cell_display_w + 4
sheet_h = num_rows * cell_display_h + 4

sheet = Image.new("RGB", (sheet_w, sheet_h), (40, 40, 40))
draw = ImageDraw.Draw(sheet)

for row in range(num_rows):
    for col_idx in range(num_cols):
        abs_col = start_col + col_idx  # 0-indexed column in spritesheet
        col_1indexed = abs_col + 1  # 1-indexed for display

        # Extract cell
        x = abs_col * CELL
        y = row * CELL
        cell_img = img.crop((x, y, x + CELL, y + CELL))

        # Scale up
        big = cell_img.resize((CELL * SCALE, CELL * SCALE), Image.NEAREST)

        # Position on sheet
        dx = 2 + col_idx * cell_display_w + BORDER
        dy = 2 + row * cell_display_h + LABEL_H + BORDER

        # Draw border
        draw.rectangle(
            [dx - BORDER, dy - BORDER, dx + CELL * SCALE + BORDER - 1, dy + CELL * SCALE + BORDER - 1],
            outline=(128, 128, 128)
        )

        # Paste cell
        sheet.paste(big, (dx, dy))

        # Label
        label = f"R{row+1}C{col_1indexed}"
        draw.text((dx, dy - LABEL_H), label, fill=(255, 255, 0))

        # Also save each cell individually
        cell_path = os.path.join(output_dir, f"cell_r{row+1}_c{col_1indexed}.png")
        cell_img.save(cell_path)

sheet_path = os.path.join(output_dir, "door_cells_labeled.png")
sheet.save(sheet_path)
print(f"Saved labeled grid: {sheet_path}")
print(f"Grid shows cols {start_col+1}-{end_col} (1-indexed), rows 1-{num_rows}")
print(f"Each cell is {CELL}x{CELL} pixels, scaled {SCALE}x")
