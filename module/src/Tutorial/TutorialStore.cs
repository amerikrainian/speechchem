using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SpeechChem.Tutorial
{
    /// <summary>
    /// The mod's tutorial progress, per profile and level (user rule 2026-10-23: a step that is not
    /// a placement stays done): %LOCALAPPDATA%\SpeechChem\tutorial.tsv, one line per done step —
    /// profile, level id, step id; the step id "*" marks the whole tutorial finished, after which
    /// the level has none (a later edit doesn't bring a step back). Placement steps are never
    /// stored: they are met while the piece is in place. Every failure is swallowed: without the
    /// file the tutorial starts over.
    /// </summary>
    internal static class TutorialStore
    {
        public const string Finished = "*";

        private static Dictionary<string, HashSet<string>> _all;

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpeechChem", "tutorial.tsv");

        /// <summary>The done steps for a "profile TAB level" key (a copy; empty when none).</summary>
        public static HashSet<string> Load(string key)
        {
            try
            {
                Ensure();
                return _all.TryGetValue(key, out var set) ? new HashSet<string>(set) : new HashSet<string>();
            }
            catch (Exception ex) { Log.Error("[tutorial] load failed", ex); return new HashSet<string>(); }
        }

        public static void Add(string key, string step)
        {
            try
            {
                Ensure();
                if (!_all.TryGetValue(key, out var set)) _all[key] = set = new HashSet<string>();
                if (set.Add(step)) Write();
            }
            catch (Exception ex) { Log.Error("[tutorial] save failed", ex); }
        }

        /// <summary>Forget a level's progress (the dev probe's reset).</summary>
        public static void Clear(string key)
        {
            try
            {
                Ensure();
                if (_all.Remove(key)) Write();
            }
            catch (Exception ex) { Log.Error("[tutorial] clear failed", ex); }
        }

        private static void Ensure()
        {
            if (_all != null) return;
            _all = new Dictionary<string, HashSet<string>>();
            if (!File.Exists(FilePath)) return;
            foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                var f = line.Split('\t');
                if (f.Length != 3) continue;
                string key = f[0] + "\t" + f[1];
                if (!_all.TryGetValue(key, out var set)) _all[key] = set = new HashSet<string>();
                set.Add(f[2]);
            }
        }

        private static void Write()
        {
            var sb = new StringBuilder();
            foreach (var kv in _all)
                foreach (var s in kv.Value)
                    sb.Append(kv.Key).Append('\t').Append(s).Append("\r\n");
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(tmp, FilePath);
        }
    }
}
