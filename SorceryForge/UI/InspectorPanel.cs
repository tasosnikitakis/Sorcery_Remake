// ============================================================================
// INSPECTOR PANEL
// SorceryForge — the right band: every placement in the room, and its fields
// ============================================================================
// REPLACES DrawInspector, DrawSectionBody, DrawInspectorRow, the
// _inspectorButtons click-zone list and _inspectorContentHeight.
//
// THE THING THIS FIXES BY CONSTRUCTION. The old inspector populated its click
// zones during Draw and consumed them during the NEXT Update — MonoGame runs
// Update before Draw, so every hit-test was against ONE-FRAME-STALE rectangles.
// Worse, HandleInspectorScroll ran before HandleInspectorClicks in the same
// Update, so a wheel notch and the click that followed it in one frame
// hit-tested against the PRE-scroll rectangles: you could click one field and
// cycle another. The palette explicitly worked around the same hazard by
// ordering its scroll before its hit-test. ImGui draws and hit-tests in the
// same call, so neither workaround has anything left to do.
//
// WHAT IS DELIBERATELY NOT AN IMGUI WIDGET, and why
//
//   A section header is NOT a CollapsingHeader or a TreeNode. Three reasons,
//   all behavioural: it is TWO lines (kind on the first, the full entity id on
//   the second, indented and truncated); its click does TWO things — select the
//   placement AND toggle its collapse, which cannot be separated because the
//   canvas outline follows the selection; and it has NO hover state at all,
//   only selected/unselected, where an ImGui header hovers by default. The
//   chevron is a literal '+' or '-' followed by two spaces, not a triangle.
//
//   A field is NOT an ImGui Combo either, even now that three of them open a
//   list. PR 7b replaced the cycle-buttons on "Room", "Door" and "Needs" with
//   filterable pickers (EDITOR_REVIEW item 10) — but the ROW is unchanged: the
//   same 16 px label over the same full-width 22 px value box, hit-tested the
//   same way, on the same press edge. Only what the click does is different,
//   which is why every geometry constant below is untouched and
//   tools/ChromeCheck's row arithmetic still finds each field where it was.
//   An ImGui Combo would have brought its own arrow, its own frame padding and
//   its own release-edge click, and none of those are what this panel looks or
//   behaves like.
//
//   "Opens" still cycles. Two values need no list: a dropdown there would be
//   two clicks and a popup to do what one click already does.
//
// LAYOUT is the old arithmetic, kept: 22 + 18 header lines, a 16 px label line
// over a full-width 22 px value box, 40 px rows, a 4 px gap between rows, a
// 6 px gap after a body, a 10 px body indent. The full-width value box exists
// so a long door id never collides with its label.
//
// ONE DOCUMENTED DIVERGENCE. The old panel had no scissor, so a section header
// scrolled past the top of the viewport painted its full 40 px over the
// "INSPECTOR" title. ImGui clips its child, so it no longer does. That was
// never intentional.
// ============================================================================

using ImGuiNET;
using SorceryRemake.Core;
using System;
using System.Collections.Generic;
using NVector2 = System.Numerics.Vector2;

namespace SorceryForge.UI
{
    public static class InspectorPanel
    {
        // Geometry, carried over verbatim from DrawInspector / DrawSectionBody.
        private const float TitleHeight = 32f;
        private const float BottomInset = 8f;
        private const float Padding = 8f;
        private const float HeaderLine1H = 22f;
        private const float HeaderLine2H = 18f;
        private const float HeaderHeight = HeaderLine1H + HeaderLine2H;   // 40
        private const float HeaderGap = 2f;
        private const float BodyIndent = 10f;
        private const float BodyGap = 6f;
        private const float LabelH = 16f;
        private const float ValueH = 22f;
        private const float InnerGap = 2f;
        private const float RowGap = 4f;

        // ---- Colours, verbatim ---------------------------------------------

        private static readonly uint HeaderBg = ChromeTheme.Packed(45, 50, 65);
        private static readonly uint HeaderBgSelected = ChromeTheme.Packed(70, 90, 130);
        private static readonly uint HeaderBorder = ChromeTheme.Packed(80, 90, 120);
        // The same yellow as the canvas selection outline: one visual language
        // for "this is what Delete and drag act on".
        private static readonly uint HeaderBorderSelected = ChromeTheme.Packed(255, 220, 60);
        private static readonly uint HeaderText = ChromeTheme.Packed(255, 255, 255);
        private static readonly uint IdText = ChromeTheme.Packed(180, 200, 230);
        private static readonly uint RowLabel = ChromeTheme.Packed(190, 190, 210);
        private static readonly uint ValueBg = ChromeTheme.Packed(40, 46, 60);
        private static readonly uint ValueBgHover = ChromeTheme.Packed(60, 75, 110);
        private static readonly uint ValueBorder = ChromeTheme.Packed(90, 100, 130);
        private static readonly uint ValueBgReadOnly = ChromeTheme.Packed(34, 38, 50);
        private static readonly uint ValueText = ChromeTheme.Packed(255, 255, 255);

        public static void Draw(IChromeActions actions, EditorState state, in ChromeView view)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new NVector2(0f, 0f));

            if (ChromeTheme.BeginPanel("##sf_inspector", EditorLayout.InspectorRect,
                                       ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse
                                       | MenuBar.Inert(view)))
            {
                ImGui.SetCursorPos(new NVector2(Padding, Padding));
                ImGui.TextColored(ChromeTheme.Muted, "INSPECTOR");

                // Left-aligned at a fixed offset from the right edge, not
                // measured — and deliberately never pluralised, so a room with
                // one entity still reads "1 entities". Both are how it has
                // always been; neither is worth a user-visible change in a
                // migration PR.
                ImGui.SetCursorPos(new NVector2(EditorLayout.InspectorWidth - 110f, Padding));
                ImGui.TextColored(ChromeTheme.Dim, $"{state.Placements.Count} entities");

                ImGui.SetCursorPos(new NVector2(0f, TitleHeight));
                float bodyHeight = EditorLayout.InspectorRect.Height - TitleHeight - BottomInset;

                // EndChild pairs with the CALL, not the return value — see the
                // matching note in PalettePanel.
                if (bodyHeight > 0f)
                {
                    // The inert flag repeated on the CHILD — NoInputs does not
                    // propagate; see the matching note in PalettePanel.
                    if (ImGui.BeginChild("##sf_inspector_list",
                            new NVector2(EditorLayout.InspectorWidth, bodyHeight),
                            ImGuiChildFlags.None, MenuBar.Inert(view)))
                    {
                        DrawSections(actions, state, view);
                    }
                    ImGui.EndChild();
                }
            }
            ChromeTheme.EndPanel();

            ImGui.PopStyleVar();
        }

        private static void DrawSections(IChromeActions actions, EditorState state, in ChromeView view)
        {
            float contentW = EditorLayout.InspectorWidth - Padding * 2;

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new NVector2(0f, 0f));

            if (state.Placements.Count == 0)
            {
                // Exact wording, em dash included.
                ImGui.SetCursorPos(new NVector2(Padding, Padding));
                ImGui.TextColored(ChromeTheme.Dim, "(empty room — drag from the palette)");
            }

            // List order, which is load order — items, enemies, wizards,
            // blocked doors, then doors — with anything dropped this session
            // appended. Same order the save path writes.
            foreach (var placement in state.Placements)
            {
                // The entity id scopes the WHOLE section, header and body.
                //
                // Not just the header: two doors in one room both draw a row
                // labelled "Opens", and an ImGui id is derived from the label
                // plus the enclosing id stack. Push only around the header and
                // those two rows become the SAME widget — hover and click state
                // shared, so pressing one door's "Opens" reports on the other's.
                // The id is already guaranteed unique: it is the persistence key
                // GenerateId spins the counter to keep free.
                ImGui.PushID(placement.Id);

                bool collapsed = state.IsCollapsed(placement.Id);
                SectionHeader(actions, state, placement, contentW, collapsed);
                Gap(HeaderGap);

                if (!collapsed)
                {
                    SectionBody(actions, placement, contentW - BodyIndent * 2, view);
                    Gap(BodyGap);
                }

                ImGui.PopID();
            }

            ImGui.PopStyleVar();
        }

        // ====================================================================
        // SECTION HEADER
        // ====================================================================

        private static void SectionHeader(IChromeActions actions, EditorState state,
                                          Placement p, float width, bool collapsed)
        {
            ImGui.SetCursorPosX(Padding);
            var p0 = ImGui.GetCursorScreenPos();

            // The caller has already pushed the entity id, so "##header" is
            // unique across the panel.
            ImGui.InvisibleButton("##header", new NVector2(width, HeaderHeight));
            // Press semantics, as the old click zones had — see PalettePanel.
            if (ImGui.IsItemClicked()) actions.SelectAndToggleSection(p);

            // No hover state. Selected or not, and nothing in between: the
            // header's colour means "the canvas outline is on this one", and a
            // hover tint would be a second thing the same border was saying.
            bool selected = ReferenceEquals(state.SelectedPlacement, p);
            var p1 = new NVector2(p0.X + width, p0.Y + HeaderHeight);
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(p0, p1, selected ? HeaderBgSelected : HeaderBg);
            dl.AddRect(p0, p1, selected ? HeaderBorderSelected : HeaderBorder);

            // Line 1: chevron, TWO spaces, the kind. A literal '+' / '-'.
            dl.AddText(new NVector2(p0.X + 6f, p0.Y + 3f), HeaderText,
                       $"{(collapsed ? "+" : "-")}  {KindShortLabel(p)}");

            // Line 2: the full entity id, indented past the chevron and
            // truncated to the header's width less 24.
            dl.AddText(new NVector2(p0.X + 18f, p0.Y + HeaderLine1H), IdText,
                       ChromeTheme.Truncate(p.Id, width - 24f));
        }

        /// <summary>
        /// The kind alone — "Item", never "Sword". The id line below already
        /// disambiguates, and Placement.DisplayName exists for status strings.
        /// </summary>
        internal static string KindShortLabel(Placement p) => p.Kind switch
        {
            PlacementKind.Item => "Item",
            PlacementKind.Enemy => "Enemy",
            PlacementKind.Wizard => "Wizard",
            PlacementKind.BlockedDoor => "BlockedDoor",
            PlacementKind.Door => "Door",
            _ => "?",
        };

        // ====================================================================
        // SECTION BODY
        // ====================================================================

        private static void SectionBody(IChromeActions actions, Placement p, float width,
                                        in ChromeView view)
        {
            float x = Padding + BodyIndent;

            // Position is read-only; drag the placement on the canvas to move.
            Row(x, width, "Pos", $"({(int)p.Position.X}, {(int)p.Position.Y})", null);

            switch (p.Kind)
            {
                case PlacementKind.Item:
                    Row(x, width, "Type", p.ItemType.ToString(), null);
                    break;

                case PlacementKind.Enemy:
                    Row(x, width, "Type", p.EnemyType.ToString(), null);
                    break;

                case PlacementKind.Wizard:
                    // Wizards have no extra attributes today — just position.
                    break;

                case PlacementKind.BlockedDoor:
                    // No "(none)" substitution here: an ItemType of None shows
                    // as "None", which is what a hand-edited JSON produces and
                    // what neither the cycle nor the picker can reach again.
                    PickerRow(x, width, "Needs", p.RequiredItem.ToString(),
                        ItemNames(view), picked =>
                        {
                            if (Enum.TryParse(picked, out ItemType item))
                                actions.SetBlockedDoorRequiredItem(p, item);
                        });
                    break;

                case PlacementKind.Door:
                    Row(x, width, "Opens", p.DoorOpeningSide,
                        () => actions.CycleDoorOpeningSide(p));

                    // Room and Door are the two EDITOR_REVIEW item 10 named:
                    // the first is a list of every room in the world, and the
                    // second depends on which one is chosen, so it is fetched
                    // rather than snapshotted.
                    PickerRow(x, width, "Room", OrNone(p.DoorTargetRoomId),
                        WithNone(view.TargetRoomIds),
                        picked => actions.SetDoorTargetRoom(p, NoneToEmpty(picked)));

                    PickerRow(x, width, "Door", OrNone(p.DoorTargetDoorId),
                        WithNone(view.DoorIdsForRoom?.Invoke(p.DoorTargetRoomId)),
                        picked => actions.SetDoorTargetDoor(p, NoneToEmpty(picked)));
                    break;
            }

            // Punch-out is generic — every kind gets the row. A wizard standing
            // on the original game's baked-in artwork needs its footprint cut
            // out just as much as a door does.
            Row(x, width, "Background", "Punch (clear 24x24)",
                () => actions.PunchBackground(p));
        }

        // ====================================================================
        // OPTION LISTS
        // ====================================================================
        // Each is rebuilt into a reused buffer rather than allocated per frame:
        // the inspector redraws sixty times a second whether or not anything is
        // open, and a room with eight doors would otherwise mint sixteen lists
        // a frame for a question nobody is asking.
        //
        // Safe as a single shared buffer because at most one popup is open at a
        // time, and the buffer is consumed inside the PickerRow call that
        // filled it.

        private static readonly List<string> _options = new();

        /// <summary>"(none)" first, then the ids. Null becomes just "(none)".</summary>
        // "(none)" is a REAL entry rather than a way to clear the field with a
        // separate gesture, because it is a real value: a door with no target
        // is what an unfinished room looks like, and the cycle had an empty
        // entry for exactly the same reason.
        private static List<string> WithNone(IReadOnlyList<string>? ids)
        {
            _options.Clear();
            _options.Add(NoneLabel);
            if (ids != null)
                for (int i = 0; i < ids.Count; i++) _options.Add(ids[i]);
            return _options;
        }

        private static List<string> ItemNames(in ChromeView view)
        {
            _options.Clear();
            var items = view.RequiredItems;
            if (items != null)
                for (int i = 0; i < items.Count; i++) _options.Add(items[i].ToString());
            return _options;
        }

        private const string NoneLabel = "(none)";

        private static string OrNone(string value) =>
            string.IsNullOrEmpty(value) ? NoneLabel : value;

        private static string NoneToEmpty(string value) =>
            value == NoneLabel ? "" : value;

        /// <summary>
        /// A field whose value box opens a filterable list instead of cycling.
        /// </summary>
        // Geometrically identical to Row — same label line, same value box,
        // same press-edge hit region — so the two can be mixed in one body and
        // tools/ChromeCheck's row arithmetic is unchanged. The popup is opened
        // INSIDE the row's PushID, which is what makes "##pick" unique per
        // placement per field without a hand-built id string.
        private static void PickerRow(float x, float width, string label, string value,
                                      IReadOnlyList<string> options, Action<string> onPick)
        {
            ImGui.SetCursorPosX(x);
            var labelPos = ImGui.GetCursorScreenPos();
            ImGui.Dummy(new NVector2(width, LabelH));
            ImGui.GetWindowDrawList().AddText(labelPos, RowLabel, label);

            ImGui.SetCursorPosX(x);
            ImGui.Dummy(new NVector2(width, InnerGap));

            ImGui.SetCursorPosX(x);
            var boxPos = ImGui.GetCursorScreenPos();

            ImGui.PushID(label);
            ImGui.InvisibleButton("##value", new NVector2(width, ValueH));
            bool hovered = ImGui.IsItemHovered();
            if (ImGui.IsItemClicked())
            {
                FilterPopup.Open();
                ImGui.OpenPopup(PopupId);
            }

            var boxMax = new NVector2(boxPos.X + width, boxPos.Y + ValueH);
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(boxPos, boxMax, hovered ? ValueBgHover : ValueBg);
            dl.AddRect(boxPos, boxMax, ValueBorder);
            dl.AddText(new NVector2(boxPos.X + 6f, boxPos.Y + 4f), ValueText,
                       ChromeTheme.Truncate(value, width - 12f));

            if (FilterPopup.Body(PopupId, options, value, out string chosen)) onPick(chosen);
            ImGui.PopID();

            Gap(RowGap);
        }

        /// <summary>
        /// The popup's id, resolved against whatever the id stack holds — which
        /// at the call site is the placement's entity id and the field's label.
        /// </summary>
        private const string PopupId = "##pick";

        /// <summary>
        /// One field: a small label on top, a full-row-width value box below.
        /// The two-line shape gives long values — door ids like
        /// chateau1_door_topright — the entire row width, so they never collide
        /// with the label.
        /// </summary>
        private static void Row(float x, float width, string label, string value, Action? onClick)
        {
            ImGui.SetCursorPosX(x);
            var labelPos = ImGui.GetCursorScreenPos();
            ImGui.Dummy(new NVector2(width, LabelH));
            ImGui.GetWindowDrawList().AddText(labelPos, RowLabel, label);

            ImGui.SetCursorPosX(x);
            ImGui.Dummy(new NVector2(width, InnerGap));

            ImGui.SetCursorPosX(x);
            var boxPos = ImGui.GetCursorScreenPos();
            bool hovered = false;

            if (onClick != null)
            {
                // The label is unique WITHIN a placement's body — no kind draws
                // two rows with the same one — and DrawSections has pushed the
                // placement's id around the whole section, so the pair is
                // unique across the panel.
                ImGui.PushID(label);
                ImGui.InvisibleButton("##value", new NVector2(width, ValueH));
                hovered = ImGui.IsItemHovered();
                if (ImGui.IsItemClicked()) onClick();
                ImGui.PopID();
            }
            else
            {
                ImGui.Dummy(new NVector2(width, ValueH));
            }

            var boxMax = new NVector2(boxPos.X + width, boxPos.Y + ValueH);
            var dl = ImGui.GetWindowDrawList();

            if (onClick != null)
            {
                dl.AddRectFilled(boxPos, boxMax, hovered ? ValueBgHover : ValueBg);
                dl.AddRect(boxPos, boxMax, ValueBorder);
            }
            else
            {
                // Read-only fields get a flatter, borderless background so it
                // is visually obvious you can't click them.
                dl.AddRectFilled(boxPos, boxMax, ValueBgReadOnly);
            }

            dl.AddText(new NVector2(boxPos.X + 6f, boxPos.Y + 4f), ValueText,
                       ChromeTheme.Truncate(value, width - 12f));

            Gap(RowGap);
        }

        private static void Gap(float h) => ImGui.Dummy(new NVector2(1f, h));
    }
}
