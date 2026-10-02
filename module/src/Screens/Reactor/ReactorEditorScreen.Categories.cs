using System;
using System.Collections.Generic;
using Impeller;
using SpaceChem;
using SpaceChem.Reactor;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;
using ReactorModel = SpaceChem.Reactor.Reactor;

namespace SpeechChem.Screens.Reactor
{
    public sealed partial class ReactorEditorScreen
    {
        // ---- category cycling (user request, 2026-09-27): [ and ] step through categories, , and .
        // through the category's items. Grid items move the cursor there (read as if arrowed onto);
        // Instructions arm the palette slot for the next Enter on the grid; Waldos read like Shift+R / Shift+B
        // and jump. Items are listed in READING ORDER (row by row, left to right) — fixed by the
        // board, not by the cursor, so the same keys always land on the same places. Items are
        // recomputed on every press (the program changes); the index is kept and clamped. ----

        private sealed class CategoryItem
        {
            public int X = -1, Y = -1;  // a grid cell to move to, or -1
            public bool Zone;           // name the zone on landing even without a crossing
            public Action Run;          // instead of moving (palette slots, waldos)
        }

        private sealed class Category
        {
            public string Name;
            public Func<ReactorModel, Class77, List<CategoryItem>> Items;
        }

        private List<Category> _categories;
        private int _category = -1, _item = -1;

        private List<Category> Categories => _categories ?? (_categories = new List<Category>
        {
            new Category { Name = "reactor.cat.instructions", Items = PaletteItems },
            new Category { Name = "reactor.cat.inputs", Items = (r, e) => ZoneItems(r, input: true) },
            new Category { Name = "reactor.cat.outputs", Items = (r, e) => ZoneItems(r, input: false) },
            new Category { Name = "reactor.cat.hardware", Items = (r, e) => FeatureItems(r) },
            new Category { Name = "reactor.cat.waldos", Items = (r, e) => WaldoItems() },
        });

        private void StepCategory(int delta)
        {
            var r = Model;
            if (r == null) return;
            int n = Categories.Count;
            _category = _category < 0 ? (delta > 0 ? 0 : n - 1) : ((_category + delta) % n + n) % n;
            _item = -1;
            var items = Categories[_category].Items(r, Editor);
            Speech.Tts.Speak(Loc.T("reactor.cat", new { name = Loc.T(Categories[_category].Name), n = items.Count }), interrupt: true);
        }

        private void StepItem(int delta)
        {
            var r = Model;
            if (r == null) return;
            if (_category < 0) _category = 0;
            var items = Categories[_category].Items(r, Editor);
            if (items.Count == 0)
            {
                Speech.Tts.Speak(Loc.T("reactor.cat", new { name = Loc.T(Categories[_category].Name), n = 0 }), interrupt: true);
                return;
            }
            _item = _item < 0 ? (delta > 0 ? 0 : items.Count - 1) : ((_item + delta) % items.Count + items.Count) % items.Count;
            var item = items[_item];
            if (item.Run != null) { item.Run(); return; }
            if (item.Zone) _lastZone = null; // the landing names the zone
            if (item.X == _cursorX && item.Y == _cursorY && Equals(Navigation.FocusedNodeId, CellId(item.X, item.Y)))
            {
                LandOnCell(item.X, item.Y);
                Speech.Tts.Speak(CellReadout(item.X, item.Y), interrupt: true);
                return;
            }
            FocusCell(item.X, item.Y);
        }

        private List<CategoryItem> PaletteItems(ReactorModel r, Class77 editor)
        {
            var items = new List<CategoryItem>();
            var palette = editor?.class715_0;
            if (palette == null) return items;
            foreach (var kv in palette.dictionary_0)
            {
                if (!kv.Value.struct116_0.bool_0) continue;
                int key = (int)kv.Key;
                items.Add(new CategoryItem { Run = () => Arm(key, quiet: true) });
            }
            return items;
        }

        private static List<CategoryItem> ZoneItems(ReactorModel r, bool input)
        {
            var items = new List<CategoryItem>();
            var seen = new HashSet<string>();
            Vector2i size = r.method_1();
            for (int y = 0; y < size.int_1; y++)
                for (int x = 0; x < size.int_0; x++)
                {
                    string zone = ZoneAt(r, x, y);
                    if (zone == null || !seen.Add(zone)) continue;
                    bool isInput = zone == Loc.T("zone.alpha") || zone == Loc.T("zone.beta");
                    if (isInput == input) items.Add(new CategoryItem { X = x, Y = y, Zone = true });
                }
            return items;
        }

        private static List<CategoryItem> FeatureItems(ReactorModel r)
        {
            var items = new List<CategoryItem>();
            var seen = new HashSet<ReactorFeature>();
            Vector2i size = r.method_1();
            for (int y = 0; y < size.int_1; y++)
                for (int x = 0; x < size.int_0; x++)
                    if (r.method_15(new Vector2i(x, y), (Enum114)ReactorText.Background) is ReactorFeature f && seen.Add(f))
                        items.Add(new CategoryItem { X = x, Y = y });
            return items;
        }

        private List<CategoryItem> WaldoItems()
        {
            var items = new List<CategoryItem>();
            var r = Model;
            foreach (bool red in new[] { true, false })
            {
                Class188 waldo;
                if (r != null && r.dictionary_2.TryGetValue((Enum114)(red ? ReactorText.Red : ReactorText.Blue), out waldo) && waldo != null)
                {
                    bool isRed = red;
                    items.Add(new CategoryItem { Run = () => SpeakWaldo(isRed, jump: true) });
                }
            }
            return items;
        }
    }
}
