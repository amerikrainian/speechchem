using System;
using System.Collections.Generic;
using SpeechChem.Localization;
using SpeechChem.Narration;

namespace SpeechChem.Screens.Settings
{
    // ---- the page model: what the Mod tab shows, shared by the real widgets (Patches/
    // OptionsInjection) and the spoken screen (OptionsScreen), so both always agree. ----

    internal abstract class SRow
    {
        public string Id;
        public Func<string> Label;
        /// <summary>The spoken label when it differs from the drawn one (a compound row's first cell:
        /// the row's name is spoken as its context, so the cell says what it toggles).</summary>
        public Func<string> Spoken;

        public string SpokenLabel() => (Spoken ?? Label)();
    }

    /// <summary>A checkbox.</summary>
    internal sealed class SToggle : SRow
    {
        public Func<bool> Get;
        public Action<bool> Set;
        public Func<string> Note; // "(some)" on a group that is partly on
    }

    /// <summary>A combo box: a cycling button in the game's kit (the Language idiom).</summary>
    internal sealed class SChoice : SRow
    {
        public Func<string[]> Options;
        public Func<int> Get;
        public Action<int> Set;
    }

    /// <summary>A button opening a sub-page.</summary>
    internal sealed class SLink : SRow
    {
        public Func<SPage> Open;
    }

    /// <summary>A button that does something (reset, move).</summary>
    internal sealed class SAction : SRow
    {
        public Action Run;
    }

    /// <summary>Several controls on one line (a format part: on / detail / up / down).</summary>
    internal sealed class SCompound : SRow
    {
        public List<SRow> Cells = new List<SRow>();
        /// <summary>Drawn as "Label:" and its cells at their natural widths (one setting's
        /// options side by side) rather than in the format rows' fixed columns.</summary>
        public bool Inline;
    }

    internal sealed class SPage
    {
        public string Id;
        public Func<string> Title;
        public List<SRow> Rows = new List<SRow>();
    }

    /// <summary>
    /// The Settings dialog's mod side (user design 2026-10-04): tabs Game / Mod; the Mod tab has
    /// sub-tabs General, Events, Step keys; pages drill down with a Back; long pages are split into
    /// visual pages of <see cref="RowsPerPage"/> rows (the dialog's box is fixed — the spoken list
    /// is whole and the visual page follows focus). Everything edits the narration DRAFT
    /// (NarrationStore) and the typing-echo draft: Save Changes commits, Cancel discards — the
    /// game tab's semantics.
    /// </summary>
    internal static class ModSettingsUi
    {
        public enum Tab { Game, Mod }
        public enum ModTab { General, Events, Keys }

        public const int RowsPerPage = 4;

        public static Tab Shown = Tab.Game;
        public static ModTab ModShown = ModTab.General;
        public static int VisualPage;
        /// <summary>The page stack under the sub-tab's top page (empty = the top page).</summary>
        public static readonly List<Func<SPage>> Stack = new List<Func<SPage>>();
        /// <summary>For each stacked page, the row that opened it (Back lands there again).</summary>
        private static readonly List<string> Origins = new List<string>();
        /// <summary>Set when the widgets must be rebuilt (OptionsInjection runs it next update).</summary>
        public static bool Dirty;
        /// <summary>A row to focus after the next build (a page change lands on its first row).</summary>
        public static string FocusAfterBuild;

        private static bool? _typingEchoDraft;

        // ---- the dialog's lifetime ----

        public static void Open()
        {
            NarrationStore.BeginEdit();
            _typingEchoDraft = null;
        }

        public static void Save()
        {
            NarrationStore.Commit();
            if (_typingEchoDraft.HasValue) TypingEcho.Set(_typingEchoDraft.Value);
            _typingEchoDraft = null;
            Close();
        }

        public static void Cancel()
        {
            NarrationStore.Discard();
            _typingEchoDraft = null;
            Close();
        }

        private static void Close()
        {
            Shown = Tab.Game;
            Stack.Clear();
            Origins.Clear();
            VisualPage = 0;
        }

        // ---- navigation ----

        public static SPage Current() => Stack.Count > 0 ? Stack[Stack.Count - 1]() : Top(ModShown);

        public static void Push(Func<SPage> page, string origin)
        {
            Stack.Add(page);
            Origins.Add(origin);
            VisualPage = 0;
            FocusAfterBuild = FirstRowId(page());
            Dirty = true;
        }

        /// <summary>Back one level; false at a sub-tab's top page.</summary>
        public static bool Pop()
        {
            if (Stack.Count == 0) return false;
            Stack.RemoveAt(Stack.Count - 1);
            string origin = Origins[Origins.Count - 1];
            Origins.RemoveAt(Origins.Count - 1);
            var page = Current();
            int i = RowIndex(page, origin);
            VisualPage = i < 0 ? 0 : i / RowsPerPage;
            FocusAfterBuild = i < 0 ? FirstRowId(page) : origin;
            Dirty = true;
            return true;
        }

        public static void ShowTab(Tab tab)
        {
            if (tab == Shown) return;
            Shown = tab;
            Dirty = true;
        }

        public static void ShowModTab(ModTab tab)
        {
            if (tab == ModShown && Stack.Count == 0) return;
            ModShown = tab;
            Stack.Clear();
            Origins.Clear();
            VisualPage = 0;
            Dirty = true;
        }

        public static int PageCount(SPage page) => Math.Max(1, (page.Rows.Count + RowsPerPage - 1) / RowsPerPage);

        /// <summary>Show the visual page holding a row (the spoken list's focus moved there).</summary>
        public static void ShowRow(SPage page, string rowId)
        {
            int i = RowIndex(page, rowId);
            if (i < 0) return;
            int p = i / RowsPerPage;
            if (p == VisualPage) return;
            VisualPage = p;
            Dirty = true;
        }

        public static void TurnPage(int delta)
        {
            var page = Current();
            int n = PageCount(page);
            int p = Math.Max(0, Math.Min(n - 1, VisualPage + delta));
            if (p == VisualPage) return;
            VisualPage = p;
            FocusAfterBuild = page.Rows.Count > p * RowsPerPage ? page.Rows[p * RowsPerPage].Id : null;
            Dirty = true;
        }

        /// <summary>The index of the row holding a row or cell id, or -1.</summary>
        public static int RowIndex(SPage page, string id)
            => page.Rows.FindIndex(r => r.Id == id || (r is SCompound k && k.Cells.Exists(c => c.Id == id)));

        private static string FirstRowId(SPage page) => page.Rows.Count > 0 ? page.Rows[0].Id : null;

        // ---- labels ----

        public static string KindLabel(EventKind k) => k.Parent == null && !k.IsTag ? GroupLabel(k.Key) : Loc.T("narr.kind." + k.Key);
        public static string GroupLabel(string g) => Loc.T("narr.group." + g);
        public static string PartLabel(string part) => Loc.T("narr.part." + part);
        public static string VariantLabel(string v) => Loc.T("narr.variant." + v);
        public static string ModTabLabel(ModTab t) => Loc.T("settings.mod." + t.ToString().ToLowerInvariant());

        private static string[] Labels(string[] keys) => Array.ConvertAll(keys, k => Loc.T(k));

        // ---- pages ----

        public static SPage Top(ModTab tab)
        {
            switch (tab)
            {
                case ModTab.Events: return EventsPage();
                case ModTab.Keys: return KeysPage();
                default: return GeneralPage();
            }
        }

        private static SPage GeneralPage()
        {
            var page = new SPage { Id = "general", Title = () => ModTabLabel(ModTab.General) };
            page.Rows.Add(new SToggle
            {
                Id = "general.echo",
                Label = () => Loc.T("settings.typingEcho"),
                Get = () => _typingEchoDraft ?? TypingEcho.Enabled,
                Set = on => _typingEchoDraft = on,
            });
            page.Rows.Add(new SAction
            {
                Id = "general.resetall",
                Label = () => Loc.T("settings.resetAll"),
                Run = () =>
                {
                    NarrationStore.ResetDraft("event.");
                    NarrationStore.ResetDraft("fmt.");
                    NarrationStore.ResetDraft("step.");
                    Dirty = true;
                    Speech.Tts.Speak(Loc.T("settings.reset.done"), interrupt: true);
                },
            });
            return page;
        }

        private static SPage EventsPage()
        {
            var page = new SPage { Id = "events", Title = () => ModTabLabel(ModTab.Events) };
            foreach (var root in EventKinds.Roots)
            {
                var node = root;
                page.Rows.Add(new SLink { Id = "event." + node.Key, Label = () => KindLabel(node), Open = () => NodePage(node) });
            }
            return page;
        }

        private static string Mixed() => Loc.T("settings.some");

        /// <summary>A toggle over a node: a leaf's value; a branch's when all its leaves agree, else
        /// unticked with "(some)" (spoken "partly on") — ticking it sets the whole subtree.</summary>
        private static SToggle NodeToggle(string id, Func<string> label, EventKind node, Func<EventKind, bool> leafValue, Action<bool> set,
            Func<EventKind, bool> applies = null, Func<string> spoken = null)
            => new SToggle
            {
                Id = id, Label = label, Spoken = spoken,
                Get = () => EventSettings.Common(node, leafValue, applies) == true,
                Set = set,
                Note = () => EventSettings.Common(node, leafValue, applies) == null ? Loc.T("settings.key.some") : null,
            };

        /// <summary>
        /// One page for every node of the event tree (user design 2026-10-09: general to specific).
        /// The settings rows come first — on a branch or a group they apply to every event under it
        /// (a "(some)" / "Mixed" value says the events under it differ; setting it makes them all
        /// follow) — then, on a branch, its events and sub-branches as links, then the reset. A leaf
        /// has the rows the event pages always had; the scope row only where an event belongs to a
        /// reactor, the waldo rows only on waldo events (user choice: leave out what doesn't apply).
        /// A tag ("Failed or no effect") writes each member; it has no formats.
        /// </summary>
        private static SPage NodePage(EventKind node)
        {
            var page = new SPage { Id = "event." + node.Key, Title = () => KindLabel(node) };
            string id = "event." + node.Key + ".";
            page.Rows.Add(NodeToggle(id + "log", () => Loc.T("settings.event.log"), node, k => EventSettings.Log(k, draft: true), on => EventSettings.SetLog(node, on)));
            // One line of checkboxes (user rule 2026-10-04: one row to pass, not a list per speed).
            var speak = new SCompound { Id = id + "speak", Label = () => Loc.T("settings.event.speakAt"), Inline = true };
            foreach (var m in EventSettings.SpeakMoments)
            {
                string moment = m;
                bool idle = moment == "idle";
                speak.Cells.Add(NodeToggle(id + "speak." + moment,
                    () => idle ? Loc.T("settings.speak.idle") : moment, node,
                    k => EventSettings.SpeaksAt(k, moment, draft: true), on => EventSettings.SetSpeaksAt(node, moment, on),
                    spoken: () => idle ? Loc.T("settings.speak.idle") : Loc.T("settings.speak.speed", new { n = moment })));
            }
            page.Rows.Add(speak);
            if (node.ReactorScoped)
                page.Rows.Add(new SChoice
                {
                    Id = id + "scope", Label = () => Loc.T("settings.event.scope"),
                    Options = () => ScopeCommon(node) == null
                        ? new[] { Loc.T("settings.scope.open"), Loc.T("settings.scope.all"), Mixed() }
                        : Labels(new[] { "settings.scope.open", "settings.scope.all" }),
                    Get = () =>
                    {
                        string common = ScopeCommon(node);
                        return common == null ? 2 : common == EventSettings.ScopeAll ? 1 : 0;
                    },
                    Set = i => { if (i < 2) EventSettings.SetScope(node, i == 1 ? EventSettings.ScopeAll : EventSettings.ScopeOpen); },
                });
            if (node.Waldo)
                foreach (int colour in new[] { 0, 1 })
                {
                    int c = colour;
                    page.Rows.Add(NodeToggle(id + (c == 0 ? "red" : "blue"), () => Loc.T(c == 0 ? "settings.event.red" : "settings.event.blue"), node,
                        k => EventSettings.Source(k, c, draft: true), on => EventSettings.SetSource(node, c, on), applies: k => k.Waldo));
                }
            if (!node.IsTag)
                foreach (var layer in new[] { FormatLayer.Default, FormatLayer.Log, FormatLayer.Speech })
                {
                    var l = layer;
                    page.Rows.Add(new SLink { Id = id + "fmt." + l, Label = () => FormatTitle(l), Open = () => FormatPage(node, l) });
                }
            var below = node.IsTag ? node.Members : node.Children;
            foreach (var c in below)
            {
                var child = c;
                page.Rows.Add(new SLink { Id = "event." + child.Key, Label = () => KindLabel(child), Open = () => NodePage(child) });
            }
            page.Rows.Add(new SAction
            {
                Id = id + "reset", Label = () => Loc.T(node.IsLeaf ? "settings.event.reset" : "settings.event.resetGroup"),
                Run = () =>
                {
                    EventSettings.Reset(node);
                    Dirty = true;
                    Speech.Tts.Speak(Loc.T("settings.reset.done"), interrupt: true);
                },
            });
            return page;
        }

        private static string ScopeCommon(EventKind node)
            => EventSettings.CommonText(node, k => EventSettings.Scope(k, draft: true), k => k.ReactorScoped);

        private static string FormatTitle(FormatLayer layer)
            => Loc.T(layer == FormatLayer.Log ? "settings.event.logFormat" : layer == FormatLayer.Speech ? "settings.event.speechFormat" : "settings.event.format");

        /// <summary>The parts in the layer's order: each a line of on / detail / Move up / Move down.</summary>
        private static SPage FormatPage(EventKind kind, FormatLayer layer)
        {
            var page = new SPage { Id = "fmt." + layer + "." + kind.Key, Title = () => FormatTitle(layer) };
            var order = EventSettings.Order(kind, layer, draft: true);
            foreach (var key in order)
            {
                var part = Array.Find(kind.Parts, p => p.Key == key);
                if (part == null) continue;
                string rowId = page.Id + "." + key;
                var row = new SCompound { Id = rowId, Label = () => PartLabel(part.Key) };
                Func<EventKind, bool> has = k => Array.Exists(k.Parts, q => q.Key == part.Key);
                row.Cells.Add(NodeToggle(rowId + ".on", () => PartLabel(part.Key), kind,
                    k => EventSettings.PartOn(k, layer, part.Key, draft: true), on => EventSettings.SetPartOn(kind, layer, part.Key, on),
                    applies: has, spoken: () => Loc.T("settings.format.include")));
                if (part.Variants != null)
                {
                    Func<string> common = () => EventSettings.CommonText(kind, k => EventSettings.Variant(k, layer, part, draft: true), has);
                    row.Cells.Add(new SChoice
                    {
                        Id = rowId + ".variant", Label = () => Loc.T("settings.format.detail"),
                        Options = () =>
                        {
                            var labels = new List<string>(Array.ConvertAll(part.Variants, VariantLabel));
                            if (common() == null) labels.Add(Mixed());
                            return labels.ToArray();
                        },
                        Get = () =>
                        {
                            string v = common();
                            return v == null ? part.Variants.Length : Array.IndexOf(part.Variants, v);
                        },
                        Set = i => { if (i < part.Variants.Length) EventSettings.SetVariant(kind, layer, part, part.Variants[i]); },
                    });
                }
                row.Cells.Add(new SAction { Id = rowId + ".up", Label = () => Loc.T("settings.format.up"), Run = () => Move(kind, layer, part.Key, -1, rowId) });
                row.Cells.Add(new SAction { Id = rowId + ".down", Label = () => Loc.T("settings.format.down"), Run = () => Move(kind, layer, part.Key, 1, rowId) });
                page.Rows.Add(row);
            }
            page.Rows.Add(new SAction
            {
                Id = page.Id + ".reset",
                Label = () => Loc.T(layer == FormatLayer.Default ? "settings.format.reset" : "settings.format.inherit"),
                Run = () =>
                {
                    EventSettings.ResetFormat(kind, layer);
                    Dirty = true;
                    Speech.Tts.Speak(Loc.T("settings.reset.done"), interrupt: true);
                },
            });
            return page;
        }

        /// <summary>Move a part up or down; say where it landed (say-the-spire2's feedback).</summary>
        private static void Move(EventKind kind, FormatLayer layer, string part, int delta, string rowId)
        {
            var order = EventSettings.Order(kind, layer, draft: true);
            int i = order.IndexOf(part), j = i + delta;
            if (i < 0 || j < 0 || j >= order.Count)
            {
                Speech.Tts.Speak(Loc.T(delta < 0 ? "settings.format.first" : "settings.format.last"), interrupt: true);
                return;
            }
            order.RemoveAt(i);
            order.Insert(j, part);
            EventSettings.SetOrder(kind, layer, order);
            string before = j > 0 ? PartLabel(order[j - 1]) : null, after = j + 1 < order.Count ? PartLabel(order[j + 1]) : null;
            Speech.Tts.Speak(before != null && after != null ? Loc.T("settings.format.between", new { a = before, b = after })
                : before != null ? Loc.T("settings.format.after", new { a = before }) : Loc.T("settings.format.before", new { b = after }), interrupt: true);
            FocusAfterBuild = rowId + (delta < 0 ? ".up" : ".down");
            var page = Current();
            int idx = page.Rows.FindIndex(r => r.Id == rowId);
            if (idx >= 0) VisualPage = idx / RowsPerPage;
            Dirty = true;
        }

        // ---- step keys ----

        private static SPage KeysPage()
        {
            var page = new SPage { Id = "keys", Title = () => ModTabLabel(ModTab.Keys) };
            foreach (var k in StepKeys.Ids)
            {
                string id = k;
                page.Rows.Add(new SLink
                {
                    Id = "keys." + id,
                    Label = () => StepKeys.Assigned(id, draft: true) ? StepKeys.Label(id) : Loc.T("settings.key.unassigned", new { key = StepKeys.Label(id) }),
                    Open = () => KeyPage(id),
                });
            }
            return page;
        }

        private static SPage KeyPage(string id)
        {
            var page = new SPage { Id = "key." + id, Title = () => Loc.T("settings.key.title", new { key = StepKeys.Label(id) }) };
            string rid = "key." + id + ".";
            page.Rows.Add(new SToggle { Id = rid + "assigned", Label = () => Loc.T("settings.key.assigned"), Get = () => StepKeys.Assigned(id, draft: true), Set = on => StepKeys.SetAssigned(id, on) });
            page.Rows.Add(new SChoice
            {
                Id = rid + "scope", Label = () => Loc.T("settings.key.scope"),
                Options = () => Labels(new[] { "settings.scope.open", "settings.scope.all" }),
                Get = () => StepKeys.Scope(id, draft: true) == EventSettings.ScopeAll ? 1 : 0,
                Set = i => StepKeys.SetScope(id, i == 1 ? EventSettings.ScopeAll : EventSettings.ScopeOpen),
            });
            foreach (var which in new[] { "stops", "speaks" })
                foreach (var root in EventKinds.Roots)
                {
                    var r = root;
                    page.Rows.Add(NodeRow(id, which, r,
                        () => Loc.T(which == "stops" ? "settings.key.stops" : "settings.key.speaks", new { group = GroupLabel(r.Key) })));
                }
            page.Rows.Add(new SToggle { Id = rid + "cycle", Label = () => Loc.T("settings.key.cycle"), Get = () => StepKeys.SayCycle(id, draft: true), Set = on => StepKeys.SetSayCycle(id, on) });
            page.Rows.Add(new SChoice
            {
                Id = rid + "giveup", Label = () => Loc.T("settings.key.giveup"),
                Options = () => Array.ConvertAll(StepKeys.GiveUpChoices, n => Loc.T("settings.key.cycles", new { n })),
                Get = () => Array.IndexOf(StepKeys.GiveUpChoices, StepKeys.GiveUp(id, draft: true)),
                Set = i => StepKeys.SetGiveUp(id, StepKeys.GiveUpChoices[i]),
            });
            page.Rows.Add(new SAction
            {
                Id = rid + "reset", Label = () => Loc.T("settings.key.reset"),
                Run = () =>
                {
                    NarrationStore.ResetDraft(StepKeys.Prefix(id));
                    Dirty = true;
                    Speech.Tts.Speak(Loc.T("settings.reset.done"), interrupt: true);
                },
            });
            return page;
        }

        private static bool Get(string id, string which, EventKind k)
            => which == "stops" ? StepKeys.Stops(id, k, draft: true) : StepKeys.Speaks(id, k, draft: true);

        private static void Put(string id, string which, EventKind k, bool on)
        {
            if (which == "stops") StepKeys.SetStops(id, k, on); else StepKeys.SetSpeaks(id, k, on);
        }

        /// <summary>"Stops on waldo actions" — on when every event under the node is, "(some)" when
        /// part; ticking sets the whole subtree; Customize lists the node's branches (each a row
        /// like this one), events and tags, down to every single event.</summary>
        private static SRow NodeRow(string id, string which, EventKind node, Func<string> label)
        {
            string rowId = "key." + id + "." + which + "." + node.Key;
            var toggle = NodeToggle(node.IsLeaf ? rowId : rowId + ".all", label, node, k => Get(id, which, k), on => { Put(id, which, node, on); Dirty = true; },
                spoken: node.IsLeaf ? null : (Func<string>)(() => Loc.T("settings.key.all")));
            if (node.IsLeaf) return toggle;
            var row = new SCompound { Id = rowId, Label = label };
            row.Cells.Add(toggle);
            row.Cells.Add(new SLink
            {
                Id = rowId + ".customize", Label = () => Loc.T("settings.key.customize"),
                Open = () =>
                {
                    var page = new SPage { Id = rowId + ".page", Title = label };
                    foreach (var c in node.IsTag ? node.Members : node.Children)
                    {
                        var child = c;
                        page.Rows.Add(NodeRow(id, which, child, () => KindLabel(child)));
                    }
                    return page;
                },
            });
            return row;
        }
    }
}
