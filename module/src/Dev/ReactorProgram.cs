#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Impeller;
using SpaceChem;
using SpaceChem.Pipeline;
using SpaceChem.Reactor;
using SpeechChem.Game;
using ReactorModel = SpaceChem.Reactor.Reactor;

namespace SpeechChem.Dev
{
    /// <summary>
    /// DEBUG probe "prog": read and write a pipeline reactor's whole program at once (reactor n =
    /// the pipeline's numbering, 1-based cells like the grid readout). Edits go through the same
    /// model calls as the reactor editor (Reactor.method_18 / method_21 + the worker's forget, one
    /// undo step), so the save sees exactly what a key edit leaves. Edits only while stopped.
    ///
    ///   prog &lt;n&gt;                         dump: one line per non-empty cell
    ///   prog &lt;n&gt; clear [r|b]             remove every instruction but START (one or both colours)
    ///   prog &lt;n&gt; set &lt;x&gt; &lt;y&gt; &lt;r|b&gt; &lt;spec&gt;  place; spec: start:DIR (moves the START), arrow:DIR,
    ///                                   in:a|b, out:p|w, grab, drop, gd, bond+, bond-, cw, ccw,
    ///                                   sync, sense:DIR:Symbol, ctrl:A-D:DIR, none (empties the instruction
    ///                                   slot), noarrow (empties the arrow slot)
    ///   prog &lt;n&gt; hw &lt;x&gt; &lt;y&gt; &lt;tx&gt; &lt;ty&gt;      move the hardware whose cell is x,y so its origin is tx,ty
    ///   prog &lt;n&gt; batch                   then one "set"/"hw"/"clear" line per following line
    ///   prog run &lt;slow|medium|fast|warp&gt;  start / change speed;  prog pause;  prog stop
    ///   prog status                       cycle and run state;  prog ctrl a|b|c|d flips a Reactor Control (F1-F4)
    /// </summary>
    internal static class ReactorProgram
    {
        public static string Run(string argument)
        {
            var lines = (argument ?? "").Replace("\r", "").Split('\n');
            var head = lines[0].Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (head.Length == 0) return "[prog <n> | prog run|pause|stop|status]\n";
            switch (head[0].ToLowerInvariant())
            {
                case "run": return RunAt(head.Length > 1 ? head[1] : "slow");
                case "pause": Class258.smethod_17((Enum16)2); return Status();
                case "stop": Class258.smethod_17((Enum16)0); return Status();
                case "status": return Status();
                case "ctrl":
                {
                    int i = char.ToUpperInvariant(head.Length > 1 ? head[1][0] : 'A') - 'A';
                    Class710.smethod_0().method_7((Enum111)i);
                    return "control " + (char)('A' + i) + " " + (Class710.smethod_0().method_6((Enum111)i) ? "on" : "off") + ", " + Status();
                }
            }
            if (!int.TryParse(head[0], out int n)) return "[bad reactor number]\n";
            var p = Class53.smethod_5<PipelineEditor>()?.pipeline_0;
            if (p == null) return "[no pipeline open]\n";
            var list = PipelineText.Reactors(p);
            if (n < 1 || n > list.Count) return "[no reactor " + n + " of " + list.Count + "]\n";
            var r = list[n - 1].class77_0?.reactor_0;
            if (r == null) return "[reactor has no model]\n";
            if (head.Length == 1) return Dump(r);
            if ((int)Class258.smethod_16() != 0) return "[stop the run first]\n";
            var sb = new StringBuilder();
            using (Locals.smethod_0().smethod_0().method_49())
            {
                if (head[1].Equals("batch", StringComparison.OrdinalIgnoreCase))
                {
                    for (int i = 1; i < lines.Length; i++)
                    {
                        var t = lines[i].Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (t.Length == 0 || t[0].StartsWith("#")) continue;
                        sb.Append(Edit(r, t, 0)).Append('\n');
                    }
                }
                else sb.Append(Edit(r, head, 1)).Append('\n');
            }
            return sb.ToString();
        }

        private static string RunAt(string speed)
        {
            SimulatorSpeed s;
            switch (speed.ToLowerInvariant())
            {
                case "medium": s = SimulatorSpeed.Medium; break;
                case "fast": s = SimulatorSpeed.Fast; break;
                case "warp": s = SimulatorSpeed.WarpSpeed; break;
                default: s = SimulatorSpeed.Slow; break;
            }
            Class258.smethod_15(s);
            return Status();
        }

        private static string Status()
            => "cycle " + Class258.int_1 + ", state " + (int)Class258.smethod_16() + ", speed " + Class258.smethod_14() + "\n";

        private static string Dump(ReactorModel r)
        {
            var size = r.method_1();
            var sb = new StringBuilder();
            for (int y = 0; y < size.int_1; y++)
                for (int x = 0; x < size.int_0; x++)
                {
                    var c = new Vector2i(x, y);
                    var parts = new List<string>();
                    Add(parts, "R", r.method_15(c, (Enum114)ReactorText.Red) as Instruction);
                    Add(parts, "RA", r.method_15(c, (Enum114)ReactorText.RedArrow) as Instruction);
                    Add(parts, "B", r.method_15(c, (Enum114)ReactorText.Blue) as Instruction);
                    Add(parts, "BA", r.method_15(c, (Enum114)ReactorText.BlueArrow) as Instruction);
                    if (r.method_15(c, (Enum114)ReactorText.Background) is ReactorFeature f)
                    {
                        var at = r.method_19(f);
                        bool origin = at.HasValue && at.Value.vector2i_0.int_0 == x && at.Value.vector2i_0.int_1 == y;
                        parts.Add("HW " + ReactorText.FeatureLabel(f) + (origin ? "" : " (part)"));
                    }
                    if (parts.Count > 0) sb.Append(x + 1).Append(',').Append(y + 1).Append(": ").Append(string.Join(" | ", parts.ToArray())).Append('\n');
                }
            return sb.Length == 0 ? "(empty)\n" : sb.ToString();
        }

        private static void Add(List<string> parts, string tag, Instruction i)
        {
            if (i != null) parts.Add(tag + " " + ReactorText.Label(i));
        }

        private static void Forget(ReactorMember m) => Locals.smethod_0().smethod_0().method_72(m);

        private static string Edit(ReactorModel r, string[] t, int at)
        {
            string verb = t[at].ToLowerInvariant();
            try
            {
                if (verb == "clear")
                {
                    string which = t.Length > at + 1 ? t[at + 1].ToLowerInvariant() : "rb";
                    int removed = 0;
                    foreach (var m in r.method_0().ToList())
                    {
                        if (!(m is Instruction) || m is StartInstruction) continue;
                        var bin = r.method_19(m);
                        if (!bin.HasValue) continue;
                        int binLayer = (int)bin.Value.enum114_0;
                        bool red = (binLayer & ReactorText.AllRed) != 0;
                        if (red ? which.IndexOf('r') < 0 : which.IndexOf('b') < 0) continue;
                        r.method_21(m);
                        Forget(m);
                        removed++;
                    }
                    return "cleared " + removed;
                }
                if (verb == "hw")
                {
                    var from = new Vector2i(int.Parse(t[at + 1]) - 1, int.Parse(t[at + 2]) - 1);
                    var to = new Vector2i(int.Parse(t[at + 3]) - 1, int.Parse(t[at + 4]) - 1);
                    var f = r.method_15(from, (Enum114)ReactorText.Background) as ReactorFeature;
                    if (f == null) return "[no hardware at " + t[at + 1] + "," + t[at + 2] + "]";
                    var size = r.method_1();
                    foreach (Vector2i off in f)
                    {
                        int x = to.int_0 + off.int_0, y = to.int_1 + off.int_1;
                        if (x < 0 || y < 0 || x >= size.int_0 || y >= size.int_1) return "[off the grid]";
                        var there = r.method_17(new ReactorBin(new Vector2i(x, y), (Enum114)ReactorText.Background));
                        if (there != null && there != f) return "[blocked at " + (x + 1) + "," + (y + 1) + "]";
                    }
                    var b = new ReactorBin(to, (Enum114)ReactorText.Background);
                    r.method_18(b, f);
                    f.vmethod_1(b);
                    return "moved " + ReactorText.FeatureLabel(f) + " to " + t[at + 3] + "," + t[at + 4];
                }
                if (verb != "set") return "[unknown verb " + verb + "]";
                int cx = int.Parse(t[at + 1]) - 1, cy = int.Parse(t[at + 2]) - 1;
                bool isRed = t[at + 3].StartsWith("r", StringComparison.OrdinalIgnoreCase);
                string spec = t[at + 4];
                var cell = new Vector2i(cx, cy);
                int instrLayer = isRed ? ReactorText.Red : ReactorText.Blue;
                int arrowLayer = isRed ? ReactorText.RedArrow : ReactorText.BlueArrow;
                if (spec == "none" || spec == "noarrow")
                {
                    var old = r.method_15(cell, (Enum114)(spec == "none" ? instrLayer : arrowLayer));
                    if (old == null || old is StartInstruction) return "nothing removed at " + (cx + 1) + "," + (cy + 1);
                    r.method_21(old);
                    Forget(old);
                    return "removed at " + (cx + 1) + "," + (cy + 1);
                }
                var ps = spec.Split(':');
                Instruction instr;
                int layer = instrLayer;
                switch (ps[0].ToLowerInvariant())
                {
                    case "start":
                    {
                        StartInstruction start = null;
                        foreach (var m in r.method_0())
                            if (m is StartInstruction s0 && r.method_19(m) is ReactorBin sb0 && (int)sb0.enum114_0 == instrLayer) start = s0;
                        if (start == null) return "[no START]";
                        var bin0 = new ReactorBin(cell, (Enum114)instrLayer);
                        var ex = r.method_17(bin0);
                        if (ex != null && ex != start) { r.method_21(ex); Forget(ex); }
                        start.vmethod_6(Dir(ps[1]));
                        r.method_18(bin0, start);
                        start.vmethod_1(bin0);
                        return "START at " + (cx + 1) + "," + (cy + 1);
                    }
                    case "arrow": instr = new ArrowInstruction(r, Dir(ps[1])); layer = arrowLayer; break;
                    case "in": instr = new InputInstruction(r, (Enum120)(ps[1] == "b" ? 1 : 0)); break;
                    case "out": instr = new OutputInstruction(r, (Enum120)(ps[1] == "w" ? 1 : 0)); break;
                    case "gd": instr = new GrabInstruction(r, (Enum85)0); break;
                    case "grab": instr = new GrabInstruction(r, (Enum85)1); break;
                    case "drop": instr = new GrabInstruction(r, (Enum85)2); break;
                    case "bond+": instr = new BondInstruction(r, (Enum146)0); break;
                    case "bond-": instr = new BondInstruction(r, (Enum146)1); break;
                    case "cw": instr = new RotateInstruction(r, (Enum141)0); break;
                    case "ccw": instr = new RotateInstruction(r, (Enum141)1); break;
                    case "sync": instr = new Class663(r); break;
                    case "ctrl": instr = new ControlInstruction(r, (Enum111)(char.ToUpperInvariant(ps[1][0]) - 'A'), Dir(ps[2])); break;
                    case "sense":
                    {
                        Element e = (Element)AtomicNumber(ps[2]);
                        instr = new SensorInstruction(r, new Atom(e), Dir(ps[1]));
                        break;
                    }
                    default: return "[bad spec " + spec + "]";
                }
                var binI = new ReactorBin(cell, (Enum114)layer);
                var existing = r.method_17(binI);
                if (existing is StartInstruction) return "[START occupies " + (cx + 1) + "," + (cy + 1) + "]";
                if (existing != null) { r.method_21(existing); Forget(existing); }
                r.method_18(binI, instr);
                return (isRed ? "red " : "blue ") + ReactorText.Label(instr) + " at " + (cx + 1) + "," + (cy + 1);
            }
            catch (Exception ex)
            {
                return "[" + string.Join(" ", t) + ": " + ex.GetType().Name + " " + ex.Message + "]";
            }
        }

        private static int AtomicNumber(string symbol)
        {
            switch (symbol.ToLowerInvariant())
            {
                case "h": return 1;
                case "c": return 6;
                case "n": return 7;
                case "o": return 8;
                case "fe": return 26;
                case "al": return 13;
                case "cr": return 24;
                default: return int.Parse(symbol);
            }
        }

        private static Enum153 Dir(string d)
        {
            switch (d.ToLowerInvariant())
            {
                case "u": case "up": return Enum153.Up;
                case "d": case "down": return Enum153.Down;
                case "l": case "left": return Enum153.Left;
                case "r": case "right": return Enum153.Right;
                default: return Enum153.None;
            }
        }
    }
}
#endif
