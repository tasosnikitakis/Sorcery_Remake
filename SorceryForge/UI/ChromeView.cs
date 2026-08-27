// ============================================================================
// CHROME VIEW
// SorceryForge — the read-only snapshot the chrome renders from
// ============================================================================
// EditorState carries the room's working set and is handed to the panels as
// it is. This carries the rest: the handful of things the chrome needs to
// SHOW that live on EditorGame rather than on the model — which mode is up,
// what the canvas zoom is, how many rooms are on the board.
//
// A snapshot, deliberately, rather than a reference to EditorGame. A panel
// that could reach EditorGame could call anything on it, and the whole point
// of IChromeActions is that a panel's reach is a reviewable list of verbs.
// Built fresh each frame in EditorGame.BuildChrome; nothing here is writable
// by a panel, because a struct passed by value cannot be written back.
// ============================================================================

using Microsoft.Xna.Framework;
using SorceryRemake.Core;
using System;
using System.Collections.Generic;

namespace SorceryForge.UI
{
    public struct ChromeView
    {
        // ---- Which mode is up ----------------------------------------------

        /// <summary>True while the world map is showing instead of the room.</summary>
        public bool MapMode;

        /// <summary>Borderless-fullscreen state, for the View menu's checkmark.</summary>
        public bool IsFullscreen;

        // ---- The current room ----------------------------------------------

        public string RoomDisplayName;
        public string RoomId;

        /// <summary>
        /// True when ANY of the three room dirty flags is set — the same
        /// condition that makes the discard guard block a room switch. The
        /// map's own unsaved state is deliberately NOT folded in; see the
        /// comment on EditorState.MapDirty for why they are separate.
        /// </summary>
        public bool RoomDirty;

        // ---- Canvas view ---------------------------------------------------

        /// <summary>Integer canvas zoom: one of 1, 2, 4, 8, 16.</summary>
        public int Zoom;

        // ---- Undo / redo -----------------------------------------------------
        //
        // Two booleans and not the labels, because the menu shows "Undo" and
        // "Redo" with their shortcuts and greys them by whether their stack has
        // anything in it. The LABEL of what would be undone is reported by the
        // status line at the moment it happens, where it names an entity the
        // author can go and look at; a menu that read "Undo move
        // chateau_0_blockeddoor_2" would change width every time anything
        // happened, and would be read by nobody, because nobody opens a menu to
        // find out what Ctrl+Z is about to do.

        /// <summary>True when the undo stack has at least one entry.</summary>
        public bool CanUndo;

        /// <summary>True when the redo stack has at least one entry.</summary>
        public bool CanRedo;

        // ---- Inspector pickers ------------------------------------------------
        //
        // What the three filterable dropdowns offer (EDITOR_REVIEW item 10).
        // Lists rather than "the next value", which is the whole change: a
        // cycle button is a list you can only see one entry of.

        /// <summary>
        /// Every registry room, in registry order. Test rooms are NOT here.
        /// </summary>
        // The standing decision, unchanged from the cycle this replaces: room_1
        // and room_2 are dev scaffolding registered in Game1.RegisterTestRooms,
        // and the door validator has a whole verdict ("ok-test") for
        // hand-edited data that points at one. Offering them in an AUTHORING
        // list would make that verdict something the editor produces rather
        // than something it tolerates.
        public IReadOnlyList<string> TargetRoomIds;

        /// <summary>
        /// The item catalog a blocked door can require. None is excluded.
        /// </summary>
        // Typed, not names: a string round-trip through an enum is exactly the
        // "the label is load-bearing" trap PaletteEntry.DoorOpeningSide exists
        // to prevent. None is left out because it is not a requirement — a
        // blocked door needing nothing is broken data, the cycle could never
        // reach it either, and a hand-edited JSON that says None still SHOWS
        // "None" in the row above the picker.
        public IReadOnlyList<ItemType> RequiredItems;

        /// <summary>
        /// The door ids of a given room: its saved doors, plus the unsaved ones
        /// when it is the room being edited.
        /// </summary>
        // A FUNCTION and not a list, because the answer depends on the value in
        // the row above it — the target room the author has just chosen — and
        // that changes while the inspector is on screen. The alternative is a
        // dictionary of every room's doors rebuilt every frame for a question
        // that is only ever asked while one popup is open.
        //
        // It is a READ, not a verb: pure, and the panels still cannot make
        // anything happen through it. Null in a harness that does not need it;
        // the panel treats that as "no doors".
        public Func<string, IReadOnlyList<string>>? DoorIdsForRoom;

        // ---- Map view -------------------------------------------------------

        /// <summary>Rooms currently on the board (the board's list, not the registry's).</summary>
        public int MapRoomCount;

        /// <summary>Board zoom as a percentage: 6, 13, 25, 50 or 100.</summary>
        public int MapZoomPercent;

        // ---- Modal overlays --------------------------------------------------
        //
        // At most one of the three is ever open, and the editor's Update
        // returns before the room or the board sees any input while one is —
        // that modality is decided in EditorGame, not here. What these carry is
        // only what the panel must SHOW.

        /// <summary>
        /// True while ANY modal owns the editor: either picker, the crop step,
        /// or a running batch import.
        /// </summary>
        // The bands consult this and go NoInputs. Without it they stay live
        // behind a picker — ImGui windows do not stop hit-testing because
        // another window is drawn over part of the screen, and the palette and
        // inspector are nowhere near the centred panel that covers the canvas.
        // The old chrome got this for free, by returning from Update before any
        // widget handler ran; here it has to be said out loud.
        //
        // A RUNNING BATCH counts, and draws no overlay of its own — it is
        // writing files one per frame, and a click that loaded a different room
        // underneath it would be genuinely destructive.
        public bool ModalOpen;

        public bool NewRoomOpen;
        public IReadOnlyList<RoomCandidate> NewRoomCandidates;

        public bool ImportOpen;
        public IReadOnlyList<ImportCandidate> ImportCandidates;

        /// <summary>Shown under the Import title so the drop folder is never a guess.</summary>
        public string ImportDir;

        /// <summary>Session preference, not room data — see EditorGame's _importQuantize.</summary>
        public bool ImportQuantize;

        /// <summary>
        /// True when "A imports all N" is actually available. The hint says
        /// nothing when it is not, so the key can never look broken.
        /// </summary>
        public bool ImportBatchOffered;
        public int ImportBatchCount;

        // ---- Crop step -------------------------------------------------------
        //
        // The IMAGE, the shaded bands and the selection box stay SpriteBatch —
        // they are a pixel-space tool. Only the header and footer strips are
        // chrome, and these are what they say.

        public bool CropOpen;
        public string CropFileName;
        public string CropRoomId;
        public string CropDisplayName;

        /// <summary>Where the box started, as text. Shown, never enforced.</summary>
        public string CropPresetNote;

        public int CropSourceWidth;
        public int CropSourceHeight;
        public Rectangle CropRect;
    }
}
