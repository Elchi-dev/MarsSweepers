using System.Collections.Generic;

namespace MarsMod
{
    /// <summary>Mars materials and buildings.</summary>
    internal static class MarsItems
    {
        public sealed class Item { public string Id, Name; public int Sell; }

        public static readonly Dictionary<string, Item> All = new Dictionary<string, Item>
        {
            ["hematite"] = new Item { Id = "hematite", Name = "Hematite ore", Sell = 6 },
            ["basalt"] = new Item { Id = "basalt", Name = "Basalt", Sell = 3 },
            ["iron_plate"] = new Item { Id = "iron_plate", Name = "Iron plate", Sell = 20 },
        };

        public static string Name(string id) => All.TryGetValue(id, out var i) ? i.Name : id;
        public static int Sell(string id) => All.TryGetValue(id, out var i) ? i.Sell : 0;

        public static string List(Dictionary<string, int> inv)
        {
            if (inv == null || inv.Count == 0) return "empty";
            var parts = new List<string>();
            foreach (var kv in inv) if (kv.Value > 0) parts.Add($"{Name(kv.Key)} ×{kv.Value}");
            return parts.Count == 0 ? "empty" : string.Join(", ", parts);
        }
    }

    internal static class MarsCatalog
    {
        public sealed class Def
        {
            public string Id, Name, Model, Info, Category = "Production";
            public int Price;
            public float Radius;        // keep-out radius for placement (metres)
            public bool NeedsMine;
        }

        public static readonly List<Def> All = new List<Def>
        {
            new Def { Id = "drill", Name = "Ore drill", Model = "drill", Price = 600, Radius = 2.6f, NeedsMine = true,
                      Info = "Build it on a mine. Digs ore by itself (1 every 4 s, holds 40)." },
            new Def { Id = "smelter", Name = "Smelter", Model = "smelter", Price = 900, Radius = 3.0f,
                      Info = "2 hematite → 1 iron plate every 6 s. Bring it ore, take the plates." },
            new Def { Id = "container", Name = "Container port", Model = "container", Price = 400, Radius = 2.1f, Category = "Logistics",
                      Info = "Stores Mars materials for the whole crew. Deposit your backpack, sell the stock." },
        };

        public static Def Get(string id) => All.Find(d => d.Id == id);
    }
}
