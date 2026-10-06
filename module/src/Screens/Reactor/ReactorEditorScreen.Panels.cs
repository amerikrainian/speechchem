using System;
using System.Collections.Generic;
using Impeller;
using SpaceChem;
using SpaceChem.Pipeline;
using SpaceChem.Reactor;
using SpaceChem.UI;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;
using SpeechChem.UI.Graph;

namespace SpeechChem.Screens.Reactor
{
    public sealed partial class ReactorEditorScreen
    {
        // ---- palette: the instruction slots this reactor allows (Class715.dictionary_0, keyed by
        // the slot's hotkey scancode, in the drawn order), "W, arrow up". The templates are the
        // game's own (recoloured for the active layer by Class715.method_4). Enter ARMS a slot (the
        // next Enter on a grid cell places it — user rule); Shift+Backspace reads the slot's
        // tooltip. ----

        private int _armedKey = -1; // scancode of the armed palette slot, -1 = none

        /// <summary>The letter printed under a palette slot (Keys values are SDL scancodes: A = 4).</summary>
        internal static string KeyLetter(int scancode)
            => scancode >= 4 && scancode <= 29 ? ((char)('A' + scancode - 4)).ToString() : scancode.ToString();

        internal static InstructionMenuItem Slot(Class77 editor, int scancode)
        {
            var palette = editor?.class715_0;
            if (palette == null) return null;
            foreach (var kv in palette.dictionary_0)
                if ((int)kv.Key == scancode) return kv.Value.struct116_0.bool_0 ? kv.Value : null;
            return null;
        }

        private void BuildPalette(GraphBuilder b, Class77 editor)
        {
            var palette = editor.class715_0;
            if (palette == null) return;
            b.BeginStop(PaletteStop);
            foreach (var kv in palette.dictionary_0)
            {
                if (!kv.Value.struct116_0.bool_0) continue;
                int key = (int)kv.Key;
                b.AddItem(ControlId.Structural("reactor.palette." + key), new NodeVtable
                {
                    ControlType = ControlTypes.Button,
                    Announcements = new[]
                    {
                        new NodeAnnouncement(() => PaletteLabel(key), kind: AnnouncementKinds.Label),
                        new NodeAnnouncement(() => _armedKey == key ? Loc.T("reactor.armed") : null, kind: AnnouncementKinds.Selected),
                    },
                    OnActivate = () => Arm(key),
                    OnTooltip = () =>
                    {
                        var slot = Slot(Editor, key);
                        Speech.Tts.Speak(Patches.TooltipCapture.Speech(slot?.class713_0)
                            ?? ReactorText.GameName(slot?.struct116_0.method_0().GetType()) ?? Loc.T("nav.no_tooltip"), interrupt: true);
                    },
                });
            }
        }

        private static string PaletteLabel(int key)
        {
            var slot = Slot(Editor, key);
            if (slot == null) return null;
            return KeyLetter(key) + ", " + ReactorText.Label(slot.struct116_0.method_0());
        }

        /// <summary>Arm a slot. From the palette Enter confirms "… armed"; the Instructions category
        /// just names the instruction (arming is what cycling it means — user rule).</summary>
        private void Arm(int key, bool quiet = false)
        {
            var slot = Slot(Editor, key);
            if (slot == null) return;
            _armedKey = key;
            string instruction = ReactorText.Label(slot.struct116_0.method_0());
            Speech.Tts.Speak(quiet ? instruction : Loc.T("reactor.armed.instr", new { instruction }), interrupt: true);
        }

        // ---- layer controls (Class714 over the reactor's masks): Active red/blue (radio — the
        // active layer is what new instructions get), Visible red/blue, Locked red/blue (toggles).
        // Labels are the panel's own ("Active", "Visible", "Locked") plus the colour. ----

        private void BuildLayers(GraphBuilder b, Class77 editor)
        {
            if (editor.class715_0?.class714_0 == null) return;
            b.BeginStop(LayersStop);
            ActiveRadio(b, true);
            ActiveRadio(b, false);
            LayerToggle(b, "visible", true);
            LayerToggle(b, "visible", false);
            LayerToggle(b, "locked", true);
            LayerToggle(b, "locked", false);
        }

        private static Class714 Layers => Editor?.class715_0?.class714_0;

        private static bool RedActive => Layers != null && ((int)Layers.method_0() & ReactorText.Red) != 0;

        private void ActiveRadio(GraphBuilder b, bool red)
        {
            b.AddItem(ControlId.Structural("reactor.layer.active." + (red ? "red" : "blue")), new NodeVtable
            {
                ControlType = ControlTypes.RadioButton,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => GameText.T("Active") + ", " + Loc.T(red ? "reactor.red" : "reactor.blue"), kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(() => RedActive == red ? Loc.T("value.selected") : null, kind: AnnouncementKinds.Selected),
                },
                OnActivate = () => SetActive(red),
            });
        }

        private void SetActive(bool red)
        {
            var l = Layers;
            if (l == null || RedActive == red) return;
            Class428.class14_4.vmethod_0();
            l.method_1((Enum114)(red ? ReactorText.AllRed : ReactorText.AllBlue));
        }

        /// <summary>L: the game's Tab (Class714.method_10), which is navigation while modeled.</summary>
        private void ToggleActiveLayer()
        {
            var l = Layers;
            if (l == null) return;
            l.method_10();
            Speech.Tts.Speak(Loc.T(RedActive ? "reactor.red" : "reactor.blue"), interrupt: true); // just the colour (user rule)
        }

        private void LayerToggle(GraphBuilder b, string kind, bool red)
        {
            bool visible = kind == "visible";
            int bits = red ? ReactorText.AllRed : ReactorText.AllBlue;
            Func<bool> on = () =>
            {
                var l = Layers;
                if (l == null) return false;
                int mask = (int)(visible ? l.method_2() : l.method_4());
                return (mask & bits) == bits;
            };
            Func<string> state = () => Loc.T(on() ? "value.on" : "value.off");
            b.AddItem(ControlId.Structural("reactor.layer." + kind + "." + (red ? "red" : "blue")), new NodeVtable
            {
                ControlType = ControlTypes.Toggle,
                Announcements = new[]
                {
                    new NodeAnnouncement(() => GameText.T(visible ? "Visible" : "Locked") + ", " + Loc.T(red ? "reactor.red" : "reactor.blue"), kind: AnnouncementKinds.Label),
                    new NodeAnnouncement(state, kind: AnnouncementKinds.Value),
                },
                StateText = state,
                OnActivate = () =>
                {
                    var l = Layers;
                    if (l == null) return;
                    Class428.class14_4.vmethod_0();
                    int mask = (int)(visible ? l.method_2() : l.method_4());
                    int next = on() ? mask & ~bits : mask | bits;
                    if (visible) l.method_3((Enum114)next);
                    else l.method_5((Enum114)next);
                },
            });
        }

        // ---- molecules: the side panels — each input (α, β) and output (ψ, ω) of this reactor, read
        // the way Class77.vmethod_7 / vmethod_8 draw them: through each port's connection to its
        // annotation (research inputs, production tanks, another reactor's output note…). ----

        private void BuildMolecules(GraphBuilder b, Class77 editor)
        {
            var rd = editor.reactorDraggable_0;
            if (rd == null) return;
            b.BeginStop(MoleculesStop);
            foreach (int index in InputPanels(editor))
            {
                var vt = ProfileUi.Text(true, () => InputLine(index));
                vt.OnActivate = () => OpenInput(index);
                b.AddItem(ControlId.Structural("reactor.mol.in." + index), vt);
            }
            foreach (int index in rd.class485_1.Keys)
            {
                var vt = ProfileUi.Text(true, () => OutputLine(index));
                vt.OnActivate = () => OpenMolecules(PanelAnnotation(false, index), "reactor.mol.out." + index);
                vt.OnSecondary = () => EditNote(index);
                b.AddItem(ControlId.Structural("reactor.mol.out." + index), vt);
            }
        }

        /// <summary>The input ports the panel draws, in order. The laser reactor (Class636
        /// draggable, Class80 editor) has a third input, the discharge gas (Xe) that fires it; its
        /// panel (Class80.vmethod_7) draws only the first two ports, α and β — so do we. From the
        /// decompile; UNTESTED live.</summary>
        private static List<int> InputPanels(Class77 editor)
        {
            var indices = new List<int>();
            foreach (int index in editor.reactorDraggable_0.class485_0.Keys)
            {
                if (editor is Class80 && indices.Count >= 2) break;
                indices.Add(index);
            }
            return indices;
        }

        /// <summary>The Molecules stop's lines, in its order (inputs, then outputs).</summary>
        private static List<string> MoleculeLines(Class77 editor)
        {
            var lines = new List<string>();
            var rd = editor?.reactorDraggable_0;
            if (rd == null) return lines;
            foreach (int index in InputPanels(editor)) lines.Add(InputLine(index));
            foreach (int index in rd.class485_1.Keys) lines.Add(OutputLine(index));
            lines.RemoveAll(string.IsNullOrEmpty);
            return lines;
        }

        /// <summary>Backspace on an output line: the game's right-click on that output panel
        /// (Class77.vmethod_2) — this reactor's own note for the output, in the Output Note Editor
        /// (NoteEditorScreen), centred. Research levels (a Class84 host) can't edit notes.</summary>
        private static void EditNote(int index)
        {
            var rd = Editor?.reactorDraggable_0;
            if (rd == null || !rd.class485_1.ContainsKey(index) || Class53.smethod_5<Class84>() != null) return;
            var note = rd.class485_1[index].method_0() as SpaceChem.Pipeline.ReactorAnnotation;
            if (note == null) return;
            try { Class53.smethod_1(new InlineAnnotationEditor(note, bool_2: true)); }
            catch (System.Exception ex) { Log.Error("[reactor] note editor failed", ex); }
        }

        /// <summary>A panel's annotation (input: the upstream port's; output: the downstream port's,
        /// else the reactor's own output note), or null.</summary>
        private static Annotation PanelAnnotation(bool input, int index)
        {
            var rd = Editor?.reactorDraggable_0;
            if (rd == null) return null;
            if (input)
            {
                if (!rd.class485_0.ContainsKey(index)) return null;
                var port = rd.class485_0[index];
                return port.vmethod_0()?.class485_1.method_4(port.pipeDraggable_0)?.method_0();
            }
            if (!rd.class485_1.ContainsKey(index)) return null;
            var outPort = rd.class485_1[index];
            return outPort.vmethod_0()?.class485_0.method_4(outPort.pipeDraggable_0)?.method_0() ?? outPort.method_0();
        }

        /// <summary>Where input <paramref name="index"/>'s zone starts in the reactor — the offset
        /// InputInstruction.vmethod_7 adds to the molecule's own positions: α at the corner, β four
        /// rows down, or six columns across on a Class80 reactor.</summary>
        private static Vector2i InputLanding(int index)
            => Editor is Class80 ? new Vector2i(index * 6, 0) : new Vector2i(0, index * 4);

        /// <summary>Enter on a panel row: its molecule in the mini-grid viewer (a chooser first when
        /// the panel lists several). Inputs pass their zone's landing offset.</summary>
        private void OpenMolecules(Annotation a, string key, Vector2i? landing = null) => OpenMolecules(this, a, key, landing);

        /// <summary>Open an annotation's molecules on <paramref name="owner"/>: one straight into the
        /// viewer, several through a chooser first (shared with the pipeline's Molecules stop).</summary>
        internal static void OpenMolecules(Screen owner, Annotation a, string key, Vector2i? landing = null)
        {
            var molecules = new List<Molecule>();
            if (a != null)
                foreach (var m in a.vmethod_6())
                    if (m != null && !m.method_6()) molecules.Add(m);
            if (molecules.Count == 0) return;
            if (molecules.Count == 1)
            {
                owner.PushChild(new MoleculeViewerScreen(key + ".view", molecules[0], landing));
                return;
            }
            var items = new List<ActionListScreen.Item>();
            foreach (var m in molecules)
            {
                var molecule = m;
                items.Add(new ActionListScreen.Item
                {
                    Label = () => MoleculeText.NameAndFormula(molecule),
                    Run = () => owner.PushChild(new MoleculeViewerScreen(key + ".view", molecule, landing)),
                });
            }
            owner.PushChild(new ActionListScreen(key + ".choose", null, items));
        }

        private static string InputLine(int index)
        {
            var rd = Editor?.reactorDraggable_0;
            if (rd == null || !rd.class485_0.ContainsKey(index)) return null;
            string zone = Loc.T(index == 0 ? "zone.alpha" : "zone.beta");
            var port = rd.class485_0[index];
            var upstream = port.vmethod_0();
            var annotation = upstream?.class485_1.method_4(port.pipeDraggable_0)?.method_0();
            // Fed by another reactor with no note on that output: nothing to show, so say where it
            // comes from, as the pipeline does (user request).
            if (upstream is ReactorDraggable && !AnyMolecule(annotation))
            {
                string line = zone + ": " + Loc.T("reactor.mol.from", new { from = PipelineText.Name(rd.pipeline_0, upstream) + " " + SourceOutput(upstream, port.pipeDraggable_0) });
                var waiting = WaitingMolecule(index);
                return waiting == null ? line : line + ", " + Loc.T("reactor.mol.waiting", new { molecule = MoleculeText.NameAndFormula(waiting) });
            }
            return zone + ": " + AnnotationText(annotation);
        }

        /// <summary>The output of <paramref name="source"/> whose pipe is <paramref name="pipe"/>, by
        /// name ("psi output").</summary>
        private static string SourceOutput(Draggable source, PipeDraggable pipe)
        {
            int i = 0;
            foreach (var kv in source.class485_1)
            {
                if (kv.Value.pipeDraggable_0 == pipe) return Pipeline.PipelineEditorScreen.OutputName(source, i);
                i++;
            }
            return Loc.T("pipeline.output");
        }

        /// <summary>A research output the level switches off (the panel's "This output is disabled.").</summary>
        private static bool OutputDisabled(int index)
        {
            var rd = Editor?.reactorDraggable_0;
            if (rd == null || !rd.class485_1.ContainsKey(index)) return false;
            return rd.class485_1[index].vmethod_0() is Class582 research && research.method_15();
        }

        /// <summary>M on the grid: the molecule of the zone under the cursor, opened exactly like
        /// Enter on its Molecules line (inputs in landing mode). Nothing outside a zone; a disabled
        /// output or a zone without a molecule speaks its Molecules line instead (user request
        /// 2026-10-01: silence read as broken). Closing returns to the cell.</summary>
        private void OpenZoneMolecules()
        {
            if (!OnGrid) return;
            bool input;
            int index;
            if (!ZoneOf(Model, _cursorX, _cursorY, out input, out index)) return;
            var rd = Editor?.reactorDraggable_0;
            if (rd == null) return;
            // A panel with no molecule (an output piped to another reactor with no note, a disabled
            // output, an unfed input) has nothing to open: say its line instead of nothing.
            if (input)
            {
                if (!rd.class485_0.ContainsKey(index)) return;
                OpenInput(index);
            }
            else
            {
                if (!rd.class485_1.ContainsKey(index)) return;
                var a = PanelAnnotation(false, index);
                if (OutputDisabled(index) || !AnyMolecule(a)) { Speech.Tts.Speak(OutputLine(index), interrupt: true); return; }
                OpenMolecules(a, "reactor.mol.out." + index);
            }
        }

        private static bool AnyMolecule(Annotation a)
        {
            if (a == null) return false;
            foreach (var m in a.vmethod_6())
                if (m != null && !m.method_6()) return true;
            return false;
        }

        /// <summary>Enter on an input's Molecules line, or M on its zone: the panel's molecules in
        /// landing mode; for an input fed by another reactor (no panel of its own), the molecule
        /// waiting at its pipe's end — the one the next "in" takes (ReactorDraggable.method_17:
        /// the pipe's last slot), drawn in the pipe during a run; else the line itself.</summary>
        private void OpenInput(int index)
        {
            var a = PanelAnnotation(true, index);
            if (AnyMolecule(a)) { OpenMolecules(a, "reactor.mol.in." + index, InputLanding(index)); return; }
            var waiting = WaitingMolecule(index);
            if (waiting != null) { PushChild(new MoleculeViewerScreen("reactor.mol.in." + index + ".view", waiting, InputLanding(index))); return; }
            Speech.Tts.Speak(InputLine(index), interrupt: true);
        }

        /// <summary>The molecule at the end of input <paramref name="index"/>'s pipe when another
        /// reactor feeds it, or null (none waiting, or not fed by a reactor).</summary>
        private static Molecule WaitingMolecule(int index)
        {
            try
            {
                var rd = Editor?.reactorDraggable_0;
                if (rd == null || !rd.class485_0.ContainsKey(index)) return null;
                var port = rd.class485_0[index];
                if (!(port.vmethod_0() is ReactorDraggable)) return null;
                var slots = port.pipeDraggable_0?.linkedList_1;
                if (slots == null || slots.Count == 0) return null;
                var m = slots.Last.Value.molecule_0;
                return m == null || m.method_6() ? null : m;
            }
            catch { return null; }
        }

        private static string OutputLine(int index)
        {
            var rd = Editor?.reactorDraggable_0;
            if (rd == null || !rd.class485_1.ContainsKey(index)) return null;
            string zone = Loc.T(index == 0 ? "zone.psi" : "zone.omega");
            var port = rd.class485_1[index];
            var downstream = port.vmethod_0();
            if (OutputDisabled(index))
                return zone + ": " + GameText.T("This output") + " " + GameText.T("is disabled.");
            var annotation = downstream?.class485_0.method_4(port.pipeDraggable_0)?.method_0() ?? port.method_0();
            // Piped into another reactor and no note: say where it goes (user request).
            if (downstream is ReactorDraggable && !AnyMolecule(annotation))
                return zone + ": " + Loc.T("reactor.mol.to", new { to = PipelineText.Name(rd.pipeline_0, downstream) + " " + Pipeline.PipelineEditorScreen.TargetInput(downstream, port.pipeDraggable_0) });
            return zone + ": " + AnnotationText(annotation);
        }

        /// <summary>What a panel annotation shows: input molecules with their percentages, an
        /// output's molecule with "produced of required", or a reactor output note's molecules.</summary>
        internal static string AnnotationText(Annotation a)
        {
            if (a is InputAnnotation input)
            {
                var parts = new List<string>();
                foreach (var kv in input.list_0)
                    parts.Add(Loc.T("reactor.mol.input", new { molecule = MoleculeText.NameAndFormula(kv.Key), percent = (int)(kv.Value * 100.0) }));
                if (parts.Count > 0) return string.Join("; ", parts.ToArray());
            }
            else if (a is Class562 output && output.draggable_0 is Class578 counter)
            {
                foreach (var kv in counter)
                    return Loc.T("reactor.mol.output", new { molecule = MoleculeText.NameAndFormula(kv.Key), done = kv.Value.int_0, required = kv.Value.int_1 });
            }
            else if (a != null)
            {
                var names = MoleculeText.JoinNames(a.vmethod_6());
                // A defense building's callout (CustomDraggableAnnotation) adds its hint text
                // ("Fill with Methane to detonate.") where the game draws it: beside the one
                // building the level made it visible on (bool_1), while stopped.
                if (a is CustomDraggableAnnotation custom && custom.bool_1 && (int)Class258.smethod_16() == 0
                    && custom.draggable_0 is Class598 special)
                {
                    var hints = new List<string>();
                    foreach (var accepted in special.list_0)
                    {
                        string hint = GameText.Speech(accepted.string_0);
                        if (!string.IsNullOrEmpty(hint) && !hints.Contains(hint)) hints.Add(hint);
                    }
                    if (hints.Count > 0) names = (string.IsNullOrEmpty(names) ? "" : names + ", ") + string.Join(" ", hints.ToArray());
                }
                if (!string.IsNullOrEmpty(names)) return names;
            }
            return Loc.T("reactor.mol.none");
        }

        // ---- tutorial: the game shows the FIRST scripted step the reactor doesn't satisfy yet
        // (Class77.method_10 over Class424.smethod_2(): a step is met while the expected piece sits
        // in its cell and layer — Class77.method_9 matches type, variant and direction). There is
        // no step counter; the stop and the announcements recompute it the same way. ----

        private object _lastStep;

        /// <summary>The active step, or null (no tutorial, or all placement steps met).</summary>
        internal static Class288 ActiveStep(Class77 editor)
        {
            var r = editor?.reactor_0;
            if (r == null) return null;
            try
            {
                foreach (Class288 step in Class424.smethod_2())
                {
                    var m = r.method_17(step.reactorBin_0);
                    if (step.bool_0 || m == null || !editor.method_9(m, step.reactorMember_0)) return step;
                }
            }
            catch { }
            return null;
        }

        private static string StepText(Class288 step)
        {
            if (step == null) return null;
            string text = GameText.Speech(step.string_0);
            if (step.bool_0 || step.reactorMember_0 == null) return text;
            // The two things the box points at, read separately (user rule): the pictured piece
            // (method_11 draws it under the text, in the layer's colour) and the highlighted cell.
            var cell = step.reactorBin_0.vector2i_0;
            string square = Loc.T("tutorial.square", new { cell = Loc.T("reactor.cell", new { x = cell.int_0 + 1, y = cell.int_1 + 1 }) });
            if (!(step.reactorMember_0 is Instruction i))
                return text + " " + Loc.T("tutorial.shown", new
                {
                    // Hardware keeps its name per piece (the tooltip title / ReactorFeature.string_0),
                    // not in the static field GameName reads — which left "Shown: ." for a Bonder.
                    what = step.reactorMember_0 is ReactorFeature f ? ReactorText.FeatureLabel(f) : ReactorText.GameName(step.reactorMember_0.GetType()),
                }) + " " + square;

            int layer = (int)step.reactorBin_0.enum114_0;
            string colour = (layer & (ReactorText.Red | ReactorText.RedArrow)) != 0 ? Loc.T("reactor.red")
                : (layer & (ReactorText.Blue | ReactorText.BlueArrow)) != 0 ? Loc.T("reactor.blue") : null;
            string label = ReactorText.Label(i);
            string name = ReactorText.GameName(i.GetType()) ?? label; // the game's name ("Input Molecule")
            var parts = new List<string> { colour == null ? name : colour + " " + name };
            if (label != name) parts.Add(label);                      // the icon's own label ("in alpha")
            int key = SlotKeyFor(i);
            if (key >= 0) parts.Add(Loc.T("tutorial.key", new { key = KeyLetter(key) }));
            return text + " " + Loc.T("tutorial.instruction", new { instruction = string.Join(", ", parts.ToArray()) }) + " " + square;
        }

        private bool IsTutorialTarget(int x, int y)
        {
            var step = ActiveStep(Editor);
            if (step == null || step.bool_0) return false;
            var c = step.reactorBin_0.vector2i_0;
            return c.int_0 == x && c.int_1 == y;
        }

        private void BuildTutorial(GraphBuilder b, Class77 editor)
        {
            if (ActiveStep(editor) == null) return;
            b.BeginStop(TutorialStop);
            b.AddItem(ControlId.Structural("reactor.tutorial.step"), ProfileUi.Text(true, () => StepText(ActiveStep(Editor))));
        }

        /// <summary>Speak the step whenever the active one changes (arrival, a step met, an earlier
        /// step undone).</summary>
        private void WatchTutorial(Class77 editor)
        {
            var step = ActiveStep(editor);
            if (ReferenceEquals(step, _lastStep)) return;
            _lastStep = step;
            if (step != null) Speech.Tts.Speak(StepText(step));
        }

        private void RepeatTutorial()
        {
            var step = ActiveStep(Editor);
            if (step != null) Speech.Tts.Speak(StepText(step), interrupt: true); // no step: silent (user rule)
        }
    }
}
