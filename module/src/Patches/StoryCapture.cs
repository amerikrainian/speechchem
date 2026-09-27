using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Impeller;
using SpaceChem;
using SpeechChem.Game;

namespace SpeechChem.Patches
{
    /// <summary>
    /// Text for the Story / Training / Performance screen, which the game keeps only as glyph
    /// Scenes (the shipping exe's literals are encrypted, so nothing can be read from the IL):
    ///
    ///  • <see cref="Record"/> runs a builder while every string handed to ExtendedFont.method_5 (the
    ///    training captions and one-line texts) is recorded; the screen calls a training entry's own
    ///    factory (Class170.smethod_5..17) under it. (Story bodies need no capture: they are read raw
    ///    from the game's text table, Class177.)
    ///  • The current tab: method_21 (Story), method_22 (Training), method_24 (Performance) each
    ///    rebuild the widget tree for their tab; a postfix records which ran last per editor
    ///    (ConditionalWeakTable — gone with the screen).
    /// </summary>
    internal static class StoryCapture
    {
        public enum Tab { Story, Training, Performance }

        private sealed class TabBox { public Tab Tab; }

        private static readonly ConditionalWeakTable<StoryTrainingPerformanceEditor, TabBox> Tabs =
            new ConditionalWeakTable<StoryTrainingPerformanceEditor, TabBox>();

        [ThreadStatic] private static List<string> _lines;

        public static void Apply(Harmony harmony)
        {
            try
            {
                var self = typeof(StoryCapture);
                harmony.Patch(Expr.MethodOf(() => default(ExtendedFont).method_5(null, default(Enum100), default(Struct116<int>))),
                    prefix: new HarmonyMethod(self, nameof(BeforeLine)));
                harmony.Patch(Expr.MethodOf(() => default(StoryTrainingPerformanceEditor).method_21()),
                    postfix: new HarmonyMethod(self, nameof(AfterStory)));
                harmony.Patch(Expr.MethodOf(() => default(StoryTrainingPerformanceEditor).method_22()),
                    postfix: new HarmonyMethod(self, nameof(AfterTraining)));
                harmony.Patch(Expr.MethodOf(() => default(StoryTrainingPerformanceEditor).method_24()),
                    postfix: new HarmonyMethod(self, nameof(AfterPerformance)));
                Log.Info("[patch] story capture armed");
            }
            catch (Exception ex) { Log.Error("[patch] story capture failed", ex); }
        }

        /// <summary>Run <paramref name="build"/> and return the text it handed the font builders.</summary>
        public static List<string> Record(Action build)
        {
            var rec = new List<string>();
            var old = _lines;
            _lines = rec;
            try { build(); }
            catch (Exception ex) { Log.Error("[story] capture build failed", ex); }
            finally { _lines = old; }
            return rec;
        }

        /// <summary>The tab the editor shows, or null when it was built before these patches (a hot
        /// reload) — the screen then infers it.</summary>
        public static Tab? TabOf(StoryTrainingPerformanceEditor editor)
        {
            TabBox box;
            return editor != null && Tabs.TryGetValue(editor, out box) ? box.Tab : (Tab?)null;
        }

        private static void BeforeLine(string __0)
        {
            if (_lines != null && !string.IsNullOrEmpty(__0)) _lines.Add(__0);
        }

        private static void Set(StoryTrainingPerformanceEditor e, Tab tab)
        {
            try
            {
                if (e == null) return;
                Tabs.GetOrCreateValue(e).Tab = tab;
            }
            catch { }
        }

        private static void AfterStory(StoryTrainingPerformanceEditor __instance) => Set(__instance, Tab.Story);
        private static void AfterTraining(StoryTrainingPerformanceEditor __instance) => Set(__instance, Tab.Training);
        private static void AfterPerformance(StoryTrainingPerformanceEditor __instance) => Set(__instance, Tab.Performance);
    }
}
