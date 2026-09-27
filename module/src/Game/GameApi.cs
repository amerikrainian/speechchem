using System;

namespace SpeechChem.Game
{
    /// <summary>
    /// Game operations the mod invokes — TYPED against the deob reference assembly (the load-time
    /// remap binds these to the shipping names; see Modularity/GameRefRemapper). Screens ("editors")
    /// form a linked chain under Class53: smethod_0() = the top screen, smethod_1(screen) = push on
    /// top, smethod_2() = pop the top (decompile-verified — the same calls the game's own handlers
    /// make).
    /// </summary>
    internal static class GameApi
    {
        /// <summary>The top of the game's screen chain, or null before the root exists.</summary>
        public static Class53 TopScreen()
        {
            try { return Class53.class53_0 == null ? null : Class53.smethod_0(); }
            catch { return null; }
        }

        public static bool PushScreen(Class53 screen)
        {
            try
            {
                if (screen == null || Class53.class53_0 == null) return false;
                Class53.smethod_1(screen);
                Log.Info("[game] pushed screen " + GameNames.DeobTypeName(screen.GetType()));
                return true;
            }
            catch (Exception ex) { Log.Error("[game] PushScreen failed", ex); return false; }
        }

        public static bool PopScreen()
        {
            try
            {
                if (Class53.class53_0 == null) return false;
                Class53.smethod_2();
                return true;
            }
            catch (Exception ex) { Log.Error("[game] PopScreen failed", ex); return false; }
        }
    }
}
