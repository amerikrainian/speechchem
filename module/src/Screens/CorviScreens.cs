using System;
using System.Collections.Generic;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;
using SpeechChem.UI.Graph;
using LevelsDb = SpaceChem.Levels.Levels;

namespace SpeechChem.Screens
{
    /// <summary>
    /// 63 Corvi (deob Class70, the quantum DLC; opened by the main menu's planet): the level select's
    /// map for planet 10 (the Enum147 the DLC's levels carry) with its score panel on the left and, below
    /// the map, the "Transcripts - 63 Corvi Mission" pane — one tab per transcript unlocked so far
    /// (QuantumDLC1..8 in the story text, each unlocked by finishing a level: method_23), the shown
    /// transcript's text, and Return to Menu. The pane is part of the screen, not a popup. Five Tab stops:
    ///
    ///  1. Levels (initial) — as on the level select: progression order, "name, button, kind[,
    ///     completed], n of m", unavailable levels "Locked" (the drawn "?"). Landing on a level points
    ///     the panel at it (the hover, method_16); Enter opens it (the click, method_15: level + intro).
    ///  2. Scores — the panel's three metrics, worded as on the level select.
    ///  3. Transcripts — the tabs as a vertical tab strip, selection follows focus (method_21 rebuilds
    ///     the pane; "selected" is never spoken). The drawn tab truncates the title; we read it whole.
    ///  4. Transcript — the pane title, then the shown transcript's lines (the story cleaner: the
    ///     signature illustration and the layout marks dropped).
    ///  5. Return to Menu (method_14).
    ///
    /// Escape stays native (Return to Menu). The game's Tab (graph / leaderboard toggle) is ours while
    /// modeled. Focus survives a level on top (the return lands on the level); leaving starts over.
    /// </summary>
    public sealed class CorviScreen : Screen
    {
        private const string LevelStop = "levels";
        private const string ScoreStop = "scores";
        private const string TabStop = "transcripts";
        private const string TextStop = "transcript";
        private const string ActionStop = "actions";
        private const string LevelPrefix = "corvi.level.";
        private const int Transcripts = 8;
        private static readonly Enum147 Planet = (Enum147)10; // Class70.method_19's filter

        public override string Key => "corvi";
        public override string ScreenName => LevelsDb.dictionary_2[Planet];
        public override bool KeepStateOnPop => _covered;
        public override object InitialFocusStop => LevelStop;

        private static Class70 Corvi => ProfileUi.Settled<Class70>();

        public override bool IsActive() => Corvi != null;

        /// <summary>Covered by a level (the screen stays in the chain under it): keep focus.
        /// Leaving through Return to Menu / Escape starts over.</summary>
        public override void OnPop()
        {
            _covered = false;
            foreach (var s in GameState.ScreenStack())
                if (s is Class70) { _covered = true; return; }
            _chosen = null;
        }

        private bool _covered;

        public override void Build(GraphBuilder b)
        {
            var corvi = Corvi;
            if (corvi == null) return;

            BuildLevels(b);
            SyncPanel(corvi);

            b.BeginStop(ScoreStop);
            for (int metric = 0; metric < 3; metric++)
            {
                int m = metric;
                b.AddItem(ControlId.Structural("corvi.score." + m),
                    ProfileUi.Text(true, () => LevelSelectScreen.ScoreRow(Corvi?.type_0?.smethod_0(), m)));
            }

            BuildTranscripts(b, corvi);

            b.BeginStop(ActionStop);
            b.AddItem(ControlId.Structural("corvi.return"),
                ProfileUi.Button(() => GameText.T("Return to Menu"), () => Corvi?.method_14()));
        }

        // ---- levels ----

        private static readonly Dictionary<string, Type> _levelIds = new Dictionary<string, Type>();
        private static Type _chosen;

        private static void BuildLevels(GraphBuilder b)
        {
            var levels = LevelSelectScreen.LevelsOn(Planet);
            _levelIds.Clear();
            b.BeginStop(LevelStop);
            for (int i = 0; i < levels.Count; i++)
            {
                var level = levels[i];
                int index = i + 1, count = levels.Count;
                string id = level.smethod_0();
                _levelIds[id] = level;
                var position = new NodeAnnouncement(() => count > 1 ? Loc.T("nav.position", new { index, count }) : null,
                    kind: AnnouncementKinds.Position);
                NodeVtable vt;
                // Class306.smethod_0's lock: prerequisites unfinished and neither unlock-all flag set.
                if (!LevelsDb.smethod_12(id) && !SpaceChem.LevelSelectEditor.bool_2 && !SpaceChem.LevelSelectEditor.bool_3)
                {
                    vt = ProfileUi.Text(() => Loc.T("levelselect.locked"));
                    vt.Announcements = new[] { vt.Announcements[0], position };
                }
                else
                {
                    vt = ProfileUi.Button(() => LevelsDb.dictionary_4[level], () => Corvi?.method_15(level));
                    vt.Announcements = new[]
                    {
                        vt.Announcements[0],
                        new NodeAnnouncement(() => LevelSelectScreen.Kind(level), kind: AnnouncementKinds.Value),
                        new NodeAnnouncement(() => LevelsDb.smethod_11(id) ? Loc.T("levelselect.completed") : null,
                            kind: AnnouncementKinds.Tooltip),
                        position,
                    };
                    vt.OnSelect = () => { _chosen = level; Corvi?.method_16(level); }; // the hover
                }
                vt.SpeaksOwnPosition = true;
                b.AddItem(ControlId.Structural(LevelPrefix + id), vt);
            }
        }

        // ---- the panel follows the level the user is on. The hover (method_16) only sets type_0, which
        // opening a level and the mouse's un-hover clear, and focus restored on a return lands without
        // an OnSelect — so every render re-points it: at the focused level, or, from the other stops,
        // at the level last chosen. ----

        private static void SyncPanel(Class70 corvi)
        {
            Type want = null;
            string key = Navigation.FocusedNodeId?.StructuralKey as string;
            if (LevelStop.Equals(Navigation.FocusedStopKey))
            {
                if (key != null && key.StartsWith(LevelPrefix, StringComparison.Ordinal))
                    _levelIds.TryGetValue(key.Substring(LevelPrefix.Length), out want);
                if (want != null) _chosen = want;
            }
            else want = _chosen;
            if (want != null && corvi.type_0 != want) corvi.method_16(want);
        }

        // ---- transcripts ----

        private static void BuildTranscripts(GraphBuilder b, Class70 corvi)
        {
            b.BeginStop(TabStop);
            for (int i = 1; i <= Transcripts; i++)
            {
                if (!corvi.method_23(i)) continue; // not unlocked: no tab (method_24's own filter)
                int n = i;
                var vt = new NodeVtable
                {
                    ControlType = ControlTypes.Tab,
                    Announcements = new[] { new NodeAnnouncement(() => Entry(n)?.string_0, kind: AnnouncementKinds.Label) },
                    Selected = () => Corvi?.int_0 == n,
                    OnSelect = () => ShowTranscript(n),
                    OnActivate = () => ShowTranscript(n),
                };
                b.AddItem(ControlId.Structural("corvi.tab." + n), vt);
            }

            b.BeginStop(TextStop);
            b.AddItem(ControlId.Structural("corvi.text.title"),
                ProfileUi.Text(true, () => GameText.T("Transcripts - 63 Corvi Mission")));
            var lines = StoryInfoScreen.CleanStory(Entry(corvi.int_0)?.string_1);
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                b.AddItem(ControlId.Structural("corvi.text." + i), ProfileUi.Text(true, () => line));
            }
        }

        private static Class248 Entry(int n)
        {
            try { return Class177.smethod_1().TryGetValue("QuantumDLC" + n, out var e) ? e : null; }
            catch { return null; }
        }

        /// <summary>The transcript tab's click (GClass15: click sound, then method_21, which rebuilds
        /// the pane). Skipped when already shown.</summary>
        private static void ShowTranscript(int n)
        {
            var c = Corvi;
            if (c == null || c.int_0 == n) return;
            ProfileUi.Press(() => c.method_21(n));
        }
    }
}
