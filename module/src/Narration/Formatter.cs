using System;
using System.Collections.Generic;
using System.Text;

namespace SpeechChem.Narration
{
    /// <summary>
    /// An event's text for a layer: its parts in the layer's order, minus the ones turned off, each
    /// in its chosen detail variant, joined with spaces after each part's own punctuation (dropped
    /// after the last part). The common parts (reactor, waldo) come once, before or after the items
    /// as the order places them; several items are joined with "; ". The reactor part in its
    /// "unnamed" variant is left out where that reactor is the open one (the mod's rule: inside
    /// reactor 2 its own events drop "reactor 2").
    ///
    /// Runs for every event and (uncached) every rendered log row, so it reads the compiled
    /// <see cref="FormatPlan"/> and reuses its scratch buffers: the only allocation is the result.
    /// </summary>
    internal static class Formatter
    {
        private struct Piece
        {
            public string Text, Suffix;
            public Piece(string text, string suffix) { Text = text; Suffix = suffix; }
        }

        // Scratch, reused per call (per thread: the game calls from its main thread, tests may not).
        [ThreadStatic] private static List<Piece> _lead, _trail, _parts, _all;
        [ThreadStatic] private static StringBuilder _items, _join;

        /// <param name="openReactor">The reactor whose view this is (the one open for speech, the
        /// reactor whose Run log is shown), or null (the pipeline's view: every reactor named).</param>
        /// <param name="continuation">Leave out the common parts (reactor, waldo): the event
        /// continues an utterance that already named them (Narrator's per-cycle merge).</param>
        public static string Format(NarrationEvent e, FormatLayer layer, object openReactor, bool continuation = false)
        {
            if (e?.Kind == null) return null;
            var plan = Rules.Of(e.Kind).Plan(layer);
            var lead = _lead ?? (_lead = new List<Piece>());
            var trail = _trail ?? (_trail = new List<Piece>());
            var parts = _parts ?? (_parts = new List<Piece>());
            var all = _all ?? (_all = new List<Piece>());
            var items = _items ?? (_items = new StringBuilder());
            lead.Clear(); trail.Clear(); all.Clear(); items.Length = 0;

            bool itemsSeen = false;
            for (int o = 0; o < plan.Order.Length; o++)
            {
                string key = plan.Order[o];
                var common = Find(e.Common, key);
                if (common == null) { if (!itemsSeen && AnyItemHas(e, key)) itemsSeen = true; continue; }
                if (continuation) continue;
                var text = Render(e, plan, o, common, openReactor);
                if (text == null) continue;
                (itemsSeen ? trail : lead).Add(new Piece(text, common.Suffix));
            }

            int itemCount = 0;
            foreach (var item in e.Items)
            {
                // A part's own punctuation holds only before the part it preceded in the event as
                // built (the default order); after a reorder, unrelated neighbours get a comma.
                parts.Clear();
                int prev = -1;
                for (int o = 0; o < plan.Order.Length; o++)
                {
                    int at = IndexOf(item, plan.Order[o]);
                    if (at < 0) continue;
                    var part = item[at];
                    var text = Render(e, plan, o, part, openReactor);
                    if (text == null) continue;
                    if (prev >= 0 && parts.Count > 0 && at != prev + 1)
                        parts[parts.Count - 1] = new Piece(parts[parts.Count - 1].Text, ",");
                    parts.Add(new Piece(text, part.Suffix));
                    prev = at;
                }
                if (parts.Count == 0) continue;
                if (itemCount++ > 0) items.Append("; ");
                Append(items, parts);
            }

            all.AddRange(lead);
            if (itemCount > 0) all.Add(new Piece(items.ToString(), trail.Count > 0 ? "," : null));
            all.AddRange(trail);
            if (all.Count == 0) return "";
            var join = _join ?? (_join = new StringBuilder());
            join.Length = 0;
            Append(join, all);
            return join.ToString();
        }

        private static EventPart Find(List<EventPart> list, string key)
        {
            for (int i = 0; i < list.Count; i++) if (list[i].Key == key) return list[i];
            return null;
        }

        private static int IndexOf(List<EventPart> list, string key)
        {
            for (int i = 0; i < list.Count; i++) if (list[i].Key == key) return i;
            return -1;
        }

        private static bool AnyItemHas(NarrationEvent e, string key)
        {
            foreach (var item in e.Items) if (IndexOf(item, key) >= 0) return true;
            return false;
        }

        private static string Render(NarrationEvent e, FormatPlan plan, int o, EventPart part, object openReactor)
        {
            if (!plan.On[o]) return null;
            string variant = plan.Variant[o];
            if (variant == "unnamed" && part.Key == "reactor" && openReactor != null && ReferenceEquals(openReactor, e.Reactor)) return null;
            string text = part.TextFor(variant);
            return string.IsNullOrEmpty(text) ? null : text;
        }

        /// <summary>"a, b: c" — each part's suffix goes before the next part, never after the last.</summary>
        private static void Append(StringBuilder sb, List<Piece> parts)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0)
                {
                    if (!string.IsNullOrEmpty(parts[i - 1].Suffix)) sb.Append(parts[i - 1].Suffix);
                    sb.Append(' ');
                }
                sb.Append(parts[i].Text);
            }
        }

        /// <summary>The joining rule on its own (tests).</summary>
        internal static string Join(List<KeyValuePair<string, string>> parts)
        {
            var list = new List<Piece>();
            foreach (var p in parts) list.Add(new Piece(p.Key, p.Value));
            var sb = new StringBuilder();
            Append(sb, list);
            return sb.ToString();
        }
    }
}
