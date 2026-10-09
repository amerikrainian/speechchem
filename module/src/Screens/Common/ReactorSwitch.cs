using System.Collections.Generic;
using SpaceChem.Pipeline;
using SpeechChem.Game;
using SpeechChem.UI;

namespace SpeechChem.Screens.Common
{
    /// <summary>
    /// Switching between a level's reactors — the buildings you program — from the pipeline or from
    /// inside a reactor (user request 2026-10-09): Ctrl+Tab / Ctrl+Shift+Tab open the next /
    /// previous reactor (wrapping; from the pipeline, the first / last), Ctrl+1 to Ctrl+9 open
    /// reactor N by the pipeline's own numbers (Game/PipelineText: "Assembly Reactor 2" is Ctrl+2).
    /// Opening is the game's double-click (ReactorDraggable.vmethod_2 → Class77.method_6: back to
    /// the pipeline, then the reactor on top), so it works mid-run. The reactor already open, a
    /// number past the last reactor, or a research level (one reactor, no pipeline screen): nothing.
    /// The reactor screen announces the arrival (its name, then a fresh landing on the grid).
    /// </summary>
    internal static class ReactorSwitch
    {
        public static IEnumerable<ElementAction> Actions()
        {
            yield return new ElementAction("screen.reactor.switch.next", () => Cycle(1));
            yield return new ElementAction("screen.reactor.switch.prev", () => Cycle(-1));
            for (int n = 1; n <= 9; n++)
            {
                int index = n - 1;
                yield return new ElementAction("screen.reactor.switch." + n, () => OpenAt(index));
            }
        }

        /// <summary>The level's reactors in number order; empty outside a production pipeline.</summary>
        private static List<ReactorDraggable> Reactors()
        {
            if (Class53.smethod_5<Class84>() != null) return new List<ReactorDraggable>();
            return PipelineText.Reactors(Class53.smethod_5<PipelineEditor>()?.pipeline_0);
        }

        /// <summary>The open reactor's index in the list, or -1 (on the pipeline).</summary>
        private static int OpenIndex(List<ReactorDraggable> reactors)
        {
            var open = Class53.smethod_5<Class77>();
            if (open == null) return -1;
            return reactors.FindIndex(rd => ReferenceEquals(rd.class77_0, open));
        }

        private static void Cycle(int delta)
        {
            var reactors = Reactors();
            if (reactors.Count == 0) return;
            int at = OpenIndex(reactors);
            int next = at < 0 ? (delta > 0 ? 0 : reactors.Count - 1) : (at + delta + reactors.Count) % reactors.Count;
            if (next != at) Open(reactors[next]);
        }

        private static void OpenAt(int index)
        {
            var reactors = Reactors();
            if (index >= reactors.Count || index == OpenIndex(reactors)) return;
            Open(reactors[index]);
        }

        private static void Open(ReactorDraggable rd)
        {
            if (rd.class77_0 == null) return;
            try { rd.vmethod_2(); }
            catch (System.Exception ex) { Log.Error("[reactor] switch failed", ex); }
        }
    }
}
