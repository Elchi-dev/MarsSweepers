using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MoonCleaners;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// Test cheats, Moon and Mars. The game ships developer helpers (MoonWorld.DevFinishAllButOne,
    /// DevCompleteRegions, DevResetDust, DevSetCarriedDust); these commands call them, plus Mars ones that go to the
    /// host like every other Mars change. Everything is logged with a [cheat] tag.
    /// </summary>
    internal static class Cheats
    {
        public static bool InfinitePower;
        public static Vector3? LastRegion;

        public static readonly (string usage, string what)[] Help =
        {
            ("finishmoon", "CHEAT: clean every sector but one (the game's own dev helper); 'tp last' goes to the last one"),
            ("clean [rings]", "CHEAT: clean the sectors around you (default 3 rings)"),
            ("resetdust yes", "CHEAT: put all the dust back on the planet"),
            ("fill [kg]", "CHEAT: fill your container with dust (default: full)"),
            ("fillstorage [n]", "CHEAT: n of every Mars material into the crew storage (default 100)"),
            ("infinitepower [on|off]", "CHEAT: every Mars building gets full power"),
            ("jam | unjam", "CHEAT: dust every Mars machine up completely / clean them all"),
            ("goal next | goal all", "CHEAT: finish the current Mars goal / all of them (bonuses paid)"),
            ("buildall", "CHEAT: one of every Mars building in a ring around you (free)"),
            ("selftest", "run the automatic Mars test (on a TEST world: it builds things); writes research/selftest.txt"),
        };

        // ------------------------------------------------------------------ terminal (any PC)

        public static bool Command(string c, string[] a, Action<string> say)
        {
            string Arg(int i) => i < a.Length ? a[i].ToLowerInvariant() : "";
            var world = Commands.FindWorld();
            var walker = Commands.FindWalker();
            var me = walker != null ? walker.transform.position : Vector3.zero;
            switch (c)
            {
                case "finishmoon":
                case "cleanall":
                {
                    if (!MarsNet.IsHost) { say("Only the host can do that."); return true; }
                    if (world == null) { say("Go into a world first."); return true; }
                    int n = world.DevFinishAllButOne(me, out Vector3 last);
                    LastRegion = last;
                    Log($"finishmoon: {n} sectors done, last at {last}");
                    say($"{n} sectors cleaned. The last one is {Vector3.Distance(me, last):0} m away to the {Haze.Compass((last - me).normalized)}: 'tp last'.");
                    return true;
                }
                case "clean":
                {
                    if (!MarsNet.IsHost) { say("Only the host can do that."); return true; }
                    int rings = a.Length > 1 && int.TryParse(a[1], out int r) ? Mathf.Clamp(r, 0, 60) : 3;
                    int n = world.DevCompleteRegions(me, rings);
                    Log($"clean {rings}: {n} sectors");
                    say($"{n} sectors around you cleaned.");
                    return true;
                }
                case "resetdust":
                {
                    if (Arg(1) != "yes") { say("This puts ALL the dust back on the whole planet. Type 'resetdust yes'."); return true; }
                    if (!MarsNet.IsHost) { say("Only the host can do that."); return true; }
                    bool ok = world.DevResetDust();
                    Log("resetdust: " + ok);
                    say(ok ? "All the dust is back." : "The game refused (it said no).");
                    return true;
                }
                case "fill":
                {
                    double kg = a.Length > 1 && double.TryParse(a[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var k) ? k : 1e6;
                    try { kg = Math.Min(kg, world.ContainerCapacityMultiplier(Buildings.Me) * world.settings.inventoryCapacityKg * 1000.0); } catch { }
                    world.DevSetCarriedDust(Buildings.Me, kg);
                    Log($"fill {kg:0.##} kg");
                    say($"Container filled ({kg:0.##} kg asked; the game caps it at your capacity).");
                    return true;
                }
                case "fillstorage": Ask("fillstorage", a.Length > 1 ? a[1] : "100"); say("Asked the host to fill the crew storage."); return true;
                case "infinitepower": Ask("power", Arg(1) == "off" ? "off" : "on"); return true;
                case "jam": Ask("jam", "1"); return true;
                case "unjam": Ask("jam", "0"); return true;
                case "goal": Ask("goal", Arg(1) == "all" ? "all" : "next"); return true;
                case "buildall": Ask("buildall", $"{me.x.ToString(System.Globalization.CultureInfo.InvariantCulture)};{me.y.ToString(System.Globalization.CultureInfo.InvariantCulture)};{me.z.ToString(System.Globalization.CultureInfo.InvariantCulture)}"); say("Asked the host to build one of everything around you."); return true;
                case "selftest": say(SelfTest.Start(Arg(1))); return true;
            }
            return false;
        }

        static void Ask(string what, string arg) => Buildings.Ask(Buildings.Op.Cheat, w => { w.Write(what); w.Write(arg ?? ""); });

        public static void Log(string text)
        {
            Plugin.Log.LogMessage("[cheat] " + text);
            try
            {
                string path = Path.Combine(BepInEx.Paths.BepInExRootPath, "MarsMod", "research", "cheats.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}");
            }
            catch { }
        }

        // ------------------------------------------------------------------ host

        public static string Apply(string what, string arg, string player)
        {
            Log($"{player}: {what} {arg}");
            switch (what)
            {
                case "fillstorage":
                {
                    int n = int.TryParse(arg, out var v) ? Mathf.Clamp(v, 1, 100000) : 100;
                    foreach (var id in MarsItems.All.Keys) Buildings.AddTo(Buildings.Items.Storage, id, n);
                    Buildings.StockChanged();
                    return $"Crew storage: +{n} of every Mars material.";
                }
                case "power":
                    InfinitePower = arg != "off";
                    return "Infinite power " + (InfinitePower ? "ON: every Mars building gets full power." : "off.");
                case "jam":
                {
                    float d = arg == "1" ? 1f : 0f; int n = 0;
                    foreach (var b in Buildings.Data.List) if (Buildings.Jams(b)) { b.Dust = d; n++; }
                    Buildings.Changed();
                    return d > 0 ? $"{n} machines jammed." : $"{n} machines clean.";
                }
                case "goal":
                {
                    int target = arg == "all" ? 99 : Goals.Data.Done + 1;
                    return Goals.ForceTo(target);
                }
                case "buildall":
                {
                    var parts = arg.Split(';');
                    var center = new Vector3(F(parts[0]), F(parts[1]), F(parts[2]));
                    return BuildAll(center, out _);
                }
            }
            return null;
        }

        static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Host: one of every Mars building on a ring around the centre (N-menu buildings, free). Returns a summary.</summary>
        public static string BuildAll(Vector3 center, out List<string> lines)
        {
            lines = new List<string>();
            var world = Commands.FindWorld();
            bool wasFree = Buildings.FreeBuild;
            Buildings.FreeBuild = true;
            int ok = 0;
            var up = center.normalized;
            var e = Vector3.Cross(up, Vector3.forward); if (e.sqrMagnitude < 1e-4f) e = Vector3.right; e.Normalize();
            var n = Vector3.Cross(up, e);
            var defs = MarsCatalog.All.ToList();
            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];
                string result = null;
                // Up to 8 tries per building: further out each time.
                for (int t = 0; t < 8; t++)
                {
                    Vector3 pos;
                    if (def.NeedsMine)
                    {
                        var m = Mines.All.Where(x => !Buildings.Data.List.Any(b => b.Mine == x.Id)).OrderBy(x => Vector3.Distance(x.Position, center)).FirstOrDefault();
                        if (m == null) { result = "no free mine"; break; }
                        pos = m.Position;
                    }
                    else
                    {
                        float ang = (i + t * 0.37f) / defs.Count * Mathf.PI * 2f, r = 18f + t * 6f + (i % 2) * 5f;
                        var dir = (center + (e * Mathf.Cos(ang) + n * Mathf.Sin(ang)) * r).normalized;
                        pos = world.Terrain.SurfacePoint(dir);
                    }
                    string why = Buildings.CanPlace(def, pos, out var mine);
                    if (why != null) { result = why; continue; }
                    var rot = Quaternion.FromToRotation(Vector3.up, pos.normalized);
                    Buildings.PlaceDirect(def, pos, rot, mine, "selftest");
                    result = null; ok++;
                    break;
                }
                lines.Add($"  {def.Id}: {(result == null ? "built" : "NOT built: " + result)}");
            }
            Buildings.FreeBuild = wasFree;
            Buildings.Changed();
            foreach (var l in lines) Log("buildall" + l);
            return $"Built {ok} of {defs.Count} Mars buildings around you (N-menu buildings: remove them with the N menu or 'selftest clean').";
        }
    }
}
