using System.Collections.Generic;
using System.Linq;
using SpeechChem.Game;

namespace SpeechChem.Audio
{
    /// <summary>
    /// End of the Line's falling asteroids and missiles as sound (user request 2026-11-09). Everything
    /// here pans over the band of the map the action is in (<see cref="Beep.FinalFieldPosition"/>),
    /// and rows are pitch, as for the enemy. Each time an asteroid falls into a new row: the ship's
    /// note, at the ship's place and at the pitch of the row an asteroid touches it, then the
    /// asteroid's tick at its own place and row. Two readings to compare, never a verdict (user
    /// rule): the same place means it is over the ship, the same pitch that it has fallen to the
    /// ship's height. The tick's pulse count tells the asteroids apart (at most 3 on the map: one per
    /// 500 cycles, 1232 cycles to cross it). A missile blips at each new row.
    /// Asteroids that miss stay in the game's list for good; off the map they lose their pulse count.
    /// </summary>
    internal static class FinalCues
    {
        private sealed class Falling
        {
            public int Row, Pulses;
        }

        private static readonly Dictionary<Class445, Falling> Asteroids = new Dictionary<Class445, Falling>();
        private static readonly Dictionary<Class6, int> Missiles = new Dictionary<Class6, int>();

#if DEBUG
        internal static long AsteroidTicks, MissileTicks;

        internal static string Status() => "falling ticks " + AsteroidTicks + ", missile ticks " + MissileTicks;
#endif

        /// <summary>A new run.</summary>
        public static void Reset()
        {
            Asteroids.Clear();
            Missiles.Clear();
        }

        private static int RowOf(float y) => (int)System.Math.Floor(y / DefenseText.CellHeight) + 1;

        private static bool OnMap(int row) => row >= 1 && row <= DefenseText.MapRows;

        /// <summary>Per cycle, after the level's update. <paramref name="audible"/> = a running cycle
        /// (otherwise only follow along silently).</summary>
        public static void Cycle(Class150 level, bool audible)
        {
            foreach (var gone in Asteroids.Keys.Where(a => !level.linkedList_0.Contains(a)).ToList()) Asteroids.Remove(gone);
            foreach (var gone in Missiles.Keys.Where(m => !level.linkedList_1.Contains(m)).ToList()) Missiles.Remove(gone);

            foreach (var asteroid in level.linkedList_0)
            {
                int row = RowOf(asteroid.vector2f_0.float_1);
                if (!OnMap(row))
                {
                    Asteroids.Remove(asteroid);
                    continue;
                }
                Falling falling;
                if (Asteroids.TryGetValue(asteroid, out falling))
                {
                    if (row == falling.Row) continue;
                    falling.Row = row;
                }
                else
                {
                    falling = new Falling { Row = row, Pulses = Beep.LowestFreePulses(Asteroids.Values.Select(f => f.Pulses).ToList()) };
                    Asteroids[asteroid] = falling;
                }
                if (!audible) continue;
                SoundEngine.AsteroidFell(Beep.FinalFieldPosition(asteroid.vector2f_0.float_0), row, falling.Pulses,
                    Beep.FinalFieldPosition(level.vector2i_2.int_0), DefenseHazards.ShipStruckRow(level));
#if DEBUG
                AsteroidTicks++;
#endif
            }

            foreach (var missile in level.linkedList_1)
            {
                int row = RowOf(missile.vector2f_1.float_1);
                if (!OnMap(row))
                {
                    Missiles.Remove(missile);
                    continue;
                }
                int last;
                if (Missiles.TryGetValue(missile, out last) && last == row) continue;
                Missiles[missile] = row;
                if (!audible) continue;
                SoundEngine.MissileMoved(Beep.FinalFieldPosition(missile.vector2f_1.float_0), row);
#if DEBUG
                MissileTicks++;
#endif
            }
        }
    }
}
