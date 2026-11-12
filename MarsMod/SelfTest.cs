using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// `selftest`: an automatic test round of everything Mars, run by the host on a TEST world. It builds one of every
    /// Mars building around the player, feeds them, runs them fast, switches power on and off, calls a storm, a dust
    /// devil and a meteorite, trades with the aliens, presses E on every building, checks the dust tiers, the belts and
    /// the goals, renders every model with the game's own materials, and writes all of it with timings to
    /// BepInEx/MarsMod/research/selftest.txt (also in the BepInEx log with a [selftest] tag). `selftest clean`
    /// removes what it built. On a Moon world it only writes the environment, the belt research and the renders.
    /// </summary>
    internal static class SelfTest
    {
        sealed class Step { public string Name; public float Wait; public Action Run; }

        static readonly List<Step> steps = new List<Step>();
        static int index = -1;
        static float nextAt, startedAt;
        static string path;
        static Vector3 center;
        static Dictionary<int, Dictionary<string, int>> snap;
        static Dictionary<string, int> storeSnap;

        public static bool Running => index >= 0;

        static void L(string s)
        {
            Plugin.Log.LogMessage("[selftest] " + s);
            try { File.AppendAllText(path, $"{Time.realtimeSinceStartup - startedAt,7:0.0}s  {s}{Environment.NewLine}"); } catch { }
        }

        static void Try(string what, Action a)
        {
            try { a(); }
            catch (Exception e) { L($"FAIL {what}: {(e.InnerException ?? e).GetType().Name}: {(e.InnerException ?? e).Message}"); }
        }

        public static string Start(string arg)
        {
            if (arg == "clean") return Clean();
            if (Running) return "The self test is already running.";
            if (!MarsNet.IsHost) return "Only the host runs the self test.";
            var walker = Commands.FindWalker();
            if (walker == null || Commands.FindWorld() == null) return "Go into a world first.";
            center = walker.transform.position;
            path = Path.Combine(BepInEx.Paths.BepInExRootPath, "MarsMod", "research", "selftest.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, $"# Mars self test {DateTime.Now:yyyy-MM-dd HH:mm:ss}, Mars Sweepers {Plugin.Version}{Environment.NewLine}");
            startedAt = Time.realtimeSinceStartup;
            Build(MarsWorld.InMarsWorld);
            index = 0; nextAt = Time.realtimeSinceStartup + 0.5f;
            return $"Self test started ({steps.Count} steps, about {steps.Sum(s => s.Wait) / 60f:0} minutes). Stay near this spot, do not open menus. Progress: BepInEx log [selftest], result: research/selftest.txt.";
        }

        public static void Frame()
        {
            if (index < 0 || Time.realtimeSinceStartup < nextAt) return;
            if (index >= steps.Count) { index = -1; L("DONE"); MarsWorld.Urgent("Self test done: research/selftest.txt. Build the report with MarsSetup."); return; }
            var s = steps[index++];
            L($"== {index}/{steps.Count} {s.Name}");
            MarsWorld.Notice($"Self test {index}/{steps.Count}: {s.Name}");
            Try(s.Name, s.Run);
            nextAt = Time.realtimeSinceStartup + s.Wait;
        }

        static void Add(string name, float wait, Action run) => steps.Add(new Step { Name = name, Wait = wait, Run = run });

        static IEnumerable<Buildings.Rec> Mine() => Buildings.Data.List.Where(b => b.Owner == "selftest");

        static string Items(Dictionary<string, int> inv) => MarsItems.List(inv);

        static void Build(bool mars)
        {
            steps.Clear();
            Add("environment", 1f, () =>
            {
                L($"Mars world {MarsWorld.InMarsWorld}, host {MarsNet.IsHost}, hooks ok {Patches.Ok}, failed: {string.Join("; ", Patches.Failed)}");
                foreach (var l in MarsNet.Status()) L("net " + l);
                foreach (var l in VanillaBuild.Status()) L("build menu " + l);
                L("models available: " + string.Join(", ", Models.Available()));
                L($"mines {Mines.All.Count}: " + string.Join(", ", Mines.All.GroupBy(m => m.Ore).Select(g => $"{g.Key} {g.Count()}")));
                L($"alien sites {Mines.AlienSites.Count}, outposts {Buildings.Data.List.Count(b => b.Owner == Aliens.Owner)}, meteorites {Buildings.Data.List.Count(b => b.Owner == Meteors.Owner)}");
                L($"buildings before the test {Buildings.Data.List.Count}; crew storage {Items(Buildings.Items.Storage)}; wallet ${Buildings.Wallet():0}");
                L(Haze.Info());
                L($"dust tier here {DustTiers.Here}, devils now {DustDevils.Active.Count}, goals done {Goals.Data.Done}, belts on {Belts.Data.On}");
            });
            if (mars)
            {
                Add("setup: free building, money, noon, filled crew storage", 2f, () =>
                {
                    Buildings.FreeBuild = true;
                    Buildings.Earn(Commands.FindWorld(), Buildings.Me, 50000);
                    Commands.SetTime("noon", out string msg); L(msg);
                    foreach (var id in MarsItems.All.Keys) Buildings.AddTo(Buildings.Items.Storage, id, 200);
                    Buildings.StockChanged();
                    L($"wallet now ${Buildings.Wallet():0}");
                });
                Add("build one of every Mars building", 4f, () =>
                {
                    L(Cheats.BuildAll(center, out var lines));
                    foreach (var l in lines) L(l);
                });
                Add("views and models", 1f, () =>
                {
                    foreach (var b in Mine())
                    {
                        var v = Buildings.ViewOf(b.Id);
                        L($"  #{b.Id} {b.Type}: view {(v != null ? v.name : "MISSING")}, at {Vector3.Distance(b.Pos, center):0} m, mine {b.Mine}, ports {Belts.LocalPorts(MarsCatalog.Get(b.Type)).Count}");
                    }
                });
                Add("feed the machines, power as built (60 s at 5x)", 60f, () =>
                {
                    foreach (var b in Mine())
                    {
                        var d = MarsCatalog.Get(b.Type);
                        foreach (var kv in d.In) Buildings.AddTo(b.Items, kv.Key, Math.Min(d.Cap, kv.Value * 8));
                    }
                    Buildings.Changed();
                    Buildings.SpeedCheat = 5f;
                    snap = Mine().ToDictionary(b => b.Id, b => new Dictionary<string, int>(b.Items));
                });
                Add("production and power report (own grid)", 1f, () => { ReportProduction(); foreach (var l in Power.Report()) L(l); });
                Add("same with infinite power (30 s at 5x)", 30f, () =>
                {
                    Cheats.InfinitePower = true;
                    snap = Mine().ToDictionary(b => b.Id, b => new Dictionary<string, int>(b.Items));
                });
                Add("production report (infinite power)", 1f, () => { ReportProduction(); Cheats.InfinitePower = false; });
                Add("logistics hub (20 s)", 20f, () => { storeSnap = new Dictionary<string, int>(Buildings.Items.Storage); });
                Add("hub report", 1f, () =>
                {
                    var diff = MarsItems.All.Keys.Select(id => (id, d: Buildings.Count(Buildings.Items.Storage, id) - (storeSnap.TryGetValue(id, out var v) ? v : 0))).Where(x => x.d != 0);
                    L("crew storage change: " + string.Join(", ", diff.Select(x => $"{x.id} {x.d:+0;-0}")));
                    foreach (var b in Mine().Where(b => b.Type == "hub")) L("  " + Strip(Machines.Describe(b, MarsCatalog.Get("hub"))));
                });
                Add("press E and Shift+E on every building", 2f, () =>
                {
                    var world = Commands.FindWorld();
                    foreach (var b in Mine().ToList())
                    {
                        var d = MarsCatalog.Get(b.Type);
                        if (d.Role == MarsCatalog.Role.Cannon) { L($"  {b.Type}: skipped (would fire the ending)"); continue; }
                        L($"  {b.Type} prompt: {Strip(Machines.Describe(b, d))} | hint: {Strip(Machines.Hint(b, d, "E"))}");
                        L($"  {b.Type} E: {Machines.Use(b, d, Buildings.Me, false, world) ?? "(nothing)"}");
                        if (d.Role != MarsCatalog.Role.Storage) L($"  {b.Type} Shift+E: {Machines.Use(b, d, Buildings.Me, true, world) ?? "(nothing)"}");
                        if (d.Role == MarsCatalog.Role.Market && b.Active) Machines.Use(b, d, Buildings.Me, true, world);   // auto-selling back off
                    }
                    Buildings.Changed();
                });
                Add("storm with the shield (60 s)", 60f, () =>
                {
                    foreach (var b in Mine()) b.Dust = 0f;
                    L("storm: " + Haze.Command("now"));
                });
                Add("storm report", 1f, () =>
                {
                    L($"storm {Haze.Storm:0.00}, front {Haze.Front:0.00}, shields wanted {Shields.Wanted}");
                    foreach (var b in Mine())
                    {
                        var d = MarsCatalog.Get(b.Type);
                        L($"  {b.Type}: dust {b.Dust:0.00}{(Buildings.Jammed(b) ? " JAMMED" : "")}, covered {Shields.Covered(b.Pos)}, roofed {Buildings.Roofed(b)}, supply {b.Supply:0.00}{(d.Role == MarsCatalog.Role.Shield ? $", dome {(b.Active ? "UP" : "down")}" : "")}");
                    }
                    if (Camera.main != null) L($"camera covered by a dome: {Shields.Covered(Camera.main.transform.position)}");
                    L("storm off: " + Haze.Command("off"));
                });
                Add("dust devil (25 s)", 25f, () => { L(DustDevils.Force(center)); });
                Add("devil report", 1f, () =>
                {
                    L($"devils active {DustDevils.Active.Count}" + string.Join("", DustDevils.Active.Select(d => $"; #{d.Id} at {Vector3.Distance(d.Center, center):0} m, strength {d.Strength:0.00}")));
                    L("machine dust after the devil: " + string.Join(", ", Mine().Where(Buildings.Jams).Select(b => $"{b.Type} {b.Dust:0.00}")));
                });
                Add("meteorite", 3f, () => { L(Meteors.Drop(center) ?? "dropped"); });
                Add("take the meteorite", 1f, () =>
                {
                    var m = Buildings.Data.List.Where(b => b.Owner == Meteors.Owner).OrderBy(b => Vector3.Distance(b.Pos, center)).FirstOrDefault();
                    L(m == null ? "FAIL no meteorite found" : $"meteorite {Vector3.Distance(m.Pos, center):0} m away, view {(Buildings.ViewOf(m.Id) != null ? "yes" : "MISSING")}: {Meteors.Take(m, Buildings.Me)}");
                });
                Add("aliens", 1f, () =>
                {
                    var world = Commands.FindWorld();
                    foreach (var o in Buildings.Data.List.Where(b => b.Owner == Aliens.Owner))
                    {
                        L($"  outpost #{o.Id} {Vector3.Distance(o.Pos, center):0} m, view {(Buildings.ViewOf(o.Id) != null ? "yes" : "MISSING")}: 1 {Aliens.Text(Aliens.Current(o, 0))} | 2 {Aliens.Text(Aliens.Current(o, 1))}");
                        L($"    trade 1: {Aliens.Trade(o, Buildings.Me, false, world)}");
                        L($"    trade 2: {Aliens.Trade(o, Buildings.Me, true, world)}");
                    }
                });
                Add("dust tiers", 1f, () =>
                {
                    var up = center.normalized;
                    var e = Vector3.Cross(up, Vector3.forward); if (e.sqrMagnitude < 1e-4f) e = Vector3.right; e.Normalize();
                    var n = Vector3.Cross(up, e);
                    foreach (float r in new[] { 100f, 500f, 1500f, 3000f })
                        L($"  {r:0} m: " + string.Join(" ", Enumerable.Range(0, 8).Select(k => DustTiers.TierAt(center + (e * Mathf.Cos(k * Mathf.PI / 4f) + n * Mathf.Sin(k * Mathf.PI / 4f)) * r))));
                    try { L($"cleaning depth multiplier here {Commands.FindWorld().CleaningDepthMultiplier(Buildings.Me):0.###} (tier {DustTiers.Here})"); } catch { }
                });
                Add("goals and market", 1f, () =>
                {
                    foreach (var l in Goals.Report()) L(l);
                    foreach (var l in Market.Report()) L(l);
                });
            }
            Add("belts", 1f, () => { foreach (var l in BeltResearch.Report(center)) L(l); });
            Add("economy dump", 1f, () => { foreach (var l in Economy.Dump()) L(l); });
            Add("renders of every model with the game's materials (about a minute)", 60f, () =>
            {
                Commands.SetTime("noon", out _);
                foreach (var l in Research.Run()) L(l);
            });
            Add("wrap up", 1f, () =>
            {
                Buildings.SpeedCheat = 1f;
                Buildings.FreeBuild = false;
                Cheats.InfinitePower = false;
                L($"buildings now {Buildings.Data.List.Count}, of them from the test {Mine().Count()}; 'selftest clean' removes them");
            });
        }

        static void ReportProduction()
        {
            foreach (var b in Mine())
            {
                var d = MarsCatalog.Get(b.Type);
                if (d.Role != MarsCatalog.Role.Extractor && d.Role != MarsCatalog.Role.Recipe && d.Role != MarsCatalog.Role.Generator) continue;
                var before = snap != null && snap.TryGetValue(b.Id, out var s) ? s : new Dictionary<string, int>();
                var change = b.Items.Keys.Concat(before.Keys).Distinct().Select(k => (k, n: Buildings.Count(b.Items, k) - (before.TryGetValue(k, out var v) ? v : 0))).Where(x => x.n != 0);
                L($"  {b.Type}: busy {Machines.Busy(b, d)}, supply {b.Supply:0.00}, factor {Power.Factor(b, d):0.00}, dust {b.Dust:0.00}, burn {b.Burn:0}, change: {string.Join(", ", change.Select(x => $"{x.k} {x.n:+0;-0}"))}");
            }
        }

        static string Strip(string s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", "<[^>]+>", "").Replace("\n", " / ");

        static string Clean()
        {
            if (!MarsNet.IsHost) return "Only the host can do that.";
            int n = Buildings.Data.List.RemoveAll(b => b.Owner == "selftest");
            Buildings.Changed();
            return $"Removed {n} test buildings.";
        }
    }
}
