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
    ///   tolevelselect      leave the open level through the game's own return-to-level-select
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
                case "tolevelselect":
                    // The game's own "return to level select" (Class53.smethod_8: pops the level with
                    // its transition — what the Story/Training screen's paths use).
                    if (Class53.smethod_5<Class83>() == null) return "[no level on the stack]\n";
                    Class53.smethod_8(true, false, false);
                    return "returned to level select\n";
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
                case "exitprompt": // the reactor's own exit prompt (Yes/No)
                    SpaceChem.MessageBoxEditor.smethod_16();
                    return "exit prompt pushed\n";
                case "reactionerror": // a Reaction Error box marked at reactor cell (4,3), 1-based (dev text)
                {
                    var reactor = Class53.smethod_5<Class77>()?.reactor_0;
                    if (reactor == null) return "[no reactor open]\n";
                    var marker = reactor.vector2i_0 + new Impeller.Vector2i(3 * 79 + 40, 2 * 79 + 40);
                    screen = SpaceChem.MessageBoxEditor.smethod_15(GameText.T("Reaction Error"), "Probe reaction error.",
                        Struct7.struct7_0, new[] { marker }, () => { });
                    break;
                }
                case "message": // a Class58 message box, like the forum-signature notice (dev text)
                    screen = new Class58("Probe message box.", false, new[]
                    {
                        new Class392(GameText.T("Continue"), new[] { Impeller.Keys.Escape, Impeller.Keys.Enter }, () => { }),
                    });
                    break;
                case "performance": // the completion screen with a made-up score (150/1/10, best 140/1/12); dismiss with pop, NOT Continue (it leaves the level)
                    screen = new SpaceChem.StoryTrainingPerformanceEditor(Struct7.struct7_0, Struct7.struct7_0,
                        new SpaceChem.Score(150, 1, 10), new SpaceChem.Score(140, 1, 12), Struct7.struct7_0, false, true);
                    break;
                default: return "push what? shiplost | credits | epilogue | performance\n";
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
