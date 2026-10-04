using System;
using SpaceChem;

namespace SpeechChem.Narration
{
    /// <summary>
    /// Where every run event goes (the capture patches build a <see cref="NarrationEvent"/> and
    /// hand it here). In order:
    ///   1. sources — a waldo's event whose colour is filtered out is dropped everywhere;
    ///   2. the log — when the event's Log setting is on, the event itself goes into the run log
    ///      (interned by content; views format it, and a format change re-renders old entries);
    ///   3. the step — the active step key decides whether this event stops it (its "stops on" set
    ///      and its scope) and says "Cycle N" first;
    ///   4. speech, in the Speech format — during a step by the step key's "speaks" set and scope;
    ///      otherwise by the event's own rule: its Speak checkbox for the moment (running at
    ///      play speed 1-4, or paused / stopped), and inside a reactor only when it concerns that
    ///      reactor unless its scope is "all reactors".
    /// Settings come compiled per revision (<see cref="Rules"/>); an event nothing wants is
    /// muted while it is built (<see cref="Wants"/>).
    /// </summary>
    internal static class Narrator
    {
        /// <param name="speakable">False keeps it out of speech whatever the settings (the
        /// "Stopped" of leaving the level — user rule).</param>
        /// <param name="cycle">The log group to file it under (default: the clock's cycle).</param>
        public static void Emit(NarrationEvent e, bool speakable = true, int? cycle = null)
        {
            try
            {
                if (e?.Kind == null || e.Muted || e.IsEmpty) return;
                var kind = e.Kind;
                var rules = Rules.Of(kind);
                if (kind.Waldo && e.Colour >= 0 && !rules.Source(e.Colour)) return;
                e.Cycle = cycle ?? Class258.int_1;
                if (rules.Log) Patches.RunCapture.Log.Add(e.Cycle, e);
                bool stepping = Patches.StepControl.Active;
                Patches.StepControl.OnEvent(e);
                if (!speakable) return;
                bool speak = stepping ? Patches.StepControl.Speaks(e) : SpeaksInRun(e, rules);
                if (speak) Speech.Tts.Speak(Formatter.Format(e, FormatLayer.Speech, OpenReactor()));
            }
            catch (Exception ex) { Log.Error("[narration] emit", ex); }
        }

#if DEBUG
        /// <summary>Dev/LogStress: every event is muted (measures capture without events).</summary>
        internal static bool DebugMute;
#endif

        /// <summary>Whether anything would use an event of this kind now: its log, a step key (any,
        /// while a step runs), or its Speak checkbox for the current moment (the reactor scope is
        /// left to Emit — this is the cheap superset). False lets the builders skip their work.</summary>
        public static bool Wants(EventKind kind)
        {
            try
            {
#if DEBUG
                if (DebugMute) return false;
#endif
                var rules = Rules.Of(kind);
                return rules.Log || LiveWants(rules);
            }
            catch { return true; }
        }

        // Game state lives in its own methods / class: Wants is reached from event construction,
        // which the unit tests run without the game assembly.
        private static bool LiveWants(KindRules rules) => Patches.StepControl.Active || rules.SpeakAt[RunMoment.Index()];

        /// <summary>The event's own speech rule outside a step.</summary>
        private static bool SpeaksInRun(NarrationEvent e, KindRules rules)
        {
            if (e.Kind.ReactorScoped && !e.Concerns && !rules.ScopeAll) return false;
            return rules.SpeakAt[RunMoment.Index()];
        }

        /// <summary>The reactor open now (its own events drop "reactor N"), or null.</summary>
        public static object OpenReactor()
        {
            try { return Class53.smethod_5<Class77>()?.reactor_0; } catch { return null; }
        }
    }

    internal static class RunMoment
    {
        // The moment's index in KindRules.SpeakAt: play speed 1-4 → 0-3, paused / stopped → 4.
        // The toolbar walk behind the speed number is cached per frame and simulator speed (a
        // defense level's toolbar maps speeds its own way).
        private static SimulatorSpeed _speed;
        private static TimeSpan _speedFrame;
        private static int _speedIndex = -1;

        public static int Index()
        {
            if ((int)Class258.smethod_16() != 1) return 4;
            var speed = Class258.smethod_14();
            var frame = Class280.struct102_0.timeSpan_0;
            if (_speedIndex < 0 || speed != _speed || frame != _speedFrame)
            {
                _speed = speed;
                _speedFrame = frame;
                _speedIndex = Math.Max(0, Math.Min(3, Screens.Common.ProgressSection.SpeedNumber(speed) - 1));
            }
            return _speedIndex;
        }
    }
}
