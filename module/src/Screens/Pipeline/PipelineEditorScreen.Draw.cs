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
        // Enter on the map or Escape ends it; so do a run, or the pipe's owner going away (a delete,
        // an undo's reload). DRAWING IS ONLY WHAT ARROWS ON THE MAP DO (user rule 2026-10-07):
        // Tab out and back, M, the menu, a reactor opened from here - the mode survives them all,
        // and every other map key (jumps, categories, marking) works as without it. Only an arrow
        // pressed with the cursor ON the pipe's end draws; elsewhere arrows just move.
        // The game's drag (its undo scope, which also blocks undo) is open only while arrows are
        // drawing: the first step opens it, focus leaving the map or a mod edit closes it, so a
        // session away from the map splits the drawing into separate undo steps. ----

        private PipeDraggable _drawPipe;
        private bool _dragOpen;             // the game's drag state + undo scope are open
        private bool _drawOnMap;            // focus was on the map last frame: a landing now is an arrow
        private string _drawStep;           // the readout of the cell just stepped onto
        private Vector2i? _drawStepCell;
        private Vector2i? _drawRefocus;     // a refused step: put the cursor back on the end, silently

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
            EndDraw(quiet: true);
            _drawPipe = pipe;
            _drawOnMap = false;
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
            if (_drawPipe == null) return;
            CloseDrag();
            _drawPipe = null;
            if (quiet) return;
            Speech.Tts.Speak(Loc.T("pipeline.draw.done"), interrupt: true); // connections were spoken as they happened (user rule)
        }

        /// <summary>Open the game's drag on the drawn pipe, as a mouse press on its end does
        /// (PipeDraggable.method_16): one undo scope until <see cref="CloseDrag"/>.</summary>
        private bool OpenDrag()
        {
            var pipe = _drawPipe;
            if (pipe == null) return false;
            if (_dragOpen) return true;
            try
            {
                pipe.class381_0 = Locals.smethod_0().smethod_0().method_49();
                pipe.enum145_0 = (Enum145)1;
                pipe.vector2i_3 = EndCell(pipe);
                _dragOpen = true;
            }
            catch (System.Exception ex) { Log.Error("[pipeline] draw start failed", ex); }
            return _dragOpen;
        }

        /// <summary>Close the game's drag (the release, vmethod_5: commits the undo step); draw mode
        /// stays. The last step's readout goes too, so the end reads as a plain cell again.</summary>
        private void CloseDrag()
        {
            _drawStep = null;
            _drawStepCell = null;
            _drawRefocus = null;
            if (!_dragOpen) return;
            _dragOpen = false;
            try { _drawPipe?.vmethod_5(); } catch (System.Exception ex) { Log.Error("[pipeline] draw end failed", ex); }
        }

        /// <summary>The drawn pipe is still one of its owner's, and the owner still on the map.</summary>
        private static bool PipeAlive(SpaceChem.Pipeline.Pipeline p, PipeDraggable pipe)
        {
            var owner = pipe.draggable_0;
            if (owner == null || pipe.linkedList_0.Count == 0 || !p.method_9(owner).HasValue) return false;
            foreach (var o in owner.class485_1.Values)
                if (ReferenceEquals(o.pipeDraggable_0, pipe)) return true;
            return false;
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
            // Only an arrow FROM the end draws: a landing from another stop, a jump, or an arrow
            // anywhere else on the map is a plain cursor move.
            if (!_drawOnMap || _cursorX != end.int_0 || _cursorY != end.int_1) return;
            if (System.Math.Abs(target.int_0 - end.int_0) + System.Math.Abs(target.int_1 - end.int_1) != 1) return;
            if (!OpenDrag()) return;
            int before = pipe.linkedList_0.Count;
            bool wasConnected = pipe.pipelineOutput_0?.vmethod_0() != null;
            string refusal = WhyRefused(p, pipe, target);
            try
            {
                p.vector2i_3 = target;
                pipe.vmethod_4();
            }
            catch (System.Exception ex) { Log.Error("[pipeline] draw step failed", ex); }
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

        /// <summary>Per frame: a run or a vanished pipe ends drawing; leaving the map closes the
        /// game's drag (the mode stays); a refused step's cursor goes back to the end.</summary>
        private void UpdateDraw()
        {
            if (_drawPipe == null) return;
            var p = Model;
            bool onMap = OnMap && ActiveChild == null;
            if (p == null || !PipeAlive(p, _drawPipe)) { EndDraw(quiet: true); return; }
            if (Running) { EndDraw(quiet: !onMap); return; }
            _drawOnMap = onMap;
            if (!onMap) { CloseDrag(); return; }
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
