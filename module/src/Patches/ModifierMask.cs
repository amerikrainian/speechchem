using System;
using HarmonyLib;
using SpeechChem.Game;

namespace SpeechChem.Patches
{
    /// <summary>
    /// Hides the held Ctrl key from the game for the length of a mod call. The pipeline's drop
    /// (Pipeline.method_12 / method_13) reads Ctrl (Class259.smethod_6) to mean COPY instead of
    /// move — and the mod's paste is Ctrl+V, so without this every keyboard move would duplicate
    /// the component. Scoped: <c>using (ModifierMask.NoCtrl()) { … }</c>.
    /// </summary>
    internal static class ModifierMask
    {
        private static int _depth;

        public static void Apply(Harmony harmony)
        {
            try
            {
                harmony.Patch(Expr.MethodOf(() => Class259.smethod_6()),
                    prefix: new HarmonyMethod(typeof(ModifierMask), nameof(CtrlPrefix)));
                Log.Info("[patch] modifier mask armed");
            }
            catch (Exception ex) { Log.Error("[patch] modifier mask failed to apply", ex); }
        }

        public static IDisposable NoCtrl() => new Scope();

        private sealed class Scope : IDisposable
        {
            private bool _done;
            public Scope() { _depth++; }
            public void Dispose() { if (_done) return; _done = true; _depth--; }
        }

        private static bool CtrlPrefix(ref bool __result)
        {
            if (_depth <= 0) return true;
            __result = false;
            return false;
        }
    }
}
