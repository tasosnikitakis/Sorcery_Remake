# `assets/import/` — screenshot drop folder

Put a screenshot of an original Sorcery+ room here, click **Import** in
SorceryForge, pick it from the list. You get a registered, editable room.

Nothing in here is repository content. `*.jpg`, `*.jpeg` and `*.png` in this
folder are gitignored (this README is not) — the files are **inputs**, and what
the import produces is a PNG in `Content/`.

## The name of the file is the name of the room

There is no text field anywhere in this flow. The base name of the file decides
everything:

| You drop | Import writes | Room id | Display name |
|----------|---------------|---------|--------------|
| `Chateau3.jpg` | `Content/RoomBG_Chateau3.png` | `chateau_3` | `Chateau 3` |
| `NearChateau.png` | `Content/RoomBG_NearChateau.png` | `near_chateau` | `Near Chateau` |
| `Stonehenge.jpeg` | `Content/RoomBG_Stonehenge.png` | `stonehenge` | `Stonehenge` |

**Use PascalCase.** The rule splits words at each internal capital and at a
trailing run of digits, so `Chateau3` becomes "Chateau 3" and `chateau3`
becomes "chateau3". The picker shows you the id and display name it derived
before you click, so you can always check first — and rename the file and
scan again if you don't like the answer.

The name may hold only letters, digits, `_` and `-`. A space, an accent or a
dot puts the file in the list greyed out, telling you to rename it. Room ids
are persistence keys (`WorldState` remembers entity ids built from them), which
is why the character set is narrow and why nothing renames a room afterwards.

## Sizes it accepts

- **320×144** — a pixel-perfect capture of one room. Used as-is.
- **An exact multiple** — 640×288, 960×432, … Downscaled by taking every Nth
  pixel, which for a scaled-up capture of a 320×144 screen gives the original
  screen back exactly.
- **Anything else** — selecting it opens a crop step (the picker marks those
  rows `[crop]`): drag the 20:9 selection over the image, wheel to resize,
  `Enter` to confirm, `Esc` to back out. It opens at the largest size that
  fits, centred. Nothing is written until you confirm.
- **Smaller than 320×144** — refused. Cropping could only upscale, and the
  answer to a too-small capture is a better capture.

Capture at an exact multiple when you can: an awkward scale factor drops whole
columns and wobbles the spacing by a pixel. Unavoidable, still better than a
filter, and the quantize cleans up what it leaves.

## The CPC quantize toggle

On by default. It snaps every pixel to the nearest of the 27 Amstrad CPC
hardware colours. JPEG's compression turns flat areas into a cloud of
near-misses that you cannot see but that make Erase and Punch cuts leave ragged
seams; snapping restores real flats so every later cut lands clean.

Turn it **off** for art that isn't a capture of the original game.

## Re-importing

The source file is never moved, deleted or modified — re-import as often as you
like. What *is* refused is overwriting a background that already exists in
`Content/`, because you may have erased or punched pixels out of it since. To
genuinely redo an import, delete `Content/RoomBG_<Name>.png` (and its
`rooms.json` entry, if the room was registered) first.

Full procedure, including what the import writes and what to do afterwards:
[`doc/07_WORLD_BUILDING.md`](../../doc/07_WORLD_BUILDING.md#importing-a-screenshot-room).
