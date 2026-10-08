using System;

namespace SpeechChem.UI
{
    /// <summary>
    /// Alt+Backspace (user request 2026-10-07): back to where focus was before the last jump — a
    /// stop jump key, Enter on a Components output, a category item, a waldo jump, a path-trace
    /// line. Pressed again it returns, so two presses toggle between the two places. A screen
    /// records the place it leaves (as how to come back to it) just before each jump.
    /// </summary>
    internal sealed class JumpBack
    {
        private Action _back;

        /// <summary>Call just before a jump, with how to return to where focus is now.</summary>
        public void Remember(Action returnHere) => _back = returnHere;

        /// <summary>Go back; <paramref name="returnHere"/> becomes the way back again (a toggle).
        /// Nothing remembered: a silent no-op.</summary>
        public void Back(Action returnHere)
        {
            var go = _back;
            if (go == null) return;
            _back = returnHere;
            go();
        }

        public void Clear() => _back = null;
    }
}
