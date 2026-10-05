using System;
using System.Collections.Generic;
using SpeechChem.Localization;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens
{
    /// <summary>
    /// A pop-up list of actions — the keyboard form of a game context menu (the reactor's
    /// right-click menus now; any screen's later). A CHILD sub-screen (Screen.PushChild): Up/Down
    /// move, Enter runs the focused action and closes the list (an action may push another list in
    /// its place — nested menus), Escape closes without acting (mod-side modal: Escape never reaches
    /// the game). Items may report a "selected" state (the menu's current choice, e.g. an arrow's
    /// direction), spoken like the game's highlighted icon.
    ///
    /// Items sharing a non-null <see cref="Item.Group"/> are mutually exclusive choices: RADIO
    /// BUTTONS that apply on Enter, say "selected" and leave the list open, so several properties
    /// (a layer and a variant) can be set in one visit (user rule, 2026-09-27). Each group is ONE
    /// HORIZONTAL ROW named by its <see cref="Item.GroupLabel"/> (the parameter: colour, direction,
    /// …; user rule 2026-10-05): Up/Down walk the parameters and plain actions, landing on a row's
    /// selected choice; Left/Right walk the choices. No counts in a grouped menu.
    /// </summary>
    public sealed class ActionListScreen : Screen
    {
        public sealed class Item
        {
            public Func<string> Label;
            public Func<bool> Selected;
            public Action Run;
            /// <summary>Non-null = one of a set of mutually exclusive choices (a radio button).</summary>
            public object Group;
            /// <summary>The group's row name (the parameter its choices set); read from the group's
            /// first item.</summary>
            public Func<string> GroupLabel;
        }

        private readonly string _key;
        private readonly string _title;
        private readonly List<Item> _items;

        public ActionListScreen(string key, string title, List<Item> items)
        {
            _key = key;
            _title = title;
            _items = items ?? new List<Item>();
        }

        public override string Key => _key;
        public override string ScreenName => _title;
        public override bool IsActive() => true; // pushed imperatively as a child, never polled
        public override bool ModalCapturesEscape => true;
        public override bool Exclusive => true;

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Close);
        }

        public override void Build(GraphBuilder b)
        {
            // Lines in menu order: a group sits where its first item does, with all its items.
            var lines = new List<List<int>>();
            var lineOf = new Dictionary<object, List<int>>();
            for (int i = 0; i < _items.Count; i++)
            {
                var group = _items[i].Group;
                if (group != null && lineOf.TryGetValue(group, out var line)) { line.Add(i); continue; }
                line = new List<int> { i };
                if (group != null) lineOf.Add(group, line);
                lines.Add(line);
            }
            bool grouped = lineOf.Count > 0;

            // Where Up/Down land on each line: a group's selected choice, else its first item.
            var landing = new ControlId[lines.Count];
            for (int l = 0; l < lines.Count; l++)
            {
                var line = lines[l];
                landing[l] = ItemId(line[0]);
                if (_items[line[0]].Group == null) continue;
                foreach (int i in line)
                    if (IsSelected(_items[i])) { landing[l] = ItemId(i); break; }
            }

            for (int l = 0; l < lines.Count; l++)
            {
                var line = lines[l];
                var first = _items[line[0]];
                if (first.Group == null)
                {
                    var vt = ItemVtable(first, false);
                    // Among parameter rows a count of the plain actions alone would mislead.
                    if (grouped) vt.SpeaksOwnPosition = true;
                    b.AddItem(ItemId(line[0]), vt);
                    continue;
                }
                string name = SafeLabel(first.GroupLabel) ?? Loc.T("menu.group.default");
                b.PushContext(name, id: ControlId.Structural(_key + ".group." + l));
                b.StartRow();
                foreach (int i in line)
                {
                    var vt = ItemVtable(_items[i], true);
                    vt.SpeaksOwnPosition = true; // no "2 of 4" within a row (user rule 2026-10-05)
                    b.AddItem(ItemId(i), vt);
                }
                b.EndRow();
                b.PopContext();
            }

            if (!grouped) return;
            for (int l = 0; l < lines.Count; l++)
                foreach (int i in lines[l])
                {
                    if (l > 0) b.Connect(ItemId(i), GraphDir.Up, landing[l - 1]);
                    if (l < lines.Count - 1) b.Connect(ItemId(i), GraphDir.Down, landing[l + 1]);
                }
            if (lines.Count > 0) b.SetStart(landing[0]);
        }

        private NodeVtable ItemVtable(Item item, bool radio)
        {
            Func<string> selected = () => IsSelected(item) ? Loc.T("value.selected") : null;
            return new NodeVtable
            {
                ControlType = radio ? ControlTypes.RadioButton : ControlTypes.Text,
                Announcements = new[]
                {
                    new NodeAnnouncement(item.Label, kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(selected, kind: radio ? AnnouncementKinds.Selected : AnnouncementKinds.Tooltip),
                },
                StateText = radio ? selected : null, // spoken after Enter: the choice took
                OnActivate = () =>
                {
                    if (!radio) Close();
                    try { item.Run?.Invoke(); }
                    catch (Exception ex) { Log.Error("[actions] item failed in " + _key, ex); }
                },
            };
        }

        private ControlId ItemId(int index) => ControlId.Structural(_key + ".item." + index);

        private static bool IsSelected(Item item)
        {
            try { return item.Selected != null && item.Selected(); }
            catch { return false; }
        }

        private static string SafeLabel(Func<string> label)
        {
            try { return label?.Invoke(); }
            catch { return null; }
        }

        private void Close() => ParentScreen?.RemoveChild(this);
    }
}
