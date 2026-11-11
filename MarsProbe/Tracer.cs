using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;

namespace MarsProbe
{
    /// <summary>
    /// Puts a tiny counting postfix on "event-like" game methods (Sell, Save, Spawn, Build, Upgrade…).
    /// Afterwards trace_timeline.txt shows which game methods ran, and when, while the player played.
    /// That tells us where to hook Mars features. It is also our "can we Harmony-patch this game" test.
    /// </summary>
    internal static class Tracer
    {
        static readonly string[] Verbs =
        {
            "Sell", "Ship", "Launch", "Pay", "Reward", "Bounty", "Buy", "Purchase", "Unlock", "Upgrade",
            "Save", "Load", "Spawn", "Build", "Place", "Demolish", "Dismantle", "Construct",
            "Complete", "Finish", "Teleport", "Join", "Leave", "Kick", "Host", "Connect", "Disconnect",
            "Rpc", "Cmd", "Receive", "Send", "Craft", "Produce", "Deposit", "Unload", "Catch", "Fire",
            "Pack", "Clean", "Sweep", "Collect", "Pickup", "PickUp", "Equip", "Enter", "Exit", "Sleep", "Wake",
            "Generate", "Create", "Init", "Setup", "SetUp", "Start", "Begin", "Region", "Sector",
            "Belt", "Conveyor", "Machine", "Cargo", "Power", "Grid", "Charge", "Process", "Packager", "Canister",
        };
        static readonly HashSet<string> Excluded = new HashSet<string> { "Update", "LateUpdate", "FixedUpdate", "OnGUI", "OnDrawGizmos", "OnRenderObject", "OnValidate" };
        /// <summary>Called thousands of times per second in the first report: not worth the overhead.</summary>
        static readonly HashSet<string> Hot = new HashSet<string>
        {
            "MoonLandforms.SpawnReserve", "MoonWorld.UnlockedSlots", "MoonWorld.LoadoutTool", "MoonWorld.PlayerUpgrades",
            "MoonWorld.PlayerLoadout", "MoonWorld.LoadoutSlotOf", "MoonDustStation.Launch", "MoonRadioCatalog.Load",
            "MoonAchievements.EnsureLoaded", "MoonAchievements.Unlock", "MoonWorld.LoadedKilograms",
        };

        class Stat { public string Name; public long Calls; public double FirstAt = -1, LastAt; }
        static readonly Dictionary<IntPtr, Stat> stats = new Dictionary<IntPtr, Stat>();
        static readonly object gate = new object();
        static readonly Stopwatch clock = Stopwatch.StartNew();
        static StreamWriter timeline;
        static int timelineLines;
        public static int Patched, Failed, Candidates;
        public static readonly List<string> Failures = new List<string>();
        static Harmony harmony;

        public static IEnumerator<bool> PatchAll(int maxMethods)
        {
            harmony = new Harmony("marsprobe.tracer");
            timeline = Out.Writer("trace_timeline.txt");
            timeline.WriteLine("# seconds-since-start  method  (first 25 calls per method, then only counted)");
            timeline.Flush();

            var imageNames = new HashSet<string>(Catalog.GameClasses.Select(c => Path.GetFileNameWithoutExtension(c.Image)));
            var asms = AppDomain.CurrentDomain.GetAssemblies().Where(a => imageNames.Contains(a.GetName().Name)).ToList();
            Out.Report($"Tracer: interop assemblies for game images: {string.Join(", ", asms.Select(a => a.GetName().Name))}");

            var cands = new List<(int prio, MethodInfo m)>();
            foreach (var a in asms)
            {
                Type[] types;
                try { types = a.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                foreach (var t in types)
                {
                    if (t.IsGenericTypeDefinition || t.ContainsGenericParameters || t.IsInterface || !typeof(Il2CppObjectBase).IsAssignableFrom(t)) continue;
                    MethodInfo[] ms;
                    try { ms = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly); }
                    catch { continue; }
                    foreach (var m in ms)
                    {
                        if (m.IsSpecialName || m.IsGenericMethodDefinition || m.IsAbstract || Excluded.Contains(m.Name)) continue;
                        if (Hot.Contains(t.Name + "." + m.Name)) continue;
                        int prio = Array.FindIndex(Verbs, v => m.Name.StartsWith(v) || m.Name.Contains(v));
                        if (prio < 0) continue;
                        if (!SafeSignature(m)) continue;
                        cands.Add((prio, m));
                    }
                }
            }
            Candidates = cands.Count;
            yield return true;

            int n = 0;
            var post = new HarmonyMethod(typeof(Tracer).GetMethod(nameof(Post), BindingFlags.Static | BindingFlags.NonPublic));
            foreach (var (prio, m) in cands.OrderBy(c => c.prio).ThenBy(c => c.m.DeclaringType.FullName))
            {
                if (Patched >= maxMethods) break;
                string name = m.DeclaringType.FullName + "." + m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name)) + ")";
                string key = "trace:" + name;
                if (Out.ShouldSkip(key)) continue;
                Out.Begin(key);
                try
                {
                    harmony.Patch(m, postfix: post);
                    lock (gate) stats[m.MethodHandle.Value] = new Stat { Name = name };
                    Patched++;
                }
                catch (Exception e)
                {
                    Failed++;
                    if (Failures.Count < 40) Failures.Add(name + " -> " + e.GetType().Name + ": " + e.Message);
                }
                Out.End(key);
                if (++n % 10 == 0) yield return true;
            }
            Out.Report($"Tracer: {Candidates} candidate methods, patched {Patched}, failed {Failed}");
            foreach (var f in Failures) Out.Report("  patch failed: " + f);
        }

        /// <summary>Only patch methods whose parameters and return value are references, primitives, strings or enums.
        /// Game structs passed by value go through interop wrappers that other modders saw corrupt the stack.</summary>
        static bool SafeSignature(MethodInfo m)
        {
            bool Ok(Type t)
            {
                if (t == typeof(void) || t == typeof(string) || t.IsPrimitive || t.IsEnum) return true;
                if (t.IsByRef || t.IsPointer || t.IsGenericParameter) return false;
                if (t.IsValueType) return false;
                return typeof(Il2CppObjectBase).IsAssignableFrom(t);
            }
            if (!Ok(m.ReturnType)) return false;
            foreach (var p in m.GetParameters()) if (!Ok(p.ParameterType)) return false;
            return true;
        }

        static void Post(MethodBase __originalMethod)
        {
            try
            {
                lock (gate)
                {
                    if (__originalMethod == null || !stats.TryGetValue(__originalMethod.MethodHandle.Value, out var s)) return;
                    double now = clock.Elapsed.TotalSeconds;
                    s.Calls++;
                    s.LastAt = now;
                    if (s.FirstAt < 0) s.FirstAt = now;
                    if (s.Calls <= 25 && timelineLines < 20000)
                    {
                        timelineLines++;
                        timeline.WriteLine($"{now,9:F2}  {s.Name}{(s.Calls == 25 ? "   (25 calls, now only counting)" : "")}");
                        if (timelineLines % 20 == 0) timeline.Flush();
                    }
                }
            }
            catch { }
        }

        public static void Flush()
        {
            lock (gate) { try { timeline?.Flush(); } catch { } }
        }

        public static void WriteCounts(string relPath)
        {
            List<Stat> list;
            lock (gate) list = stats.Values.Where(s => s.Calls > 0).OrderByDescending(s => s.Calls).ToList();
            Flush();
            using var w = Out.Writer(relPath);
            w.WriteLine($"# {list.Count} of {stats.Count} traced methods have been called so far.  calls  first  last  method");
            foreach (var s in list) w.WriteLine($"{s.Calls,10}  {s.FirstAt,8:F1}s  {s.LastAt,8:F1}s  {s.Name}");
        }

        public static void WriteCountsRoot() => WriteCounts("trace_counts.txt");
    }
}
