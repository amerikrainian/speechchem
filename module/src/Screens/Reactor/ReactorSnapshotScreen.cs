using System;
using System.Collections.Generic;
using Impeller;
using SpeechChem.Localization;
using SpeechChem.UI;
using SpeechChem.UI.Graph;
using ReactorModel = SpaceChem.Reactor.Reactor;

namespace SpeechChem.Screens.Reactor
{
    /// <summary>
    /// The reactor as it stood when a run failed (user request, 2026-09-27). A reaction error
    /// (GoalTracker.smethod_12: atom collision, atom against the wall, a molecule pulled apart)
    /// pauses the run under the error box, and closing the box stops it — Class258.smethod_17(0)
    /// clears the molecules and sends the waldos home, so the crash scene is gone the moment the
    /// box is dismissed. Patches/RunCapture takes this snapshot as the box opens (the reactor is
    /// still frozen) and hangs it on the run log's error entry; Enter there opens it.
    /// </summary>
    internal sealed class ReactorSnapshot
    {
        public int Width, Height, Cycle;
        public string[,] Contents; // spoken cell contents, "" when empty
        public string[,] Zones;    // zone name or null
        public bool[,] Marked;     // the error box's markers

        /// <summary>Every cell's full contents — instructions on every layer (hidden ones too),
        /// hardware, waldos, every atom with its bonds — and the error markers (screen pixels, as
        /// the box draws them) mapped to cells.</summary>
        public static ReactorSnapshot Capture(ReactorModel r, IEnumerable<Vector2i> markers, int cycle)
        {
            if (r == null) return null;
            var size = r.method_1();
            var s = new ReactorSnapshot
            {
                Width = size.int_0,
                Height = size.int_1,
                Cycle = cycle,
                Contents = new string[size.int_0, size.int_1],
                Zones = new string[size.int_0, size.int_1],
                Marked = new bool[size.int_0, size.int_1],
            };
            for (int y = 0; y < s.Height; y++)
                for (int x = 0; x < s.Width; x++)
                {
                    s.Contents[x, y] = string.Join(", ", ReactorEditorScreen.CellContents(r, x, y, allLayers: true).ToArray());
                    s.Zones[x, y] = ReactorEditorScreen.ZoneAt(r, x, y);
                }
            if (markers != null)
                foreach (var p in markers)
                {
                    var rel = p - r.vector2i_0;
                    if (rel.int_0 < 0 || rel.int_1 < 0) continue;
                    int x = rel.int_0 / ReactorModel.vector2i_5.int_0;
                    int y = rel.int_1 / ReactorModel.vector2i_5.int_1;
                    if (x < s.Width && y < s.Height) s.Marked[x, y] = true;
                }
            return s;
        }
    }

    /// <summary>
    /// A read-only grid over a <see cref="ReactorSnapshot"/>: cells read like the live grid's —
    /// "x, y" first, the zone when entered, the contents — plus "error here" on the error box's
    /// marked cells. Opens on the first marked cell. Home / End = row edges. A CHILD sub-screen
    /// (mod-side modal): Escape closes back to the log entry.
    /// </summary>
    internal sealed class ReactorSnapshotScreen : Screen
    {
        private readonly ReactorSnapshot _s;
        private string _lastZone;
        private bool _zoneInit;
        private int _zoneX = -1, _zoneY = -1;

        public ReactorSnapshotScreen(ReactorSnapshot snapshot) => _s = snapshot;

        public override string Key => "reactor.snapshot";
        public override string ScreenName => Loc.T("snapshot.title", new { n = _s.Cycle });
        public override bool IsActive() => true;
        public override bool ModalCapturesEscape => true;
        public override bool Exclusive => true;

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, () => ParentScreen?.RemoveChild(this));
        }

        private static ControlId CellId(int x, int y) => ControlId.Structural("reactor.snapshot.cell." + x + "." + y);

        public override void Build(GraphBuilder b)
        {
            if (_s == null) return;
            ControlId start = null;
            for (int y = 0; y < _s.Height; y++)
            {
                b.StartRow("reactor.snapshot.row");
                for (int x = 0; x < _s.Width; x++)
                {
                    int cx = x, cy = y;
                    if (start == null && _s.Marked[x, y]) start = CellId(x, y);
                    b.AddItem(CellId(x, y), new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[] { new NodeAnnouncement(() => Readout(cx, cy), kind: AnnouncementKinds.Label) },
                        SpeaksOwnPosition = true,
                        OnSelect = () => Land(cx, cy),
                        OnJumpEdge = first => { Navigation.FocusNode(CellId(first ? 0 : _s.Width - 1, cy)); return true; },
                    });
                }
                b.EndRow();
            }
            b.SetStart(start ?? CellId(0, 0));
        }

        /// <summary>Note a zone crossing, so the zone is read once on entering it (as the grid does).</summary>
        private void Land(int x, int y)
        {
            string zone = _s.Zones[x, y];
            if (zone != null && zone != _lastZone) { _zoneX = x; _zoneY = y; }
            else _zoneX = _zoneY = -1;
            _lastZone = zone;
        }

        private string Readout(int x, int y)
        {
            var parts = new List<string> { Loc.T("reactor.cell", new { x = x + 1, y = y + 1 }) };
            if (!_zoneInit)
            {
                _zoneInit = true;
                _lastZone = _s.Zones[x, y];
                if (_lastZone != null) parts.Add(_lastZone);
            }
            else if (x == _zoneX && y == _zoneY) parts.Add(_s.Zones[x, y]);
            if (!string.IsNullOrEmpty(_s.Contents[x, y])) parts.Add(_s.Contents[x, y]);
            if (_s.Marked[x, y]) parts.Add(Loc.T("snapshot.marked"));
            return string.Join(", ", parts.ToArray());
        }
    }
}
