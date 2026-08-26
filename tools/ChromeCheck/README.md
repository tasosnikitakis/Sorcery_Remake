# ChromeCheck — SorceryForge chrome / input-routing harness

Proves this, headlessly, in about a second:

> **Dear ImGui gets first refusal on every frame's mouse. The canvas, the map
> board and the crop image see that frame's mouse only when ImGui declines it —
> except that a gesture which started on one of those surfaces keeps the mouse
> until it ends, wherever the cursor wanders.**

Fourth of the headless harnesses, alongside [`tools/RoundTrip`](../RoundTrip/README.md),
[`tools/ImportCheck`](../ImportCheck/README.md) and [`tools/MapCheck`](../MapCheck/README.md),
and built the same way: compile the editor's own sources and drive them.

## Run it

```powershell
dotnet build tools/ChromeCheck/ChromeCheck.csproj
dotnet run   --project tools/ChromeCheck/ChromeCheck.csproj
```

Exit `0` = every check passed. `1` = failures (listed inline as `FAIL`).
`2` = could not run.

It writes nothing, anywhere. It does not even read `assets/data`.

## Why it can run without a desktop

Dear ImGui is pure CPU. It builds its font atlas with stb_truetype, lays out
its windows, decides what the mouse is over, and records draw lists — all in
ordinary memory. A renderer only *paints* what it produced.

So this harness creates a real ImGui context against the real pinned cimgui,
feeds it synthetic mouse and keyboard events, and asks it exactly the questions
the editor asks: `io.WantCaptureMouse`, `io.WantCaptureKeyboard`. No window, no
`GraphicsDevice`, no desktop session.

**The design rule that keeps this possible:** every file under `SorceryForge/UI/`
is device-free *except* `ImGuiRenderer.cs`, which is the one piece that
genuinely needs a `GraphicsDevice`. A panel that needs a texture takes the
`IntPtr` handle ImGui uses, not a `Texture2D`. Keep it that way and this stays
possible — the same bargain `WorldMap.cs` and `ImageImport.cs` already make
with MapCheck and ImportCheck.

## Why this invariant, and not another

Routing is the thing a chrome migration is most likely to get wrong, and it is
the thing least likely to be caught by looking:

- The hand-rolled chrome had **three** independent wheel consumers in room mode
  (inspector scroll, palette scroll, canvas zoom), each region-testing its own
  rectangle, with the rectangles maintained in three different places. That is
  the arrangement that shipped a palette whose scrolling and whose hit-testing
  disagreed about where a row was.
- Dragging a placement toward a room edge routinely takes the cursor onto the
  inspector before the button comes up. The old code caught that release in
  `HandleCanvasInput`'s out-of-canvas branch, ended the move, and fired
  auto-punch there. Gate that branch on ImGui alone and the release is never
  seen: the move stays "in progress" for ever, and the next canvas click
  resumes dragging an entity the user thought they had dropped.

Both failures look like a working editor in a screenshot.

## Sections

| # | Section | What it pins down |
|---|---------|-------------------|
| 1 | capture | Which screen regions ImGui claims, at two window sizes, including the canvas's corner pixels |
| 2 | override | A gesture begun on the canvas survives crossing a panel — and hands the panel back when it ends |
| 3 | ownership | A gesture begun on a panel does **not** leak onto the canvas; and right-click over chrome *is* captured, which is why the modal pickers' cancel bypasses the router |
| 4 | wheel | One notch reaches exactly one consumer, decided by ImGui's own hover |
| 5 | keyboard | Merely hovering chrome never costs the editor a keypress |

## What it cannot cover

Pixels, and the real driver. Whether the font is legible, whether the menu bar
looks right, and whether the cursor the window manager reports is the cursor
ImGui was told about are all the owner's smoke test. Run the editor with
`--imgui-probe` for a live readout of the same three values this harness
asserts.
