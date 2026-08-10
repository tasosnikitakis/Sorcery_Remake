# MapCheck — SorceryForge world-map regression harness

Proves this, headlessly, in about a second:

> **The board is the same every time it is built from the same world; every
> door in every room becomes exactly one arrow; and every arrow starts and ends
> on the rooms and doors it claims to, wearing the verdict the Doors button
> would give it.**

Third of the headless harnesses, alongside [`tools/RoundTrip`](../RoundTrip/README.md)
and [`tools/ImportCheck`](../ImportCheck/README.md), and built the same way:
compile the editor's own sources and drive them.

## Run it

```powershell
dotnet build tools/MapCheck/MapCheck.csproj
dotnet run   --project tools/MapCheck/MapCheck.csproj
```

| Exit | Meaning |
|------|---------|
| `0`  | Every check passed. |
| `1`  | Failures — each is a `FAIL` line inline. |
| `2`  | Could not run (bad argument, repo root not found, bad `rooms.json`). |

`--board` prints the computed board instead of checking it — every room with
its column, row and position, and every arrow with its verdict and endpoints.
Read it when the map looks wrong on screen and you need to know whether the
picture or the data is at fault.

**It writes nothing, anywhere.** No scratch directory, no `--out`:
`assets/data` and `Content/` are opened, parsed and left alone.

## What it checks

| Section | Invariant |
|---------|-----------|
| 1 verdicts | `DoorValidator` against hand-built worlds holding each of the five outcomes — `ok`, `ok-test`, `asymmetric`, `orphan-door`, `orphan-room` — including the near-miss where the partner names the right *room* and the wrong *door*. Then against the real world, cross-checked against a direct count of the doors in the `layout_*.json` files. |
| 2 layout | BFS columns on a synthetic chain, a fork and an island; a one-way link still places its room next door; on the real registry the columns are **re-derived by a different algorithm over independently-loaded data** and compared; two builds are identical; no two boxes overlap; a stored position overrides auto-placement and disturbs nothing else. |
| 3 geometry | Which edge each door sits on (including the case that defeats a naive rule: a side door at `y=112` is near the *bottom* and must still read as a side door), where its arrow meets the box, two doors on one edge anchoring apart, content bounds, hit-testing. |
| 4 arrows | Every door is on exactly one arrow and no door is on two; a wired pair collapses to one double-headed line; an orphan-door arrow still reaches the target room; missing rooms, test rooms and self-links become outward stubs; every arrow's status is its own door's verdict. |
| 5 view | `MapView` round-trips screen↔map at every zoom, keeps the point under the cursor still while zooming, clamps panning back to the board, pins a board smaller than the viewport, and scales a box exactly. |

Determinism is the one worth stating twice. The map's whole value is that a
user learns where things are; a layout that reshuffles between sessions is
*worse* than no layout, because it teaches something false. The BFS is seeded
and enqueued in registry order and its adjacency is symmetric — and this
harness builds the board twice and demands the same answer.

## What it cannot cover

Drawing. Thumbnails, arrowheads, hover, and whether the thing is pleasant to
use are the owner's smoke test. Everything the pixels are computed *from* is
here.

## Keeping it possible

`SorceryForge/WorldMap.cs`, `DoorValidator.cs` and `MapView.cs` contain no
`Texture2D` and no `GraphicsDevice`; `EditorGame` only turns their results into
pixels. **Keep it that way** — a `GraphicsDevice` reference in any of those
three takes this whole harness with it, exactly as it would for ImportCheck.
