using System;
using HarmonyLib;
using SpaceChem;
using SpaceChem.Pipeline;
using SpaceChem.UI;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.Narration;

namespace SpeechChem.Patches
{
    /// <summary>
    /// Step to the next event (user request, 2026-10-03, replacing the single-cycle step of
    /// 2026-09-27; the game has no step button). The simulation clock (Class258.smethod_22) runs in
    /// sub-ticks, 10 per cycle: sub-tick 10k is the cycle boundary (waldos move and run their
    /// instructions — PipelineSimulator.method_3 via delegate22 — then the cycle counter int_1
    /// increments), and every sub-tick runs the collision checks (PipelineSimulator.method_4 via
    /// delegate25). The loop runs only while the state is Running, so pausing from inside the
    /// sub-tick callback stops it exactly — the same way a reaction error stops it.
    ///
    /// A step: run from the next boundary (the current sub-tick when paused on one; after the rest
    /// of a cycle paused mid-way; 0 from stopped) until RunCapture logs an event, then through the
    /// rest of the cycle the clock is in (so all of that cycle's events are heard), then Paused.
    /// Cycles without an event pass silently; the first event says "Cycle N" (its log group)
    /// before it, and the cycle's events queue after it (RunCapture speaks events while stepping,
    /// at any speed). It runs at WARP whatever speed was in use (user rule 2026-10-03; in defense
    /// levels the toolbar's own map of warp, ToolbarComponent.method_1) and puts the speed back
    /// when it ends, however it ends. The Running / Paused announcements it causes are silenced; a
    /// reaction error or a completion ends it normally. With no event for
    /// <see cref="MaxCycles"/> cycles (both waldos stuck in a sync, say) it pauses and says so.
    ///
    /// Step KEYS (user design 2026-10-04, Narration/StepKeys): 0, Ctrl+0, 5-9 and Ctrl+1 to Ctrl+9,
    /// each with its own rules from the Settings dialog — unassigned does nothing; which event
    /// types stop it and which it speaks (separate sets); its scope (the open reactor's events, or
    /// every reactor's — an event "concerns" the open reactor as RunCapture decides); whether it
    /// says "Cycle N"; how many event-less cycles before it gives up. Defaults reproduce the
    /// original two: 0 = the open reactor's events, Ctrl+0 = every reactor's. Everything is logged
    /// by the events' own Log settings either way.
    /// </summary>
    internal static class StepControl
    {
        private const int Searching = int.MaxValue;

        private static int _target = -1;
        private static int _startTick;
        private static bool _starting, _pausing;
        private static DateTime _found;
        private static SimulatorSpeed? _restore;
        private static string _key;

        /// <summary>A step is in progress (its events are spoken).</summary>
        public static bool Active => _target >= 0;

        /// <summary>The state change in progress is the step's own (its start or its final pause):
        /// not announced or logged. Any other change during a step ends the step.</summary>
        public static bool Quiet => _starting || _pausing;

        /// <summary>The step key's scope admits this event (its reactor's, or every reactor's).</summary>
        private static bool InScope(NarrationEvent e)
            => e.Concerns || StepKeys.Scope(_key) == EventSettings.ScopeAll;

        /// <summary>During a step: whether the step key speaks this event.</summary>
        public static bool Speaks(NarrationEvent e)
            => Active && _key != null && StepKeys.Speaks(_key, e.Kind) && InScope(e);

        public static void Apply(Harmony harmony)
        {
            try
            {
                harmony.Patch(Expr.MethodOf(() => default(PipelineSimulator).method_4()),
                    postfix: new HarmonyMethod(typeof(StepControl), nameof(AfterSubTick)));
                Log.Info("[patch] step control armed");
            }
            catch (Exception ex) { Log.Error("[patch] step control failed to apply", ex); }
        }

        /// <summary>The 0 key / the toolbar's Step button.</summary>
        public static void Step() => Step("0");

        /// <summary>Ctrl+0.</summary>
        public static void StepAll() => Step("c0");

        /// <summary>The step keys' screen actions (0, Ctrl+0 and the configurable rest), for the
        /// reactor and pipeline editors.</summary>
        public static System.Collections.Generic.IEnumerable<UI.ElementAction> Actions()
        {
            yield return new UI.ElementAction("screen.reactor.step", Step);
            yield return new UI.ElementAction("screen.reactor.step.all", StepAll);
            foreach (var id in StepKeys.Ids)
            {
                if (id == "0" || id == "c0") continue;
                string key = id;
                yield return new UI.ElementAction("screen.reactor.step.key." + id, () => Step(key));
            }
        }

        /// <summary>A step key: its rules, or nothing when it is unassigned.</summary>
        public static void Step(string key)
        {
            try
            {
                if (Active || !StepKeys.Assigned(key)) return; // one at a time; an unassigned key is a no-op
                if (GoalTracker.bool_0)
                {
                    // The enemy is destroyed: the game freezes every waldo until it declares the
                    // win 700 cycles later (Class310.method_4, Class188), so there is nothing to
                    // step to — say so instead of doing nothing.
                    Speech.Tts.Speak(Loc.T("run.step.frozen", new { name = DefenseText.EnemyName(DefenseText.Level) }), interrupt: true);
                    return;
                }
                _key = key;
                int state = (int)Class258.smethod_16();
                int tick = state == 0 ? 0 : Class258.int_2;
                _startTick = tick % 10 == 0 ? tick : (tick / 10 + 1) * 10;
                _target = Searching;
                _starting = true;
                try
                {
                    if (state != 1) Class258.smethod_17((Enum16)1);
                    var current = Class258.smethod_14();
                    var fastest = ToolbarComponent.smethod_0().method_1(SimulatorSpeed.WarpSpeed);
                    if (current != fastest)
                    {
                        _restore = current;
                        SetSpeed(fastest);
                    }
                }
                finally { _starting = false; }
            }
            catch (Exception ex)
            {
                End();
                Log.Error("[step] start failed", ex);
            }
        }

        /// <summary>The Narrator saw an event (filed under its cycle). The step's first one that its
        /// key stops on (in its scope) says the cycle (when the key does) and sets the pause at the
        /// end of the cycle the clock is in.</summary>
        public static void OnEvent(NarrationEvent e)
        {
            try
            {
                if (_target != Searching || _key == null || !StepKeys.Stops(_key, e.Kind) || !InScope(e)) return;
                _target = Class258.int_2 / 10 * 10 + 9;
                _found = DateTime.UtcNow;
                if (StepKeys.SayCycle(_key)) Speech.Tts.Speak(Loc.T("run.cycle", new { n = e.Cycle }), interrupt: true);
            }
            catch { }
        }

        /// <summary>Per sub-tick (PipelineSimulator.method_4): at the event cycle's last sub-tick,
        /// pause — the clock loop checks the state before its next sub-tick.</summary>
        private static void AfterSubTick()
        {
            try
            {
                if (!Active) return;
                if (_target == Searching ? GaveUp() : Class258.int_2 >= _target) Finish();
            }
            catch { }
        }

        /// <summary>No event in the key's give-up cycles: say so (the caller pauses).</summary>
        private static bool GaveUp()
        {
            int cycles = _key != null ? StepKeys.GiveUp(_key) : StepKeys.GiveUpDefault;
            if (Class258.int_2 - _startTick < cycles * 10) return false;
            Speech.Tts.Speak(Loc.T("run.step.none", new { n = cycles }), interrupt: true);
            return true;
        }

        /// <summary>FrameLoop step: the safety net — if the pause was missed (a level without the
        /// pipeline simulator's sub-tick callback) or never came, pause anyway.</summary>
        public static void Tick()
        {
            try
            {
                if (!Active) return;
                int state = (int)Class258.smethod_16();
                if (state != 1) { End(); return; } // an error, a completion, a stop: the step is over
                bool over = _target == Searching
                    ? GaveUp()
                    : Class258.int_2 > _target || (DateTime.UtcNow - _found).TotalSeconds > 10;
                if (over) Finish();
            }
            catch { End(); }
        }

        private static void Finish()
        {
            End();
            _pausing = true;
            try { Class258.smethod_17((Enum16)2); }
            finally { _pausing = false; }
        }

        /// <summary>A state change the step didn't make (error box, completion, leaving) ends it.</summary>
        public static void OnForeignStateChange() => End();

        /// <summary>The step is over: the speed it replaced comes back.</summary>
        private static void End()
        {
            _target = -1;
            if (_restore == null) return;
            var speed = _restore.Value;
            _restore = null;
            try { SetSpeed(speed); }
            catch (Exception ex) { Log.Error("[step] speed restore failed", ex); }
        }

        /// <summary>Class258.smethod_13's clock change without its smethod_15 tail, which would
        /// start a paused or stopped run and fire the speed-change callbacks: the step's warp is
        /// not a speed the player chose (the toolbar keeps showing theirs).</summary>
        private static void SetSpeed(SimulatorSpeed speed)
        {
            int n = (int)speed;
            Class258.simulatorSpeed_0 = speed;
            Class258.int_0 = n;
            Class258.timeSpan_0 = TimeSpan.FromTicks(10000000 / n / 10);
            Class258.timeSpan_1 = TimeSpan.FromTicks(10000000 / n);
            if ((int)Class258.smethod_16() == 1)
            {
                var now = Class280.struct102_0.timeSpan_0;
                Class258.timeSpan_2 = now - TimeSpan.FromSeconds(Class258.timeSpan_0.TotalSeconds * Class258.float_0);
                Class258.timeSpan_3 = now - TimeSpan.FromSeconds(Class258.timeSpan_1.TotalSeconds * Class258.float_0);
            }
        }
    }
}
