using System;

namespace SpeechChem.UI
{
    /// <summary>
    /// Ctrl+arrow on a grid (user request): skip the run of cells that read the same as the one
    /// under the cursor and land on the first cell that differs, or on the grid's edge when the
    /// run reaches it. Cells compare by their contents without coordinates, so a stretch of empty
    /// cells, a straight pipe or a row of blocked terrain is one hop.
    /// </summary>
    internal static class GridSkip
    {
        /// <summary>The cell a skip from (x, y) by (dx, dy) lands on; the start cell itself when
        /// it is already on that edge.</summary>
        public static void Target(int x, int y, int dx, int dy, int width, int height,
            Func<int, int, string> contents, out int tx, out int ty)
        {
            tx = x;
            ty = y;
            string here = contents(x, y) ?? "";
            int nx = x + dx, ny = y + dy;
            while (nx >= 0 && ny >= 0 && nx < width && ny < height)
            {
                tx = nx;
                ty = ny;
                if ((contents(nx, ny) ?? "") != here) return;
                nx += dx;
                ny += dy;
            }
        }
    }
}
