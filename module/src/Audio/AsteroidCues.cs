using System.Collections.Generic;
using System.Linq;
using SpeechChem.Game;

namespace SpeechChem.Audio
{
    /// <summary>
    /// Bha Shogth's asteroids and the Hydrogen Laser's aim as sound (user design 2026-11-09). Each
    /// time an asteroid enters a new column: the aim note, then the asteroid's tick, both panned to
    /// its column. Tick pitch = its bearing from the laser, aim note pitch = the laser's angle, on one
    /// scale, so a level pair means the two line up. That is two readings to compare, never a
    /// verdict on whether a shot would hit (user rule). The tick's pulse count tells the asteroids
    /// apart: each takes the lowest free count when it reaches the map, at most 3 in this level. The
    /// aim note also plays alone, centred, when the laser turns.
    /// At the level's top speed (400 cycles a second) that is about 19 ticks a second: well within
    /// the reserved channels, so the no-drop queue doesn't build a backlog.
    /// </summary>
    internal static class AsteroidCues
    {
        private sealed class Tracker
        {
            public int Column, Pulses;
        }

        private static readonly Dictionary<int, Tracker> Live = new Dictionary<int, Tracker>();
        private static int _aim = -1;

#if DEBUG
        internal static long Ticks, Turns;

        internal static string Status()
            => "ticks " + Ticks + ", turns " + Turns + ", asteroids "
               + string.Join(" ", Live.OrderBy(kv => kv.Key).Select(kv => kv.Key + ":column " + kv.Value.Column + ":pulses " + kv.Value.Pulses).ToArray());
#endif

        /// <summary>Per enemy cycle. <paramref name="fresh"/> = a new run (start over);
        /// <paramref name="audible"/> = a running cycle (otherwise only follow along silently).</summary>
        public static void Cycle(Class310 enemy, bool fresh, bool audible)
        {
            var level = enemy.defenseLevelEditor_0 as Class146;
            if (level == null) return;
            if (fresh)
            {
                Live.Clear();
                _aim = -1;
            }

            int aim = level.class600_0.int_3;
            if (aim != _aim)
            {
                if (audible && _aim >= 0)
                {
                    SoundEngine.LaserTurned(aim);
#if DEBUG
                    Turns++;
#endif
                }
                _aim = aim;
            }

            var asteroids = DefenseHazards.Asteroids(enemy);
            foreach (int gone in Live.Keys.Where(n => !asteroids.Any(kv => kv.Key == n)).ToList()) Live.Remove(gone);
            foreach (var kv in asteroids)
            {
                int column = (int)System.Math.Floor(kv.Value.vector2f_0.float_0 / DefenseText.CellWidth) + 1;
                if (column < 1 || column > DefenseText.MapColumns) continue;
                Tracker tracker;
                if (Live.TryGetValue(kv.Key, out tracker))
                {
                    if (column == tracker.Column) continue;
                    tracker.Column = column;
                }
                else
                {
                    tracker = new Tracker { Column = column, Pulses = Beep.LowestFreePulses(Live.Values.Select(t => t.Pulses).ToList()) };
                    Live[kv.Key] = tracker;
                }
                if (!audible) continue;
                double? bearing = DefenseHazards.BearingDegrees(kv.Value);
                if (bearing == null) continue;
                SoundEngine.AsteroidTick(column, bearing.Value, tracker.Pulses, aim);
#if DEBUG
                Ticks++;
#endif
            }
        }
    }
}
