# 09 — Items & Combat

This document covers the inventory system, the weapon-enemy effectiveness matrix, the projectile system (Shooting Star), and the captive wizard rescue mechanic. The full set of original-game items (14+ sprites) is partially extracted but only 5 are wired up; this is tracked under Phase 5C in [12_ROADMAP.md](./12_ROADMAP.md).

## The "One Item" Rule

Sorcery+ has the strictest possible inventory: **the player carries exactly one item at a time.** Picking up a new item drops the currently-held item *at the picked-up item's position*. This single rule produces most of the game's puzzle texture:

- You can't stockpile.
- Choosing what to leave behind is half the gameplay.
- Returning to a room means returning to the items you left there.

The implementation is in [`Game1.TryPickupItem`](../Game1.cs#L407):

```csharp
ItemType pickedType = item.Type;
Vector2 dropPos = item.Position;

_worldState.PickedUpItems.Add(item.Id);     // permanent
_roomItems.RemoveAt(i);

if (_worldState.CarriedItem != ItemType.None)
{
    string droppedId = $"dropped_{_worldState.SpawnCounter++}";
    Texture2D dropTex = _itemSystem.GetTexture(_worldState.CarriedItem);
    Rectangle dropSrc = _itemSystem.GetSourceRect(_worldState.CarriedItem);
    if (dropTex != null)
        _roomItems.Add(new ItemInstance(droppedId, _worldState.CarriedItem, dropPos, dropTex, dropSrc));
}

_worldState.CarriedItem = pickedType;
```

The dropped item gets a unique `dropped_<n>` ID. It's NOT in `WorldState.PickedUpItems`, so it persists across leaves-and-returns. Importantly, the dropped item is only persisted in the room's runtime list `_roomItems`, which itself is wiped on every transition. **A dropped item only survives in its room while you're there or while the snapshot system holds it.** Leaving the room and coming back recreates the room from `RoomRegistry` (so the original spawn doesn't reappear because its ID is in `PickedUpItems`, but the dropped item is also gone).

This is a known gap in the current build vs. the original, where dropped items persisted globally. A fuller implementation would store dropped items in a `WorldState.DroppedItems` per-room dictionary so they survive transitions.

## The Five Currently-Wired Items

| Item | Type role | Effect |
|------|-----------|--------|
| **Sword** | Weapon | Kills Guard. Consumed on use. |
| **Ball-and-Chain** | Weapon | Kills Mask, Boar, Eye. Consumed on use. |
| **Axe** | Weapon | Kills Wraith. Consumed on use. |
| **Shooting Star** | Projectile | Fires 8-direction radial burst from player center, AOE-kills any enemy. Consumed on use. |
| **Lyre** | Key | Unlocks any blocked door whose `RequiredItem == Lyre`. Consumed on use. |

All five are 48×48 source rectangles (full sheet) in their respective spritesheets, rendered at 24×24 in-game.

## The Weapon-Enemy Matrix

Defined in `Core/ItemSystem.cs` `CanKillEnemy`:

| | Sword | Ball-and-Chain | Axe | Shooting Star | Lyre |
|---|---|---|---|---|---|
| **Guard** | ✅ | — | — | ✅ (AOE) | — |
| **Mask** | — | ✅ | — | ✅ (AOE) | — |
| **Boar** | — | ✅ | — | ✅ (AOE) | — |
| **Eye** | — | ✅ | — | ✅ (AOE) | — |
| **Wraith** | — | — | ✅ | ✅ (AOE) | — |

The matrix is hardcoded today. Extending it to support more enemies / weapons in Phase 5C should keep the static method approach (clear, no allocations, easy to grep) but consider moving `EnemyType` to its own file alongside.

### Wrong-Weapon Behavior

Striking an enemy with the wrong weapon **does nothing** — no consumption, no animation, no damage. The melee check explicitly gates on `CanKillEnemy`:

```csharp
if (currentKeyState.IsKeyDown(Keys.Space) && _worldState.CarriedItem != ItemType.None)
{
    foreach enemy:
        if !enemy.IsDying
           && IsOverlapping(_player, enemy.Entity)
           && ItemSystem.CanKillEnemy(enemy.Type, _worldState.CarriedItem):
            StartEnemyDeath(enemy);
            break;
}
```

The matrix is the *only* gate. Holding Space while overlapping a Wraith with a Sword is silent.

## The Shooting Star (AOE)

The Shooting Star is the only projectile weapon. Its mechanics:

1. Tap **Space** while carrying it (and not standing on a pickup).
2. `FireShootingStar` spawns 8 `Projectile` entries at the player's center, with velocity vectors:
   - 4 cardinal directions at full speed: `(0,-S)`, `(0,S)`, `(-S,0)`, `(S,0)` — where `S = PROJECTILE_SPEED = 200 px/s`.
   - 4 diagonals at speed × 0.7071 (≈ √2/2): `(-D,-D)`, `(D,-D)`, `(-D,D)`, `(D,D)`.
3. The Shooting Star item is consumed (`CarriedItem = None`).

Projectiles update each frame:

```
proj.Position += proj.Velocity * dt
if proj.Position out of [0, 320] × [0, 144]: remove proj
for each non-dying enemy:
    if enemy.Rectangle.Contains((int)proj.Position): StartEnemyDeath(enemy)
```

Projectiles are **single-pixel** and rendered as 3×3 (RENDER_SCALE) yellow squares. They have no decay distance — they only stop on screen-edge departure or after killing an enemy.

The 8-direction radial burst is the original game's signature AOE: it covers the whole room within ~0.7 seconds and kills everything in its path regardless of type.

## Captive Wizards

Wizards are not items the player carries — they are room-resident entities the player rescues by touching them.

### Visual states

- **Captive (idle):** 4-frame loop in `CaptiveWizardSheet.png` (48×192, vertical strip), bottom-to-top cycling at 0.15 s/frame. Rendered 24×24.
- **Saving:** When the player overlaps the wizard, `IsSaving=true`. Texture switches to `StarSheet` (top-to-bottom 4 frames, 0.12 s/frame). The wizard moves upward at `PROJECTILE_SPEED` (200 px/s) until it goes off the top of the screen, then despawns.

### Persistence

`WorldState.SavedWizards` records the ID. `WorldState.SavedWizardCount` increments by one (guarded by `wiz.CountedAsSaved` to prevent double-count if the rescue is interrupted).

A saved wizard never respawns. The HUD info panel shows the running total ("Saved Wizards: N").

### Win Condition (future)

There is no win state today. The original game's win condition is rescuing all 8 wizards before the crumbling-book timer expires. Adding this is straightforward (Phase 6A in the roadmap): when `SavedWizardCount == TARGET_WIZARDS`, set a state flag, freeze gameplay, show a win screen.

## Blocked Doors

Blocked doors are room-resident entities (`BlockedDoorInstance`) that block movement until unlocked with a specific key item. Mechanics:

### Layout

- 24×24 sprite (single frame in `BlockedDoorSheet.png`).
- Solid hitbox is *not* the full 24×24 — it's an 8-px-wide central bar (`BLOCKED_DOOR_HITBOX_OFFSET_X=8`, `WIDTH=8`, `HEIGHT=24`). The visual sprite has wider decorative wings; the gameplay hitbox is just the central bar.
- Defined per-room in `RoomRegistry.Initialize` as `BlockedDoorSpawn(id, position, requiredItem)`.

### Unlock Sequence

Each frame `Game1.UpdateBlockedDoors`:

```
for each blockedDoor:
    if player.rect ∩ door.hitbox && CarriedItem == door.RequiredItem:
        WorldState.UnlockedDoors.Add(door.Id)
        WorldState.CarriedItem = ItemType.None
        _roomBlockedDoors.RemoveAt(i)
        RebuildSolidRects()
```

The unlock is permanent (`UnlockedDoors` set), the key item is consumed, and the player's `PhysicsComponent.SolidRects` is rebuilt to drop the door's collision rectangle.

### Today's Only Blocked Door

`room_2`'s "iron door" — an instance at `Vector2(216f, 72f)` requiring the Lyre. The Lyre is found in `room_1`. So the puzzle in the current build is: drop everything in room_1 except the Lyre, fly to room_2, unlock the door, walk to the wizard at the right end of the tunnel.

## Adding a New Item

Even with the Phase-4A refactor, adding a new item still touches a few sites. The clean pattern:

### 1. Extend `ItemType` enum

`Core/ItemSystem.cs`:

```csharp
public enum ItemType
{
    None, Sword, BallAndChain, Axe, ShootingStar, Lyre,
    Wand,    // ← new
}
```

### 2. Add the spritesheet to Content

Drop `WandSheet.png` into `Content/` and add a `#begin` block to `Content/Content.mgcb`. Use one of the existing item-sheet entries as a template.

(Note: `WandSheet.png`, `BagSheet.png`, `BottleSheet.png`, etc. are *already in* `Content/` from a prior asset-extraction pass. They just aren't loaded yet.)

### 3. Add a frame rectangle

`Graphics/SpriteConfig.cs`:

```csharp
public static readonly Rectangle WAND_FRAME =
    new Rectangle(0, 0, ITEM_SOURCE_SIZE, ITEM_SOURCE_SIZE);
```

### 4. Register in `LoadContent`

`Game1.LoadContent`:

```csharp
var wandSheet = LoadAndTransparent("WandSheet");
_itemSystem.Register(ItemType.Wand, wandSheet, SpriteConfig.WAND_FRAME);
```

### 5. (If a weapon) Extend the kill matrix

`Core/ItemSystem.cs` `CanKillEnemy`:

```csharp
EnemyType.Dragon => weapon == ItemType.Wand,
```

### 6. Place it in a room

`Rooms/RoomRegistry.cs` `Initialize`:

```csharp
forest1.Items.Add(new ItemSpawn("forest1_wand", ItemType.Wand, new Vector2(180, 80)));
```

That's it. No changes to picking, dropping, rendering, or the info panel — those all flow through the registered texture and source rectangle.

### Categories of Items in the Original

The roadmap (Phase 5C) categorizes the 14+ extracted items into:

- **Weapons:** Sword, Ball-and-Chain, Axe, Wand (verify), Moon (verify)
- **Key items:** Lyre, Key, Coat (poison-immunity)
- **Consumables:** Bottle (used at healing cauldrons), Water (room-state mod)
- **Quest items:** Parchment, Book (display message / advance plot)
- **Room interaction:** Chalice, Cup, Fountain (per-room puzzle pieces)
- **Chapter-2 transition:** Gateway

Each category is a slightly different integration: weapons extend the matrix, key items extend `BlockedDoorSpawn` configurations, consumables need a "use at cauldron" interaction, quest items need a message display system that doesn't exist yet.

## Info Panel Display

The info panel (`Game1.DrawInfoPanel`) shows:

- Top line: "You are in: \<RoomDisplayName\>" (yellow text)
- Middle line: "Carrying: \<itemName\>" (yellow text, "Nothing" if `None`)
- Bottom line: "Saved Wizards: \<N\>" (yellow text)
- Right side: a 48×48-rendered icon of the carried item (or nothing if `None`)

This is debug-font rendering, not the final UI. Phase 4C plans a sprite-based info panel matching the original's layout (energy bar, wizard count graphic, lives indicator, item icon). Today the icon is the only visual element.
