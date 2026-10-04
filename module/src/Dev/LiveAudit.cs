#if DEBUG
using System;
using System.Linq;
using System.Text;
using Impeller;
using SpaceChem;
using SpaceChem.Levels;
using SpaceChem.Pipeline;
using SpeechChem.Game;

namespace SpeechChem.Dev
{
    /// <summary>
    /// DEBUG tools for auditing levels the player has not reached, on a THROWAWAY profile:
    ///   profile list | create &lt;name&gt; | select &lt;name&gt; | delete &lt;name&gt;
    ///       the picker's own calls (Class75: create Locals.smethod_7, select Locals.smethod_1 +
    ///       reload, delete Locals.smethod_6 — the game renames the save to NNN.user-deleted).
    ///       create / delete only accept names starting "SCAudit", so a player's profile can never
    ///       be created over or deleted by a probe. Use from the main menu.
    ///   openlevel &lt;id&gt;   open any level by id from level select (LevelSelectEditor.method_20
    ///       without the unlock check or the story screen) — it loads / saves that level's solution
    ///       on the CURRENT profile, so only on a throwaway one.
    ///   fire list | fire &lt;n&gt; &lt;code&gt;   the open level's special buildings (Class598, reading
    ///       order) / raise building n's event `code` (method_15), as the building itself would.
    ///       Only while PAUSED: a destroyed enemy wins 700 cycles later, and a win submits scores
    ///       and can unlock Steam achievements — stop the run after.
    /// </summary>
    internal static class LiveAudit
    {
        private const string Prefix = "SCAudit";

        public static string Profile(string argument)
        {
            var parts = (argument ?? "").Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            string verb = parts.Length > 0 ? parts[0] : "list", name = parts.Length > 1 ? parts[1].Trim() : null;
            if (verb == "list")
            {
                var sb = new StringBuilder();
                foreach (var p in Locals.smethod_5())
                    sb.Append(ReferenceEquals(p, Locals.smethod_0()) ? "* " : "  ").Append(p.string_0).Append('\n');
                return sb.ToString();
            }
            if (string.IsNullOrEmpty(name)) return "[usage: profile list|create|select|delete <name>]\n";
            if (!(GameApi.TopScreen() is MainMenuEditor)) return "[use from the main menu]\n";
            var existing = Locals.smethod_5().FirstOrDefault(p => string.Equals(p.string_0, name, StringComparison.OrdinalIgnoreCase));
            switch (verb)
            {
                case "create":
                    if (!name.StartsWith(Prefix, StringComparison.Ordinal)) return "[only " + Prefix + "* names]\n";
                    if (existing != null) return "[exists]\n";
                    Locals.smethod_7(new Class436(name, (int)((Enum147)0).smethod_0()));
                    return "created " + name + "\n";
                case "select":
                    if (existing == null) return "[no such profile]\n";
                    if (!ReferenceEquals(existing, Locals.smethod_0()))
                    {
                        Locals.smethod_1(existing);
                        Locals.smethod_0().smethod_0();
                        Levels.smethod_16();
                    }
                    return "selected " + existing.string_0 + "\n";
                case "delete":
                    if (!name.StartsWith(Prefix, StringComparison.Ordinal)) return "[only " + Prefix + "* names]\n";
                    if (existing == null) return "[no such profile]\n";
                    if (ReferenceEquals(existing, Locals.smethod_0())) return "[select another profile first]\n";
                    Locals.smethod_6(existing);
                    return "deleted " + existing.string_0 + "\n";
            }
            return "[usage: profile list|create|select|delete <name>]\n";
        }

        public static string OpenLevel(string id)
        {
            id = (id ?? "").Trim();
            if (!(GameApi.TopScreen() is LevelSelectEditor)) return "[open level select first]\n";
            if (Locals.smethod_0() == null || !Locals.smethod_0().string_0.StartsWith(Prefix, StringComparison.Ordinal))
                return "[only on an " + Prefix + "* profile]\n";
            if (!Levels.dictionary_5.ContainsKey(id)) return "[no such level id]\n";
            var level = Levels.smethod_14(id);
            Class53.smethod_1(level);
            return "opened " + id + "\n";
        }

        public static string Fire(string argument)
        {
            var level = DefenseText.Level;
            if (level == null) return "[no defense level open]\n";
            var buildings = PipelineText.Components(level.pipeline_0).Select(kv => kv.Key).OfType<Class598>().ToList();
            var parts = (argument ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts[0] == "list")
            {
                var sb = new StringBuilder();
                for (int i = 0; i < buildings.Count; i++)
                    sb.Append(i).Append(": ").Append(GameNames.DeobTypeName(buildings[i].GetType())).Append(" at ")
                      .Append(level.pipeline_0.method_9(buildings[i])?.ToString() ?? "?").Append('\n');
                return sb.ToString();
            }
            if ((int)Class258.smethod_16() != 2) return "[pause the run first]\n";
            int n, code;
            if (parts.Length < 2 || !int.TryParse(parts[0], out n) || !int.TryParse(parts[1], out code) || n < 0 || n >= buildings.Count)
                return "[usage: fire list | fire <n> <code>]\n";
            buildings[n].method_15(code, null);
            return "fired " + code + " on " + n + "\n";
        }
    }
}
#endif
