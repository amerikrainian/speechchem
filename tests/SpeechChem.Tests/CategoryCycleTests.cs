using SpeechChem.UI;
using Xunit;

namespace SpeechChem.Tests
{
    public class CategoryCycleTests
    {
        // Five categories; 1 and 3 are empty.
        private static readonly int[] Counts = { 2, 0, 4, 0, 1 };
        private static int Count(int i) => Counts[i];

        [Fact]
        public void SkipsEmptyCategoriesBothWays()
        {
            Assert.Equal(2, CategoryCycle.Next(0, 1, 5, Count));
            Assert.Equal(4, CategoryCycle.Next(2, 1, 5, Count));
            Assert.Equal(2, CategoryCycle.Next(4, -1, 5, Count));
            Assert.Equal(0, CategoryCycle.Next(2, -1, 5, Count));
        }

        [Fact]
        public void WrapsAround()
        {
            Assert.Equal(0, CategoryCycle.Next(4, 1, 5, Count));
            Assert.Equal(4, CategoryCycle.Next(0, -1, 5, Count));
        }

        [Fact]
        public void StartsAtTheFirstOrLastNonEmpty()
        {
            Assert.Equal(0, CategoryCycle.Next(-1, 1, 5, Count));
            Assert.Equal(4, CategoryCycle.Next(-1, -1, 5, Count));
        }

        [Fact]
        public void AllEmptyIsNone()
        {
            Assert.Equal(-1, CategoryCycle.Next(0, 1, 3, i => 0));
            Assert.Equal(-1, CategoryCycle.ForItems(1, 3, i => 0));
        }

        [Fact]
        public void ItemsKeepACategoryWhileItHoldsSomething()
        {
            Assert.Equal(2, CategoryCycle.ForItems(2, 5, Count));
            Assert.Equal(0, CategoryCycle.ForItems(3, 5, Count)); // emptied: the first non-empty
            Assert.Equal(0, CategoryCycle.ForItems(-1, 5, Count));
        }
    }
}
