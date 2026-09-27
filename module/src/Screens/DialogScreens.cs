using System;
using System.Collections.Generic;
using Impeller;
using SpaceChem;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.Patches;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens
{
    /// <summary>
    /// SpaceChem.MessageBoxEditor, the in-level message box: "Reaction Error" (collisions, atoms
    /// leaving the reactor, molecules pulled apart), the "Return to the assignment selection
    /// screen?" prompt (Yes/No), the pipeline errors. Arrival speaks the title and the text, then
    /// the error markers the box draws on the reactor ("reactor/error_indicator") as grid cells:
    /// the "where" a sighted player sees. Buttons are an uncounted vertical list, focus on the
    /// first; activation runs the box's own button path (close, then the action). Escape stays
    /// native (the box's own hotkeys); Enter is ours while modeled, through the focused button.
    /// </summary>
    public sealed class MessageBoxEditorScreen : Screen
    {
        public override string Key => "messageboxeditor";

        private static MessageBoxEditor Box => ProfileUi.Settled<MessageBoxEditor>();

        public override bool IsActive() => Box != null;

        public override void OnFocus()
        {
            var info = DialogCapture.BoxOf(Box);
            if (info == null) return;
            if (!string.IsNullOrEmpty(info.Title)) Speech.Tts.Speak(GameText.Speech(info.Title));
            if (!string.IsNullOrEmpty(info.Text)) Speech.Tts.Speak(GameText.Speech(info.Text));
            string where = Markers(info);
            if (where != null) Speech.Tts.Speak(where);
        }

        public override void Build(GraphBuilder b)
        {
            var box = Box;
            var info = DialogCapture.BoxOf(box);
            if (box == null || info == null) return;
            if (!string.IsNullOrEmpty(info.Text))
                b.AddItem(ControlId.Structural("mbe.text"), ProfileUi.Text(true, () => GameText.Speech(DialogCapture.BoxOf(Box)?.Text)));
            if (Markers(info) != null)
                b.AddItem(ControlId.Structural("mbe.where"), ProfileUi.Text(true, () => Markers(DialogCapture.BoxOf(Box))));
            if (info.Buttons == null) return;
            ControlId first = null;
            for (int i = 0; i < info.Buttons.Count; i++)
            {
                var button = info.Buttons[i];
                var id = ControlId.Structural("mbe.button." + i);
                if (first == null) first = id;
                // The widget's own click path (MessageBoxEditor.Class555.method_0): close, then act.
                var vt = ProfileUi.Button(() => button.string_0, () =>
                {
                    var bx = Box;
                    if (bx == null) return;
                    bx.method_5();
                    button.action_0?.Invoke();
                });
                vt.SpeaksOwnPosition = true;
                b.AddItem(id, vt);
            }
            if (first != null) b.SetStart(first);
        }

        /// <summary>The error markers as reactor cells ("At 5, 3"), when the box sits over a reactor
        /// editor and the markers fall inside its grid; null otherwise.</summary>
        private static string Markers(DialogCapture.MessageBox info)
        {
            if (info?.Markers == null || info.Markers.Count == 0) return null;
            var reactor = Class53.smethod_5<Class77>()?.reactor_0;
            if (reactor == null) return null;
            var cells = new List<string>();
            var size = reactor.method_1();
            foreach (var p in info.Markers)
            {
                var rel = p - reactor.vector2i_0;
                if (rel.int_0 < 0 || rel.int_1 < 0) continue;
                int x = rel.int_0 / SpaceChem.Reactor.Reactor.vector2i_5.int_0;
                int y = rel.int_1 / SpaceChem.Reactor.Reactor.vector2i_5.int_1;
                if (x >= size.int_0 || y >= size.int_1) continue;
                string cell = Loc.T("reactor.cell", new { x = x + 1, y = y + 1 });
                if (!cells.Contains(cell)) cells.Add(cell);
            }
            if (cells.Count == 0) return null;
            return Loc.T("dialog.markers", new { cells = string.Join("; ", cells.ToArray()) });
        }
    }

    /// <summary>
    /// Class69, "An invalid molecule was passed to" an output: the output's name (or "a disabled
    /// output"), the produced molecule and the accepted ones, and Okay (stops the simulation and
    /// closes, like the game's Enter/Escape). Arrival speaks it all; focus on Okay.
    /// </summary>
    public sealed class WrongMoleculeScreen : Screen
    {
        public override string Key => "wrongmolecule";

        private static Class69 Dialog => ProfileUi.Settled<Class69>();

        public override bool IsActive() => Dialog != null;

        public override void OnFocus()
        {
            foreach (var line in Lines(DialogCapture.WrongOf(Dialog)))
                Speech.Tts.Speak(line);
        }

        public override void Build(GraphBuilder b)
        {
            var info = DialogCapture.WrongOf(Dialog);
            if (Dialog == null || info == null) return;
            var lines = Lines(info);
            for (int i = 0; i < lines.Count; i++)
            {
                int index = i;
                b.AddItem(ControlId.Structural("wrongmol.line." + i), ProfileUi.Text(true, () =>
                {
                    var l = Lines(DialogCapture.WrongOf(Dialog));
                    return index < l.Count ? l[index] : null;
                }));
            }
            var ok = ControlId.Structural("wrongmol.ok");
            var vt = ProfileUi.Button(() => GameText.T("Okay"), () => Dialog?.method_13());
            vt.SpeaksOwnPosition = true;
            b.AddItem(ok, vt);
            b.SetStart(ok);
        }

        private static List<string> Lines(DialogCapture.WrongMolecule info)
        {
            var lines = new List<string>();
            if (info == null) return lines;
            string output = info.Accepted.Count == 0 ? GameText.T("a disabled output") : info.Output;
            lines.Add(GameText.T("An invalid molecule was passed to") + " " + output);
            lines.Add(GameText.T("Produced") + ": " + MoleculeText.NameAndFormula(info.Produced));
            foreach (var m in info.Accepted)
                lines.Add(GameText.T("Accepted") + ": " + MoleculeText.NameAndFormula(m));
            return lines;
        }
    }
}
