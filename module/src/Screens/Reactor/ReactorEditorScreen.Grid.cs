using System;
using System.Collections.Generic;
using Impeller;
using SpaceChem;
using SpaceChem.Reactor;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;
using SpeechChem.UI.Graph;
using ReactorModel = SpaceChem.Reactor.Reactor;

namespace SpeechChem.Screens.Reactor
{
    public sealed partial class ReactorEditorScreen
    {
        // ---- the grid stop: one node per cell, rows sharing a key so Up/Down keep the column.
        // A cell reads "x, y" FIRST (1-based column, row from the top — user rule), then the zone
        // name when the cursor has just crossed into a different zone, then its contents: visible
        // instructions per colour (non-arrow, then arrow), hardware, waldos and atoms while a run is
        // live, and "highlighted" on the tutorial's target cell. An empty cell is just its
        // coordinates (user rule). ----

        private int _cursorX, _cursorY;
        private string _lastZone;
        private Vector2i? _zoneFor; // the cell whose readout carries the zone name just crossed into
        private bool _zoneInit;     // the first readout names its zone (no crossing precedes it)

        private static ControlId CellId(int x, int y) => ControlId.Structural("reactor.cell." + x + "." + y);

        private void BuildGrid(GraphBuilder b, ReactorModel reactor)
        {
            var size = reactor.method_1();
            if (_cursorX >= size.int_0) _cursorX = size.int_0 - 1;
            if (_cursorY >= size.int_1) _cursorY = size.int_1 - 1;
            b.BeginStop(GridStop);
            for (int y = 0; y < size.int_1; y++)
            {
                b.StartRow("reactor.grid.row");
                for (int x = 0; x < size.int_0; x++)
                {
                    int cx = x, cy = y;
                    b.AddItem(CellId(x, y), new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[] { new NodeAnnouncement(() => CellReadout(cx, cy), kind: AnnouncementKinds.Label) },
                        SpeaksOwnPosition = true,
                        OnSelect = () => { ClearSelection(); LandOnCell(cx, cy); },
                        OnActivate = () => ActivateCell(cx, cy),
                        OnSecondary = OpenContextMenu,
                        OnTooltip = () => Speech.Tts.Speak(CellDetails(cx, cy), interrupt: true),
                        OnJumpEdge = first => JumpRowEdge(cy, first),
                    });
                }
                b.EndRow();
            }
            b.SetStart(CellId(_cursorX, _cursorY));
        }

        /// <summary>Directional landing on a cell: note the cursor and whether a zone was crossed.</summary>
        private void LandOnCell(int x, int y)
        {
            _cursorX = x;
            _cursorY = y;
            string zone = ZoneAt(Model, x, y);
            if (zone != null && zone != _lastZone) _zoneFor = new Vector2i(x, y);
            else _zoneFor = null;
            _lastZone = zone;
        }

        /// <summary>Keep the cursor in step with focus that arrived another way (Tab landing, a
        /// jump), so C and the edit keys act on the focused cell.</summary>
        private void TrackCursor()
        {
            string key = Navigation.FocusedNodeId?.StructuralKey as string;
            if (key == null || !key.StartsWith("reactor.cell.", StringComparison.Ordinal)) return;
            var parts = key.Substring("reactor.cell.".Length).Split('.');
            int x, y;
            if (parts.Length == 2 && int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y))
            {
                _cursorX = x;
                _cursorY = y;
            }
        }

        private bool JumpRowEdge(int y, bool first)
        {
            var r = Model;
            if (r == null) return false;
            int x = first ? 0 : r.method_1().int_0 - 1;
            LandOnCell(x, y);
            Navigation.FocusNode(CellId(x, y));
            return true;
        }

        /// <summary>Move the grid cursor (and focus) to a cell — the waldo jump keys.</summary>
        private void FocusCell(int x, int y, bool announce = true)
        {
            LandOnCell(x, y);
            Navigation.FocusStop(GridStop);
            Navigation.FocusNode(CellId(x, y), announce);
        }

        // ---- zones ----

        /// <summary>The named zone a cell lies in ("alpha input", "psi output"), or null. Inputs
        /// follow the reactor's zone layout as Reactor.method_43 draws it (GEnum0: 1 = disassembly
        /// has only α; 4 = the laser reactor puts β TOP-RIGHT, columns 6-9 rows 0-3 — where
        /// InputInstruction.vmethod_7 drops it, six columns across — and has no outputs); outputs are
        /// the editor's own output rectangles (Class77.rectangle_1: [0] ψ, [1] ω; the variants shrink
        /// or drop them). The laser β is from the decompile, not yet verified live.</summary>
        internal static string ZoneAt(ReactorModel reactor, int x, int y)
        {
            bool input;
            int index;
            if (!ZoneOf(reactor, x, y, out input, out index)) return null;
            return Loc.T(input ? (index == 0 ? "zone.alpha" : "zone.beta") : (index == 0 ? "zone.psi" : "zone.omega"));
        }

        /// <summary>Which zone a cell lies in: an input (0 = α, 1 = β) or an output (0 = ψ, 1 = ω) —
        /// the same indices as the side panels' ports. False outside every zone.</summary>
        internal static bool ZoneOf(ReactorModel reactor, int x, int y, out bool input, out int index)
        {
            input = true;
            index = 0;
            if (reactor == null) return false;
            int layout = (int)reactor.genum0_0;
            if (x < 4 && y < 4) return true;
            index = 1;
            if (layout == 4) return x >= 6 && x < 10 && y < 4;
            if (x < 4 && y >= 4 && y < 8 && layout != 1) return true;
            input = false;
            var outputs = reactor.class77_0?.rectangle_1;
            if (outputs != null)
            {
                for (int i = 0; i < outputs.Length; i++)
                {
                    var r = outputs[i];
                    if (r.vector2i_1.int_0 <= 0 || r.vector2i_1.int_1 <= 0) continue;
                    if (x >= r.vector2i_0.int_0 && x < r.vector2i_0.int_0 + r.vector2i_1.int_0
                        && y >= r.vector2i_0.int_1 && y < r.vector2i_0.int_1 + r.vector2i_1.int_1)
                    {
                        index = i;
                        return true;
                    }
                }
            }
            return false;
        }

        // ---- cell content ----

        private static bool Visible(ReactorModel r, int layerBit) => ((int)r.method_5() & layerBit) != 0;

        internal static Instruction InstructionAt(ReactorModel r, int x, int y, int layer)
            => r.method_15(new Vector2i(x, y), (Enum114)layer) as Instruction;

        private static bool Live => (int)Class258.smethod_16() != 0;

        private string CellReadout(int x, int y)
        {
            var r = Model;
            if (r == null) return null;
            var parts = new List<string> { Loc.T("reactor.cell", new { x = x + 1, y = y + 1 }) };
            if (!_zoneInit)
            {
                _zoneInit = true;
                _lastZone = ZoneAt(r, x, y);
                if (_lastZone != null) parts.Add(_lastZone);
            }
            else if (_zoneFor.HasValue && _zoneFor.Value.int_0 == x && _zoneFor.Value.int_1 == y)
                parts.Add(ZoneAt(r, x, y));
            parts.AddRange(CellContents(r, x, y));
            if (IsTutorialTarget(x, y)) parts.Add(Loc.T("reactor.highlighted"));
            if (InSelection(x, y)) parts.Add(Loc.T("text.selectedword"));
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>What a cell holds, as spoken phrases (no coordinates).</summary>
        internal static List<string> CellContents(ReactorModel r, int x, int y)
        {
            var parts = new List<string>();
            AddColour(parts, r, x, y, ReactorText.Red, ReactorText.RedArrow, "reactor.red");
            AddColour(parts, r, x, y, ReactorText.Blue, ReactorText.BlueArrow, "reactor.blue");
            if (r.method_15(new Vector2i(x, y), (Enum114)ReactorText.Background) is ReactorFeature f)
                parts.Add(ReactorText.FeatureLabel(f));
            if (Live)
            {
                foreach (var w in r.dictionary_2)
                {
                    var p = w.Value.method_0();
                    if (p.int_0 == x && p.int_1 == y)
                        parts.Add(Loc.T((int)w.Key == ReactorText.Red ? "reactor.waldo.red" : "reactor.waldo.blue"));
                }
                string atom = AtomAt(r, x, y);
                if (atom != null) parts.Add(atom);
            }
            return parts;
        }

        private static void AddColour(List<string> parts, ReactorModel r, int x, int y, int layer, int arrowLayer, string colourKey)
        {
            var labels = new List<string>();
            if (Visible(r, layer))
            {
                var i = InstructionAt(r, x, y, layer);
                if (i != null) labels.Add(ReactorText.Label(i));
            }
            if (Visible(r, arrowLayer))
            {
                var a = InstructionAt(r, x, y, arrowLayer);
                if (a != null) labels.Add(ReactorText.Label(a));
            }
            if (labels.Count > 0)
                parts.Add(Loc.T(colourKey) + " " + string.Join(", ", labels.ToArray()));
        }

        /// <summary>The atom in a cell during a run, with its bonds ("Oxygen, double bond right").</summary>
        private static string AtomAt(ReactorModel r, int x, int y)
        {
            foreach (MoleculeSheet sheet in r.class201_0)
            {
                foreach (var kv in sheet.method_14())
                {
                    if (kv.Key.int_0 != x || kv.Key.int_1 != y) continue;
                    var parts = new List<string> { kv.Value.method_0() };
                    foreach (var bond in sheet.method_15())
                    {
                        var from = bond.Key.vector2i_0;
                        var to = bond.Key.method_0();
                        string dir = null;
                        bool right = bond.Key.enum128_0 == Enum128.Right;
                        if (from.int_0 == x && from.int_1 == y) dir = Loc.T(right ? "dir.right" : "dir.down");
                        else if (to.int_0 == x && to.int_1 == y) dir = Loc.T(right ? "dir.left" : "dir.up");
                        if (dir != null)
                            parts.Add(Loc.T("reactor.bond", new { kind = ReactorText.BondWord((int)bond.Value), dir }));
                    }
                    return string.Join(", ", parts.ToArray());
                }
            }
            return null;
        }

        /// <summary>Shift+Backspace on a cell: the game's hover text for what is there — each
        /// instruction's palette tooltip (the only description the game has for it), each piece of
        /// hardware's tooltip, and the atom info box for an atom during a run.</summary>
        private string CellDetails(int x, int y)
        {
            var r = Model;
            if (r == null) return null;
            var parts = new List<string>();
            foreach (int layer in new[] { ReactorText.Red, ReactorText.RedArrow, ReactorText.Blue, ReactorText.BlueArrow })
            {
                if (!Visible(r, layer)) continue;
                var i = InstructionAt(r, x, y, layer);
                if (i != null) parts.Add(InstructionDetails(i));
            }
            if (r.method_15(new Vector2i(x, y), (Enum114)ReactorText.Background) is ReactorFeature f)
                parts.Add(Patches.TooltipCapture.Speech(f.class713_0) ?? ReactorText.FeatureLabel(f));
            if (Live)
            {
                foreach (MoleculeSheet sheet in r.class201_0)
                    foreach (var kv in sheet.method_14())
                        if (kv.Key.int_0 == x && kv.Key.int_1 == y) parts.Add(ReactorText.AtomDetails(kv.Value));
            }
            if (parts.Count == 0) return Loc.T("nav.no_tooltip");
            return string.Join(". ", parts.ToArray());
        }

        /// <summary>An instruction's description: the palette tooltip of the matching slot (same
        /// type; arrows also match direction), else the game's name for its type.</summary>
        internal static string InstructionDetails(Instruction i)
        {
            string tip = Patches.TooltipCapture.Speech(Slot(Editor, SlotKeyFor(i))?.class713_0);
            return tip ?? ReactorText.GameName(i.GetType()) ?? ReactorText.Label(i);
        }

        /// <summary>The palette key (scancode) of the slot an instruction comes from: same type;
        /// arrows also match direction (one slot each). -1 when no enabled slot offers it.</summary>
        internal static int SlotKeyFor(Instruction i)
        {
            var palette = Editor?.class715_0;
            if (palette == null || i == null) return -1;
            foreach (var kv in palette.dictionary_0)
            {
                if (!kv.Value.struct116_0.bool_0) continue;
                var t = kv.Value.struct116_0.method_0();
                if (t.GetType() != i.GetType()) continue;
                if (i is ArrowInstruction && t.vmethod_5() != i.vmethod_5()) continue;
                return (int)kv.Key;
            }
            return -1;
        }

        private void SpeakCoordinates()
            => Speech.Tts.Speak(Loc.T("reactor.cell", new { x = _cursorX + 1, y = _cursorY + 1 }), interrupt: true);
    }
}
