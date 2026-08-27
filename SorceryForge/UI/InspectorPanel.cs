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
        // The amber every section header and modal title in this editor wears —
        // the ROOM strip is a heading, not an entity.
        private static readonly uint RoomHeaderText = ChromeTheme.Packed(255, 220, 110);
        private static readonly uint RoomNoteColor = ChromeTheme.Packed(150, 160, 185);

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

            RoomBlock(actions, view, contentW);

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
        // ROOM BLOCK — the room's own properties, above the entities
        // ====================================================================
        // PR 7b commit 3. The editor had no text field anywhere, so a room's
        // display name was fixed at creation from its background PNG's filename
        // and could only be changed by hand-editing rooms.json. This is the
        // field.
        //
        // WHERE IT IS, and why it is not a modal. Three options were on the
        // table: File > Room Properties…, an inspector block shown only when
        // nothing is selected, and this — a block that is ALWAYS at the top of
        // the inspector's list.
        //
        //   A modal would be a FOURTH thing in the ModalOpen set, with its own
        //   NoInputs handling for the three bands, its own Escape path and its
        //   own overlay. That is a lot of new modality for one text field, and
        //   the editor's modality rules are the part of PR 7a that took the
        //   longest to get right.
        //
        //   Shown-only-when-nothing-is-selected would appear and disappear on
        //   every canvas click, and everything below it would jump 96 px each
        //   time. The inspector is a list you scroll and click in; a list whose
        //   contents move when you select something is a list that hands you
        //   the wrong row.
        //
        //   Always-present costs 96 px, and costs it only until you scroll —
        //   the block lives INSIDE the scrolling child, so it is not pinned
        //   furniture. The inspector already reports the room's entity count at
        //   the top; the room's own identity belongs in the same place.
        //
        // AND IT IS THE FIRST TEXT FIELD WITH NO POPUP OVER IT. The pickers'
        // filter boxes are inside ImGui popups, so ChromeInputRouter's popup
        // term alone would gate the keyboard for them. This one is a plain band
        // widget: while it has focus, io.WantTextInput is the ONLY thing
        // stopping P, Delete, the brackets and the rest from firing as editor
        // keybinds under the author's typing. That is exactly the case PR 7a
        // wrote the rule for and had nothing to test it with.
        //
        // NO ID FIELD, deliberately. An id is a persistence key, three file
        // names and a cross-room link — renaming one is a migration, not a text
        // field. The note under the name says so, and doc/07 says why.
        // ====================================================================

        private const float RoomHeaderH = 22f;
        private const float RoomNoteH = 16f;

        /// <summary>
        /// The exact height of the ROOM block, so that everything below it can
        /// be found without the arithmetic being duplicated anywhere.
        /// </summary>
        // internal, and read by tools/ChromeCheck rather than re-derived there.
        // The whole reason the hand-rolled chrome kept handing back the wrong
        // row was a rectangle maintained in more than one place.
        internal const float RoomBlockHeight =
            RoomHeaderH + RowGap                       // the "ROOM" strip
            + LabelH + InnerGap + ValueH + RowGap      // the Name row
            + RoomNoteH + RowGap                       // the id note
            + BodyGap;

        // The draft the field edits, and which room it belongs to. Presentation
        // state, in the same category as the panel scroll offsets ImGui owns:
        // nothing outside this file reads it, and the moment it is APPLIED it
        // stops being the chrome's business and becomes a verb call.
        private static string _nameDraft = "";
        private static string _nameDraftRoomId = "";
        private static bool _nameFieldActive;

        private static void RoomBlock(IChromeActions actions, in ChromeView view, float width)
        {
            // Re-seed from the room whenever the draft and the registry
            // disagree. Three cases, one condition: a room switch (the field
            // must not still be showing the previous room's name, because
            // applying it would rename the room you just arrived in), a rename
            // that succeeded, and a rename the logic side REFUSED — where the
            // draft has to fall back to what the file still says rather than
            // sitting there showing a name that was never written.
            //
            // The !_nameFieldActive term is belt-and-braces rather than
            // load-bearing, and is written down as such because a test cannot
            // tell: ImGui's InputText keeps its own edit buffer while its item
            // is active and only writes back to the caller's string on the way
            // out, so assigning to _nameDraft mid-edit is ignored in this
            // version anyway. Deleting the guard breaks nothing today and would
            // break everything under a version that read the buffer each frame.
            if (!_nameFieldActive &&
                (_nameDraftRoomId != view.RoomId || _nameDraft != (view.RoomDisplayName ?? "")))
            {
                _nameDraft = view.RoomDisplayName ?? "";
                _nameDraftRoomId = view.RoomId ?? "";
            }

            ImGui.SetCursorPosX(Padding);
            var strip = ImGui.GetCursorScreenPos();
            ImGui.Dummy(new NVector2(width, RoomHeaderH));
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(strip, new NVector2(strip.X + width, strip.Y + RoomHeaderH), HeaderBg);
            dl.AddRect(strip, new NVector2(strip.X + width, strip.Y + RoomHeaderH), HeaderBorder);
            dl.AddText(new NVector2(strip.X + 8f, strip.Y + 4f), RoomHeaderText, "ROOM");
            Gap(RowGap);

            float x = Padding + BodyIndent;
            float fieldW = width - BodyIndent * 2;

            ImGui.SetCursorPosX(x);
            var labelPos = ImGui.GetCursorScreenPos();
            ImGui.Dummy(new NVector2(fieldW, LabelH));
            ImGui.GetWindowDrawList().AddText(labelPos, RowLabel, "Name");

            ImGui.SetCursorPosX(x);
            ImGui.Dummy(new NVector2(fieldW, InnerGap));

            // Frame padding chosen so the field is EXACTLY ValueH tall, like
            // every other value box in this panel. Without it the field would
            // be whatever the font and the style's padding happened to add up
            // to, and RoomBlockHeight above would be a lie.
            float padY = Math.Max(0f, (ValueH - ImGui.GetTextLineHeight()) * 0.5f);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new NVector2(6f, padY));
            ImGui.SetCursorPosX(x);
            ImGui.SetNextItemWidth(fieldW);
            ImGui.InputText("##sf_roomname", ref _nameDraft, RoomNameBufferSize);

            // Read IMMEDIATELY after the call, both of them. IsItemActive is
            // what suppresses the re-seed above on the next frame, and
            // IsItemDeactivatedAfterEdit fires on Enter, on a click away, and
            // on Escape-after-typing — the last of which arrives with the text
            // already reverted, which is why the logic side treats an unchanged
            // name as a silent no-op rather than as a write.
            _nameFieldActive = ImGui.IsItemActive();
            bool applied = ImGui.IsItemDeactivatedAfterEdit();
            ImGui.PopStyleVar();

            if (applied) actions.SetRoomDisplayName(_nameDraft);

            Gap(RowGap);

            ImGui.SetCursorPosX(x);
            var notePos = ImGui.GetCursorScreenPos();
            ImGui.Dummy(new NVector2(fieldW, RoomNoteH));
            ImGui.GetWindowDrawList().AddText(notePos, RoomNoteColor,
                ChromeTheme.Truncate($"id {view.RoomId} — fixed", fieldW));

            Gap(RowGap);
            Gap(BodyGap);
        }

        /// <summary>Room names are short by rule; the buffer says so.</summary>
        // Matches RoomProperties.MaxDisplayNameLength, plus one for the
        // terminator ImGui's buffer wants. The logic side still checks — a
        // buffer limit is a convenience, not a validation.
        private const uint RoomNameBufferSize = 49;

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
