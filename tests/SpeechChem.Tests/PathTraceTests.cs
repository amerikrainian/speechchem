using System.Collections.Generic;
using System.Linq;
using SpeechChem.Game;
using Xunit;
using static SpeechChem.Game.PathTrace;

namespace SpeechChem.Tests
{
    public class PathTraceTests
    {
        private sealed class Grid
        {
            private readonly Dictionary<(int, int), Cell> _cells = new Dictionary<(int, int), Cell>();

            public Grid Start(int x, int y, int dir) => Set(x, y, c => { c.InstrLabel = "start"; c.InstrDir = dir; c.IsStart = true; return c; });
            public Grid Arrow(int x, int y, int dir) => Set(x, y, c => { c.ArrowLabel = "arrow " + dir; c.ArrowDir = dir; return c; });
            public Grid Instr(int x, int y, string label, int dir = None) => Set(x, y, c => { c.InstrLabel = label; c.InstrDir = dir; return c; });

            private Grid Set(int x, int y, System.Func<Cell, Cell> f)
            {
                Cell c;
                if (!_cells.TryGetValue((x, y), out c)) c = new Cell { ArrowDir = None, InstrDir = None };
                _cells[(x, y)] = f(c);
                return this;
            }

            public List<Line> Walk(int sx, int sy) => PathTrace.Walk(10, 8, sx, sy, (x, y) =>
            {
                Cell c;
                return _cells.TryGetValue((x, y), out c) ? c : new Cell { ArrowDir = None, InstrDir = None };
            });
        }

        private static string Sig(Line l) => l.Kind + "@" + l.X + "," + l.Y;

        [Fact]
        public void NoStartMeansNoPath()
        {
            Assert.Empty(new Grid().Walk(4, 1));
        }

        [Fact]
        public void StraightRunEndsAtTheWall()
        {
            var lines = new Grid().Start(4, 1, Left).Walk(4, 1);
            Assert.Equal(new[] { "Start@4,1", "Wall@0,1" }, lines.Select(Sig));
            Assert.Equal(Left, lines[0].Dir);
        }

        [Fact]
        public void InstructionsAndTurnsAreSteps()
        {
            var lines = new Grid().Start(4, 1, Left).Instr(3, 1, "in").Arrow(1, 1, Down).Walk(4, 1);
            Assert.Equal(new[] { "Start@4,1", "Step@3,1", "Step@1,1", "Wall@1,7" }, lines.Select(Sig));
            Assert.False(lines[1].Turned);
            Assert.True(lines[2].Turned);
            Assert.Equal(Down, lines[2].Dir);
        }

        [Fact]
        public void ClosedLoopIsReported()
        {
            var lines = new Grid().Start(4, 1, Left).Arrow(1, 1, Down).Arrow(1, 5, Right).Arrow(6, 5, Up).Arrow(6, 1, Left).Walk(4, 1);
            Assert.Equal("Loop@4,1", Sig(lines.Last()));
            Assert.Equal(6, lines.Count); // start, 4 arrows, loop
        }

        [Fact]
        public void ArrowOnTheStartCellWins()
        {
            var lines = new Grid().Start(4, 1, Left).Arrow(4, 1, Down).Walk(4, 1);
            Assert.Equal(Down, lines[0].Dir);
            Assert.Equal("Wall@4,7", Sig(lines.Last()));
        }

        [Fact]
        public void PassingTheStartAgainDoesNotTurn()
        {
            // Heading right across the START (which faces left): the marker is ignored.
            var lines = new Grid().Start(4, 1, Left).Arrow(1, 1, Right).Walk(4, 1);
            Assert.Equal(new[] { "Start@4,1", "Step@1,1", "Wall@9,1" }, lines.Select(Sig));
        }

        [Fact]
        public void DirectedInstructionOpensABranchAfterTheMainLine()
        {
            var lines = new Grid().Start(4, 1, Left).Instr(2, 1, "sense", Up).Walk(4, 1);
            Assert.Equal(new[] { "Start@4,1", "Step@2,1", "Wall@0,1", "Branch@2,1", "Wall@2,0" }, lines.Select(Sig));
            Assert.Equal(Up, lines[3].Dir);
        }

        [Fact]
        public void BranchRejoiningTheMainLineLoops()
        {
            // The branch goes down, then left, then up back into (1,1) heading up... then continues;
            // it rejoins the main line where the main line walked with the same heading.
            var g = new Grid().Start(4, 1, Left).Arrow(1, 1, Down).Instr(1, 3, "flip", Right).Arrow(3, 3, Up).Arrow(3, 1, Left);
            var lines = g.Walk(4, 1);
            Assert.Equal("Branch@1,3", Sig(lines.First(l => l.Kind == Kind.Branch)));
            Assert.Equal("Loop@2,1", Sig(lines.Last()));
        }
    }
}
