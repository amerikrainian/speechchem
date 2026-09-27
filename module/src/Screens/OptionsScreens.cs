using System;
using System.Collections.Generic;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens
{
    /// <summary>
    /// Options — the "Settings" dialog (deob Class74, a TitleScreenEditor over the main menu). One
    /// vertical list, no counts (user layout, like the name dialog), in drawn order:
    ///
    ///   Fullscreen, Keep Aspect Ratio — toggles (Class475: method_7 = state, Enum106 0 = on; a press
    ///       flips it with the click sound, as the checkbox click does).
    ///   Language — the button that CYCLES languages on each click (method_21: English, then the
    ///       table codes from Class239.smethod_0(), shown by the game's own native names). A combo box
    ///       (user spec): Enter opens the choice list (ChoiceListScreen) on the current language;
    ///       Up/Down, Enter commits (the dialog's own index + button relabel, what method_21 sets),
    ///       Escape closes the list. Left/Right on the box do nothing.
    ///   Music volume, Sound volume — sliders (Class478: method_8/method_9, 0–1): Left/Right 5%,
    ///       PageUp/PageDown 25%. The dialog applies both volumes live every frame (vmethod_6); a Sound
    ///       change plays the game's sample, as releasing the mouse on that slider does.
    ///   Show Bonder Priority — toggle.
    ///   Save Changes (method_19: writes config.ini; a changed LANGUAGE restarts the game, a changed
    ///       Fullscreen/aspect re-creates the window) and Cancel (method_20: restores the volumes).
    ///
    /// Nothing is applied until Save, exactly like the mouse. Escape stays native (Cancel); the
    /// dialog's own Enter (Save) is suppressed while modeled — Save Changes is a node.
    /// </summary>
    public sealed class OptionsScreen : Screen
    {
        private const float SmallStep = 0.05f;
        private const float LargeStep = 0.25f;

        public override string Key => "options";
        public override string ScreenName => GameText.T("Settings");

        private static Class74 Dialog => ProfileUi.Settled<Class74>();

        public override bool IsActive() => Dialog != null;

        public override void Build(GraphBuilder b)
        {
            var d = Dialog;
            if (d == null || d.class475_0 == null) return;

            Toggle(b, "options.fullscreen", "Fullscreen", () => Dialog?.class475_0);
            Toggle(b, "options.aspect", "Keep Aspect Ratio", () => Dialog?.class475_1);
            Language(b, this);
            Slider(b, "options.music", "Music volume", () => Dialog?.class478_0, playSample: false);
            Slider(b, "options.sound", "Sound volume", () => Dialog?.class478_1, playSample: true);
            Toggle(b, "options.priority", "Show Bonder Priority", () => Dialog?.class475_2);
            Plain(b, ProfileUi.Button(() => GameText.T("Save Changes"), () => Dialog?.method_19()), "options.save");
            Plain(b, ProfileUi.Button(() => GameText.T("Cancel"), () => Dialog?.method_20()), "options.cancel");
        }

        private static void Plain(GraphBuilder b, NodeVtable vt, string id)
        {
            vt.SpeaksOwnPosition = true;
            b.AddItem(ControlId.Structural(id), vt);
        }

        // ---- toggles ----

        private static bool IsOn(Class475 t) => t != null && (int)t.method_7() == 0;

        private static void Toggle(GraphBuilder b, string id, string gameLabel, Func<Class475> widget)
        {
            Func<string> state = () => Loc.T(IsOn(widget()) ? "value.on" : "value.off");
            Plain(b, new NodeVtable
            {
                ControlType = ControlTypes.Toggle,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => GameText.T(gameLabel), kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(state, kind: AnnouncementKinds.Value),
                },
                StateText = state,
                OnActivate = () =>
                {
                    var t = widget();
                    if (t == null || !t.bool_1) return; // bool_1 = enabled (the draw dims otherwise)
                    t.method_8(IsOn(t) ? (Enum106)1 : (Enum106)0);
                    Class428.class14_4.vmethod_0();
                },
            }, id);
        }

        // ---- language ----

        private static void Language(GraphBuilder b, Screen owner)
        {
            Func<string> current = () =>
            {
                var d = Dialog;
                if (d == null || d.list_0 == null || d.list_0.Count == 0) return null;
                return Class74.smethod_11(d.list_0[d.int_0]);
            };
            Plain(b, new NodeVtable
            {
                ControlType = ControlTypes.ComboBox,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => GameText.T("Language"), kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(current, kind: AnnouncementKinds.Value),
                },
                OnActivate = () =>
                {
                    var d = Dialog;
                    if (d == null || d.list_0 == null || d.list_0.Count == 0) return;
                    owner.PushChild(new ChoiceListScreen("options.language.list", LanguageNames, d.int_0, CommitLanguage));
                },
            }, "options.language");
        }

        /// <summary>The choices in the button's cycle order, by the game's own native names.</summary>
        private static IReadOnlyList<string> LanguageNames()
        {
            var d = Dialog;
            var names = new List<string>();
            if (d?.list_0 == null) return names;
            foreach (var code in d.list_0) names.Add(Class74.smethod_11(code));
            return names;
        }

        /// <summary>What clicking the button until it shows this language leaves behind: the dialog's
        /// index (read by Save) and the relabelled button (method_21's Class203.smethod_6).</summary>
        private static void CommitLanguage(int index)
        {
            var d = Dialog;
            if (d == null || d.list_0 == null || index < 0 || index >= d.list_0.Count) return;
            try
            {
                Class428.class14_4.vmethod_0();
                d.int_0 = index;
                Class203.smethod_6(d.gclass15_0, Class74.smethod_11(d.list_0[index]));
            }
            catch (Exception ex) { Log.Error("[options] language commit failed", ex); }
        }

        // ---- sliders ----

        private static void Slider(GraphBuilder b, string id, string gameLabel, Func<Class478> widget, bool playSample)
        {
            Func<string> percent = () =>
            {
                var s = widget();
                return s == null ? null : Loc.T("value.percent", new { value = (int)Math.Round(s.method_8() * 100f) });
            };
            Plain(b, new NodeVtable
            {
                ControlType = ControlTypes.Slider,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => GameText.T(gameLabel), kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(percent, kind: AnnouncementKinds.Value),
                },
                StateText = percent,
                OnAdjust = (sign, large) =>
                {
                    var s = widget();
                    if (s == null) return;
                    float step = large ? LargeStep : SmallStep;
                    // Snap to the step grid so repeated presses land on round percentages.
                    float target = (float)Math.Round((s.method_8() + sign * step) / SmallStep) * SmallStep;
                    s.method_9(target);
                    // The release path: the Sound slider plays its sample (Class74.method_18).
                    if (playSample && s.struct116_0.bool_0) s.struct116_0.method_0()();
                },
            }, id);
        }
    }
}
