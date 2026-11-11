using System;
using System.Collections.Generic;
using System.Linq;
using MoonCleaners;
using UnityEngine;
using static MarsMod.MarsCatalog;

namespace MarsMod
{
    /// <summary>
    /// What every Mars building does, driven by its catalogue entry: extractors dig, recipe machines turn inputs
    /// into outputs, generators burn fuel, solar panels and batteries feed the grid, the shield raises its dome,
    /// storage and the market trade. The host runs it; the crew reads the result from the synced records.
    /// </summary>
    internal static class Machines
    {
        static Dictionary<string, int> Items(Buildings.Rec b) => b.Items;

        public const int AutoKeep = 20;          // auto-selling keeps this many of each material
        public const float AutoRatio = 1.1f;     // and sells only at 10 % over the normal price

        // ------------------------------------------------------------------ production (host, every 0.5 s)

        public static bool Produce(Buildings.Rec b, Def def, float dt, float power)
        {
            bool changed = false;
            float slow = (1f - b.Dust * 0.5f) * power;     // dust slows a machine down, so does a weak grid
            switch (def.Role)
            {
                case Role.Extractor:
                {
                    var mine = Mines.ById(b.Mine);
                    if (mine == null || Buildings.Total(b.Items) >= def.Cap) break;
                    b.Progress += dt * slow / def.Seconds;
                    while (b.Progress >= 1f && Buildings.Total(b.Items) < def.Cap) { b.Progress -= 1f; Buildings.AddTo(b.Items, mine.Ore, 1); Goals.Produced(mine.Ore, 1); changed = true; }
                    if (Buildings.Total(b.Items) >= def.Cap) b.Progress = 0f;
                    break;
                }
                case Role.Recipe:
                {
                    bool haveIn = def.In.All(kv => Buildings.Count(b.Items, kv.Key) >= kv.Value);
                    bool room = def.Out.All(kv => Buildings.Count(b.Items, kv.Key) + kv.Value <= def.Cap);
                    if (!haveIn || !room) { b.Progress = 0f; break; }
                    b.Progress += dt * slow / def.Seconds;
                    if (b.Progress >= 1f)
                    {
                        b.Progress = 0f;
                        foreach (var kv in def.In) Buildings.AddTo(b.Items, kv.Key, -kv.Value);
                        foreach (var kv in def.Out) { Buildings.AddTo(b.Items, kv.Key, kv.Value); Goals.Produced(kv.Key, kv.Value); }
                        changed = true;
                    }
                    break;
                }
                case Role.Market:
                {
                    if (!b.Active) break;
                    b.Progress += dt / 30f;
                    if (b.Progress < 1f) break;
                    b.Progress = 0f;
                    double money = 0; int sold = 0;
                    foreach (var kv in Buildings.Items.Storage.ToList())
                    {
                        int n = kv.Value - AutoKeep;
                        if (n <= 0 || Market.Price(kv.Key) < MarsItems.Sell(kv.Key) * AutoRatio) continue;
                        money += Market.Sell(kv.Key, n); sold += n;
                        Buildings.AddTo(Buildings.Items.Storage, kv.Key, -n);
                    }
                    if (sold > 0)
                    {
                        Buildings.StockChanged();
                        Buildings.Earn(Commands.FindWorld(), Buildings.Me, money);
                        Plugin.Log.LogMessage($"[market] auto-sold {sold} items for ${money:0}");
                        MarsWorld.Notice($"Market board sold {sold} items for ${money:0}");
                    }
                    break;
                }
                case Role.Hub:
                {
                    b.Progress += dt * power / 5f;
                    if (b.Progress < 1f) break;
                    b.Progress = 0f;
                    if (Haul(b, def)) changed = true;
                    break;
                }
            }
            return changed;
        }

        /// <summary>The logistics hub's round: outputs of every Mars machine in range to the crew storage, inputs back.</summary>
        static bool Haul(Buildings.Rec hub, Def def)
        {
            var store = Buildings.Items.Storage;
            int moved = 0;
            foreach (var r in Buildings.Data.List)
            {
                if (r == hub || r.Owner == Aliens.Owner || (r.Pos - hub.Pos).sqrMagnitude > def.Radius2 * def.Radius2) continue;
                var d = Get(r.Type);
                if (d == null || Buildings.Jammed(r)) continue;
                switch (d.Role)
                {
                    case Role.Extractor:
                        foreach (var kv in r.Items.ToList()) { Buildings.AddTo(store, kv.Key, kv.Value); moved += kv.Value; }
                        r.Items.Clear();
                        break;
                    case Role.Recipe:
                    case Role.Generator:
                        foreach (var kv in d.Out)
                        {
                            int n = Buildings.Count(r.Items, kv.Key);
                            if (n <= 0) continue;
                            Buildings.AddTo(r.Items, kv.Key, -n); Buildings.AddTo(store, kv.Key, n); moved += n;
                        }
                        foreach (var kv in d.In)
                        {
                            int want = Math.Min(d.Cap, d.Role == Role.Generator ? 5 : kv.Value * 5);
                            int n = Math.Min(want - Buildings.Count(r.Items, kv.Key), Buildings.Count(store, kv.Key));
                            if (n <= 0) continue;
                            Buildings.AddTo(store, kv.Key, -n); Buildings.AddTo(r.Items, kv.Key, n); moved += n;
                        }
                        break;
                }
            }
            if (moved > 0) Buildings.StockChanged();
            hub.Burn = moved;          // shown in the prompt: items moved in the last round
            return moved > 0;
        }

        /// <summary>Is this machine doing work right now (for its moving parts and the grid's demand).</summary>
        public static bool Busy(Buildings.Rec b, Def def)
        {
            if (Buildings.Jammed(b)) return false;
            switch (def.Role)
            {
                case Role.Extractor: return b.Mine >= 0 && Buildings.Total(b.Items) < def.Cap;
                case Role.Recipe:
                    return def.In.All(kv => Buildings.Count(b.Items, kv.Key) >= kv.Value) &&
                           def.Out.All(kv => Buildings.Count(b.Items, kv.Key) + kv.Value <= def.Cap);
                case Role.Shield: return Shields.Wanted;
                case Role.Cannon: return Buildings.Count(b.Items, "rocket_fuel") >= def.Cap;
                case Role.Hub: return true;   // charging draws power once it is loaded
            }
            return false;
        }

        // ------------------------------------------------------------------ using a building (host applies, any player asks)

        public static string Use(Buildings.Rec b, Def def, string player, bool alt, MoonWorld world)
        {
            var pack = Buildings.Backpack(player);
            if (def.Jams && Buildings.Jammed(b)) return $"The {def.Name} is jammed by storm dust: hold the interact key to scrub it.";
            switch (def.Role)
            {
                case Role.Extractor:
                {
                    int n = Buildings.Total(b.Items);
                    if (n == 0) return $"The {def.Name.ToLowerInvariant()} has nothing yet.";
                    foreach (var kv in b.Items.ToList()) Buildings.AddTo(pack, kv.Key, kv.Value);
                    b.Items.Clear();
                    return $"Took {n} {MarsItems.Name(Mines.ById(b.Mine)?.Ore ?? "ore").ToLowerInvariant()}.";
                }
                case Role.Recipe:
                case Role.Generator:
                {
                    // E: take what is made, then load what it needs from the backpack. Shift+E: only take.
                    var took = new List<string>();
                    foreach (var kv in def.Out)
                    {
                        int n = Buildings.Count(b.Items, kv.Key);
                        if (n <= 0) continue;
                        Buildings.AddTo(b.Items, kv.Key, -n); Buildings.AddTo(pack, kv.Key, n);
                        took.Add($"{n} {MarsItems.Name(kv.Key).ToLowerInvariant()}");
                    }
                    var loaded = new List<string>();
                    if (!alt)
                        foreach (var kv in def.In)
                        {
                            int room = def.Cap - Buildings.Count(b.Items, kv.Key);
                            int n = Math.Min(room, Buildings.Count(pack, kv.Key));
                            if (n <= 0) continue;
                            Buildings.AddTo(pack, kv.Key, -n); Buildings.AddTo(b.Items, kv.Key, n);
                            loaded.Add($"{n} {MarsItems.Name(kv.Key).ToLowerInvariant()}");
                        }
                    if (took.Count == 0 && loaded.Count == 0)
                        return def.In.Count > 0 ? $"The {def.Name.ToLowerInvariant()} needs {MarsItems.Cost(def.In)} per round. Bring it some." : "Nothing to take yet.";
                    return (took.Count > 0 ? "Took " + string.Join(", ", took) + ". " : "") + (loaded.Count > 0 ? "Loaded " + string.Join(", ", loaded) + "." : "");
                }
                case Role.Storage:
                {
                    if (alt)
                    {
                        double money = 0; int n = 0;
                        foreach (var kv in Buildings.Items.Storage.ToList()) { money += Market.Sell(kv.Key, kv.Value); n += kv.Value; }
                        if (n == 0) return "The crew storage is empty.";
                        Buildings.Items.Storage.Clear();
                        Buildings.Earn(world, player, money);
                        return $"Sold {n} items from the crew storage for ${money:0}.";
                    }
                    int moved = Buildings.Total(pack);
                    if (moved == 0) return "Your Mars backpack is empty. (Shift + interact sells the crew storage.)";
                    foreach (var kv in pack.ToList()) Buildings.AddTo(Buildings.Items.Storage, kv.Key, kv.Value);
                    pack.Clear();
                    return $"Stored {moved} items in the crew storage.";
                }
                case Role.Market:
                {
                    // E: sell the material that pays best right now (against its normal price). Shift+E: auto-selling on/off.
                    if (alt)
                    {
                        b.Active = !b.Active;
                        return b.Active ? $"Auto-selling ON: every 30 s it sells what the crew storage has beyond {AutoKeep} of each material, while its price is {AutoRatio * 100f - 100f:0}% or more above normal."
                                        : "Auto-selling off.";
                    }
                    var stock = Buildings.Items.Storage.Where(kv => kv.Value > 0).ToList();
                    if (stock.Count == 0) return "The crew storage is empty: bring materials to a container port first.";
                    var best = stock.OrderByDescending(kv => Market.Price(kv.Key) / Math.Max(0.01f, MarsItems.Sell(kv.Key))).First();
                    double money2 = Market.Sell(best.Key, best.Value);
                    Buildings.Items.Storage.Remove(best.Key);
                    Buildings.Earn(world, player, money2);
                    return $"Sold {best.Value} {MarsItems.Name(best.Key).ToLowerInvariant()} for ${money2:0} (the best price against normal right now).";
                }
                case Role.Solar:
                    if (b.Dust > 0.05f) { b.Dust = 0f; return "Wiped the dust off the panel."; }
                    return null;
                case Role.Trader: return Aliens.Trade(b, player, alt, world);
                case Role.Meteor: return Meteors.Take(b, player);
                case Role.Cannon:
                {
                    int fuel = Buildings.Count(b.Items, "rocket_fuel");
                    if (alt)
                    {
                        if (fuel < def.Cap) return $"The cannon needs {def.Cap} rocket fuel (it has {fuel}).";
                        if (b.Supply < 0.99f) return $"The cannon needs its full {def.PowerUse:0} kW (the grid gives {b.Supply * 100f:0}%). Build more power.";
                        Buildings.AddTo(b.Items, "rocket_fuel", -def.Cap);
                        Ending.Fire();
                        Goals.CannonFired();
                        return null;
                    }
                    int n = Math.Min(def.Cap - fuel, Buildings.Count(pack, "rocket_fuel"));
                    if (n <= 0) return fuel >= def.Cap ? "Loaded. Give it full power and fire (Shift + interact)." : "Bring rocket fuel.";
                    Buildings.AddTo(pack, "rocket_fuel", -n); Buildings.AddTo(b.Items, "rocket_fuel", n);
                    return $"Loaded {n} rocket fuel ({fuel + n}/{def.Cap}).";
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ what the prompt shows

        public static string Describe(Buildings.Rec b, Def def)
        {
            string name = $"<b>{def.Name}</b>";
            string power = Power.Line(b, def);
            switch (def.Role)
            {
                case Role.Extractor:
                {
                    var mine = Mines.ById(b.Mine);
                    int n = Buildings.Total(b.Items);
                    return $"{name}  ·  {(mine != null ? MarsItems.Name(mine.Ore) : "<color=#FF6B5A>not on a mine</color>")} {n}/{def.Cap}" +
                           (n >= def.Cap ? "  <color=#FFC266>FULL</color>" : $"  ·  {b.Progress * 100f:0}%") + power;
                }
                case Role.Recipe:
                {
                    string ins = string.Join(" + ", def.In.Select(kv => $"{MarsItems.Name(kv.Key).ToLowerInvariant()} {Buildings.Count(b.Items, kv.Key)}"));
                    string outs = string.Join(" + ", def.Out.Select(kv => $"{MarsItems.Name(kv.Key).ToLowerInvariant()} {Buildings.Count(b.Items, kv.Key)}/{def.Cap}"));
                    return $"{name}  ·  {ins}  →  {outs}" + (b.Progress > 0 ? $"  ·  {b.Progress * 100f:0}%" : "") + power;
                }
                case Role.Storage: return $"{name}  ·  crew storage: {MarsItems.List(Buildings.Items.Storage)}";
                case Role.Market:
                {
                    var lines = MarsItems.All.Keys.Select(id =>
                    {
                        int have = Buildings.Count(Buildings.Items.Storage, id);
                        return $"{MarsItems.Name(id)} ${Market.Price(id):0.0} {Market.Arrow(id)}" + (have > 0 ? $" <color=#7FD8F0>×{have}</color>" : "");
                    });
                    return $"{name}" + (b.Active ? "  <color=#7CFF9A>auto-selling</color>" : "") + "\n<size=18>" + string.Join("   ", lines) + "</size>";
                }
                case Role.Light: return $"{name}  ·  clears the storm air around it, carries power 25 m" + power;
                case Role.Solar: return $"{name}  ·  {Power.SolarNow(b, def):0.0} of {def.PowerMake:0} kW" + power;
                case Role.Battery: return $"{name}  ·  {b.Energy:0}/{def.Battery:0} kW·min ({b.Energy / Math.Max(1f, def.Battery) * 100f:0}%)" + power;
                case Role.Generator:
                    return $"{name}  ·  fuel {Buildings.Count(b.Items, "rocket_fuel")}/{def.Cap}" + (b.Burn > 0 ? $"  ·  running ({b.Burn:0} s of fuel left)" : "  ·  idle") + power;
                case Role.Trader: return Aliens.Describe(b, def);
                case Role.Meteor: return $"{name}  ·  {MarsItems.List(b.Items)}";
                case Role.Hub:
                {
                    int n = Buildings.Data.List.Count(r => r != b && r.Owner != Aliens.Owner && (r.Pos - b.Pos).sqrMagnitude <= def.Radius2 * def.Radius2 &&
                                                          Get(r.Type) is Def d2 && (d2.Role == Role.Extractor || d2.Role == Role.Recipe || d2.Role == Role.Generator));
                    return $"{name}  ·  serves {n} machine{(n == 1 ? "" : "s")} within {def.Radius2:0} m  ·  last round moved {b.Burn:0}" + power;
                }
                case Role.Cannon:
                    return $"{name}  ·  rocket fuel {Buildings.Count(b.Items, "rocket_fuel")}/{def.Cap}" + power;
                case Role.Shield:
                    Shields.Preview(b);
                    return $"{name}  ·  " + (Shields.IsUp(b) ? "<color=#7FD8F0>dome up</color>" : Shields.Wanted ? "<color=#FF6B5A>no power for the dome</color>" : "waiting for a storm") + power;
            }
            return name;
        }

        public static string Hint(Buildings.Rec b, Def def, string key)
        {
            if (def.Jams && b.Dust >= 0.3f && def.Role != Role.Solar) return $"[hold {key}] scrub off the dust";
            switch (def.Role)
            {
                case Role.Extractor: return $"[{key}] take the ore";
                case Role.Recipe: return $"[{key}] take products and load {MarsItems.Cost(def.In)}   [Shift+{key}] only take products";
                case Role.Generator: return $"[{key}] load rocket fuel";
                case Role.Storage: return $"[{key}] store your backpack   [Shift+{key}] sell the crew storage at market prices";
                case Role.Market: return $"[{key}] sell the best-paying material   [Shift+{key}] auto-selling {(b.Active ? "off" : "on")}";
                case Role.Solar: return b.Dust > 0.05f ? $"[{key}] wipe the dust off" : "";
                case Role.Trader: return Aliens.Hint(key);
                case Role.Meteor: return $"[{key}] take it all";
                case Role.Cannon: return $"[{key}] load rocket fuel   [Shift+{key}] FIRE at the Moon";
            }
            return "";
        }
    }
}
