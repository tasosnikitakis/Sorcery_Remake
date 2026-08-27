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

using SorceryRemake.Core;

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

        // ---- Undo / redo ----------------------------------------------------

        /// <summary>Ctrl+Z. Takes back the most recent edit, of any kind.</summary>
        // The chrome does not get to know what is on the stack, or what kind of
        // command it is. It asks for "the last thing", and the editor's one
        // undo path decides — which is the point of EDITOR_REVIEW item 11.
        void Undo();

        /// <summary>Ctrl+Y / Ctrl+Shift+Z.</summary>
        void Redo();

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

        // ---- Inspector -------------------------------------------------------
        //
        // Every one of these was a lambda inside DrawInspector, carrying its
        // full side-effect set inline in a render method — which made "what
        // happens when I retarget a door" a question you answered by reading
        // the renderer. They are named methods on the logic side now.

        /// <summary>
        /// A section header click: select the placement AND toggle its
        /// collapse. Both, always — the two cannot be separated, because the
        /// canvas outline follows the selection.
        /// </summary>
        void SelectAndToggleSection(Placement p);

        /// <summary>
        /// Flip a door between LeftOpening and RightOpening.
        /// </summary>
        // Still a CYCLE while the other three became filterable pickers, and
        // that is a decision rather than an oversight: two values need no list.
        // A dropdown here would be two clicks and a popup to do what one click
        // already does, and the value box would have to be re-read to find out
        // what happened either way.
        void CycleDoorOpeningSide(Placement p);

        // ---- The three pickers (EDITOR_REVIEW item 10) ----------------------
        //
        // SET, not CYCLE. The chrome now names the value it wants rather than
        // asking for "the next one", which is what a list of seventy-five rooms
        // requires — and it also means the logic side no longer owns an
        // ordering that only existed to make cycling bearable.
        //
        // Each is one applied change and therefore one undo entry. Selecting
        // the value already set is a no-op that records nothing.

        /// <summary>Point a door at a room — and blank its target door with it.</summary>
        // The blanking stays here, on the logic side, exactly as it was in the
        // cycle: a door id is only meaningful inside one room, so carrying the
        // old one across would leave a link that validates as orphan-door and
        // reads like a typo. A panel that had to remember to blank it is a
        // panel that will one day forget.
        void SetDoorTargetRoom(Placement p, string roomId);

        /// <summary>Point a door at a door in its target room. "" means none.</summary>
        void SetDoorTargetDoor(Placement p, string doorId);

        void SetBlockedDoorRequiredItem(Placement p, ItemType item);

        /// <summary>Clear the background under a placement's 24x24 footprint.</summary>
        void PunchBackground(Placement p);

        // ---- Room properties -------------------------------------------------

        /// <summary>
        /// Rewrite the current room's displayName in rooms.json.
        /// </summary>
        // Called on every DEACTIVATION of the inspector's name field, including
        // the one Escape produces after reverting the text — so the common case
        // is "the name did not change", and the logic side is what decides that
        // and stays quiet about it. A panel that tried to work out whether a
        // rename was needed would be a panel holding an opinion about the
        // registry.
        //
        // There is deliberately no SetRoomId. An id is a persistence key, three
        // file names and a cross-room link; renaming one is a migration, not a
        // text field. See the header of RoomProperties.cs and doc/07.
        void SetRoomDisplayName(string displayName);

        // ---- Modal pickers -------------------------------------------------

        void OpenNewRoomPicker();
        void OpenImportPicker();

        /// <summary>Write a room's files, register it, and open it.</summary>
        void CreateRoom(RoomCandidate candidate);

        void CancelNewRoomPicker();

        /// <summary>Decode a source and either import it or open the crop step.</summary>
        void RunImport(ImportCandidate candidate);

        void CancelImportPicker();
        void ToggleImportQuantize();

        /// <summary>Cut the selection to 320x144 and finish the import.</summary>
        void ConfirmCrop();

        void CancelCrop();
    }
}
