using System;
using System.Collections.Generic;
using SpaceChem;
using SpaceChem.Reactor;
using SpeechChem.Localization;

namespace SpeechChem.Game
{
    /// <summary>
    /// Speakable names for reactor contents, shared by every reactor surface (the reactor editor
    /// for all reactor types, and later the pipeline previews). Short labels transcribe the
    /// instruction ICONS ("IN α", "GRAB DROP", "SYNC", arrows): the palette and grid draw no text
    /// for them, so the labels are mod strings (ui.json "instr.*") with the variant and direction
    /// read from the model. The game's own full names and descriptions (the tooltip text) come
    /// from the captured tooltips (Patches/TooltipCapture) and the types' static names.
    ///
    /// Variant meanings (Instruction.method_3, int_1), from the icon sprites each constructor
    /// installs: Input 0 α / 1 β; Output 0 ψ / 1 ω; Grab 0 grab+drop / 1 grab / 2 drop; Rotate 0
    /// clockwise / 1 counter-clockwise; Bond 0 add / 1 remove; Control 0-3 = A-D. Direction
    /// (vmethod_5, Enum153): Right 0, Down 90, Left 180, Up -90, None -1.
    /// </summary>
    internal static class ReactorText
    {
        public const int Red = 0x80;        // Enum114.Alpha
        public const int RedArrow = 0x40;   // Enum114.AlphaArrow
        public const int Blue = 0x20;       // Enum114.Beta
        public const int BlueArrow = 0x10;  // Enum114.BetaArrow
        public const int Background = 0x01; // Enum114.Background (features)
        public const int AllRed = 0xC8;     // Enum114.AllAlpha (incl. lines)
        public const int AllBlue = 0x34;    // Enum114.AllBeta

        /// <summary>"up" / "down" / "left" / "right", or null for no direction.</summary>
        public static string Direction(Enum153 d)
        {
            switch ((int)d)
            {
                case -90: return Loc.T("dir.up");
                case 90: return Loc.T("dir.down");
                case 180: return Loc.T("dir.left");
                case 0: return Loc.T("dir.right");
                default: return null;
            }
        }

        /// <summary>The short, icon-transcribing label of an instruction: "alpha", "arrow down",
        /// "grab drop", "bond", "clockwise", "start left", "sense hydrogen up" (input / output say
        /// only the zone letter, bonds and rotations only the variant — user request 2026-10-09).</summary>
        public static string Label(Instruction i)
        {
            if (i == null) return null;
            int v = i.method_3();
            string dir = Direction(i.vmethod_5());
            if (i is StartInstruction) return Loc.T("instr.start", new { dir });
            if (i is ArrowInstruction) return Loc.T("instr.arrow", new { dir });
            if (i is InputInstruction) return Loc.T(v == 1 ? "instr.in.beta" : "instr.in.alpha");
            if (i is OutputInstruction) return Loc.T(v == 1 ? "instr.out.omega" : "instr.out.psi");
            if (i is GrabInstruction) return Loc.T(v == 1 ? "instr.grab" : v == 2 ? "instr.drop" : "instr.grabdrop");
            if (i is RotateInstruction) return Loc.T(v == 1 ? "instr.rotate.ccw" : "instr.rotate.cw");
            if (i is BondInstruction) return Loc.T(v == 1 ? "instr.bond.remove" : "instr.bond.add");
            if (i is Class663) return Loc.T("instr.sync");
            if (i is SensorInstruction s)
            {
                string element = null;
                try { element = s.method_8().method_0(); } catch { }
                return Loc.T("instr.sense", new { element, dir });
            }
            if (i is ToggleInstruction) return Loc.T("instr.flipflop", new { dir });
            if (i is ControlInstruction)
            {
                // During a defense run the game dims a control instruction whose toggle is off.
                string label = Loc.T("instr.control", new { letter = (char)('A' + Math.Max(0, Math.Min(3, v))), dir });
                return DefenseText.ControlsShown ? label + ", " + DefenseText.OnOff(DefenseText.ControlOn(Math.Max(0, Math.Min(3, v)))) : label;
            }
            if (i is Class662) return Loc.T("instr.fuse");
            if (i is Class664) return Loc.T("instr.fission");
            if (i is Class665) return Loc.T("instr.pause");
            if (i is Class666) return Loc.T("instr.swap");
            return GameName(i.GetType()) ?? i.GetType().Name;
        }

        private static readonly Dictionary<Type, string> NameFieldCache = new Dictionary<Type, string>();

        /// <summary>The game's own name for a member type (its static string_0: "Input Molecule",
        /// "Redirect Arrow", "Bonder"), or null when the type declares none.</summary>
        public static string GameName(Type t)
        {
            if (t == null) return null;
            string name;
            if (NameFieldCache.TryGetValue(t, out name)) return name;
            name = null;
            try
            {
                var f = Deobf.Field(t, "string_0");
                if (f != null && f.IsStatic && f.FieldType == typeof(string)) name = f.GetValue(null) as string;
            }
            catch { }
            NameFieldCache[t] = name;
            return name;
        }

        /// <summary>A placed feature (bonder, sensor, laser target, tunnel): its captured tooltip
        /// title, else its own name field, else the plain word "hardware" — never the type name,
        /// which is obfuscated in the shipping game (the laser targets carry an empty name field,
        /// so a missing tooltip capture, e.g. after a dev reload, would have spoken "#=q…").</summary>
        public static string FeatureLabel(ReactorFeature f)
        {
            if (f == null) return null;
            string label = BaseFeatureLabel(f);
            int? priority = BonderPriority(f);
            return priority.HasValue ? Loc.T("reactor.bonder.priority", new { name = label, n = priority.Value }) : label;
        }

        private static string BaseFeatureLabel(ReactorFeature f)
        {
            var tip = Patches.TooltipCapture.Of(f.class713_0);
            if (tip != null && !string.IsNullOrEmpty(tip.Title)) return tip.Title;
            try
            {
                var field = Deobf.Field(typeof(ReactorFeature), "string_0");
                string s = field?.GetValue(f) as string;
                if (!string.IsNullOrEmpty(s)) return s;
            }
            catch { }
            return Loc.T("reactor.hardware");
        }

        /// <summary>The number the game draws on a bonder while the Settings dialog's "Show Bonder
        /// Priority" is on (Class210.bool_3), else null: 1 + the bonders before it in the reactor's
        /// member order (Class668.vmethod_3, which counts the bond-only / unbond-only kinds too).</summary>
        public static int? BonderPriority(ReactorFeature f)
        {
            try
            {
                if (!(f is Class668) || f.reactor_0 == null) return null;
                if (!Class184.smethod_3().class210_0.bool_3) return null;
                int n = 1;
                foreach (ReactorMember m in f.reactor_0.method_0())
                {
                    if (m == f) return n;
                    if (m is Class668) n++;
                }
            }
            catch { }
            return null;
        }

        /// <summary>The bottom info box for an atom (Class712.method_0): name, atomic number and
        /// "Maximum Bonds:" — with the box's own special cases (Australium 79 / 5, the exotic
        /// Greek elements "??").</summary>
        public static string AtomDetails(Atom a)
        {
            string name = a.method_0();
            string number = ((int)a.element_0).ToString();
            string bonds = a.method_2().ToString();
            try
            {
                if (a.element_0.ToString() == "Australium") { number = "79"; bonds = "5"; }
                else if (a.element_0.smethod_4()) { number = "??"; bonds = "??"; }
            }
            catch { }
            return Loc.T("atom.details", new { name, number, bondsLabel = GameText.T("Maximum Bonds:"), bonds });
        }

        /// <summary>"single" / "double" / "triple" for a bond count.</summary>
        public static string BondWord(int count)
            => Loc.T(count >= 3 ? "bond.triple" : count == 2 ? "bond.double" : "bond.single");
    }
}
