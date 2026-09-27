using SpaceChem.Pipeline;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.Screens.Reactor;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens.Pipeline
{
    public sealed partial class PipelineEditorScreen
    {
        // ---- molecules: the panels the pipeline draws, component by component in reading order —
        // an input's molecules with their percentages (the InputAnnotation on its output port), an
        // output's molecule with produced / required (the counter annotation on its input port), a
        // reactor output's note when it has one (ReactorAnnotation). Worded like the reactor's
        // Molecules stop (ReactorEditorScreen.AnnotationText); Enter opens the molecule viewer. ----

        private const string MoleculesStop = "pipeline.molecules";

        private void BuildMolecules(GraphBuilder b, SpaceChem.Pipeline.Pipeline p)
        {
            bool begun = false;
            int row = 0;
            foreach (var kv in PipelineText.Components(p))
            {
                var d = kv.Key;
                int i = 0;
                foreach (var port in d.class485_1)
                {
                    int index = i++;
                    var a = port.Value.method_0();
                    if (a == null || (a is ReactorAnnotation note && note.method_4())) continue; // no note on this output
                    string label = d is ReactorDraggable ? PipelineText.Name(p, d) + " " + OutputName(d, index) : PipelineText.Name(p, d);
                    AddMolecule(b, ref begun, row++, label, a);
                }
                i = 0;
                foreach (var port in d.class485_0)
                {
                    int index = i++;
                    var a = port.Value.method_0();
                    if (a == null) continue;
                    string label = d.class485_0.Count > 1 ? PipelineText.Name(p, d) + " " + InputName(d, index) : PipelineText.Name(p, d);
                    AddMolecule(b, ref begun, row++, label, a);
                }
            }
        }

        private void AddMolecule(GraphBuilder b, ref bool begun, int row, string label, Annotation a)
        {
            if (!HasMolecules(a)) return; // e.g. the recycler's inputs: an empty panel
            if (!begun) { b.BeginStop(MoleculesStop); begun = true; }
            var vt = ProfileUi.Text(true, () => label + ": " + ReactorEditorScreen.AnnotationText(a));
            int n = row;
            vt.OnActivate = () => ReactorEditorScreen.OpenMolecules(this, a, "pipeline.mol." + n);
            b.AddItem(ControlId.Structural("pipeline.mol." + row), vt);
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
