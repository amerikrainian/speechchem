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

namespace SpeechChem.Screens
{
    /// <summary>
    /// The ResearchNet journal (SpaceChem.ResearchNetJournalEditor, a Class59 paged ResearchNet
    /// screen): "The Journal of Reaction Engineering" — the published puzzles of journal.json
    /// (PublishedLevelCache.theJournal_0), newest issue first, three issues per page, each issue a
    /// header (volume, issue, title) and its puzzle cards (JournalIssueWidget, keys in sorted order);
    /// the level select's score panel on the left; Back, Create or Import an Assignment, Newer, Older.
    ///
    /// ONE LIST, THE PAGE FOLLOWS (user decision 2026-09-27): the Issues stop holds every issue, not
    /// just the drawn page, each issue its own region (Ctrl+Up/Down hop issues); whenever focus is in
    /// it the game's page (int_1) is turned to the focused issue's page, so the screen shows what is
    /// being read. Newer / Older run the game's page turn and move focus to that page's first issue.
    ///
    /// A puzzle is a ROW (user decision: both): the puzzle button — "name, button, author,
    /// difficulty[, completed], n of m" — then the molecules its card draws as cells ("name,
    /// formula, output"; research cards draw outputs, production cards inputs then outputs, sandbox
    /// cards inputs), Enter on one opens the molecule viewer; Shift+Backspace on the puzzle speaks
    /// the same molecules as one line. Difficulty is a pictograph (the SpaceChem logo with one to
    /// three segments filled: researchnet/icon_easy / _medium / _hard), so its words are the mod's.
    /// Landing on a puzzle points the panel at it (the card's hover, method_18); Enter opens it (the
    /// card's click: PublishedLevelCache.smethod_3). A card the client can't load (Class680) reads the
    /// game's "Unavailable" text and does nothing.
    ///
    /// Escape stays native (Back). The game's Tab (graph / leaderboard toggle) is ours while modeled.
    /// Focus survives a puzzle or the assignment editor on top; leaving starts over.
    /// </summary>
    public sealed class JournalScreen : Screen
    {
        private const string IssueStop = "issues";
        private const string ScoreStop = "scores";
        private const string ActionStop = "actions";
        private const string IssuePrefix = "journal.issue.";
        private const string PuzzlePrefix = "journal.puzzle.";
        private const string MoleculePrefix = "journal.mol.";
        private const int PerPage = 3; // ResearchNetJournalEditor.int_0

        public override string Key => "journal";
        public override string ScreenName
            => GameText.T("The Journal of", "ENGLISH ALPHABET ONLY") + " " + GameText.T("Reaction Engineering");
        public override bool KeepStateOnPop => _covered;
        public override object InitialFocusStop => IssueStop;

        private static ResearchNetJournalEditor Journal => ProfileUi.Settled<ResearchNetJournalEditor>();

        public override bool IsActive() => Journal != null;

        /// <summary>Covered by a puzzle or the assignment editor (the journal stays in the chain under
        /// it): keep focus. Leaving through Back / Escape starts over.</summary>
        public override void OnPop()
        {
            _covered = false;
            foreach (var s in GameState.ScreenStack())
                if (s is ResearchNetJournalEditor) { _covered = true; return; }
            _chosen = null;
        }

        private bool _covered;

        // Built fresh every render; the maps let SyncPage / SyncPanel resolve the focused node.
        private static readonly Dictionary<string, int> _issueOf = new Dictionary<string, int>(); // node key → issue index
        private static readonly Dictionary<string, string> _puzzleOf = new Dictionary<string, string>(); // node key → level id
        private static string _chosen;

        public override void Build(GraphBuilder b)
        {
            var journal = Journal;
            if (journal == null) return;

            var issues = Issues();
            BuildIssues(b, issues);
            SyncPage(journal, issues.Count);
            SyncPanel(journal);

            b.BeginStop(ScoreStop);
            for (int metric = 0; metric < 3; metric++)
            {
                int m = metric;
                b.AddItem(ControlId.Structural("journal.score." + m), ProfileUi.Text(true, () => LevelSelectScreen.ScoreRow(Shown(), m)));
            }

            b.BeginStop(ActionStop);
            b.AddItem(ControlId.Structural("journal.back"), ProfileUi.Button(() => GameText.T("Back"), () => Journal?.method_12()));
            b.AddItem(ControlId.Structural("journal.create"),
                ProfileUi.Button(() => GameText.T("Create or Import an Assignment"), () => Journal?.method_20()));
            b.AddItem(ControlId.Structural("journal.newer"), PageButton(journal, "Newer", journal.gclass15_1, -1, issues.Count));
            b.AddItem(ControlId.Structural("journal.older"), PageButton(journal, "Older", journal.gclass15_0, 1, issues.Count));
        }

        /// <summary>The issues in the draw's order: newest first (vmethod_8's OrderByDescending).</summary>
        private static List<TheJournal.Issue> Issues()
            => PublishedLevelCache.theJournal_0.list_0.OrderByDescending(i => i.dateTime_0).ToList();

        // ---- issues ----

        private void BuildIssues(GraphBuilder b, List<TheJournal.Issue> issues)
        {
            _issueOf.Clear();
            _puzzleOf.Clear();
            b.BeginStop(IssueStop);
            for (int i = 0; i < issues.Count; i++)
            {
                var issue = issues[i];
                b.SetRegion(IssuePrefix + i);
                string header = IssuePrefix + i;
                _issueOf[header] = i;
                b.AddItem(ControlId.Structural(header), ProfileUi.Text(true, () => IssueHeader(issue)));

                var keys = issue.dictionary_0.Keys.OrderBy(k => k).ToList(); // JournalIssueWidget.method_7's order
                for (int p = 0; p < keys.Count; p++)
                    BuildPuzzle(b, i, keys[p], issue.dictionary_0[keys[p]], p + 1, keys.Count);
            }
            b.SetRegion(null);
        }

        private static string IssueHeader(TheJournal.Issue issue)
        {
            var parts = new List<string>();
            foreach (var s in new[] { issue.string_0, issue.string_1, issue.string_2 })
                if (!string.IsNullOrEmpty(s)) parts.Add(s);
            return string.Join(", ", parts.ToArray());
        }

        private void BuildPuzzle(GraphBuilder b, int issue, string id, CustomLevel level, int index, int count)
        {
            string key = PuzzlePrefix + id;
            _issueOf[key] = issue;
            var position = new NodeAnnouncement(() => Loc.T("nav.position", new { index, count }), kind: AnnouncementKinds.Position);

            if (!level.method_1()) // Class680: the card the game draws for a puzzle this client can't load
            {
                var vt = ProfileUi.Text(() => GameText.T("Unavailable") + ". "
                    + GameText.T("You must update your copy of the SpaceChem client to be able to play this puzzle."));
                vt.Announcements = new[] { vt.Announcements[0], position };
                vt.SpeaksOwnPosition = true;
                b.AddItem(ControlId.Structural(key), vt);
                return;
            }

            _puzzleOf[key] = id;
            var previews = Previews(level);
            var puzzle = ProfileUi.Button(() => level.string_1, () => PublishedLevelCache.smethod_3(id));
            puzzle.Announcements = new[]
            {
                puzzle.Announcements[0],
                new NodeAnnouncement(() => level.string_2, kind: AnnouncementKinds.Value),
                new NodeAnnouncement(() => Difficulty(level), kind: AnnouncementKinds.Value),
                new NodeAnnouncement(() => LevelsDb.smethod_11(id) ? Loc.T("levelselect.completed") : null,
                    kind: AnnouncementKinds.Tooltip),
                position,
            };
            puzzle.SpeaksOwnPosition = true;
            puzzle.OnSelect = () => { _chosen = id; Journal?.method_18(id); }; // the card's hover
            puzzle.OnTooltip = () => Speech.Tts.Speak(PreviewLine(previews), interrupt: true);

            b.StartRow();
            b.AddItem(ControlId.Structural(key), puzzle);
            for (int m = 0; m < previews.Count; m++)
            {
                var preview = previews[m];
                string cell = MoleculePrefix + id + "." + m;
                _issueOf[cell] = issue;
                _puzzleOf[cell] = id;
                var vt = ProfileUi.Button(() => Loc.T(preview.Output ? "journal.output" : "journal.input",
                    new { molecule = MoleculeText.NameAndFormula(preview.Molecule) }),
                    () => PushChild(new MoleculeViewerScreen("journal.viewer", preview.Molecule)));
                vt.SpeaksOwnPosition = true; // a cell of the puzzle's row, not a list item
                b.AddItem(ControlId.Structural(cell), vt);
            }
            b.EndRow();
        }

        private struct Preview
        {
            public Molecule Molecule;
            public bool Output;
        }

        /// <summary>The molecules a card draws, in its order (CustomLevel.vmethod_4 overrides):
        /// research = the output zones; production = the random input zones' molecules, the fixed
        /// inputs, then the output zones; sandbox = the inputs only.</summary>
        private static List<Preview> Previews(CustomLevel level)
        {
            var list = new List<Preview>();
            void Add(Molecule m, bool output) { if (m != null) list.Add(new Preview { Molecule = m, Output = output }); }
            switch (level)
            {
                case CustomResearchLevel r:
                    foreach (var o in r.dictionary_2.Values) Add(o.molecule_0, true);
                    break;
                case CustomProductionLevel p:
                    foreach (var zone in p.dictionary_1.Values) foreach (var i in zone.list_0) Add(i.molecule_0, false);
                    foreach (var i in p.dictionary_2.Values) Add(i, false);
                    foreach (var o in p.dictionary_3.Values) Add(o.molecule_0, true);
                    break;
                case CustomSandboxLevel s:
                    foreach (var zone in s.dictionary_1.Values) foreach (var i in zone.list_0) Add(i.molecule_0, false);
                    foreach (var i in s.dictionary_2.Values) Add(i, false);
                    break;
            }
            return list;
        }

        /// <summary>"Inputs: a; b. Outputs: c" — the card's molecules as one line.</summary>
        private static string PreviewLine(List<Preview> previews)
        {
            var parts = new List<string>();
            foreach (bool output in new[] { false, true })
            {
                var names = previews.Where(p => p.Output == output).Select(p => MoleculeText.NameAndFormula(p.Molecule)).ToArray();
                if (names.Length == 0) continue;
                parts.Add(Loc.T("journal.previews", new
                {
                    label = Loc.T(output ? "reactor.cat.outputs" : "reactor.cat.inputs"),
                    molecules = string.Join("; ", names),
                }));
            }
            return string.Join(". ", parts.ToArray());
        }

        /// <summary>The card's difficulty icon (CustomLevel.dictionary_0; the sandbox card draws none).</summary>
        private static string Difficulty(CustomLevel level)
        {
            if (level is CustomSandboxLevel) return null;
            int d = (int)level.enum164_0;
            return d >= 1 && d <= 3 ? Loc.T("journal.difficulty." + d) : null;
        }

        // ---- the page follows focus ----

        private static void SyncPage(ResearchNetJournalEditor journal, int issueCount)
        {
            if (!IssueStop.Equals(Navigation.FocusedStopKey)) return;
            string key = Navigation.FocusedNodeId?.StructuralKey as string;
            if (key == null || !_issueOf.TryGetValue(key, out int issue)) return;
            int page = issue / PerPage;
            if (journal.int_1 == page) return;
            journal.int_1 = page;
            journal.method_13(); // the page buttons' own rebuild
        }

        /// <summary>Newer / Older: the game's page turn (click sound + vmethod_11 / vmethod_12), then
        /// focus on the new page's first issue. Drawn disabled on the first / last page.</summary>
        private NodeVtable PageButton(ResearchNetJournalEditor journal, string label, GClass15 button, int step, int issueCount)
        {
            bool enabled = button.method_7();
            var vt = ProfileUi.Button(() => GameText.T(label), () =>
            {
                var j = Journal;
                if (j == null) return;
                int pages = Math.Max(1, (issueCount + PerPage - 1) / PerPage);
                int page = j.int_1 + step;
                if (page < 0 || page >= pages) return;
                if (step < 0) j.vmethod_11(); else j.vmethod_12();
                Navigation.FocusNode(ControlId.Structural(IssuePrefix + page * PerPage));
            });
            if (!enabled)
            {
                vt.Announcements = new[] { vt.Announcements[0], new NodeAnnouncement(() => Loc.T("value.unavailable"), kind: AnnouncementKinds.Enabled) };
                vt.OnActivate = () => Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true); // no click: the game ignores it
            }
            return vt;
        }

        // ---- the score panel follows the focused puzzle (the hover only sets it, and a rebuild —
        // a page turn, a return from a puzzle — clears it), re-pointed every render: at the focused
        // puzzle, from the other stops at the last one chosen, on an issue header at nothing new. ----

        private static void SyncPanel(ResearchNetJournalEditor journal)
        {
            string want;
            if (IssueStop.Equals(Navigation.FocusedStopKey))
            {
                string key = Navigation.FocusedNodeId?.StructuralKey as string;
                if (key == null || !_puzzleOf.TryGetValue(key, out want)) return;
                _chosen = want;
            }
            else want = _chosen;
            if (want == null || journal.class469_0 == null || Shown() == want) return;
            journal.method_18(want);
        }

        /// <summary>The id of the puzzle the panel shows, or null.</summary>
        private static string Shown()
        {
            var panel = Journal?.class469_0;
            if (panel == null) return null;
            var shown = panel.method_8();
            return shown.bool_0 ? shown.method_0() : null;
        }
    }
}
