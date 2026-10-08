using SpaceChem.Pipeline;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens.Pipeline
{
    public sealed partial class PipelineEditorScreen
    {
        // ---- ports: a component's inputs (Draggable.class485_0, PipelineInput) and outputs
        // (class485_1, PipelineOutput — each owns its pipe), the cells of the Components table. The
        // column header names the port, so a cell says only where an input is fed from / where an
        // output's pipe leads or ends; a reactor's cell adds its zone name (alpha / beta inputs,
        // psi / omega outputs). The port's molecule panel follows (Molecules.cs). ----

        private NodeVtable InputCell(Draggable d, PipelineInput input, int index)
        {
            var panel = Panel(d, false, index, input.method_0());
            var vt = Cell(() => WithPanel(InputLine(Model, d, input, index, named: d is ReactorDraggable), panel));
            vt.OnActivate = () => JumpToInput(d, input); // Enter: the input's cell on the map
            vt.OnTooltip = () => SpeakTooltip(d); // the component's, as on its component cell
            return vt;
        }

        private NodeVtable OutputCell(Draggable d, PipelineOutput output, int index)
        {
            var panel = Panel(d, true, index, output.method_0());
            var vt = Cell(() => WithPanel(OutputLine(Model, d, output, index, named: d is ReactorDraggable), panel));
            vt.OnActivate = () => JumpToOutput(d, output); // Enter: the pipe's end on the map (Enter there draws)
            vt.OnTooltip = () => SpeakTooltip(d);
            return vt;
        }

        private void JumpToInput(Draggable d, PipelineInput input)
        {
            var origin = Model?.method_9(d);
            if (!origin.HasValue) return;
            var at = origin.Value + input.vector2i_0;
            _jumps.Remember(Here());
            FocusMapCell(at.int_0, at.int_1);
        }

        private void JumpToOutput(Draggable d, PipelineOutput output)
        {
            _jumps.Remember(Here());
            var pipe = output.pipeDraggable_0;
            if (pipe != null && pipe.linkedList_0.Count > 0)
            {
                var end = EndCell(pipe);
                FocusMapCell(end.int_0, end.int_1);
                return;
            }
            var origin = Model?.method_9(d);
            if (!origin.HasValue) return;
            var at = origin.Value + output.vector2i_0;
            FocusMapCell(at.int_0, at.int_1);
        }

        private NodeVtable Cell(System.Func<string> text)
        {
            var vt = ProfileUi.Text(text);
            vt.OnSecondary = OpenMenu;
            vt.SpeaksOwnPosition = true; // a table cell, never counted
            return vt;
        }

        private static ControlId PortId(Draggable d, bool output, int index)
            => ControlId.Structural("pipeline.port." + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(d)
                + (output ? ".out." : ".in.") + index);

        /// <summary>"alpha input", "input 2", "input".</summary>
        internal static string InputName(Draggable d, int index)
        {
            if (d is ReactorDraggable) return Loc.T(index == 0 ? "zone.alpha" : "zone.beta");
            return d.class485_0.Count > 1 ? Loc.T("pipeline.input.n", new { n = index + 1 }) : Loc.T("pipeline.input");
        }

        /// <summary>"psi output", "output 2", "output".</summary>
        internal static string OutputName(Draggable d, int index)
        {
            if (d is ReactorDraggable) return Loc.T(index == 0 ? "zone.psi" : "zone.omega");
            return d.class485_1.Count > 1 ? Loc.T("pipeline.output.n", new { n = index + 1 }) : Loc.T("pipeline.output");
        }

        /// <summary>"from Storage Tank" / "24, 8" — an unfed input names its cell, the way an open
        /// output names its end (a pipe connects from the cell to its left, PipeDraggable.method_24).
        /// <paramref name="named"/> puts the port's name first ("alpha input, from Storage Tank").</summary>
        private static string InputLine(SpaceChem.Pipeline.Pipeline p, Draggable d, PipelineInput input, int index, bool named)
        {
            string body = null;
            var from = input.vmethod_0();
            if (from != null) body = Loc.T("pipeline.from", new { from = PipelineText.Name(p, from) });
            else
            {
                var origin = p?.method_9(d);
                if (origin.HasValue) body = PipelineText.Cell(origin.Value + input.vector2i_0);
            }
            return Named(named ? InputName(d, index) : null, body);
        }

        /// <summary>"to Recycler input 2" / "open 18, 4"; <paramref name="named"/> as above.</summary>
        private static string OutputLine(SpaceChem.Pipeline.Pipeline p, Draggable d, PipelineOutput output, int index, bool named)
        {
            string body = null;
            var to = output.vmethod_0();
            var pipe = output.pipeDraggable_0;
            if (to != null)
                body = Loc.T("pipeline.to", new { to = PipelineText.Name(p, to) + " " + TargetInput(to, pipe) });
            else if (pipe != null && pipe.linkedList_0.Count > 0)
                body = Loc.T("pipeline.open", new { cell = PipelineText.Cell(pipe.linkedList_0.Last.Value + pipe.method_14()) });
            return Named(named ? OutputName(d, index) : null, body);
        }

        private static string Named(string name, string body)
        {
            if (name == null) return body ?? Loc.T("reactor.mol.none");
            return body == null ? name : name + ", " + body;
        }

        /// <summary>The input of <paramref name="target"/> a pipe feeds, by name.</summary>
        internal static string TargetInput(Draggable target, PipeDraggable pipe)
        {
            int i = 0;
            foreach (var kv in target.class485_0)
            {
                if (kv.Value.pipeDraggable_0 == pipe) return InputName(target, i);
                i++;
            }
            return Loc.T("pipeline.input");
        }
    }
}
