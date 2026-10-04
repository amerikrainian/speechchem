using System;
using System.Collections.Generic;

namespace SpeechChem.Narration
{
    /// <summary>Where a format applies: the event's default format, or one of the layers that can
    /// override it (say-the-spire2's focus / buffer / hotkey layers).</summary>
    internal enum FormatLayer { Default, Log, Speech }

    /// <summary>
    /// Every narration setting as a typed accessor over <see cref="NarrationStore"/> keys, with the
    /// registry's defaults. <paramref name="draft"/> reads the dialog's draft (the settings pages),
    /// otherwise the committed values (the runtime).
    ///   event.{kind}.log / .speak.{1-4|idle} / .scope / .red / .blue
    ///   fmt.{layer}.{kind}.order (csv) / .{part}.on / .{part}.variant   (layer "" = the default
    ///   format, "log", "speech"; an unset override inherits the default format)
    ///   step.{key}.assigned / .scope / .stops.{kind} / .speaks.{kind} / .cycle / .giveup
    /// </summary>
    internal static class EventSettings
    {
        public const string ScopeOpen = "open", ScopeAll = "all";

        private static string Raw(string key, bool draft) => draft ? NarrationStore.GetDraft(key) : NarrationStore.Get(key);

        public static bool Bool(string key, bool fallback, bool draft = false)
        {
            bool b;
            return bool.TryParse(Raw(key, draft), out b) ? b : fallback;
        }

        public static int Int(string key, int fallback, bool draft = false)
        {
            int n;
            return int.TryParse(Raw(key, draft), out n) ? n : fallback;
        }

        public static string Str(string key, string fallback, bool draft = false) => Raw(key, draft) ?? fallback;

        /// <summary>Set in the draft; equal to the default = unset (the file stays sparse).</summary>
        public static void Set(string key, string value, string fallback) => NarrationStore.SetDraft(key, value == fallback ? null : value);

        // ---- per event ----

        public static string EventKey(EventKind k, string setting) => "event." + k.Key + "." + setting;

        public static bool Log(EventKind k, bool draft = false) => Bool(EventKey(k, "log"), k.LogDefault, draft);
        /// <summary>The moments an event can be spoken outside a step: running at each play speed
        /// (the play buttons' numbering — a defense level's are its own remapped speeds), and
        /// paused or stopped ("idle": state and speed changes).</summary>
        public static readonly string[] SpeakMoments = { "1", "2", "3", "4", "idle" };

        public static bool SpeaksAtDefault(EventKind k, string moment)
        {
            switch (k.SpeakDefault)
            {
                case SpeakLevel.Off: return false;
                case SpeakLevel.Always: return true;
                default:
                    int n;
                    return int.TryParse(moment, out n) && n <= (int)k.SpeakDefault;
            }
        }

        public static string SpeakKey(EventKind k, string moment) => EventKey(k, "speak." + moment);
        public static bool SpeaksAt(EventKind k, string moment, bool draft = false) => Bool(SpeakKey(k, moment), SpeaksAtDefault(k, moment), draft);
        public static void SetSpeaksAt(EventKind k, string moment, bool on)
            => Set(SpeakKey(k, moment), on ? "true" : "false", SpeaksAtDefault(k, moment) ? "true" : "false");
        public static string Scope(EventKind k, bool draft = false) => Str(EventKey(k, "scope"), ScopeOpen, draft);
        public static bool Source(EventKind k, int colour, bool draft = false) => Bool(EventKey(k, colour == 0 ? "red" : "blue"), true, draft);

        // ---- formats ----

        private static string LayerName(FormatLayer layer) => layer == FormatLayer.Log ? "log" : layer == FormatLayer.Speech ? "speech" : "";

        public static string FormatPrefix(FormatLayer layer, EventKind k) => "fmt." + LayerName(layer) + "." + k.Key;

        public static string DefaultOrder(EventKind k)
        {
            var keys = new List<string>();
            foreach (var p in k.Parts) keys.Add(p.Key);
            return string.Join(",", keys.ToArray());
        }

        /// <summary>The part order for a layer: its override, else the default format's, else the
        /// registry's — with any part the stored list lacks (a newer version's) inserted after its
        /// registry predecessor, and parts the registry no longer has dropped.</summary>
        public static List<string> Order(EventKind k, FormatLayer layer, bool draft = false)
        {
            string stored = layer != FormatLayer.Default ? Raw(FormatPrefix(layer, k) + ".order", draft) : null;
            if (stored == null) stored = Raw(FormatPrefix(FormatLayer.Default, k) + ".order", draft);
            return MergeOrder(stored, k);
        }

        internal static List<string> MergeOrder(string stored, EventKind k)
        {
            var known = new List<string>();
            foreach (var p in k.Parts) known.Add(p.Key);
            var order = new List<string>();
            if (!string.IsNullOrEmpty(stored))
                foreach (var key in stored.Split(','))
                    if (known.Contains(key) && !order.Contains(key)) order.Add(key);
            for (int i = 0; i < known.Count; i++)
            {
                if (order.Contains(known[i])) continue;
                int at = 0;
                for (int j = i - 1; j >= 0; j--)
                {
                    int idx = order.IndexOf(known[j]);
                    if (idx >= 0) { at = idx + 1; break; }
                }
                order.Insert(at, known[i]);
            }
            return order;
        }

        public static void SetOrder(EventKind k, FormatLayer layer, List<string> order)
        {
            string value = string.Join(",", order.ToArray());
            string key = FormatPrefix(layer, k) + ".order";
            // A layer equal to what it would inherit stores nothing; the default format equal to
            // the registry's stores nothing.
            string inherited = layer == FormatLayer.Default
                ? DefaultOrder(k)
                : string.Join(",", Order(k, FormatLayer.Default, draft: true).ToArray());
            NarrationStore.SetDraft(key, value == inherited ? null : value);
        }

        public static bool PartOn(EventKind k, FormatLayer layer, string part, bool draft = false)
        {
            if (layer != FormatLayer.Default)
            {
                string v = Raw(FormatPrefix(layer, k) + "." + part + ".on", draft);
                bool b;
                if (bool.TryParse(v, out b)) return b;
            }
            return Bool(FormatPrefix(FormatLayer.Default, k) + "." + part + ".on", true, draft);
        }

        public static void SetPartOn(EventKind k, FormatLayer layer, string part, bool on)
        {
            bool inherited = layer == FormatLayer.Default ? true : PartOn(k, FormatLayer.Default, part, draft: true);
            NarrationStore.SetDraft(FormatPrefix(layer, k) + "." + part + ".on", on == inherited ? null : (on ? "true" : "false"));
        }

        public static string Variant(EventKind k, FormatLayer layer, PartDef part, bool draft = false)
        {
            if (part.Variants == null) return null;
            string v = layer != FormatLayer.Default ? Raw(FormatPrefix(layer, k) + "." + part.Key + ".variant", draft) : null;
            if (v == null) v = Raw(FormatPrefix(FormatLayer.Default, k) + "." + part.Key + ".variant", draft);
            return v != null && Array.IndexOf(part.Variants, v) >= 0 ? v : part.DefaultVariant;
        }

        public static void SetVariant(EventKind k, FormatLayer layer, PartDef part, string variant)
        {
            string inherited = layer == FormatLayer.Default ? part.DefaultVariant : Variant(k, FormatLayer.Default, part, draft: true);
            NarrationStore.SetDraft(FormatPrefix(layer, k) + "." + part.Key + ".variant", variant == inherited ? null : variant);
        }

        /// <summary>Whether a layer differs from the default format (shown on its page).</summary>
        public static bool Overridden(EventKind k, FormatLayer layer, bool draft = true)
        {
            string prefix = FormatPrefix(layer, k) + ".";
            foreach (var p in k.Parts)
                if (Raw(prefix + p.Key + ".on", draft) != null || Raw(prefix + p.Key + ".variant", draft) != null) return true;
            return Raw(prefix + "order", draft) != null;
        }
    }
}
