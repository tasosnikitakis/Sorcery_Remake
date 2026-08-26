# ImportCheck — SorceryForge screenshot-import regression harness

Proves this, headlessly, in about a second:

> **A screenshot dropped into `assets/import/` becomes a 320×144 PNG whose
> pixels are the source's own pixels — point-sampled, never blended, and (with
> the toggle on) snapped to the 27 Amstrad CPC hardware colours — registered as
> a room by exactly the code the New Room button runs.**

Sibling of [`tools/RoundTrip`](../RoundTrip/README.md), same idea: compile the
editor's own sources and drive them, rather than testing a reimplementation.

## Run it

```powershell
dotnet build tools/ImportCheck/ImportCheck.csproj
dotnet run   --project tools/ImportCheck/ImportCheck.csproj
```

| Exit | Meaning |
|------|---------|
| `0`  | Every check passed. |
| `1`  | Failures — each is a `FAIL` line inline. |
| `2`  | Could not run (bad argument, unsafe `--out`, repo root not found, bad `rooms.json`). |

`--out <dir>` picks the scratch directory (default
`%TEMP%\sorcery-importcheck`); it is rebuilt each run and left in place
afterwards.

`--probe <image>` prints the dimensions `TryReadImageSize` reads from one file
and whether the import can take it directly. That is how you check the header
reader against a real capture — an emulator screenshot, a phone photo, an
EXIF-laden export — without committing a binary fixture:

```powershell
dotnet run --project tools/ImportCheck/ImportCheck.csproj -- --probe "C:\shots\chateau3.jpg"
```

## What it checks

| Section | Invariant |
|---------|-----------|
| 1 palette | 27 colours, distinct, opaque, all channels in `CpcLevels` — and they **contain every triple in `extraction/convert_cpc_graphics.py`**, so a Mode 0 colour is its own nearest neighbour and quantizing can never move one. |
| 2 quantize | Nearest-colour behaviour on near-misses, midpoints and a deliberate tie; alpha-0 normalises to `(0,0,0,0)`, other alpha survives; an arbitrary sweep lands entirely in the palette. Also pins how far the two emulator palettes the shipped backgrounds actually use get moved. |
| 3 resample | Point sampling at 1×–4× returns *exactly* the sampled pixels; a blend would fail, because non-sampled source pixels carry loud junk. Out-of-range regions are refused rather than trusted. |
| 4 pipeline | A synthesised 640×288 source end to end. Toggle OFF is a pure pass-through; toggle ON differs from OFF by precisely one `QuantizeToCpc` and nothing else. |
| 5 naming | The `[A-Za-z0-9_-]+` filename rule, the size classifier, and derivation parity — the import's names come from `NewRoomFlow`'s own methods, asserted against them. |
| 5a derivation | `derive(derive(x)) == derive(x)` and "no derived id contains `__`", over a corpus covering PascalCase, trailing digits, already-snake_case names, hyphens, acronyms and doubled/edge separators — plus the specific PR 5b regression (`chateau_1.png` used to derive `chateau__1` and slip past the id-collision check) and the claim that all nine shipped backgrounds re-derive their own names. |
| 6 candidates | A scratch import folder with one file per outcome: importable, illegal name, taken id, reserved id, wrong size, target PNG already present, unreadable header, duplicate derivation, wrong extension. Each refusal's *reason* is checked, not just the refusal. |
| 7 creation | `NewRoomFlow.Create` against a scratch copy of `Content.mgcb` and an empty data dir: an all-empty 40×18 collision grid, an **append-only** `.mgcb` edit that is idempotent, and one new `rooms.json` row with the header comment and every pre-existing row untouched. |
| 8 headers | PNG and JPEG dimension reading, including a real repo PNG, a progressive JPEG, and a JPEG whose APP1 holds a decoy frame header (an EXIF thumbnail) that must **not** be mistaken for the real one. |
| 9 crop | The aspect lock, the 320×144 floor, clamping, the wheel step (60 notches in and 60 back out, checking the invariants every time) and the fit transform in both directions — then the pixels an awkward 2.19× crop actually cuts. |
| 10 presets | The built-in 384×270 calibration `(32, 41, 320, 144)` — including that `x` equals the CPC's own `(384−320)/2` border arithmetic and that no near-miss size matches it — and the resolution order stored → built-in → largest-that-fits, with a nonsense stored rect clamped rather than trusted. Then `.sorceryforge/settings.json`: born empty, byte-identical round-trip, unknown members carried through, malformed input reported and never partially applied, and a `.gitignore` line that actually covers it. Finally the aspect lock over **every reachable selection state** — twelve source sizes × seven opening rectangles × 120 mixed wheel/drag gestures, ~10k states, each checked for `Height == CropHeightFor(Width)`, the 320-wide floor, the source bounds, and samplability. |
| 11 batch | Which files "Import All" takes and which it names as skips, over a mixed folder — including that an exact multiple ignores a preset for its size (branch-order parity with the single import) and that storing one preset moves exactly one file across the line. Then the summary: counts, named reasons, the five-item list cap with its `and K more` tail, and the distinct wording for a stopped run. Plus the reason the editor's loop must reload the registry between files, asserted rather than trusted. |

Section 9's source carries each pixel's own coordinates as its colour, so
decoding an output pixel says where it came from **independently of the
sampling code**. A blend decodes to coordinates that are wrong or don't exist;
there is nowhere for a filter to hide.

## What it cannot cover

The two MonoGame calls at the ends of the pipeline: `Texture2D.FromStream`
(decode) and `Texture2D.SaveAsPng` (encode). Both need a `GraphicsDevice`,
which needs a desktop session. Those, and the visual question of whether
quantized output *looks* right on a real screenshot, are the owner's smoke
test. Everything between them is here.

## Why the import is shaped the way it is

`SorceryForge/ImageImport.cs` and `SorceryForge/NewRoomFlow.cs` contain no
`Texture2D` and no `GraphicsDevice`. `EditorGame` decodes into a `Color[]`,
hands it over, and encodes what comes back. That split is not tidiness for its
own sake — it is what makes the table above possible at all. **Keep it**: a
`GraphicsDevice` reference anywhere in those two files takes the whole of this
harness with it.

## Safety

Nothing outside the scratch directory is written. `assets/data`, `Content/` and
`assets/import/` are read only — `Content.mgcb` and `rooms.json` are *copied*
into the scratch tree and the copies are what `Create` edits. A scratch path
that is, holds, or sits inside the repository is refused up front, and the
pre-run clean removes only the five subdirectories the harness itself creates.
Section 10 writes its `settings.json` into `<scratch>/settings`, never into the
repo's real `.sorceryforge/`.
