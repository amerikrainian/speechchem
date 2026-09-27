using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace SpeechChem
{
    /// <summary>
    /// The host's view of namemap.tsv (deob name → shipping obfuscated name; generated at build time
    /// by tools/NameMap and deployed as &lt;game&gt;\SpeechChem\namemap.tsv). EXAPUNKS kept most TYPE names,
    /// so its host could resolve "GameLogic" by real name and members by token ordinal. SpaceChem's
    /// Eazfuscator pass renamed the TYPES too (only ~190 of ~1300 survive: SpaceChem.Program, the
    /// *Editor screens, Impeller.*), so the host resolves the game's engine classes the same way the
    /// module's remapper does — through the anchor-validated map. The map is a hard runtime
    /// prerequisite for the module anyway (no map = no module), so the host adds no new dependency.
    ///
    /// Row format (tab-separated): T deobFullName shippingBareName / M deobType deobMember shippingMember
    /// / F deobType deobField shippingField. Deob type names are Cecil-style ('/' nests).
    /// </summary>
    internal static class GameNames
    {
        private static readonly Dictionary<string, string> TypeRows = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> MemberRows = new Dictionary<string, string>(StringComparer.Ordinal);
        private static Dictionary<string, Type> _typesByFullName;
        private static Assembly _game;

        public static bool Loaded { get; private set; }

        /// <summary>Read the map. False (logged) when it is missing or unreadable.</summary>
        public static bool Load(string mapPath)
        {
            TypeRows.Clear();
            MemberRows.Clear();
            _deobByShippingBare = null;
            _deobMemberByShipping = null;
            Loaded = false;
            try
            {
                if (!File.Exists(mapPath))
                {
                    Log.Error("[names] namemap.tsv not found at " + mapPath + " — the game cannot be hooked.");
                    return false;
                }
                foreach (var line in File.ReadAllLines(mapPath))
                    AddRow(line);
                Loaded = true;
                Log.Info("[names] loaded " + TypeRows.Count + " type row(s), " + MemberRows.Count + " member row(s).");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[names] namemap load failed", ex);
                return false;
            }
        }

        /// <summary>Test seam: feed rows without a file.</summary>
        internal static void LoadRows(IEnumerable<string> lines)
        {
            TypeRows.Clear();
            MemberRows.Clear();
            _deobByShippingBare = null;
            _deobMemberByShipping = null;
            foreach (var line in lines) AddRow(line);
            Loaded = true;
            _typesByFullName = null;
        }

        private static void AddRow(string line)
        {
            var parts = line.Split('\t');
            if (parts.Length >= 3 && parts[0] == "T") TypeRows[parts[1]] = parts[2];
            else if (parts.Length >= 4 && (parts[0] == "M" || parts[0] == "F"))
                MemberRows[parts[0] + "\n" + parts[1] + "\n" + parts[2]] = parts[3];
        }

        /// <summary>The shipping name for a deob member (or the deob name itself when the map has no row
        /// — members de4dot didn't rename keep their name on both sides).</summary>
        public static string Member(string kind, string deobType, string deobMember)
        {
            return MemberRows.TryGetValue(kind + "\n" + deobType + "\n" + deobMember, out var obf) ? obf : deobMember;
        }

        /// <summary>The live game type for a deob type name ("Class185", "Class53"), or null (logged).
        /// Top-level types only — the host never needs nested ones.</summary>
        public static Type Type(Assembly game, string deobFullName)
        {
            if (_typesByFullName == null || _game != game)
            {
                _game = game;
                _typesByFullName = new Dictionary<string, Type>(StringComparer.Ordinal);
                Type[] all;
                try { all = game.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { all = ex.Types; }
                foreach (var t in all)
                    if (t != null && !t.IsNested && t.FullName != null) _typesByFullName[t.FullName] = t;
            }

            string shipping = TypeRows.TryGetValue(deobFullName, out var obf) ? obf : deobFullName;
            // Renamed types lose their namespace in the T row (it holds the bare shipping name), but
            // Eazfuscator keeps the namespace itself, so re-attach it for namespaced deob names.
            if (obf != null)
            {
                int dot = deobFullName.LastIndexOf('.');
                if (dot > 0) shipping = deobFullName.Substring(0, dot + 1) + obf;
            }
            if (_typesByFullName.TryGetValue(shipping, out var type)) return type;
            Log.Error("[names] type '" + deobFullName + "' (shipping '" + Printable(shipping) + "') not found in the game assembly.");
            return null;
        }

        /// <summary>A declared method by deob name, or null (logged).</summary>
        public static MethodInfo Method(Type type, string deobType, string deobMethod)
        {
            if (type == null) return null;
            string name = Member("M", deobType, deobMethod);
            try
            {
                var m = type.GetMethod(name, MemberResolver.AllDeclared);
                if (m == null) Log.Error("[names] method " + deobType + "." + deobMethod + " not found.");
                return m;
            }
            catch (AmbiguousMatchException)
            {
                Log.Error("[names] method " + deobType + "." + deobMethod + " is ambiguous on the shipping type.");
                return null;
            }
        }

        /// <summary>A declared field by deob name, or null (logged).</summary>
        public static FieldInfo Field(Type type, string deobType, string deobField)
        {
            if (type == null) return null;
            var f = type.GetField(Member("F", deobType, deobField), MemberResolver.AllDeclared);
            if (f == null) Log.Error("[names] field " + deobType + "." + deobField + " not found.");
            return f;
        }

        // ---- reverse lookups (shipping -> deob), for dev tooling that prints live objects ----

        private static Dictionary<string, List<string>> _deobByShippingBare;
        private static Dictionary<string, string> _deobMemberByShipping;

        /// <summary>The deob name of a live game type ("Class154", "Class53/Class156"), or its own
        /// name when de4dot kept it. Cecil-style '/' nesting, like the map.</summary>
        public static string DeobTypeName(Type t)
        {
            if (t == null) return "<null>";
            if (t.IsGenericType && !t.IsGenericTypeDefinition) t = t.GetGenericTypeDefinition();
            BuildReverse();
            string parent = t.DeclaringType != null ? DeobTypeName(t.DeclaringType) : null;
            if (_deobByShippingBare.TryGetValue(t.Name, out var candidates))
            {
                foreach (var deob in candidates)
                {
                    int slash = deob.LastIndexOf('/');
                    if (parent == null ? slash < 0 : (slash > 0 && deob.Substring(0, slash) == parent))
                        return deob;
                }
            }
            return parent != null ? parent + "/" + t.Name : t.FullName ?? t.Name;
        }

        /// <summary>The deob name of a live member (field "F" / method "M") of a type whose deob name
        /// is <paramref name="deobType"/>; the shipping name itself when unmapped.</summary>
        public static string DeobMemberName(string kind, string deobType, string shippingName)
        {
            BuildReverse();
            return _deobMemberByShipping.TryGetValue(kind + Sep + deobType + Sep + shippingName, out var deob) ? deob : shippingName;
        }

        private const char Sep = '\u0001';

        private static void BuildReverse()
        {
            if (_deobByShippingBare != null) return;
            _deobByShippingBare = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var kv in TypeRows)
            {
                if (!_deobByShippingBare.TryGetValue(kv.Value, out var list))
                    _deobByShippingBare[kv.Value] = list = new List<string>();
                list.Add(kv.Key);
            }
            _deobMemberByShipping = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in MemberRows)
            {
                // MemberRows key: kind, deobType, deobMember (newline-separated) -> shipping name.
                var parts = kv.Key.Split(new[] { '\n' });
                if (parts.Length == 3) _deobMemberByShipping[parts[0] + Sep + parts[1] + Sep + kv.Value] = parts[2];
            }
        }

        /// <summary>Obfuscated names carry zero-width/control characters; keep logs readable.</summary>
        public static string Printable(string s)
        {
            if (s == null) return "<null>";
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (chars[i] > 126 || char.IsControl(chars[i])) chars[i] = '?';
            return new string(chars);
        }
    }
}
