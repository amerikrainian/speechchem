#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Impeller;
using SpaceChem;
using SpaceChem.Levels;
using SpaceChem.Pipeline;
using SpeechChem.Game;

namespace SpeechChem.Dev
{
    /// <summary>
    /// DEBUG static audit of every defense level (probe "defense audit"): each DefenseLevelEditor
    /// subclass is BUILT IN MEMORY — never pushed, never through Levels.smethod_14 (which loads and
    /// saves a solution) — and the mod's generic defense readers run on it: the enemy found by type,
    /// its parts entry, a footprint from one draw onto a throwaway SpriteBatch, the graph's labelled
    /// ranges and their peaks, every special building's meter label and the event codes its code
    /// can raise (read from its IL: the constant pushed before each Class598.method_15 call), which
    /// of those the Events / States tables cover, and whether the enemy's attack has a body.
    ///
    /// Building a level registers its enemy and buildings with the game's clock (Class258's static
    /// delegates and list) and resets globals (GoalTracker defense mode / Control Center health /
    /// flags, Class83.bool_1, the tutorial list Class424.list_0): all are saved first and restored
    /// after, whatever happens. Refused unless the run is stopped. The report names enemies and
    /// levels (spoilers): it is for the developer, never spoken.
    /// </summary>
    internal static class DefenseAudit
    {
        public static string Run()
        {
            if ((int)Class258.smethod_16() != 0) return "[stop the run first]\n";
            var sb = new StringBuilder();
            var types = GameTypes();
            var levels = types.Where(t => t.IsSubclassOf(typeof(DefenseLevelEditor)) && !t.IsAbstract).OrderBy(t => t.Name).ToList();
            var buildings = types.Where(t => t.IsSubclassOf(typeof(Class598)) && !t.IsAbstract).ToList();
            var enemies = types.Where(t => t.IsSubclassOf(typeof(Class310)) && !t.IsAbstract).ToList();

            sb.Append("== special buildings: event codes (from IL) / table coverage\n");
            foreach (var b in buildings)
            {
                var codes = EventCodes(b);
                sb.Append(Deob(b)).Append(": codes ");
                sb.Append(codes.Count == 0 ? "none" : string.Join(", ", codes.Select(c => c.HasValue
                    ? c.Value + (DefenseText.HasEvent(b, c.Value) ? " (table)" : " (NO ENTRY)")
                    : "? (not a constant)").ToArray()));
                sb.Append(DefenseText.HasState(b) ? "; state entry" : "").Append('\n');
            }
            sb.Append("== enemies\n");
            foreach (var e in enemies)
            {
                var attack = Expr.OverrideOf(e, Expr.MethodOf(() => default(Class310).vmethod_2(0)));
                sb.Append(Deob(e)).Append(": parts ").Append(DefenseText.HasParts(e) ? "table" : "none")
                  .Append(", body ").Append(DefenseText.HasBody(e) ? "table" : "drawn footprint")
                  .Append(", state ").Append(DefenseText.HasEnemyState(e) ? "table" : "none")
                  .Append(", attack ").Append(HasBody(attack) ? "has a body" : "empty").Append('\n');
            }

            foreach (var t in levels)
            {
                sb.Append("== level ").Append(Deob(t)).Append('\n');
                var saved = GameState.Save();
                try { AuditLevel(t, sb); }
                catch (Exception ex) { sb.Append("  [failed: ").Append(ex.GetType().Name).Append(": ").Append(ex.Message).Append("]\n"); }
                finally { saved.Restore(); }
            }
            return sb.ToString();
        }

        /// <summary>The open defense level as the mod reads it (names included — never spoken).</summary>
        public static string Status()
        {
            var level = DefenseText.Level;
            if (level == null) return "[no defense level open]\n";
            var sb = new StringBuilder();
            var enemy = DefenseText.Enemy(level);
            sb.Append("cycle ").Append(Class258.int_1).Append(", state ").Append((int)Class258.smethod_16())
              .Append(", base ").Append(GoalTracker.int_1).Append('\n');
            if (enemy != null)
            {
                sb.Append("enemy ").Append(Deob(enemy.GetType())).Append(": ").Append(DefenseText.Span(enemy));
                string parts = DefenseText.PartsText(enemy), state = DefenseText.EnemyState(level, enemy);
                if (parts != null) sb.Append("; ").Append(parts);
                if (state != null) sb.Append("; ").Append(state);
                if (DefenseText.Defeated(enemy)) sb.Append("; destroyed");
                sb.Append('\n');
            }
            foreach (var row in DefenseText.GraphRows()) sb.Append("graph ").Append(row).Append('\n');
            foreach (var kv in PipelineText.Components(level.pipeline_0))
            {
                string meter = DefenseText.Meter(kv.Key);
                if (meter != null) sb.Append(PipelineText.Name(level.pipeline_0, kv.Key)).Append(": ").Append(meter).Append('\n');
            }
            return sb.ToString();
        }

        private static void AuditLevel(Type t, StringBuilder sb)
        {
            var level = (DefenseLevelEditor)Activator.CreateInstance(t);
            sb.Append("  name / title: ").Append(DefenseText.EnemyName(level)).Append(" / ").Append(DefenseText.EnemyTitle(level)).Append('\n');
            var enemy = DefenseText.Enemy(level);
            sb.Append("  enemy: ").Append(enemy == null ? "none found" : Deob(enemy.GetType())).Append('\n');
            if (enemy != null)
            {
                var batch = new SpriteBatch();
                batch.method_0();
                try { enemy.vmethod_3(batch); }
                catch (Exception ex) { sb.Append("  draw failed: ").Append(ex.Message).Append('\n'); }
                var rects = Patches.DefenseCapture.Footprint(enemy);
                sb.Append("  footprint: ").Append(rects == null ? "none captured" : rects.Count + " sprites, " + DefenseText.Span(enemy)).Append('\n');
                string parts = DefenseText.PartsText(enemy);
                if (parts != null) sb.Append("  parts: ").Append(parts).Append('\n');
                string state = DefenseText.EnemyState(level, enemy);
                if (state != null) sb.Append("  state: ").Append(state).Append('\n');
            }

            // The graph: labelled ranges over the level's current waveform.
            try
            {
                var marks = Patches.DefenseCapture.CaptureGraphLabels(level);
                var wave = level.vmethod_8();
                sb.Append("  graph: ").Append(marks == null ? "labels failed" : marks.Count + " labelled ranges").Append(wave == null ? ", no waveform" : "").Append('\n');
                if (marks != null)
                    foreach (var m in marks)
                        sb.Append("    ").Append(m.Label).Append(" x ").Append(m.X0).Append('-').Append(m.X1).Append('\n');
            }
            catch (Exception ex) { sb.Append("  graph failed: ").Append(ex.Message).Append('\n'); }

            // Special buildings on the map, and the shelf's.
            foreach (var kv in level.pipeline_0.dictionary_1)
            {
                if (!(kv.Key is Class598 b)) continue;
                var meter = Patches.DefenseCapture.CaptureMeter(b);
                sb.Append("  building ").Append(Deob(b.GetType())).Append(" \"").Append(b.string_1).Append("\" at ").Append(kv.Value.int_0).Append(',').Append(kv.Value.int_1)
                  .Append(", meter ").Append(meter == null ? "none" : string.Join(", ", meter.Select(x => "\"" + x.Key + "\"").ToArray()))
                  .Append(", accepts ").Append(string.Join("; ", b.list_0.Select(a => MoleculeText.NameAndFormula(a.molecule_0) + " on input " + a.int_0).ToArray())).Append('\n');
            }
            var shelf = level.hashSet_0.Where(s => s.IsSubclassOf(typeof(Class598))).Select(Deob).ToArray();
            if (shelf.Length > 0) sb.Append("  shelf special buildings: ").Append(string.Join(", ", shelf)).Append('\n');
        }

        // ---- the game's global state a level's construction touches ----

        private sealed class GameState
        {
            private Class258.Delegate22 d22; private Class258.Delegate23 d23; private Class258.Delegate25 d25; private Class258.Delegate26 d26; private Class258.Delegate27 d27;
            private List<Class258.Delegate24> list161;
            private Enum93 mode; private bool frozen, flag1; private GoalTracker.Enum158 outcome; private int health; private Score score;
            private bool level83;
            private List<Class288> tutorial;

            public static GameState Save() => new GameState
            {
                d22 = Class258.delegate22_0, d23 = Class258.delegate23_0, d25 = Class258.delegate25_0,
                d26 = Class258.delegate26_0, d27 = Class258.delegate27_0,
                list161 = new List<Class258.Delegate24>(Class258.class161_0),
                mode = GoalTracker.enum93_0, frozen = GoalTracker.bool_0, flag1 = GoalTracker.bool_1,
                outcome = GoalTracker.enum158_0, health = GoalTracker.int_1, score = GoalTracker.score_0,
                level83 = Class83.smethod_11(),
                tutorial = new List<Class288>(Class424.list_0),
            };

            public void Restore()
            {
                Class258.delegate22_0 = d22; Class258.delegate23_0 = d23; Class258.delegate25_0 = d25;
                Class258.delegate26_0 = d26; Class258.delegate27_0 = d27;
                Class258.class161_0.Clear();
                foreach (var d in list161) Class258.class161_0.AddLast(d);
                GoalTracker.enum93_0 = mode; GoalTracker.bool_0 = frozen; GoalTracker.bool_1 = flag1;
                GoalTracker.enum158_0 = outcome; GoalTracker.int_1 = health; GoalTracker.score_0 = score;
                Class83.smethod_12(level83);
                Class424.list_0.Clear();
                Class424.list_0.AddRange(tutorial);
            }
        }

        // ---- IL: the event codes a building raises ----

        private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null))
            .GroupBy(o => o.Value).ToDictionary(g => g.Key, g => g.First());

        /// <summary>The codes passed to Class598.method_15 anywhere in the type (and its nested
        /// closures); null = not a constant.</summary>
        private static List<int?> EventCodes(Type t)
        {
            var raise = Expr.MethodOf(() => default(Class598).method_15(0, null));
            var codes = new List<int?>();
            foreach (var type in new[] { t }.Concat(t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)))
                foreach (var m in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Cast<MethodBase>()
                    .Concat(type.GetConstructors(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)))
                {
                    var ins = Decode(m);
                    for (int i = 0; i < ins.Count; i++)
                    {
                        if (!(ins[i].Operand is MethodBase called) || called != raise) continue;
                        // this, code, obj, call: the code is two instructions back when obj is one.
                        int? code = i >= 2 ? ConstOf(ins[i - 2]) : null;
                        if (!codes.Contains(code)) codes.Add(code);
                    }
                }
            return codes;
        }

        private struct Instr
        {
            public OpCode Op;
            public object Operand;
        }

        private static int? ConstOf(Instr i)
        {
            var op = i.Op;
            if (op == OpCodes.Ldc_I4_M1) return -1;
            if (op.Value >= OpCodes.Ldc_I4_0.Value && op.Value <= OpCodes.Ldc_I4_8.Value) return op.Value - OpCodes.Ldc_I4_0.Value;
            if (op == OpCodes.Ldc_I4_S || op == OpCodes.Ldc_I4) return Convert.ToInt32(i.Operand);
            if (op == OpCodes.Ldsfld && i.Operand is FieldInfo f && f.IsStatic && f.FieldType == typeof(int))
            {
                try { return (int)f.GetValue(null); } catch { return null; }
            }
            return null;
        }

        private static List<Instr> Decode(MethodBase m)
        {
            var list = new List<Instr>();
            byte[] il;
            try { il = m.GetMethodBody()?.GetILAsByteArray(); } catch { return list; }
            if (il == null) return list;
            int pos = 0;
            while (pos < il.Length)
            {
                short value = il[pos++];
                if (value == 0xFE && pos < il.Length) value = (short)(0xFE00 | il[pos++]);
                OpCode op;
                if (!OpCodesByValue.TryGetValue(value, out op)) break;
                object operand = null;
                switch (op.OperandType)
                {
                    case OperandType.InlineNone: break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineVar: pos += 1; break;
                    case OperandType.ShortInlineI: operand = (sbyte)il[pos]; pos += 1; break;
                    case OperandType.InlineVar: pos += 2; break;
                    case OperandType.InlineI: operand = BitConverter.ToInt32(il, pos); pos += 4; break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR: pos += 8; break;
                    case OperandType.ShortInlineR:
                    case OperandType.InlineBrTarget:
                    case OperandType.InlineSig:
                    case OperandType.InlineString:
                    case OperandType.InlineTok:
                    case OperandType.InlineType: pos += 4; break;
                    case OperandType.InlineSwitch: pos += 4 + 4 * BitConverter.ToInt32(il, pos); break;
                    case OperandType.InlineField:
                    case OperandType.InlineMethod:
                    {
                        int token = BitConverter.ToInt32(il, pos);
                        pos += 4;
                        try
                        {
                            var generic = m.DeclaringType != null && m.DeclaringType.IsGenericType ? m.DeclaringType.GetGenericArguments() : null;
                            operand = op.OperandType == OperandType.InlineField
                                ? (object)m.Module.ResolveField(token, generic, null)
                                : m.Module.ResolveMethod(token, generic, null);
                        }
                        catch { }
                        break;
                    }
                    default: pos += 4; break;
                }
                list.Add(new Instr { Op = op, Operand = operand });
            }
            return list;
        }

        private static bool HasBody(MethodInfo m)
        {
            try { var il = m.GetMethodBody()?.GetILAsByteArray(); return il != null && il.Length > 2; }
            catch { return true; }
        }

        private static Type[] GameTypes()
        {
            try { return typeof(Class598).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null).ToArray(); }
        }

        /// <summary>The deob name of a game type (the shipping name is obfuscated).</summary>
        private static string Deob(Type t) => SpeechChem.GameNames.DeobTypeName(t) ?? t.Name;
    }
}
#endif
