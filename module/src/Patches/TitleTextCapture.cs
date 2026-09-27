using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SpeechChem.Game;
using HarmonyLib;
using Impeller;

namespace SpeechChem.Patches
{
    /// <summary>
    /// Title-screen text the game keeps only as rendered Scenes, captured where it is built:
    ///
    ///  • The main menu's news pane (MainMenuEditor.method_19): a header ("Welcome to SpaceChem!",
    ///    localized), a date and a body — the date and body are hard-coded English literals in the game
    ///    code, in no loc table. While method_19 runs, every string it hands to the font builders
    ///    (ExtendedFont.method_9 for the wrapped body, Scene.smethod_4 for the one-line header/date) is
    ///    recorded against the menu instance, in build order: body, date, header.
    ///  • Class60 dialogs (ResearchNet "ACCESS DENIED", the ResearchNet unlock notice, …): buttons are
    ///    added through method_15(label, action) and kept only as widgets; the prefix records each
    ///    (label, action) pair against the dialog instance, in order.
    ///
    /// Held in ConditionalWeakTables — gone with their screens.
    /// </summary>
    internal static class TitleTextCapture
    {
        internal sealed class NewsText
        {
            public string Header;
            public string Date;
            public string Body;
        }

        internal struct DialogButton
        {
            public string Label;
            public Action Action;
        }

        private static readonly ConditionalWeakTable<object, NewsText> News = new ConditionalWeakTable<object, NewsText>();
        private static readonly ConditionalWeakTable<object, List<DialogButton>> Buttons = new ConditionalWeakTable<object, List<DialogButton>>();
        private static List<string> _newsWrapped; // ExtendedFont.method_9 (wrapped paragraphs)
        private static List<string> _newsLines;   // Scene.smethod_4 (one-line texts)

        public static void Apply(Harmony harmony)
        {
            try
            {
                var self = typeof(TitleTextCapture);
                harmony.Patch(Expr.MethodOf(() => default(SpaceChem.MainMenuEditor).method_19()),
                    prefix: new HarmonyMethod(self, nameof(BeforeNews)),
                    finalizer: new HarmonyMethod(self, nameof(AfterNews)));
                harmony.Patch(Expr.MethodOf(() => default(ExtendedFont).method_9(null, default(Struct114))),
                    postfix: new HarmonyMethod(self, nameof(AfterFontText)));
                harmony.Patch(Expr.MethodOf(() => Scene.smethod_4(null, null, default(Struct104))),
                    postfix: new HarmonyMethod(self, nameof(AfterSceneText)));
                harmony.Patch(Expr.MethodOf(() => default(Class60).method_15(null, null)),
                    prefix: new HarmonyMethod(self, nameof(BeforeDialogButton)));
                Log.Info("[patch] title text capture armed");
            }
            catch (Exception ex) { Log.Error("[patch] title text capture failed to apply", ex); }
        }

        /// <summary>The captured news pane of a main menu instance, or null.</summary>
        public static NewsText NewsOf(object menu)
        {
            if (menu == null) return null;
            return News.TryGetValue(menu, out var n) ? n : null;
        }

        /// <summary>Re-run the menu's own news build inside a capture window — for a module generation
        /// that arrived after the menu was built (hot reload). method_19 only rebuilds the news scene
        /// and the pane's button states, so running it again is side-effect free.</summary>
        public static void Recapture(SpaceChem.MainMenuEditor menu)
        {
            try
            {
                if (menu == null || menu.gclass15_2 == null) return;
                menu.method_19();
            }
            catch (Exception ex) { Log.Error("[capture] news recapture failed", ex); }
        }

        /// <summary>The captured buttons of a Class60 dialog, in order, or null.</summary>
        public static List<DialogButton> ButtonsOf(object dialog)
        {
            if (dialog == null) return null;
            return Buttons.TryGetValue(dialog, out var b) ? b : null;
        }

        private static void BeforeNews()
        {
            _newsWrapped = new List<string>();
            _newsLines = new List<string>();
        }

        private static Exception AfterNews(SpaceChem.MainMenuEditor __instance, Exception __exception)
        {
            try
            {
                // Build order (decompile): the body through method_9, then the date and the header
                // through smethod_4 — so the header is the LAST one-line text and the date the one
                // before it, whatever else the builders may add.
                var wrapped = _newsWrapped;
                var lines = _newsLines;
                if (__instance != null && wrapped != null && lines != null && wrapped.Count >= 1 && lines.Count >= 2)
                {
                    News.Remove(__instance);
                    News.Add(__instance, new NewsText
                    {
                        Body = wrapped[0],
                        Date = lines[lines.Count - 2],
                        Header = lines[lines.Count - 1],
                    });
                }
            }
            catch (Exception ex) { Log.Error("[capture] news", ex); }
            finally { _newsWrapped = null; _newsLines = null; }
            return __exception;
        }

        private static void AfterFontText(string __0)
        {
            if (_newsWrapped != null && __0 != null) _newsWrapped.Add(__0);
        }

        private static void AfterSceneText(string __1)
        {
            if (_newsLines != null && __1 != null) _newsLines.Add(__1);
        }

        private static void BeforeDialogButton(Class60 __instance, string __0, Action __1)
        {
            try
            {
                if (__instance == null) return;
                if (!Buttons.TryGetValue(__instance, out var list))
                {
                    list = new List<DialogButton>();
                    Buttons.Add(__instance, list);
                }
                list.Add(new DialogButton { Label = __0, Action = __1 });
            }
            catch (Exception ex) { Log.Error("[capture] dialog button", ex); }
        }
    }
}
