using System;
using SpaceChem;

namespace SpeechChem.Screens.Common
{
    /// <summary>
    /// Escape in a level (user rule 2026-10-01): while a run exists (running or paused), the first
    /// Escape stops it and the game never sees that press; with the run stopped, Escape is the
    /// game's again (the research reactor's exit prompt, a production reactor's return to the
    /// pipeline, the pipeline's exit prompt). The game itself does this only in research reactors
    /// (Class77.vmethod_2: Class258.smethod_17(0) while not stopped); a production reactor left
    /// mid-run and the pipeline opened the exit prompt mid-run. Screens report <see cref="Active"/>
    /// through ModalCapturesEscape (GameKeySuppression latches the press) and bind Back to
    /// <see cref="Stop"/>.
    /// </summary>
    internal static class RunEscape
    {
        public static bool Active
        {
            get
            {
                try { return (int)Class258.smethod_16() != 0; }
                catch { return false; }
            }
        }

        public static void Stop()
        {
            try { Class258.smethod_17((Enum16)0); } // the game's own stop (its research-reactor Escape)
            catch (Exception ex) { Log.Error("[run] escape stop failed", ex); }
        }
    }
}
