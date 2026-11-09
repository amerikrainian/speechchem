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
    /// Things in a defense level's sky that are not the enemy itself, read from where the game draws
    /// them. Two levels have them:
    ///   Bha Shogth's asteroids in the research-boss level (Class314 — its linkedList_0 of Class21).
    ///     Attack timer 0 (every 500 cycles) launches one off-screen right (x 1300, a random fixed
    ///     height); it drifts left 0.5 px a cycle, has 100 health (int_0) and the Hydrogen Laser's
    ///     beam takes 2 a cycle; one that drifts past x 440 hits the station for 20
    ///     (Class314.vmethod_0 removes both kinds).
    ///   End of the Line (Class150, enemy Class312): the LEVEL holds them. Asteroids (linkedList_0 of
    ///     Class445): attack timer 0 (every 500 cycles) drops one at x 790-940, y -50, falling 0.5 px
    ///     a cycle; one touching the Prometheus (the ship's sprite, its top cut 75 px) hits it for 20,
    ///     one touching a missile is destroyed with it; the rest fall off the map and stay in the list.
    ///     Missiles (linkedList_1 of Class6): one per Ω-Missile Launcher shot, from the ship, 2 px a
    ///     cycle, steering toward the enemy — or, while it phases, toward a point far above the map, so
    ///     they fly off the top (removed past y -50); one within 20 px of the enemy hits it for 10.
    /// NAMED BY LAUNCH ORDER (user design 2026-10-24): "Asteroid 3", "Missile 2", numbers restarting
    /// each run (Class314.vmethod_1, Class150.vmethod_11 clear the lists). WHERE: the drawn sprite
    /// (centred on its position) as map cells; for Class314's asteroids also their bearing and
    /// distance from the laser's turret pivot (Class600.vector2i_2, the point the beam's hit test
    /// aims from) — a reading of where the game draws them, never which aim would hit (user rule:
    /// no prediction).
    /// </summary>
    internal static class DefenseHazards
    {
        public const int AsteroidKind = 0, MissileKind = 1;

        /// <summary>One thing drawn in the sky: its name ("Missile 2"), the game object, its sprite.</summary>
        internal struct Item
        {
            public int Kind;
            public int Number;
            public object Thing;
            public Rectangle Rect;
            public string Name => DefenseHazards.Name(Kind, Number);
        }

        private static readonly ConditionalWeakTable<object, int[]> Numbers = new ConditionalWeakTable<object, int[]>();
        private static readonly ConditionalWeakTable<object, int[]> Counters = new ConditionalWeakTable<object, int[]>();

        /// <summary>A new run: numbering starts over (owner = the Class314 enemy or the Class150 level).</summary>
        public static void Reset(object owner)
        {
            if (owner == null) return;
            var c = Counters.GetValue(owner, _ => new int[2]);
            c[0] = c[1] = 0;
        }

        /// <summary>A thing's number, given the first time it is seen (the lists are in launch order).</summary>
        public static int Number(object owner, object thing, int kind)
        {
            int[] n;
            if (Numbers.TryGetValue(thing, out n)) return n[0];
            var counter = Counters.GetValue(owner, _ => new int[2]);
            n = new[] { ++counter[kind] };
            Numbers.Add(thing, n);
            return n[0];
        }

        /// <summary>Class314's live asteroids in launch order, with their numbers; empty for any other enemy.</summary>
        public static List<KeyValuePair<int, Class21>> Asteroids(Class310 enemy)
        {
            var list = new List<KeyValuePair<int, Class21>>();
            var s = enemy as Class314;
            if (s?.linkedList_0 == null) return list;
            foreach (var a in s.linkedList_0) list.Add(new KeyValuePair<int, Class21>(Number(s, a, AsteroidKind), a));
            return list;
        }

        /// <summary>The End of the Line level the enemy belongs to, or null.</summary>
        public static Class150 Final(Class310 enemy) => (enemy as Class312)?.defenseLevelEditor_0 as Class150;

        /// <summary>Every asteroid and missile the enemy's level holds, in launch order (asteroids
        /// first), whether on the map or not.</summary>
        public static List<Item> Items(Class310 enemy)
        {
            var items = new List<Item>();
            var s = enemy as Class314;
            if (s?.linkedList_0 != null)
                foreach (var a in s.linkedList_0)
                    items.Add(new Item { Kind = AsteroidKind, Number = Number(s, a, AsteroidKind), Thing = a, Rect = Rect(a) });
            var w = Final(enemy);
            if (w != null)
            {
                foreach (var a in w.linkedList_0)
                    items.Add(new Item { Kind = AsteroidKind, Number = Number(w, a, AsteroidKind), Thing = a, Rect = Centred(a.vector2f_0, a.class358_1.method_2()) });
                foreach (var m in w.linkedList_1)
                    items.Add(new Item { Kind = MissileKind, Number = Number(w, m, MissileKind), Thing = m, Rect = Centred(m.vector2f_1, Class6.class358_0.method_2()) });
            }
            return items;
        }

        /// <summary>The newest asteroid (the one an attack just launched), or null.</summary>
        public static Item? Newest(Class310 enemy, int kind)
        {
            Item? last = null;
            foreach (var item in Items(enemy))
                if (item.Kind == kind) last = item;
            return last;
        }

        /// <summary>The name the next thing of this kind will get ("Missile 3"): asked just before
        /// the game creates it (everything already there is numbered first).</summary>
        public static string NextName(Class310 enemy, int kind)
        {
            Items(enemy);
            object owner = (object)Final(enemy) ?? enemy;
            if (owner == null) return null;
            return Name(kind, Counters.GetValue(owner, _ => new int[2])[kind] + 1);
        }

        public static string Name(int n) => Name(AsteroidKind, n);

        public static string Name(int kind, int n) => Loc.T(kind == MissileKind ? "defense.missile" : "defense.asteroid", new { n });

        private static Rectangle Centred(Vector2f centre, Vector2i size)
            => new Rectangle(new Vector2i((int)Math.Round(centre.float_0) - size.int_0 / 2, (int)Math.Round(centre.float_1) - size.int_1 / 2), size);

        /// <summary>The drawn sprite (Class21.method_1: centred on vector2f_0), pipeline pixels.</summary>
        public static Rectangle Rect(Class21 a) => Centred(a.vector2f_0, a.class358_1.method_2());

        public static bool OnMap(Rectangle r) => DefenseText.SpanOf(r) != null;

        public static bool OnMap(Class21 a) => OnMap(Rect(a));

        /// <summary>The asteroids and missiles drawn over a map cell, by name.</summary>
        public static List<string> NamesAt(Class310 enemy, Vector2i cell)
        {
            var names = new List<string>();
            foreach (var item in Items(enemy))
                if (DefenseText.Covers(item.Rect, cell)) names.Add(item.Name);
            return names;
        }

        /// <summary>"row 7": the map row of its centre, the height it keeps.</summary>
        public static string Row(Class21 a)
        {
            int row = (int)Math.Floor(a.vector2f_0.float_1 / DefenseText.CellHeight) + 1;
            return Loc.T("defense.row", new { row = Math.Max(1, Math.Min(DefenseText.MapRows, row)) });
        }

        /// <summary>"columns 26 to 27" (or "column 26"): the map columns a drawing covers, clipped.</summary>
        public static string Columns(Rectangle r)
        {
            int x0 = (int)Math.Floor(r.vector2i_0.int_0 / (double)DefenseText.CellWidth);
            int x1 = (int)Math.Floor((r.vector2i_0.int_0 + r.vector2i_1.int_0 - 1) / (double)DefenseText.CellWidth);
            x0 = Math.Max(0, x0) + 1;
            x1 = Math.Min(DefenseText.MapColumns - 1, x1) + 1;
            return x0 == x1 ? Loc.T("defense.column", new { x = x0 }) : Loc.T("defense.columns", new { x0, x1 });
        }

        /// <summary>"bearing 20 degrees down" from the laser's turret, or null (no laser drawn yet).</summary>
        public static string Bearing(Class21 a)
        {
            Vector2f d;
            if (!FromLaser(a, out d)) return null;
            float degrees = (float)Math.Round(Math.Atan2(d.float_1, d.float_0) * 180.0 / Math.PI);
            return Loc.T("defense.bearing", new { angle = DefenseText.Angle(degrees) });
        }

        /// <summary>Its bearing from the laser's turret in degrees (positive = down), or null.</summary>
        public static double? BearingDegrees(Class21 a)
        {
            Vector2f d;
            if (!FromLaser(a, out d)) return null;
            return Math.Atan2(d.float_1, d.float_0) * 180.0 / Math.PI;
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
        /// degrees down, 14 cells" (bearing and distance only toward a Hydrogen Laser).</summary>
        public static string Describe(Item item)
        {
            var parts = new List<string> { item.Name, DefenseText.SpanOf(item.Rect) ?? Loc.T("defense.offmap") };
            if (item.Thing is Class21 a)
            {
                string bearing = Bearing(a), distance = Distance(a);
                if (bearing != null) parts.Add(bearing);
                if (distance != null) parts.Add(distance);
            }
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>The Enemy stop's rows: every asteroid and missile drawn on the map, in launch
        /// order, keyed by a stable id ("asteroid.3", "missile.2").</summary>
        public static List<KeyValuePair<string, string>> Rows(Class310 enemy)
        {
            var rows = new List<KeyValuePair<string, string>>();
            foreach (var item in Items(enemy))
                if (OnMap(item.Rect)) rows.Add(new KeyValuePair<string, string>(Id(item), Describe(item)));
            return rows;
        }

        public static string Id(Item item) => (item.Kind == MissileKind ? "missile." : "asteroid.") + item.Number;

        /// <summary>The row of the item with this id, live, or null when it is gone.</summary>
        public static string Describe(Class310 enemy, string id)
        {
            foreach (var item in Items(enemy))
                if (Id(item) == id) return Describe(item);
            return null;
        }

        // ---- the Prometheus (End of the Line's ship, drawn by the level) ----

        /// <summary>The ship's drawn sprite (Class150.vmethod_10: player sprite centred on
        /// vector2i_2), or null outside End of the Line.</summary>
        public static Rectangle? Ship(Class310 enemy)
        {
            var w = Final(enemy);
            if (w == null) return null;
            var size = Class150.class358_0[0].method_2();
            return new Rectangle(w.vector2i_2 - size / 2, size);
        }

        /// <summary>The ship's name: the progress panel's ("The Prometheus").</summary>
        public static string ShipName => DefenseText.BaseName();

        /// <summary>The Enemy stop's ship row: "The Prometheus, columns 26 to 28, rows 17 to 22", or null.</summary>
        public static string ShipRow(Class310 enemy)
        {
            var r = Ship(enemy);
            if (r == null) return null;
            return ShipName + ", " + (DefenseText.SpanOf(r.Value) ?? Loc.T("defense.offmap"));
        }

        /// <summary>The asteroid touches the ship: the level's own test (the ship's sprite with its
        /// top cut 75 px against the asteroid's 35 x 35 box).</summary>
        public static bool TouchesShip(Class150 w, Class445 a)
        {
            var size = Class150.class358_0[0].method_2();
            var ship = new Rectangle(w.vector2i_2 - size / 2, size);
            ship.method_16(ship.method_15() + 75);
            return ship.method_31(a.method_0());
        }

        /// <summary>The map row (1-based, clamped) an asteroid's centre is in when it touches the
        /// ship: the level's test cuts 75 px off the sprite's top, and the asteroid's box is 35 tall.</summary>
        public static int ShipStruckRow(Class150 w)
        {
            double top = w.vector2i_2.int_1 - Class150.class358_0[0].method_2().int_1 / 2 + 75 - 17.5;
            return Math.Max(0, Math.Min(DefenseText.MapRows - 1, (int)Math.Floor(top / DefenseText.CellHeight))) + 1;
        }

        /// <summary>The ship's position for its move lines: the map column (1-based) of its
        /// left edge, as the enemy's moves give its top-left cell.</summary>
        public static int ShipColumn(Class150 w)
        {
            var size = Class150.class358_0[0].method_2();
            int left = w.vector2i_2.int_0 - size.int_0 / 2;
            return Math.Max(0, Math.Min(DefenseText.MapColumns - 1, (int)Math.Floor(left / (double)DefenseText.CellWidth))) + 1;
        }
    }
}
