using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Impeller;
using SpaceChem;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.Patches;
using SpeechChem.UI;
using SpeechChem.UI.Graph;
using Editor = SpaceChem.StoryTrainingPerformanceEditor;

namespace SpeechChem.Screens
{
    /// <summary>
    /// StoryTrainingPerformanceEditor — the Story / Training / Performance screen: shown before a
    /// level with new story or training, from the in-level "Story &amp; Info" button, and on level
    /// completion (Performance). Everything it draws is glyph Scenes, so the text comes from the
    /// sources:
    ///
    ///  • Story: entry n is text-table section Keys[n] (StoryEntries.dictionary_0's lambdas, in
    ///    order, decompile-verified), read raw from Class177.smethod_1() (string_0 title, string_1
    ///    body) and cleaned of the font markup (ExtendedFont.method_9: "[name]" illustration lines,
    ///    ↨ ← { } ● formatting marks, the ↑ page break).
    ///  • Training: entry n's captions, recorded while its factory Class170.smethod_(5+n) builds
    ///    (Patches/StoryCapture).
    ///  • Performance: the score (struct116_2 this run, struct116_3 the previous best; int_0 cycles,
    ///    int_1 reactors, int_2 symbols) against either the histograms (scores.json via Class157,
    ///    summarized by Game/Histogram) or the Steam leaderboards — whichever the game's Tab toggle
    ///    (Class280.smethod_0) shows; a button here flips it. Promotion / challenge lines, Record
    ///    Solution.
    ///
    /// Stops: the tabs (selection follows focus, as the game's tab click), the entry (a combo box
    /// over the unlocked entries — Story / Training only), the text, and the buttons. The tab shown
    /// is tracked by StoryCapture (method_21/22/24), inferred from the constructor's rule after a
    /// hot reload.
    /// </summary>
    public sealed class StoryInfoScreen : Screen
    {
        private const string TabsStop = "story.tabs", EntryStop = "story.entry", TextStop = "story.text", ActionsStop = "story.actions";

        private static readonly string[] Keys =
        {
            "Training1", "Training2", "Training3", "Training4", "Training5", "Bonding1", "Bonding2", "Bonding4",
            "Bonding5", "Sensing1", "Sensing2", "Sensing3", "Sensing4", "Sensing5", "FusionIntro",
            "FusionConvoMarianne", "FusionConvoTim", "FusionNightmare", "FusionBossPre", "FusionBossPost",
            "MiningIntro", "MiningHelloSFCM", "MiningFollowup", "MiningBossPrePre", "MiningBossPre",
            "MiningBossPost", "ResearchIntro", "ResearchLunch", "ResearchHikaru1", "ResearchHikaru2",
            "ResearchLaser", "ResearchBossPre", "ResearchBossPost", "OrganicIntro", "OrganicChat",
            "OrganicDream", "OrganicBossPre", "OrganicBossPost", "FinalBossPre",
        };

        private static readonly Func<Scene>[] TrainingFactories =
        {
            Class170.smethod_5, Class170.smethod_6, Class170.smethod_7, Class170.smethod_8, Class170.smethod_9,
            Class170.smethod_10, Class170.smethod_11, Class170.smethod_12, Class170.smethod_13, Class170.smethod_14,
            Class170.smethod_15, Class170.smethod_16, Class170.smethod_17,
        };

        private readonly Dictionary<int, List<string>> _training = new Dictionary<int, List<string>>();

        public override string Key => "storyinfo";

        private static Editor Ed => ProfileUi.Settled<Editor>();

        public override bool IsActive() => Ed != null;

        public override object InitialFocusStop => TextStop;

        public override string ScreenName
        {
            get
            {
                var e = Ed;
                return e == null ? null : Loc.T("story.screen", new { tab = TabName(TabOf(e)) });
            }
        }

        public override void OnPop() => _training.Clear();

        // ---- tabs ----

        private static StoryCapture.Tab TabOf(Editor e)
        {
            var t = StoryCapture.TabOf(e);
            if (t != null) return t.Value;
            if (e.struct116_2.bool_0) return StoryCapture.Tab.Performance;
            if (e.bool_6) return StoryCapture.Tab.Training;
            if (e.bool_5) return StoryCapture.Tab.Story;
            return e.struct116_1.bool_0 ? StoryCapture.Tab.Training : StoryCapture.Tab.Story;
        }

        private static string TabName(StoryCapture.Tab t)
        {
            switch (t)
            {
                case StoryCapture.Tab.Training: return GameText.T("Training");
                case StoryCapture.Tab.Performance: return GameText.T("Performance");
                default: return GameText.T("Story");
            }
        }

        public override void Build(GraphBuilder b)
        {
            var e = Ed;
            if (e == null) return;
            var tab = TabOf(e);
            BuildTabs(b, tab);
            if (tab != StoryCapture.Tab.Performance) BuildEntry(b, e, tab == StoryCapture.Tab.Story);
            b.BeginStop(TextStop);
            var lines = tab == StoryCapture.Tab.Story ? StoryLines(e)
                : tab == StoryCapture.Tab.Training ? TrainingLines(e)
                : PerformanceLines(e);
            ControlId lastLine = null;
            for (int i = 0; i < lines.Count; i++)
            {
                string text = lines[i];
                lastLine = ControlId.Structural("story.line." + tab + "." + i);
                b.AddItem(lastLine, ProfileUi.Text(true, () => text));
            }
            if (tab == StoryCapture.Tab.Performance) BuildMetrics(b, e, lastLine);
            BuildActions(b, e, tab);
        }

        private static void BuildTabs(GraphBuilder b, StoryCapture.Tab current)
        {
            b.BeginStop(TabsStop);
            b.StartRow();
            foreach (StoryCapture.Tab t in new[] { StoryCapture.Tab.Story, StoryCapture.Tab.Training, StoryCapture.Tab.Performance })
            {
                var tab = t;
                var vt = new NodeVtable
                {
                    ControlType = ControlTypes.Tab,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => TabName(tab), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => IsNew(tab) ? Loc.T("story.new") : null, kind: AnnouncementKinds.Value),
                        new NodeAnnouncement(() => Available(tab) ? null : Loc.T("value.unavailable"), kind: AnnouncementKinds.Enabled),
                    },
                    Selected = () => Ed != null && TabOf(Ed) == tab,
                };
                vt.OnSelect = () => ShowTab(tab);
                vt.OnActivate = () => ShowTab(tab);
                b.AddItem(ControlId.Structural("story.tab." + tab), vt);
            }
            b.EndRow();
        }

        private static bool IsNew(StoryCapture.Tab t)
        {
            var e = Ed;
            if (e == null) return false;
            return t == StoryCapture.Tab.Story ? e.bool_5 : t == StoryCapture.Tab.Training && e.bool_6;
        }

        private static bool Available(StoryCapture.Tab t) => t != StoryCapture.Tab.Performance || (Ed != null && Ed.struct116_2.bool_0);

        private static void ShowTab(StoryCapture.Tab t)
        {
            var e = Ed;
            if (e == null || TabOf(e) == t || !Available(t)) return;
            ProfileUi.Press(() =>
            {
                if (t == StoryCapture.Tab.Story) e.method_31();
                else if (t == StoryCapture.Tab.Training) e.method_32();
                else e.method_33();
            });
        }

        // ---- entry (combo box over the unlocked entries) ----

        private void BuildEntry(GraphBuilder b, Editor e, bool story)
        {
            b.BeginStop(EntryStop);
            b.AddItem(ControlId.Structural("story.entry"), new NodeVtable
            {
                ControlType = ControlTypes.ComboBox,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => Loc.T("story.entry"), kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => EntryValue(story), kind: AnnouncementKinds.Value),
                },
                OnActivate = () =>
                {
                    var ed = Ed;
                    if (ed == null) return;
                    PushChild(new ChoiceListScreen("story.entry.list", () => EntryTitles(story), Shown(ed, story), i => PickEntry(story, i)));
                },
            });
        }

        private static int Shown(Editor e, bool story) => story ? (int)e.enum130_1 : (int)e.enum126_1;
        private static int Unlocked(Editor e, bool story) => (story ? (int)e.enum130_0 : (int)e.enum126_0) + 1;

        private static string EntryValue(bool story)
        {
            var e = Ed;
            if (e == null) return null;
            // Just the title, like every other combo box (user rule) — the count lives in the list.
            return Title(story, Shown(e, story));
        }

        private static IReadOnlyList<string> EntryTitles(bool story)
        {
            var list = new List<string>();
            var e = Ed;
            if (e == null) return list;
            for (int i = 0; i < Unlocked(e, story); i++) list.Add(Title(story, i));
            return list;
        }

        private static string Title(bool story, int i)
        {
            try
            {
                if (story) return i < Keys.Length ? Class177.smethod_1()[Keys[i]].string_0 : "";
                return Class170.dictionary_0[(Enum126)i].string_0;
            }
            catch { return ""; }
        }

        private static void PickEntry(bool story, int i)
        {
            var e = Ed;
            if (e == null || i == Shown(e, story)) return;
            ProfileUi.Press(() => e.method_19(i, story));
        }

        // ---- text ----

        private static List<string> StoryLines(Editor e)
        {
            var lines = new List<string>();
            int n = (int)e.enum130_1;
            if (n < 0 || n >= Keys.Length) return lines;
            Class248 entry;
            try { if (!Class177.smethod_1().TryGetValue(Keys[n], out entry)) return lines; }
            catch { return lines; }
            lines.Add(entry.string_0);
            lines.AddRange(CleanStory(entry.string_1));
            return lines;
        }

        private static readonly Regex Illustration = new Regex(@"^\[[^\]]*\]$");

        /// <summary>The story body as speakable paragraphs: ExtendedFont.method_9's markup removed.</summary>
        internal static List<string> CleanStory(string raw)
        {
            var paragraphs = new List<string>();
            if (string.IsNullOrEmpty(raw)) return paragraphs;
            foreach (var line in raw.Replace("\r", "").Split('\n'))
            {
                string t = line.Trim();
                if (t.Length == 0 || Illustration.IsMatch(t)) continue;
                var sb = new StringBuilder();
                foreach (var word in t.Split(' '))
                {
                    if (word == "↑") continue; // ↑ page break
                    string w = word.Replace("↨", "").Replace("←", "").Replace("{", "").Replace("}", "").Replace("●", "");
                    if (w.Length == 0) continue;
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(w);
                }
                if (sb.Length > 0) paragraphs.Add(sb.ToString());
            }
            return paragraphs;
        }

        private List<string> TrainingLines(Editor e)
        {
            int n = (int)e.enum126_1;
            List<string> lines;
            if (_training.TryGetValue(n, out lines)) return lines;
            lines = new List<string> { Title(false, n) };
            if (n >= 0 && n < TrainingFactories.Length)
            {
                var factory = TrainingFactories[n];
                foreach (var caption in StoryCapture.Record(() => factory()))
                    foreach (var p in caption.Replace("\r", "").Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string t = p.Replace('\n', ' ').Trim();
                        if (t.Length > 0) lines.Add(t);
                    }
            }
            _training[n] = lines;
            return lines;
        }

        private static List<string> PerformanceLines(Editor e)
        {
            var lines = new List<string> { GameText.T("Assignment Complete!") };
            string level = GoalTracker.string_0;
            bool confidential = false;
            try { confidential = SpaceChem.Levels.Levels.smethod_13(level); } catch { }
            lines.Add(GameText.T(confidential ? "PERFORMANCE STATISTICS UNAVAILABLE"
                : "Your performance, compared to other engineers, is as follows:"));

            try
            {
                var challenge = GoalTracker.smethod_2();
                if (e.struct116_4.bool_0)
                    lines.Add(GameText.T("Congratulations! You have been promoted to") + " " + e.struct116_4.method_0());
                else if (challenge.bool_0 && challenge.method_0().bool_1)
                    lines.Add(GameText.T("Challenge complete!") + " " + challenge.method_0().method_2());
            }
            catch { }
            return lines;
        }

        private static int? Metric(Struct116<Score> s, int k)
        {
            if (!s.bool_0) return null;
            var score = s.method_0();
            int v = k == 0 ? score.int_0 : k == 1 ? score.int_1 : score.int_2;
            return v == int.MaxValue ? (int?)null : v;
        }

        private static string MetricLabel(int k)
            => k == 0 ? global::SpaceChem.Graph.string_0 : k == 1 ? global::SpaceChem.Graph.string_1 : global::SpaceChem.Graph.string_2;

        private static string Scores(int? mine, int? best)
        {
            var parts = new List<string>();
            if (mine != null) parts.Add(Loc.T("story.this", new { n = mine.Value }));
            if (best != null) parts.Add(Loc.T("story.best", new { n = best.Value }));
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>The three stats, in the TEXT stop right under the performance lines (user rule,
        /// 2026-09-27: Down from the last line reaches the stats; Up from any stat's top returns
        /// there), as a TABLE (user design, 2026-10-02, the Echopunks ColumnGrid): each stat a
        /// column headed by its name, spoken when focus crosses into it and never while it stays —
        /// first the THIS / BEST numbers the game prints over its markers, then the histogram's bars
        /// or the leaderboard's rows, whichever view the game shows. Up/Down walk a column;
        /// Left/Right cross to the same row of the next stat (clamped to a shorter one).</summary>
        private static void BuildMetrics(GraphBuilder b, Editor e, ControlId above)
        {
            string level = GoalTracker.string_0;
            try { if (SpaceChem.Levels.Levels.smethod_13(level)) return; } catch { }
            bool boards = Safe(Class280.smethod_0);
            string[] data = null;
            if (!boards)
            {
                try
                {
                    var counts = Class157.smethod_2(level);
                    if (counts.bool_0) data = new[] { counts.method_0().string_0, counts.method_0().string_1, counts.method_0().string_2 };
                }
                catch { }
            }
            string view = boards ? "b" : "h";
            var columns = new List<ColumnGrid.Column>();
            for (int k = 0; k < 3; k++)
            {
                int? mine = Metric(e.struct116_2, k), best = Metric(e.struct116_3, k);
                var lines = boards ? BoardLines(level, k, mine, best) : HistogramLines(k, mine, best, data?[k]);
                var column = new ColumnGrid.Column { Header = MetricLabel(k), ContextId = ControlId.Structural("story.metric.col." + k) };
                for (int i = 0; i < lines.Count; i++)
                    column.Cells.Add(new ColumnGrid.Cell
                    {
                        Id = ControlId.Structural("story.metric." + view + "." + k + "." + i),
                        Vtable = ColumnGrid.TextCell(lines[i]),
                    });
                columns.Add(column);
            }
            ColumnGrid.Edges edges;
            if (!ColumnGrid.Build(b, columns, out edges, Loc.T("role.table"), ControlId.Structural("story.metric.table"))) return;
            if (above == null) return;
            foreach (var top in edges.Top) b.Connect(top, GraphDir.Up, above);
            b.Connect(above, GraphDir.Down, edges.EnterTop);
        }

        /// <summary>A stat's first row: "THIS 45, BEST 40" (the column header names the stat).</summary>
        private static string Caption(int? mine, int? best)
        {
            string scores = Scores(mine, best);
            return string.IsNullOrEmpty(scores) ? Loc.T("text.na") : scores;
        }

        private static List<string> HistogramLines(int k, int? mine, int? best, string data)
        {
            var lines = new List<string> { Caption(mine, best) };
            var h = Histogram.Parse(data);
            if (h == null) { lines.Add(GameText.T("Data Unavailable")); return lines; }
            foreach (var bin in h.Bins(mine, best))
            {
                string tags = (bin.This ? ", " + Loc.T("story.marker.this") : "") + (bin.Best ? ", " + Loc.T("story.marker.best") : "");
                string pct = h.Shares
                    ? (bin.Percent == 0 && bin.Drawn ? Loc.T("story.bin.tiny") : Loc.T("story.bin.pct", new { n = bin.Percent }))
                    : (bin.Percent == 0 && bin.Drawn ? Loc.T("story.bin.tiny.tallest") : Loc.T("story.bin.pct.tallest", new { n = bin.Percent }));
                lines.Add(bin.Low == bin.High
                    ? Loc.T("story.bin.one", new { lo = bin.Low, pct, tags })
                    : Loc.T("story.bin", new { lo = bin.Low, hi = bin.High, pct, tags }));
            }
            return lines;
        }

        /// <summary>The leaderboard as Graph.smethod_2 ranks it: your entry takes the better of this run
        /// and your best (added when missing), sorted ascending, an 11-row window around you.</summary>
        private static List<string> BoardLines(string level, int k, int? mine, int? best)
        {
            var lines = new List<string> { Caption(mine, best) };
            int? yours = mine != null && best != null ? Math.Min(mine.Value, best.Value) : mine ?? best;
            try
            {
                Leaderboard board = Leaderboards.smethod_6(level, (Enum83)k);
                if (board == null) { lines.Add(GameText.T("Data Unavailable")); return lines; }
                var rows = new List<KeyValuePair<string, int>>();
                int me = -1;
                foreach (var entry in board.method_0())
                {
                    int score = entry.method_5();
                    if (entry.method_7())
                    {
                        if (yours != null && yours.Value < score) score = yours.Value;
                        me = rows.Count;
                    }
                    rows.Add(new KeyValuePair<string, int>(entry.method_7() ? null : entry.method_3(), score));
                }
                if (me < 0 && yours != null) rows.Add(new KeyValuePair<string, int>(null, yours.Value));
                var sorted = new List<KeyValuePair<string, int>>(rows);
                StableSort(sorted);
                int myRank = sorted.FindIndex(r => r.Key == null);
                const int window = 11;
                int start = myRank >= window ? myRank - window + 1 : 0;
                for (int i = start; i < Math.Min(start + window, sorted.Count); i++)
                    lines.Add(Loc.T("story.rank", new { rank = i + 1, name = sorted[i].Key ?? Loc.T("story.you"), score = sorted[i].Value }));
            }
            catch { lines.Add(GameText.T("Data Unavailable")); }
            return lines;
        }

        private static void StableSort(List<KeyValuePair<string, int>> rows)
        {
            for (int i = 1; i < rows.Count; i++)
            {
                var r = rows[i];
                int j = i - 1;
                while (j >= 0 && rows[j].Value > r.Value) { rows[j + 1] = rows[j]; j--; }
                rows[j + 1] = r;
            }
        }

        // ---- buttons ----

        /// <summary>The buttons: a plain vertical list with no counts (user layout, like the dialogs).</summary>
        private static void BuildActions(GraphBuilder b, Editor e, StoryCapture.Tab tab)
        {
            b.BeginStop(ActionsStop);
            Action<string, NodeVtable> add = (id, vt) => { vt.SpeaksOwnPosition = true; b.AddItem(ControlId.Structural(id), vt); };
            if (tab == StoryCapture.Tab.Performance && e.struct116_2.bool_0)
            {
                bool confidential = false;
                try { confidential = SpaceChem.Levels.Levels.smethod_13(GoalTracker.string_0); } catch { }
                if (!confidential)
                {
                    add("story.view", ProfileUi.Button(
                        () => Loc.T(Safe(Class280.smethod_0) ? "story.view.histograms" : "story.view.leaderboards"),
                        () =>
                        {
                            Class280.smethod_1(!Class280.smethod_0());
                            Ed?.method_33();
                            Speech.Tts.Speak(Loc.T(Class280.smethod_0() ? "story.view.nowboards" : "story.view.nowhistograms"), interrupt: true);
                        }));
                }
                bool canRecord = !Class184.bool_1 && !confidential;
                var record = ProfileUi.Button(() => GameText.T("Record Solution"), () => { if (canRecord) Ed?.method_34(); });
                record.Announcements = new[]
                {
                    record.Announcements[0],
                    new NodeAnnouncement(() => canRecord ? null : Loc.T("value.unavailable"), kind: AnnouncementKinds.Enabled),
                };
                record.OnTooltip = () => Speech.Tts.Speak(GameText.T(Class184.bool_1 ? "This action is disabled."
                    : confidential ? "The details of this assignment are SpaceChem confidential and may not be shared with unprivileged employees."
                    : "After recording your solution, a video file will be saved to your desktop."), interrupt: true);
                add("story.record", record);
            }
            if (e.bool_2)
                add("story.back", ProfileUi.Button(() => GameText.T("Back"), () => Ed?.method_26()));
            add("story.continue", ProfileUi.Button(() => GameText.T("Continue"), () => Ed?.method_30()));
        }

        private static bool Safe(Func<bool> f)
        {
            try { return f(); } catch { return false; }
        }
    }
}
