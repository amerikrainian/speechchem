using System;
using SpeechChem.UI;
using Xunit;

namespace SpeechChem.Tests
{
    public class GroupedLogTests
    {
        [Fact]
        public void GroupsKeepArrivalOrderAndEntries()
        {
            var log = new GroupedLog<int>(100);
            log.Add(3, "a");
            log.Add(1, "b");
            log.Add(3, "c");
            Assert.Equal(new[] { 3, 1 }, log.Groups);
            Assert.Equal(new[] { "a", "c" }, log.Entries(3));
            Assert.Equal(1, log.IndexOf(1));
            Assert.Equal(-1, log.IndexOf(7));
            Assert.Equal(3, log.EntryCount);
        }

        [Fact]
        public void TagsStayWithTheirEntryAndClearWithTheLog()
        {
            var log = new GroupedLog<int>(100);
            var snapshot = new object();
            log.Add(5, "a");
            log.Add(5, "crash", snapshot);
            log.Add(5, "c");
            Assert.Null(log.TagAt(5, 0));
            Assert.Same(snapshot, log.TagAt(5, 1));
            Assert.Null(log.TagAt(5, 2));
            Assert.Null(log.TagAt(9, 0));
            log.Clear();
            Assert.Null(log.TagAt(5, 1));
        }

        [Fact]
        public void EmptyTextIsIgnored()
        {
            var log = new GroupedLog<int>(100);
            log.Add(1, "");
            log.Add(1, null);
            Assert.True(log.IsEmpty);
        }

        [Fact]
        public void ClearEmptiesEverything()
        {
            var log = new GroupedLog<int>(100);
            log.Add(1, "a");
            log.Clear();
            Assert.True(log.IsEmpty);
            Assert.Equal(0, log.EntryCount);
            Assert.Empty(log.Entries(1));
            Assert.Equal(-1, log.IndexOf(1));
        }

        [Fact]
        public void CapShedsOldestGroupsWholeAndReindexes()
        {
            var log = new GroupedLog<int>(20);
            for (int g = 0; g < 10; g++)
                for (int i = 0; i < 3; i++) log.Add(g, g + "." + i);
            Assert.True(log.EntryCount <= 20);
            Assert.True(log.DroppedGroups > 0);
            Assert.Equal(log.DroppedGroups, log.Groups[0]);
            for (int i = 0; i < log.Groups.Count; i++) Assert.Equal(i, log.IndexOf(log.Groups[i]));
            Assert.Equal(-1, log.IndexOf(0));
        }

        [Fact]
        public void CapNeverShedsTheLiveGroup()
        {
            var log = new GroupedLog<int>(5);
            for (int i = 0; i < 50; i++) log.Add(1, "x" + i);
            Assert.Equal(new[] { 1 }, log.Groups);
            Assert.Equal(50, log.Entries(1).Count);
        }
    }

    public class WindowedLogViewTests
    {
        private static Tuple<int, int> Window(int count, int anchor, Func<int, int> rows)
        {
            int lo, hi;
            WindowedLogView<int>.ExpandWindow(count, anchor, rows, out lo, out hi);
            return Tuple.Create(lo, hi);
        }

        [Fact]
        public void EmptyLogHasEmptyWindow()
        {
            Assert.Equal(Tuple.Create(0, -1), Window(0, 0, i => 1));
        }

        [Fact]
        public void SmallLogIsWhollyMaterialized()
        {
            Assert.Equal(Tuple.Create(0, 9), Window(10, 4, i => 1));
        }

        [Fact]
        public void WindowCentersOnTheAnchor()
        {
            var w = Window(1000, 500, i => 1);
            Assert.Equal(WindowedLogView<int>.WindowRegions, w.Item2 - w.Item1 + 1);
            Assert.InRange(500 - w.Item1, 24, 26);
        }

        [Fact]
        public void TailAnchorStillGetsAFullWindow()
        {
            var w = Window(1000, 999, i => 1);
            Assert.Equal(999, w.Item2);
            Assert.Equal(WindowedLogView<int>.WindowRegions, w.Item2 - w.Item1 + 1);
        }

        [Fact]
        public void RowBudgetLimitsHeavyGroups()
        {
            var w = Window(1000, 500, i => 500);
            int rows = (w.Item2 - w.Item1 + 1) * 500;
            Assert.True(rows < WindowedLogView<int>.WindowRows + 500);
            Assert.InRange(500, w.Item1, w.Item2);
        }

        [Fact]
        public void HugeAnchorGroupIsAloneButPresent()
        {
            var w = Window(10, 5, i => i == 5 ? 100000 : 1);
            Assert.Equal(Tuple.Create(5, 5), w);
        }
    }
}
