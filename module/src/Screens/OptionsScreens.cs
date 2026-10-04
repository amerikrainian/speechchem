using System;
using System.Collections.Generic;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.Screens.Settings;
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

        private const string TabStop = "options.tabs", BodyStop = "options.body", ModTabStop = "options.modtabs",
            PageStop = "options.page", ButtonStop = "options.buttons";
        private const string RowPrefix = "options.mod.";

        public override object InitialFocusStop => TabStop; // user rule 2026-10-04: opening lands on the Game / Mod tabs

        /// <summary>A sub-page of the Mod tab: Escape goes back a page instead of cancelling.</summary>
        public override bool ModalCapturesEscape => ModSettingsUi.Shown == ModSettingsUi.Tab.Mod && ModSettingsUi.Stack.Count > 0;

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, () => { if (ModSettingsUi.Shown == ModSettingsUi.Tab.Mod) Back(); });
        }

        /// <summary>A tab switch's rebuild runs here, on the next update after the click
        /// (Patches/OptionsInjection); then focus follows a page change, and the drawn page
        /// follows focus (only RowsPerPage rows fit the dialog; the spoken list is whole).</summary>
        public override void OnUpdate()
        {
            Patches.OptionsInjection.Update(GameApi.TopScreen() as Class74);
            if (ModSettingsUi.Shown != ModSettingsUi.Tab.Mod) return;
            if (ModSettingsUi.FocusAfterBuild != null)
            {
                // A compound row is no node itself: its first cell is.
                var page = ModSettingsUi.Current();
                int i = ModSettingsUi.RowIndex(page, ModSettingsUi.FocusAfterBuild);
                string id = i >= 0 && page.Rows[i] is SCompound k && k.Id == ModSettingsUi.FocusAfterBuild ? k.Cells[0].Id : ModSettingsUi.FocusAfterBuild;
                Navigation.FocusNode(RowNode(id));
                ModSettingsUi.FocusAfterBuild = null;
                return;
            }
            string key = Navigation.FocusedNodeId?.StructuralKey as string;
            if (key != null && key.StartsWith(RowPrefix, StringComparison.Ordinal))
                ModSettingsUi.ShowRow(ModSettingsUi.Current(), key.Substring(RowPrefix.Length));
        }

        public override bool IsActive() => Dialog != null;

        public override void Build(GraphBuilder b)
        {
            var d = Dialog;
            if (d == null || d.class475_0 == null) return;

            b.BeginStop(TabStop);
            TabNode(b, "options.tab.game", () => Loc.T("settings.tab.game"), () => ModSettingsUi.Shown == ModSettingsUi.Tab.Game,
                () => ModSettingsUi.ShowTab(ModSettingsUi.Tab.Game));
            TabNode(b, "options.tab.mod", () => Loc.T("settings.tab.mod"), () => ModSettingsUi.Shown == ModSettingsUi.Tab.Mod,
                () => ModSettingsUi.ShowTab(ModSettingsUi.Tab.Mod));

            if (ModSettingsUi.Shown == ModSettingsUi.Tab.Mod) { BuildMod(b); return; }

            b.BeginStop(BodyStop);
            Toggle(b, "options.fullscreen", "Fullscreen", () => Dialog?.class475_0);
            Toggle(b, "options.aspect", "Keep Aspect Ratio", () => Dialog?.class475_1);
            Language(b, this);
            Slider(b, "options.music", "Music volume", () => Dialog?.class478_0, playSample: false);
            Slider(b, "options.sound", "Sound volume", () => Dialog?.class478_1, playSample: true);
            Toggle(b, "options.priority", "Show Bonder Priority", () => Dialog?.class475_2);
            Plain(b, ProfileUi.Button(() => GameText.T("Save Changes"), () => Dialog?.method_19()), "options.save");
            Plain(b, ProfileUi.Button(() => GameText.T("Cancel"), () => Dialog?.method_20()), "options.cancel");
        }

        /// <summary>A tab of a strip: selection follows focus, "selected" is never spoken.</summary>
        private static void TabNode(GraphBuilder b, string id, Func<string> label, Func<bool> selected, Action select)
        {
            b.AddItem(ControlId.Structural(id), new NodeVtable
            {
                ControlType = ControlTypes.Tab,
                Announcements = new[] { new NodeAnnouncement(label, kind: AnnouncementKinds.Label) },
                Selected = selected,
                OnSelect = select,
                OnActivate = select,
            });
        }

        // ---- the Mod tab (Screens/Settings/ModSettingsUi: the same page model the widgets draw) ----

        private void BuildMod(GraphBuilder b)
        {
            b.BeginStop(ModTabStop);
            foreach (ModSettingsUi.ModTab t in Enum.GetValues(typeof(ModSettingsUi.ModTab)))
            {
                var tab = t;
                TabNode(b, "options.modtab." + tab, () => ModSettingsUi.ModTabLabel(tab), () => ModSettingsUi.ModShown == tab,
                    () => ModSettingsUi.ShowModTab(tab));
            }

            b.BeginStop(PageStop);
            var page = ModSettingsUi.Current();
            // A sub-page names itself on entry; a top page is named by its sub-tab already.
            bool nested = ModSettingsUi.Stack.Count > 0;
            if (nested) b.PushContext(page.Title(), id: ControlId.Structural("options.pagectx." + page.Id));
            // Positions are the page's own (the builder counts only single-control rows): a
            // compound row counts once, on its first cell.
            int count = page.Rows.Count;
            for (int r = 0; r < count; r++)
            {
                var row = page.Rows[r];
                int index = r + 1;
                if (row is SCompound k)
                {
                    // The row's name is its context (spoken when focus enters the row); its cells are
                    // one line, the same columns on every row, Up/Down keeping the column.
                    b.PushContext(k.Label(), positions: false, id: ControlId.Structural("options.rowctx." + k.Id));
                    b.StartRow(k.Inline ? "options.inline." + k.Id : "options.compound");
                    for (int c = 0; c < k.Cells.Count; c++)
                    {
                        var cell = k.Cells[c];
                        var vt = Cell(cell);
                        if (!k.Inline) vt.Column = CellColumn(cell);
                        if (c == 0) AddPosition(vt, index, count);
                        b.AddItem(RowNode(cell.Id), vt);
                    }
                    b.EndRow();
                    b.PopContext();
                }
                else
                {
                    var vt = Cell(row);
                    AddPosition(vt, index, count);
                    b.AddItem(RowNode(row.Id), vt);
                }
            }
            if (nested) b.PopContext();

            b.BeginStop(ButtonStop);
            if (nested) Plain(b, ProfileUi.Button(() => Loc.T("settings.back"), Back), "options.back");
            Plain(b, ProfileUi.Button(() => GameText.T("Save Changes"), () => Dialog?.method_19()), "options.modsave");
            Plain(b, ProfileUi.Button(() => GameText.T("Cancel"), () => Dialog?.method_20()), "options.modcancel");
        }

        private static void AddPosition(NodeVtable vt, int index, int count)
        {
            var parts = new List<NodeAnnouncement>(vt.Announcements);
            parts.Add(new NodeAnnouncement(() => Loc.T("nav.position", new { index, count }), kind: AnnouncementKinds.Position));
            vt.Announcements = parts;
        }

        private static ControlId RowNode(string rowId) => ControlId.Structural(RowPrefix + rowId);

        /// <summary>A compound row's logical columns, the drawn ones: checkbox, choice, then
        /// buttons — a row without a choice leaves its column out, so Up/Down keep the column.</summary>
        private static int CellColumn(SRow cell)
        {
            if (cell is SToggle) return 0;
            if (cell is SChoice) return 1;
            string id = cell.Id;
            return id.EndsWith(".down", StringComparison.Ordinal) ? 3 : 2;
        }

        private static void Back()
        {
            if (ModSettingsUi.Pop()) Click();
        }

        private static void Click()
        {
            try { Class428.class14_4.vmethod_0(); } catch { }
        }

        /// <summary>One settings control as a node: checkbox, combo box (the user's combo spec:
        /// Enter opens the choices on the current one), or button.</summary>
        private NodeVtable Cell(SRow row)
        {
            NodeVtable vt;
            switch (row)
            {
                case SToggle t:
                {
                    Func<string> state = () =>
                    {
                        // A group partly on: drawn unticked with "(some)", spoken "partly on".
                        if (t.Get()) return Loc.T("value.on");
                        return Loc.T(t.Note?.Invoke() != null ? "settings.partly" : "value.off");
                    };
                    vt = new NodeVtable
                    {
                        ControlType = ControlTypes.Toggle,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(t.SpokenLabel, kind: AnnouncementKinds.Label),
                            new NodeAnnouncement(state, kind: AnnouncementKinds.Value),
                        },
                        StateText = state,
                        OnActivate = () => { t.Set(!t.Get()); ModSettingsUi.Dirty = true; Click(); },
                    };
                    break;
                }
                case SChoice c:
                {
                    Func<string> value = () => { var o = c.Options(); int i = c.Get(); return i >= 0 && i < o.Length ? o[i] : null; };
                    vt = new NodeVtable
                    {
                        ControlType = ControlTypes.ComboBox,
                        Announcements = new[]
                        {
                            new NodeAnnouncement(c.SpokenLabel, kind: AnnouncementKinds.Label),
                            new NodeAnnouncement(value, kind: AnnouncementKinds.Value),
                        },
                        OnActivate = () => PushChild(new ChoiceListScreen("options.choice." + c.Id, () => c.Options(), c.Get(), i =>
                        {
                            Click();
                            c.Set(i);
                            ModSettingsUi.Dirty = true;
                        })),
                    };
                    break;
                }
                case SLink l:
                    vt = ProfileUi.Button(l.SpokenLabel, () => ModSettingsUi.Push(l.Open, l.Id));
                    break;
                case SAction a:
                    vt = ProfileUi.Button(a.SpokenLabel, a.Run);
                    break;
                default:
                    vt = new NodeVtable { Announcements = new[] { new NodeAnnouncement(row.SpokenLabel) } };
                    break;
            }
            vt.SpeaksOwnPosition = true;
            return vt;
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
