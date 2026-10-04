using System;
using SpeechChem.UI.Graph;

namespace SpeechChem.UI
{
    /// <summary>
    /// A virtually limitless log as a Tab stop, reusable by any screen: the store
    /// (<see cref="GroupedLog{TKey}"/>) keeps the whole run, the graph materializes only a WINDOW
    /// of groups around an anchor, so a million-row log costs the navigator what fifty groups do.
    /// Generalized from the Echopunks editor's execution log (which had this wired into one
    /// screen):
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
    /// BCL-pure over the graph core, so the window arithmetic is unit-tested.
    /// </summary>
    internal sealed class WindowedLogView<TKey> where TKey : struct, IEquatable<TKey>
    {
        internal const int WindowRegions = 51; // groups materialized around the anchor
        internal const int WindowRows = 1200;  // row budget guarding groups with huge fan-out

        private readonly string _idPrefix;
        private readonly Func<TKey, string> _encode;
        private readonly Func<string, TKey?> _decode;
        private TKey? _anchor;
        private int _jumpHold;

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
        /// <param name="open">Turns an entry's tag into its Enter action (null = tags ignored); a
        /// tagged entry reads as a button.</param>
        /// <param name="render">An entry's text for this view (null = as stored), from its stored
        /// text and its tag — the store keeps one text for every view.</param>
        public void Build(GraphBuilder b, object stopKey, string title, GroupedLog<TKey> log,
            Func<TKey, string> groupLabel, ControlId focusCursor, Func<object, Action> open = null,
            Func<string, object, string> render = null)
        {
            if (log == null || log.IsEmpty) return;

            var focused = GroupOf(focusCursor);
            if (_jumpHold > 0) _jumpHold--;
            else if (focused != null) _anchor = focused;

            var groups = log.Groups;
            int anchor = _anchor != null ? log.IndexOf(_anchor.Value) : -1;
            if (anchor < 0) anchor = groups.Count - 1;
            int lo, hi;
            ExpandWindow(groups.Count, anchor, i => log.Entries(groups[i]).Count, out lo, out hi);

            Func<bool, bool> jumpEdge = first => JumpEdge(log, first);
            b.BeginStop(stopKey);
            b.PushContext(title, positions: false);
            for (int gi = lo; gi <= hi; gi++)
            {
                var key = groups[gi];
                b.SetRegion(_idPrefix + _encode(key));
                b.PushContext(groupLabel(key));
                var entries = log.Entries(key);
                for (int i = 0; i < entries.Count; i++)
                {
                    var tag = log.TagAt(key, i);
                    string text = render != null ? render(entries[i], tag) ?? entries[i] : entries[i];
                    var action = open != null && tag != null ? open(tag) : null;
                    b.AddItem(RowId(key, i), new NodeVtable
                    {
                        ControlType = action != null ? ControlTypes.Button : ControlTypes.Text,
                        Announcements = new[] { new NodeAnnouncement(() => text, kind: AnnouncementKinds.Label) },
                        OnActivate = action,
                        OnJumpEdge = jumpEdge,
                    });
                }
                b.PopContext();
            }
            b.SetRegion(null);
            b.PopContext();
        }

        /// <summary>Home/End: the whole log's first / last row.</summary>
        private bool JumpEdge(GroupedLog<TKey> log, bool first)
        {
            if (log.IsEmpty) return false;
            var groups = log.Groups;
            var key = groups[first ? 0 : groups.Count - 1];
            int row = first ? 0 : log.Entries(key).Count - 1;
            _anchor = key;
            _jumpHold = 2;
            Navigation.FocusNode(RowId(key, row));
            return true;
        }

        /// <summary>Grow a window of consecutive group indexes outward from the anchor, alternating
        /// sides, until either budget is spent; at the list's ends the free side keeps growing, so the
        /// tail still gets a full window.</summary>
        internal static void ExpandWindow(int count, int anchor, Func<int, int> rowsAt, out int lo, out int hi)
        {
            lo = hi = anchor;
            if (count <= 0) { lo = 0; hi = -1; return; }
            int rows = rowsAt(anchor);
            bool grew = true;
            while (grew && hi - lo + 1 < WindowRegions && rows < WindowRows)
            {
                grew = false;
                if (hi + 1 < count)
                {
                    hi++;
                    rows += rowsAt(hi);
                    grew = true;
                }
                if (lo > 0 && hi - lo + 1 < WindowRegions && rows < WindowRows)
                {
                    lo--;
                    rows += rowsAt(lo);
                    grew = true;
                }
            }
        }
    }
}
