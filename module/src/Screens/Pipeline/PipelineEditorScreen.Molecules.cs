using System.Collections.Generic;
using SpaceChem.Pipeline;
using SpeechChem.Localization;
using SpeechChem.Screens.Reactor;
using SpeechChem.UI;

namespace SpeechChem.Screens.Pipeline
{
    public sealed partial class PipelineEditorScreen
    {
        // ---- molecule panels: the game draws each port's annotation beside its building — an
        // input's molecules with their percentages (the InputAnnotation on a tank's output port),
        // an output's molecule with produced / required (the counter annotation on a freighter's
        // input port), a reactor output's note when it has one (ReactorAnnotation). Each is read
        // in its port's cell of the Components table, after the connection, worded like the
        // reactor's Molecules stop (ReactorEditorScreen.AnnotationText); M on the cell opens the
        // molecule viewer, as M on a reactor zone does. ----

        // Port cell key -> its panel, rebuilt every render with the table.
        private readonly Dictionary<string, Annotation> _panels = new Dictionary<string, Annotation>();

        /// <summary>The port's panel when it shows molecules (recorded for M), else null.</summary>
        private Annotation Panel(Draggable d, bool output, int index, Annotation a)
        {
            if (a is ReactorAnnotation note && note.method_4()) return null; // no note on this output
            if (!HasMolecules(a)) return null;                              // e.g. the recycler's inputs: an empty panel
            _panels[(string)PortId(d, output, index).StructuralKey] = a;
            return a;
        }

        private static string WithPanel(string line, Annotation panel)
            => panel == null ? line : line + ", " + ReactorEditorScreen.AnnotationText(panel);

        /// <summary>M on a port cell: its panel's molecules in the viewer; "none" on a port
        /// without one (silence would read as broken); nothing elsewhere.</summary>
        private void OpenFocusedMolecules()
        {
            string key = Navigation.FocusedNodeId?.StructuralKey as string;
            if (key == null || !key.StartsWith("pipeline.port.", System.StringComparison.Ordinal)) return;
            Annotation a;
            if (_panels.TryGetValue(key, out a)) ReactorEditorScreen.OpenMolecules(this, a, "pipeline.mol." + key);
            else Speech.Tts.Speak(Loc.T("reactor.mol.none"), interrupt: true);
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
