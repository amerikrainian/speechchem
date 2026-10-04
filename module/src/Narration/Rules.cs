using System.Collections.Generic;

namespace SpeechChem.Narration
{
    /// <summary>
    /// The COMMITTED settings of one event kind, compiled once per settings revision: the runtime
    /// (Narrator on every event, Formatter on every rendered row) reads these instead of building
    /// "event.waldo.grab.log"-style keys and parsing strings per call — at a fast run's event rate
    /// that was most of the narration's garbage. The settings pages keep using EventSettings
    /// directly (they read the draft).
    /// </summary>
    internal sealed class KindRules
    {
        public bool Log;
        /// <summary>Speak at speeds 1-4 (index 0-3) and paused / stopped (index 4).</summary>
        public readonly bool[] SpeakAt = new bool[5];
        public bool ScopeAll;
        public bool Red, Blue;
        private readonly FormatPlan[] _plans = new FormatPlan[3];

        public bool Source(int colour) => colour == 0 ? Red : colour == 1 ? Blue : true;

        public FormatPlan Plan(FormatLayer layer) => _plans[(int)layer];

        public static KindRules Compile(EventKind k)
        {
            var r = new KindRules
            {
                Log = EventSettings.Log(k),
                ScopeAll = EventSettings.Scope(k) == EventSettings.ScopeAll,
                Red = EventSettings.Source(k, 0),
                Blue = EventSettings.Source(k, 1),
            };
            for (int i = 0; i < EventSettings.SpeakMoments.Length; i++) r.SpeakAt[i] = EventSettings.SpeaksAt(k, EventSettings.SpeakMoments[i]);
            foreach (FormatLayer layer in new[] { FormatLayer.Default, FormatLayer.Log, FormatLayer.Speech })
                r._plans[(int)layer] = FormatPlan.Compile(k, layer);
            return r;
        }
    }

    /// <summary>A layer's format, compiled: the parts in order, each with on / variant.</summary>
    internal sealed class FormatPlan
    {
        public string[] Order;
        public bool[] On;
        public string[] Variant;

        public static FormatPlan Compile(EventKind k, FormatLayer layer)
        {
            var order = EventSettings.Order(k, layer);
            var plan = new FormatPlan { Order = order.ToArray(), On = new bool[order.Count], Variant = new string[order.Count] };
            for (int i = 0; i < order.Count; i++)
            {
                plan.On[i] = EventSettings.PartOn(k, layer, order[i]);
                PartDef def = null;
                foreach (var p in k.Parts) if (p.Key == order[i]) { def = p; break; }
                plan.Variant[i] = def != null ? EventSettings.Variant(k, layer, def) : null;
            }
            return plan;
        }
    }

    internal static class Rules
    {
        private static int _revision = int.MinValue;
        private static readonly Dictionary<EventKind, KindRules> ByKind = new Dictionary<EventKind, KindRules>();

        public static KindRules Of(EventKind k)
        {
            if (_revision != NarrationStore.Revision)
            {
                ByKind.Clear();
                _revision = NarrationStore.Revision;
            }
            KindRules r;
            if (!ByKind.TryGetValue(k, out r)) ByKind[k] = r = KindRules.Compile(k);
            return r;
        }
    }
}
