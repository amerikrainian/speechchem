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
    /// Tab stops (user-approved layout, 2026-09-27): Components (initial) → map → (defense levels:
    /// the enemy) → shelf (a defense run: Reactor Controls in its place) → tools (the shared
    /// ToolbarSection) → run log. No status stop: Ctrl+S / Ctrl+G / Ctrl+Q read it (Common/LevelStatus).
    /// Reactors are named "Assembly Reactor 2", numbered in reading order (Game/PipelineText, shared
    /// with the run log). Escape stays native (the exit prompt; the game polls it itself).
    /// </summary>
    public sealed partial class PipelineEditorScreen : Screen
    {
        private const string ComponentsStop = "pipeline.components";
        private const string ToolsStop = "pipeline.tools";
        private const string ControlsStop = "pipeline.controls";

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

        public override IEnumerable<ElementAction> GetActions()
        {
            // The reactor editor's bindings: C, [ ], , .
            yield return new ElementAction("screen.reactor.coords", SpeakCoordinates);
            yield return new ElementAction("screen.reactor.cat.prev", () => StepCategory(-1));
            yield return new ElementAction("screen.reactor.cat.next", () => StepCategory(1));
            yield return new ElementAction("screen.reactor.item.prev", () => StepItem(-1));
            yield return new ElementAction("screen.reactor.item.next", () => StepItem(1));
            yield return new ElementAction("screen.reactor.cut", Cut);
            yield return new ElementAction("screen.reactor.copy", Copy);
            yield return new ElementAction("screen.reactor.paste", Paste);
            yield return new ElementAction("screen.reactor.delete", () =>
            {
                var design = FocusedDesign();
                if (design != null) DeleteDesign(design);
                else if (CursorInRectangle) DeleteMarked();
                else if (Zoomed && OnMap) DeleteBlock();
                else Delete(FocusedComponent());
            });
            yield return new ElementAction("screen.reactor.status", SpeakPipeStatus); // P: the drawn pipe, or the pipe under the cursor
            yield return new ElementAction("screen.pipeline.zoom.out", () => ZoomStep(1)); // Zoom.cs
            yield return new ElementAction("screen.pipeline.zoom.in", () => ZoomStep(-1));
            yield return new ElementAction("screen.pipeline.zoom.max", () => SetZoom(8));
            yield return new ElementAction("screen.pipeline.zoom.reset", () => SetZoom(1));
            foreach (var a in LevelStatus.Actions()) yield return a; // Ctrl+S score, Ctrl+G progress, Ctrl+Q quota
            yield return new ElementAction("screen.reactor.molecule", OpenFocusedMolecules); // M on a port cell
            foreach (var a in Patches.StepControl.Actions()) yield return a; // 0, Ctrl+0, 5-9, Ctrl+1-9 (Narration/StepKeys)
            yield return new ElementAction("screen.reactor.skip.left", () => SkipMapSideways(-1));
            yield return new ElementAction("screen.reactor.skip.right", () => SkipMapSideways(1));
            yield return new ElementAction("screen.reactor.mark", MarkCorner); // the marked rectangle (Mark.cs)
            yield return new ElementAction("screen.reactor.unmark", ClearMark);
            yield return new ElementAction("screen.reactor.markall", MarkAll);
            // Stop jumps (user request 2026-10-07): Alt+1 map (from a Components row: that
            // building's top-left cell), Alt+` the Components table (from the map on a building:
            // its row), Alt+2 shelf / Reactor Controls, Alt+3 tools, Alt+4 run log, Alt+5 the enemy;
            // Alt+Backspace back.
            yield return new ElementAction("screen.jump.grid", JumpToMap);
            yield return new ElementAction("screen.jump.table", JumpToTable);
            yield return new ElementAction("screen.jump.place", () => JumpToStop(DefenseText.ControlsShown ? ControlsStop : ShelfStop));
            yield return new ElementAction("screen.jump.tools", () => JumpToStop(ToolsStop));
            yield return new ElementAction("screen.jump.log", () => JumpToStop(LogStop));
            yield return new ElementAction("screen.jump.extra", () => JumpToStop(EnemyStop));
            yield return new ElementAction("screen.jump.back", () => _jumps.Back(Here()));
            if (_drawPipe != null) yield return new ElementAction(ActionIds.Back, () => EndDraw());
            else if (_armed != null) yield return new ElementAction(ActionIds.Back, Unarm);
        }

        public override void Build(GraphBuilder b)
        {
            var editor = Editor;
            var pipeline = editor?.pipeline_0;
            if (pipeline == null) return;
            EnsurePipeline(pipeline);

            BuildComponents(b, pipeline);
            BuildMap(b, pipeline);
            BuildEnemy(b);
            // A defense run shows Reactor Controls where the shelf was (PipelineEditor.method_5).
            if (DefenseText.ControlsShown) ReactorControlsSection.Build(b, ControlsStop, "pipeline.controls");
            else BuildShelf(b, editor);
            ToolbarSection.Build(b, ToolsStop, "pipeline.tools");
            BuildLog(b);
        }

        public override void OnUpdate()
        {
            var pipeline = Model;
            if (pipeline == null) return;
            EnsurePipeline(pipeline);
            PipelineText.Sync(pipeline); // every frame: a delete and a later placement are never seen together
            if (_deleteFocus != null && ActiveChild == null)
            {
                Navigation.FocusNode(_deleteFocus); // a menu Delete's landing, once the menu has closed
                _deleteFocus = null;
            }
            TrackMapCursor();
            // A Reaction Error box that closed over the pipeline: no reactor grid to show it on (the
            // game opens the failing reactor before the box, so this is a safety net).
            if (Reactor.ReactorEditorScreen.PendingCrash != null && !Running) Reactor.ReactorEditorScreen.PendingCrash = null;
            // An invalid-molecule box closed (it stops the run): land on the input that refused it.
            // At once, before focus resumes: the old cell's re-announcement is never heard (user
            // rule 2026-10-07); Alt+Backspace returns to the last place seen before the box (Here).
            if (Patches.DialogCapture.InvalidAt != null && !Running && ActiveChild == null)
            {
                var cell = Patches.DialogCapture.InvalidCell;
                Patches.DialogCapture.InvalidAt = null;
                var size = pipeline.method_4();
                if (cell.int_0 >= 0 && cell.int_1 >= 0 && cell.int_0 < size.int_0 && cell.int_1 < size.int_1)
                {
                    _jumps.Remember(Here());
                    FocusMapCell(cell.int_0, cell.int_1);
                }
            }
            UpdateDraw();
            UpdateRunWatch();
            NoteHere();
        }

        // ---- lifetime: a reactor opened from here, a dialog or Story & Info cover the editor but
        // leave it in the chain, so focus survives them; leaving the level starts over. ----

        private SpaceChem.Pipeline.Pipeline _pipeline;
        private bool _covered;
        private ControlId _deleteFocus;

        /// <summary>A different pipeline after a covered pop (the level was left from above): the
        /// kept focus is stale, start over.</summary>
        public override void OnPush()
        {
            if (!_covered || ReferenceEquals(Editor?.pipeline_0, _pipeline)) return;
            _covered = false;
            Navigation.ScreenClosed(this);
        }

        private void EnsurePipeline(SpaceChem.Pipeline.Pipeline pipeline)
        {
            if (ReferenceEquals(pipeline, _pipeline)) return;
            _pipeline = pipeline;
            _cursorX = _cursorY = 0;
            _category = _item = -1;
            _armed = null;
            _armedSticky = false;
            _clip.Clear();
            DropMark();
            _copy = false;
            _designs = null;
            _drawPipe = null;
            _dragOpen = false;
            _zoom = 1;
            _jumps.Clear();
        }

        // ---- stop jumps and the way back ----

        private readonly JumpBack _jumps = new JumpBack();

        // The last place focus was seen (each update), for a jump made before focus has resumed —
        // the first frame after a dialog closes.
        private ControlId _hereNode;
        private bool _hereOnMap;
        private int _hereX, _hereY;

        private void NoteHere()
        {
            var id = Navigation.FocusedNodeId;
            if (id == null) return;
            _hereNode = id;
            _hereOnMap = OnMap;
            _hereX = _cursorX;
            _hereY = _cursorY;
        }

        /// <summary>How to come back to where focus is now: the map cell (at any zoom), or the node;
        /// focus not resumed yet: the last place seen.</summary>
        private System.Action Here()
        {
            var id = Navigation.FocusedNodeId;
            if (id == null)
            {
                if (_hereOnMap) { int hx = _hereX, hy = _hereY; return () => FocusMapCell(hx, hy); }
                var last = _hereNode;
                return last == null ? (System.Action)null : () => Navigation.FocusNode(last);
            }
            if (OnMap) { int x = _cursorX, y = _cursorY; return () => FocusMapCell(x, y); }
            return () => Navigation.FocusNode(id);
        }

        /// <summary>Land on a stop as Tab would. Already there, or a stop not shown now: nothing.</summary>
        private void JumpToStop(string stop)
        {
            if (Equals(Navigation.FocusedStopKey, stop)) return;
            _jumps.Remember(Here());
            Navigation.FocusStop(stop);
        }

        /// <summary>Alt+1: the map; from a Components row, that building's top-left cell.</summary>
        private void JumpToMap()
        {
            var p = Model;
            if (p == null || OnMap) return;
            var d = ComponentsStop.Equals(Navigation.FocusedStopKey) ? FocusedComponent() : null;
            var at = d == null ? null : p.method_9(d);
            if (!at.HasValue) { JumpToStop(MapStop); return; }
            _jumps.Remember(Here());
            FocusMapCell(at.Value.int_0, at.Value.int_1);
        }

        /// <summary>Alt+`: the Components table; from the map on a building (cell cursor), its row.</summary>
        private void JumpToTable()
        {
            var p = Model;
            if (p == null || ComponentsStop.Equals(Navigation.FocusedStopKey)) return;
            var d = OnMap && !Zoomed ? BuildingAt(p, new Impeller.Vector2i(_cursorX, _cursorY)) : null;
            if (d == null || !PipelineText.Components(p).Exists(kv => ReferenceEquals(kv.Key, d))) { JumpToStop(ComponentsStop); return; }
            _jumps.Remember(Here());
            Navigation.FocusNode(ComponentId(d));
        }

        public override void OnPop()
        {
            CloseDrag(); // a covered pipeline keeps drawing for the return (user rule 2026-10-07)
            _covered = false;
            foreach (var s in GameState.ScreenStack())
                if (s is PipelineEditor e && ReferenceEquals(e.pipeline_0, _pipeline)) { _covered = true; return; }
            EndDraw(quiet: true);
            _pipeline = null;
        }

        // ---- components: a TABLE (user design, 2026-10-02, the Echopunks ColumnGrid): one row per
        // named component in reading order; columns Component, then one per input and one per
        // output, as many as the component with the most has. A component without that port reads
        // "N/A". The column header is spoken when focus crosses into a column, never while it stays
        // there. Up/Down walk a column (the same port of the next component), Left/Right cross. ----

        private void BuildComponents(GraphBuilder b, SpaceChem.Pipeline.Pipeline pipeline)
        {
            b.BeginStop(ComponentsStop);
            _panels.Clear();
            var components = PipelineText.Components(pipeline);
            int inputs = 0, outputs = 0;
            foreach (var kv in components)
            {
                inputs = System.Math.Max(inputs, kv.Key.class485_0.Count);
                outputs = System.Math.Max(outputs, kv.Key.class485_1.Count);
            }
            var names = new ColumnGrid.Column { Header = Loc.T("pipeline.col.component"), ContextId = ControlId.Structural("pipeline.col.comp") };
            var ins = new ColumnGrid.Column[inputs];
            for (int i = 0; i < inputs; i++)
                ins[i] = new ColumnGrid.Column
                {
                    Header = inputs > 1 ? Loc.T("pipeline.input.n", new { n = i + 1 }) : Loc.T("pipeline.input"),
                    ContextId = ControlId.Structural("pipeline.col.in." + i),
                };
            var outs = new ColumnGrid.Column[outputs];
            for (int i = 0; i < outputs; i++)
                outs[i] = new ColumnGrid.Column
                {
                    Header = outputs > 1 ? Loc.T("pipeline.output.n", new { n = i + 1 }) : Loc.T("pipeline.output"),
                    ContextId = ControlId.Structural("pipeline.col.out." + i),
                };

            foreach (var kv in components)
            {
                var d = kv.Key;
                names.Cells.Add(new ColumnGrid.Cell { Id = ComponentId(d), Vtable = ComponentCell(d) });
                var dIns = new List<PipelineInput>();
                foreach (var p in d.class485_0) dIns.Add(p.Value);
                var dOuts = new List<PipelineOutput>();
                foreach (var p in d.class485_1) dOuts.Add(p.Value);
                for (int i = 0; i < inputs; i++)
                    ins[i].Cells.Add(new ColumnGrid.Cell
                    {
                        Id = PortId(d, false, i),
                        Vtable = i < dIns.Count ? InputCell(d, dIns[i], i) : NaCell(),
                    });
                for (int i = 0; i < outputs; i++)
                    outs[i].Cells.Add(new ColumnGrid.Cell
                    {
                        Id = PortId(d, true, i),
                        Vtable = i < dOuts.Count ? OutputCell(d, dOuts[i], i) : NaCell(),
                    });
            }

            var columns = new List<ColumnGrid.Column> { names };
            columns.AddRange(ins);
            columns.AddRange(outs);
            ColumnGrid.Edges edges;
            ColumnGrid.Build(b, columns, out edges, Loc.T("role.table"), ControlId.Structural("pipeline.comp.table"));
        }

        /// <summary>The Component column's cell: Enter opens a reactor (the double-click) or jumps
        /// to any other building's top-left cell on the map.</summary>
        private NodeVtable ComponentCell(Draggable d)
        {
            var vt = Cell(() => ComponentLabel(Model, d));
            vt.ControlType = d is ReactorDraggable ? ControlTypes.Button : ControlTypes.Text;
            if (d is ReactorDraggable rd) vt.OnActivate = () => OpenReactor(rd);
            else vt.OnActivate = () => JumpToComponent(d);
            vt.OnTooltip = () => SpeakTooltip(d);
            return vt;
        }

        private NodeVtable NaCell() => Cell(() => Loc.T("text.na"));

        private void JumpToComponent(Draggable d)
        {
            var at = Model?.method_9(d);
            if (!at.HasValue) return;
            _jumps.Remember(Here());
            FocusMapCell(at.Value.int_0, at.Value.int_1);
        }

        /// <summary>A stable id per component: its type and top-left cell at build time would move
        /// with it, so the instance's hash is used (components are the same objects between frames).</summary>
        private static ControlId ComponentId(Draggable d)
            => ControlId.Structural("pipeline.comp." + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(d));

        /// <summary>"Assembly Reactor 2, 12, 3" — the name, then its top-left cell; during a run a
        /// reactor adds its waldos' waiting text, as its thumbnail shows it (Class188.method_2).</summary>
        internal static string ComponentLabel(SpaceChem.Pipeline.Pipeline p, Draggable d)
        {
            var at = p?.method_9(d);
            string name = PipelineText.Name(p, d);
            string text = at.HasValue ? name + ", " + PipelineText.Cell(at.Value) : name;
            string meter = DefenseText.Meter(d); // a building's run meter ("Pressure 12 of 35")
            if (meter != null) text += ", " + meter;
            if ((int)Class258.smethod_16() != 0 && d is ReactorDraggable rd && rd.class77_0?.reactor_0 != null)
            {
                var waldos = rd.class77_0.reactor_0.dictionary_2;
                foreach (var colour in new[] { Enum114.Alpha, Enum114.Beta })
                {
                    if (!waldos.ContainsKey(colour)) continue;
                    string status = waldos[colour].method_2();
                    if (!string.IsNullOrEmpty(status))
                        text += ", " + Loc.T(colour == Enum114.Alpha ? "reactor.red" : "reactor.blue") + " " + GameText.Speech(status);
                }
            }
            return text;
        }

        /// <summary>The game's double-click on a reactor (ReactorDraggable.vmethod_2 → the reactor
        /// editor, Class77.method_6).</summary>
        private static void OpenReactor(ReactorDraggable rd)
        {
            try { rd.vmethod_2(); }
            catch (System.Exception ex) { Log.Error("[pipeline] open reactor failed", ex); }
        }

        // ---- the run log, shared with the reactor editor's (Patches/RunCapture) ----

        private const string LogStop = "pipeline.log";

        private readonly WindowedLogView<int, Narration.NarrationEvent> _logView = new WindowedLogView<int, Narration.NarrationEvent>(
            "pipeline.log.", k => k.ToString(), s => { int n; return int.TryParse(s, out n) ? n : (int?)null; });

        private int _logGeneration = -1;

        private void BuildLog(GraphBuilder b)
        {
            _logView.Build(b, LogStop, Loc.T("run.log"), RunCapture.Log,
                cycle => Loc.T("run.cycle", new { n = cycle }),
                (Navigation.Active as GraphNavigator)?.FocusCursorId,
                e => e.Payload is Reactor.ReactorSnapshot s ? () => PushChild(new Reactor.ReactorSnapshotScreen(s)) : (System.Action)null,
                // Entries are events: the log format, every reactor named (this is no reactor's view).
                e => Narration.Formatter.Format(e, Narration.FormatLayer.Log, null),
                Narration.NarrationStore.Revision);
        }

        private void UpdateRunWatch()
        {
            if (_logGeneration == RunCapture.Generation) return;
            _logGeneration = RunCapture.Generation;
            _logView.Reset();
        }
    }
}
