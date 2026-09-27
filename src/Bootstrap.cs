using System;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace SpeechChem
{
    /// <summary>
    /// The mod's only entry point. SpaceChem is a single .NET x86 executable running Zachtronics' own
    /// SDL2/OpenGL engine ("Impeller" — NOT Unity), built against CLR 2.0. Our SpaceChem.exe.config
    /// (deploy/) moves the process to CLR 4 and names this class as the AppDomainManager, so the CLR
    /// instantiates it before any game code runs.
    ///
    /// TWO APPDOMAINS. SpaceChem.Program.Main, run with no arguments, does not play the game: it
    /// configures sqlite and then loops { create AppDomain "SANDBOX"; ExecuteAssembly("SpaceChem.exe",
    /// new Evidence(), "--game"); unload } for as long as the game returns 1 (its in-game restart). The
    /// game lives in SANDBOX, so the manager instance in the DEFAULT domain only opens the log; every
    /// SANDBOX domain boots the mod afresh and tears it down on unload. (That ExecuteAssembly overload
    /// takes Evidence, which CLR 4 rejects unless the config enables NetFx40_LegacySecurityPolicy —
    /// verified: without it the process dies with NotSupportedException before the window opens.)
    ///
    /// Inside SANDBOX, SpaceChem.exe is not loaded yet when the domain is created, so hooking waits for
    /// the game assembly's AssemblyLoad event (still strictly before its entry point runs): bind
    /// GameState, attach the init/tick patches, load the reloadable module.
    ///
    /// Install = copy into the game folder: SpaceChem.exe.config (replaces the stock one; deleting ours
    /// and restoring the stock file = vanilla), SpeechChem.dll, SpeechChem.Module.dll, Mono.Cecil.dll,
    /// 0Harmony.dll, prism.dll, steam_appid.txt, SpeechChem\. The config binds this assembly by FULL
    /// display name, so AssemblyVersion is pinned in the csproj — bump both in lockstep or the mod
    /// silently stops loading.
    /// </summary>
    public sealed class Bootstrap : AppDomainManager
    {
        private const string SteamAppId = "92800";
        private const string GameAssemblyName = "SpaceChem";
        private const string SandboxDomainName = "SANDBOX";

        private static Modularity.ModHost _modHost;
        private static Modularity.ModuleLoader _moduleLoader;
        private static bool _tickErrorLogged;
        private static bool _hooked;
        private static string _gameDir;

        public override void InitializeNewDomain(AppDomainSetup appDomainInfo)
        {
            base.InitializeNewDomain(appDomainInfo);
            try
            {
                if (AppDomain.CurrentDomain.IsDefaultAppDomain())
                {
                    // The launcher half of Program.Main: nothing to hook here. One fresh log per
                    // process starts now; every SANDBOX generation appends to it.
                    Log.Init(fresh: true);
                    Log.Info("SpeechChem: default domain up (CLR " + Environment.Version + "); waiting for the game's SANDBOX domain.");
                    return;
                }
                if (AppDomain.CurrentDomain.FriendlyName != SandboxDomainName) return;
                Boot();
            }
            catch (Exception ex)
            {
                // An exception escaping InitializeNewDomain kills the domain before the game starts.
                // Whatever went wrong, the user must still get their vanilla game.
                try { Log.Error("Bootstrap failed — game continues without accessibility hooks.", ex); }
                catch { }
            }
        }

        private static void Boot()
        {
            Log.Init(fresh: false);
            Log.Info("SpeechChem bootstrap (AppDomainManager, domain " + AppDomain.CurrentDomain.FriendlyName + ") starting.");

            _gameDir = AppDomain.CurrentDomain.BaseDirectory;
            try { Directory.SetCurrentDirectory(_gameDir); }
            catch (Exception ex) { Log.Error("Failed to set working directory to " + _gameDir, ex); }

            EnsureSteamAppId(_gameDir);

            // Speech first, so failures further down can still be announced. The localized greeting
            // itself comes from the module (localization is module-side); the host speaks only
            // emergency strings — see the CLAUDE.md localization rule's host exemption.
            if (!Speech.Tts.Init())
                Log.Warning("Speech unavailable — continuing so the game still launches.");

            // SANDBOX is unloaded when the game exits or restarts itself; give speech and the dev
            // server an orderly release so the next generation can take them over.
            AppDomain.CurrentDomain.DomainUnload += (s, e) => OnDomainUnload();

            // The module's references (this assembly, Harmony, the game) normally bind via appbase
            // probing; the resolver answers with the already-loaded copy if fusion ever misses
            // (byte-loaded assemblies resolve through the default Load context).
            AppDomain.CurrentDomain.AssemblyResolve += ResolveLoadedByName;

            GameNames.Load(Path.Combine(_gameDir, "SpeechChem", "namemap.tsv"));

            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                if (IsGameAssembly(a)) { Hook(a); break; }

#if DEBUG
            try { Dev.DevServer.Instance.Start(); }
            catch (Exception ex) { Log.Error("Dev server failed to start", ex); }
#endif
        }

        private static bool IsGameAssembly(Assembly a)
        {
            try { return !a.IsDynamic && a.GetName().Name == GameAssemblyName; }
            catch { return false; }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            try
            {
                if (_hooked || !IsGameAssembly(args.LoadedAssembly)) return;
                Hook(args.LoadedAssembly);
            }
            catch (Exception ex) { Log.Error("[boot] hooking the game assembly failed", ex); }
        }

        private static void Hook(Assembly game)
        {
            if (_hooked) return;
            _hooked = true;
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            Log.Info("Game assembly: " + game.FullName + " (" + game.Location + ")");

            GameState.Bind(game);
            if (!ApplyPatches())
                Log.Warning("Harmony patches failed to attach — the game will still run, just without accessibility hooks.");

            // The reloadable feature module (see Modularity/).
            _modHost = new Modularity.ModHost();
            _moduleLoader = new Modularity.ModuleLoader(Path.Combine(_gameDir, "SpeechChem.Module.dll"), _modHost);
            if (!_moduleLoader.Reload())
                Speech.Tts.Speak("Speech Chem could not load its features module. The game will run without accessibility.");
        }

        private static void OnDomainUnload()
        {
            try { Log.Info("SANDBOX domain unloading — releasing speech."); } catch { }
#if DEBUG
            try { Dev.DevServer.Instance.Stop(); } catch { }
#endif
            try { _moduleLoader?.Module?.Dispose(); } catch { }
            try { Speech.Tts.Shutdown(); } catch { }
        }

        /// <summary>The per-frame heartbeat — the tick prefix's one call. Dev pump first (so a /reload
        /// swaps before the module runs this frame), then the module, read fresh so a swap is just a
        /// field assignment. A throwing module logs once per reload, not once per frame.</summary>
        internal static void TickFrame()
        {
#if DEBUG
            try { Dev.DevServer.Instance.Pump(); }
            catch (Exception ex) { Log.Error("[host] dev pump failed", ex); }
#endif
            var module = _moduleLoader?.Module;
            if (module == null) return;
            try
            {
                module.Tick();
                _tickErrorLogged = false;
            }
            catch (Exception ex)
            {
                if (!_tickErrorLogged)
                {
                    _tickErrorLogged = true;
                    Log.Error("[module] Tick threw — suppressing repeats until it recovers or reloads", ex);
                }
            }
        }

        /// <summary>The live module generation (dev tooling reaches its types through this).</summary>
        internal static Modularity.IModModule CurrentModule => _moduleLoader?.Module;

        /// <summary>Called by the init postfix: the game is up, the main menu exists.</summary>
        internal static void OnGameInitialized()
        {
            if (_modHost != null) _modHost.GameInitialized = true;
        }

        /// <summary>Swap in the current on-disk module. Main thread only (the dev server routes
        /// /reload through its main-thread queue). Returns a status line.</summary>
        internal static string ReloadModule()
        {
            if (_moduleLoader == null) return "[no module loader]\n";
            bool ok = _moduleLoader.Reload();
            _tickErrorLogged = false;
#if DEBUG
            // The REPL holds references into the old module's types; start it fresh.
            if (ok) { try { Dev.DevServer.Instance.ResetEvaluator(); } catch { } }
#endif
            return (ok ? "reloaded: generation " + _moduleLoader.Generation : "[reload failed] see the log") + "\n";
        }

        private static Assembly ResolveLoadedByName(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                if (!a.IsDynamic && a.GetName().Name == name) return a;
            return null;
        }

        /// <summary>Write steam_appid.txt next to the game so Steamworks accepts a launch that didn't
        /// come from Steam. Without it, Program.Main's Steam check (RestartAppIfNecessary(92800)) makes
        /// the process exit while Steam starts a fresh one — a cosmetic double launch rather than a mod
        /// failure, since the relaunched stock exe re-enters through the same config.</summary>
        private static void EnsureSteamAppId(string gameDir)
        {
            string path = Path.Combine(gameDir, "steam_appid.txt");
            try
            {
                if (!File.Exists(path) || File.ReadAllText(path).Trim() != SteamAppId)
                {
                    File.WriteAllText(path, SteamAppId);
                    Log.Info("Wrote steam_appid.txt (" + SteamAppId + ").");
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Could not write steam_appid.txt (" + ex.Message + "). " +
                    "A Steam-relaunch bounce may occur; the relaunched game still loads the mod.");
            }
        }

        private static bool ApplyPatches()
        {
            try
            {
                var init = GameState.InitMethod;
                var tick = GameState.TickMethod;
                if (init == null || tick == null)
                {
                    Log.Error("[patch] init/tick methods unresolved (init=" + (init != null) + ", tick=" + (tick != null) + ").");
                    return false;
                }

                var harmony = new Harmony("com.speechchem");
                var patches = typeof(Patches.GamePatches);
                harmony.Patch(init, postfix: new HarmonyMethod(patches.GetMethod(nameof(Patches.GamePatches.AfterInit))));
                harmony.Patch(tick, prefix: new HarmonyMethod(patches.GetMethod(nameof(Patches.GamePatches.BeforeTick))));
                Log.Info("[patch] attached postfix to init and prefix to tick.");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[patch] failed to attach Harmony patches", ex);
                return false;
            }
        }
    }
}
