using System.Collections.Generic;
using Impeller;
using SpaceChem;
using SpaceChem.Pipeline;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;

namespace SpeechChem.Screens.Pipeline
{
    /// <summary>
    /// The pipeline as it stood when a run failed (user request 2026-10-09): a Reaction Error or an
    /// invalid molecule pauses the whole level under the box, and closing it stops the run, which
    /// empties the pipes. Patches/RunCapture captures this as the box opens, with every reactor's
    /// own snapshot (ReactorEditorScreen.RecordCrash). Only what the run changes is kept: each map
    /// cell's contents (the molecules in pipes, building meters, the enemy), which cells hold a
    /// molecule and which the enemy covers (zoomed blocks count them), the Components table's cells
    /// (reactors' waiting text, meters, output counters) and the Enemy stop's rows.
    /// </summary>
    internal sealed class PipelineSnapshot
    {
        public SpaceChem.Pipeline.Pipeline Pipeline;
        public int Width, Height, Cycle;
        public string[,] Contents;  // spoken cell contents (no coordinates), "" when empty
        public string[,] Molecules; // the molecule in the pipe cell (name and formula), null when none
        public bool[,] Enemy;       // the enemy's drawing covers the cell
        public readonly Dictionary<Draggable, string> Components = new Dictionary<Draggable, string>();
        public readonly Dictionary<string, string> Ports = new Dictionary<string, string>(); // port cell key -> text
        public string EnemyName, EnemySpan, EnemyParts, EnemyState;
        public int? Signature;      // the layout when the map first showed it
    }

    public sealed partial class PipelineEditorScreen
    {
        // ---- the crash view: like the reactor's crash overlay, the map, the Components table and
        // the Enemy stop read the snapshot once the run is stopped ("Pipeline at cycle N", the screen
        // name while it shows, says so);
        // everything else is live. An edit on the pipeline (its layout's Signature changes), a new
        // run, another level or ` (Backquote, the game's stop key — ReactorEditorScreen.EndAllCrashes)
        // ends it. Escape is the game's here (the exit prompt), never the view's. ----

        private static PipelineSnapshot _crashPending;
        private PipelineSnapshot _crashView;

        internal static bool HasCrash => _crashPending != null;

        /// <summary>At the error (the run paused under the box): capture the level's pipeline.</summary>
        internal static void RecordCrash(int cycle)
        {
            _crashPending = null;
            var p = Class53.smethod_5<PipelineEditor>()?.pipeline_0;
            if (p == null) return;
            try { _crashPending = Capture(p, cycle); }
            catch (System.Exception ex) { Log.Error("[run] pipeline crash snapshot", ex); }
        }

        internal static void ClearCrash() => _crashPending = null;

        private static PipelineSnapshot Capture(SpaceChem.Pipeline.Pipeline p, int cycle)
        {
            var size = p.method_4();
            var s = new PipelineSnapshot
            {
                Pipeline = p,
                Width = size.int_0,
                Height = size.int_1,
                Cycle = cycle,
                Contents = new string[size.int_0, size.int_1],
                Molecules = new string[size.int_0, size.int_1],
                Enemy = new bool[size.int_0, size.int_1],
            };
            var level = DefenseText.Level;
            var enemy = DefenseText.Enemy(level);
            for (int y = 0; y < s.Height; y++)
                for (int x = 0; x < s.Width; x++)
                {
                    var c = new Vector2i(x, y);
                    s.Contents[x, y] = string.Join(", ", CellContents(p, c).ToArray());
                    s.Molecules[x, y] = MoleculeAt(p, c);
                    s.Enemy[x, y] = level != null && enemy != null && DefenseText.Covers(enemy, c);
                }
            foreach (var kv in PipelineText.Components(p))
            {
                var d = kv.Key;
                s.Components[d] = ComponentLabel(p, d);
                int i = 0;
                foreach (var port in d.class485_0)
                {
                    s.Ports[(string)PortId(d, false, i).StructuralKey] = WithPanel(InputLine(p, d, port.Value, i, named: d is ReactorDraggable), PanelOf(port.Value.method_0()));
                    i++;
                }
                i = 0;
                foreach (var port in d.class485_1)
                {
                    s.Ports[(string)PortId(d, true, i).StructuralKey] = WithPanel(OutputLine(p, d, port.Value, i, named: d is ReactorDraggable), PanelOf(port.Value.method_0()));
                    i++;
                }
            }
            if (level != null && enemy != null)
            {
                s.EnemySpan = DefenseText.Span(enemy);
                if (DefenseText.PartFlags(enemy) != null) s.EnemyParts = DefenseText.PartsText(enemy);
                if (DefenseText.HasEnemyState(enemy.GetType())) s.EnemyState = DefenseText.EnemyState(level, enemy) ?? Loc.T("defense.state.none");
            }
            return s;
        }

        /// <summary>The molecule in the pipe slot of this cell during a run (name and formula), or null.</summary>
        private static string MoleculeAt(SpaceChem.Pipeline.Pipeline p, Vector2i cell)
        {
            var pipe = PipeAt(p, cell);
            if (pipe == null) return null;
            var local = cell - pipe.method_14();
            int i = 0;
            foreach (var c in pipe.linkedList_0)
            {
                if (c == local) break;
                i++;
            }
            int j = 0;
            foreach (var slot in Slots(pipe))
                if (j++ == i) return slot.Text;
            return null;
        }

        /// <summary>The layout as one number: every component and pipe, where it is and how its
        /// pipe runs. Any edit on the pipeline changes it.</summary>
        private static int Signature(SpaceChem.Pipeline.Pipeline p)
        {
            unchecked
            {
                int h = 17;
                foreach (var kv in p.dictionary_1)
                {
                    h = h * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(kv.Key);
                    h = h * 31 + kv.Value.int_0 * 131 + kv.Value.int_1;
                    foreach (var o in kv.Key.class485_1)
                    {
                        var pipe = o.Value.pipeDraggable_0;
                        if (pipe == null) continue;
                        foreach (var c in pipe.linkedList_0) h = h * 31 + c.int_0 * 131 + c.int_1;
                        h = h * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o.Value.vmethod_0());
                    }
                }
                return h;
            }
        }

        /// <summary>Per frame: show the snapshot once the box is gone and the run stopped; end it
        /// on an edit, a new run or when it was cleared.</summary>
        private void WatchCrash(SpaceChem.Pipeline.Pipeline p)
        {
            if (_crashView != null)
            {
                if (Running || !ReferenceEquals(_crashPending, _crashView) || Signature(p) != _crashView.Signature)
                {
                    // An edit acts as stopping the run (user rule 2026-10-09): every snapshot goes,
                    // the reactors' too.
                    if (ReferenceEquals(_crashPending, _crashView)) Reactor.ReactorEditorScreen.ClearCrashes();
                    _crashView = null;
                }
                return;
            }
            var s = _crashPending;
            if (s == null || Running) return; // the box's close stops the run; wait for it
            if (!ReferenceEquals(s.Pipeline, p)) { _crashPending = null; return; }
            if (!s.Signature.HasValue) s.Signature = Signature(p);
            _crashView = s; // the arrival already said so: the screen name is the title (CrashTitle)
        }

        /// <summary>"Pipeline at cycle N" when this pipeline's crash snapshot shows (or will, the
        /// run being stopped), else null.</summary>
        private string CrashTitle()
        {
            var s = _crashView ?? _crashPending;
            if (s == null || Running || !ReferenceEquals(s.Pipeline, Model)) return null;
            return Loc.T("snapshot.pipeline", new { n = s.Cycle });
        }

        /// <summary>`: back to the live pipeline (and every reactor), re-reading the focused node.</summary>
        private void EndAllCrashes()
        {
            if (!Reactor.ReactorEditorScreen.EndAllCrashes()) return;
            if (_crashView == null) return;
            _crashView = null;
            Navigation.AnnounceCurrent();
        }

        private List<string> ViewContents(SpaceChem.Pipeline.Pipeline p, int x, int y)
        {
            var s = _crashView;
            if (s == null || x >= s.Width || y >= s.Height) return CellContents(p, new Vector2i(x, y));
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(s.Contents[x, y])) parts.Add(s.Contents[x, y]);
            return parts;
        }
    }
}
