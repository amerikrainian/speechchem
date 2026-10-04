using System;
using System.Collections.Generic;
using HarmonyLib;
using Impeller;
using SpaceChem;
using SpaceChem.Pipeline;
using SpaceChem.Reactor;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.UI;

namespace SpeechChem.Patches
{
    /// <summary>
    /// Run events — only ones the game itself has (user rule): what each waldo's instruction did
    /// this cycle, molecules leaving through outputs (with the output's progress), reaction errors,
    /// invalid molecules, completion, and run-state changes. Everything goes into the run log (a
    /// virtually limitless GroupedLog keyed by cycle, cleared when a stopped reactor starts);
    /// events are SPOKEN only at the slowest speed (user rule — faster runs would be a flood), run
    /// state changes always (they answer the user's own key).
    ///
    /// Seams (all first-tick armed):
    ///   Class188.method_3  the per-cycle step of one waldo: runs the non-arrow instruction under it
    ///                      (vmethod_7), then the arrow. Prefix snapshots the waldo (held molecule,
    ///                      waiting / sync flags, heading) and the reactor's molecule count; postfix
    ///                      diffs → "red: in alpha, took Oxygen", "red: grabbed Oxygen",
    ///                      "red: sync, waiting", "red: heading down" (a turn, whatever made it).
    ///                      A wait is reported once when it starts, not every cycle it lasts; so is
    ///                      a rotation (two cycles: "red: rotate clockwise, rotated Oxygen").
    ///   Class188.method_4  the move: a waldo that should move but stays put is at the wall →
    ///                      "red: hit the wall at 10, 4", once until it moves again.
    ///   Class578.vmethod_11  an output consuming molecules: counter (Class503.int_0) diff → "Research
    ///                      Output ψ: Oxygen, 3 of 10".
    ///   GoalTracker.smethod_12  "Reaction Error" (message); Draggable.method_7 an invalid molecule;
    ///   GoalTracker.smethod_7  all outputs complete.
    ///   Class258.smethod_17 / smethod_15  run state and speed changes.
    /// </summary>
    internal static class RunCapture
    {
        public const int MaxEntries = 10000000; // insurance only (see GroupedLog)

        public static readonly GroupedLog<int> Log = new GroupedLog<int>(MaxEntries);

        /// <summary>Bumped whenever the store is cleared, so views can reset their window.</summary>
        public static int Generation { get; private set; }

        public static void Apply(Harmony harmony)
        {
            try
            {
                var self = typeof(RunCapture);
                harmony.Patch(Expr.MethodOf(() => default(Class188).method_3()),
                    prefix: new HarmonyMethod(self, nameof(BeforeWaldo)), postfix: new HarmonyMethod(self, nameof(AfterWaldo)));
                harmony.Patch(Expr.MethodOf(() => default(Class188).method_4()),
                    prefix: new HarmonyMethod(self, nameof(BeforeMove)), postfix: new HarmonyMethod(self, nameof(AfterMove)));
                harmony.Patch(Expr.OverrideOf(typeof(Class578), Expr.MethodOf(() => default(Class578).vmethod_11())),
                    prefix: new HarmonyMethod(self, nameof(BeforeOutput)), postfix: new HarmonyMethod(self, nameof(AfterOutput)));
                harmony.Patch(Expr.MethodOf(() => GoalTracker.smethod_12(default(Struct116<Class77>), null, default(Struct116<Vector2i>), null)),
                    postfix: new HarmonyMethod(self, nameof(AfterReactionError)));
                harmony.Patch(Expr.MethodOf(() => default(Draggable).method_7(null, null, default(Vector2i))),
                    postfix: new HarmonyMethod(self, nameof(AfterInvalidMolecule)));
                harmony.Patch(Expr.MethodOf(() => GoalTracker.smethod_7(false)),
                    prefix: new HarmonyMethod(self, nameof(BeforeComplete)), postfix: new HarmonyMethod(self, nameof(AfterComplete)));
                harmony.Patch(Expr.MethodOf(() => Class258.smethod_17(default(Enum16))),
                    prefix: new HarmonyMethod(self, nameof(BeforeState)), postfix: new HarmonyMethod(self, nameof(AfterState)));
                harmony.Patch(Expr.MethodOf(() => Class53.smethod_8(false, false, false)),
                    prefix: new HarmonyMethod(self, nameof(BeforeLeave)), finalizer: new HarmonyMethod(self, nameof(AfterLeave)));
                harmony.Patch(Expr.MethodOf(() => Class53.smethod_9()),
                    prefix: new HarmonyMethod(self, nameof(BeforeLeave)), finalizer: new HarmonyMethod(self, nameof(AfterLeave)));
                harmony.Patch(Expr.MethodOf(() => Class258.smethod_15(default(SimulatorSpeed))),
                    prefix: new HarmonyMethod(self, nameof(BeforeSpeed)), postfix: new HarmonyMethod(self, nameof(AfterSpeed)));
                SpeechChem.Log.Info("[patch] run capture armed");
            }
            catch (Exception ex) { SpeechChem.Log.Error("[patch] run capture failed to apply", ex); }
        }

        private static int Cycle => Class258.int_1;

        // The log belongs to one level (the Class83 level screen instance): opening another level,
        // or the same one again, starts it empty instead of showing the last level's run until the
        // first Play (user report). Switching reactors inside a level keeps it — it covers the
        // whole pipeline.
        private static object _level;

        /// <summary>FrameLoop step: clear the log when a different level instance is open.</summary>
        public static void SyncLevel()
        {
            try
            {
                var level = Class53.smethod_5<Class83>();
                if (level == null || ReferenceEquals(level, _level)) return;
                _level = level;
                Log.Clear();
                Generation++;
                Blocked.Clear();
            }
            catch { }
        }

        // Spoken at the slowest speed, and during a step at any speed (they queue after the step's
        // "Cycle N") — inside a reactor only the events that concern it (user rule 2026-10-03),
        // except during an all-events step (Ctrl+0).
        private static bool SpeakEvents(bool concerns) => StepControl.Active
            ? concerns || !StepControl.OwnOnly
            : concerns && (int)Class258.smethod_16() == 1 && Class258.smethod_14() == SimulatorSpeed.Slow;

        private static void Add(string text, bool speak) => Add(text, speak, null, concerns: true);

        /// <summary>A level-wide event (defense: the enemy, special buildings, the Control
        /// Center) — it concerns every reactor, so it ends any step and is spoken inside any.</summary>
        internal static void AddLevelEvent(string text) => Add(text, speak: true, spoken: null, concerns: true);

        /// <summary>Log <paramref name="text"/> (always, whoever it concerns); speak
        /// <paramref name="spoken"/> when given (a waldo event of the reactor being edited drops
        /// its "reactor 2, " — user rule), else the logged text. <paramref name="concerns"/>: the
        /// event concerns the open reactor (always true with none open) — it may end a step, and
        /// only such events are spoken inside a reactor.</summary>
        private static void Add(string text, bool speak, string spoken, bool concerns)
        {
            if (string.IsNullOrEmpty(text)) return;
            Log.Add(Cycle, text);
            StepControl.OnEvent(Cycle, concerns); // a step ends with this event's cycle ("Cycle N" first)
            if (speak && SpeakEvents(concerns)) Speech.Tts.Speak(spoken ?? text);
        }

        /// <summary>A waldo event: logged with its reactor's number (several reactors), spoken
        /// without it when that reactor is the one open (user rule 2026-10-01).</summary>
        private static void AddWaldo(Class188 w, string text)
        {
            string colour = Loc.T((int)w.enum114_0 == ReactorText.Red ? "reactor.red" : "reactor.blue");
            string full = WaldoName(w) + ": " + text;
            Class77 open = null;
            try { open = Class53.smethod_5<Class77>(); } catch { }
            bool here = open != null && ReferenceEquals(open.reactor_0, w.reactor_0);
            Add(full, speak: true, spoken: here ? colour + ": " + text : null, concerns: open == null || here);
        }

        /// <summary>An event of building <paramref name="d"/> concerns the open reactor when the
        /// reactor feeds it straight through a pipe, when the level has one reactor, or when no
        /// reactor is open.</summary>
        private static bool ConcernsOpen(Draggable d)
        {
            try
            {
                var editor = Class53.smethod_5<Class77>();
                if (editor == null || d == null) return true;
                foreach (var kv in d.class485_0)
                    if (kv.Value.vmethod_0() is ReactorDraggable rd && rd.class77_0?.reactor_0 == editor.reactor_0) return true;
                var p = editor.reactorDraggable_0?.pipeline_0;
                if (p == null) return true;
                int reactors = 0;
                foreach (var c in p.dictionary_1.Keys) if (c is ReactorDraggable) reactors++;
                return reactors <= 1;
            }
            catch { return true; }
        }

        // ---- waldo steps ----

        private struct WaldoState
        {
            public MoleculeSheet Held;
            public bool Waiting;
            public bool Sync;
            public bool Rotating;
            public Vector2i Heading;
            public int Molecules;
            public Instruction Instruction;
            public BondBoard Bonds; // only under a bond instruction
            public List<LaserShot> Lasers; // only under a laser instruction
            public List<TunnelEnd> Tunnels; // only under a swap instruction
        }

        /// <summary>A quantum tunnel's cell before a swap: its atom (name) and whether it was bonded.</summary>
        private struct TunnelEnd
        {
            public Vector2i Cell;
            public string Atom;
            public bool Bonded;
        }

        /// <summary>One laser's cells before its instruction ran: the laser (top-left = its left
        /// cell) and the element on the cell whose atom changes when it works.</summary>
        private struct LaserShot
        {
            public Vector2i Left;
            public Element? Before;
        }

        private static void BeforeWaldo(Class188 __instance, out WaldoState __state)
        {
            __state = default(WaldoState);
            try
            {
                var r = __instance.reactor_0;
                if (r == null) return;
                var cell = __instance.method_0();
                __state.Held = __instance.moleculeSheet_0;
                __state.Waiting = __instance.bool_0;
                __state.Sync = __instance.bool_4;
                __state.Rotating = __instance.bool_3;
                __state.Heading = __instance.vector2i_1;
                __state.Molecules = r.class201_0.Count;
                __state.Instruction = r.method_15(cell, __instance.enum114_0) as Instruction;
                if (__state.Instruction is BondInstruction) __state.Bonds = BondBoard.Of(r);
                if (__state.Instruction is Class662) __state.Lasers = Shots<Class672>(r, right: true);
                if (__state.Instruction is Class664) __state.Lasers = Shots<Class667>(r, right: false);
                if (__state.Instruction is Class666) __state.Tunnels = TunnelEnds(r);
            }
            catch { }
        }

        private static void AfterWaldo(Class188 __instance, WaldoState __state)
        {
            try
            {
                if (GoalTracker.bool_0) return; // the step is skipped once the level is complete
                var r = __instance.reactor_0;
                if (r == null) return;
                bool turned = __instance.vector2i_1 != __state.Heading;
                var i = __state.Instruction;
                bool nonArrowTurned = false;
                if (i != null && !(i is StartInstruction))
                {
                    string text = InstructionEffect(__instance, __state, i, ref nonArrowTurned, turned);
                    if (text != null) AddWaldo(__instance, text);
                }
                // A turn the instruction above didn't already report (an arrow): the new heading.
                // An arrow the waldo already follows changes nothing and logs nothing.
                if (turned && !nonArrowTurned)
                    AddWaldo(__instance, Loc.T("run.heading", new { dir = Heading(__instance.vector2i_1) }));
            }
            catch (Exception ex) { SpeechChem.Log.Error("[run] waldo step capture", ex); }
        }

        // ---- wall stops: Class188.method_4 moves the waldo one cell along its heading unless it is
        // waiting (bool_0), syncing (bool_4) or rotating (bool_3); method_1 clamps the new cell to
        // the grid, so a move that leaves the waldo where it was is the wall. Reported once when it
        // starts; the waldo counts as blocked until it moves again. ----

        private static readonly HashSet<Class188> Blocked = new HashSet<Class188>();

        /// <summary>Whether the waldo last tried to move and was held by the wall (until it moves).</summary>
        internal static bool AtWall(Class188 waldo) => waldo != null && Blocked.Contains(waldo);

        private struct MoveState
        {
            public bool Moving;
            public Vector2i Cell;
        }

        private static void BeforeMove(Class188 __instance, out MoveState __state)
        {
            __state = default(MoveState);
            try
            {
                var w = __instance;
                __state.Cell = w.method_0();
                __state.Moving = !GoalTracker.bool_0 && !w.bool_4 && !w.bool_0 && !w.bool_3
                    && (w.vector2i_1.int_0 != 0 || w.vector2i_1.int_1 != 0);
            }
            catch { }
        }

        private static void AfterMove(Class188 __instance, MoveState __state)
        {
            try
            {
                var cell = __instance.method_0();
                if (cell != __state.Cell) { Blocked.Remove(__instance); return; }
                if (!__state.Moving || !Blocked.Add(__instance)) return;
                AddWaldo(__instance, Loc.T("run.wall", new
                {
                    cell = Loc.T("reactor.cell", new { x = cell.int_0 + 1, y = cell.int_1 + 1 }),
                }));
            }
            catch (Exception ex) { SpeechChem.Log.Error("[run] waldo move capture", ex); }
        }

        /// <summary>What the non-arrow instruction did, or null when nothing new happened (a wait that
        /// was already reported).</summary>
        private static string InstructionEffect(Class188 w, WaldoState before, Instruction i, ref bool redirected, bool turned)
        {
            string label = ReactorText.Label(i);
            var r = w.reactor_0;
            if (i is InputInstruction)
            {
                if (w.bool_0 && w.bool_2)
                    return before.Waiting ? null : Loc.T("run.waiting", new { what = label });
                if (r.class201_0.Count > before.Molecules)
                {
                    // The input drops the molecule into its zone; no waldo touches it. Where it
                    // landed = its first atom in reading order.
                    var arrived = r.class201_0[r.class201_0.Count - 1];
                    return Loc.T("run.input", new { what = label, molecule = MoleculeText.NameAndFormula(arrived.molecule_0), cell = CellText(FirstCell(arrived)) });
                }
                return label;
            }
            if (i is OutputInstruction)
            {
                if (w.bool_0 && !w.bool_2)
                    return before.Waiting ? null : Loc.T("run.waiting", new { what = label });
                return label;
            }
            if (i is GrabInstruction)
            {
                // What happened, not the instruction's name (user rule): GrabInstruction.vmethod_7 —
                // grab/drop toggles (method_8), grab (method_9) keeps a held molecule, drop
                // (method_10) releases.
                var after = w.moleculeSheet_0;
                if (before.Held == null && after != null)
                    return Loc.T("run.grabbed", new { molecule = MoleculeText.NameAndFormula(after.molecule_0) });
                if (before.Held != null && after == null)
                    return Loc.T("run.dropped", new { molecule = MoleculeText.NameAndFormula(before.Held.molecule_0) });
                if (before.Held != null)
                    return Loc.T("run.holding", new { molecule = MoleculeText.NameAndFormula(before.Held.molecule_0) });
                return Loc.T(i.method_3() == 2 ? "run.drop.none" : "run.grab.none");
            }
            if (i is BondInstruction && before.Bonds != null)
                return BondEffect(r, before.Bonds, i.method_3() == 0);
            if (i is RotateInstruction)
            {
                // Two cycles (RotateInstruction.vmethod_7 toggles bool_3): the first sets it and the
                // held molecule turns while the waldo stays; the second only clears it and the waldo
                // moves on — reported once, when it starts. Empty-handed it never sets.
                if (before.Rotating) return null;
                if (w.bool_3 && w.moleculeSheet_0 != null)
                    return Loc.T("run.rotated", new { what = label, molecule = MoleculeText.NameAndFormula(w.moleculeSheet_0.molecule_0) });
                return Loc.T("run.rotate.none");
            }
            if (i is Class663)
            {
                if (w.bool_4) return before.Sync ? null : Loc.T("run.waiting", new { what = label });
                return label;
            }
            if (i is Class662 && before.Lasers != null) return FusionEffect(r, before.Lasers);
            if (i is Class664 && before.Lasers != null) return FissionEffect(r, before.Lasers);
            if (i is Class666) return SwapEffect(before.Tunnels);
            if (i is SensorInstruction sensor)
            {
                // SensorInstruction.vmethod_7 branches when any sensor has the trigger element above
                // it (method_10) and otherwise does nothing — so a match while already heading that
                // way and a miss look alike on the waldo; say which it was.
                redirected = true;
                if (sensor.method_10())
                    return Loc.T("run.sensed", new { atom = sensor.method_8().method_0(), dir = Heading(w.vector2i_1) });
                return Loc.T("run.sensed.miss", new { atom = SensedAtom(r) ?? Loc.T("run.sensed.nothing") });
            }
            if (i is ToggleInstruction flip)
            {
                // ToggleInstruction.vmethod_7 alternates: with bool_3 set it branches and clears it,
                // otherwise it only sets it — so after the step a clear bool_3 means it branched.
                redirected = true;
                return flip.bool_3 ? Loc.T("run.flipflop.pass") : Loc.T("run.flipflop.branch", new { dir = Heading(w.vector2i_1) });
            }
            if (turned && i.vmethod_5() != Enum153.None)
            {
                redirected = true;
                return Loc.T("run.turned", new { what = label, dir = Heading(w.vector2i_1) });
            }
            return label;
        }

        // ---- lasers: the instruction fires every laser of its kind in the reactor. Fusion
        // (Class672.method_7): projectile atom on the left cell, target on the right; when both are
        // there and their atomic numbers sum to 109 or less, the projectile is removed and the target
        // becomes the sum — otherwise nothing happens. So the target cell's element is diffed. ----

        private static List<LaserShot> Shots<T>(SpaceChem.Reactor.Reactor r, bool right) where T : ReactorFeature
        {
            var shots = new List<LaserShot>();
            foreach (var member in r.method_0())
            {
                if (!(member is T laser)) continue;
                var at = r.method_19(laser);
                if (!at.HasValue) continue;
                var left = at.Value.vector2i_0;
                var cell = right ? new Vector2i(left.int_0 + 1, left.int_1) : left;
                shots.Add(new LaserShot { Left = left, Before = AtomAt(r, cell)?.element_0 });
            }
            return shots;
        }

        private static string FusionEffect(SpaceChem.Reactor.Reactor r, List<LaserShot> shots)
        {
            var parts = new List<string>();
            foreach (var shot in shots)
            {
                var target = new Vector2i(shot.Left.int_0 + 1, shot.Left.int_1);
                var now = AtomAt(r, target);
                if (now == null || !shot.Before.HasValue || now.Value.element_0 == shot.Before.Value) continue;
                parts.Add(Loc.T("run.fusion", new { cell = CellText(target), atom = now.Value.method_0() }));
            }
            return parts.Count > 0 ? string.Join("; ", parts.ToArray()) : Loc.T("run.fusion.none");
        }

        // Fission (Class667.method_7): the target atom is on the laser's LEFT cell; unless it is
        // hydrogen it becomes the upper half of its atomic number and a new lone atom of the lower
        // half appears on the right cell. So the left cell's element is diffed.
        private static string FissionEffect(SpaceChem.Reactor.Reactor r, List<LaserShot> shots)
        {
            var parts = new List<string>();
            foreach (var shot in shots)
            {
                var now = AtomAt(r, shot.Left);
                if (now == null || !shot.Before.HasValue || now.Value.element_0 == shot.Before.Value) continue;
                var split = AtomAt(r, new Vector2i(shot.Left.int_0 + 1, shot.Left.int_1));
                parts.Add(Loc.T("run.fission", new
                {
                    cell = CellText(shot.Left),
                    a = now.Value.method_0(),
                    b = split?.method_0() ?? "?",
                }));
            }
            return parts.Count > 0 ? string.Join("; ", parts.ToArray()) : Loc.T("run.fission.none");
        }

        // Swap (Class666 → Class671.smethod_1): only with exactly two tunnels; each tunnel's atom is
        // cut from its molecule (all its bonds broken) and moved to the other tunnel. ----

        private static List<TunnelEnd> TunnelEnds(SpaceChem.Reactor.Reactor r)
        {
            var ends = new List<TunnelEnd>();
            foreach (var member in r.method_0())
            {
                if (!(member is Class671 tunnel)) continue;
                var at = r.method_19(tunnel);
                if (!at.HasValue) continue;
                var cell = at.Value.vector2i_0;
                var end = new TunnelEnd { Cell = cell, Atom = AtomAt(r, cell)?.method_0() };
                foreach (MoleculeSheet sheet in r.class201_0)
                    foreach (var bond in sheet.method_15())
                        if (bond.Key.vector2i_0 == cell || bond.Key.method_0() == cell) end.Bonded = true;
                ends.Add(end);
            }
            return ends;
        }

        private static string SwapEffect(List<TunnelEnd> ends)
        {
            var moves = new List<string>();
            if (ends != null && ends.Count == 2)
                for (int k = 0; k < 2; k++)
                {
                    var from = ends[k];
                    if (from.Atom == null) continue;
                    moves.Add(Loc.T(from.Bonded ? "run.swap.move.bonded" : "run.swap.move",
                        new { atom = from.Atom, cell = CellText(ends[1 - k].Cell) }));
                }
            return moves.Count > 0 ? Loc.T("run.swap", new { moves = string.Join("; ", moves.ToArray()) }) : Loc.T("run.swap.none");
        }

        /// <summary>A molecule's first atom cell in reading order (top row first, then left).</summary>
        private static Vector2i FirstCell(MoleculeSheet sheet)
        {
            Vector2i best = default(Vector2i);
            bool any = false;
            foreach (var kv in sheet.method_14())
                if (!any || kv.Key.int_1 < best.int_1 || (kv.Key.int_1 == best.int_1 && kv.Key.int_0 < best.int_0))
                {
                    best = kv.Key;
                    any = true;
                }
            return best;
        }

        private static string CellText(Vector2i c) => Loc.T("reactor.cell", new { x = c.int_0 + 1, y = c.int_1 + 1 });

        /// <summary>The atom on a reactor cell (any molecule), or null.</summary>
        private static Atom? AtomAt(SpaceChem.Reactor.Reactor r, Vector2i cell)
        {
            foreach (MoleculeSheet sheet in r.class201_0)
                foreach (var kv in sheet.method_14())
                    if (kv.Key.int_0 == cell.int_0 && kv.Key.int_1 == cell.int_1) return kv.Value;
            return null;
        }

        /// <summary>The atom above the reactor's sensor (Class673.method_7), by name; null when none.</summary>
        private static string SensedAtom(SpaceChem.Reactor.Reactor r)
        {
            foreach (var member in r.method_0())
                if (member is Class673 s && s.method_7() is Atom atom) return atom.method_0();
            return null;
        }

        // ---- bonds: BondInstruction.vmethod_7 → Class668.smethod_1 changes bonds immediately, pair
        // by pair over the connected bonders (Class668.smethod_0, each pair a Struct98: a cell and
        // Right / Down to its neighbour): bond plus raises the pair's bond by one (max triple) when
        // both cells hold atoms — or flashes a failure when the atoms can't take it
        // (Molecule.method_30) — and bond minus lowers it, splitting molecules at zero. So the step
        // is diffed per pair: "bonded Hydrogen at 3, 2 and Oxygen at 4, 2, single bond". ----

        /// <summary>Every bond (by cell + Right/Down, reactor coordinates) and atom on the board.</summary>
        internal sealed class BondBoard
        {
            public readonly Dictionary<long, int> Bonds = new Dictionary<long, int>();
            public readonly Dictionary<long, string> Atoms = new Dictionary<long, string>();

            public static long Cell(Vector2i c) => ((long)c.int_0 << 32) | (uint)c.int_1;
            public static long Bond(Vector2i c, bool right) => (Cell(c) << 1) | (right ? 1L : 0L);

            public static BondBoard Of(SpaceChem.Reactor.Reactor r)
            {
                var b = new BondBoard();
                foreach (MoleculeSheet sheet in r.class201_0)
                {
                    foreach (var kv in sheet.method_14()) b.Atoms[Cell(kv.Key)] = kv.Value.method_0();
                    foreach (var kv in sheet.method_15()) b.Bonds[Bond(kv.Key.vector2i_0, kv.Key.enum128_0 == Enum128.Right)] = (int)kv.Value;
                }
                return b;
            }

            public int Order(Vector2i c, bool right) { int n; return Bonds.TryGetValue(Bond(c, right), out n) ? n : 0; }
            public string Atom(Vector2i c) { string a; return Atoms.TryGetValue(Cell(c), out a) ? a : null; }
        }

        private static string BondEffect(SpaceChem.Reactor.Reactor r, BondBoard before, bool plus)
        {
            var after = BondBoard.Of(r);
            var parts = new List<string>();
            foreach (var pair in Class668.smethod_0(r, (Enum146)(plus ? 0 : 1)))
            {
                var c1 = pair.vector2i_0;
                var c2 = pair.method_0();
                bool right = pair.enum128_0 == Enum128.Right;
                string a = before.Atom(c1) ?? after.Atom(c1), b = before.Atom(c2) ?? after.Atom(c2);
                if (a == null || b == null) continue; // an empty bonder: nothing to act on
                int was = before.Order(c1, right), now = after.Order(c1, right);
                var args = new
                {
                    a, b,
                    c1 = Loc.T("reactor.cell", new { x = c1.int_0 + 1, y = c1.int_1 + 1 }),
                    c2 = Loc.T("reactor.cell", new { x = c2.int_0 + 1, y = c2.int_1 + 1 }),
                    kind = ReactorText.BondWord(now),
                };
                if (now == was)
                {
                    // Bond plus on two atoms that didn't change: the game's failure flash (no free
                    // bonds, or already triple). Bond minus with no bond there does nothing.
                    if (plus) parts.Add(Loc.T("run.bond.failed", args));
                }
                else if (was == 0) parts.Add(Loc.T("run.bonded", args));
                else if (now == 0) parts.Add(Loc.T("run.unbonded", args));
                else parts.Add(Loc.T("run.bond.now", args));
            }
            if (parts.Count == 0) return Loc.T(plus ? "run.bond.none" : "run.unbond.none");
            return string.Join("; ", parts.ToArray());
        }

        private static string Heading(Vector2i v)
        {
            if (v.int_0 > 0) return Loc.T("dir.right");
            if (v.int_0 < 0) return Loc.T("dir.left");
            if (v.int_1 > 0) return Loc.T("dir.down");
            if (v.int_1 < 0) return Loc.T("dir.up");
            return null;
        }

        /// <summary>"red" / "blue", prefixed with the reactor when the pipeline has several.</summary>
        private static string WaldoName(Class188 w)
        {
            string colour = Loc.T((int)w.enum114_0 == ReactorText.Red ? "reactor.red" : "reactor.blue");
            string reactor = ReactorName(w.reactor_0);
            return reactor == null ? colour : reactor + ", " + colour;
        }

        internal static string ReactorName(SpaceChem.Reactor.Reactor r)
        {
            try
            {
                // The pipeline screen's own numbers (Game/PipelineText: first-seen order).
                var editor = Class53.smethod_5<PipelineEditor>();
                if (editor == null || r == null) return null;
                foreach (var kv in PipelineText.Components(editor.pipeline_0))
                {
                    if (!(kv.Key is ReactorDraggable rd) || rd.class77_0 == null || rd.class77_0.reactor_0 != r) continue;
                    int n = PipelineText.Number(editor.pipeline_0, rd);
                    return n > 0 ? Loc.T("run.reactor", new { n }) : null;
                }
                return null;
            }
            catch { return null; }
        }

        // ---- outputs ----

        private static void BeforeOutput(Class578 __instance, out Dictionary<Molecule, int> __state)
        {
            __state = null;
            try
            {
                __state = new Dictionary<Molecule, int>();
                foreach (var kv in __instance.dictionary_0) if (kv.Value != null) __state[kv.Key] = kv.Value.int_0;
            }
            catch { }
        }

        private static void AfterOutput(Class578 __instance, Dictionary<Molecule, int> __state)
        {
            try
            {
                if (__state == null) return;
                foreach (var kv in __instance.dictionary_0)
                {
                    int before;
                    if (kv.Value == null || !__state.TryGetValue(kv.Key, out before) || kv.Value.int_0 <= before) continue;
                    string name = OutputLabel(__instance);
                    Add(Loc.T("run.produced", new { output = name, molecule = MoleculeText.NameAndFormula(kv.Key), done = kv.Value.int_0, required = kv.Value.int_1 }),
                        speak: true, spoken: null, concerns: ConcernsOpen(__instance));
                }
            }
            catch (Exception ex) { SpeechChem.Log.Error("[run] output capture", ex); }
        }

        /// <summary>An output building as the pipeline names it ("Cargo Freighter 2").</summary>
        private static string OutputLabel(Draggable d)
        {
            if (string.IsNullOrEmpty(d?.string_1?.Trim())) return Loc.T("run.output");
            return PipelineText.Name(d.pipeline_0, d);
        }

        // ---- failures and completion (the dialogs speak themselves; the log keeps a line) ----

        /// <summary>The error entry carries the crash snapshot: the run is paused under the box
        /// here, and closing the box stops it (which wipes the scene) — see ReactorSnapshot.</summary>
        private static void AfterReactionError(Struct116<Class77> __0, string __1, IEnumerable<Vector2i> __3)
        {
            try
            {
                Screens.Reactor.ReactorSnapshot snapshot = null;
                try
                {
                    var editor = __0.bool_0 ? __0.method_0() : Class53.smethod_5<Class77>();
                    snapshot = Screens.Reactor.ReactorSnapshot.Capture(editor?.reactor_0, __3, Cycle);
                }
                catch (Exception ex) { SpeechChem.Log.Error("[run] crash snapshot", ex); }
                // Several reactors: say which one failed, as the waldo events do ("reactor 2, ...").
                string text = GameText.T("Reaction Error") + ": " + GameText.Speech(__1);
                string reactor = null;
                try { reactor = ReactorName((__0.bool_0 ? __0.method_0() : Class53.smethod_5<Class77>())?.reactor_0); } catch { }
                Log.Add(Cycle, reactor == null ? text : reactor + ", " + text, snapshot);
            }
            catch { }
        }

        private static void AfterInvalidMolecule(Draggable __instance, Molecule __0)
        {
            try
            {
                string target = OutputLabel(__instance);
                Add(GameText.T("An invalid molecule was passed to") + " " + target + ": " + MoleculeText.NameAndFormula(__0),
                    speak: false, spoken: null, concerns: ConcernsOpen(__instance));
            }
            catch { }
        }

        private static void BeforeComplete(out bool __state)
        {
            __state = false;
            try { __state = (int)GoalTracker.enum158_0 == 0; } catch { }
        }

        private static void AfterComplete(bool __state)
        {
            // A defense level has no outputs: it is won by destroying the enemy.
            if (__state) Add(Loc.T(DefenseText.IsDefense ? "run.completed.defense" : "run.completed"), speak: false);
        }

        // ---- run state ----

        private static void BeforeState(out int __state)
        {
            __state = (int)Class258.smethod_16();
        }

        private static void AfterState(int __state)
        {
            try
            {
                int now = (int)Class258.smethod_16();
                if (now == __state) return;
                if (__state == 0 && now == 1)
                {
                    Log.Clear();
                    Generation++;
                    Blocked.Clear();
                }
                // Stopping zeroes the cycle counter first (Class258.smethod_17), so "Stopped" would
                // file under cycle 0 — the log's TOP. It belongs at the end: the last group.
                // A step's own start and pause stay out of the log and speech (it says
                // "Cycle N" itself); any other change during a step ends the step.
                if (StepControl.Quiet) return;
                if (StepControl.Active) StepControl.OnForeignStateChange();
                int key = now == 0 && !Log.IsEmpty ? Log.Groups[Log.Groups.Count - 1] : Cycle;
                Log.Add(key, Screens.Common.ProgressSection.RunState());
                // Completing the level (GoalTracker.smethod_7: enum158_0 = 1, then pause) pauses
                // the run; the completion screen that follows speaks for it (user rule).
                bool completed = now == 2 && (int)GoalTracker.enum158_0 == 1;
                if (_leaving == 0 && !completed) Speech.Tts.Speak(Screens.Common.ProgressSection.RunState());
            }
            catch { }
        }

        // ---- leaving the level: Class53.smethod_8 (back to level select — Continue after a
        // completion, the exit prompt's Yes) and smethod_9 stop the run first; that "Stopped" is
        // logged but not spoken, since the level is going away (user rule). A finalizer, so a
        // throwing exit can't leave the flag stuck. ----

        private static int _leaving;

        private static void BeforeLeave() => _leaving++;

        private static Exception AfterLeave(Exception __exception)
        {
            if (_leaving > 0) _leaving--;
            return __exception;
        }

        private static void BeforeSpeed(out KeyValuePair<int, SimulatorSpeed> __state)
        {
            __state = new KeyValuePair<int, SimulatorSpeed>((int)Class258.smethod_16(), Class258.smethod_14());
        }

        private static void AfterSpeed(KeyValuePair<int, SimulatorSpeed> __state)
        {
            try
            {
                // A speed change while running (a start is announced by the state hook).
                if (__state.Key == 1 && (int)Class258.smethod_16() == 1 && Class258.smethod_14() != __state.Value)
                    Speech.Tts.Speak(Screens.Common.ProgressSection.RunState());
            }
            catch { }
        }
    }
}
