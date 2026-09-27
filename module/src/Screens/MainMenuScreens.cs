using System;
using System.Collections.Generic;
using Impeller;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.Patches;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens
{
    /// <summary>
    /// The main menu (SpaceChem.MainMenuEditor, name-preserved; the root screen). Three Tab stops:
    ///
    ///  1. Menu — the six drawn buttons as one vertical list: Start Game (LevelSelectEditor), Challenges
    ///     (ChallengeEditor), Switch Profile (the profile picker), Options (Class74 "Settings"), Credits
    ///     (the credits sequence), Quit (exits at once, like the button). Each runs the button's own
    ///     handler after the GClass15 click sound; transitions play as for a click.
    ///  2. News — the pane's header, date and body (captured where method_19 builds them; the date and
    ///     body are English literals in the game code). The pane's "&lt;" / "&gt;" buttons are drawn
    ///     disabled with empty handlers and "More Information" is hidden, so none of them is a node.
    ///  3. Extras — the clickable art the title-screen base draws on the main menu only (lettered art,
    ///     so the labels are mod transcriptions): the ResearchNet tablet ("Sign On" — the journal, or
    ///     the game's ACCESS DENIED dialog before it unlocks), the 63 Corvi planet (only when the DLC is
    ///     owned — the draw's own gate), the Team Fortress 2 icon; then the profile plate (name, rank).
    ///
    /// Escape stays native: on the main menu it QUITS THE GAME immediately (MainMenuEditor.imethod_0).
    /// </summary>
    public sealed class MainMenuScreen : Screen
    {
        private const string MenuStop = "menu";
        private const string NewsStop = "news";
        private const string ExtrasStop = "extras";

        public override string Key => "mainmenu";
        public override string ScreenName => Loc.T("screen.MainMenuEditor");
        public override bool KeepStateOnPop => true; // come back to the button you left through
        public override object InitialFocusStop => MenuStop;

        private static SpaceChem.MainMenuEditor Menu => ProfileUi.Settled<SpaceChem.MainMenuEditor>();

        public override bool IsActive() => Menu != null;

        public override void Build(GraphBuilder b)
        {
            var menu = Menu;
            if (menu == null) return;

            b.BeginStop(MenuStop);
            Button(b, "mainmenu.start", () => GameText.T("Start Game"), () => Menu?.method_24());
            Button(b, "mainmenu.challenges", () => GameText.T("Challenges"), () => Menu?.method_26());
            Button(b, "mainmenu.profile", () => GameText.T("Switch Profile"), () => Menu?.method_25());
            Button(b, "mainmenu.options", () => GameText.T("Options"), () => Menu?.method_27());
            Button(b, "mainmenu.credits", () => GameText.T("Credits"), () => Menu?.method_28());
            Button(b, "mainmenu.quit", () => GameText.T("Quit"), () => Class280.class185_0.vmethod_0());

            var news = TitleTextCapture.NewsOf(menu);
            if (news == null)
            {
                TitleTextCapture.Recapture(menu); // a module generation newer than the menu's build
                news = TitleTextCapture.NewsOf(menu);
            }
            if (news != null)
            {
                b.BeginStop(NewsStop);
                Text(b, "mainmenu.news.header", () => TitleTextCapture.NewsOf(Menu)?.Header);
                Text(b, "mainmenu.news.date", () => TitleTextCapture.NewsOf(Menu)?.Date);
                Text(b, "mainmenu.news.body", () => GameText.Speech(TitleTextCapture.NewsOf(Menu)?.Body));
            }

            // The extras count only their buttons: the builder would count the plate row with them.
            var extras = new List<KeyValuePair<string, KeyValuePair<Func<string>, Action>>>();
            extras.Add(Extra("mainmenu.researchnet", () => Loc.T("mainmenu.researchnet"), () => Menu?.method_12()));
            if (Class280.bool_5 || Class47.smethod_10())
                extras.Add(Extra("mainmenu.corvi", () => Loc.T("mainmenu.corvi"), () => Menu?.method_14()));
            if (Class448.bool_0)
                extras.Add(Extra("mainmenu.tf2", () => Loc.T("mainmenu.tf2"), () => Menu?.method_13()));
            b.BeginStop(ExtrasStop);
            for (int i = 0; i < extras.Count; i++)
            {
                var vt = ProfileUi.Button(extras[i].Value.Key, extras[i].Value.Value);
                int index = i + 1, count = extras.Count;
                if (count > 1)
                    vt.Announcements = new[]
                    {
                        vt.Announcements[0],
                        new NodeAnnouncement(() => Loc.T("nav.position", new { index, count }), kind: AnnouncementKinds.Position),
                    };
                vt.SpeaksOwnPosition = true;
                b.AddItem(ControlId.Structural(extras[i].Key), vt);
            }
            if (Locals.smethod_0() != null)
                Text(b, "mainmenu.plate", ProfilePlate);
        }

        private static KeyValuePair<string, KeyValuePair<Func<string>, Action>> Extra(string id, Func<string> label, Action onPress)
            => new KeyValuePair<string, KeyValuePair<Func<string>, Action>>(id, new KeyValuePair<Func<string>, Action>(label, onPress));

        private static void Button(GraphBuilder b, string id, Func<string> label, Action onPress)
            => b.AddItem(ControlId.Structural(id), ProfileUi.Button(label, onPress));

        private static void Text(GraphBuilder b, string id, Func<string> text)
            => b.AddItem(ControlId.Structural(id), ProfileUi.Text(true, text));

        /// <summary>The plate under the ResearchNet tablet: the current profile's name and rank title.</summary>
        private static string ProfilePlate()
        {
            var p = Locals.smethod_0();
            if (p == null) return null;
            string rank = null;
            try { rank = ((Enum105)p.method_0()).smethod_1(); } catch { }
            return string.IsNullOrEmpty(rank) ? p.string_0 : p.string_0 + ", " + rank;
        }
    }

    /// <summary>
    /// Class60 dialogs — ResearchNet-styled notices with a title, a paragraph and a row of buttons
    /// (ACCESS DENIED before ResearchNet unlocks: Back / "Hack into the system!"; the unlock
    /// congratulations; …). Arrival speaks title and text; the buttons (one vertical list, no counts —
    /// the game draws them side by side) are the labels and actions the
    /// dialog registered (captured by TitleTextCapture), run after the click sound. Focus starts on the
    /// first button, as registered. Escape stays native (Class56 closes).
    /// </summary>
    public sealed class NetDialogScreen : Screen
    {
        public override string Key => "netdialog";

        private static Class60 Dialog => ProfileUi.Settled<Class60>();

        public override bool IsActive() => Dialog != null;

        public override void OnFocus()
        {
            var d = Dialog;
            if (d == null) return;
            if (!string.IsNullOrEmpty(d.string_0)) Speech.Tts.Speak(GameText.Speech(d.string_0));
            if (!string.IsNullOrEmpty(d.string_1)) Speech.Tts.Speak(GameText.Speech(d.string_1));
        }

        public override void Build(GraphBuilder b)
        {
            var d = Dialog;
            if (d == null) return;
            b.AddItem(ControlId.Structural("netdialog.text"), ProfileUi.Text(true, () => GameText.Speech(Dialog?.string_1)));
            var buttons = TitleTextCapture.ButtonsOf(d);
            if (buttons == null) return;
            ControlId first = null;
            for (int i = 0; i < buttons.Count; i++)
            {
                var button = buttons[i];
                var id = ControlId.Structural("netdialog.button." + i);
                if (first == null) first = id;
                var vt = ProfileUi.Button(() => button.Label, button.Action);
                vt.SpeaksOwnPosition = true; // a plain vertical list, no counts (user layout, as the name dialog)
                b.AddItem(id, vt);
            }
            if (first != null) b.SetStart(first);
        }
    }
}
