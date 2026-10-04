using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SpeechChem.Game;
using HarmonyLib;
using Impeller;
using SpaceChem;

namespace SpeechChem.Patches
{
    /// <summary>
    /// Two click-gated screens keep only RENDERED text (Impeller Scene objects), never the strings, so
    /// the words are captured where the game builds them — the model's own inputs, not pixels:
    ///
    ///  • Credit cards (deob Class152): smethod_11(name, role, roleY, slide, nameY) builds one card
    ///    from two strings. A postfix records them against the returned card.
    ///  • The end-game epilogue scroll (deob Class81): smethod_13() builds it from a fixed list of
    ///    story-text section keys, each through smethod_12(key, illustration). A prefix on smethod_13
    ///    opens a collection window, smethod_12's postfix appends each key, smethod_13's postfix files
    ///    the keys against the returned screen. Section bodies are read from the game's story text
    ///    table (Class177, text\&lt;lang&gt;.text) at speak time.
    ///
    /// Both builders run only when the screen is about to show (the credits after the final level,
    /// the epilogue after the last boss), long after the first-tick arming. Entries are held in
    /// ConditionalWeakTables, so a finished screen's text goes away with it.
    /// </summary>
    internal static class GateTextCapture
    {
        /// <summary>One drawn line of a credit card: its text and its vertical position (screen y,
        /// top = 0) so cards read top to bottom as drawn.</summary>
        internal struct CardLine
        {
            public string Text;
            public int Y;
        }

        private static readonly ConditionalWeakTable<object, List<CardLine>> Cards = new ConditionalWeakTable<object, List<CardLine>>();
        private static readonly ConditionalWeakTable<object, List<string>> Epilogues = new ConditionalWeakTable<object, List<string>>();
        private static List<string> _collecting;

        public static void Apply(Harmony harmony)
        {
            try
            {
                var self = typeof(GateTextCapture);
                harmony.Patch(Expr.MethodOf(() => Class152.smethod_11(null, null, 0, default(Enum98), 0)),
                    postfix: new HarmonyMethod(self, nameof(AfterCard)));
                harmony.Patch(Expr.MethodOf(() => default(StoryTrainingPerformanceEditor).method_28()),
                    postfix: new HarmonyMethod(self, nameof(AfterEnemyCard)));
                harmony.Patch(Expr.MethodOf(() => Class81.smethod_13()),
                    prefix: new HarmonyMethod(self, nameof(BeforeEpilogue)),
                    postfix: new HarmonyMethod(self, nameof(AfterEpilogue)));
                harmony.Patch(Expr.MethodOf(() => Class81.smethod_12(null, default(Struct116<string>))),
                    postfix: new HarmonyMethod(self, nameof(AfterSection)));
                Log.Info("[patch] click-gate text capture armed");
            }
            catch (Exception ex) { Log.Error("[patch] click-gate text capture failed to apply", ex); }
        }

        /// <summary>The captured lines of a credit card, top to bottom, or null.</summary>
        public static List<CardLine> CardLines(object card)
        {
            if (card == null) return null;
            return Cards.TryGetValue(card, out var lines) ? lines : null;
        }

        /// <summary>The captured story-section keys of an epilogue screen, in order, or null.</summary>
        public static List<string> EpilogueKeys(object screen)
        {
            if (screen == null) return null;
            return Epilogues.TryGetValue(screen, out var keys) ? keys : null;
        }

        // __0 name (large font), __1 role (small font), __2 role y, __4 name y.
        private static void AfterCard(string __0, string __1, int __2, int __4, Class53 __result)
        {
            try
            {
                if (__result == null) return;
                var lines = new List<CardLine>
                {
                    new CardLine { Text = __0, Y = __4 },
                    new CardLine { Text = __1, Y = __2 },
                };
                lines.Sort((a, b) => a.Y.CompareTo(b.Y));
                Cards.Remove(__result);
                Cards.Add(__result, lines);
            }
            catch (Exception ex) { Log.Error("[capture] credit card", ex); }
        }

        /// <summary>A defense level's enemy card (StoryTrainingPerformanceEditor.method_28): a
        /// Class152 built straight from two Scenes, the level's title (vmethod_7, large, y 550) and
        /// name (vmethod_6, small, y 250) — not through smethod_11, so captured here; the card it
        /// just pushed is the top screen.</summary>
        private static void AfterEnemyCard(StoryTrainingPerformanceEditor __instance)
        {
            try
            {
                var card = Class53.smethod_0() as Class152;
                var level = __instance.struct116_5.bool_0 ? __instance.struct116_5.method_0() : null;
                if (card == null || level == null) return;
                var lines = new List<CardLine>
                {
                    new CardLine { Text = GameText.Speech(level.vmethod_6()), Y = 250 },
                    new CardLine { Text = GameText.Speech(level.vmethod_7()), Y = 550 },
                };
                Cards.Remove(card);
                Cards.Add(card, lines);
            }
            catch (Exception ex) { Log.Error("[capture] enemy card", ex); }
        }

        private static void BeforeEpilogue()
        {
            _collecting = new List<string>();
        }

        private static void AfterSection(string __0)
        {
            try { _collecting?.Add(__0); }
            catch { }
        }

        private static void AfterEpilogue(Class81 __result)
        {
            try
            {
                if (__result != null && _collecting != null)
                {
                    Epilogues.Remove(__result);
                    Epilogues.Add(__result, _collecting);
                }
            }
            catch (Exception ex) { Log.Error("[capture] epilogue", ex); }
            finally { _collecting = null; }
        }
    }
}
