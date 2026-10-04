using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Impeller;
using SpaceChem;
using SpaceChem.Levels;
using SpaceChem.Pipeline;
using SpeechChem.Localization;
using SpeechChem.Narration;

namespace SpeechChem.Game
{
    /// <summary>
    /// Defense levels (DefenseLevelEditor: pipelines with an enemy, special buildings and the
    /// Reactor Controls panel), read GENERICALLY from what every one of them shares (user
    /// direction 2026-10-04: no bespoke work per enemy beyond naming):
    ///   The enemy: the Class310 the level holds (found by type among the level's fields); its name
    ///   and title (level vmethod_6 / vmethod_7, the intro card); its footprint = the rectangles its
    ///   own draw (vmethod_3) puts on the pipeline (Patches/DefenseCapture records them; particle
    ///   effects excluded), spoken as map columns / rows (cells 32 x 28 px); defeated (method_1).
    ///   Enemy health and attack timers are drawn nowhere except through the level's graph, so they
    ///   are not read as numbers.
    ///   Buildings' run meters: the bubble every building draws while running (vmethod_19, a
    ///   picture of a bar + a label) is rebuilt under capture — the bar's fraction (Class45.smethod_0)
    ///   and its label (Scene.smethod_4): "Pressure 37 percent", "Capacity 12 percent".
    ///   The graph (Class710, while a run is on): the current waveform (Class377, 100 samples,
    ///   normalised) and the labelled ranges of the level's vmethod_9 (brackets Class377.smethod_1,
    ///   labels smethod_2), each range read as its peaks in percent of the graph's height.
    /// What the game keeps only as meaning — which array of an enemy is its "motors", what event
    /// code 0 of an Oxygen Tank is, which of an enemy's sprites is its body, what its eye sprite
    /// says — lives in the small tables below (Parts, Events, Bodies, EnemyStates, States), the
    /// only per-enemy / per-building pieces; filled for every defense level from the decompile and
    /// checked by Dev/DefenseAudit.
    ///   Reactor Controls (Class710): four toggles, Enum111 0-3 = control A-D = F1-F4, shown in
    ///   place of the shelf / palette while a defense run is not stopped.
    /// </summary>
    internal static class DefenseText
    {
        public const int CellWidth = 32, CellHeight = 28, MapColumns = 32, MapRows = 22;

        public static DefenseLevelEditor Level
        {
            get { try { return Class53.smethod_5<DefenseLevelEditor>(); } catch { return null; } }
        }

        public static bool IsDefense => Level != null;

        /// <summary>The game shows Reactor Controls instead of the shelf / palette.</summary>
        public static bool ControlsShown => IsDefense && (int)Class258.smethod_16() != 0;

        // ---- the per-enemy / per-building tables (meaning only) ----

        private sealed class PartSpec
        {
            public Func<Class310, bool[]> Get;
            public string Key;     // "{n} of {total} motors intact"
            public string LostKey; // "motor destroyed"
        }

        /// <summary>Enemy parts the game tracks as a flag array (true = intact).</summary>
        private static readonly Dictionary<Type, PartSpec> Parts = new Dictionary<Type, PartSpec>
        {
            { typeof(Class313), new PartSpec { Get = e => ((Class313)e).bool_0, Key = "defense.part.motors", LostKey = "defense.part.motor.lost" } },
        };

        private sealed class EventSpec
        {
            public string Key;       // the action: "exploded"
            public string Suffix;    // after the building's name ("Thruster Controls: left")
            public bool Weapon;      // an attack on the enemy: report hit / missed
            public bool Continuous;  // raised every cycle while it lasts (a beam): logged once per
                                     // burst, "hit" once per burst, never "missed"
        }

        private static KeyValuePair<Type, int> Code(Type t, int code) => new KeyValuePair<Type, int>(t, code);

        /// <summary>Special buildings' event codes (Class598.method_15) — what each level's handler
        /// (its method_13 registration) does with them.</summary>
        private static readonly Dictionary<KeyValuePair<Type, int>, EventSpec> Events = new Dictionary<KeyValuePair<Type, int>, EventSpec>
        {
            { Code(typeof(Class605), Class605.int_1), new EventSpec { Key = "defense.act.exploded", Weapon = true } },
            { Code(typeof(Class599), Class599.int_1), new EventSpec { Key = "defense.act.fired", Weapon = true } },
            { Code(typeof(Class600), Class600.int_1), new EventSpec { Key = "defense.act.firing", Weapon = true, Continuous = true } },
            { Code(typeof(ParticleAcceleratorDraggable), ParticleAcceleratorDraggable.int_1), new EventSpec { Key = "defense.act.fired", Weapon = true } },
            { Code(typeof(Class601), 0), new EventSpec { Key = "defense.act.launched" } },
            { Code(typeof(Class603), 0), new EventSpec { Key = "defense.act.left", Suffix = ":" } },
            { Code(typeof(Class603), 1), new EventSpec { Key = "defense.act.right", Suffix = ":" } },
            { Code(typeof(Class604), 0), new EventSpec { Key = "defense.act.missile" } }, // hits later: the enemy's damage reports it
            { Code(typeof(Class607), 0), new EventSpec { Key = "defense.act.launched" } },
        };

        /// <summary>Where an enemy's BODY is drawn (pipeline pixels), when its drawing also holds
        /// attacks, backgrounds or nothing at all (the level draws it): an empty rectangle = not in
        /// sight. Enemies without an entry use everything their own draw puts on the map.</summary>
        private static readonly Dictionary<Type, Func<DefenseLevelEditor, Class310, Rectangle>> Bodies = new Dictionary<Type, Func<DefenseLevelEditor, Class310, Rectangle>>
        {
            { typeof(Class313), (l, e) => new Rectangle(((Class313)e).vector2i_0, Class313.class358_0.method_2()) },
            { typeof(Class311), (l, e) => new Rectangle(Class311.vector2i_0, new Vector2i(800, 558)) },
            { typeof(Class312), (l, e) => l is Class150 w ? Centred(w.vector2i_3, new Vector2i(123, 173)) : default(Rectangle) },
            { typeof(Class314), (l, e) => { var s = (Class314)e; return s.bool_0 && !s.method_1() ? Centred(s.vector2f_0, new Vector2i(368, 230)) : default(Rectangle); } },
            { typeof(Class315), (l, e) => Centred(Class315.vector2i_1, new Vector2i(375, 348)) },
            { typeof(Ktrechtasach), (l, e) => new Rectangle(((Ktrechtasach)e).method_12(), new Vector2i(382, 272)) },
            // The pyramid: its lower and upper sprites (not the offset shadow drawn first, nor the
            // lightning to the Control Center), each drawn around method_6 with its own origin.
            { typeof(Quororque), (l, e) =>
                {
                    var q = (Quororque)e;
                    var at = q.method_6();
                    return Union(new Rectangle(at - new Vector2i(256, 31), Quororque.class358_0.method_2()),
                                 new Rectangle(at - new Vector2i(76, 160), q.class249_0.method_2().method_2()));
                } },
        };

        private static Rectangle Union(Rectangle a, Rectangle b)
        {
            int x0 = Math.Min(a.vector2i_0.int_0, b.vector2i_0.int_0), y0 = Math.Min(a.vector2i_0.int_1, b.vector2i_0.int_1);
            int x1 = Math.Max(a.vector2i_0.int_0 + a.vector2i_1.int_0, b.vector2i_0.int_0 + b.vector2i_1.int_0);
            int y1 = Math.Max(a.vector2i_0.int_1 + a.vector2i_1.int_1, b.vector2i_0.int_1 + b.vector2i_1.int_1);
            return new Rectangle(new Vector2i(x0, y0), new Vector2i(x1 - x0, y1 - y0));
        }

        private static Rectangle Centred(Vector2i centre, Vector2i size) => new Rectangle(centre - size / 2, size);

        /// <summary>An enemy's visible state the game shows only as sprites ("eye open, red",
        /// "shield down"); a change while running is a run event.</summary>
        private static readonly Dictionary<Type, Func<DefenseLevelEditor, Class310, string>> EnemyStates = new Dictionary<Type, Func<DefenseLevelEditor, Class310, string>>
        {
            { typeof(Class311), (l, e) =>
                {
                    var b = (Class311)e;
                    if (b.method_1()) return null;
                    int eye = (int)b.enum92_0;
                    string text = Loc.T("defense.eye." + eye);
                    if (eye != 0) text += ", " + Loc.T("defense.colour." + (int)b.eyeColor_0);
                    return b.bool_0 ? text + ", " + Loc.T("defense.state.laser") : text;
                } },
            { typeof(Class312), (l, e) =>
                {
                    var w = l as Class150;
                    if (w == null || e.method_1()) return null;
                    var parts = new List<string>();
                    if (w.int_5 > 0) parts.Add(Loc.T("defense.state.phasing"));
                    if (w.int_4 > 0) parts.Add(Loc.T("defense.state.stunned"));
                    return parts.Count == 0 ? null : string.Join(", ", parts.ToArray());
                } },
            { typeof(Ktrechtasach), (l, e) =>
                {
                    var s = (Ktrechtasach)e;
                    if (s.method_1()) return null;
                    string mouth = Loc.T("defense.mouth." + Math.Max(0, Math.Min(2, s.int_3)));
                    return s.bool_0 ? Loc.T("defense.state.walking") + ", " + mouth : mouth;
                } },
            { typeof(Quororque), (l, e) =>
                {
                    var q = (Quororque)e;
                    if (q.method_1()) return null;
                    var parts = new List<string>();
                    int stage = q.class249_0.method_0();
                    if (stage > 0) parts.Add(Loc.T(stage == 1 ? "defense.state.damaged" : "defense.state.damaged2"));
                    if (q.bool_0) parts.Add(Loc.T("defense.state.shielddown"));
                    if (q.int_2 > 0) parts.Add(Loc.T("defense.state.lightning"));
                    return parts.Count == 0 ? null : string.Join(", ", parts.ToArray());
                } },
        };

        /// <summary>A building state the game shows only as a sprite change.</summary>
        private static readonly Dictionary<Type, Func<Draggable, string>> States = new Dictionary<Type, Func<Draggable, string>>
        {
            { typeof(Class605), d => ((Class605)d).bool_3 ? Loc.T("defense.exploded") : null },
        };

        /// <summary>Table coverage, for the dev audit.</summary>
        internal static bool HasParts(Type enemy) => Parts.ContainsKey(enemy);
        internal static bool HasEvent(Type building, int code) => Events.ContainsKey(Code(building, code));
        internal static bool HasState(Type building) => States.ContainsKey(building);
        internal static bool HasBody(Type enemy) => Bodies.ContainsKey(enemy);
        internal static bool HasEnemyState(Type enemy) => EnemyStates.ContainsKey(enemy);

        // ---- the enemy ----

        private static readonly ConditionalWeakTable<DefenseLevelEditor, Class310[]> EnemyCache = new ConditionalWeakTable<DefenseLevelEditor, Class310[]>();

        /// <summary>The level's enemy (the Class310 among its fields), or null (some defense
        /// levels have none).</summary>
        public static Class310 Enemy(DefenseLevelEditor level)
        {
            if (level == null) return null;
            Class310[] found;
            if (EnemyCache.TryGetValue(level, out found)) return found[0];
            found = new Class310[1];
            try
            {
                for (var t = level.GetType(); t != null && t != typeof(DefenseLevelEditor); t = t.BaseType)
                {
                    foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        if (typeof(Class310).IsAssignableFrom(f.FieldType) && f.GetValue(level) is Class310 e) { found[0] = e; break; }
                    if (found[0] != null) break;
                }
            }
            catch { }
            if (found[0] != null) EnemyCache.Add(level, found);
            return found[0];
        }

        public static string EnemyName(DefenseLevelEditor level)
        {
            try { return GameText.Speech(level?.vmethod_6()); } catch { return null; }
        }

        public static string EnemyTitle(DefenseLevelEditor level)
        {
            try { return GameText.Speech(level?.vmethod_7()); } catch { return null; }
        }

        public static bool Defeated(Class310 enemy)
        {
            try { return enemy != null && enemy.method_1(); } catch { return false; }
        }

        /// <summary>The enemy's visible state, or null (none, or no entry).</summary>
        public static string EnemyState(DefenseLevelEditor level, Class310 enemy)
        {
            Func<DefenseLevelEditor, Class310, string> f;
            if (enemy == null || !EnemyStates.TryGetValue(enemy.GetType(), out f)) return null;
            try { return f(enemy.defenseLevelEditor_0 ?? level, enemy); } catch { return null; }
        }

        /// <summary>The enemy's part flags (true = intact), or null when it has no parts entry.</summary>
        public static bool[] PartFlags(Class310 enemy)
        {
            if (enemy == null) return null;
            PartSpec spec;
            if (!Parts.TryGetValue(enemy.GetType(), out spec)) return null;
            try { return spec.Get(enemy); } catch { return null; }
        }

        public static int Intact(bool[] flags)
        {
            int n = 0;
            if (flags != null) foreach (bool b in flags) if (b) n++;
            return n;
        }

        /// <summary>"2 of 3 motors intact", or null.</summary>
        public static string PartsText(Class310 enemy)
        {
            var flags = PartFlags(enemy);
            if (flags == null) return null;
            return Loc.T(Parts[enemy.GetType()].Key, new { n = Intact(flags), total = flags.Length });
        }

        /// <summary>"motor destroyed" for the enemy's part kind, or null.</summary>
        public static string PartLost(Class310 enemy)
        {
            PartSpec spec;
            return enemy != null && Parts.TryGetValue(enemy.GetType(), out spec) ? Loc.T(spec.LostKey) : null;
        }

        /// <summary>The rectangles that stand for the enemy on the map: its body entry, else what
        /// its own draw put there (null before the first draw).</summary>
        private static List<Rectangle> Shape(Class310 enemy)
        {
            if (enemy == null) return null;
            Func<DefenseLevelEditor, Class310, Rectangle> body;
            if (Bodies.TryGetValue(enemy.GetType(), out body))
            {
                try { return new List<Rectangle> { body(enemy.defenseLevelEditor_0 ?? Level, enemy) }; } catch { return null; }
            }
            return Patches.DefenseCapture.Footprint(enemy);
        }

        private static bool Empty(Rectangle r) => r.vector2i_1.int_0 <= 0 || r.vector2i_1.int_1 <= 0;

        /// <summary>The cells (0-based, on the map) one drawn rectangle covers.</summary>
        private static void CellsOf(Rectangle r, out int x0, out int x1, out int y0, out int y1)
        {
            x0 = FloorDiv(r.vector2i_0.int_0, CellWidth);
            x1 = FloorDiv(r.vector2i_0.int_0 + r.vector2i_1.int_0 - 1, CellWidth);
            y0 = FloorDiv(r.vector2i_0.int_1, CellHeight);
            y1 = FloorDiv(r.vector2i_0.int_1 + r.vector2i_1.int_1 - 1, CellHeight);
        }

        public static bool Covers(Class310 enemy, Vector2i cell)
        {
            var rects = Shape(enemy);
            if (rects == null) return false;
            foreach (var r in rects)
            {
                if (Empty(r)) continue;
                int x0, x1, y0, y1;
                CellsOf(r, out x0, out x1, out y0, out y1);
                if (cell.int_0 >= x0 && cell.int_0 <= x1 && cell.int_1 >= y0 && cell.int_1 <= y1) return true;
            }
            return false;
        }

        /// <summary>"columns 21 to 32, rows 3 to 11" (1-based, clipped to the map), "off the map",
        /// or null before the enemy's first draw.</summary>
        public static string Span(Class310 enemy)
        {
            var rects = Shape(enemy);
            if (rects == null) return null;
            int ax0 = int.MaxValue, ax1 = int.MinValue, ay0 = int.MaxValue, ay1 = int.MinValue;
            foreach (var r in rects)
            {
                if (Empty(r)) continue;
                int x0, x1, y0, y1;
                CellsOf(r, out x0, out x1, out y0, out y1);
                x0 = Math.Max(0, x0); x1 = Math.Min(MapColumns - 1, x1);
                y0 = Math.Max(0, y0); y1 = Math.Min(MapRows - 1, y1);
                if (x0 > x1 || y0 > y1) continue;
                ax0 = Math.Min(ax0, x0); ax1 = Math.Max(ax1, x1);
                ay0 = Math.Min(ay0, y0); ay1 = Math.Max(ay1, y1);
            }
            if (ax0 == int.MaxValue) return Loc.T("defense.offmap");
            return Loc.T("defense.span", new { x0 = ax0 + 1, x1 = ax1 + 1, y0 = ay0 + 1, y1 = ay1 + 1 });
        }

        /// <summary>The enemy's position for movement events: the map cell (1-based, clamped to
        /// the map) of the top-left corner of its body — the corner the Components table gives for
        /// buildings, and the leading edge of an enemy moving left or up. Null when no part of it is
        /// on the map (not in sight, or before its first draw).</summary>
        public static Vector2i? Anchor(Class310 enemy)
        {
            var rects = Shape(enemy);
            if (rects == null) return null;
            int x0 = int.MaxValue, y0 = int.MaxValue;
            bool onMap = false;
            foreach (var r in rects)
            {
                if (Empty(r)) continue;
                int ax0, ax1, ay0, ay1;
                CellsOf(r, out ax0, out ax1, out ay0, out ay1);
                if (ax1 < 0 || ay1 < 0 || ax0 >= MapColumns || ay0 >= MapRows) continue;
                onMap = true;
                x0 = Math.Min(x0, ax0);
                y0 = Math.Min(y0, ay0);
            }
            if (!onMap) return null;
            return new Vector2i(Math.Max(0, Math.Min(MapColumns - 1, x0)) + 1, Math.Max(0, Math.Min(MapRows - 1, y0)) + 1);
        }

        /// <summary>A movement event, as terse as the grid readouts (user design 2026-10-04): the
        /// enemy's name, then only what changed — "Isambard MMD 21" (column), "… row 5" (row),
        /// "… 21, 5" (both, or on coming into sight), "… off the map". Null when nothing changed.</summary>
        public static NarrationEvent MoveEvent(string name, Vector2i? before, Vector2i? now)
        {
            var e = new NarrationEvent("defense.move").Part("enemy", name);
            if (now == null) return before == null ? null : e.Part("column", Loc.T("defense.offmap"));
            var n = now.Value;
            if (before == null || (before.Value.int_0 != n.int_0 && before.Value.int_1 != n.int_1))
                return e.Part("column", n.int_0.ToString(), ",").Part("row", n.int_1.ToString());
            if (before.Value.int_0 != n.int_0) return e.Part("column", n.int_0.ToString());
            if (before.Value.int_1 != n.int_1) return e.Part("row", Loc.T("defense.row", new { row = n.int_1 }));
            return null;
        }

        // ---- buildings' run meters ----

        /// <summary>A building's run meter ("Pressure 37 percent", "exploded"), or null when it has
        /// none or the run is stopped. Reactors' bubbles are their waldos' text (read elsewhere).</summary>
        public static string Meter(Draggable d)
        {
            if (d == null || d is ReactorDraggable || (int)Class258.smethod_16() == 0) return null;
            Func<Draggable, string> state;
            if (States.TryGetValue(d.GetType(), out state))
            {
                try { string s = state(d); if (s != null) return s; } catch { }
            }
            var meters = Patches.DefenseCapture.CaptureMeter(d);
            if (meters == null) return null;
            var parts = new List<string>();
            foreach (var m in meters)
            {
                int percent = (int)Math.Round(Math.Max(0f, Math.Min(1f, m.Value)) * 100f);
                parts.Add(string.IsNullOrEmpty(m.Key)
                    ? Loc.T("run.percent", new { percent })
                    : Loc.T("defense.status", new { label = m.Key, percent }));
            }
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>The Control Center's name as the progress panel titles it.</summary>
        public static string BaseName() => Screens.Common.ProgressSection.ProgressLabel();

        // ---- special-building events ----

        /// <summary>"Oxygen Tank 2 exploded", or "Oxygen Tank 2, event 3" for a code not in the
        /// table; <paramref name="weapon"/> = the event attacks the enemy, <paramref name="continuous"/>
        /// = raised every cycle while it lasts.</summary>
        public static NarrationEvent BuildingEvent(Class598 b, int code, out bool weapon, out bool continuous)
        {
            weapon = false;
            continuous = false;
            var e = new NarrationEvent("defense.event");
            string name = PipelineText.Name(b.pipeline_0, b);
            EventSpec spec;
            if (Events.TryGetValue(Code(b.GetType(), code), out spec))
            {
                weapon = spec.Weapon;
                continuous = spec.Continuous;
                return e.Part("building", name, spec.Suffix).Part("action", Loc.T(spec.Key));
            }
            return e.Part("building", name, ",").Part("action", Loc.T("defense.act.code", new { code }));
        }

        // ---- the enemy graph (the Reactor Controls panel, while running) ----

        /// <summary>Each labelled range of the graph: "ELECTRIC MOTORS: 100, 98, 24 percent".</summary>
        public static List<string> GraphRows()
        {
            var rows = new List<string>();
            try
            {
                var panel = Class710.class710_0;
                var level = Level;
                if (panel == null || level == null || !panel.struct116_0.bool_0) return rows;
                var wave = panel.struct116_0.method_0().double_0;
                var marks = Patches.DefenseCapture.CaptureGraphLabels(level);
                if (marks == null) return rows;
                foreach (var mark in marks)
                {
                    var values = new List<string>();
                    foreach (var p in Peaks(wave, Index(mark.X0), Index(mark.X1))) values.Add(((int)Math.Round(p * 100)).ToString());
                    rows.Add(Loc.T("defense.graph.row", new { label = mark.Label, values = string.Join(", ", values.ToArray()) }));
                }
            }
            catch { }
            return rows;
        }

        /// <summary>The waveform sample under a graph x (Class377.smethod_0: x = 3 + 393 i / 100).</summary>
        private static int Index(int x) => Math.Max(0, Math.Min(99, (int)Math.Round((x - 3) * 100.0 / 393.0)));

        /// <summary>The range's peaks: local maxima whose prominence — the drop to the higher of
        /// the two lowest points before a higher sample (or the range's end) on either side — is at
        /// least 0.08 (the waveform carries ±0.05 noise); the range's maximum when none stands out.</summary>
        private static List<double> Peaks(double[] v, int i0, int i1)
        {
            var result = new List<double>();
            if (i1 < i0) return result;
            for (int i = i0; i <= i1; i++)
            {
                bool left = i == i0 || v[i] >= v[i - 1], right = i == i1 || v[i] > v[i + 1];
                if (!left || !right) continue;
                double floor = double.MinValue;
                if (i > i0)
                {
                    double m = v[i];
                    for (int j = i - 1; j >= i0 && v[j] <= v[i]; j--) m = Math.Min(m, v[j]);
                    floor = Math.Max(floor, m);
                }
                if (i < i1)
                {
                    double m = v[i];
                    for (int j = i + 1; j <= i1 && v[j] <= v[i]; j++) m = Math.Min(m, v[j]);
                    floor = Math.Max(floor, m);
                }
                if (floor != double.MinValue && v[i] - floor >= 0.08) result.Add(v[i]);
            }
            if (result.Count == 0)
            {
                double max = 0;
                for (int i = i0; i <= i1; i++) max = Math.Max(max, v[i]);
                result.Add(max);
            }
            return result;
        }

        // ---- Reactor Controls ----

        public static bool ControlOn(int i)
        {
            try { return Class710.smethod_0().method_6((Enum111)i); } catch { return false; }
        }

        /// <summary>"control A, F1".</summary>
        public static string ControlName(int i)
            => Loc.T("defense.control", new { letter = (char)('A' + i), key = i + 1 });

        public static string OnOff(bool on) => Loc.T(on ? "value.on" : "value.off");

        private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
    }
}
