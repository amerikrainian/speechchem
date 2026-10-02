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
    /// controls → run and tools (the shared ToolbarSection) → molecules (input/output panels) →
    /// status (the shared ProgressSection) → tutorial (only while a tutorial step is active) → run
    /// log (the shared WindowedLogView over Patches/RunCapture; only once a run has logged events).
    /// Screen keys: C coordinates, Shift+R / Shift+B red / blue waldo (Ctrl+Shift also jumps
    /// the cursor there, Ctrl alone traces its path), P status,
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
        private const string MoleculesStop = "reactor.molecules";
        private const string StatusStop = "reactor.status";
        private const string TutorialStop = "reactor.tutorial";

        public override string Key => "reactor";
        public override string ScreenName => Loc.T("screen.reactor");
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
            yield return new ElementAction("screen.reactor.status", () => Speech.Tts.Speak(ProgressSection.Summary(), interrupt: true));
            yield return new ElementAction("screen.reactor.tutorial", RepeatTutorial);
            yield return new ElementAction("screen.reactor.layer", ToggleActiveLayer);
            yield return new ElementAction("screen.reactor.molecule", OpenZoneMolecules);
            yield return new ElementAction("screen.reactor.step", Patches.StepControl.Step);
            yield return new ElementAction("screen.reactor.skip.left", () => SkipSideways(-1));
            yield return new ElementAction("screen.reactor.skip.right", () => SkipSideways(1));
            foreach (var a in EditActions()) yield return a;
        }

        public override void Build(GraphBuilder b)
        {
            var editor = Editor;
            var reactor = editor?.reactor_0;
            if (reactor == null) return;
            EnsureReactor(reactor);

            BuildGrid(b, reactor);
            BuildPalette(b, editor);
            BuildLayers(b, editor);
            ToolbarSection.Build(b, ToolsStop, "reactor.tools");
            BuildMolecules(b, editor);
            ProgressSection.Build(b, StatusStop, "reactor.status");
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
            _lastZone = null;
            _zoneFor = null;
            _zoneInit = false;
            _lastStep = null;
            _armedKey = -1;
            _pendingJump = null;
            _category = _item = -1;
            ResetEditState();
        }

        public override void OnUpdate()
        {
            var editor = Editor;
            if (editor?.reactor_0 == null) return;
            EnsureReactor(editor.reactor_0);
            TrackCursor();
            WatchTutorial(editor);
            UpdateRunWatch(editor);
            ApplyPendingJump();
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
