using System;
using SpeechChem.Localization;
using SpeechChem.UI.Graph;

namespace SpeechChem.UI
{
    /// <summary>
    /// A text-entry node over one of the game's text widgets (GClass16: append-only — typed
    /// characters arrive through SDL text input, Backspace deletes the last one, Enter runs its
    /// action; no caret, no selection; a length cap int_0 and a font filter drop what doesn't fit,
    /// silently). The navigator's echo (Echopunks' WatchTextEntry: typed characters, deletions)
    /// watches the RAW buffer string_0 — method_7() marks up digits in formula fields ("~02") —
    /// and the widget instance is the buffer's identity, so a dialog rebuild re-baselines instead
    /// of reading the swap as a deletion. An empty field reads "blank". Characters and the paste
    /// key reach the widget only while this node is focused (GameKeySuppression).
    /// </summary>
    internal static class GameTextField
    {
        /// <param name="field">The live widget (may change on a rebuild, or be null).</param>
        /// <param name="label">The field's label (the game's prompt).</param>
        /// <param name="submit">Enter on the field (the widget's own Enter, e.g. the dialog's Okay).</param>
        public static NodeVtable Node(Func<GClass16> field, Func<string> label, Action submit) => new NodeVtable
        {
            ControlType = ControlTypes.TextField,
            Announcements = new[]
            {
                new NodeAnnouncement(label, kind: AnnouncementKinds.Label),
                new NodeAnnouncement(() => Value(field()), kind: AnnouncementKinds.Value),
            },
            TextEntry = true,
            TextValue = () => field()?.string_0 ?? "",
            TextIdentity = () => field(),
            SpeaksOwnPosition = true, // a lone field among a dialog's buttons, not "1 of 3"
            OnActivate = submit,
        };

        /// <summary>The field's text, or "blank".</summary>
        public static string Value(GClass16 field)
        {
            string v = field?.string_0;
            return string.IsNullOrEmpty(v) ? Loc.T("text.blank") : v;
        }

        /// <summary>The text with surrounding spaces trimmed, as the game validates it.</summary>
        public static bool IsBlank(GClass16 field) => string.IsNullOrEmpty(field?.string_0?.Trim());
    }
}
