// ============================================================================
// PICKERS
// SorceryForge — the three modal overlays: New Room, Import, and the crop step
// ============================================================================
// REPLACES DrawNewRoomPicker, DrawImportPicker, DrawImportQuantizeToggle,
// DrawCropOverlay's header and footer strips, DrawCropButton, and the three
// click-zone lists (_newRoomButtons, _importButtons, _cropButtons) with their
// populate-in-Draw / consume-next-Update pattern.
//
// MODALITY IS NOT DECIDED HERE, and that is the important part. These are
// ordinary ImGui windows, deliberately NOT BeginPopupModal. An ImGui modal
// forces io.WantCaptureKeyboard true for as long as it is up, which would gate
// off the very keys these overlays live by — Escape to cancel, Enter to
// confirm a crop, A to import all. Modality is what it always was: EditorGame's
// Update returns before the room or the board sees any input while one of these
// is open, and their cancel gestures are read raw and ungated so that a
// right-click over the panel still cancels.
//
// THE CROP SPLIT. The fitted source image, the four shading bands, the
// selection outline, its corner ticks and the drag-and-wheel that move and
// resize it all stay SpriteBatch, in EditorGame. They are a pixel-space tool
// and ImGui has nothing to offer them. What moved here is the header strip
// (what is being cropped, into what) and the footer strip (the controls and the
// two buttons). The router keeps the two apart: hovering either strip makes
// ImGui claim the mouse, so a click on Confirm can never also start a drag —
// which is exactly what the old code's "buttons first, then the image" ordering
// was for.
//
// ONE DOCUMENTED DIVERGENCE. The wheel scrolls the candidate lists while the
// cursor is over the LIST. It used to scroll them from anywhere on screen. With
// a dimmed, centred, screen-filling modal there is very little "anywhere" left,
// and the alternative is a second scroll offset kept beside ImGui's — which is
// the arrangement this whole PR exists to delete.
// ============================================================================

using ImGuiNET;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using NVector2 = System.Numerics.Vector2;

namespace SorceryForge.UI
{
    public static class Pickers
    {
        private const float RowHeight = 46f;
        private const float RowGap = 4f;

        private static readonly uint RowBg = ChromeTheme.Packed(40, 46, 60);
        private static readonly uint RowBgHover = ChromeTheme.Packed(60, 75, 110);
        private static readonly uint RowBgUnavailable = ChromeTheme.Packed(46, 36, 40);
        private static readonly uint RowBorder = ChromeTheme.Packed(90, 100, 130);
        private static readonly uint RowText = ChromeTheme.Packed(255, 255, 255);
        private static readonly uint RowTextUnavailable = ChromeTheme.Packed(190, 150, 150);
        private static readonly uint RowSub = ChromeTheme.Packed(180, 200, 230);
        private static readonly uint RowSubBad = ChromeTheme.Packed(255, 140, 140);

        public static void Draw(IChromeActions actions, in ChromeView view)
        {
            if (view.NewRoomOpen) DrawNewRoom(actions, view);
            else if (view.ImportOpen) DrawImport(actions, view);
            else if (view.CropOpen) DrawCropChrome(actions, view);
        }

        // ====================================================================
        // DIM
        // ====================================================================

        /// <summary>
        /// Dim everything behind a modal so it reads as blocking input, which
        /// it does.
        /// </summary>
        // In its OWN full-screen window, not in the background draw list. The
        // background list renders BELOW every ImGui window, so a dim drawn
        // there would slide under the palette and the inspector and leave them
        // at full brightness — where the old chrome's full-screen FillRect was
        // drawn after them and covered them.
        //
        // NoInputs, so the window itself hit-tests nothing: the crop step
        // reaches its image THROUGH this dim, and the image is a world surface
        // the router must still hand to EditorGame. What stops clicks reaching
        // the bands underneath is their own NoInputs (MenuBar.Inert), not this.
        private static void Dim() =>
            DimRegions("##sf_modal_dim", 170,
                new Rectangle(0, 0, EditorLayout.WindowWidth, EditorLayout.WindowHeight));

        /// <summary>
        /// The crop step's dim: the two SIDE BANDS only, at the crop's own
        /// alpha.
        /// </summary>
        // The crop already dims the whole screen in SpriteBatch, and must —
        // that pass runs BEFORE the fitted image, so the image sits on top of
        // it. But SpriteBatch cannot reach an ImGui window, and the palette and
        // inspector are now ImGui windows painted after every SpriteBatch pass,
        // so they came out at full brightness beside a darkened room. Dimming
        // the full screen again here would darken the crop image too; dimming
        // exactly the two bands the SpriteBatch pass can no longer reach is the
        // whole of the gap. The top and status bands need nothing — the crop's
        // own header and footer strips cover them.
        private static void DimCropBands() =>
            DimRegions("##sf_crop_dim", 210,
                EditorLayout.PaletteRect, EditorLayout.InspectorRect);

        private static void DimRegions(string id, int alpha, params Rectangle[] regions)
        {
            var full = new Rectangle(0, 0, EditorLayout.WindowWidth, EditorLayout.WindowHeight);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new NVector2(0f, 0f));
            if (ChromeTheme.BeginOverlay(id, full,
                    ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoBackground))
            {
                var dl = ImGui.GetWindowDrawList();
                uint shade = ChromeTheme.Packed(0, 0, 0, alpha);
                foreach (var r in regions)
                    dl.AddRectFilled(new NVector2(r.X, r.Y), new NVector2(r.Right, r.Bottom), shade);
            }
            ChromeTheme.EndPanel();
            ImGui.PopStyleVar();
        }

        private static Rectangle Centred(int maxW, int maxH)
        {
            int w = Math.Min(maxW, EditorLayout.WindowWidth - 80);
            int h = Math.Min(maxH, EditorLayout.WindowHeight - 120);
            return new Rectangle((EditorLayout.WindowWidth - w) / 2,
                                 (EditorLayout.WindowHeight - h) / 2, w, h);
        }

        // ====================================================================
        // NEW ROOM
        // ====================================================================

        private static void DrawNewRoom(IChromeActions actions, in ChromeView view)
        {
            Dim();

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new NVector2(14f, 12f));
            if (ChromeTheme.BeginOverlay("##sf_newroom", Centred(660, 540)))
            {
                ImGui.TextColored(ChromeTheme.Amber, "NEW ROOM — pick an unused background");
                ImGui.TextColored(ChromeTheme.Dim,
                    "Content/RoomBG_*.png not already claimed by a room in rooms.json");
                ImGui.Dummy(new NVector2(1f, 6f));

                // EndChild pairs with the CALL, not the return value — see the
                // matching note in PalettePanel.
                float listHeight = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeight() - 12f;
                if (listHeight > 0f)
                {
                    if (ImGui.BeginChild("##sf_newroom_list", new NVector2(0f, listHeight)))
                    {
                        if (view.NewRoomCandidates.Count == 0) NewRoomEmptyText();

                        for (int i = 0; i < view.NewRoomCandidates.Count; i++)
                        {
                            // Captured for the click, exactly as the old lambda
                            // did: the row outlives this iteration of the loop.
                            var captured = view.NewRoomCandidates[i];
                            string sub = captured.CanCreate
                                ? $"-> {captured.RoomId}   \"{captured.DisplayName}\""
                                : $"unavailable: {captured.Problem}";

                            if (CandidateRow(captured.BackgroundAsset + ".png", sub,
                                             captured.CanCreate, i))
                                actions.CreateRoom(captured);
                        }
                    }
                    ImGui.EndChild();
                }

                Footer("Esc / right-click cancels", "Cancel", actions.CancelNewRoomPicker);
            }
            ChromeTheme.EndPanel();
            ImGui.PopStyleVar();
        }

        // The intended path for a room that has no PNG yet is the screenshot
        // import, so say so rather than leaving an empty box.
        private static void NewRoomEmptyText()
        {
            ImGui.TextColored(ChromeTheme.White, "No unused RoomBG_*.png in Content/.");
            ImGui.TextColored(ChromeTheme.Muted, "Every background is already claimed by a room.");
            ImGui.Dummy(new NVector2(1f, 8f));
            ImGui.TextColored(ChromeTheme.Dim, "To add a room from a screenshot, use Import instead —");
            ImGui.TextColored(ChromeTheme.Dim, "it writes the RoomBG_*.png this picker lists.");
        }

        // ====================================================================
        // IMPORT
        // ====================================================================

        private static void DrawImport(IChromeActions actions, in ChromeView view)
        {
            Dim();

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new NVector2(14f, 12f));
            // Wider than the New Room panel because each row carries a
            // filename, a size and a derived room id.
            if (ChromeTheme.BeginOverlay("##sf_import", Centred(760, 560)))
            {
                ImGui.TextColored(ChromeTheme.Amber, "IMPORT SCREENSHOT — pick a file from assets/import/");
                ImGui.TextColored(ChromeTheme.Dim,
                    ChromeTheme.Truncate(view.ImportDir, ImGui.GetContentRegionAvail().X));
                ImGui.Dummy(new NVector2(1f, 6f));

                QuantizeToggle(actions, view);
                ImGui.Dummy(new NVector2(1f, 6f));

                // EndChild pairs with the CALL, not the return value — see the
                // matching note in PalettePanel.
                float listHeight = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeight() - 12f;
                if (listHeight > 0f)
                {
                    if (ImGui.BeginChild("##sf_import_list", new NVector2(0f, listHeight)))
                    {
                        if (view.ImportCandidates.Count == 0) ImportEmptyText();

                        for (int i = 0; i < view.ImportCandidates.Count; i++)
                        {
                            var captured = view.ImportCandidates[i];
                            // "[crop]" marks the sources that open the crop step
                            // instead of importing on the click, so that is
                            // never a surprise.
                            string sub = captured.CanCreate
                                ? (captured.NeedsCrop ? "[crop] " : "") +
                                  $"-> {captured.BackgroundAsset}.png   ->   {captured.RoomId}   \"{captured.DisplayName}\""
                                : $"unavailable: {captured.Problem}";

                            if (CandidateRow($"{captured.FileName}   {captured.SizeLabel}", sub,
                                             captured.CanCreate, i))
                                actions.RunImport(captured);
                        }
                    }
                    ImGui.EndChild();
                }

                // The batch hint appears only when a batch is actually
                // available, so the key can never look broken — the same reason
                // "[crop]" only marks the rows that open the crop step.
                string hint = view.ImportBatchOffered
                    ? $"A imports all {view.ImportBatchCount} ready file(s)   |   Esc / right-click cancels   " +
                      "|   sources are never modified or deleted"
                    : "Esc / right-click cancels   |   sources are never modified or deleted";
                Footer(hint, "Cancel", actions.CancelImportPicker,
                       view.ImportBatchOffered ? ChromeTheme.Value : ChromeTheme.Dim);
            }
            ChromeTheme.EndPanel();
            ImGui.PopStyleVar();
        }

        private static void ImportEmptyText()
        {
            ImGui.TextColored(ChromeTheme.White, "Nothing to import.");
            ImGui.Dummy(new NVector2(1f, 8f));
            ImGui.TextColored(ChromeTheme.Muted, "Drop a .jpg / .jpeg / .png screenshot into assets/import/");
            ImGui.TextColored(ChromeTheme.Muted, "and click Import again. The file name becomes the room:");
            ImGui.Dummy(new NVector2(1f, 6f));
            ImGui.TextColored(ChromeTheme.Dim, "Chateau3.jpg  ->  RoomBG_Chateau3.png  ->  chateau_3");
        }

        /// <summary>
        /// The CPC quantize checkbox. A whole row rather than a small box, so
        /// the click target is obvious and the "why" fits beside it.
        /// </summary>
        private static void QuantizeToggle(IChromeActions actions, in ChromeView view)
        {
            float width = ImGui.GetContentRegionAvail().X;
            var p0 = ImGui.GetCursorScreenPos();

            ImGui.InvisibleButton("##sf_quantize", new NVector2(width, 28f));
            bool hovered = ImGui.IsItemHovered();
            if (ImGui.IsItemClicked()) actions.ToggleImportQuantize();

            var p1 = new NVector2(p0.X + width, p0.Y + 28f);
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(p0, p1, hovered ? RowBgHover : RowBg);
            dl.AddRect(p0, p1, RowBorder);

            var box = new NVector2(p0.X + 7f, p0.Y + 7f);
            dl.AddRect(box, new NVector2(box.X + 14f, box.Y + 14f), ChromeTheme.Packed(170, 180, 205));
            if (view.ImportQuantize)
                dl.AddRectFilled(new NVector2(box.X + 3f, box.Y + 3f),
                                 new NVector2(box.X + 11f, box.Y + 11f),
                                 ChromeTheme.Packed(120, 230, 140));

            dl.AddText(new NVector2(p0.X + 30f, p0.Y + 5f),
                       view.ImportQuantize ? ChromeTheme.Packed(210, 230, 210)
                                           : ChromeTheme.Packed(200, 200, 215),
                       ChromeTheme.Truncate(
                           view.ImportQuantize
                               ? "CPC quantize ON — snap to the 27 hardware colours (removes JPEG noise)"
                               : "CPC quantize OFF — source colours pass through untouched",
                           width - 40f));
        }

        // ====================================================================
        // SHARED ROW / FOOTER
        // ====================================================================

        /// <summary>
        /// One candidate row: a filename line and a derived-name line, coloured
        /// by whether it can be used. Returns true when clicked.
        /// </summary>
        // An unusable row is drawn but has no hit region at all, so it cannot
        // be hovered or clicked — the same rule the palette's dimmed rows
        // follow, and the reason the red tint is trustworthy.
        private static bool CandidateRow(string title, string sub, bool usable, int index)
        {
            float width = ImGui.GetContentRegionAvail().X;
            var p0 = ImGui.GetCursorScreenPos();
            bool clicked = false, hovered = false;

            // Scoped by INDEX rather than by name: two files in assets/import/
            // can derive the same room id — that is precisely the collision the
            // candidate's Problem field reports — and two rows sharing an ImGui
            // id would fuse into one widget.
            ImGui.PushID(index);
            if (usable)
            {
                ImGui.InvisibleButton("##row", new NVector2(width, RowHeight));
                hovered = ImGui.IsItemHovered();
                // Press semantics, as every old click zone had — see PalettePanel.
                clicked = ImGui.IsItemClicked();
            }
            else
            {
                ImGui.Dummy(new NVector2(width, RowHeight));
            }
            ImGui.PopID();

            var p1 = new NVector2(p0.X + width, p0.Y + RowHeight);
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(p0, p1, !usable ? RowBgUnavailable : hovered ? RowBgHover : RowBg);
            dl.AddRect(p0, p1, RowBorder);

            dl.AddText(new NVector2(p0.X + 8f, p0.Y + 5f),
                       usable ? RowText : RowTextUnavailable,
                       ChromeTheme.Truncate(title, width - 20f));
            dl.AddText(new NVector2(p0.X + 8f, p0.Y + 25f),
                       usable ? RowSub : RowSubBad,
                       ChromeTheme.Truncate(sub, width - 20f));

            ImGui.Dummy(new NVector2(1f, RowGap));
            return clicked;
        }

        /// <summary>
        /// A hint on the left and a Cancel button flush right. The button
        /// exists because a modal with no visible way out is a usability trap;
        /// Escape and right-click do the same thing.
        /// </summary>
        private static void Footer(string hint, string buttonLabel, Action onClick,
                                   System.Numerics.Vector4? hintColor = null)
        {
            float buttonWidth = 100f;
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(hintColor ?? ChromeTheme.Dim,
                ChromeTheme.Truncate(hint, ImGui.GetContentRegionAvail().X - buttonWidth - 16f));

            ImGui.SameLine();
            float x = ImGui.GetWindowWidth() - buttonWidth - 14f;
            if (x > ImGui.GetCursorPosX()) ImGui.SetCursorPosX(x);
            if (ChromeTheme.PressButton(buttonLabel, new NVector2(buttonWidth, 0f))) onClick();
        }

        // ====================================================================
        // CROP — header and footer strips only
        // ====================================================================

        private static void DrawCropChrome(IChromeActions actions, in ChromeView view)
        {
            DimCropBands();

            float scale = view.CropRect.Width / (float)ImageImport.RoomWidth;

            // Header strip, over the top bar's band: what is being cropped, and
            // into what.
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new NVector2(14f, 8f));
            if (ChromeTheme.BeginOverlay("##sf_crop_header", EditorLayout.TopBarRect))
            {
                float avail = ImGui.GetContentRegionAvail().X;
                ImGui.TextColored(ChromeTheme.Amber, ChromeTheme.Truncate(
                    $"CROP  {view.CropFileName}  ({view.CropSourceWidth}x{view.CropSourceHeight})  ->  " +
                    $"{view.CropRoomId}   \"{view.CropDisplayName}\"", avail));
                ImGui.TextColored(ChromeTheme.CropDetail,
                    ChromeTheme.Truncate(
                        $"selection {view.CropRect.Width}x{view.CropRect.Height} at " +
                        $"({view.CropRect.X}, {view.CropRect.Y})  ->  " +
                        $"{ImageImport.RoomWidth}x{ImageImport.RoomHeight} ({scale:0.00}x down)   |   " +
                        $"CPC quantize {(view.ImportQuantize ? "ON" : "OFF")}   |   " +
                        // Where the box STARTED. Left unchanged as the user
                        // drags, because that is what it is claiming.
                        view.CropPresetNote, avail));
            }
            ChromeTheme.EndPanel();
            ImGui.PopStyleVar();

            // Footer strip, over the status bar's band: the controls, and the
            // two buttons.
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new NVector2(8f, 4f));
            if (ChromeTheme.BeginOverlay("##sf_crop_footer", EditorLayout.StatusBarRect))
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(ChromeTheme.Status, ChromeTheme.Truncate(
                    "drag to move   |   wheel resizes (20:9 locked)   |   Enter confirms   |   Esc / right-click cancels",
                    ImGui.GetContentRegionAvail().X - 224f));

                ImGui.SameLine();
                float x = ImGui.GetWindowWidth() - 216f;
                if (x > ImGui.GetCursorPosX()) ImGui.SetCursorPosX(x);
                if (ChromeTheme.PressButton("Cancel", new NVector2(100f, 0f))) actions.CancelCrop();
                ImGui.SameLine(0f, 8f);
                if (ChromeTheme.PressButton("Confirm", new NVector2(100f, 0f))) actions.ConfirmCrop();
            }
            ChromeTheme.EndPanel();
            ImGui.PopStyleVar();
        }
    }
}
