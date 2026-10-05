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
        // ---- the instruction picker (user design 2026-10-05): a cell's instructions edited in
        // place, without the context menu.
        //   Shift+Up / Down     the active colour's instructions in the cell (non-arrow, then arrow;
        //                       wraps), spoken without the colour
        //   Shift+Left / Right  switch the active colour (the L key), then the new colour's first
        //                       instruction as a separate, queued line
        //   Alt+Up / Down       the picked instruction's parameters — its context menu's radio
        //                       groups (Colour, Direction, ...); wraps; "Direction, up"
        //   Alt+Left / Right    that parameter's next / previous value (wraps): the menu item's own
        //                       click, so a colour change really moves the instruction.
        // The pick belongs to the cell: landing elsewhere drops it. Alt with nothing picked takes
        // the active colour's first instruction. ----

        private Instruction _pick;
        private int _param = -1;

        private IEnumerable<ElementAction> PickActions()
        {
            yield return new ElementAction("screen.reactor.pick.prev", () => StepPick(-1));
            yield return new ElementAction("screen.reactor.pick.next", () => StepPick(1));
            yield return new ElementAction("screen.reactor.pick.colour.prev", SwitchPickColour);
            yield return new ElementAction("screen.reactor.pick.colour.next", SwitchPickColour);
            yield return new ElementAction("screen.reactor.param.prev", () => StepParam(-1));
            yield return new ElementAction("screen.reactor.param.next", () => StepParam(1));
            yield return new ElementAction("screen.reactor.value.prev", () => StepValue(-1));
            yield return new ElementAction("screen.reactor.value.next", () => StepValue(1));
        }

        private void DropPick()
        {
            _pick = null;
            _param = -1;
        }

        /// <summary>The active colour's visible instructions in the cursor's cell, in reading order
        /// (the instruction, then the arrow).</summary>
        private List<Instruction> ActiveInstructionsHere(ReactorModel r)
        {
            var list = new List<Instruction>();
            foreach (int layer in ActiveLayers())
            {
                if (!Visible(r, layer)) continue;
                var i = InstructionAt(r, _cursorX, _cursorY, layer);
                if (i != null) list.Add(i);
            }
            return list;
        }

        /// <summary>The picked instruction if it still sits in the cursor's cell (any layer: a colour
        /// change keeps it picked), else null.</summary>
        private Instruction CurrentPick(ReactorModel r)
        {
            if (_pick == null) return null;
            Vector2i cell;
            try
            {
                var at = r.method_19(_pick);
                if (!at.HasValue) { DropPick(); return null; }
                cell = at.Value.vector2i_0;
            }
            catch { DropPick(); return null; }
            if (cell.int_0 != _cursorX || cell.int_1 != _cursorY) { DropPick(); return null; }
            return _pick;
        }

        private string NoInstructions()
            => Loc.T("reactor.pick.none", new { colour = Loc.T(RedActive ? "reactor.red" : "reactor.blue") });

        private void StepPick(int dir)
        {
            if (!OnGrid) return;
            var r = Model;
            if (r == null) return;
            var list = ActiveInstructionsHere(r);
            if (list.Count == 0) { DropPick(); Speech.Tts.Speak(NoInstructions(), interrupt: true); return; }
            int at = list.IndexOf(CurrentPick(r));
            int next = at < 0 ? (dir > 0 ? 0 : list.Count - 1) : ((at + dir) % list.Count + list.Count) % list.Count;
            _pick = list[next];
            _param = -1;
            Speech.Tts.Speak(ReactorText.Label(_pick), interrupt: true);
        }

        private void SwitchPickColour()
        {
            if (!OnGrid) return;
            var r = Model;
            if (r == null) return;
            ToggleActiveLayer(); // speaks the colour
            var list = ActiveInstructionsHere(r);
            _param = -1;
            _pick = list.Count > 0 ? list[0] : null;
            Speech.Tts.Speak(_pick != null ? ReactorText.Label(_pick) : Loc.T("reactor.pick.empty"));
        }

        /// <summary>The picked instruction, or the active colour's first one in the cell (which then
        /// becomes the pick). Speaks why when there is none.</summary>
        private Instruction PickOrFirst(ReactorModel r)
        {
            var pick = CurrentPick(r);
            if (pick != null) return pick;
            var list = ActiveInstructionsHere(r);
            if (list.Count == 0) { Speech.Tts.Speak(NoInstructions(), interrupt: true); return null; }
            _pick = list[0];
            _param = -1;
            return _pick;
        }

        /// <summary>One parameter of an instruction: a radio group of its context menu.</summary>
        private sealed class Param
        {
            public Func<string> Label;
            public readonly List<MenuItem<Instruction>> Items = new List<MenuItem<Instruction>>();
        }

        private static InstructionMenu MenuOf(Instruction member)
        {
            InstructionMenu menu;
            return Instruction.dictionary_1.TryGetValue(member.GetType(), out menu) ? menu : null;
        }

        /// <summary>The instruction's parameters in its menu's order, fitted to it (SyncMenu).</summary>
        private static List<Param> ParamsOf(Instruction member, out InstructionMenu menu)
        {
            var result = new List<Param>();
            menu = MenuOf(member);
            if (menu == null) return result;
            SyncMenu(menu, member);
            var byGroup = new Dictionary<object, Param>();
            foreach (var component in menu.linkedList_0)
            {
                var item = component as MenuItem<Instruction>;
                if (item == null || MenuItemLabel(item) == null) continue;
                object group = MenuItemGroup(item);
                if (group == null) continue;
                Param p;
                if (!byGroup.TryGetValue(group, out p))
                {
                    p = new Param { Label = MenuItemGroupLabel(item, member.GetType()) ?? (() => Loc.T("menu.group.default")) };
                    byGroup[group] = p;
                    result.Add(p);
                }
                p.Items.Add(item);
            }
            return result;
        }

        private static int SelectedIndex(Param p, Instruction member)
        {
            for (int i = 0; i < p.Items.Count; i++)
            {
                try { if (p.Items[i].isSelectedFunc_0(member)) return i; }
                catch { }
            }
            return -1;
        }

        /// <summary>A choice as a bare value: the colour, a direction word, a control letter, or the
        /// choice's own instruction label ("grab", "in beta").</summary>
        private static string ValueWord(MenuItem<Instruction> item)
        {
            if (item is Class720 layer) return Loc.T(layer.bool_3 ? "reactor.red" : "reactor.blue");
            object value = ClosureValue(item);
            if (value is Enum153 d) return ReactorText.Direction(d);
            if (value is Enum111 c) return ((char)('A' + Math.Max(0, Math.Min(3, (int)c)))).ToString();
            return MenuItemLabel(item)?.Invoke();
        }

        /// <summary>The value an icon choice's selected-test closes over (its closure's enum field).</summary>
        private static object ClosureValue(MenuItem<Instruction> item)
        {
            try
            {
                var target = item.isSelectedFunc_0?.Target;
                if (target == null) return null;
                foreach (var f in target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    if (f.FieldType.IsEnum) return f.GetValue(target);
            }
            catch { }
            return null;
        }

        private void StepParam(int dir)
        {
            if (!OnGrid) return;
            var r = Model;
            if (r == null) return;
            var member = PickOrFirst(r);
            if (member == null) return;
            var ps = ParamsOf(member, out _);
            if (ps.Count == 0) { Speech.Tts.Speak(Loc.T("reactor.pick.noparams"), interrupt: true); return; }
            _param = _param < 0 || _param >= ps.Count
                ? (dir > 0 ? 0 : ps.Count - 1)
                : ((_param + dir) % ps.Count + ps.Count) % ps.Count;
            var p = ps[_param];
            int sel = SelectedIndex(p, member);
            string label = p.Label();
            Speech.Tts.Speak(sel >= 0 ? label + ", " + ValueWord(p.Items[sel]) : label, interrupt: true);
        }

        private void StepValue(int dir)
        {
            if (!OnGrid) return;
            var r = Model;
            if (r == null) return;
            var member = PickOrFirst(r);
            if (member == null) return;
            var ps = ParamsOf(member, out var menu);
            if (ps.Count == 0) { Speech.Tts.Speak(Loc.T("reactor.pick.noparams"), interrupt: true); return; }
            if (!CanEdit()) return;
            if (_param < 0 || _param >= ps.Count) _param = 0;
            var p = ps[_param];
            int layer = (int)(r.method_19(member)?.enum114_0 ?? 0);
            if (!LayerEditable(r, layer))
            {
                Speech.Tts.Speak(Loc.T("reactor.edit.locked", new { colour = ColourWord(layer) }), interrupt: true);
                return;
            }
            int sel = SelectedIndex(p, member);
            int next = sel < 0 ? 0 : ((sel + dir) % p.Items.Count + p.Items.Count) % p.Items.Count;
            var item = p.Items[next];
            if (item is Class720 colour && !ColourChangeAllowed(r, member, layer, colour.bool_3)) return;
            RunMenuItem(r, member, item);
            SyncMenu(menu, member);
            bool took;
            try { took = item.isSelectedFunc_0(member); }
            catch { took = false; }
            Speech.Tts.Speak(took ? ValueWord(item) : Loc.T("value.unavailable"), interrupt: true);
        }

        /// <summary>The colour item moves the instruction to the same slot of the other colour, and
        /// silently does nothing when that slot is taken (InstructionMenu's Class723): say what is in
        /// the way, and refuse a locked or hidden target colour as an edit would.</summary>
        private static bool ColourChangeAllowed(ReactorModel r, Instruction member, int layer, bool toRed)
        {
            bool arrow = (layer & (ReactorText.RedArrow | ReactorText.BlueArrow)) != 0;
            int target = toRed ? (arrow ? ReactorText.RedArrow : ReactorText.Red) : (arrow ? ReactorText.BlueArrow : ReactorText.Blue);
            if (target == layer) return true;
            if (!LayerEditable(r, target))
            {
                Speech.Tts.Speak(Loc.T("reactor.edit.locked", new { colour = ColourWord(target) }), interrupt: true);
                return false;
            }
            var cell = r.method_19(member).Value.vector2i_0;
            var occupant = r.method_17(new ReactorBin(cell, (Enum114)target)) as Instruction;
            if (occupant == null) return true;
            Speech.Tts.Speak(Loc.T("reactor.pick.blocked", new { what = ColourWord(target) + " " + ReactorText.Label(occupant) }), interrupt: true);
            return false;
        }
    }
}
