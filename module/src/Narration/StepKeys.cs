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

        public static bool Stops(string id, EventKind kind, bool draft = false) => EventSettings.Bool(K(id, "stops." + kind.Key), kind.StepStopsDefault, draft);
        public static void SetStops(string id, EventKind kind, bool on) => EventSettings.Set(K(id, "stops." + kind.Key), on ? "true" : "false", kind.StepStopsDefault ? "true" : "false");

        public static bool Speaks(string id, EventKind kind, bool draft = false) => EventSettings.Bool(K(id, "speaks." + kind.Key), kind.StepSpeaksDefault, draft);
        public static void SetSpeaks(string id, EventKind kind, bool on) => EventSettings.Set(K(id, "speaks." + kind.Key), on ? "true" : "false", kind.StepSpeaksDefault ? "true" : "false");

        public static bool SayCycle(string id, bool draft = false) => EventSettings.Bool(K(id, "cycle"), true, draft);
        public static void SetSayCycle(string id, bool on) => EventSettings.Set(K(id, "cycle"), on ? "true" : "false", "true");

        public static int GiveUp(string id, bool draft = false) => EventSettings.Int(K(id, "giveup"), GiveUpDefault, draft);
        public static void SetGiveUp(string id, int cycles) => EventSettings.Set(K(id, "giveup"), cycles.ToString(), GiveUpDefault.ToString());

        public static string Prefix(string id) => "step." + id + ".";
    }
}
