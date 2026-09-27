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
                        OnSelect = () => { _cursorX = cx; _cursorY = cy; },
                        OnActivate = () => ActivateMapCell(cx, cy),
                        OnJumpEdge = first => JumpMapEdge(cy, first),
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
            FocusMapCell(first ? 0 : p.method_4().int_0 - 1, y);
            return true;
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
            var parts = new List<string> { PipelineText.Cell(new Vector2i(x, y)) };
            parts.AddRange(CellContents(p, new Vector2i(x, y)));
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>What occupies a map cell, as spoken phrases (no coordinates).</summary>
        internal static List<string> CellContents(SpaceChem.Pipeline.Pipeline p, Vector2i cell)
        {
            var parts = new List<string>();
            var d = p.method_7(cell);
            if (d == null) return parts;
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
                    if (pipe.dictionary_4.TryGetValue(local, out var other) && other?.draggable_0 != null)
                        parts.Add(Loc.T("pipeline.pipe.crossing", new { pipe = PipeLabel(p, other.draggable_0, OutputIndex(other)) }));
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
            return parts;
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

        private static readonly string[] CategoryNames =
            { "pipeline.cat.reactors", "pipeline.cat.inputs", "pipeline.cat.outputs", "pipeline.cat.other", "pipeline.cat.ends" };

        private static List<Vector2i> CategoryCells(SpaceChem.Pipeline.Pipeline p, int category)
        {
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
                int role = reactor ? 0 : outs && !ins ? 1 : ins && !outs ? 2 : 3;
                if (role == category) cells.Add(kv.Value);
            }
            return cells;
        }

        private void StepCategory(int delta)
        {
            var p = Model;
            if (p == null) return;
            int n = CategoryNames.Length;
            _category = _category < 0 ? (delta > 0 ? 0 : n - 1) : ((_category + delta) % n + n) % n;
            _item = -1;
            Speech.Tts.Speak(Loc.T("reactor.cat", new { name = Loc.T(CategoryNames[_category]), n = CategoryCells(p, _category).Count }), interrupt: true);
        }

        private void StepItem(int delta)
        {
            var p = Model;
            if (p == null) return;
            if (_category < 0) _category = 0;
            var cells = CategoryCells(p, _category);
            if (cells.Count == 0)
            {
                Speech.Tts.Speak(Loc.T("reactor.cat", new { name = Loc.T(CategoryNames[_category]), n = 0 }), interrupt: true);
                return;
            }
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
