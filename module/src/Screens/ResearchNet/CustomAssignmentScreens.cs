using System;
using System.Collections.Generic;
using System.Linq;
using SpaceChem;
using SpaceChem.ResearchNet;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;
using SpeechChem.UI.Graph;
using LevelsDb = SpaceChem.Levels.Levels;

namespace SpeechChem.Screens.ResearchNet
{
    /// <summary>
    /// SpaceChem.ResearchNetCustomEditor, "Custom Assignments" (a Class59 paged ResearchNet screen
    /// opened by the journal's "Create or Import an Assignment"): the profile's own assignments
    /// (Class429 rows of the save's ResearchNet table, oldest first, 12 cards a page), New Research /
    /// New Production / New Sandbox, then Edit / Play / Duplicate / Delete, Import / Export, Back,
    /// Previous / Next. The game is select-then-act: a click on a card selects it
    /// (CustomLevelWidget.bool_2), the buttons act on the selection (method_20), a double click plays.
    ///
    /// ONE LIST, THE PAGE FOLLOWS (the journal's rule): the Assignments stop holds every assignment;
    /// focus turns the game's page (int_2) to the focused one's and SELECTS its card, exactly as the
    /// click does (method_34 re-enables the buttons), so the action buttons act on what was last
    /// focused. An assignment is a row: "name, button, author, kind[, completed], n of m", then the
    /// molecules its card draws as cells (the journal's previews; Enter opens the molecule viewer);
    /// Shift+Backspace speaks them as one line. Enter = Play (the double click); Backspace = the
    /// actions as a menu (Edit, Play, Duplicate, Delete, Export — what the buttons allow for it).
    /// New and Duplicate land on the new assignment (the game selects it and turns to its page).
    /// Escape is native (Back). Focus survives what opens on top (an editor, a level, a dialog).
    /// </summary>
    public sealed class CustomAssignmentsScreen : Screen
    {
        private const string ListStop = "custom.list", NewStop = "custom.new", ActionStop = "custom.actions";
        private const string EntryPrefix = "custom.a.", MoleculePrefix = "custom.mol.";
        private const int PerPage = 12; // ResearchNetCustomEditor.int_0

        public override string Key => "researchnet.custom";
        public override string ScreenName => GameText.T("Custom", "ENGLISH ALPHABET ONLY") + " " + GameText.T("Assignments");
        public override bool KeepStateOnPop => _covered;
        public override object InitialFocusStop => Entries().Count > 0 ? ListStop : NewStop;

        private static ResearchNetCustomEditor Editor => ProfileUi.Settled<ResearchNetCustomEditor>();

        public override bool IsActive() => Editor != null;

        private bool _covered;
        private static string _chosen; // level id of the assignment last focused
        private static readonly Dictionary<string, string> _entryOf = new Dictionary<string, string>(); // node key → level id

        public override void OnPop()
        {
            _covered = false;
            foreach (var s in GameState.ScreenStack())
                if (s is ResearchNetCustomEditor) { _covered = true; return; }
            _chosen = null;
        }

        /// <summary>The assignments in the game's order (Class429.list_0: oldest first).</summary>
        internal static List<Class429.Class431> Entries()
        {
            try { return Impeller.Locals.smethod_0().smethod_0().method_41().method_0().ToList(); }
            catch { return new List<Class429.Class431>(); }
        }

        public override void Build(GraphBuilder b)
        {
            var editor = Editor;
            if (editor == null) return;
            var entries = Entries();
            _entryOf.Clear();

            if (entries.Count > 0)
            {
                b.BeginStop(ListStop);
                for (int i = 0; i < entries.Count; i++) BuildEntry(b, entries[i], i + 1, entries.Count);
            }
            SyncSelection(editor, entries);

            b.BeginStop(NewStop);
            b.AddItem(ControlId.Structural("custom.new.research"), NewButton("New\nResearch", e => e.method_21()));
            b.AddItem(ControlId.Structural("custom.new.production"), NewButton("New\nProduction", e => e.method_22()));
            b.AddItem(ControlId.Structural("custom.new.sandbox"), NewButton("New\nSandbox", e => e.method_23()));

            b.BeginStop(ActionStop);
            b.AddItem(ControlId.Structural("custom.edit"), GameButton("Edit", editor.gclass15_0, e => e.method_24()));
            b.AddItem(ControlId.Structural("custom.play"), GameButton("Play", editor.gclass15_1, e => e.method_25()));
            b.AddItem(ControlId.Structural("custom.duplicate"), GameButton("Duplicate", editor.gclass15_2, e => { e.method_26(); FocusLast(); }));
            b.AddItem(ControlId.Structural("custom.delete"), GameButton("Delete", editor.gclass15_3, e => e.method_27()));
            b.AddItem(ControlId.Structural("custom.import"), GameButton("Import", null, e => e.method_29()));
            b.AddItem(ControlId.Structural("custom.export"), GameButton("Export", editor.gclass15_4, e => e.method_30()));
            b.AddItem(ControlId.Structural("custom.back"), GameButton("Back", null, e => e.method_12()));
            b.AddItem(ControlId.Structural("custom.previous"), PageButton("Previous", editor.gclass15_5, -1));
            b.AddItem(ControlId.Structural("custom.next"), PageButton("Next", editor.gclass15_6, 1));
        }

        // ---- the assignments ----

        private void BuildEntry(GraphBuilder b, Class429.Class431 entry, int index, int count)
        {
            string id = entry.string_0;
            string key = EntryPrefix + id;
            _entryOf[key] = id;
            var level = entry.customLevel_0;
            var position = new NodeAnnouncement(() => Loc.T("nav.position", new { index, count }), kind: AnnouncementKinds.Position);

            if (!level.method_1()) // Class680: a definition this client can't load; only Delete works on it
            {
                var vt = ProfileUi.Text(() => GameText.T("Unavailable") + ". "
                    + GameText.T("You must update your copy of the SpaceChem client to be able to play this puzzle."));
                vt.Announcements = new[] { vt.Announcements[0], position };
                vt.SpeaksOwnPosition = true;
                vt.OnSelect = () => _chosen = id;
                vt.OnSecondary = () => OpenMenu(entry);
                b.AddItem(ControlId.Structural(key), vt);
                return;
            }

            var previews = JournalScreen.Previews(level);
            var button = ProfileUi.Button(() => Name(level), () => entry.method_7()); // the double click
            button.Announcements = new[]
            {
                button.Announcements[0],
                new NodeAnnouncement(() => string.IsNullOrEmpty(level.string_2) ? null : level.string_2, kind: AnnouncementKinds.Value),
                new NodeAnnouncement(() => Kind(level), kind: AnnouncementKinds.Value),
                new NodeAnnouncement(() => LevelsDb.smethod_11(id) ? Loc.T("levelselect.completed") : null, kind: AnnouncementKinds.Tooltip),
                position,
            };
            button.SpeaksOwnPosition = true;
            button.OnSelect = () => _chosen = id;
            button.OnSecondary = () => OpenMenu(entry);
            button.OnTooltip = () => Speech.Tts.Speak(JournalScreen.PreviewLine(previews), interrupt: true);

            b.StartRow();
            b.AddItem(ControlId.Structural(key), button);
            for (int m = 0; m < previews.Count; m++)
            {
                var preview = previews[m];
                string cell = MoleculePrefix + id + "." + m;
                _entryOf[cell] = id;
                var vt = ProfileUi.Button(() => Loc.T(preview.Output ? "journal.output" : "journal.input",
                    new { molecule = MoleculeText.NameAndFormula(preview.Molecule) }),
                    () => PushChild(new MoleculeViewerScreen("custom.viewer", preview.Molecule)));
                vt.SpeaksOwnPosition = true;
                b.AddItem(ControlId.Structural(cell), vt);
            }
            b.EndRow();
        }

        /// <summary>The card's name line (an assignment may be saved with a blank name).</summary>
        internal static string Name(CustomLevel level)
            => string.IsNullOrEmpty(level?.string_1) ? Loc.T("text.blank") : level.string_1;

        /// <summary>The editor the assignment opens in, by the game's words.</summary>
        internal static string Kind(CustomLevel level)
        {
            if (level is CustomResearchLevel) return GameText.T("Research");
            if (level is CustomProductionLevel) return GameText.T("Production");
            if (level is CustomSandboxLevel) return GameText.T("Sandbox");
            return null;
        }

        /// <summary>Backspace on an assignment: what the action buttons allow for it
        /// (ResearchNetCustomEditor.method_34: an unloadable one can only be deleted).</summary>
        private void OpenMenu(Class429.Class431 entry)
        {
            _chosen = entry.string_0;
            var editor = Editor;
            if (editor != null) SyncSelection(editor, Entries());
            bool loads = entry.customLevel_0.method_1();
            var items = new List<ActionListScreen.Item>();
            void Add(string label, Action<ResearchNetCustomEditor> run) => items.Add(new ActionListScreen.Item
            {
                Label = () => GameText.T(label),
                Run = () => { var e = Editor; if (e != null) ProfileUi.Press(() => run(e)); },
            });
            if (loads)
            {
                Add("Edit", e => e.method_24());
                Add("Play", e => e.method_25());
                Add("Duplicate", e => { e.method_26(); FocusLast(); });
            }
            Add("Delete", e => e.method_27());
            if (loads) Add("Export", e => e.method_30());
            PushChild(new ActionListScreen("custom.menu", Name(entry.customLevel_0), items));
        }

        private static bool _focusLastPending;

        /// <summary>The import dialog appended one: focus it once this screen is back on top
        /// (a focus request made while the dialog is focused would go to the dialog's navigator).</summary>
        internal static void FocusLastAfterImport() => _focusLastPending = true;

        public override void OnUpdate()
        {
            if (!_focusLastPending || ActiveChild != null || Editor == null) return;
            _focusLastPending = false;
            FocusLast();
        }

        /// <summary>Focus the last assignment next render: New and Duplicate append one, and the
        /// game selects it and turns to its page (bool_5).</summary>
        private static void FocusLast()
        {
            var entries = Entries();
            if (entries.Count == 0) return;
            _chosen = entries[entries.Count - 1].string_0;
            Navigation.FocusNode(ControlId.Structural(EntryPrefix + _chosen));
        }

        private NodeVtable NewButton(string label, Action<ResearchNetCustomEditor> create)
            => ProfileUi.Button(() => GameText.Speech(GameText.T(label)), () =>
            {
                var e = Editor;
                if (e == null) return;
                create(e);
                FocusLast();
            });

        /// <summary>One of the screen's buttons: disabled (the draw dims it, GClass15.method_7) reads
        /// "unavailable" and does nothing.</summary>
        private static NodeVtable GameButton(string label, GClass15 widget, Action<ResearchNetCustomEditor> run)
        {
            var vt = ProfileUi.Button(() => GameText.T(label), () => { var e = Editor; if (e != null) run(e); });
            if (widget != null && !widget.method_7()) Disable(vt);
            return vt;
        }

        private static void Disable(NodeVtable vt)
        {
            vt.Announcements = new[] { vt.Announcements[0], new NodeAnnouncement(() => Loc.T("value.unavailable"), kind: AnnouncementKinds.Enabled) };
            vt.OnActivate = () => Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true);
        }

        /// <summary>Previous / Next: the game's page turn, then focus on the new page's first card.</summary>
        private static NodeVtable PageButton(string label, GClass15 widget, int step)
        {
            var vt = ProfileUi.Button(() => GameText.T(label), () =>
            {
                var e = Editor;
                if (e == null) return;
                int page = e.int_2 + step;
                if (step < 0) e.vmethod_11(); else e.vmethod_12();
                var entries = Entries();
                int first = page * PerPage;
                if (first >= 0 && first < entries.Count)
                {
                    _chosen = entries[first].string_0;
                    Navigation.FocusNode(ControlId.Structural(EntryPrefix + _chosen));
                }
            });
            if (widget != null && !widget.method_7()) Disable(vt);
            return vt;
        }

        // ---- the page and the selection follow focus ----

        /// <summary>Show the focused assignment's page (or, from the other stops, the last one
        /// focused) and select its card as the click does: that card's bool_2 on, every other off,
        /// then the editor's own button refresh (method_34). A rebuild (a page turn, regaining the
        /// top) deselects everything, so this runs every render.</summary>
        private static void SyncSelection(ResearchNetCustomEditor editor, List<Class429.Class431> entries)
        {
            try
            {
                if (ListStop.Equals(Navigation.FocusedStopKey))
                {
                    string key = Navigation.FocusedNodeId?.StructuralKey as string;
                    string id;
                    if (key != null && _entryOf.TryGetValue(key, out id)) _chosen = id;
                }
                if (_chosen == null) return;
                int index = entries.FindIndex(e => e.string_0 == _chosen);
                if (index < 0) { _chosen = null; return; }
                int page = index / PerPage;
                if (editor.int_2 != page)
                {
                    editor.int_2 = page;
                    editor.method_13(); // the page buttons' own rebuild
                }
                bool changed = false;
                foreach (var w in editor.list_0)
                {
                    bool want = w.method_7().string_0 == _chosen;
                    if (w.method_8() == want) continue;
                    w.method_9(want);
                    changed = true;
                }
                if (changed) editor.method_34();
            }
            catch (Exception ex) { Log.Error("[custom] selection sync failed", ex); }
        }
    }

    /// <summary>
    /// Class57, the custom assignments' Import dialog (a Class56 overlay): it reads the clipboard
    /// when built — "Your clipboard contains the following custom assignment:" and the assignment's
    /// card, or "Your clipboard does not contain a valid custom assignment to import." — then Rescan
    /// Clipboard (rebuild = read again), Import Assignment (disabled without a valid one, or for one
    /// this client can't load) and Cancel. The parsed assignment lives only in a closure of the
    /// build, so the mod parses the clipboard the same way (Class211.smethod_0 over Class448's
    /// clipboard read) once per build of the dialog, and imports through the game's own method_16.
    /// One vertical list, no counts (the dialog rule); Escape is native (Cancel).
    /// </summary>
    public sealed class ImportAssignmentScreen : Screen
    {
        public override string Key => "researchnet.import";
        public override string ScreenName => GameText.T("Import");

        private static Class57 Dialog => ProfileUi.Settled<Class57>();

        public override bool IsActive() => Dialog != null;

        private object _builtFrom; // the dialog's widget tree the parse belongs to
        private Struct116<CustomLevel> _parsed;

        private Struct116<CustomLevel> Parsed(Class57 dialog)
        {
            var tree = dialog.gclass8_0;
            if (!ReferenceEquals(tree, _builtFrom))
            {
                _builtFrom = tree;
                _parsed = Struct7.struct7_0;
                try
                {
                    string text = Class448.smethod_10();
                    if (!string.IsNullOrEmpty(text)) _parsed = Class211.smethod_0(text);
                }
                catch (Exception ex) { Log.Error("[import] clipboard parse failed", ex); }
            }
            return _parsed;
        }

        public override void Build(GraphBuilder b)
        {
            var dialog = Dialog;
            if (dialog == null) return;
            var parsed = Parsed(dialog);
            if (parsed.bool_0)
            {
                var level = parsed.method_0();
                b.AddItem(ControlId.Structural("import.text"), ProfileUi.Text(true,
                    () => GameText.Speech(GameText.T("Your clipboard contains the following custom assignment:"))));
                b.AddItem(ControlId.Structural("import.level"), ProfileUi.Text(true, () => Card(level)));
            }
            else
            {
                b.AddItem(ControlId.Structural("import.text"), ProfileUi.Text(true,
                    () => GameText.Speech(GameText.T("Your clipboard does not contain a valid custom assignment to import."))));
            }
            var rescan = ProfileUi.Button(() => GameText.T("Rescan Clipboard"), () => Dialog?.method_15());
            rescan.SpeaksOwnPosition = true;
            b.AddItem(ControlId.Structural("import.rescan"), rescan);

            bool importable = parsed.bool_0 && parsed.method_0().method_1();
            var import = ProfileUi.Button(() => GameText.T("Import Assignment"), () =>
            {
                var d = Dialog;
                if (d == null) return;
                d.method_16(Parsed(d));
                CustomAssignmentsScreen.FocusLastAfterImport();
            });
            import.SpeaksOwnPosition = true;
            if (!importable)
            {
                import.Announcements = new[] { import.Announcements[0], new NodeAnnouncement(() => Loc.T("value.unavailable"), kind: AnnouncementKinds.Enabled) };
                import.OnActivate = () => Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true);
            }
            b.AddItem(ControlId.Structural("import.import"), import);

            var cancel = ProfileUi.Button(() => GameText.T("Cancel"), () => Dialog?.method_17());
            cancel.SpeaksOwnPosition = true;
            b.AddItem(ControlId.Structural("import.cancel"), cancel);
        }

        /// <summary>What the card shows: name, author, kind, its molecules.</summary>
        private static string Card(CustomLevel level)
        {
            if (!level.method_1())
                return GameText.T("Unavailable") + ". " + GameText.T("You must update your copy of the SpaceChem client to be able to play this puzzle.");
            var parts = new List<string> { CustomAssignmentsScreen.Name(level) };
            if (!string.IsNullOrEmpty(level.string_2)) parts.Add(level.string_2);
            string kind = CustomAssignmentsScreen.Kind(level);
            if (kind != null) parts.Add(kind);
            string molecules = JournalScreen.PreviewLine(JournalScreen.Previews(level));
            if (!string.IsNullOrEmpty(molecules)) parts.Add(molecules);
            return string.Join(", ", parts.ToArray());
        }
    }
}
