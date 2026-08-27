// ============================================================================
// FILTER POPUP
// SorceryForge — click a value, type, pick from what is left
// ============================================================================
// EDITOR_REVIEW item 10, and the reason it is item 10: cycling a door's target
// room one click at a time is fine for nine rooms and unusable for seventy-five.
// Worse than unusable — it is unusable in a way that gets slowly worse without
// ever breaking, so nobody files it. The room you want is on average half the
// registry away, you cannot see where you are in the list, and overshooting
// means going round again.
//
// THE INTERACTION, in full:
//
//   click the value box   the popup opens, and the filter box already has the
//                         keyboard — you can type immediately, which is the
//                         whole point of a filter
//   type                  the list narrows, case-insensitively, on substring
//   Enter                 picks the top remaining hit
//   click a row           picks that row, on the PRESS edge like every other
//                         row in this chrome
//   Esc                   clears the field's focus FIRST; a second Esc closes
//                         the popup. Neither ever reaches the editor's exit
//   click outside         closes the popup
//
// THE TWO-STEP ESCAPE IS DELIBERATE AND IS NOT FREE. ImGui's InputText handles
// Escape itself (revert and deactivate), and ImGui.IsKeyPressed does not do
// owner-aware filtering — so a naive `if (Escape) CloseCurrentPopup()` in the
// body would fire on the SAME press that deactivated the field, collapsing the
// two steps into one. Hence the flag read at the TOP of the body, before the
// InputText call that clears it: `fieldActive` is whether a field owned the
// keyboard when the frame began, and only when it did not does Escape close
// the popup.
//
// KEYBOARD SAFETY. While this popup is open, ChromeInputRouter reports
// KeyboardReachesEditor false — twice over, and both matter. `WantTextInput` is
// true while the filter box is taking keystrokes, which is what stops `P`,
// `Delete`, `[`, `]`, `N`, `I` and `A` from firing as editor keybinds while
// somebody types a room name. `ImGuiPopupOpen` is true for as long as the popup
// is up at all, which covers the frames after Escape has defocused the field
// but before the popup itself closes. PR 7a built that rule for text fields
// that did not exist yet; this is the file that makes it live, and
// tools/ChromeCheck section 15 asserts it across frames — because the flags are
// latched during NewFrame and a same-frame read of them reports a clean result
// whatever the rule is. That trap has already shipped once here.
//
// WHY THE FILTER STRING LIVES IN A STATIC, in a layer that is otherwise
// stateless. It is presentation state with no logic opinion attached — the same
// category as the panel scroll offsets, which EditorState explicitly handed to
// ImGui for the same reason. Nothing outside this file can read it, nothing is
// persisted, and it is reset by Open(). At most one ImGui popup is open at a
// time, so one buffer is provably enough; a dictionary keyed by popup id would
// be a cache with no second entry ever in it.
//
// DEVICE-FREE, like every other file under UI/ except ImGuiRenderer.cs.
// ============================================================================

using ImGuiNET;
using System.Collections.Generic;
using NVector2 = System.Numerics.Vector2;

namespace SorceryForge.UI
{
    public static class FilterPopup
    {
        /// <summary>Wide enough for a door id like chateau1_door_topright.</summary>
        private const float Width = 260f;
        private const float RowHeight = 20f;
        private const float MaxListHeight = 200f;

        private static readonly uint RowBg = ChromeTheme.Packed(40, 46, 60);
        private static readonly uint RowBgHover = ChromeTheme.Packed(60, 75, 110);
        private static readonly uint RowBgCurrent = ChromeTheme.Packed(70, 90, 130);
        private static readonly uint RowText = ChromeTheme.Packed(255, 255, 255);

        // The filter text of whichever popup is open. See the header for why a
        // single static is provably enough.
        private static string _filter = "";

        // Focus is requested on the first frame the popup draws, not on the
        // frame it is opened: SetKeyboardFocusHere targets the NEXT widget
        // submitted, and on the opening frame no widget of this popup has been
        // submitted yet.
        private static bool _focusPending;

        // Reused every frame so a popup that is open for a minute does not
        // allocate a list per frame per row.
        private static readonly List<string> _hits = new();

        /// <summary>
        /// Call alongside ImGui.OpenPopup: resets the filter and asks for the
        /// keyboard.
        /// </summary>
        public static void Open()
        {
            _filter = "";
            _focusPending = true;
        }

        /// <summary>
        /// Draw the popup body if it is open. Returns true, and sets
        /// <paramref name="chosen"/>, on the frame the user picks something.
        /// </summary>
        // strId must be the SAME string passed to ImGui.OpenPopup, from the
        // same point in the id stack — ImGui resolves both against it, and the
        // inspector pushes a placement id and a field label around each row, so
        // a bare "##pick" is already unique across the panel.
        public static bool Body(string strId, IReadOnlyList<string>? options,
                                string current, out string chosen)
        {
            chosen = "";

            // BeginPopup is NOT Begin: EndPopup must be called only when it
            // returned true. Getting this backwards asserts inside ImGui rather
            // than misdrawing, which is at least loud.
            if (!ImGui.BeginPopup(strId)) return false;

            // Read BOTH at the top, before the InputText that changes them.
            // See the header: Escape must not close the popup on the same press
            // that defocuses the filter box.
            bool escapePressed = ImGui.IsKeyPressed(ImGuiKey.Escape);
            bool fieldActive = ImGui.IsAnyItemActive();

            if (_focusPending)
            {
                ImGui.SetKeyboardFocusHere();
                _focusPending = false;
            }

            ImGui.SetNextItemWidth(Width);
            bool enter = ImGui.InputText("##sf_filter", ref _filter, 64,
                                         ImGuiInputTextFlags.EnterReturnsTrue);

            Narrow(options, _filter, _hits);

            bool picked = false;

            // Enter takes the TOP hit — the one the eye is already on, and the
            // reason typing three characters is usually the whole interaction.
            if (enter && _hits.Count > 0)
            {
                chosen = _hits[0];
                picked = true;
            }

            float listHeight = System.Math.Min(MaxListHeight,
                                               System.Math.Max(RowHeight, _hits.Count * RowHeight));

            // EndChild pairs with the CALL, not the return value — the same
            // rule the palette and inspector note.
            if (ImGui.BeginChild("##sf_filter_list", new NVector2(Width, listHeight)))
            {
                if (_hits.Count == 0)
                {
                    ImGui.TextColored(ChromeTheme.Dim, "(no match)");
                }
                else
                {
                    ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new NVector2(0f, 0f));
                    for (int i = 0; i < _hits.Count; i++)
                    {
                        ImGui.PushID(i);
                        // The value already set wears the selected colour, so
                        // "what is this door pointing at now" is answerable
                        // from inside the picker rather than by closing it.
                        if (Row(_hits[i], _hits[i] == current))
                        {
                            chosen = _hits[i];
                            picked = true;
                        }
                        ImGui.PopID();
                    }
                    ImGui.PopStyleVar();
                }
            }
            ImGui.EndChild();

            if (picked) ImGui.CloseCurrentPopup();
            else if (escapePressed && !fieldActive) ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
            return picked;
        }

        /// <summary>
        /// The options whose text contains <paramref name="filter"/>, ignoring
        /// case. An empty filter matches everything.
        /// </summary>
        // Substring rather than prefix: door ids carry their room as a prefix
        // (chateau1_door_topright), so a prefix match would make "topright"
        // find nothing — which is exactly the half of the id an author
        // remembers.
        //
        // Internal rather than private so tools/ChromeCheck can assert the rule
        // itself as well as driving the widget: what narrows is worth pinning
        // separately from whether the click lands.
        internal static void Narrow(IReadOnlyList<string>? options, string filter, List<string> into)
        {
            into.Clear();
            if (options == null) return;

            bool all = string.IsNullOrEmpty(filter);
            for (int i = 0; i < options.Count; i++)
            {
                string option = options[i];
                if (all || option.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    into.Add(option);
            }
        }

        /// <summary>One option. Press-edge, like every other row in this chrome.</summary>
        // An InvisibleButton and four draw-list calls rather than a Selectable,
        // for the reasons PalettePanel gives: a Selectable fires on RELEASE and
        // brings ImGui's own hovered/selected precedence, and this chrome fires
        // on the press edge everywhere except MenuItem.
        private static bool Row(string label, bool isCurrent)
        {
            var p0 = ImGui.GetCursorScreenPos();
            ImGui.InvisibleButton("##row", new NVector2(Width, RowHeight));
            bool hovered = ImGui.IsItemHovered();
            bool clicked = ImGui.IsItemClicked();

            // HOVER outranks CURRENT, the opposite way round from the palette's
            // ACTIVE-over-HOVER. The palette's active row is a drag you are in
            // the middle of and must not lose sight of; here "current" is
            // information and the hover is the thing your hand is doing.
            var dl = ImGui.GetWindowDrawList();
            dl.AddRectFilled(p0, new NVector2(p0.X + Width, p0.Y + RowHeight),
                             hovered ? RowBgHover : isCurrent ? RowBgCurrent : RowBg);
            dl.AddText(new NVector2(p0.X + 6f, p0.Y + 2f), RowText,
                       ChromeTheme.Truncate(label, Width - 12f));

            return clicked;
        }
    }
}
