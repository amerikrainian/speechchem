using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens.Pipeline
{
    public sealed partial class PipelineEditorScreen
    {
        // ---- defense levels: the Enemy stop (after the map), generic over every enemy
        // (Game/DefenseText): the intro card's name and title (+ "destroyed"), where its drawing
        // lies on the map ("columns 21 to 32, rows 3 to 11", live), its parts when the level's
        // enemy has a parts entry ("2 of 3 motors intact", live), its visible state when it has a
        // state entry ("eye open, red", live). The map also names the enemy on
        // the cells its drawing covers. ----

        private const string EnemyStop = "pipeline.enemy";

        private void BuildEnemy(GraphBuilder b)
        {
            var level = DefenseText.Level;
            if (level == null) return;
            b.BeginStop(EnemyStop);
            b.AddItem(ControlId.Structural("pipeline.enemy.name"), ProfileUi.Text(true, () =>
            {
                var lvl = DefenseText.Level;
                string text = DefenseText.EnemyName(lvl) + ", " + DefenseText.EnemyTitle(lvl);
                if (_crashView != null) return text; // as it stood at the crash: still standing
                return DefenseText.Defeated(DefenseText.Enemy(lvl)) ? text + ", " + Loc.T("defense.state.destroyed") : text;
            }));
            var enemy = DefenseText.Enemy(level);
            if (enemy == null) return;
            // The crash view (Crash.cs): the rows as they stood at the error.
            LiveRow(b, "pipeline.enemy.span", () => _crashView != null ? _crashView.EnemySpan : DefenseText.Span(DefenseText.Enemy(DefenseText.Level)));
            if (DefenseText.PartFlags(enemy) != null)
                LiveRow(b, "pipeline.enemy.parts", () => _crashView != null ? _crashView.EnemyParts : DefenseText.PartsText(DefenseText.Enemy(DefenseText.Level)));
            if (DefenseText.HasEnemyState(enemy.GetType()))
                LiveRow(b, "pipeline.enemy.state", () =>
                {
                    if (_crashView != null) return _crashView.EnemyState;
                    var lvl = DefenseText.Level;
                    return DefenseText.EnemyState(lvl, DefenseText.Enemy(lvl)) ?? Loc.T("defense.state.none");
                });
        }

        private static void LiveRow(GraphBuilder b, string id, System.Func<string> text)
            => b.AddItem(ControlId.Structural(id), new NodeVtable
            {
                ControlType = ControlTypes.Text,
                Announcements = new[] { new NodeAnnouncement(text, live: true, kind: AnnouncementKinds.Value) },
            });
    }
}
