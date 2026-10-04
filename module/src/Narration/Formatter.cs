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
    /// </summary>
    internal static class Formatter
    {
        /// <param name="openReactor">The reactor whose view this is (the one open for speech, the
        /// reactor whose Run log is shown), or null (the pipeline's view: every reactor named).</param>
        public static string Format(NarrationEvent e, FormatLayer layer, object openReactor)
        {
            if (e?.Kind == null) return null;
            var kind = e.Kind;
            var order = EventSettings.Order(kind, layer);
            var defs = new Dictionary<string, PartDef>();
            foreach (var p in kind.Parts) defs[p.Key] = p;

            var lead = new List<KeyValuePair<string, string>>();
            var trail = new List<KeyValuePair<string, string>>();
            bool itemsSeen = false;
            foreach (var key in order)
            {
                var common = e.Common.Find(p => p.Key == key);
                if (common == null) { if (AnyItemHas(e, key)) itemsSeen = true; continue; }
                var text = Render(e, kind, layer, defs, common, openReactor);
                if (text == null) continue;
                (itemsSeen ? trail : lead).Add(new KeyValuePair<string, string>(text, common.Suffix));
            }

            var items = new List<string>();
            foreach (var item in e.Items)
            {
                // A part's own punctuation holds only before the part it preceded in the event as
                // built (the default order); after a reorder, unrelated neighbours get a comma.
                var parts = new List<KeyValuePair<string, string>>();
                EventPart prev = null;
                foreach (var key in order)
                {
                    var part = item.Find(p => p.Key == key);
                    if (part == null) continue;
                    var text = Render(e, kind, layer, defs, part, openReactor);
                    if (text == null) continue;
                    if (prev != null && parts.Count > 0 && !Follows(item, prev, part))
                        parts[parts.Count - 1] = new KeyValuePair<string, string>(parts[parts.Count - 1].Key, ",");
                    parts.Add(new KeyValuePair<string, string>(text, part.Suffix));
                    prev = part;
                }
                string joined = Join(parts);
                if (!string.IsNullOrEmpty(joined)) items.Add(joined);
            }

            var all = new List<KeyValuePair<string, string>>(lead);
            if (items.Count > 0) all.Add(new KeyValuePair<string, string>(string.Join("; ", items.ToArray()), trail.Count > 0 ? "," : null));
            all.AddRange(trail);
            return Join(all);
        }

        /// <summary>Whether <paramref name="b"/> came straight after <paramref name="a"/> in the item as
        /// built (ignoring parts turned off in between is not needed: built order = default order).</summary>
        private static bool Follows(List<EventPart> item, EventPart a, EventPart b)
        {
            int ia = item.IndexOf(a), ib = item.IndexOf(b);
            return ia >= 0 && ib == ia + 1;
        }

        private static bool AnyItemHas(NarrationEvent e, string key)
        {
            foreach (var item in e.Items) if (item.Exists(p => p.Key == key)) return true;
            return false;
        }

        private static string Render(NarrationEvent e, EventKind kind, FormatLayer layer, Dictionary<string, PartDef> defs,
            EventPart part, object openReactor)
        {
            if (!EventSettings.PartOn(kind, layer, part.Key)) return null;
            PartDef def;
            string variant = defs.TryGetValue(part.Key, out def) ? EventSettings.Variant(kind, layer, def) : null;
            if (part.Key == "reactor" && variant == "unnamed" && openReactor != null && ReferenceEquals(openReactor, e.Reactor)) return null;
            string text = part.TextFor(variant);
            return string.IsNullOrEmpty(text) ? null : text;
        }

        /// <summary>"a, b: c" — each part's suffix goes before the next part, never after the last.</summary>
        internal static string Join(List<KeyValuePair<string, string>> parts)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0)
                {
                    if (!string.IsNullOrEmpty(parts[i - 1].Value)) sb.Append(parts[i - 1].Value);
                    sb.Append(' ');
                }
                sb.Append(parts[i].Key);
            }
            return sb.ToString();
        }
    }
}
