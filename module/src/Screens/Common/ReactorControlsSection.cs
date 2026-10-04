using System;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens.Common
{
    /// <summary>
    /// The defense levels' Reactor Controls (Class710), shown by the game in place of the pipeline
    /// shelf and the reactor palette while a defense run is not stopped: four toggles, control A-D
    /// on F1-F4, which the "Branch - Control" instruction checks. A Tab stop of four toggles in
    /// order A-D; Enter flips one exactly as its F key does (Class710.method_7: flip + click
    /// sound). The F keys themselves reach the game (DefenseCapture speaks their new state).
    /// Then the panel's graph, one row per labelled range (DefenseText.GraphRows).
    /// </summary>
    internal static class ReactorControlsSection
    {
        public static void Build(GraphBuilder b, string stopKey, string idPrefix)
        {
            b.BeginStop(stopKey);
            b.PushContext(GameText.T("Reactor Controls"), id: ControlId.Structural(idPrefix));
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                Func<string> state = () => DefenseText.OnOff(DefenseText.ControlOn(index));
                b.AddItem(ControlId.Structural(idPrefix + "." + i), new NodeVtable
                {
                    ControlType = ControlTypes.Toggle,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => DefenseText.ControlName(index), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(state, kind: AnnouncementKinds.Value),
                    },
                    StateText = state,
                    OnActivate = () => Flip(index),
                });
            }
            b.PopContext();
            // The panel's graph (the enemy's state as the level draws it): one row per labelled
            // range, read when focused (the waveform carries noise, so it is not live).
            var rows = DefenseText.GraphRows();
            for (int i = 0; i < rows.Count; i++)
            {
                int index = i;
                b.AddItem(ControlId.Structural(idPrefix + ".graph." + i), ProfileUi.Text(true, () =>
                {
                    var now = DefenseText.GraphRows();
                    return index < now.Count ? now[index] : null;
                }));
            }
        }

        private static void Flip(int i)
        {
            if (!DefenseText.ControlsShown) return;
            Patches.DefenseCapture.QuietToggle = true;
            try { Class710.smethod_0().method_7((Enum111)i); }
            catch (Exception ex) { Log.Error("[defense] control toggle failed", ex); }
            finally { Patches.DefenseCapture.QuietToggle = false; }
        }
    }
}
