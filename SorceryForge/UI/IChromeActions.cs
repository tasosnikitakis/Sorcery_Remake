// ============================================================================
// CHROME ACTIONS
// SorceryForge — everything the chrome is allowed to do
// ============================================================================
// THIS INTERFACE IS THE ARCHITECTURE, not a convenience.
//
// The rule the ImGui migration was built to hold is "logic stays out of the UI
// layer": a menu item, a palette row and an inspector button may CALL the
// editor's logic and may do nothing else. A rule like that decays the moment it
// depends on discipline — the hand-rolled inspector proved it, where four
// cycle-buttons carried their entire side-effect set inline in a lambda inside
// a Draw method, so "what happens when I retarget a door" was a question you
// answered by reading the renderer.
//
// So the panels under UI/ cannot see EditorGame at all. They see this, and this
// is a list of verbs. There is no state to mutate through it, no field to
// assign, no flag to set. If a panel needs something new to happen, a method
// has to be added here and implemented on the logic side, in the open, next to
// the rest of the editor's behaviour — which is a change a reviewer notices.
//
// Read-only state travels the other way, as EditorState plus the ChromeView
// snapshot. Neither is writable by a panel.
//
// DEVICE-FREE, like every other file under UI/ except ImGuiRenderer.cs. That is
// what lets tools/ChromeCheck implement this interface with a recording stub
// and assert that a given click invokes exactly the verb it should.
// ============================================================================

namespace SorceryForge.UI
{
    public interface IChromeActions
    {
        // ---- Room navigation and files ------------------------------------

        /// <summary>Previous room in registry order. Runs the discard guard.</summary>
        void CyclePrevRoom();

        /// <summary>Next room in registry order. Runs the discard guard.</summary>
        void CycleNextRoom();

        /// <summary>Write content + layout (+ collision, + background PNG when dirty).</summary>
        void SaveCurrentRoom();

        /// <summary>Write the world-map arrangement to worldmap.json.</summary>
        void SaveWorldMap();

        /// <summary>Escape's exit: the discard guard first, including the map.</summary>
        void ExitEditor();

        // ---- Mode and toggles ---------------------------------------------

        void SetMode(EditorMode mode);
        void ToggleSnap();
        void ToggleAutoPunch();
        void ToggleFullscreen();
        void ToggleMapMode();

        // ---- Validators ----------------------------------------------------

        void ValidateReachability();
        void ValidateDoors();
        void AnalyzePuzzle();

        // ---- Palette --------------------------------------------------------

        /// <summary>
        /// Pick an entry up. The cursor then carries it until a click on the
        /// canvas drops it or a right-click anywhere cancels.
        /// </summary>
        // Not "set Dragging": picking up also clears the placement selection
        // and says so in the status line, and those three writes belong
        // together on the logic side rather than in a click handler.
        void BeginPaletteDrag(PaletteEntry entry);

        // ---- Modal pickers -------------------------------------------------

        void OpenNewRoomPicker();
        void OpenImportPicker();
    }
}
