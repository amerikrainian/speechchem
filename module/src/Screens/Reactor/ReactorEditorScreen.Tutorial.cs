using System.Collections.Generic;
using SpaceChem.Reactor;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens.Reactor
{
    public sealed partial class ReactorEditorScreen
    {
        // ---- tutorial: the steps themselves live in Tutorial/ (a FrameLoop step, which also speaks
        // them); the reactor shows the active one as its Tutorial stop, marks its target cell
        // "highlighted", and tells the tutorial where the cursor is. ----

        internal int CursorX => _cursorX;
        internal int CursorY => _cursorY;
        internal static bool CursorOnGrid => OnGrid;
        internal static bool BlueActive => Layers != null && ((int)Layers.method_0() & ReactorText.Blue) != 0;

        /// <summary>Whether the level-start lines are out (research levels speak them a few frames
        /// after arrival), so a tutorial step queues after them. Production reactors have none.</summary>
        internal bool LevelStartSpoken
        {
            get
            {
                if (Class53.smethod_5<Class84>() == null) return true;
                var level = Class53.smethod_5<Class83>();
                return level != null && ReferenceEquals(level, _startSpokenFor) && _startFrames == 0;
            }
        }

        /// <summary>A game tutorial step as the box shows it: the text, then the pictured piece and
        /// the highlighted cell (levels without a mod script).</summary>
        internal static string GameStepText(Class288 step)
        {
            if (step == null) return null;
            string text = GameText.Speech(step.string_0);
            if (step.bool_0 || step.reactorMember_0 == null) return text;
            // The two things the box points at, read separately (user rule): the pictured piece
            // (method_11 draws it under the text, in the layer's colour) and the highlighted cell.
            var cell = step.reactorBin_0.vector2i_0;
            string square = Loc.T("tutorial.square", new { cell = Loc.T("reactor.cell", new { x = cell.int_0 + 1, y = cell.int_1 + 1 }) });
            if (!(step.reactorMember_0 is Instruction i))
                return text + " " + Loc.T("tutorial.shown", new
                {
                    // Hardware keeps its name per piece (the tooltip title / ReactorFeature.string_0),
                    // not in the static field GameName reads — which left "Shown: ." for a Bonder.
                    what = step.reactorMember_0 is ReactorFeature f ? ReactorText.FeatureLabel(f) : ReactorText.GameName(step.reactorMember_0.GetType()),
                }) + " " + square;

            int layer = (int)step.reactorBin_0.enum114_0;
            string colour = (layer & (ReactorText.Red | ReactorText.RedArrow)) != 0 ? Loc.T("reactor.red")
                : (layer & (ReactorText.Blue | ReactorText.BlueArrow)) != 0 ? Loc.T("reactor.blue") : null;
            string label = ReactorText.Label(i);
            string name = ReactorText.GameName(i.GetType()) ?? label; // the game's name ("Input Molecule")
            var parts = new List<string> { colour == null ? name : colour + " " + name };
            if (label != name) parts.Add(label);                      // the icon's own label ("in alpha")
            int key = SlotKeyFor(i);
            if (key >= 0) parts.Add(Loc.T("tutorial.key", new { key = KeyLetter(key) }));
            return text + " " + Loc.T("tutorial.instruction", new { instruction = string.Join(", ", parts.ToArray()) }) + " " + square;
        }

        private static bool IsTutorialTarget(int x, int y) => Tutorial.Tutorial.IsTarget(x, y);

        private void BuildTutorial(GraphBuilder b)
        {
            if (Tutorial.Tutorial.ActiveText() == null) return;
            b.BeginStop(TutorialStop);
            b.AddItem(ControlId.Structural("reactor.tutorial.step"), ProfileUi.Text(true, () => Tutorial.Tutorial.ActiveText()));
        }
    }
}
