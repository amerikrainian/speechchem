using SpeechChem.Localization;
using SpeechChem.Patches;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens.Reactor
{
    public sealed partial class ReactorEditorScreen
    {
        // ---- the run log: every event of the current / last run (Patches/RunCapture), one region
        // per cycle, through the shared WindowedLogView (virtually limitless store, windowed graph,
        // tail-following while focus is elsewhere). The last Tab stop; absent while empty. ----

        private const string LogStop = "reactor.log";

        private readonly WindowedLogView<int> _logView = new WindowedLogView<int>(
            "reactor.log.", k => k.ToString(), s => { int n; return int.TryParse(s, out n) ? n : (int?)null; });

        private int _logGeneration = -1;

        private void BuildLog(GraphBuilder b)
        {
            // This reactor's own entries read without their "reactor 2, " (user rule 2026-10-01:
            // the number only names the OTHER reactors); the pipeline's view keeps every number.
            string own = RunCapture.ReactorName(Model);
            string prefix = own == null ? null : own + ", ";
            _logView.Build(b, LogStop, Loc.T("run.log"), RunCapture.Log,
                cycle => Loc.T("run.cycle", new { n = cycle }),
                (Navigation.Active as GraphNavigator)?.FocusCursorId,
                // A reaction error's entry opens the crash snapshot (Enter).
                tag => tag is ReactorSnapshot s ? () => PushChild(new ReactorSnapshotScreen(s)) : (System.Action)null,
                prefix == null ? (System.Func<string, string>)null
                    : text => text.StartsWith(prefix, System.StringComparison.Ordinal) ? text.Substring(prefix.Length) : text);
        }

        /// <summary>A new run cleared the store: the window goes back to following the tail.</summary>
        private void UpdateRunWatch(Class77 editor)
        {
            if (_logGeneration == RunCapture.Generation) return;
            _logGeneration = RunCapture.Generation;
            _logView.Reset();
        }
    }
}
