using System;
using System.Collections.Generic;
using System.Linq;
using SpaceChem;
using SpaceChem.ResearchNet;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens.ResearchNet
{
    /// <summary>
    /// The custom assignment editors, opened by Custom Assignments' Edit: Class63 "Research Editor",
    /// Class65 "Production Editor", Class64 "Sandbox Editor" (Class56 widget screens that rebuild
    /// whenever they regain the top, bool_2). Each edits the assignment's CustomLevel in place:
    /// Assignment Name / Author Name fields (GClass16, copied into the level by the editor's own
    /// save-fields method before anything that rebuilds), input / output buttons that open the
    /// molecule editors (ResearchNetInputEditor / ResearchNetOutputEditor), the parameters, then
    /// Save and Play / Save and Exit / Cancel (the editor's own: a conflicting saved solution or
    /// unsaved changes ask first through a message box).
    ///
    /// Tab stops: Details (the two fields), Inputs, Outputs (research: + Large Output; sandbox: the
    /// eight printers P-1..P-8), Parameters (research: the reactor components as combo boxes over
    /// the game's radio choices; production: the six reactor types as toggles — the game refuses a
    /// fifth —, Reactor Quota and terrain as combo boxes, Include Recycler; sandbox: the two
    /// programmed input sequences, each a row "Programmed Input (…): P-1 Water, P-3 …" followed by
    /// add buttons P-1..P-8 and the game's "&lt;-" as "Remove last"), Save. An input / output is a
    /// row: the game's button with what its box shows ("Water, H2O, 50 percent; …" / "empty"),
    /// then the molecules as cells opening the molecule viewer. Every change runs the editor's own
    /// handler (its rebuild included). Escape is native (Cancel).
    /// </summary>
    public sealed class AssignmentEditorScreen : Screen
    {
        private const string DetailsStop = "rnedit.details", InputStop = "rnedit.inputs", OutputStop = "rnedit.outputs",
            ParamStop = "rnedit.params", SaveStop = "rnedit.save";

        public override string Key => "researchnet.editor";
        public override object InitialFocusStop => DetailsStop;
        public override bool KeepStateOnPop => _covered;

        public override string ScreenName
        {
            get
            {
                var e = Editor;
                if (e is Class63) return GameText.T("Research Editor", "ENGLISH ALPHABET ONLY");
                if (e is Class65) return GameText.T("Production Editor", "ENGLISH ALPHABET ONLY");
                if (e is Class64) return GameText.T("Sandbox Editor", "ENGLISH ALPHABET ONLY");
                return null;
            }
        }

        private static Class56 Editor
        {
            get
            {
                var top = GameApi.TopScreen();
                if (!(top is Class63 || top is Class64 || top is Class65)) return null;
                var s = (Class54)top;
                return s.bool_1 ? (Class56)s : null;
            }
        }

        public override bool IsActive() => Editor != null;

        private bool _covered;
        private object _editor;

        public override void OnPush()
        {
            var e = Editor;
            if (_covered && !ReferenceEquals(e, _editor)) { _covered = false; Navigation.ScreenClosed(this); }
            _editor = e;
        }

        /// <summary>Covered by a molecule editor or a message box (the editor stays in the chain):
        /// keep focus. Leaving starts over.</summary>
        public override void OnPop()
        {
            _covered = false;
            foreach (var s in GameState.ScreenStack())
                if (ReferenceEquals(s, _editor)) { _covered = true; return; }
            _editor = null;
        }

        public override void Build(GraphBuilder b)
        {
            switch (Editor)
            {
                case Class63 r: BuildResearch(b, r); break;
                case Class65 p: BuildProduction(b, p); break;
                case Class64 s: BuildSandbox(b, s); break;
            }
        }

        // ---- shared pieces ----

        private static void BuildDetails(GraphBuilder b, Class56 editor, Func<GClass16> name, Func<GClass16> author)
        {
            b.BeginStop(DetailsStop);
            b.AddItem(ControlId.Structural("rnedit.name"), Field(editor, name, "Assignment Name"));
            b.AddItem(ControlId.Structural("rnedit.author"), Field(editor, author, "Author Name"));
        }

        /// <summary>A text field; landing on it gives it the game's keyboard focus (Class56.method_14),
        /// as a click on it does.</summary>
        internal static NodeVtable Field(Class56 editor, Func<GClass16> field, string gameLabel)
        {
            var vt = GameTextField.Node(field, () => GameText.T(gameLabel), () => { });
            vt.OnSelect = () =>
            {
                var f = field();
                if (f != null) try { editor.method_14(f); } catch (Exception ex) { Log.Error("[rnedit] field focus failed", ex); }
            };
            return vt;
        }

        private static void BuildSave(GraphBuilder b, Action play, Action exit, Action cancel)
        {
            b.BeginStop(SaveStop);
            b.AddItem(ControlId.Structural("rnedit.play"), ProfileUi.Button(() => GameText.T("Save and Play"), play));
            b.AddItem(ControlId.Structural("rnedit.exit"), ProfileUi.Button(() => GameText.T("Save and Exit"), exit));
            b.AddItem(ControlId.Structural("rnedit.cancel"), ProfileUi.Button(() => GameText.T("Cancel"), cancel));
        }

        /// <summary>A zone row: the game's edit button, what its box shows, then the molecules as
        /// cells (Enter opens the molecule viewer).</summary>
        private void ZoneRow(GraphBuilder b, string id, string gameLabel, Func<List<KeyValuePair<Molecule, string>>> molecules, Action edit, int index, int count)
        {
            var list = molecules();
            var button = ProfileUi.Button(() => GameText.Speech(gameLabel), edit);
            button.Announcements = new[]
            {
                button.Announcements[0],
                new NodeAnnouncement(() =>
                {
                    var now = molecules();
                    return now.Count == 0 ? Loc.T("note.empty")
                        : string.Join("; ", now.Select(m => MoleculeText.NameAndFormula(m.Key) + (m.Value == null ? "" : ", " + m.Value)).ToArray());
                }, kind: AnnouncementKinds.Value),
                new NodeAnnouncement(() => Loc.T("nav.position", new { index, count }), kind: AnnouncementKinds.Position),
            };
            button.SpeaksOwnPosition = true; // the row's molecule cells are not list items
            b.StartRow();
            b.AddItem(ControlId.Structural("rnedit.zone." + id), button);
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i].Key;
                var cell = ProfileUi.Button(() => MoleculeText.NameAndFormula(m), () => PushChild(new MoleculeViewerScreen("rnedit.viewer", m)));
                cell.SpeaksOwnPosition = true;
                b.AddItem(ControlId.Structural("rnedit.zone." + id + "." + i), cell);
            }
            b.EndRow();
        }

        /// <summary>A random input zone's molecules with the shares its box prints ((int)(100 × count /
        /// total) percent).</summary>
        private static Func<List<KeyValuePair<Molecule, string>>> Random(Func<Dictionary<int, CustomResearchLevel.Class684>> zones, int k) => () =>
        {
            var list = new List<KeyValuePair<Molecule, string>>();
            CustomResearchLevel.Class684 zone;
            if (!zones().TryGetValue(k, out zone)) return list;
            int total = zone.list_0.Sum(p => p.int_0);
            foreach (var p in zone.list_0)
                list.Add(new KeyValuePair<Molecule, string>(p.molecule_0,
                    total > 0 ? Loc.T("value.percent", new { value = (int)(100f * p.int_0 / total) }) : null));
            return list;
        };

        private static Func<List<KeyValuePair<Molecule, string>>> One<T>(Func<Dictionary<int, T>> zones, int k, Func<T, Molecule> molecule) => () =>
        {
            var list = new List<KeyValuePair<Molecule, string>>();
            T v;
            if (zones().TryGetValue(k, out v) && molecule(v) != null) list.Add(new KeyValuePair<Molecule, string>(molecule(v), null));
            return list;
        };

        private static NodeVtable Toggle(Func<string> label, Func<bool> on, Action flip)
        {
            Func<string> state = () => Loc.T(on() ? "value.on" : "value.off");
            return new NodeVtable
            {
                ControlType = ControlTypes.Toggle,
                Announcements = new[]
                {
                    new NodeAnnouncement(label, kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(state, kind: AnnouncementKinds.Value),
                },
                StateText = state,
                OnActivate = () => ProfileUi.Press(flip),
            };
        }

        private NodeVtable Combo(string id, Func<string> label, Func<IReadOnlyList<string>> choices, Func<int> current, Action<int> commit)
        {
            Func<string> value = () => { var c = choices(); int i = current(); return i >= 0 && i < c.Count ? c[i] : null; };
            return new NodeVtable
            {
                ControlType = ControlTypes.ComboBox,
                Announcements = new[]
                {
                    new NodeAnnouncement(label, kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(value, kind: AnnouncementKinds.Value),
                },
                OnActivate = () => PushChild(new ChoiceListScreen("rnedit.choice." + id, choices, current(), i => ProfileUi.Press(() => commit(i)))),
            };
        }

        // ---- research (Class63) ----

        private static readonly string[] ComponentNames = { "Bonder", "Sensor", "Tunnel", "Fusion Laser", "Fission Laser" };
        private static readonly int[][] ComponentChoices = { new[] { 0, 2, 4, 8 }, new[] { 0, 1 }, new[] { 0, 2 }, new[] { 0, 1 }, new[] { 0, 1 } };

        private void BuildResearch(GraphBuilder b, Class63 e)
        {
            var level = e.customResearchLevel_0;
            BuildDetails(b, e, () => e.gclass16_0, () => e.gclass16_1);

            b.BeginStop(InputStop);
            ZoneRow(b, "a", GameText.T("Edit Input α"), Random(() => level.dictionary_1, 0), () => e.method_28(0), 1, 2);
            ZoneRow(b, "b", GameText.T("Edit Input β"), Random(() => level.dictionary_1, 1), () => e.method_28(1), 2, 2);

            b.BeginStop(OutputStop);
            ZoneRow(b, "psi", GameText.T("Edit Output ψ"), One(() => level.dictionary_2, 0, p => p.molecule_0), () => e.method_30(0), 1, level.bool_0 ? 1 : 2);
            if (!level.bool_0)
                ZoneRow(b, "omega", GameText.T("Edit Output ω"), One(() => level.dictionary_2, 1, p => p.molecule_0), () => e.method_30(1), 2, 2);
            var large = Toggle(() => GameText.T("Large Output"), () => level.bool_0, () => e.method_32(!level.bool_0));
            large.SpeaksOwnPosition = true;
            b.AddItem(ControlId.Structural("rnedit.large"), large);

            b.BeginStop(ParamStop);
            for (int c = 0; c < ComponentNames.Length; c++)
            {
                var component = (Enum32)c;
                var values = ComponentChoices[c];
                string name = ComponentNames[c];
                b.AddItem(ControlId.Structural("rnedit.component." + c), Combo("component." + c, () => GameText.T(name),
                    () => values.Select(v => v.ToString()).ToList(),
                    () => Array.IndexOf(values, e.method_26(component)),
                    i => e.method_24(true, component, values[i])));
            }

            BuildSave(b, e.method_34, e.method_35, e.method_38);
        }

        // ---- production (Class65) ----

        private static readonly string[] ReactorNames = { "Standard Reactor", "Assembly Reactor", "Disassembly Reactor", "Sensor Reactor", "Nuclear Reactor", "Super-Bonder Reactor" };

        private void BuildProduction(GraphBuilder b, Class65 e)
        {
            var level = e.customProductionLevel_0;
            BuildDetails(b, e, () => e.gclass16_0, () => e.gclass16_1);

            b.BeginStop(InputStop);
            ZoneRow(b, "r", GameText.T("Edit Input R"), Random(() => level.dictionary_1, 0), () => e.method_33(0), 1, 3);
            ZoneRow(b, "a", GameText.T("Edit Input A"), One(() => level.dictionary_2, 0, m => m), () => e.method_35(0), 2, 3);
            ZoneRow(b, "b", GameText.T("Edit Input B"), One(() => level.dictionary_2, 1, m => m), () => e.method_35(1), 3, 3);

            b.BeginStop(OutputStop);
            ZoneRow(b, "x", GameText.T("Edit Output X"), One(() => level.dictionary_3, 0, p => p.molecule_0), () => e.method_37(0), 1, 3);
            ZoneRow(b, "y", GameText.T("Edit Output Y"), One(() => level.dictionary_3, 1, p => p.molecule_0), () => e.method_37(1), 2, 3);
            ZoneRow(b, "z", GameText.T("Edit Output Z"), One(() => level.dictionary_3, 2, p => p.molecule_0), () => e.method_37(2), 3, 3);

            b.BeginStop(ParamStop);
            for (int t = 0; t < ReactorNames.Length; t++)
            {
                var type = (Enum135)t;
                string name = ReactorNames[t];
                b.AddItem(ControlId.Structural("rnedit.reactor." + t), Toggle(() => GameText.T(name), () => e.method_31(type),
                    () => e.method_28(!e.method_31(type), type)));
            }
            b.AddItem(ControlId.Structural("rnedit.quota"), Combo("quota", () => GameText.T("Reactor Quota"),
                () => Enumerable.Range(0, 7).Select(v => v.ToString()).ToList(), () => level.int_3, i => e.method_27(i)));
            b.AddItem(ControlId.Structural("rnedit.terrain"), Combo("terrain", () => Loc.T("custom.terrain"),
                () => CustomProductionLevel.list_0.Select(t => t.string_0).ToList(), () => level.int_2, i => e.method_26(i)));
            b.AddItem(ControlId.Structural("rnedit.recycler"), Toggle(() => GameText.T("Include Recycler"), () => level.bool_6, () =>
            {
                // The checkbox flips itself and calls method_52; the mod's flip rebuilds to show it.
                e.method_39();
                e.method_52(!level.bool_6);
                e.method_13();
            }));

            BuildSave(b, e.method_40, e.method_41, e.method_44);
        }

        // ---- sandbox (Class64) ----

        private const int Printers = 8, SequenceMax = 18; // Class64.method_27's cap

        private void BuildSandbox(GraphBuilder b, Class64 e)
        {
            var level = e.customSandboxLevel_0;
            BuildDetails(b, e, () => e.gclass16_0, () => e.gclass16_1);

            b.BeginStop(InputStop);
            ZoneRow(b, "r", GameText.T("Edit Input R"), Random(() => level.dictionary_1, 0), () => e.method_30(0), 1, 2);
            ZoneRow(b, "a", GameText.T("Edit Input A"), One(() => level.dictionary_2, 0, m => m), () => e.method_32(0), 2, 2);

            b.BeginStop(OutputStop);
            for (int p = 0; p < Printers; p++)
            {
                int k = p;
                ZoneRow(b, "p" + k, string.Format(GameText.T("Edit P-{0}"), k + 1), One(() => level.dictionary_3, k, m => m), () => e.method_34(k), k + 1, Printers);
            }

            b.BeginStop(ParamStop);
            Sequence(b, e, "start", "Programmed Input (Starting Sequence)", () => level.list_0);
            Sequence(b, e, "repeat", "Programmed Input (Repeating Sequence)", () => level.list_1);

            BuildSave(b, e.method_37, e.method_38, e.method_41);
        }

        /// <summary>"P-1 Water": a printer as the sequence draws it (its molecule's icon over "P-n").</summary>
        private static string PrinterText(CustomSandboxLevel level, int p)
        {
            Molecule m;
            string name = "P-" + (p + 1);
            return level.dictionary_3.TryGetValue(p, out m) && m != null ? name + " " + MoleculeText.NameAndFormula(m) : name;
        }

        private static string SequenceText(CustomSandboxLevel level, List<int> seq)
            => seq.Count == 0 ? Loc.T("note.empty") : string.Join(", ", seq.Select(p => PrinterText(level, p)).ToArray());

        private void Sequence(GraphBuilder b, Class64 e, string id, string gameLabel, Func<List<int>> seq)
        {
            var level = e.customSandboxLevel_0;
            b.AddItem(ControlId.Structural("rnedit.seq." + id), ProfileUi.Text(true,
                () => GameText.T(gameLabel) + ": " + SequenceText(level, seq())));
            b.StartRow();
            for (int p = 0; p < Printers; p++)
            {
                int k = p;
                var add = ProfileUi.Button(() => PrinterText(level, k), () =>
                {
                    var list = seq();
                    if (list.Count >= SequenceMax) { Speech.Tts.Speak(Loc.T("custom.seq.full", new { max = SequenceMax }), interrupt: true); return; }
                    e.method_27(list, k);
                    Speech.Tts.Speak(Loc.T("custom.seq.count", new { count = seq().Count, max = SequenceMax }), interrupt: true);
                });
                add.SpeaksOwnPosition = true;
                b.AddItem(ControlId.Structural("rnedit.seq." + id + ".add." + k), add);
            }
            var remove = ProfileUi.Button(() => Loc.T("custom.seq.remove"), () =>
            {
                var list = seq();
                if (list.Count == 0) return;
                string last = PrinterText(level, list[list.Count - 1]);
                e.method_28(list);
                Speech.Tts.Speak(Loc.T("note.removed", new { atom = last }), interrupt: true);
            });
            remove.SpeaksOwnPosition = true;
            b.AddItem(ControlId.Structural("rnedit.seq." + id + ".remove"), remove);
            b.EndRow();
        }
    }
}
