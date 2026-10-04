using System;
using System.Collections.Generic;
using Impeller;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens
{
    /// <summary>
    /// The profile flow — the first thing on screen after boot. Four small TitleScreenEditor
    /// overlays on top of the main menu, each a widget tree (Class226 content + a title) whose buttons
    /// are mouse-only GClass15s:
    ///
    ///   Class75  "Select a User Profile": up to three profile buttons (rank, name, "[ Last Played:
    ///            date ]") each with an X delete button, and "New Profile" in the empty slots.
    ///   Class72  "Enter a Profile Name": a text field (GClass16, 12 characters, the game's own
    ///            keyboard focus), Create Profile / Cancel, and the public-name notice.
    ///   Class73  "Really Delete this Profile?": Delete Profile / Cancel.
    ///   Class71  a message box (title, text, Okay) — the name validation errors, and any other
    ///            TitleScreenEditor message.
    ///
    /// Buttons replicate the click exactly: the game's click sound (Class428.class14_4, what GClass15
    /// plays on press) then the button's own handler. Escape stays native everywhere (the dialogs'
    /// imethod_0 cancels; the picker has no Escape).
    /// </summary>
    internal static class ProfileUi
    {
        /// <summary>The top screen as <typeparamref name="T"/>, but only once the game has (re)built its
        /// widgets. A Class54 rebuilds its whole tree (TitleScreenEditor.vmethod_5 → vmethod_8) the
        /// first update after it becomes the top again (bool_1 = "built since regaining the top"), and
        /// a rebuild makes NEW widgets — the name dialog's text field comes back EMPTY after the game's
        /// error box. Reading before that rebuild announced the stale field for one frame.</summary>
        public static T Settled<T>() where T : Class54
        {
            var top = GameApi.TopScreen() as T;
            return top != null && top.bool_1 ? top : null;
        }

        /// <summary>What a GClass15 button press does: click sound, then the action.</summary>
        public static void Press(Action action)
        {
            try
            {
                Class428.class14_4.vmethod_0();
                action();
            }
            catch (Exception ex) { Log.Error("[profiles] button action failed", ex); }
        }

        public static NodeVtable Button(Func<string> label, Action onPress) => new NodeVtable
        {
            ControlType = ControlTypes.Button,
            Announcements = new[] { new NodeAnnouncement(label, kind: AnnouncementKinds.Label) },
            OnActivate = () => Press(onPress),
        };

        public static NodeVtable Text(Func<string> text) => Text(false, text);

        /// <summary>A read-only text row; <paramref name="noPosition"/> keeps a lone paragraph from
        /// being counted "n of m" against the dialog's other single rows.</summary>
        public static NodeVtable Text(bool noPosition, Func<string> text) => new NodeVtable
        {
            ControlType = ControlTypes.Text,
            Announcements = new[] { new NodeAnnouncement(text, kind: AnnouncementKinds.Label) },
            SpeaksOwnPosition = noPosition,
        };
    }

    /// <summary>
    /// Class75, the profile picker. One row per drawn slot: a profile slot reads as the game draws it
    /// (name, rank, "Last Played: date") with the X button to its right (Right arrow) — also on
    /// Backspace; an empty slot is the "New Profile" button. The game lists the FIRST THREE profiles
    /// of Locals' profile set in its enumeration order; the graph enumerates the same live set the
    /// same way. Enter = the game's select (method_20: make current, load the save, close the picker).
    /// </summary>
    public sealed class ProfilePickerScreen : Screen
    {
        private const int Slots = 3;

        public override string Key => "profiles.picker";
        public override string ScreenName => GameText.T("Select a User Profile");

        // Keep the focused slot across the dialogs this screen opens (name entry, delete confirm).
        public override bool KeepStateOnPop => true;

        private static Class75 Picker => ProfileUi.Settled<Class75>();

        public override bool IsActive() => Picker != null;

        public override void Build(GraphBuilder b)
        {
            var picker = Picker;
            if (picker == null) return;

            var profiles = DrawnProfiles();
            for (int i = 0; i < Slots; i++)
            {
                var slot = ControlId.Structural("profiles.slot." + i);
                // Every slot speaks "n of 3" itself: the builder would count a profile row's cells
                // (profile, Delete) and the single-cell New Profile rows only among themselves.
                int index = i + 1;
                var position = new NodeAnnouncement(() => Loc.T("nav.position", new { index, count = Slots }),
                    kind: AnnouncementKinds.Position);
                if (i < profiles.Count)
                {
                    var p = profiles[i];
                    b.StartRow("profiles.row");
                    b.AddItem(slot, new NodeVtable
                    {
                        ControlType = ControlTypes.Button,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(() => p.string_0, kind: AnnouncementKinds.Label),
                            new NodeAnnouncement(() => Rank(p), kind: AnnouncementKinds.Value),
                            new NodeAnnouncement(() => GameText.T("Last Played") + ": " + p.method_2().ToString(),
                                kind: AnnouncementKinds.Tooltip),
                            position,
                        },
                        SpeaksOwnPosition = true,
                        OnActivate = () => ProfileUi.Press(() => Picker?.method_20(p)),
                        OnSecondary = () => ProfileUi.Press(() => Picker?.method_21(p)),
                    });
                    var delete = ProfileUi.Button(() => Loc.T("profiles.delete"), () => Picker?.method_21(p));
                    delete.SpeaksOwnPosition = true; // a cell of the slot's row, not a list item
                    b.AddItem(ControlId.Structural("profiles.delete." + i), delete);
                    b.EndRow();
                }
                else
                {
                    var newProfile = ProfileUi.Button(() => GameText.T("New Profile"), () => Picker?.method_18());
                    newProfile.Announcements = new[] { newProfile.Announcements[0], position };
                    newProfile.SpeaksOwnPosition = true;
                    b.AddItem(slot, newProfile);
                }
            }
        }

        /// <summary>The profiles the picker draws: the first three of the live set, in its order.</summary>
        private static List<Class436> DrawnProfiles()
        {
            var list = new List<Class436>();
            foreach (var p in Locals.smethod_5())
            {
                if (list.Count >= Slots) break;
                list.Add(p);
            }
            return list;
        }

        private static string Rank(Class436 p)
        {
            try { return ((Enum105)p.method_0()).smethod_1(); }
            catch { return null; }
        }
    }

    /// <summary>
    /// Class72, "Enter a Profile Name". The field is a TYPING-FIRST node over the game's own text
    /// widget (GClass16, which already holds the game's keyboard focus): printable characters arrive
    /// through SDL text input untouched, Backspace passes through to the widget, and typed/deleted
    /// characters echo. Enter on the field creates the profile the way the widget's own Enter does
    /// (method_19: validates — empty or duplicate names open the game's message box — then adds the
    /// profile and closes). The game's key queue is suppressed for our nav keys (GameKeySuppression's
    /// Class54 seam), so Enter never creates twice.
    /// </summary>
    public sealed class NewProfileScreen : Screen
    {
        public override string Key => "profiles.new";

        private static Class72 Entry => ProfileUi.Settled<Class72>();

        public override bool IsActive() => Entry != null;

        public override void Build(GraphBuilder b)
        {
            var entry = Entry;
            if (entry == null || entry.gclass16_0 == null) return;

            // The widget INSTANCE is the buffer: a rebuild swaps it (see ProfileUi.Settled), and the
            // echo re-baselines instead of reading the swap as a deletion (UI/GameTextField).
            b.AddItem(ControlId.Structural("profiles.new.name"), GameTextField.Node(
                () => Entry?.gclass16_0, () => GameText.T("Enter a Profile Name"), () => Entry?.method_19()));
            // One vertical column, no counts: name -> Create Profile -> Cancel -> notice (user layout,
            // 2026-09-27; the game draws the two buttons side by side).
            var create = ProfileUi.Button(() => GameText.T("Create Profile"), () => Entry?.method_19());
            create.SpeaksOwnPosition = true;
            b.AddItem(ControlId.Structural("profiles.new.create"), create);
            var cancel = ProfileUi.Button(() => GameText.T("Cancel"), () => Entry?.method_18());
            cancel.SpeaksOwnPosition = true;
            b.AddItem(ControlId.Structural("profiles.new.cancel"), cancel);
            b.AddItem(ControlId.Structural("profiles.new.notice"), ProfileUi.Text(true, () => GameText.Speech(string.Format(
                GameText.T("The profile name you enter here may be displayed publicly, such as on a high-score table or with one of your solutions.\n\nFor more information, please visit {0}."),
                "http://www.zachtronics.com"))));
        }
    }

    /// <summary>Class73, "Really Delete this Profile?". Focus starts on Cancel — the destructive
    /// button is one arrow away.</summary>
    public sealed class DeleteProfileScreen : Screen
    {
        public override string Key => "profiles.delete";
        public override string ScreenName => GameText.T("Really Delete this Profile?");

        private static Class73 Confirm => ProfileUi.Settled<Class73>();

        public override bool IsActive() => Confirm != null;

        public override void Build(GraphBuilder b)
        {
            if (Confirm == null) return;
            b.AddItem(ControlId.Structural("profiles.delete.confirm"),
                ProfileUi.Button(() => GameText.T("Delete Profile"), () => Confirm?.method_19()));
            var cancel = ControlId.Structural("profiles.delete.cancel");
            b.AddItem(cancel, ProfileUi.Button(() => GameText.T("Cancel"), () => Confirm?.method_18()));
            b.SetStart(cancel);
        }
    }

    /// <summary>Class71, the title-screen message box (title + text + Okay). Arrival speaks the title
    /// and the text; focus starts on Okay, whose Enter closes the box like the game's own imethod_0
    /// (the game's Enter is suppressed while we own the keyboard; Escape stays native). The text row
    /// stays reachable with the arrows.</summary>
    public sealed class TitleMessageScreen : Screen
    {
        public override string Key => "title.message";

        private static Class71 Box => ProfileUi.Settled<Class71>();

        public override bool IsActive() => Box != null;

        public override void OnFocus()
        {
            var box = Box;
            if (box == null) return;
            if (!string.IsNullOrEmpty(box.string_0)) Speech.Tts.Speak(box.string_0);
            if (!string.IsNullOrEmpty(box.string_1)) Speech.Tts.Speak(GameText.Speech(box.string_1));
        }

        public override void Build(GraphBuilder b)
        {
            var box = Box;
            if (box == null) return;
            b.AddItem(ControlId.Structural("title.message.text"), ProfileUi.Text(true, () => GameText.Speech(Box?.string_1)));
            var ok = ControlId.Structural("title.message.ok");
            var okButton = ProfileUi.Button(() => GameText.T("Okay"), () => Box?.method_18());
            okButton.SpeaksOwnPosition = true;
            b.AddItem(ok, okButton);
            b.SetStart(ok);
        }
    }
}
