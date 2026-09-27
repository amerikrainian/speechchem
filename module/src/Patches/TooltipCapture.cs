using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SpeechChem.Game;

namespace SpeechChem.Patches
{
    /// <summary>
    /// The game's hover tooltips (Class713) keep only a rendered Scene. Every one is built through
    /// Class713.smethod_0(title, body, extra), so a postfix records (title, body) against the tooltip
    /// it returns; any owner's tooltip field (palette slots: InstructionMenuItem.class713_0; placed
    /// hardware: ReactorFeature.class713_0; pipeline pieces: Draggable.class713_0) then maps back to
    /// its text. Tooltips are built when their owner is (level load), so a module generation newer
    /// than the open level has none until the level is reopened (dev reload caveat).
    /// </summary>
    internal static class TooltipCapture
    {
        internal sealed class Tip
        {
            public string Title;
            public string Body;
        }

        private static readonly ConditionalWeakTable<object, Tip> Tips = new ConditionalWeakTable<object, Tip>();

        public static void Apply(Harmony harmony)
        {
            try
            {
                harmony.Patch(Expr.MethodOf(() => Class713.smethod_0(null, null, default(Struct116<Struct103>))),
                    postfix: new HarmonyMethod(typeof(TooltipCapture), nameof(AfterBuild)));
                Log.Info("[patch] tooltip capture armed");
            }
            catch (Exception ex) { Log.Error("[patch] tooltip capture failed to apply", ex); }
        }

        /// <summary>The captured text of a tooltip object, or null.</summary>
        public static Tip Of(object tooltip)
        {
            if (tooltip == null) return null;
            return Tips.TryGetValue(tooltip, out var t) ? t : null;
        }

        /// <summary>"Title. Body" for speech, or null.</summary>
        public static string Speech(object tooltip)
        {
            var t = Of(tooltip);
            if (t == null) return null;
            string title = GameText.Speech(t.Title), body = GameText.Speech(t.Body);
            if (string.IsNullOrEmpty(title)) return body;
            if (string.IsNullOrEmpty(body)) return title;
            return title + ". " + body;
        }

        private static void AfterBuild(string __0, string __1, Class713 __result)
        {
            try
            {
                if (__result == null) return;
                Tips.Remove(__result);
                Tips.Add(__result, new Tip { Title = __0, Body = __1 });
            }
            catch { }
        }
    }
}
