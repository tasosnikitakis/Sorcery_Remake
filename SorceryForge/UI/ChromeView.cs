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

        // ---- Map view -------------------------------------------------------

        /// <summary>Rooms currently on the board (the board's list, not the registry's).</summary>
        public int MapRoomCount;

        /// <summary>Board zoom as a percentage: 6, 13, 25, 50 or 100.</summary>
        public int MapZoomPercent;
    }
}
