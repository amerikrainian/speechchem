using System.Collections.Generic;

namespace SpeechChem.Narration
{
    /// <summary>
    /// The configurable step keys (user design 2026-10-04): 0, Ctrl+0 and 5-9 (Ctrl+1 to Ctrl+9
    /// were step keys too until 2026-10-09; they switch reactors now, Common/ReactorSwitch).
    /// Each runs the step (to the next event, at top speed) with its own rules: whether it is
    /// assigned at all (an unassigned key does nothing), which reactor's events count (the open
    /// one, or all), which event types STOP it and which it SPEAKS (separate sets), whether it says
    /// "Cycle N", and how many event-less cycles it runs before giving up. Defaults reproduce the
    /// original keys: 0 = the open reactor's events, Ctrl+0 = every reactor's.
    /// </summary>
    internal static class StepKeys
    {
        public static readonly string[] Ids = { "0", "c0", "5", "6", "7", "8", "9" };
        public static readonly int[] GiveUpChoices = { 100, 250, 500, 1000, 2500, 5000, 10000 };
        public const int GiveUpDefault = 1000;

        /// <summary>"0", "Ctrl+0", "5"…</summary>
        public static string Label(string id) => id.StartsWith("c") ? Localization.Loc.T("step.key.ctrl", new { n = id.Substring(1) }) : id;

        private static string K(string id, string setting) => "step." + id + "." + setting;

        public static bool AssignedDefault(string id) => id == "0" || id == "c0";
        public static string ScopeDefault(string id) => id == "c0" ? EventSettings.ScopeAll : EventSettings.ScopeOpen;

        public static bool Assigned(string id, bool draft = false) => EventSettings.Bool(K(id, "assigned"), AssignedDefault(id), draft);
        public static void SetAssigned(string id, bool on) => EventSettings.Set(K(id, "assigned"), on ? "true" : "false", AssignedDefault(id) ? "true" : "false");

        public static string Scope(string id, bool draft = false) => EventSettings.Str(K(id, "scope"), ScopeDefault(id), draft);
        public static void SetScope(string id, string scope) => EventSettings.Set(K(id, "scope"), scope, ScopeDefault(id));

        // Stops on / speaks inherit down the event tree like the event settings (EventSettings.Put):
        // a branch sets its whole subtree, a node below can be set apart.
        public static bool Stops(string id, EventKind kind, bool draft = false) => Flag(id, "stops.", kind, kind.StepStopsDefault, draft);
        public static void SetStops(string id, EventKind node, bool on)
            => EventSettings.Put(node, n => K(id, "stops." + n.Key), on ? "true" : "false", leaf => Stops(id, leaf, draft: true) ? "true" : "false");

        public static bool Speaks(string id, EventKind kind, bool draft = false) => Flag(id, "speaks.", kind, kind.StepSpeaksDefault, draft);
        public static void SetSpeaks(string id, EventKind node, bool on)
            => EventSettings.Put(node, n => K(id, "speaks." + n.Key), on ? "true" : "false", leaf => Speaks(id, leaf, draft: true) ? "true" : "false");

        // The committed flags, resolved once per settings revision: a step asks for every event.
        private static int _revision = int.MinValue;
        private static readonly Dictionary<(string, string, EventKind), bool> Cache = new Dictionary<(string, string, EventKind), bool>();

        private static bool Flag(string id, string which, EventKind kind, bool fallback, bool draft)
        {
            var cacheKey = (id, which, kind);
            if (!draft)
            {
                if (_revision != NarrationStore.Revision) { Cache.Clear(); _revision = NarrationStore.Revision; }
                if (Cache.TryGetValue(cacheKey, out bool hit)) return hit;
            }
            bool b;
            bool value = bool.TryParse(EventSettings.Inherited(kind, n => K(id, which + n.Key), draft), out b) ? b : fallback;
            if (!draft) Cache[cacheKey] = value;
            return value;
        }

        public static bool SayCycle(string id, bool draft = false) => EventSettings.Bool(K(id, "cycle"), true, draft);
        public static void SetSayCycle(string id, bool on) => EventSettings.Set(K(id, "cycle"), on ? "true" : "false", "true");

        public static int GiveUp(string id, bool draft = false) => EventSettings.Int(K(id, "giveup"), GiveUpDefault, draft);
        public static void SetGiveUp(string id, int cycles) => EventSettings.Set(K(id, "giveup"), cycles.ToString(), GiveUpDefault.ToString());

        public static string Prefix(string id) => "step." + id + ".";
    }
}
