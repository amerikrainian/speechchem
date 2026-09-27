using System;
using SpaceChem;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens.Common
{
    /// <summary>
    /// The run status every level screen shows (Class709, drawn by the reactor and pipeline
    /// editors): run state, Cycles, Symbols, Reactors and the progress ring, as a reusable Tab stop
    /// and a one-shot readout (the P key). Values are the game's: GoalTracker.score_0 (int_0
    /// cycles, int_1 reactors, int_2 symbols — refreshed every frame by the pipeline editor),
    /// GoalTracker.smethod_1() for the ring (produced over required), Class258 for the run state.
    /// Labels are the panel's own ("Current Progress", "Cycles", "Symbols", "Reactors").
    /// </summary>
    internal static class ProgressSection
    {
        public static void Build(GraphBuilder b, string stopKey, string idPrefix)
        {
            b.BeginStop(stopKey);
            Row(b, idPrefix + ".state", RunState);
            Row(b, idPrefix + ".cycles", () => GameText.T("Cycles") + " " + GoalTracker.score_0.int_0);
            Row(b, idPrefix + ".symbols", () => GameText.T("Symbols") + " " + GoalTracker.score_0.int_2);
            Row(b, idPrefix + ".reactors", () => GameText.T("Reactors") + " " + GoalTracker.score_0.int_1);
            // The one LIVE row (user rule): while focused, each change of the percentage is spoken
            // on its own ("20 percent") as a run produces. The other rows only read on arrival.
            b.AddItem(ControlId.Structural(idPrefix + ".progress"), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => GameText.T("Current Progress"), kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => Loc.T("run.percent", new { percent = ProgressPercent() }), live: true, kind: AnnouncementKinds.Value),
                },
            });
        }

        private static void Row(GraphBuilder b, string id, Func<string> text)
            => b.AddItem(ControlId.Structural(id), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[] { new NodeAnnouncement(text, kind: AnnouncementKinds.Label) },
            });

        /// <summary>"Stopped" / "Running, speed 2" / "Paused". Speeds are numbered like the play
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

        public static string Progress()
            => Loc.T("run.progress", new { label = GameText.T("Current Progress"), percent = ProgressPercent() });

        private static int ProgressPercent()
        {
            try { return (int)Math.Round(GoalTracker.smethod_1() * 100f); }
            catch { return 0; }
        }

        /// <summary>The whole panel in one line, for the status key.</summary>
        public static string Summary()
            => Loc.T("run.summary", new
            {
                state = RunState(),
                cyclesLabel = GameText.T("Cycles"),
                cycles = GoalTracker.score_0.int_0,
                symbolsLabel = GameText.T("Symbols"),
                symbols = GoalTracker.score_0.int_2,
                reactorsLabel = GameText.T("Reactors"),
                reactors = GoalTracker.score_0.int_1,
                progress = Progress(),
            });
    }
}
