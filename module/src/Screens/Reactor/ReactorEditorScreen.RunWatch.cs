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
            // Entries are events, formatted for this view: this reactor's own read without their
            // "reactor 2" (user rule 2026-10-01; the reactor part's "unnamed" variant), the log
            // format re-applied so a settings change re-renders old entries.
            var reactor = Model;
            _logView.Build(b, LogStop, Loc.T("run.log"), RunCapture.Log,
                cycle => Loc.T("run.cycle", new { n = cycle }),
                (Navigation.Active as GraphNavigator)?.FocusCursorId,
                // A reaction error's entry opens the crash snapshot (Enter).
                tag => tag is Narration.NarrationEvent e && e.Payload is ReactorSnapshot s ? () => PushChild(new ReactorSnapshotScreen(s)) : (System.Action)null,
                (text, tag) => tag is Narration.NarrationEvent e ? Narration.Formatter.Format(e, Narration.FormatLayer.Log, reactor) : text);
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
