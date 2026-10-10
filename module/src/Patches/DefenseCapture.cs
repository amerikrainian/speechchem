using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Impeller;
using SpaceChem;
using SpaceChem.Levels;
using SpaceChem.Pipeline;
using SpeechChem.Game;
using SpeechChem.Localization;
using SpeechChem.Narration;

namespace SpeechChem.Patches
{
    /// <summary>
    /// Defense levels, GENERICALLY (every enemy and special building; the only per-type pieces are
    /// the meaning tables in Game/DefenseText). Run events are Narration events (level-wide: they
    /// concern every reactor), routed by the player's settings — defaults: logged, spoken at play
    /// speeds 1-3, the per-molecule line at 1 (user, 2026-10-04):
    ///   every Class598 subclass's vmethod_23 (a special building took a molecule):
    ///       "Oxygen Tank 2 took Methane, Pressure 37 percent" (the building's run meter after it);
    ///   Class598.method_15     a special building's event: "Oxygen Tank 2 exploded" (the table) or
    ///                          "…, event 3"; for a weapon event, its effect on the enemy right
    ///                          after: a part lost ("Isambard MMD: motor destroyed, 2 of 3 motors
    ///                          intact"), "Isambard MMD hit" (health only), or "missed";
    ///   Class310.method_4      enemy damage outside such an event ("… hit"), and its destruction
    ///                          ("Isambard MMD destroyed", after the hit that caused it);
    ///   Class310.method_3      the enemy's per-cycle step: a part lost by any other cause, a
    ///                          move (its top-left cell changed: "Isambard MMD 21", "… row 5",
    ///                          "… 21, 5", "… off the map"), a change of its visible state
    ///                          (DefenseText's EnemyStates table: "Xothothor: eye open, red");
    ///   every Class310 subclass's vmethod_2 with a body: "{enemy} attacks" (its attack timer);
    ///   every Class310 subclass's vmethod_3 (its draw): the rectangles it draws through
    ///       SpriteBatch.method_8 (every sprite overload funnels there; top-left = position −
    ///       origin), minus particle effects (Class190) — the enemy's FOOTPRINT, used when the
    ///       Bodies table has no entry for it;
    ///   GoalTracker.smethod_10 damage to the Control Center: "Control Center 95 percent";
    ///   Class710.method_7      an F1-F4 toggle: spoken at once.
    /// Captures run under flags set only around the mod's own calls: a building's run meter
    /// (vmethod_19's bar Class45.smethod_0 + label Scene.smethod_4) and the graph's labelled
    /// ranges (the level's vmethod_9: Class377.smethod_1 brackets, smethod_2 labels).
    /// Overrides are patched per declaring type (Expr.OverrideOf, CLAUDE.md §17).
    /// </summary>
    internal static class DefenseCapture
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                var self = typeof(DefenseCapture);
                Type[] types;
                try { types = typeof(Class598).Assembly.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }

                var intake = Expr.MethodOf(() => default(Class598).vmethod_23(null, 0));
                var attack = Expr.MethodOf(() => default(Class310).vmethod_2(0));
                var draw = Expr.MethodOf(() => default(Class310).vmethod_3(null));
                int buildings = 0, enemies = 0;
                foreach (var t in types)
                {
                    if (t == null || t.IsAbstract) continue;
                    if (t.IsSubclassOf(typeof(Class598)) && Declares(t, intake))
                    {
                        harmony.Patch(Expr.OverrideOf(t, intake), prefix: new HarmonyMethod(self, nameof(BeforeIntake)), finalizer: new HarmonyMethod(self, nameof(AfterIntake)));
                        buildings++;
                    }
                    if (t.IsSubclassOf(typeof(Class310)))
                    {
                        if (Declares(t, draw))
                            harmony.Patch(Expr.OverrideOf(t, draw), prefix: new HarmonyMethod(self, nameof(BeforeDraw)), finalizer: new HarmonyMethod(self, nameof(AfterDraw)));
                        if (Declares(t, attack) && HasBody(Expr.OverrideOf(t, attack)))
                            harmony.Patch(Expr.OverrideOf(t, attack), postfix: new HarmonyMethod(self, nameof(AfterAttack)));
                        enemies++;
                    }
                }

                harmony.Patch(Expr.MethodOf(() => default(Class598).method_15(0, null)),
                    prefix: new HarmonyMethod(self, nameof(BeforeEvent)), finalizer: new HarmonyMethod(self, nameof(AfterEvent)));
                harmony.Patch(Expr.MethodOf(() => default(Class310).method_4(0f)),
                    prefix: new HarmonyMethod(self, nameof(BeforeEnemyDamage)), postfix: new HarmonyMethod(self, nameof(AfterEnemyDamage)));
                harmony.Patch(Expr.MethodOf(() => default(Class310).method_3()),
                    postfix: new HarmonyMethod(self, nameof(AfterEnemyCycle)));
                harmony.Patch(Expr.MethodOf(() => default(SpriteBatch).method_8(null, default(Rectangle), null, default(Struct104), default(Struct100), default(Vector2i), default(Enum86), 0f)),
                    prefix: new HarmonyMethod(self, nameof(BeforeSprite)));
                harmony.Patch(Expr.MethodOf(() => default(Class190).method_2(null, default(Vector2i), 0f)),
                    prefix: new HarmonyMethod(self, nameof(BeforeParticles)), finalizer: new HarmonyMethod(self, nameof(AfterParticles)));
                harmony.Patch(Expr.MethodOf(() => default(Class190).method_3(null)),
                    prefix: new HarmonyMethod(self, nameof(BeforeParticles)), finalizer: new HarmonyMethod(self, nameof(AfterParticles)));
                harmony.Patch(Expr.MethodOf(() => Class45.smethod_0(0f, false)),
                    prefix: new HarmonyMethod(self, nameof(BeforeBar)));
                harmony.Patch(Expr.MethodOf(() => Scene.smethod_4(null, null, default(Struct104))),
                    prefix: new HarmonyMethod(self, nameof(BeforeText)));
                harmony.Patch(Expr.MethodOf(() => Class377.smethod_1(0, 0)),
                    prefix: new HarmonyMethod(self, nameof(BeforeBracket)));
                harmony.Patch(Expr.MethodOf(() => Class377.smethod_2(null, 0, 0)),
                    prefix: new HarmonyMethod(self, nameof(BeforeGraphLabel)));
                harmony.Patch(Expr.MethodOf(() => GoalTracker.smethod_10(0)),
                    prefix: new HarmonyMethod(self, nameof(BeforeBaseDamage)), postfix: new HarmonyMethod(self, nameof(AfterBaseDamage)));
                harmony.Patch(Expr.MethodOf(() => default(Class710).method_7(default(Enum111))),
                    postfix: new HarmonyMethod(self, nameof(AfterToggle)));
                Log.Info("[patch] defense capture armed (" + buildings + " special buildings, " + enemies + " enemies)");
            }
            catch (Exception ex) { Log.Error("[patch] defense capture failed to apply", ex); }
        }

        private static bool Declares(Type t, MethodInfo virtualMethod)
        {
            try { Expr.OverrideOf(t, virtualMethod); return true; }
            catch (MissingMethodException) { return false; }
        }

        /// <summary>An override that does something (an empty body is a single ret, or nop + ret).</summary>
        private static bool HasBody(MethodInfo m)
        {
            try { var il = m.GetMethodBody()?.GetILAsByteArray(); return il != null && il.Length > 2; }
            catch { return true; }
        }

        private static DefenseLevelEditor Level => DefenseText.Level;

        private static string EnemyName => DefenseText.EnemyName(Level);

        // ---- special buildings: molecules taken, events ----

        // A building's intake is logged after it (with the meter it leaves), except when the
        // molecule sets off an event inside: then the intake line goes first, before the event's
        // (and without the meter — the event says what happened).
        private static Class598 _intakeBuilding;
        private static Molecule _intakeMoleculeObject;

        private static void BeforeIntake(Class598 __instance, Molecule __0)
        {
            try
            {
                _intakeBuilding = __instance;
                _intakeMoleculeObject = __0;
            }
            catch { }
        }

        private static Exception AfterIntake(Class598 __instance, Exception __exception)
        {
            try { if (ReferenceEquals(_intakeBuilding, __instance)) FlushIntake(withMeter: true); }
            catch (Exception ex) { Log.Error("[defense] intake", ex); }
            return __exception;
        }

        private static void FlushIntake(bool withMeter)
        {
            var b = _intakeBuilding;
            var molecule = _intakeMoleculeObject;
            _intakeBuilding = null;
            _intakeMoleculeObject = null;
            if (b == null) return;
            string meter = withMeter ? DefenseText.Meter(b) : null;
            var e = new NarrationEvent("defense.took").Part("building", PipelineText.Name(b.pipeline_0, b)).Part("action", Loc.T("defense.act.took"))
                .Molecule(molecule, meter != null ? "," : null);
            Narrator.Emit(e.Part("meter", meter));
        }

        private struct EnemySnapshot
        {
            public Class310 Enemy;
            public int Parts;
            public float Health;
            public bool Weapon;
            public bool Continuous;
        }

        // A continuous weapon (a beam raises its event every cycle while firing): one line per
        // burst, "hit" once per burst. Burst = consecutive cycles; [last cycle, hit yet].
        private static readonly ConditionalWeakTable<Class598, int[]> Bursts = new ConditionalWeakTable<Class598, int[]>();

        private static int _inEvent;
        private static NarrationEvent _pendingDestroyed;

        private static void BeforeEvent(Class598 __instance, int __0, out EnemySnapshot __state)
        {
            __state = default(EnemySnapshot);
            try
            {
                // Inside the building's intake (its 35th methane): the intake line goes first.
                if (ReferenceEquals(_intakeBuilding, __instance)) FlushIntake(withMeter: false);
                bool weapon, continuous;
                var text = DefenseText.BuildingEvent(__instance, __0, out weapon, out continuous);
                var enemy = DefenseText.Enemy(Level);
                __state = new EnemySnapshot
                {
                    Enemy = enemy,
                    Parts = DefenseText.Intact(DefenseText.PartFlags(enemy)),
                    Health = enemy?.float_0 ?? 0f,
                    Weapon = weapon,
                    Continuous = continuous,
                };
                _inEvent++;
                if (continuous)
                {
                    var burst = Bursts.GetValue(__instance, _ => new[] { int.MinValue, 0 });
                    int cycle = Class258.int_1;
                    bool same = burst[0] == cycle || burst[0] == cycle - 1;
                    burst[0] = cycle;
                    if (same) return;
                    burst[1] = 0;
                }
                Narrator.Emit(text);
            }
            catch (Exception ex) { Log.Error("[defense] building event", ex); }
        }

        private static Exception AfterEvent(Class598 __instance, Exception __exception, EnemySnapshot __state)
        {
            try
            {
                if (_inEvent > 0) _inEvent--;
                var enemy = __state.Enemy;
                if (enemy != null)
                {
                    int parts = DefenseText.Intact(DefenseText.PartFlags(enemy));
                    if (parts < __state.Parts) Narrator.Emit(PartLost(enemy));
                    else if (__state.Continuous)
                    {
                        int[] burst;
                        if (enemy.float_0 < __state.Health && __instance != null && Bursts.TryGetValue(__instance, out burst) && burst[1] == 0)
                        {
                            burst[1] = 1;
                            Narrator.Emit(Effect(EnemyName, "defense.act.hit"));
                        }
                    }
                    else if (enemy.float_0 < __state.Health) Narrator.Emit(Effect(EnemyName, "defense.act.hit"));
                    else if (__state.Weapon) Narrator.Emit(Effect(null, "defense.miss"));
                    RememberParts(enemy);
                }
                if (_pendingDestroyed != null && _inEvent == 0)
                {
                    Narrator.Emit(_pendingDestroyed);
                    _pendingDestroyed = null;
                }
            }
            catch (Exception ex) { Log.Error("[defense] building event effect", ex); }
            return __exception;
        }

        /// <summary>"Isambard MMD: motor destroyed, 2 of 3 motors intact".</summary>
        private static NarrationEvent PartLost(Class310 enemy)
            => new NarrationEvent("defense.effect.part").Part("enemy", EnemyName, ":").Part("result", DefenseText.PartLost(enemy), ",").Part("parts", DefenseText.PartsText(enemy));

        /// <summary>"Isambard MMD hit", "missed" (no enemy name: what the blast did).</summary>
        private static NarrationEvent Effect(string enemy, string resultKey)
            => new NarrationEvent(enemy == null ? "defense.effect.miss" : "defense.effect.hit").Part("enemy", enemy).Part("result", Loc.T(resultKey));

        // ---- the enemy: damage, parts, attacks ----

        private static void BeforeEnemyDamage(Class310 __instance, out KeyValuePair<bool, float> __state)
        {
            __state = new KeyValuePair<bool, float>(true, 0f);
            try { __state = new KeyValuePair<bool, float>(__instance.method_1(), __instance.float_0); } catch { }
        }

        private static void AfterEnemyDamage(Class310 __instance, KeyValuePair<bool, float> __state)
        {
            try
            {
                if (!__state.Key && __instance.method_1())
                {
                    var text = new NarrationEvent("defense.destroyed").Part("enemy", EnemyName).Part("result", Loc.T("defense.state.destroyed"));
                    if (_inEvent > 0) _pendingDestroyed = text; // after the hit that caused it
                    else Narrator.Emit(text);
                }
                else if (_inEvent == 0 && __instance.float_0 < __state.Value)
                    Narrator.Emit(Effect(EnemyName, "defense.act.hit"));
            }
            catch (Exception ex) { Log.Error("[defense] enemy damage", ex); }
        }

        private static readonly ConditionalWeakTable<Class310, int[]> LastParts = new ConditionalWeakTable<Class310, int[]>();

        private static void RememberParts(Class310 enemy)
        {
            var box = LastParts.GetValue(enemy, _ => new int[1]);
            box[0] = DefenseText.Intact(DefenseText.PartFlags(enemy));
        }

        private sealed class Watch
        {
            public int Cycle = -1;
            public Vector2i? At;
            public List<string> State;
        }

        private static readonly ConditionalWeakTable<Class310, Watch> Watches = new ConditionalWeakTable<Class310, Watch>();

        /// <summary>Per cycle (whatever draws the enemy): a part lost outside a building's event,
        /// a move ("Isambard MMD at columns 22 to 32, rows 3 to 11") and a change of its visible
        /// state ("…: eye open, red"). A new run (the cycle counter went back) re-baselines silently.</summary>
        private static void AfterEnemyCycle(Class310 __instance)
        {
            try
            {
                if (DefenseText.PartFlags(__instance) != null)
                {
                    int now = DefenseText.Intact(DefenseText.PartFlags(__instance));
                    var box = LastParts.GetValue(__instance, _ => new[] { now });
                    if (now < box[0] && _inEvent == 0) Narrator.Emit(PartLost(__instance));
                    box[0] = now;
                }
                var level = Level;
                var watch = Watches.GetValue(__instance, _ => new Watch());
                var at = DefenseText.Anchor(__instance);
                var state = DefenseText.EnemyStateParts(level, __instance);
                int cycle = Class258.int_1;
                bool fresh = watch.Cycle < 0 || cycle < watch.Cycle;
                watch.Cycle = cycle;
                if (!fresh && (int)Class258.smethod_16() == 1 && !DefenseText.Defeated(__instance))
                {
                    if (!Equals(at, watch.At))
                    {
                        var move = DefenseText.MoveEvent(EnemyName, watch.At, at);
                        if (move != null) Narrator.Emit(move);
                    }
                    // Only what changed (Narration/StateChange); a state clearing is a change too:
                    // back to "normal" (the shield coming back up on an undamaged enemy used to pass
                    // silently — user report 2026-10-06), or "… ended" while something else remains.
                    string change = StateChange.Describe(watch.State, state);
                    if (change != null)
                        Narrator.Emit(new NarrationEvent("defense.state").Part("enemy", EnemyName, ":").Part("state", change));
                }
                watch.At = at;
                watch.State = state;
            }
            catch { }
        }

        private static void AfterAttack()
        {
            try { Narrator.Emit(new NarrationEvent("defense.attack").Part("enemy", EnemyName).Part("action", Loc.T("defense.act.attacks"))); }
            catch { }
        }

        // ---- the enemy's footprint (what its own draw puts on the pipeline) ----

        private static List<Rectangle> _sprites;
        private static int _particles;
        private static readonly ConditionalWeakTable<Class310, List<Rectangle>> Footprints = new ConditionalWeakTable<Class310, List<Rectangle>>();

        /// <summary>The rectangles (pipeline pixels) of the enemy's last draw, or null.</summary>
        public static List<Rectangle> Footprint(Class310 enemy)
        {
            if (enemy == null) return null;
            List<Rectangle> rects;
            return Footprints.TryGetValue(enemy, out rects) ? rects : null;
        }

        private static void BeforeDraw(out List<Rectangle> __state)
        {
            __state = _sprites;
            _sprites = new List<Rectangle>();
        }

        private static Exception AfterDraw(Class310 __instance, List<Rectangle> __state, Exception __exception)
        {
            var rects = _sprites;
            _sprites = __state;
            try
            {
                if (rects == null) return __exception;
                Footprints.Remove(__instance);
                Footprints.Add(__instance, rects);
            }
            catch (Exception ex) { Log.Error("[defense] enemy footprint", ex); }
            return __exception;
        }

        /// <summary>method_8(texture, rect, source, colour, rotation, ORIGIN, flip, depth): the
        /// sprite is drawn with its origin at rect's position, so its top-left is position − origin
        /// (rotation ignored).</summary>
        private static void BeforeSprite(Rectangle __1, Vector2i __5)
        {
            if (_sprites != null && _particles == 0) _sprites.Add(new Rectangle(__1.vector2i_0 - __5, __1.vector2i_1));
        }

        private static void BeforeParticles() => _particles++;

        private static Exception AfterParticles(Exception __exception)
        {
            if (_particles > 0) _particles--;
            return __exception;
        }

        // ---- building run meters (the bubble's bar + label, rebuilt under capture) ----

        private static bool _meterCapture;
        private static readonly List<float> _bars = new List<float>();
        private static readonly List<string> _barLabels = new List<string>();

        /// <summary>The run meter(s) of a building — (label, fraction) per bar, in the order the
        /// bubble builds them (a launch pad draws one bar per ingredient) — or null when it draws
        /// none. Labels lose the formula font markup ("H~02" → "H2").</summary>
        public static List<KeyValuePair<string, float>> CaptureMeter(Draggable d)
        {
            _meterCapture = true;
            _bars.Clear();
            _barLabels.Clear();
            try { d.vmethod_19(); }
            catch { return null; }
            finally { _meterCapture = false; }
            if (_bars.Count == 0) return null;
            var meters = new List<KeyValuePair<string, float>>();
            for (int i = 0; i < _bars.Count; i++)
                meters.Add(new KeyValuePair<string, float>(i < _barLabels.Count ? MoleculeText.Clean(GameText.Speech(_barLabels[i])) : null, _bars[i]));
            return meters;
        }

        private static void BeforeBar(float __0)
        {
            if (_meterCapture) _bars.Add(__0);
        }

        private static void BeforeText(string __1)
        {
            if (_meterCapture) _barLabels.Add(__1);
        }

        // ---- the graph's labelled ranges (the level's vmethod_9, rebuilt under capture) ----

        internal struct GraphMark
        {
            public string Label;
            public int X0, X1;
        }

        private static List<KeyValuePair<int, int>> _graphCapture;
        private static List<KeyValuePair<string, int>> _graphLabels;

        /// <summary>The graph's labelled ranges, in the level's order, or null.</summary>
        public static List<GraphMark> CaptureGraphLabels(DefenseLevelEditor level)
        {
            _graphCapture = new List<KeyValuePair<int, int>>();
            _graphLabels = new List<KeyValuePair<string, int>>();
            List<KeyValuePair<int, int>> brackets;
            List<KeyValuePair<string, int>> labels;
            try { level.vmethod_9(); }
            catch { return null; }
            finally
            {
                brackets = _graphCapture;
                labels = _graphLabels;
                _graphCapture = null;
                _graphLabels = null;
            }
            var marks = new List<GraphMark>();
            if (brackets.Count == 0) return marks;
            // vmethod_9 builds every bracket, then every label, but not always in the same order
            // (Gorgathar's labels go S, NW, VITAL, WAIL over brackets S, VITAL, NW, WAIL). A label
            // whose x lies inside a bracket takes it; the rest take the unclaimed brackets in order
            // (labels drawn just outside their bracket: MOTORS at 30 for 45-170, OCULAR at 357 for
            // 208-338); past the last bracket, the nearest one. Verified against every defense level.
            var owner = new int[labels.Count];
            var taken = new bool[brackets.Count];
            for (int i = 0; i < labels.Count; i++)
            {
                owner[i] = -1;
                for (int b = 0; b < brackets.Count; b++)
                    if (!taken[b] && brackets[b].Key <= labels[i].Value && labels[i].Value <= brackets[b].Value)
                    {
                        owner[i] = b;
                        taken[b] = true;
                        break;
                    }
            }
            for (int i = 0; i < labels.Count; i++)
            {
                if (owner[i] >= 0) continue;
                for (int b = 0; b < brackets.Count; b++)
                    if (!taken[b])
                    {
                        owner[i] = b;
                        taken[b] = true;
                        break;
                    }
                if (owner[i] >= 0) continue;
                owner[i] = 0;
                for (int b = 0; b < brackets.Count; b++)
                    if (Math.Abs((brackets[b].Key + brackets[b].Value) / 2 - labels[i].Value) < Math.Abs((brackets[owner[i]].Key + brackets[owner[i]].Value) / 2 - labels[i].Value)) owner[i] = b;
            }
            for (int i = 0; i < labels.Count; i++)
            {
                var range = brackets[owner[i]];
                marks.Add(new GraphMark { Label = GameText.Speech(labels[i].Key), X0 = range.Key, X1 = range.Value });
            }
            return marks;
        }

        private static void BeforeBracket(int __0, int __1)
        {
            _graphCapture?.Add(new KeyValuePair<int, int>(__0, __1));
        }

        private static void BeforeGraphLabel(string __0, int __1)
        {
            _graphLabels?.Add(new KeyValuePair<string, int>(__0, __1));
        }

        // ---- the Control Center ----

        private static void BeforeBaseDamage(out int __state)
        {
            __state = GoalTracker.int_1;
        }

        private static void AfterBaseDamage(int __state)
        {
            try
            {
                int now = GoalTracker.int_1;
                if (now >= __state) return;
                string name = DefenseText.BaseName();
                Narrator.Emit(new NarrationEvent("defense.base").Part("base", name)
                    .Part("result", now <= 0 ? Loc.T("defense.state.destroyed") : Loc.T("run.percent", new { percent = now })));
            }
            catch (Exception ex) { Log.Error("[defense] base damage", ex); }
        }

        // ---- Reactor Controls (F1-F4 reach the game's own key path; say the new state) ----

        /// <summary>Set while the mod's own Reactor Controls node flips a toggle: the navigator
        /// speaks the node's new state itself.</summary>
        internal static bool QuietToggle;

        private static void AfterToggle(Enum111 __0)
        {
            try
            {
                if (QuietToggle) return;
                int i = (int)__0;
                Speech.Tts.Speak(DefenseText.ControlName(i) + ", " + DefenseText.OnOff(DefenseText.ControlOn(i)), interrupt: true);
            }
            catch { }
        }
    }
}
