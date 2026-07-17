// ============================================================================
// DoorEraser
// Sorcery+ Remake — strip baked-in closed-door pixels out of a room background
// ============================================================================
// The room background PNGs (extracted from original-game screenshots) have a
// CLOSED door drawn into the scenery. When SorceryForge places an animated
// door entity on top, that baked door bleeds through during the open/close
// animation. This tool removes it: it builds a 24x24 colour template from the
// closed-door sprite (frame 0 of Left/RightDoorFrames.png), finds every place
// that template appears in the background by exact-ish pixel matching, and
// sets the matched door pixels to transparent — writing an RGBA PNG.
//
// Geometry (from DoorConfig / DoorComponent):
//   - Door sprite frame is 48x48, a clean 2x upscale of a 24x24 chunky door.
//   - In game/background space the door occupies 24x24 px at 1:1 with the bg.
//   - Backgrounds are 320x144; some are 24bpp (no alpha) — we always emit RGBA.
//
// Usage:
//   DoorEraser scan  <bg.png> [opts]            # locate doors, write nothing
//   DoorEraser erase <bg.png> <out.png> [opts]  # locate + erase, write RGBA PNG
// Options:
//   --content <dir>     folder holding Left/RightDoorFrames.png (default: Content)
//   --threshold <0..1>  min fraction of the 576 template px that must match (default 0.85)
//   --mode <matched|box>  erase only matched door px, or the whole 24x24 box (default matched)
//   --top <n>           in scan, also print the N global best offsets (default 6)
// ============================================================================

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

const int D = 24;          // door size in background pixels
const int SHEET_FRAME = 48; // sprite frame size (2x)

if (args.Length < 2)
{
    Console.WriteLine("usage: DoorEraser <scan|erase> <bg.png> [out.png] [--content DIR] [--threshold F] [--mode matched|box] [--top N]");
    return 1;
}

string cmd = args[0].ToLowerInvariant();
string bgPath = args[1];

string contentDir = "Content";
double threshold = 0.85;
string mode = "matched";
int top = 6;
int tol = 10;   // per-channel colour tolerance (bg uses true CPC palette, sprite uses rounded values)
bool emitProof = false;
string? outPath = null;

// erase takes an out path as positional arg before options
int optStart = 2;
if (cmd == "erase")
{
    if (args.Length < 3 || args[2].StartsWith("--"))
    {
        Console.WriteLine("erase requires <out.png>");
        return 1;
    }
    outPath = args[2];
    optStart = 3;
}

for (int i = optStart; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--content":   contentDir = args[++i]; break;
        case "--threshold": threshold = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
        case "--mode":      mode = args[++i].ToLowerInvariant(); break;
        case "--tol":       tol = int.Parse(args[++i]); break;
        case "--proof":     emitProof = true; break;
        case "--top":       top = int.Parse(args[++i]); break;
        default:
            if (args[i].StartsWith("--")) { Console.WriteLine($"unknown option {args[i]}"); return 1; }
            break; // positional arg (e.g. dump region numbers) — handled per-command

    }
}

string leftSheet = Path.Combine(contentDir, "LeftDoorFrames.png");
string rightSheet = Path.Combine(contentDir, "RightDoorFrames.png");

// ---- Build 24x24 templates from frame 0 of each sheet (nearest 2x downscale) ----
Rgb24[,] tLeft = BuildTemplate(leftSheet);
Rgb24[,] tRight = BuildTemplate(rightSheet);

var templates = new List<(string name, Rgb24[,] px)> { ("left", tLeft) };
if (!TemplatesEqual(tLeft, tRight)) templates.Add(("right", tRight));
else Console.WriteLine("(left and right closed-door templates are identical — using one)");

// ---- Load background as RGBA ----
using var bg = Image.Load<Rgba32>(bgPath);
int W = bg.Width, H = bg.Height;
Console.WriteLine($"background {Path.GetFileName(bgPath)}: {W}x{H}");

// snapshot bg rgb into an array for fast scanning
var px = new Rgb24[W, H];
bg.ProcessPixelRows(acc =>
{
    for (int y = 0; y < H; y++)
    {
        var row = acc.GetRowSpan(y);
        for (int x = 0; x < W; x++) px[x, y] = new Rgb24(row[x].R, row[x].G, row[x].B);
    }
});

// ---- dump: render a region (or the whole image, downsampled) as ASCII ----
if (cmd == "dump")
{
    var legend = new Dictionary<Rgb24, char>();
    var pool = " .abcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*+=ABCDEFGHIJKLMNOP".ToCharArray();
    char CharFor(Rgb24 c)
    {
        if (c.R == 0 && c.G == 0 && c.B == 0) return ' ';           // black -> space for readability
        if (!legend.TryGetValue(c, out var ch)) { ch = pool[2 + (legend.Count % (pool.Length - 2))]; legend[c] = ch; }
        return ch;
    }

    int rx = 0, ry = 0, rw = W, rh = H, step = 1;
    var extra = args.Skip(optStart).Where(a => !a.StartsWith("--")).ToArray();
    // dump <bg> x y w h   (full-res region)  OR  dump <bg> (whole image, step=2)
    var nums = new List<int>();
    foreach (var a in args.Skip(2)) { if (int.TryParse(a, out var n)) nums.Add(n); }
    if (nums.Count >= 4) { rx = nums[0]; ry = nums[1]; rw = nums[2]; rh = nums[3]; step = 1; }
    else { step = 2; } // whole image at half res

    Console.WriteLine($"=== dump {Path.GetFileName(bgPath)} region ({rx},{ry}) {rw}x{rh} step={step} ===");
    for (int y = ry; y < ry + rh && y < H; y += step)
    {
        var sb = new System.Text.StringBuilder();
        for (int x = rx; x < rx + rw && x < W; x += step) sb.Append(CharFor(px[x, y]));
        Console.WriteLine(sb.ToString());
    }
    Console.WriteLine("=== legend (space=black) ===");
    foreach (var kv in legend.OrderBy(k => k.Value))
        Console.WriteLine($"  '{kv.Value}' = {kv.Key.R},{kv.Key.G},{kv.Key.B}");
    return 0;
}

// ---- analyze: compare the baked door against the template visually ----
if (cmd == "analyze")
{
    var legend = new Dictionary<Rgb24, char>();
    var pool = "abcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*+=".ToCharArray();
    char CharFor(Rgb24 c)
    {
        if (!legend.TryGetValue(c, out var ch)) { ch = pool[legend.Count % pool.Length]; legend[c] = ch; }
        return ch;
    }

    Console.WriteLine("=== template LEFT frame0 (24x24) ===");
    for (int j = 0; j < D; j++)
    {
        var sb = new System.Text.StringBuilder("  ");
        for (int k = 0; k < D; k++) sb.Append(CharFor(tLeft[k, j]));
        Console.WriteLine(sb.ToString());
    }

    var olive = new Rgb24(128, 128, 0);
    int best = -1, bx = 0, by = 0;
    for (int oy = 0; oy <= H - D; oy++)
        for (int ox = 0; ox <= W - D; ox++)
        {
            int c = 0;
            for (int j = 0; j < D; j++) for (int k = 0; k < D; k++) if (px[ox + k, oy + j].Equals(olive)) c++;
            if (c > best) { best = c; bx = ox; by = oy; }
        }
    Console.WriteLine($"=== most-olive bg window @({bx},{by}) olive={best}/576 ===");
    for (int j = 0; j < D; j++)
    {
        var sb = new System.Text.StringBuilder("  ");
        for (int k = 0; k < D; k++) sb.Append(CharFor(px[bx + k, by + j]));
        Console.WriteLine(sb.ToString());
    }

    Console.WriteLine("=== legend (char = R,G,B) ===");
    foreach (var kv in legend.OrderBy(k => k.Value))
        Console.WriteLine($"  {kv.Value} = {kv.Key.R},{kv.Key.G},{kv.Key.B}");
    return 0;
}

// ---- Score every offset against every template ----
var cands = new List<(int x, int y, string tpl, int matched)>();
foreach (var (name, tpl) in templates)
{
    for (int oy = 0; oy <= H - D; oy++)
        for (int ox = 0; ox <= W - D; ox++)
        {
            int m = 0;
            for (int j = 0; j < D; j++)
                for (int k = 0; k < D; k++)
                {
                    var a = px[ox + k, oy + j];
                    var b = tpl[k, j];
                    if (Close(a, b, tol)) m++;
                }
            cands.Add((ox, oy, name, m));
        }
}

cands.Sort((a, b) => b.matched.CompareTo(a.matched));

// Greedy non-overlapping selection above threshold
int need = (int)Math.Ceiling(threshold * D * D);
var accepted = new List<(int x, int y, string tpl, int matched)>();
foreach (var c in cands)
{
    if (c.matched < need) break;
    bool overlaps = accepted.Any(a => Math.Abs(a.x - c.x) < D && Math.Abs(a.y - c.y) < D);
    if (!overlaps) accepted.Add(c);
}

Console.WriteLine($"templates={templates.Count}  threshold={threshold:0.00} ({need}/{D * D} px)  mode={mode}");
Console.WriteLine($"--- accepted door matches: {accepted.Count} ---");
foreach (var a in accepted)
    Console.WriteLine($"  ({a.x,3},{a.y,3})  tpl={a.tpl,-5} match={a.matched}/{D * D} ({100.0 * a.matched / (D * D):0.0}%)");

// Show global best regardless of threshold, so we can calibrate
Console.WriteLine($"--- global best {top} offsets (any score) ---");
var seen = new List<(int x, int y)>();
int shown = 0;
foreach (var c in cands)
{
    if (seen.Any(s => Math.Abs(s.x - c.x) < D && Math.Abs(s.y - c.y) < D)) continue;
    seen.Add((c.x, c.y));
    Console.WriteLine($"  ({c.x,3},{c.y,3})  tpl={c.tpl,-5} match={c.matched}/{D * D} ({100.0 * c.matched / (D * D):0.0}%)");
    if (++shown >= top) break;
}

// For each accepted match print a 24x24 hit map for visual sanity
foreach (var a in accepted)
{
    var tpl = templates.First(t => t.name == a.tpl).px;
    Console.WriteLine($"hit map @({a.x},{a.y}) tpl={a.tpl}  ('#'=match  '.'=mismatch):");
    for (int j = 0; j < D; j++)
    {
        var sb = new System.Text.StringBuilder("  ");
        for (int k = 0; k < D; k++)
        {
            var p = px[a.x + k, a.y + j];
            var t = tpl[k, j];
            sb.Append(Close(p, t, tol) ? '#' : '.');
        }
        Console.WriteLine(sb.ToString());
    }
}

if (cmd == "scan") return 0;

// ---- ERASE ----
int erased = 0;
bg.ProcessPixelRows(acc =>
{
    foreach (var a in accepted)
    {
        var tpl = templates.First(t => t.name == a.tpl).px;
        for (int j = 0; j < D; j++)
        {
            var row = acc.GetRowSpan(a.y + j);
            for (int k = 0; k < D; k++)
            {
                int X = a.x + k;
                var p = px[X, a.y + j];
                var t = tpl[k, j];
                bool isDoor = mode == "box" || Close(p, t, tol);
                if (isDoor)
                {
                    var c = row[X];
                    if (c.A != 0) { row[X] = new Rgba32(c.R, c.G, c.B, 0); erased++; }
                }
            }
        }
    }
});

// Force a real alpha channel: the source PNGs are 24bpp RGB, and ImageSharp
// otherwise preserves that colour-type on save and silently drops our alpha.
var rgbaEncoder = new PngEncoder { ColorType = PngColorType.RgbWithAlpha, BitDepth = PngBitDepth.Bit8 };
await bg.SaveAsPngAsync(outPath!, rgbaEncoder);
Console.WriteLine($"erased {erased} pixels across {accepted.Count} door(s) -> {outPath}");

// Proof image: same pixels, but transparent ones painted magenta so the
// erase is visible at a glance.
if (!emitProof) return 0;
string proof = Path.Combine(Path.GetDirectoryName(outPath!) ?? ".",
    Path.GetFileNameWithoutExtension(outPath!) + ".proof.png");
using (var pImg = bg.CloneAs<Rgba32>())
{
    pImg.ProcessPixelRows(acc =>
    {
        for (int y = 0; y < pImg.Height; y++)
        {
            var row = acc.GetRowSpan(y);
            for (int x = 0; x < pImg.Width; x++)
                if (row[x].A == 0) row[x] = new Rgba32(255, 0, 255, 255);
        }
    });
    await pImg.SaveAsPngAsync(proof);
}
Console.WriteLine($"proof (erased=magenta) -> {proof}");
return 0;

// ---------------------------------------------------------------------------
static Rgb24[,] BuildTemplate(string sheetPath)
{
    using var sheet = Image.Load<Rgba32>(sheetPath);
    // frame 0 is the top-left 48x48; downscale 2x by sampling each block corner
    var t = new Rgb24[D, D];
    sheet.ProcessPixelRows(acc =>
    {
        for (int j = 0; j < D; j++)
        {
            var row = acc.GetRowSpan(j * 2);
            for (int k = 0; k < D; k++)
            {
                var p = row[k * 2];
                t[k, j] = new Rgb24(p.R, p.G, p.B);
            }
        }
    });
    return t;
}

static bool Close(Rgb24 a, Rgb24 b, int tol) =>
    Math.Abs(a.R - b.R) <= tol && Math.Abs(a.G - b.G) <= tol && Math.Abs(a.B - b.B) <= tol;

static bool TemplatesEqual(Rgb24[,] a, Rgb24[,] b)
{
    for (int j = 0; j < D; j++)
        for (int k = 0; k < D; k++)
            if (!a[k, j].Equals(b[k, j])) return false;
    return true;
}
