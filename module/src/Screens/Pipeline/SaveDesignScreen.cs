using SpeechChem.Game;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens.Pipeline
{
    /// <summary>
    /// Class55, the pipeline menu's "Save to Toolbox" dialog (a Class54 over the pipeline editor):
    /// "Enter a name for this reactor design:", a 10-character text field (GClass16, the game's own
    /// keyboard focus), Okay and Cancel. Okay — or Enter on the field, the widget's own action — is
    /// method_13: a name that is blank once trimmed does nothing in the game (no message), so the
    /// mod speaks the widget's own "Required!"; otherwise the reactor's program and notes are copied
    /// into a level-less Component row (SpaceChemUserWorker.method_58, queued) and the dialog closes.
    /// Names need not be unique. Cancel = method_12; Escape stays native (imethod_0 cancels).
    /// One vertical column, no counts (the dialog layout rule).
    /// </summary>
    public sealed class SaveDesignScreen : Screen
    {
        public override string Key => "pipeline.savedesign";
        public override string ScreenName => GameText.T("Save to Toolbox");

        private static Class55 Dialog => ProfileUi.Settled<Class55>();

        public override bool IsActive() => Dialog != null;

        public override void Build(GraphBuilder b)
        {
            if (Dialog == null) return;
            b.AddItem(ControlId.Structural("savedesign.name"), GameTextField.Node(
                () => Dialog?.gclass16_0, () => GameText.T("Enter a name for this reactor design:"), Okay));
            var okay = ProfileUi.Button(() => GameText.T("Okay"), Okay);
            okay.SpeaksOwnPosition = true;
            b.AddItem(ControlId.Structural("savedesign.okay"), okay);
            var cancel = ProfileUi.Button(() => GameText.T("Cancel"), () => Dialog?.method_12());
            cancel.SpeaksOwnPosition = true;
            b.AddItem(ControlId.Structural("savedesign.cancel"), cancel);
        }

        private static void Okay()
        {
            var d = Dialog;
            if (d == null) return;
            if (GameTextField.IsBlank(d.gclass16_0))
            {
                Speech.Tts.Speak(GameText.Speech(d.gclass16_0.string_1), interrupt: true); // the widget's "Required!"
                return;
            }
            d.method_13();
        }
    }
}
