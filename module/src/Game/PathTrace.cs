using System;
using System.Collections.Generic;

namespace SpeechChem.Game
{
    /// <summary>
    /// A waldo's static path through a reactor program, as the game draws its path lines
    /// (Reactor.method_46/47, decompile-verified): from the START marker, each cell's arrow sets
    /// the heading (no arrow: keep going); a non-arrow instruction with its own direction (sensor,
    /// flip-flop, control) opens a second branch in that direction; START counts only on its own
    /// cell and only when no arrow shares it. A branch ends at the wall or on a (cell, heading)
    /// already walked. Here the depth-first walk is linearized for reading: the main line first,
    /// then each pending branch in the order it opened. Straight runs of empty cells are
    /// condensed away; a step is emitted where something happens.
    ///
    /// BCL-pure over a cell lookup, so the walk is unit-tested; directions use the game's
    /// Enum153 degrees (Right 0, Down 90, Left 180, Up -90, None -1).
    /// </summary>
    internal static class PathTrace
    {
        public const int None = -1, Right = 0, Down = 90, Left = 180, Up = -90;

        /// <summary>What the lookup reports for one cell in the traced colour.</summary>
        public struct Cell
        {
            public string ArrowLabel;   // null: no arrow
            public int ArrowDir;
            public string InstrLabel;   // null: no non-arrow instruction
            public int InstrDir;        // None unless it branches / is START
            public bool IsStart;
        }

        public enum Kind { Start, Step, Branch, Wall, Loop }

        public sealed class Line
        {
            public Kind Kind;
            public int X, Y;
            public int Dir;             // Start / Branch: the heading taken; Step: the new heading after an arrow
            public bool Turned;         // Step: an arrow changed the heading here
            public List<string> Labels = new List<string>();
        }

        /// <summary>Walk from the START at (sx, sy). Returns nothing when that cell holds no START.</summary>
        public static List<Line> Walk(int width, int height, int sx, int sy, Func<int, int, Cell> lookup)
        {
            var lines = new List<Line>();
            var start = lookup(sx, sy);
            if (!start.IsStart) return lines;

            var visited = new HashSet<long>();
            var pending = new Queue<Line>();

            // The start cell: an arrow sharing it wins, else the marker's own heading.
            int dir = start.ArrowLabel != null ? start.ArrowDir : start.InstrDir;
            var first = new Line { Kind = Kind.Start, X = sx, Y = sy, Dir = dir };
            if (start.ArrowLabel != null) first.Labels.Add(start.ArrowLabel);
            lines.Add(first);
            visited.Add(Key(sx, sy, dir));
            Follow(width, height, sx, sy, dir, lookup, visited, pending, lines);

            while (pending.Count > 0)
            {
                var branch = pending.Dequeue();
                lines.Add(branch);
                Follow(width, height, branch.X, branch.Y, branch.Dir, lookup, visited, pending, lines);
            }
            return lines;
        }

        /// <summary>Leave (x, y) heading dir and keep walking until the wall or a repeat.</summary>
        private static void Follow(int width, int height, int x, int y, int dir, Func<int, int, Cell> lookup,
            HashSet<long> visited, Queue<Line> pending, List<Line> lines)
        {
            while (true)
            {
                int nx, ny;
                if (!Next(width, height, x, y, dir, out nx, out ny))
                {
                    lines.Add(new Line { Kind = Kind.Wall, X = x, Y = y, Dir = dir });
                    return;
                }
                x = nx; y = ny;
                if (!visited.Add(Key(x, y, dir)))
                {
                    lines.Add(new Line { Kind = Kind.Loop, X = x, Y = y, Dir = dir });
                    return;
                }

                var c = lookup(x, y);
                string instr = c.IsStart ? null : c.InstrLabel; // START only acts on its own walk's first cell
                int newDir = c.ArrowLabel != null ? c.ArrowDir : dir;
                if (instr != null || c.ArrowLabel != null)
                {
                    var step = new Line { Kind = Kind.Step, X = x, Y = y, Dir = newDir, Turned = newDir != dir };
                    if (instr != null) step.Labels.Add(instr);
                    if (c.ArrowLabel != null) step.Labels.Add(c.ArrowLabel);
                    lines.Add(step);
                }
                if (instr != null && c.InstrDir != None)
                {
                    var branch = new Line { Kind = Kind.Branch, X = x, Y = y, Dir = c.InstrDir };
                    branch.Labels.Add(instr);
                    pending.Enqueue(branch);
                }
                dir = newDir;
            }
        }

        private static bool Next(int width, int height, int x, int y, int dir, out int nx, out int ny)
        {
            nx = x; ny = y;
            switch (dir)
            {
                case Right: nx++; break;
                case Left: nx--; break;
                case Down: ny++; break;
                case Up: ny--; break;
                default: return false;
            }
            return nx >= 0 && nx < width && ny >= 0 && ny < height;
        }

        private static long Key(int x, int y, int dir) => ((long)x << 32) | ((long)(y & 0xFFFF) << 16) | (uint)(dir & 0xFFFF);
    }
}
