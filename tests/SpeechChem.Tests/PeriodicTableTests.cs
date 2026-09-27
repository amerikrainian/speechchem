using System.Collections.Generic;
using SpeechChem.Screens;
using Xunit;

namespace SpeechChem.Tests
{
    public class PeriodicTableTests
    {
        [Fact]
        public void EveryElementHasItsOwnCell()
        {
            var seen = new HashSet<(int, int)>();
            for (int z = 1; z <= 109; z++)
            {
                int row, col;
                Assert.True(PeriodicTableScreen.Place(z, out row, out col), "element " + z);
                Assert.InRange(row, 0, 8);
                Assert.InRange(col, 0, 17);
                Assert.True(seen.Add((row, col)), "element " + z + " collides");
            }
        }

        [Theory]
        [InlineData(6, 1, 13)]   // carbon: period 2, group 14
        [InlineData(26, 3, 7)]   // iron: period 4, group 8
        [InlineData(71, 5, 2)]   // lutetium: group 3
        [InlineData(57, 7, 3)]   // lanthanum: first of the f row
        [InlineData(118, -1, -1)]
        public void KnownPlacements(int z, int row, int col)
        {
            int r, c;
            PeriodicTableScreen.Place(z, out r, out c);
            Assert.Equal(row, r);
            Assert.Equal(col, c);
        }
    }
}
