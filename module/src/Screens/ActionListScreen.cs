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
    /// </summary>
    public sealed class ActionListScreen : Screen
    {
        public sealed class Item
        {
            public Func<string> Label;
            public Func<bool> Selected;
            public Action Run;
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
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                b.AddItem(ControlId.Structural(_key + ".item." + i), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(item.Label, kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => item.Selected != null && item.Selected() ? Loc.T("value.selected") : null,
                            kind: AnnouncementKinds.Tooltip),
                    },
                    OnActivate = () =>
                    {
                        Close();
                        try { item.Run?.Invoke(); }
                        catch (Exception ex) { Log.Error("[actions] item failed in " + _key, ex); }
                    },
                });
            }
        }

        private void Close() => ParentScreen?.RemoveChild(this);
    }
}
