# 00 — Game Design Document (The Original *Sorcery* / *Sorcery+*)

> **Purpose.** This document describes the **original 1985 game** as the
> authoritative design target the remake reproduces. Where `doc/01`–`doc/12`
> describe *how the remake is currently built* (and are sometimes ahead of or
> behind the original), **this file is the spec they should conform to.** When
> the remake and this document disagree, this document wins on questions of
> *authentic design intent* — and any disagreement should be logged in §11.
>
> **Sourcing & confidence.** Facts below are tagged:
> **[C]** confirmed by multiple independent sources · **[S]** single-source or
> community-consensus · **[U]** uncertain / needs emulator verification ·
> **[R]** remake-side assumption not yet verified against the original.
> Sources are listed in §12. Nothing here was taken from memory without a
> source; unverifiable specifics are called out rather than invented.

---

## 1. Identity & History

| Attribute | Value | Conf. |
|-----------|-------|-------|
| Title | *Sorcery* (base) / *Sorcery+* ("Sorcery Plus") | [C] |
| Original platform | ZX Spectrum (1984) | [C] |
| Original author (Spectrum) | Martin Wheeler (reportedly a schoolboy) | [S] |
| CPC/C64 developer | **Gang of Five** | [C] |
| Publisher | **Virgin Games** (CPC budget re-release via Amsoft/Amsoft Gold) | [C] |
| CPC release year | **1985** | [C] |
| Target machine (Sorcery+) | Amstrad CPC **6128 / 128K, disk only** | [C] |
| Sound hardware | AY-3-8912 PSG (3 channels) | [C] |

**The lineage.** *Sorcery* began as a ZX Spectrum game by Martin Wheeler
(1984, Virgin). Gang of Five took it over and "beefed it up" for the Commodore
64 and Amstrad CPC (1985). *Sorcery* was a flagship CPC hit — used in Amstrad's
own advertising as a showcase of the machine's graphics. **[C]**

**Sorcery vs. Sorcery+.** *Sorcery+* is the **128K disk-only enhanced edition**
for the CPC. It contains an updated version of the original quest **plus a
second chapter/scenario that only unlocks after completing the first**, with
improved graphics and altered audio (notably changed door and death sound
effects). **[C]** The remake targets *Sorcery+*.

---

## 2. Premise & Objective

You are **one of the last nine sorcerers**. The other **eight have been
imprisoned by an evil necromancer**. Your quest: **fly through the world, find
and free all eight captive sorcerers**, then — powerful enough at last —
**confront the necromancer**. Each rescued sorcerer **increases your power**;
all eight are needed for the final confrontation. The world is populated by
**demons, ghosts, and witches**. **[C]**

- **Win condition:** rescue all 8 sorcerers (and, in the full game, defeat the
  necromancer / complete the second chapter). **[C]**
- **Time pressure:** the quest is against the clock, visualised as a **book that
  slowly erodes/crumbles away**. When it is gone, time is up. **[C]**  *(The
  "Cauldron of Immortality" framing sometimes cited for the story is **[U]** —
  not confirmed by the sources consulted; treat as flavour pending
  verification.)*

---

## 3. World Structure (Screens / Rooms)

Sorcery is a **flip-screen** ("room at a time") action-adventure: each screen is
a self-contained tableau; exits via doors flip to the adjacent screen. **[C]**

| Measure | Value | Conf. |
|---------|-------|-------|
| Original *Sorcery* screen count | **17 screens** (Stonehenge cited as the last) | [S] |
| *Sorcery+* additional screens | **+38 new screens** (enhanced ch.1 + new ch.2) | [S] |

> ⚠️ **Room-count discrepancy to resolve.** The remake's own docs assume a
> **75-room map (47 in Chapter 1, 28+ in Chapter 2)** **[R]**. The community
> sources consulted here instead say the base game had **17 screens** and
> *Sorcery+* **added 38** **[S]**. These do not obviously reconcile. Before mass
> room authoring (roadmap Phase 5A), **the true screen count and map graph must
> be verified** against a mapped source — cross-reference the room maps on
> VGMaps / CPC-Power and, ideally, count screens directly in an emulator. Do not
> commit the "47/28/75" figures to design until confirmed; they may be an
> earlier estimate rather than a sourced fact.

**Named/known areas (from remake screenshots + community maps):** a Chateau
interior sequence, Stonehenge, Wastelands, a Tunnel Mouth. **[S/R]**  The full
area list is part of the map-verification task above.

---

## 4. Movement & Controls (The "Flight" Model)

The sorcerer **does not walk — he flies**, constantly fighting gravity, and
drifts/falls when not thrusting. This flight feel is the game's signature. **[C]**

| Input | Action | Conf. |
|-------|--------|-------|
| Joystick / arrows | Directional flight (thrust up, dive, move L/R) | [C] |
| Fire button | Use the currently-held **weapon** | [C] |
| (automatic) | Non-weapon items are **used automatically** when relevant (e.g. a key at the matching door) | [S] |
| Pause | Supported | [S] |

- The character is **always pulled downward**; holding "up" thrusts against
  gravity. Releasing lets him fall. **[C]** *(Exact acceleration curve — true
  momentum vs. direct-velocity — is **[U]**; see §11 / remake Phase 4D.)*
- **Instant-death hazard:** **falling into water ends the game immediately**,
  regardless of energy. **[C]**

---

## 5. Energy, Lives & Death

| Element | Behaviour | Conf. |
|---------|-----------|-------|
| Energy | A depletable meter; **contact with enemies drains it** | [C] |
| Energy restore | **Cauldrons** refill energy (see §7) | [C] |
| Poison | **Some cauldrons drain energy instead** — visually similar; the player must remember which | [C] |
| Death (energy = 0) | Ends the current attempt | [C] |
| Instant death | **Falling into water** — immediate, bypasses energy | [C] |
| Energy cell count | Original shows a segmented energy indicator in the status panel | [U] |
| Lives count | A finite number of attempts | [U] |

> **[U] to verify in emulator:** the exact number of energy segments, whether
> there are discrete "lives" vs. a single life per attempt, and how much a
> healing cauldron restores (full vs. partial). The remake currently *plans*
> an 8-cell bar / 3 lives **[R]** — plausible but **not yet source-confirmed**.

---

## 6. Inventory & Items

### 6.1 The one-item rule
The sorcerer **carries a single item at a time** — picking up a new one
**swaps** it for what he's holding. This constraint drives the whole puzzle
economy (what to carry, what to leave, and where). **[S/R]** *(Universally
reproduced in descriptions and the remake; worth one emulator confirmation of
the exact swap/drop behaviour.)* **[U]** on precise drop mechanics.

### 6.2 Item catalogue (original)
Compiled from community item lists (multiface "give object" tables) and reviews.
Names vary slightly between sources; canonical in-game naming is **[U]**.

| Item | Role (best current understanding) | Conf. |
|------|-----------------------------------|-------|
| Strong **Sword** | Weapon | [S] |
| Wooden **Club** | Weapon | [S] |
| Sharp **Axe** | Weapon | [S] |
| **Mace** / Ball & Chain | Weapon | [S] |
| **Bow and arrow** | Weapon (ranged) | [U] |
| **Magic wand** | Weapon / magic | [S] |
| **Shooting star** | Area/spell attack | [S] |
| Spell **book** / **scroll** | Magic / quest | [S] |
| **Clove of garlic** | Protective (vs. an undead enemy?) | [U] |
| **Fleur de lys** ("Flaire") | Key-type / special | [S] |
| **Door key** | Opens locked doors | [S] |
| Large **bottle** | Consumable (cauldron-related?) | [U] |
| **Jewelled crown** | Quest / value item | [S] |
| **Chalice / Cup / Fountain / Water / Moon / Coat / Bag / Gateway** | Various (room puzzles, protection, ch.2 transition) | [U/R] |

> **Item-name mapping to the remake's extracted sprites.** The remake already
> has sprites for Bag, Bottle, Chalice, Cup, Parchment(scroll), Wand, Key,
> Coat, Book, **Flaire = Fleur de lys**, Moon, Water, Fountain, Gateway. The
> per-item *function* is the open question, not the art. Confirming each item's
> true effect (§11) is the Phase-5C prerequisite.

### 6.3 Doors
Some doors are **locked and require a specific item** to pass. The pairing is a
memory puzzle — the player must deduce which item opens which door. **[C]**

---

## 7. Cauldrons (Healing & Poison)

A core resource/memory mechanic:

- **Healing cauldron:** restores energy. **[C]**
- **Poison cauldron:** *drains* energy — and can look identical to a healing
  one. **[C]** The player must **remember which cauldrons are safe**. This is
  the game's signature "memory-based puzzle." **[C]**
- Whether a protective/consumable item (garlic? coat? bottle?) neutralises
  poison or is required to drink safely is **[U]**.

---

## 8. Combat — Weapon/Enemy Matrix

Combat is **weapon-selection puzzle, not twitch**: **different enemies are
vulnerable to different weapons, and the wrong weapon simply doesn't work.** The
player must remember the correct pairing. **[C]**

- Enemy families named in sources: **demons, ghosts, witches** (plus the
  remake's Guard/Mask/Boar/Eye/Wraith interpretations). **[C]** on the broad
  families; **[U]** on the exact original bestiary and their sprite identities.
- The **exact weapon→enemy table is not documented in the sources consulted**
  and **must be derived from the original** (emulator observation or an
  authoritative FAQ). The remake's current matrix (Sword→Guard,
  Ball&Chain→Mask/Boar/Eye, Axe→Wraith, Shooting-Star = AoE) is a **[R]**
  working model, **not** a verified reproduction. **[U]**

> **This matrix is the single most important gameplay table to verify** — it is
> the backbone of the whole game and every room's difficulty is tuned around it.

---

## 9. The Timer — Crumbling Book

A **global countdown** rendered as a **book that erodes/crumbles**. It runs the
whole session; when it expires the game ends whether or not the player has died
or finished. It creates the core tension: **explore thoroughly vs. move fast.**
**[C]** Exact duration is **[U]** (needs emulator timing).

---

## 10. Presentation & Technology (Original CPC)

| Aspect | Value | Conf. |
|--------|-------|-------|
| **Split video mode** | **Play area in Mode 0 (160×200, 16 colours); status/HUD panel in Mode 1 (320×200, 4 colours)** | [S] |
| Why it matters | Mode 0 gives chunky, colourful sprites for the play field; Mode 1 gives finer horizontal resolution for crisp status text/graphics. The CPC switches mode mid-screen (raster split). | [S] |
| Palette | 16 on-screen colours (Mode 0 area) from the CPC's 27-colour hardware palette | [C] |
| Sound | AY-3-8912, 3 channels; *Sorcery+* altered some SFX (door, death) | [C] |
| Screen model | Flip-screen, one tableau per screen | [C] |

> ⚠️ **Authenticity note that affects the remake's rendering contract.** The
> remake's `doc/10_RENDERING.md` and `CLAUDE.md` treat the **entire** frame as
> "Mode 0, 320×144 game area + 320×56 panel, 3× scale" **[R]**. The original is
> **mixed-mode**: the *game area* is Mode 0 at **160** px wide (not 320), and
> the *panel* is Mode 1 at **320** px wide. The remake's choice to run the play
> field at 320-wide effectively doubles the horizontal pixel budget vs. the
> original's chunky Mode-0 look. This is a **deliberate-or-accidental deviation
> that should be decided consciously** (see §11): either (a) accept the
> higher-res play field as a remake liberty, or (b) render the play field in
> true 160-wide Mode-0 chunky pixels for pixel-exact authenticity. It changes
> how sprites and rooms are authored, so decide **before** mass content work.

---

## 11. Open Questions / Verify-Against-Original Backlog

These are the specifics this document could **not** source authoritatively.
Each should be resolved by direct emulator observation or an authoritative FAQ,
then folded back into this file (and the relevant `doc/` file) with a source.

1. **True screen/room count and map graph** (§3). Reconcile "17 base + 38 in +"
   vs. the remake's "47+28=75". Highest content-blocking priority.
2. **Exact weapon→enemy matrix and the real bestiary** (§8). Backbone of combat.
3. **Energy model numerals** (§5): cell count, lives count, heal amount.
4. **Cauldron protection item** (§7): does garlic/coat/bottle gate safe use?
5. **Per-item true function** for the full catalogue (§6.2), esp. garlic,
   fleur-de-lys, crown, bottle, moon, coat, gateway.
6. **Flight physics character** (§4): momentum vs. direct-velocity; gravity and
   thrust values (remake Phase 4D hinges on this).
7. **Crumbling-book duration** (§9).
8. **Mixed video mode decision** (§10): reproduce 160-wide Mode-0 play field, or
   keep the remake's 320-wide field as a conscious liberty.
9. **Story framing** (§2): confirm/deny the "Cauldron of Immortality" premise.
10. **Individual Gang of Five credits** (§1) — nice-to-have for the about screen.

**Recommended verification method:** run *Sorcery+* in an accurate CPC emulator
(WinAPE / CaPriCe Forever / RetroVirtualMachine) against a known-good `.dsk`,
capture screen-by-screen reference, and cross-check community maps
(VGMaps, CPC-Power). The repo already contains `extraction/` disk assets and a
Python extraction pipeline that can assist.

---

## 12. Sources

Primary community references consulted (July 2026). CPCWiki and Wikipedia block
automated fetches; their content was accessed via search snippets and the
secondary sources below.

- CPCWiki — *Sorcery +*: https://www.cpcwiki.eu/index.php/Sorcery_+
- The King of Grabs — *Sorcery Plus, Amstrad CPC*: https://thekingofgrabs.com/2022/04/11/sorcery-plus-amstrad-cpc/
- Time Extension — *Sorcery (1985)*: https://www.timeextension.com/games/amstrad/sorcery
- Retro Arcadia — *Discovering Sorcery on Amstrad CPC*: https://retroarcadia.blog/2025/10/29/discovering-sorcery-on-amstrad-cpc/
- UVList — *Sorcery / Sorcery+ (Gang of Five, 1985)*: https://www.uvlist.net/game-49261-Sorcery
- MobyGames — *Sorcery (1984)* / *Sorcery+ (1985)*: https://www.mobygames.com/game/53475/sorcery/
- Andy's Retro Computing — *Sorcery Plus Multiface Codes* (item/"give object" list): https://retro.m1ner.co.uk/amstrad-cpc-guides/multiface-2-codes/sorcery-plus/
- CPC-Power — *Sorcery* (notice/manual scans, maps): https://www.cpc-power.com/index.php?page=detail&num=1988
- CPCRULEZ — *Sorcery Plus* test/review: https://cpcrulez.fr/GamesTest/sorcery_plus.htm
- The Spriters Resource — *Sorcery* (CPC sprite rips): https://www.spriters-resource.com/amstrad_cpc/sorcery/
- VGMaps — Amstrad CPC map atlas: https://www.vgmaps.com/Atlas/CPC/index.htm

> Confidence tags in this document reflect these sources. Facts tagged **[U]**
> or **[R]** are explicitly *not* settled and must not be treated as canon until
> verified per §11.
</content>
