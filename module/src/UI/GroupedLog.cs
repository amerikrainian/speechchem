using System;
using System.Collections;
using System.Collections.Generic;

namespace SpeechChem.UI
{
    /// <summary>
    /// The store behind the windowed run logs (ported from Echopunks with its compact storage):
    /// entries grouped under keys in arrival order, UNCAPPED by design ("virtually limitless" — user
    /// rule; the graph side materializes only a window of groups around the focused one, so the
    /// store keeps the whole run). The entry cap is pure INSURANCE against the one unbounded case —
    /// a non-terminating solution left running unattended for hours — set so high it never fires in
    /// real use ("never crash the game"): past it the OLDEST groups drop whole, silently, in chunks
    /// with one index rebuild each (amortized O(1) per add). BCL-pure; unit-tested.
    ///
    /// COMPACT STORAGE (Echopunks 2026-09-30, after a 1M-cycle run held 8M string objects + 1M
    /// lists, ~860 MB, with every full GC walking all of them while the run had 9 DISTINCT texts):
    /// values are INTERNED into a table (equal values — by the comparer — stored once) and entries
    /// are int ids in one chunked arrival-order sequence; a group is a contiguous run of it
    /// (<see cref="_start"/>). Only the NEWEST group can extend its run, so an add to an older group
    /// goes to that group's LATE list, read after the run — the arrival order a per-group list
    /// kept. Chunks hold no references, so the GC never scans the entries, and they stay under the
    /// large-object threshold. SpeechChem stores run EVENTS (Narration/NarrationEvent, interned by
    /// content), so a looping program's millions of entries share a handful of records.
    ///
    /// THE GROUP INDEX (2026-10-04, a real top-speed run: ~13k cycle groups a second): the group
    /// keys and run starts live in <see cref="ChunkedList{T}"/>s (no doubling regrowth, nothing on
    /// the large-object heap — a 32-bit process can fail one contiguous 70 MB array), and a group
    /// is found by BINARY SEARCH while keys only grow (cycles do); the first key that arrives out of
    /// order switches to a dictionary for good (until Clear).
    /// </summary>
    internal sealed class GroupedLog<TKey, TValue> where TKey : IEquatable<TKey>, IComparable<TKey> where TValue : class
    {
        private const int ChunkBits = 14; // 16384 ints = 64 KB, under the 85 KB LOH threshold
        private const int ChunkSize = 1 << ChunkBits;
        private const int ChunkMask = ChunkSize - 1;
        private const int CompactThreshold = 4096; // value tables this small aren't worth compacting

        private static readonly TValue[] NoEntries = new TValue[0];

        private readonly int _maxEntries;
        private readonly IEqualityComparer<TValue> _comparer;

        public GroupedLog(int maxEntries, IEqualityComparer<TValue> comparer = null)
        {
            _maxEntries = maxEntries > 0 ? maxEntries : 1;
            _comparer = comparer ?? EqualityComparer<TValue>.Default;
            _ids = new Dictionary<TValue, int>(_comparer);
        }

        // Value table: each distinct value once.
        private List<TValue> _values = new List<TValue>();
        private Dictionary<TValue, int> _ids;

        // Every entry's value id in arrival order, chunked. Positions are ABSOLUTE: slots
        // [_base, _length) are held (those before _start[0] belong to shed groups — the first
        // chunk's dead head), and shedding whole chunks only moves _base (a multiple of the chunk
        // size), so a backstop drop never rewrites the millions of group starts.
        private readonly List<int[]> _chunks = new List<int[]>();
        private int _length;
        private int _base;
        /// <summary>Absolute positions are rebased past this, long before int overflow (tests lower it).</summary>
        internal int RebaseAt = 1 << 30;

        private int[] ChunkOf(int position) => _chunks[(position - _base) >> ChunkBits];

#if DEBUG
        /// <summary>The last backstop drop's duration (Dev/LogStress reports it).</summary>
        internal double LastDropMs;
#endif

        private readonly ChunkedList<TKey> _order = new ChunkedList<TKey>();
        private readonly ChunkedList<int> _start = new ChunkedList<int>(); // group i's run = [_start[i], _start[i+1] or _length)
        private Dictionary<TKey, int> _pos; // key -> index in _order; null while keys only grow (binary search)
        private readonly Dictionary<TKey, List<int>> _late = new Dictionary<TKey, List<int>>();
        private int _count;

        /// <summary>Groups holding entries, oldest first.</summary>
        public IReadOnlyList<TKey> Groups => _order;

        public bool IsEmpty => _order.Count == 0;

        /// <summary>Total entries across all groups.</summary>
        public int EntryCount => _count;

        /// <summary>Groups the insurance cap shed, oldest-first (realistically always 0; kept
        /// for the dev log line and the unit tests).</summary>
        public int DroppedGroups { get; private set; }

        /// <summary>Distinct values held (tests and diagnostics).</summary>
        internal int DistinctValues => _values.Count;

        /// <summary>Changes whenever value ids stop meaning what they meant (a clear, a value-table
        /// compaction): caches keyed by <see cref="IdAt"/> drop everything when it moves.</summary>
        public int Version { get; private set; }

        public void Clear()
        {
            _values = new List<TValue>();
            _ids = new Dictionary<TValue, int>(_comparer);
            _chunks.Clear();
            _length = 0;
            _base = 0;
            _order.Clear();
            _start.Clear();
            _pos = null;
            _late.Clear();
            _count = 0;
            DroppedGroups = 0;
            Version++;
        }

        public void Add(TKey key, TValue value)
        {
            if (value == null || (value is string s && s.Length == 0)) return;
            int id;
            if (!_ids.TryGetValue(value, out id))
            {
                id = _values.Count;
                _values.Add(value);
                _ids[value] = id;
            }
            int last = _order.Count - 1;
            int gi = last >= 0 && _order[last].Equals(key) ? last : IndexOf(key);
            if (gi < 0)
            {
                if (_pos == null && last >= 0 && key.CompareTo(_order[last]) < 0) BuildIndex();
                gi = _order.Count;
                if (_pos != null) _pos[key] = gi;
                _order.Add(key);
                _start.Add(_length);
            }
            if (gi == _order.Count - 1) Append(id);
            else
            {
                List<int> late;
                if (!_late.TryGetValue(key, out late)) _late[key] = late = new List<int>();
                late.Add(id);
            }
            _count++;
            if (_count > _maxEntries) DropOldest();
        }

        private void Append(int id)
        {
            int c = (_length - _base) >> ChunkBits;
            if (c == _chunks.Count) _chunks.Add(new int[ChunkSize]);
            _chunks[c][_length & ChunkMask] = id;
            _length++;
        }

        private int RunEnd(int gi) => gi + 1 < _start.Count ? _start[gi + 1] : _length;

        private int GroupCount(int gi)
        {
            List<int> late;
            int n = RunEnd(gi) - _start[gi];
            return _late.TryGetValue(_order[gi], out late) ? n + late.Count : n;
        }

        // Runaway backstop: shed the oldest groups whole until 5% under the cap, so the drop
        // and its index rebuild run rarely.
        private void DropOldest()
        {
#if DEBUG
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try { DropOldestCore(); }
            finally { LastDropMs = clock.Elapsed.TotalMilliseconds; }
        }

        private void DropOldestCore()
        {
#endif
            int target = _maxEntries - _maxEntries / 20;
            int k = 0;
            while (k < _order.Count - 1 && _count > target)
            {
                TKey key = _order[k];
                _count -= GroupCount(k);
                _late.Remove(key);
                k++;
                DroppedGroups++;
            }
            if (k == 0) return; // only the newest group is left; it is never shed
            _order.RemoveFirst(k);
            _start.RemoveFirst(k);
            int deadChunks = (_start[0] - _base) >> ChunkBits;
            if (deadChunks > 0)
            {
                _chunks.RemoveRange(0, deadChunks);
                _base += deadChunks << ChunkBits;
                if (_base >= RebaseAt)
                {
                    // Every ~10^9 entries: renumber from zero (the one O(groups) pass).
                    int shift = _base;
                    _base = 0;
                    _length -= shift;
                    for (int i = 0; i < _start.Count; i++) _start[i] -= shift;
                }
            }
            if (_pos != null) BuildIndex();
            if (_values.Count > CompactThreshold) CompactValues();
        }

        // Drop values only shed groups used, so a runaway of ever-new values stays bounded by the
        // cap too. Renumbers the survivors' ids; runs once per backstop drop.
        private void CompactValues()
        {
            var remap = new int[_values.Count];
            for (int i = 0; i < remap.Length; i++) remap[i] = -1;
            var values = new List<TValue>();
            var ids = new Dictionary<TValue, int>(_comparer);
            Func<int, int> map = old =>
            {
                int nu = remap[old];
                if (nu < 0)
                {
                    nu = remap[old] = values.Count;
                    values.Add(_values[old]);
                    ids[_values[old]] = nu;
                }
                return nu;
            };
            for (int p = _start[0]; p < _length; p++)
            {
                int[] chunk = ChunkOf(p);
                chunk[p & ChunkMask] = map(chunk[p & ChunkMask]);
            }
            foreach (var late in _late.Values)
                for (int i = 0; i < late.Count; i++) late[i] = map(late[i]);
            _values = values;
            _ids = ids;
            Version++;
        }

        // Keys stopped growing: index them by dictionary from now on.
        private void BuildIndex()
        {
            _pos = new Dictionary<TKey, int>(Math.Max(16, _order.Count));
            for (int i = 0; i < _order.Count; i++) _pos[_order[i]] = i;
        }

        /// <summary>The number of entries in a group (0 when absent) — no allocation.</summary>
        public int CountOf(TKey key)
        {
            int gi = IndexOf(key);
            return gi >= 0 ? GroupCount(gi) : 0;
        }

        /// <summary>The value id of entry <paramref name="index"/> of a group (stable until
        /// <see cref="Version"/> changes), or -1.</summary>
        public int IdAt(TKey key, int index)
        {
            int gi = index < 0 ? -1 : IndexOf(key);
            if (gi < 0) return -1;
            int run = RunEnd(gi) - _start[gi];
            if (index < run)
            {
                int p = _start[gi] + index;
                return ChunkOf(p)[p & ChunkMask];
            }
            List<int> late;
            return _late.TryGetValue(key, out late) && index - run < late.Count ? late[index - run] : -1;
        }

        /// <summary>The value behind an id from <see cref="IdAt"/>.</summary>
        public TValue ValueOf(int id) => id >= 0 && id < _values.Count ? _values[id] : null;

        /// <summary>The group's entries in arrival order (empty when absent). A snapshot VIEW
        /// over the store: read it right away — it is not valid across a later Add or Clear.</summary>
        public IReadOnlyList<TValue> Entries(TKey key)
        {
            int gi = IndexOf(key);
            if (gi < 0) return NoEntries;
            List<int> late;
            _late.TryGetValue(key, out late);
            return new EntryView(this, _start[gi], RunEnd(gi) - _start[gi], late);
        }

        /// <summary>The group's index in <see cref="Groups"/>, or -1 when absent (never added,
        /// cleared, or shed by the backstop).</summary>
        public int IndexOf(TKey key)
        {
            int n = _order.Count;
            if (n == 0) return -1;
            if (_pos != null)
            {
                int i;
                return _pos.TryGetValue(key, out i) ? i : -1;
            }
            // Keys only grew: the newest is the usual target, else binary search.
            if (_order[n - 1].Equals(key)) return n - 1;
            int lo = 0, hi = n - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                int c = _order[mid].CompareTo(key);
                if (c == 0) return mid;
                if (c < 0) lo = mid + 1; else hi = mid - 1;
            }
            return -1;
        }

        private sealed class EntryView : IReadOnlyList<TValue>
        {
            private readonly GroupedLog<TKey, TValue> _log;
            private readonly int _start, _run, _count;
            private readonly List<int> _late;

            public EntryView(GroupedLog<TKey, TValue> log, int start, int run, List<int> late)
            {
                _log = log;
                _start = start;
                _run = run;
                _late = late;
                _count = run + (late != null ? late.Count : 0);
            }

            public int Count => _count;

            public TValue this[int index]
            {
                get
                {
                    if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
                    int id;
                    if (index < _run)
                    {
                        int p = _start + index;
                        id = _log.ChunkOf(p)[p & ChunkMask];
                    }
                    else id = _late[index - _run];
                    return _log._values[id];
                }
            }

            public IEnumerator<TValue> GetEnumerator()
            {
                for (int i = 0; i < _count; i++) yield return this[i];
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
