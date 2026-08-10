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
//     -> PointSample                         down to 320x144
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

            if (candidate.Multiple <= 0)
                return ExpectedSizeMessage(candidate.SourceWidth, candidate.SourceHeight);

            return null;
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
