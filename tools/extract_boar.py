"""
Extract wild boar enemy frames from the main character spritesheet.
Top row (Y=0), middle 4 frames (after the mask's first 4).
Boar frames are 22px wide (2px thinner than 24px mask frames).
Per-frame green line crop from top: 0, 1, 2, 1.
"""
from PIL import Image

src = Image.open(r"d:\sorcery+_remake\assets\images\Amstrad CPC - Sorcery - Characters.png")

CELL_W = 24       # cell width in the spritesheet grid
BOAR_W = 22       # actual boar sprite width (2px thinner)
FRAME_H = 24
CELL_SPACING = 1  # spacing between cells in source
NUM_FRAMES = 4
ROW_Y = 0
# Boar is the middle 4 frames (cells 5-8), cell 5 starts at X=100
CELL_START_X = 100
INSET = 1         # skip 1px left separator to get to actual content

# Per-frame green line crop amounts (pixels from top)
crop_top = [0, 1, 2, 1]

# Output: 4 frames in a row with 1px spacing, each 22x24
out_w = NUM_FRAMES * BOAR_W + (NUM_FRAMES - 1) * CELL_SPACING  # 91
out_h = FRAME_H  # 24
out = Image.new("RGBA", (out_w, out_h), (0, 0, 0, 0))

for i in range(NUM_FRAMES):
    cell_x = CELL_START_X + i * (CELL_W + CELL_SPACING)
    src_x = cell_x + INSET  # skip 1px left separator
    crop = crop_top[i]
    frame = src.crop((src_x, ROW_Y + crop, src_x + BOAR_W, ROW_Y + FRAME_H))
    dst_x = i * (BOAR_W + CELL_SPACING)
    out.paste(frame, (dst_x, crop))

out.save(r"d:\sorcery+_remake\Content\BoarSheet.png")
print(f"Saved BoarSheet.png: {out.size[0]}x{out.size[1]}, {NUM_FRAMES} frames")
