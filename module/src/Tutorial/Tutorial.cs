using System;
using System.Collections.Generic;
using Impeller;
using SpaceChem;
using SpeechChem.Game;
using SpeechChem.Input;
using SpeechChem.Screens;
using SpeechChem.Screens.Reactor;

namespace SpeechChem.Tutorial
{
    /// <summary>
    /// The reactor tutorial (user design 2026-10-23, replacing the read-only view of the game's).
    /// The game's own tutorial is a static list of placement steps (Class424.list_0, filled by the
    /// level's constructor; three research levels have one) drawn as "the first step whose piece
    /// isn't in place" — no counter, no other step kinds. Here a level either has a SCRIPT
    /// (<see cref="TutorialScripts"/>: the game's placement steps reworded, interleaved with
    /// latched steps that teach the mod's keys) or falls back to the game's list as live steps.
    ///
    /// A FrameLoop step, not the reactor screen's OnUpdate: steps are met inside its children
    /// (the molecule viewer, the path trace), whose parent gets no update. Runs only while the
    /// game's top screen is the reactor and the level-start lines have been spoken.
    /// </summary>
    internal static class Tutorial
    {
        private static Class83 _level;
        private static List<TutorialStep> _steps;
        private static bool _scripted;
        private static string _key;              // profile TAB level, for the progress file
        private static HashSet<string> _latched = new HashSet<string>();
        private static TutorialStep _active;
        private static readonly HashSet<string> _pressed = new HashSet<string>();
        private static readonly HashSet<string> _afterSaid = new HashSet<string>(); // once per level visit: a re-placed piece doesn't repeat it
        private static readonly TutorialContext _ctx = new TutorialContext { Pressed = id => _pressed.Contains(id) };

        /// <summary>The navigator handled an action (Navigation.DispatchJustPressed).</summary>
        public static void OnAction(string id)
        {
            if (_active != null) _pressed.Add(id);
        }

        public static void Tick()
        {
            var level = Class53.smethod_5<Class83>();
            if (!ReferenceEquals(level, _level)) Load(level);
            if (_steps == null) return;
            var editor = GameApi.TopScreen() as Class77;
            var screen = ReactorScreen();
            if (editor == null || screen == null || !screen.LevelStartSpoken) { _pressed.Clear(); return; }
            _ctx.Editor = editor;
            _ctx.Screen = screen;
            if (Patches.StepControl.Active) _ctx.Stepped = true;

            if (_active != null && _active.Latches && Met(_active))
            {
                _latched.Add(_active.Id);
                if (_scripted) TutorialStore.Add(_key, _active.Id);
            }
            var next = FirstUndone();
            if (ReferenceEquals(next, _active)) return;

            bool forward = _active != null && Done(_active) && (next == null || _steps.IndexOf(next) > _steps.IndexOf(_active));
            if (forward && _active.After != null && _afterSaid.Add(_active.Id)) Say(_active.After(_ctx));
            _active = next;
            _pressed.Clear();
            _ctx.Stepped = false;
            if (next != null) Say(next.Text(_ctx));
            else if (forward && _scripted)
            {
                // Finished from an earlier step (the last pieces placed out of order): the last
                // step's After is the tutorial's closing word, so it is still said. A finished
                // tutorial stays loaded: its placements come back when the pieces go (a delete,
                // the grid's Reset — user rule 2026-10-23), the latched steps don't.
                var last = _steps[_steps.Count - 1];
                if (last.After != null && _afterSaid.Add(last.Id)) Say(last.After(_ctx));
            }
        }

        /// <summary>The repeat key (Ctrl+T), from anywhere in the reactor, children included. False
        /// with no step: the key then does nothing (user rule).</summary>
        public static bool Repeat()
        {
            if (_active == null || !(GameApi.TopScreen() is Class77)) return false;
            Say(_active.Text(_ctx), interrupt: true);
            return true;
        }

        /// <summary>The active step's text for the reactor's Tutorial stop, or null (no stop).</summary>
        public static string ActiveText() => _active == null || _ctx.Editor == null ? null : _active.Text(_ctx);

        /// <summary>Whether a cell (0-based) is the active placement step's target ("highlighted").</summary>
        public static bool IsTarget(int x, int y)
        {
            var t = _active?.Target;
            if (t == null || t.bool_0) return false;
            var c = t.reactorBin_0.vector2i_0;
            return c.int_0 == x && c.int_1 == y;
        }

        /// <summary>For the dev probe: level, active step, done steps.</summary>
        public static string Status()
        {
            if (_steps == null) return "no tutorial (level " + (GoalTracker.string_0 ?? "none") + ")\n";
            return "level " + GoalTracker.string_0 + (_scripted ? " (scripted)" : " (game's)") + ", active: " + (_active?.Id ?? "none")
                + ", done: " + string.Join(" ", new List<string>(_latched).ToArray()) + "\n";
        }

        /// <summary>Forget this level's progress for the profile and start over (dev probe).</summary>
        public static string Reset()
        {
            if (_key != null) TutorialStore.Clear(_key);
            _level = null; // reload next tick
            return "tutorial progress cleared\n";
        }

        private static void Load(Class83 level)
        {
            _level = level;
            _steps = null;
            _scripted = false;
            _active = null;
            _key = null;
            _latched = new HashSet<string>();
            _pressed.Clear();
            _afterSaid.Clear();
            _ctx.Editor = null;
            if (level == null) return;
            try
            {
                string id = GoalTracker.string_0;
                var script = TutorialScripts.For(id);
                if (script != null)
                {
                    string profile = Locals.smethod_0()?.string_0;
                    _key = (profile ?? "") + "\t" + id;
                    _latched = TutorialStore.Load(_key);
                    _steps = script(Class424.list_0);
                    _scripted = _steps != null;
                    if (_steps == null) Log.Error("[tutorial] the game's steps for " + id + " are not what the script expects; using the game's");
                }
                if (_steps == null && Class424.list_0.Count > 0) _steps = GameSteps();
            }
            catch (Exception ex) { Log.Error("[tutorial] load failed", ex); _steps = null; }
        }

        /// <summary>The game's list as live steps: each placement met while its piece is in place,
        /// a text-only step (bool_0) never — the game shows it forever too.</summary>
        private static List<TutorialStep> GameSteps()
        {
            var steps = new List<TutorialStep>();
            int n = 0;
            foreach (var g in Class424.list_0)
            {
                var step = g;
                steps.Add(new TutorialStep
                {
                    Id = "game." + n++,
                    Text = c => ReactorEditorScreen.GameStepText(step),
                    Met = c => !step.bool_0 && c.Placed(step),
                    Target = step.bool_0 ? null : step,
                });
            }
            return steps;
        }

        private static bool Met(TutorialStep s)
        {
            try { return s.Met(_ctx); }
            catch (Exception ex) { Log.Error("[tutorial] step " + s.Id + " failed", ex); return false; }
        }

        private static bool Done(TutorialStep s) => s.Latches ? _latched.Contains(s.Id) : Met(s);

        private static TutorialStep FirstUndone()
        {
            foreach (var s in _steps)
                if (!Done(s)) return s;
            return null;
        }

        private static void Say(string text, bool interrupt = false)
        {
            if (!string.IsNullOrEmpty(text)) Speech.Tts.Speak(text, interrupt: interrupt);
        }

        private static ReactorEditorScreen ReactorScreen()
        {
            foreach (var s in ScreenManager.Stack)
                if (s is ReactorEditorScreen r) return r;
            return null;
        }

        /// <summary>The key an action is bound to, as the player would say it ("Ctrl+Shift+R",
        /// "0") — read from the live binding, never written into the text.</summary>
        public static string KeyOf(string actionId)
        {
            foreach (var a in InputManager.Actions)
            {
                if (a.Key != actionId) continue;
                foreach (var b in a.Bindings)
                {
                    if (!(b is SdlKeyboardBinding k)) return b.DisplayName;
                    var sb = new System.Text.StringBuilder();
                    if (k.Ctrl) sb.Append("Ctrl+");
                    if (k.Shift) sb.Append("Shift+");
                    if (k.Alt) sb.Append("Alt+");
                    string name = k.Key.ToString();
                    if (k.Key >= Scancode.Num1 && k.Key <= Scancode.Num0) name = k.Key == Scancode.Num0 ? "0" : ((int)k.Key - (int)Scancode.Num1 + 1).ToString();
                    sb.Append(name);
                    return sb.ToString();
                }
            }
            return "?";
        }
    }
}
