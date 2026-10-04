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
    /// An event type: its group, its parts in their default order (the order that reproduces the
    /// mod's original wording), what it applies to, and its defaults. Everything the settings
    /// pages show is generated from these — every event page has the same rows, minus the ones
    /// that don't apply (sources on a level-wide event; the reactor scope on one).
    /// </summary>
    internal sealed class EventKind
    {
        public string Key;
        public string Group;
        public PartDef[] Parts;
        /// <summary>A waldo's event: red / blue source filters apply.</summary>
        public bool Waldo;
        /// <summary>Belongs to a reactor or a building it feeds: "inside a reactor" scope applies.</summary>
        public bool ReactorScoped;
        public bool LogDefault = true;
        public SpeakLevel SpeakDefault = SpeakLevel.Speed1;
        /// <summary>The default step keys (0 and Ctrl+0) stop on it / speak it.</summary>
        public bool StepStopsDefault = true, StepSpeaksDefault = true;
    }

    /// <summary>The registry. Adding an event type = one entry here; its settings, pages and
    /// stored keys follow.</summary>
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

        private static EventKind W(string key, params PartDef[] parts)
        {
            var all = new List<PartDef> { Reactor(), Colour() };
            all.AddRange(parts);
            return new EventKind { Key = key, Group = Waldo, Parts = all.ToArray(), Waldo = true, ReactorScoped = true };
        }

        public static readonly EventKind[] All =
        {
            // ---- waldo actions (spoken at speed 1, only the open reactor's inside a reactor) ----
            W("waldo.input", new PartDef("instruction"), Molecule(), new PartDef("place")),
            W("waldo.wait", new PartDef("instruction"), new PartDef("state")),
            W("waldo.instruction", new PartDef("instruction")),
            W("waldo.grab", new PartDef("action"), Molecule()),
            W("waldo.drop", new PartDef("action"), Molecule()),
            W("waldo.holding", new PartDef("action"), Molecule()),
            W("waldo.nothing", new PartDef("action")),
            W("waldo.bond", new PartDef("action"), new PartDef("atoms", AtomsVariants), new PartDef("result")),
            W("waldo.rotate", new PartDef("instruction"), new PartDef("action"), Molecule()),
            W("waldo.turn", new PartDef("instruction"), new PartDef("heading")),
            W("waldo.heading", new PartDef("heading")),
            W("waldo.sensor", new PartDef("action"), new PartDef("atom"), new PartDef("heading")),
            W("waldo.flipflop", new PartDef("action"), new PartDef("heading")),
            W("waldo.fusion", new PartDef("action"), new PartDef("place"), new PartDef("atom")),
            W("waldo.fission", new PartDef("action"), new PartDef("place"), new PartDef("atom")),
            W("waldo.swap", new PartDef("action"), new PartDef("moves")),
            W("waldo.wall", new PartDef("action"), new PartDef("place")),

            // ---- outputs and errors ----
            new EventKind { Key = "output.produced", Group = Outputs, ReactorScoped = true,
                Parts = new[] { new PartDef("output"), Molecule(), new PartDef("count") } },
            new EventKind { Key = "output.invalid", Group = Outputs, ReactorScoped = true, SpeakDefault = SpeakLevel.Off, StepSpeaksDefault = false,
                Parts = new[] { new PartDef("action"), new PartDef("target"), Molecule() } },
            new EventKind { Key = "run.error", Group = Outputs, SpeakDefault = SpeakLevel.Off, StepStopsDefault = false, StepSpeaksDefault = false,
                Parts = new[] { Reactor(), new PartDef("label"), new PartDef("message") } },
            new EventKind { Key = "run.completed", Group = Outputs, SpeakDefault = SpeakLevel.Off, StepSpeaksDefault = false,
                Parts = new[] { new PartDef("message") } },

            // ---- the run itself ----
            new EventKind { Key = "run.state", Group = Run, SpeakDefault = SpeakLevel.Always, StepStopsDefault = false, StepSpeaksDefault = false,
                Parts = new[] { new PartDef("state") } },
            new EventKind { Key = "run.speed", Group = Run, LogDefault = false, SpeakDefault = SpeakLevel.Always, StepStopsDefault = false, StepSpeaksDefault = false,
                Parts = new[] { new PartDef("state") } },

            // ---- defense levels (spoken at speeds 1-3, the per-molecule line at 1) ----
            new EventKind { Key = "defense.took", Group = Defense,
                Parts = new[] { new PartDef("building"), new PartDef("action"), Molecule(), new PartDef("meter") } },
            new EventKind { Key = "defense.event", Group = Defense, SpeakDefault = SpeakLevel.Speed3,
                Parts = new[] { new PartDef("building"), new PartDef("action") } },
            new EventKind { Key = "defense.effect", Group = Defense, SpeakDefault = SpeakLevel.Speed3,
                Parts = new[] { new PartDef("enemy"), new PartDef("result"), new PartDef("parts") } },
            new EventKind { Key = "defense.destroyed", Group = Defense, SpeakDefault = SpeakLevel.Speed3,
                Parts = new[] { new PartDef("enemy"), new PartDef("result") } },
            new EventKind { Key = "defense.attack", Group = Defense, SpeakDefault = SpeakLevel.Speed3,
                Parts = new[] { new PartDef("enemy"), new PartDef("action") } },
            new EventKind { Key = "defense.move", Group = Defense, SpeakDefault = SpeakLevel.Speed3,
                Parts = new[] { new PartDef("enemy"), new PartDef("column"), new PartDef("row") } },
            new EventKind { Key = "defense.state", Group = Defense, SpeakDefault = SpeakLevel.Speed3,
                Parts = new[] { new PartDef("enemy"), new PartDef("state") } },
            new EventKind { Key = "defense.base", Group = Defense, SpeakDefault = SpeakLevel.Speed3,
                Parts = new[] { new PartDef("base"), new PartDef("result") } },
        };

        private static Dictionary<string, EventKind> _byKey;

        public static EventKind Get(string key)
        {
            if (_byKey == null)
            {
                _byKey = new Dictionary<string, EventKind>();
                foreach (var k in All) _byKey[k.Key] = k;
            }
            EventKind kind;
            return _byKey.TryGetValue(key, out kind) ? kind : null;
        }

        public static List<EventKind> InGroup(string group)
        {
            var list = new List<EventKind>();
            foreach (var k in All) if (k.Group == group) list.Add(k);
            return list;
        }
    }
}
