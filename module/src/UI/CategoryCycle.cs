using System;

namespace SpeechChem.UI
{
    /// <summary>
    /// [ / ] category cycling shared by the reactor and pipeline editors: only categories that
    /// hold something are stops (user rule 2026-10-04 — "Other components, 0" was a stop to skip).
    /// </summary>
    internal static class CategoryCycle
    {
        /// <summary>The next category (wrapping) from <paramref name="current"/> in direction
        /// <paramref name="delta"/> whose <paramref name="count"/> is above zero; -1 when every
        /// category is empty. With no current category, delta &gt; 0 starts at the first and
        /// delta &lt; 0 at the last.</summary>
        public static int Next(int current, int delta, int n, Func<int, int> count)
        {
            if (n <= 0) return -1;
            int step = delta < 0 ? -1 : 1;
            int at = current < 0 || current >= n ? (step > 0 ? -1 : n) : current;
            for (int i = 1; i <= n; i++)
            {
                int idx = ((at + step * i) % n + n) % n;
                if (count(idx) > 0) return idx;
            }
            return -1;
        }

        /// <summary>The category the item keys use: the current one while it holds something,
        /// else the first that does (-1 when all are empty).</summary>
        public static int ForItems(int current, int n, Func<int, int> count)
        {
            if (current >= 0 && current < n && count(current) > 0) return current;
            return Next(-1, 1, n, count);
        }
    }
}
