using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Impeller;
using SpaceChem;

namespace SpeechChem.Patches
{
    /// <summary>
    /// In-level dialogs keep only rendered text, so their inputs are captured at construction:
    ///
    ///  • SpaceChem.MessageBoxEditor — the "Reaction Error" box, the "Return to the assignment
    ///    selection screen?" prompt, the pipeline errors. The string constructor (title, text, pos,
    ///    buttons[, error markers]) renders the text into a Scene before chaining to the main
    ///    constructor, which builds one widget per Class392 button and draws a
    ///    "reactor/error_indicator" at each marker position (screen pixels — where the collision or
    ///    stray atom is). Prefixes record title, text, buttons and markers against the instance.
    ///  • Class69 — "An invalid molecule was passed to" (output name, produced molecule, accepted
    ///    molecules).
    ///
    /// Button sequences are materialized once so the game and the capture see the same items.
    /// </summary>
    internal static class DialogCapture
    {
        internal sealed class MessageBox
        {
            public string Title;
            public string Text;
            public List<Class392> Buttons;
            public List<Vector2i> Markers;
        }

        internal sealed class WrongMolecule
        {
            public string Output;
            public Molecule Produced;
            public List<Molecule> Accepted;
        }

        private static readonly ConditionalWeakTable<object, MessageBox> Boxes = new ConditionalWeakTable<object, MessageBox>();
        private static readonly ConditionalWeakTable<object, WrongMolecule> Wrong = new ConditionalWeakTable<object, WrongMolecule>();

        public static void Apply(Harmony harmony)
        {
            try
            {
                var self = typeof(DialogCapture);
                foreach (var ctor in typeof(MessageBoxEditor).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var ps = ctor.GetParameters();
                    if (ps.Length == 5 && ps[1].ParameterType == typeof(string))
                        harmony.Patch(ctor, prefix: new HarmonyMethod(self, nameof(BeforeBoxText)));
                    else if (ps.Length == 5 && ps[1].ParameterType == typeof(Scene))
                        harmony.Patch(ctor, prefix: new HarmonyMethod(self, nameof(BeforeBoxMain)));
                }
                harmony.Patch(typeof(Class69).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)[0],
                    prefix: new HarmonyMethod(self, nameof(BeforeWrongMolecule)));
                Log.Info("[patch] dialog capture armed");
            }
            catch (Exception ex) { Log.Error("[patch] dialog capture failed to apply", ex); }
        }

        public static MessageBox BoxOf(object box)
        {
            if (box == null) return null;
            return Boxes.TryGetValue(box, out var b) ? b : null;
        }

        public static WrongMolecule WrongOf(object dialog)
        {
            if (dialog == null) return null;
            return Wrong.TryGetValue(dialog, out var w) ? w : null;
        }

        private static MessageBox Entry(object instance)
        {
            if (!Boxes.TryGetValue(instance, out var b))
            {
                b = new MessageBox();
                Boxes.Add(instance, b);
            }
            return b;
        }

        // (Struct116<string> title, string text, Struct116<Vector2i> pos, IEnumerable<Class392>, IEnumerable<Vector2i>)
        private static void BeforeBoxText(MessageBoxEditor __instance, string __1)
        {
            try { if (__instance != null) Entry(__instance).Text = __1; }
            catch (Exception ex) { Log.Error("[capture] message box text", ex); }
        }

        // (Struct116<string> title, Scene text, Struct116<Vector2i> pos, IEnumerable<Class392>, IEnumerable<Vector2i>)
        private static void BeforeBoxMain(MessageBoxEditor __instance, Struct116<string> __0,
            ref IEnumerable<Class392> __3, ref IEnumerable<Vector2i> __4)
        {
            try
            {
                if (__instance == null) return;
                var b = Entry(__instance);
                b.Title = __0.bool_0 ? __0.method_0() : null;
                if (__3 != null)
                {
                    b.Buttons = new List<Class392>(__3);
                    __3 = b.Buttons;
                }
                if (__4 != null)
                {
                    b.Markers = new List<Vector2i>(__4);
                    __4 = b.Markers;
                }
            }
            catch (Exception ex) { Log.Error("[capture] message box", ex); }
        }

        // (string output, Molecule produced, IEnumerable<Molecule> accepted, Struct116<Vector2i> pos)
        private static void BeforeWrongMolecule(Class69 __instance, string __0, Molecule __1, ref IEnumerable<Molecule> __2)
        {
            try
            {
                if (__instance == null) return;
                var w = new WrongMolecule { Output = __0, Produced = __1, Accepted = new List<Molecule>() };
                if (__2 != null)
                {
                    w.Accepted = new List<Molecule>(__2);
                    __2 = w.Accepted;
                }
                Wrong.Remove(__instance);
                Wrong.Add(__instance, w);
            }
            catch (Exception ex) { Log.Error("[capture] wrong molecule", ex); }
        }
    }
}
