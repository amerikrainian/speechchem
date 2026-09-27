using System;
using System.Collections.Generic;
using System.Reflection;

namespace SpeechChem
{
    /// <summary>
    /// A reflection-cached view onto the live game, and the host-side seam every early read goes
    /// through — read the MODEL, never the pixels.
    ///
    /// The obfuscation reality (Eazfuscator + de4dot): SpaceChem.exe keeps real names for a minority
    /// of types (SpaceChem.Program, the *Editor screens, Impeller.*) and renames the rest, members
    /// included. The host therefore resolves everything through <see cref="GameNames"/> (the
    /// build-generated, anchor-validated deob → shipping map) and cross-checks each result by
    /// signature, so a game update fails loudly instead of patching a random method. The module never
    /// needs this: it compiles against the deob names and is remapped at load.
    ///
    /// The engine, decompile-verified (game/decompiled):
    ///   Class184            abstract engine game: SDL window + GL context, the main loop (method_7),
    ///                       the boot splash loops (method_6), the SDL event pump (method_17).
    ///   Class185 : Class184 SpaceChem's concrete game. vmethod_2 = post-load init (builds the
    ///                       MainMenuEditor root screen), vmethod_3(Struct102) = the per-frame UPDATE
    ///                       (input snapshot + every screen's update), vmethod_4 = per-frame draw.
    ///   Class53             the screen ("editor") base. A doubly linked chain: static class53_0 =
    ///                       root (MainMenuEditor), class53_1 = parent (below), class53_2 = child
    ///                       (above); smethod_0() = the top screen. Screens push via smethod_1.
    /// </summary>
    public static class GameState
    {
        private const string GameTypeName = "Class185";
        private const string ScreenTypeName = "Class53";

        private static Type _screenType;
        private static FieldInfo _rootField;   // Class53.class53_0 (static root screen)
        private static FieldInfo _childField;  // Class53.class53_2 (the screen above this one)

        /// <summary>The post-load init method (postfix target). Null if unresolved.</summary>
        public static MethodInfo InitMethod { get; private set; }

        /// <summary>The per-frame update method (prefix target). Null if unresolved.</summary>
        public static MethodInfo TickMethod { get; private set; }

        public static bool Bound => _screenType != null && _rootField != null && _childField != null;

        /// <summary>The game assembly, for feature code (the module) that must resolve additional
        /// obfuscated types/members. Set by Bind.</summary>
        public static Assembly GameAssembly { get; private set; }

        /// <summary>Resolve the members we read/patch. Call once the game assembly is loaded in this
        /// AppDomain and before its entry point runs. Requires <see cref="GameNames"/> loaded.</summary>
        public static void Bind(Assembly game)
        {
            GameAssembly = game;
            var gameType = GameNames.Type(game, GameTypeName);
            _screenType = GameNames.Type(game, ScreenTypeName);

            InitMethod = CheckedMethod(gameType, GameTypeName, "vmethod_2", "init", 0);
            TickMethod = CheckedMethod(gameType, GameTypeName, "vmethod_3", "tick", 1);

            if (_screenType != null)
            {
                _rootField = CheckedScreenField("class53_0", isStatic: true);
                _childField = CheckedScreenField("class53_2", isStatic: false);
            }

            Log.Info("[gamestate] bound: init=" + MemberResolver.Describe(InitMethod) + " tick=" + MemberResolver.Describe(TickMethod)
                + " screens=" + (Bound ? "ok" : "<unresolved>"));
        }

        /// <summary>The screen chain, bottom (root) → top, or an empty list before the root exists.
        /// Read-only use only.</summary>
        public static List<object> ScreenStack()
        {
            var list = new List<object>();
            if (!Bound) return list;
            object s = _rootField.GetValue(null);
            // The chain is short (a handful of screens); the cap only guards against a corrupted cycle.
            while (s != null && list.Count < 64)
            {
                list.Add(s);
                s = _childField.GetValue(s);
            }
            return list;
        }

        /// <summary>The active screen object (top of the chain), or null.</summary>
        public static object TopScreen()
        {
            var list = ScreenStack();
            return list.Count == 0 ? null : list[list.Count - 1];
        }

        /// <summary>Concrete type name of the active screen (e.g. "MainMenuEditor"), or null. Renamed
        /// types report their obfuscated shipping name — never speak it (ScreenNames filters).</summary>
        public static string TopScreenName() => TopScreen()?.GetType().Name;

        /// <summary>Every screen on the chain, bottom → top, by DEOB type name (the names the
        /// decompile and the module use). For dev inspection.</summary>
        public static List<string> ScreenStackNames()
        {
            var names = new List<string>();
            foreach (var s in ScreenStack()) names.Add(s == null ? "<null>" : GameNames.DeobTypeName(s.GetType()));
            return names;
        }

        // ---- resolution helpers ----

        private static MethodInfo CheckedMethod(Type t, string deobType, string deobName, string role, int paramCount)
        {
            var m = GameNames.Method(t, deobType, deobName);
            // Both hooks are instance void methods with a known arity. A mismatch means the layout
            // shifted — refuse rather than patch whatever now sits under that name.
            if (m != null && (m.IsStatic || m.ReturnType != typeof(void) || m.GetParameters().Length != paramCount))
            {
                Log.Error("[gamestate] " + role + " method has unexpected signature (" + MemberResolver.Describe(m) + ") — game updated? Not hooking it.");
                return null;
            }
            return m;
        }

        private static FieldInfo CheckedScreenField(string deobName, bool isStatic)
        {
            var f = GameNames.Field(_screenType, ScreenTypeName, deobName);
            if (f != null && (f.IsStatic != isStatic || f.FieldType != _screenType))
            {
                Log.Error("[gamestate] screen field " + deobName + " has unexpected shape — game updated?");
                return null;
            }
            return f;
        }
    }
}
