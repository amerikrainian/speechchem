using System;
using System.Collections.Generic;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.Patches;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens
{
    /// <summary>
    /// SpaceChem's "click anywhere" screens: full-screen cards that advance on ANY left click, with no
    /// hit test, and read no keys at all (the boot splash is NOT one of them: Space/Escape skip it and
    /// it advances on its own). Each is modeled as a tiny graph: the drawn text as read-only rows,
    /// then a Continue button. Rows are spoken as they APPEAR (the cards reveal text on timers), and
    /// arrows re-read them. Activating Continue replays the gate's own advance: a synthetic left click
    /// through SDL's queue (<see cref="SyntheticClick"/>), so the game runs its real path — sound,
    /// transition, pop — byte-identically to a mouse click; the epilogue, which advances by scrolling
    /// rather than clicking, gets its own finish (see <see cref="EpilogueScreen"/>).
    /// </summary>
    public abstract class ClickGateScreen : Screen
    {
        private object _watched;
        private int _spoken;

        /// <summary>The game screen this gate models, when it is the top of the chain; else null.</summary>
        protected abstract Class53 Gate(Class53 top);

        /// <summary>The text the gate currently DRAWS, in reading order (timed reveals included).</summary>
        protected abstract List<string> Lines(Class53 gate);

        /// <summary>Advance the gate the way the game expects. Default: a synthetic left click.</summary>
        protected virtual void Advance(Class53 gate) => SyntheticClick.Click();

        /// <summary>The button's label. Default: the game's own "Continue".</summary>
        protected virtual string ContinueLabel => GameText.T("Continue");

        protected Class53 Current => Gate(GameApi.TopScreen());

        public override bool IsActive() => Current != null;

        public override void Build(GraphBuilder b)
        {
            var gate = Current;
            if (gate == null) return;
            var lines = Lines(gate);
            for (int i = 0; i < lines.Count; i++)
            {
                int index = i;
                b.AddItem(ControlId.Structural(Key + ".line." + index), new NodeVtable
                {
                    ControlType = ControlTypes.Text,
                    Announcements = new[] { new NodeAnnouncement(() => LineAt(index), kind: AnnouncementKinds.Label) },
                });
            }
            var cont = ControlId.Structural(Key + ".continue");
            b.AddItem(cont, new NodeVtable
            {
                ControlType = ControlTypes.Button,
                Announcements = new[] { new NodeAnnouncement(() => ContinueLabel, kind: AnnouncementKinds.Label) },
                OnActivate = () =>
                {
                    var g = Current;
                    if (g != null) Advance(g);
                },
            });
            b.SetStart(cont);
        }

        private string LineAt(int index)
        {
            var gate = Current;
            if (gate == null) return null;
            var lines = Lines(gate);
            return index < lines.Count ? lines[index] : null;
        }

        /// <summary>Speak lines as the card reveals them. Watermarked per GAME screen instance, so a
        /// second card (the credits sequence) starts over.</summary>
        public override void OnUpdate()
        {
            var gate = Current;
            if (gate == null) return;
            if (!ReferenceEquals(gate, _watched))
            {
                _watched = gate;
                _spoken = 0;
            }
            var lines = Lines(gate);
            while (_spoken < lines.Count)
            {
                string line = lines[_spoken++];
                if (!string.IsNullOrEmpty(line)) Speech.Tts.Speak(line);
            }
        }

        public override void OnPop()
        {
            _watched = null;
            _spoken = 0;
        }
    }

    /// <summary>
    /// "{ship} has been lost." (deob Class154): pushed when a defense level is lost. Draw timeline
    /// (seconds since the card was pushed, all drawn by the card itself): the ship shakes and explodes
    /// until 2, the loss line appears at float_2 (5), "click to continue" two seconds later. A click
    /// works from the first frame (vmethod_2 tests only Class259.bool_0) and returns to level select
    /// through the game's transition. The ship name is the game's own: Levels.dictionary_2 per
    /// Enum147, or "The Prometheus" for the final defeat (value 8).
    /// </summary>
    public sealed class ShipLostScreen : ClickGateScreen
    {
        private const int PrometheusShip = 8;

        public override string Key => "gate.shiplost";

        protected override Class53 Gate(Class53 top) => top as Class154;

        protected override List<string> Lines(Class53 gate)
        {
            var lines = new List<string>();
            var card = (Class154)gate;
            double elapsed = (Class280.struct102_0.timeSpan_0 - card.timeSpan_0).TotalSeconds;
            if (elapsed <= Class154.float_2) return lines;
            string ship = (int)card.enum147_0 == PrometheusShip
                ? GameText.T("The Prometheus", "ENGLISH ALPHABET ONLY")
                : SpaceChem.Levels.Levels.dictionary_2[card.enum147_0];
            lines.Add(ship + " " + GameText.T("has been lost.", "ENGLISH ALPHABET ONLY"));
            return lines;
        }
    }

    /// <summary>
    /// The credits (deob Class153 running a list of deob Class152 cards): each card slides a name and
    /// a role in, holds, and slides out after four seconds; a click skips the card. Only the card is
    /// the gate — between cards the sequencer itself (Class153) is up, reads nothing, and fades out
    /// after the last one. Card text is captured at construction (<see cref="GateTextCapture"/>).
    /// </summary>
    public sealed class CreditsScreen : ClickGateScreen
    {
        public override string Key => "gate.credits";

        protected override Class53 Gate(Class53 top) => top as Class152;

        protected override List<string> Lines(Class53 gate)
        {
            var lines = new List<string>();
            var captured = GateTextCapture.CardLines(gate);
            if (captured != null)
                foreach (var l in captured) lines.Add(l.Text);
            return lines;
        }
    }

    /// <summary>
    /// The end-game epilogue (deob Class81, "CLICK TO SCROLL STORY"): a long illustrated text column
    /// that scrolls up while the left button is HELD (200 units/s) and pops itself once scrolled past
    /// its end (float_0 &gt; 768 + height) — the game's only exit. Its paragraphs are the story text
    /// table's sections (Class177, keys captured by <see cref="GateTextCapture"/>). Continue performs
    /// the game's exit condition directly: it moves the scroll past the end, and the card's own next
    /// update pops it — no held-button emulation, which would take a minute of scrolling.
    /// </summary>
    public sealed class EpilogueScreen : ClickGateScreen
    {
        public override string Key => "gate.epilogue";

        protected override Class53 Gate(Class53 top) => top as Class81;

        protected override List<string> Lines(Class53 gate)
        {
            var lines = new List<string>();
            var keys = GateTextCapture.EpilogueKeys(gate);
            if (keys == null) return lines;
            var sections = Class177.smethod_1();
            foreach (var key in keys)
            {
                if (key != null && sections.TryGetValue(key, out var section))
                    lines.Add(GameText.Speech(section.string_1));
            }
            return lines;
        }

        protected override void Advance(Class53 gate)
        {
            var story = (Class81)gate;
            try
            {
                story.float_0 = 768f + story.scene_0.vmethod_0().vector2i_1.int_1 + 1f;
            }
            catch (Exception ex) { Log.Error("[epilogue] finish failed", ex); }
        }
    }
}
