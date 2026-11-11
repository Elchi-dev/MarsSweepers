using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace MarsProbe
{
    [BepInPlugin(Guid, "MarsProbe", Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "marsmod.marsprobe";
        public const string Version = "0.1.3";

        internal static new ManualLogSource Log;
        internal static ConfigEntry<bool> TracerEnabled, AutoSnapshots;
        internal static ConfigEntry<int> TracerMax;

        public override void Load()
        {
            Log = base.Log;
            // Off by default since 2026-11-11: on 2026-11-10 a start died 4 s in, right after the tracer patched 200 game
            // methods, and Windows then attached a compatibility fix to the game that kept BepInEx out.
            TracerEnabled = Config.Bind("Tracer", "On", false, "Count calls to event-like game methods (Sell, Save, Build…). Research only: it patches hundreds of game methods and once crashed the game at startup.");
            TracerMax = Config.Bind("Tracer", "MaxMethods", 200, "How many game methods the tracer may patch at most.");
            AutoSnapshots = Config.Bind("Snapshots", "Auto", true, "Take snapshots automatically at the main menu and after a world loads.");
            Config.Save();

            try
            {
                Out.Init(Paths.GameRootPath);
            }
            catch (Exception e)
            {
                Log.LogError("MarsProbe could not create its report folder: " + e);
                return;
            }

            Out.Report("MarsProbe " + Version + " report");
            Out.Report("Started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Out.Report("Game root: " + Paths.GameRootPath);
            Out.Report("BepInEx: " + typeof(BasePlugin).Assembly.GetName().Version + "  " + System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(BasePlugin).Assembly.Location).ProductVersion);
            Out.Report("Patcher present: " + File.Exists(Path.Combine(Paths.PatcherPluginPath, "MoonSweepers.ModEngine", "MoonSweepers.ModEngine.dll")));
            Out.Report("Other plugins: " + string.Join(", ", Directory.GetFiles(Paths.PluginPath, "*.dll", SearchOption.AllDirectories).Select(Path.GetFileName)));
            if (Out.NewlySkipped.Count > 0)
            {
                Out.Report("!! The previous run crashed inside these units, they are skipped from now on:");
                foreach (var k in Out.NewlySkipped) Out.Report("   " + k);
            }
            Out.Report("");
            Out.Report("Managed assemblies loaded at plugin start:");
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.GetName().Name))
                Out.Report($"  {a.GetName().Name} {a.GetName().Version}");

            try
            {
                AddComponent<ProbeBehaviour>();
                Out.Report("CHECK injected MonoBehaviour: OK");
            }
            catch (Exception e)
            {
                Out.Report("CHECK injected MonoBehaviour: FAILED " + e);
                Log.LogError("MarsProbe could not add its behaviour: " + e);
                return;
            }

            Scheduler.Add("catalog", Catalog.Build());
            Scheduler.Add("catalog-summary", CatalogSummary());
            if (TracerEnabled.Value) Scheduler.Add("tracer", Tracer.PatchAll(TracerMax.Value));
            Msg("MarsProbe loaded. Snapshots: \"snap\" in the Mars dev terminal. Output: <game>/MarsProbe/Report");
        }

        /// <summary>Called by the Mars mod's dev terminal ("snap").</summary>
        public static string RequestSnapshot()
        {
            if (Scheduler.Busy) return "MarsProbe is busy, try again in a moment.";
            Scheduler.Add("snapshot", Snapshot.Run("manual", includeStatics: true));
            return "Snapshot started; the console says when it is finished.";
        }

        /// <summary>Called by the dev terminal ("tint").</summary>
        public static void ToggleTint() => Tests.ToggleTint();

        internal static void Msg(string s)
        {
            Log?.LogMessage(s);
        }

        static IEnumerator<bool> CatalogSummary()
        {
            Out.Report("");
            Out.Report($"=== CATALOG: {Catalog.TotalClasses} classes in the runtime; game: {Catalog.GameClasses.Count} classes, {Catalog.TotalGameFields} fields, {Catalog.TotalGameMethods} methods");
            Out.Report($"    MonoBehaviours: {Catalog.GameClasses.Count(c => c.IsMB)}, ScriptableObjects: {Catalog.GameClasses.Count(c => c.IsSO)}, enums: {Catalog.GameClasses.Count(c => c.IsEnum)}");
            Out.Report("Images:");
            foreach (var l in Catalog.ImageSummary) Out.Report("  " + l);
            Out.Report("");
            Out.Report("Interesting game types by keyword:");
            foreach (var kv in Catalog.Interesting)
                Out.Report($"  [{kv.Key}] " + string.Join(", ", kv.Value.Take(60)) + (kv.Value.Count > 60 ? $" …(+{kv.Value.Count - 60})" : ""));
            Msg($"Catalogue done: {Catalog.GameClasses.Count} game classes.");
            yield break;
        }
    }

    /// <summary>Runs queued work a few milliseconds per frame so the game never freezes for long.</summary>
    internal static class Scheduler
    {
        static readonly Queue<(string name, IEnumerator<bool> job)> jobs = new Queue<(string, IEnumerator<bool>)>();
        static (string name, IEnumerator<bool> job)? current;
        public static bool Busy => current != null || jobs.Count > 0;

        public static void Add(string name, IEnumerator<bool> job) => jobs.Enqueue((name, job));

        public static void Tick(double budgetMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < budgetMs)
            {
                if (current == null)
                {
                    if (jobs.Count == 0) return;
                    current = jobs.Dequeue();
                }
                bool more;
                try { more = current.Value.job.MoveNext(); }
                catch (Exception e)
                {
                    Out.Report($"JOB {current.Value.name} crashed: {e}");
                    Plugin.Log.LogError($"MarsProbe job {current.Value.name}: {e}");
                    more = false;
                }
                if (!more) current = null;
            }
        }
    }

    public class ProbeBehaviour : MonoBehaviour
    {
        public ProbeBehaviour(IntPtr ptr) : base(ptr) { }

        float nextSceneCheck, nextCountsWrite = 30f, pendingSnapshotAt = -1f;
        string lastScene, pendingLabel;
        int autoTaken;
        bool keysWork;

        void Update()
        {
            float now = Time.realtimeSinceStartup;
            try
            {
                // Keys moved to the Mars mod's dev terminal (Ctrl+Shift+K): snap, cube, tint.

                if (Plugin.AutoSnapshots.Value && now >= nextSceneCheck)
                {
                    nextSceneCheck = now + 1f;
                    string sc = Snapshot.SafeActiveScene();
                    if (sc != lastScene)
                    {
                        Out.Report($"[{now:F0}s] active scene -> '{sc}'");
                        lastScene = sc;
                        if (autoTaken < 4)
                        {
                            // Give the scene time to finish loading the world before we look at it.
                            pendingSnapshotAt = now + (autoTaken == 0 ? 20f : 40f);
                            pendingLabel = "auto_" + sc;
                        }
                    }
                }

                if (pendingSnapshotAt > 0 && now >= pendingSnapshotAt && !Scheduler.Busy)
                {
                    pendingSnapshotAt = -1f;
                    autoTaken++;
                    Scheduler.Add("snapshot", Snapshot.Run(pendingLabel, includeStatics: autoTaken > 1));
                }

                if (now >= nextCountsWrite)
                {
                    nextCountsWrite = now + 30f;
                    Tracer.WriteCountsRoot();
                }

                Scheduler.Tick(25);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("MarsProbe Update: " + e.Message);
            }
        }

        void Keys()
        {
            BepInEx.IInputSystem input;
            try { input = UnityInput.Current; } catch { return; }
            if (input == null) return;
            if (input.GetKeyDown(KeyCode.F7))
            {
                if (!keysWork) { keysWork = true; Out.Report("CHECK keyboard input: OK"); }
                if (Scheduler.Busy) Plugin.Msg("MarsProbe is still busy, try F7 again in a moment.");
                else Scheduler.Add("snapshot", Snapshot.Run("manual", includeStatics: true));
            }
            if (input.GetKeyDown(KeyCode.F9)) { keysWork = true; Tests.SpawnCube(); }
            if (input.GetKeyDown(KeyCode.F10)) { keysWork = true; Tests.ToggleTint(); }
        }
    }
}
