using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MoonCleaners;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// Numbers for balancing Mars against the Moon. Two parts:
    ///  * a quiet logger: every 15 s of play in any world (Moon too, it only reads), one CSV row with the team's
    ///    money, deposited kilograms, cleaned sections and bought upgrades, in BepInEx/MarsMod/economy/;
    ///  * the `economy` command: the whole upgrade tree with prices and effects, the money settings, the
    ///    section count and weight, and the current multipliers, in BepInEx/MarsMod/research/economy.txt.
    /// </summary>
    internal static class Economy
    {
        static string Dir => Path.Combine(BepInEx.Paths.BepInExRootPath, "MarsMod");
        static float nextRow, worldSince = -1f;
        static IntPtr world;
        static double lastWallet = double.NaN;
        static double income, spent;
        static string file;
        static IntPtr collectedFor;

        /// <summary>economy + deep dump + a MarsProbe snapshot (+ the reference renders when asked from the terminal).</summary>
        public static IEnumerable<string> Collect(bool renders)
        {
            var outp = new List<string>();
            try { outp.AddRange(Dump()); } catch (Exception e) { outp.Add("economy failed: " + e.Message); }
            try { outp.AddRange(Deep.Dump(null)); } catch (Exception e) { outp.Add("deep dump failed: " + e.Message); }
            try { outp.Add(Commands.Probe("RequestSnapshot") ?? "(MarsProbe not installed, no snapshot)"); } catch { }
            if (renders) try { Research.Request(s => outp.Add(s)); } catch { }
            return outp;
        }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void Tick(float now)
        {
            if (now < nextRow) return;
            nextRow = now + 15f;
            MoonWorld w = null;
            try { w = Commands.FindWorld(); } catch { }
            bool playing = w != null && Commands.FindWalker() != null;
            try { playing &= !MoonSession.Loading; } catch { }
            if (!playing) { world = IntPtr.Zero; return; }
            if (w.Pointer != world)
            {
                world = w.Pointer; worldSince = now; lastWallet = double.NaN; income = spent = 0;
                int slot = MarsState.ActiveSlot;
                file = Path.Combine(Dir, "economy", $"{(MarsState.CurrentIsMars ? "mars" : "moon")}-slot{slot}-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            }
            try { Row(w, now); } catch (Exception e) { Log.Once("economy-row", "[economy] " + e.Message); }
            // Once per world and game start, a minute in: write the research files by themselves.
            if (now - worldSince > 60f && collectedFor != world)
            {
                collectedFor = world;
                foreach (var l in Collect(false)) Plugin.Log.LogMessage("[collect] " + l);
            }
        }

        static void Row(MoonWorld w, float now)
        {
            string me = Buildings.Me;
            double wallet = D(w.WalletDollars(me));
            if (!double.IsNaN(lastWallet))
            {
                double d = wallet - lastWallet;
                if (d > 0) income += d; else spent -= d;
            }
            lastWallet = wallet;
            int regions = 0;
            try { regions = w.Regions.Count; } catch { }
            bool header = !File.Exists(file);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var sb = new StringBuilder();
            if (header) sb.AppendLine("minutes,wallet,income,spent,deposited_kg,sections_done,sections_total,tree_levels_mine,tree_levels_team,payout_x,price_x,bounty,buildings,crew");
            int crew = 1;
            try { crew = Math.Max(1, Lobby.OtherMembers().Count() + 1); } catch { }
            sb.Append(string.Join(",", new[]
            {
                F((now - worldSince) / 60f), F(wallet), F(income), F(spent), F(w.depositedKilograms),
                w.CompletedRegionCount.ToString(Inv), regions.ToString(Inv),
                w.TreeLevelsOwned(me).ToString(Inv), w.TreeLevelsTotal().ToString(Inv),
                F(D(w.PayoutMultiplier(me))), F(D(w.PriceMultiplier(me))), F(D(w.RegionBounty(me))),
                w.BuiltDepotCount.ToString(Inv), crew.ToString(Inv),
            })).AppendLine();
            File.AppendAllText(file, sb.ToString());
        }

        static double D(Il2CppSystem.Decimal d) => Il2CppSystem.Decimal.ToDouble(d);
        static string F(double v) => v.ToString("0.###", Inv);

        // ------------------------------------------------------------------ the `economy` command

        public static IEnumerable<string> Dump()
        {
            var w = Commands.FindWorld();
            if (w == null) { yield return "Load a world first (Moon or Mars, both work)."; yield break; }
            var lines = new List<string>();
            string me = Buildings.Me;
            void L(string s) => lines.Add(s);

            L($"# economy dump {DateTime.Now:yyyy-MM-dd HH:mm}, {(MarsState.CurrentIsMars ? "Mars" : "Moon")} slot {MarsState.ActiveSlot}");
            L("");
            L("## now");
            Try(() => L($"wallet ${D(w.WalletDollars(me)):0}  deposited {w.depositedKilograms:0.0} kg  sections {w.CompletedRegionCount}/{w.Regions.Count}  tree levels mine {w.TreeLevelsOwned(me)} team {w.TreeLevelsTotal()}"));
            Try(() => L($"payout x{D(w.PayoutMultiplier(me)):0.###}  price x{D(w.PriceMultiplier(me)):0.###}  section bounty ${D(w.RegionBounty(me)):0.##}"));
            Try(() => L($"cleaning width x{w.CleaningWidthMultiplier(me):0.###}  depth x{w.CleaningDepthMultiplier(me):0.###}  reach +{w.CleaningReachBonus(me):0.##}  container x{w.ContainerCapacityMultiplier(me):0.###}  move x{w.MoveSpeedMultiplier(me):0.###}  deposit time x{w.DepositTimeMultiplier(me):0.###}"));
            Try(() => L($"buildings {w.BuiltDepotCount}  robot metres cleaned {w.RobotStrokeMetresCleaned:0}"));

            L("");
            L("## settings");
            Try(() =>
            {
                var s = w.settings;
                L($"dollarsPerKilogram {s.dollarsPerKilogram}  sandValueMultiplier {s.sandValueMultiplier}  stoneValueMultiplier {s.stoneValueMultiplier}  dollarsPerSection {s.dollarsPerSection}");
                L($"inventoryCapacityKg {s.inventoryCapacityKg}  dustKilogramsPerLitre {s.dustKilogramsPerLitre}  regionDiameter {s.regionDiameter}  radius {s.radius}  dustThickness {s.dustThicknessMin}-{s.dustThicknessMax}  dustCoverage {s.dustCoverage}");
            });

            L("");
            L("## sections (sample)");
            Try(() =>
            {
                int n = w.Regions.Count, step = Math.Max(1, n / 400), count = 0;
                double kg = 0, area = 0, maxKg = 0;
                for (int id = 0; id < n; id += step)
                {
                    w.WeighHex(id, out double k, out double a);
                    kg += k; area += a; maxKg = Math.Max(maxKg, k); count++;
                }
                L($"{n} sections, {count} weighed: average {kg / Math.Max(1, count):0.00} kg and {area / Math.Max(1, count):0} m² each, heaviest {maxKg:0.00} kg; whole planet about {kg / Math.Max(1, count) * n:0} kg");
            });

            L("");
            L("## upgrade tree (id | title | branch | levels | prices | requires | tool | effect per level)");
            foreach (var (label, nodes) in new[] { ("tree", Nodes(false)), ("gear", Nodes(true)) })
            {
                L($"### {label}");
                foreach (var node in nodes)
                {
                    try
                    {
                        var prices = node.prices != null ? string.Join(" ", node.prices.Select(p => "$" + p)) : "";
                        string req = "";
                        try { req = Requires(node) + (node.requiresAny ? " (any)" : ""); } catch { req = "?"; }
                        var effects = new List<string>();
                        if (node.effect != null)
                            for (int lv = 1; lv <= node.maxLevel; lv++)
                                try { effects.Add(node.effect.Invoke(lv)); } catch { effects.Add("?"); }
                        L($"{node.id} | {node.title} | {node.branch} | {node.maxLevel} | {prices} | {req} | {node.tool} | {string.Join(" / ", effects)}");
                        if (!string.IsNullOrEmpty(node.summary)) L($"    {node.summary}");
                    }
                    catch (Exception e) { L($"(node failed: {e.Message})"); }
                }
            }

            L("");
            L("## building prices");
            Try(() =>
            {
                var defs = w.depotDefinition.additionalStructures;
                for (int k = 1; k <= defs.Length; k++)
                {
                    var d = defs[k - 1];
                    if (d == null || d.priceDollars <= 0) continue;
                    L($"{k} {d.displayName} ${d.priceDollars}");
                }
            });

            string path = Path.Combine(Dir, "research", "economy.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllLines(path, lines);
            yield return $"Economy written to BepInEx/MarsMod/research/economy.txt ({lines.Count} lines).";
            foreach (var l in lines.Skip(2).Take(4)) yield return l;

            void Try(Action a) { try { a(); } catch (Exception e) { L("(failed: " + e.Message + ")"); } }
        }

        /// <summary>(string id, int level)[]: the value tuple type is not in the compile-time stubs, so by reflection.</summary>
        static string Requires(MoonUpgradeTree.Node node)
        {
            var arr = node.GetType().GetProperty("requires")?.GetValue(node);
            if (arr == null) return "";
            int n = (int)arr.GetType().GetProperty("Length").GetValue(arr);
            var item = arr.GetType().GetProperty("Item");
            var parts = new List<string>();
            for (int i = 0; i < n; i++)
            {
                var t = item.GetValue(arr, new object[] { i });
                object a = R.Get(t, "Item1") ?? t.GetType().GetField("Item1")?.GetValue(t);
                object b = R.Get(t, "Item2") ?? t.GetType().GetField("Item2")?.GetValue(t);
                parts.Add(a + "@" + b);
            }
            return string.Join(" ", parts);
        }

        static IEnumerable<MoonUpgradeTree.Node> Nodes(bool gear)
        {
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<MoonUpgradeTree.Node> arr = null;
            try { arr = gear ? MoonUpgradeTree.Gear : MoonUpgradeTree.Nodes; } catch { }
            if (arr == null) yield break;
            for (int i = 0; i < arr.Length; i++) if (arr[i] != null) yield return arr[i];
        }
    }
}
