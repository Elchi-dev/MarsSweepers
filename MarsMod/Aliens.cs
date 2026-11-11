using System;
using System.Collections.Generic;
using System.Linq;
using MoonCleaners;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// Alien traders. Three small outposts stand on every Mars world (placed with the mines, the same on every PC).
    /// They want what the crew makes and pay in alien alloy, the one material Mars cannot make: the planet cannon
    /// needs it. Each outpost shows two offers that change every 8 minutes; what is given comes from the
    /// player's backpack first, then from the crew storage.
    /// </summary>
    internal static class Aliens
    {
        public const string Owner = "aliens";
        public const float OfferMinutes = 8f;

        public sealed class Offer
        {
            public Dictionary<string, int> Give = new Dictionary<string, int>();
            public double Dollars;
            public Dictionary<string, int> Get = new Dictionary<string, int>();
            public double PayDollars;
        }

        static Offer O(string giveItem, int giveN, string getItem, int getN) =>
            new Offer { Give = { [giveItem] = giveN }, Get = { [getItem] = getN } };

        // What the aliens trade. The first block pays alloy, the second sells the crew useful things.
        static readonly Offer[] Pool =
        {
            O("machine_part", 20, "alien_alloy", 1),
            O("rocket_fuel", 25, "alien_alloy", 1),
            O("basalt_fiber", 60, "alien_alloy", 1),
            O("iron_plate", 70, "alien_alloy", 1),
            O("oxygen", 100, "alien_alloy", 1),
            new Offer { Dollars = 3000, Get = { ["alien_alloy"] = 1 } },
            O("ice", 80, "rocket_fuel", 6),
            O("hematite", 120, "machine_part", 6),
            O("alien_alloy", 1, "machine_part", 10),
            new Offer { Give = { ["alien_alloy"] = 1 }, PayDollars = 250 },
        };

        // The wall clock (UTC), so every PC shows the same offers without sending anything.
        static double Minutes => (DateTime.UtcNow - new DateTime(2026, 1, 1)).TotalMinutes;
        public static int Period => (int)(Minutes / OfferMinutes) + MarsState.StormSeed % 97;

        public static Offer Current(Buildings.Rec b, int slot)
        {
            var rnd = new System.Random(unchecked(b.Id * 7919 + Period * 104729 + slot * 31));
            int first = rnd.Next(6);                     // slot 0 always pays alloy
            if (slot == 0) return Pool[first];
            int other;
            do other = rnd.Next(Pool.Length); while (other == first);
            return Pool[other];
        }

        public static float MinutesLeft => (float)(OfferMinutes - Minutes % OfferMinutes);

        public static string Text(Offer o)
        {
            string give = o.Dollars > 0 ? $"${o.Dollars:0}" : MarsItems.Cost(o.Give);
            string get = o.PayDollars > 0 ? $"${o.PayDollars:0}" : MarsItems.Cost(o.Get);
            return $"{give}  →  {get}";
        }

        // ------------------------------------------------------------------ host: put the outposts into the world

        public static bool EnsureHost(List<Buildings.Rec> list, ref int nextId)
        {
            if (Mines.AlienSites.Count == 0 || list.Any(b => b.Owner == Owner)) return false;
            foreach (var (p, q) in Mines.AlienSites)
            {
                list.Add(new Buildings.Rec { Id = nextId++, Type = "outpost", Owner = Owner, Px = p.x, Py = p.y, Pz = p.z, Qx = q.x, Qy = q.y, Qz = q.z, Qw = q.w });
            }
            Plugin.Log.LogMessage($"[aliens] {Mines.AlienSites.Count} trader outposts landed");
            return true;
        }

        // ------------------------------------------------------------------ trading (host applies)

        public static string Trade(Buildings.Rec b, string player, bool alt, MoonWorld world)
        {
            var o = Current(b, alt ? 1 : 0);
            var pack = Buildings.Backpack(player);
            var store = Buildings.Items.Storage;
            var lack = o.Give.Where(kv => Buildings.Count(pack, kv.Key) + Buildings.Count(store, kv.Key) < kv.Value)
                .Select(kv => $"{kv.Value} {MarsItems.Name(kv.Key).ToLowerInvariant()}").ToList();
            if (lack.Count > 0) return "The aliens want " + string.Join(", ", lack) + " (from your backpack or the crew storage).";
            if (o.Dollars > 0 && !Buildings.Pay(world, player, o.Dollars)) return $"The aliens want ${o.Dollars:0}. Not enough money.";
            foreach (var kv in o.Give)
            {
                int fromPack = Math.Min(kv.Value, Buildings.Count(pack, kv.Key));
                Buildings.AddTo(pack, kv.Key, -fromPack);
                Buildings.AddTo(store, kv.Key, -(kv.Value - fromPack));
            }
            foreach (var kv in o.Get) Buildings.AddTo(pack, kv.Key, kv.Value);
            if (o.PayDollars > 0) Buildings.Earn(world, player, o.PayDollars);
            Goals.Traded();
            Plugin.Log.LogMessage($"[aliens] {player} traded {Text(o)}");
            return "Traded: " + Text(o) + ". The aliens hum happily.";
        }

        public static string Describe(Buildings.Rec b, MarsCatalog.Def def) =>
            $"<b>{def.Name}</b>  ·  offers change in {MinutesLeft:0} min\n<size=18>1: {Text(Current(b, 0))}     2: {Text(Current(b, 1))}</size>";

        /// <summary>A purple glow under the pod, so an outpost can be found at night.</summary>
        public static void Decorate(GameObject go)
        {
            try
            {
                var holder = new GameObject(); holder.name = "alien glow";
                holder.transform.SetParent(go.transform, false);
                holder.transform.localPosition = new Vector3(0f, 2.2f, 0f);
                var l = holder.AddComponent(Il2CppInterop.Runtime.Il2CppType.Of<Light>()).Cast<Light>();
                l.type = LightType.Point;
                l.color = new Color(0.65f, 0.35f, 1f);
                l.range = 26f;
                l.intensity = 3f;
                l.shadows = LightShadows.None;
            }
            catch (Exception e) { Log.Once("alien-glow", "[aliens] glow failed: " + e.Message); }
        }

        public static string Hint(string key) => $"[{key}] trade offer 1   [Shift+{key}] trade offer 2";
    }
}
