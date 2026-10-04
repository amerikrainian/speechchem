using System.Collections.Generic;
using Impeller;
using SpaceChem;
using SpaceChem.Pipeline;
using SpeechChem.Localization;

namespace SpeechChem.Game
{
    /// <summary>
    /// Naming for the pipeline editor's components, shared by the pipeline screen and the run log.
    /// A component's name is the game's own (Draggable.string_1: "Assembly Reactor", "Recycler",
    /// "Storage Tank"). When several share a group — all reactors form one group (the run log's
    /// "reactor 2"), any other component's group is its name — they are numbered in the order
    /// they were FIRST SEEN (user rule 2026-10-01, replacing reading order: placing a reactor
    /// above an existing one renumbered both): what a level opens with is numbered in reading
    /// order, a placed component takes the next number, a moved one keeps its number, and a
    /// deleted one closes the gap (delete 2 of 3 and 3 becomes 2). <see cref="Sync"/> keeps the
    /// order (the pipeline screen calls it every frame, so a delete and a later placement are
    /// never seen together); components replaced in one go — an undo reloads the level with new
    /// objects — keep their numbers by matching group and top-left cell, then, for a moved one,
    /// the vanished slot of its group.
    /// </summary>
    internal static class PipelineText
    {
        /// <summary>The components worth naming, in reading order: everything placed on the
        /// pipeline except the terrain (Class612) and unnamed decoration (no name, no ports).</summary>
        public static List<KeyValuePair<Draggable, Vector2i>> Components(Pipeline p)
        {
            var list = new List<KeyValuePair<Draggable, Vector2i>>();
            if (p == null) return list;
            foreach (var kv in p)
            {
                var d = kv.Key;
                if (d is Class612 || d is PipeDraggable) continue;
                if (!(d is ReactorDraggable) && string.IsNullOrEmpty(d.string_1?.Trim())
                    && d.class485_0.Count == 0 && d.class485_1.Count == 0) continue;
                if (kv.Value.int_0 < 0) continue; // a shelf clone parked off the grid mid-placement
                list.Add(kv);
            }
            list.Sort((a, b) =>
            {
                int c = a.Value.int_1.CompareTo(b.Value.int_1);
                return c != 0 ? c : a.Value.int_0.CompareTo(b.Value.int_0);
            });
            return list;
        }

        private sealed class Entry
        {
            public Draggable D;
            public Vector2i Origin;
            public string Group;
        }

        // First-seen order of every named component of the current pipeline.
        private static readonly List<Entry> Order = new List<Entry>();

        // The level the order belongs to ("profile TAB level id"; null = unknown, nothing saved),
        // and the saved order to restore on the first sync after opening it (NumberingStore).
        private static string _key;
        private static List<NumberingStore.Slot> _saved;

        private static string Group(Draggable d) => d is ReactorDraggable ? "#reactors" : d.string_1?.Trim() ?? "";

        /// <summary>The current profile and level (GoalTracker.string_0, the id the game records
        /// progress under), or null.</summary>
        private static string LevelKey()
        {
            try
            {
                string level = GoalTracker.string_0, profile = Locals.smethod_0()?.string_0;
                return string.IsNullOrEmpty(level) || profile == null ? null : profile + "\t" + level;
            }
            catch { return null; }
        }

        /// <summary>Bring the first-seen order up to date with the pipeline's components.</summary>
        public static void Sync(Pipeline p)
        {
            string key = LevelKey();
            if (key != _key)
            {
                // Another level (or profile): start over from what was saved for it.
                _key = key;
                Order.Clear();
                _saved = key == null ? null : NumberingStore.Load(key);
            }
            var current = Components(p);
            var present = new HashSet<Draggable>();
            foreach (var kv in current) present.Add(kv.Key);
            var vanished = new List<Entry>();
            var known = new HashSet<Draggable>();
            foreach (var e in Order)
                if (present.Contains(e.D)) known.Add(e.D);
                else vanished.Add(e);
            var fresh = new List<KeyValuePair<Draggable, Vector2i>>();
            foreach (var kv in current)
                if (!known.Contains(kv.Key)) fresh.Add(kv);
            // Present ones: remember where they are now (a move keeps the entry).
            bool moved = false;
            foreach (var e in Order)
                foreach (var kv in current)
                    if (ReferenceEquals(kv.Key, e.D))
                    {
                        if (e.Origin != kv.Value) { e.Origin = kv.Value; moved = true; }
                        break;
                    }
            if (vanished.Count == 0 && fresh.Count == 0)
            {
                if (moved) Persist();
                return;
            }
            // Replaced in the same frame (an undo's reload): same group and cell first, then the
            // group's remaining vanished slots in order (an undone move).
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < fresh.Count; i++)
                {
                    var f = fresh[i];
                    string g = Group(f.Key);
                    var slot = vanished.Find(v => v.Group == g && (pass == 1 || v.Origin == f.Value));
                    if (slot == null) continue;
                    slot.D = f.Key;
                    slot.Origin = f.Value;
                    vanished.Remove(slot);
                    fresh.RemoveAt(i--);
                }
            foreach (var v in vanished) Order.Remove(v); // deleted: later numbers close the gap
            if (_saved != null)
            {
                // Just opened: the saved order first (matched by group and cell), the rest after,
                // in reading order (fresh is in reading order already; the sort is stable).
                var saved = _saved;
                _saved = null;
                var used = new bool[saved.Count];
                var rank = new Dictionary<Draggable, int>();
                for (int i = 0; i < fresh.Count; i++)
                {
                    var f = fresh[i];
                    string g = Group(f.Key);
                    int r = saved.Count + i;
                    for (int j = 0; j < saved.Count; j++)
                        if (!used[j] && saved[j].Group == g && saved[j].X == f.Value.int_0 && saved[j].Y == f.Value.int_1)
                        {
                            used[j] = true;
                            r = j;
                            break;
                        }
                    rank[f.Key] = r;
                }
                var sorted = new List<KeyValuePair<Draggable, Vector2i>>(fresh);
                fresh.Clear();
                for (int r = 0; sorted.Count > 0; r++)
                {
                    // stable selection by rank (lists are short)
                    int best = 0;
                    for (int i = 1; i < sorted.Count; i++)
                        if (rank[sorted[i].Key] < rank[sorted[best].Key]) best = i;
                    fresh.Add(sorted[best]);
                    sorted.RemoveAt(best);
                }
            }
            foreach (var f in fresh) Order.Add(new Entry { D = f.Key, Origin = f.Value, Group = Group(f.Key) });
            Persist();
        }

        /// <summary>Save the order for this level when a group has two or more members (numbers
        /// are only spoken then); otherwise forget it.</summary>
        private static void Persist()
        {
            if (_key == null) return;
            var counts = new Dictionary<string, int>();
            bool numbered = false;
            foreach (var e in Order)
            {
                counts.TryGetValue(e.Group, out int c);
                counts[e.Group] = ++c;
                if (c > 1) numbered = true;
            }
            List<NumberingStore.Slot> slots = null;
            if (numbered)
            {
                slots = new List<NumberingStore.Slot>();
                foreach (var e in Order) slots.Add(new NumberingStore.Slot { Group = e.Group, X = e.Origin.int_0, Y = e.Origin.int_1 });
            }
            NumberingStore.Save(_key, slots);
        }

        /// <summary>A component's number in its group (1-based, first-seen order), or 0 when it is
        /// the group's only member.</summary>
        public static int Number(Pipeline p, Draggable d)
        {
            if (p == null || d == null) return 0;
            Sync(p);
            string g = Group(d);
            int count = 0, index = 0;
            foreach (var e in Order)
            {
                if (e.Group != g) continue;
                count++;
                if (ReferenceEquals(e.D, d)) index = count;
            }
            return count < 2 ? 0 : index;
        }

        /// <summary>"Assembly Reactor 2", "Recycler", "Storage Tank".</summary>
        public static string Name(Pipeline p, Draggable d)
        {
            if (d == null) return null;
            string name = d.string_1?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                if (!(d is ReactorDraggable)) return Loc.T("pipeline.component");
                name = Loc.T("pipeline.reactor");
            }
            int n = Number(p, d);
            return n > 0 ? name + " " + n : name;
        }

        /// <summary>A component's type name without its number ("Assembly Reactor").</summary>
        public static string TypeName(Draggable d)
        {
            string name = d?.string_1?.Trim();
            if (!string.IsNullOrEmpty(name)) return name;
            return Loc.T(d is ReactorDraggable ? "pipeline.reactor" : "pipeline.component");
        }

        public static string Cell(Vector2i c) => Loc.T("reactor.cell", new { x = c.int_0 + 1, y = c.int_1 + 1 });

        /// <summary>"2 inputs, 1 output" — the ports the component's picture shows (class485_0 /
        /// class485_1); the game writes no such text (a mod addition, user request 2026-10-03).</summary>
        public static string Ports(Draggable d)
        {
            if (d == null) return null;
            return Count(d.class485_0.Count, "pipeline.ports.in") + ", " + Count(d.class485_1.Count, "pipeline.ports.out");
        }

        private static string Count(int n, string key)
            => Loc.T(n == 0 ? key + ".none" : n == 1 ? key + ".one" : key + ".many", new { n });

        /// <summary>The component's hover tooltip as the game builds it (Draggable.vmethod_18 →
        /// Class713.smethod_0(string_1, string_2, struct103_0)) minus the title, which the focused
        /// item already names: the general description (string_2), then the type's own text
        /// (struct103_0.string_1 — for a reactor its abilities, outputs and the flavour line).</summary>
        public static string Tooltip(Draggable d)
        {
            if (d == null) return null;
            var parts = new System.Collections.Generic.List<string>();
            string body = GameText.Speech(d.string_2);
            if (!string.IsNullOrEmpty(body)) parts.Add(body);
            string extra = GameText.Speech(d.struct103_0.string_1);
            if (!string.IsNullOrEmpty(extra)) parts.Add(extra);
            return parts.Count == 0 ? null : string.Join(" ", parts.ToArray());
        }
    }
}
