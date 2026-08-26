// ============================================================================
// PALETTE PANEL
// SorceryForge — the left band: what you can drop onto the canvas
// ============================================================================
// REPLACES LayoutPalette, PaletteViewportRect, PaletteRowRect,
// PaletteRowVisible, ClampPaletteScroll, DrawPaletteChrome, DrawPaletteEntries,
// HandlePaletteInput and HandlePaletteScroll — nine members that between them
// maintained the same rectangle in four places and the same scroll offset in
// two, and whose entire failure mode was those copies disagreeing about where
// a row was. A scrolled palette that hands you the entry above the one you
// clicked is not a bug you find by reading; it is a bug you find by losing an
// afternoon. Here there is one rectangle, ImGui's, used to draw and to hit-test
// in the same call.
//
// WHAT IS DELIBERATELY NOT AN IMGUI WIDGET
//
//   Section headers are NOT CollapsingHeaders. They never collapsed and they
//   were never clickable; making them so would add state to a panel that has
//   none. They are a filled rectangle and a word.
//
//   Rows are NOT Selectables. A Selectable gives you hovered/active/selected
//   with ImGui's precedence and no border; the palette wants three specific
//   backgrounds with ACTIVE outranking HOVER, plus a constant 1-px border that
//   never changes for any state. So a row is an InvisibleButton for the hit
//   region and four draw-list calls for the pixels — which is also how it keeps
//   the exact 44 px height, the 6 px icon inset and the 46/14 label offset.
//
//   Dimming outside Place mode is NOT BeginDisabled. Disabling would grey the
//   row background and border too; the original changes exactly two things —
//   the icon's alpha (90/255) and the label's colour — and leaves the row
//   furniture at full strength.
//
// THE THREE DOCUMENTED DIVERGENCES, all in the scroll:
//
//   1. The wheel scrolls while the cursor is over the ENTRY LIST. It used to
//      scroll while the cursor was anywhere in the panel, including the 30 px
//      "PALETTE" title strip.
//   2. The step is ImGui's, not the old delta * 0.25f (~30 px per notch).
//   3. The scrollbar is DRAGGABLE. The old one was a painted hint that nothing
//      hit-tested, and a scrollbar that cannot be dragged is a thing users try
//      to drag.
//
// Each is a consequence of ImGui owning panel scrolling, which is the whole
// point: it is what makes the wheel unambiguous between this panel and the
// canvas's zoom without a fourth copy of a rectangle.
// ============================================================================

using ImGuiNET;
using NVector2 = System.Numerics.Vector2;

namespace SorceryForge.UI
{
    public static class PalettePanel
    {
        // Geometry, carried over verbatim from LayoutPalette so the panel looks
        // and measures the same.
        private const float TitleHeight = 30f;
        private const float BottomInset = 8f;
        private const float Padding = 8f;
        private const float RowHeight = 44f;
        private const float HeaderHeight = 22f;
        private const float RowGap = 4f;
        private const float SectionGap = 6f;
        private const float IconSize = 32f;
        private const float IconInset = 6f;
        private const float LabelX = 46f;
        private const float LabelY = 14f;

        // Section names in display order. META sits last: it holds room-level
        // markers rather than entities, and appending it leaves every existing
        // section where the muscle memory of anyone who has authored a room
        // expects to find it.
        private static readonly string[] SectionOrder =
            { "WEAPONS", "KEY ITEMS", "ENEMIES", "DOORS", "OTHER", "META" };

        // ---- Row colours, verbatim ----------------------------------------

        private static readonly uint HeaderBg = ChromeTheme.Packed(45, 50, 65);
        private static readonly uint HeaderBorder = ChromeTheme.Packed(80, 90, 120);
        private static readonly uint HeaderText = ChromeTheme.Packed(255, 220, 110);
        private static readonly uint RowBg = ChromeTheme.Packed(38, 42, 52);
        private static readonly uint RowBgHover = ChromeTheme.Packed(50, 55, 70);
        private static readonly uint RowBgActive = ChromeTheme.Packed(80, 90, 130);
        private static readonly uint RowBorder = ChromeTheme.Packed(70, 74, 88);
        private static readonly uint LabelColor = ChromeTheme.Packed(255, 255, 255);
        private static readonly uint LabelDim = ChromeTheme.Packed(140, 140, 150);
        private static readonly uint IconTint = ChromeTheme.Packed(255, 255, 255);
        private static readonly uint IconTintDim = ChromeTheme.Packed(255, 255, 255, 90);

        public static void Draw(IChromeActions actions, EditorState state, in ChromeView view)
        {
            // Zero padding, because every offset below is the absolute one the
            // old layout used and adding ImGui's default 8 to each would move
            // the whole panel.
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new NVector2(0f, 0f));

            if (ChromeTheme.BeginPanel("##sf_palette", EditorLayout.PaletteRect,
                                       ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
                                       | MenuBar.Inert(view)))
            {
                ImGui.SetCursorPos(new NVector2(Padding, Padding));
                ImGui.TextColored(ChromeTheme.Muted, Title(state.Mode));

                ImGui.SetCursorPos(new NVector2(0f, TitleHeight));
                float listHeight = EditorLayout.PaletteRect.Height - TitleHeight - BottomInset;

                // EndChild is paired with the BeginChild CALL, not with its
                // return value — ImGui asserts on an unmatched End. Writing the
                // height guard as a short-circuit inside the if would call
                // EndChild without a BeginChild at a punishingly small window.
                if (listHeight > 0f)
                {
                    // The inert flag has to be repeated on the CHILD.
                    // NoInputs does not propagate: a child is its own ImGui
                    // window, and the rows live in it — so a palette whose
                    // frame was inert but whose list was not stayed clickable
                    // behind a modal picker.
                    if (ImGui.BeginChild("##sf_palette_list",
                            new NVector2(EditorLayout.PaletteWidth, listHeight),
                            ImGuiChildFlags.None, MenuBar.Inert(view)))
                    {
                        DrawEntries(actions, state);
                    }
                    ImGui.EndChild();
                }
            }
            ChromeTheme.EndPanel();

            ImGui.PopStyleVar();
        }

        /// <summary>
        /// The panel's title. Says which mode the palette is in, because in the
        /// other two it is dimmed and ignores clicks, and a panel that stops
        /// answering should say why.
        /// </summary>
        internal static string Title(EditorMode mode) => mode switch
        {
            EditorMode.Paint => "PALETTE (paint mode)",
            EditorMode.Erase => "PALETTE (erase mode)",
            _ => "PALETTE",
        };

        private static void DrawEntries(IChromeActions actions, EditorState state)
        {
            // The palette is interactive only in Place mode. Outside it the
            // entries dim and clicks are ignored — the same rule the old panel
            // advertised with the same two colour changes.
            bool dim = state.Mode != EditorMode.Place;
            float rowWidth = EditorLayout.PaletteWidth - Padding * 2;

            // Every gap below is explicit, so the spacing is the old layout's
            // and not a style default that could change under it.
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new NVector2(0f, 0f));

            foreach (var section in SectionOrder)
            {
                // Skip empty sections so a header never appears with no entries
                // — and consume no vertical space for one, as before.
                if (!HasAny(state, section)) continue;

                SectionHeader(section, rowWidth);
                Gap(RowGap);

                // Insertion order within a section is palette order; the loop
                // above decides the order of the sections themselves.
                for (int i = 0; i < state.Palette.Count; i++)
                {
                    var entry = state.Palette[i];
                    if (entry.Section != section) continue;
                    Row(actions, state, entry, rowWidth, dim, i);
                    Gap(RowGap);
                }

                Gap(SectionGap);
            }

            ImGui.PopStyleVar();
        }

        private static bool HasAny(EditorState state, string section)
        {
            foreach (var p in state.Palette) if (p.Section == section) return true;
            return false;
        }

        private static void SectionHeader(string name, float width)
        {
            ImGui.SetCursorPosX(Padding);
            var p0 = ImGui.GetCursorScreenPos();
            ImGui.Dummy(new NVector2(width, HeaderHeight));

            var p1 = new NVector2(p0.X + width, p0.Y + HeaderHeight);
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(p0, p1, HeaderBg);
            dl.AddRect(p0, p1, HeaderBorder);
            dl.AddText(new NVector2(p0.X + 8f, p0.Y + 4f), HeaderText, name);
        }

        private static void Row(IChromeActions actions, EditorState state,
                                PaletteEntry entry, float width, bool dim, int index)
        {
            ImGui.SetCursorPosX(Padding);
            var p0 = ImGui.GetCursorScreenPos();
            var size = new NVector2(width, RowHeight);

            bool hovered = false;
            if (dim)
            {
                // No hit region at all outside Place mode, so a dimmed row
                // cannot be hovered OR clicked — which is what the dimming is
                // advertising.
                ImGui.Dummy(size);
            }
            else
            {
                // PushID on the entry's INDEX, not its label. Two entries may
                // legitimately share a display name, and an ImGui id collision
                // would fuse them into one widget — hover and click state
                // shared, so one of them stops answering. It also keeps labels
                // as pure display text, the same rule DoorOpeningSide exists to
                // enforce for door sides.
                ImGui.PushID(index);
                ImGui.InvisibleButton("##row", size);
                hovered = ImGui.IsItemHovered();
                // IsItemClicked, not InvisibleButton's return value: ImGui's
                // buttons fire on RELEASE-inside, and every widget in the
                // chrome this replaces fired on the PRESS edge. It matters
                // here more than anywhere: picking an entry up starts a drag,
                // and a drag that only begins when you let go is not a drag.
                if (ImGui.IsItemClicked()) actions.BeginPaletteDrag(entry);
                ImGui.PopID();
            }

            // ACTIVE outranks HOVER, and the border never changes.
            bool active = ReferenceEquals(state.Dragging, entry);
            uint bg = active ? RowBgActive : hovered ? RowBgHover : RowBg;

            var p1 = new NVector2(p0.X + width, p0.Y + RowHeight);
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(p0, p1, bg);
            dl.AddRect(p0, p1, RowBorder);

            var iconMin = new NVector2(p0.X + IconInset, p0.Y + IconInset);
            var iconMax = new NVector2(iconMin.X + IconSize, iconMin.Y + IconSize);
            dl.AddImage(entry.ImGuiTextureId, iconMin, iconMax,
                        entry.IconUv0, entry.IconUv1, dim ? IconTintDim : IconTint);

            // Not truncated, exactly as before: an over-long label runs to the
            // panel edge and is clipped there. The inspector truncates its
            // values because they are data; a palette label is a name.
            dl.AddText(new NVector2(p0.X + LabelX, p0.Y + LabelY),
                       dim ? LabelDim : LabelColor, entry.Label);
        }

        private static void Gap(float h) => ImGui.Dummy(new NVector2(1f, h));
    }
}
