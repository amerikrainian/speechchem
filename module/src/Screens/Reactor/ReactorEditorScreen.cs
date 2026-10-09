using System;
using System.Collections.Generic;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.Screens.Common;
using SpeechChem.UI;
using SpeechChem.UI.Graph;
using ReactorModel = SpaceChem.Reactor.Reactor;

namespace SpeechChem.Screens.Reactor
{
    /// <summary>
    /// The reactor editor — ONE screen for every reactor in the game: the game has a single reactor
    /// editor (Class77) with three variants that only change the zones and side panels (Class78
    /// large output, Class79 single output, Class80 VGM laser), and every reactor type is the same
    /// 10x8 Reactor model with a different allowed-instruction set, pre-placed hardware and zone
    /// layout (GEnum0). Everything here reads those from the live objects, so research, production
    /// and defense reactors all come through this class.
    ///
    /// Tab stops (user-approved layout, 2026-09-27): grid (a 2D cursor; initial) → palette → layer
    /// controls → run and tools (the shared ToolbarSection) → tutorial (only while a tutorial step
    /// is active) → run log (the shared WindowedLogView over Patches/RunCapture; only once a run
    /// has logged events). No status stop: Ctrl+S / Ctrl+G / Ctrl+Q read it (Common/LevelStatus).
    /// No molecules stop: Alt+A / B / P / O read the alpha / beta / psi / omega panels, Alt+Shift
    /// opens their molecule, Ctrl+Shift+P / O edit the output notes (Panels.cs).
    /// Screen keys: C coordinates, Shift+R / Shift+B red / blue waldo (Ctrl+Shift also jumps
    /// the cursor there, Ctrl alone traces its path),
    /// Ctrl+T repeats the tutorial step, L switches the active layer (the game's Tab, which is
    /// navigation here), Shift+Backspace details. The game keeps 1-4, ~ and Space (run controls),
    /// Ctrl+Z / Ctrl+Y and Escape (native exit / stop / deselect).
    /// </summary>
    public sealed partial class ReactorEditorScreen : Screen
    {
        private const string GridStop = "reactor.grid";
        private const string PaletteStop = "reactor.palette";
        private const string LayersStop = "reactor.layers";
        private const string ToolsStop = "reactor.tools";
        private const string TutorialStop = "reactor.tutorial";

        public override string Key => "reactor";
        /// <summary>The reactor's pipeline name ("Assembly Reactor 2", Game/PipelineText) in a
        /// production level, so a switch says which one; "Reactor" in research levels.</summary>
        public override string ScreenName
        {
            get
            {
                try
                {
                    var editor = Editor;
                    var pipeline = Class53.smethod_5<SpaceChem.Pipeline.PipelineEditor>()?.pipeline_0;
                    if (editor != null && pipeline != null && Class53.smethod_5<Class84>() == null)
                        foreach (var rd in PipelineText.Reactors(pipeline))
                            if (ReferenceEquals(rd.class77_0, editor)) return PipelineText.Name(pipeline, rd);
                }
                catch (Exception ex) { Log.Error("[reactor] name failed", ex); }
                return Loc.T("screen.reactor");
            }
        }
        public override object InitialFocusStop => GridStop;

        /// <summary>The top screen as a reactor editor (any variant), or null.</summary>
        internal static Class77 Editor => GameApi.TopScreen() as Class77;

        internal static ReactorModel Model => Editor?.reactor_0;

        public override bool IsActive() => Model != null;

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction("screen.reactor.coords", SpeakCoordinates);
            yield return new ElementAction("screen.reactor.waldo.red", () => SpeakWaldo(red: true, jump: false));
            yield return new ElementAction("screen.reactor.waldo.blue", () => SpeakWaldo(red: false, jump: false));
            yield return new ElementAction("screen.reactor.jump.red", () => SpeakWaldo(red: true, jump: true));
            yield return new ElementAction("screen.reactor.jump.blue", () => SpeakWaldo(red: false, jump: true));
            yield return new ElementAction("screen.reactor.trace.red", () => TraceWaldo(red: true));
            yield return new ElementAction("screen.reactor.trace.blue", () => TraceWaldo(red: false));
            yield return new ElementAction("screen.reactor.cat.prev", () => StepCategory(-1));
            yield return new ElementAction("screen.reactor.cat.next", () => StepCategory(1));
            yield return new ElementAction("screen.reactor.item.prev", () => StepItem(-1));
            yield return new ElementAction("screen.reactor.item.next", () => StepItem(1));
            foreach (var a in LevelStatus.Actions()) yield return a; // Ctrl+S score, Ctrl+G progress, Ctrl+Q quota
            yield return new ElementAction("screen.reactor.tutorial", RepeatTutorial);
            yield return new ElementAction("screen.reactor.layer", ToggleActiveLayer);
            yield return new ElementAction("screen.reactor.molecule", OpenZoneMolecules);
            yield return new ElementAction("screen.reactor.port.alpha", () => SayPort(InputLine(0)));
            yield return new ElementAction("screen.reactor.port.beta", () => SayPort(InputLine(1)));
            yield return new ElementAction("screen.reactor.port.psi", () => SayPort(OutputLine(0)));
            yield return new ElementAction("screen.reactor.port.omega", () => SayPort(OutputLine(1)));
            yield return new ElementAction("screen.reactor.view.alpha", () => OpenPort(true, 0));
            yield return new ElementAction("screen.reactor.view.beta", () => OpenPort(true, 1));
            yield return new ElementAction("screen.reactor.view.psi", () => OpenPort(false, 0));
            yield return new ElementAction("screen.reactor.view.omega", () => OpenPort(false, 1));
            yield return new ElementAction("screen.reactor.note.psi", () => EditNote(0));
            yield return new ElementAction("screen.reactor.note.omega", () => EditNote(1));
            foreach (var a in Patches.StepControl.Actions()) yield return a; // 0, Ctrl+0, 5-9 (Narration/StepKeys)
            foreach (var a in ReactorSwitch.Actions()) yield return a; // Ctrl+Tab, Ctrl+Shift+Tab, Ctrl+1-9
            yield return new ElementAction("screen.reactor.skip.left", () => SkipSideways(-1));
            yield return new ElementAction("screen.reactor.skip.right", () => SkipSideways(1));
            foreach (var a in EditActions()) yield return a;
            // Stop jumps (user request 2026-10-07): Alt+1 grid, Alt+2 palette / Reactor Controls,
            // Alt+3 tools, Alt+4 run log, Alt+5 layers; Alt+Backspace back. Alt+` (the pipeline's
            // table) is not offered here: a no-op.
            yield return new ElementAction("screen.jump.grid", () => JumpToStop(GridStop));
            yield return new ElementAction("screen.jump.place", () => JumpToStop(DefenseText.ControlsShown ? "reactor.controls" : PaletteStop));
            yield return new ElementAction("screen.jump.tools", () => JumpToStop(ToolsStop));
            yield return new ElementAction("screen.jump.log", () => JumpToStop(LogStop));
            yield return new ElementAction("screen.jump.extra", () => JumpToStop(LayersStop));
            yield return new ElementAction("screen.jump.back", () => _jumps.Back(Here()));
            if (_crash != null) yield return new ElementAction(ActionIds.Back, EndCrash); // Escape: the live grid
        }

        private readonly JumpBack _jumps = new JumpBack();

        // The last place focus was seen (each update), for a jump made before focus has resumed —
        // the first frame after a dialog closes.
        private ControlId _hereNode;
        private bool _hereOnGrid;
        private int _hereX, _hereY;

        private void NoteHere()
        {
            var id = Navigation.FocusedNodeId;
            if (id == null) return;
            _hereNode = id;
            _hereOnGrid = OnGrid;
            _hereX = _cursorX;
            _hereY = _cursorY;
        }

        /// <summary>How to come back to where focus is now: the grid cell, or the node; focus not
        /// resumed yet: the last place seen.</summary>
        private Action Here()
        {
            var id = Navigation.FocusedNodeId;
            if (id == null)
            {
                if (_hereOnGrid) { int hx = _hereX, hy = _hereY; return () => FocusCell(hx, hy); }
                var last = _hereNode;
                return last == null ? (Action)null : () => Navigation.FocusNode(last);
            }
            if (OnGrid) { int x = _cursorX, y = _cursorY; return () => FocusCell(x, y); }
            return () => Navigation.FocusNode(id);
        }

        /// <summary>A stop jump key: land on the stop as Tab would (where you last were in it).
        /// Already there, or a stop this screen doesn't show right now: nothing.</summary>
        private void JumpToStop(string stop)
        {
            if (Equals(Navigation.FocusedStopKey, stop)) return;
            _jumps.Remember(Here());
            Navigation.FocusStop(stop);
        }

        public override void Build(GraphBuilder b)
        {
            var editor = Editor;
            var reactor = editor?.reactor_0;
            if (reactor == null) return;
            EnsureReactor(reactor);

            BuildGrid(b, reactor);
            // A defense run shows Reactor Controls where the palette was (Class77.method_5).
            if (DefenseText.ControlsShown) ReactorControlsSection.Build(b, "reactor.controls", "reactor.controls");
            else BuildPalette(b, editor);
            BuildLayers(b, editor);
            ToolbarSection.Build(b, ToolsStop, "reactor.tools");
            BuildTutorial(b, editor);
            BuildLog(b);
        }

        private ReactorModel _reactor;

        /// <summary>Start the trackers over when a different reactor is open. Called from both
        /// OnUpdate and Build (OnUpdate runs first in a frame), so a reset can never land between
        /// the tutorial watch and the next frame's rebuild.</summary>
        private void EnsureReactor(ReactorModel reactor)
        {
            if (!ReferenceEquals(reactor, _reactor)) ResetFor(reactor);
        }

        /// <summary>A different reactor opened (another level, another reactor of a pipeline): start
        /// the cursor and trackers over.</summary>
        private void ResetFor(ReactorModel reactor)
        {
            _reactor = reactor;
            _cursorX = 0;
            _cursorY = 0;
            _zones.Reset();
            _lastStep = null;
            _armedKey = -1;
            _pendingJump = null;
            _jumps.Clear();
            _crash = null;
            _category = _item = -1;
            ResetEditState();
        }

        public override void OnUpdate()
        {
            var editor = Editor;
            if (editor?.reactor_0 == null) return;
            // Another reactor replaced this one with the screen still on top (Ctrl+Tab / Ctrl+1-9,
            // Common/ReactorSwitch, or a pause instruction switching to its reactor): arrive afresh,
            // as from the pipeline — its name, then the grid's first landing.
            bool swapped = _reactor != null && !ReferenceEquals(editor.reactor_0, _reactor);
            EnsureReactor(editor.reactor_0);
            if (swapped)
            {
                Navigation.Attach(null);
                Navigation.ScreenClosed(this);
                Navigation.Attach(this);
                OnFocus();
            }
            WatchCrash(editor); // the crash overlay (Crash.cs)
            WatchInvalidMolecule(editor);
            TrackCursor();
            WatchTutorial(editor);
            UpdateRunWatch(editor);
            ApplyPendingJump();
            SpeakLevelStart(editor);
            NoteHere();
        }

        // ---- level start (user request 2026-10-06): opening a research level speaks the Molecules
        // stop's lines, one utterance each, queued after the screen name and the focused cell — the
        // navigator reads the cell at the end of the focus frame, so the lines wait a frame more.
        // Once per level instance (Class83): closing Story & Info or a dialog doesn't repeat them;
        // leaving and reopening the level does. Production reactors open from the pipeline mid-level,
        // so they don't. ----

        private static Class83 _startSpokenFor;
        private int _startFrames;

        public override void OnFocus()
        {
            base.OnFocus();
            _startFrames = 2;
        }

        private void SpeakLevelStart(Class77 editor)
        {
            if (_startFrames == 0 || --_startFrames > 0) return;
            var level = Class53.smethod_5<Class83>();
            if (level == null || ReferenceEquals(level, _startSpokenFor) || Class53.smethod_5<Class84>() == null) return;
            _startSpokenFor = level;
            foreach (var line in MoleculeLines(editor)) Speech.Tts.Speak(line);
        }

        /// <summary>A game screen over the reactor (a Reaction Error, the exit prompt, the periodic
        /// table, Story &amp; Info) pops this screen too, but the editor stays in the chain beneath it:
        /// keep the cursor, trackers and focus so closing it lands back where the player was (the
        /// grid cell, or the toolbar button that opened it). Leaving the reactor starts over.</summary>
        public override void OnPop()
        {
            _covered = false;
            foreach (var s in GameState.ScreenStack())
                if (s is Class77 e && ReferenceEquals(e.reactor_0, _reactor)) { _covered = true; return; }
            _reactor = null;
        }

        private bool _covered;

        public override bool KeepStateOnPop => _covered;

        /// <summary>The covering screen can lead out of the level instead (the completion screen's
        /// Continue): the kept focus belongs to a reactor that is gone, so a different one starts
        /// over on the grid.</summary>
        public override void OnPush()
        {
            if (!_covered || ReferenceEquals(Model, _reactor)) return;
            _covered = false;
            Navigation.ScreenClosed(this);
        }
    }
}
