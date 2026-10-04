using System;
using System.Collections.Generic;

namespace SpeechChem
{
    /// <summary>
    /// The mod's per-frame dispatcher — the one consumer of the GameLogic tick prefix. Subsystems that
    /// need frame time (dev pump, screen watcher, input polling, UI) register a named step at boot;
    /// the tick hook calls <see cref="Tick"/>. Mirrors WrathAccess's Ticker in role: one ordered,
    /// defensive loop instead of every subsystem growing its own patch. Steps run in registration order
    /// (Bootstrap is the composition root and owns that order); a throwing step is logged and skipped
    /// for that frame, never unregistered and never allowed to break the frame — an accessibility mod
    /// must not crash the game.
    /// </summary>
    internal static class FrameLoop
    {
        private sealed class Step
        {
            public string Name;
            public Action Run;
            public string LastError; // the failure already logged; repeats stay quiet until recovery
        }

        // Registration happens once at boot (main thread, before the game loop starts); ticks happen on
        // the game's main thread only. No lock needed under that contract.
        private static readonly List<Step> Steps = new List<Step>();

        public static void Register(string name, Action run)
        {
            if (run == null) return;
            Steps.Add(new Step { Name = name, Run = run });
            Log.Info("[frameloop] step registered: " + name);
        }

        /// <summary>Run every registered step, in order. Called from the GameLogic tick prefix.</summary>
#if DEBUG
        /// <summary>The last tick's own duration (Dev/LogStress measures the mod's work with it).</summary>
        internal static double LastTickMs;
        private static readonly System.Diagnostics.Stopwatch TickClock = new System.Diagnostics.Stopwatch();
#endif

        public static void Tick()
        {
#if DEBUG
            TickClock.Restart();
            try { TickSteps(); }
            finally { LastTickMs = TickClock.Elapsed.TotalMilliseconds; }
        }

        private static void TickSteps()
        {
#endif
            for (int i = 0; i < Steps.Count; i++)
            {
                var step = Steps[i];
                try
                {
                    step.Run();
                    if (step.LastError != null)
                    {
                        Log.Info("[frameloop] step '" + step.Name + "' recovered.");
                        step.LastError = null;
                    }
                }
                catch (Exception ex)
                {
                    // A broken step fails EVERY frame (~60/s); log each distinct failure once.
                    string key = ex.GetType().FullName + ": " + ex.Message;
                    if (key == step.LastError) continue;
                    step.LastError = key;
                    Log.Error("[frameloop] step '" + step.Name + "' failed (repeats suppressed until it recovers)", ex);
                }
            }
        }
    }
}
