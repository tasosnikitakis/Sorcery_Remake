// ============================================================================
// IMAGE IMPORT
// SorceryForge — turning a screenshot in assets/import/ into a room background
// ============================================================================
// The owner has JPEG screenshots of the original Sorcery+ with its items,
// doors and enemies in their true positions. This is the pipeline that turns
// one of those files into a registered, editable room (EDITOR_REVIEW item A).
//
// JPEG IS AN INPUT FORMAT ONLY. Nothing downstream of here ever sees one: the
// output is always a PNG in Content/. Erase mode, the punch-out tool and the
// atomic background save all write transparency through SaveAsPng, and JPEG
// has no alpha channel — a JPEG round-trip anywhere in that chain would
// silently throw the holes away. Decode here, write PNG, never look back.
//
// NO NEW DEPENDENCIES. MonoGame's Texture2D.FromStream already decodes JPEG
// and PNG (bundled StbImageSharp), and Texture2D.SaveAsPng already encodes.
// Everything between those two calls — resampling, quantizing — is plain
// Color[] arithmetic and lives in this file.
//
// UI-FREE AND GRAPHICS-FREE ON PURPOSE, exactly like NewRoomFlow. Not one
// method here touches a Texture2D or a GraphicsDevice: EditorGame decodes into
// a Color[], hands it over, and encodes what comes back. That is what lets
// tools/ImportCheck exercise the whole of the interesting logic headlessly —
// the two MonoGame calls at the ends are the only parts a human has to smoke
// test.
//
// THE FLOW, END TO END
//   assets/import/Chateau3.jpg              (the user drops this in)
//     -> FindCandidates                      filename rule + size + id checks
//     -> EditorGame: Texture2D.FromStream    decode to Color[]
//     -> [crop overlay]                      only when the size is not a whole
//                                            multiple of a room; the user drags
//                                            a 20:9 box and it becomes `region`
//     -> PointSample(region)                 down to 320x144
//     -> QuantizeToCpc (toggle, default ON)  snap to the 27 CPC colours
//     -> EditorGame: SaveAsPng               Content/RoomBG_Chateau3.png
//     -> NewRoomFlow.Create                  collision + .mgcb + rooms.json
//   ...and the editor is sitting in room chateau_3 with the screenshot behind
//   it. The source file is left alone (see FindCandidates).
// ============================================================================

using Microsoft.Xna.Framework;
using SorceryRemake.Rooms;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SorceryForge
{
    /// <summary>
    /// One importable file in assets/import/, with everything the picker needs
    /// to show it and everything Create needs to register it.
    /// </summary>
    // Extends RoomCandidate so NewRoomFlow.Create takes it unchanged — the
    // import runs the New Room creation path, it does not reimplement it.
    public class ImportCandidate : RoomCandidate
    {
        public string SourcePath = "";     // absolute path in assets/import/
        public string FileName = "";       // "Chateau3.jpg"
        public string BaseName = "";       // "Chateau3" — the whole naming rule's input

        public int SourceWidth;            // 0 when the header could not be read
        public int SourceHeight;

        /// <summary>
        /// N when the source is exactly N x (320x144) — 1 for a pixel-perfect
        /// 320x144 capture, 2 for 640x288, and so on. 0 for any other size.
        /// </summary>
        public int Multiple;

        /// <summary>
        /// True when picking this file opens the crop step rather than
        /// importing straight away: the size is usable but not a whole
        /// multiple of a room.
        /// </summary>
        public bool NeedsCrop => Problem == null && Multiple <= 0;

        /// <summary>Human-readable source size for the picker row.</summary>
        public string SizeLabel =>
            SourceWidth <= 0 ? "unreadable"
            : Multiple > 0 ? $"{SourceWidth}x{SourceHeight} ({Multiple}x)"
            : $"{SourceWidth}x{SourceHeight}";
    }

    public static class ImageImport
    {
        // Room backgrounds are 320x144 — the same constants EditorLayout holds,
        // restated here because this file is compiled into tools/ImportCheck,
        // which deliberately does not compile the windowed editor.
        public const int RoomWidth = 320;
        public const int RoomHeight = 144;

        // ====================================================================
        // THE AMSTRAD CPC HARDWARE PALETTE
        // ====================================================================
        // The CPC's video hardware can show 27 colours: three levels per RGB
        // channel, every combination. Mode 0 (which Sorcery+ runs in) picks 16
        // of those 27 for its palette at any one time.
        //
        // SOURCE OF THE LEVELS: extraction/convert_cpc_graphics.py, this
        // project's CPC Mode 0 decoder. Its CPC_PALETTE lists 16 triples and
        // every channel value in them is 0, 128 or 255 — so the 27-colour set
        // generated below is exactly that table's superset, and picking the
        // nearest of the 27 can never move a decoded-from-CPC pixel at all.
        // tools/ImportCheck asserts that containment against a hardcoded copy
        // of those 16 triples, so the two files cannot drift apart silently.
        //
        // WHAT THE SHIPPED ROOM BACKGROUNDS ACTUALLY USE — read this before
        // judging the quantizer's output. Their channel levels are NOT these:
        //   RoomBG_Chateau{0,1,2}, *Chateau (6 rooms)  R/B 0,123,255  G 0,125,251
        //   RoomBG_{Stonehenge,TunnelMouth,Wastelands} R/B 0,99,206   G 0,101,207
        // Two different emulator palettes, neither of them 0/128/255. So the
        // claim "a screenshot of the real game is already in-palette and
        // quantizes to itself" holds only for the levels the capture was made
        // with. Against this table a 123 moves to 128 (invisible) and a 206
        // moves to 255 (not invisible). The toggle is the answer either way:
        // quantize ON normalises everything onto one clean palette that also
        // matches the sprite sheets (which do use 128/255); OFF passes the
        // source through untouched. Judge it on a real screenshot — that is a
        // deliberate item on the owner's smoke list.
        //
        // Changing the levels is a one-line edit to CpcLevels; everything else,
        // including ImportCheck's containment assertion, follows from it.
        // ====================================================================

        /// <summary>The three per-channel levels the CPC hardware can output.</summary>
        public static readonly byte[] CpcLevels = { 0, 128, 255 };

        /// <summary>
        /// All 27 CPC hardware colours: every combination of the three levels.
        /// </summary>
        public static readonly Color[] CpcPalette = BuildCpcPalette();

        private static Color[] BuildCpcPalette()
        {
            var palette = new Color[CpcLevels.Length * CpcLevels.Length * CpcLevels.Length];
            int i = 0;
            foreach (byte r in CpcLevels)
            foreach (byte g in CpcLevels)
            foreach (byte b in CpcLevels)
                palette[i++] = new Color(r, g, b, (byte)255);
            return palette;
        }

        /// <summary>
        /// Snap every pixel to the nearest CPC hardware colour, in place.
        /// Nearest means smallest squared RGB distance; there is no dithering.
        /// </summary>
        // WHY THIS IS WORTH DOING. The sources are JPEGs, and JPEG's DCT turns
        // a flat block of one colour into a cloud of near-misses. Those
        // near-misses are invisible on screen and ruinous to edit: Erase mode
        // and the punch-out tool cut hard rectangles, and a hard cut through
        // noise leaves a visibly ragged seam. Snapping first restores genuine
        // flats, so every later cut lands on a clean edge.
        //
        // Fully transparent pixels are normalised to (0,0,0,0) rather than
        // snapped — the same normalisation EditorGame.LoadRoom applies, for the
        // same reason: RGB left under an alpha-0 hole bleeds additively under
        // premultiplied blending. Any other alpha is preserved as-is; only RGB
        // is quantized.
        public static void QuantizeToCpc(Color[] pixels)
        {
            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                if (c.A == 0) { pixels[i] = Color.Transparent; continue; }

                // Hand-rolled scan of all 27. It is also true that, because the
                // palette is the full product of the three levels, snapping
                // each channel independently gives the identical answer — the
                // squared distance separates across channels. The explicit scan
                // is kept because it stays correct if CpcPalette ever stops
                // being a full product.
                int best = 0, bestDist = int.MaxValue;
                for (int p = 0; p < CpcPalette.Length; p++)
                {
                    Color q = CpcPalette[p];
                    int dr = c.R - q.R, dg = c.G - q.G, db = c.B - q.B;
                    int dist = dr * dr + dg * dg + db * db;
                    if (dist >= bestDist) continue;
                    bestDist = dist;
                    best = p;
                }

                Color snapped = CpcPalette[best];
                pixels[i] = new Color(snapped.R, snapped.G, snapped.B, c.A);
            }
        }

        // ====================================================================
        // RESAMPLING
        // ====================================================================

        /// <summary>
        /// Nearest-neighbour point sample of <paramref name="region"/> of a
        /// source image down (or up) to dstW x dstH. No filtering, ever.
        /// </summary>
        // The mapping is a floor, sx = region.X + dx * region.Width / dstW, not
        // the half-pixel-centred variant. For an exact integer multiple that
        // makes it literally "every Nth pixel starting at the first", which is
        // the lossless answer for a scaled-up capture of a 320x144 screen: each
        // source block of NxN identical pixels contributes its top-left, and
        // the result is the original screen back, bit for bit.
        //
        // For an awkward scale factor (a 700-wide window capture, say) it is
        // the least-bad option rather than the right one — some source columns
        // are dropped and the spacing wobbles by a pixel. It still beats any
        // filter: a filter would invent colours that are not in the palette and
        // blur exactly the hard edges the punch-out tool needs. The CPC
        // quantize then cleans up whatever the wobble left.
        public static Color[] PointSample(Color[] src, int srcW, int srcH,
                                          Rectangle region, int dstW, int dstH)
        {
            if (src == null) throw new ArgumentNullException(nameof(src));
            if (srcW <= 0 || srcH <= 0)
                throw new ArgumentOutOfRangeException(nameof(srcW), "source dimensions must be positive");
            if (src.Length < srcW * srcH)
                throw new ArgumentException("pixel array is smaller than the stated dimensions", nameof(src));
            if (dstW <= 0 || dstH <= 0)
                throw new ArgumentOutOfRangeException(nameof(dstW), "destination dimensions must be positive");
            if (region.Width <= 0 || region.Height <= 0
                || region.Left < 0 || region.Top < 0
                || region.Right > srcW || region.Bottom > srcH)
                throw new ArgumentOutOfRangeException(nameof(region),
                    $"region {region} does not lie inside the {srcW}x{srcH} source");

            var dst = new Color[dstW * dstH];
            for (int dy = 0; dy < dstH; dy++)
            {
                int sy = region.Y + dy * region.Height / dstH;
                int rowBase = sy * srcW;
                int dstBase = dy * dstW;
                for (int dx = 0; dx < dstW; dx++)
                {
                    int sx = region.X + dx * region.Width / dstW;
                    dst[dstBase + dx] = src[rowBase + sx];
                }
            }
            return dst;
        }

        /// <summary>
        /// The whole conversion, from decoded source pixels to the 320x144
        /// Color[] that becomes Content/RoomBG_&lt;Name&gt;.png.
        /// </summary>
        // One entry point for both routes into the pipeline: the picker passes
        // the full image as the region, the crop overlay passes the rectangle
        // the user dragged. Nothing else differs between them.
        public static Color[] BuildRoomBackground(Color[] src, int srcW, int srcH,
                                                  Rectangle region, bool quantize)
        {
            var pixels = PointSample(src, srcW, srcH, region, RoomWidth, RoomHeight);
            if (quantize) QuantizeToCpc(pixels);
            return pixels;
        }

        /// <summary>The whole source image as a region — the no-crop case.</summary>
        public static Rectangle WholeImage(int srcW, int srcH) => new(0, 0, srcW, srcH);

        // ====================================================================
        // SIZE CLASSIFICATION
        // ====================================================================

        /// <summary>
        /// N when (w, h) is exactly N x (320x144); 0 for every other size.
        /// </summary>
        public static int ExactMultiple(int w, int h)
        {
            if (w <= 0 || h <= 0) return 0;
            if (w % RoomWidth != 0 || h % RoomHeight != 0) return 0;
            int n = w / RoomWidth;
            return n == h / RoomHeight ? n : 0;
        }

        /// <summary>The expected-size message shown on a source we can't take as-is.</summary>
        public static string ExpectedSizeMessage(int w, int h) =>
            $"{w}x{h} is not {RoomWidth}x{RoomHeight} or an exact multiple " +
            $"({RoomWidth * 2}x{RoomHeight * 2}, {RoomWidth * 3}x{RoomHeight * 3}, ...)";

        // ====================================================================
        // CROP SELECTION
        // ====================================================================
        // A real emulator capture arrives with a border, a scaled window, and
        // whatever aspect the screenshot key produced — almost never an exact
        // multiple of 320x144. The crop step is how those get in: the user
        // drags a fixed-aspect box over the image and that box becomes the
        // room.
        //
        // The maths lives here rather than in the overlay for the usual reason
        // — no GraphicsDevice, so tools/ImportCheck can drive a crop directly
        // and check the pixels that come out, which is the one thing clicking
        // around in the editor cannot tell you.
        //
        // ASPECT. 320:144 is 20:9. The selection's height is always derived
        // from its width, so the aspect cannot drift while dragging; the
        // rounding in CropHeightFor leaves it out by at most half a source
        // pixel, which point sampling absorbs entirely.
        //
        // MINIMUM. 320x144 source pixels. Below that the crop would be
        // upscaling, which invents pixels — if the capture really is smaller
        // than one room, the answer is a better capture, not interpolation.
        // ====================================================================

        /// <summary>Height of a room-aspect selection <paramref name="width"/> source pixels wide.</summary>
        public static int CropHeightFor(int width) =>
            (width * RoomHeight + RoomWidth / 2) / RoomWidth;

        /// <summary>True when a source is at least one room in both dimensions.</summary>
        public static bool CanCrop(int srcW, int srcH) => srcW >= RoomWidth && srcH >= RoomHeight;

        /// <summary>Widest room-aspect selection that still fits inside the source.</summary>
        public static int MaxCropWidth(int srcW, int srcH)
        {
            int w = Math.Min(srcW, srcH * RoomWidth / RoomHeight);
            // CropHeightFor rounds, so the width the division suggested can
            // still be one pixel too tall. Walk it back rather than solving the
            // rounding algebraically — it is at most a step or two.
            while (w > RoomWidth && CropHeightFor(w) > srcH) w--;
            return w;
        }

        /// <summary>
        /// Force a selection back into shape: room aspect, no smaller than one
        /// room, no bigger than the source, and wholly inside it.
        /// </summary>
        // Every mutation of the selection goes through here, so "the rectangle
        // is always valid" is a property of the type rather than something each
        // drag and wheel handler has to remember.
        public static Rectangle ClampCropRect(Rectangle rect, int srcW, int srcH)
        {
            int maxW = Math.Max(RoomWidth, MaxCropWidth(srcW, srcH));
            int w = Math.Clamp(rect.Width, RoomWidth, maxW);
            int h = CropHeightFor(w);
            int x = Math.Clamp(rect.X, 0, Math.Max(0, srcW - w));
            int y = Math.Clamp(rect.Y, 0, Math.Max(0, srcH - h));
            return new Rectangle(x, y, w, h);
        }

        /// <summary>The selection the crop step opens with: as large as fits, centred.</summary>
        // Starting at maximum means the common case — a capture that is one
        // room plus a border — needs a nudge inward rather than a hunt for the
        // room in an image the box does not yet cover.
        public static Rectangle DefaultCropRect(int srcW, int srcH)
        {
            int w = MaxCropWidth(srcW, srcH);
            int h = CropHeightFor(w);
            return ClampCropRect(new Rectangle((srcW - w) / 2, (srcH - h) / 2, w, h), srcW, srcH);
        }

        // ====================================================================
        // CROP PRESETS
        // ====================================================================
        // Every source in a batch of emulator captures is framed identically —
        // same emulator, same window, same screenshot key — so the rectangle
        // that was right for the first one is right for all of them. Framing it
        // seventy-five times by hand is the whole of the tedium the crop step
        // adds, and it is entirely avoidable: remember the rectangle against
        // the SOURCE DIMENSIONS it was cut from, and offer it back the next
        // time a source of that size turns up.
        //
        // Keyed by (width, height) and nothing else. Not by filename, not by
        // folder: dimensions are what actually determine where the playfield
        // sits inside a capture, and they are the one thing that stays true
        // across renaming and moving files around.
        //
        // A PRESET IS A STARTING POSITION, NOT A DECISION. The crop step still
        // opens, still draws the box, still waits for Enter. One glance
        // confirms the frame is right; a nudge fixes it if it isn't, and the
        // nudged rectangle becomes the new preset. Nothing is ever cut without
        // the user seeing what is about to be cut.
        //
        // WHERE THEY LIVE. .sorceryforge/settings.json at the repo root,
        // gitignored — personal workspace state, see EditorSettings.cs. The
        // one exception is the built-in below, which is in the source because
        // it is a fact about the hardware and this project's captures, not
        // about one person's machine.
        // ====================================================================

        /// <summary>Where a pre-placed crop selection came from.</summary>
        public enum CropPresetOrigin
        {
            /// <summary>No preset applied — the selection is DefaultCropRect.</summary>
            None,
            /// <summary>The shipped calibration for a full CPC frame.</summary>
            BuiltIn,
            /// <summary>The user's last confirmed crop of a source this size.</summary>
            Stored,
        }

        // ====================================================================
        // THE ONE BUILT-IN: A FULL AMSTRAD CPC FRAME
        // ====================================================================
        // 384x270 is what the project's own captures are: the 320x200 Mode 0
        // screen with the hardware border around it. Because the border is part
        // of the frame the emulator draws, the playfield lands at the same
        // offset in every single capture — so this one rectangle serves the
        // whole remaining set of rooms with no crop decision at all.
        //
        // PROVENANCE OF THE NUMBERS. The owner framed a real capture by eye in
        // the crop overlay and read the selection off the header strip:
        //
        //   x = 32   also exactly (384 - 320) / 2, the CPC's horizontal border
        //            arithmetic. Two independent derivations agreeing is the
        //            reason to trust it.
        //   y = 41   measured, not derived. The vertical border is not
        //            symmetric about the playfield — the 320x144 room is a
        //            slice of the 200-line screen, not the whole of it — so
        //            there is no arithmetic to check this against. It is what
        //            the picture actually showed.
        //   320x144  exactly one room: this crop is a 1:1 copy, not a rescale,
        //            which is the best case the import has.
        //
        // It is a DEFAULT, not a law. The moment the user confirms a crop of a
        // 384x270 source, their rectangle is stored and takes precedence here
        // forever after (ResolveCropRect checks stored first). A different
        // emulator with a different border would be corrected once and then
        // remembered.
        // ====================================================================

        public const int CpcFrameWidth = 384;
        public const int CpcFrameHeight = 270;

        /// <summary>The playfield inside a 384x270 CPC frame capture.</summary>
        public static readonly Rectangle CpcFrameCrop = new(32, 41, RoomWidth, RoomHeight);

        /// <summary>The shipped preset for a source size, if there is one.</summary>
        public static bool TryBuiltInCropPreset(int srcW, int srcH, out Rectangle rect)
        {
            if (srcW == CpcFrameWidth && srcH == CpcFrameHeight)
            {
                rect = CpcFrameCrop;
                return true;
            }
            rect = Rectangle.Empty;
            return false;
        }

        /// <summary>
        /// The selection the crop step should open with: the user's stored
        /// preset for this size, else the built-in for this size, else the
        /// largest box that fits.
        /// </summary>
        // Every branch returns through ClampCropRect or DefaultCropRect, both
        // of which enforce the aspect, the floor and the bounds — so a
        // hand-edited settings file holding nonsense costs a badly placed box
        // the user can see and move, never an out-of-range region reaching
        // PointSample.
        public static Rectangle ResolveCropRect(int srcW, int srcH, Rectangle? stored,
                                                out CropPresetOrigin origin)
        {
            if (stored.HasValue)
            {
                origin = CropPresetOrigin.Stored;
                return ClampCropRect(stored.Value, srcW, srcH);
            }
            if (TryBuiltInCropPreset(srcW, srcH, out var builtIn))
            {
                origin = CropPresetOrigin.BuiltIn;
                return ClampCropRect(builtIn, srcW, srcH);
            }
            origin = CropPresetOrigin.None;
            return DefaultCropRect(srcW, srcH);
        }

        /// <summary>
        /// True when a source this size opens the crop step already framed —
        /// which is what makes it eligible for a batch import.
        /// </summary>
        public static bool HasCropPreset(int srcW, int srcH, Rectangle? stored) =>
            stored.HasValue || TryBuiltInCropPreset(srcW, srcH, out _);

        /// <summary>One line naming where the opening selection came from.</summary>
        public static string DescribeCropPreset(CropPresetOrigin origin, int srcW, int srcH) => origin switch
        {
            CropPresetOrigin.Stored  => $"preset from last {srcW}x{srcH} crop",
            CropPresetOrigin.BuiltIn => $"built-in {srcW}x{srcH} preset (CPC full frame)",
            _                        => "no preset — largest 20:9 box that fits",
        };

        /// <summary>Smallest width change one wheel notch can make, in source pixels.</summary>
        public const int CropMinStep = 8;

        /// <summary>
        /// Resize the selection by one wheel notch about its own centre.
        /// <paramref name="direction"/> is +1 for wheel-up, which tightens the
        /// selection — the same "up means closer in" the canvas zoom uses.
        /// </summary>
        // The step is a tenth of the current width rather than a constant, so
        // the number of notches it takes to cross the whole range is the same
        // whether the source is 700 pixels wide or 4000.
        public static Rectangle StepCropWidth(Rectangle rect, int direction, int srcW, int srcH)
        {
            if (direction == 0) return rect;
            int delta = Math.Max(CropMinStep, rect.Width / 10);
            int wanted = rect.Width + (direction > 0 ? -delta : delta);

            int maxW = Math.Max(RoomWidth, MaxCropWidth(srcW, srcH));
            int w = Math.Clamp(wanted, RoomWidth, maxW);
            int h = CropHeightFor(w);

            int cx = rect.X + rect.Width / 2;
            int cy = rect.Y + rect.Height / 2;
            return ClampCropRect(new Rectangle(cx - w / 2, cy - h / 2, w, h), srcW, srcH);
        }

        // ====================================================================
        // FIT TRANSFORM
        // ====================================================================
        // The crop overlay shows the whole source scaled to fit the canvas
        // area, and has to turn mouse positions into source pixels and the
        // selection back into screen pixels. Both directions live here so they
        // cannot disagree — the classic way a crop box ends up cutting
        // somewhere other than where it was drawn.
        // ====================================================================

        /// <summary>
        /// The largest rectangle with the source's aspect that fits inside
        /// <paramref name="area"/>, centred in it.
        /// </summary>
        public static Rectangle FitInside(int srcW, int srcH, Rectangle area)
        {
            if (srcW <= 0 || srcH <= 0 || area.Width <= 0 || area.Height <= 0)
                return new Rectangle(area.X, area.Y, 0, 0);

            float scale = Math.Min(area.Width / (float)srcW, area.Height / (float)srcH);
            int w = Math.Max(1, (int)(srcW * scale));
            int h = Math.Max(1, (int)(srcH * scale));
            return new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
        }

        /// <summary>Screen point → source pixel, through a FitInside rectangle.</summary>
        public static Point ScreenToSource(Point screen, Rectangle fit, int srcW, int srcH)
        {
            if (fit.Width <= 0 || fit.Height <= 0) return Point.Zero;
            return new Point(
                Math.Clamp((screen.X - fit.X) * srcW / fit.Width, 0, Math.Max(0, srcW - 1)),
                Math.Clamp((screen.Y - fit.Y) * srcH / fit.Height, 0, Math.Max(0, srcH - 1)));
        }

        /// <summary>Source-pixel distance for a screen-pixel distance (drag deltas).</summary>
        public static Point ScreenDeltaToSource(Point delta, Rectangle fit, int srcW, int srcH)
        {
            if (fit.Width <= 0 || fit.Height <= 0) return Point.Zero;
            return new Point(
                (int)Math.Round(delta.X * srcW / (float)fit.Width),
                (int)Math.Round(delta.Y * srcH / (float)fit.Height));
        }

        /// <summary>Source rectangle → screen rectangle, through a FitInside rectangle.</summary>
        public static Rectangle SourceRectToScreen(Rectangle rect, Rectangle fit, int srcW, int srcH)
        {
            if (srcW <= 0 || srcH <= 0) return Rectangle.Empty;
            // Both edges are mapped and then subtracted, rather than mapping
            // the origin and scaling the size: that keeps the drawn box flush
            // with the drawn image at every scale instead of drifting a pixel
            // wide or narrow as the selection moves.
            int left   = fit.X + rect.Left   * fit.Width  / srcW;
            int right  = fit.X + rect.Right  * fit.Width  / srcW;
            int top    = fit.Y + rect.Top    * fit.Height / srcH;
            int bottom = fit.Y + rect.Bottom * fit.Height / srcH;
            return new Rectangle(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
        }

        // ====================================================================
        // THE FILENAME RULE
        // ====================================================================
        // Zero typing, same as New Room: the source file's base name is the
        // only input the user gives, and it decides the Content asset name, the
        // room id and the display name. Rename the file, get a different room.
        //
        //   assets/import/Chateau3.jpg
        //     -> Content/RoomBG_Chateau3.png       (AssetNameFor)
        //     -> room id "chateau_3"               (NewRoomFlow.DeriveRoomId)
        //     -> display name "Chateau 3"          (NewRoomFlow.DeriveDisplayName)
        //
        // PascalCase gives the nicest results, because the derivation splits
        // words at internal capitals. A base name is legal if it is one or more
        // of [A-Za-z0-9_-] and nothing else: those are the characters that are
        // safe in a Content pipeline asset name and in a room id, which is a
        // persistence key. Anything else — a space, an accent, a dot — is
        // listed in the picker but refused, with "rename the file" as the fix,
        // because the editor has no text field to offer instead.
        // ====================================================================

        /// <summary>Extensions the picker will list. JPEG is input-only; see the file header.</summary>
        public static readonly string[] SourceExtensions = { ".jpg", ".jpeg", ".png" };

        /// <summary>True when a base name is usable as-is for an asset name and a room id.</summary>
        public static bool IsLegalBaseName(string baseName)
        {
            if (string.IsNullOrEmpty(baseName)) return false;
            foreach (char c in baseName)
            {
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')
                       || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        // ====================================================================
        // CANDIDATES
        // ====================================================================

        /// <summary>
        /// Every importable file in <paramref name="importDir"/>, in filename
        /// order, each with its derived names, its source size, and any reason
        /// it can't be imported.
        /// </summary>
        // The source file is never consumed, moved or deleted. The folder is
        // gitignored, so leaving it costs nothing, and re-importing with the
        // quantize toggle the other way round is then just another click. What
        // stops a second click from quietly overwriting a background the user
        // has since erased pixels out of is the target-exists check below.
        public static List<ImportCandidate> FindCandidates(string importDir, string contentDir,
                                                           IReadOnlyList<RoomManifest> registry)
        {
            var result = new List<ImportCandidate>();
            if (!Directory.Exists(importDir)) return result;

            var takenIds = NewRoomFlow.TakenRoomIds(registry);
            var derivedSoFar = new HashSet<string>(StringComparer.Ordinal);

            var files = new List<string>();
            foreach (string path in Directory.GetFiles(importDir))
            {
                string ext = Path.GetExtension(path);
                foreach (string want in SourceExtensions)
                    if (string.Equals(ext, want, StringComparison.OrdinalIgnoreCase)) { files.Add(path); break; }
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);

            foreach (string path in files)
            {
                string baseName = Path.GetFileNameWithoutExtension(path);
                var candidate = new ImportCandidate
                {
                    SourcePath = path,
                    FileName = Path.GetFileName(path),
                    BaseName = baseName,
                };

                // An illegal base name stops everything: there is no asset name
                // to derive, so no id, so no point reading the image header.
                if (!IsLegalBaseName(baseName))
                {
                    candidate.Problem =
                        "rename the file — the name may hold only letters, digits, '_' and '-' " +
                        "(PascalCase reads best: Chateau3.jpg)";
                    result.Add(candidate);
                    continue;
                }

                string asset = NewRoomFlow.AssetNameFor(baseName);
                var derived = NewRoomFlow.MakeCandidate(asset);
                candidate.BackgroundAsset = derived.BackgroundAsset;
                candidate.RoomId = derived.RoomId;
                candidate.DisplayName = derived.DisplayName;

                if (TryReadImageSize(path, out int w, out int h))
                {
                    candidate.SourceWidth = w;
                    candidate.SourceHeight = h;
                    candidate.Multiple = ExactMultiple(w, h);
                }

                candidate.Problem = DescribeProblem(candidate, contentDir, takenIds, derivedSoFar);
                result.Add(candidate);
            }
            return result;
        }

        /// <summary>
        /// Why this source can't be imported, or null if it can. Checked in
        /// order of how fundamental the objection is: an id collision cannot be
        /// fixed by deleting a file, so it is reported ahead of one that can.
        /// </summary>
        private static string? DescribeProblem(ImportCandidate candidate, string contentDir,
                                               HashSet<string> takenIds, HashSet<string> derivedSoFar)
        {
            string? idProblem = NewRoomFlow.CheckRoomId(candidate.RoomId, takenIds);
            if (idProblem != null) return idProblem;

            // Same short-circuit rule FindCandidates uses in NewRoomFlow: an id
            // that already failed above is not recorded, so a second file
            // deriving it reports the same first reason.
            if (!derivedSoFar.Add(candidate.RoomId))
                return $"another file in assets/import/ already derives '{candidate.RoomId}'";

            // Skip-if-target-exists. A re-import of a background the user has
            // since erased or punched would throw that work away without
            // asking, and this flow has no undo that reaches across a file
            // write. Deleting the PNG is the explicit opt-in.
            string targetPng = Path.Combine(contentDir, candidate.BackgroundAsset + ".png");
            if (File.Exists(targetPng))
                return $"{candidate.BackgroundAsset}.png already exists in Content/ — " +
                       "register it with New Room, or delete it to re-import";

            if (candidate.SourceWidth <= 0)
                return "could not read the image size — is the file a real JPEG or PNG?";

            // Any other size is not a refusal: picking it opens the crop step
            // (NeedsCrop). The one size that IS a refusal is a source smaller
            // than a single room, because cropping it could only upscale.
            if (candidate.Multiple <= 0 && !CanCrop(candidate.SourceWidth, candidate.SourceHeight))
                return $"{candidate.SourceWidth}x{candidate.SourceHeight} is smaller than a " +
                       $"{RoomWidth}x{RoomHeight} room — there is nothing to crop out of it";

            return null;
        }

        // ====================================================================
        // BATCH IMPORT
        // ====================================================================
        // Once a preset exists, a source of that size needs no decision at all:
        // the crop is known, the quantize toggle is already set, and the naming
        // rule takes it from there. Seventy-five files that each need one click
        // and one Enter is seventy-five chances to misclick; "Import All" is
        // the same seventy-five with one keypress.
        //
        // WHAT IT DOES NOT DO. It does not lower any bar. Every file still goes
        // through the same FindCandidates checks and the same NewRoomFlow.Create
        // — one creation path, exactly as the single import has always used —
        // and anything that fails is SKIPPED and named, never forced through.
        // The batch is a loop over the existing functions, not a second import.
        //
        // ELIGIBLE means "would need no decision if you clicked it":
        //
        //   - the candidate has no Problem (name, id, target, size all fine),
        //     AND
        //   - it is an exact multiple of a room (no crop step at all), OR its
        //     source size has a preset — stored or built-in — so the crop step
        //     would open already framed.
        //
        // Everything else is listed as a skip with its reason. A size with no
        // preset is the interesting one: the fix is to import ONE of them by
        // hand, which stores the preset, after which the rest are eligible.
        // ====================================================================

        /// <summary>Fewest eligible files worth offering a batch for.</summary>
        // One file is not a batch — pressing A would be a slower way to click
        // the row that is already in front of you.
        public const int MinBatchSize = 2;

        /// <summary>
        /// The region a batch would cut from a source of this size, or null
        /// when the size still needs a human to frame it.
        /// </summary>
        // The single decision function, called twice on purpose: once by
        // PlanBatch against the size read from the file HEADER (to decide what
        // to offer), and again by the editor against the size the DECODER
        // actually produced (to decide what to cut). A header this misreads
        // therefore costs a skip with a reason, never a bad room.
        public static Rectangle? BatchRegionFor(int srcW, int srcH, Rectangle? stored,
                                                out CropPresetOrigin origin)
        {
            origin = CropPresetOrigin.None;
            if (srcW <= 0 || srcH <= 0) return null;

            // An exact multiple never opens the crop step, so it needs no
            // preset — the whole image IS the region.
            if (ExactMultiple(srcW, srcH) > 0) return WholeImage(srcW, srcH);

            if (!CanCrop(srcW, srcH)) return null;
            if (!HasCropPreset(srcW, srcH, stored)) return null;
            return ResolveCropRect(srcW, srcH, stored, out origin);
        }

        /// <summary>One file a batch will import, and the region it will cut.</summary>
        public class BatchEntry
        {
            public ImportCandidate Candidate;
            public Rectangle Region;              // source pixels, from the header size
            public CropPresetOrigin Origin;

            public BatchEntry(ImportCandidate candidate, Rectangle region, CropPresetOrigin origin)
            {
                Candidate = candidate;
                Region = region;
                Origin = origin;
            }
        }

        /// <summary>One file a batch will not touch, and why.</summary>
        public class BatchSkip
        {
            /// <summary>The room id if one could be derived, else the filename.</summary>
            // The id is what the summary is about — it is what would have been
            // created — but a file refused for its NAME never got one, and
            // there the filename is the only handle the user has.
            public string Label;
            public string Reason;

            public BatchSkip(string label, string reason)
            {
                Label = label;
                Reason = reason;
            }
        }

        /// <summary>How a folder of candidates partitions for a batch import.</summary>
        public class BatchPlan
        {
            public readonly List<BatchEntry> Eligible = new();
            public readonly List<BatchSkip> Skipped = new();

            /// <summary>True when the batch is worth offering at all.</summary>
            public bool Offered => Eligible.Count >= MinBatchSize;
        }

        /// <summary>
        /// Partition the picker's candidates into "would import with no
        /// decision" and "would not, because —".
        /// </summary>
        // storedPreset is a lookup rather than a dictionary so this file keeps
        // knowing nothing about EditorSettings or the filesystem; the editor
        // passes its settings' accessor, tools/ImportCheck passes a lambda.
        public static BatchPlan PlanBatch(IReadOnlyList<ImportCandidate> candidates,
                                          Func<int, int, Rectangle?> storedPreset)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (storedPreset == null) throw new ArgumentNullException(nameof(storedPreset));

            var plan = new BatchPlan();
            foreach (var candidate in candidates)
            {
                string label = string.IsNullOrEmpty(candidate.RoomId) ? candidate.FileName : candidate.RoomId;

                // Every refusal the picker already made stands. Restating the
                // picker's own reason keeps the summary and the greyed-out row
                // saying the same thing.
                if (!candidate.CanCreate)
                {
                    plan.Skipped.Add(new BatchSkip(label, candidate.Problem ?? "unavailable"));
                    continue;
                }

                var stored = storedPreset(candidate.SourceWidth, candidate.SourceHeight);
                var region = BatchRegionFor(candidate.SourceWidth, candidate.SourceHeight,
                                            stored, out var origin);
                if (region == null)
                {
                    // Two ways to get here, and only one of them has a fix the
                    // user can act on. Unreachable from the editor today —
                    // FindCandidates already refuses an unreadable source — but
                    // a defensive branch that gives the wrong advice is worse
                    // than no branch at all.
                    plan.Skipped.Add(new BatchSkip(label,
                        candidate.SourceWidth <= 0 || candidate.SourceHeight <= 0
                            ? "the image size could not be read"
                            : $"{candidate.SourceWidth}x{candidate.SourceHeight} has no crop preset yet — " +
                              "import one of these on its own first, then the rest go in a batch"));
                    continue;
                }

                plan.Eligible.Add(new BatchEntry(candidate, region.Value, origin));
            }
            return plan;
        }

        /// <summary>How many skips a summary names before it stops listing.</summary>
        // The status bar is one line. Listing every skip in a folder of
        // seventy-five would push the count — the part that is always worth
        // reading — off the end of it. The tail is COUNTED, never dropped
        // silently: "and 9 more" is the difference between a cap and a lie.
        public const int BatchSummaryListLimit = 5;

        /// <summary>The line the status bar carries when a batch finishes.</summary>
        public static string SummariseBatch(int imported, IReadOnlyList<BatchSkip> skipped, bool aborted)
        {
            int m = skipped?.Count ?? 0;
            var sb = new StringBuilder();
            sb.Append(aborted ? "Import All stopped: imported " : "Import All: imported ").Append(imported);
            sb.Append(", skipped ").Append(m);

            if (m > 0)
            {
                sb.Append(": ");
                int listed = Math.Min(m, BatchSummaryListLimit);
                for (int i = 0; i < listed; i++)
                {
                    if (i > 0) sb.Append("; ");
                    sb.Append(skipped![i].Label).Append(" (").Append(skipped[i].Reason).Append(')');
                }
                if (m > listed) sb.Append($"; and {m - listed} more");
            }

            sb.Append('.');
            if (imported > 0)
                sb.Append(" Rebuild the game (dotnet build) for it to see the backgrounds.");
            return sb.ToString();
        }

        // ====================================================================
        // IMAGE HEADERS
        // ====================================================================
        // The picker states each source's size and greys out the ones it can't
        // take, which means knowing the dimensions before anything is decoded.
        // Decoding every file in the folder just to fill in a list would need a
        // GraphicsDevice and would drag this whole file into the editor's
        // windowed half; reading the two headers is thirty lines and keeps it
        // out. EditorGame re-checks the real dimensions after it decodes, so a
        // header this misreads costs a status-bar message, not a bad room.
        // ====================================================================

        /// <summary>Image dimensions from a PNG or JPEG header, without decoding it.</summary>
        public static bool TryReadImageSize(string path, out int width, out int height)
        {
            width = height = 0;
            try
            {
                using var fs = File.OpenRead(path);
                using var reader = new BinaryReader(fs);
                var signature = reader.ReadBytes(8);
                if (signature.Length < 8) return false;

                if (IsPngSignature(signature)) return TryReadPngSize(reader, out width, out height);
                if (signature[0] == 0xFF && signature[1] == 0xD8)
                {
                    fs.Position = 2;   // straight after the SOI marker
                    return TryReadJpegSize(reader, out width, out height);
                }
                return false;
            }
            // A file that is being written, locked, or simply truncated is a
            // "size unknown" candidate, not a crash. EndOfStreamException is
            // listed first because it derives from IOException.
            catch (EndOfStreamException) { return false; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static bool IsPngSignature(byte[] s) =>
            s[0] == 0x89 && s[1] == 0x50 && s[2] == 0x4E && s[3] == 0x47 &&
            s[4] == 0x0D && s[5] == 0x0A && s[6] == 0x1A && s[7] == 0x0A;

        /// <summary>
        /// PNG: the IHDR chunk is mandated to be first, so width and height sit
        /// at a fixed offset — 4 length bytes, "IHDR", then two big-endian ints.
        /// </summary>
        private static bool TryReadPngSize(BinaryReader reader, out int width, out int height)
        {
            width = height = 0;
            reader.ReadBytes(4);                        // chunk length
            var type = reader.ReadBytes(4);
            if (type.Length < 4 || type[0] != 'I' || type[1] != 'H' || type[2] != 'D' || type[3] != 'R')
                return false;
            width = ReadBigEndianInt32(reader);
            height = ReadBigEndianInt32(reader);
            return width > 0 && height > 0;
        }

        /// <summary>
        /// JPEG: walk the marker segments until a start-of-frame, which carries
        /// the dimensions. Segments are skipped by their declared length rather
        /// than by scanning for the next 0xFF, so an EXIF thumbnail (a whole
        /// second JPEG living inside an APP1 segment) can't be mistaken for the
        /// real frame.
        /// </summary>
        private static bool TryReadJpegSize(BinaryReader reader, out int width, out int height)
        {
            width = height = 0;
            var stream = reader.BaseStream;
            while (true)
            {
                int b = stream.ReadByte();
                if (b < 0) return false;
                if (b != 0xFF) continue;          // resynchronise; a valid file is already aligned

                // Any number of 0xFF fill bytes may precede a marker code.
                int marker;
                do { marker = stream.ReadByte(); } while (marker == 0xFF);
                if (marker < 0) return false;

                // Standalone markers: no length, no payload. TEM (0x01) and the
                // restart markers (0xD0-0xD7); SOI/EOI can't appear here.
                if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD9)) continue;

                int length = ReadBigEndianUInt16(stream);
                if (length < 2) return false;

                // SOF0-SOF15 minus the three that aren't frame headers (DHT
                // 0xC4, JPG 0xC8, DAC 0xCC). Baseline, progressive, lossless
                // and arithmetic frames all lay their first five payload bytes
                // out the same way: precision, height, width.
                bool isFrameHeader = marker >= 0xC0 && marker <= 0xCF
                                     && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                if (isFrameHeader)
                {
                    if (length < 7) return false;
                    if (stream.ReadByte() < 0) return false;   // sample precision
                    height = ReadBigEndianUInt16(stream);
                    width = ReadBigEndianUInt16(stream);
                    return width > 0 && height > 0;
                }

                // Start of scan: entropy-coded data follows and there is no
                // frame header left to find.
                if (marker == 0xDA) return false;

                stream.Seek(length - 2, SeekOrigin.Current);
            }
        }

        /// <summary>Two big-endian bytes, or -1 at end of stream.</summary>
        private static int ReadBigEndianUInt16(Stream stream)
        {
            int hi = stream.ReadByte();
            int lo = stream.ReadByte();
            return (hi < 0 || lo < 0) ? -1 : (hi << 8) | lo;
        }

        private static int ReadBigEndianInt32(BinaryReader reader)
        {
            var b = reader.ReadBytes(4);
            if (b.Length < 4) return 0;
            return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
        }
    }
}
