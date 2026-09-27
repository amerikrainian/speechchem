using System.Collections.Generic;
using SpaceChem;
using SpaceChem.Pipeline;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.Patches;
using SpeechChem.Screens.Common;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens.Pipeline
{
    /// <summary>
    /// The pipeline editor (SpaceChem.Pipeline.PipelineEditor, a Class53): production levels, the
    /// ResearchNet production puzzles and the sandbox (a DefenseLevelEditor). A 32 x 22 map of cells
    /// holding components — reactors (4 x 4), fixed inputs, outputs and the recycler, sandbox tanks
    /// and printers — and each output's pipe. Research levels never show it: their pipeline editor
    /// opens its single reactor at once (PipelineEditor.vmethod_2), so they are skipped here.
    ///
    /// Tab stops (user-approved layout, 2026-09-27): Components (initial) → status (the shared
    /// ProgressSection, then the reactor quota) → tools (the shared ToolbarSection) → run log.
    /// Reactors are named "Assembly Reactor 2", numbered in reading order (Game/PipelineText, shared
    /// with the run log). Escape stays native (the exit prompt; the game polls it itself).
    /// </summary>
    public sealed partial class PipelineEditorScreen : Screen
    {
        private const string ComponentsStop = "pipeline.components";
        private const string StatusStop = "pipeline.status";
        private const string ToolsStop = "pipeline.tools";

        public override string Key => "pipeline";
        public override string ScreenName => Loc.T("screen.PipelineEditor");
        public override object InitialFocusStop => ComponentsStop;
        public override bool KeepStateOnPop => _covered;

        /// <summary>The top screen as a pipeline editor outside research levels, or null.</summary>
        internal static PipelineEditor Editor
        {
            get
            {
                var e = GameApi.TopScreen() as PipelineEditor;
                if (e == null || Class53.smethod_5<Class84>() != null) return null;
                return e;
            }
        }

        internal static SpaceChem.Pipeline.Pipeline Model => Editor?.pipeline_0;

        public override bool IsActive() => Model != null;

        public override void Build(GraphBuilder b)
        {
            var editor = Editor;
            var pipeline = editor?.pipeline_0;
            if (pipeline == null) return;
            EnsurePipeline(pipeline);

            BuildComponents(b, pipeline);
            ProgressSection.Build(b, StatusStop, "pipeline.status");
            b.AddItem(ControlId.Structural("pipeline.status.quota"), ProfileUi.Text(true, () => QuotaText(Model)));
            ToolbarSection.Build(b, ToolsStop, "pipeline.tools");
            BuildLog(b);
        }

        public override void OnUpdate()
        {
            var pipeline = Model;
            if (pipeline == null) return;
            EnsurePipeline(pipeline);
            UpdateRunWatch();
        }

        // ---- lifetime: a reactor opened from here, a dialog or Story & Info cover the editor but
        // leave it in the chain, so focus survives them; leaving the level starts over. ----

        private SpaceChem.Pipeline.Pipeline _pipeline;
        private bool _covered;

        private void EnsurePipeline(SpaceChem.Pipeline.Pipeline pipeline)
        {
            if (ReferenceEquals(pipeline, _pipeline)) return;
            _pipeline = pipeline;
        }

        public override void OnPop()
        {
            _covered = false;
            foreach (var s in GameState.ScreenStack())
                if (s is PipelineEditor e && ReferenceEquals(e.pipeline_0, _pipeline)) { _covered = true; return; }
            _pipeline = null;
        }

        // ---- components: every named component in reading order ----

        private void BuildComponents(GraphBuilder b, SpaceChem.Pipeline.Pipeline pipeline)
        {
            b.BeginStop(ComponentsStop);
            var components = PipelineText.Components(pipeline);
            for (int i = 0; i < components.Count; i++)
            {
                var d = components[i].Key;
                int index = i + 1, count = components.Count;
                var vt = new NodeVtable
                {
                    ControlType = d is ReactorDraggable ? ControlTypes.Button : ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => ComponentLabel(Model, d), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => Loc.T("nav.position", new { index, count }), kind: AnnouncementKinds.Position),
                    },
                    SpeaksOwnPosition = true,
                };
                if (d is ReactorDraggable rd) vt.OnActivate = () => OpenReactor(rd);
                // A component with ports is a ROW: Right walks its inputs, then its outputs.
                bool ports = d.class485_0.Count > 0 || d.class485_1.Count > 0;
                if (ports) b.StartRow();
                b.AddItem(ComponentId(d), vt);
                if (ports)
                {
                    BuildPorts(b, d);
                    b.EndRow();
                }
            }
        }

        /// <summary>A stable id per component: its type and top-left cell at build time would move
        /// with it, so the instance's hash is used (components are the same objects between frames).</summary>
        private static ControlId ComponentId(Draggable d)
            => ControlId.Structural("pipeline.comp." + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(d));

        /// <summary>"Assembly Reactor 2, 12, 3" — the name, then its top-left cell.</summary>
        internal static string ComponentLabel(SpaceChem.Pipeline.Pipeline p, Draggable d)
        {
            var at = p?.method_9(d);
            string name = PipelineText.Name(p, d);
            return at.HasValue ? name + ", " + PipelineText.Cell(at.Value) : name;
        }

        /// <summary>The game's double-click on a reactor (ReactorDraggable.vmethod_2 → the reactor
        /// editor, Class77.method_6).</summary>
        private static void OpenReactor(ReactorDraggable rd)
        {
            try { rd.vmethod_2(); }
            catch (System.Exception ex) { Log.Error("[pipeline] open reactor failed", ex); }
        }

        // ---- the reactor quota (Class711): how many reactors against GoalTracker.int_0 ----

        internal static string QuotaText(SpaceChem.Pipeline.Pipeline p)
        {
            if (p == null) return null;
            if (GoalTracker.int_0 == 0) return GameText.Speech(GameText.T("NO\nREACTORS\nREQUIRED"));
            int used = p.method_21();
            string text = Loc.T("pipeline.quota", new { label = GameText.T("Reactor Quota"), used, quota = GoalTracker.int_0 });
            return used > GoalTracker.int_0 ? text + ", " + Loc.T("pipeline.quota.exceeded") : text;
        }

        // ---- the run log, shared with the reactor editor's (Patches/RunCapture) ----

        private const string LogStop = "pipeline.log";

        private readonly WindowedLogView<int> _logView = new WindowedLogView<int>(
            "pipeline.log.", k => k.ToString(), s => { int n; return int.TryParse(s, out n) ? n : (int?)null; });

        private int _logGeneration = -1;

        private void BuildLog(GraphBuilder b)
        {
            _logView.Build(b, LogStop, Loc.T("run.log"), RunCapture.Log,
                cycle => Loc.T("run.cycle", new { n = cycle }),
                (Navigation.Active as GraphNavigator)?.FocusCursorId,
                tag => tag is Reactor.ReactorSnapshot s ? () => PushChild(new Reactor.ReactorSnapshotScreen(s)) : (System.Action)null);
        }

        private void UpdateRunWatch()
        {
            if (_logGeneration == RunCapture.Generation) return;
            _logGeneration = RunCapture.Generation;
            _logView.Reset();
        }
    }
}
