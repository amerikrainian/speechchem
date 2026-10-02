using SpeechChem.UI;
using Xunit;

namespace SpeechChem.Tests
{
    public class GridSkipTests
    {
        // One column of 22 cells: blocked 0-4, empty 5-18, a pipe at 19, empty 20-21.
        private static string Column(int x, int y) => y < 5 ? "blocked" : y == 19 ? "pipe" : "";

        private static (int, int) Skip(int x, int y, int dx, int dy)
        {
            GridSkip.Target(x, y, dx, dy, 1, 22, Column, out int tx, out int ty);
            return (tx, ty);
        }

        [Fact]
        public void LandsOnTheFirstCellThatDiffers()
        {
            Assert.Equal((0, 5), Skip(0, 4, 0, 1));  // blocked -> the first empty cell
            Assert.Equal((0, 19), Skip(0, 5, 0, 1)); // empty run -> the pipe
            Assert.Equal((0, 4), Skip(0, 5, 0, -1)); // and back up onto the blocked run
        }

        [Fact]
        public void StopsAtTheEdgeWhenTheRunReachesIt()
        {
            Assert.Equal((0, 21), Skip(0, 20, 0, 1));
            Assert.Equal((0, 0), Skip(0, 3, 0, -1));
        }

        [Fact]
        public void StaysPutOnTheEdge()
        {
            Assert.Equal((0, 21), Skip(0, 21, 0, 1));
            Assert.Equal((0, 0), Skip(0, 0, 0, -1));
        }
    }
}
