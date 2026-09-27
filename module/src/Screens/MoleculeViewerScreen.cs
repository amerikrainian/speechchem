using System;
using System.Collections.Generic;
using Impeller;
using SpaceChem;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens
{
    /// <summary>
    /// A molecule as a navigable mini-grid (user-approved, 2026-09-27) — reusable for any molecule
    /// the game draws: reactor input/output panels now; dialogs, output notes and the ResearchNet
    /// molecule builders later. A CHILD sub-screen over the Molecule model itself: atoms by grid
    /// position (dictionary_2) and bonds keyed (position, Down|Right) with a count 1-3
    /// (dictionary_3), bounded by method_4 (top-left) / method_5 (size). Cells read like reactor
    /// cells — "x, y" first (1-based within the molecule), then the atom and its bonds by
    /// direction; an empty cell is just its coordinates. Shift+Backspace gives the game's atom
    /// details (name, atomic number, maximum bonds). Escape closes (mod-side modal).
    ///
    /// LANDING mode (an input molecule, user-approved 2026-09-27): the game draws an input's
    /// molecule inside a zone-shaped box at its OWN grid positions (Molecule.method_36 draws
    /// dictionary_2 keys unshifted; a molecule taller than 4 rows gets a second box, method_7), and
    /// InputInstruction.vmethod_7 drops it into the reactor at exactly those positions plus the
    /// zone's offset. So with a landing offset the viewer shows the whole box and every cell reads
    /// the REACTOR cell it maps to — where the atoms will appear.
    /// </summary>
    public sealed class MoleculeViewerScreen : Screen
    {
        private readonly Molecule _molecule;
        private readonly string _key;
        private readonly Vector2i? _landing;

        /// <param name="landing">For an input molecule: the reactor cell (0-based) of its zone's
        /// top-left corner. Null = a shape only, counted from the molecule's own corner.</param>
        public MoleculeViewerScreen(string key, Molecule molecule, Vector2i? landing = null)
        {
            _key = key;
            _molecule = molecule;
            _landing = landing;
        }

        public override string Key => _key;
        public override string ScreenName => MoleculeText.NameAndFormula(_molecule);
        public override bool IsActive() => true;
        public override bool ModalCapturesEscape => true;
        public override bool Exclusive => true;

        public override IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, () => ParentScreen?.RemoveChild(this));
        }

        public override void Build(GraphBuilder b)
        {
            if (_molecule == null || _molecule.dictionary_2.Count == 0) return;
            Vector2i origin = _molecule.method_4();
            Vector2i size = _molecule.method_5();
            if (_landing.HasValue)
            {
                // The drawn box: 4 x 4 (4 x 8 for a tall molecule), from the zone's corner.
                size = new Vector2i(Math.Max(4, origin.int_0 + size.int_0),
                    Math.Max(_molecule.method_7() ? 8 : 4, origin.int_1 + size.int_1));
                origin = new Vector2i(0, 0);
            }
            ControlId start = null;
            for (int y = 0; y < size.int_1; y++)
            {
                b.StartRow(_key + ".row");
                for (int x = 0; x < size.int_0; x++)
                {
                    var pos = new Vector2i(origin.int_0 + x, origin.int_1 + y);
                    int cx = x, cy = y;
                    var id = ControlId.Structural(_key + ".cell." + x + "." + y);
                    if (start == null && _molecule.dictionary_2.ContainsKey(pos)) start = id;
                    b.AddItem(id, new NodeVtable
                    {
                        ControlType = ControlTypes.Text,
                        Announcements = new[] { new NodeAnnouncement(() => Readout(pos, cx, cy), kind: AnnouncementKinds.Label) },
                        SpeaksOwnPosition = true,
                        OnTooltip = () => Speech.Tts.Speak(Details(pos), interrupt: true),
                    });
                }
                b.EndRow();
            }
            if (start != null) b.SetStart(start);
        }

        private string Readout(Vector2i pos, int x, int y)
        {
            var at = _landing.HasValue ? new Vector2i(pos.int_0 + _landing.Value.int_0, pos.int_1 + _landing.Value.int_1) : new Vector2i(x, y);
            var parts = new List<string> { Loc.T("reactor.cell", new { x = at.int_0 + 1, y = at.int_1 + 1 }) };
            Atom atom;
            if (_molecule.dictionary_2.TryGetValue(pos, out atom))
            {
                parts.Add(atom.method_0());
                foreach (var bond in Bonds(pos)) parts.Add(bond);
            }
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>The bonds touching an atom, by direction ("double bond right").</summary>
        private IEnumerable<string> Bonds(Vector2i pos)
        {
            foreach (var kv in _molecule.dictionary_3)
            {
                var from = kv.Key.vector2i_0;
                var to = kv.Key.method_0();
                bool right = kv.Key.enum128_0 == Enum128.Right;
                string dir = null;
                if (from == pos) dir = Loc.T(right ? "dir.right" : "dir.down");
                else if (to == pos) dir = Loc.T(right ? "dir.left" : "dir.up");
                if (dir != null)
                    yield return Loc.T("reactor.bond", new { kind = ReactorText.BondWord((int)kv.Value), dir });
            }
        }

        private string Details(Vector2i pos)
        {
            Atom atom;
            return _molecule.dictionary_2.TryGetValue(pos, out atom) ? ReactorText.AtomDetails(atom) : Loc.T("nav.no_tooltip");
        }
    }
}
