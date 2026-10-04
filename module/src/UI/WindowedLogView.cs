using System;
using System.Collections.Generic;
using SpeechChem.UI.Graph;

namespace SpeechChem.UI
{
    /// <summary>
    /// A virtually limitless log as a Tab stop, reusable by any screen: the store
    /// (<see cref="GroupedLog{TKey, TValue}"/>) keeps the whole run, the graph materializes only a
    /// WINDOW of groups around an anchor, so a million-row log costs the navigator what fifty
    /// groups do. Generalized from the Echopunks editor's execution log:
    ///
    ///  • one REGION per group (Ctrl+Up/Down hop groups, arrows walk rows) under a group label;
    ///  • the window's anchor FOLLOWS FOCUS (read from the persisted focus cursor, so a store that
    ///    is cleared under focus doesn't unpin it), and it is re-centered during every rebuild, so
    ///    moving to the window's edge always finds the next chunk already loaded — the log
    ///    "scrolls" by itself;
    ///  • with no anchor (focus elsewhere, or a fresh log) the window TRACKS THE TAIL — the live
    ///    end of a running simulation;
    ///  • Home/End jump to the whole LOG's first/last row, not the window's edge (the anchor is set
    ///    first and held for a couple of rebuilds while the deferred focus applies).
    ///
    /// ROWS ARE CACHED (2026-10-04, a 500k-entry stress run): screens rebuild their graph every
    /// frame, and rendering the whole window each time (up to 1200 formatted rows) was ~2 MB of
    /// garbage per frame. A row's id, node and text are made once and reused until the store's
    /// <see cref="GroupedLog{TKey, TValue}.Version"/>, or the caller's render revision / scope
    /// (a settings commit, the reactor whose view it is) changes; texts are cached per stored value,
    /// so a looping run's rows share a few strings.
    ///
    /// BCL-pure over the graph core, so the window arithmetic is unit-tested.
    /// </summary>
    internal sealed class WindowedLogView<TKey, TValue> where TKey : struct, IEquatable<TKey>, IComparable<TKey> where TValue : class
    {
        internal const int WindowRegions = 51; // groups materialized around the anchor
        internal const int WindowRows = 1200;  // row budget guarding groups with huge fan-out
        // While focus is elsewhere only the anchor's group is materialized (the graph's nodes are
        // rebuilt every frame): a Tab landing finds its remembered row there, or the tail's first;
        // the full window grows around it on the next rebuild.
        internal const int IdleRegions = 1, IdleRows = 1;
        private const int CacheLimit = 4 * WindowRows; // past it a cache starts over (the window moved far)

        private readonly string _idPrefix;
        private readonly Func<TKey, string> _encode;
        private readonly Func<string, TKey?> _decode;
        private TKey? _anchor;
        private int _jumpHold;

        // The row cache and what it was made for.
        private struct RowKey : IEquatable<RowKey>
        {
            public TKey Group;
            public int Row;
            public bool Equals(RowKey o) => Row == o.Row && Group.Equals(o.Group);
            public override bool Equals(object o) => o is RowKey k && Equals(k);
            public override int GetHashCode() => Group.GetHashCode() * 397 ^ Row;
        }

        private sealed class GroupInfo
        {
            public string Label, Region;
            public ControlId Context;
        }

        private readonly Dictionary<RowKey, KeyValuePair<ControlId, NodeVtable>> _rows = new Dictionary<RowKey, KeyValuePair<ControlId, NodeVtable>>();
        private readonly Dictionary<int, string> _texts = new Dictionary<int, string>();
        private readonly Dictionary<TKey, GroupInfo> _groups = new Dictionary<TKey, GroupInfo>();
        private object _cacheLog;
        private int _cacheVersion = -1, _cacheRevision = int.MinValue;
        private object _cacheScope;
        private GroupedLog<TKey, TValue> _log;
        private Func<bool, bool> _jumpEdge;

        /// <param name="idPrefix">Control-id prefix for this log's rows (unique per screen).</param>
        /// <param name="encode">Group key → an id-safe string (no '.').</param>
        /// <param name="decode">The inverse of <paramref name="encode"/>, null when not a key.</param>
        public WindowedLogView(string idPrefix, Func<TKey, string> encode, Func<string, TKey?> decode)
        {
            _idPrefix = idPrefix;
            _encode = encode;
            _decode = decode;
        }

        /// <summary>Forget the anchor (a new run cleared the store): back to tail-tracking.</summary>
        public void Reset()
        {
            _anchor = null;
            _jumpHold = 0;
        }

        private ControlId RowId(TKey key, int row) => ControlId.Structural(_idPrefix + _encode(key) + "." + row);

        /// <summary>The group a row id belongs to, or null for anything else.</summary>
        internal TKey? GroupOf(ControlId id)
        {
            string s = id?.StructuralKey as string;
            if (s == null || !s.StartsWith(_idPrefix, StringComparison.Ordinal)) return null;
            string rest = s.Substring(_idPrefix.Length);
            int dot = rest.LastIndexOf('.');
            if (dot <= 0) return null;
            return _decode(rest.Substring(0, dot));
        }

        /// <summary>Declare the log's stop (nothing while the store is empty).</summary>
        /// <param name="open">Turns an entry into its Enter action (null = none); such an entry
        /// reads as a button.</param>
        /// <param name="render">An entry's text for this view.</param>
        /// <param name="renderRevision">Changes when <paramref name="render"/> would give different
        /// texts (a settings commit): cached rows are rebuilt.</param>
        /// <param name="renderScope">What else the texts depend on (the reactor whose view this is),
        /// compared by reference: a different one rebuilds the cache.</param>
        public void Build(GraphBuilder b, object stopKey, string title, GroupedLog<TKey, TValue> log,
            Func<TKey, string> groupLabel, ControlId focusCursor, Func<TValue, Action> open,
            Func<TValue, string> render, int renderRevision = 0, object renderScope = null)
        {
            if (log == null || log.IsEmpty) return;
            Validate(log, renderRevision, renderScope);

            var focused = GroupOf(focusCursor);
            if (_jumpHold > 0) _jumpHold--;
            else if (focused != null) _anchor = focused;

            var groups = log.Groups;
            int anchor = _anchor != null ? log.IndexOf(_anchor.Value) : -1;
            if (anchor < 0) anchor = groups.Count - 1;
            int lo, hi;
            if (focused != null || _jumpHold > 0) ExpandWindow(groups.Count, anchor, i => log.CountOf(groups[i]), out lo, out hi);
            else ExpandWindow(groups.Count, anchor, i => log.CountOf(groups[i]), out lo, out hi, IdleRegions, IdleRows);

            if (_rows.Count > CacheLimit) _rows.Clear();
            if (_texts.Count > CacheLimit) _texts.Clear();
            if (_groups.Count > CacheLimit) _groups.Clear();

            b.BeginStop(stopKey);
            b.PushContext(title, positions: false);
            for (int gi = lo; gi <= hi; gi++)
            {
                var key = groups[gi];
                GroupInfo info;
                if (!_groups.TryGetValue(key, out info))
                {
                    string label = groupLabel(key);
                    _groups[key] = info = new GroupInfo
                    {
                        Label = label,
                        Region = _idPrefix + _encode(key),
                        Context = ControlId.Structural("ctx:" + _idPrefix + _encode(key)),
                    };
                }
                b.SetRegion(info.Region);
                b.PushContext(info.Label, id: info.Context);
                int count = log.CountOf(key);
                for (int i = 0; i < count; i++)
                {
                    var rk = new RowKey { Group = key, Row = i };
                    KeyValuePair<ControlId, NodeVtable> row;
                    if (!_rows.TryGetValue(rk, out row))
                    {
                        int id = log.IdAt(key, i);
                        var value = log.ValueOf(id);
                        string text;
                        if (!_texts.TryGetValue(id, out text)) _texts[id] = text = (value != null ? render(value) : null) ?? "";
                        var action = open != null && value != null ? open(value) : null;
                        row = new KeyValuePair<ControlId, NodeVtable>(RowId(key, i), new NodeVtable
                        {
                            ControlType = action != null ? ControlTypes.Button : ControlTypes.Text,
                            Announcements = new[] { new NodeAnnouncement(() => text, kind: AnnouncementKinds.Label) },
                            OnActivate = action,
                            OnJumpEdge = _jumpEdge,
                        });
                        _rows[rk] = row;
                    }
                    b.AddItem(row.Key, row.Value);
                }
                b.PopContext();
            }
            b.SetRegion(null);
            b.PopContext();
        }

        /// <summary>Drop every cached row when what they were made from changed.</summary>
        private void Validate(GroupedLog<TKey, TValue> log, int revision, object scope)
        {
            _log = log;
            if (_jumpEdge == null) _jumpEdge = first => JumpEdge(_log, first);
            if (ReferenceEquals(log, _cacheLog) && log.Version == _cacheVersion && revision == _cacheRevision && ReferenceEquals(scope, _cacheScope)) return;
            _cacheLog = log;
            _cacheVersion = log.Version;
            _cacheRevision = revision;
            _cacheScope = scope;
            _rows.Clear();
            _texts.Clear();
            _groups.Clear();
        }

        /// <summary>Home/End: the whole log's first / last row.</summary>
        private bool JumpEdge(GroupedLog<TKey, TValue> log, bool first)
        {
            if (log == null || log.IsEmpty) return false;
            var groups = log.Groups;
            var key = groups[first ? 0 : groups.Count - 1];
            int row = first ? 0 : log.CountOf(key) - 1;
            _anchor = key;
            _jumpHold = 2;
            Navigation.FocusNode(RowId(key, row));
            return true;
        }

        /// <summary>Grow a window of consecutive group indexes outward from the anchor, alternating
        /// sides, until either budget is spent; at the list's ends the free side keeps growing, so the
        /// tail still gets a full window.</summary>
        internal static void ExpandWindow(int count, int anchor, Func<int, int> rowsAt, out int lo, out int hi,
            int maxRegions = WindowRegions, int maxRows = WindowRows)
        {
            lo = hi = anchor;
            if (count <= 0) { lo = 0; hi = -1; return; }
            int rows = rowsAt(anchor);
            bool grew = true;
            while (grew && hi - lo + 1 < maxRegions && rows < maxRows)
            {
                grew = false;
                if (hi + 1 < count)
                {
                    hi++;
                    rows += rowsAt(hi);
                    grew = true;
                }
                if (lo > 0 && hi - lo + 1 < maxRegions && rows < maxRows)
                {
                    lo--;
                    rows += rowsAt(lo);
                    grew = true;
                }
            }
        }
    }
}
