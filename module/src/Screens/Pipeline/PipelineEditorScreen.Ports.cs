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
        // (class485_1, PipelineOutput — each owns its pipe). A reactor's ports take the reactor's
        // own zone names (inputs α β top to bottom, outputs ψ ω); others count them. An input says
        // where it is fed from; an output where its pipe leads, or where the pipe's open end is.
        // Each port is followed by its molecule panel, when it has one (Molecules.cs). ----

        private void BuildPorts(GraphBuilder b, Draggable d)
        {
            int i = 0;
            foreach (var kv in d.class485_0)
            {
                int index = i++;
                var input = kv.Value;
                var cell = Cell(() => InputLine(Model, d, input, index));
                cell.OnActivate = () => JumpToInput(d, input); // Enter: the input's cell on the map
                b.AddItem(PortId(d, false, index), cell);
                AddPanel(b, d, false, index, input.method_0());
            }
            i = 0;
            foreach (var kv in d.class485_1)
            {
                int index = i++;
                var output = kv.Value;
                var cell = Cell(() => OutputLine(Model, d, output, index));
                cell.OnActivate = () => JumpToOutput(d, output); // Enter: the pipe's end on the map (Enter there draws)
                b.AddItem(PortId(d, true, index), cell);
                AddPanel(b, d, true, index, output.method_0());
            }
        }

        private void JumpToInput(Draggable d, PipelineInput input)
        {
            var origin = Model?.method_9(d);
            if (!origin.HasValue) return;
            var at = origin.Value + input.vector2i_0;
            FocusMapCell(at.int_0, at.int_1);
        }

        private void JumpToOutput(Draggable d, PipelineOutput output)
        {
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
            vt.SpeaksOwnPosition = true; // a cell of the component's row, not a list item
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

        /// <summary>"alpha input, from Storage Tank" / "input, 24, 8" — an unfed input names its
        /// cell, the way an open output names its end (a pipe connects from the cell to its left,
        /// PipeDraggable.method_24).</summary>
        private static string InputLine(SpaceChem.Pipeline.Pipeline p, Draggable d, PipelineInput input, int index)
        {
            string name = InputName(d, index);
            var from = input.vmethod_0();
            if (from != null) return Loc.T("pipeline.port.from", new { port = name, from = PipelineText.Name(p, from) });
            var origin = p?.method_9(d);
            if (!origin.HasValue) return name;
            return Loc.T("pipeline.port.at", new { port = name, cell = PipelineText.Cell(origin.Value + input.vector2i_0) });
        }

        /// <summary>"psi output, to Recycler input 2" / "psi output, open end 18, 4".</summary>
        private static string OutputLine(SpaceChem.Pipeline.Pipeline p, Draggable d, PipelineOutput output, int index)
        {
            string name = OutputName(d, index);
            var to = output.vmethod_0();
            if (to != null)
                return Loc.T("pipeline.port.to", new { port = name, to = PipelineText.Name(p, to) + " " + TargetInput(to, output.pipeDraggable_0) });
            var pipe = output.pipeDraggable_0;
            if (pipe == null || pipe.linkedList_0.Count == 0) return name;
            var end = pipe.linkedList_0.Last.Value + pipe.method_14();
            return Loc.T("pipeline.port.open", new { port = name, cell = PipelineText.Cell(end) });
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
