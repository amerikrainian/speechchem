using System.Collections.Generic;
using Impeller;
using SpaceChem;
using SpaceChem.Reactor;
using SpeechChem.Game;
using SpeechChem.Localization;

namespace SpeechChem.Screens.Reactor
{
    public sealed partial class ReactorEditorScreen
    {
        // ---- waldo path trace (Ctrl+N red, Ctrl+M blue): the path lines the game draws, walked by
        // Game/PathTrace and shown as a list, one line per event (start, instruction, turn, branch,
        // wall, loop); Enter moves the grid cursor to that line's cell, Escape closes. ----

        private void TraceWaldo(bool red)
        {
            var r = Model;
            if (r == null) return;
            string title = Loc.T(red ? "reactor.trace.red" : "reactor.trace.blue");
            StartInstruction start = FindStart(r, red);
            var bin = start != null ? r.method_19(start) : null;
            if (!bin.HasValue)
            {
                Speech.Tts.Speak(Loc.T("reactor.trace.nostart", new { waldo = title }), interrupt: true);
                return;
            }

            int arrowLayer = red ? ReactorText.RedArrow : ReactorText.BlueArrow;
            int instrLayer = red ? ReactorText.Red : ReactorText.Blue;
            Vector2i size = r.method_1();
            var lines = PathTrace.Walk(size.int_0, size.int_1, bin.Value.vector2i_0.int_0, bin.Value.vector2i_0.int_1,
                (x, y) => TraceCell(r, new Vector2i(x, y), arrowLayer, instrLayer));

            var items = new List<ActionListScreen.Item>();
            foreach (var line in lines)
            {
                var l = line;
                string text = TraceLine(l);
                items.Add(new ActionListScreen.Item { Label = () => text, Run = () => _pendingJump = new Vector2i(l.X, l.Y) });
            }
            PushChild(new ActionListScreen("reactor.trace", title, items));
        }

        // Closing the list restores the grid's saved focus after the item runs, so the jump waits
        // for the next frame with the list gone (applied from OnUpdate).
        private Vector2i? _pendingJump;

        private void ApplyPendingJump()
        {
            if (_pendingJump == null || ActiveChild != null) return;
            var cell = _pendingJump.Value;
            _pendingJump = null;
            FocusCell(cell.int_0, cell.int_1);
        }

        private static PathTrace.Cell TraceCell(SpaceChem.Reactor.Reactor r, Vector2i cell, int arrowLayer, int instrLayer)
        {
            var c = new PathTrace.Cell { ArrowDir = PathTrace.None, InstrDir = PathTrace.None };
            var arrow = r.method_17(new ReactorBin(cell, (Enum114)arrowLayer)) as Instruction;
            if (arrow != null)
            {
                c.ArrowLabel = ReactorText.Label(arrow);
                c.ArrowDir = (int)arrow.vmethod_5();
            }
            var instr = r.method_17(new ReactorBin(cell, (Enum114)instrLayer)) as Instruction;
            if (instr != null)
            {
                c.InstrLabel = ReactorText.Label(instr);
                c.InstrDir = (int)instr.vmethod_5();
                c.IsStart = instr is StartInstruction;
            }
            return c;
        }

        private static string TraceLine(PathTrace.Line l)
        {
            string cell = Loc.T("reactor.cell", new { x = l.X + 1, y = l.Y + 1 });
            string dir = ReactorText.Direction((Enum153)l.Dir);
            string labels = string.Join(", ", l.Labels.ToArray());
            switch (l.Kind)
            {
                case PathTrace.Kind.Start:
                    return Loc.T("reactor.trace.start", new { cell, dir, labels = l.Labels.Count > 0 ? ", " + labels : "" });
                case PathTrace.Kind.Branch:
                    return Loc.T("reactor.trace.branch", new { cell, dir, labels });
                case PathTrace.Kind.Wall:
                    return Loc.T("reactor.trace.wall", new { cell });
                case PathTrace.Kind.Loop:
                    return Loc.T("reactor.trace.loop", new { cell });
                default:
                    return cell + ", " + labels;
            }
        }
    }
}
