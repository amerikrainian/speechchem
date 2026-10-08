using System;
using System.Collections.Generic;
using SpaceChem;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;

namespace SpeechChem.Screens.Common
{
    /// <summary>
    /// The run status every level screen shows (Class709, drawn by the reactor and pipeline
    /// editors) and the pipeline's reactor quota (Class711), read by keys rather than a Tab stop
    /// (user decision 2026-10-07): Ctrl+S score (Cycles, Symbols, Reactors), Ctrl+G progress,
    /// Ctrl+Q quota — each a silent no-op where it does not apply. The run state is not read (the
    /// run's own state changes are spoken). Values are the game's: GoalTracker.score_0 (int_0
    /// cycles, int_1 reactors, int_2 symbols — refreshed every frame by the pipeline editor),
    /// GoalTracker.smethod_1() for the ring (produced over required), Class258 for the run state.
    /// Labels are the panel's own ("Current Progress" — "Control Center" in defense-style levels —
    /// "Cycles", "Symbols", "Reactors", "Reactor Quota").
    /// </summary>
    internal static class LevelStatus
    {
        /// <summary>The keys, for the reactor and pipeline screens' actions.</summary>
        public static IEnumerable<ElementAction> Actions()
        {
            yield return new ElementAction("screen.reactor.score", () => Say(Score()));
            yield return new ElementAction("screen.reactor.progress", () => Say(Progress()));
            yield return new ElementAction("screen.reactor.quota", () => Say(Quota()));
        }

        private static void Say(string text)
        {
            if (!string.IsNullOrEmpty(text)) Speech.Tts.Speak(text, interrupt: true);
        }

        /// <summary>"Running, speed 2" / "Paused" / "Stopped". Speeds are numbered like the play
        /// buttons (1-4), read through the toolbar's own button-to-speed map (Defense remaps).</summary>
        public static string RunState()
        {
            switch ((int)Class258.smethod_16())
            {
                case 1: return Loc.T("run.running", new { n = SpeedNumber(Class258.smethod_14()) });
                case 2: return Loc.T("run.paused");
                default: return Loc.T("run.stopped");
            }
        }

        public static int SpeedNumber(SimulatorSpeed speed)
        {
            var tb = SpaceChem.UI.ToolbarComponent.smethod_0();
            SimulatorSpeed[] buttons = { SimulatorSpeed.Slow, SimulatorSpeed.Medium, SimulatorSpeed.Fast, SimulatorSpeed.WarpSpeed };
            for (int i = 0; i < buttons.Length; i++)
            {
                try { if (tb.method_1(buttons[i]) == speed) return i + 1; }
                catch { }
            }
            return 1;
        }

        /// <summary>"Cycles 120, Symbols 14, Reactors 1" (the panel's order).</summary>
        public static string Score()
            => Loc.T("run.score", new
            {
                cyclesLabel = GameText.T("Cycles"),
                cycles = GoalTracker.score_0.int_0,
                symbolsLabel = GameText.T("Symbols"),
                symbols = GoalTracker.score_0.int_2,
                reactorsLabel = GameText.T("Reactors"),
                reactors = GoalTracker.score_0.int_1,
            });

        public static string Progress()
            => Loc.T("run.progress", new { label = ProgressLabel(), percent = ProgressPercent() });

        /// <summary>The panel's title, as Class709.vmethod_2 picks it: "Current Progress", or in
        /// defense-style levels (GoalTracker.enum93_0 == 2, the sandbox included) "Control Center"
        /// ("The Prometheus" in End of the Line, Class150).</summary>
        internal static string ProgressLabel()
        {
            try
            {
                if ((int)GoalTracker.enum93_0 == 2 && GoalTracker.string_0 != typeof(Class148).smethod_0())
                    return GameText.T(GoalTracker.string_0 == typeof(Class150).smethod_0() ? "The Prometheus" : "Control Center");
            }
            catch { }
            return GameText.T("Current Progress");
        }

        /// <summary>The panel's figure, as Class709.vmethod_2 computes it: the Execution level's own
        /// measure (Class148.method_9), GoalTracker.int_1 percent in defense-style levels, else the
        /// output progress (GoalTracker.smethod_1).</summary>
        private static int ProgressPercent()
        {
            try
            {
                float f = GoalTracker.smethod_1();
                if (GoalTracker.string_0 == typeof(Class148).smethod_0())
                {
                    var execution = Class53.smethod_5<Class148>();
                    if (execution != null) f = execution.method_9();
                }
                else if ((int)GoalTracker.enum93_0 == 2) f = GoalTracker.int_1 / 100f;
                return (int)Math.Round(f * 100f);
            }
            catch { return 0; }
        }

        /// <summary>The reactor quota the pipeline's Class711 draws: "Reactor Quota, 2 of 3"
        /// (", exceeded" when over), or the game's "NO REACTORS REQUIRED". Null in research levels
        /// (a Class84 in the chain: no pipeline is shown, so no quota) — also from inside a reactor
        /// of a pipeline level, where the pipeline editor is lower in the chain.</summary>
        public static string Quota()
        {
            try
            {
                if (Class53.smethod_5<Class84>() != null) return null;
                var p = Class53.smethod_5<SpaceChem.Pipeline.PipelineEditor>()?.pipeline_0;
                if (p == null) return null;
                if (GoalTracker.int_0 == 0) return GameText.Speech(GameText.T("NO\nREACTORS\nREQUIRED"));
                int used = p.method_21();
                string text = Loc.T("pipeline.quota", new { label = GameText.T("Reactor Quota"), used, quota = GoalTracker.int_0 });
                return used > GoalTracker.int_0 ? text + ", " + Loc.T("pipeline.quota.exceeded") : text;
            }
            catch { return null; }
        }
    }
}
