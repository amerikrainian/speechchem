using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace SpeechChem.Narration
{
    /// <summary>
    /// The narration settings (events, formats, step keys): a flat key → string map in
    /// %LOCALAPPDATA%\SpeechChem\narration.json holding only values the player changed (a missing
    /// key = the registry's default, so new defaults reach players who never touched them), plus a
    /// "version" for later migrations. The Settings dialog edits a DRAFT (BeginEdit); Save Changes
    /// commits it to the file, Cancel drops it (the game tab's semantics — user rule 2026-10-04).
    /// The runtime (Narrator, step keys) always reads the committed values.
    /// </summary>
    internal static class NarrationStore
    {
        public const int Version = 1;
        private const string VersionKey = "version";

        private static Dictionary<string, string> _saved;
        private static Dictionary<string, string> _draft;

        /// <summary>Overridable for tests.</summary>
        internal static string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpeechChem", "narration.json");

        /// <summary>Raised after a commit (formats cached by views may re-render).</summary>
        public static event Action Committed;

        private static Dictionary<string, string> Saved => _saved ?? (_saved = Load());

        /// <summary>The committed value, or null (= default).</summary>
        public static string Get(string key)
        {
            string v;
            return Saved.TryGetValue(key, out v) ? v : null;
        }

        // ---- the dialog's draft ----

        public static bool Editing => _draft != null;

        public static void BeginEdit()
        {
            if (_draft == null) _draft = new Dictionary<string, string>(Saved);
        }

        /// <summary>The value the dialog shows: the draft's while editing, else the committed one.</summary>
        public static string GetDraft(string key)
        {
            string v;
            var map = _draft ?? Saved;
            return map.TryGetValue(key, out v) ? v : null;
        }

        /// <summary>Set (null = back to the default) in the draft; outside an edit, commit at once.</summary>
        public static void SetDraft(string key, string value)
        {
            bool immediate = _draft == null;
            var map = _draft ?? Saved;
            if (value == null) map.Remove(key); else map[key] = value;
            if (immediate) Write(Saved);
        }

        /// <summary>Drop every draft key under <paramref name="prefix"/> (a reset).</summary>
        public static void ResetDraft(string prefix)
        {
            var map = _draft ?? Saved;
            var keys = new List<string>();
            foreach (var k in map.Keys) if (k.StartsWith(prefix, StringComparison.Ordinal) && k != VersionKey) keys.Add(k);
            foreach (var k in keys) map.Remove(k);
            if (_draft == null) Write(Saved);
        }

        public static void Commit()
        {
            if (_draft == null) return;
            _saved = _draft;
            _draft = null;
            Write(_saved);
            try { Committed?.Invoke(); } catch { }
        }

        public static void Discard() => _draft = null;

        // ---- the file ----

        private static Dictionary<string, string> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var map = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath));
                    if (map != null) return Migrate(map);
                }
            }
            catch (Exception ex) { Log.Error("[narration] settings unreadable — defaults in use, file left alone", ex); _unreadable = true; }
            return new Dictionary<string, string>();
        }

        private static bool _unreadable;

        /// <summary>Upgrade an older file's keys (none yet: version 1 is the first).</summary>
        private static Dictionary<string, string> Migrate(Dictionary<string, string> map) => map;

        private static void Write(Dictionary<string, string> map)
        {
            if (_unreadable) { Log.Info("[narration] not saving over an unreadable settings file"); return; }
            try
            {
                map[VersionKey] = Version.ToString();
                var sorted = new SortedDictionary<string, string>(map, StringComparer.Ordinal);
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, Pretty(sorted));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Log.Error("[narration] settings save failed", ex); }
        }

        /// <summary>One key per line, sorted — readable and diffable.</summary>
        private static string Pretty(SortedDictionary<string, string> map)
        {
            var js = new JavaScriptSerializer();
            var lines = new List<string>();
            foreach (var kv in map) lines.Add("  " + js.Serialize(kv.Key) + ": " + js.Serialize(kv.Value));
            return "{\n" + string.Join(",\n", lines.ToArray()) + "\n}\n";
        }

        /// <summary>Tests: forget everything loaded (the next read reloads from FilePath).</summary>
        internal static void ResetForTests() { _saved = null; _draft = null; _unreadable = false; }
    }
}
