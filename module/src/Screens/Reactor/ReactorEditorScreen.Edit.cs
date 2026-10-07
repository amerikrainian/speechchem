using System;
using System.Collections.Generic;
using System.Reflection;
using Impeller;
using SpaceChem;
using SpaceChem.Reactor;
using SpaceChem.UI;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;
using ReactorModel = SpaceChem.Reactor.Reactor;

namespace SpeechChem.Screens.Reactor
{
    public sealed partial class ReactorEditorScreen
    {
        // ---- editing (user-approved design, 2026-09-27). Every change goes through the game's own
        // model calls inside ONE undo step (SpaceChemUserWorker.method_49), so Ctrl+Z / Ctrl+Y and
        // the save file see exactly what a mouse edit leaves:
        //   Reactor.method_18(bin, member)  place (saves the member, method_71)
        //   Reactor.method_21(member) + worker.method_72(member)  remove
        //   member.vmethod_2(reactor)        clone (a palette template or a copied instruction)
        //   member.vmethod_1(bin)            a moved member's new position (the drag path)
        // Rules: edits only while the reactor is stopped (the game's own rule), never on a locked or
        // hidden layer, and START markers can be moved (cut + paste) but never deleted or copied.
        //
        // Keys: a palette letter places that instruction at the cursor in the active colour
        // (replacing what occupies that slot — user rule); Enter places the ARMED palette slot (one
        // shot); Delete removes the active colour's instructions in the cell or selection; Ctrl+X /
        // Ctrl+C / Ctrl+V cut, copy and paste them; Shift+Space marks a rectangle's corners, Ctrl+A
        // the whole reactor;
        // Backspace on a grid cell (the secondary action, the right-click key) opens the context
        // menu: the cell's instruction menu (the game's right-click menu, item by item) or, on an
        // empty cell, the Reactor Grid menu. ----

        private static readonly int[] PaletteLetters =
        {
            20, 26, 8, 21, 23, 28, 24, 12, // Q W E R T Y U I
            4, 22, 7, 9, 10, 11, 13, 14,   // A S D F G H J K
        };

        private IEnumerable<ElementAction> EditActions()
        {
            foreach (int key in PaletteLetters)
            {
                int k = key;
                yield return new ElementAction("screen.reactor.place." + k, () => PlaceSlot(k, _cursorX, _cursorY, fromLetter: true));
            }
            yield return new ElementAction("screen.reactor.delete", DeleteAtCursor);
            yield return new ElementAction("screen.reactor.cut", () => CopyAtCursor(cut: true));
            yield return new ElementAction("screen.reactor.copy", () => CopyAtCursor(cut: false));
            yield return new ElementAction("screen.reactor.paste", Paste);
            yield return new ElementAction("screen.reactor.mark", MarkCorner);
            yield return new ElementAction("screen.reactor.unmark", ClearMark);
            yield return new ElementAction("screen.reactor.markall", MarkAll);
            foreach (var a in PickActions()) yield return a;
        }

        private void ResetEditState()
        {
            _markFirst = _markSecond = null;
            _clip.Clear();
            LeavePickCell();
        }

        private static bool OnGrid => GridStop.Equals(Navigation.FocusedStopKey);

        private static IDisposable UndoStep() => Locals.smethod_0().smethod_0().method_49();

        private static void Forget(ReactorMember m) => Locals.smethod_0().smethod_0().method_72(m);

        /// <summary>Refuse edits the game itself would refuse, saying why. True = editing is allowed.</summary>
        private static bool CanEdit()
        {
            if (Live)
            {
                Speech.Tts.Speak(Loc.T("reactor.edit.running"), interrupt: true);
                return false;
            }
            return true;
        }

        private static bool LayerEditable(ReactorModel r, int layer)
        {
            int bits = (layer & (ReactorText.Red | ReactorText.RedArrow)) != 0 ? ReactorText.AllRed : ReactorText.AllBlue;
            bool locked = ((int)r.method_7() & bits) != 0;
            bool visible = ((int)r.method_5() & layer) != 0;
            return !locked && visible;
        }

        private static string ColourWord(int layer)
            => Loc.T((layer & (ReactorText.Red | ReactorText.RedArrow)) != 0 ? "reactor.red" : "reactor.blue");

        // ---- placement ----

        /// <summary>Enter on a cell: place the armed palette slot there (one shot).</summary>
        private void ActivateCell(int x, int y)
        {
            if (_armedKey < 0) return;
            int key = _armedKey;
            if (PlaceSlot(key, x, y, fromLetter: false)) _armedKey = -1;
        }

        /// <summary>Place a copy of a palette slot's instruction at a cell, in the slot's (active)
        /// colour, replacing whatever holds that slot of the cell.</summary>
        private bool PlaceSlot(int key, int x, int y, bool fromLetter)
        {
            if (fromLetter && !OnGrid) return false;
            var editor = Editor;
            var r = editor?.reactor_0;
            var slot = Slot(editor, key);
            if (r == null || slot == null) return false;
            if (!CanEdit()) return true;
            int layer = (int)slot.enum114_0;
            if (!LayerEditable(r, layer))
            {
                Speech.Tts.Speak(Loc.T("reactor.edit.locked", new { colour = ColourWord(layer) }), interrupt: true);
                return true;
            }
            var cell = new Vector2i(x, y);
            var existing = r.method_15(cell, (Enum114)layer) as Instruction;
            if (existing is StartInstruction)
            {
                Speech.Tts.Speak(Loc.T("reactor.edit.start"), interrupt: true);
                return true;
            }
            var clone = slot.struct116_0.method_0().vmethod_2(r) as Instruction;
            if (clone == null) return true;
            using (UndoStep())
            {
                if (existing != null)
                {
                    r.method_21(existing);
                    Forget(existing);
                }
                r.method_18(new ReactorBin(cell, (Enum114)layer), clone);
            }
            if (x == _cursorX && y == _cursorY) PickPlaced(clone, layer);
            Class428.class14_11.vmethod_0(); // the drop sound (Reactor.method_11 leaving the drag state)
            // Just the new instruction, replacement or not (user rule).
            string placed = ColourWord(layer) + " " + ReactorText.Label(clone);
            Speech.Tts.Speak(Loc.T("reactor.edit.placed", new { placed }), interrupt: true);
            return true;
        }

        // ---- the marked rectangle (Shift+Space; user design 2026-10-05). The first press marks one
        // corner, the second the opposite one; a third starts a new rectangle (there is only one).
        // It is independent of the cursor: cells inside read "Marked" first, and the edit keys act on
        // the whole rectangle while the cursor is inside it, else on the cursor's cell alone. Spoken
        // top-left to bottom-right whichever corners were marked. Ctrl+Space unmarks it (or the
        // corner waiting for its pair) — "Cleared", or nothing at all when nothing is marked. A cut,
        // copy or delete that took the rectangle, and any paste that lands something, clear it
        // silently (user rule 2026-10-06). Ctrl+A marks the whole reactor in one go. ----

        private Vector2i? _markFirst, _markSecond;

        private void MarkCorner()
        {
            if (!OnGrid) return;
            var here = new Vector2i(_cursorX, _cursorY);
            if (!_markFirst.HasValue || _markSecond.HasValue)
            {
                _markFirst = here;
                _markSecond = null;
                Speech.Tts.Speak(Loc.T("reactor.mark.first", new { cell = CellName(_cursorX, _cursorY) }), interrupt: true);
                return;
            }
            _markSecond = here;
            SpeakRectangle();
        }

        /// <summary>Ctrl+A: the rectangle becomes the whole reactor.</summary>
        private void MarkAll()
        {
            if (!OnGrid) return;
            var r = Model;
            if (r == null) return;
            var size = r.method_1();
            _markFirst = new Vector2i(0, 0);
            _markSecond = new Vector2i(size.int_0 - 1, size.int_1 - 1);
            SpeakRectangle();
        }

        private void SpeakRectangle()
        {
            MarkBounds(out int x0, out int y0, out int x1, out int y1);
            Speech.Tts.Speak(Loc.T("reactor.mark.rect", new
            {
                w = x1 - x0 + 1,
                h = y1 - y0 + 1,
                from = CellName(x0, y0),
                to = CellName(x1, y1),
            }), interrupt: true);
        }

        private void ClearMark()
        {
            if (!OnGrid || !_markFirst.HasValue) return;
            _markFirst = _markSecond = null;
            Speech.Tts.Speak(Loc.T("reactor.mark.cleared"), interrupt: true);
        }

        private void DropMark() => _markFirst = _markSecond = null;

        private bool CursorInRectangle => _markSecond.HasValue && IsMarked(_cursorX, _cursorY);

        private static string CellName(int x, int y) => Loc.T("reactor.cell", new { x = x + 1, y = y + 1 });

        /// <summary>The marked cells' bounds: the rectangle, or the first corner alone while the
        /// second is still to come. False when nothing is marked.</summary>
        private bool MarkBounds(out int x0, out int y0, out int x1, out int y1)
        {
            x0 = y0 = x1 = y1 = 0;
            if (!_markFirst.HasValue) return false;
            var a = _markFirst.Value;
            var b = _markSecond ?? a;
            x0 = Math.Min(a.int_0, b.int_0); x1 = Math.Max(a.int_0, b.int_0);
            y0 = Math.Min(a.int_1, b.int_1); y1 = Math.Max(a.int_1, b.int_1);
            return true;
        }

        private bool IsMarked(int x, int y)
            => MarkBounds(out int x0, out int y0, out int x1, out int y1) && x >= x0 && x <= x1 && y >= y0 && y <= y1;

        /// <summary>The cells an edit applies to: the whole marked rectangle while the cursor is in
        /// it, else the cursor's cell.</summary>
        private List<Vector2i> TargetCells()
        {
            var cells = new List<Vector2i>();
            if (CursorInRectangle)
            {
                MarkBounds(out int x0, out int y0, out int x1, out int y1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++) cells.Add(new Vector2i(x, y));
            }
            else cells.Add(new Vector2i(_cursorX, _cursorY));
            return cells;
        }

        /// <summary>The active colour's layers (non-arrow, arrow).</summary>
        private static int[] ActiveLayers()
            => RedActive ? new[] { ReactorText.Red, ReactorText.RedArrow } : new[] { ReactorText.Blue, ReactorText.BlueArrow };

        // ---- delete ----

        private void DeleteAtCursor()
        {
            if (!OnGrid) return;
            var r = Model;
            if (r == null || !CanEdit()) return;
            bool fromRectangle = CursorInRectangle;
            var victims = new List<Instruction>();
            bool sawStart = false;
            foreach (var cell in TargetCells())
                foreach (int layer in ActiveLayers())
                {
                    if (!LayerEditable(r, layer)) continue;
                    var i = r.method_15(cell, (Enum114)layer) as Instruction;
                    if (i == null) continue;
                    if (!i.bool_0) { sawStart = true; continue; } // START: not deletable
                    victims.Add(i);
                }
            if (victims.Count == 0)
            {
                Speech.Tts.Speak(Loc.T(sawStart ? "reactor.edit.start" : "reactor.edit.nothing"), interrupt: true);
                return;
            }
            var labels = new List<string>();
            using (UndoStep())
            {
                foreach (var i in victims)
                {
                    labels.Add(ReactorText.Label(i));
                    r.method_21(i);
                    Forget(i);
                }
            }
            if (fromRectangle) DropMark();
            Speech.Tts.Speak(Loc.T("reactor.edit.deleted", new { what = string.Join(", ", labels.ToArray()) }), interrupt: true);
        }

        // ---- clipboard ----

        private sealed class ClipEntry
        {
            public Vector2i Offset;  // from the copied region's top-left cell
            public int Layer;
            public Instruction Source; // for a copy: cloned on paste; for a cut START: moved on paste
            public bool MoveStart;
            public ReactorFeature Feature; // cut hardware: moved on paste (never copied or deleted)
        }

        /// <summary>The hardware a cut takes from a cell whose active colour gave nothing (none
        /// there, or that colour locked) — the game's own pick order: its mouse press walks a cell
        /// red, red arrow, blue, blue arrow, hardware LAST, skipping pieces on locked or hidden
        /// layers (Reactor.method_50 / ReactorMember.method_1), so locking the instruction layers is
        /// how a player reaches hardware under them. Null when there is none or it can't move.</summary>
        private static ReactorFeature HardwareAt(ReactorModel r, Vector2i cell)
        {
            var f = r.method_15(cell, (Enum114)ReactorText.Background) as ReactorFeature;
            return f != null && !f.method_1() ? f : null;
        }

        private readonly List<ClipEntry> _clip = new List<ClipEntry>();

        private void CopyAtCursor(bool cut)
        {
            if (!OnGrid) return;
            var r = Model;
            if (r == null) return;
            if (cut && !CanEdit()) return;
            bool fromRectangle = CursorInRectangle;
            var cells = TargetCells();
            int ox = int.MaxValue, oy = int.MaxValue;
            foreach (var c in cells) { ox = Math.Min(ox, c.int_0); oy = Math.Min(oy, c.int_1); }
            var entries = new List<ClipEntry>();
            var toRemove = new List<Instruction>();
            var hardware = new HashSet<ReactorFeature>();
            foreach (var cell in cells)
            {
                bool took = false;
                foreach (int layer in ActiveLayers())
                {
                    if (!Visible(r, layer)) continue;
                    var i = r.method_15(cell, (Enum114)layer) as Instruction;
                    if (i == null) continue;
                    var offset = new Vector2i(cell.int_0 - ox, cell.int_1 - oy);
                    if (i is StartInstruction)
                    {
                        // START can only move: a cut remembers it and the paste relocates it.
                        if (cut && LayerEditable(r, layer)) { entries.Add(new ClipEntry { Offset = offset, Layer = layer, Source = i, MoveStart = true }); took = true; }
                        continue;
                    }
                    if (cut && !LayerEditable(r, layer)) continue;
                    // Keep a detached copy, so later edits to the original don't change the clipboard.
                    var copy = i.vmethod_2(r) as Instruction;
                    entries.Add(new ClipEntry { Offset = offset, Layer = layer, Source = copy });
                    took = true;
                    if (cut) toRemove.Add(i);
                }
                // Nothing of the active colour taken here: a cut reaches the hardware (user rule, the
                // game's pick order). Like START it only moves — it stays put until the paste — and a
                // copy never takes it (the game has no way to duplicate hardware). Offsets come from
                // the piece's own origin, so a piece covering several cells moves whole.
                if (cut && !took)
                {
                    var f = HardwareAt(r, cell);
                    if (f != null && hardware.Add(f))
                    {
                        var origin = r.method_19(f).Value.vector2i_0;
                        entries.Add(new ClipEntry { Offset = new Vector2i(origin.int_0 - ox, origin.int_1 - oy), Layer = ReactorText.Background, Feature = f });
                    }
                }
            }
            if (entries.Count == 0)
            {
                Speech.Tts.Speak(Loc.T("reactor.edit.nothing"), interrupt: true);
                return;
            }
            _clip.Clear();
            _clip.AddRange(entries);
            if (fromRectangle) DropMark();
            if (toRemove.Count > 0)
            {
                using (UndoStep())
                {
                    foreach (var i in toRemove)
                    {
                        r.method_21(i);
                        Forget(i);
                    }
                }
            }
            var names = new List<string>();
            foreach (var e in entries) names.Add(ClipLabel(e));
            Speech.Tts.Speak(Loc.T(cut ? "reactor.edit.cut" : "reactor.edit.copied", new { what = Summary(names) }), interrupt: true);
        }

        private const int MaxNamed = 3;

        /// <summary>What a clipboard entry is: "red grab drop", "Bonder".</summary>
        private static string ClipLabel(ClipEntry e)
            => e.Feature != null ? ReactorText.FeatureLabel(e.Feature) : ColourWord(e.Layer) + " " + ReactorText.Label(e.Source);

        /// <summary>Up to three phrases named, joined; more = "5 items".</summary>
        private static string Summary(List<string> phrases, string separator = ", ")
            => phrases.Count <= MaxNamed ? string.Join(separator, phrases.ToArray()) : Loc.T("reactor.edit.items", new { n = phrases.Count });

        private void Paste()
        {
            if (!OnGrid) return;
            var r = Model;
            if (r == null || _clip.Count == 0) { Speech.Tts.Speak(Loc.T("reactor.edit.clipempty"), interrupt: true); return; }
            if (!CanEdit()) return;
            var size = r.method_1();
            int placed = 0, skipped = 0;
            var landed = new List<string>(); // "red grab drop at 3, 2", one per placed item
            var refused = new List<string>(); // what didn't fit, by name
            var moved = new HashSet<ClipEntry>();
            Action<ClipEntry> skip = e => { skipped++; refused.Add(ClipLabel(e)); };
            Action<ClipEntry, Vector2i> land = (e, at) =>
            {
                placed++;
                if (e.MoveStart || e.Feature != null) moved.Add(e);
                landed.Add(Loc.T("reactor.edit.at", new { what = ClipLabel(e), cell = Loc.T("reactor.cell", new { x = at.int_0 + 1, y = at.int_1 + 1 }) }));
            };
            using (UndoStep())
            {
                foreach (var e in _clip)
                {
                    var cell = new Vector2i(_cursorX + e.Offset.int_0, _cursorY + e.Offset.int_1);
                    if (e.Feature != null)
                    {
                        if (MoveHardware(r, e.Feature, cell)) land(e, cell);
                        else skip(e);
                        continue;
                    }
                    if (cell.int_0 >= size.int_0 || cell.int_1 >= size.int_1 || !LayerEditable(r, e.Layer)) { skip(e); continue; }
                    var bin = new ReactorBin(cell, (Enum114)e.Layer);
                    var existing = r.method_17(bin);
                    if (e.MoveStart)
                    {
                        if (existing != null && existing != e.Source)
                        {
                            if (existing is StartInstruction || !(existing is Instruction)) { skip(e); continue; }
                            r.method_21(existing);
                            Forget(existing);
                        }
                        r.method_18(bin, e.Source);
                        e.Source.vmethod_1(bin);
                        land(e, cell);
                        continue;
                    }
                    if (existing is StartInstruction) { skip(e); continue; }
                    if (existing != null)
                    {
                        r.method_21(existing);
                        Forget(existing);
                    }
                    var clone = e.Source.vmethod_2(r) as Instruction;
                    if (clone == null) { skip(e); continue; }
                    r.method_18(bin, clone);
                    land(e, cell);
                }
            }
            // A START or a piece of hardware moves once; later pastes of the same clipboard should
            // not move it again. One the paste refused stays on the clipboard for another try.
            _clip.RemoveAll(moved.Contains);
            if (placed > 0) DropMark();
            Class428.class14_11.vmethod_0();
            // What landed where ("Bonder at 9, 2"; past three, "5 items at 3, 2" from the cursor),
            // then what didn't fit.
            string what = placed == 0 ? null
                : placed <= MaxNamed ? string.Join("; ", landed.ToArray())
                : Loc.T("reactor.edit.at", new { what = Loc.T("reactor.edit.items", new { n = placed }), cell = Loc.T("reactor.cell", new { x = _cursorX + 1, y = _cursorY + 1 }) });
            string notFit = skipped <= MaxNamed ? string.Join(", ", refused.ToArray()) : Loc.T("reactor.edit.items", new { n = skipped });
            string text = what == null ? Loc.T("reactor.edit.nofit", new { n = notFit })
                : skipped > 0 ? Loc.T("reactor.edit.pasted.skipped", new { what, skipped = notFit })
                : what;
            Speech.Tts.Speak(text, interrupt: true);
        }

        /// <summary>Move a piece of hardware so its origin sits at <paramref name="origin"/> — the
        /// game's drop (Reactor.method_30: method_18 then the member's vmethod_1). Refused when a
        /// cell it would cover lies outside the grid or holds other hardware, or the piece's layer
        /// is locked or hidden. The instruction layers are separate, so instructions don't block it.</summary>
        private static bool MoveHardware(ReactorModel r, ReactorFeature f, Vector2i origin)
        {
            if (f.method_1()) return false;
            var size = r.method_1();
            foreach (Vector2i offset in f)
            {
                int x = origin.int_0 + offset.int_0, y = origin.int_1 + offset.int_1;
                if (x < 0 || y < 0 || x >= size.int_0 || y >= size.int_1) return false;
                var there = r.method_17(new ReactorBin(new Vector2i(x, y), (Enum114)ReactorText.Background));
                if (there != null && there != f) return false;
            }
            var bin = new ReactorBin(origin, (Enum114)ReactorText.Background);
            r.method_18(bin, f);
            f.vmethod_1(bin);
            return true;
        }

        // ---- context menu (Backspace on the grid) ----

        private void OpenContextMenu()
        {
            if (!OnGrid) return;
            var r = Model;
            if (r == null || !CanEdit()) return;
            var cell = new Vector2i(_cursorX, _cursorY);
            var members = new List<Instruction>();
            foreach (int layer in new[] { ReactorText.Red, ReactorText.RedArrow, ReactorText.Blue, ReactorText.BlueArrow })
            {
                if (!LayerEditable(r, layer)) continue;
                var i = r.method_15(cell, (Enum114)layer) as Instruction;
                if (i != null && Instruction.dictionary_1.ContainsKey(i.GetType())) members.Add(i);
            }
            if (members.Count == 0) { PushChild(GridMenu(r)); return; }
            if (members.Count == 1) { PushChild(InstructionMenuFor(r, members[0])); return; }
            var chooser = new List<ActionListScreen.Item>();
            foreach (var m in members)
            {
                var member = m;
                chooser.Add(new ActionListScreen.Item
                {
                    Label = () => ColourWord((int)(r.method_19(member)?.enum114_0 ?? 0)) + " " + ReactorText.Label(member),
                    Run = () => PushChild(InstructionMenuFor(r, member)),
                });
            }
            PushChild(new ActionListScreen("reactor.menu.members", null, chooser));
        }

        /// <summary>The game's right-click menu for one instruction, item by item: "Red Layer" /
        /// "Blue Layer" (Class720), the icon variants (InstructionMenuItem — labelled from their
        /// template instruction), and text items ("Delete", "Change Trigger Element" — Class719).
        /// Running an item replays the game's click: select just this instruction (what the
        /// right-click does), then the item's own action.</summary>
        private ActionListScreen InstructionMenuFor(ReactorModel r, Instruction member)
        {
            InstructionMenu menu;
            if (!Instruction.dictionary_1.TryGetValue(member.GetType(), out menu))
                menu = Instruction.dictionary_1[typeof(Instruction)];
            SyncMenu(menu, member);
            var items = new List<ActionListScreen.Item>();
            foreach (var component in menu.linkedList_0)
            {
                var item = component as MenuItem<Instruction>;
                if (item == null) continue;
                Func<string> label = MenuItemLabel(item);
                if (label == null) continue;
                var it = item;
                items.Add(new ActionListScreen.Item
                {
                    Label = label,
                    Selected = () => { try { return it.isSelectedFunc_0(member); } catch { return false; } },
                    Run = () => { RunMenuItem(r, member, it); SyncMenu(menu, member); },
                    Group = MenuItemGroup(item),
                    GroupLabel = MenuItemGroupLabel(item, member.GetType()),
                });
            }
            string title = ColourWord((int)(r.method_19(member)?.enum114_0 ?? 0)) + " " + ReactorText.Label(member);
            return new ActionListScreen("reactor.menu.instr", title, items);
        }

        /// <summary>What the game's Menu.vmethod_4 does to each item as the menu opens
        /// (MenuItem.vmethod_4 → its update delegate): fit the item's icon to the instruction —
        /// its colour, and on a control instruction the letters take its direction and the
        /// directions its letter. Labels read the icons, so without this a control menu said "up" /
        /// "A" whatever the instruction was. Re-run after each radio choice (the menu stays open).
        /// The menu's own opener can't be used: it pushes the game's menu screen and clicks.</summary>
        internal static void SyncMenu(InstructionMenu menu, Instruction member)
        {
            // The items read the instruction's colour from its reactor cell (ReactorMember.method_0
            // → reactor.method_19(this).Value): after the menu's Delete it has none, and every item
            // threw "Nullable object must have a value" (the re-sync after a run). Nothing to fit.
            try { if (member?.reactor_0?.method_19(member) == null) return; }
            catch { return; }
            foreach (var component in menu.linkedList_0)
            {
                if (!(component is MenuItem<Instruction> item)) continue;
                try { item.vmethod_4(member); }
                catch (Exception ex) { Log.Error("[reactor] menu item sync failed", ex); }
            }
        }

        internal static Func<string> MenuItemLabel(MenuItem<Instruction> item)
        {
            if (item is Class720 layer) return () => GameText.T(layer.bool_3 ? "Red Layer" : "Blue Layer");
            if (item is Class719<Instruction> text) return () => text.string_0;
            if (item is InstructionMenuItem icon && icon.struct116_0.bool_0) return () => ReactorText.Label(icon.struct116_0.method_0());
            return null;
        }

        /// <summary>Which mutually exclusive family a menu item belongs to, or null for a plain
        /// action. Each family's items share one selected-test method, closed over their own value
        /// (Class722.method_0 for the layers, a per-type closure for the icons — a control
        /// instruction has two families: its letters and its directions). Text items (Delete,
        /// Change Trigger Element) are actions, whatever default test they carry.</summary>
        private static object MenuItemGroup(MenuItem<Instruction> item)
        {
            if (!(item is Class720) && !(item is InstructionMenuItem)) return null;
            try { return item.isSelectedFunc_0?.Method; }
            catch { return null; }
        }

        /// <summary>The name of a menu item's family — the instruction parameter its choices set,
        /// spoken as the family's row name. The layers are the colour; an icon family is told by the
        /// value its selected-test closes over (the closure's one enum field): Enum153 direction,
        /// Enum111 control letter, Enum120 input / output zone, Enum146 add / remove bond, Enum85
        /// grab / drop, Enum141 rotation. Mod words: the game draws these as unlabelled icons.</summary>
        internal static Func<string> MenuItemGroupLabel(MenuItem<Instruction> item, Type instruction)
        {
            if (item is Class720) return () => Loc.T("reactor.menu.group.colour");
            Type value = null;
            try
            {
                var target = item.isSelectedFunc_0?.Target;
                if (target != null)
                    foreach (var f in target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        if (f.FieldType.IsEnum) { value = f.FieldType; break; }
            }
            catch { }
            string key =
                value == typeof(Enum153) ? "direction" :
                value == typeof(Enum111) ? "control" :
                value == typeof(Enum120) ? (typeof(OutputInstruction).IsAssignableFrom(instruction) ? "output" : "input") :
                value == typeof(Enum146) ? "bond" :
                value == typeof(Enum85) ? "grab" :
                value == typeof(Enum141) ? "rotation" :
                null;
            if (key == null) return null;
            return () => Loc.T("reactor.menu.group." + key);
        }

        private void RunMenuItem(ReactorModel r, Instruction member, MenuItem<Instruction> item)
        {
            if (Live) return;
            try
            {
                r.method_27();
                r.method_24(member);
                Class428.class14_4.vmethod_0();
                item.clickAction_0(member);
            }
            catch (Exception ex) { Log.Error("[reactor] menu item failed", ex); }
            finally { try { r.method_27(); } catch { } }
            // No readout here: a radio item says "selected" itself, and focus returning from the
            // menu re-reads the cell, changed.
        }

        /// <summary>Right-click on empty grid: the "Reactor Grid" menu (ReactorMenu — Swap Waldo
        /// Colors, Mirror Vertically, Reset), each item's own action on this reactor.</summary>
        private ActionListScreen GridMenu(ReactorModel r)
        {
            var items = new List<ActionListScreen.Item>();
            foreach (var component in r.reactorMenu_0.linkedList_0)
            {
                if (!(component is Class719<ReactorModel> item)) continue;
                var it = item;
                items.Add(new ActionListScreen.Item
                {
                    Label = () => it.string_0,
                    Run = () =>
                    {
                        if (Live) return;
                        try
                        {
                            Class428.class14_4.vmethod_0();
                            it.clickAction_0(r);
                        }
                        catch (Exception ex) { Log.Error("[reactor] grid menu item failed", ex); }
                    },
                });
            }
            return new ActionListScreen("reactor.menu.grid", GameText.T("Reactor Grid"), items);
        }
    }
}
