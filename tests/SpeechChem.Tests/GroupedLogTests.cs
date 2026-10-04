using System;
using System.Collections.Generic;
using System.Linq;
using SpeechChem.UI;
using SpeechChem.UI.Graph;
using Xunit;

namespace SpeechChem.Tests
{
    /// <summary>The compact run-log store against the plain one it replaced (kept here verbatim
    /// as the reference model): whatever the add pattern — newest-group appends, adds to older
    /// groups, backstop drops across chunk boundaries, text-table compaction, clears — every
    /// observable read must match exactly.</summary>
    public class GroupedLogTests
    {
        private static GroupedLog<int, string> NewLog(int cap) => new GroupedLog<int, string>(cap, StringComparer.Ordinal);

        /// <summary>The pre-2026-09-30 GroupedLog: a string list per group.</summary>
        private sealed class ReferenceLog
        {
            private readonly int _maxEntries;
            private readonly List<int> _order = new List<int>();
            private readonly Dictionary<int, List<string>> _entries = new Dictionary<int, List<string>>();
            private readonly Dictionary<int, int> _pos = new Dictionary<int, int>();
            private int _count;

            public ReferenceLog(int maxEntries) { _maxEntries = maxEntries > 0 ? maxEntries : 1; }
            public IReadOnlyList<int> Groups => _order;
            public bool IsEmpty => _order.Count == 0;
            public int EntryCount => _count;
            public int DroppedGroups { get; private set; }

            public void Clear()
            {
                _order.Clear(); _entries.Clear(); _pos.Clear(); _count = 0; DroppedGroups = 0;
            }

            public void Add(int key, string text)
            {
                if (string.IsNullOrEmpty(text)) return;
                List<string> list;
                if (!_entries.TryGetValue(key, out list))
                {
                    list = new List<string>();
                    _entries[key] = list;
                    _pos[key] = _order.Count;
                    _order.Add(key);
                }
                list.Add(text);
                _count++;
                if (_count > _maxEntries) DropOldest();
            }

            private void DropOldest()
            {
                int target = _maxEntries - _maxEntries / 20;
                int k = 0;
                while (k < _order.Count - 1 && _count > target)
                {
                    int key = _order[k];
                    _count -= _entries[key].Count;
                    _entries.Remove(key);
                    _pos.Remove(key);
                    k++;
                    DroppedGroups++;
                }
                _order.RemoveRange(0, k);
                for (int i = 0; i < _order.Count; i++) _pos[_order[i]] = i;
            }

            public IReadOnlyList<string> Entries(int key)
            {
                List<string> list;
                return _entries.TryGetValue(key, out list) ? list : (IReadOnlyList<string>)new string[0];
            }

            public int IndexOf(int key)
            {
                int i;
                return _pos.TryGetValue(key, out i) ? i : -1;
            }
        }

        private static void AssertSame(ReferenceLog want, GroupedLog<int, string> got, int keySpace)
        {
            Assert.Equal(want.IsEmpty, got.IsEmpty);
            Assert.Equal(want.EntryCount, got.EntryCount);
            Assert.Equal(want.DroppedGroups, got.DroppedGroups);
            Assert.Equal(want.Groups, got.Groups);
            for (int k = -1; k <= keySpace; k++)
            {
                Assert.Equal(want.IndexOf(k), got.IndexOf(k));
                var w = want.Entries(k);
                var g = got.Entries(k);
                Assert.Equal(w.Count, g.Count);
                for (int i = 0; i < w.Count; i++) Assert.Equal(w[i], g[i]);
                Assert.Equal(w, g.ToList()); // the enumerator too
            }
        }

        // Each pattern: (seed, cap, adds, keySpace, chance an add targets an OLDER key, distinct
        // texts, chance of a unique never-repeated text, clear every N adds or 0).
        [Theory]
        [InlineData(1, 1000000, 20000, 400, 0.0, 9, 0.0, 0)]        // the looping program: in order, few texts
        [InlineData(2, 1000000, 20000, 60, 0.3, 50, 0.1, 0)]        // out-of-order adds (the test log's case)
        [InlineData(3, 300, 20000, 2000, 0.05, 20, 0.0, 0)]         // tight backstop, many drops
        [InlineData(4, 50000, 200000, 20000, 0.01, 30, 0.0, 0)]     // drops across 16K-entry chunks
        [InlineData(5, 20000, 120000, 5000, 0.02, 10, 0.5, 0)]      // unique texts: compaction under drops
        [InlineData(6, 5, 2000, 3, 0.2, 4, 0.0, 0)]                 // cap far below one group
        [InlineData(7, 2000, 30000, 800, 0.1, 15, 0.2, 7000)]       // clears mid-stream
        public void MatchesThePlainStoreExactly(int seed, int cap, int adds, int keySpace,
            double olderChance, int distinct, double uniqueChance, int clearEvery)
        {
            var rng = new Random(seed);
            var want = new ReferenceLog(cap);
            var got = NewLog(cap);
            int newest = 0, unique = 0;
            for (int n = 1; n <= adds; n++)
            {
                int key;
                if (rng.NextDouble() < olderChance) key = rng.Next(0, newest + 1);
                else
                {
                    if (rng.Next(8) == 0 && newest < keySpace) newest++; // ~8 entries per group
                    key = newest;
                }
                string text = rng.NextDouble() < uniqueChance
                    ? "u" + unique++
                    : rng.Next(40) == 0 ? (rng.Next(2) == 0 ? null : "") : "t" + rng.Next(distinct);
                want.Add(key, text);
                got.Add(key, text);
                if (n % 997 == 0) AssertSame(want, got, keySpace);
                if (clearEvery > 0 && n % clearEvery == 0)
                {
                    want.Clear();
                    got.Clear();
                    newest = 0;
                    AssertSame(want, got, keySpace);
                }
            }
            AssertSame(want, got, keySpace);
        }

        [Fact]
        public void StoresEachDistinctTextOnce()
        {
            var log = NewLog(1000000);
            for (int c = 0; c < 10000; c++)
                for (int i = 0; i < 8; i++) log.Add(c, "XA:" + i + ": JUMP STARTUP");
            Assert.Equal(80000, log.EntryCount);
            Assert.Equal(8, log.DistinctValues);
        }

        [Fact]
        public void CompactionBoundsTheTextTableUnderTheBackstop()
        {
            var log = NewLog(10000);
            for (int n = 0; n < 200000; n++) log.Add(n / 4, "wrote #X, now " + n);
            Assert.True(log.EntryCount <= 10000);
            // Every text is unique, so the live table can never need more than the entries it
            // holds plus the growth before the next compaction.
            Assert.True(log.DistinctValues <= 10000 + 4096, "table held " + log.DistinctValues);
            int lastGroup = log.Groups[log.Groups.Count - 1];
            Assert.Equal("wrote #X, now 199999", log.Entries(lastGroup)[log.Entries(lastGroup).Count - 1]);
        }

        [Fact]
        public void GroupsKeepArrivalOrderAndEntries()
        {
            var log = NewLog(100);
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
        public void EmptyTextIsIgnored()
        {
            var log = NewLog(100);
            log.Add(1, "");
            log.Add(1, null);
            Assert.True(log.IsEmpty);
        }

        [Fact]
        public void ClearEmptiesEverything()
        {
            var log = NewLog(100);
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
            var log = NewLog(20);
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
            var log = NewLog(5);
            for (int i = 0; i < 50; i++) log.Add(1, "x" + i);
            Assert.Equal(new[] { 1 }, log.Groups);
            Assert.Equal(50, log.Entries(1).Count);
        }

        [Fact]
        public void RebasingAbsolutePositionsChangesNothingObservable()
        {
            var rng = new Random(11);
            var want = new ReferenceLog(50000);
            var got = NewLog(50000);
            got.RebaseAt = 3 * 16384; // rebase every few chunk drops
            int key = 0;
            for (int n = 1; n <= 400000; n++)
            {
                if (rng.Next(6) == 0) key++;
                string text = "t" + rng.Next(12);
                want.Add(key, text);
                got.Add(key, text);
                if (n % 49999 == 0) AssertSame(want, got, key + 1);
            }
            AssertSame(want, got, key + 1);
            Assert.True(got.DroppedGroups > 0);
        }

        [Fact]
        public void GroupsAreFoundWhetherKeysGrowOrNot()
        {
            var log = NewLog(1000000);
            for (int c = 0; c < 50000; c += 2) log.Add(c, "t" + (c % 7)); // growing: binary search
            Assert.Equal(12345, log.IndexOf(24690));
            Assert.Equal(-1, log.IndexOf(24691));
            log.Add(100, "late");                                         // an older group: its late list
            Assert.Equal(2, log.CountOf(100));
            Assert.Equal("late", log.Entries(100)[1]);
            log.Add(7, "new and older");                                  // a NEW smaller key: dictionary from now on
            Assert.Equal(25000, log.IndexOf(7));
            Assert.Equal(12345, log.IndexOf(24690));
            Assert.Equal(new[] { "new and older" }, log.Entries(7));
            log.Clear();
            log.Add(3, "a");
            log.Add(5, "b");
            Assert.Equal(1, log.IndexOf(5));
        }

        [Fact]
        public void ChunkedListAppendsAndDropsItsHead()
        {
            var list = new ChunkedList<int>();
            for (int i = 0; i < 40000; i++) list.Add(i);
            list.RemoveFirst(20000);                                      // crosses a chunk boundary
            Assert.Equal(20000, list.Count);
            Assert.Equal(20000, list[0]);
            Assert.Equal(39999, list[list.Count - 1]);
            for (int i = 0; i < 10; i++) list.Add(40000 + i);
            Assert.Equal(40009, list[list.Count - 1]);
            list[0] = -1;
            Assert.Equal(-1, list[0]);
            list.RemoveFirst(list.Count);
            Assert.Empty(list);
            list.Add(5);
            Assert.Equal(5, list[0]);
        }

        [Fact]
        public void IdsAndCountsReadWithoutAViewAndVersionMovesOnClear()
        {
            var log = NewLog(100);
            log.Add(1, "a");
            log.Add(2, "b");
            log.Add(1, "a"); // late, same value
            Assert.Equal(2, log.CountOf(1));
            Assert.Equal(log.IdAt(1, 0), log.IdAt(1, 1));
            Assert.Equal("b", log.ValueOf(log.IdAt(2, 0)));
            Assert.Equal(-1, log.IdAt(1, 2));
            Assert.Equal(0, log.CountOf(9));
            int v = log.Version;
            log.Clear();
            Assert.NotEqual(v, log.Version);
        }
    }

    public class WindowedLogViewTests
    {
        private static Tuple<int, int> Window(int count, int anchor, Func<int, int> rows)
        {
            int lo, hi;
            WindowedLogView<int, string>.ExpandWindow(count, anchor, rows, out lo, out hi);
            return Tuple.Create(lo, hi);
        }

        [Fact]
        public void RowsAreReusedUntilTheStoreOrTheRenderingChanges()
        {
            var log = new GroupedLog<int, string>(1000, StringComparer.Ordinal);
            for (int c = 0; c < 5; c++) log.Add(c, "line " + c);
            var view = new WindowedLogView<int, string>("log.", k => k.ToString(), s => { int n; return int.TryParse(s, out n) ? n : (int?)null; });
            int renders = 0;
            Func<int, NodeVtable> build = revision =>
            {
                var b = new GraphBuilder();
                view.Build(b, "stop", "Log", log, k => "Cycle " + k, ControlId.Structural("log.4.0"), null, v => { renders++; return v.ToUpperInvariant(); }, revision);
                return b.Build().Nodes[ControlId.Structural("log.4.0")].Vtable;
            };
            var first = build(1);
            Assert.Equal(5, renders);
            Assert.Same(first, build(1));                  // a rebuild reuses the rows
            Assert.Equal(5, renders);
            log.Add(5, "line 5");
            Assert.Same(first, build(1));                  // new rows only render the new entry
            Assert.Equal(6, renders);
            Assert.NotSame(first, build(2));               // a settings commit renders again
            Assert.Equal(12, renders);
            log.Clear();
            log.Add(4, "again");
            Assert.Equal("AGAIN", build(2).Announcements[0].Text());
        }

        [Fact]
        public void WithFocusElsewhereOnlyTheAnchorGroupIsBuilt()
        {
            var log = new GroupedLog<int, string>(1000, StringComparer.Ordinal);
            for (int c = 0; c < 10; c++) { log.Add(c, "a" + c); log.Add(c, "b" + c); }
            var view = new WindowedLogView<int, string>("log.", k => k.ToString(), s => { int n; return int.TryParse(s, out n) ? n : (int?)null; });
            Func<ControlId, GraphRender> build = focus =>
            {
                var b = new GraphBuilder();
                view.Build(b, "stop", "Log", log, k => "Cycle " + k, focus, null, v => v);
                return b.Build();
            };
            var idle = build(ControlId.Structural("elsewhere"));
            Assert.Equal(2, idle.Nodes.Count);                       // the tail group's two rows
            Assert.True(idle.Nodes.ContainsKey(ControlId.Structural("log.9.0")));
            Assert.Equal(20, build(ControlId.Structural("log.3.1")).Nodes.Count); // focused: the window
            var back = build(ControlId.Structural("elsewhere"));
            Assert.True(back.Nodes.ContainsKey(ControlId.Structural("log.3.1"))); // the anchor stays
            Assert.Equal(2, back.Nodes.Count);
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
            Assert.Equal(WindowedLogView<int, string>.WindowRegions, w.Item2 - w.Item1 + 1);
            Assert.InRange(500 - w.Item1, 24, 26);
        }

        [Fact]
        public void TailAnchorStillGetsAFullWindow()
        {
            var w = Window(1000, 999, i => 1);
            Assert.Equal(999, w.Item2);
            Assert.Equal(WindowedLogView<int, string>.WindowRegions, w.Item2 - w.Item1 + 1);
        }

        [Fact]
        public void RowBudgetLimitsHeavyGroups()
        {
            var w = Window(1000, 500, i => 500);
            int rows = (w.Item2 - w.Item1 + 1) * 500;
            Assert.True(rows < WindowedLogView<int, string>.WindowRows + 500);
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
