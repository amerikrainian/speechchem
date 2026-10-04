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

        public NarrationEvent(string kind)
        {
            Kind = EventKinds.Get(kind);
            Items.Add(new List<EventPart>());
        }

        private List<EventPart> Current => Items[Items.Count - 1];

        /// <summary>Add a part to the current item (empty text is skipped).</summary>
        public NarrationEvent Part(string key, string text, string suffix = null)
        {
            if (!string.IsNullOrEmpty(text)) Current.Add(new EventPart { Key = key, Text = text, Suffix = suffix });
            return this;
        }

        public NarrationEvent Part(string key, Dictionary<string, string> variants, string defaultText, string suffix = null)
        {
            if (!string.IsNullOrEmpty(defaultText)) Current.Add(new EventPart { Key = key, Text = defaultText, Variants = variants, Suffix = suffix });
            return this;
        }

        /// <summary>A molecule part with its detail variants: name and formula, name, formula.</summary>
        public NarrationEvent Molecule(Molecule m, string suffix = null)
        {
            if (m == null) return this;
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
            if (!string.IsNullOrEmpty(text)) Common.Add(new EventPart { Key = key, Text = text, Suffix = suffix, Variants = variants });
            return this;
        }

        /// <summary>Start another item.</summary>
        public NarrationEvent NextItem()
        {
            if (Current.Count > 0) Items.Add(new List<EventPart>());
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
