using System;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;
using SpeechChem.UI.Graph;
using LevelsDb = SpaceChem.Levels.Levels;

namespace SpeechChem.Screens
{
    /// <summary>
    /// "Australium Research Sites" (deob Class67, opened by the main menu's Team Fortress 2 icon): the
    /// TF2 promotion's level map — three Research levels on a blueprint of Australia, the level select's
    /// score panel on the left (Class469 draws LevelSelectEditor.smethod_11), and Back. Every word on
    /// it except the level labels is lettered into the background art (misc/tf2_background, English
    /// only), so the title and the two sentences are mod transcriptions; the diagram legend beside the
    /// moustache drawing (Furrowed Brow / Moustache Originator Module / Moustache) and the footer
    /// plates are decoration and not read. Three Tab stops, in the level select's shape:
    ///
    ///  1. Levels (initial) — the headline, then the levels in the drawn order (top to bottom):
    ///     "name, button, Research[, completed], n of 3", then the closing sentence. The levels have no
    ///     prerequisites and are never drawn locked (Class306.smethod_0 gets unlocked = true). Landing on
    ///     a level points the panel at it (the hover, method_14); Enter opens it (the click, method_16 —
    ///     no story / training intro, unlike the level select).
    ///  2. Scores — the panel's three metrics, worded as on the level select.
    ///  3. Back (method_17).
    ///
    /// Escape stays native (Back). The game's Tab (graph / leaderboard toggle) is ours while modeled.
    /// Focus survives a level on top (the return lands on the level); leaving starts over.
    /// </summary>
    public sealed class AustraliumScreen : Screen
    {
        private const string LevelStop = "levels";
        private const string ScoreStop = "scores";
        private const string ActionStop = "actions";
        private const string LevelPrefix = "australium.level.";

        // Class67.method_12's widgets, in the order it places them.
        private static readonly Type[] SiteLevels = { typeof(Class91), typeof(Class105), typeof(Class96) };

        public override string Key => "australium";
        public override string ScreenName => Loc.T("mainmenu.tf2");
        public override bool KeepStateOnPop => _covered;
        public override object InitialFocusStop => LevelStop;

        private static Class67 Sites => ProfileUi.Settled<Class67>();

        /// <summary>Covered by a level (the screen stays in the chain under it): keep focus, so the
        /// return lands on the level you opened. Leaving through Back / Escape starts over.</summary>
        public override void OnPop()
        {
            _covered = false;
            foreach (var s in GameState.ScreenStack())
                if (s is Class67) { _covered = true; return; }
            _chosen = null;
        }

        private bool _covered;

        public override bool IsActive() => Sites != null;

        public override void Build(GraphBuilder b)
        {
            var sites = Sites;
            if (sites == null) return;

            b.BeginStop(LevelStop);
            b.AddItem(ControlId.Structural("australium.headline"), ProfileUi.Text(true, () => Loc.T("australium.headline")));
            for (int i = 0; i < SiteLevels.Length; i++)
            {
                var level = SiteLevels[i];
                int index = i + 1, count = SiteLevels.Length;
                string id = level.smethod_0();
                var vt = ProfileUi.Button(() => LevelsDb.dictionary_4[level], () => Sites?.method_16(level));
                vt.Announcements = new[]
                {
                    vt.Announcements[0],
                    new NodeAnnouncement(() => LevelSelectScreen.Kind(level), kind: AnnouncementKinds.Value),
                    new NodeAnnouncement(() => LevelsDb.smethod_11(id) ? Loc.T("levelselect.completed") : null,
                        kind: AnnouncementKinds.Tooltip),
                    new NodeAnnouncement(() => Loc.T("nav.position", new { index, count }), kind: AnnouncementKinds.Position),
                };
                vt.SpeaksOwnPosition = true; // the headline and closing rows are not levels
                vt.OnSelect = () => { _chosen = level; Sites?.method_14(level); };
                b.AddItem(ControlId.Structural(LevelPrefix + i), vt);
            }
            b.AddItem(ControlId.Structural("australium.footer"), ProfileUi.Text(true, () => Loc.T("australium.footer")));

            SyncPanel(sites);

            b.BeginStop(ScoreStop);
            for (int metric = 0; metric < 3; metric++)
            {
                int m = metric;
                b.AddItem(ControlId.Structural("australium.score." + m), ProfileUi.Text(true, () => LevelSelectScreen.ScoreRow(Shown(), m)));
            }

            b.BeginStop(ActionStop);
            b.AddItem(ControlId.Structural("australium.back"), ProfileUi.Button(() => GameText.T("Back"), () => Sites?.method_17()));
        }

        // ---- the panel follows the level the user is on. Only the hover (OnSelect) sets it, and the
        // screen rebuilds with an empty panel whenever it regains the top (back from a level, where
        // focus is restored without an OnSelect), so every render re-points it: at the focused level,
        // or, from the other stops, at the level last chosen. ----

        private static Type _chosen;

        private static void SyncPanel(Class67 sites)
        {
            Type want = null;
            object stop = Navigation.FocusedStopKey;
            string key = Navigation.FocusedNodeId?.StructuralKey as string;
            if (LevelStop.Equals(stop))
            {
                if (key != null && key.StartsWith(LevelPrefix, StringComparison.Ordinal)
                    && int.TryParse(key.Substring(LevelPrefix.Length), out int i) && i >= 0 && i < SiteLevels.Length)
                    want = _chosen = SiteLevels[i];
            }
            else want = _chosen;
            if (want == null || sites.class469_0 == null) return;
            var shown = sites.class469_0.method_8();
            if (!shown.bool_0 || shown.method_0() != want.smethod_0()) sites.method_14(want);
        }

        /// <summary>The id of the level the panel shows, or null.</summary>
        private static string Shown()
        {
            var panel = Sites?.class469_0;
            if (panel == null) return null;
            var shown = panel.method_8();
            return shown.bool_0 ? shown.method_0() : null;
        }
    }
}
