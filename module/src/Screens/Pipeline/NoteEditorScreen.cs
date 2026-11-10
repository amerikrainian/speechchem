using System;
using System.Collections.Generic;
using Impeller;
using SpaceChem;
using SpaceChem.Pipeline;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.Screens.Common;
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

        private readonly MoleculeGrid _grid;

        public NoteEditorScreen()
        {
            _grid = new MoleculeGrid(this, "note", MoleculeAt,
                k => { var m = MoleculeAt(k); if (m != null) Class307.smethod_1(m); },
                k => MoleculeText.NameAndFormula(MoleculeAt(k)));
        }

        public override IEnumerable<ElementAction> GetActions() => _grid.Actions();

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
            if (!ReferenceEquals(editor, _editor)) _grid.Armed = null;
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
            _grid.Armed = null;
        }

        // ---- the molecules ----

        private void BuildMolecule(GraphBuilder b, int k)
        {
            b.BeginStop(MoleculeStop(k));
            b.AddItem(ControlId.Structural("note.mol." + k), ProfileUi.Text(true, () => Header(k)));
            _grid.BuildCells(b, k, Size);
        }

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
                b.AddItem(ControlId.Structural("note.atom." + i), _grid.AtomNode(() => atom));
            }
            b.EndRow();
            b.StartRow("note.palette.recent");
            for (int i = 0; i < Size; i++)
            {
                int index = i;
                b.AddItem(RecentId(i), _grid.AtomNode(() => index < InlineAnnotationEditor.list_0.Count ? InlineAnnotationEditor.list_0[index] : (Atom?)null,
                    () => Loc.T("note.recent")));
            }
            b.EndRow();
            b.AddItem(ControlId.Structural("note.select"), ProfileUi.Button(() => GameText.T("Select Element"), SelectElement));
        }

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
