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
        // its top-left corner on that cell (one shot) — the reactor palette's rule. Armed from the
        // Shelf category instead it is STICKY (user rule 2026-10-07): every Enter on the map places
        // another, until Escape, arming something else, or starting a pipe drawing (Enter on an
        // open pipe end draws instead of placing). ----

        private const string ShelfStop = "pipeline.shelf";
        private Draggable _armed;
        private bool _armedSticky;

        /// <summary>The Shelf category's items: the stock tiles, then the saved designs.</summary>
        private sealed class ShelfItem
        {
            public Draggable Template;
            public string Label;
            public bool Locked;
        }

        private List<ShelfItem> ShelfItems()
        {
            var items = new List<ShelfItem>();
            var editor = Editor;
            if (editor == null || DefenseText.ControlsShown) return items; // a defense run shows Reactor Controls instead
            foreach (var t in ShelfTemplates(editor)) items.Add(new ShelfItem { Template = t, Label = ShelfLabel(t) });
            foreach (var d in SavedDesigns()) items.Add(new ShelfItem { Template = d.Template, Label = DesignLabel(d), Locked = d.Locked });
            return items;
        }

        /// <summary>, / . in the Shelf category: name the item and arm it, sticky. A locked design
        /// is only named (its label says "(LOCKED)").</summary>
        private void ArmFromCategory(ShelfItem item)
        {
            Speech.Tts.Speak(item.Label, interrupt: true);
            if (item.Locked) return;
            _armed = item.Template;
            _armedSticky = true;
        }

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
            var designs = SavedDesigns();
            if (templates.Count == 0 && designs.Count == 0) return;
            b.BeginStop(ShelfStop);
            // One list, counted as one (a design's row would count its Delete cell).
            int total = templates.Count + designs.Count;
            for (int i = 0; i < templates.Count; i++)
            {
                var template = templates[i];
                int position = i + 1;
                var vt = new NodeVtable
                {
                    ControlType = ControlTypes.Button,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => ShelfLabel(template), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => ReferenceEquals(_armed, template) ? Loc.T("reactor.armed") : null, kind: AnnouncementKinds.Selected),
                        ShelfPosition(position, total),
                    },
                    SpeaksOwnPosition = true,
                    OnActivate = () =>
                    {
                        _armed = template;
                        _armedSticky = false;
                        Speech.Tts.Speak(Loc.T("pipeline.armed"), interrupt: true); // the focused tile already names it (user rule)
                    },
                    OnTooltip = () => Speech.Tts.Speak(PipelineText.Tooltip(template) ?? Loc.T("nav.no_tooltip"), interrupt: true),
                    OnSelect = () => ShowShelfPage(-1),
                };
                b.AddItem(ControlId.Structural("pipeline.shelf." + i), vt);
            }
            for (int i = 0; i < designs.Count; i++)
            {
                var design = designs[i];
                int index = i;
                int position = templates.Count + i + 1;
                b.StartRow("pipeline.shelf.design");
                b.AddItem(DesignId(i), new NodeVtable
                {
                    ControlType = ControlTypes.Button,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => DesignLabel(design), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => ReferenceEquals(_armed, design.Template) ? Loc.T("reactor.armed") : null, kind: AnnouncementKinds.Selected),
                        ShelfPosition(position, total),
                    },
                    SpeaksOwnPosition = true,
                    OnActivate = () =>
                    {
                        // A design the level can't use (its type isn't on the shelf, or the program
                        // has members this level forbids) draws "(LOCKED)" and won't drag.
                        if (design.Locked) { Speech.Tts.Speak(Loc.T("value.unavailable"), interrupt: true); return; }
                        _armed = design.Template;
                        _armedSticky = false;
                        Speech.Tts.Speak(Loc.T("pipeline.armed"), interrupt: true);
                    },
                    OnSecondary = () => DesignMenu(design),
                    OnTooltip = () => Speech.Tts.Speak(PipelineText.Tooltip(design.Template) ?? Loc.T("nav.no_tooltip"), interrupt: true),
                    OnSelect = () => ShowShelfPage(index / DesignsPerPage),
                });
                // The tile's red X (Class470), to its right — as the profile picker's delete.
                var delete = ProfileUi.Button(() => GameText.T("Delete"), () => DeleteDesign(design));
                delete.SpeaksOwnPosition = true;
                delete.OnSelect = () => ShowShelfPage(index / DesignsPerPage);
                b.AddItem(ControlId.Structural("pipeline.shelf.design.delete." + i), delete);
                b.EndRow();
            }
        }

        private static NodeAnnouncement ShelfPosition(int index, int count)
            => new NodeAnnouncement(() => Loc.T("nav.position", new { index, count }), kind: AnnouncementKinds.Position);

        // ---- saved reactor designs (the shelf's second tab, Class717.gclass11_1): every design of
        // the profile, level-less Component rows, read 4 per page by SpaceChemUserWorker.method_60
        // as locked templates (string_0 = the design's name, string_1 = the reactor type) — ONE list
        // here, all pages, the game's tab and page following focus (the paged-list rule). Placing
        // one is the stock tiles' drop (the template hands the pipeline an unlocked clone carrying
        // its program and notes). Only in levels that allow saved designs (Class83.bool_0). ----

        private const int DesignsPerPage = 4;

        private sealed class Design
        {
            public Draggable Template;
            public bool Locked;
        }

        private List<Design> _designs;
        private object _designsPage; // the shelf's page widget (Class717.gclass10_1) the cache was read under

        private static ControlId DesignId(int i) => ControlId.Structural("pipeline.shelf.design." + i);

        private static bool DesignsAllowed => (Editor?.method_0() as Class83)?.bool_0 == true;

        /// <summary>The saved designs, re-read whenever the game rebuilt the shelf's saved page (a
        /// save or delete refreshes it — Class717.bool_0 — once the worker has written the row).</summary>
        private List<Design> SavedDesigns()
        {
            var shelf = Class717.smethod_0();
            if (!DesignsAllowed || shelf == null) return new List<Design>();
            if (_designs != null && ReferenceEquals(_designsPage, shelf.gclass10_1)) return _designs;
            _designsPage = shelf.gclass10_1;
            var list = new List<Design>();
            try
            {
                var worker = Locals.smethod_0().smethod_0();
                int pages = worker.method_59();
                for (int page = 0; page < pages; page++)
                    foreach (var item in worker.method_60(shelf.pipeline_0, page))
                        list.Add(new Design
                        {
                            Template = item.gparam_0,
                            Locked = !item.gparam_1 || !shelf.hashSet_0.Contains(item.gparam_0.GetType()),
                        });
            }
            catch (System.Exception ex) { Log.Error("[pipeline] reading saved designs failed", ex); }
            _designs = list;
            return list;
        }

        /// <summary>"Ethylene, Standard Reactor, 2 inputs, 2 outputs" (+ the tile's "(LOCKED)").</summary>
        private static string DesignLabel(Design d)
        {
            string text = (d.Template.string_0 ?? "").Trim() + ", " + d.Template.string_1 + ", " + PipelineText.Ports(d.Template);
            return d.Locked ? text + ", " + GameText.Speech(GameText.T("\n(LOCKED)")) : text;
        }

        /// <summary>Focus on the shelf shows the matching tab and page (stock: -1): Class717.method_3
        /// switches tab, method_7 builds a saved page. The rebuild is ours, so the cache keeps it.</summary>
        private void ShowShelfPage(int page)
        {
            try
            {
                var shelf = Class717.smethod_0();
                if (shelf == null) return;
                bool stockShown = shelf.gclass11_0.method_9();
                if (page < 0) { if (!stockShown) shelf.method_3(bool_2: true); return; }
                if (page >= shelf.int_1) return;
                if (shelf.int_0 != page || stockShown)
                {
                    bool ours = ReferenceEquals(_designsPage, shelf.gclass10_1);
                    shelf.int_0 = page;
                    shelf.gclass10_1 = shelf.method_7(page);
                    shelf.method_3(bool_2: false);
                    if (ours) _designsPage = shelf.gclass10_1;
                }
            }
            catch (System.Exception ex) { Log.Error("[pipeline] shelf page failed", ex); }
        }

        private void DesignMenu(Design d)
        {
            var items = new List<ActionListScreen.Item>
            {
                new ActionListScreen.Item { Label = () => GameText.T("Delete"), Run = () => DeleteDesign(d) },
            };
            PushChild(new ActionListScreen("pipeline.design.menu", DesignLabel(d), items));
        }

        /// <summary>The tile's red X: the game's own confirm box (Class470.vmethod_4) — Yes deletes
        /// the design's rows (method_61) and refreshes the shelf; focus starts on No (irreversible).</summary>
        private void DeleteDesign(Design d)
        {
            try
            {
                var box = new MessageBoxEditor(Struct7.struct7_0,
                    GameText.T("Are you sure you want to delete this reactor design? This action cannot be undone."), Struct7.struct7_0,
                    new Class392[2]
                    {
                        new Class392(GameText.T("Yes"), new Keys[0], () =>
                        {
                            Locals.smethod_0().smethod_0().method_61(d.Template); // synchronous
                            _designs = null; // re-read now, not when the shelf next rebuilds
                        }),
                        new Class392(GameText.T("No"), new Keys[1] { Keys.Escape }, Class396.action_0),
                    });
                MessageBoxEditorScreen.StartOnLastFor = box;
                if (ReferenceEquals(_armed, d.Template)) _armed = null;
                Class53.smethod_1(box);
            }
            catch (System.Exception ex) { Log.Error("[pipeline] delete design failed", ex); }
        }

        /// <summary>The saved design under focus (its entry or its Delete cell), or null.</summary>
        private Design FocusedDesign()
        {
            string key = Navigation.FocusedNodeId?.StructuralKey as string;
            if (_designs == null || key == null || !key.StartsWith("pipeline.shelf.design.", System.StringComparison.Ordinal)) return null;
            int i;
            string tail = key.Substring(key.LastIndexOf('.') + 1);
            return int.TryParse(tail, out i) && i < _designs.Count ? _designs[i] : null;
        }

        /// <summary>"Standard Reactor, 4 by 4, 2 inputs, 2 outputs": the name, the body's size in
        /// cells (Draggable vector2i_0; the output pipes start one column beyond it) and the ports
        /// the tile's picture shows.</summary>
        private static string ShelfLabel(Draggable template)
        {
            var size = template.vector2i_0;
            string text = size.int_0 <= 0 || size.int_1 <= 0 ? template.string_1
                : Loc.T("pipeline.shelf.item", new { name = template.string_1, w = size.int_0, h = size.int_1 });
            return text + ", " + PipelineText.Ports(template);
        }

        /// <summary>Escape anywhere on the pipeline (user rule): drop the armed shelf type.</summary>
        private void Unarm()
        {
            if (_armed == null) return;
            _armed = null;
            _armedSticky = false;
            Speech.Tts.Speak(Loc.T("pipeline.unarmed"), interrupt: true);
        }

        /// <summary>Enter on a map cell: end drawing; else place the armed shelf type there; else,
        /// on a pipe's end, start drawing that pipe.</summary>
        private void ActivateMapCell(int x, int y)
        {
            if (_drawPipe != null) { EndDraw(); return; }
            var p = Model;
            var pipe = p == null ? null : PipeEndingAt(p, new Vector2i(x, y));
            if (_armed != null && !(_armedSticky && pipe != null)) // a sticky arm yields to a pipe end: draw
            {
                var template = _armed;
                if (Place(template, new Vector2i(x, y)) && !_armedSticky) _armed = null;
                return;
            }
            if (pipe != null) { StartDraw(pipe); return; }
            var reactor = p == null ? null : ReactorBodyAt(p, new Vector2i(x, y));
            if (reactor != null) OpenReactor(reactor); // the double-click, as Enter on its Components entry (user rule)
        }

        /// <summary>The reactor whose BODY covers a map cell (not its pipes), or null.</summary>
        private static ReactorDraggable ReactorBodyAt(SpaceChem.Pipeline.Pipeline p, Vector2i cell)
        {
            if (!(p.method_7(cell) is ReactorDraggable rd)) return null;
            var origin = p.method_9(rd);
            if (!origin.HasValue) return null;
            var local = cell - origin.Value;
            var size = rd.vector2i_0;
            return local.int_0 >= 0 && local.int_1 >= 0 && local.int_0 < size.int_0 && local.int_1 < size.int_1 ? rd : null;
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
            CloseDrag(); // edits never run inside the drawing's undo step
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
                    string why = Refusal(p, parked, at);
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
        // to the cell). Pipes move with it. A refused drop keeps it on the clipboard. With the map
        // cursor in the marked rectangle, the clipboard takes every marked building and the
        // rectangle's top-left corner is the anchor that lands on the paste cell
        // (PipelineEditorScreen.Mark.cs). ----

        private readonly List<Draggable> _clip = new List<Draggable>();
        private Vector2i _clipAnchor; // the clipboard's cell that lands on the paste cell
        private bool _copy; // the clipboard holds a copy (Ctrl+C): the paste duplicates, the source stays

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

        private void Cut() => Take(copy: false);

        /// <summary>Ctrl+C: the game's Ctrl-drag copy (training: "Copy reactors within a production
        /// assignment by holding the control key and dragging the reactor"), split like the move —
        /// the paste drops a duplicate (program, notes and pipe shapes; input links cleared) through
        /// the same Pipeline.method_13 with Ctrl forced on, and the clipboard keeps the source for
        /// more copies. Fixed components can't be copied, as the game refuses to drag them.</summary>
        private void Copy() => Take(copy: true);

        private void Take(bool copy)
        {
            var p = Model;
            if (p == null) return;
            CloseDrag();
            bool inRect = CursorInRectangle;
            int x0 = 0, y0 = 0, x1 = 0, y1 = 0;
            // Zoomed with no rectangle: the block's buildings, anchored at its top-left (Zoom.cs).
            if (inRect ? MarkBounds(out x0, out y0, out x1, out y1) : Zoomed && OnMap && CursorBlockBounds(out x0, out y0, out x1, out y1))
            {
                var marked = BuildingsIn(p, x0, y0, x1, y1);
                if (marked.Count == 0) return; // nothing movable inside: silent (user rule 2026-10-07)
                if (!CanEdit()) return;
                _clip.Clear();
                _clip.AddRange(marked);
                _clipAnchor = new Vector2i(x0, y0);
                _copy = copy;
                if (inRect) DropMark();
                var names = new List<string>();
                foreach (var d in marked) names.Add(PipelineText.Name(p, d));
                Speech.Tts.Speak(Loc.T(copy ? "reactor.edit.copied" : "reactor.edit.cut", new { what = Summary(names) }), interrupt: true);
                return;
            }
            var item = FocusedComponent();
            if (item == null) return;
            if (!CanEdit()) return;
            if (item.bool_0) { Speech.Tts.Speak(Loc.T("pipeline.edit.fixed", new { what = PipelineText.Name(p, item) }), interrupt: true); return; }
            _clip.Clear();
            _clip.Add(item);
            _clipAnchor = p.method_9(item).Value;
            _copy = copy;
            Speech.Tts.Speak(Loc.T(copy ? "reactor.edit.copied" : "reactor.edit.cut", new { what = PipelineText.Name(p, item) }), interrupt: true);
        }

        private void Paste()
        {
            var p = Model;
            if (p == null || !MapStop.Equals(Navigation.FocusedStopKey)) return;
            _clip.RemoveAll(d => !p.method_9(d).HasValue); // deleted, or gone in an undo's reload
            if (_clip.Count == 0) return;
            if (!CanEdit()) return;
            CloseDrag();
            var items = new List<Draggable>(_clip);
            var at = PlacementCell(); // zoomed: the block's top-left
            try
            {
                bool ok;
                p.hashSet_0.Clear();
                foreach (var d in items) p.hashSet_0.Add(d);
                p.vector2i_4 = _clipAnchor;
                p.method_1((Enum18)2);
                // A copy: Ctrl held makes method_13 clone each selected item (and counts the sources
                // as blockers, so a copy can't overlap them).
                using (_copy ? Patches.ModifierMask.ForceCtrl() : Patches.ModifierMask.NoCtrl())
                {
                    p.vector2i_3 = at;
                    ok = p.method_13();
                }
                // Refused: each item that didn't fit (method_12's hashSet_1) and what was in its way.
                var refusals = new List<string>();
                if (!ok)
                    foreach (var d in items)
                        if (p.hashSet_1.Contains(d))
                            refusals.Add(Loc.T("pipeline.edit.nofit", new { what = PipelineText.Name(p, d), why = Refusal(p, d, p.method_9(d).Value + at - _clipAnchor) }));
                var placed = ok && _copy ? new List<Draggable>(p.hashSet_0) : items; // a copy's selection is now the clones
                p.method_23();
                if (!ok)
                {
                    string text = refusals.Count <= MaxNamed ? string.Join("; ", refusals.ToArray())
                        : string.Join("; ", refusals.GetRange(0, MaxNamed).ToArray()) + " " + Loc.T("pipeline.edit.more", new { n = refusals.Count - MaxNamed });
                    Speech.Tts.Speak(text, interrupt: true);
                    return;
                }
                if (!_copy) _clip.Clear(); // a copy stays on the clipboard for more
                DropMark();
                PipelineText.Sync(p); // number the new copies before they are named
                // What landed where ("Assembly Reactor 3 at 9, 2"; past three, "5 items at 3, 2").
                var landed = new List<string>();
                bool reactor = false;
                foreach (var d in placed)
                {
                    var cell = p.method_9(d);
                    landed.Add(cell.HasValue ? Loc.T("reactor.edit.at", new { what = PipelineText.Name(p, d), cell = PipelineText.Cell(cell.Value) }) : PipelineText.Name(p, d));
                    reactor |= d is ReactorDraggable;
                }
                string said = landed.Count <= MaxNamed ? string.Join("; ", landed.ToArray())
                    : Loc.T("reactor.edit.at", new { what = Loc.T("reactor.edit.items", new { n = landed.Count }), cell = PipelineText.Cell(at) });
                if (_copy && reactor && GoalTracker.int_0 > 0 && p.method_21() > GoalTracker.int_0)
                    said += ", " + Loc.T("pipeline.quota.over");
                Speech.Tts.Speak(said, interrupt: true);
            }
            catch (System.Exception ex) { Log.Error("[pipeline] move failed", ex); }
        }

        /// <summary>Delete (the key, or the menu): the game's DraggableMenu "Delete" — remove it,
        /// recompute the connections, record the undo step. Fixed components can't be deleted.</summary>
        private void Delete(Draggable d) => Delete(d, ComponentsStop.Equals(Navigation.FocusedStopKey), viaMenu: false);

        /// <summary>Delete, then say only where focus is (user rule: no "Deleted"): from the
        /// Components list the next building (the previous when it was last); on the map the cell,
        /// now empty. From the menu the list closes first, so the list's landing waits for it
        /// (<see cref="_deleteFocus"/>) and the map cell is re-read by the return itself.</summary>
        private void Delete(Draggable d, bool fromList, bool viaMenu)
        {
            var p = Model;
            if (p == null || d == null) return;
            if (!CanEdit()) return;
            if (d.bool_0) { Speech.Tts.Speak(Loc.T("pipeline.edit.fixed", new { what = PipelineText.Name(p, d) }), interrupt: true); return; }
            CloseDrag();
            string name = PipelineText.Name(p, d);
            // Deleted from the Components list: its entry vanishes, so land on the next building
            // (the previous one when it was last) instead of wherever the navigator re-seats.
            Draggable neighbour = null;
            bool onMap = !viaMenu && MapStop.Equals(Navigation.FocusedStopKey);
            if (fromList)
            {
                var list = PipelineText.Components(p);
                int i = list.FindIndex(kv => ReferenceEquals(kv.Key, d));
                if (i >= 0 && list.Count > 1) neighbour = list[i + 1 < list.Count ? i + 1 : i - 1].Key;
            }
            try
            {
                p.method_10(d, null);
                p.method_15();
                Locals.smethod_0().smethod_0().method_66(new[] { d });
                _clip.Remove(d);
                if (neighbour != null)
                {
                    if (viaMenu) _deleteFocus = ComponentId(neighbour);
                    else Navigation.FocusNode(ComponentId(neighbour));
                }
                else if (onMap) Speech.Tts.Speak(MapReadout(_cursorX, _cursorY), interrupt: true);
                else if (!viaMenu) Speech.Tts.Speak(Loc.T("reactor.edit.deleted", new { what = name }), interrupt: true); // nothing left to land on
            }
            catch (System.Exception ex) { Log.Error("[pipeline] delete failed", ex); }
        }

        /// <summary>Backspace: the game's right-click menu (Class82 / DraggableMenu) as a list, in its
        /// order — "Reset Pipes" (a component with outputs and no locked pipe: every pipe back to its
        /// stub); on a reactor its output notes ("Add Note to Upper Output" / "Edit Upper Note
        /// (Visible)" / "(Hidden)", the same for Lower: the Output Note Editor, NoteEditorScreen);
        /// "Save to Toolbox" (an unlocked reactor in a level that allows saved designs, Class83.bool_0:
        /// the name dialog, SaveDesignScreen); "Delete" (unlocked components).</summary>
        private void OpenMenu()
        {
            var p = Model;
            var d = FocusedComponent();
            if (p == null || d == null || !CanEdit()) return;
            CloseDrag();
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
            if (d is ReactorDraggable rd)
            {
                AddNoteItem(items, rd, 0, "Upper");
                AddNoteItem(items, rd, 1, "Lower");
                if (!d.bool_0 && (Editor?.method_0() as Class83)?.bool_0 == true)
                    items.Add(new ActionListScreen.Item
                    {
                        Label = () => GameText.T("Save to Toolbox"),
                        Run = () => Editor?.method_3(new Class55(rd)),
                    });
            }
            bool fromList = ComponentsStop.Equals(Navigation.FocusedStopKey);
            if (!d.bool_0)
                items.Add(new ActionListScreen.Item { Label = () => GameText.T("Delete"), Run = () => Delete(d, fromList, viaMenu: true) });
            if (items.Count == 0) { Speech.Tts.Speak(Loc.T("pipeline.edit.fixed", new { what = PipelineText.Name(p, d) }), interrupt: true); return; }
            PushChild(new ActionListScreen("pipeline.menu", PipelineText.Name(p, d), items));
        }

        /// <summary>A reactor output's note item, labelled as the game's menu labels it (DraggableMenu):
        /// "Add Note to Upper Output" when the note is empty, else "Edit Upper Note (Visible)" /
        /// "(Hidden)". Opens the game's Output Note Editor over the pipeline.</summary>
        private void AddNoteItem(List<ActionListScreen.Item> items, ReactorDraggable rd, int output, string side)
        {
            if (!rd.class485_1.ContainsKey(output)) return;
            var note = rd.class485_1[output].method_0() as ReactorAnnotation;
            if (note == null) return;
            items.Add(new ActionListScreen.Item
            {
                Label = () => GameText.T(note.method_4() ? "Add Note to " + side + " Output"
                    : note.bool_1 ? "Edit " + side + " Note (Visible)" : "Edit " + side + " Note (Hidden)"),
                Run = () => Editor?.method_3(new InlineAnnotationEditor(note, bool_2: false)),
            });
        }

        /// <summary>Why the last drop was refused: the components it would overlap (Pipeline.hashSet_2,
        /// filled by method_12), terrain as "blocked"; nothing overlapped = off the map.</summary>
        private const int MaxBlockedCells = 3; // cells named per blocker; the rest are counted

        /// <summary>Why a drop of <paramref name="item"/> with its top-left on <paramref name="at"/>
        /// was refused: what is in the way and on which cells ("terrain at 7, 3; 8, 3") — the
        /// item's cells (body, then its pipes: Draggable's enumerator, the cells Pipeline.method_12
        /// checks) whose occupant the game recorded as a blocker (hashSet_2); off the map when
        /// nothing blocked.</summary>
        private static string Refusal(SpaceChem.Pipeline.Pipeline p, Draggable item, Vector2i at)
        {
            var order = new List<string>();
            var cells = new Dictionary<string, List<Vector2i>>();
            foreach (Vector2i c in item)
            {
                var cell = at + c;
                if (!p.dictionary_0.TryGetValue(cell, out var d) || d == null || !p.hashSet_2.Contains(d)) continue;
                string n = d is Class612 || string.IsNullOrEmpty(d.string_1?.Trim()) && !(d is ReactorDraggable)
                    ? Loc.T("pipeline.terrain") : PipelineText.Name(p, d);
                if (!cells.TryGetValue(n, out var list)) { cells[n] = list = new List<Vector2i>(); order.Add(n); }
                if (!list.Contains(cell)) list.Add(cell);
            }
            if (order.Count == 0) return Loc.T("pipeline.edit.offmap");
            var parts = new List<string>();
            foreach (var n in order)
            {
                var list = cells[n];
                var named = new List<string>();
                for (int i = 0; i < list.Count && i < MaxBlockedCells; i++) named.Add(PipelineText.Cell(list[i]));
                string where = string.Join("; ", named.ToArray());
                if (list.Count > MaxBlockedCells) where += " " + Loc.T("pipeline.edit.more", new { n = list.Count - MaxBlockedCells });
                parts.Add(Loc.T("pipeline.edit.blockedat", new { what = n, cells = where }));
            }
            return string.Join(", ", parts.ToArray());
        }
    }
}
