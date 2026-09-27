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
            // the screen stack (which drives the navigator), then the synthetic-click release.
            // (The dev pump is host-side, first.)
            FrameLoop.Register("keyboard", Input.SdlKeyboard.Update);
            FrameLoop.Register("input", Input.InputManager.Tick);
            FrameLoop.Register("loc", LocalizationManager.Tick);
            FrameLoop.Register("screens", Screens.ScreenManager.Tick);
            FrameLoop.Register("click", Game.SyntheticClick.Tick);

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
            Input.InputManager.Register("ui.tooltip", "Read details", Input.InputCategory.UI).AddBinding(Input.Scancode.Space);
            Input.InputManager.Register("ui.back", "Back", Input.InputCategory.UI).AddBinding(Input.Scancode.Escape);
            Input.InputManager.Register("ui.regionPrev", "Previous region", Input.InputCategory.UI).AddBinding(Input.Scancode.Up, ctrl: true).Repeating();
            Input.InputManager.Register("ui.regionNext", "Next region", Input.InputCategory.UI).AddBinding(Input.Scancode.Down, ctrl: true).Repeating();

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
