using System.Collections.Generic;
using SpeechChem.Localization;

namespace SpeechChem.Narration
{
    /// <summary>
    /// A visible state's change as a run event says it: only what changed (user rule 2026-10-09:
    /// "badly damaged, shield down" → "shield down" when the shield went down — the rest can be
    /// looked at on the map or the Enemy stop). States are lists of components (an enemy's:
    /// Game/DefenseText). Game-free, so the tests reach it.
    /// </summary>
    internal static class StateChange
    {
        /// <summary>The components that appeared; else, everything gone, "normal"; else the ones that
        /// ended ("lightning ended"). Null when nothing changed.</summary>
        public static string Describe(List<string> before, List<string> now)
        {
            before = before ?? new List<string>();
            now = now ?? new List<string>();
            var added = now.FindAll(p => !before.Contains(p));
            if (added.Count > 0) return string.Join(", ", added.ToArray());
            var ended = before.FindAll(p => !now.Contains(p));
            if (ended.Count == 0) return null;
            if (now.Count == 0) return Loc.T("defense.state.none");
            return string.Join(", ", ended.ConvertAll(p => Loc.T("defense.state.ended", new { state = p })).ToArray());
        }
    }
}
