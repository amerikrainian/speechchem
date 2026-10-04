#if DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using SpeechChem.Narration;

namespace SpeechChem.Dev
{
    /// <summary>
    /// Probe "logstress": measures what a big run log costs (memory held, GC work, frame time).
    ///
    ///   logstress fill &lt;events&gt; [reactors]   emit synthetic waldo events through the real
    ///       Narrator (logged, never spoken) — a looping 24-step program per reactor, two waldos,
    ///       molecules with their three detail variants, the open reactor as reactor 1 — and
    ///       report the time, the GC collections it caused and the memory held after a full GC
    ///   logstress frames &lt;n&gt;   time the next n frames (tick to tick) and count collections
    ///   logstress burst &lt;per frame&gt; &lt;n&gt; [reactors]   emit that many events every frame
    ///       for n frames while timing them (a fast run's load)
    ///   logstress report        the frame measurement, once finished
    ///   logstress clear         empty the run log (a new run does the same)
    ///   logstress mem           memory held now (after a full collection)
    /// </summary>
    internal static class LogStress
    {
        public static string Run(string argument)
        {
            var args = (argument ?? "").Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string cmd = args.Length > 0 ? args[0] : "";
            switch (cmd)
            {
                case "fill":
                {
                    int n = args.Length > 1 ? int.Parse(args[1]) : 100000;
                    int reactors = args.Length > 2 ? int.Parse(args[2]) : 4;
                    return Fill(n, reactors);
                }
                case "frames": _burst = 0; return StartFrames(args.Length > 1 ? int.Parse(args[1]) : 300);
                case "burst":
                {
                    // burst <events per frame> <frames> [reactors]: emit while timing, as a fast run does
                    _burst = args.Length > 1 ? int.Parse(args[1]) : 1000;
                    int frames = args.Length > 2 ? int.Parse(args[2]) : 300;
                    _burstReactors = args.Length > 3 ? int.Parse(args[3]) : 4;
                    _burstObjects = null;
                    return StartFrames(frames);
                }
                case "report": return Report();
                case "clear":
                    Patches.RunCapture.ClearLog();
                    return "cleared; " + ClearOlderGenerations() + Held() + "\n";
                case "mem": return Held() + "\n";
                case "mode":
                {
                    // mode normal | mute | bypass: events as usual / every event muted / the
                    // per-cycle capture hooks return at once
                    string m = args.Length > 1 ? args[1] : "normal";
                    Narrator.DebugMute = m == "mute";
                    Patches.RunCapture.DebugBypass = m == "bypass";
                    return "mode " + m + "\n";
                }
                default: return "usage: logstress fill <events> [reactors] | frames <n> | report | clear | mem\n";
            }
        }

        /// <summary>Hot reload never unloads a module generation, and an older one's static run log
        /// stays alive with it (dev only): empty those too, so measurements start clean.</summary>
        private static string ClearOlderGenerations()
        {
            var self = typeof(LogStress).Assembly;
            int n = 0;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (ReferenceEquals(asm, self) || asm.GetName().Name != self.GetName().Name) continue;
                try
                {
                    var type = asm.GetType("SpeechChem.Patches.RunCapture");
                    var log = type?.GetField("Log", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)?.GetValue(null);
                    log?.GetType().GetMethod("Clear")?.Invoke(log, null);
                    if (log != null) n++;
                }
                catch { }
            }
            return n + " older generations' logs cleared; ";
        }

        private static string Held()
        {
            long bytes = GC.GetTotalMemory(true);
            var log = Patches.RunCapture.Log;
            return "held " + (bytes / (1024 * 1024)) + " MB, log entries " + log.EntryCount + ", groups " + log.Groups.Count + ", distinct events " + log.DistinctValues
                + ", groups shed " + log.DroppedGroups + " (last drop " + log.LastDropMs.ToString("F1") + " ms)";
        }

        private static readonly string[] Molecules = { "Methane", "Water", "Ammonia", "Carbon Dioxide", "Hydrogen", "Oxygen" };
        private static readonly string[] Formulas = { "CH4", "H2O", "NH3", "CO2", "H2", "O2" };

        /// <summary>Reactor r's program step s, as RunCapture builds it (same parts, same shapes).</summary>
        private static NarrationEvent Step(object reactor, int r, int waldo, int s)
        {
            int k = s % 4;
            string kind = k == 0 ? "waldo.input" : k == 1 ? "waldo.grab" : k == 2 ? "waldo.drop" : "waldo.turn";
            var e = new NarrationEvent(kind);
            e.CommonPart("reactor", "reactor " + (r + 1), ",");
            e.CommonPart("waldo", waldo == 0 ? "red" : "blue", ":");
            e.Reactor = reactor;
            e.Colour = waldo;
            int m = (s + r) % Molecules.Length;
            Func<NarrationEvent, NarrationEvent> molecule = ev =>
            {
                var v = new Dictionary<string, string>
                {
                    { "both", Molecules[m] + ", " + Formulas[m] },
                    { "name", Molecules[m] },
                    { "formula", Formulas[m] },
                };
                return ev.Part("molecule", v, v["both"]);
            };
            switch (k)
            {
                case 0:
                    e.Part("instruction", waldo == 0 ? "in alpha" : "in beta", ",");
                    molecule(e).Part("place", "at " + (s % 4 + 1) + ", " + (s % 8 + 1));
                    break;
                case 1: molecule(e.Part("action", "grabbed")); break;
                case 2: molecule(e.Part("action", "dropped")); break;
                default: e.Part("instruction", "arrow", ",").Part("heading", s % 2 == 0 ? "heading up" : "heading left"); break;
            }
            return e;
        }

        private static int _burst, _burstReactors, _burstCycle;
        private static object[] _burstObjects;

        private static void EmitBurst()
        {
            if (_burstObjects == null)
            {
                _burstObjects = new object[_burstReactors];
                _burstObjects[0] = Narrator.OpenReactor() ?? new object();
                for (int r = 1; r < _burstReactors; r++) _burstObjects[r] = new object();
                var log = Patches.RunCapture.Log;
                _burstCycle = log.IsEmpty ? 0 : log.Groups[log.Groups.Count - 1] + 1;
            }
            int perCycle = _burstReactors * 2;
            for (int emitted = 0; emitted < _burst; _burstCycle++)
                for (int i = 0; i < perCycle && emitted < _burst; i++, emitted++)
                    Narrator.Emit(Step(_burstObjects[i / 2], i / 2, i % 2, _burstCycle), speakable: false, cycle: _burstCycle);
        }

        private static string Fill(int n, int reactors)
        {
            var objects = new object[reactors];
            objects[0] = Narrator.OpenReactor() ?? new object();
            for (int r = 1; r < reactors; r++) objects[r] = new object();
            long before = GC.GetTotalMemory(true);
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            int start = Patches.RunCapture.Log.IsEmpty ? 0 : Patches.RunCapture.Log.Groups[Patches.RunCapture.Log.Groups.Count - 1] + 1;
            var sw = Stopwatch.StartNew();
            int perCycle = reactors * 2, emitted = 0, cycle = start;
            while (emitted < n)
            {
                for (int i = 0; i < perCycle && emitted < n; i++, emitted++)
                    Narrator.Emit(Step(objects[i / 2], i / 2, i % 2, cycle - start), speakable: false, cycle: cycle);
                cycle++;
            }
            sw.Stop();
            int d0 = GC.CollectionCount(0) - g0, d1 = GC.CollectionCount(1) - g1, d2 = GC.CollectionCount(2) - g2;
            long after = GC.GetTotalMemory(true);
            return string.Format("filled {0} events over {1} cycles in {2} ms ({3:F2} us/event); GC gen0 {4}, gen1 {5}, gen2 {6}; held {7} MB -> {8} MB ({9} bytes/event); log entries {10}\n",
                n, cycle - start, sw.ElapsedMilliseconds, sw.Elapsed.TotalMilliseconds * 1000 / Math.Max(1, n),
                d0, d1, d2, before / (1024 * 1024), after / (1024 * 1024), (after - before) / Math.Max(1, n), Patches.RunCapture.Log.EntryCount);
        }

        // ---- frame timing ----

        private static bool _registered;
        private static int _framesLeft, _framesTotal;
        private static readonly List<double> _times = new List<double>();
        private static readonly List<double> _ticks = new List<double>();
        private static readonly Stopwatch _clock = new Stopwatch();
        private static int _g0, _g1, _g2;
        private static long _alloc0;
        private static int _cycle0, _entries0;
        private static readonly Stopwatch _wall = new Stopwatch();
        private static string _result;

        private static string StartFrames(int n)
        {
            if (!_registered) { FrameLoop.Register("logstress", Tick); _registered = true; }
            AppDomain.MonitoringIsEnabled = true;
            _times.Clear();
            _ticks.Clear();
            _framesLeft = _framesTotal = Math.Max(1, n);
            _clock.Reset();
            _g0 = GC.CollectionCount(0); _g1 = GC.CollectionCount(1); _g2 = GC.CollectionCount(2);
            _alloc0 = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
            _cycle0 = global::Class258.int_1;
            _entries0 = Patches.RunCapture.Log.EntryCount;
            _wall.Restart();
            _result = null;
            return "timing the next " + n + " frames\n";
        }

        private static void Tick()
        {
            if (_framesLeft <= 0) return;
            if (_clock.IsRunning) { _times.Add(_clock.Elapsed.TotalMilliseconds); _ticks.Add(FrameLoop.LastTickMs); }
            if (_burst > 0 && _framesLeft > 1) EmitBurst();
            _clock.Reset();
            _clock.Start();
            if (--_framesLeft > 0) return;
            _clock.Stop();
            var sorted = new List<double>(_times);
            sorted.Sort();
            double sum = 0;
            foreach (var t in sorted) sum += t;
            long alloc = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize - _alloc0;
            var sb = new StringBuilder();
            sb.AppendFormat("{0} frames: avg {1:F1} ms, p95 {2:F1} ms, max {3:F1} ms; GC gen0 {4}, gen1 {5}, gen2 {6}; allocated {7:F1} MB ({8:F0} KB/frame)\n",
                sorted.Count, sum / Math.Max(1, sorted.Count),
                sorted.Count > 0 ? sorted[(int)(sorted.Count * 0.95)] : 0, sorted.Count > 0 ? sorted[sorted.Count - 1] : 0,
                GC.CollectionCount(0) - _g0, GC.CollectionCount(1) - _g1, GC.CollectionCount(2) - _g2,
                alloc / (1024.0 * 1024.0), alloc / 1024.0 / Math.Max(1, _framesTotal));
            double secs = _wall.Elapsed.TotalSeconds;
            sb.AppendFormat("run: {0:F0} cycles/s, {1:F0} log entries/s ({2:F0} per frame)\n",
                (global::Class258.int_1 - _cycle0) / secs, (Patches.RunCapture.Log.EntryCount - _entries0) / secs,
                (Patches.RunCapture.Log.EntryCount - _entries0) / (double)Math.Max(1, _framesTotal));
            var ticks = new List<double>(_ticks);
            ticks.Sort();
            double tsum = 0;
            foreach (var t in ticks) tsum += t;
            sb.AppendFormat("mod tick (the previous frame's; a burst's emits included): avg {0:F2} ms, p95 {1:F2} ms, max {2:F2} ms\n",
                tsum / Math.Max(1, ticks.Count), ticks.Count > 0 ? ticks[(int)(ticks.Count * 0.95)] : 0, ticks.Count > 0 ? ticks[ticks.Count - 1] : 0);
            _result = sb.ToString();
        }

        private static string Report() => _result ?? (_framesLeft > 0 ? "still timing (" + _framesLeft + " frames left)\n" : "no measurement\n");
    }
}
#endif
