using System;
using System.Collections.Generic;
using System.Linq;
using MoonCleaners;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// EXPERIMENTAL, needs `beltmode on`: logic gates. A gate is a rule on one of the game's splitters (`gate ...` in the
    /// terminal while standing next to it). Mars canisters that match the rule go out of the splitter's first output,
    /// everything else out of the others. Rules:
    ///  * item &lt;id&gt;: that material.
    ///  * stock &lt;id&gt; &lt;low&gt; &lt;high&gt;: that material while the crew holds fewer than low (crew storage plus the
    ///    canisters already on the belts), until it holds high again: a gap so it does not flip on every canister.
    ///  * price &lt;id&gt; &lt;percent&gt;: that material while its market price is at least that percent of normal.
    /// How it works: the canister entering the splitter is remembered (EnterMachine prefix), and while the game checks
    /// which outputs are clear (RouteEntranceClear), the outputs that do not fit the rule are reported as blocked.
    /// Whether the game routes this way is not known yet; the first 60 checks are logged.
    /// </summary>
    internal static class LogicGates
    {
        public const int SplitterKind = 11;

        public sealed class Gate { public string Mode = "item", Item; public int Low, High; public float Percent = 110f; public bool Open; }
        public sealed class State { public Dictionary<int, Gate> Gates = new Dictionary<int, Gate>(); }

        const string Key = "gates";
        public static State Data = new State();
        static int currentMachine = -1, currentCanister = -1, logged;

        static LogicGates()
        {
            MarsData.Changed += key =>
            {
                if (key != "" && key != Key) return;
                Data = MarsData.Get<State>(Key);
                if (Data.Gates.Count > 0) Patches.EnsureGatePatch();
            };
        }

        public static void Init() { }

        // ------------------------------------------------------------------ terminal

        public static string Command(string[] a, Vector3 player)
        {
            if (!MarsNet.IsHost) return "Only the host sets gates.";
            if (!Belts.Data.On) return "Gates work on Mars items on belts: switch that on first with 'beltmode on' (experimental).";
            var w = Commands.FindWorld();
            int id = NearestSplitter(w, player, out float dist);
            if (id == int.MinValue) return "Stand next to one of the game's splitters (within 6 m).";
            string mode = a.Length > 1 ? a[1].ToLowerInvariant() : "";
            if (mode == "" || mode == "show")
                return Data.Gates.TryGetValue(id, out var g0) ? $"Splitter {id}: {Describe(g0)}" : $"Splitter {id} has no gate. gate item <id> | gate stock <id> <low> <high> | gate price <id> <percent> | gate off";
            if (mode == "off") { Data.Gates.Remove(id); Save(); return $"Splitter {id}: gate removed."; }
            string item = a.Length > 2 ? a[2].ToLowerInvariant() : "";
            if (!MarsItems.All.ContainsKey(item)) return "Unknown item. Items: " + string.Join(", ", MarsItems.All.Keys);
            var g = new Gate { Mode = mode, Item = item };
            if (mode == "stock")
            {
                if (a.Length < 5 || !int.TryParse(a[3], out g.Low) || !int.TryParse(a[4], out g.High) || g.High < g.Low) return "gate stock <id> <low> <high>   (high at least low)";
            }
            else if (mode == "price")
            {
                if (a.Length < 4 || !float.TryParse(a[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out g.Percent)) return "gate price <id> <percent of normal>";
            }
            else if (mode != "item") return "gate item <id> | gate stock <id> <low> <high> | gate price <id> <percent> | gate off";
            Data.Gates[id] = g; Save();
            Patches.EnsureGatePatch();
            return $"Splitter {id}: {Describe(g)}. Matching Mars canisters leave by its first output.";
        }

        static string Describe(Gate g) =>
            g.Mode == "stock" ? $"{MarsItems.Name(g.Item)} while the crew holds under {g.Low}, until {g.High} (now {Holding(g.Item)}, {(g.Open ? "open" : "closed")})"
            : g.Mode == "price" ? $"{MarsItems.Name(g.Item)} while its price is at least {g.Percent:0}% of normal (now {Market.Price(g.Item) / Math.Max(0.01f, MarsItems.Sell(g.Item)) * 100f:0}%)"
            : $"only {MarsItems.Name(g.Item)}";

        static void Save() { if (MarsNet.IsHost) MarsData.Set(Key, Data); }

        static int NearestSplitter(MoonWorld w, Vector3 p, out float dist)
        {
            int best = int.MinValue; dist = 6f;
            try
            {
                foreach (var kv in w.depots)
                {
                    var d = kv.Value;
                    if (d == null || d.kind != SplitterKind) continue;
                    float dd = Vector3.Distance(new Vector3(d.x, d.y, d.z), p);
                    if (dd < dist) { dist = dd; best = d.id; }
                }
            }
            catch { }
            return best;
        }

        /// <summary>Crew storage plus the canisters of that item already riding on belts.</summary>
        static int Holding(string item) => Buildings.Count(Buildings.Items.Storage, item) + Belts.Data.Carried.Count(kv => kv.Value == item);

        // ------------------------------------------------------------------ hooks

        /// <summary>From the EnterMachine prefix: which canister the splitter is handling right now.</summary>
        public static void Entering(MoonDepotSnapshot machine, int canister)
        {
            if (machine == null || machine.kind != SplitterKind) { currentMachine = -1; return; }
            currentMachine = machine.id; currentCanister = canister;
        }

        /// <summary>Postfix on RouteEntranceClear(machine, input, output): block the outputs the rule does not allow.</summary>
        public static void RouteCheck(int machine, int input, int output, ref bool clear)
        {
            if (!Belts.Data.On || Data.Gates.Count == 0) return;
            if (logged < 60) { logged++; Plugin.Log.LogMessage($"[gates] route check machine {machine} input {input} output {output} clear {clear} (current splitter {currentMachine}, canister {currentCanister})"); }
            if (!clear || machine != currentMachine || !Data.Gates.TryGetValue(machine, out var g)) return;
            bool match = Matches(g, currentCanister);
            bool wantFirst = match;
            bool isFirst = output == 0;
            if (isFirst != wantFirst) clear = false;
        }

        static bool Matches(Gate g, int canister)
        {
            if (!Belts.Data.Carried.TryGetValue(canister, out var item) || item != g.Item) return false;
            switch (g.Mode)
            {
                case "stock":
                {
                    int have = Holding(item);
                    if (g.Open && have >= g.High) g.Open = false;
                    else if (!g.Open && have < g.Low) g.Open = true;
                    return g.Open;
                }
                case "price": return Market.Price(item) >= MarsItems.Sell(item) * g.Percent / 100f;
            }
            return true;
        }

        public static void Reset() { Data = new State(); currentMachine = -1; logged = 0; }
    }
}
