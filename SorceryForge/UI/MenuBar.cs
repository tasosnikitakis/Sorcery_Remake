// ============================================================================
// MENU BAR
// SorceryForge — the top band: menus, mode toolbar, room title
// ============================================================================
// REPLACES a row of twelve hand-positioned buttons whose x-coordinates were
// arithmetic in RelayoutButtons: two banks growing inward from the window
// edges, and the room title drawn in whatever gap was left between them. That
// arrangement had a hard failure at width: the bar gained the Import button,
// the left bank moved right by ~90 px, and at the DEFAULT 1280 px window the
// gap was 36 px — enough for the bare "*" and nothing else. The room's name
// and id had already stopped being visible on a normal-sized screen, and the
// only always-on sign of unsaved work was one character wide.
//
// THE FIX, and why it is structural rather than a bigger budget: nothing here
// is positioned by arithmetic against anything else. The menus lay themselves
// out left to right; the toolbar's toggles lay themselves out left to right;
// the room title is placed by measuring itself against the window's right
// edge. No item's position is a function of another item's width, so no item
// can be squeezed out by another growing. The `*` is additionally reported by
// the status bar's marker group (room*), so the WARNING survives even a window
// narrow enough to clip the title itself.
//
// THE BAND IS STILL 56 px. EditorLayout.TopBarHeight is unchanged on purpose:
// every canvas coordinate, the door labels drawn in the canvas margin and the
// reachability validator all measure against the rectangles it derives, and a
// chrome migration has no business moving the canvas. Row 1 is the menu bar,
// row 2 is the toolbar, and the band is the same height it always was.
//
// MAP MODE. Every top-bar button used to be inert while the board was up —
// not by decision but because Update returns before HandleButtons ever runs.
// DrawButton greyed them to be honest about it. That is preserved item for
// item, with three deliberate exceptions, each of which has a working
// map-mode keyboard path already:
//
//   View > World Map          Tab toggles the map from BOTH modes.
//   File > New Room           N opens it from the map (HandleMapInput).
//   File > Import Screenshot  I opens it from the map (HandleMapInput).
//
// and one item that only exists in map mode:
//
//   File > Save Map Arrangement   Ctrl+S in map mode, which had no button.
//
// Every other item is disabled while the board is up.
// ============================================================================

using ImGuiNET;
using SorceryRemake.Core;
using NVector2 = System.Numerics.Vector2;

namespace SorceryForge.UI
{
    public static class MenuBar
    {
        // ====================================================================
        // ENABLEMENT — the map-mode rule, in one readable table
        // ====================================================================
        // Named predicates rather than `!view.MapMode` scattered through five
        // methods. Two reasons: the rule is the single most-likely thing to
        // drift item by item as the menus grow, and tools/ChromeCheck asserts
        // this table directly, in both modes, which a scattered condition
        // could not be.
        //
        // The default is "room actions are off while the board is up", which
        // matches the twelve top-bar buttons this replaces — every one of them
        // was inert in map mode. The exceptions are the items whose keyboard
        // path ALREADY works from the board.
        // ====================================================================

        /// <summary>Anything that acts on the room being edited.</summary>
        internal static bool CanActOnRoom(in ChromeView v) => !v.MapMode;

        /// <summary>Ctrl+S in room view writes the room.</summary>
        internal static bool CanSaveRoom(in ChromeView v) => !v.MapMode;

        /// <summary>Ctrl+S on the board writes worldmap.json. No button ever had this.</summary>
        internal static bool CanSaveMap(in ChromeView v) => v.MapMode;

        /// <summary>N and I already open these from the board.</summary>
        internal static bool CanOpenPickers(in ChromeView v) => true;

        /// <summary>Tab already toggles the board from both modes.</summary>
        internal static bool CanToggleMap(in ChromeView v) => true;

        /// <summary>Escape exits from room view only; on the board it returns to the room.</summary>
        internal static bool CanExit(in ChromeView v) => !v.MapMode;

        /// <summary>F11 is read in HandleKeyboardShortcuts, which the board never reaches.</summary>
        internal static bool CanToggleFullscreen(in ChromeView v) => !v.MapMode;

        /// <summary>
        /// NoInputs while any modal owns the editor. Not BeginDisabled: that
        /// would grey the widgets on top of the overlay's own dim, and the old
        /// chrome greyed nothing - it simply stopped being reached.
        /// </summary>
        internal static ImGuiWindowFlags Inert(in ChromeView view) =>
            view.ModalOpen ? ImGuiWindowFlags.NoInputs : ImGuiWindowFlags.None;

        public static void Draw(IChromeActions actions, EditorState state, in ChromeView view)
        {
            // NoInputs while a modal owns the editor: the menus are drawn,
            // dimmed by the overlay, and answer nothing. The old top bar was
            // inert then for a structural reason - Update returned before
            // HandleButtons ran - and an ImGui window does not stop hit-testing
            // just because something is drawn over the middle of the screen.
            if (ChromeTheme.BeginPanel("##sf_topbar", EditorLayout.TopBarRect,
                    ImGuiWindowFlags.MenuBar | Inert(view)))
            {
                if (ImGui.BeginMenuBar())
                {
                    FileMenu(actions, view);
                    EditMenu(actions, state, view);
                    ViewMenu(actions, view);
                    ValidateMenu(actions, view);
                    ImGui.EndMenuBar();
                }

                Toolbar(actions, state, view);
            }
            ChromeTheme.EndPanel();
        }

        // ====================================================================
        // MENUS
        // ====================================================================

        /// <summary>
        /// Escape closes an open menu. Call as the first thing inside every
        /// BeginMenu body.
        /// </summary>
        // ImGui does this itself only with keyboard navigation on, and
        // navigation is deliberately off here — it would claim the arrow keys
        // (the canvas pan), Enter (the crop confirm) and Escape (the discard
        // guard), all documented keybinds of this editor. So the one piece of
        // it that a mouse-driven menu still needs is done by hand.
        //
        // The router swallows this same Escape, so it closes the menu and does
        // NOT also reach the editor's exit path. Both halves are required:
        // without the router's gate, Escape would close the menu AND quit; and
        // without this, Escape over an open menu would do nothing at all.
        private static void CloseOnEscape()
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Escape)) ImGui.CloseCurrentPopup();
        }

        private static void FileMenu(IChromeActions actions, in ChromeView view)
        {
            if (!ImGui.BeginMenu("File")) return;
            CloseOnEscape();

            // Two save items, not one with a changing label. Ctrl+S has always
            // meant "persist what is in front of you", and in map mode that is
            // the arrangement, not the room. Two items, each enabled in its own
            // mode, says that plainly; one item would have to lie in one of
            // them about what it is going to write.
            if (ImGui.MenuItem("Save Room", "Ctrl+S", false, CanSaveRoom(view)))
                actions.SaveCurrentRoom();
            if (ImGui.MenuItem("Save Map Arrangement", "Ctrl+S", false, CanSaveMap(view)))
                actions.SaveWorldMap();

            ImGui.Separator();

            // Enabled in BOTH modes. The old buttons were inert on the board,
            // but only as a side effect of HandleButtons not running there —
            // the handlers themselves are mode-agnostic, the map already
            // reaches them with N and I, and CreateAndOpenRoom explicitly
            // leaves map mode, i.e. it was written to be called from there.
            if (ImGui.MenuItem("New Room...", "N", false, CanOpenPickers(view)))
                actions.OpenNewRoomPicker();
            if (ImGui.MenuItem("Import Screenshot...", "I", false, CanOpenPickers(view)))
                actions.OpenImportPicker();

            ImGui.Separator();

            // Escape exits from room view only; on the board it returns to the
            // room, so there is no exit path from map mode and this matches.
            if (ImGui.MenuItem("Exit", "Esc", false, CanExit(view)))
                actions.ExitEditor();

            ImGui.EndMenu();
        }

        private static void EditMenu(IChromeActions actions, EditorState state, in ChromeView view)
        {
            if (!ImGui.BeginMenu("Edit")) return;
            CloseOnEscape();

            // Checkmarked rather than labelled with their state ("Snap: OFF").
            // The toolbar below still carries both as always-visible toggles,
            // which is where the at-a-glance reading of them lives.
            if (ImGui.MenuItem("Snap to tile (8 px)", "", state.SnapEnabled, CanActOnRoom(view)))
                actions.ToggleSnap();
            if (ImGui.MenuItem("Auto-punch background", "", state.AutoPunch, CanActOnRoom(view)))
                actions.ToggleAutoPunch();

            ImGui.EndMenu();
        }

        private static void ViewMenu(IChromeActions actions, in ChromeView view)
        {
            if (!ImGui.BeginMenu("View")) return;
            CloseOnEscape();

            // Enabled in both modes because Tab is. This is the first CLICK
            // path the map has ever had; the key is unchanged.
            if (ImGui.MenuItem("World Map", "Tab", view.MapMode, CanToggleMap(view)))
                actions.ToggleMapMode();

            // F11 is read in HandleKeyboardShortcuts, which map mode does not
            // reach — so the item matches the key and is off on the board.
            if (ImGui.MenuItem("Fullscreen", "F11", view.IsFullscreen, CanToggleFullscreen(view)))
                actions.ToggleFullscreen();

            ImGui.Separator();

            // Zoom, as information. Disabled because there is nothing to click:
            // the canvas zooms with the wheel and the board with the wheel, and
            // neither has ever had a button.
            ImGui.MenuItem(view.MapMode ? $"Board zoom  {view.MapZoomPercent}%"
                                        : $"Canvas zoom  {view.Zoom}x",
                           "wheel", false, false);

            ImGui.EndMenu();
        }

        private static void ValidateMenu(IChromeActions actions, in ChromeView view)
        {
            if (!ImGui.BeginMenu("Validate")) return;
            CloseOnEscape();

            // All three act on the room being edited (the door and puzzle
            // passes read the whole world, but they overlay THIS room's canvas
            // with their verdicts), so all three are off on the board — as
            // their buttons were. Entering map mode runs the door validation
            // anyway, to colour the arrows.
            if (ImGui.MenuItem("Reachability", "", false, CanActOnRoom(view)))
                actions.ValidateReachability();
            if (ImGui.MenuItem("Doors", "", false, CanActOnRoom(view)))
                actions.ValidateDoors();
            if (ImGui.MenuItem("Puzzle", "", false, CanActOnRoom(view)))
                actions.AnalyzePuzzle();

            ImGui.EndMenu();
        }

        // ====================================================================
        // TOOLBAR (row 2 of the band)
        // ====================================================================

        private static void Toolbar(IChromeActions actions, EditorState state, in ChromeView view)
        {
            ImGui.BeginDisabled(!CanActOnRoom(view));

            // Direct selection rather than the old single button that cycled
            // Place -> Paint -> Erase. Same three states, one click each
            // instead of up to two, and the current one is readable without
            // reading a label.
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted("Mode");
            ImGui.SameLine();
            ModeButton(actions, state, EditorMode.Place, "Place");
            ImGui.SameLine(0f, 2f);
            ModeButton(actions, state, EditorMode.Paint, "Paint");
            ImGui.SameLine(0f, 2f);
            ModeButton(actions, state, EditorMode.Erase, "Erase");

            ImGui.SameLine(0f, 16f);

            // The two toggles keep a permanent, always-readable state here —
            // that is what the old "Snap: OFF" / "Punch: OFF" labels were for,
            // and burying them in a menu would have lost it.
            if (ChromeTheme.PressCheckbox("Snap", state.SnapEnabled)) actions.ToggleSnap();
            ImGui.SameLine(0f, 12f);
            if (ChromeTheme.PressCheckbox("Auto-punch", state.AutoPunch)) actions.ToggleAutoPunch();

            ImGui.EndDisabled();

            RoomTitle(actions, state, view);
        }

        private static void ModeButton(IChromeActions actions, EditorState state, EditorMode mode, string label)
        {
            bool active = state.Mode == mode;
            if (active) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));
            if (ChromeTheme.PressButton(label)) actions.SetMode(mode);
            if (active) ImGui.PopStyleColor();
        }

        /// <summary>
        /// Room navigation and the room title, right-aligned. The permanent
        /// home of the title and its unsaved marker.
        /// </summary>
        private static void RoomTitle(IChromeActions actions, EditorState state, in ChromeView view)
        {
            string title = TitleText(state, view);

            // The two nav buttons sit immediately left of the title they
            // change. Measured together so the group lands flush right at any
            // width, and so the title never has to negotiate for a gap.
            const float navWidth = 64f;      // two small buttons plus spacing
            float need = ImGui.CalcTextSize(title).X + navWidth + (view.RoomDirty ? 16f : 0f);

            ImGui.SameLine();
            float x = ImGui.GetWindowWidth() - need - 8f;
            if (x > ImGui.GetCursorPosX()) ImGui.SetCursorPosX(x);

            ImGui.BeginDisabled(!CanActOnRoom(view));
            if (ChromeTheme.PressSmallButton("<")) actions.CyclePrevRoom();
            Tooltip("Previous room (PageUp)");
            ImGui.SameLine(0f, 2f);
            if (ChromeTheme.PressSmallButton(">")) actions.CycleNextRoom();
            Tooltip("Next room (PageDown)");
            ImGui.EndDisabled();

            ImGui.SameLine(0f, 8f);
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(view.MapMode ? ChromeTheme.Amber : ChromeTheme.White, title);

            // The marker is drawn separately, in its own colour, so that
            // "unsaved" is a thing the eye finds rather than a punctuation mark
            // at the end of a sentence. The status bar reports it a second time
            // as `room*`, which is what makes it survive a window narrow enough
            // to clip this title.
            if (!view.MapMode && view.RoomDirty)
            {
                ImGui.SameLine(0f, 4f);
                ImGui.TextColored(ChromeTheme.Dirty, "*");
            }
        }

        /// <summary>
        /// The right-hand title, in whichever mode is up. Pure — no ImGui call
        /// — so tools/ChromeCheck can assert the exact strings.
        /// </summary>
        // Map mode keeps the exact sentence the old centre title carried,
        // including its em dash and its triple-spaced pipe: it is how the board
        // tells you the way out, and the status bar repeats only the short form
        // of that. Its "*" is MapDirty ALONE — the board's unsaved arrangement
        // — and never the room's three flags, which the room-mode form marks
        // separately (drawn as its own coloured glyph, not baked in here).
        internal static string TitleText(EditorState state, in ChromeView view) =>
            view.MapMode
                ? $"WORLD MAP{(state.MapDirty ? " *" : "")} — {view.MapRoomCount} rooms" +
                  $"   |   Tab or Esc: back to {view.RoomId}"
                : $"Room: {view.RoomDisplayName}  ({view.RoomId})";

        private static void Tooltip(string text)
        {
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(text);
        }
    }
}
