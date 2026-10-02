using SpaceChem.Pipeline;
using SpeechChem.Screens.Reactor;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens.Pipeline
{
    public sealed partial class PipelineEditorScreen
    {
        // ---- molecule panels: the game draws each port's annotation beside its building — an
        // input's molecules with their percentages (the InputAnnotation on a tank's output port),
        // an output's molecule with produced / required (the counter annotation on a freighter's
        // input port), a reactor output's note when it has one (ReactorAnnotation). So each is a
        // cell of the building's row right after its port (user layout 2026-10-01; there is no
        // separate Molecules stop), worded like the reactor's Molecules stop
        // (ReactorEditorScreen.AnnotationText); Enter opens the molecule viewer. ----

        private void AddPanel(GraphBuilder b, Draggable d, bool output, int index, Annotation a)
        {
            if (a is ReactorAnnotation note && note.method_4()) return; // no note on this output
            if (!HasMolecules(a)) return;                              // e.g. the recycler's inputs: an empty panel
            string key = "pipeline.mol." + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(d)
                + (output ? ".out." : ".in.") + index;
            var vt = Cell(() => ReactorEditorScreen.AnnotationText(a));
            vt.OnActivate = () => ReactorEditorScreen.OpenMolecules(this, a, key);
            b.AddItem(ControlId.Structural(key), vt);
        }

        private static bool HasMolecules(Annotation a)
        {
            if (a == null) return false;
            foreach (var m in a.vmethod_6())
                if (m != null && !m.method_6()) return true;
            return false;
        }
    }
}
