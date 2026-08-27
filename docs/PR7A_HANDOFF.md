# PR 7a — ImGui chrome migration: hand-off

Branch `pr7a-imgui-chrome`, ten commits off `main`. **Nothing user-visible was
added.** Every menu, panel, overlay and status line the editor draws is now Dear
ImGui; the canvas, the world-map board and the crop image are untouched
SpriteBatch. `EDITOR_REVIEW` item 17 is decided and item 18 is partly done.

---

## 1. The binding, and why

**`ImGui.NET` 1.91.6.1, pinned exactly**, plus a ~560-line renderer at
`SorceryForge/UI/ImGuiRenderer.cs`. Editor only — `SorceryRemake.csproj` is
untouched and the game's `deps.json` contains no reference to it (verified,
`grep -c imgui bin/Debug/net8.0/SorceryRemake.deps.json` → 0).

Every MonoGame-flavoured ImGui package on NuGet was surveyed first, and every
one was rejected:

| Package | Verdict |
|---|---|
| `MonoGame.ImGuiNet` 1.0.5 | Ships its assembly at the **package root**, not under `lib/<tfm>/` — referencing it compiles against nothing. Also declares no MonoGame dependency at all. |
| `Monogame.Imgui.Renderer` 1.0.5 | Pinned to ImGui.NET **1.87.3** (2022). |
| `ImGuiHandler.MonoGame` 1.1.1 | Pinned to ImGui.NET **1.75.0** (2020). |
| `ImGui.NET.Monogame-with-types` | Depends on **MonoGame.Framework.Portable 3.7.1** — a different framework flavour from the DesktopGL 3.8.1 this repo runs on. |

So the dependency is the binding everything else wraps: 5.6M downloads, MIT, and
the only one shipping a first-class **net8.0** target plus native cimgui for
win-x64 / win-arm64 / win-x86 / linux-x64 / osx. What a wrapper would have
supplied — font atlas, vertex path, input pump — is the renderer file, and it is
entirely mechanical.

**Pinned, not floating.** ImGui's C# surface is generated from cimgui and moves
with it; a floating version is a build that breaks on its own months later.
`tools/ChromeCheck` carries the same pin, deliberately: if one moves and the
other does not, the harness stops testing the editor's ImGui.

One `<AllowUnsafeBlocks>` was added to each of the two projects, for exactly one
pointer write: `io.IniFilename` has no managed setter, and is set to null so the
editor never drops an `imgui.ini` beside the source tree.

---

## 2. Input capture — how the conflict is resolved

**The rule:** ImGui gets first refusal on the mouse; a gesture already running on
a *world surface* (room canvas, map board, crop image) keeps the mouse until it
ends, wherever the cursor wanders. `SorceryForge/UI/ChromeInputRouter.cs`.

**The wheel** is settled structurally rather than by arithmetic. The palette and
inspector are ImGui windows, so hovering either raises `WantCaptureMouse` and
ImGui scrolls itself; the canvas is not a window, so hovering it leaves the flag
false and `HandleCanvasView` zooms. One notch reaches exactly one consumer,
decided by ImGui's own hover rather than by three hand-maintained rectangles in
three files — which is the arrangement that shipped a palette whose scrolling
and whose hit-testing disagreed about where a row was.

**The override is not optional.** Dragging a placement toward a room edge
routinely takes the cursor onto the inspector before the button comes up; the
old code caught that release in `HandleCanvasInput`'s out-of-canvas branch, ended
the move and fired auto-punch. Gate that on ImGui alone and the release is never
seen — the move stays "in progress" for ever and the next canvas click resumes
dragging an entity the user thought they had dropped. Same shape for the
middle-drag pan, the erase stroke and the crop-box drag.

**Three things deliberately bypass the router:**

1. **The modal pickers' cancel gestures** (Escape, right-click). They were
   defined as consuming every input, and the cursor is most often *over the
   panel* when you change your mind — where ImGui captures the mouse.
2. **The palette drag-cancel** (right-click, Place mode). It has always worked
   from anywhere on screen, and anywhere is now mostly ImGui.
3. **The crop step's Escape / Enter.** Modal confirm and cancel, over the crop's
   own chrome strips.

**Modality is enforced explicitly, not implicitly.** The old chrome was inert
behind a picker because `Update` returned before any widget handler ran. An ImGui
window does *not* stop hit-testing because something is drawn over the middle of
the screen — and the centred picker covers the canvas, not the palette at
x 0..280. So the three bands take `NoInputs` while any modal owns the editor,
including a running batch import (which shows no overlay of its own and is
writing files). The flag is repeated on the scrolling **child** windows, because
`NoInputs` does not propagate.

**The keyboard rule is built on `io.WantTextInput`, not `io.WantCaptureKeyboard`**
— and that distinction is the subtlest thing in this PR. ImGui raises
`WantCaptureKeyboard` whenever `g.ActiveId` is set, which happens on **any** press
into an ImGui window, including plain empty space (a window takes its own MoveId
even when it is `NoMove`). Gating on it meant every editor keybind died for as
long as a mouse button was held anywhere on the chrome — hold the button on the
status bar and `Ctrl+S`, `PageDown`, `F11` and the arrows all stopped working.
`WantTextInput` is true only while a text field is actually taking keystrokes —
of which there are none today — so every keybind fires exactly as it did on
`main`.

> The flag is still **false on the frame of the press** and only goes true from
> the next one, so a probe that presses and asserts immediately reports a clean
> result whatever the rule is. That is how this shipped into a *passing*
> ChromeCheck assertion for several commits, until an adversarial pass caught it.
> The corrected assertion holds for three frames and covers empty chrome space.

**An open menu holds the keyboard.** `WantCaptureKeyboard` does *not* cover an
open popup, and with keyboard navigation off (it would claim the arrow keys,
Enter and Escape — all documented keybinds) ImGui's own Escape-closes-a-popup
path never runs. Without a third router input, "open File, change your mind,
press Escape" reached the editor's Escape, which arms the discard guard and on a
clean room **quits**. Now the popup state gates the keyboard, and each menu
closes itself on Escape.

---

## 3. Line counts

| | Before | After |
|---|---:|---:|
| `SorceryForge/EditorGame.cs` | 4,709 | **3,912** (−797, −17%) |
| `SorceryForge/UI/` (new) | — | 2,588 across 10 files |
| `tools/ChromeCheck/` (new) | — | 1,524 |

Largest new files: `ImGuiRenderer.cs` 558 (the binding, once), `Pickers.cs` 395,
`MenuBar.cs` 353, `InspectorPanel.cs` 335.

**No hand-rolled chrome remains.** A grep across `SorceryForge/` for
`UiButton`, `_buttons`, `RelayoutButtons`, `HandleButtons`, `DrawButton`,
`_inspectorButtons`, `_newRoomButtons`, `_importButtons`, `_cropButtons`,
`LayoutPalette`, `PaletteRowRect`, `PaletteViewportRect`, `ScrollY`,
`DrawInspectorRow`, `DrawSectionBody`, `DrawTopBar`, `DrawStatusBar`,
`DrawPalette*` returns **nothing**.

---

## 4. What was verified headlessly, and what needs your eyes

### Verified headlessly — `tools/ChromeCheck`, 121 checks

Dear ImGui is pure CPU: it builds its font atlas, lays out its windows, decides
what the mouse is over and records draw lists in ordinary memory. So the harness
drives the **real** pinned cimgui with synthetic input and asserts the answers,
with no window and no `GraphicsDevice`. The rule that keeps this possible: every
file under `SorceryForge/UI/` is device-free **except** `ImGuiRenderer.cs`.

| § | Covers |
|---|---|
| 1 | Which regions ImGui claims, at two window sizes, down to the canvas's corner pixels |
| 2 | A gesture begun on the canvas survives crossing a panel, and hands it back on release |
| 3 | A gesture begun on a panel does not leak onto the canvas; right-click over chrome *is* captured (why the picker cancels bypass the router) |
| 4 | One wheel notch, one consumer |
| 5 | Keybinds survive hovering every band **and holding a button on one, across frames**; an open menu holds them and Escape closes the menu |
| 6 | Menu enablement in both modes, and the four documented map-mode exceptions |
| 7 | Room and board titles verbatim; which `*` means which unsaved thing |
| 8 | Every fragment of the status line's right-hand group, in order |
| 9 | The real palette: the row you click is the row you saw — **scrolled to the bottom included** |
| 10 | Every inspector field reaches its own verb on its own placement; collapsed sections register nothing; two placements of the same kind do not collide |
| 11 | Candidate rows, the quantize toggle, the crop buttons — and that the crop **image** is still left to the canvas |
| 12 | With a modal up, nothing behind it answers a click |

`RoundTrip` 13 identical / 0 violations, `ImportCheck` 232, `MapCheck` 78 — all
**unchanged from `main`**, zero modifications to their expectations.

### Needs your eyes — what a headless harness cannot reach

- **Pixels.** Font legibility, the menu bar's proportions, whether the panels
  look right at your window size, whether the icons are crisp.
- **The real driver.** The harness proves ImGui's *answer*; it cannot prove the
  window manager put the cursor where ImGui was told it was. Run
  `dotnet run --project SorceryForge/SorceryForge.csproj -- --imgui-probe` for a
  live readout of `WantCaptureMouse` / `WantCaptureKeyboard` / the routing
  verdict.
- **Anything touching a `GraphicsDevice`:** the font atlas actually rendering,
  the vertex path, sprite icons drawn through `ImGui.Image`, the drag ghost in
  the foreground draw list, SpriteBatch state surviving `RenderDrawData`, and the
  fullscreen round-trip.
- **The file-writing flows end to end** (New Room, Import, Import All, crop).
  Their *logic* is covered by ImportCheck; the *chrome around them* is covered by
  ChromeCheck; nobody has watched a real PNG land.

---

## 5. Divergences — the complete list

Everything below is a deliberate, known difference. Nothing else was intended.

### Scroll (consequence of ImGui owning panel scrolling)
1. The wheel scrolls the palette / inspector while the cursor is over the **list**;
   it used to scroll while anywhere in the panel, including the 30 px title strip.
2. The step is ImGui's, not `delta * 0.25f` (~30 px per notch).
3. Scrollbars are **draggable**. The old ones were painted hints nothing
   hit-tested — and a scrollbar you cannot drag is one users try to drag.
4. The picker lists scroll on hover rather than from anywhere on screen.
5. The crop wheel no longer resizes the selection while the cursor is over a
   chrome strip. (It cannot: hovering a strip is how a Confirm click is kept from
   also starting a drag.)

### Layout and appearance
6. The room title moved from the top bar's centre to the toolbar row's right end.
   **This is the point of the exercise** — see §6.
7. The status bar gained a `room*` marker beside `PNG*` and `map*`. On the board
   it is still `map*` alone.
8. The mode switch is three buttons instead of one cycling button.
9. The inspector no longer paints over its own "INSPECTOR" title when scrolled.
   The old panel had no scissor; that was never intentional.
10. Fonts differ (ImGui's built-in vs `DebugFont`), so text metrics and
    truncation points differ. Truncation still uses the same three-dot rule.

### Behaviour
11. Inspector hit-testing is **same-frame**. It used to test one-frame-stale
    rectangles, and — because scroll ran before hit-test in the same `Update` — a
    wheel notch plus a click in one frame could cycle a *different* field.
12. `File > New Room…`, `File > Import Screenshot…` and `View > World Map` are
    enabled in map mode. Their keyboard paths (`N`, `I`, `Tab`) already worked
    there; the old buttons were inert only because `HandleButtons` never ran.
13. `File > Save Map Arrangement` is a new **menu item** for an existing keybind
    (`Ctrl+S` in map mode), which never had a button.
14. `N` and `I` remain **map-mode-only keys**. Room mode reaches both through the
    File menu, as it always did through the buttons. Unchanged, but easy to
    misread from the menu.
15. ImGui's `MenuItem` fires on **release**; every hand-rolled zone fired on the
    press edge. Everything else in the chrome — palette rows, inspector fields,
    picker candidates, the quantize toggle, both pickers' Cancel, the crop's
    Cancel and Confirm, the mode buttons, the room nav buttons and both
    checkboxes — was put back on the press edge via `ChromeTheme.Press*`.
    `MenuItem` is left alone deliberately: menus are new here, so there is no
    prior behaviour to match, release-to-commit is the platform convention, and
    it is also what closes the menu.
16. The drag ghost is suppressed while a modal is open or the board is showing.

### Found by the adversarial pass, after the migration was "done"

Twenty-six candidates went through three-way adversarial refutation. Twenty-one
were dismissed — all of them things already fixed earlier in the branch, which is
the result you want from a verifier run against a moving target. Five survived,
covering four distinct issues (two were the same one seen from different angles),
and **all four are fixed**:

| Issue | Now |
|---|---|
| Keybinds died while a mouse button was held anywhere on chrome | Rule rebuilt on `io.WantTextInput` |
| `Escape` with a menu open quit the editor | Popup gates the keyboard; menus close on `Escape` |
| `F11` resized the back buffer inside the open ImGui frame | Deferred until after `EndFrame` |
| Modal buttons fired on release while their rows fired on press | `ChromeTheme.Press*` wrappers |

The first is the instructive one. It had a **passing** ChromeCheck assertion
behind it — the assertion pressed the button and read the flag on that same
frame, one frame before ImGui sets it. A test that measures the wrong moment is
worse than no test, because it is evidence. The corrected assertion holds for
three frames, covers empty chrome space as well as widgets, and was verified to
fail against the old rule before being trusted.

---

## 6. The saturation problem, and why it is closed

The old room title was drawn **only if it fitted the gap between the two button
banks**, and the bar had gained the Import button, pushing the left bank right by
~90 px. At the **default 1280 px window** that gap was **36 px** — enough for the
bare `*` and nothing else. The room's name and id had already stopped being
visible on a normal screen, and the only always-on sign of unsaved work was one
character wide.

Nothing in the new bar is positioned by arithmetic against anything else: the
menus lay themselves out left to right, and the title measures itself against the
window's right edge. No item's position is a function of another item's width, so
no item can be squeezed out by another growing. The status bar reports `room*`
independently, so the *warning* survives even a window narrow enough to clip the
title.

---

## 7. Owner smoke pass

The longest of the series, by design: it is a full regression of PR 1–6 through a
chrome that was entirely rebuilt. **Phases are ordered by risk.** If you have
time for one phase, do phase A.

Build first — both must be clean:

```powershell
dotnet build SorceryRemake.csproj
dotnet build SorceryForge/SorceryForge.csproj
dotnet run --project tools/RoundTrip/RoundTrip.csproj      # 13 identical, 0 violations
dotnet run --project tools/ImportCheck/ImportCheck.csproj  # 232 checks, 0 failures
dotnet run --project tools/MapCheck/MapCheck.csproj        # 78 checks, 0 failures
dotnet run --project tools/ChromeCheck/ChromeCheck.csproj  # 121 checks, 0 failures
dotnet run --project SorceryForge/SorceryForge.csproj
```

### Phase A — guards, dirty markers, input routing at boundaries

The three things that lose work if they are wrong.

**A1 — the discard guard, every path.** With a dirty room (move a placement),
each of these must warn on the first attempt and go through on the second:
`PageUp`, `PageDown`, the toolbar's `<` and `>`, `File > New Room…`,
`File > Import Screenshot…`, clicking a room on the world map, `Escape`.

**A2 — the guard is one GLOBAL flag.** Arm it with `PageUp`, then click
`File > Import Screenshot…` — Import must go straight through, because the flag
was already armed. (This is pre-existing behaviour, not new.)

**A3 — the guard disarms on a new edit.** Arm it, then nudge a placement. The
next room switch must warn again.

**A4 — `Escape` with an open menu does NOT quit.** On a **clean** room, open
`File`, then press `Escape`. The menu must close and the editor must still be
running. *(This was a real bug found during review — please confirm on the real
driver.)*

**A5 — the out-of-canvas release.** Turn `Auto-punch` on. Drag a placement from
mid-canvas out over the **inspector** and release there. The move must end, the
status line must report the position, and the background must be punched.
Repeat releasing over the **palette**, and over the **top bar**.

**A6 — middle-drag pan across a panel.** Zoom in (wheel over canvas), then
middle-drag from the canvas out over the inspector and back. Panning must
continue the whole time and stop on release.

**A7 — erase stroke across a panel.** Erase mode, hold left, drag off the canvas
onto the palette and back. The stroke must not tear, and `Ctrl+Z` must undo the
whole stroke as one.

**A8 — the wheel, at every boundary.** Over the canvas → zoom, and the palette
must not scroll. Over the palette → the palette scrolls, and the zoom must not
change. Over the inspector → the inspector scrolls. Move between them without
clicking and confirm nothing "sticks".

**A9 — markers.** Move a placement: `room*` appears in the status bar and `*`
beside the room title. Erase a pixel: `PNG*` joins it. `Ctrl+S`: both clear.
Drag a room on the map: `map*` appears — and appears in **room** mode too.
Confirm the board's status line shows `map*` **alone** (never `room*`/`PNG*`).

**A10 — nothing behind a modal answers.** Open `File > New Room…`. Now try to
click a palette entry, an inspector field, and `File > Save Room`. **None may do
anything.** Escape, then confirm all three work again. Repeat with
`File > Import Screenshot…` and with the crop step.

### Phase B — keybinds, mode by mode

Every key from the PR 1–6 passes, in each mode it applies to.

| Key | Room mode | Map mode | Picker | Crop |
|---|---|---|---|---|
| `Tab` | to map | to room | — | — |
| `Esc` | exit (guarded) | to room | cancel | cancel |
| `Ctrl+S` | save room | save arrangement | — | — |
| `Ctrl+Z` | undo bg stroke | — | — | — |
| `PageUp` / `PageDown` | cycle rooms | — | — | — |
| arrows | pan canvas 8 px | pan board ½ room | — | — |
| `[` / `]` (+`Shift`) | brush ∓1 / ∓4 | — | — | — |
| `Delete` | delete placement / clear spawn | — | — | — |
| `P` | punch under selection | — | — | — |
| `F11` | fullscreen | — | — | — |
| `N` / `I` | **nothing** (menu only) | pickers | — | — |
| `A` | — | — | Import All | — |
| `Enter` | — | — | — | confirm |

Also: `F11` in and out — the window must restore to its previous size, keep its
border, and stay drag-resizable. Then resize the window by dragging an edge and
confirm the canvas re-centres and the panels re-pin.

### Phase C — the panels

**C1 — palette.** Every section header present, in order (WEAPONS, KEY ITEMS,
ENEMIES, DOORS, OTHER, META), each with its entries. Icons crisp (point
sampled). Headers not clickable. Scroll to the bottom, then click a row — you
must get **that** row. Switch to Paint mode: entries dim (icon and label only —
the row background and border stay), clicks ignored, title reads
`PALETTE (paint mode)`. Same in Erase.

**C2 — palette drag.** Click an entry: the ghost follows the cursor and is drawn
**over** the palette and inspector. Right-click over the palette cancels it.
Right-click over the inspector cancels it. Drop on the canvas places it and
expands its inspector section.

**C3 — the two door entries.** `Door (LeftOpening)` must show the *right*-hinged
artwork and vice versa. **This mismatch is correct — do not report it.** Drop
one and confirm the canvas draws the same sprite.

**C4 — inspector.** One section per placement, two-line header (`-`/`+`, kind,
then the truncated id). Clicking a header **both** selects (yellow outline on
canvas) **and** toggles collapse. Rows per kind: Pos (read-only), Type
(read-only), Needs / Opens / Room / Door (clickable), Background → Punch.
Cycling `Room` must blank `Door` to `(none)`.

**C5 — two placements of the same kind.** Put **two doors** in a room. Cycle the
*second* door's `Opens` and confirm the *first* one does not change. *(This was
a real bug found during review.)*

### Phase D — the modal flows, end to end

**D1 — New Room.** `File > New Room…` → pick an unused background → the room is
created and opened. Cancel with the button, with `Escape`, and with a right-click
over the panel. An unavailable row must be red and unclickable.

**D2 — Import.** `File > Import Screenshot…` → the quantize row toggles → click a
320×144 source → it imports. Click an odd-sized source → the crop step opens.

**D3 — crop.** Drag the box, wheel to resize, watch the header numbers update.
`Enter` confirms; `Esc` and right-click cancel. Click `Confirm` and `Cancel` in
the footer. **Confirm that clicking a footer button does not also start a drag.**

**D4 — Import All.** With a stored preset and ≥2 ready files, press `A` in the
picker. The status line counts up; `Esc` stops after the current file.

### Phase E — the map board

Tab in. Thumbnails load as you pan. Drag a room (4 px slop — a small wobble must
still open it, not move it). Click a room to open it. `Ctrl+S` saves the
arrangement. `N` and `I` open the pickers. Confirm every room-acting menu item
is greyed, and that New Room / Import / World Map / Save Map Arrangement are not.

### Phase F — the data

The point of the whole thing: **the JSON must not move.**

```powershell
git status --short assets Content    # nothing new, nothing modified
git diff -- assets/data              # empty after a no-op load/save cycle
```

Cycle every room, save each without editing, and confirm `git diff` is empty.
Then place / move / delete one of each placement kind, save, reload, and confirm
the diff is minimal and ordering-stable.

Finally: `git status` for stray untracked files — in particular **no
`imgui.ini`** anywhere. Both the editor and the harness disable it, and its
reappearance means one of them regressed.
