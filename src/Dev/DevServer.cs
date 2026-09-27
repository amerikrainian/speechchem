#if DEBUG
using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;

namespace SpeechChem.Dev
{
    /// <summary>
    /// Dev-only in-process driver, ON by default in Debug builds (SPEECHCHEM_DEV=0 turns it off). Exposes a
    /// loopback HTTP server so an external driver (Claude, curl) can introspect and drive the live mod/game
    /// while it runs — the test harness copied over from the WrathAccess architecture, trimmed to what the
    /// SpaceChem mod needs:
    ///
    ///   GET  /health         liveness.
    ///   POST /say            speak the request body through the real speech path (also lands in /speech).
    ///   GET  /speech?since=N lines the mod has spoken since cursor N (we can't hear the TTS, so this is
    ///                        how we observe it). Tapped at the Tts chokepoint.
    ///   GET  /screen         the active screen + the whole screen chain (bottom → top), by deob type name.
    ///   GET  /gui            reflection dump of the active screen (fields by ordinal + deob name).
    ///   GET  /screenshot     capture the game window to a PNG (path returned).
    ///   POST /reload         hot-swap the module from disk.
    ///   POST /probe          "command [argument]" → the live module's typed probe (SpeechChem.Dev.Probe):
    ///                        push/pop game screens by deob type, dispatch input actions, …
    ///   POST /eval           body = C# source, run against the live game on the main thread (REPL state
    ///                        persists across calls); returns captured output + result/errors.
    ///
    /// /eval and /screen run on the game's main thread: HTTP requests enqueue a job and block until
    /// <see cref="Pump"/> (called once per frame from the Class185.vmethod_3 prefix) executes it. /say and
    /// /speech are thread-safe and answer directly off the HTTP thread.
    ///
    /// This whole subsystem is compiled only in DEBUG (#if DEBUG) — a Release build has none of it. In
    /// Debug it starts with every launch unless SPEECHCHEM_DEV=0.
    /// </summary>
    internal sealed class DevServer
    {
        public static readonly DevServer Instance = new DevServer();

        public const string EnableEnv = "SPEECHCHEM_DEV";
        public const string PortEnv = "SPEECHCHEM_DEV_PORT";
        public const string MarkerFile = "devserver.enable"; // in the working dir (the game folder)
        private const int DefaultPort = 8773; // WotR uses 8771, Echopunks 8772; keep ours distinct.

        // ON by default in Debug builds (user decision, 2026-09-27: a plain Steam launch of a dev
        // build should come up drivable). SPEECHCHEM_DEV=0 opts out. Release builds compile none of
        // this, so shipped zips never listen. The marker file is kept as an explicit record only.
        private static bool DevEnabled(out string how)
        {
            string env = Environment.GetEnvironmentVariable(EnableEnv);
            if (env == "0") { how = "disabled by " + EnableEnv + "=0"; return false; }
            if (env == "1") { how = "env"; return true; }
            try
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(Environment.CurrentDirectory, MarkerFile)))
                { how = "marker"; return true; }
            }
            catch { }
            how = "debug default";
            return true;
        }

        private sealed class Job
        {
            public Func<string> Work;
            public string Result = "";
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        private readonly SpeechLog _speech = new SpeechLog();
        private readonly CSharpEvaluator _evaluator = new CSharpEvaluator();
        private readonly ConcurrentQueue<Job> _jobs = new ConcurrentQueue<Job>();
        private DevHttpServer _http;
        private bool _enabled;

        public void Start()
        {
            string how;
            if (!DevEnabled(out how)) { Log.Info("Dev server off (" + how + ")."); return; }

            int port = DefaultPort;
            string p = Environment.GetEnvironmentVariable(PortEnv);
            if (!string.IsNullOrEmpty(p)) int.TryParse(p, out port);

            // Tap every string the mod speaks into the ring buffer so /speech can read it back.
            SpeechChem.Speech.Tts.Observer = _speech.Add;

            try
            {
                _http = new DevHttpServer(port, HandleRequest);
                _http.Start();
                _enabled = true;
                Log.Info("Dev server on http://127.0.0.1:" + port + " (gate: " + how + "; GET /health /speech /screen /gui /screenshot, POST /say /eval /reload /probe)");
            }
            catch (Exception e)
            {
                Log.Error("Dev server failed to start", e);
            }
        }

        /// <summary>Release the port. The game unloads its SANDBOX AppDomain on exit and on its own
        /// restart; the next generation's server must be able to bind the same port.</summary>
        public void Stop()
        {
            _enabled = false;
            try { _http?.Stop(); } catch { }
            _http = null;
        }

        /// <summary>Drop the REPL so the next /eval builds a fresh evaluator — required after a module
        /// hot-reload (the old evaluator holds references into the retired module's types).</summary>
        public void ResetEvaluator() => _evaluator.Reset();

        /// <summary>Run queued main-thread jobs. Called once per frame from the game's tick prefix.</summary>
        public void Pump()
        {
            if (!_enabled) return;
            Job job;
            while (_jobs.TryDequeue(out job))
            {
                try { job.Result = job.Work() ?? ""; }
                catch (Exception e) { job.Result = "[host error] " + e + "\n"; }
                job.Done.Set();
            }
        }

        private string OnMainThread(Func<string> work, int timeoutSeconds = 30)
        {
            var job = new Job { Work = work };
            _jobs.Enqueue(job);
            if (!job.Done.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
                return "[timeout] main thread did not run the job within " + timeoutSeconds + "s (frozen / not ticking?)\n";
            return job.Result;
        }

        // Runs on the HTTP thread.
        private string HandleRequest(string method, string path, string body)
        {
            string route = path;
            string query = "";
            int q = path.IndexOf('?');
            if (q >= 0) { route = path.Substring(0, q); query = path.Substring(q + 1); }

            if (route == "/health" || route == "/") return "ok\n";

            if (route == "/say" && method == "POST")
            {
                string text = (body ?? "").Trim();
                if (text.Length == 0) return "[empty] POST the text to speak as the body\n";
                SpeechChem.Speech.Tts.Speak(text, interrupt: true);
                return "said: " + text + "\n";
            }

            if (route == "/speech" && method == "GET")
            {
                long since = 0;
                foreach (string kv in query.Split('&'))
                    if (kv.StartsWith("since=", StringComparison.Ordinal))
                        long.TryParse(kv.Substring("since=".Length), out since);
                long next;
                string lines = _speech.Render(since, out next);
                return "cursor: " + next + "\n" + lines;
            }

            if (route == "/screen" && method == "GET")
                return OnMainThread(DumpScreens);

            if (route == "/reload" && method == "POST")
                // Main thread: module Load/Dispose touch Harmony and game state mid-frame otherwise.
                return OnMainThread(Bootstrap.ReloadModule);

            if (route == "/gui" && method == "GET")
                return OnMainThread(ObjectDumper.DumpActiveScreen);

            if (route == "/screenshot" && method == "GET")
                return Screenshot.Capture(); // DWM read, no game-state touch — fine off-thread

            if (route == "/probe" && method == "POST")
                return OnMainThread(() => RunProbe(body));

            if (route == "/eval" && method == "POST")
            {
                if (string.IsNullOrWhiteSpace(body)) return "[empty] POST C# source as the request body\n";
                return OnMainThread(() => _evaluator.Eval(body));
            }

            return "[404] " + method + " " + route + "\n";
        }

        /// <summary>Forward "command [argument]" to the LIVE module generation's typed probe
        /// (SpeechChem.Dev.Probe.Run) — the module can name deob game types, /eval cannot.</summary>
        private static string RunProbe(string body)
        {
            var module = Bootstrap.CurrentModule;
            if (module == null) return "[no module loaded]\n";
            var probe = module.GetType().Assembly.GetType("SpeechChem.Dev.Probe");
            var run = probe?.GetMethod("Run", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (run == null) return "[probe missing] the loaded module has no SpeechChem.Dev.Probe.Run (Release build?)\n";
            string text = (body ?? "").Trim();
            int space = text.IndexOf(' ');
            string cmd = space < 0 ? text : text.Substring(0, space);
            string arg = space < 0 ? "" : text.Substring(space + 1).Trim();
            try { return (string)run.Invoke(null, new object[] { cmd, arg }); }
            catch (System.Reflection.TargetInvocationException ex) { return "[probe threw] " + ex.InnerException + "\n"; }
        }

        private static string DumpScreens()
        {
            if (!GameState.Bound) return "[not bound] GameState hasn't resolved the game yet\n";
            var names = GameState.ScreenStackNames();
            if (names.Count == 0) return "(no screens on the stack yet)\n";
            var sb = new StringBuilder();
            sb.Append("active: ").Append(names[names.Count - 1]).Append('\n');
            sb.Append("stack (bottom -> top):\n");
            for (int i = 0; i < names.Count; i++)
                sb.Append("  ").Append(i).Append(": ").Append(names[i]).Append('\n');
            return sb.ToString();
        }
    }
}
#endif
