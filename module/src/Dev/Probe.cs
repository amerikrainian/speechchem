#if DEBUG
using System;
using System.Linq;
using System.Text;
using SpaceChem;
using SpaceChem.Reactor;
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
    ///   menugroups         every instruction menu's items by the context menu's parameter row
    ///   instrmenu          the context-menu labels of a control instruction (C, down), placed on the
    ///                      first empty red cell of the open reactor for the read, then removed
    ///   custom &lt;json&gt;|clean  open a test research puzzle / wipe its saved solution (CustomPuzzle)
    ///   pipemap            the open pipeline as text (0-based cells): '#' blocked, a letter per
    ///                      component body, '+' pipe, '*' crossing, '.' free; then each component's
    ///                      origin, ports, and every pipe's end and link
    ///   runlog [n]         the last n (default 40) cycle groups of the run log, as logged
    ///   blast &lt;x&gt;          defense level 1 (Class144): the Oxygen Tank at map column x
    ///                      (0-based: 12, 18, 24) raises its blast event (method_15) without
    ///                      being filled — the level's handler then hits or misses the robot;
    ///                      during a run only
    ///   profile …, openlevel &lt;id&gt;, fire …   auditing unreached levels on a throwaway
    ///                      "SCAudit*" profile (Dev/LiveAudit)
    ///   defense status     the open defense level as the mod reads it: enemy span / parts /
    ///                      state, graph rows, special buildings' meters
    ///   defense audit      every defense level built in memory and run through the mod's generic
    ///                      defense readers (Dev/DefenseAudit; names enemies — never spoken)
    ///   focus &lt;stop&gt; &lt;id&gt;  focus a stop, then a node by structural id (e.g. "pipeline.map
    ///                      pipeline.cell.4.7"), as the navigator's own jumps do
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
                case "instrmenu": return InstrMenu();
                case "menugroups": return MenuGroups();
                case "custom": return CustomPuzzle.Run(argument);
                case "pipemap": return PipeMap();
                case "focus": return Focus(argument);
                case "runlog": return RunLog(argument);
                case "logstress": return LogStress.Run(argument);
                case "optionstab":
                {
                    // Settings dialog tabs: "game", "mod", or a Mod sub-tab "general" / "events" / "keys".
                    string a = (argument ?? "").Trim();
                    if (a == "game") SpeechChem.Screens.Settings.ModSettingsUi.ShowTab(SpeechChem.Screens.Settings.ModSettingsUi.Tab.Game);
                    else
                    {
                        SpeechChem.Screens.Settings.ModSettingsUi.ShowTab(SpeechChem.Screens.Settings.ModSettingsUi.Tab.Mod);
                        if (a == "events") SpeechChem.Screens.Settings.ModSettingsUi.ShowModTab(SpeechChem.Screens.Settings.ModSettingsUi.ModTab.Events);
                        else if (a == "keys") SpeechChem.Screens.Settings.ModSettingsUi.ShowModTab(SpeechChem.Screens.Settings.ModSettingsUi.ModTab.Keys);
                        else if (a == "general") SpeechChem.Screens.Settings.ModSettingsUi.ShowModTab(SpeechChem.Screens.Settings.ModSettingsUi.ModTab.General);
                    }
                    return "tab requested" + (char)10;
                }
                case "settingsrow":
                {
                    // Activate a row (or a compound row's cell) of the shown Mod page by id, as a click would.
                    string id = (argument ?? "").Trim();
                    var page = SpeechChem.Screens.Settings.ModSettingsUi.Current();
                    SpeechChem.Screens.Settings.SRow hit = null;
                    foreach (var r in page.Rows)
                    {
                        if (r.Id == id) hit = r;
                        if (r is SpeechChem.Screens.Settings.SCompound k) foreach (var c in k.Cells) if (c.Id == id) hit = c;
                    }
                    switch (hit)
                    {
                        case SpeechChem.Screens.Settings.SLink l: SpeechChem.Screens.Settings.ModSettingsUi.Push(l.Open, l.Id); break;
                        case SpeechChem.Screens.Settings.SAction a: a.Run(); break;
                        case SpeechChem.Screens.Settings.SToggle t: t.Set(!t.Get()); break;
                        case SpeechChem.Screens.Settings.SChoice c: c.Set((c.Get() + 1) % c.Options().Length); break;
                        default:
                            var ids = new System.Collections.Generic.List<string>();
                            foreach (var r in page.Rows) ids.Add(r.Id);
                            return "[no row " + id + "] rows: " + string.Join(" ", ids.ToArray()) + (char)10;
                    }
                    SpeechChem.Screens.Settings.ModSettingsUi.Dirty = true;
                    return "activated " + id + (char)10;
                }
                case "settingspage":
                {
                    int delta; int.TryParse((argument ?? "").Trim(), out delta);
                    if (delta == 0) { SpeechChem.Screens.Settings.ModSettingsUi.Pop(); return "back" + (char)10; }
                    SpeechChem.Screens.Settings.ModSettingsUi.TurnPage(delta);
                    return "page" + (char)10;
                }
                case "blast": return Blast(argument);
                case "invalid": return Invalid();
                case "crash": return Crash(argument);
                case "profile": return LiveAudit.Profile(argument);
                case "openlevel": return LiveAudit.OpenLevel(argument);
                case "fire": return LiveAudit.Fire(argument);
                case "defense":
                    switch ((argument ?? "").Trim())
                    {
                        case "audit": return DefenseAudit.Run();
                        case "status": return DefenseAudit.Status();
                        default: return "[usage: defense audit|status]" + (char)10;
                    }
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

        private static string RunLog(string argument)
        {
            int n;
            if (!int.TryParse((argument ?? "").Trim(), out n) || n <= 0) n = 40;
            var log = Patches.RunCapture.Log;
            var sb = new StringBuilder();
            int start = Math.Max(0, log.Groups.Count - n);
            for (int g = start; g < log.Groups.Count; g++)
            {
                int key = log.Groups[g];
                sb.Append("Cycle ").Append(key).Append('\n');
                foreach (var e in log.Entries(key)) sb.Append("  ").Append(Narration.Formatter.Format(e, Narration.FormatLayer.Log, null)).Append('\n');
            }
            return sb.Length == 0 ? "[run log empty]\n" : sb.ToString();
        }

        private static string Blast(string argument)
        {
            var level = Class53.smethod_5<Class144>();
            if (level == null) return "[not in the first defense level]\n";
            if ((int)Class258.smethod_16() == 0) return "[run stopped]\n";
            int x;
            if (!int.TryParse((argument ?? "").Trim(), out x)) return "[usage: blast <tank column 12|18|24>]\n";
            foreach (var tank in level.list_1)
            {
                var at = level.pipeline_0.method_9(tank);
                if (!at.HasValue || at.Value.int_0 != x) continue;
                tank.method_15(Class605.int_1, null); // the tank's own event, as its 35th methane raises it
                var robot = level.class313_0;
                return "blast from column " + x + "; robot x " + robot.vector2i_0.int_0 + ", " + Game.DefenseText.PartsText(robot) +"\n";
            }
            return "[no tank at column " + x + "]\n";
        }

        /// <summary>The game's own Reaction Error (GoalTracker.smethod_12) in reactor n of the
        /// pipeline, marked at its cell (4, 3): opens that reactor and the box exactly as a crash.
        /// Only during a run (paused is safest); Okay stops it.</summary>
        private static string Crash(string argument)
        {
            if ((int)Class258.smethod_16() == 0) return "[no run: start one and pause it first]\n";
            var p = Class53.smethod_5<SpaceChem.Pipeline.PipelineEditor>()?.pipeline_0;
            var reactors = Game.PipelineText.Reactors(p);
            if (!int.TryParse((argument ?? "").Trim(), out int n) || n < 1 || n > reactors.Count) return "[usage: crash <reactor 1.." + reactors.Count + ">]\n";
            var editor = reactors[n - 1].class77_0;
            if (editor?.reactor_0 == null) return "[reactor has no editor]\n";
            var marker = editor.reactor_0.vector2i_0 + new Impeller.Vector2i(3 * 79 + 40, 2 * 79 + 40);
            GoalTracker.smethod_12(editor, "Probe reaction error.", Struct7.struct7_0, new[] { marker });
            return "reaction error in reactor " + n + "\n";
        }

        /// <summary>The game's own invalid-molecule error on the first output building's first
        /// input (Draggable.method_7 with a Xenon, as the laser reactor raises it): the box, then
        /// Okay stops the run. Safe while stopped: no cycle runs.</summary>
        private static string Invalid()
        {
            var p = Class53.smethod_5<SpaceChem.Pipeline.PipelineEditor>()?.pipeline_0;
            if (p == null) return "[no pipeline open]\n";
            var cellSize = p.method_3();
            foreach (var kv in p.dictionary_1)
            {
                if (!(kv.Key is Class578 output) || output.class485_0.Count == 0) continue;
                var input = output.class485_0.Values.First();
                var cell = kv.Value + input.vector2i_0;
                var pos = new Impeller.Vector2i(cell.int_0 * cellSize.int_0 + cellSize.int_0 / 2, cell.int_1 * cellSize.int_1 + cellSize.int_1 / 2);
                output.method_7(Class307.smethod_6(Element.Xenon), output.dictionary_0.Keys, pos);
                return "invalid molecule at " + output.string_1 + ", input cell " + (cell.int_0 + 1) + ", " + (cell.int_1 + 1) + "\n";
            }
            return "[no output building]\n";
        }

        private static string Focus(string argument)
        {
            var parts = (argument ?? "").Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return "[usage: focus <stop key> <structural id>]\n";
            UI.Navigation.FocusStop(parts[0]);
            UI.Navigation.FocusNode(UI.Graph.ControlId.Structural(parts[1]));
            return "focus requested: " + parts[1] + "\n";
        }

        private static string PipeMap()
        {
            var p = Class53.smethod_5<SpaceChem.Pipeline.PipelineEditor>()?.pipeline_0;
            if (p == null) return "[no pipeline open]\n";
            var size = p.method_4();
            var comps = PipelineText.Components(p);
            var sb = new StringBuilder();
            sb.Append("    ");
            for (int x = 0; x < size.int_0; x++) sb.Append(x % 10);
            sb.Append('\n');
            for (int y = 0; y < size.int_1; y++)
            {
                sb.Append(y.ToString().PadLeft(3)).Append(' ');
                for (int x = 0; x < size.int_0; x++)
                {
                    var cell = new Impeller.Vector2i(x, y);
                    var d = p.method_7(cell);
                    var origin = d == null ? null : p.method_9(d);
                    char c = '.';
                    if (d == null) c = '.';
                    else if (d is Class612 || !origin.HasValue) c = '#';
                    else
                    {
                        var local = cell - origin.Value;
                        bool pipe = false, cross = false;
                        foreach (var o in d.class485_1.Values)
                        {
                            var pd = o.pipeDraggable_0;
                            if (pd == null || !pd.dictionary_3.ContainsKey(local)) continue;
                            pipe = true;
                            if (pd.dictionary_4.ContainsKey(local)) cross = true;
                        }
                        int i = comps.FindIndex(kv => ReferenceEquals(kv.Key, d));
                        c = cross ? '*' : pipe ? '+' : i >= 0 ? (char)('A' + i) : '?';
                    }
                    sb.Append(c);
                }
                sb.Append('\n');
            }
            for (int i = 0; i < comps.Count; i++)
            {
                var d = comps[i].Key;
                var o = comps[i].Value;
                sb.Append((char)('A' + i)).Append(' ').Append(PipelineText.Name(p, d)).Append(" at ").Append(o.int_0).Append(',').Append(o.int_1)
                  .Append(" size ").Append(d.vector2i_0.int_0).Append('x').Append(d.vector2i_0.int_1).Append(d.bool_0 ? " fixed" : "").Append('\n');
                foreach (var kv in d.class485_0)
                    sb.Append("   in ").Append(kv.Key).Append(" cell ").Append(o.int_0 + kv.Value.vector2i_0.int_0).Append(',').Append(o.int_1 + kv.Value.vector2i_0.int_1)
                      .Append(kv.Value.pipeDraggable_0 != null ? " fed by " + PipelineText.Name(p, kv.Value.pipeDraggable_0.draggable_0) : "").Append('\n');
                foreach (var kv in d.class485_1)
                {
                    var pd = kv.Value.pipeDraggable_0;
                    if (pd == null) continue;
                    var end = pd.linkedList_0.Last.Value + pd.method_14();
                    var to = kv.Value.vmethod_0();
                    sb.Append("   out ").Append(kv.Key).Append(" len ").Append(pd.linkedList_0.Count).Append(" end ").Append(end.int_0).Append(',').Append(end.int_1)
                      .Append(to != null ? " -> " + PipelineText.Name(p, to) : " open").Append('\n');
                }
            }
            return sb.ToString();
        }

        private static string InstrMenu()
        {
            var r = Class53.smethod_5<Class77>()?.reactor_0;
            if (r == null) return "[no reactor open]\n";
            if ((int)Class258.smethod_16() != 0) return "[reactor is running]\n";
            var size = r.method_1();
            ReactorBin? free = null;
            for (int y = size.int_1 - 1; y >= 0 && free == null; y--)
                for (int x = 0; x < size.int_0 && free == null; x++)
                {
                    var bin = new ReactorBin(new Impeller.Vector2i(x, y), (Enum114)ReactorText.Red);
                    if (r.method_17(bin) == null) free = bin;
                }
            if (free == null) return "[no empty red cell]\n";
            var member = new SpaceChem.Reactor.ControlInstruction(r, (Enum111)2, Enum153.Down);
            var menu = SpaceChem.Reactor.Instruction.dictionary_1[member.GetType()];
            var sb = new StringBuilder("placed control C down at ").Append(free.Value.vector2i_0.int_0 + 1).Append(", ").Append(free.Value.vector2i_0.int_1 + 1).Append('\n');
            try
            {
                r.method_18(free.Value, member);
                sb.Append("label: ").Append(ReactorText.Label(member)).Append('\n');
                SpeechChem.Screens.Reactor.ReactorEditorScreen.SyncMenu(menu, member);
                foreach (var component in menu.linkedList_0)
                {
                    if (!(component is SpaceChem.UI.MenuItem<SpaceChem.Reactor.Instruction> item)) continue;
                    var label = SpeechChem.Screens.Reactor.ReactorEditorScreen.MenuItemLabel(item);
                    if (label == null) continue;
                    sb.Append("  ").Append(label()).Append(item.isSelectedFunc_0(member) ? "  [selected]" : "").Append('\n');
                }
            }
            finally
            {
                r.method_21(member);
                Impeller.Locals.smethod_0().smethod_0().method_72(member);
                sb.Append("removed; cell now ").Append(r.method_17(free.Value) == null ? "empty" : "OCCUPIED").Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Every instruction menu's items with the row (family) each lands in — the
        /// context menu's parameter rows, without placing anything.</summary>
        private static string MenuGroups()
        {
            var sb = new StringBuilder();
            foreach (var kv in SpaceChem.Reactor.Instruction.dictionary_1)
            {
                sb.Append(GameNames.DeobTypeName(kv.Key) ?? kv.Key.Name).Append('\n');
                foreach (var component in kv.Value.linkedList_0)
                {
                    if (!(component is SpaceChem.UI.MenuItem<SpaceChem.Reactor.Instruction> item)) continue;
                    var label = SpeechChem.Screens.Reactor.ReactorEditorScreen.MenuItemLabel(item);
                    if (label == null) continue;
                    var group = SpeechChem.Screens.Reactor.ReactorEditorScreen.MenuItemGroupLabel(item, kv.Key);
                    string text;
                    try { text = label(); } catch (Exception ex) { text = "[" + ex.GetType().Name + "]"; }
                    sb.Append("  ").Append(group == null ? "(action)" : group()).Append(": ").Append(text).Append('\n');
                }
            }
            return sb.ToString();
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
