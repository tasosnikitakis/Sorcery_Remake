"""
Extract mask enemy frames from the main character spritesheet.
Top row (Y=0), first 4 frames from the left.
Output: 4 frames in a single row, each 24x24 with 1px spacing.
"""
from PIL import Image

src = Image.open(r"d:\sorcery+_remake\assets\images\Amstrad CPC - Sorcery - Characters.png")

FRAME_W = 24
FRAME_H = 24
SPACING = 1
NUM_FRAMES = 4
ROW_Y = 0
GREEN_LINE = 1  # 1px green line to crop from top

# Per-frame green line crop amounts (pixels from top)
crop_top = [1, 2, 3, 2]

# Output: 4 frames in a row with 1px spacing, each 24x24 (transparent pad at top)
out_w = NUM_FRAMES * FRAME_W + (NUM_FRAMES - 1) * SPACING  # 99
out_h = FRAME_H  # 24
out = Image.new("RGBA", (out_w, out_h), (0, 0, 0, 0))

for i in range(NUM_FRAMES):
    src_x = i * (FRAME_W + SPACING)
    crop = crop_top[i]
    frame = src.crop((src_x, ROW_Y + crop, src_x + FRAME_W, ROW_Y + FRAME_H))
    dst_x = i * (FRAME_W + SPACING)
    out.paste(frame, (dst_x, crop))  # pad top with transparent pixels

out.save(r"d:\sorcery+_remake\Content\MaskSheet.png")
print(f"Saved MaskSheet.png: {out.size[0]}x{out.size[1]}, {NUM_FRAMES} frames")
