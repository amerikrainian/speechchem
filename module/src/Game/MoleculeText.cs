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

        /// <summary>"Oxygen, O2": the name and formula as the panels draw them.</summary>
        public static string NameAndFormula(Molecule m)
        {
            if (m == null) return null;
            string name = Name(m), formula = Formula(m);
            if (string.IsNullOrEmpty(formula) || formula == name) return name;
            return name + ", " + formula;
        }

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
