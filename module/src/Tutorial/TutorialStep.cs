using System;
using SpeechChem.Screens;
using SpeechChem.Screens.Reactor;
using ReactorModel = SpaceChem.Reactor.Reactor;

namespace SpeechChem.Tutorial
{
    /// <summary>
    /// One tutorial step. Two kinds (user rule 2026-10-23):
    /// - LIVE (<see cref="Latches"/> false): met while <see cref="Met"/> holds — the game's own
    ///   placement steps, so deleting or undoing the piece brings the step back.
    /// - LATCHED: done the first time <see cref="Met"/> holds while the step is the active one, and
    ///   done for good (saved per profile and level) — keys pressed, things looked at, a run.
    /// The active step is the first one not done, as in the game.
    /// </summary>
    internal sealed class TutorialStep
    {
        /// <summary>Stable: the progress file stores it.</summary>
        public string Id;

        /// <summary>Spoken when the step becomes active, and by the repeat key.</summary>
        public Func<TutorialContext, string> Text;

        /// <summary>Spoken once when the step is met going forward (what just happened), before the
        /// next step's text. Optional.</summary>
        public Func<TutorialContext, string> After;

        public Func<TutorialContext, bool> Met;

        public bool Latches;

        /// <summary>A placement's game step: its cell reads "highlighted" while the step is active.</summary>
        public Class288 Target;
    }

    /// <summary>What a step's condition and text can see, refreshed every frame.</summary>
    internal sealed class TutorialContext
    {
        public Class77 Editor;
        public ReactorEditorScreen Screen;

        public ReactorModel Reactor => Editor?.reactor_0;

        /// <summary>The mod's focused screen: the reactor, or a child on it (molecule viewer,
        /// path trace, context menu).</summary>
        public Screen Focused => ScreenManager.Current;

        /// <summary>Whether an action id ran since this step became active.</summary>
        public Func<string, bool> Pressed;

        /// <summary>Whether a step (key 0, its toolbar button) ran since this step became active.</summary>
        public bool Stepped;

        /// <summary>The game's run state: 0 stopped, 1 running, 2 paused.</summary>
        public int RunState => (int)Class258.smethod_16();

        /// <summary>The game's placement test (Class77.method_9 on the step's cell and layer).</summary>
        public bool Placed(Class288 step)
        {
            var m = Reactor?.method_17(step.reactorBin_0);
            return m != null && Editor.method_9(m, step.reactorMember_0);
        }
    }
}
