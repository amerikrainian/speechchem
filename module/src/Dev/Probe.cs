#if DEBUG
using System;
using System.Linq;
using System.Text;
using SpeechChem.Game;

namespace SpeechChem.Dev
{
    /// <summary>
    /// DEBUG-only typed driver for the live game, reached through the host's POST /probe route (body
    /// "command [argument]"). The host's /eval REPL compiles against the SHIPPING assembly, whose
    /// types are obfuscated gibberish, so anything that must name a game type goes here instead: this
    /// code is written against the deob names and remapped at load like the rest of the module.
    /// Runs on the game's main thread (the host routes it through the tick pump).
    ///
    ///   screens            the game's screen chain (deob names) + the modeled screen + focused node
    ///   push shiplost      push a "ship lost" card (the final defeat's Prometheus variant: the other
    ///                      variants read the current level's map position and need a level loaded)
    ///   push credits       push the credits sequence (the game's own builder)
    ///   push epilogue      push the end-game epilogue scroll (the game's own builder)
    ///   pop                pop the top game screen
    ///   key &lt;action id&gt;   dispatch a registered input action (ui.down, ui.activate, …) through the
    ///                      navigator exactly as its key binding would
    ///   click              push a synthetic left click (what the click gates use)
    /// </summary>
    internal static class Probe
    {
        public static string Run(string command, string argument)
        {
            switch ((command ?? "").Trim().ToLowerInvariant())
            {
                case "screens": return Screens();
                case "push": return Push(argument);
                case "pop": return GameApi.PopScreen() ? "popped\n" : "[pop failed]\n";
                case "key": return Key(argument);
                case "click": return SyntheticClick.Click() ? "click queued\n" : "[click refused]\n";
                default: return "commands: screens | push shiplost|credits|epilogue | pop | key <action id> | click\n";
            }
        }

        private static string Screens()
        {
            var sb = new StringBuilder();
            sb.Append("game chain (bottom -> top): ").Append(string.Join(" > ", GameState.ScreenStackNames().ToArray())).Append('\n');
            var cur = SpeechChem.Screens.ScreenManager.Current;
            sb.Append("modeled: ").Append(cur == null ? "(none)" : cur.Key).Append('\n');
            sb.Append("focused node: ").Append(UI.Navigation.FocusedNodeId?.ToString() ?? "(none)").Append('\n');
            return sb.ToString();
        }

        private static string Push(string what)
        {
            Class53 screen;
            switch ((what ?? "").Trim().ToLowerInvariant())
            {
                case "shiplost": screen = new Class154((Enum147)8); break;
                case "credits": screen = Class152.smethod_12(); break;
                case "epilogue": screen = Class81.smethod_13(); break;
                default: return "push what? shiplost | credits | epilogue\n";
            }
            return GameApi.PushScreen(screen) ? "pushed " + what + "\n" : "[push failed]\n";
        }

        private static string Key(string id)
        {
            var action = Input.InputManager.Actions.FirstOrDefault(a => a.Key == (id ?? "").Trim());
            if (action == null) return "[no such action] " + id + "\n";
            bool handled = UI.Navigation.DispatchJustPressed(action);
            return (handled ? "handled: " : "not handled: ") + action.Key + "\n";
        }
    }
}
#endif
