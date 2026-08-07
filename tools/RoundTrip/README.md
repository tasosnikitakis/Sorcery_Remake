# RoundTrip — SorceryForge save-path regression harness

Proves this, headlessly, in about a second:

> **Load every manifest room in SorceryForge, save each one untouched:
> `git diff` shows nothing and `git status` shows nothing new.**

That invariant is what makes an editor diff reviewable. If an untouched save
reformats a file or creates a new one, real edits drown in noise and nobody
can tell a data change from a serialiser change. Two violations of it were
fixed in PR 3b (Python-era layout formatting, and born-empty JSON files); this
tool is what keeps it fixed.

## Run it

```powershell
dotnet build tools/RoundTrip/RoundTrip.csproj
dotnet run   --project tools/RoundTrip/RoundTrip.csproj
```

| Exit | Meaning |
|------|---------|
| `0`  | Invariant holds. |
| `1`  | Violations found — each one is listed above the summary. |
| `2`  | Could not run (bad argument, unsafe `--out`, repo root not found). |

`--out <dir>` picks the scratch directory; the default is
`%TEMP%\sorcery-roundtrip`. It is cleared and re-seeded on every run, and left
in place afterwards so you can diff it by hand.

Unlike SorceryForge itself, this needs no desktop session — no `GraphicsDevice`
is ever created.

## What it actually does

1. **Seeds** a scratch directory with every `content_*.json` / `layout_*.json`
   from `assets/data`. That copy is the model of the working tree, and seeding
   it is load-bearing: the loaders' "don't *create* an empty file" rule keys
   off whether the **target** file exists, so a harness saving into an empty
   directory would take different branches than the editor and prove nothing.
2. **Round-trips** every room in `Rooms/RoomManifest.All` through the editor's
   own code path — `RoomContentLoader.TryLoad` + `RoomMeta.LoadDoorsFor` →
   `EditorState.LoadFromRoomContent` → `ToRoomContent` / `ToRoomLayoutJson` →
   `RoomContentLoader.Save` / `RoomLayoutLoader.Save` — writing into the
   scratch copy.
3. **Diffs** the scratch copy against `assets/data`: byte comparison stands in
   for `git diff`, and a stray-file sweep stands in for `git status`.

Before all that it runs a **self-test** of the empty-file rule against
synthetic room IDs, so the rule is pinned directly rather than inferred from
its effects on real data.

### Verdicts

| Verdict | Meaning |
|---------|---------|
| `identical` | File came back byte-for-byte. |
| `eol-only` | Line endings differ only. `core.autocrlf=true` here, so git never sees it. Reported, not a violation. |
| `skipped` | No file before, none after — the correct no-op for a genuinely empty room. |
| `changed` | **Violation.** An existing file came back different. |
| `extra` | **Violation.** A file was created that `assets/data` lacks — the born-empty-file failure. |
| `not-rewritten` | **Violation.** A file that *exists* was not written. The rule is "don't **create** empty files", never "don't **write** them" — an emptied room must still be flushed or the user's deletions vanish. |
| `missing` | **Violation.** A file present in `assets/data` is gone from the scratch copy afterwards. |

`not-rewritten` deserves the emphasis: a byte comparison alone cannot catch it,
because the untouched seed file still compares equal. It is checked explicitly.

### Not covered

`SaveCurrentRoom` steps 3 and 4 — collision grid and background PNG — are
gated behind the `CollisionDirty` / `BackgroundDirty` flags, which an untouched
load-then-save never sets. Out of scope by construction. If a future change
made either fire unconditionally, the stray-file sweep would notice the
collision JSON appearing in the scratch directory.

## Comparing against `main`

To show a branch changed no written bytes at all:

```powershell
git checkout main
dotnet run --project tools/RoundTrip/RoundTrip.csproj -- --out $env:TEMP\rt-main
git checkout -
dotnet run --project tools/RoundTrip/RoundTrip.csproj -- --out $env:TEMP\rt-head
git diff --no-index $env:TEMP\rt-main $env:TEMP\rt-head
```

An empty diff means the save path's output is unchanged. Commit or stash before
switching branches, and read the diff rather than just the exit code — each run
seeds from its own checkout of `assets/data`, so a data-only commit shows up
here too.

## Safety

It never writes into `assets/data`. The scratch directory is rejected up front
if it is, contains, or sits inside `assets/data`, or is/contains the repo root
(`AssertScratchSafe`). The pre-run clean deletes only `*.json` and refuses a
directory holding anything else, so a mistyped `--out` cannot eat someone's
folder.

## Keeping it honest

The harness is a deliberate mirror of `EditorGame.LoadRoom` and
`EditorGame.SaveCurrentRoom`, and `RoundTrip.csproj` mirrors
`SorceryForge.csproj`'s `<Compile Include>` globs so it compiles the same
sources the editor does. **If either changes shape, change this to match** — a
harness testing a parallel implementation tests nothing.
