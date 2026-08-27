// ============================================================================
// UNDO STACK
// SorceryForge — the one path Ctrl+Z and Ctrl+Y walk
// ============================================================================
// Two lists of IEditorCommand and five rules. Everything interesting about undo
// is in the commands (EditorCommands.cs); what is interesting HERE is the four
// decisions that a stack gets to make, each of which has a way of being wrong
// that nobody notices until it costs someone an hour.
//
// 1. A NEW EDIT CLEARS THE REDO STACK. Undo three things, then paint a tile:
//    the three you undid are gone, because the history they belonged to no
//    longer exists. Keeping them would let Ctrl+Y replay a move onto a
//    placement that the new edit deleted. Every editor works this way; it is
//    written down because it is the rule most easily left out.
//
// 2. THE STACK IS CAPPED, AND EVICTS THE OLDEST. 64 entries. The cap is about
//    memory (a background command can be the size of the changed pixels) and
//    about honesty: an unbounded stack in a program with no save-points is a
//    promise it cannot keep across a long session.
//
// 3. THE STACK IS PER ROOM, AND A ROOM SWITCH CLEARS BOTH HALVES. This is the
//    load-bearing one. LoadRoom REBUILDS Placements from disk — every Placement
//    object in the room's working set is replaced by a new instance — and the
//    commands hold REFERENCES to those objects. A move command surviving a room
//    switch would, on Ctrl+Z, set the position of an object that is no longer in
//    any room's list: no crash, no visible effect, and the edit the user thought
//    they took back still there. Worse, come back to the first room and the
//    stale command is holding the OLD object while the canvas draws the NEW one.
//    Making commands carry entity ids instead of references would not fix it
//    either — it would just move the problem to "the id now names a different
//    object with different fields". Undo history is per-room working state; a
//    room switch discards working state; the stack goes with it. doc/07 records
//    the decision where an author would look for it.
//
// 4. UNDO AND REDO ALWAYS DIRTY WHAT THEY TOUCH. Conservative, deliberately:
//    see the note on MarkPlacementsChanged. Undoing back to exactly the
//    last-saved state leaves the room marked dirty, which costs one redundant
//    save; the opposite error loses work.
//
// The fifth rule is not this file's: the EDITOR closes any in-progress stroke
// or drag before it calls Undo, so a half-finished gesture can never merge into
// the entry being popped. See EditorGame.UndoLastEdit.
//
// DEVICE-FREE, like EditorCommands.cs, so tools/EditCheck drives all of it.
// ============================================================================

using System.Collections.Generic;

namespace SorceryForge
{
    public sealed class UndoStack
    {
        /// <summary>How many actions deep the history goes.</summary>
        // 64, up from the 40 the background-only history carried. Both numbers
        // are arbitrary; this one is larger because the stack now holds every
        // kind of edit, so a single minute of authoring fills far more of it.
        public const int MaxDepth = 64;

        private readonly List<IEditorCommand> _undo = new();
        private readonly List<IEditorCommand> _redo = new();

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        public int UndoDepth => _undo.Count;
        public int RedoDepth => _redo.Count;

        /// <summary>What Ctrl+Z would take back, or null when there is nothing.</summary>
        public string? NextUndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;

        /// <summary>What Ctrl+Y would put back, or null when there is nothing.</summary>
        public string? NextRedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;

        /// <summary>
        /// Run a command and record it. The command's Do IS the edit — nothing
        /// else has happened yet.
        /// </summary>
        // Preferred over PushApplied wherever the edit is discrete, because it
        // makes Do() the single description of what the action does. A command
        // whose Do drifts from the code that "really" performs the edit is a
        // redo that produces something the original click did not, and there is
        // no way to notice that by reading two methods that look similar.
        public void Execute(IEditorCommand command, EditorCommandContext ctx)
        {
            command.Do(ctx);
            Push(command);
        }

        /// <summary>
        /// Record a command whose effect is ALREADY in the state.
        /// </summary>
        // For the gestures that are inherently incremental: a drag has moved the
        // placement sixty times before anyone knows where it ended, and an erase
        // stroke has already written its pixels. Re-running Do() for those would
        // be a no-op at best and a second identical edit at worst, so the
        // command is built from the before/after the gesture observed and pushed
        // as history. tools/EditCheck asserts the round-trip property on every
        // command class, which is what keeps this half honest.
        public void PushApplied(IEditorCommand command) => Push(command);

        private void Push(IEditorCommand command)
        {
            // Rule 1: the future this command's history no longer leads to.
            _redo.Clear();

            _undo.Add(command);
            // Rule 2: oldest out first, so the entries nearest to "now" — the
            // ones anyone actually reaches for — are the ones that survive.
            if (_undo.Count > MaxDepth) _undo.RemoveAt(0);
        }

        /// <summary>
        /// Take back the most recent action. Returns its label, or null when
        /// there was nothing to undo.
        /// </summary>
        public string? Undo(EditorCommandContext ctx)
        {
            if (_undo.Count == 0) return null;

            var command = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            command.Undo(ctx);

            _redo.Add(command);
            if (_redo.Count > MaxDepth) _redo.RemoveAt(0);
            return command.Label;
        }

        /// <summary>
        /// Put back the most recently undone action. Returns its label, or null
        /// when there was nothing to redo.
        /// </summary>
        public string? Redo(EditorCommandContext ctx)
        {
            if (_redo.Count == 0) return null;

            var command = _redo[^1];
            _redo.RemoveAt(_redo.Count - 1);
            command.Do(ctx);

            // Straight onto the undo stack — NOT through Push, which would
            // clear the redo list we are in the middle of walking. Redoing four
            // things in a row has to stay possible.
            _undo.Add(command);
            if (_undo.Count > MaxDepth) _undo.RemoveAt(0);
            return command.Label;
        }

        /// <summary>Rule 3: a room switch throws away both halves.</summary>
        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
        }
    }
}
