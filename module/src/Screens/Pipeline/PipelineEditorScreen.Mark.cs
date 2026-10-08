using System;
using System.Collections.Generic;
using Impeller;
using SpaceChem;
using SpaceChem.Pipeline;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;

namespace SpeechChem.Screens.Pipeline
{
    public sealed partial class PipelineEditorScreen
    {
        // ---- the marked rectangle on the map (the reactor's, user request 2026-10-07): Shift+Space
        // marks one corner, the next the opposite one, a third starts over; Ctrl+A marks the whole
        // map; Ctrl+Space unmarks ("Cleared", silent when nothing is marked). Cells inside read
        // "Marked" before the coordinates. While the map cursor is inside it, Delete / Ctrl+X /
        // Ctrl+C act on every movable building whose BODY lies wholly inside (the game's own
        // drag-select, Pipeline.method_30, also wants the pipes inside — a rectangle around two
        // reactors would then miss any whose pipe leaves it; pipes move with their owner anyway),
        // and the paste moves or copies them as one drop (Pipeline.method_13 over the whole
        // selection: all or nothing, one undo step), the rectangle's top-left corner landing on
        // the cursor. A cut / copy / delete that took it, or a paste that lands, clears it. ----

        private Vector2i? _markFirst, _markSecond;

        private bool OnMap => MapStop.Equals(Navigation.FocusedStopKey);

        private void MarkCorner()
        {
            if (!OnMap) return;
            if (Zoomed) { MarkBlock(); return; } // whole blocks (Zoom.cs)
            _blockMarkOpen = false;
            var here = new Vector2i(_cursorX, _cursorY);
            if (!_markFirst.HasValue || _markSecond.HasValue)
            {
                _markFirst = here;
                _markSecond = null;
                Speech.Tts.Speak(Loc.T("reactor.mark.first", new { cell = PipelineText.Cell(here) }), interrupt: true);
                return;
            }
            _markSecond = here;
            SpeakRectangle();
        }

        /// <summary>Ctrl+A: the rectangle becomes the whole map.</summary>
        private void MarkAll()
        {
            var p = Model;
            if (p == null || !OnMap) return;
            var size = p.method_4();
            _blockMarkOpen = false;
            _markFirst = new Vector2i(0, 0);
            _markSecond = new Vector2i(size.int_0 - 1, size.int_1 - 1);
            SpeakRectangle();
        }

        private void SpeakRectangle()
        {
            MarkBounds(out int x0, out int y0, out int x1, out int y1);
            Speech.Tts.Speak(Loc.T("reactor.mark.rect", new
            {
                w = x1 - x0 + 1,
                h = y1 - y0 + 1,
                from = PipelineText.Cell(new Vector2i(x0, y0)),
                to = PipelineText.Cell(new Vector2i(x1, y1)),
            }), interrupt: true);
        }

        private void ClearMark()
        {
            if (!OnMap || !_markFirst.HasValue) return;
            DropMark();
            Speech.Tts.Speak(Loc.T("reactor.mark.cleared"), interrupt: true);
        }

        private void DropMark()
        {
            _markFirst = _markSecond = null;
            _blockMarkOpen = false;
        }

        private bool CursorInRectangle => _markSecond.HasValue && OnMap && IsMarked(_cursorX, _cursorY);

        /// <summary>The marked cells' bounds: the rectangle, or the first corner alone while the
        /// second is still to come. False when nothing is marked.</summary>
        private bool MarkBounds(out int x0, out int y0, out int x1, out int y1)
        {
            x0 = y0 = x1 = y1 = 0;
            if (!_markFirst.HasValue) return false;
            var a = _markFirst.Value;
            var b = _markSecond ?? a;
            x0 = Math.Min(a.int_0, b.int_0); x1 = Math.Max(a.int_0, b.int_0);
            y0 = Math.Min(a.int_1, b.int_1); y1 = Math.Max(a.int_1, b.int_1);
            return true;
        }

        private bool IsMarked(int x, int y)
            => MarkBounds(out int x0, out int y0, out int x1, out int y1) && x >= x0 && x <= x1 && y >= y0 && y <= y1;

        /// <summary>The movable buildings (not fixed, Draggable.bool_0) whose body (origin +
        /// vector2i_0) lies wholly inside the rectangle, in the Components list's order.</summary>
        private List<Draggable> MarkedBuildings(SpaceChem.Pipeline.Pipeline p)
            => MarkBounds(out int x0, out int y0, out int x1, out int y1) ? BuildingsIn(p, x0, y0, x1, y1) : new List<Draggable>();

        /// <summary>The movable buildings whose body lies wholly inside the cells (inclusive).</summary>
        private static List<Draggable> BuildingsIn(SpaceChem.Pipeline.Pipeline p, int x0, int y0, int x1, int y1)
        {
            var list = new List<Draggable>();
            foreach (var kv in PipelineText.Components(p))
            {
                var d = kv.Key;
                if (d.bool_0) continue;
                var at = kv.Value;
                var size = d.vector2i_0;
                int w = Math.Max(1, size.int_0), h = Math.Max(1, size.int_1);
                if (at.int_0 >= x0 && at.int_1 >= y0 && at.int_0 + w - 1 <= x1 && at.int_1 + h - 1 <= y1) list.Add(d);
            }
            return list;
        }

        private const int MaxNamed = 3;

        /// <summary>Up to three phrases named, joined; more = "5 items".</summary>
        private static string Summary(List<string> phrases, string separator = ", ")
            => phrases.Count <= MaxNamed ? string.Join(separator, phrases.ToArray()) : Loc.T("reactor.edit.items", new { n = phrases.Count });

        /// <summary>Delete with the cursor in the rectangle: every marked building, as one undo
        /// step (the game's own multi-delete: method_10 each, method_15, method_66 of the set).
        /// Nothing movable inside: a silent no-op (user rule 2026-10-07).</summary>
        private void DeleteMarked()
        {
            var p = Model;
            if (p == null) return;
            DeleteSet(p, MarkedBuildings(p));
        }

        /// <summary>Delete zoomed with no rectangle: the movable buildings wholly inside the block.</summary>
        private void DeleteBlock()
        {
            var p = Model;
            if (p == null || !CursorBlockBounds(out int x0, out int y0, out int x1, out int y1)) return;
            DeleteSet(p, BuildingsIn(p, x0, y0, x1, y1));
        }

        private void DeleteSet(SpaceChem.Pipeline.Pipeline p, List<Draggable> victims)
        {
            if (victims.Count == 0) return;
            if (!CanEdit()) return;
            CloseDrag();
            var names = new List<string>();
            foreach (var d in victims) names.Add(PipelineText.Name(p, d));
            try
            {
                foreach (var d in victims) p.method_10(d, null);
                p.method_15();
                Locals.smethod_0().smethod_0().method_66(victims);
                _clip.RemoveAll(victims.Contains);
                DropMark();
                Speech.Tts.Speak(Loc.T("reactor.edit.deleted", new { what = Summary(names) }), interrupt: true);
            }
            catch (Exception ex) { Log.Error("[pipeline] delete marked failed", ex); }
        }
    }
}
