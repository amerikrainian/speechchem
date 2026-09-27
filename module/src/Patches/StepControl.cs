using System;
using HarmonyLib;
using SpaceChem;
using SpaceChem.Pipeline;
using SpeechChem.Game;
using SpeechChem.Localization;

namespace SpeechChem.Patches
{
    /// <summary>
    /// Single-cycle stepping (user request, 2026-09-27 — the game has no step button; this replaces
    /// the earlier "no single-step" rule). The simulation clock (Class258.smethod_22) runs in
    /// sub-ticks, 10 per cycle: sub-tick 10k is the cycle boundary (waldos move and run their
    /// instructions — PipelineSimulator.method_3 via delegate22 — then the cycle counter int_1
    /// increments), and every sub-tick runs the collision checks (PipelineSimulator.method_4 via
    /// delegate25). The loop runs only while the state is Running, so pausing from inside the
    /// sub-tick callback stops it exactly — the same way a reaction error stops it.
    ///
    /// A step: from the next boundary (the current sub-tick when paused on one; after the rest of
    /// a cycle paused mid-way; 0 from stopped) through that cycle's last sub-tick, then Paused. It
    /// says "Cycle N" first; the cycle's run events queue after it (RunCapture speaks events
    /// while stepping, at any speed). It runs at the speed last used — the pause is exact at any
    /// speed, so a step at warp is instant and nothing needs restoring. The Running / Paused
    /// announcements it causes are silenced; a reaction error or a completion ends it normally.
    /// </summary>
    internal static class StepControl
    {
        private static int _target = -1;
        private static bool _starting, _pausing;
        private static DateTime _started;

        /// <summary>A step is in progress (its events are spoken).</summary>
        public static bool Active => _target >= 0;

        /// <summary>The state change in progress is the step's own (its start or its final pause):
        /// not announced or logged. Any other change during a step ends the step.</summary>
        public static bool Quiet => _starting || _pausing;

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
        public static void Step()
        {
            try
            {
                if (Active || GoalTracker.bool_0) return; // one at a time; nothing after a completion
                int state = (int)Class258.smethod_16();
                int tick = state == 0 ? 0 : Class258.int_2;
                int boundary = tick % 10 == 0 ? tick : (tick / 10 + 1) * 10;
                _target = boundary + 9;
                _started = DateTime.UtcNow;
                Speech.Tts.Speak(Loc.T("run.cycle", new { n = boundary / 10 }), interrupt: true);
                if (state != 1)
                {
                    _starting = true;
                    try { Class258.smethod_17((Enum16)1); }
                    finally { _starting = false; }
                }
            }
            catch (Exception ex)
            {
                _target = -1;
                Log.Error("[step] start failed", ex);
            }
        }

        /// <summary>Per sub-tick (PipelineSimulator.method_4): at the target cycle's last sub-tick,
        /// pause — the clock loop checks the state before its next sub-tick.</summary>
        private static void AfterSubTick()
        {
            try
            {
                if (!Active || Class258.int_2 < _target) return;
                Finish();
            }
            catch { }
        }

        /// <summary>FrameLoop step: the safety net — if the pause was missed (a level without the
        /// pipeline simulator's sub-tick callback) or never came, pause anyway.</summary>
        public static void Tick()
        {
            try
            {
                if (!Active) return;
                int state = (int)Class258.smethod_16();
                if (state != 1) { _target = -1; return; } // an error, a completion, a stop: the step is over
                if (Class258.int_2 > _target || (DateTime.UtcNow - _started).TotalSeconds > 10) Finish();
            }
            catch { _target = -1; }
        }

        private static void Finish()
        {
            _target = -1;
            _pausing = true;
            try { Class258.smethod_17((Enum16)2); }
            finally { _pausing = false; }
        }

        /// <summary>A state change the step didn't make (error box, completion, leaving) ends it.</summary>
        public static void OnForeignStateChange() => _target = -1;
    }
}
