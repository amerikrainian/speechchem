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
    /// Challenges (SpaceChem.ChallengeEditor, name-preserved): the game's achievement list. The screen
    /// draws every visible challenge (Levels.list_3 without the hidden ones, bool_0, ordered by int_1)
    /// as a badge + its description, six per column, columns left to right; the badge is in colour
    /// when earned (Challenge.method_1 — the profile's "Challenge_…" unlock) and dimmed otherwise. Two
    /// Tab stops: the challenges as one list in the drawn column order ("description[, completed]",
    /// the description is the game's own text, Challenge.method_2), then Continue. The screen name is
    /// the drawn title. Escape stays native (Continue).
    /// </summary>
    public sealed class ChallengesScreen : Screen
    {
        public override string Key => "challenges";
        public override string ScreenName => GameText.T("Challenges", "ENGLISH ALPHABET ONLY");

        private static SpaceChem.ChallengeEditor Editor => ProfileUi.Settled<SpaceChem.ChallengeEditor>();

        public override bool IsActive() => Editor != null;

        public override void Build(GraphBuilder b)
        {
            if (Editor == null) return;

            b.BeginStop("list");
            var challenges = Visible();
            for (int i = 0; i < challenges.Count; i++)
            {
                var c = challenges[i];
                // By position: int_1 (the badge number) is not unique.
                b.AddItem(ControlId.Structural("challenges.item." + i), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => GameText.Speech(c.method_2()), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => Completed(c) ? Loc.T("levelselect.completed") : null,
                            kind: AnnouncementKinds.Tooltip),
                    },
                });
            }

            b.BeginStop("continue");
            b.AddItem(ControlId.Structural("challenges.continue"),
                ProfileUi.Button(() => GameText.T("Continue"), () => Editor?.method_12()));
        }

        /// <summary>The challenges the screen draws, in its order.</summary>
        private static List<SpaceChem.Challenge> Visible()
        {
            // The game's own query: Where(!hidden).OrderBy(int_1) — a STABLE sort; badge numbers
            // repeat, so ties must keep list_3 order exactly as the draw does.
            return System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(
                System.Linq.Enumerable.Where(LevelsDb.list_3, c => !c.bool_0), c => c.int_1));
        }

        private static bool Completed(SpaceChem.Challenge c)
        {
            try { return c.method_1(); }
            catch { return false; }
        }
    }
}
