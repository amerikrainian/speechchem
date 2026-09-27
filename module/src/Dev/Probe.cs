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
    ///   type &lt;text&gt;        push the text as one SDL_TEXTINPUT event (the game's real typing path)
    ///   profiles           the profile set as the picker enumerates it (* = current)
    ///   rawkey &lt;scancode&gt;  push a raw SDL key press to the GAME (bypasses the mod's own input)
    ///   switchprofile      press the main menu's Switch Profile (opens the profile picker)
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
                case "type": return SdlNative.PushText(argument) ? "typed: " + argument + "\n" : "[type refused]\n";
                case "profiles": return Profiles();
                case "rawkey": return RawKey(argument);
                case "switchprofile":
                {
                    // The main menu's "Switch Profile" button handler (the picker only shows at boot
                    // when no profile exists).
                    var menu = GameApi.TopScreen() as SpaceChem.MainMenuEditor;
                    if (menu == null) return "[main menu is not the top screen]\n";
                    menu.method_25();
                    return "switch profile pressed\n";
                }
                default: return "commands: screens | push shiplost|credits|epilogue | pop | key <action id> | click | type <text> | profiles\n";
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

        // A raw SDL key press+release straight into the game's event queue. It reaches the GAME's key
        // paths (and our suppression seams) but not the mod's own input, which reads SDL's keyboard
        // state array — SDL_PushEvent does not update that. Tests what the game sees for a key.
        private static string RawKey(string arg)
        {
            int scancode;
            if (!int.TryParse((arg ?? "").Trim(), out scancode)) return "rawkey <scancode>\n";
            bool ok = SdlNative.PushKey(scancode, 0, down: true) && SdlNative.PushKey(scancode, 0, down: false);
            return ok ? "pushed scancode " + scancode + "\n" : "[rawkey refused]\n";
        }

        private static string Profiles()
        {
            var sb = new StringBuilder();
            var current = Impeller.Locals.smethod_0();
            foreach (var p in Impeller.Locals.smethod_5())
                sb.Append(p == current ? "* " : "  ").Append(p.string_0).Append(" | rank ").Append(p.method_0())
                  .Append(" | ").Append(p.method_2().ToString()).Append('\n');
            return sb.Length == 0 ? "(no profiles)\n" : sb.ToString();
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
