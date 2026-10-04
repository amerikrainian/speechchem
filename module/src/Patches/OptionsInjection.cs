using System;
using System.Collections.Generic;
using HarmonyLib;
using Impeller;
using SpaceChem;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.Screens.Settings;

namespace SpeechChem.Patches
{
    /// <summary>
    /// The game's Settings dialog (deob Class74, a TitleScreenEditor) with the mod's settings in it,
    /// as REAL game widgets (user design 2026-10-04): a postfix on its widget-tree builder
    /// (vmethod_8) rebuilds the tree as tab buttons (Game, Mod), the Mod tab's sub-tabs (General,
    /// Events, Step keys), the shown page's rows, and a bottom line of Back / Previous / Next with
    /// the dialog's own Save Changes and Cancel. Rows (Screens/Settings/ModSettingsUi): toggles are
    /// the game's checkboxes (Class475), choices cycling buttons (the Language idiom), links and
    /// actions buttons. The Game tab is the game's own layout, widget for widget.
    /// Any change of what is shown rebuilds the dialog (TitleScreenEditor.vmethod_5), deferred to
    /// the next update — never inside a click; the game tab's unsaved values are carried across it
    /// (the rebuild re-reads config.ini). A checkbox clicked with the mouse is noticed by polling.
    /// Save Changes (Class74.method_19) commits the mod's draft, Cancel (method_20, also Escape)
    /// discards it.
    /// </summary>
    internal static class OptionsInjection
    {
        private static Class74 _open;
        private static GameValues _carry;
        private static readonly List<KeyValuePair<Class475, SToggle>> Boxes = new List<KeyValuePair<Class475, SToggle>>();

        public static void Apply(Harmony harmony)
        {
            try
            {
                var self = typeof(OptionsInjection);
                harmony.Patch(Expr.OverrideOf(typeof(Class74), Expr.MethodOf(() => default(TitleScreenEditor).vmethod_8())),
                    postfix: new HarmonyMethod(self, nameof(AfterBuild)));
                harmony.Patch(Expr.MethodOf(() => default(Class74).method_19()), prefix: new HarmonyMethod(self, nameof(BeforeSave)));
                harmony.Patch(Expr.MethodOf(() => default(Class74).method_20()), prefix: new HarmonyMethod(self, nameof(BeforeCancel)));
                Log.Info("[patch] options injection armed");
            }
            catch (Exception ex) { Log.Error("[patch] options injection failed to apply", ex); }
        }

        // The mod's draft goes with the dialog's own buttons (prefixes: method_19 may restart the
        // game on a language change before returning).
        private static void BeforeSave() { try { ModSettingsUi.Save(); _open = null; } catch (Exception ex) { Log.Error("[options] save", ex); } }
        private static void BeforeCancel() { try { ModSettingsUi.Cancel(); _open = null; } catch (Exception ex) { Log.Error("[options] cancel", ex); } }

        /// <summary>Per update while the dialog is up (OptionsScreen.OnUpdate): notice mouse-clicked
        /// checkboxes, and run a pending rebuild.</summary>
        public static void Update(Class74 dialog)
        {
            if (dialog == null) return;
            try
            {
                // A pending rebuild already carries the model's values (a spoken change sets Dirty
                // with the change; the boxes on screen still show the old state until it runs).
                if (!ModSettingsUi.Dirty)
                    foreach (var kv in Boxes)
                    {
                        bool on = (int)kv.Key.method_7() == 0;
                        if (on != kv.Value.Get()) { kv.Value.Set(on); ModSettingsUi.Dirty = true; }
                    }
                if (!ModSettingsUi.Dirty) return;
                ModSettingsUi.Dirty = false;
                _carry = GameValues.Read(dialog);
                dialog.vmethod_5();
            }
            catch (Exception ex) { Log.Error("[options] update", ex); }
        }

        private static void AfterBuild(Class74 __instance, ref Class226 __result)
        {
            try
            {
                var d = __instance;
                if (!ReferenceEquals(_open, d)) { _open = d; ModSettingsUi.Open(); }
                if (_carry != null) { _carry.Apply(d); _carry = null; }
                Boxes.Clear();

                var lines = new List<GClass10>
                {
                    Row(Button(Loc.T("settings.tab.game"), 110, () => ModSettingsUi.ShowTab(ModSettingsUi.Tab.Game)),
                        Button(Loc.T("settings.tab.mod"), 110, () => ModSettingsUi.ShowTab(ModSettingsUi.Tab.Mod))),
                };
                var bottom = new List<GClass10>();
                if (ModSettingsUi.Shown == ModSettingsUi.Tab.Game) lines.Add(GameBody(d));
                else
                {
                    lines.Add(Row(Button(ModSettingsUi.ModTabLabel(ModSettingsUi.ModTab.General), 120, () => ModSettingsUi.ShowModTab(ModSettingsUi.ModTab.General)),
                        Button(ModSettingsUi.ModTabLabel(ModSettingsUi.ModTab.Events), 120, () => ModSettingsUi.ShowModTab(ModSettingsUi.ModTab.Events)),
                        Button(ModSettingsUi.ModTabLabel(ModSettingsUi.ModTab.Keys), 120, () => ModSettingsUi.ShowModTab(ModSettingsUi.ModTab.Keys))));
                    var page = ModSettingsUi.Current();
                    int pages = ModSettingsUi.PageCount(page);
                    if (ModSettingsUi.VisualPage >= pages) ModSettingsUi.VisualPage = pages - 1;
                    var rows = new List<GClass10> { (GClass10)Scene.smethod_4(Class234.extendedFont_9, Breadcrumb(page), Class203.struct104_0) };
                    int from = ModSettingsUi.VisualPage * ModSettingsUi.RowsPerPage;
                    for (int i = from; i < Math.Min(page.Rows.Count, from + ModSettingsUi.RowsPerPage); i++) rows.Add(Widget(page.Rows[i], 360));
                    lines.Add(GClass10.smethod_3((Enum100)0, 2, rows));
                    if (ModSettingsUi.Stack.Count > 0) bottom.Add(Button(Loc.T("settings.back"), 70, () => ModSettingsUi.Pop()));
                    if (pages > 1)
                    {
                        bottom.Add(Button(Loc.T("settings.prev"), 60, () => ModSettingsUi.TurnPage(-1)));
                        bottom.Add(Text(Loc.T("settings.page", new { n = ModSettingsUi.VisualPage + 1, m = pages })));
                        bottom.Add(Button(Loc.T("settings.next"), 60, () => ModSettingsUi.TurnPage(1)));
                    }
                }
                bottom.Add(Button(GameText.T("Save Changes"), ModSettingsUi.Shown == ModSettingsUi.Tab.Game ? 225 : 160, d.method_19));
                bottom.Add(Button(GameText.T("Cancel"), ModSettingsUi.Shown == ModSettingsUi.Tab.Game ? 150 : 90, d.method_20));
                lines.Add(GClass10.smethod_2((Enum119)1, ModSettingsUi.Shown == ModSettingsUi.Tab.Game ? 15 : 6, bottom));
                __result = new Class226(GClass10.smethod_3((Enum100)1, ModSettingsUi.Shown == ModSettingsUi.Tab.Game ? 6 : 3, lines), GameText.T("Settings"), __result.struct116_0);
            }
            catch (Exception ex) { Log.Error("[options] build failed", ex); }
        }

        // ---- widgets ----

        private const int Height = 26;

        private static GClass10 Button(string text, int width, Action action) => Class203.smethod_4(text, new Vector2i(width, Height), action);

        /// <summary>A widget left-aligned in a fixed box (columns line up across rows).</summary>
        private static GClass10 Fixed(GClass10 widget, int width)
        {
            var size = widget.vmethod_1().vector2i_1;
            return new CompositeWidget(new[]
            {
                new WidgetPosition(Vector2i.vector2i_0, new Class473(Scene.smethod_11(new Rectangle(0, 0, width, Math.Max(Height, size.int_1))))),
                new WidgetPosition(new Vector2i(0, Math.Max(0, (Math.Max(Height, size.int_1) - size.int_1) / 2)), widget),
            });
        }

        private static GClass10 Text(string text) => (GClass10)Scene.smethod_4(Class234.extendedFont_6, text, Class203.struct104_0);

        /// <summary>Where the page is: "Events, Waldo actions, Grabbed".</summary>
        private static string Breadcrumb(SPage page)
        {
            var parts = new List<string> { ModSettingsUi.ModTabLabel(ModSettingsUi.ModShown) };
            for (int i = 0; i < ModSettingsUi.Stack.Count; i++) parts.Add(ModSettingsUi.Stack[i]().Title());
            return string.Join(", ", parts.ToArray());
        }

        private static GClass10 Row(params GClass10[] cells) => GClass10.smethod_2((Enum119)1, 8, cells);

        private static GClass10 Widget(SRow row, int width)
        {
            switch (row)
            {
                case SToggle t:
                {
                    string label = t.Label();
                    string note = t.Note?.Invoke();
                    var box = new Class475(Scene.smethod_4(Class234.extendedFont_6, note == null ? label : label + " " + note, Class203.struct104_0),
                        t.Get() ? (Enum106)0 : (Enum106)1);
                    Boxes.Add(new KeyValuePair<Class475, SToggle>(box, t));
                    return box;
                }
                case SChoice c:
                {
                    var options = c.Options();
                    int i = c.Get();
                    string value = i >= 0 && i < options.Length ? options[i] : "";
                    return Button(c.Label() + ": " + value, width, () =>
                    {
                        int n = options.Length;
                        if (n == 0) return;
                        c.Set(((c.Get() + 1) % n + n) % n);
                        ModSettingsUi.Dirty = true;
                    });
                }
                case SLink l: return Button(l.Label(), width, () => ModSettingsUi.Push(l.Open, l.Id));
                case SAction a: return Button(a.Label(), width, a.Run);
                case SCompound k when k.Inline:
                {
                    var cells = new List<GClass10> { Text(k.Label() + ":") };
                    foreach (var cell in k.Cells) cells.Add(Widget(cell, 0));
                    return GClass10.smethod_2((Enum119)1, 10, cells);
                }
                case SCompound k:
                {
                    var cells = new List<GClass10>();
                    // Fixed columns, so the same control sits at the same place on every row; a row
                    // without a detail choice keeps its column empty.
                    bool hasChoice = k.Cells.Exists(c => c is SChoice);
                    // Format rows (checkbox, detail, Up, Down) keep a narrow checkbox column; a
                    // checkbox with only a link after it (a step key's group) gets the line's room.
                    int boxWidth = hasChoice || k.Cells.Exists(c => c is SAction) ? 150 : 330;
                    foreach (var cell in k.Cells)
                    {
                        if (cell is SToggle)
                        {
                            cells.Add(Fixed(Widget(cell, 0), boxWidth));
                            if (!hasChoice && k.Cells.Exists(c => c is SAction)) cells.Add(Fixed((GClass10)Scene.scene_0, 170));
                        }
                        else if (cell is SChoice c2) cells.Add(ValueButton(c2, 170));
                        else cells.Add(Widget(cell, cell is SLink ? 110 : 60));
                    }
                    return GClass10.smethod_2((Enum119)1, 6, cells);
                }
            }
            return (GClass10)Scene.scene_0;
        }

        /// <summary>A choice inside a compound row shows only its value (its label is spoken).</summary>
        private static GClass10 ValueButton(SChoice c, int width)
        {
            var options = c.Options();
            int i = c.Get();
            return Button(i >= 0 && i < options.Length ? options[i] : "", width, () =>
            {
                int n = options.Length;
                if (n == 0) return;
                c.Set(((c.Get() + 1) % n + n) % n);
                ModSettingsUi.Dirty = true;
            });
        }

        /// <summary>The game's own settings column (Class74.vmethod_8), over the dialog's widgets.</summary>
        private static GClass10 GameBody(Class74 d)
        {
            Func<string, Scene> label = s => Scene.smethod_4(Class234.extendedFont_6, Class323.smethod_0(s), Class203.struct104_0);
            return GClass10.smethod_3((Enum100)0, 7, new GClass10[]
            {
                GClass10.smethod_2((Enum119)2, 47, new GClass10[]
                {
                    GClass10.smethod_3((Enum100)0, 0, new GClass10[] { d.class475_0, d.class475_1 }),
                    GClass10.smethod_3((Enum100)1, 5, new GClass10[] { (GClass10)label("Language"), d.gclass15_0 }),
                }),
                GClass10.smethod_1(0, 0),
                d.class478_0.method_7(label("Music volume")),
                d.class478_1.method_7(label("Sound volume")),
                d.class475_2,
            });
        }

        /// <summary>The game tab's values as the player left them (Class74.method_19 reads these).</summary>
        private sealed class GameValues
        {
            private Enum106 _full, _aspect, _priority;
            private float _music, _sound;
            private int _language;

            public static GameValues Read(Class74 d) => new GameValues
            {
                _full = d.class475_0.method_7(), _aspect = d.class475_1.method_7(), _priority = d.class475_2.method_7(),
                _music = d.class478_0.method_8(), _sound = d.class478_1.method_8(), _language = d.int_0,
            };

            public void Apply(Class74 d)
            {
                d.class475_0.method_8(_full);
                d.class475_1.method_8(_aspect);
                d.class475_2.method_8(_priority);
                d.class478_0.method_9(_music);
                d.class478_1.method_9(_sound);
                if (_language >= 0 && _language < d.list_0.Count)
                {
                    d.int_0 = _language;
                    Class203.smethod_6(d.gclass15_0, Class74.smethod_11(d.list_0[d.int_0]));
                }
            }
        }
    }
}
