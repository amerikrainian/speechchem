using System;

namespace SpeechChem.Patches
{
    /// <summary>
    /// The host's Harmony patches on the game loop — deliberately thin: they only translate the
    /// game's lifecycle into mod-side events, all real behavior lives in the module.
    ///
    ///   • <see cref="AfterInit"/>  — postfix on Class185.vmethod_2() (runs once, right after the boot
    ///     splash and texture load, when the game builds its MainMenuEditor root screen).
    ///   • <see cref="BeforeTick"/> — prefix on Class185.vmethod_3(Struct102) (the per-frame UPDATE,
    ///     called from the engine's main loop after the SDL event pump and before the draw). Runs
    ///     <see cref="Bootstrap.TickFrame"/>: dev pump, then the reloadable module's Tick.
    ///
    /// Attached MANUALLY from <see cref="Bootstrap"/> with MethodInfos resolved by GameState (the host
    /// has no compile-time reference to the game). Instance methods receive the live object as
    /// <c>object __instance</c>. Every hook body is defensive: an accessibility mod must never crash
    /// the game.
    /// </summary>
    public static class GamePatches
    {
        private static bool _announcedReady;

        /// <summary>Postfix on Class185.vmethod_2() — init finished.</summary>
        public static void AfterInit(object __instance)
        {
            try
            {
                if (_announcedReady) return; // runs once per SANDBOX domain, but guard anyway
                _announcedReady = true;
                Log.Info("[hook] game init complete — SpeechChem is live.");
                Bootstrap.OnGameInitialized();
            }
            catch (Exception ex) { Log.Error("[hook] AfterInit failed", ex); }
        }

        /// <summary>Prefix on Class185.vmethod_3() — runs at the top of every frame's update.</summary>
        public static void BeforeTick(object __instance)
        {
            try { Bootstrap.TickFrame(); } // defensively layered inside; this guards the call itself
            catch (Exception ex) { Log.Error("[hook] BeforeTick failed", ex); }
        }
    }
}
