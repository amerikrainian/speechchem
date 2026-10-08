using System;
using System.Collections.Generic;
using Impeller;
using SpaceChem;
using SpaceChem.Pipeline;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens.Pipeline
{
    public sealed partial class PipelineEditorScreen
    {
        // ---- zoom (user design 2026-10-07; OniAccess's big cursor and FactorioAccess's cursor
        // size, with FIXED BLOCKS instead of a square centred on the cursor: their worlds have no
        // meaningful origin, this 32 x 22 map does, and a reactor is exactly 4 x 4). At size 4 or
        // 8 the map is cut into blocks from 1, 1 (the last row / column of blocks smaller) and the
        // map stop holds one node per block. The cell cursor stays: it rides along keeping its
        // place inside the block, so zooming back in lands where expected. Shift+Up / Shift+Down
        // step the size, Ctrl+Shift+Up / Ctrl+Shift+Down jump to 8 / 1. A block reads its range,
        // then (only when it holds anything — silence is faster) buildings, pipes, crossings, open
        // ends, molecules in transit, the enemy, the free count; Shift+Backspace the long form.
        // Enter places the armed item at the block's top-left; Delete / cut / copy with no
        // rectangle take the movable buildings wholly inside the block; Ctrl+V pastes at its
        // top-left; Shift+Space marks whole blocks. ----

        private static readonly int[] ZoomSizes = { 1, 4, 8 };
        private int _zoom = 1;
        private int _zoomOffX, _zoomOffY;   // the cursor's place inside its block
        private bool _blockMarkOpen;        // one block marked; the next Shift+Space stretches it

        private bool Zoomed => _zoom > 1;

        // The size is part of the id: a zoom change makes new nodes, never a stale block.
        private static ControlId BlockId(int zoom, int bx, int by) => ControlId.Structural("pipeline.block." + zoom + "." + bx + "." + by);

        /// <summary>The map node holding a cell at the current size.</summary>
        private ControlId MapNodeFor(int x, int y) => Zoomed ? BlockId(_zoom, x / _zoom, y / _zoom) : MapCellId(x, y);

        /// <summary>What the map node holding a cell reads at the current size.</summary>
        private string MapNodeReadout(int x, int y) => Zoomed ? BlockReadout(x / _zoom, y / _zoom) : MapReadout(x, y);

        private void BuildBlocks(GraphBuilder b, SpaceChem.Pipeline.Pipeline p)
        {
            var size = p.method_4();
            int z = _zoom, nx = (size.int_0 + z - 1) / z, ny = (size.int_1 + z - 1) / z;
            b.BeginStop(MapStop);
            for (int by = 0; by < ny; by++)
            {
                b.StartRow("pipeline.map.blockrow");
                for (int bx = 0; bx < nx; bx++)
                {
                    int cx = bx, cy = by;
                    b.AddItem(BlockId(z, bx, by), new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[] { new NodeAnnouncement(() => BlockReadout(cx, cy), kind: AnnouncementKinds.Label) },
                        SpeaksOwnPosition = true,
                        OnSelect = () => LandOnBlock(cx, cy),
                        OnActivate = () => ActivateBlock(cx, cy),
                        OnTooltip = () => Speech.Tts.Speak(BlockDetails(cx, cy), interrupt: true),
                        OnJumpEdge = first => { FocusBlock(first ? 0 : nx - 1, cy); return true; },
                        OnRegionJump = dir => SkipBlocks(cx, cy, 0, dir),
                    });
                }
                b.EndRow();
            }
            b.SetStart(BlockId(z, _cursorX / z, _cursorY / z));
        }

        /// <summary>A block got focus (an arrow, Tab, a jump): the cursor moves with it, keeping
        /// its place inside the block (clamped in the smaller edge blocks).</summary>
        private void LandOnBlock(int bx, int by)
        {
            if (!BlockBounds(bx, by, out int x0, out int y0, out int x1, out int y1)) return;
            _cursorX = Math.Min(x0 + _zoomOffX, x1);
            _cursorY = Math.Min(y0 + _zoomOffY, y1);
        }

        private void FocusBlock(int bx, int by)
        {
            if (!BlockBounds(bx, by, out _, out _, out _, out _)) return;
            LandOnBlock(bx, by);
            Navigation.FocusNode(BlockId(_zoom, bx, by));
        }

        /// <summary>A block's cells (0-based, inclusive), clipped to the map.</summary>
        private bool BlockBounds(int bx, int by, out int x0, out int y0, out int x1, out int y1)
        {
            x0 = y0 = x1 = y1 = 0;
            var p = Model;
            if (p == null) return false;
            var size = p.method_4();
            x0 = bx * _zoom;
            y0 = by * _zoom;
            if (x0 >= size.int_0 || y0 >= size.int_1 || x0 < 0 || y0 < 0) return false;
            x1 = Math.Min(x0 + _zoom, size.int_0) - 1;
            y1 = Math.Min(y0 + _zoom, size.int_1) - 1;
            return true;
        }

        private bool CursorBlockBounds(out int x0, out int y0, out int x1, out int y1)
            => BlockBounds(_cursorX / _zoom, _cursorY / _zoom, out x0, out y0, out x1, out y1);

        // ---- the size keys ----

        private void ZoomStep(int delta)
        {
            int i = Array.IndexOf(ZoomSizes, _zoom) + delta;
            if (i < 0 || i >= ZoomSizes.Length) return; // already at the end: silent, as OniAccess
            SetZoom(ZoomSizes[i]);
        }

        private void SetZoom(int zoom)
        {
            var p = Model;
            if (p == null || !OnMap || zoom == _zoom) return;
            _zoom = zoom;
            _zoomOffX = _cursorX % zoom;
            _zoomOffY = _cursorY % zoom;
            _blockMarkOpen = false;
            Navigation.FocusNode(MapNodeFor(_cursorX, _cursorY), announce: false);
            Speech.Tts.Speak(Loc.T("pipeline.zoom", new { n = zoom }) + ", " + MapNodeReadout(_cursorX, _cursorY), interrupt: true);
        }

        /// <summary>Ctrl+arrows on blocks: past the blocks that hold the same as this one.</summary>
        private bool SkipBlocks(int bx, int by, int dx, int dy)
        {
            var p = Model;
            if (p == null) return false;
            var size = p.method_4();
            int nx = (size.int_0 + _zoom - 1) / _zoom, ny = (size.int_1 + _zoom - 1) / _zoom;
            GridSkip.Target(bx, by, dx, dy, nx, ny, (x, y) => string.Join(", ", BlockContentsAt(x, y).ToArray()), out int tx, out int ty);
            if (tx == bx && ty == by) Speech.Tts.Speak(BlockReadout(bx, by), interrupt: true);
            else FocusBlock(tx, ty);
            return true;
        }

        /// <summary>Enter on a block: end a pipe drawing, else place the armed item with its
        /// top-left on the block's (sticky arms stay armed); nothing else.</summary>
        private void ActivateBlock(int bx, int by)
        {
            if (_drawPipe != null) { EndDraw(); return; }
            if (_armed == null || !BlockBounds(bx, by, out int x0, out int y0, out _, out _)) return;
            if (Place(_armed, new Vector2i(x0, y0)) && !_armedSticky) _armed = null;
        }

        /// <summary>Where a paste or placement lands: the block's top-left when zoomed, else the cursor.</summary>
        private Vector2i PlacementCell()
        {
            if (Zoomed && CursorBlockBounds(out int x0, out int y0, out _, out _)) return new Vector2i(x0, y0);
            return new Vector2i(_cursorX, _cursorY);
        }

        // ---- marking whole blocks: the first Shift+Space marks the block, the next (in any block)
        // stretches the rectangle over both, the one after starts over. ----

        private void MarkBlock()
        {
            if (!CursorBlockBounds(out int x0, out int y0, out int x1, out int y1)) return;
            if (_blockMarkOpen && MarkBounds(out int a0, out int b0, out int a1, out int b1))
            {
                _markFirst = new Vector2i(Math.Min(a0, x0), Math.Min(b0, y0));
                _markSecond = new Vector2i(Math.Max(a1, x1), Math.Max(b1, y1));
                _blockMarkOpen = false;
            }
            else
            {
                _markFirst = new Vector2i(x0, y0);
                _markSecond = new Vector2i(x1, y1);
                _blockMarkOpen = true;
            }
            SpeakRectangle();
        }

        private bool BlockMarked(int x0, int y0, int x1, int y1)
            => MarkBounds(out int a0, out int b0, out int a1, out int b1) && x0 <= a1 && x1 >= a0 && y0 <= b1 && y1 >= b0;

        // ---- readouts ----

        /// <summary>"Marked, 13, 13 to 16, 16, Standard Reactor 1, pipes: …, 9 free"; a block with
        /// nothing in it is its range alone.</summary>
        private string BlockReadout(int bx, int by)
        {
            if (!BlockBounds(bx, by, out int x0, out int y0, out int x1, out int y1)) return null;
            var parts = new List<string>();
            if (BlockMarked(x0, y0, x1, y1)) parts.Add(Loc.T("reactor.marked"));
            parts.Add(BlockRange(x0, y0, x1, y1));
            parts.AddRange(BlockContentsAt(bx, by));
            return string.Join(", ", parts.ToArray());
        }

        private static string BlockRange(int x0, int y0, int x1, int y1)
            => Loc.T("pipeline.block.range", new { from = PipelineText.Cell(new Vector2i(x0, y0)), to = PipelineText.Cell(new Vector2i(x1, y1)) });

        private List<string> BlockContentsAt(int bx, int by)
        {
            var p = Model;
            if (p == null || !BlockBounds(bx, by, out int x0, out int y0, out int x1, out int y1)) return new List<string>();
            return BlockContents(p, x0, y0, x1, y1, details: false);
        }

        /// <summary>Shift+Backspace on a block: the range, then each pipe's run through the block
        /// ("Storage Tank 1 output 13, 11 to 13, 16") and the free cells as runs per row.</summary>
        private string BlockDetails(int bx, int by)
        {
            var p = Model;
            if (p == null || !BlockBounds(bx, by, out int x0, out int y0, out int x1, out int y1)) return null;
            var parts = new List<string> { BlockRange(x0, y0, x1, y1) };
            parts.AddRange(BlockContents(p, x0, y0, x1, y1, details: true));
            return string.Join("; ", parts.ToArray());
        }

        private static bool Inside(Vector2i c, int x0, int y0, int x1, int y1)
            => c.int_0 >= x0 && c.int_0 <= x1 && c.int_1 >= y0 && c.int_1 <= y1;

        private static List<string> BlockContents(SpaceChem.Pipeline.Pipeline p, int x0, int y0, int x1, int y1, bool details)
        {
            var parts = new List<string>();

            // Buildings, reading order; one crossing the block's edge is "part of" it.
            foreach (var kv in PipelineText.Components(p))
            {
                var d = kv.Key;
                var at = kv.Value;
                int ax1 = at.int_0 + Math.Max(1, d.vector2i_0.int_0) - 1, ay1 = at.int_1 + Math.Max(1, d.vector2i_0.int_1) - 1;
                if (ax1 < x0 || at.int_0 > x1 || ay1 < y0 || at.int_1 > y1) continue;
                string name = PipelineText.Name(p, d);
                bool whole = at.int_0 >= x0 && ax1 <= x1 && at.int_1 >= y0 && ay1 <= y1;
                parts.Add(whole ? name : Loc.T("pipeline.block.part", new { name }));
            }

            // Pipes through the block, crossings, open ends, molecules in transit.
            var pipes = new List<string>();
            var ends = new List<string>();
            var crossings = new HashSet<int>();
            int molecules = 0;
            bool running = Running;
            foreach (var kv in PipelineText.Components(p))
                foreach (var o in kv.Key.class485_1)
                {
                    var pipe = o.Value.pipeDraggable_0;
                    if (pipe == null || pipe.linkedList_0.Count == 0) continue;
                    var origin = pipe.method_14();
                    var slots = running ? new List<PipeSlotView>(Slots(pipe)) : null;
                    bool through = false;
                    Vector2i? runStart = null, runEnd = null;
                    int i = 0;
                    foreach (var local in pipe.linkedList_0)
                    {
                        var c = local + origin;
                        if (Inside(c, x0, y0, x1, y1))
                        {
                            through = true;
                            if (pipe.dictionary_4.ContainsKey(local)) crossings.Add(c.int_1 * 1000 + c.int_0);
                            if (slots != null && i < slots.Count && slots[i].HasMolecule) molecules++;
                            if (!runStart.HasValue) runStart = c;
                            runEnd = c;
                        }
                        else if (runStart.HasValue && details)
                        {
                            pipes.Add(Segment(p, pipe, runStart.Value, runEnd.Value));
                            runStart = null;
                        }
                        i++;
                    }
                    if (details && runStart.HasValue) pipes.Add(Segment(p, pipe, runStart.Value, runEnd.Value));
                    else if (!details && through) pipes.Add(DrawName(p, pipe));
                    if (o.Value.vmethod_0() == null)
                    {
                        var end = pipe.linkedList_0.Last.Value + origin;
                        if (Inside(end, x0, y0, x1, y1)) ends.Add(Loc.T("pipeline.block.end", new { cell = PipelineText.Cell(end) }));
                    }
                }
            if (pipes.Count > 0)
                parts.Add(details ? string.Join("; ", pipes.ToArray()) : Loc.T("pipeline.block.pipes", new { pipes = string.Join(", ", pipes.ToArray()) }));
            if (crossings.Count > 0) parts.Add(Loc.T(crossings.Count == 1 ? "pipeline.block.crossing" : "pipeline.block.crossings", new { n = crossings.Count }));
            parts.AddRange(ends);
            if (molecules > 0) parts.Add(MoleculeCount(molecules));

            // A defense level's enemy, when its drawing covers part of the block.
            var level = DefenseText.Level;
            var enemy = DefenseText.Enemy(level);
            if (level != null && enemy != null)
            {
                bool covers = false;
                for (int y = y0; y <= y1 && !covers; y++)
                    for (int x = x0; x <= x1 && !covers; x++)
                        covers = DefenseText.Covers(enemy, new Vector2i(x, y));
                if (covers) parts.Add(DefenseText.EnemyName(level));
            }

            // Free cells: nothing at all in the block says nothing (silence is faster — user rule).
            int free = 0, total = (x1 - x0 + 1) * (y1 - y0 + 1);
            var freeRuns = new List<string>();
            for (int y = y0; y <= y1; y++)
            {
                int start = -1;
                for (int x = x0; x <= x1 + 1; x++)
                {
                    bool isFree = x <= x1 && p.method_7(new Vector2i(x, y)) == null;
                    if (isFree) { free++; if (start < 0) start = x; continue; }
                    if (start < 0) continue;
                    freeRuns.Add(start == x - 1
                        ? Loc.T("pipeline.block.freecell", new { row = y + 1, col = start + 1 })
                        : Loc.T("pipeline.block.freerun", new { row = y + 1, from = start + 1, to = x }));
                    start = -1;
                }
            }
            if (free < total || parts.Count > 0)
            {
                if (details) { if (freeRuns.Count > 0) parts.Add(Loc.T("pipeline.block.freelist", new { runs = string.Join("; ", freeRuns.ToArray()) })); }
                else parts.Add(Loc.T("pipeline.block.free", new { n = free }));
            }
            return parts;
        }

        /// <summary>"Storage Tank 1 output 13, 11 to 13, 16" ("… at 13, 11" for one cell).</summary>
        private static string Segment(SpaceChem.Pipeline.Pipeline p, PipeDraggable pipe, Vector2i from, Vector2i to)
            => from == to
                ? Loc.T("pipeline.block.segment1", new { pipe = DrawName(p, pipe), cell = PipelineText.Cell(from) })
                : Loc.T("pipeline.block.segment", new { pipe = DrawName(p, pipe), from = PipelineText.Cell(from), to = PipelineText.Cell(to) });

        private static string MoleculeCount(int n) => Loc.T(n == 1 ? "pipeline.molecules.one" : "pipeline.molecules", new { n });

        private struct PipeSlotView
        {
            public bool HasMolecule;
        }

        /// <summary>The pipe's molecule slots, one per cell in the cells' order (linkedList_1).</summary>
        private static IEnumerable<PipeSlotView> Slots(PipeDraggable pipe)
        {
            foreach (var slot in pipe.linkedList_1)
                yield return new PipeSlotView { HasMolecule = slot.molecule_0 != null && !slot.molecule_0.method_6() };
        }

        /// <summary>The pipe one of whose cells is <paramref name="cell"/> (its owner's), or null.</summary>
        private static PipeDraggable PipeAt(SpaceChem.Pipeline.Pipeline p, Vector2i cell)
        {
            var d = p.method_7(cell);
            var origin = d == null || d is Class612 ? null : p.method_9(d);
            if (!origin.HasValue) return null;
            var local = cell - origin.Value;
            foreach (var kv in d.class485_1)
                if (kv.Value.pipeDraggable_0 != null && kv.Value.pipeDraggable_0.dictionary_3.ContainsKey(local)) return kv.Value.pipeDraggable_0;
            return null;
        }

        /// <summary>P: while drawing, the drawn pipe; else, on a pipe cell at size 1, that pipe —
        /// "Storage Tank 1 output, connected, Standard Reactor 1 alpha input" — and during a run
        /// the molecules in it. Nothing elsewhere.</summary>
        private void SpeakPipeStatus()
        {
            var p = Model;
            if (p == null) return;
            if (_drawPipe != null)
            {
                Speech.Tts.Speak(Loc.T("pipeline.draw.status", new { pipe = DrawName(p, _drawPipe), state = PipeState(p, _drawPipe) }), interrupt: true);
                return;
            }
            if (Zoomed || !OnMap) return;
            var pipe = PipeAt(p, new Vector2i(_cursorX, _cursorY));
            if (pipe == null) return;
            string text = Loc.T("pipeline.draw.status", new { pipe = DrawName(p, pipe), state = PipeState(p, pipe) });
            if (Running)
            {
                int n = 0;
                foreach (var s in Slots(pipe)) if (s.HasMolecule) n++;
                text += ", " + MoleculeCount(n);
            }
            Speech.Tts.Speak(text, interrupt: true);
        }
    }
}
