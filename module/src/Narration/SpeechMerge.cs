using SpeechChem.Localization;

namespace SpeechChem.Narration
{
    /// <summary>
    /// One utterance per waldo per cycle (user request 2026-10-09, from ahicks' "red grab and up"):
    /// a waldo's instruction and its turn are separate events — separately logged, set up and
    /// stepped on — but their SPEECH is held here and joined, the follow-ups without the common
    /// parts: "red: grabbed Oxygen, O2 and heading up". The Narrator speaks the held text when an
    /// event of another waldo, cycle or reactor (or no waldo's) comes, and at the start of every
    /// frame. Only consecutive events merge: a waldo's wall stop comes from a later pass over the
    /// waldos (Reactor.method_34, after every waldo's step in method_35), so it stays its own.
    /// </summary>
    internal static class SpeechMerge
    {
        private static object _reactor;
        private static int _colour = -1, _cycle;
        private static string _text;

        public static bool Pending => _text != null;

        /// <summary>The event continues the held utterance: same reactor, waldo and cycle.</summary>
        public static bool Continues(NarrationEvent e)
            => _text != null && e.Colour == _colour && e.Cycle == _cycle && ReferenceEquals(e.Reactor, _reactor);

        public static void Start(NarrationEvent e, string text)
        {
            _reactor = e.Reactor;
            _colour = e.Colour;
            _cycle = e.Cycle;
            _text = string.IsNullOrEmpty(text) ? null : text;
        }

        public static void Append(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _text = _text == null ? text : Loc.T("narr.t.merge", new { a = _text, b = text });
        }

        /// <summary>The held utterance (null when none), forgotten.</summary>
        public static string Take()
        {
            string t = _text;
            _text = null;
            _reactor = null;
            _colour = -1;
            return t;
        }
    }
}
