using System.Collections.Generic;
using Impeller;
using SpaceChem;
using SpaceChem.Pipeline;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens.Pipeline
{
    public sealed partial class PipelineEditorScreen
    {
        // ---- the shelf (Class717: one Class472 tile per component type the level allows, each
        // holding a locked template). Enter arms a type; the next Enter on the map places it with
        // its top-left corner on that cell (one shot) — the reactor palette's rule. ----

        private const string ShelfStop = "pipeline.shelf";
        private Draggable _armed;

        private static List<Draggable> ShelfTemplates(PipelineEditor editor)
        {
            var list = new List<Draggable>();
            var shelf = editor?.class717_0;
            if (shelf?.list_0 == null) return list;
            foreach (var tile in shelf.list_0)
                if (tile is Class472 t && t.draggable_0 != null) list.Add(t.draggable_0);
            return list;
        }

        private void BuildShelf(GraphBuilder b, PipelineEditor editor)
        {
            var templates = ShelfTemplates(editor);
            if (templates.Count == 0) return;
            b.BeginStop(ShelfStop);
            for (int i = 0; i < templates.Count; i++)
            {
                var template = templates[i];
                b.AddItem(ControlId.Structural("pipeline.shelf." + i), new NodeVtable
                {
                    ControlType = ControlTypes.Button,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => template.string_1, kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => ReferenceEquals(_armed, template) ? Loc.T("reactor.armed") : null, kind: AnnouncementKinds.Selected),
                    },
                    OnActivate = () =>
                    {
                        _armed = template;
                        Speech.Tts.Speak(Loc.T("reactor.armed.instr", new { instruction = template.string_1 }), interrupt: true);
                    },
                    OnTooltip = () => Speech.Tts.Speak(GameText.Speech(template.string_2) ?? Loc.T("nav.no_tooltip"), interrupt: true),
                });
            }
        }

        /// <summary>Enter on a map cell: place the armed shelf type there.</summary>
        private void ActivateMapCell(int x, int y)
        {
            if (_armed == null) return;
            var template = _armed;
            if (Place(template, new Vector2i(x, y))) _armed = null;
        }

        private static bool Running => (int)Class258.smethod_16() != 0;

        private static bool CanEdit()
        {
            if (!Running) return true;
            Speech.Tts.Speak(Loc.T("pipeline.edit.running"), interrupt: true);
            return false;
        }

        /// <summary>The shelf tile's press and the release on the map, in one step: the template hands
        /// the pipeline an unlocked clone parked off the grid in the moving state (Draggable.method_8 →
        /// Pipeline.method_14), the "mouse" cell becomes the target, and Pipeline.method_13 validates
        /// the drop (bounds, overlaps; pipes may only cross at right angles), places it and records
        /// the undo step. A refused drop removes the parked clone, as releasing off the grid does.</summary>
        private bool Place(Draggable template, Vector2i at)
        {
            var p = Model;
            if (p == null || !CanEdit()) return false;
            try
            {
                template.method_8();
                var parked = p.draggable_0;
                if (parked == null) return false;
                bool ok;
                using (Patches.ModifierMask.NoCtrl())
                {
                    p.vector2i_3 = at;
                    ok = p.method_13();
                }
                if (!ok)
                {
                    string why = Refusal(p);
                    p.method_10(parked, null);
                    p.draggable_0 = null;
                    p.method_23();
                    Speech.Tts.Speak(Loc.T("pipeline.edit.nofit", new { what = template.string_1, why }), interrupt: true);
                    return false;
                }
                Draggable placed = null;
                foreach (var d in p.hashSet_0) placed = d;
                p.method_10(parked, null);
                p.draggable_0 = null;
                p.method_23();
                string text = Loc.T("reactor.edit.at", new { what = PipelineText.Name(p, placed), cell = PipelineText.Cell(at) });
                if (placed is ReactorDraggable && GoalTracker.int_0 > 0 && p.method_21() > GoalTracker.int_0)
                    text += ", " + Loc.T("pipeline.quota.over");
                Speech.Tts.Speak(text, interrupt: true);
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error("[pipeline] place failed", ex);
                return false;
            }
        }

        // ---- moving (user-approved: cut / paste like the reactor's hardware): Ctrl+X on a component
        // (its list entry, a port cell or any of its map cells) remembers it — it stays put until
        // the paste — and Ctrl+V on a map cell drops it with its top-left corner there, through the
        // game's own move (Pipeline.method_13 with the component selected, dragged from its origin
        // to the cell). Pipes move with it. A refused drop keeps it on the clipboard. ----

        private Draggable _cut;

        /// <summary>The component under focus: a Components entry, one of its port cells, or a map
        /// cell it occupies (pipe cells count as their owner's).</summary>
        private Draggable FocusedComponent()
        {
            var p = Model;
            if (p == null) return null;
            if (MapStop.Equals(Navigation.FocusedStopKey))
            {
                var d = p.method_7(new Vector2i(_cursorX, _cursorY));
                return d is Class612 ? null : d;
            }
            string key = Navigation.FocusedNodeId?.StructuralKey as string;
            if (key == null) return null;
            foreach (var kv in PipelineText.Components(p))
            {
                string id = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(kv.Key).ToString();
                if (key == "pipeline.comp." + id || key.StartsWith("pipeline.port." + id + ".", System.StringComparison.Ordinal))
                    return kv.Key;
            }
            return null;
        }

        private void Cut()
        {
            var p = Model;
            var d = FocusedComponent();
            if (p == null || d == null) { Speech.Tts.Speak(Loc.T("reactor.edit.nothing"), interrupt: true); return; }
            if (!CanEdit()) return;
            if (d.bool_0) { Speech.Tts.Speak(Loc.T("pipeline.edit.fixed", new { what = PipelineText.Name(p, d) }), interrupt: true); return; }
            _cut = d;
            Speech.Tts.Speak(Loc.T("reactor.edit.cut", new { what = PipelineText.Name(p, d) }), interrupt: true);
        }

        private void Paste()
        {
            var p = Model;
            if (p == null || !MapStop.Equals(Navigation.FocusedStopKey)) return;
            if (_cut == null || !p.method_9(_cut).HasValue) { _cut = null; Speech.Tts.Speak(Loc.T("reactor.edit.clipempty"), interrupt: true); return; }
            if (!CanEdit()) return;
            var item = _cut;
            var at = new Vector2i(_cursorX, _cursorY);
            try
            {
                bool ok;
                p.hashSet_0.Clear();
                p.hashSet_0.Add(item);
                p.vector2i_4 = p.method_9(item).Value;
                p.method_1((Enum18)2);
                using (Patches.ModifierMask.NoCtrl())
                {
                    p.vector2i_3 = at;
                    ok = p.method_13();
                }
                string why = ok ? null : Refusal(p);
                p.method_23();
                if (!ok)
                {
                    Speech.Tts.Speak(Loc.T("pipeline.edit.nofit", new { what = PipelineText.Name(p, item), why }), interrupt: true);
                    return;
                }
                _cut = null;
                Speech.Tts.Speak(Loc.T("reactor.edit.at", new { what = PipelineText.Name(p, item), cell = PipelineText.Cell(at) }), interrupt: true);
            }
            catch (System.Exception ex) { Log.Error("[pipeline] move failed", ex); }
        }

        /// <summary>Delete (the key, or the menu): the game's DraggableMenu "Delete" — remove it,
        /// recompute the connections, record the undo step. Fixed components can't be deleted.</summary>
        private void Delete(Draggable d)
        {
            var p = Model;
            if (p == null || d == null) { Speech.Tts.Speak(Loc.T("reactor.edit.nothing"), interrupt: true); return; }
            if (!CanEdit()) return;
            if (d.bool_0) { Speech.Tts.Speak(Loc.T("pipeline.edit.fixed", new { what = PipelineText.Name(p, d) }), interrupt: true); return; }
            string name = PipelineText.Name(p, d);
            try
            {
                p.method_10(d, null);
                p.method_15();
                Locals.smethod_0().smethod_0().method_66(new[] { d });
                if (ReferenceEquals(_cut, d)) _cut = null;
                Speech.Tts.Speak(Loc.T("reactor.edit.deleted", new { what = name }), interrupt: true);
            }
            catch (System.Exception ex) { Log.Error("[pipeline] delete failed", ex); }
        }

        /// <summary>Backspace: the game's right-click menu (Class82 / DraggableMenu) as a list —
        /// "Reset Pipes" (a component with outputs and no locked pipe: every pipe back to its stub)
        /// and "Delete" (unlocked components). The output notes and "Save to Toolbox" open game
        /// screens not modeled yet, so they are left out for now.</summary>
        private void OpenMenu()
        {
            var p = Model;
            var d = FocusedComponent();
            if (p == null || d == null || !CanEdit()) return;
            var items = new List<ActionListScreen.Item>();
            bool lockedPipe = false;
            foreach (var o in d.class485_1.Values) if (o.pipeDraggable_0 != null && o.pipeDraggable_0.bool_0) lockedPipe = true;
            if (d.class485_1.Count > 0 && !lockedPipe)
                items.Add(new ActionListScreen.Item
                {
                    Label = () => GameText.T("Reset Pipes"),
                    Run = () =>
                    {
                        d.method_6();
                        Speech.Tts.Speak(Loc.T("pipeline.edit.reset", new { what = PipelineText.Name(p, d) }), interrupt: true);
                    },
                });
            if (!d.bool_0)
                items.Add(new ActionListScreen.Item { Label = () => GameText.T("Delete"), Run = () => Delete(d) });
            if (items.Count == 0) { Speech.Tts.Speak(Loc.T("pipeline.edit.fixed", new { what = PipelineText.Name(p, d) }), interrupt: true); return; }
            PushChild(new ActionListScreen("pipeline.menu", PipelineText.Name(p, d), items));
        }

        /// <summary>Why the last drop was refused: the components it would overlap (Pipeline.hashSet_2,
        /// filled by method_12), terrain as "blocked"; nothing overlapped = off the map.</summary>
        private static string Refusal(SpaceChem.Pipeline.Pipeline p)
        {
            var names = new List<string>();
            foreach (var d in p.hashSet_2)
            {
                string n = d is Class612 || string.IsNullOrEmpty(d.string_1?.Trim()) && !(d is ReactorDraggable)
                    ? Loc.T("pipeline.terrain") : PipelineText.Name(p, d);
                if (!names.Contains(n)) names.Add(n);
            }
            return names.Count > 0 ? Loc.T("pipeline.edit.blockedby", new { what = string.Join(", ", names.ToArray()) }) : Loc.T("pipeline.edit.offmap");
        }
    }
}
