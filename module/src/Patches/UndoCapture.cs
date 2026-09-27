using System;
using System.Collections.Generic;
using HarmonyLib;
using SpaceChem;
using SpaceChem.Reactor;
using SpeechChem.Game;
using SpeechChem.Localization;

namespace SpeechChem.Patches
{
    /// <summary>
    /// Undo / redo feedback in the reactor (user request, 2026-09-27). Every path — Ctrl+Z / Ctrl+Y
    /// (Reactor polls Keys.Undo / Keys.Redo) and the toolbar buttons (ToolbarComponent.method_10 /
    /// method_11) — lands in SpaceChemUserWorker.method_46 (undo) / method_47 (redo), which replay
    /// the save's SQLite history and rebuild what changed synchronously (method_52 → method_54
    /// reloads the reactor and re-pushes its editor). The history carries no description, so a
    /// prefix snapshots the open reactor's members and a postfix diffs the rebuilt one: what
    /// appeared ("red grab drop at 3, 2"), what went ("removed red rotate clockwise at 10, 8"),
    /// what moved; a cell whose occupant changed reads as its new occupant. No "Undo:" prefix —
    /// the user knows which key they pressed. At the end of the history (the toolbar's own
    /// button disabled) it says "Nothing to undo" / "Nothing to redo". Reactor only: with no
    /// reactor editor open (the pipeline) it stays silent.
    /// </summary>
    internal static class UndoCapture
    {
        private struct Entry
        {
            public int X, Y, Layer;
            public string Label;
        }

        private sealed class Before
        {
            public List<Entry> Members;
            public bool Available;
        }

        private const int MaxNamed = 3;

        public static void Apply(Harmony harmony)
        {
            try
            {
                var self = typeof(UndoCapture);
                harmony.Patch(Expr.MethodOf(() => default(SpaceChemUserWorker).method_46()),
                    prefix: new HarmonyMethod(self, nameof(BeforeUndo)), postfix: new HarmonyMethod(self, nameof(AfterUndo)));
                harmony.Patch(Expr.MethodOf(() => default(SpaceChemUserWorker).method_47()),
                    prefix: new HarmonyMethod(self, nameof(BeforeRedo)), postfix: new HarmonyMethod(self, nameof(AfterRedo)));
                Log.Info("[patch] undo capture armed");
            }
            catch (Exception ex) { Log.Error("[patch] undo capture failed to apply", ex); }
        }

        private static void BeforeUndo(out Before __state) => __state = Snapshot(undo: true);
        private static void BeforeRedo(out Before __state) => __state = Snapshot(undo: false);
        private static void AfterUndo(Before __state) => Report(__state, undo: true);
        private static void AfterRedo(Before __state) => Report(__state, undo: false);

        private static Before Snapshot(bool undo)
        {
            try
            {
                var members = Members();
                if (members == null) return null;
                bool available = true;
                try
                {
                    var tb = SpaceChem.UI.ToolbarComponent.smethod_0();
                    var button = undo ? tb.gclass15_0 : tb.gclass15_1;
                    if (button != null) available = button.method_7();
                }
                catch { }
                return new Before { Members = members, Available = available };
            }
            catch { return null; }
        }

        private static void Report(Before before, bool undo)
        {
            try
            {
                if (before == null) return;
                if (!before.Available)
                {
                    Speech.Tts.Speak(Loc.T(undo ? "undo.none" : "redo.none"), interrupt: true);
                    return;
                }
                var after = Members();
                if (after == null) return;
                string text = Describe(before.Members, after);
                if (text != null) Speech.Tts.Speak(text, interrupt: true);
            }
            catch (Exception ex) { Log.Error("[undo] report failed", ex); }
        }

        /// <summary>The open reactor's members as (cell, layer, spoken label), or null when no
        /// reactor editor is open.</summary>
        private static List<Entry> Members()
        {
            var r = Class53.smethod_5<Class77>()?.reactor_0;
            if (r == null) return null;
            var list = new List<Entry>();
            foreach (var kv in r)
            {
                int layer = (int)kv.Value.enum114_0;
                string label = null;
                if (kv.Key is Instruction i) label = Colour(layer) + " " + ReactorText.Label(i);
                else if (kv.Key is ReactorFeature f) label = ReactorText.FeatureLabel(f);
                if (string.IsNullOrEmpty(label)) continue;
                list.Add(new Entry { X = kv.Value.vector2i_0.int_0, Y = kv.Value.vector2i_0.int_1, Layer = layer, Label = label });
            }
            return list;
        }

        private static string Colour(int layer)
            => Loc.T((layer & (ReactorText.Red | ReactorText.RedArrow)) != 0 ? "reactor.red" : "reactor.blue");

        private static string Cell(Entry e) => Loc.T("reactor.cell", new { x = e.X + 1, y = e.Y + 1 });

        /// <summary>The difference as speech, or null when nothing on the board changed.</summary>
        private static string Describe(List<Entry> before, List<Entry> after)
        {
            var removed = new List<Entry>(before);
            var added = new List<Entry>();
            foreach (var e in after)
            {
                int k = removed.FindIndex(o => o.X == e.X && o.Y == e.Y && o.Layer == e.Layer && o.Label == e.Label);
                if (k >= 0) removed.RemoveAt(k);
                else added.Add(e);
            }
            if (removed.Count == 0 && added.Count == 0) return null;

            var phrases = new List<string>();
            // A cell whose occupant changed (variant, colour): just the new occupant.
            for (int a = added.Count - 1; a >= 0; a--)
            {
                var e = added[a];
                int k = removed.FindIndex(o => o.X == e.X && o.Y == e.Y);
                if (k < 0) continue;
                removed.RemoveAt(k);
                added.RemoveAt(a);
                phrases.Add(Loc.T("undo.placed", new { what = e.Label, cell = Cell(e) }));
            }
            // The same piece on the same layer somewhere else: a move.
            for (int a = added.Count - 1; a >= 0; a--)
            {
                var e = added[a];
                int k = removed.FindIndex(o => o.Layer == e.Layer && o.Label == e.Label);
                if (k < 0) continue;
                var from = removed[k];
                removed.RemoveAt(k);
                added.RemoveAt(a);
                phrases.Add(Loc.T("undo.moved", new { what = e.Label, from = Cell(from), to = Cell(e) }));
            }
            foreach (var e in added) phrases.Add(Loc.T("undo.placed", new { what = e.Label, cell = Cell(e) }));
            foreach (var e in removed) phrases.Add(Loc.T("undo.removed", new { what = e.Label, cell = Cell(e) }));

            if (phrases.Count <= MaxNamed) return string.Join("; ", phrases.ToArray());
            return Loc.T("undo.many", new { n = phrases.Count, list = string.Join("; ", phrases.GetRange(0, MaxNamed - 1).ToArray()) });
        }
    }
}
