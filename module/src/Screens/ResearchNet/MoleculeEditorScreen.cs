using System;
using System.Collections.Generic;
using System.Linq;
using SpaceChem;
using SpaceChem.ResearchNet;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.Screens.Common;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens.ResearchNet
{
    /// <summary>
    /// The assignment editors' molecule editors (Class56 widget screens, built once):
    /// ResearchNetInputEditor "Molecule Input Editor" — a random input zone's up to three molecules,
    /// each a 4 x 4 grid with a name and a formula field, plus the Molecule Ratio slider (the
    /// molecules' shares in twelfths; RatioSliderWidget) — and ResearchNetOutputEditor "Molecule
    /// Output Editor" (or "Molecule Input Editor" for a fixed input or a printer) — one molecule,
    /// 4 x 4 or 4 x 8 for a large output. Both have the atom palette (the 15 fixed atoms
    /// Class450.list_2; four more tiles, Class286 over Class450.list_1, that Select Element's pick
    /// joins at the end, the oldest dropping off), a Molecule Database button per molecule (Class66:
    /// the game's catalog, the pick replaces the molecule), the INVALID marker (atoms not all
    /// connected), Okay (validates through the game's own message boxes) and Cancel.
    ///
    /// Grids edit like the note editor's (Common/MoleculeGrid: arm an atom, Enter places; Delete;
    /// Ctrl+X / C / V; Backspace = bonds), then the editor's own tidy-up runs (input: method_19 —
    /// tidy all three, INVALID markers, a known molecule fills its name and formula, the ratio
    /// re-evens when the molecule count changes; output: method_16). The fields are TextEntry
    /// nodes; landing gives the field the game's keyboard focus (method_14). The input editor's
    /// fields of an EMPTY molecule are disabled by the game (read "unavailable").
    ///
    /// Tab stops: one per molecule (header "Molecule 1, Water, H2O[, INVALID]" / "empty", Molecule
    /// Database, the grid, Molecule Name, Molecule Formula), the palette, the ratio (input editor:
    /// the shares as one row, then one slider per handle between two neighbouring molecules —
    /// Left / Right move it a twelfth, as the drag snaps; each keeps at least one), Okay / Cancel.
    /// Escape is native (Cancel).
    /// </summary>
    public sealed class MoleculeEditorScreen : Screen
    {
        private const string PaletteStop = "rnmol.palette", RatioStop = "rnmol.ratio", ButtonStop = "rnmol.buttons";
        private const int Twelfths = 12; // RatioSliderWidget.int_0

        public override string Key => "researchnet.molecule";
        public override bool KeepStateOnPop => _covered;
        public override object InitialFocusStop => MoleculeStop(0);

        public override string ScreenName
        {
            get
            {
                var top = Editor;
                if (top is ResearchNetInputEditor) return GameText.T("Molecule Input Editor", "ENGLISH ALPHABET ONLY");
                if (top is ResearchNetOutputEditor o)
                    return o.bool_5 ? GameText.T("Molecule Input Editor", "ENGLISH ALPHABET ONLY") : GameText.T("Molecule Output Editor", "ENGLISH ALPHABET ONLY");
                return null;
            }
        }

        private static Class56 Editor
        {
            get
            {
                var top = GameApi.TopScreen();
                if (!(top is ResearchNetInputEditor || top is ResearchNetOutputEditor)) return null;
                var s = (Class56)top;
                return s.bool_1 ? s : null;
            }
        }

        private static ResearchNetInputEditor Input => Editor as ResearchNetInputEditor;
        private static ResearchNetOutputEditor Output => Editor as ResearchNetOutputEditor;

        public override bool IsActive() => Editor != null;

        private static string MoleculeStop(int k) => "rnmol.molecule." + k;

        private readonly MoleculeGrid _grid;

        public MoleculeEditorScreen()
        {
            _grid = new MoleculeGrid(this, "rnmol", MoleculeAt, AfterEdit, k => NameAndFormula(k));
        }

        public override IEnumerable<ElementAction> GetActions() => _grid.Actions();

        // ---- lifetime: the periodic table, the database and the game's message boxes cover it ----

        private bool _covered;
        private object _editor;
        private string _tilesBefore;
        private bool _focusTile;

        public override void OnPush()
        {
            var e = Editor;
            if (_covered && !ReferenceEquals(e, _editor)) { _covered = false; Navigation.ScreenClosed(this); }
            if (!ReferenceEquals(e, _editor)) _grid.Armed = null;
            _editor = e;
            if (_tilesBefore != null && _tilesBefore != TilesKey()) _focusTile = true;
            _tilesBefore = null;
        }

        public override void OnUpdate()
        {
            if (!_focusTile || ActiveChild != null) return;
            _focusTile = false;
            Navigation.FocusNode(TileId(Tiles().Count - 1));
        }

        public override void OnPop()
        {
            _covered = false;
            foreach (var s in GameState.ScreenStack())
                if (ReferenceEquals(s, _editor)) { _covered = true; return; }
            _editor = null;
            _grid.Armed = null;
        }

        // ---- the molecules ----

        private static int MoleculeCount => Input != null ? 3 : Output != null ? 1 : 0;

        private static Molecule MoleculeAt(int k)
        {
            var i = Input;
            if (i != null)
            {
                switch (k)
                {
                    case 0: return i.moleculeCountPair_0.molecule_0;
                    case 1: return i.moleculeCountPair_1.molecule_0;
                    case 2: return i.moleculeCountPair_2.molecule_0;
                }
                return null;
            }
            return k == 0 ? Output?.molecule_0 : null;
        }

        private static GClass16 NameField(int k)
        {
            var i = Input;
            if (i != null) return k == 0 ? i.gclass16_0 : k == 1 ? i.gclass16_2 : k == 2 ? i.gclass16_4 : null;
            return k == 0 ? Output?.gclass16_0 : null;
        }

        private static GClass16 FormulaField(int k)
        {
            var i = Input;
            if (i != null) return k == 0 ? i.gclass16_1 : k == 1 ? i.gclass16_3 : k == 2 ? i.gclass16_5 : null;
            return k == 0 ? Output?.gclass16_1 : null;
        }

        private static bool Invalid(int k)
        {
            var i = Input;
            Class474 marker = i != null ? (k == 0 ? i.class474_0 : k == 1 ? i.class474_1 : i.class474_2) : Output?.class474_0;
            return marker != null && marker.bool_1;
        }

        private static void AfterEdit(int k)
        {
            var i = Input;
            if (i != null) { i.method_19(); return; }
            Output?.method_16();
        }

        /// <summary>The molecule's name and formula as its fields hold them (the game names it from
        /// the fields on Okay), or null when both are blank.</summary>
        private static string NameAndFormula(int k)
        {
            string name = NameField(k)?.string_0, formula = FormulaField(k)?.string_0;
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(name)) parts.Add(name);
            if (!string.IsNullOrEmpty(formula) && formula != name) parts.Add(formula);
            return parts.Count == 0 ? null : string.Join(", ", parts.ToArray());
        }

        private static int Rows(int k) => Output != null && Output.bool_4 ? 8 : 4;

        private string Header(int k)
        {
            var m = MoleculeAt(k);
            var parts = new List<string>();
            if (Input != null) parts.Add(Loc.T("note.molecule", new { n = k + 1 }));
            if (m == null || m.dictionary_2.Count == 0) parts.Add(Loc.T("note.empty"));
            else
            {
                string label = NameAndFormula(k);
                if (label != null) parts.Add(label);
                if (Invalid(k)) parts.Add(GameText.T("INVALID"));
            }
            return string.Join(", ", parts.ToArray());
        }

        public override void Build(GraphBuilder b)
        {
            var editor = Editor;
            if (editor == null) return;
            for (int k = 0; k < MoleculeCount; k++) BuildMolecule(b, editor, k);
            BuildPalette(b);
            if (Input != null) BuildRatio(b, Input);
            BuildButtons(b);
        }

        private void BuildMolecule(GraphBuilder b, Class56 editor, int k)
        {
            b.BeginStop(MoleculeStop(k));
            b.AddItem(ControlId.Structural("rnmol.header." + k), ProfileUi.Text(true, () => Header(k)));
            var database = ProfileUi.Button(() => GameText.T("Molecule Database", "ENGLISH ALPHABET ONLY"), () =>
            {
                var i = Input;
                if (i != null) i.method_21(k + 1);
                else Output?.method_18();
            });
            database.SpeaksOwnPosition = true;
            b.AddItem(ControlId.Structural("rnmol.database." + k), database);
            _grid.BuildCells(b, k, Rows(k));
            b.AddItem(ControlId.Structural("rnmol.name." + k), MoleculeField(editor, () => NameField(k), "Molecule Name"));
            b.AddItem(ControlId.Structural("rnmol.formula." + k), MoleculeField(editor, () => FormulaField(k), "Molecule Formula"));
        }

        private static NodeVtable MoleculeField(Class56 editor, Func<GClass16> field, string gameLabel)
        {
            var f = field();
            if (f != null && !f.method_9()) // disabled: the molecule is empty
            {
                var vt = ProfileUi.Text(true, () => GameText.T(gameLabel));
                vt.Announcements = new[] { vt.Announcements[0], new NodeAnnouncement(() => Loc.T("value.unavailable"), kind: AnnouncementKinds.Enabled) };
                return vt;
            }
            return AssignmentEditorScreen.Field(editor, field, gameLabel);
        }

        // ---- the palette ----

        private static ControlId TileId(int i) => ControlId.Structural("rnmol.tile." + i);

        /// <summary>The four changeable tiles (Class286.ilist_0 — Select Element's picks shift in).</summary>
        private static List<Class467> Tiles()
        {
            Class286 palette = Input?.class286_0 ?? Output?.class286_0;
            return palette == null ? new List<Class467>() : palette.ilist_0.ToList();
        }

        private static string TilesKey()
            => string.Join(",", Tiles().Select(t => ((int)t.method_7().atom_0.element_0).ToString()).ToArray());

        private void BuildPalette(GraphBuilder b)
        {
            b.BeginStop(PaletteStop);
            var fixedAtoms = Class450.list_2;
            for (int row = 0; row * 8 < fixedAtoms.Count; row++)
            {
                b.StartRow("rnmol.palette.fixed");
                for (int i = row * 8; i < Math.Min(fixedAtoms.Count, row * 8 + 8); i++)
                {
                    var atom = fixedAtoms[i];
                    b.AddItem(ControlId.Structural("rnmol.atom." + i), _grid.AtomNode(() => atom));
                }
                b.EndRow();
            }
            b.StartRow("rnmol.palette.tiles");
            var tiles = Tiles();
            for (int i = 0; i < tiles.Count; i++)
            {
                int index = i;
                b.AddItem(TileId(i), _grid.AtomNode(() =>
                {
                    var t = Tiles();
                    return index < t.Count && t[index].method_7().enum110_0 == (Enum110)0 ? t[index].method_7().atom_0 : (Atom?)null;
                }));
            }
            b.EndRow();
            b.AddItem(ControlId.Structural("rnmol.select"), ProfileUi.Button(() => GameText.T("Select Element"), () =>
            {
                _tilesBefore = TilesKey();
                var i = Input;
                if (i != null) i.method_20();
                else Output?.method_17();
            }));
        }

        // ---- the ratio (input editor) ----

        private static CustomLevel.MoleculeCountPair[] Shares(ResearchNetInputEditor e)
            => e?.ratioSliderWidget_0?.moleculeCountPair_0 ?? new CustomLevel.MoleculeCountPair[0];

        private static string ShareText(CustomLevel.MoleculeCountPair p)
            => MoleculeText.NameAndFormula(p.molecule_0) + " " + Loc.T("value.percent", new { value = (int)Math.Round(100.0 * p.int_0 / Twelfths) });

        private void BuildRatio(GraphBuilder b, ResearchNetInputEditor e)
        {
            b.BeginStop(RatioStop);
            string title = GameText.Speech(GameText.T("Molecule\nRatio"));
            b.AddItem(ControlId.Structural("rnmol.ratio"), ProfileUi.Text(true, () =>
            {
                var shares = Shares(Input);
                string body = shares.Length == 0 ? "N/A" : string.Join(", ", shares.Select(ShareText).ToArray());
                return title + ", " + body;
            }));
            var now = Shares(e);
            for (int h = 1; h < now.Length; h++)
            {
                int handle = h;
                Func<string> value = () =>
                {
                    var s = Shares(Input);
                    return handle < s.Length ? ShareText(s[handle - 1]) + ", " + ShareText(s[handle]) : null;
                };
                b.AddItem(ControlId.Structural("rnmol.handle." + h), new NodeVtable
                {
                    ControlType = ControlTypes.Slider,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => title, kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(value, kind: AnnouncementKinds.Value),
                    },
                    SpeaksOwnPosition = true,
                    StateText = value,
                    OnAdjust = (sign, large) => MoveHandle(handle, sign * (large ? 3 : 1)),
                });
            }
        }

        /// <summary>The handle between molecules h-1 and h moves by <paramref name="delta"/> twelfths
        /// (the drag's own rule, vmethod_7: the pair's sum is kept, each side at least one).</summary>
        private static void MoveHandle(int h, int delta)
        {
            var s = Shares(Input);
            if (h < 1 || h >= s.Length) return;
            int sum = s[h - 1].int_0 + s[h].int_0;
            int left = Math.Max(1, Math.Min(sum - 1, s[h - 1].int_0 + delta));
            s[h - 1].int_0 = left;
            s[h].int_0 = sum - left;
        }

        // ---- Okay / Cancel ----

        private static void BuildButtons(GraphBuilder b)
        {
            b.BeginStop(ButtonStop);
            b.AddItem(ControlId.Structural("rnmol.okay"), ProfileUi.Button(() => GameText.T("Okay"), () =>
            {
                var i = Input;
                if (i != null) i.method_23();
                else Output?.method_20();
            }));
            b.AddItem(ControlId.Structural("rnmol.cancel"), ProfileUi.Button(() => GameText.T("Cancel"), () => Editor?.method_12()));
        }
    }

    /// <summary>
    /// Class66, "Molecule Database" (from a molecule editor's database button): "Select a molecule
    /// to use it in your design:", the game's 28 catalog molecules as buttons in its order (7 to a
    /// drawn row), Cancel. Enter = the button (method_16: a copy replaces the molecule being edited,
    /// the database closes). One list, then Cancel; Escape is native (Cancel).
    /// </summary>
    public sealed class MoleculeDatabaseScreen : Screen
    {
        public override string Key => "researchnet.database";
        public override string ScreenName => GameText.T("Molecule Database", "ENGLISH ALPHABET ONLY");

        private static Class66 Database => ProfileUi.Settled<Class66>();

        public override bool IsActive() => Database != null;

        public override void Build(GraphBuilder b)
        {
            var d = Database;
            if (d == null) return;
            b.BeginStop("rndb.list");
            b.AddItem(ControlId.Structural("rndb.prompt"), ProfileUi.Text(true, () => GameText.T("Select a molecule to use it in your design:")));
            var molecules = d.list_0;
            for (int i = 0; i < molecules.Count; i++)
            {
                var m = molecules[i];
                int index = i + 1, count = molecules.Count;
                var vt = ProfileUi.Button(() => MoleculeText.NameAndFormula(m), () => Database?.method_16(m));
                vt.Announcements = new[] { vt.Announcements[0], new NodeAnnouncement(() => Loc.T("nav.position", new { index, count }), kind: AnnouncementKinds.Position) };
                vt.SpeaksOwnPosition = true; // the prompt above is not one of them
                b.AddItem(ControlId.Structural("rndb.m." + i), vt);
            }
            b.BeginStop("rndb.cancel");
            b.AddItem(ControlId.Structural("rndb.cancel"), ProfileUi.Button(() => GameText.T("Cancel"), () =>
            {
                var db = Database;
                if (db != null) db.method_8(db.method_4)();
            }));
        }
    }
}
