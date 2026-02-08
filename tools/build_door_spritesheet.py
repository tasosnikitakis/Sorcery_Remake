"""
Build left and right door animation spritesheets from Spritesheet2.png.
Each frame is 48x48 pixels (2x2 block of 24x24 cells).
Right door frames are horizontally mirrored left frames.

Left-opening door frames:
  Frame 0 (closed):  R3C25+R3C26 / R4C25+R4C26 -> crop(576, 48, 624, 96)
  Frame 1:           R1C25+R1C26 / R2C25+R2C26 -> crop(576, 0, 624, 48)
  Frame 2:           R7C23+R7C24 / R8C23+R8C24 -> crop(528, 144, 576, 192)
  Frame 3 (open):    R5C23+R5C24 / R6C23+R6C24 -> crop(528, 96, 576, 144)

Output: LeftDoorFrames.png (192x48), RightDoorFrames.png (192x48)
"""

from PIL import Image, ImageOps
import os

script_dir = os.path.dirname(os.path.abspath(__file__))
project_dir = os.path.dirname(script_dir)
spritesheet_path = os.path.join(project_dir, "Content", "Spritesheet2.png")
content_dir = os.path.join(project_dir, "Content")

img = Image.open(spritesheet_path)
CELL = 24
FRAME_W = 48
FRAME_H = 48

frames_spec = [
    ("closed",   576, 48),   # Frame 0
    ("opening1", 576, 0),    # Frame 1
    ("opening2", 528, 144),  # Frame 2
    ("open",     528, 96),   # Frame 3
]

# Build left door spritesheet
left_sheet = Image.new("RGBA", (FRAME_W * len(frames_spec), FRAME_H))
for i, (name, x, y) in enumerate(frames_spec):
    frame = img.crop((x, y, x + FRAME_W, y + FRAME_H))
    left_sheet.paste(frame, (i * FRAME_W, 0))

left_path = os.path.join(content_dir, "LeftDoorFrames.png")
left_sheet.save(left_path)
print(f"Left door spritesheet: {left_path} ({left_sheet.width}x{left_sheet.height})")

# Build right door spritesheet (horizontally mirrored)
right_sheet = Image.new("RGBA", (FRAME_W * len(frames_spec), FRAME_H))
for i, (name, x, y) in enumerate(frames_spec):
    frame = img.crop((x, y, x + FRAME_W, y + FRAME_H))
    mirrored = ImageOps.mirror(frame)
    right_sheet.paste(mirrored, (i * FRAME_W, 0))

right_path = os.path.join(content_dir, "RightDoorFrames.png")
right_sheet.save(right_path)
print(f"Right door spritesheet: {right_path} ({right_sheet.width}x{right_sheet.height})")

print("\nDone! Both spritesheets saved to Content/")
