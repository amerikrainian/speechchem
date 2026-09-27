using System.Collections.Generic;
using Impeller;
using SpaceChem;
using SpaceChem.Pipeline;
using SpeechChem.Localization;

namespace SpeechChem.Game
{
    /// <summary>
    /// Naming for the pipeline editor's components, shared by the pipeline screen and the run log.
    /// A component's name is the game's own (Draggable.string_1: "Assembly Reactor", "Recycler",
    /// "Storage Tank"); reactors are numbered in READING ORDER (top to bottom, then left to right, by
    /// their top-left cell) when a pipeline holds more than one — user-approved 2026-09-27, so the
    /// run log's "reactor 2" and the list's "Assembly Reactor 2" always agree.
    /// </summary>
    internal static class PipelineText
    {
        /// <summary>The components worth naming, in reading order: everything placed on the
        /// pipeline except the terrain (Class612) and unnamed decoration (no name, no ports).</summary>
        public static List<KeyValuePair<Draggable, Vector2i>> Components(Pipeline p)
        {
            var list = new List<KeyValuePair<Draggable, Vector2i>>();
            if (p == null) return list;
            foreach (var kv in p)
            {
                var d = kv.Key;
                if (d is Class612 || d is PipeDraggable) continue;
                if (!(d is ReactorDraggable) && string.IsNullOrEmpty(d.string_1?.Trim())
                    && d.class485_0.Count == 0 && d.class485_1.Count == 0) continue;
                if (kv.Value.int_0 < 0) continue; // a shelf clone parked off the grid mid-placement
                list.Add(kv);
            }
            list.Sort((a, b) =>
            {
                int c = a.Value.int_1.CompareTo(b.Value.int_1);
                return c != 0 ? c : a.Value.int_0.CompareTo(b.Value.int_0);
            });
            return list;
        }

        /// <summary>The reactors in reading order.</summary>
        public static List<ReactorDraggable> Reactors(Pipeline p)
        {
            var list = new List<ReactorDraggable>();
            foreach (var kv in Components(p))
                if (kv.Key is ReactorDraggable rd) list.Add(rd);
            return list;
        }

        /// <summary>A reactor's number (1-based, reading order), or 0 when it is the only one.</summary>
        public static int ReactorNumber(Pipeline p, ReactorDraggable rd)
        {
            var reactors = Reactors(p);
            if (reactors.Count < 2) return 0;
            int i = reactors.IndexOf(rd);
            return i < 0 ? 0 : i + 1;
        }

        /// <summary>"Assembly Reactor 2", "Recycler", "Storage Tank".</summary>
        public static string Name(Pipeline p, Draggable d)
        {
            if (d == null) return null;
            string name = d.string_1?.Trim();
            if (d is ReactorDraggable rd)
            {
                if (string.IsNullOrEmpty(name)) name = Loc.T("pipeline.reactor");
                int n = ReactorNumber(p, rd);
                return n > 0 ? name + " " + n : name;
            }
            return string.IsNullOrEmpty(name) ? Loc.T("pipeline.component") : name;
        }

        public static string Cell(Vector2i c) => Loc.T("reactor.cell", new { x = c.int_0 + 1, y = c.int_1 + 1 });
    }
}
