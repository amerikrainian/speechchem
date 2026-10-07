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
    /// <summary>
    /// InlineAnnotationEditor, the "Output Note Editor" (a Class54 opened from the pipeline menu's
    /// note items, a right-click on a pipeline note, or a right-click on a reactor's output panel).
    /// A note (ReactorAnnotation, one per reactor output) is THREE molecules (molecule_0[0..2]),
    /// each built on a 4 x 4 grid, plus "Show in Pipeline" (bool_1); no text. The game builds them
    /// by dragging atoms from a palette and bond tools onto the grids; its grid cells (Class468) are
    /// bound straight to the molecules, so the mod edits the molecules with the cells' own drop logic
    /// and the drawn grids follow. Closing commits (method_13: tidy and rename the molecules, store
    /// the checkbox, renumber the bubbles, save or delete the Annotation row, as an undo step) — on
    /// Escape (native) or the mod's Done; the game has no cancel.
    ///
    /// Tab stops: Molecule 1-3 (a header row with the molecule's name, then its 4 x 4 grid), the
    /// palette (the 16 fixed atoms; the 4 recent atoms; Select Element = the periodic-table picker,
    /// whose pick joins the recent row and takes focus there), options (Show in Pipeline, Done).
    /// Editing follows the reactor's rules: Enter on an atom arms it (one shot), Enter on a grid
    /// cell places it — the drop bonds it singly to every neighbour that has room, as the game's
    /// does; Delete removes an atom; Ctrl+C arms the cell's atom, Ctrl+X arms it and removes it,
    /// Ctrl+V places like Enter; Backspace = the cell's bonds as radio groups per neighbour
    /// (none, single, double, triple — refused when an atom has no room) and Delete.
    /// </summary>
    public sealed class NoteEditorScreen : Screen
    {
        private const int Size = 4;
        private const string PaletteStop = "note.palette";
        private const string OptionsStop = "note.options";

        public override string Key => "note";
        public override string ScreenName => GameText.T("Output Note Editor");
        public override object InitialFocusStop => MoleculeStop(0);
        public override bool KeepStateOnPop => _covered;

        private static InlineAnnotationEditor Editor => ProfileUi.Settled<InlineAnnotationEditor>();

        private static ReactorAnnotation Note => Editor?.reactorAnnotation_0;

        public override bool IsActive() => Note != null;

        private static string MoleculeStop(int k) => "note.molecule." + k;

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction("screen.reactor.delete", () => WithCell(Remove));
            yield return new ElementAction("screen.reactor.copy", () => WithCell((k, pos) => Pick(k, pos, cut: false)));
            yield return new ElementAction("screen.reactor.cut", () => WithCell((k, pos) => Pick(k, pos, cut: true)));
            yield return new ElementAction("screen.reactor.paste", () => WithCell(PlaceArmed));
        }

        public override void Build(GraphBuilder b)
        {
            var note = Note;
            if (note == null) return;
            for (int k = 0; k < note.molecule_0.Length; k++) BuildMolecule(b, k);
            BuildPalette(b);
            BuildOptions(b);
        }

        // ---- lifetime: the periodic-table picker covers the editor and keeps it in the chain ----

        private bool _covered;
        private InlineAnnotationEditor _editor;
        private string _recentBefore;
        private bool _focusRecent;

        public override void OnPush()
        {
            var editor = Editor;
            if (_covered && !ReferenceEquals(editor, _editor)) { _covered = false; Navigation.ScreenClosed(this); }
            if (!ReferenceEquals(editor, _editor)) _armed = null;
            _editor = editor;
            // Back from the picker with a new atom: it joined the recent row at the end — focus it
            // (next update: the navigator's re-attach drops a focus request made now).
            if (_recentBefore != null && _recentBefore != RecentKey()) _focusRecent = true;
            _recentBefore = null;
        }

        public override void OnUpdate()
        {
            if (!_focusRecent || ActiveChild != null) return;
            _focusRecent = false;
            Navigation.FocusNode(RecentId(Size - 1));
        }

        public override void OnPop()
        {
            _covered = false;
            foreach (var s in GameState.ScreenStack())
                if (ReferenceEquals(s, _editor)) { _covered = true; return; }
            _editor = null;
            _armed = null;
        }

        // ---- the molecules ----

        private void BuildMolecule(GraphBuilder b, int k)
        {
            b.BeginStop(MoleculeStop(k));
            b.AddItem(ControlId.Structural("note.mol." + k), ProfileUi.Text(true, () => Header(k)));
            for (int y = 0; y < Size; y++)
            {
                b.StartRow("note.row");
                for (int x = 0; x < Size; x++)
                {
                    int cx = x, cy = y;
                    b.AddItem(CellId(k, x, y), new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[] { new NodeAnnouncement(() => Readout(k, new Vector2i(cx, cy), true), kind: AnnouncementKinds.Label) },
                        SpeaksOwnPosition = true,
                        OnActivate = () => PlaceArmed(k, new Vector2i(cx, cy)),
                        OnSecondary = () => OpenBondMenu(k, new Vector2i(cx, cy)),
                        OnTooltip = () => Speech.Tts.Speak(Details(k, new Vector2i(cx, cy)), interrupt: true),
                    });
                }
                b.EndRow();
            }
        }

        private static ControlId CellId(int k, int x, int y) => ControlId.Structural("note.cell." + k + "." + x + "." + y);

        private static Molecule MoleculeAt(int k)
        {
            var note = Note;
            return note == null || k < 0 || k >= note.molecule_0.Length ? null : note.molecule_0[k];
        }

        /// <summary>"Molecule 1, Water, H2O" / "Molecule 2, empty".</summary>
        private static string Header(int k)
        {
            var m = MoleculeAt(k);
            string title = Loc.T("note.molecule", new { n = k + 1 });
            if (m == null || m.dictionary_2.Count == 0) return title + ", " + Loc.T("note.empty");
            return title + ", " + MoleculeText.NameAndFormula(m);
        }

        /// <summary>"2, 1, Oxygen, single bond left" — coordinates (1-based in the grid) first.</summary>
        private static string Readout(int k, Vector2i pos, bool withCell)
        {
            var parts = new List<string>();
            if (withCell) parts.Add(Loc.T("reactor.cell", new { x = pos.int_0 + 1, y = pos.int_1 + 1 }));
            var m = MoleculeAt(k);
            Atom atom;
            if (m != null && m.dictionary_2.TryGetValue(pos, out atom))
            {
                parts.Add(atom.method_0());
                parts.AddRange(MoleculeViewerScreen.Bonds(m, pos));
            }
            else if (!withCell) parts.Add(Loc.T("note.empty"));
            return string.Join(", ", parts.ToArray());
        }

        private static string Details(int k, Vector2i pos)
        {
            var m = MoleculeAt(k);
            Atom atom;
            return m != null && m.dictionary_2.TryGetValue(pos, out atom) ? ReactorText.AtomDetails(atom) : Loc.T("nav.no_tooltip");
        }

        /// <summary>Runs <paramref name="act"/> on the focused grid cell, if a cell is focused.</summary>
        private static void WithCell(Action<int, Vector2i> act)
        {
            string key = Navigation.FocusedNodeId?.StructuralKey as string;
            if (key == null || !key.StartsWith("note.cell.", StringComparison.Ordinal)) return;
            var p = key.Substring("note.cell.".Length).Split('.');
            int k, x, y;
            if (p.Length != 3 || !int.TryParse(p[0], out k) || !int.TryParse(p[1], out x) || !int.TryParse(p[2], out y)) return;
            act(k, new Vector2i(x, y));
        }

        // ---- editing: the grid cell's own drop (Class468.vmethod_15) and the editor's tidy-up ----

        private Atom? _armed;

        /// <summary>Enter / Ctrl+V on a cell: drop the armed atom there (one shot). Like the game's
        /// drop: replace whatever is there (bonds reduced if the new atom has fewer), strip dangling
        /// bonds, then a single bond to each neighbour that has room; rename the molecule.</summary>
        private void PlaceArmed(int k, Vector2i pos)
        {
            var m = MoleculeAt(k);
            if (m == null || _armed == null) return;
            var atom = _armed.Value;
            _armed = null;
            try
            {
                m.method_19(MoleculePart.smethod_0(atom), pos.int_0, pos.int_1);
                m.method_28();
                m.method_30(new Struct98(new Vector2i(pos.int_0, pos.int_1 - 1), Enum128.Down), (Enum4)1);
                m.method_30(new Struct98(pos, Enum128.Right), (Enum4)1);
                m.method_30(new Struct98(pos, Enum128.Down), (Enum4)1);
                m.method_30(new Struct98(new Vector2i(pos.int_0 - 1, pos.int_1), Enum128.Right), (Enum4)1);
                Class307.smethod_1(m);
                Speech.Tts.Speak(Readout(k, pos, false) + MoleculeSuffix(m), interrupt: true);
            }
            catch (Exception ex) { Log.Error("[note] place failed", ex); }
        }

        /// <summary>". Water, H2O" after an edit — nothing for a lone atom, whose name is the atom's.</summary>
        private static string MoleculeSuffix(Molecule m)
            => m.dictionary_2.Count <= 1 ? "" : ". " + MoleculeText.NameAndFormula(m);

        /// <summary>Delete: take the atom off (the game's drag off the grid: method_18, then the
        /// editor's tidy-up strips its bonds and renames).</summary>
        private static void Remove(int k, Vector2i pos)
        {
            var m = MoleculeAt(k);
            if (m == null) return;
            if (!m.dictionary_2.ContainsKey(pos)) return;
            try
            {
                string name = m.dictionary_2[pos].method_0();
                m.method_18(Class178.smethod_0(pos));
                m.method_28();
                Class307.smethod_1(m);
                string rest = m.dictionary_2.Count == 0 ? ". " + Loc.T("note.empty") : MoleculeSuffix(m);
                Speech.Tts.Speak(Loc.T("note.removed", new { atom = name }) + rest, interrupt: true);
            }
            catch (Exception ex) { Log.Error("[note] remove failed", ex); }
        }

        /// <summary>Ctrl+C arms the cell's atom (the game's Ctrl-drag copy); Ctrl+X also takes it off.</summary>
        private void Pick(int k, Vector2i pos, bool cut)
        {
            var m = MoleculeAt(k);
            Atom atom;
            if (m == null || !m.dictionary_2.TryGetValue(pos, out atom)) return;
            _armed = atom;
            if (cut) Remove(k, pos);
            else Speech.Tts.Speak(Loc.T("reactor.edit.copied", new { what = atom.method_0() }), interrupt: true);
        }

        /// <summary>Backspace on a cell: a bond radio group toward each neighbouring atom (the game's
        /// bond tools dropped on the atom: method_30 sets 1-3 when both atoms have room, method_31
        /// removes), then Delete.</summary>
        private void OpenBondMenu(int k, Vector2i pos)
        {
            var m = MoleculeAt(k);
            if (m == null || !m.dictionary_2.ContainsKey(pos)) return;
            var items = new List<ActionListScreen.Item>();
            // Right / Down from this atom; left / up = the neighbour's own Right / Down bond.
            var sides = new[]
            {
                new KeyValuePair<Struct98, string>(new Struct98(pos, Enum128.Right), Loc.T("dir.right")),
                new KeyValuePair<Struct98, string>(new Struct98(pos, Enum128.Down), Loc.T("dir.down")),
                new KeyValuePair<Struct98, string>(new Struct98(new Vector2i(pos.int_0 - 1, pos.int_1), Enum128.Right), Loc.T("dir.left")),
                new KeyValuePair<Struct98, string>(new Struct98(new Vector2i(pos.int_0, pos.int_1 - 1), Enum128.Down), Loc.T("dir.up")),
            };
            for (int s = 0; s < sides.Length; s++)
            {
                var bond = sides[s].Key;
                string dir = sides[s].Value;
                var other = bond.vector2i_0 == pos ? bond.method_0() : bond.vector2i_0;
                if (!m.dictionary_2.ContainsKey(other)) continue;
                object group = "note.bond." + s;
                for (int n = 0; n <= 3; n++)
                {
                    int count = n;
                    items.Add(new ActionListScreen.Item
                    {
                        Group = group,
                        GroupLabel = () => Loc.T("note.bond.group", new { dir }),
                        Label = () => Loc.T("note.bond.item", new { dir, kind = count == 0 ? Loc.T("note.bond.none") : ReactorText.BondWord(count) }),
                        Selected = () => BondCount(MoleculeAt(k), bond) == count,
                        Run = () => SetBond(k, bond, count),
                    });
                }
            }
            items.Add(new ActionListScreen.Item { Label = () => GameText.T("Delete"), Run = () => Remove(k, pos) });
            PushChild(new ActionListScreen("note.menu", Readout(k, pos, false), items));
        }

        private static int BondCount(Molecule m, Struct98 bond)
        {
            Enum4 n;
            return m != null && m.dictionary_3.TryGetValue(bond, out n) ? (int)n : 0;
        }

        private static void SetBond(int k, Struct98 bond, int count)
        {
            var m = MoleculeAt(k);
            if (m == null) return;
            try
            {
                bool ok = count == 0 ? m.method_31(bond) : m.method_30(bond, (Enum4)count);
                m.method_28();
                Class307.smethod_1(m);
                if (!ok) Speech.Tts.Speak(Loc.T("note.bond.refused"), interrupt: true);
            }
            catch (Exception ex) { Log.Error("[note] bond failed", ex); }
        }

        // ---- the palette: the 16 fixed atoms, the 4 recent ones, Select Element ----

        private static ControlId RecentId(int i) => ControlId.Structural("note.recent." + i);

        private void BuildPalette(GraphBuilder b)
        {
            b.BeginStop(PaletteStop);
            b.StartRow("note.palette.fixed");
            var fixedAtoms = InlineAnnotationEditor.list_1;
            for (int i = 0; i < fixedAtoms.Count; i++)
            {
                var atom = fixedAtoms[i];
                b.AddItem(ControlId.Structural("note.atom." + i), AtomNode(() => atom));
            }
            b.EndRow();
            b.StartRow("note.palette.recent");
            for (int i = 0; i < Size; i++)
            {
                int index = i;
                b.AddItem(RecentId(i), AtomNode(() => index < InlineAnnotationEditor.list_0.Count ? InlineAnnotationEditor.list_0[index] : (Atom?)null,
                    () => Loc.T("note.recent")));
            }
            b.EndRow();
            b.AddItem(ControlId.Structural("note.select"), ProfileUi.Button(() => GameText.T("Select Element"), SelectElement));
        }

        private NodeVtable AtomNode(Func<Atom?> atom, Func<string> prefix = null) => new NodeVtable
        {
            ControlType = ControlTypes.Button,
            Announcements = new[]
            {
                new NodeAnnouncement(() => { var a = atom(); return a == null ? null : (prefix != null ? prefix() + " " : "") + a.Value.method_0(); },
                    kind: AnnouncementKinds.Label),
                new NodeAnnouncement(() => { var a = atom(); return a != null && _armed != null && _armed.Value.element_0 == a.Value.element_0 ? Loc.T("reactor.armed") : null; },
                    kind: AnnouncementKinds.Selected),
            },
            OnActivate = () =>
            {
                var a = atom();
                if (a == null) return;
                _armed = a;
                Speech.Tts.Speak(Loc.T("pipeline.armed"), interrupt: true);
            },
            OnTooltip = () => { var a = atom(); if (a != null) Speech.Tts.Speak(ReactorText.AtomDetails(a.Value), interrupt: true); },
        };

        /// <summary>The editor's "Select Element" (method_15: the periodic-table picker; the pick goes
        /// to the end of the recent row, method_16).</summary>
        private void SelectElement()
        {
            var editor = Editor;
            if (editor == null) return;
            _recentBefore = RecentKey();
            editor.method_15();
        }

        private static string RecentKey()
        {
            var parts = new List<string>();
            foreach (var a in InlineAnnotationEditor.list_0) parts.Add(((int)a.element_0).ToString());
            return string.Join(",", parts.ToArray());
        }

        // ---- options ----

        private void BuildOptions(GraphBuilder b)
        {
            b.BeginStop(OptionsStop);
            Func<string> state = () => Loc.T(ShowInPipeline() ? "value.on" : "value.off");
            b.AddItem(ControlId.Structural("note.show"), new NodeVtable
            {
                ControlType = ControlTypes.Toggle,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => GameText.Speech(GameText.T("Show in\nPipeline")), kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(state, kind: AnnouncementKinds.Value),
                },
                StateText = state,
                OnActivate = () =>
                {
                    var box = Editor?.class476_0;
                    if (box == null || !box.bool_1) return;
                    box.method_8(ShowInPipeline() ? (Enum106)1 : (Enum106)0);
                    Class428.class14_4.vmethod_0();
                },
            });
            b.AddItem(ControlId.Structural("note.done"), ProfileUi.Button(() => Loc.T("note.done"), () => Editor?.method_13()));
        }

        private static bool ShowInPipeline()
        {
            var box = Editor?.class476_0;
            return box != null && (int)box.method_7() == 0;
        }
    }
}
