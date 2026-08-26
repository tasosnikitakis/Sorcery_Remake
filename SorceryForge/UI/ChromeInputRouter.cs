// ============================================================================
// CHROME INPUT ROUTER
// SorceryForge — who gets this frame's mouse and keyboard: ImGui, or the canvas
// ============================================================================
// THE RULE, in one line: ImGui gets first refusal; a gesture already under way
// on the canvas overrides that refusal until it finishes.
//
// FIRST REFUSAL. Dear ImGui computes io.WantCaptureMouse / WantCaptureKeyboard
// during NewFrame, from where the cursor is and what it is doing. When it says
// it wants the mouse, the canvas and the map board do not see that frame's
// mouse at all. This is what finally settles the wheel: the palette and the
// inspector are ImGui windows, so hovering either sets WantCaptureMouse and
// ImGui scrolls itself; the canvas is NOT an ImGui window, so hovering it
// leaves the flag false and EditorGame.HandleCanvasView zooms. The two can no
// longer both act on one notch, and neither can be silently starved — which is
// exactly the bug the hand-rolled palette scroll shipped with, where three
// independent wheel consumers each region-tested their own rectangle and the
// rectangles were maintained in three different places.
//
// THE OVERRIDE, and why it is not optional. First refusal alone breaks any
// gesture that legitimately leaves the canvas. Drag a placement toward the room
// edge and the cursor routinely crosses onto the inspector before the button
// comes up; the old code caught that release in HandleCanvasInput's
// out-of-canvas branch, ended the move, and fired auto-punch there. Gate that
// branch on ImGui and the release is never seen: the move stays "in progress"
// for ever and the next canvas click resumes dragging an entity the user
// thought they had dropped. Same shape for a middle-drag pan and for an erase
// stroke, both of which are documented as continuing past the canvas edge.
//
// So a gesture that STARTED on the canvas keeps the mouse until it ends,
// wherever the cursor wanders. Nothing may START through a chrome panel.
//
// Dear ImGui has its own click-ownership tracking that points the same way, and
// on the frames it agrees this override does nothing. It is written out here
// anyway rather than leaned on, because "the gesture continues" is a promise
// this editor makes to the person dragging, and a promise that depends on the
// internals of a third-party immediate-mode library is a promise with a version
// number attached. tools/ChromeCheck drives the real ImGui and asserts both.
//
// KEYBOARD. Two things hold it, not one: WantCaptureKeyboard, and an open
// popup. With keyboard navigation deliberately off (see ImGuiRenderer) and
// no text field anywhere in the chrome, WantCaptureKeyboard is false except
// while an ImGui widget is actively held. Every editor keybind therefore keeps
// firing exactly as it did. The gate is implemented properly regardless — the
// first text field to land must not have to discover this file.
//
// DEVICE-FREE by construction: no Texture2D, no GraphicsDevice, no SpriteBatch.
// That is what lets tools/ChromeCheck compile it and drive it headlessly.
// ============================================================================

namespace SorceryForge.UI
{
    public sealed class ChromeInputRouter
    {
        /// <summary>What ImGui asked for during the current frame's NewFrame.</summary>
        public bool ImGuiWantsMouse { get; private set; }
        public bool ImGuiWantsKeyboard { get; private set; }

        /// <summary>True while an ImGui popup — in practice, a menu — is open.</summary>
        // A THIRD input, because WantCaptureKeyboard does not cover it and the
        // gap is dangerous. ImGui raises that flag for an active widget or a
        // modal window; an open MENU is neither, and with keyboard navigation
        // deliberately off (see ImGuiRenderer) ImGui's own Escape-closes-a-popup
        // path never runs either. So without this, "open the File menu, change
        // your mind, press Escape" reaches the editor's Escape — which arms the
        // discard guard, and on a clean room quits outright.
        //
        // Measured rather than assumed: tools/ChromeCheck section 5 opens a real
        // menu and reports WantCaptureKeyboard false with the popup still up.
        public bool ImGuiPopupOpen { get; private set; }

        /// <summary>
        /// True while a canvas/map gesture that began on the world surface is
        /// still running: a placement or spawn move, a middle-drag pan, an
        /// erase stroke, a map room-drag or board-pan.
        /// </summary>
        // Set by EditorGame each frame from its own state, because EditorGame
        // is where those flags live and duplicating them here would give the
        // router a second opinion about whether a drag is happening.
        public bool WorldGestureInProgress { get; set; }

        /// <summary>Record this frame's ImGui verdict. Call right after NewFrame.</summary>
        public void Sample(bool wantsMouse, bool wantsKeyboard, bool popupOpen = false)
        {
            ImGuiWantsMouse = wantsMouse;
            ImGuiWantsKeyboard = wantsKeyboard;
            ImGuiPopupOpen = popupOpen;
        }

        /// <summary>
        /// True when the canvas / map board may read this frame's mouse.
        /// </summary>
        public bool MouseReachesWorld => !ImGuiWantsMouse || WorldGestureInProgress;

        /// <summary>
        /// True when the editor's own keybinds may read this frame's keyboard.
        /// </summary>
        public bool KeyboardReachesEditor => !ImGuiWantsKeyboard && !ImGuiPopupOpen;
    }
}
