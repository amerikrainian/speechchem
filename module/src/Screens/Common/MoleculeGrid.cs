using System;
using System.Collections.Generic;
using Impeller;
using SpaceChem;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens.Common
{
    /// <summary>
    /// A molecule built on the game's drag-and-drop grids (Class420: 4 columns, 4 or 8 rows of
    /// Class468 cells bound straight to a Molecule; the atom palettes Class286 / Class467 tiles),
    /// as the mod edits it — the note editor (§16b) and the ResearchNet molecule editors share it.
    /// The game's cell drop (Class468.vmethod_15) is reproduced exactly: method_19 puts the part,
    /// method_28 tidies, an atom bonds singly to each neighbour that has room; then the HOST's own
    /// after-edit action runs (the note: rename through Class307.smethod_1; ResearchNet: the
    /// editor's method_19 / method_16 — tidy, INVALID marker, name / formula fields, ratio).
    ///
    /// Keys (the reactor's rules): Enter on a palette atom arms it (one shot), Enter / Ctrl+V on a
    /// cell places it; Delete removes; Ctrl+C arms the cell's atom, Ctrl+X arms it and removes it;
    /// Backspace = the cell's bonds as radio groups per neighbouring atom, then Delete.
    /// </summary>
    internal sealed class MoleculeGrid
    {
        public const int Columns = 4;

        private readonly string _prefix;
        private readonly Func<int, Molecule> _molecule;
        private readonly Action<int> _afterEdit;
        private readonly Func<int, string> _label;
        private readonly Screen _host;

        /// <param name="prefix">Node id prefix, unique per screen ("note", "rnmol").</param>
        /// <param name="molecule">Molecule k (null when absent).</param>
        /// <param name="afterEdit">The host's tidy-up after any edit of molecule k.</param>
        /// <param name="label">Molecule k's "name, formula" as the host shows it, or null (spoken
        /// after an edit; nothing for a lone atom, whose name is the atom's).</param>
        public MoleculeGrid(Screen host, string prefix, Func<int, Molecule> molecule, Action<int> afterEdit, Func<int, string> label)
        {
            _host = host;
            _prefix = prefix;
            _molecule = molecule;
            _afterEdit = afterEdit;
            _label = label;
        }

        /// <summary>The armed atom (one shot), or null.</summary>
        public Atom? Armed;

        public IEnumerable<ElementAction> Actions()
        {
            yield return new ElementAction("screen.reactor.delete", () => WithCell(Remove));
            yield return new ElementAction("screen.reactor.copy", () => WithCell((k, pos) => Pick(k, pos, cut: false)));
            yield return new ElementAction("screen.reactor.cut", () => WithCell((k, pos) => Pick(k, pos, cut: true)));
            yield return new ElementAction("screen.reactor.paste", () => WithCell(PlaceArmed));
        }

        /// <summary>Molecule k's grid, <paramref name="rows"/> rows of 4 cells, into the open stop.</summary>
        public void BuildCells(GraphBuilder b, int k, int rows)
        {
            for (int y = 0; y < rows; y++)
            {
                b.StartRow(_prefix + ".row");
                for (int x = 0; x < Columns; x++)
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

        public ControlId CellId(int k, int x, int y) => ControlId.Structural(_prefix + ".cell." + k + "." + x + "." + y);

        /// <summary>"2, 1, Oxygen, single bond left" — coordinates (1-based in the grid) first.</summary>
        public string Readout(int k, Vector2i pos, bool withCell)
        {
            var parts = new List<string>();
            if (withCell) parts.Add(Loc.T("reactor.cell", new { x = pos.int_0 + 1, y = pos.int_1 + 1 }));
            var m = _molecule(k);
            Atom atom;
            if (m != null && m.dictionary_2.TryGetValue(pos, out atom))
            {
                parts.Add(atom.method_0());
                parts.AddRange(Screens.MoleculeViewerScreen.Bonds(m, pos));
            }
            else if (!withCell) parts.Add(Loc.T("note.empty"));
            return string.Join(", ", parts.ToArray());
        }

        private string Details(int k, Vector2i pos)
        {
            var m = _molecule(k);
            Atom atom;
            return m != null && m.dictionary_2.TryGetValue(pos, out atom) ? ReactorText.AtomDetails(atom) : Loc.T("nav.no_tooltip");
        }

        /// <summary>Runs <paramref name="act"/> on the focused grid cell, if one of ours is focused.</summary>
        private void WithCell(Action<int, Vector2i> act)
        {
            string key = Navigation.FocusedNodeId?.StructuralKey as string;
            string head = _prefix + ".cell.";
            if (key == null || !key.StartsWith(head, StringComparison.Ordinal)) return;
            var p = key.Substring(head.Length).Split('.');
            int k, x, y;
            if (p.Length != 3 || !int.TryParse(p[0], out k) || !int.TryParse(p[1], out x) || !int.TryParse(p[2], out y)) return;
            act(k, new Vector2i(x, y));
        }

        private void AfterEdit(int k)
        {
            try { _afterEdit(k); }
            catch (Exception ex) { Log.Error("[molgrid] after-edit failed", ex); }
        }

        /// <summary>". Water, H2O" after an edit — nothing for a lone atom.</summary>
        private string Suffix(int k, Molecule m)
        {
            if (m.dictionary_2.Count <= 1) return "";
            string label = _label(k);
            return string.IsNullOrEmpty(label) ? "" : ". " + label;
        }

        // ---- editing: the grid cell's own drop (Class468.vmethod_15) ----

        /// <summary>Enter / Ctrl+V on a cell: drop the armed atom there (one shot), as the game's
        /// drop: replace whatever is there, tidy, a single bond to each neighbour with room.</summary>
        public void PlaceArmed(int k, Vector2i pos)
        {
            var m = _molecule(k);
            if (m == null || Armed == null) return;
            var atom = Armed.Value;
            Armed = null;
            try
            {
                m.method_19(MoleculePart.smethod_0(atom), pos.int_0, pos.int_1);
                m.method_28();
                m.method_30(new Struct98(new Vector2i(pos.int_0, pos.int_1 - 1), Enum128.Down), (Enum4)1);
                m.method_30(new Struct98(pos, Enum128.Right), (Enum4)1);
                m.method_30(new Struct98(pos, Enum128.Down), (Enum4)1);
                m.method_30(new Struct98(new Vector2i(pos.int_0 - 1, pos.int_1), Enum128.Right), (Enum4)1);
                AfterEdit(k);
                Speech.Tts.Speak(Readout(k, pos, false) + Suffix(k, m), interrupt: true);
            }
            catch (Exception ex) { Log.Error("[molgrid] place failed", ex); }
        }

        /// <summary>Delete: take the atom off (the game's drag off the grid: method_18), then the
        /// host's tidy-up strips its bonds.</summary>
        public void Remove(int k, Vector2i pos)
        {
            var m = _molecule(k);
            if (m == null || !m.dictionary_2.ContainsKey(pos)) return;
            try
            {
                string name = m.dictionary_2[pos].method_0();
                m.method_18(Class178.smethod_0(pos));
                m.method_28();
                AfterEdit(k);
                string rest = m.dictionary_2.Count == 0 ? ". " + Loc.T("note.empty") : Suffix(k, m);
                Speech.Tts.Speak(Loc.T("note.removed", new { atom = name }) + rest, interrupt: true);
            }
            catch (Exception ex) { Log.Error("[molgrid] remove failed", ex); }
        }

        /// <summary>Ctrl+C arms the cell's atom (the game's Ctrl-drag copy); Ctrl+X also takes it off.</summary>
        private void Pick(int k, Vector2i pos, bool cut)
        {
            var m = _molecule(k);
            Atom atom;
            if (m == null || !m.dictionary_2.TryGetValue(pos, out atom)) return;
            Armed = atom;
            if (cut) Remove(k, pos);
            else Speech.Tts.Speak(Loc.T("reactor.edit.copied", new { what = atom.method_0() }), interrupt: true);
        }

        /// <summary>Backspace on a cell: a bond radio group toward each neighbouring atom (the game's
        /// bond tools dropped on the atom: method_30 sets 1-3 when both atoms have room, method_31
        /// removes), then Delete.</summary>
        public void OpenBondMenu(int k, Vector2i pos)
        {
            var m = _molecule(k);
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
                object group = _prefix + ".bond." + s;
                for (int n = 0; n <= 3; n++)
                {
                    int count = n;
                    items.Add(new ActionListScreen.Item
                    {
                        Group = group,
                        GroupLabel = () => Loc.T("note.bond.group", new { dir }),
                        Label = () => Loc.T("note.bond.item", new { dir, kind = count == 0 ? Loc.T("note.bond.none") : ReactorText.BondWord(count) }),
                        Selected = () => BondCount(_molecule(k), bond) == count,
                        Run = () => SetBond(k, bond, count),
                    });
                }
            }
            items.Add(new ActionListScreen.Item { Label = () => GameText.T("Delete"), Run = () => Remove(k, pos) });
            _host.PushChild(new ActionListScreen(_prefix + ".menu", Readout(k, pos, false), items));
        }

        private static int BondCount(Molecule m, Struct98 bond)
        {
            Enum4 n;
            return m != null && m.dictionary_3.TryGetValue(bond, out n) ? (int)n : 0;
        }

        private void SetBond(int k, Struct98 bond, int count)
        {
            var m = _molecule(k);
            if (m == null) return;
            try
            {
                bool ok = count == 0 ? m.method_31(bond) : m.method_30(bond, (Enum4)count);
                m.method_28();
                AfterEdit(k);
                if (!ok) Speech.Tts.Speak(Loc.T("note.bond.refused"), interrupt: true);
            }
            catch (Exception ex) { Log.Error("[molgrid] bond failed", ex); }
        }

        // ---- palette tiles ----

        /// <summary>A palette atom: Enter arms it, Shift+Backspace its details; "armed" while it is.</summary>
        public NodeVtable AtomNode(Func<Atom?> atom, Func<string> prefix = null) => new NodeVtable
        {
            ControlType = ControlTypes.Button,
            Announcements = new[]
            {
                new NodeAnnouncement(() => { var a = atom(); return a == null ? null : (prefix != null ? prefix() + " " : "") + a.Value.method_0(); },
                    kind: AnnouncementKinds.Label),
                new NodeAnnouncement(() => { var a = atom(); return a != null && Armed != null && Armed.Value.element_0 == a.Value.element_0 ? Loc.T("reactor.armed") : null; },
                    kind: AnnouncementKinds.Selected),
            },
            OnActivate = () =>
            {
                var a = atom();
                if (a == null) return;
                Armed = a;
                Speech.Tts.Speak(Loc.T("pipeline.armed"), interrupt: true);
            },
            OnTooltip = () => { var a = atom(); if (a != null) Speech.Tts.Speak(ReactorText.AtomDetails(a.Value), interrupt: true); },
        };
    }
}
