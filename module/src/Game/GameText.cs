using System;

namespace SpeechChem.Game
{
    /// <summary>
    /// The GAME's own localized strings, typed. SpaceChem localizes through
    /// Class323.smethod_0(key) / smethod_1(key, comment) → Class239's per-language table (lang\&lt;code&gt;
    /// files; the game ships seven languages). The keys are literally the English text, so a failed
    /// lookup falls back to the key and stays readable. HARD RULE COROLLARY: anything the game has a
    /// label for is read from HERE, never duplicated in our locale tables.
    /// </summary>
    internal static class GameText
    {
        /// <summary>The game's current text for a loc key ("Continue", "has been lost.", …).</summary>
        public static string T(string key) => Lookup(key, null);

        /// <summary>The comment-qualified lookup (the game passes "ENGLISH ALPHABET ONLY" for strings
        /// drawn in its display fonts, which lack non-Latin glyphs; translations differ by comment).</summary>
        public static string T(string key, string comment) => Lookup(key, comment);

        private static string Lookup(string key, string comment)
        {
            if (key == null) return null;
            try
            {
                string s = comment == null ? Class323.smethod_0(key) : Class323.smethod_1(key, comment);
                if (!string.IsNullOrEmpty(s)) return s;
            }
            catch (Exception ex) { Log.Error("[gametext] lookup failed for '" + key + "': " + ex.Message); }
            return key;
        }

        /// <summary>Speech massage for a game string: the game hard-wraps drawn text with newlines.</summary>
        public static string Speech(string s)
        {
            if (s == null) return null;
            return s.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        }
    }
}
