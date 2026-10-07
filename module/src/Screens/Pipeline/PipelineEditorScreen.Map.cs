using System;
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
        // ---- the map: the pipeline's cells (Pipeline.method_4, 32 x 22), one node per cell, rows
        // sharing a key so Up/Down keep the column. A cell reads "x, y" FIRST (1-based, like the
        // reactor grid), then what occupies it (Pipeline.dictionary_0 maps every cell to its
        // component; pipe cells belong to the pipe's owner): a component's name and the port on
        // that cell, a pipe ("pipe, Assembly Reactor 2 psi output", "end", "crossing"), or
        // "blocked" (terrain, decoration). An empty cell is its coordinates alone. ----

        private const string MapStop = "pipeline.map";
        private int _cursorX, _cursorY;

        private static ControlId MapCellId(int x, int y) => ControlId.Structural("pipeline.cell." + x + "." + y);

        private void BuildMap(GraphBuilder b, SpaceChem.Pipeline.Pipeline pipeline)
        {
            var size = pipeline.method_4();
            b.BeginStop(MapStop);
            for (int y = 0; y < size.int_1; y++)
            {
                b.StartRow("pipeline.map.row");
                for (int x = 0; x < size.int_0; x++)
                {
                    int cx = x, cy = y;
                    b.AddItem(MapCellId(x, y), new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[] { new NodeAnnouncement(() => MapReadout(cx, cy), kind: AnnouncementKinds.Label) },
                        SpeaksOwnPosition = true,
                        OnSelect = () => { if (_drawPipe != null) DrawStep(cx, cy); _cursorX = cx; _cursorY = cy; },
                        OnActivate = () => ActivateMapCell(cx, cy),
                        OnSecondary = OpenMenu,
                        OnTooltip = () => SpeakTooltip(BuildingAt(Model, new Vector2i(cx, cy))),
                        OnJumpEdge = first => JumpMapEdge(cy, first),
                        OnRegionJump = dir => SkipMapCells(cx, cy, 0, dir),
                    });
                }
                b.EndRow();
            }
            b.SetStart(MapCellId(_cursorX, _cursorY));
        }

        private bool JumpMapEdge(int y, bool first)
        {
            var p = Model;
            if (p == null) return false;
            if (_drawPipe != null) return true; // no jumps while drawing: the cursor is the pipe's end
            FocusMapCell(first ? 0 : p.method_4().int_0 - 1, y);
            return true;
        }

        /// <summary>Ctrl+arrows: past the cells that hold the same as this one (UI/GridSkip).</summary>
        private bool SkipMapCells(int x, int y, int dx, int dy)
        {
            var p = Model;
            if (p == null) return false;
            if (_drawPipe != null) return true; // no jumps while drawing: the cursor is the pipe's end
            var size = p.method_4();
            GridSkip.Target(x, y, dx, dy, size.int_0, size.int_1,
                (cx, cy) => string.Join(", ", CellContents(p, new Vector2i(cx, cy)).ToArray()), out int tx, out int ty);
            if (tx == x && ty == y) Speech.Tts.Speak(MapReadout(x, y), interrupt: true);
            else FocusMapCell(tx, ty);
            return true;
        }

        /// <summary>Ctrl+Left / Ctrl+Right (Up / Down come through the cells' region jump).</summary>
        private void SkipMapSideways(int dx)
        {
            if (!Equals(Navigation.FocusedStopKey, MapStop)) return;
            SkipMapCells(_cursorX, _cursorY, dx, 0);
        }

        /// <summary>Move the map cursor (and focus) to a cell.</summary>
        private void FocusMapCell(int x, int y, bool announce = true)
        {
            _cursorX = x;
            _cursorY = y;
            Navigation.FocusStop(MapStop);
            Navigation.FocusNode(MapCellId(x, y), announce);
        }

        /// <summary>Keep the cursor in step with focus that reached the map another way.</summary>
        private void TrackMapCursor()
        {
            string key = Navigation.FocusedNodeId?.StructuralKey as string;
            if (key == null || !key.StartsWith("pipeline.cell.", StringComparison.Ordinal)) return;
            var parts = key.Substring("pipeline.cell.".Length).Split('.');
            int x, y;
            if (parts.Length == 2 && int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y))
            {
                _cursorX = x;
                _cursorY = y;
            }
        }

        private string MapReadout(int x, int y)
        {
            var p = Model;
            if (p == null) return null;
            if (_drawPipe != null && _drawStepCell.HasValue && _drawStepCell.Value.int_0 == x && _drawStepCell.Value.int_1 == y)
                return _drawStep;
            var parts = new List<string> { PipelineText.Cell(new Vector2i(x, y)) };
            parts.AddRange(CellContents(p, new Vector2i(x, y)));
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>What occupies a map cell, as spoken phrases (no coordinates).</summary>
        internal static List<string> CellContents(SpaceChem.Pipeline.Pipeline p, Vector2i cell)
        {
            var parts = CellContentsCore(p, cell);
            // A defense level's enemy is drawn over the map (Game/DefenseText): name it on the cells
            // its drawing covers, after whatever lies beneath, with its visible state when it has one
            // ("shield down"; nothing while normal — user request 2026-10-06).
            var level = DefenseText.Level;
            var enemy = DefenseText.Enemy(level);
            if (level != null && DefenseText.Covers(enemy, cell))
            {
                parts.Add(DefenseText.EnemyName(level));
                string state = DefenseText.Defeated(enemy) ? null : DefenseText.EnemyState(level, enemy);
                if (state != null) parts.Add(state);
            }
            return parts;
        }

        /// <summary>A named building whose footprint covers <paramref name="cell"/> although the
        /// cell maps to terrain: a level that lays its terrain after a building overwrites the
        /// building's cells (Pipeline.method_8; Class144's Control Center).</summary>
        private static Draggable BuildingUnderTerrain(SpaceChem.Pipeline.Pipeline p, Vector2i cell)
        {
            foreach (var kv in p.dictionary_1)
            {
                var d = kv.Key;
                if (d is Class612 || d is PipeDraggable || string.IsNullOrEmpty(d.string_1?.Trim())) continue;
                foreach (Vector2i local in d)
                    if (kv.Value + local == cell) return d;
            }
            return null;
        }

        /// <summary>The building whose own footprint covers <paramref name="cell"/> (a pipe cell is
        /// not its owner's footprint; terrain and decoration are no building), or null.</summary>
        private static Draggable BuildingAt(SpaceChem.Pipeline.Pipeline p, Vector2i cell)
        {
            var d = p?.method_7(cell);
            if (d == null) return null;
            if (d is Class612) d = BuildingUnderTerrain(p, cell);
            var origin = d == null ? null : p.method_9(d);
            if (!origin.HasValue) return null;
            var local = cell - origin.Value;
            foreach (var kv in d.class485_1)
                if (kv.Value.pipeDraggable_0 != null && kv.Value.pipeDraggable_0.dictionary_3.ContainsKey(local)) return null;
            return d;
        }

        /// <summary>Shift+Backspace on a building: the hover tooltip the game draws over it on the map
        /// (Draggable.class713_0, drawn by Pipeline's frame draw) minus the title — only where the
        /// game has one (its builder ran vmethod_18).</summary>
        private static void SpeakTooltip(Draggable d)
        {
            string text = d?.class713_0 != null ? PipelineText.Tooltip(d) : null;
            Speech.Tts.Speak(text ?? Loc.T("nav.no_tooltip"), interrupt: true);
        }

        private static List<string> CellContentsCore(SpaceChem.Pipeline.Pipeline p, Vector2i cell)
        {
            var parts = new List<string>();
            var d = p.method_7(cell);
            if (d == null) return parts;
            if (d is Class612) d = BuildingUnderTerrain(p, cell) ?? d;
            var origin = p.method_9(d);
            if (d is Class612 || !origin.HasValue) { parts.Add(Loc.T("pipeline.blocked")); return parts; }
            var local = cell - origin.Value;

            // A pipe cell of one of the owner's outputs (a crossing belongs to both pipes).
            int outIndex = 0;
            foreach (var kv in d.class485_1)
            {
                var pipe = kv.Value.pipeDraggable_0;
                if (pipe != null && pipe.dictionary_3.ContainsKey(local))
                {
                    parts.Add(PipeLabel(p, d, outIndex));
                    if (pipe.linkedList_0.Count > 0 && pipe.linkedList_0.Last.Value == local) parts.Add(Loc.T("pipeline.pipe.end"));
                    string carried = MoleculeIn(pipe, local);
                    if (carried != null) parts.Add(carried);
                    if (pipe.dictionary_4.TryGetValue(local, out var other) && other?.draggable_0 != null)
                        parts.Add(Loc.T("pipeline.pipe.crossing", new { pipe = DrawName(p, other) })); // "crossing Storage Tank 2 output"
                    return parts;
                }
                outIndex++;
            }

            // The component's own footprint.
            string name = d.string_1?.Trim();
            if (!(d is ReactorDraggable) && string.IsNullOrEmpty(name) && d.class485_0.Count == 0 && d.class485_1.Count == 0)
            {
                parts.Add(Loc.T("pipeline.blocked")); // decoration
                return parts;
            }
            parts.Add(PipelineText.Name(p, d));
            int inIndex = 0;
            foreach (var kv in d.class485_0)
            {
                if (kv.Value.vector2i_0 == local) { parts.Add(InputName(d, inIndex)); break; }
                inIndex++;
            }
            string meter = DefenseText.Meter(d); // a building's run meter ("Pressure 12 of 35")
            if (meter != null) parts.Add(meter);
            return parts;
        }

        /// <summary>During a run, the molecule in a pipe cell: the pipe keeps one slot per cell
        /// (PipeDraggable.linkedList_1, in the cells' order), shifted along each cycle.</summary>
        private static string MoleculeIn(PipeDraggable pipe, Vector2i local)
        {
            if ((int)Class258.smethod_16() == 0) return null;
            int i = 0;
            foreach (var c in pipe.linkedList_0)
            {
                if (c == local) break;
                i++;
            }
            int j = 0;
            foreach (var slot in pipe.linkedList_1)
            {
                if (j++ != i) continue;
                return slot.molecule_0 == null ? null : MoleculeText.NameAndFormula(slot.molecule_0);
            }
            return null;
        }

        /// <summary>"pipe, Assembly Reactor 2 psi output".</summary>
        private static string PipeLabel(SpaceChem.Pipeline.Pipeline p, Draggable owner, int outIndex)
            => Loc.T("pipeline.pipe", new { owner = PipelineText.Name(p, owner), output = OutputName(owner, outIndex) });

        private static int OutputIndex(PipeDraggable pipe)
        {
            int i = 0;
            foreach (var kv in pipe.draggable_0.class485_1)
            {
                if (kv.Value.pipeDraggable_0 == pipe) return i;
                i++;
            }
            return 0;
        }

        // ---- categories ([ ] and , . — the reactor editor's keys): components by role and the
        // open pipe ends, each in reading order; an item moves the map cursor there. ----

        private int _category = -1, _item = -1;

        // Roles: 0 reactors, 1 inputs, 2 outputs, 3 other, 4 open pipe ends, 5 defenses (the
        // special buildings, Class598: the Control Center, weapons — defense levels only; an
        // Oxygen Tank has only an input, which made it an "Output").
        private static readonly string[] RoleNames =
            { "pipeline.cat.reactors", "pipeline.cat.inputs", "pipeline.cat.outputs", "pipeline.cat.other", "pipeline.cat.ends", "pipeline.cat.defenses" };

        private static int[] Categories => DefenseText.IsDefense ? new[] { 0, 1, 2, 5, 3, 4 } : new[] { 0, 1, 2, 3, 4 };

        private static string CategoryName(int category) => RoleNames[Categories[category]];

        private static List<Vector2i> CategoryCells(SpaceChem.Pipeline.Pipeline p, int category)
        {
            category = Categories[category];
            var cells = new List<Vector2i>();
            if (category == 4)
            {
                foreach (var kv in PipelineText.Components(p))
                    foreach (var o in kv.Key.class485_1)
                    {
                        var pipe = o.Value.pipeDraggable_0;
                        if (pipe == null || pipe.linkedList_0.Count == 0 || o.Value.vmethod_0() != null) continue;
                        cells.Add(pipe.linkedList_0.Last.Value + kv.Value);
                    }
                cells.Sort((a, b) => a.int_1 != b.int_1 ? a.int_1.CompareTo(b.int_1) : a.int_0.CompareTo(b.int_0));
                return cells;
            }
            foreach (var kv in PipelineText.Components(p))
            {
                var d = kv.Key;
                bool reactor = d is ReactorDraggable, ins = d.class485_0.Count > 0, outs = d.class485_1.Count > 0;
                int role = reactor ? 0 : d is Class598 && DefenseText.IsDefense ? 5 : outs && !ins ? 1 : ins && !outs ? 2 : 3;
                if (role == category) cells.Add(kv.Value);
            }
            return cells;
        }

        // Only categories holding something are stops (UI/CategoryCycle, user rule 2026-10-04).
        private void StepCategory(int delta)
        {
            var p = Model;
            if (p == null || _drawPipe != null) return;
            int next = CategoryCycle.Next(_category, delta, Categories.Length, i => CategoryCells(p, i).Count);
            if (next < 0) { Speech.Tts.Speak(Loc.T("reactor.cat.none"), interrupt: true); return; }
            _category = next;
            _item = -1;
            Speech.Tts.Speak(Loc.T("reactor.cat", new { name = Loc.T(CategoryName(_category)), n = CategoryCells(p, _category).Count }), interrupt: true);
        }

        private void StepItem(int delta)
        {
            var p = Model;
            if (p == null || _drawPipe != null) return;
            int category = CategoryCycle.ForItems(_category, Categories.Length, i => CategoryCells(p, i).Count);
            if (category < 0) { Speech.Tts.Speak(Loc.T("reactor.cat.none"), interrupt: true); return; }
            if (category != _category) { _category = category; _item = -1; }
            var cells = CategoryCells(p, _category);
            _item = _item < 0 ? (delta > 0 ? 0 : cells.Count - 1) : ((_item + delta) % cells.Count + cells.Count) % cells.Count;
            var c = cells[_item];
            if (c.int_0 == _cursorX && c.int_1 == _cursorY && Equals(Navigation.FocusedNodeId, MapCellId(c.int_0, c.int_1)))
                Speech.Tts.Speak(MapReadout(c.int_0, c.int_1), interrupt: true);
            else
                FocusMapCell(c.int_0, c.int_1);
        }

        private void SpeakCoordinates()
        {
            if (!MapStop.Equals(Navigation.FocusedStopKey)) return;
            Speech.Tts.Speak(PipelineText.Cell(new Vector2i(_cursorX, _cursorY)), interrupt: true);
        }
    }
}
