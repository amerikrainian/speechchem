using System.Collections.Generic;
using SpaceChem;

namespace SpeechChem.Game
{
    /// <summary>
    /// Speakable molecule text. Molecule.string_0 is the (localized) name, string_1 the formula,
    /// which carries the display font's markup: Class307.smethod_0 puts "~0" before every digit
    /// (subscripts) and turns charge signs into "~10" (+) and "~11" (−). The markup is stripped;
    /// everything else is the game's own text.
    /// </summary>
    internal static class MoleculeText
    {
        public static string Name(Molecule m) => m?.string_0;

        public static string Formula(Molecule m) => Clean(m?.string_1);

        /// <summary>"Oxygen, O2": the name and formula as the panels draw them. A molecule built in
        /// the reactor (bonded or split — MoleculeSheet.method_8 makes a fresh Molecule) still holds
        /// the class's placeholders, "Unknown" / "???" (Molecule fields string_0 / string_1) until
        /// it leaves through an output, where the game names it (Class77 → Class307.smethod_1).
        /// So it is named the GAME's way first (Class307.smethod_2: one atom = the element; else the
        /// first match in the game's catalog of named molecules, list_0, then a custom level's own,
        /// list_1 — the hand-written names and formulas, "Hydrochloric Acid", "HCl"); then from the
        /// level's panels (a puzzle molecule the catalog lacks); only then from its atoms, where
        /// the game itself would say "Unknown".</summary>
        public static string NameAndFormula(Molecule m)
        {
            if (m == null) return null;
            if (m.string_1 == "???") return GameName(m) ?? NameAndFormula(Known(m)) ?? FromAtoms(m);
            string name = Name(m), formula = Formula(m);
            if (string.IsNullOrEmpty(formula) || formula == name) return name;
            return name + ", " + formula;
        }

        /// <summary>The game's own name for a built molecule (Class307.smethod_2), or null when the
        /// game has none for it.</summary>
        private static string GameName(Molecule m)
        {
            try
            {
                var named = Class307.smethod_2(m);
                if (!named.bool_0) return null;
                var n = named.method_0();
                if (n == null || n.string_1 == "???") return null;
                string name = n.string_0, formula = Clean(n.string_1);
                if (string.IsNullOrEmpty(formula) || formula == name) return name;
                return name + ", " + formula;
            }
            catch { return null; }
        }

        /// <summary>The level's named molecule this built one is — matched the way an output accepts
        /// molecules (Molecule.method_2: the same atoms bonded the same way, any position or
        /// rotation) against every molecule panel on the pipeline: each component's port
        /// annotations (storage tanks' inputs, freighters' and research outputs' targets, reactor
        /// notes), CONNECTED OR NOT — matching only through pipes missed a freighter's molecule
        /// until the reactor's output was piped to it ("ClH" for Hydrochloric Acid). Null when
        /// none matches.</summary>
        private static Molecule Known(Molecule m)
        {
            try
            {
                var pipeline = Class53.smethod_5<SpaceChem.Pipeline.PipelineEditor>()?.pipeline_0;
                if (pipeline == null) return null;
                foreach (var kv in pipeline)
                {
                    var d = kv.Key;
                    if (d == null) continue;
                    foreach (var port in d.class485_0)
                        if (Match(port.Value.method_0(), m, out var hit)) return hit;
                    foreach (var port in d.class485_1)
                        if (Match(port.Value.method_0(), m, out var hit)) return hit;
                }
            }
            catch { }
            return null;
        }

        private static bool Match(SpaceChem.Pipeline.Annotation a, Molecule m, out Molecule hit)
        {
            hit = null;
            if (a == null) return false;
            foreach (var known in a.vmethod_6())
            {
                if (known == null || known.string_1 == "???" || !m.method_2(known)) continue;
                hit = known;
                return true;
            }
            return false;
        }

        /// <summary>One atom: "Fluorine, F", as an input's single atom reads. Several (a molecule
        /// the game has no name for — it would say "Unknown"): a formula in Hill order — carbon,
        /// then hydrogen, then the rest alphabetically (with no carbon, all alphabetically). The
        /// game's catalog formulas are hand-written and only mostly follow it ("CH4", "AgF", but
        /// "H2O", "HCl", "NH3"), which is why the game's own lookup goes first.</summary>
        private static string FromAtoms(Molecule m)
        {
            var counts = new Dictionary<string, int>();
            Atom? only = null;
            int atoms = 0;
            foreach (var atom in m.dictionary_2.Values)
            {
                atoms++;
                only = atom;
                string symbol = atom.element_0.smethod_2();
                counts[symbol] = counts.TryGetValue(symbol, out var n) ? n + 1 : 1;
            }
            if (atoms == 0) return null;
            if (atoms == 1) return only.Value.method_0() + ", " + only.Value.element_0.smethod_2();
            var symbols = new List<string>(counts.Keys);
            bool carbon = counts.ContainsKey("C");
            symbols.Sort((a, b) =>
            {
                int ra = Rank(a, carbon), rb = Rank(b, carbon);
                return ra != rb ? ra.CompareTo(rb) : string.CompareOrdinal(a, b);
            });
            var formula = new System.Text.StringBuilder();
            foreach (var s in symbols)
            {
                formula.Append(s);
                if (counts[s] > 1) formula.Append(counts[s]);
            }
            return formula.ToString();
        }

        private static int Rank(string symbol, bool carbon)
            => !carbon ? 2 : symbol == "C" ? 0 : symbol == "H" ? 1 : 2;

        /// <summary>Strip the formula font markup.</summary>
        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("~10", "+").Replace("~11", "-").Replace("~0", "");
        }

        public static string JoinNames(IEnumerable<Molecule> molecules)
        {
            var parts = new List<string>();
            if (molecules != null)
                foreach (var m in molecules) parts.Add(NameAndFormula(m));
            return string.Join("; ", parts.ToArray());
        }
    }
}
