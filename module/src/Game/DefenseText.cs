using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Impeller;
using SpaceChem;
using SpaceChem.Levels;
using SpaceChem.Pipeline;
using SpeechChem.Localization;

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
    /// code 0 of an Oxygen Tank is — lives in the small tables below (Parts, Events, States), the
    /// only per-enemy / per-building pieces.
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
            public string Key;  // "{building} exploded"
            public bool Weapon; // an attack on the enemy: report hit / missed
        }

        /// <summary>Special buildings' event codes (Class598.method_15).</summary>
        private static readonly Dictionary<KeyValuePair<Type, int>, EventSpec> Events = new Dictionary<KeyValuePair<Type, int>, EventSpec>
        {
            { new KeyValuePair<Type, int>(typeof(Class605), Class605.int_1), new EventSpec { Key = "defense.event.exploded", Weapon = true } },
        };

        /// <summary>A building state the game shows only as a sprite change.</summary>
        private static readonly Dictionary<Type, Func<Draggable, string>> States = new Dictionary<Type, Func<Draggable, string>>
        {
            { typeof(Class605), d => ((Class605)d).bool_3 ? Loc.T("defense.exploded") : null },
        };

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

        /// <summary>The cells (0-based, on the map) one drawn rectangle covers.</summary>
        private static void CellsOf(Rectangle r, out int x0, out int x1, out int y0, out int y1)
        {
            x0 = FloorDiv(r.vector2i_0.int_0, CellWidth);
            x1 = FloorDiv(r.vector2i_0.int_0 + Math.Max(1, r.vector2i_1.int_0) - 1, CellWidth);
            y0 = FloorDiv(r.vector2i_0.int_1, CellHeight);
            y1 = FloorDiv(r.vector2i_0.int_1 + Math.Max(1, r.vector2i_1.int_1) - 1, CellHeight);
        }

        public static bool Covers(Class310 enemy, Vector2i cell)
        {
            var rects = Patches.DefenseCapture.Footprint(enemy);
            if (rects == null) return false;
            foreach (var r in rects)
            {
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
            var rects = Patches.DefenseCapture.Footprint(enemy);
            if (rects == null) return null;
            int ax0 = int.MaxValue, ax1 = int.MinValue, ay0 = int.MaxValue, ay1 = int.MinValue;
            foreach (var r in rects)
            {
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
            var m = Patches.DefenseCapture.CaptureMeter(d);
            if (m == null) return null;
            int percent = (int)Math.Round(Math.Max(0f, Math.Min(1f, m.Value.Value)) * 100f);
            return string.IsNullOrEmpty(m.Value.Key)
                ? Loc.T("run.percent", new { percent })
                : Loc.T("defense.status", new { label = m.Value.Key, percent });
        }

        /// <summary>The Control Center's name as the progress panel titles it.</summary>
        public static string BaseName() => Screens.Common.ProgressSection.ProgressLabel();

        // ---- special-building events ----

        /// <summary>"Oxygen Tank 2 exploded", or "Oxygen Tank 2, event 3" for a code not in the
        /// table; <paramref name="weapon"/> = the event attacks the enemy.</summary>
        public static string EventText(Class598 b, int code, out bool weapon)
        {
            weapon = false;
            string name = PipelineText.Name(b.pipeline_0, b);
            EventSpec spec;
            if (Events.TryGetValue(new KeyValuePair<Type, int>(b.GetType(), code), out spec))
            {
                weapon = spec.Weapon;
                return Loc.T(spec.Key, new { building = name });
            }
            return Loc.T("defense.event", new { building = name, code });
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
