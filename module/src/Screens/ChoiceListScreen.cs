using System;
using System.Collections.Generic;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens
{
    /// <summary>
    /// The expanded list of a combo box — a CHILD sub-screen pushed by the screen owning the box
    /// (Screen.PushChild), so the navigator moves into it and returns to the box when it closes. User
    /// spec (2026-09-27): Enter on a combo box opens this list with focus on the box's current choice;
    /// Up/Down move through the choices; Enter commits the focused choice and closes; Escape closes
    /// without changing anything. (Left/Right on the box itself do nothing.)
    ///
    /// Mod-side modal: <see cref="ModalCapturesEscape"/> keeps Escape from the game (it would cancel
    /// the dialog under the list) and routes it to the Back action here. Reusable for any game control
    /// that picks one of several values.
    /// </summary>
    public sealed class ChoiceListScreen : Screen
    {
        private readonly string _key;
        private readonly Func<IReadOnlyList<string>> _choices;
        private readonly int _initial;
        private readonly Action<int> _commit;

        /// <param name="key">Stable identity (control ids derive from it).</param>
        /// <param name="choices">The choices' labels, read live.</param>
        /// <param name="initial">The index focused on opening (the box's current choice).</param>
        /// <param name="commit">Runs with the chosen index when Enter commits.</param>
        public ChoiceListScreen(string key, Func<IReadOnlyList<string>> choices, int initial, Action<int> commit)
        {
            _key = key;
            _choices = choices;
            _initial = initial;
            _commit = commit;
        }

        public override string Key => _key;

        // Pushed imperatively as a child; never polled from the registry.
        public override bool IsActive() => true;

        public override bool ModalCapturesEscape => true;

        public override bool Exclusive => true;

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Close);
        }

        public override void Build(GraphBuilder b)
        {
            var choices = _choices();
            if (choices == null) return;
            for (int i = 0; i < choices.Count; i++)
            {
                int index = i;
                var id = ControlId.Structural(_key + ".choice." + i);
                b.AddItem(id, new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => { var c = _choices(); return c != null && index < c.Count ? c[index] : null; },
                            kind: AnnouncementKinds.Label),
                    },
                    OnActivate = () =>
                    {
                        Close();
                        try { _commit(index); }
                        catch (Exception ex) { Log.Error("[choices] commit failed for " + _key, ex); }
                    },
                });
                if (i == _initial) b.SetStart(id);
            }
        }

        private void Close() => ParentScreen?.RemoveChild(this);
    }
}
