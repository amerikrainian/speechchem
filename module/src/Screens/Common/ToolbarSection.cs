using System;
using SpaceChem;
using SpaceChem.UI;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens.Common
{
    /// <summary>
    /// The level toolbar (SpaceChem.UI.ToolbarComponent — ONE shared instance drawn by the reactor
    /// editor and the pipeline editor alike), as a reusable Tab stop: Stop, Pause, the four play
    /// speeds, Story &amp; Info, Undo, Redo, the periodic table, Exit Level / Exit Reactor. Each
    /// node presses the game's own widget or handler, so sounds, enable rules and the Defense speed
    /// remap all apply. The run-state buttons are radio-like: the game highlights the current
    /// state/speed (GClass11.method_9), spoken as "selected". Disabled buttons (the draw dims them:
    /// GClass11/GClass15.method_7) read "unavailable" and do nothing.
    /// </summary>
    internal static class ToolbarSection
    {
        private static ToolbarComponent Toolbar => ToolbarComponent.smethod_0();

        public static void Build(GraphBuilder b, string stopKey, string idPrefix)
        {
            b.BeginStop(stopKey);
            StateButton(b, idPrefix + ".stop", () => Loc.T("toolbar.stop"), () => Toolbar.method_2((Enum16)0));
            StateButton(b, idPrefix + ".pause", () => Loc.T("toolbar.pause"), () => Toolbar.method_2((Enum16)2));
            StateButton(b, idPrefix + ".play1", () => Loc.T("toolbar.play", new { n = 1 }), () => Toolbar.method_4(SimulatorSpeed.Slow));
            StateButton(b, idPrefix + ".play2", () => Loc.T("toolbar.play", new { n = 2 }), () => Toolbar.method_4(SimulatorSpeed.Medium));
            StateButton(b, idPrefix + ".play3", () => Loc.T("toolbar.play", new { n = 3 }), () => Toolbar.method_4(SimulatorSpeed.Fast));
            StateButton(b, idPrefix + ".play4", () => Loc.T("toolbar.play", new { n = 4 }), () => Toolbar.method_4(SimulatorSpeed.WarpSpeed));
            ToolButton(b, idPrefix + ".story", () => GameText.T("Story & Info"), () => Toolbar.gclass15_3, () => Toolbar.method_12());
            ToolButton(b, idPrefix + ".undo", () => GameText.T("Undo"), () => Toolbar.gclass15_0, () => Toolbar.method_10());
            ToolButton(b, idPrefix + ".redo", () => GameText.T("Redo"), () => Toolbar.gclass15_1, () => Toolbar.method_11());
            ToolButton(b, idPrefix + ".periodic", () => Loc.T("toolbar.periodic"), () => Toolbar.gclass15_4, () => Toolbar.method_6());
            ToolButton(b, idPrefix + ".exit", () => GameText.T(Toolbar.method_7() ? "Exit Reactor" : "Exit Level"), () => Toolbar.gclass15_2, () => Toolbar.method_8());
        }

        private static void StateButton(GraphBuilder b, string id, Func<string> label, Func<GClass11> widget)
        {
            b.AddItem(ControlId.Structural(id), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[]
                {
                    new NodeAnnouncement(label, kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => Safe(() => widget().method_9()) ? Loc.T("value.selected") : null,
                        kind: AnnouncementKinds.Selected),
                    new NodeAnnouncement(() => Safe(() => widget().method_7()) ? null : Loc.T("value.unavailable"),
                        kind: AnnouncementKinds.Enabled),
                },
                OnActivate = () =>
                {
                    var w = widget();
                    if (w == null) return;
                    if (w.method_7()) w.method_12();
                    else Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true); // e.g. Pause while stopped
                },
            });
        }

        private static void ToolButton(GraphBuilder b, string id, Func<string> label, Func<GClass15> widget, Action press)
        {
            b.AddItem(ControlId.Structural(id), new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[]
                {
                    new NodeAnnouncement(label, kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => Safe(() => widget().method_7()) ? null : Loc.T("value.unavailable"),
                        kind: AnnouncementKinds.Enabled),
                },
                OnActivate = () =>
                {
                    var w = widget();
                    if (w == null) return;
                    if (!w.method_7())
                    {
                        // The game ignores a disabled button; say so instead of nothing (Undo / Redo
                        // at the end of the history, Pause while stopped).
                        Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true);
                        return;
                    }
                    try
                    {
                        Class428.class14_4.vmethod_0(); // the GClass15 press sound
                        press();
                    }
                    catch (Exception ex) { Log.Error("[toolbar] press failed", ex); }
                },
            });
        }

        private static bool Safe(Func<bool> f)
        {
            try { return f(); }
            catch { return false; }
        }
    }
}
