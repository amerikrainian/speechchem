using System;
using SpaceChem;

namespace SpeechChem.Narration
{
    /// <summary>
    /// Where every run event goes (the capture patches build a <see cref="NarrationEvent"/> and
    /// hand it here). In order:
    ///   1. sources — a waldo's event whose colour is filtered out is dropped everywhere;
    ///   2. the log — when the event's Log setting is on, its Log-format text goes into the run log
    ///      with the event itself attached (views re-format it: the open reactor's view drops its
    ///      own "reactor N", and a format change re-renders old entries);
    ///   3. the step — the active step key decides whether this event stops it (its "stops on" set
    ///      and its scope) and says "Cycle N" first;
    ///   4. speech, in the Speech format — during a step by the step key's "speaks" set and scope;
    ///      otherwise by the event's own rule: its Speak checkbox for the moment (running at
    ///      play speed 1-4, or paused / stopped), and inside a reactor only when it concerns that reactor unless its scope is
    ///      "all reactors".
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
                if (e?.Kind == null || e.IsEmpty) return;
                var kind = e.Kind;
                if (kind.Waldo && e.Colour >= 0 && !EventSettings.Source(kind, e.Colour)) return;
                e.Cycle = cycle ?? Class258.int_1;
                if (EventSettings.Log(kind))
                    Patches.RunCapture.Log.Add(e.Cycle, Formatter.Format(e, FormatLayer.Log, null), e);
                bool stepping = Patches.StepControl.Active;
                Patches.StepControl.OnEvent(e);
                if (!speakable) return;
                bool speak = stepping ? Patches.StepControl.Speaks(e) : SpeaksInRun(e);
                if (speak) Speech.Tts.Speak(Formatter.Format(e, FormatLayer.Speech, OpenReactor()));
            }
            catch (Exception ex) { Log.Error("[narration] emit", ex); }
        }

        /// <summary>The event's own speech rule outside a step.</summary>
        private static bool SpeaksInRun(NarrationEvent e)
        {
            var kind = e.Kind;
            if (kind.ReactorScoped && !e.Concerns && EventSettings.Scope(kind) != EventSettings.ScopeAll) return false;
            string moment = (int)Class258.smethod_16() == 1
                ? Screens.Common.ProgressSection.SpeedNumber(Class258.smethod_14()).ToString() : "idle";
            return EventSettings.SpeaksAt(kind, moment);
        }

        /// <summary>The reactor open now (its own events drop "reactor N"), or null.</summary>
        public static object OpenReactor()
        {
            try { return Class53.smethod_5<Class77>()?.reactor_0; } catch { return null; }
        }
    }
}
