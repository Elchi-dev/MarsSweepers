using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MarsMod
{
    /// <summary>
    /// Writes what the game keeps about belts, machines, cargo and power in a running world, a few levels deep,
    /// by reflection over the interop objects. This is the map for building Mars belts and power on top of the
    /// game's own, so it is most useful in a world with belts, packagers, presses and solar panels running.
    /// </summary>
    internal static class Deep
    {
        static readonly string[] WorldFields =
        {
            "conveyors", "beltPaths", "beltQueues", "beltStartAt", "beltEndAt", "machinePaths", "machineInputAt", "machineOutputs",
            "machineTurns", "beltOwed", "crossingByMachine", "enteredBelts", "lastInput", "leftBelts", "garageOutfeeds",
            "packagerStatus", "packagerShares", "production", "staleProduction", "powerNodes", "powerGrids", "balancedGrids",
            "powerLinks", "powerAnchors", "canisters", "carriedCanisters", "depots", "productionClock", "productionNow",
            "beltSteps", "nextCanisterId", "nextConveyorId", "machineSnaps",
        };

        static readonly HashSet<string> Skip = new HashSet<string> { "Pointer", "ObjectClass", "WasCollected", "isWrapped", "pooledPtr" };
        const int MaxItems = 60, MaxDepth = 4;

        public static IEnumerable<string> Dump(string extra)
        {
            var w = Commands.FindWorld();
            if (w == null) { yield return "Load a world first."; yield break; }
            var sb = new StringBuilder();
            sb.AppendLine($"# deep dump {DateTime.Now:yyyy-MM-dd HH:mm}, {(MarsState.CurrentIsMars ? "Mars" : "Moon")} slot {MarsState.ActiveSlot}");
            var names = WorldFields.ToList();
            if (!string.IsNullOrEmpty(extra)) names.AddRange(extra.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0));
            foreach (var n in names)
            {
                sb.AppendLine();
                sb.AppendLine("## " + n);
                try
                {
                    var p = w.GetType().GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p == null) { sb.AppendLine("(no such field)"); continue; }
                    Write(sb, p.GetValue(w), 0, "");
                }
                catch (Exception e) { sb.AppendLine("(failed: " + (e.InnerException ?? e).Message + ")"); }
            }
            // The kit that draws belts, and the scene's machine components.
            foreach (var typeName in new[] { "MoonCleaners.MoonConveyorNetwork", "MoonCleaners.MoonMachineBelt", "MoonCleaners.MoonPackagerView",
                                             "MoonCleaners.MoonStonePressView", "MoonCleaners.MoonPressureChamberView", "MoonCleaners.MoonSolarPanelView",
                                             "MoonCleaners.MoonDustCanisterView" })
            {
                sb.AppendLine();
                sb.AppendLine("## scene: " + typeName);
                try
                {
                    var t = typeof(MoonCleaners.MoonWorld).Assembly.GetType(typeName);
                    if (t == null) { sb.AppendLine("(type not found)"); continue; }
                    var all = UnityEngine.Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.From(t));
                    sb.AppendLine($"{all.Length} objects");
                    for (int i = 0; i < Math.Min(all.Length, 4); i++)
                    {
                        var o = Activator.CreateInstance(t, all[i].Pointer);
                        sb.AppendLine($"- #{i}");
                        Write(sb, o, 1, "  ");
                    }
                }
                catch (Exception e) { sb.AppendLine("(failed: " + (e.InnerException ?? e).Message + ")"); }
            }
            string path = Path.Combine(BepInEx.Paths.BepInExRootPath, "MarsMod", "research", $"deep-{(MarsState.CurrentIsMars ? "mars" : "moon")}.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString());
            yield return $"Belts, machines and power written to BepInEx/MarsMod/research/{Path.GetFileName(path)} ({sb.Length / 1024} KB).";
        }

        static void Write(StringBuilder sb, object o, int depth, string indent)
        {
            if (o == null) { sb.AppendLine(indent + "null"); return; }
            var t = o.GetType();
            if (Simple(t)) { sb.AppendLine(indent + Fmt(o)); return; }
            if (o is UnityEngine.Object uo && !(o is MoonCleaners.MoonDepotBuildDefinition))
            {
                string nm = "?";
                try { nm = uo.name; } catch { }
                if (depth > 1) { sb.AppendLine($"{indent}<{t.Name} '{nm}'>"); return; }
            }
            if (TryItems(o, out var items, out int count))
            {
                sb.AppendLine($"{indent}{t.Name} n={count}");
                if (depth >= MaxDepth) return;
                int k = 0;
                foreach (var it in items)
                {
                    if (k++ >= MaxItems) { sb.AppendLine(indent + "  …"); break; }
                    sb.Append(indent + "  [" + (k - 1) + "] ");
                    if (it != null && Simple(it.GetType())) sb.AppendLine(Fmt(it));
                    else { sb.AppendLine(); Write(sb, it, depth + 1, indent + "    "); }
                }
                return;
            }
            if (depth >= MaxDepth) { sb.AppendLine(indent + Fmt(o)); return; }
            sb.AppendLine($"{indent}{t.Name}");
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (Skip.Contains(p.Name) || p.GetIndexParameters().Length > 0) continue;
                object v;
                try { v = p.GetValue(o); } catch { continue; }
                if (v == null || Simple(v.GetType())) { sb.AppendLine($"{indent}  {p.Name} = {Fmt(v)}"); continue; }
                sb.AppendLine($"{indent}  {p.Name}:");
                Write(sb, v, depth + 1, indent + "    ");
            }
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object v;
                try { v = f.GetValue(o); } catch { continue; }
                sb.AppendLine($"{indent}  {f.Name} = {Fmt(v)}");
            }
        }

        static bool Simple(Type t) =>
            t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) ||
            t.FullName == "UnityEngine.Vector3" || t.FullName == "UnityEngine.Vector2" || t.FullName == "UnityEngine.Quaternion" ||
            t.FullName == "UnityEngine.Color" || t.FullName == "Il2CppSystem.Decimal";

        static string Fmt(object o)
        {
            if (o == null) return "null";
            try
            {
                if (o is Il2CppSystem.Decimal d) return Il2CppSystem.Decimal.ToDouble(d).ToString("0.###");
                if (o is float f) return f.ToString("0.####");
                if (o is double db) return db.ToString("0.####");
                if (o is Il2CppSystem.Object io) { try { return io.ToString(); } catch { } }
                return o.ToString();
            }
            catch { return "?"; }
        }

        /// <summary>Lists, dictionaries, sets, queues and arrays of the interop runtime, by their shape.</summary>
        static bool TryItems(object o, out IEnumerable<object> items, out int count)
        {
            items = null; count = 0;
            var t = o.GetType();
            var countP = t.GetProperty("Count") ?? t.GetProperty("Length");
            if (countP == null || countP.PropertyType != typeof(int)) return false;
            try { count = (int)countP.GetValue(o); } catch { return false; }
            var get = t.GetMethod("GetEnumerator", Type.EmptyTypes);
            if (get != null)
            {
                items = Enumerate(get.Invoke(o, null));
                return true;
            }
            var idx = t.GetProperty("Item", new[] { typeof(int) });
            if (idx != null)
            {
                int n = count;
                items = Enumerable.Range(0, n).Select(i => { try { return idx.GetValue(o, new object[] { i }); } catch { return null; } });
                return true;
            }
            return false;
        }

        static IEnumerable<object> Enumerate(object en)
        {
            if (en == null) yield break;
            var t = en.GetType();
            var move = t.GetMethod("MoveNext", Type.EmptyTypes);
            var cur = t.GetProperty("Current");
            if (move == null || cur == null)
            {
                if (en is System.Collections.IEnumerator e) while (e.MoveNext()) yield return e.Current;
                yield break;
            }
            int guard = 0;
            while (guard++ < 100000)
            {
                bool ok;
                try { ok = (bool)move.Invoke(en, null); } catch { yield break; }
                if (!ok) yield break;
                object c = null;
                try { c = cur.GetValue(en); } catch { }
                yield return c;
            }
        }
    }
}
