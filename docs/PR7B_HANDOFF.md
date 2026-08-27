# PR 7b — Authoring speed: undo, pickers, rename: hand-off

Branch `pr7b-authoring-speed`, four commits off `main`. Where PR 7a rebuilt the
chrome at parity and added nothing, this spends the capability: `Ctrl+Z` now
reaches every kind of edit, the inspector's three list fields are filterable
dropdowns instead of cycle-buttons, a room's display name is editable, and three
smaller debts are paid. `EDITOR_REVIEW` items **10** and **11** are done.

Editor-only. The game's `deps.json` still contains no reference to ImGui
(verified: `grep -c -i imgui bin/Debug/net8.0/SorceryRemake.deps.json` → 0).

---

## 1. Unified undo/redo

`Ctrl+Z` undid exactly one thing before this: a background brush stroke.
Dropping an entity, nudging it two pixels, retargeting a door, painting a wall,
setting the spawn — all permanent the moment they happened, with no way back
from a wrong nudge except remembering where the thing used to be.

**The shape.** Every action is an object that knows how to do itself and how to
take itself back — `IEditorCommand { Label; Do(ctx); Undo(ctx); }` — on one
stack, with a redo stack beside it. `Ctrl+Z` walks it backwards, `Ctrl+Y` (and
`Ctrl+Shift+Z`) forwards, and neither knows what kind of action it is holding.
That is the point of the pattern: the alternative is a switch over "what was the
last thing", which grows a case per feature and silently omits the one somebody
forgot.

| Command | Recorded when |
|---|---|
| `AddPlacementCommand` | a palette entry is dropped on the canvas |
| `DeletePlacementCommand` | `Delete` with a placement selected |
| `MovePlacementCommand` | a placement drag is **released** — once, not per frame |
| `SetPlacementFieldCommand` | one applied inspector change (all editable fields together) |
| `SetPlayerSpawnCommand` | the spawn is set, moved or cleared |
| `PaintTilesCommand` | a paint drag **ends** — every cell it changed, in one command |
| `BackgroundEditCommand` | an erase/restore stroke ends, or a punch happens |
| `CompositeCommand` | a drop or move that also auto-punched: both halves, one entry |

The status line names what happened: `Undid: move chateau_0_sword_2 (3 more,
Ctrl+Y redoes)`. **Edit > Undo / Redo** carry the same shortcuts and are greyed
when their stack is empty.

### The four rules, and why each is written down

1. **A new edit clears the redo stack.** Otherwise `Ctrl+Y` could replay a move
   onto a placement the new edit deleted.
2. **The stack caps at 64 and evicts the oldest.** An unbounded stack in a
   program with no save-points is a promise it cannot keep.
3. **A room switch clears BOTH halves.** The load-bearing one. `LoadRoom`
   rebuilds `Placements` from disk — every `Placement` object is replaced — and
   commands hold *references* to those objects. A command surviving the switch
   would, on `Ctrl+Z`, write to an object no longer in any room's list: no
   crash, no visible effect, and the edit the author thought they took back
   still there. Keying commands by entity id instead would only move the failure
   to "the id now names a different object with different fields".
4. **Undo and redo always dirty what they touch**, in both directions. Undoing
   back to exactly the last-saved state still shows `room*`. Conservative on
   purpose: the cost is one redundant save, and the opposite error loses work.

### One user action, one Ctrl+Z

With auto-punch **on**, one click drops a door *and* cuts a hole under it. Two
commands would need two `Ctrl+Z` presses to take back one click, and the first
of them would leave a hole with a door still standing in it — a state the author
never asked for and cannot reach any other way. `CompositeCommand` runs its parts
forward and **reverses them on undo**, which is what makes the composite itself
invertible. Same shape for a move-release that re-punches.

### Background history stores a rectangle, not an image

The old scheme kept a full 320×144 `Color[]` per stroke — 180 KB each, forty
deep. Doubling that for redo at a 64-deep cap would be ~23 MB of mostly identical
pixels. A `BackgroundEditCommand` stores the **tight bounding box** of the pixels
that actually differ plus its before/after: a punch is 24×24 (~2 KB), and the
pathological case (a stroke across the whole image) costs exactly what the old
scheme charged for every stroke. The full clone taken while a stroke is running
is transient and is not retained.

### What is deliberately NOT undoable

- **The world-map arrangement.** Not per-room working state; it survives every
  room switch, which is the event that clears this stack. Folding it in would
  mean a stack that some clears apply to and some do not. Undo is disabled on
  the board for the same reason `F11` is: `Ctrl+Z` is read in
  `HandleKeyboardShortcuts`, which map mode never reaches.
- **Saving**, and **the room rename**. Both write a file the moment they happen;
  undoing them would mean rewriting the previous file, which is version
  control's job.
- **Room creation and screenshot import.** Both register a room and then load
  it, and loading clears the stack by design.

---

## 2. Filterable pickers

Cycling `TargetRoomId` one click at a time is fine for nine rooms and unusable
for seventy-five — unusable in the way that never gets filed, because it only
gets worse and never breaks.

Click the value box on **Room**, **Door** or **Needs**: a popup opens with the
filter box already holding the keyboard. Type to narrow, `Enter` takes the top
hit, a click takes the row, `Esc` closes. The value already set wears the
selected colour, so "what is this pointing at now" is answerable from inside the
picker.

**`Opens` still cycles.** Two values need no list — a dropdown there would be two
clicks and a popup to do what one click already does.

**Filtering is substring, case-insensitive.** Door ids carry their room as a
prefix (`chateau1_door_topright`), so a prefix match would make `topright` find
nothing — and the second half of the id is the half anyone remembers.

**The row is unchanged.** Same 16 px label over the same full-width 22 px value
box, same press-edge hit region, same geometry constants. Only what the click
*does* is different, which is why ChromeCheck's row arithmetic still finds every
field where it was. Not an ImGui `Combo`: that brings its own arrow, its own
frame padding and its own release-edge click, none of which is what this panel
looks or behaves like.

**The verbs changed shape.** `IChromeActions` now names the value it wants
(`SetDoorTargetRoom` / `SetDoorTargetDoor` / `SetBlockedDoorRequiredItem`)
instead of asking for "the next one", and the logic side no longer owns an
ordering that existed only to make cycling bearable. The target-room blanking
stays on the logic side, where a panel cannot forget it — and both writes are
one command, so `Ctrl+Z` restores the pair.

Two decisions carried over verbatim from the cycles: **test rooms are not
offered** (dev scaffolding; the door validator's `ok-test` verdict exists to
*tolerate* hand-edited data pointing at one, not to author it), and
**`ItemType.None` is not offered** (a blocked door requiring nothing is broken
data, and the cycle skipped it too).

### Modality — measured, not assumed

PR 7a had to make the three bands `NoInputs` by hand for the modal pickers,
because an ImGui *window* does not stop hit-testing because something is drawn
over the middle of the screen. An ImGui *popup* is different: while one is open
ImGui reports other windows' content as not hoverable, so their widgets never
see the click. The whole of the inspector picker's modality rests on that, so
ChromeCheck asserts it rather than reading it out of the library's source —
**with a picker open, a click on a palette row does not start a drag; it closes
the popup, and the palette answers again the moment it is closed.**

---

## 3. Rename, and two debts

### Display-name rename

The inspector carries an always-present **ROOM** block at the top of its list: a
text field for `displayName`, and a dim `id <roomid> — fixed` note under it.
`Enter` or a click away applies; `Esc` reverts. It writes through
`RoomManifest.Save`, so the header comment survives verbatim and the array order
is untouched — one field of one row changes.

**Why always-present, and not the two alternatives the brief offered.** A modal
would be a fourth entry in the `ModalOpen` set with its own `NoInputs` handling
for the three bands, its own Escape path and its own overlay — a lot of new
modality for one text field, in the part of PR 7a that took the longest to get
right. Shown-only-when-nothing-is-selected would appear and disappear on every
canvas click, moving everything below it by 96 px each time; the inspector is a
list you scroll and click in, and a list whose contents move when you select
something is a list that hands you the wrong row. Always-present costs 96 px and
costs it only until you scroll, because the block lives *inside* the scrolling
child rather than being pinned furniture.

**Empty names are refused up front.** `RoomManifest.LoadAll` substitutes the room
id for a blank `displayName`, so writing one would look like it had worked and
would quietly rename the room to its own id.

**It does not reload the room.** Renaming touches `rooms.json` and nothing else,
so calling `LoadRoom` would throw away every unsaved edit in the room for a
cosmetic change. Reloading the two catalogues is enough.

**Room ID rename is out of scope**, and `doc/07` says so where an author will
look for it. An id is a persistence key (`WorldState` remembers *entity* ids
built from it), three file names, every other room's `targetRoom`, and a
`worldmap.json` key. Doing it properly is a migration; a text field that did a
third of it would be worse than none.

### `NewRoomFlow.Create` can no longer be held wrong

It built the new registry from the **cached** `RoomManifest.All`, so two Creates
without a `Reload` between them both started from the same list and the second
write silently dropped the first room. Nothing failed, nothing warned; a room
simply was not there. The editor never hit it because `CreateAndOpenRoom`
reloads after every file — which is a caller *remembering* something, and the PR
5b hand-off said so.

Create now reads `rooms.json` from the directory it is about to write, at the
moment it writes it, and **re-checks the derived id against it** (a candidate is
computed when a picker *opens*; a batch can create a colliding room before the
click). `RoomManifest` gained a public `LoadFrom(path)` for it — the cached `All`
is right for readers and wrong for every writer, and both writers now use it.

`tools/ImportCheck`'s 5b assertion is **inverted, not deleted**: it pinned the
hazard, and it now pins the fix and fails the moment Create reads a snapshot
again.

### `Ctrl+S` names what it wrote

```
Saved chateau_0: content + layout + PNG — rebuild (dotnet build) for the game to see the PNG.
```

Each part appears only when the write that produced it reported back. The
rebuild note rides on `PNG` **alone**, because the background is the only part of
a save the *game* cannot see until the content pipeline runs again — the silent
ride-along the PR 4b smoke pass found. Short names rather than file names because
the room id is stated once, in front, and four full file names did not fit the
status bar beside the view-info group.

---

## 4. The keyboard rule finally has something to gate

PR 7a built `ChromeInputRouter.KeyboardReachesEditor` on `io.WantTextInput`
rather than `io.WantCaptureKeyboard` — for a chrome that had **no text field at
all**. Every assertion about it until now was about the false branch.

This PR ships two kinds of text field, and they exercise different halves:

- **the pickers' filter boxes** live inside ImGui popups, so `ImGuiPopupOpen`
  covers them;
- **the ROOM block's name field** is a plain band widget with no popup over it,
  so `WantTextInput` is the *only* thing standing between the author's typing and
  `P`, `Delete`, `[`, `]`, `N`, `I` and `A` firing as editor keybinds.

That distinction was measurable, and it caught a hole: with only the pickers in
place, **deleting the `WantTextInput` term entirely left every assertion
passing**, because every field lived in a popup. ChromeCheck now pins the
router's truth table directly — including that `WantCaptureKeyboard` alone is
**not** the rule, which is 7a's trap as a negative — and section 16 fails three
assertions if the term goes.

**Escape is a two-step inside a picker**, and it costs a line to be one. ImGui's
`InputText` handles Escape itself, and `ImGui.IsKeyPressed` is not owner-aware,
so a naive `if (Escape) CloseCurrentPopup()` would fire on the *same* press that
defocused the field and collapse the two steps into one. `FilterPopup` reads both
flags at the top of the body, before the `InputText` that clears them. Neither
press ever reaches the editor's Escape — which on a clean room is `Exit`.

---

## 5. Divergences — the complete list

Everything below is a deliberate, known difference from `main`. Nothing else was
intended.

### Undo

1. `Ctrl+Z` no longer means "undo a background stroke". It undoes the last edit
   of any kind, so a session that erased pixels and then moved a placement now
   takes the *move* back first.
2. The background history depth changed from 40 stroke snapshots to 64 entries
   of every kind, shared.
3. `Ctrl+Y` and `Ctrl+Shift+Z` are new. `Ctrl+Shift+Z` is deliberately not
   advertised in the menu — one accelerator per item, and `Ctrl+Y` is the one
   documented.
4. An inspector field edit now **selects its placement and expands its section**.
   It used to touch neither. An action that can be replayed by `Ctrl+Z` has to be
   visible when it is replayed, and a rule that applied only to the undo half
   would make `Do` and `Undo` asymmetric — the exact defect `tools/EditCheck`'s
   round-trip property exists to catch.
5. An inspector field edit now clears `HasValidated` as well as
   `HasValidatedDoors` — one flag-clearing rule for every placement edit, at the
   cost of re-running a validator that would have given the same answer.
6. A move that ends where it started records nothing. It used to mark the room
   dirty on any release; now `PlacementsDirty` is only set while the position
   actually differs (which was already true frame-by-frame) and no undo entry is
   pushed for a click that changed nothing.
7. A paint drag is one undo entry and now has an **end**: `_paintStroke` joins
   `WorldGestureInProgress`, so a release over a panel is seen. Painting has
   never been able to *start* off-canvas and still cannot.
8. **`Ctrl+Z` / `Ctrl+Y` with a drag still held now COMMITS that drag first**,
   so the press takes back the drag itself rather than an older edit. On `main`
   the drag survived the keypress and its release committed normally; the first
   version of this branch cleared the drag flags without recording, which
   stranded it. See §7b.
9. `Tab` to the world map closes any open paint or erase drag first, so it is
   recorded rather than merging with the next one.
10. `Delete`'s status line gained "— Ctrl+Z undoes.", and the spawn-clear message
    gained the same.

### Pickers

11. `Room`, `Door` and `Needs` open a popup instead of advancing one value.
    Clicking them no longer changes anything by itself.
12. Their popups make the rest of the chrome inert while open (ImGui's doing,
    asserted here). A click outside closes the popup and does **not** also
    activate what it landed on.
13. The wheel over an open picker scrolls the picker's list.
14. `(none)` is an entry in the `Room` and `Door` lists rather than a position in
    a cycle. It stores the empty string, exactly as the cycle's empty entry did.

### Rename and status

15. The inspector's list is 96 px taller: the ROOM block sits above the first
    placement section. Everything below it moved down by that much.
16. `Ctrl+S`'s message changed from a list of file names to
    `Saved <roomid>: content + layout + PNG`.
17. The room title in the top bar changes the moment a rename is applied, without
    a room reload.

---

## 6. Counts

| | Before | After |
|---|---:|---:|
| `SorceryForge/EditorGame.cs` | 3,912 | **4,363** |
| `SorceryForge/` new logic files | — | `EditorCommands.cs` 620, `UndoStack.cs` 156, `RoomProperties.cs` 171 |
| `SorceryForge/UI/` new | — | `FilterPopup.cs` 235 |
| `tools/EditCheck/` (new harness) | — | 1,034 |

| Harness | main | this branch |
|---|---:|---:|
| `tools/RoundTrip` | 13 identical / 0 violations | **13 identical / 0 violations** |
| `tools/ImportCheck` | 232 | **235** |
| `tools/MapCheck` | 78 | **78** |
| `tools/ChromeCheck` | 121 | **191** |
| `tools/EditCheck` | — | **138** |

`RoundTrip` and `MapCheck` are unchanged, with zero modifications to their
expectations. `ImportCheck` changed in exactly two places, both required by
commit 3: its scratch data directory is now seeded with a copy of `rooms.json`
(because `Create` reads the registry it writes), and the PR 5b hazard assertion
is inverted to pin the fix.

### The fifth harness, and why it is not part of ChromeCheck

`tools/EditCheck` drives the undo/redo command layer and the registry edits.
ChromeCheck's charter is one sentence — "Dear ImGui gets first refusal on every
frame's mouse" — and it needs a real ImGui context to answer anything. What
EditCheck drives has no chrome in it: pure state transitions over `EditorState`
and over `rooms.json`. Folding them into a file whose name promises input routing
would make that name a lie. `.claude/CLAUDE.md`'s harness list is updated from
four to five.

An undo stack is exactly the thing that has to be tested exhaustively, because
its failures are **silent**: a command whose `Undo` is not the inverse of its
`Do` does not crash and does not draw anything wrong; it leaves the room in a
state the author never authored, and the first sign of it is a `git diff` nobody
can explain three commits later. So every command class goes through the property
that defines it —

```
Do(); Undo();          leaves the state it found
Do(); Undo(); Do();    leaves the state Do() alone would have
```

— against a canonical string covering the placement list **and its order**, every
placement's editable fields, the spawn, the collision grid and a checksum of the
background pixels.

---

## 7. Fail-first verification

**Every new assertion in both harnesses was verified to FAIL against a
deliberately broken rule before being trusted.** That is not ceremony here: PR 7a
shipped a *passing* ChromeCheck assertion in front of a router that was killing
every editor keybind, because the assertion read a latched flag one frame too
early.

The sweep broke, one at a time: the menu-enablement predicates (four ways); every
field of `PlacementFields`, in both `Equals` and `ApplyTo`; every command's
`Undo` direction; the composite's reverse ordering; the region blit's origin;
each dirty-flag write; each `UndoStack` rule including which end the cap evicts;
the `rooms.json` header emission; the filter's substring and case rules; `Enter`
picking; the `(none)` mapping; the popup's two-step Escape; the filter box's
focus request; and the keyboard rule with each term removed and with
`WantCaptureKeyboard` substituted.

**Three assertions were found passing trivially and were strengthened:**

- A round-trip that changed a door's *target room* and *target door* together
  could not see an `ApplyTo` that never wrote the door id at all — because the
  room change blanks it anyway, so a command that ignored the field left the
  world looking exactly as a correct one would, in both directions. Every field
  now gets a change of its own, to a different non-empty value.
- Deleting the `WantTextInput` term from the keyboard rule broke nothing (§4).
- "Switching rooms re-seeds the name field" passed with *no* rename happening at
  all, because an unedited `InputText` reports no deactivation-after-edit. It now
  appends a character (`End`, then `!`) so the value that comes back has to start
  with what the field was seeded with.

**And one harness bug was found by the sweep, not by a failing test:** the
harness could not close a picker it had just opened, because ImGui's `InputText`
ignores keys on the frame its item was just activated — so a section that looked
like it was clicking the `Door` row was clicking the still-open `Room` popup.

---

## 7b. The adversarial pass — including what it did NOT cover

Five reviewers over five dimensions (undo, input routing, the data path, the
chrome, harness honesty), each finding then put to three independent skeptics
with distinct lenses (does the code really do this / is the state reachable /
was it already true on `main`), majority-refutation to dismiss.

**The run was cut short by a session limit.** Fifteen of thirty-eight agents
finished; the rest died on the rate limit. Concretely: the **input-routing
reviewer never ran at all**, and most of the undo / chrome / data findings never
reached their skeptics. Eleven findings were produced and judged, one survived.
So this pass is real evidence for what it covered and **no evidence at all**
about input routing — treat phase B of the smoke pass as the primary check
there rather than as a formality.

**The one survivor was a real bug, and it is fixed** (see divergence 8 below and
the commit `Adversarial pass: Ctrl+Z during a drag stranded the drag`):

> `Ctrl+Z` or `Ctrl+Y` pressed with a placement (or spawn) drag still held
> applied the move and recorded nothing, then undid an unrelated older edit.

`UpdateEditor` handles the canvas *before* the keyboard in the same frame, so by
the time `Ctrl+Z` is read the placement has already been moved and
`PlacementsDirty` is already set. `EndPlacementDrag` closed the drag by
*discarding* the record — and because both release handlers are gated on
`IsMovingSelection`, which it had just cleared, the release could never commit it
either. The placement sat at its dragged position with no undo entry, no
auto-punch, and an edit the author had not aimed at popped in its place.

The instructive part is that the wrong behaviour had a *comment defending it*:
"recording a half-finished drag would push a command the user never completed".
That reasoning is wrong in the direction that loses work — the drag's effect is
already in the state, so not recording it does not cancel it, it strands it. All
three gestures now close by **recording**, which also makes `CloseOpenGestures`
uniform: the `Ctrl+Z` that closed the drag then pops that very command, which is
what "take back the last thing I did" means when the last thing is the drag under
your hand.

**One dismissed finding was still worth acting on.** `FakeBackground.PushCount`
was written and never read, which the harness reviewer flagged as dead code. It
is dead code hiding a real gap: `BackgroundPixelsChanged` is the only thing that
pushes the edited pixels into the `Texture2D`, so a command that wrote the array
and stayed silent would pass every other assertion in `EditCheck` and leave the
author looking at a background that did not change when they pressed `Ctrl+Z`.
EditCheck now asserts the push in both directions (verified fail-first).

**One thing the pass found and I left alone.** `PaintTilesCommand`'s
out-of-range `continue` is redundant with `TileMapComponent.SetTile`'s own bounds
check, so EditCheck's "skips cells outside the grid" assertion is really testing
`SetTile`. Both are true and both are cheap; the duplicated guard stays because
the command indexes nothing itself and should not have to know that.

---

## 8. What a desktop session must verify

Everything below is a thing no headless harness can reach.

- **Filter-typing keybind suppression, live.** The harness proves ImGui's answer;
  it cannot prove the window manager delivered the keystroke ImGui was told
  about. Type a room name containing `p`, `n`, `i`, `a`, `[` or a `Delete` and
  confirm the editor does nothing at all.
- **Picker feel.** Popup placement near the window's right edge (the inspector is
  at x ≥ 980 and the popup is ~276 px wide, so ImGui clamps it left), the list's
  height at one hit and at seventy-five, whether the filter box is obviously the
  filter box, and whether `Enter`-takes-the-top-hit reads as helpful or as a
  surprise.
- **Undo of a mixed sequence.** The one thing worth doing slowly: drop a door,
  drag it, retarget it, paint three tiles, erase a patch, punch under it — then
  `Ctrl+Z` six times and confirm each press takes back exactly one thing you did,
  in order, and that `Ctrl+Y` walks back up.
- **Rename round-trip on the real file.** Rename, `git diff assets/data/rooms.json`
  (one line), reopen the editor, confirm the name stuck and the header is intact.
- **Pixels.** The ROOM block's proportions, whether the `id … — fixed` note reads
  as information or as clutter, the popup's colours against the panels.

Run `dotnet run --project SorceryForge/SorceryForge.csproj -- --imgui-probe` for a
live readout of `WantCaptureMouse` / `WantCaptureKeyboard` / the routing verdict
while typing.

---

## 9. Owner smoke pass

Ordered by risk. **If you have time for one phase, do phase A.**

Build first — all of these must be clean:

```powershell
dotnet build SorceryRemake.csproj
dotnet build SorceryForge/SorceryForge.csproj
dotnet run --project tools/RoundTrip/RoundTrip.csproj      # 13 identical, 0 violations
dotnet run --project tools/ImportCheck/ImportCheck.csproj  # 235 checks, 0 failures
dotnet run --project tools/MapCheck/MapCheck.csproj        # 78 checks, 0 failures
dotnet run --project tools/ChromeCheck/ChromeCheck.csproj  # 191 checks, 0 failures
dotnet run --project tools/EditCheck/EditCheck.csproj      # 138 checks, 0 failures
dotnet run --project SorceryForge/SorceryForge.csproj
```

### Phase A — undo, and the ways it can lose work

**A1 — one action, one press.** In a room with a background PNG, with
**Auto-punch ON**, drop a door on the canvas. One `Ctrl+Z` must remove the door
**and** fill the hole back in. A second must do nothing but say "Nothing to
undo." *(Two presses here would be a bug.)*

**A2 — the mixed sequence.** Drop an item, drag it, cycle its `Opens` (on a
door), paint three tiles in Paint mode, erase a patch in Erase mode, and punch
under a placement. Now `Ctrl+Z` six times. Each press must take back exactly one
of those, newest first, and the status line must name it. Then `Ctrl+Y` six times
and confirm you land back where you were.

**A3 — the drag is one entry.** Drag a placement slowly right across the canvas.
One `Ctrl+Z` must put it back where it started — not sixty.

**A4 — the paint drag is one entry.** Hold left and sweep across ten tiles. One
`Ctrl+Z` must clear all ten.

**A5 — a paint drag released over a panel.** Start painting on the canvas, drag
off onto the inspector, release there. `Ctrl+Z` must undo that drag as one — and
the *next* drag must be its own entry, not merged into it.

**A5b — `Ctrl+Z` WITH THE BUTTON STILL DOWN.** *(This is the bug the adversarial
pass found; no harness can reach it, so it is yours.)* Press and hold the left
button on a placement, drag it a long way, and — **without releasing** — press
`Ctrl+Z`. The placement must jump back to where the drag started, and the status
line must say `Undid: move <its id>`. Now release. Nothing further may happen: no
second jump, and no entity welded to the cursor. Repeat with the **spawn
marker**, and repeat with **Auto-punch ON** (the hole must be filled back in
too).

**A6 — `Tab` mid-drag.** Hold the paint button, press `Tab` to the board, release,
`Tab` back, and paint again. The two drags must be two undo entries.

**A7 — the room switch clears it.** Move a placement, `Ctrl+S`, `PageDown`,
`PageUp`. `Ctrl+Z` must say "Nothing to undo." *(A room switch discards undo
history by design — see doc/07.)*

**A8 — undo dirties.** With a clean room, move a placement (`room*` appears),
then `Ctrl+Z`. `room*` must **still** be there. Deliberate: undo marks what it
touched.

**A9 — the Edit menu.** On a fresh room both `Undo` and `Redo` are greyed. After
one edit `Undo` is live and `Redo` is not. After one `Ctrl+Z` both are live.
After a new edit `Redo` is greyed again. On the **board**, both are greyed
whatever the stack holds.

**A10 — `Ctrl+Shift+Z`.** Must redo, not undo.

**A11 — the discard guard still guards.** Everything in PR 7a's phase A1 —
`PageUp`, `PageDown`, the toolbar `<` `>`, `File > New Room…`,
`File > Import Screenshot…`, a click on the world map, `Escape` — must still warn
once on a dirty room and go through on the second attempt.

### Phase B — the pickers, and the keyboard while typing

**B1 — open, type, Enter.** Select a door, click `Room`, type three characters of
a room's name, press `Enter`. The value must become that room, and `Door` must
blank to `(none)`. One `Ctrl+Z` must restore **both**.

**B2 — the keybinds are held.** With the filter box open and focused, press `p`,
`n`, `i`, `a`, `[`, `]` and `Delete` in turn while typing them into the filter.
**Nothing** in the editor may happen: no punch, no picker, no brush change, no
deletion.

**B3 — the two-step Escape, on a CLEAN room.** Open a picker, type something,
press `Esc` once (the field loses focus; the popup stays), press `Esc` again (the
popup closes). The editor must still be running. A third `Esc` is the editor's
own exit path and *should* arm/quit — that is correct.

**B4 — click a row.** Open `Door`, click a row with the mouse. It must take that
row.

**B5 — outside clicks.** With a picker open, click a palette entry. The popup
must close and **no drag may start**. Click the palette again: now it must pick
up.

**B6 — the Door list follows the Room.** Point a door at `chateau_1`, then open
`Door`: only `chateau_1`'s doors. Change `Room` to another room and reopen
`Door`: only that room's.

**B7 — self-linking before saving.** In a room with two unsaved doors, point one
at *this* room and confirm the other unsaved door appears in the `Door` list.

**B8 — test rooms are absent.** `room_1` and `room_2` must not be in the `Room`
list. **This is correct — do not report it.**

**B9 — `Needs`.** On a blocked door the list must hold the five item types and
**not** `None`.

**B10 — `Opens` still cycles.** One click flips the side; no popup.

### Phase C — the ROOM block and rename

**C1 — the field is seeded.** Every room shows its own name. `PageDown` and
confirm the field changes with it.

**C2 — rename.** Type a new name, press `Enter`. The top bar's title changes
immediately. `git diff assets/data/rooms.json` must be **one line**, and the
header comment must be intact.

**C3 — rename does not lose unsaved work.** Move a placement (don't save), then
rename the room. The placement must still be where you left it and `room*` must
still be showing.

**C4 — `Esc` reverts.** Type into the field, press `Esc`. The old name comes
back and nothing is written.

**C5 — empty is refused.** Clear the field entirely and press `Enter`. The status
line must refuse it and the file must be untouched.

**C6 — the name reaches the game.** After a rename, `dotnet run --project
SorceryRemake.csproj` and confirm the HUD shows it.

### Phase D — save reporting

**D1 — the enumeration.** In a room with entities and doors, `Ctrl+S` must say
`Saved <roomid>: content + layout`. Erase a pixel and save again: `+ PNG`, with
the rebuild note. Paint a tile and save: `+ collision`.

**D2 — it never over-claims.** In an **empty** room with no files, `Ctrl+S` must
say "Nothing to save", and `git status assets/data` must show nothing new.

### Phase E — New Room, twice

**E1 — two rooms in a row.** Put two unused `RoomBG_*.png` in `Content/`, then
create **both** through `File > New Room…` without restarting. `rooms.json` must
gain **two** rows. *(Before this PR the second create dropped the first — it was
only safe because the editor reloaded between them.)*

**E2 — Import All still works.** With a stored preset and ≥2 ready files, press
`A` in the import picker and confirm every file lands and every room is in
`rooms.json`.

### Phase F — the data

The point of the whole thing: **the JSON must not move.**

```powershell
git status --short assets Content    # nothing new, nothing modified
git diff -- assets/data              # empty after a no-op load/save cycle
```

Cycle every room, save each without editing, confirm `git diff` is empty. Then
place / move / delete one of each placement kind, save, reload, and confirm the
diff is minimal and ordering-stable. **Undo a placement and redo it, then save:**
the file must be byte-identical to the un-undone version — list order included.

Finally `git status` for stray untracked files, and in particular **no
`imgui.ini`** anywhere.
