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
    /// Level select (SpaceChem.LevelSelectEditor, name-preserved): one planet at a time (enum147_0),
    /// its levels drawn as a map of icons joined by prerequisite paths, the planet bar at the bottom,
    /// the score panel on the left for the level under the mouse, Return to Menu, and Generate Forum
    /// Signature. Four Tab stops, in the Echopunks tab style:
    ///
    ///  1. Planets — the planet bar as a TAB STRIP (one row, selection follows focus: arrowing onto an
    ///     unlocked planet switches to it through the game's own bar handler, method_16; "selected" is
    ///     never spoken). Locked planets draw a lock icon and read "Locked".
    ///  2. Levels (initial) — the planet's name (the drawn header), then each level in campaign order
    ///     (Levels.dictionary_5): "name, button, kind[, completed]" — kind is the game's own subtitle
    ///     (Research / Production / Defense / Execution, "(OPTIONAL)"), completed is the drawn check.
    ///     A level whose prerequisites are unfinished draws only a "?" icon: it reads "Locked" and does
    ///     nothing. Landing on a level shows its scores in the panel (the hover path, method_21); Enter
    ///     opens it (the click path, method_20: level + any story/training intro). Planet 1 also carries
    ///     the orientation video ("SpaceChem: A Brief Introduction", opens the browser like the click).
    ///  3. Scores — the panel's three metrics for the focused level: "metric: BEST n, LAST m" once
    ///     solved (the game's own marker labels), the bare metric name otherwise. The global histogram
    ///     shapes behind them (and the Tab leaderboard view) are not read yet.
    ///  4. Actions — Return to Menu, Generate Forum Signature (drawn DISABLED in some modes: it reads
    ///     unavailable and does nothing).
    ///
    /// Escape stays native (Return to Menu). The game's own Left/Right planet keys and Tab (graph /
    /// leaderboard toggle) are ours while the screen is modeled.
    /// </summary>
    public sealed class LevelSelectScreen : Screen
    {
        private const string PlanetStop = "planets";
        private const string LevelStop = "levels";
        private const string ScoreStop = "scores";
        private const string ActionStop = "actions";
        private const string IntroVideoUrl = "https://www.youtube.com/watch?v=gQumwR326Sk"; // the game's own click target

        public override string Key => "levelselect";
        public override string ScreenName => Loc.T("screen.LevelSelectEditor");
        public override bool KeepStateOnPop => true; // back from a level onto the level you opened
        public override object InitialFocusStop => LevelStop;

        private static SpaceChem.LevelSelectEditor Select => ProfileUi.Settled<SpaceChem.LevelSelectEditor>();

        public override bool IsActive() => Select != null;

        public override void Build(GraphBuilder b)
        {
            var select = Select;
            if (select == null) return;

            BuildPlanets(b, select);
            BuildLevels(b, select);
            SyncPanel(select);
            BuildScores(b, select);
            BuildActions(b);
        }

        // ---- the score panel follows the level the user is on. The hover (method_21) only sets
        // type_0, and only OnSelect runs it — an arrow landing. Coming back from a level the game
        // rebuilds the screen (type_0 cleared) while focus is restored onto the level you left
        // without an OnSelect (stop landings skip it), and the mouse's un-hover (method_22) can clear
        // it too — so the scores read bare metric names for a completed level. Every render re-points
        // the panel: at the focused level, or, from the Scores / Actions stops, at the level last
        // chosen on this planet. ----

        private static Type _chosen;
        private static readonly Dictionary<string, Type> _levelIds = new Dictionary<string, Type>();

        private static void SyncPanel(SpaceChem.LevelSelectEditor select)
        {
            Type want = null;
            object stop = Navigation.FocusedStopKey;
            string key = Navigation.FocusedNodeId?.StructuralKey as string;
            if (LevelStop.Equals(stop))
            {
                if (key != null && key.StartsWith("levelselect.level.", StringComparison.Ordinal))
                    _levelIds.TryGetValue(key.Substring("levelselect.level.".Length), out want);
                if (want != null) _chosen = want;
            }
            else if (ScoreStop.Equals(stop) || ActionStop.Equals(stop))
            {
                if (_chosen != null && _levelIds.ContainsValue(_chosen)) want = _chosen; // on this planet
            }
            if (want != null && select.type_0 != want) select.method_21(want);
        }

        // ---- planets ----

        private static void BuildPlanets(GraphBuilder b, SpaceChem.LevelSelectEditor select)
        {
            var planets = LevelsDb.smethod_0();
            var unlocked = select.method_13();
            b.BeginStop(PlanetStop);
            // A vertical list, one tab per row: Up/Down switch planets (user layout, 2026-09-27).
            for (int i = 0; i < planets.Count; i++)
            {
                var planet = planets[i];
                int index = i;
                bool open = unlocked.Contains(planet);
                var vt = new NodeVtable
                {
                    ControlType = ControlTypes.Tab,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => open ? LevelsDb.dictionary_2[planet] : Loc.T("levelselect.locked"),
                            kind: AnnouncementKinds.Label),
                    },
                    // Selection follows focus, so "selected" is never spoken; the engine still needs the
                    // state for stop landings (Tab lands on the current planet).
                    Selected = () => Equals(Select?.enum147_0, planet),
                };
                if (open)
                {
                    vt.OnSelect = () => SelectPlanet(planet, index);
                    vt.OnActivate = () => SelectPlanet(planet, index);
                }
                b.AddItem(ControlId.Structural("levelselect.planet." + i), vt);
            }
        }

        /// <summary>The planet bar's click (method_16 rebuilds the map). Skipped when the planet is
        /// already current — a rebuild resets the map's arrival animation.</summary>
        private static void SelectPlanet(Enum147 planet, int index)
        {
            var s = Select;
            if (s == null || Equals(s.enum147_0, planet)) return;
            try
            {
                Class428.class14_4.vmethod_0();
                s.method_16(index);
            }
            catch (Exception ex) { Log.Error("[levelselect] planet switch failed", ex); }
        }

        // ---- levels ----

        private static void BuildLevels(GraphBuilder b, SpaceChem.LevelSelectEditor select)
        {
            var planet = select.enum147_0;
            b.BeginStop(LevelStop);
            b.AddItem(ControlId.Structural("levelselect.planetname"),
                ProfileUi.Text(true, () => { var s = Select; return s == null ? null : LevelsDb.dictionary_2[s.enum147_0]; }));

            var levels = LevelsOn(planet);
            _levelIds.Clear();
            foreach (var t in levels) _levelIds[t.smethod_0()] = t;
            int count = levels.Count + ((int)planet == 0 ? 1 : 0);
            int index = 0;

            if ((int)planet == 0)
            {
                int position = ++index;
                var vt = ProfileUi.Button(() => GameText.T("SpaceChem: A Brief Introduction"),
                    () => Class11.smethod_4(IntroVideoUrl, new Struct3[0]));
                vt.Announcements = new[]
                {
                    vt.Announcements[0],
                    new NodeAnnouncement(() => GameText.T("Orientation (EXTERNAL VIDEO)"), kind: AnnouncementKinds.Value),
                    Position(position, count),
                };
                vt.SpeaksOwnPosition = true;
                b.AddItem(ControlId.Structural("levelselect.intro"), vt);
            }

            foreach (var type in levels)
            {
                var level = type;
                int position = ++index;
                string id = level.smethod_0();
                NodeVtable vt;
                if (!LevelsDb.smethod_12(id))
                {
                    vt = ProfileUi.Text(() => Loc.T("levelselect.locked"));
                    vt.Announcements = new[] { vt.Announcements[0], Position(position, count) };
                    vt.SpeaksOwnPosition = true;
                }
                else
                {
                    vt = ProfileUi.Button(() => LevelsDb.dictionary_4[level], () => Select?.method_20(level));
                    vt.Announcements = new[]
                    {
                        vt.Announcements[0],
                        new NodeAnnouncement(() => Kind(level), kind: AnnouncementKinds.Value),
                        new NodeAnnouncement(() => LevelsDb.smethod_11(id) ? Loc.T("levelselect.completed") : null,
                            kind: AnnouncementKinds.Tooltip), // not Selected: the engine reads Selected parts as selection state
                        Position(position, count),
                    };
                    vt.SpeaksOwnPosition = true;
                    vt.OnSelect = () => { _chosen = level; Select?.method_21(level); }; // the hover: the score panel follows
                }
                b.AddItem(ControlId.Structural("levelselect.level." + id), vt);
            }
        }

        /// <summary>The planet's levels in PROGRESSION order: the map joins each level to its
        /// prerequisites (Levels.dictionary_3) with drawn paths, and the registry order
        /// (dictionary_5) is not the campaign order (the first level came fifth on Sernimir II). So:
        /// a topological sort over the in-planet prerequisites, ties broken by map position (top to
        /// bottom, then left to right) — a level is always listed after the levels it needs.</summary>
        private static List<Type> LevelsOn(Enum147 planet)
        {
            var onPlanet = new List<Type>();
            foreach (var t in LevelsDb.dictionary_5.Values)
                if (Equals(LevelsDb.smethod_3(t).enum147_0, planet)) onPlanet.Add(t);

            var pending = new Dictionary<Type, int>();
            foreach (var t in onPlanet)
            {
                int needs = 0;
                foreach (var pre in LevelsDb.dictionary_3[t])
                    if (onPlanet.Contains(pre)) needs++;
                pending[t] = needs;
            }

            var ordered = new List<Type>();
            var ready = new List<Type>();
            foreach (var t in onPlanet) if (pending[t] == 0) ready.Add(t);
            while (ready.Count > 0)
            {
                ready.Sort(ByMapPosition);
                var next = ready[0];
                ready.RemoveAt(0);
                ordered.Add(next);
                foreach (var t in onPlanet)
                {
                    if (pending[t] <= 0 || Array.IndexOf(LevelsDb.dictionary_3[t], next) < 0) continue;
                    if (--pending[t] == 0) ready.Add(t);
                }
            }
            // A prerequisite cycle can't happen in the shipped data; if one did, list the rest anyway.
            foreach (var t in onPlanet) if (!ordered.Contains(t)) ordered.Add(t);
            return ordered;
        }

        private static int ByMapPosition(Type a, Type b)
        {
            var pa = LevelsDb.dictionary_6[a];
            var pb = LevelsDb.dictionary_6[b];
            int c = pa.int_1.CompareTo(pb.int_1);
            return c != 0 ? c : pa.int_0.CompareTo(pb.int_0);
        }

        /// <summary>The subtitle Class306.smethod_0 draws above a level's name.</summary>
        private static string Kind(Type t)
        {
            string text = null;
            if (t == typeof(Class148)) text = GameText.T("Execution");
            else if (t.IsSubclassOf(typeof(Class84))) text = GameText.T("Research");
            else if (t.IsSubclassOf(typeof(Class123))) text = GameText.T("Production");
            else if (t.IsSubclassOf(typeof(SpaceChem.Levels.DefenseLevelEditor))) text = GameText.T("Defense");
            if (LevelsDb.list_1.Contains(t))
                text = (text == null ? "" : text + " ") + GameText.T("(OPTIONAL)");
            return text;
        }

        private static NodeAnnouncement Position(int index, int count)
            => new NodeAnnouncement(() => count > 1 ? Loc.T("nav.position", new { index, count }) : null,
                kind: AnnouncementKinds.Position);

        // ---- scores ----

        private static void BuildScores(GraphBuilder b, SpaceChem.LevelSelectEditor select)
        {
            b.BeginStop(ScoreStop);
            b.AddItem(ControlId.Structural("levelselect.score.cycles"), ProfileUi.Text(true, () => ScoreRow(0)));
            b.AddItem(ControlId.Structural("levelselect.score.reactors"), ProfileUi.Text(true, () => ScoreRow(1)));
            b.AddItem(ControlId.Structural("levelselect.score.symbols"), ProfileUi.Text(true, () => ScoreRow(2)));
        }

        /// <summary>One metric of the panel for the level it shows (type_0, set by the hover path).</summary>
        private static string ScoreRow(int metric)
        {
            string name = metric == 0 ? SpaceChem.Graph.string_0 : metric == 1 ? SpaceChem.Graph.string_1 : SpaceChem.Graph.string_2;
            var s = Select;
            var level = s?.type_0;
            if (level == null) return name;
            var state = LevelsDb.smethod_9(level.smethod_0());
            if (!state.bool_0 || state.score_0 == null || state.score_1 == null) return name;
            int best = Metric(state.score_1, metric), last = Metric(state.score_0, metric);
            return Loc.T("levelselect.score", new
            {
                metric = name,
                bestLabel = GameText.T("BEST"),
                best,
                lastLabel = GameText.T("LAST"),
                last,
            });
        }

        private static int Metric(SpaceChem.Score score, int metric)
            => metric == 0 ? score.int_0 : metric == 1 ? score.int_1 : score.int_2;

        // ---- actions ----

        private static void BuildActions(GraphBuilder b)
        {
            b.BeginStop(ActionStop);
            b.AddItem(ControlId.Structural("levelselect.return"),
                ProfileUi.Button(() => GameText.T("Return to Menu"), () => Select?.method_17()));

            // Class184.bool_1 is the draw's own gate: the button is built disabled ("DISABLED").
            bool disabled = Class184.bool_1;
            var sig = ProfileUi.Button(() => GameText.T("Generate Forum Signature"), () => { if (!Class184.bool_1) Select?.method_23(); });
            if (disabled)
                sig.Announcements = new[] { sig.Announcements[0], new NodeAnnouncement(() => Loc.T("value.unavailable"), kind: AnnouncementKinds.Enabled) };
            b.AddItem(ControlId.Structural("levelselect.signature"), sig);
        }
    }
}
