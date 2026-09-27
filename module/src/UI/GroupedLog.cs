using System;
using System.Collections.Generic;

namespace SpeechChem.UI
{
    /// <summary>
    /// The store behind the windowed run logs (the reactor run log; ported from Echopunks): text
    /// entries grouped under keys in arrival order, UNCAPPED by design ("virtually limitless" — user rule;
    /// the graph side materializes only a window of groups around the focused one, so the store
    /// can afford to keep the whole run; realistic runs are a few MB). The entry cap is pure
    /// INSURANCE against the one unbounded case — a non-terminating solution left
    /// fast-forwarding unattended for hours (~7k entries/s) — set so high it never fires in
    /// real use ("never crash the game"): past it the OLDEST groups drop whole, silently, in
    /// chunks with one index rebuild each (amortized O(1) per add). BCL-pure; unit-tested
    /// (GroupedLogTests).
    /// </summary>
    internal sealed class GroupedLog<TKey> where TKey : IEquatable<TKey>
    {
        private readonly int _maxEntries;

        public GroupedLog(int maxEntries)
        {
            _maxEntries = maxEntries > 0 ? maxEntries : 1;
        }

        private readonly List<TKey> _order = new List<TKey>();
        private readonly Dictionary<TKey, List<string>> _entries = new Dictionary<TKey, List<string>>();
        private readonly Dictionary<TKey, int> _pos = new Dictionary<TKey, int>(); // key -> index in _order
        // Sparse per-entry payloads (a crash snapshot the view opens on Enter): key -> entry index -> tag.
        private readonly Dictionary<TKey, Dictionary<int, object>> _tags = new Dictionary<TKey, Dictionary<int, object>>();
        private int _count;

        /// <summary>Groups holding entries, oldest first.</summary>
        public IReadOnlyList<TKey> Groups => _order;

        public bool IsEmpty => _order.Count == 0;

        /// <summary>Total entries across all groups.</summary>
        public int EntryCount => _count;

        /// <summary>Groups the insurance cap shed, oldest-first (realistically always 0; kept
        /// for the dev log line and the unit tests).</summary>
        public int DroppedGroups { get; private set; }

        public void Clear()
        {
            _order.Clear();
            _entries.Clear();
            _pos.Clear();
            _tags.Clear();
            _count = 0;
            DroppedGroups = 0;
        }

        public void Add(TKey key, string text) => Add(key, text, null);

        /// <summary>Add an entry carrying a payload the view can act on (null = plain text).</summary>
        public void Add(TKey key, string text, object tag)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (tag != null)
            {
                Dictionary<int, object> tags;
                if (!_tags.TryGetValue(key, out tags)) _tags[key] = tags = new Dictionary<int, object>();
                tags[Entries(key).Count] = tag;
            }
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

        // Runaway backstop: shed the oldest groups whole until 5% under the cap, so the drop
        // and its index rebuild run rarely.
        private void DropOldest()
        {
            int target = _maxEntries - _maxEntries / 20;
            int k = 0;
            while (k < _order.Count - 1 && _count > target)
            {
                TKey key = _order[k];
                _count -= _entries[key].Count;
                _entries.Remove(key);
                _pos.Remove(key);
                _tags.Remove(key);
                k++;
                DroppedGroups++;
            }
            _order.RemoveRange(0, k);
            for (int i = 0; i < _order.Count; i++) _pos[_order[i]] = i;
        }

        public IReadOnlyList<string> Entries(TKey key)
        {
            List<string> list;
            return _entries.TryGetValue(key, out list) ? list : (IReadOnlyList<string>)new string[0];
        }

        /// <summary>The payload of entry <paramref name="index"/> of a group, or null.</summary>
        public object TagAt(TKey key, int index)
        {
            Dictionary<int, object> tags;
            object tag;
            return _tags.TryGetValue(key, out tags) && tags.TryGetValue(index, out tag) ? tag : null;
        }

        /// <summary>The group's index in <see cref="Groups"/>, or -1 when absent (never added,
        /// cleared, or shed by the backstop).</summary>
        public int IndexOf(TKey key)
        {
            int i;
            return _pos.TryGetValue(key, out i) ? i : -1;
        }
    }
}
