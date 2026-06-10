# 11 — Assets & Content Pipeline

This document inventories the assets shipped with the project, explains the MonoGame Content Builder pipeline, and describes the runtime fallback paths.

## Asset Inventory

### Spritesheets — `Content/`

| File | Used by | Status |
|------|---------|--------|
| `Characters.png` | Player wizard | ✅ Live |
| `GuardSheet.png` | Guard enemy | ✅ Live |
| `MaskSheet.png` | Mask enemy | ✅ Live |
| `BoarSheet.png` | Boar enemy | ✅ Live |
| `EyeSheet.png` | Eye enemy | ✅ Live |
| `WraithSheet.png` | Wraith enemy | ✅ Live |
| `EnemyDeathSheet.png` | Enemy death animation | ✅ Live |
| `CaptiveWizardSheet.png` | Idle captive wizard | ✅ Live |
| `StarSheet.png` | Wizard rescue transformation | ✅ Live |
| `BlockedDoorSheet.png` | Locked door visual | ✅ Live |
| `LeftDoorFrames.png` | Door (left-opening) animation | ✅ Live |
| `RightDoorFrames.png` | Door (right-opening) animation | ✅ Live |
| `SwordSheet.png` | Sword item | ✅ Live |
| `BallandChainSheet.png` | Ball-and-Chain item | ✅ Live |
| `AxeSheet.png` | Axe item | ✅ Live |
| `ShootingStarSheet.png` | Shooting Star item | ✅ Live |
| `LyreSheet.png` | Lyre key item | ✅ Live |
| `Tiles.png` | Tile rendering for tile-based rooms | ✅ Live |
| `RoomBG_Stonehenge.png` | Stonehenge room background | ✅ Live |
| `RoomBG_Wastelands.png` | Wastelands room background | ✅ Live |
| `RoomBG_TunnelMouth.png` | Tunnel Mouth room background | ✅ Live |
| `RoomBG_Chateau0.png` | Chateau 0 room background | ✅ Live |
| `RoomBG_Chateau1.png` | Chateau 1 room background | ✅ Live |
| `RoomBG_Chateau2.png` | Chateau 2 room background | ✅ Live |
| `BagSheet.png` | Bag item | 🟡 Extracted, not wired |
| `BottleSheet.png` | Bottle item (cauldron consumable) | 🟡 Not wired |
| `ChaliceSheet.png` | Chalice quest item | 🟡 Not wired |
| `CupSheet.png` | Cup quest item | 🟡 Not wired |
| `ParchmentSheet.png` | Parchment scroll | 🟡 Not wired |
| `WandSheet.png` | Wand weapon | 🟡 Not wired |
| `KeySheet.png` | Key (door unlock) | 🟡 Not wired |
| `CoatSheet.png` | Coat (poison immunity) | 🟡 Not wired |
| `Book.png` | Book quest item | 🟡 Not wired |
| `FlaireSheet.png` | Flaire weapon | 🟡 Not wired |
| `MoonSheet.png` | Moon item | 🟡 Not wired |
| `WaterSheet.png` | Water item | 🟡 Not wired |
| `Fountain.png` | Fountain (room feature) | 🟡 Not wired |
| `GatewatSheet.png` | Gateway (Chapter 2 transition) | 🟡 Not wired |
| `FireandDrownedSheet.png` | Fire/Drowned death variant | 🟡 Not wired |
| `Spritesheet2.png` | Secondary spritesheet | 🟡 Not wired |
| `object1.png` | Misc | 🟡 Not wired |

### Fonts

| File | Used by |
|------|---------|
| `Content/DebugFont.spritefont` | Info panel text + F1 debug overlay |

The `.spritefont` is an XML descriptor. MGCB compiles it into a `.xnb` font asset. The current font is whatever MonoGame's default sample font produces — debug-quality, intentionally placeholder.

### Collision Data — `assets/data/`

| File | Used by |
|------|---------|
| `collision_chateau0.json` | Chateau 0 |
| `collision_chateau1.json` | Chateau 1 |
| `collision_chateau2.json` | Chateau 2 |
| `collision_stonehenge.json` | Stonehenge |
| `collision_wastelands.json` | Wastelands |
| `collision_tunnelmouth.json` | Tunnel Mouth |
| `collision_*_debug.png` | (Visual reference, not loaded by game) |

JSON schema and authoring guide: see [06_COLLISION.md](./06_COLLISION.md#authoring-a-collision-grid).

### Original Reference Images — `assets/images/`

| File | Used by |
|------|---------|
| `Amstrad CPC - Sorcery - Characters.png` | Runtime fallback for player sprite if MGCB build fails |

### Repo-Root Screenshots

The repository root contains development screenshots used during room-collision authoring (`chateu 1.png`, `clean_outside_chateau.png`, etc.). These are reference material, not loaded by the game. They're committed because they're cheap and useful when revising collision data.

## MonoGame Content Builder (MGCB)

### How Build Integration Works

`SorceryRemake.csproj` references `Content/Content.mgcb`:

```xml
<ItemGroup>
  <MonoGameContentReference Include="Content\Content.mgcb" />
</ItemGroup>
```

The `MonoGame.Content.Builder.Task` NuGet package contributes an MSBuild target that:

1. On `dotnet build`, runs MGCB.
2. MGCB reads `Content.mgcb`, compiles each referenced asset (PNG → .xnb), and writes outputs into `bin/Debug/net8.0/Content/`.
3. The compiled `.xnb` files are loaded at runtime via `Content.Load<Texture2D>("AssetName")` (note: no extension).

### Adding a New Sprite to MGCB

Open `Content/Content.mgcb` and add a block patterned after existing entries:

```
#begin BatSheet.png
/importer:TextureImporter
/processor:TextureProcessor
/processorParam:ColorKeyEnabled=False
/processorParam:GenerateMipmaps=False
/processorParam:PremultiplyAlpha=True
/processorParam:ResizeToPowerOfTwo=False
/processorParam:MakeSquare=False
/processorParam:TextureFormat=Color
/build:BatSheet.png
#end
```

Key processor parameters:

- **`ColorKeyEnabled=False`** — we do our own black-to-transparent in `Game1.MakeColorTransparent`, so MGCB shouldn't apply its default magenta color key.
- **`PremultiplyAlpha=True`** — required for `BlendState.AlphaBlend` to render correctly.
- **`ResizeToPowerOfTwo=False`** — modern GPUs don't require power-of-two textures, and forcing it would distort our pixel-art dimensions.
- **`TextureFormat=Color`** — uncompressed 32-bit, lossless. Don't switch to DXT-compressed for pixel art.

### Build Outputs

```
bin/Debug/net8.0/
├── SorceryRemake.exe
├── Content/
│   ├── Characters.xnb
│   ├── GuardSheet.xnb
│   ├── ...
│   └── DebugFont.xnb
└── assets/data/
    └── collision_*.json   (copied verbatim)
```

The collision JSON files are **not** processed by MGCB — they're plain content copied to output via the project's `<Content>` block:

```xml
<ItemGroup>
  <Content Include="assets\data\*.json">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

`PreserveNewest` means modified files are re-copied on rebuild without forcing a full rebuild.

## Runtime Asset Loading

`Game1.LoadContent` is the single asset-loading entry point. It:

1. Initializes `_spriteBatch`.
2. Loads spritesheets via `Content.Load<Texture2D>(name)` (sometimes wrapped in `LoadAndTransparent` to apply the black-key conversion).
3. Loads room backgrounds (no transparency conversion, since backgrounds may legitimately contain black sky).
4. Constructs the `_pixelTexture` 1×1 white pixel.
5. Registers items with the `_itemSystem` (texture + source rect).
6. Wires up `RoomManager` (textures, registers all rooms).
7. Initializes `RoomRegistry`.
8. Loads `chateau_0` and spawns its content.
9. Tries to load `DebugFont`; tolerates failure.

### Player-Sprite Fallback

The player spritesheet `Characters` has a runtime fallback path: if `Content.Load` fails, try direct `Texture2D.FromStream` from `assets/images/Amstrad CPC - Sorcery - Characters.png`. If THAT fails, generate a 16×16 magenta placeholder.

This means the project still launches in degraded form even if MGCB asset compilation broke — useful during early bring-up. Other textures don't have this fallback; missing them throws and crashes.

### Order Matters

`MakeColorTransparent` is called *immediately* after every sprite-sheet load (player, enemies, items, doors). The player sheet is processed before its `SpriteComponent` is built, otherwise the initial frame would render with a black background.

Backgrounds are NOT processed by `MakeColorTransparent`. Black is a valid background color (sky in Stonehenge, void in Chateau interior).

## Asset Authoring Conventions

When adding new sprites to the project:

- **Frame size:** Stick to 24×24 for entities, 48×48 for items (which render at 24×24). Deviating means hand-tuning frame coordinates in `SpriteConfig`.
- **Frame spacing:** 1-px gap between adjacent frames (24 + 1 = 25 px stride). This is a Sorcery-specific convention from the original sheets.
- **Background color:** Pure black `(0, 0, 0)` for transparent regions (the `MakeColorTransparent` key).
- **PNG format:** RGB or RGBA. Indexed PNGs work but are converted to 32-bit at build time.
- **Native resolution:** Don't pre-scale. Authoring at 24×24 native, MGCB output at 24×24 native, render at 3× = 72×72 displayed. The render pipeline handles scaling.

## Asset Extraction History

Many sprites in `Content/` were extracted from the original game's disk image (`.DSK`) in a separate Python pipeline. The legacy `docs/AssetExtraction.md` describes this process. The extraction pipeline is **not** part of the live game — it's a one-shot tool. The remake treats `Content/` as the source of truth.

The Python extraction pipeline is preserved in repo-root files (`main.py`, `player.py`, `settings.py`, `spritesheet.py`, plus the `extraction/` and `tools/` folders) and the older `docs/` notes. These are reference artifacts, not active code.

## Future: Dynamic Background Loading

The DEVELOPMENT_PHASES roadmap (Phase 5A) proposes loading room backgrounds at runtime from disk via `Texture2D.FromStream`, with a `Dictionary<string, Texture2D>` cache in `RoomManager`. This would eliminate the need to edit `Content.mgcb` and add a private field to `Game1` for every new room background — significantly accelerating room authoring.

The fallback path for the player sprite already demonstrates this pattern; promoting it to first-class for backgrounds is mostly a `LoadBackground(name)` helper.

## Asset Licensing

From the repo-root README:

> **Original Game Assets:** Copyright © Original Rights Holders
> - Assets are extracted from legally owned copies for preservation
> - Not included in repository (must be extracted by end user)
> - For personal, educational, and preservation use only

In practice, the current `Content/` folder DOES include the extracted assets, since they're committed for the project to build. If the project is ever opened to public distribution, the assets must be removed and replaced with an extraction step the end-user runs locally on their own copy of the original disk image.
