using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SpeechChem.Game
{
    /// <summary>
    /// The pipeline's first-seen building numbers, saved per profile and level (user request
    /// 2026-10-01) so they survive leaving the level and restarting the game:
    /// %LOCALAPPDATA%\SpeechChem\numbering.tsv, one line per numbered building in order —
    /// profile, level id, group, x, y (the building's top-left cell). Only levels where some group
    /// has two or more buildings are kept; numbering is moot otherwise. Every failure is swallowed:
    /// without the file the numbers fall back to reading order.
    /// </summary>
    internal static class NumberingStore
    {
        public struct Slot
        {
            public string Group;
            public int X, Y;
        }

        private static Dictionary<string, List<Slot>> _all;

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpeechChem", "numbering.tsv");

        /// <summary>The saved order for a "profile TAB level" key, or null.</summary>
        public static List<Slot> Load(string key)
        {
            try
            {
                Ensure();
                return _all.TryGetValue(key, out var list) ? new List<Slot>(list) : null;
            }
            catch (Exception ex) { Log.Error("[numbering] load failed", ex); return null; }
        }

        /// <summary>Record a level's order (null or empty forgets it); writes only on a change.</summary>
        public static void Save(string key, List<Slot> slots)
        {
            try
            {
                Ensure();
                bool had = _all.TryGetValue(key, out var old);
                if (slots == null || slots.Count == 0)
                {
                    if (!had) return;
                    _all.Remove(key);
                }
                else
                {
                    if (had && Same(old, slots)) return;
                    _all[key] = new List<Slot>(slots);
                }
                Write();
            }
            catch (Exception ex) { Log.Error("[numbering] save failed", ex); }
        }

        private static bool Same(List<Slot> a, List<Slot> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i].Group != b[i].Group || a[i].X != b[i].X || a[i].Y != b[i].Y) return false;
            return true;
        }

        private static void Ensure()
        {
            if (_all != null) return;
            _all = new Dictionary<string, List<Slot>>();
            if (!File.Exists(FilePath)) return;
            foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                var f = line.Split('\t');
                if (f.Length != 5 || !int.TryParse(f[3], out int x) || !int.TryParse(f[4], out int y)) continue;
                string key = f[0] + "\t" + f[1];
                if (!_all.TryGetValue(key, out var list)) _all[key] = list = new List<Slot>();
                list.Add(new Slot { Group = f[2], X = x, Y = y });
            }
        }

        private static void Write()
        {
            var sb = new StringBuilder();
            foreach (var kv in _all)
                foreach (var s in kv.Value)
                    sb.Append(kv.Key).Append('\t').Append(s.Group).Append('\t').Append(s.X).Append('\t').Append(s.Y).Append("\r\n");
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(tmp, FilePath);
        }
    }
}
