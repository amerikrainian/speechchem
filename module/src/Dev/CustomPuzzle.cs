#if DEBUG
using System;
using Impeller;
using SpaceChem;
using SpaceChem.ResearchNet;
using SpeechChem.Game;

namespace SpeechChem.Dev
{
    /// <summary>
    /// Probe "custom": a test bench for reactor and pipeline mechanics. Opens a ResearchNet puzzle
    /// built from a journal.json-style level object — the same JSON the published puzzles use. Its
    /// "type" picks what the game builds (Levels.smethod_15): "research" (Class85, straight into the
    /// reactor), "production" (Class136, the pipeline editor) or "sandbox" (Class147, a defense-style
    /// level editor). E.g.
    ///
    ///   {"type":"research","name":"Fusion test","author":"","difficulty":0,"bonder-count":0,
    ///    "has-fuser":true,"input-zones":{"0":{"inputs":[{"molecule":"Hydrogen;H;000100","count":1}]}},
    ///    "output-zones":{"0":{"molecule":"Helium;He;000200","count":10}}}
    ///
    /// Molecule strings: "name;formula;" then one token per atom — x, y (one digit each), atomic
    /// number, then the bond counts to the right and down (Molecule.smethod_9).
    ///
    /// It opens the way the journal's card click does (PublishedLevelCache.smethod_3: the profile's
    /// current custom level, the known molecules, Levels.smethod_14), under the fixed id
    /// <see cref="TestId"/> — a "custom-" id, so the game builds its level from the custom level.
    /// The game AUTO-SAVES the reactor under that id like any solution; "custom clean" wipes it with
    /// the game's own solution delete (SpaceChemUserWorker.method_74, what deleting a custom
    /// assignment runs) plus the status row opening a level writes (Level). The puzzle itself is
    /// never added to the saved custom assignments.
    ///
    ///   custom &lt;json&gt;   open the puzzle
    ///   custom clean    delete the test id's saved solution and status row (refused while it is open)
    /// </summary>
    internal static class CustomPuzzle
    {
        public const string TestId = "custom-speechchem-test";

        public static string Run(string argument)
        {
            string arg = (argument ?? "").Trim();
            if (arg.Length == 0) return "usage: custom <journal-style level json> | custom clean\n";
            if (arg.Equals("clean", StringComparison.OrdinalIgnoreCase)) return Clean();
            return Open(arg);
        }

        private static string Open(string json)
        {
            if (Class53.smethod_5<Class83>() != null) return "[a level is already open]\n";
            CustomLevel level;
            try
            {
                level = CustomLevel.smethod_0(json);
                level.vmethod_1(); // the import path's normalization (CustomLevel.smethod_1)
            }
            catch (Exception ex) { return "[bad level json] " + ex.Message + "\n"; }
            // The game's editor only builds connected molecules, and its code relies on it: fusing
            // the atoms of one unbonded two-atom "molecule" crashed the game (Class672.method_7 looks
            // for the target's fragment after dropping the projectile's whole molecule).
            foreach (var m in level.vmethod_2())
                if (!Connected(m)) return "[molecule \"" + m.string_0 + "\" has unbonded atoms: split it into separate molecules]\n";

            Class280.class185_0.method_8(new Class513());
            try
            {
                Locals.smethod_0().smethod_0().customLevel_0 = level;
                Class307.smethod_3(level.vmethod_2());
                Class53.smethod_1(SpaceChem.Levels.Levels.smethod_14(TestId));
            }
            finally { Class280.class185_0.method_9(); }
            return "opened " + TestId + ": " + level.string_1 + "\n";
        }

        /// <summary>Every atom reachable from the first through bonds (dictionary_3: a cell and
        /// Right / Down to its neighbour).</summary>
        private static bool Connected(Molecule m)
        {
            var atoms = new System.Collections.Generic.HashSet<Vector2i>(m.dictionary_2.Keys);
            if (atoms.Count <= 1) return true;
            var seen = new System.Collections.Generic.HashSet<Vector2i>();
            var todo = new System.Collections.Generic.Stack<Vector2i>();
            foreach (var first in atoms) { todo.Push(first); break; }
            while (todo.Count > 0)
            {
                var c = todo.Pop();
                if (!seen.Add(c)) continue;
                foreach (var bond in m.dictionary_3.Keys)
                {
                    if ((int)m.dictionary_3[bond] == 0) continue;
                    var from = bond.vector2i_0;
                    var to = bond.enum128_0 == Enum128.Right ? new Vector2i(from.int_0 + 1, from.int_1) : new Vector2i(from.int_0, from.int_1 + 1);
                    if (from == c && atoms.Contains(to)) todo.Push(to);
                    else if (to == c && atoms.Contains(from)) todo.Push(from);
                }
            }
            return seen.Count == atoms.Count;
        }

        private static string Clean()
        {
            if (Class53.smethod_5<Class83>() != null && GoalTracker.string_0 == TestId)
                return "[leave the test level first]\n";
            var worker = Locals.smethod_0().smethod_0();
            worker.method_74(TestId);
            // Opening a level also writes its status row (Level: passed 0, no scores); the game
            // never deletes those, even for a deleted custom assignment. Same worker thread and
            // connection as the game's own statements.
            worker.vmethod_2(() =>
            {
                try
                {
                    using (var cmd = worker.method_6().CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM [Level] WHERE [id] == @ID";
                        cmd.Parameters.AddWithValue("@ID", TestId);
                        cmd.ExecuteNonQuery();
                    }
                }
                catch (Exception ex) { Log.Error("[probe] custom clean: Level row", ex); }
            });
            return "wiped the saved solution and status of " + TestId + "\n";
        }
    }
}
#endif
