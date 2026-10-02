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
        // ---- pipe draw mode (user-approved 2026-09-27: MANUAL drawing only). Every output owns one
        // pipe (PipeDraggable: cells relative to its owner, first cell one column right of the
        // port). The game grows it with the mouse one cell at a time (vmethod_4 steps from the pipe's
        // end toward Pipeline.vector2i_3, vertical first: a step onto the previous cell retracts, a
        // new cell must be free or a straight pipe crossed at a right angle, no turning on a
        // crossing), inside one undo scope per drag (method_16 opens it, vmethod_5 closes it).
        // Draw mode drives exactly that from the map: the cursor sits on the pipe's end, an arrow
        // lands on the neighbour and the landing asks the game for that one step. Each step says
        // the coordinates, then only what changed ("back", "crossing …", "connected, …"); a refused
        // step says why and the cursor returns to the end (FactorioAccess lessons: an explicit mode,
        // terse steps, a reason for every refusal, the connection spoken once when it forms).
        // Enter or Escape ends it; so does leaving the map. ----

        private PipeDraggable _drawPipe;
        private string _drawStep;           // the readout of the cell just stepped onto
        private Vector2i? _drawStepCell;
        private Vector2i? _drawRefocus;     // a refused step: put the cursor back on the end, silently
        private bool _drawFocusPending;     // the focus move onto the map lands a frame after StartDraw

        public override bool ModalCapturesEscape => _drawPipe != null || _armed != null; // Escape ends drawing / unarms

        /// <summary>The pipe's end cell on the map.</summary>
        private static Vector2i EndCell(PipeDraggable pipe) => pipe.linkedList_0.Last.Value + pipe.method_14();

        /// <summary>The output pipe whose END is on this map cell, or null.</summary>
        private static PipeDraggable PipeEndingAt(SpaceChem.Pipeline.Pipeline p, Vector2i cell)
        {
            foreach (var kv in PipelineText.Components(p))
                foreach (var o in kv.Key.class485_1.Values)
                {
                    var pipe = o.pipeDraggable_0;
                    if (pipe != null && pipe.linkedList_0.Count > 0 && EndCell(pipe) == cell) return pipe;
                }
            return null;
        }

        private void StartDraw(PipeDraggable pipe)
        {
            var p = Model;
            if (p == null || pipe == null || !CanEdit()) return;
            if (pipe.bool_0) { Speech.Tts.Speak(Loc.T("pipeline.draw.fixed"), interrupt: true); return; }
            try
            {
                pipe.class381_0 = Locals.smethod_0().smethod_0().method_49(); // one undo step, as a drag
                pipe.enum145_0 = (Enum145)1;
                pipe.vector2i_3 = EndCell(pipe);
            }
            catch (System.Exception ex) { Log.Error("[pipeline] draw start failed", ex); return; }
            _drawPipe = pipe;
            _drawFocusPending = true;
            _drawStep = null;
            _drawStepCell = null;
            var end = EndCell(pipe);
            _cursorX = end.int_0;
            _cursorY = end.int_1;
            Navigation.FocusStop(MapStop);
            Navigation.FocusNode(MapCellId(end.int_0, end.int_1), announce: false);
            Speech.Tts.Speak(Loc.T("pipeline.draw.start", new { pipe = DrawName(p, pipe), cell = PipelineText.Cell(end) }), interrupt: true);
        }

        private void EndDraw(bool quiet = false)
        {
            var pipe = _drawPipe;
            if (pipe == null) return;
            _drawPipe = null;
            _drawStep = null;
            _drawStepCell = null;
            _drawRefocus = null;
            try { pipe.vmethod_5(); } catch (System.Exception ex) { Log.Error("[pipeline] draw end failed", ex); }
            if (quiet) return;
            var p = Model;
            Speech.Tts.Speak(Loc.T("pipeline.draw.done"), interrupt: true); // connections were spoken as they happened (user rule)
        }

        /// <summary>"Storage Tank output", "Assembly Reactor 2 psi output".</summary>
        private static string DrawName(SpaceChem.Pipeline.Pipeline p, PipeDraggable pipe)
            => PipelineText.Name(p, pipe.draggable_0) + " " + OutputName(pipe.draggable_0, OutputIndex(pipe));

        /// <summary>"connected, Recycler input 2" or "open end 12, 4".</summary>
        private static string PipeState(SpaceChem.Pipeline.Pipeline p, PipeDraggable pipe)
        {
            var to = pipe.pipelineOutput_0?.vmethod_0();
            if (to != null) return Loc.T("pipeline.draw.connected", new { to = PipelineText.Name(p, to) + " " + TargetInput(to, pipe) });
            return Loc.T("pipeline.draw.open", new { cell = PipelineText.Cell(EndCell(pipe)) });
        }

        /// <summary>The cursor landed on a map cell while drawing: ask the game for one step there.</summary>
        private void DrawStep(int x, int y)
        {
            var p = Model;
            var pipe = _drawPipe;
            if (p == null || pipe == null) return;
            var target = new Vector2i(x, y);
            var end = EndCell(pipe);
            if (target == end) return;
            int before = pipe.linkedList_0.Count;
            bool wasConnected = pipe.pipelineOutput_0?.vmethod_0() != null;
            string refusal = null;
            if (System.Math.Abs(target.int_0 - end.int_0) + System.Math.Abs(target.int_1 - end.int_1) != 1)
                refusal = Loc.T("pipeline.draw.notnext");
            else
            {
                refusal = WhyRefused(p, pipe, target);
                try
                {
                    p.vector2i_3 = target;
                    pipe.vmethod_4();
                }
                catch (System.Exception ex) { Log.Error("[pipeline] draw step failed", ex); }
            }
            int after = pipe.linkedList_0.Count;
            var parts = new List<string> { PipelineText.Cell(target) };
            if (after == before || EndCell(pipe) != target)
            {
                // Refused: say why, and put the cursor back on the end.
                parts.Add(refusal ?? Loc.T("pipeline.draw.refused"));
                _drawRefocus = EndCell(pipe);
            }
            else
            {
                // A retraction is not spoken: the game plays its own sound for it (user rule).
                var local = target - pipe.method_14();
                if (pipe.dictionary_4.TryGetValue(local, out var other) && other?.draggable_0 != null)
                    parts.Add(Loc.T("pipeline.pipe.crossing", new { pipe = DrawName(p, other) }));
                bool connected = pipe.pipelineOutput_0?.vmethod_0() != null;
                if (connected && !wasConnected) parts.Add(PipeState(p, pipe));
                else if (!connected && wasConnected) parts.Add(Loc.T("pipeline.draw.disconnected"));
            }
            _drawStep = string.Join(", ", parts.ToArray());
            _drawStepCell = target;
        }

        /// <summary>Why the game would refuse a step onto <paramref name="target"/> (PipeDraggable's
        /// Class642 rules), or null when it looks allowed.</summary>
        private static string WhyRefused(SpaceChem.Pipeline.Pipeline p, PipeDraggable pipe, Vector2i target)
        {
            var size = p.method_4();
            if (target.int_0 < 0 || target.int_1 < 0 || target.int_0 >= size.int_0 || target.int_1 >= size.int_1)
                return Loc.T("pipeline.edit.offmap");
            var origin = pipe.method_14();
            var local = target - origin;
            var list = pipe.linkedList_0;
            if (list.Count > 1 && list.Last.Previous.Value == local) return null; // stepping back
            if (pipe.dictionary_3.ContainsKey(local)) return Loc.T("pipeline.draw.own");
            var endLocal = list.Last.Value;
            if (pipe.dictionary_4.ContainsKey(endLocal) && list.Count > 1)
            {
                var along = endLocal - list.Last.Previous.Value;
                var step = local - endLocal;
                if (along != step) return Loc.T("pipeline.draw.noturn");
            }
            var d = p.method_7(target);
            if (d == null) return null;
            if (d is Class612) return Loc.T("pipeline.edit.blockedby", new { what = Loc.T("pipeline.terrain") });
            var dOrigin = p.method_9(d);
            if (dOrigin.HasValue)
                foreach (var o in d.class485_1.Values)
                    if (o.pipeDraggable_0 != null && o.pipeDraggable_0.dictionary_3.ContainsKey(target - dOrigin.Value))
                        return Loc.T("pipeline.draw.rightangle");
            string name = string.IsNullOrEmpty(d.string_1?.Trim()) && !(d is ReactorDraggable) ? Loc.T("pipeline.terrain") : PipelineText.Name(p, d);
            return Loc.T("pipeline.edit.blockedby", new { what = name });
        }

        /// <summary>Per frame: leaving the map ends drawing; a refused step's cursor goes back to the end.</summary>
        private void UpdateDraw()
        {
            if (_drawPipe == null) return;
            if (_drawFocusPending)
            {
                if (!MapStop.Equals(Navigation.FocusedStopKey)) return;
                _drawFocusPending = false;
            }
            if (Running || !MapStop.Equals(Navigation.FocusedStopKey) || ActiveChild != null) { EndDraw(quiet: !MapStop.Equals(Navigation.FocusedStopKey)); return; }
            if (_drawRefocus.HasValue)
            {
                var c = _drawRefocus.Value;
                _drawRefocus = null;
                _cursorX = c.int_0;
                _cursorY = c.int_1;
                Navigation.FocusNode(MapCellId(c.int_0, c.int_1), announce: false);
            }
        }

        /// <summary>The draw-mode status (P): which pipe, its length, and where it ends.</summary>
        private void SpeakDrawStatus()
        {
            var p = Model;
            if (p == null) return;
            if (_drawPipe == null) { Speech.Tts.Speak(Common.ProgressSection.Summary(), interrupt: true); return; }
            Speech.Tts.Speak(Loc.T("pipeline.draw.status", new { pipe = DrawName(p, _drawPipe), state = PipeState(p, _drawPipe) }), interrupt: true);
        }
    }
}
