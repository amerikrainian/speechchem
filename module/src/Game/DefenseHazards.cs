using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Impeller;
using SpaceChem;
using SpaceChem.Levels;
using SpeechChem.Localization;

namespace SpeechChem.Game
{
    /// <summary>
    /// Hazards an enemy sends that are not the enemy itself: Bha Shogth's asteroids (Class314, the
    /// research-boss level Class146 — its linkedList_0 of Class21). Attack timer 0 (every 500 cycles)
    /// launches one off-screen right (x 1300, a random fixed height); it drifts left 0.5 px a cycle,
    /// has 100 health (int_0) and the Hydrogen Laser's beam takes 2 a cycle; one that drifts past
    /// x 440 hits the station for 20 (Class314.vmethod_0 removes both kinds).
    /// NAMED BY LAUNCH ORDER (user design 2026-10-24): "Asteroid 3". All drift at one speed, so the
    /// order is also the arrival order and the lowest live number the nearest; numbers restart with
    /// each run (Class314.vmethod_1 clears the list). WHERE: the drawn sprite (centred on vector2f_0)
    /// as map cells, and its bearing and distance from the laser's turret pivot (Class600.vector2i_2,
    /// the point the beam's hit test aims from; the same pixel space) — a reading of where the game
    /// draws it, never which aim would hit (user rule: no prediction).
    /// </summary>
    internal static class DefenseHazards
    {
        private static readonly ConditionalWeakTable<Class21, int[]> Numbers = new ConditionalWeakTable<Class21, int[]>();
        private static readonly ConditionalWeakTable<Class314, int[]> Counters = new ConditionalWeakTable<Class314, int[]>();

        /// <summary>A new run: numbering starts over.</summary>
        public static void Reset(Class314 enemy)
        {
            if (enemy != null) Counters.GetValue(enemy, _ => new int[1])[0] = 0;
        }

        /// <summary>The asteroid's number, given the first time it is seen (the list is in launch order).</summary>
        public static int Number(Class314 enemy, Class21 a)
        {
            int[] n;
            if (Numbers.TryGetValue(a, out n)) return n[0];
            var counter = Counters.GetValue(enemy, _ => new int[1]);
            n = new[] { ++counter[0] };
            Numbers.Add(a, n);
            return n[0];
        }

        /// <summary>The enemy's live asteroids in launch order, with their numbers; empty for any other enemy.</summary>
        public static List<KeyValuePair<int, Class21>> Asteroids(Class310 enemy)
        {
            var list = new List<KeyValuePair<int, Class21>>();
            var s = enemy as Class314;
            if (s?.linkedList_0 == null) return list;
            foreach (var a in s.linkedList_0) list.Add(new KeyValuePair<int, Class21>(Number(s, a), a));
            return list;
        }

        /// <summary>The newest asteroid (the one an attack just launched), or null.</summary>
        public static KeyValuePair<int, Class21>? Newest(Class310 enemy)
        {
            var s = enemy as Class314;
            var last = s?.linkedList_0?.Last;
            if (last == null) return null;
            return new KeyValuePair<int, Class21>(Number(s, last.Value), last.Value);
        }

        public static string Name(int n) => Loc.T("defense.asteroid", new { n });

        /// <summary>The drawn sprite (Class21.method_1: centred on vector2f_0), pipeline pixels.</summary>
        public static Rectangle Rect(Class21 a)
        {
            var size = a.class358_1.method_2();
            return new Rectangle(new Vector2i((int)Math.Round(a.vector2f_0.float_0) - size.int_0 / 2, (int)Math.Round(a.vector2f_0.float_1) - size.int_1 / 2), size);
        }

        public static bool OnMap(Class21 a) => DefenseText.SpanOf(Rect(a)) != null;

        public static bool Covers(Class21 a, Vector2i cell) => DefenseText.Covers(Rect(a), cell);

        /// <summary>The asteroids drawn over a map cell, by name.</summary>
        public static List<string> NamesAt(Class310 enemy, Vector2i cell)
        {
            var names = new List<string>();
            foreach (var kv in Asteroids(enemy))
                if (Covers(kv.Value, cell)) names.Add(Name(kv.Key));
            return names;
        }

        /// <summary>"row 7": the map row of its centre, the height it keeps.</summary>
        public static string Row(Class21 a)
        {
            int row = (int)Math.Floor(a.vector2f_0.float_1 / DefenseText.CellHeight) + 1;
            return Loc.T("defense.row", new { row = Math.Max(1, Math.Min(DefenseText.MapRows, row)) });
        }

        /// <summary>"bearing 20 degrees down" from the laser's turret, or null (no laser drawn yet).</summary>
        public static string Bearing(Class21 a)
        {
            Vector2f d;
            if (!FromLaser(a, out d)) return null;
            float degrees = (float)Math.Round(Math.Atan2(d.float_1, d.float_0) * 180.0 / Math.PI);
            return Loc.T("defense.bearing", new { angle = DefenseText.Angle(degrees) });
        }

        /// <summary>"14 cells" from the laser's turret (pixels / the cell width), or null.</summary>
        public static string Distance(Class21 a)
        {
            Vector2f d;
            if (!FromLaser(a, out d)) return null;
            double cells = Math.Sqrt(d.float_0 * d.float_0 + d.float_1 * d.float_1) / DefenseText.CellWidth;
            return Loc.T("defense.cells", new { n = (int)Math.Round(cells) });
        }

        private static bool FromLaser(Class21 a, out Vector2f d)
        {
            d = default(Vector2f);
            var laser = Laser();
            if (laser == null || (laser.vector2i_2.int_0 == 0 && laser.vector2i_2.int_1 == 0)) return false;
            d = new Vector2f(a.vector2f_0.float_0 - laser.vector2i_2.int_0, a.vector2f_0.float_1 - laser.vector2i_2.int_1);
            return true;
        }

        private static Class600 Laser()
        {
            var p = DefenseText.Level?.pipeline_0;
            if (p == null) return null;
            foreach (var kv in PipelineText.Components(p))
                if (kv.Key is Class600 laser) return laser;
            return null;
        }

        /// <summary>The Enemy stop's row: "Asteroid 2, columns 25 to 27, rows 6 to 8, bearing 20
        /// degrees down, 14 cells".</summary>
        public static string Describe(int n, Class21 a)
        {
            var parts = new List<string> { Name(n), DefenseText.SpanOf(Rect(a)) ?? Loc.T("defense.offmap") };
            string bearing = Bearing(a), distance = Distance(a);
            if (bearing != null) parts.Add(bearing);
            if (distance != null) parts.Add(distance);
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>The Enemy stop's rows: every asteroid drawn on the map, in launch order.</summary>
        public static List<KeyValuePair<int, string>> Rows(Class310 enemy)
        {
            var rows = new List<KeyValuePair<int, string>>();
            foreach (var kv in Asteroids(enemy))
                if (OnMap(kv.Value)) rows.Add(new KeyValuePair<int, string>(kv.Key, Describe(kv.Key, kv.Value)));
            return rows;
        }
    }
}
