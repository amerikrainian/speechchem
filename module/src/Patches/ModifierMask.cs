using System;
using HarmonyLib;
using SpeechChem.Game;

namespace SpeechChem.Patches
{
    /// <summary>
    /// Decides what the game sees of the Ctrl key for the length of a mod call. The pipeline's drop
    /// (Pipeline.method_12 / method_13) reads Ctrl (Class259.smethod_6) to mean COPY instead of
    /// move — and the mod's paste is Ctrl+V, so without a mask every keyboard move would duplicate
    /// the component; a keyboard COPY (Ctrl+C, then Ctrl+V) forces it on instead, whatever is
    /// held. Scoped: <c>using (ModifierMask.NoCtrl()) { … }</c> / <c>ForceCtrl()</c>.
    /// </summary>
    internal static class ModifierMask
    {
        private static int _depth;
        private static bool _value;

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

        public static IDisposable NoCtrl() => new Scope(false);

        public static IDisposable ForceCtrl() => new Scope(true);

        private sealed class Scope : IDisposable
        {
            private bool _done;
            private readonly bool _outer;
            public Scope(bool value) { _outer = _value; _value = value; _depth++; }
            public void Dispose() { if (_done) return; _done = true; _depth--; _value = _outer; }
        }

        private static bool CtrlPrefix(ref bool __result)
        {
            if (_depth <= 0) return true;
            __result = _value;
            return false;
        }
    }
}
