using System.Collections.Generic;

namespace SpeechChem.Narration
{
    /// <summary>The registry's compact DEFAULT for when an event is spoken outside a step: never,
    /// while running at play speed 1, up to 2, up to 3, or always (every speed, and paused or
    /// stopped). The player's setting is one checkbox per moment (EventSettings.SpeaksAt).</summary>
    internal enum SpeakLevel { Off = 0, Speed1 = 1, Speed2 = 2, Speed3 = 3, Always = 4 }

    /// <summary>One named part of an event's text (an action, a molecule, a cell…). A part with
    /// variants offers a detail choice (a molecule as name, formula or both).</summary>
    internal sealed class PartDef
    {
        public string Key;
        public string[] Variants;     // null = no detail choice
        public string DefaultVariant;

        public PartDef(string key, string[] variants = null, string defaultVariant = null)
        {
            Key = key;
            Variants = variants;
            DefaultVariant = defaultVariant ?? (variants != null ? variants[0] : null);
        }
    }

    /// <summary>
    /// A node of the EVENT TREE (user request 2026-10-09: settings from general to specific).
    /// A LEAF is an event type the capture code emits: its parts in their default order (the order
    /// that reproduces the mod's wording), what it applies to, and its defaults. A BRANCH groups
    /// events ("Grab and drop", "Grab or drop failed", a whole group such as "Waldo actions" at the
    /// root); its parts are the union of its leaves'. Every setting (log, speak moments, scope,
    /// sources, formats, step keys) set on a node applies to everything under it until a node
    /// further down sets its own (EventSettings resolves leaf → root → registry default). A TAG is
    /// a cross-cutting view ("Failed or no effect" across every instruction): no inheritance, its
    /// settings write each member. Only leaves reach the runtime, so the tree costs nothing per
    /// event and nothing in the log.
    /// </summary>
    internal sealed class EventKind
    {
        public string Key;
        /// <summary>The root's key: waldo, outputs, run, defense.</summary>
        public string Group;
        public PartDef[] Parts;
        public EventKind Parent;
        public readonly List<EventKind> Children = new List<EventKind>(); // branches, leaves and tags, in page order
        /// <summary>A tag's members (leaves anywhere in the tree); null on every other node.</summary>
        public List<EventKind> Members;
        /// <summary>A waldo's event: red / blue source filters apply (a branch: any leaf under it).</summary>
        public bool Waldo;
        /// <summary>Belongs to a reactor or a building it feeds: "inside a reactor" scope applies.</summary>
        public bool ReactorScoped;
        public bool LogDefault = true;
        public SpeakLevel SpeakDefault = SpeakLevel.Speed1;
        /// <summary>The default step keys (0 and Ctrl+0) stop on it / speak it.</summary>
        public bool StepStopsDefault = true, StepSpeaksDefault = true;

        public bool IsTag => Members != null;
        public bool IsLeaf => !IsTag && Children.Count == 0;

        /// <summary>The leaves this node's settings reach: itself, everything under it, a tag's members.</summary>
        public List<EventKind> Leaves()
        {
            var list = new List<EventKind>();
            if (IsTag) list.AddRange(Members);
            else Collect(this, list);
            return list;
        }

        private static void Collect(EventKind n, List<EventKind> into)
        {
            if (n.IsTag) return;
            if (n.IsLeaf) { into.Add(n); return; }
            foreach (var c in n.Children) Collect(c, into);
        }

        /// <summary>This node and every branch and leaf under it (tags excluded).</summary>
        public List<EventKind> Subtree()
        {
            var list = new List<EventKind>();
            Walk(this, list);
            return list;
        }

        private static void Walk(EventKind n, List<EventKind> into)
        {
            if (n.IsTag) return;
            into.Add(n);
            foreach (var c in n.Children) Walk(c, into);
        }
    }

    /// <summary>The registry. Adding an event type = one entry in the tree here; its settings,
    /// pages and stored keys follow.</summary>
    internal static class EventKinds
    {
        public const string Waldo = "waldo", Outputs = "outputs", Run = "run", Defense = "defense";
        public static readonly string[] Groups = { Waldo, Outputs, Run, Defense };

        // Shared parts.
        public static readonly string[] MoleculeVariants = { "both", "name", "formula" };
        public static readonly string[] AtomsVariants = { "cells", "names" };
        public static readonly string[] ReactorVariants = { "unnamed", "named" }; // in the open reactor's own views

        private static PartDef Reactor() => new PartDef("reactor", ReactorVariants);
        private static PartDef Colour() => new PartDef("waldo");
        private static PartDef Molecule() => new PartDef("molecule", MoleculeVariants);
        private static PartDef P(string key) => new PartDef(key);

        /// <summary>A waldo's leaf: reactor and colour first.</summary>
        private static EventKind W(string key, params PartDef[] parts)
        {
            var all = new List<PartDef> { Reactor(), Colour() };
            all.AddRange(parts);
            return new EventKind { Key = key, Parts = all.ToArray(), Waldo = true, ReactorScoped = true };
        }

        private static EventKind Leaf(string key, params PartDef[] parts) => new EventKind { Key = key, Parts = parts };

        /// <summary>A branch over its children (parts, flags: derived in <see cref="Link"/>).</summary>
        private static EventKind B(string key, params EventKind[] children)
        {
            var n = new EventKind { Key = key };
            n.Children.AddRange(children);
            return n;
        }

        /// <summary>A tag: members by key, resolved once the tree is built.</summary>
        private static EventKind Tag(string key, params string[] members)
        {
            var n = new EventKind { Key = key, Members = new List<EventKind>() };
            _tagMembers[n] = members;
            return n;
        }

        private static readonly Dictionary<EventKind, string[]> _tagMembers = new Dictionary<EventKind, string[]>();

        public static readonly EventKind[] Roots = Build();

        private static EventKind[] Build()
        {
            var roots = new[]
            {
                // ---- waldo actions (spoken at speed 1, only the open reactor's inside a reactor) ----
                B(Waldo,
                    W("waldo.input", P("instruction"), Molecule(), P("place")),
                    W("waldo.wait", P("instruction"), P("state")),
                    W("waldo.instruction", P("instruction")),
                    B("waldo.grabdrop",
                        W("waldo.grab", P("action"), Molecule()),
                        W("waldo.drop", P("action"), Molecule()),
                        W("waldo.holding", P("action"), Molecule()),
                        B("waldo.grabdrop.fail",
                            W("waldo.grab.none", P("action")),
                            W("waldo.drop.none", P("action")))),
                    B("waldo.bond",
                        W("waldo.bond.made", P("action"), new PartDef("atoms", AtomsVariants), P("result")),
                        W("waldo.bond.changed", P("action"), new PartDef("atoms", AtomsVariants), P("result")),
                        W("waldo.bond.broken", P("action"), new PartDef("atoms", AtomsVariants), P("result")),
                        B("waldo.bond.fail",
                            W("waldo.bond.full", P("action"), new PartDef("atoms", AtomsVariants), P("result")),
                            W("waldo.bond.none", P("action")),
                            W("waldo.unbond.none", P("action")))),
                    B("waldo.rotate",
                        W("waldo.rotate.done", P("instruction"), P("action"), Molecule()),
                        W("waldo.rotate.none", P("action"))),
                    W("waldo.turn", P("instruction"), P("heading")),
                    W("waldo.heading", P("heading")),
                    B("waldo.sensor",
                        W("waldo.sensor.match", P("action"), P("atom"), P("heading")),
                        W("waldo.sensor.miss", P("action"), P("atom"))),
                    B("waldo.flipflop",
                        W("waldo.flipflop.branch", P("action"), P("heading")),
                        W("waldo.flipflop.pass", P("action"))),
                    B("waldo.fusion",
                        W("waldo.fusion.done", P("action"), P("place"), P("atom")),
                        W("waldo.fusion.none", P("action"))),
                    B("waldo.fission",
                        W("waldo.fission.done", P("action"), P("place"), P("atom")),
                        W("waldo.fission.none", P("action"))),
                    B("waldo.swap",
                        W("waldo.swap.done", P("action"), P("moves")),
                        W("waldo.swap.none", P("action"))),
                    W("waldo.wall", P("action"), P("place")),
                    // Every instruction that failed or did nothing, wherever it sits above.
                    Tag("waldo.noeffect", "waldo.grab.none", "waldo.drop.none", "waldo.holding", "waldo.bond.full",
                        "waldo.bond.none", "waldo.unbond.none", "waldo.rotate.none", "waldo.fusion.none",
                        "waldo.fission.none", "waldo.swap.none")),

                // ---- outputs and errors ----
                B(Outputs,
                    new EventKind { Key = "output.produced", ReactorScoped = true, Parts = new[] { P("output"), Molecule(), P("count") } },
                    new EventKind { Key = "output.invalid", ReactorScoped = true, SpeakDefault = SpeakLevel.Off, StepSpeaksDefault = false,
                        Parts = new[] { P("action"), P("target"), Molecule() } },
                    new EventKind { Key = "run.error", SpeakDefault = SpeakLevel.Off, StepStopsDefault = false, StepSpeaksDefault = false,
                        Parts = new[] { Reactor(), P("label"), P("message") } },
                    new EventKind { Key = "run.completed", SpeakDefault = SpeakLevel.Off, StepSpeaksDefault = false, Parts = new[] { P("message") } }),

                // ---- the run itself ----
                B(Run,
                    new EventKind { Key = "run.state", SpeakDefault = SpeakLevel.Always, StepStopsDefault = false, StepSpeaksDefault = false, Parts = new[] { P("state") } },
                    new EventKind { Key = "run.speed", LogDefault = false, SpeakDefault = SpeakLevel.Always, StepStopsDefault = false, StepSpeaksDefault = false,
                        Parts = new[] { P("state") } }),

                // ---- defense levels (spoken at speeds 1-3, the per-molecule line at 1) ----
                B(Defense,
                    Leaf("defense.took", P("building"), P("action"), Molecule(), P("meter")),
                    D(Leaf("defense.event", P("building"), P("action"))),
                    B("defense.effect",
                        // Hits and misses: logged, never spoken by default (user rule 2026-10-09).
                        Quiet(Leaf("defense.effect.hit", P("enemy"), P("result"))),
                        Quiet(Leaf("defense.effect.miss", P("result"))),
                        D(Leaf("defense.effect.part", P("enemy"), P("result"), P("parts")))),
                    D(Leaf("defense.destroyed", P("enemy"), P("result"))),
                    D(Leaf("defense.attack", P("enemy"), P("action"))),
                    D(Leaf("defense.move", P("enemy"), P("column"), P("row"))),
                    D(Leaf("defense.state", P("enemy"), P("state"))),
                    D(Leaf("defense.base", P("base"), P("result")))),
            };
            foreach (var r in roots) Link(r, null, r.Key);
            return roots;
        }

        /// <summary>Logged only: not spoken outside a step, and steps neither stop on it nor speak it
        /// (a silent stop would only say "Cycle N").</summary>
        private static EventKind Quiet(EventKind k)
        {
            k.SpeakDefault = SpeakLevel.Off;
            k.StepStopsDefault = false;
            k.StepSpeaksDefault = false;
            return k;
        }

        private static EventKind D(EventKind k)
        {
            k.SpeakDefault = SpeakLevel.Speed3;
            return k;
        }

        /// <summary>Parents, groups, and each branch's parts (the union of its leaves', in order) and flags.</summary>
        private static void Link(EventKind n, EventKind parent, string group)
        {
            n.Parent = parent;
            n.Group = group;
            if (n.IsTag || n.IsLeaf) return;
            foreach (var c in n.Children) Link(c, n, group);
            var leaves = n.Leaves();
            var parts = new List<PartDef>();
            foreach (var leaf in leaves)
            {
                int at = 0;
                foreach (var p in leaf.Parts)
                {
                    int i = parts.FindIndex(q => q.Key == p.Key);
                    if (i >= 0) { at = i + 1; continue; }
                    parts.Insert(at++, p);
                }
            }
            n.Parts = parts.ToArray();
            n.Waldo = leaves.Exists(l => l.Waldo);
            n.ReactorScoped = leaves.Exists(l => l.ReactorScoped);
        }

        private static List<EventKind> _all, _nodes;
        private static Dictionary<string, EventKind> _byKey;

        private static void Index()
        {
            if (_byKey != null) return;
            _all = new List<EventKind>();
            _nodes = new List<EventKind>();
            _byKey = new Dictionary<string, EventKind>();
            var tags = new List<EventKind>();
            foreach (var r in Roots) Visit(r, tags);
            foreach (var t in tags)
            {
                t.Members.Clear();
                foreach (var key in _tagMembers[t])
                    if (_byKey.TryGetValue(key, out var leaf) && leaf.IsLeaf) t.Members.Add(leaf);
                t.Parts = new PartDef[0];
                t.Waldo = t.Members.Exists(l => l.Waldo);
                t.ReactorScoped = t.Members.Exists(l => l.ReactorScoped);
            }
        }

        private static void Visit(EventKind n, List<EventKind> tags)
        {
            _nodes.Add(n);
            _byKey[n.Key] = n;
            if (n.IsTag) { tags.Add(n); return; }
            if (n.IsLeaf) _all.Add(n);
            foreach (var c in n.Children) Visit(c, tags);
        }

        /// <summary>Every leaf (the event types the runtime emits), in tree order.</summary>
        public static List<EventKind> All { get { Index(); return _all; } }

        /// <summary>Every node: roots, branches, leaves, tags.</summary>
        public static List<EventKind> Nodes { get { Index(); return _nodes; } }

        public static EventKind Get(string key)
        {
            Index();
            return key != null && _byKey.TryGetValue(key, out var kind) ? kind : null;
        }

        public static EventKind Root(string group) => Get(group);
    }
}
