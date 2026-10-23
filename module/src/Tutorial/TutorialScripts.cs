using System;
using System.Collections.Generic;
using SpaceChem.Reactor;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.Screens;
using SpeechChem.Screens.Reactor;

namespace SpeechChem.Tutorial
{
    /// <summary>
    /// The mod's tutorials, by level id (GoalTracker.string_0). A script gets the game's own step
    /// list for the level (Class424.list_0) and reuses its placement steps — the cell, layer and
    /// piece the game checks — with the mod's text; it returns null when the list isn't what it
    /// expects, and the level falls back to the game's tutorial. Texts are "tut.&lt;level&gt;.&lt;id&gt;"
    /// (+ ".after") in ui.json; keys in them come from the live bindings (<see cref="Args"/>).
    /// </summary>
    internal static class TutorialScripts
    {
        public static Func<List<Class288>, List<TutorialStep>> For(string levelId)
        {
            switch (levelId)
            {
                case "research-example-1": return Example1;
                case "research-tutorial-1": return Tutorial1;
                default: return null;
            }
        }

        // ---- Of Pancakes and Spaceships (Class116): the game's steps are 0 red START at 5, 2
        // facing left, 1 in alpha at 4, 2, 2 grab drop at 3, 2, 3-4 arrows down / right at 2, 2
        // and 2, 4, 5 rotate counter-clockwise at 6, 4, 6-7 arrows up / left at 9, 4 and 9, 2,
        // 8 grab drop at 8, 2, 9 out psi at 7, 2, 10 the text-only "start the reactor". ----

        private static List<TutorialStep> Example1(List<Class288> game)
        {
            if (game.Count != 11) return null;
            const string P = "tut.ex1.";
            return new List<TutorialStep>
            {
                Latch(P, "welcome", c => c.Pressed("screen.reactor.jump.red"), after: true),
                Place(P, "start", game[0]),
                Latch(P, "alpha", c => InZone(c, input: true, index: 0), after: true),
                Latch(P, "viewer", c => c.Focused is MoleculeViewerScreen, after: true),
                Latch(P, "viewer.close", c => !(c.Focused is MoleculeViewerScreen), after: true),
                Latch(P, "psi", c => c.Pressed("screen.reactor.port.psi"), after: true),
                Place(P, "input", game[1], after: true),
                Place(P, "grab", game[2]),
                Place(P, "down", game[3]),
                Place(P, "right", game[4]),
                Place(P, "rotate", game[5]),
                Place(P, "up", game[6]),
                Place(P, "left", game[7]),
                Place(P, "drop", game[8]),
                Place(P, "output", game[9]),
                Latch(P, "trace", c => c.Pressed("screen.reactor.trace.red")),
                Latch(P, "trace.close", c => c.Focused is ReactorEditorScreen),
                Latch(P, "step", c => c.Stepped && !Patches.StepControl.Active && c.RunState == 2, after: true),
                Latch(P, "run", c => c.RunState == 1 && !Patches.StepControl.Active),
            };
        }

        // ---- Slightly Different (Class100): the game's steps are 0 blue START at 5, 7 facing left,
        // 1 blue in beta at 3, 7 (its text: drop a red alpha input and change it), 2 blue arrow up
        // at 2, 7 (its text: switch to blue), 3 blue arrow right at 2, 6, 4 blue grab drop at 3, 6
        // (its text: finish the program yourself) — and nothing after it. The mod stops where the
        // game does: no step after the grab, the rest of the solution is the player's. ----

        private static List<TutorialStep> Tutorial1(List<Class288> game)
        {
            if (game.Count != 5) return null;
            const string P = "tut.t1.";
            var input = game[1];
            // With blue active the palette's input is already a blue beta one (Class715.method_4
            // switches inputs and outputs with the layer), so the step says so instead.
            var place = Live(P, "input.place", input, c => InputAt(c, input, ReactorText.Red) || InputAt(c, input, ReactorText.Blue));
            place.Text = c => Loc.T(P + (ReactorEditorScreen.BlueActive ? "input.place.blue" : "input.place"), new Args(c, input));
            return new List<TutorialStep>
            {
                Latch(P, "welcome", c => c.Pressed("screen.reactor.jump.blue"), after: true),
                Place(P, "start", game[0]),
                Latch(P, "beta", c => InZone(c, input: true, index: 1), after: true),
                Latch(P, "viewer", c => c.Focused is MoleculeViewerScreen),
                Latch(P, "viewer.close", c => !(c.Focused is MoleculeViewerScreen), after: true),
                Latch(P, "omega", c => c.Pressed("screen.reactor.port.omega")),
                Latch(P, "skip", c => c.Pressed("ui.regionPrev") || c.Pressed("ui.regionNext")
                                      || c.Pressed("screen.reactor.skip.left") || c.Pressed("screen.reactor.skip.right")),
                // The game's input step in three live parts, each met by the piece in the cell (so a
                // delete or an undo goes back): any input, then a blue one, then the blue beta input.
                place,
                Live(P, "input.colour", input, c => InputAt(c, input, ReactorText.Blue)),
                Place(P, "input", input, after: true),
                // Switching the active colour: met while blue is active, and for good once the next
                // blue piece is down (switching back to red later doesn't ask again).
                Live(P, "layer", null, c => ReactorEditorScreen.BlueActive || c.Placed(game[2]), after: true),
                Place(P, "up", game[2]),
                Place(P, "right", game[3]),
                Place(P, "grab", game[4], after: true),
            };
        }

        // ---- builders ----

        private static TutorialStep Latch(string prefix, string id, Func<TutorialContext, bool> met, bool after = false)
            => new TutorialStep
            {
                Id = id,
                Latches = true,
                Met = met,
                Text = c => Loc.T(prefix + id, new Args(c, null)),
                After = after ? (Func<TutorialContext, string>)(c => Loc.T(prefix + id + ".after", new Args(c, null))) : null,
            };

        private static TutorialStep Place(string prefix, string id, Class288 game, bool after = false)
            => new TutorialStep
            {
                Id = id,
                Met = c => c.Placed(game),
                Target = game,
                Text = c => Loc.T(prefix + id, new Args(c, game)),
                After = after ? (Func<TutorialContext, string>)(c => Loc.T(prefix + id + ".after", new Args(c, game))) : null,
            };

        /// <summary>A live step with its own condition, pointing at a game step's cell (none: no
        /// highlighted cell).</summary>
        private static TutorialStep Live(string prefix, string id, Class288 game, Func<TutorialContext, bool> met, bool after = false)
        {
            var step = Place(prefix, id, game, after);
            step.Met = met;
            return step;
        }

        /// <summary>An input instruction of either kind in a game step's cell, on a colour's layer.</summary>
        private static bool InputAt(TutorialContext c, Class288 game, int layer)
            => c.Reactor?.method_17(new ReactorBin(game.reactorBin_0.vector2i_0, (Enum114)layer)) is InputInstruction;

        /// <summary>The grid cursor in a zone (input or output, 0 / 1 as the panels count them).</summary>
        private static bool InZone(TutorialContext c, bool input, int index)
            => ReactorEditorScreen.CursorOnGrid
               && ReactorEditorScreen.ZoneOf(c.Reactor, c.Screen.CursorX, c.Screen.CursorY, out bool isInput, out int i)
               && isInput == input && i == index;

        /// <summary>The placeholders a tutorial text can use: keys from the live bindings, and for a
        /// placement its cell (1-based) and palette letter.</summary>
        private sealed class Args
        {
            private readonly TutorialContext _c;
            private readonly Class288 _game;

            public Args(TutorialContext c, Class288 game)
            {
                _c = c;
                _game = game;
            }

            public string repeat => Tutorial.KeyOf("screen.reactor.tutorial");
            public string jumpRed => Tutorial.KeyOf("screen.reactor.jump.red");
            public string jumpBlue => Tutorial.KeyOf("screen.reactor.jump.blue");
            public string traceBlue => Tutorial.KeyOf("screen.reactor.trace.blue");
            public string omega => Tutorial.KeyOf("screen.reactor.port.omega");
            public string skipUp => Tutorial.KeyOf("ui.regionPrev");
            public string skipDown => Tutorial.KeyOf("ui.regionNext");
            public string skipLeft => Tutorial.KeyOf("screen.reactor.skip.left");
            public string skipRight => Tutorial.KeyOf("screen.reactor.skip.right");
            public string paramPrev => Tutorial.KeyOf("screen.reactor.param.prev");
            public string paramNext => Tutorial.KeyOf("screen.reactor.param.next");
            public string valuePrev => Tutorial.KeyOf("screen.reactor.value.prev");
            public string primaryPrev => Tutorial.KeyOf("screen.reactor.primary.prev");
            public string primaryNext => Tutorial.KeyOf("screen.reactor.primary.next");
            public string layer => Tutorial.KeyOf("screen.reactor.layer");
            public string menu => Tutorial.KeyOf("ui.secondary");
            public string traceRed => Tutorial.KeyOf("screen.reactor.trace.red");
            public string molecule => Tutorial.KeyOf("screen.reactor.molecule");
            public string psi => Tutorial.KeyOf("screen.reactor.port.psi");
            public string back => Tutorial.KeyOf("ui.back");
            public string cut => Tutorial.KeyOf("screen.reactor.cut");
            public string paste => Tutorial.KeyOf("screen.reactor.paste");
            public string delete => Tutorial.KeyOf("screen.reactor.delete");
            public string valueNext => Tutorial.KeyOf("screen.reactor.value.next");
            public string step => Tutorial.KeyOf("screen.reactor.step");

            public string cell
            {
                get
                {
                    if (_game == null) return "";
                    var v = _game.reactorBin_0.vector2i_0;
                    return Loc.T("reactor.cell", new { x = v.int_0 + 1, y = v.int_1 + 1 });
                }
            }

            public string key
            {
                get
                {
                    int k = _game?.reactorMember_0 is Instruction i ? ReactorEditorScreen.SlotKeyFor(i, _c.Editor) : -1;
                    return k < 0 ? "?" : ReactorEditorScreen.KeyLetter(k);
                }
            }
        }
    }
}
