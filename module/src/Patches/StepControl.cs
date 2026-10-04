using System;
using HarmonyLib;
using SpaceChem;
using SpaceChem.Pipeline;
using SpaceChem.UI;
using SpeechChem.Game;
using SpeechChem.Localization;

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
    /// Two kinds (user rule 2026-10-03): 0 stops only on an event that concerns the open reactor
    /// (its waldos, a building it feeds straight — RunCapture decides) and speaks only those;
    /// Ctrl+0 stops on any event and speaks the cycle's events from every reactor. With no
    /// reactor open (the pipeline screen) the two are the same. Everything is logged either way.
    /// </summary>
    internal static class StepControl
    {
        public const int MaxCycles = 1000;
        private const int Searching = int.MaxValue;

        private static int _target = -1;
        private static int _startTick;
        private static bool _starting, _pausing;
        private static DateTime _found;
        private static SimulatorSpeed? _restore;
        private static bool _ownOnly;

        /// <summary>A step is in progress (its events are spoken).</summary>
        public static bool Active => _target >= 0;

        /// <summary>The state change in progress is the step's own (its start or its final pause):
        /// not announced or logged. Any other change during a step ends the step.</summary>
        public static bool Quiet => _starting || _pausing;

        /// <summary>The step in progress is 0's: only events concerning the open reactor end it or
        /// are spoken.</summary>
        public static bool OwnOnly => Active && _ownOnly;

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

        /// <summary>The 0 key / the toolbar's Step button: to the next event of the open reactor.</summary>
        public static void Step() => Step(ownOnly: true);

        /// <summary>Ctrl+0: to the next event of any reactor.</summary>
        public static void StepAll() => Step(ownOnly: false);

        private static void Step(bool ownOnly)
        {
            try
            {
                if (Active) return; // one at a time
                if (GoalTracker.bool_0)
                {
                    // The enemy is destroyed: the game freezes every waldo until it declares the
                    // win 700 cycles later (Class310.method_4, Class188), so there is nothing to
                    // step to — say so instead of doing nothing.
                    Speech.Tts.Speak(Loc.T("run.step.frozen", new { name = DefenseText.EnemyName(DefenseText.Level) }), interrupt: true);
                    return;
                }
                _ownOnly = ownOnly;
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

        /// <summary>RunCapture logged an event under <paramref name="cycle"/> (its log group);
        /// <paramref name="concerns"/>: it concerns the open reactor (true with none open). The
        /// step's first qualifying one says the cycle and sets the pause at the end of the cycle
        /// the clock is in.</summary>
        public static void OnEvent(int cycle, bool concerns)
        {
            try
            {
                if (_target != Searching || (_ownOnly && !concerns)) return;
                _target = Class258.int_2 / 10 * 10 + 9;
                _found = DateTime.UtcNow;
                Speech.Tts.Speak(Loc.T("run.cycle", new { n = cycle }), interrupt: true);
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

        /// <summary>No event in <see cref="MaxCycles"/> cycles: say so (the caller pauses).</summary>
        private static bool GaveUp()
        {
            if (Class258.int_2 - _startTick < MaxCycles * 10) return false;
            Speech.Tts.Speak(Loc.T("run.step.none", new { n = MaxCycles }), interrupt: true);
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
