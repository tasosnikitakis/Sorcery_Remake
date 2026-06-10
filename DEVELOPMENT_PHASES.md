# Sorcery+ Remake: Next Development Phases

## Current State Summary (as read from source)

What actually exists right now:

- **Game1.cs**: 1,732 lines. All game state (enemy lists, item lists, wizard lists, blocked doors,
  item enum, all inner classes, all spawn switches) lives in this one file. It allocates a new
  `Texture2D` 1x1 pixel inside `DrawFilledRectangle` on every single draw call — a real leak.
- **Physics**: Direct velocity, gravity constant of 120 px/s, speed 200 px/s for player.
  `PhysicsComponent` is well-structured and cleanly separated.
- **Item system**: `ItemType` enum is private inside `Game1`. Adding new items means editing the
  enum, `GetItemTexture`, `GetItemSourceRect`, `CanKillEnemy`, and two `SpawnRoomItems` switch
  cases — all in `Game1.cs`.
- **Room registration**: Rooms are registered via inline lambdas in `Game1.cs` calling
  `RegisterRoom`, `RegisterBackgroundRooms`. Every new room adds lines to `Game1.cs`.
- **No energy/health system**. Player is immortal. Touching enemies does nothing.
- **No HUD beyond text**. Info panel draws carried item name and wizard count as debug font strings.
- **No audio**. No title screen. No game over.
- **Weapon-enemy matrix is present** but uses string.Contains() on enemy IDs to determine type —
  brittle at scale.
- **14+ item sprites exist in Content** (Bag, Bottle, Chalice, Cup, Parchment, Wand, Key, Coat,
  Book, Flaire, Moon, Water, Fountain, Gateway) but are not loaded, not in `Content.mgcb`, and
  not referenced anywhere.

---

## Priority Framework

The question driving phase ordering is: **what breaks if you skip it?**

- Refactoring does not gate gameplay but it gates every developer's speed on every future feature.
- Energy/health gates whether the game is playable as a game (not a tech demo).
- Rooms cannot scale until the room authoring workflow is fast and reliable.
- Flight physics is a feel question — wrong but functional — and costs rework later if delayed.
- Audio, title screen, and saving are polish that can come last without breaking anything.

---

## Phase 4A: Emergency Refactor of Game1.cs (Do This First)

**Why now, not later:** Every subsequent phase adds more code to `Game1.cs`. The item enum
extension, the energy system, the new rooms — all of them touch the same file. At 1,732 lines
it is already painful. At 2,500 it becomes the single biggest obstacle to forward progress.

This is also the right moment because the existing systems are stable and well-understood.
Refactoring after adding health, 10 new rooms, and audio means refactoring under uncertainty.

### What to Extract

**1. `RoomData.cs` (new file in `Rooms/`)**

Move all room-specific spawn data out of `Game1.cs`. Instead of `SpawnRoomEnemies` containing
a giant switch statement, define a `RoomData` struct:

```csharp
public class RoomData
{
    public string RoomId;
    public List<EnemySpawnEntry> Enemies;
    public List<ItemSpawnEntry> Items;
    public List<WizardSpawnEntry> Wizards;
    public List<BlockedDoorEntry> BlockedDoors;
}
```

Register all rooms through a `RoomRegistry` that maps room IDs to `RoomData`. `Game1.cs` calls
`RoomRegistry.GetData(roomId)` and iterates — no switch statements, no room-specific code in
the main file. Adding room 6 through room 47 becomes adding one `RoomData` object in
`RoomRegistry.cs`, not editing `Game1.cs` at all.

**2. `ItemSystem.cs` (new file in `Items/`)**

Move `ItemType`, `ItemInstance`, `GetItemTexture`, `GetItemSourceRect`, `CanKillEnemy`,
`SpawnItem`, and all item sprite fields out of `Game1.cs`. The enum should be public, not a
private nested type, so `RoomData` and other systems can reference it without circular
dependencies. This is the prerequisite for extending the item set to include Bag, Bottle,
Chalice, and the rest.

**3. `WorldState.cs` (new file)**

The sets `_deadEnemies`, `_pickedUpItems`, `_savedWizards`, `_unlockedDoors`,
`_savedRoomEnemies`, and the counter `_savedWizardCount` are the game's persistent state.
They currently sit as scattered private fields. Extract them into a `WorldState` class that
`Game1` holds a single reference to. `RestartGame()` becomes `_worldState.Reset()`.
This class will become the target for save/load when that phase arrives.

**4. Fix the `DrawFilledRectangle` leak**

The current implementation allocates `new Texture2D(GraphicsDevice, 1, 1)` on every call.
Move `_pixelTexture` reuse to `DrawFilledRectangle` — it already exists as a field
(`_pixelTexture`). One line change, but it matters because `DrawInfoPanel` calls this
every frame.

**5. `HudRenderer.cs` (new file in `Graphics/`)**

Move `DrawInfoPanel` and `DrawDebugInfo` out of `Game1.cs`. `HudRenderer` takes a reference
to `WorldState` and `ItemSystem` to read what it needs. This separates rendering concerns
and is the prerequisite for building a proper sprite-based HUD in Phase 4C.

### Deliverable

`Game1.cs` reduces to approximately 400-500 lines: initialization, the update loop calling
into subsystems, and the draw loop calling into renderers. Every subsequent phase adds a new
file, not lines to `Game1.cs`.

### Dependencies

None. This phase has no gameplay dependencies.

---

## Phase 4B: Energy System and Player Death

**Why now:** The game currently has no stakes. Touching a wraith, walking into a guard,
standing near a mask — all harmless. Without an energy system, every room that contains
enemies is a sandbox, not a challenge. This needs to exist before rooms are authored at scale,
because enemy placement difficulty cannot be tuned without consequences.

### Mechanics to Implement

**Energy bar: 8 points**

The original Sorcery+ used an 8-cell energy bar. Player starts at 8. Contact with any enemy
costs 1 point. Energy does not regenerate naturally (that is the cauldron's job, Phase 5B).
At 0, the player dies.

The damage model uses a contact cooldown, not a one-frame hit. In the original, damage was
applied once per "touch event" — the player had to leave the enemy's hitbox and re-enter to
take another point of damage. Implement this as a `float _invincibilityTimer` (approximately
1.5 seconds) on the player. When the timer is active, no damage is applied. This is both
authentic and prevents the otherwise brutal outcome of standing adjacent to an enemy and
losing all 8 points in two seconds at 60 FPS.

**Damage formula:**

```
On enemy contact:
  if _invincibilityTimer <= 0:
    _energy -= 1
    _invincibilityTimer = 1.5f
    // Visual flash: toggle player sprite visible/invisible at 0.1s intervals
    // during invincibility window
  if _energy <= 0:
    TriggerPlayerDeath()
```

**Player death sequence:**

Play the same `EnemyDeathSheet` animation centered on the player's position (the original
used the same explosion effect). Freeze all gameplay during the animation. After the animation
completes, either restart the current room with full energy (lives system — see below) or
show a game-over state.

**Lives: 3**

The original gave the player a finite number of attempts. Three lives is correct for Chapter 1
difficulty. Dying in a room with remaining lives resets the player to the room's entry spawn
point with full energy. Enemies in the room respawn (world state for that room is discarded).
Items that were picked up remain gone (they stay in `WorldState._pickedUpItems`). Losing the
last life triggers the game-over sequence.

### HUD Integration

The energy bar should be 8 sprites drawn in the info panel, not text. The existing
`_pixelTexture` can render a temporary colored rectangle bar (8 cells, each 12x12 px at 3x
scale = 36x36 px displayed) until proper energy bar sprites are extracted from screenshots.

### Dependencies

Requires Phase 4A's `WorldState` extraction to store `_energy` and `_lives`. Without the
refactor, adding these fields means adding more to an already overcrowded `Game1.cs`.

---

## Phase 4C: Sprite-Based HUD (Info Panel)

**Why this phase exists separately from 4B:** The info panel currently renders with debug
font strings. This is acceptable for testing but incorrect for the final game. However,
designing the HUD properly requires knowing what it needs to display — which requires the
energy system to exist first.

### What the Info Panel Must Show

Based on the original Sorcery+:

- **Wizard count**: Number of captive wizards saved (numeric display)
- **Energy bar**: 8 cells showing remaining energy
- **Carried item icon**: Large sprite of currently held item (already partially implemented)
- **Lives indicator**: Some representation of remaining lives

The panel area is 320x56 base pixels (960x168 at 3x scale). At this resolution there is
enough room for all four elements without crowding.

### Implementation

Extract a dedicated info panel spritesheet region from the original game's screenshots.
If no suitable source exists, construct the panel from the tile assets already in `Tiles.png`
using colored rectangles as placeholders. The key constraint is that the panel layout must
be defined in pixel-exact positions that match the original — do not invent a layout.

### Dependencies

Requires Phase 4B (energy/lives data must exist to display).

---

## Phase 4D: Flight Physics Authenticity

**Why this matters:** The current movement model — instant velocity, constant gravity at 120
px/s — produces movement that feels like a platformer, not the original game. The original
Sorcery+ used what can be described as "momentum flight": pressing Up applies upward thrust
that fights gravity, and releasing Up causes the character to gradually drift downward.
Horizontal movement had mild deceleration. This gives the game its distinctive floaty feel
and directly affects difficulty, since enemies and hazards were designed for this movement model.

**Why this is Phase 4D and not Phase 1:** The current physics are consistent and do not block
any other system. Getting flight physics wrong is a feel problem, not a functional problem.
But it needs to happen before room authoring scales up (Phase 5A), because platform heights,
enemy patrol ranges, and item placement in 47 rooms will be calibrated to whichever physics
model is in place.

### Authentic Physics Model

From analysis of the original game's behavior:

```
Constants (all in base px/s):
  GRAVITY          = 180.0   // Constant downward acceleration
  THRUST_FORCE     = 320.0   // Applied upward while Up is held
  H_SPEED          = 160.0   // Horizontal target speed (instant set)
  H_DECEL          = 600.0   // Horizontal deceleration when no input
  MAX_FALL_SPEED   = 200.0   // Terminal velocity
  MAX_RISE_SPEED   = 160.0   // Max upward speed
```

The vertical axis uses acceleration, not direct assignment:

```
if pressing Up:
    vel.Y -= THRUST_FORCE * dt
    vel.Y = max(vel.Y, -MAX_RISE_SPEED)
else:
    vel.Y += GRAVITY * dt
    vel.Y = min(vel.Y, MAX_FALL_SPEED)
```

The horizontal axis keeps the instant-set behavior from the original but adds deceleration
on release:

```
if pressing Left or Right:
    vel.X = target_direction * H_SPEED  // instant set
else:
    vel.X = approach(vel.X, 0, H_DECEL * dt)  // glide to stop
```

This is a breaking change to `PhysicsComponent` and `PlayerController`. The `IsOnGround`
check remains relevant because ground contact should zero vertical velocity (no bouncing).

### Balancing Consideration

After implementing flight physics, enemy speeds will need retuning. A guard at 80 px/s that
felt appropriately challenging against a player at 200 px/s direct may feel trivial or
impossible against a player with 160 px/s momentum-based movement. Playtest all five enemy
types against the new physics before locking speeds.

### Dependencies

Phase 4A (refactored physics layer makes this change cleaner). Can be done before 4B if
prioritized, since energy and physics are independent systems.

---

## Phase 5A: Room Authoring Pipeline and Batch Room Creation

**Why this becomes the priority after Phase 4:** With health, death, correct physics, and a
proper HUD, the game is fundamentally playable. The blocker to finishing is content: 5 of
~75 rooms exist. The question is how to build the other 70 without it taking 70x the time
the first 5 took.

### The Current Workflow Problem

Each screenshot-based room currently requires:
1. Take screenshot of original room
2. Manually identify collision boundaries in the image
3. Write a JSON collision grid by hand
4. Register the room in `RegisterBackgroundRooms()` in `Game1.cs` (after Phase 4A: in `RoomRegistry.cs`)
5. Add texture load call in `LoadContent`
6. Add entry to `Content.mgcb`

Steps 2 and 3 are the bottleneck. The collision grid for `collision_stonehenge.json` likely
took 20-30 minutes of careful pixel counting to produce. Multiply by 70 rooms = 35 hours of
mechanical labor before writing a single line of game logic.

### Collision Grid Tool

Build a standalone C# tool (can be a separate project in the same solution) that:
- Loads a room screenshot PNG
- Displays it at 8x zoom
- Lets you click tiles in a 40x18 grid overlay to mark them solid/empty
- Exports the JSON collision grid in the existing `RoomLoader` format

This tool does not need to be polished. It needs to be functional. A 2-day tool investment
that reduces each room's collision authoring from 30 minutes to 5 minutes saves 25+ hours
across the full game.

The grid format is already defined by `RoomLoader.BuildCollisionTileMap` — the tool just
needs to produce valid JSON matching that schema.

### Room Data Definition Format

After Phase 4A, rooms are defined by `RoomData` objects in `RoomRegistry.cs`. The format for
a new screenshot-based room should be:

```csharp
new RoomData
{
    RoomId = "room_forest_1",
    BackgroundAsset = "RoomBG_Forest1",         // Content name
    CollisionJson = "collision_forest_1.json",   // In assets/data/
    Doors = new List<DoorEntry>
    {
        new DoorEntry("forest1_right", DoorType.LeftOpening, new Vector2(296, 112),
                      "room_forest_2", "forest2_left"),
    },
    Enemies = new List<EnemySpawnEntry>
    {
        new EnemySpawnEntry("forest1_guard_1", "guard", new Vector2(120, 104)),
        new EnemySpawnEntry("forest1_mask_1",  "mask",  new Vector2(180, 60)),
    },
    Items = new List<ItemSpawnEntry>
    {
        new ItemSpawnEntry("forest1_wand", ItemType.Wand, new Vector2(200, 104)),
    },
    Wizards = new List<WizardSpawnEntry>
    {
        new WizardSpawnEntry("forest1_wizard", new Vector2(260, 80)),
    }
}
```

This is the entire room definition. No `Game1.cs` edit required. No switch cases.

### Connecting the Two Room Chains

Currently `room_1 <-> room_2` and `stonehenge <-> wastelands <-> tunnelmouth` are isolated.
The original game's Chapter 1 map is a connected graph. The first priority in room authoring
is establishing the spine — the main corridor of rooms the player traverses — and connecting
the existing 5 rooms into it.

### Room Creation Rate Target

With the collision grid tool and `RoomData` format in place, a realistic throughput is 3-5
rooms per development session. Chapter 1's 47 rooms become achievable in 2-3 weeks of
focused work rather than months.

### Content Pipeline Automation

Adding a new room background currently requires a manual `Content.mgcb` edit (10 lines per
texture) plus a new `Content.Load<Texture2D>` call and a private field. This is mechanical
and error-prone.

Instead: load room backgrounds at runtime using `Texture2D.FromStream` (the pattern already
exists in `LoadContent`'s fallback path) keyed to the `BackgroundAsset` string from
`RoomData`. Keep a `Dictionary<string, Texture2D> _backgroundCache` in `RoomManager`.
New rooms add zero lines to the content pipeline.

---

## Phase 5B: Cauldron, Healing, and Poison System

**Why this comes after rooms, not before:** The cauldron is a room-specific hazard/feature.
It needs rooms to live in. But it is the original game's primary energy management mechanic
and directly affects difficulty balance across the room set.

### Cauldron Mechanics (from original)

There are two cauldron types:

**Healing cauldron**: Player touches it while carrying the correct item (Bottle in the
original). Energy is restored by some amount (full restore or partial — needs screenshot
verification). The cauldron becomes inert after use.

**Poison cauldron**: Player touching it without the correct protective item takes damage
(same damage model as enemy contact). Some cauldrons are visually identical to healing ones
— this is intentional in the original design, creating risk/reward decisions.

### Implementation

Add `CauldronType` to the item system. Cauldrons are not items the player carries — they are
placed entities in rooms. Add a `CauldronInstance` class (similar to `BlockedDoorInstance`)
with a position, type, sprite, and `IsUsed` flag. Register cauldrons in `RoomData`.

The interaction model: player walks into cauldron hitbox. If healing type and player has
correct item: restore energy, mark cauldron used. If poison type and no protection: apply
damage with invincibility timer.

### Balancing Formula

Energy restoration should scale with room depth. Early rooms (stonehenge, forest entrance)
have reliable healing cauldrons. Deeper rooms have more ambiguous cauldron placement, more
poison risk. This creates a risk curve:

```
Expected energy available per chapter sector:
  Rooms 1-10:  2 healing cauldrons, 0-1 poison
  Rooms 11-25: 1-2 healing, 1-2 poison
  Rooms 26-47: 0-1 healing, 2-3 poison, hidden behind key items
```

These numbers need verification against original screenshots, but the principle — increasing
energy scarcity as the player goes deeper — is correct for a 1985 single-chapter adventure game.

### Dependencies

Requires Phase 5A's `RoomData` system (cauldrons are defined per room). Requires Phase 4B's
energy system (nothing to restore without energy).

---

## Phase 5C: Full Item Set Integration

**Why this is Phase 5C and not earlier:** All 14+ item sprites are already extracted and
sitting in the Content folder unused. The sprites are ready. The system is not. The `ItemType`
enum, `GetItemTexture`, and `GetItemSourceRect` methods must be extended, and the
weapon-enemy matrix must cover the new items. This is straightforward once Phase 4A has
moved the item system to its own file.

### Items to Integrate

From the available sprites: Bag, Bottle, Chalice, Cup, Parchment, Wand, Key, Coat, Book,
Flaire, Moon, Water, Fountain, Gateway.

Not all of these are weapons. Categorize them:

**Weapons (expand `CanKillEnemy`):**
- Wand: likely kills certain enemy types not covered by existing weapons
- Moon: potential weapon against specific undead-type enemies

**Key items (expand blocked door system):**
- Key: generic door unlocker (complements Lyre for different door types)
- Coat: protective item (cauldron poison immunity)

**Consumables:**
- Bottle: used at healing cauldrons
- Water: potentially a room-state modifier

**Quest items:**
- Parchment: likely displays a message or advances plot
- Book: similar to Parchment

**Room interaction items:**
- Chalice, Cup: specific room puzzles
- Fountain: room feature interaction
- Gateway: Chapter 2 transition

### Weapon-Enemy Matrix (Full)

The current `CanKillEnemy` uses string.Contains() on enemy IDs. After Phase 4A, enemies
should carry an `EnemyType` enum field. The matrix becomes:

```
EnemyType.Guard   -> ItemType.Sword
EnemyType.Mask    -> ItemType.BallAndChain
EnemyType.Boar    -> ItemType.BallAndChain
EnemyType.Eye     -> ItemType.BallAndChain
EnemyType.Wraith  -> ItemType.Axe
// Future:
EnemyType.Dragon  -> ItemType.Wand      (verify against original)
EnemyType.Demon   -> ItemType.Flaire    (verify against original)
```

Wrong weapon on an enemy: no kill, no weapon loss. Correct weapon: kills enemy, consumes
weapon (existing behavior). ShootingStar remains the AOE exception.

### Dependencies

Requires Phase 4A (`ItemSystem.cs` as a proper class). Requires Phase 5A (items need rooms
to appear in).

---

## Phase 6A: Timer and Crumbling Book System

**Why this comes last in the main game loop:** The crumbling book is a game-wide countdown
that creates urgency. It is a completion mechanic — it only makes sense to implement once
there are enough rooms to traverse that the countdown actually creates tension. With 5 rooms,
a 3-minute timer is meaningless. With 47 rooms, it becomes a core design pillar.

### Mechanics

A visible timer counts down from a fixed value (verify against original — likely 20-30
minutes for Chapter 1). The timer represents the book of spells crumbling. When it reaches
zero, the game ends regardless of player health. The timer creates two competing pressures:

1. Explore thoroughly to save wizards and collect items
2. Move fast enough that the book doesn't run out

This tension is the game's core loop. Healing, wizard saving, and item collection all become
more meaningful with a real time constraint.

### Display

The timer should appear in the info panel. A minutes:seconds display in the original's
style. The crumbling effect (visual degradation of the timer display at low values) is an
enhancement — get the functional timer first.

### Dependencies

Requires Phase 5A (enough rooms to make the timer meaningful). Requires Phase 4B (player
death on timer expiry uses the same death/game-over flow).

---

## Phase 6B: Audio

**Why last:** Audio has zero gameplay dependencies but high implementation overhead. MonoGame's
`SoundEffect` and `Song` APIs are straightforward, but sourcing authentic audio from
screenshots is impossible — audio requires the original game's sound data or recreation from
scratch. This is the most uncertain phase from a sourcing standpoint.

### What is needed

- Background music per room area (the original had distinct tracks for different zones)
- Sound effects: door open, item pickup, enemy death, player damage, wizard rescue,
  projectile fire

If ROM extraction is off the table, audio must be recreated as chiptune compositions in the
style of the original Amstrad CPC. This is a specialist task (musician or tracker software)
separate from the development workflow.

**Implementation placeholder:** Add a `AudioManager.cs` stub with empty method signatures
(`PlaySfx(SfxId id)`, `PlayMusic(MusicId trackId)`, `StopMusic()`) now. When audio assets
are ready, the stub becomes real. This prevents future audio integration from requiring
changes across every system that should trigger a sound.

---

## Phase 6C: Title Screen, Menus, and Game Over

**Why last:** These are player-facing polish that require the game beneath them to be complete.
A title screen for a game with 5 rooms and no health system is premature.

### What is needed

- Title screen with the original's visual style (wizard sprite, logo, press-start prompt)
- Chapter selection (Chapter 1 / Chapter 2)
- Game over screen (death animation, score display, restart option)
- Pause screen with carried item display and wizard count

These are all `GameState` enum values managed by a state machine wrapping `Game1`. The
current `Game1` is always in "playing" state. The state machine adds `TitleScreen`,
`Playing`, `GameOver`, `Paused` as states with corresponding update/draw paths.

---

## Phase 7: Save/Load System

**Why this comes after everything else:** Saving requires knowing what the complete game
state looks like. The `WorldState` class from Phase 4A is the serialization target. Once
`WorldState` contains everything that matters (energy, lives, wizard count, timer,
dead enemies, picked items, unlocked doors), saving becomes:

```csharp
File.WriteAllText(savePath, JsonSerializer.Serialize(_worldState));
```

Loading is the reverse. The architecture is correct for this as soon as `WorldState` is
extracted in Phase 4A. The implementation is deferred until the game state is stable enough
that save file format changes stop being needed.

One slot is sufficient for an authentic recreation. The original had no save at all (it was
a 30-minute arcade-style game). A single auto-save on room transition is the right call —
authentic enough, and helpful for modern play sessions that may be interrupted.

---

## Immediate Next Step (This Week)

**Phase 4A is the only correct starting point.** Everything else described above becomes
harder or more fragile without it.

Specifically, start with these three extractions in this order:

1. `WorldState.cs` — pure data, no dependencies, enables everything else
2. `ItemSystem.cs` — move enum, textures, source rects, kill matrix
3. `RoomRegistry.cs` with `RoomData` — move all spawn switch statements

Fix the `DrawFilledRectangle` allocation bug as a zero-cost change while touching `Game1.cs`.

After those three are done, Phase 4B (energy system) can be implemented cleanly in under a
day because its data home (`WorldState`) already exists and its display home (`HudRenderer`)
is ready for it.

---

## Phases at a Glance

| Phase | Name | Blocks | Estimate |
|-------|------|--------|----------|
| 4A | Refactor Game1.cs | Everything | 3-4 days |
| 4B | Energy + Death + Lives | Room balance | 2-3 days |
| 4C | Sprite HUD | Polish | 1-2 days |
| 4D | Flight Physics | Room design | 2-3 days |
| 5A | Room Pipeline + Batch Rooms | Content scale | 2 weeks |
| 5B | Cauldron + Healing | Difficulty | 2-3 days |
| 5C | Full Item Set | Puzzle rooms | 3-4 days |
| 6A | Timer / Crumbling Book | Completion | 2 days |
| 6B | Audio | Polish | varies |
| 6C | Title + Menus + Game Over | Ship condition | 1 week |
| 7  | Save/Load | Convenience | 2-3 days |
