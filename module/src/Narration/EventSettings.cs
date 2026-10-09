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
    ///   event.{node}.log / .speak.{1-4|idle} / .scope / .red / .blue
    ///   fmt.{layer}.{node}.order (csv) / .{part}.on / .{part}.variant   (layer "" = the default
    ///   format, "log", "speech"; an unset override inherits the default format)
    ///   step.{key}.assigned / .scope / .stops.{node} / .speaks.{node} / .cycle / .giveup
    /// INHERITANCE (the event tree, EventKinds): a leaf's value is its own stored one, else its
    /// nearest ancestor's, else the registry default. Formats go LAYER FIRST: the Log / Speech
    /// override anywhere from the leaf up, and only then the shared Format, leaf up again.
    /// WRITING a node stores it there and
    /// clears the same setting below it (the general setting takes over the whole subtree; a node
    /// further down can then be set apart again); nothing is stored when every leaf below would
    /// read that value anyway, so the file stays sparse. A tag writes each member. A branch READS as
    /// its leaves' common value, or mixed.
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

        // ---- the tree ----

        /// <summary>The nearest stored value on <paramref name="k"/> or an ancestor, or null.</summary>
        public static string Inherited(EventKind k, Func<EventKind, string> key, bool draft)
        {
            for (var n = k; n != null; n = n.Parent)
            {
                string v = Raw(key(n), draft);
                if (v != null) return v;
            }
            return null;
        }

        /// <summary>Write a setting on a node (see the class summary). <paramref name="effective"/>
        /// gives a leaf's resolved value (draft) as the string that would be stored; leaves it
        /// does not apply to return null and are ignored.</summary>
        public static void Put(EventKind node, Func<EventKind, string> key, string value, Func<EventKind, string> effective)
        {
            if (node.IsTag)
            {
                foreach (var m in node.Members) Put(m, key, value, effective);
                return;
            }
            foreach (var n in node.Subtree()) NarrationStore.SetDraft(key(n), null);
            foreach (var leaf in node.Leaves())
            {
                string now = effective(leaf);
                if (now != null && now != value) { NarrationStore.SetDraft(key(node), value); return; }
            }
        }

        /// <summary>A node's boolean as its leaves read it: true / false, or null when they differ.</summary>
        public static bool? Common(EventKind node, Func<EventKind, bool> leafValue, Func<EventKind, bool> applies = null)
        {
            bool? seen = null;
            foreach (var leaf in node.Leaves())
            {
                if (applies != null && !applies(leaf)) continue;
                bool v = leafValue(leaf);
                if (seen == null) seen = v;
                else if (seen.Value != v) return null;
            }
            return seen;
        }

        /// <summary>The same for a string setting: the common value, or null when they differ.</summary>
        public static string CommonText(EventKind node, Func<EventKind, string> leafValue, Func<EventKind, bool> applies = null)
        {
            string seen = null;
            foreach (var leaf in node.Leaves())
            {
                if (applies != null && !applies(leaf)) continue;
                string v = leafValue(leaf);
                if (seen == null) seen = v;
                else if (seen != v) return null;
            }
            return seen;
        }

        private static string B(bool b) => b ? "true" : "false";

        // ---- per event ----

        public static string EventKey(EventKind k, string setting) => "event." + k.Key + "." + setting;

        private static bool InheritedBool(EventKind k, string setting, bool fallback, bool draft)
        {
            bool b;
            return bool.TryParse(Inherited(k, n => EventKey(n, setting), draft), out b) ? b : fallback;
        }

        public static bool Log(EventKind k, bool draft = false) => InheritedBool(k, "log", k.LogDefault, draft);
        public static void SetLog(EventKind node, bool on) => Put(node, n => EventKey(n, "log"), B(on), leaf => B(Log(leaf, draft: true)));

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
        public static bool SpeaksAt(EventKind k, string moment, bool draft = false) => InheritedBool(k, "speak." + moment, SpeaksAtDefault(k, moment), draft);
        public static void SetSpeaksAt(EventKind node, string moment, bool on)
            => Put(node, n => SpeakKey(n, moment), B(on), leaf => B(SpeaksAt(leaf, moment, draft: true)));

        public static string Scope(EventKind k, bool draft = false) => Inherited(k, n => EventKey(n, "scope"), draft) ?? ScopeOpen;
        public static void SetScope(EventKind node, string scope)
            => Put(node, n => EventKey(n, "scope"), scope, leaf => leaf.ReactorScoped ? Scope(leaf, draft: true) : null);

        public static bool Source(EventKind k, int colour, bool draft = false) => InheritedBool(k, colour == 0 ? "red" : "blue", true, draft);
        public static void SetSource(EventKind node, int colour, bool on)
            => Put(node, n => EventKey(n, colour == 0 ? "red" : "blue"), B(on), leaf => leaf.Waldo ? B(Source(leaf, colour, draft: true)) : null);

        // ---- formats ----

        private static string LayerName(FormatLayer layer) => layer == FormatLayer.Log ? "log" : layer == FormatLayer.Speech ? "speech" : "";

        public static string FormatPrefix(FormatLayer layer, EventKind k) => "fmt." + LayerName(layer) + "." + k.Key;

        /// <summary>A format setting for a layer, LAYER FIRST (user decision 2026-10-09): the
        /// layer's override on the leaf or any node above it wins; only when none is set anywhere
        /// up the tree is the shared Format asked, leaf up again. "Speech format" on a group thus
        /// reaches every event's speech, whatever an event's own Format says.</summary>
        private static string FormatValue(EventKind k, FormatLayer layer, string setting, bool draft)
        {
            if (layer != FormatLayer.Default)
                for (var n = k; n != null; n = n.Parent)
                {
                    string v = Raw(FormatPrefix(layer, n) + "." + setting, draft);
                    if (v != null) return v;
                }
            for (var n = k; n != null; n = n.Parent)
            {
                string v = Raw(FormatPrefix(FormatLayer.Default, n) + "." + setting, draft);
                if (v != null) return v;
            }
            return null;
        }

        public static string DefaultOrder(EventKind k)
        {
            var keys = new List<string>();
            foreach (var p in k.Parts) keys.Add(p.Key);
            return string.Join(",", keys.ToArray());
        }

        /// <summary>The part order for a layer (inherited as above, else the registry's) — with any
        /// part the stored list lacks (a newer version's, or a leaf's own) inserted after its
        /// registry predecessor, and parts the node doesn't have dropped.</summary>
        public static List<string> Order(EventKind k, FormatLayer layer, bool draft = false)
            => MergeOrder(FormatValue(k, layer, "order", draft), k);

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

        public static void SetOrder(EventKind node, FormatLayer layer, List<string> order)
        {
            string value = string.Join(",", order.ToArray());
            Put(node, n => FormatPrefix(layer, n) + ".order", value,
                leaf => string.Join(",", Order(leaf, layer, draft: true).ToArray()) == string.Join(",", MergeOrder(value, leaf).ToArray()) ? value : "");
        }

        private static bool HasPart(EventKind k, string part) => Array.Exists(k.Parts, p => p.Key == part);

        public static bool PartOn(EventKind k, FormatLayer layer, string part, bool draft = false)
        {
            bool b;
            if (bool.TryParse(FormatValue(k, layer, part + ".on", draft), out b)) return b;
            foreach (var p in k.Parts) if (p.Key == part) return p.DefaultOn;
            return true;
        }

        public static void SetPartOn(EventKind node, FormatLayer layer, string part, bool on)
            => Put(node, n => FormatPrefix(layer, n) + "." + part + ".on", B(on), leaf => HasPart(leaf, part) ? B(PartOn(leaf, layer, part, draft: true)) : null);

        public static string Variant(EventKind k, FormatLayer layer, PartDef part, bool draft = false)
        {
            if (part.Variants == null) return null;
            string v = FormatValue(k, layer, part.Key + ".variant", draft);
            return v != null && Array.IndexOf(part.Variants, v) >= 0 ? v : part.DefaultVariant;
        }

        public static void SetVariant(EventKind node, FormatLayer layer, PartDef part, string variant)
            => Put(node, n => FormatPrefix(layer, n) + "." + part.Key + ".variant", variant,
                leaf => HasPart(leaf, part.Key) ? Variant(leaf, layer, part, draft: true) : null);

        /// <summary>Whether a node's layer differs from what it inherits (its own keys only).</summary>
        public static bool Overridden(EventKind k, FormatLayer layer, bool draft = true)
        {
            string prefix = FormatPrefix(layer, k) + ".";
            foreach (var p in k.Parts)
                if (Raw(prefix + p.Key + ".on", draft) != null || Raw(prefix + p.Key + ".variant", draft) != null) return true;
            return Raw(prefix + "order", draft) != null;
        }

        // ---- resets: exact keys (a prefix would also catch a sibling whose key extends this one's,
        // "waldo.grab" vs "waldo.grab.none") ----

        private static readonly FormatLayer[] Layers = { FormatLayer.Default, FormatLayer.Log, FormatLayer.Speech };

        /// <summary>Forget a node's event settings and formats, and everything set below it (a tag:
        /// its members' event settings).</summary>
        public static void Reset(EventKind node)
        {
            var nodes = node.IsTag ? node.Members : node.Subtree();
            foreach (var n in nodes)
            {
                NarrationStore.SetDraft(EventKey(n, "log"), null);
                foreach (var m in SpeakMoments) NarrationStore.SetDraft(SpeakKey(n, m), null);
                foreach (var s in new[] { "scope", "red", "blue" }) NarrationStore.SetDraft(EventKey(n, s), null);
                if (node.IsTag) continue;
                foreach (var layer in Layers) ResetFormat(n, layer, subtree: false);
            }
        }

        /// <summary>Forget a node's format for one layer (and below it, with <paramref name="subtree"/>).</summary>
        public static void ResetFormat(EventKind node, FormatLayer layer, bool subtree = true)
        {
            foreach (var n in subtree ? node.Subtree() : new List<EventKind> { node })
            {
                string prefix = FormatPrefix(layer, n) + ".";
                NarrationStore.SetDraft(prefix + "order", null);
                foreach (var p in n.Parts)
                {
                    NarrationStore.SetDraft(prefix + p.Key + ".on", null);
                    NarrationStore.SetDraft(prefix + p.Key + ".variant", null);
                }
            }
        }
    }
}
