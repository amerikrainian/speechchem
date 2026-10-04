using System.Collections.Generic;
using SpaceChem;

namespace SpeechChem.Narration
{
    /// <summary>One part of one event: its text (or one text per detail variant) and the
    /// punctuation that follows it when another part comes after (say-the-spire2's suffix).</summary>
    internal sealed class EventPart
    {
        public string Key;
        public string Text;
        public Dictionary<string, string> Variants; // overrides Text per variant key
        public string Suffix;

        public string TextFor(string variant)
        {
            string t;
            if (Variants != null && variant != null && Variants.TryGetValue(variant, out t)) return t;
            return Text;
        }
    }

    /// <summary>
    /// A run event as data: its kind, its parts (an event can hold several ITEMS — a bond
    /// instruction acting on two bonder pairs — joined with "; "), and what the narration rules
    /// need: the cycle, the reactor it belongs to (null = level-wide), the waldo colour, whether it
    /// concerns the reactor open now, and a payload (a reaction error's crash snapshot). The text
    /// is made per layer by <see cref="Formatter"/>.
    /// </summary>
    internal sealed class NarrationEvent
    {
        public EventKind Kind;
        public readonly List<EventPart> Common = new List<EventPart>();      // reactor, waldo
        public readonly List<List<EventPart>> Items = new List<List<EventPart>>();
        public int Cycle;
        public object Reactor;
        public int Colour = -1;      // 0 red, 1 blue, -1 not a waldo's
        public bool Concerns = true; // concerns the open reactor (true with none open)
        public object Payload;

        /// <summary>Nothing will use this event (not logged, not speakable now, no step running —
        /// <see cref="Narrator.Wants"/>): the builders skip their work (molecule texts, part lists)
        /// and Emit drops it. A fast run builds an event for every waldo action.</summary>
        public bool Muted;

        public NarrationEvent(string kind)
        {
            Kind = EventKinds.Get(kind);
            Items.Add(new List<EventPart>());
            Muted = Kind != null && !Narrator.Wants(Kind);
        }

        /// <summary>Set the waldo colour; a colour the event's source filter drops mutes it.</summary>
        public NarrationEvent WithColour(int colour)
        {
            Colour = colour;
            if (!Muted && Kind != null && Kind.Waldo && colour >= 0 && !Rules.Of(Kind).Source(colour)) Muted = true;
            return this;
        }

        private List<EventPart> Current => Items[Items.Count - 1];

        /// <summary>Add a part to the current item (empty text is skipped).</summary>
        public NarrationEvent Part(string key, string text, string suffix = null)
        {
            if (!Muted && !string.IsNullOrEmpty(text)) Current.Add(new EventPart { Key = key, Text = text, Suffix = suffix });
            return this;
        }

        public NarrationEvent Part(string key, Dictionary<string, string> variants, string defaultText, string suffix = null)
        {
            if (!Muted && !string.IsNullOrEmpty(defaultText)) Current.Add(new EventPart { Key = key, Text = defaultText, Variants = variants, Suffix = suffix });
            return this;
        }

        /// <summary>A molecule part with its detail variants: name and formula, name, formula.</summary>
        public NarrationEvent Molecule(Molecule m, string suffix = null)
        {
            if (m == null || Muted) return this;
            var v = new Dictionary<string, string>
            {
                { "both", Game.MoleculeText.NameAndFormula(m) },
                { "name", Game.MoleculeText.Name(m) },
                { "formula", Game.MoleculeText.Formula(m) },
            };
            return Part("molecule", v, v["both"], suffix);
        }

        /// <summary>A part shown once per event (the reactor, the waldo), not per item.</summary>
        public NarrationEvent CommonPart(string key, string text, string suffix, Dictionary<string, string> variants = null)
        {
            if (!Muted && !string.IsNullOrEmpty(text)) Common.Add(new EventPart { Key = key, Text = text, Suffix = suffix, Variants = variants });
            return this;
        }

        /// <summary>Start another item.</summary>
        public NarrationEvent NextItem()
        {
            if (!Muted && Current.Count > 0) Items.Add(new List<EventPart>());
            return this;
        }

        public bool IsEmpty
        {
            get
            {
                foreach (var item in Items) if (item.Count > 0) return false;
                return Common.Count == 0;
            }
        }
    }
}

namespace SpeechChem.Narration
{
    /// <summary>
    /// Two events are the SAME LOG ENTRY when everything a view renders from is equal: kind,
    /// reactor (by reference), payload (by reference — a crash snapshot keeps its entry its own)
    /// and every part's key, text, variants and suffix. The run log interns events with it, so a
    /// looping program's millions of entries share a few records (UI/GroupedLog). Cycle, colour and
    /// "concerns" are not rendered and not compared.
    /// </summary>
    internal sealed class EventContentComparer : IEqualityComparer<NarrationEvent>
    {
        public static readonly EventContentComparer Instance = new EventContentComparer();

        public bool Equals(NarrationEvent a, NarrationEvent b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (!ReferenceEquals(a.Kind, b.Kind) || !ReferenceEquals(a.Reactor, b.Reactor) || !ReferenceEquals(a.Payload, b.Payload)) return false;
            if (!SameParts(a.Common, b.Common) || a.Items.Count != b.Items.Count) return false;
            for (int i = 0; i < a.Items.Count; i++) if (!SameParts(a.Items[i], b.Items[i])) return false;
            return true;
        }

        private static bool SameParts(List<EventPart> a, List<EventPart> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                EventPart x = a[i], y = b[i];
                if (!string.Equals(x.Key, y.Key, System.StringComparison.Ordinal) || !string.Equals(x.Text, y.Text, System.StringComparison.Ordinal)
                    || !string.Equals(x.Suffix, y.Suffix, System.StringComparison.Ordinal)) return false;
                if (!SameVariants(x.Variants, y.Variants)) return false;
            }
            return true;
        }

        private static bool SameVariants(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Count != b.Count) return false;
            foreach (var kv in a)
            {
                string v;
                if (!b.TryGetValue(kv.Key, out v) || !string.Equals(v, kv.Value, System.StringComparison.Ordinal)) return false;
            }
            return true;
        }

        public int GetHashCode(NarrationEvent e)
        {
            if (e == null) return 0;
            unchecked
            {
                int h = e.Kind != null ? e.Kind.GetHashCode() : 0;
                if (e.Reactor != null) h = h * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(e.Reactor);
                if (e.Payload != null) h = h * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(e.Payload);
                h = Mix(h, e.Common);
                foreach (var item in e.Items) h = Mix(h * 17, item);
                return h;
            }
        }

        // Keys and texts carry the content (variants follow from the text in practice).
        private static int Mix(int h, List<EventPart> parts)
        {
            unchecked
            {
                foreach (var p in parts)
                {
                    h = h * 31 + (p.Key != null ? p.Key.GetHashCode() : 0);
                    h = h * 31 + (p.Text != null ? p.Text.GetHashCode() : 0);
                }
                return h;
            }
        }
    }
}
