using System;
using SpeechChem.Localization;
using SpeechChem.Modularity;
using HarmonyLib;

namespace SpeechChem
{
    /// <summary>
    /// The module entry point — what the host's ModuleLoader instantiates. Composition root for all
    /// reloadable features: localization first (everything speakable depends on it), then input, the
    /// screen stack and the FrameLoop steps; the module's own Harmony patches (per-load unique id — see
    /// IModModule) arm on the first tick. All statics in this assembly are per-load — a reload starts
    /// this whole half of the mod cold, re-deriving everything from the live game (and re-reading the
    /// locale files).
    /// </summary>
    public sealed class SpeechChemModule : IModModule
    {
        private ModHost _host;
        private Harmony _harmony;

        public void Load(ModHost host)
        {
            _host = host;
            LocalizationManager.Initialize();

            // The announcer's localized wording hooks (the graph core itself never touches Loc).
            UI.Graph.GraphAnnouncer.PositionText = (index, count) => Loc.T("nav.position", new { index, count });
            UI.Graph.GraphAnnouncer.ExpandedStateText = e => Loc.T(e ? "role.expanded" : "role.collapsed");

            // Module-owned patches: per-load UNIQUE id so THIS load's Dispose unpatches exactly these
            // (the host loads the new module before disposing the old — a fixed id would let the old
            // teardown strip the fresh patches; see IModModule). Nothing is patched here: see Tick.
            _harmony = new Harmony("com.speechchem.module." + Guid.NewGuid().ToString("N"));

            RegisterInput();
            Screens.ScreenManager.Initialize();

            // Per-frame steps, in the order they run: one keyboard snapshot, then input dispatch, then
            // the screen stack (which drives the navigator), then the synthetic-click release. The
            // Escape latch sits between the snapshot and dispatch: it must see the modal still open.
            // (The dev pump is host-side, first.)
            FrameLoop.Register("keyboard", Input.SdlKeyboard.Update);
            FrameLoop.Register("escape", Patches.GameKeySuppression.LatchEscape);
            FrameLoop.Register("input", Input.InputManager.Tick);
            FrameLoop.Register("loc", LocalizationManager.Tick);
            FrameLoop.Register("screens", Screens.ScreenManager.Tick);
            FrameLoop.Register("click", Game.SyntheticClick.Tick);
            FrameLoop.Register("step", Patches.StepControl.Tick); // the step's safety net
            FrameLoop.Register("runlevel", Patches.RunCapture.SyncLevel); // a new level starts an empty run log

            // One greeting per game launch (the module loads while the boot splash plays). A hot reload
            // mid-session doesn't re-greet.
            if (host.ModuleGeneration == 1)
            {
                Speech.Tts.Speak(Loc.T("app.ready"));
                // The launch update check: one background request per game launch.
                _updateCheck = new Update.UpdateChecker();
                _updateCheck.Start(Update.UpdateChecker.LocalVersion());
            }
        }

        // Its line is spoken from Tick when the request lands with a release strictly newer than
        // the running build; every other outcome is a log line only.
        private Update.UpdateChecker _updateCheck;
        private bool _updateAnnounced;

        /// <summary>The nav action set (the WrathAccess ui.* vocabulary GraphNavigator dispatches on)
        /// and the input hooks. Bindings are the standard screen-reader set; rebinding UI comes with
        /// the settings tree. Screen-scoped actions use the "screen." prefix (GraphNavigator routes
        /// those to the focused screen's GetActions).</summary>
        private static void RegisterInput()
        {
            Input.InputManager.Register("ui.up", "Up", Input.InputCategory.UI).AddBinding(Input.Scancode.Up).Repeating();
            Input.InputManager.Register("ui.down", "Down", Input.InputCategory.UI).AddBinding(Input.Scancode.Down).Repeating();
            Input.InputManager.Register("ui.left", "Left", Input.InputCategory.UI).AddBinding(Input.Scancode.Left).Repeating();
            Input.InputManager.Register("ui.right", "Right", Input.InputCategory.UI).AddBinding(Input.Scancode.Right).Repeating();
            Input.InputManager.Register("ui.next", "Next group", Input.InputCategory.UI).AddBinding(Input.Scancode.Tab).Repeating();
            Input.InputManager.Register("ui.prev", "Previous group", Input.InputCategory.UI).AddBinding(Input.Scancode.Tab, shift: true).Repeating();
            Input.InputManager.Register("ui.home", "First item", Input.InputCategory.UI).AddBinding(Input.Scancode.Home);
            Input.InputManager.Register("ui.end", "Last item", Input.InputCategory.UI).AddBinding(Input.Scancode.End);
            Input.InputManager.Register("ui.pageUp", "Adjust up, large step", Input.InputCategory.UI).AddBinding(Input.Scancode.PageUp).Repeating();
            Input.InputManager.Register("ui.pageDown", "Adjust down, large step", Input.InputCategory.UI).AddBinding(Input.Scancode.PageDown).Repeating();
            Input.InputManager.Register("ui.activate", "Activate", Input.InputCategory.UI)
                .AddBinding(Input.Scancode.Return).AddBinding(Input.Scancode.KpEnter);
            Input.InputManager.Register("ui.secondary", "Secondary action", Input.InputCategory.UI).AddBinding(Input.Scancode.Backspace);
            // Details (the mouse-over text) on Shift+Backspace: plain Backspace stays the secondary
            // action, and Space belongs to the game (pause/resume in the reactor) — user rule, 2026-09-27.
            Input.InputManager.Register("ui.tooltip", "Read details", Input.InputCategory.UI).AddBinding(Input.Scancode.Backspace, shift: true);
            Input.InputManager.Register("ui.back", "Back", Input.InputCategory.UI).AddBinding(Input.Scancode.Escape);
            Input.InputManager.Register("ui.echo", "Toggle typing echo", Input.InputCategory.UI).AddBinding(Input.Scancode.F6);
            Input.InputManager.Register("ui.regionPrev", "Previous region", Input.InputCategory.UI).AddBinding(Input.Scancode.Up, ctrl: true).Repeating();
            Input.InputManager.Register("ui.regionNext", "Next region", Input.InputCategory.UI).AddBinding(Input.Scancode.Down, ctrl: true).Repeating();

            // Reactor editor (screen-scoped; user-approved keys, 2026-09-27).
            Input.InputManager.Register("screen.reactor.coords", "Read coordinates", Input.InputCategory.UI).AddBinding(Input.Scancode.C);
            // Waldos on R / B under modifiers (user rule, 2026-09-27): the bare letters stay the game's
            // own palette hotkeys (R = Input); the game gives Ctrl only V / Y / Z.
            Input.InputManager.Register("screen.reactor.waldo.red", "Red waldo", Input.InputCategory.UI).AddBinding(Input.Scancode.R, shift: true);
            Input.InputManager.Register("screen.reactor.waldo.blue", "Blue waldo", Input.InputCategory.UI).AddBinding(Input.Scancode.B, shift: true);
            Input.InputManager.Register("screen.reactor.jump.red", "Jump to red waldo", Input.InputCategory.UI).AddBinding(Input.Scancode.R, ctrl: true, shift: true);
            Input.InputManager.Register("screen.reactor.jump.blue", "Jump to blue waldo", Input.InputCategory.UI).AddBinding(Input.Scancode.B, ctrl: true, shift: true);
            Input.InputManager.Register("screen.reactor.trace.red", "Trace red waldo path", Input.InputCategory.UI).AddBinding(Input.Scancode.R, ctrl: true);
            Input.InputManager.Register("screen.reactor.trace.blue", "Trace blue waldo path", Input.InputCategory.UI).AddBinding(Input.Scancode.B, ctrl: true);
            Input.InputManager.Register("screen.reactor.cat.prev", "Previous category", Input.InputCategory.UI).AddBinding(Input.Scancode.LeftBracket);
            Input.InputManager.Register("screen.reactor.cat.next", "Next category", Input.InputCategory.UI).AddBinding(Input.Scancode.RightBracket);
            Input.InputManager.Register("screen.reactor.item.prev", "Previous item in category", Input.InputCategory.UI).AddBinding(Input.Scancode.Comma).Repeating();
            Input.InputManager.Register("screen.reactor.item.next", "Next item in category", Input.InputCategory.UI).AddBinding(Input.Scancode.Period).Repeating();
            Input.InputManager.Register("screen.reactor.status", "Pipe status", Input.InputCategory.UI).AddBinding(Input.Scancode.P);
            // Pipeline zoom (blocks of 4 / 8); Shift+Up / Down also pick instructions in a reactor —
            // same-category duplicates both fire, and each screen takes only its own ids.
            // Stop jumps on the reactor / pipeline screens (each screen offers the ones it has).
            Input.InputManager.Register("screen.jump.grid", "Jump to the grid", Input.InputCategory.UI).AddBinding(Input.Scancode.Num1, alt: true);
            Input.InputManager.Register("screen.jump.table", "Jump to the components table", Input.InputCategory.UI).AddBinding(Input.Scancode.Grave, alt: true);
            Input.InputManager.Register("screen.jump.place", "Jump to the palette or shelf", Input.InputCategory.UI).AddBinding(Input.Scancode.Num2, alt: true);
            Input.InputManager.Register("screen.jump.tools", "Jump to the tools", Input.InputCategory.UI).AddBinding(Input.Scancode.Num3, alt: true);
            Input.InputManager.Register("screen.jump.log", "Jump to the run log", Input.InputCategory.UI).AddBinding(Input.Scancode.Num4, alt: true);
            Input.InputManager.Register("screen.jump.extra", "Jump to the layers or the enemy", Input.InputCategory.UI).AddBinding(Input.Scancode.Num5, alt: true);
            Input.InputManager.Register("screen.jump.back", "Jump back", Input.InputCategory.UI).AddBinding(Input.Scancode.Backspace, alt: true);
            // Backquote = the game's stop key; in production / defense levels it also ends the crash view.
            Input.InputManager.Register("screen.crash.end", "Leave the crash view", Input.InputCategory.UI).AddBinding(Input.Scancode.Grave);
            Input.InputManager.Register("screen.pipeline.zoom.out", "Zoom out", Input.InputCategory.UI).AddBinding(Input.Scancode.Up, shift: true);
            Input.InputManager.Register("screen.pipeline.zoom.in", "Zoom in", Input.InputCategory.UI).AddBinding(Input.Scancode.Down, shift: true);
            Input.InputManager.Register("screen.pipeline.zoom.max", "Zoom to 8 by 8", Input.InputCategory.UI).AddBinding(Input.Scancode.Up, ctrl: true, shift: true);
            Input.InputManager.Register("screen.pipeline.zoom.reset", "Zoom to 1 by 1", Input.InputCategory.UI).AddBinding(Input.Scancode.Down, ctrl: true, shift: true);
            Input.InputManager.Register("screen.reactor.score", "Cycles, symbols, reactors", Input.InputCategory.UI).AddBinding(Input.Scancode.S, ctrl: true);
            Input.InputManager.Register("screen.reactor.progress", "Level progress", Input.InputCategory.UI).AddBinding(Input.Scancode.G, ctrl: true);
            Input.InputManager.Register("screen.reactor.quota", "Reactor quota", Input.InputCategory.UI).AddBinding(Input.Scancode.Q, ctrl: true);
            Input.InputManager.Register("screen.reactor.port.alpha", "Alpha input", Input.InputCategory.UI).AddBinding(Input.Scancode.A, alt: true);
            Input.InputManager.Register("screen.reactor.port.beta", "Beta input", Input.InputCategory.UI).AddBinding(Input.Scancode.B, alt: true);
            Input.InputManager.Register("screen.reactor.port.psi", "Psi output", Input.InputCategory.UI).AddBinding(Input.Scancode.P, alt: true);
            Input.InputManager.Register("screen.reactor.port.omega", "Omega output", Input.InputCategory.UI).AddBinding(Input.Scancode.O, alt: true);
            Input.InputManager.Register("screen.reactor.view.alpha", "View alpha input molecule", Input.InputCategory.UI).AddBinding(Input.Scancode.A, shift: true, alt: true);
            Input.InputManager.Register("screen.reactor.view.beta", "View beta input molecule", Input.InputCategory.UI).AddBinding(Input.Scancode.B, shift: true, alt: true);
            Input.InputManager.Register("screen.reactor.view.psi", "View psi output molecule", Input.InputCategory.UI).AddBinding(Input.Scancode.P, shift: true, alt: true);
            Input.InputManager.Register("screen.reactor.view.omega", "View omega output molecule", Input.InputCategory.UI).AddBinding(Input.Scancode.O, shift: true, alt: true);
            Input.InputManager.Register("screen.reactor.note.psi", "Edit psi output note", Input.InputCategory.UI).AddBinding(Input.Scancode.P, ctrl: true, shift: true);
            Input.InputManager.Register("screen.reactor.note.omega", "Edit omega output note", Input.InputCategory.UI).AddBinding(Input.Scancode.O, ctrl: true, shift: true);
            Input.InputManager.Register("screen.reactor.tutorial", "Repeat tutorial step", Input.InputCategory.UI).AddBinding(Input.Scancode.T, ctrl: true); // bare T = the game's Sync hotkey
            Input.InputManager.Register("screen.reactor.layer", "Switch active layer", Input.InputCategory.UI).AddBinding(Input.Scancode.L);
            Input.InputManager.Register("screen.reactor.molecule", "Molecule of this zone", Input.InputCategory.UI).AddBinding(Input.Scancode.M);
            // Step to the next event on 0 (this reactor) / Ctrl+0 (any), beside the game's speed keys 1-4 (the game doesn't use 0).
            Input.InputManager.Register("screen.reactor.step", "Step to next event of this reactor", Input.InputCategory.UI).AddBinding(Input.Scancode.Num0).Repeating();
            Input.InputManager.Register("screen.reactor.step.all", "Step to next event of any reactor", Input.InputCategory.UI).AddBinding(Input.Scancode.Num0, ctrl: true).Repeating();
            // The other configurable step keys (Narration/StepKeys): 5-9 (1-4 bare stay the game's
            // speeds). Unassigned in the settings, they do nothing.
            var digits = new[] { Input.Scancode.Num1, Input.Scancode.Num2, Input.Scancode.Num3, Input.Scancode.Num4, Input.Scancode.Num5,
                Input.Scancode.Num6, Input.Scancode.Num7, Input.Scancode.Num8, Input.Scancode.Num9 };
            for (int d = 5; d <= 9; d++)
                Input.InputManager.Register("screen.reactor.step.key." + d, "Step key " + d, Input.InputCategory.UI).AddBinding(digits[d - 1]).Repeating();
            // Reactor switching (Screens/Common/ReactorSwitch): Ctrl+Tab / Ctrl+Shift+Tab next /
            // previous, Ctrl+1 to Ctrl+9 reactor N.
            Input.InputManager.Register("screen.reactor.switch.next", "Next reactor", Input.InputCategory.UI).AddBinding(Input.Scancode.Tab, ctrl: true);
            Input.InputManager.Register("screen.reactor.switch.prev", "Previous reactor", Input.InputCategory.UI).AddBinding(Input.Scancode.Tab, ctrl: true, shift: true);
            for (int d = 1; d <= 9; d++)
                Input.InputManager.Register("screen.reactor.switch." + d, "Reactor " + d, Input.InputCategory.UI).AddBinding(digits[d - 1], ctrl: true);
            // Palette letters place that instruction at the grid cursor (the scancode is the id).
            foreach (var letter in new[] { Input.Scancode.Q, Input.Scancode.W, Input.Scancode.E, Input.Scancode.R, Input.Scancode.T,
                Input.Scancode.Y, Input.Scancode.U, Input.Scancode.I, Input.Scancode.A, Input.Scancode.S, Input.Scancode.D,
                Input.Scancode.F, Input.Scancode.G, Input.Scancode.H, Input.Scancode.J, Input.Scancode.K })
                Input.InputManager.Register("screen.reactor.place." + (int)letter, "Place instruction " + letter, Input.InputCategory.UI).AddBinding(letter);
            Input.InputManager.Register("screen.reactor.delete", "Delete", Input.InputCategory.UI).AddBinding(Input.Scancode.Delete);
            Input.InputManager.Register("screen.reactor.cut", "Cut", Input.InputCategory.UI).AddBinding(Input.Scancode.X, ctrl: true);
            Input.InputManager.Register("screen.reactor.copy", "Copy", Input.InputCategory.UI).AddBinding(Input.Scancode.C, ctrl: true);
            Input.InputManager.Register("screen.reactor.paste", "Paste", Input.InputCategory.UI).AddBinding(Input.Scancode.V, ctrl: true);
            // The instruction picker (user design 2026-10-05): Shift+arrows pick an instruction and
            // its colour, Alt+arrows its parameter and value, ; / ' its main parameter's value.
            Input.InputManager.Register("screen.reactor.pick.prev", "Previous instruction in the cell", Input.InputCategory.UI).AddBinding(Input.Scancode.Up, shift: true).Repeating();
            Input.InputManager.Register("screen.reactor.pick.next", "Next instruction in the cell", Input.InputCategory.UI).AddBinding(Input.Scancode.Down, shift: true).Repeating();
            Input.InputManager.Register("screen.reactor.pick.colour.prev", "Switch colour", Input.InputCategory.UI).AddBinding(Input.Scancode.Left, shift: true);
            Input.InputManager.Register("screen.reactor.pick.colour.next", "Switch colour", Input.InputCategory.UI).AddBinding(Input.Scancode.Right, shift: true);
            Input.InputManager.Register("screen.reactor.param.prev", "Previous instruction parameter", Input.InputCategory.UI).AddBinding(Input.Scancode.Up, alt: true).Repeating();
            Input.InputManager.Register("screen.reactor.param.next", "Next instruction parameter", Input.InputCategory.UI).AddBinding(Input.Scancode.Down, alt: true).Repeating();
            Input.InputManager.Register("screen.reactor.value.prev", "Previous parameter value", Input.InputCategory.UI).AddBinding(Input.Scancode.Left, alt: true).Repeating();
            Input.InputManager.Register("screen.reactor.value.next", "Next parameter value", Input.InputCategory.UI).AddBinding(Input.Scancode.Right, alt: true).Repeating();
            Input.InputManager.Register("screen.reactor.primary.prev", "Previous value of the instruction's main parameter", Input.InputCategory.UI).AddBinding(Input.Scancode.Semicolon).Repeating();
            Input.InputManager.Register("screen.reactor.primary.next", "Next value of the instruction's main parameter", Input.InputCategory.UI).AddBinding(Input.Scancode.Apostrophe).Repeating();
            Input.InputManager.Register("screen.reactor.mark", "Mark a rectangle corner", Input.InputCategory.UI).AddBinding(Input.Scancode.Space, shift: true);
            Input.InputManager.Register("screen.reactor.unmark", "Clear the marked rectangle", Input.InputCategory.UI).AddBinding(Input.Scancode.Space, ctrl: true);
            Input.InputManager.Register("screen.reactor.markall", "Mark the whole grid", Input.InputCategory.UI).AddBinding(Input.Scancode.A, ctrl: true);
            Input.InputManager.Register("screen.reactor.skip.left", "Skip identical cells left", Input.InputCategory.UI).AddBinding(Input.Scancode.Left, ctrl: true).Repeating();
            Input.InputManager.Register("screen.reactor.skip.right", "Skip identical cells right", Input.InputCategory.UI).AddBinding(Input.Scancode.Right, ctrl: true).Repeating();
            // Context menus live on ui.secondary (Backspace, the right-click key) — user rule, 2026-09-27.

            Input.InputManager.ActiveCategoriesProvider = () =>
                new System.Collections.Generic.List<Input.InputCategory>(Screens.ScreenManager.ActiveInputCategories());
            Input.InputManager.UiDispatcher = UI.Navigation.DispatchJustPressed;
            Input.InputManager.SuppressPoll = () =>
            {
                var cur = Screens.ScreenManager.Current;
                return cur != null && cur.CapturesRawInput;
            };
        }

        /// <summary>Ticks come from the Class185.vmethod_3 prefix, which never runs until the game's
        /// init has returned — so first-tick arming guarantees everything init builds (the loc table,
        /// textures, the root screen) exists before any patched type's static constructor can be
        /// forced (Harmony's detour JIT-prepares the target, which runs its declaring type's cctor; the
        /// Echopunks cold-boot crash of 2026-08-23 came from patching before init). On a hot reload the
        /// next frame arms immediately.</summary>
        private bool _gamePatchesArmed;

        public void Tick()
        {
            if (!_gamePatchesArmed)
            {
                _gamePatchesArmed = true;
                Patches.GameKeySuppression.Apply(_harmony); // focus-mode key swallow (see the class doc)
                Patches.GateTextCapture.Apply(_harmony);    // credit-card and epilogue strings for the click gates
                Patches.TitleTextCapture.Apply(_harmony);   // main-menu news text + Class60 dialog buttons
                Patches.DialogCapture.Apply(_harmony);      // in-level message boxes + the wrong-molecule dialog
                Patches.TooltipCapture.Apply(_harmony);     // hover tooltip text (palette slots, reactor hardware, pipeline pieces)
                Patches.RunCapture.Apply(_harmony);         // run events -> the run log (spoken at the slowest speed)
                Patches.OptionsInjection.Apply(_harmony);   // the Settings dialog's Game / Mod tabs (real widgets)
                Patches.DefenseCapture.Apply(_harmony);     // defense run events (tanks, the enemy, the Control Center) + F1-F4
                Patches.StoryCapture.Apply(_harmony);       // training captions + the story screen's current tab
                Patches.UndoCapture.Apply(_harmony);        // what an undo / redo changed in the open reactor
                Patches.StepControl.Apply(_harmony);        // step to the next event: pause at the end of its cycle
                Patches.ModifierMask.Apply(_harmony);       // hide Ctrl from the pipeline's drop during mod moves
            }
            if (!_updateAnnounced && _updateCheck != null && _updateCheck.NewerVersion != null)
            {
                _updateAnnounced = true;
                Speech.Tts.Speak(Loc.T("app.update_available", new { version = _updateCheck.NewerVersion }));
            }
            FrameLoop.Tick();
        }

        public void Dispose()
        {
            // This Harmony build predates UnpatchSelf; UnpatchAll(ownId) is the same owner-scoped removal.
            try { _harmony?.UnpatchAll(_harmony.Id); }
            catch (Exception ex) { Log.Error("[module] UnpatchSelf failed", ex); }
            _harmony = null;
        }
    }
}
