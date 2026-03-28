"""
Extract hooded guard frames from the main character spritesheet.
Crops the 1px green line from the top of each frame.
Output: 12 frames in a single row, each 24x24 (23px content + 1px transparent padding at top).
"""
from PIL import Image

src = Image.open(r"d:\sorcery+_remake\assets\images\Amstrad CPC - Sorcery - Characters.png")

FRAME_W = 24
FRAME_H = 24
SPACING = 1
NUM_FRAMES = 12
ROW_Y = 25  # Guard row starts at Y=25
GREEN_LINE = 1  # 1px green line to crop from top

# Content starts at Y=26, is 23px tall
content_y = ROW_Y + GREEN_LINE
content_h = FRAME_H - GREEN_LINE  # 23px

# Output: 12 frames in a row, each 24x24, with 1px spacing (matching existing pattern)
out_w = NUM_FRAMES * FRAME_W + (NUM_FRAMES - 1) * SPACING  # 299
out_h = FRAME_H  # 24
out = Image.new("RGBA", (out_w, out_h), (0, 0, 0, 0))  # Fully transparent

for i in range(NUM_FRAMES):
    src_x = i * (FRAME_W + SPACING)
    # Crop frame without green line (Y=26, height=23)
    frame = src.crop((src_x, content_y, src_x + FRAME_W, content_y + content_h))
    # Paste at bottom of 24px cell (1px transparent padding at top)
    dst_x = i * (FRAME_W + SPACING)
    out.paste(frame, (dst_x, GREEN_LINE))

out.save(r"d:\sorcery+_remake\Content\GuardSheet.png")
print(f"Saved GuardSheet.png: {out.size[0]}x{out.size[1]}, {NUM_FRAMES} frames")
