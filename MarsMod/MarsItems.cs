using System.Collections.Generic;
using System.Linq;

namespace MarsMod
{
    /// <summary>
    /// Mars materials. Prices are the market's normal values; docs/BALANCE.md explains where they come from
    /// (the Moon pays $1 per kg of dust, x3 for sand, x7 for stone, and a mid-game crew earns about $100 a minute).
    /// </summary>
    internal static class MarsItems
    {
        public sealed class Item { public string Id, Name; public float Sell; }

        static Item I(string id, string name, float sell) => new Item { Id = id, Name = name, Sell = sell };

        public static readonly Dictionary<string, Item> All = new[]
        {
            I("hematite", "Hematite ore", 1.5f),
            I("basalt", "Basalt", 0.8f),
            I("ice", "Water ice", 1.2f),
            I("iron_plate", "Iron plate", 4.5f),
            I("basalt_grit", "Basalt grit", 1.0f),
            I("basalt_fiber", "Basalt fiber", 5f),
            I("machine_part", "Machine part", 22f),
            I("oxygen", "Oxygen", 3f),
            I("hydrogen", "Hydrogen", 3f),
            I("rocket_fuel", "Rocket fuel", 12f),
            I("alien_alloy", "Alien alloy", 90f),
        }.ToDictionary(i => i.Id);

        public static string Name(string id) => All.TryGetValue(id, out var i) ? i.Name : id;
        public static float Sell(string id) => All.TryGetValue(id, out var i) ? i.Sell : 0f;

        public static string List(Dictionary<string, int> inv)
        {
            if (inv == null || inv.Count == 0) return "empty";
            var parts = new List<string>();
            foreach (var kv in inv) if (kv.Value > 0) parts.Add($"{Name(kv.Key)} ×{kv.Value}");
            return parts.Count == 0 ? "empty" : string.Join(", ", parts);
        }

        public static string Cost(Dictionary<string, int> items) =>
            items == null || items.Count == 0 ? "" : string.Join(", ", items.Select(kv => $"{kv.Value} {Name(kv.Key).ToLowerInvariant()}"));
    }

    internal static class MarsCatalog
    {
        public enum Role { Extractor, Recipe, Storage, Light, Solar, Battery, Generator, Shield, Market, Cannon, Trader, Hub, Meteor }

        public sealed class Def
        {
            public string Id, Name, Model, Info, Category = "Production";
            public Role Role;
            public int Price;
            public float Radius;                       // keep-out radius for the old N-menu placement (metres)
            public bool NeedsMine;
            public Dictionary<string, int> ItemCost = new Dictionary<string, int>();   // taken from crew storage when built
            public Dictionary<string, int> In = new Dictionary<string, int>(), Out = new Dictionary<string, int>();
            public float Seconds = 1f;                 // one recipe cycle at full power
            public int Cap = 40;                       // each output (and the extractor's store) holds this many
            public float PowerUse, PowerMake;          // kW
            public float Battery;                      // kW·min a battery holds
            public bool Jams;                          // storm dust builds up on it when it stands outside
            public float Radius2;                      // working range (logistics hub)
        }

        static Dictionary<string, int> D(params (string, int)[] kv) => kv.ToDictionary(x => x.Item1, x => x.Item2);

        public static readonly List<Def> All = new List<Def>
        {
            new Def { Id = "drill", Name = "Ore drill", Model = "drill", Role = Role.Extractor, Price = 250, Radius = 2.6f, NeedsMine = true,
                      Seconds = 4f, Cap = 40, PowerUse = 3f, Jams = true,
                      Info = "Build it on a mine: digs its ore by itself, 1 every 4 s, holds 40. Needs 3 kW." },
            new Def { Id = "smelter", Name = "Smelter", Model = "smelter", Role = Role.Recipe, Price = 500, Radius = 3.0f,
                      In = D(("hematite", 2)), Out = D(("iron_plate", 1)), Seconds = 6f, PowerUse = 6f, Jams = true,
                      Info = "2 hematite → 1 iron plate every 6 s. Needs 6 kW." },
            new Def { Id = "crusher", Name = "Crusher", Model = "crusher", Role = Role.Recipe, Price = 350, Radius = 2.6f,
                      In = D(("basalt", 1)), Out = D(("basalt_grit", 2)), Seconds = 4f, PowerUse = 4f, Jams = true,
                      Info = "1 basalt → 2 basalt grit every 4 s. Needs 4 kW." },
            new Def { Id = "spinner", Name = "Fiber spinner", Model = "spinner", Role = Role.Recipe, Price = 450, Radius = 2.6f,
                      In = D(("basalt_grit", 3)), Out = D(("basalt_fiber", 1)), Seconds = 6f, PowerUse = 3f, Jams = true,
                      Info = "3 basalt grit → 1 basalt fiber every 6 s. Needs 3 kW." },
            new Def { Id = "assembler", Name = "Assembler", Model = "assembler", Role = Role.Recipe, Price = 800, Radius = 3.0f,
                      ItemCost = D(("iron_plate", 10)), In = D(("iron_plate", 2), ("basalt_fiber", 1)), Out = D(("machine_part", 1)),
                      Seconds = 10f, PowerUse = 6f, Jams = true,
                      Info = "2 iron plates + 1 basalt fiber → 1 machine part every 10 s. Needs 6 kW. Costs 10 iron plates from the crew storage too." },
            new Def { Id = "electrolyzer", Name = "Electrolyzer", Model = "electrolyzer", Role = Role.Recipe, Price = 900, Radius = 3.0f,
                      ItemCost = D(("machine_part", 4)), In = D(("ice", 1)), Out = D(("oxygen", 1), ("hydrogen", 1)),
                      Seconds = 8f, PowerUse = 12f, Jams = true,
                      Info = "1 water ice → 1 oxygen + 1 hydrogen every 8 s. Hungry: 12 kW. Costs 4 machine parts too." },
            new Def { Id = "refinery", Name = "Fuel refinery", Model = "refinery", Role = Role.Recipe, Price = 1200, Radius = 3.0f,
                      ItemCost = D(("machine_part", 8)), In = D(("hydrogen", 1), ("oxygen", 1)), Out = D(("rocket_fuel", 1)),
                      Seconds = 8f, PowerUse = 6f, Jams = true,
                      Info = "1 hydrogen + 1 oxygen → 1 rocket fuel every 8 s. Needs 6 kW. Costs 8 machine parts too." },
            new Def { Id = "container", Name = "Container port", Model = "container", Role = Role.Storage, Price = 100, Radius = 2.1f, Category = "Logistics",
                      Info = "The crew storage: deposit your backpack, sell everything at the market price." },
            new Def { Id = "market", Name = "Market board", Model = "market", Role = Role.Market, Price = 200, Radius = 1.6f, Category = "Logistics",
                      Info = "Shows every Mars price and where it is heading. Sells from the crew storage, one material at a time." },
            new Def { Id = "hub", Name = "Logistics hub", Model = "hub", Role = Role.Hub, Price = 900, Radius = 2.0f, Category = "Logistics",
                      ItemCost = D(("iron_plate", 20)), Radius2 = 30f, PowerUse = 4f, Jams = true,
                      Info = "Its drones empty every Mars machine within 30 m into the crew storage, refill their inputs from it and brush the dust off, every 5 s. Needs 4 kW. Costs 20 iron plates too." },
            new Def { Id = "lightpole", Name = "Light pole", Model = "lightpole", Role = Role.Light, Price = 60, Radius = 0.8f, Category = "Power",
                      Info = "A lamp that clears the storm air around it (about 20 m), and carries power 25 m to the next building or pole." },
            new Def { Id = "solar", Name = "Mars solar panel", Model = "solar", Role = Role.Solar, Price = 150, Radius = 2.0f, Category = "Power",
                      PowerMake = 8f, Jams = true,
                      Info = "Up to 8 kW in full sun, a quarter of it in a storm, nothing at night. Dust on it lowers it further." },
            new Def { Id = "battery", Name = "Battery", Model = "battery", Role = Role.Battery, Price = 200, Radius = 1.6f, Category = "Power",
                      Battery = 120f, Info = "Stores 120 kW·min: one drill and one smelter for about 13 minutes of night." },
            new Def { Id = "generator", Name = "Fuel generator", Model = "generator", Role = Role.Generator, Price = 600, Radius = 2.2f, Category = "Power",
                      ItemCost = D(("machine_part", 4)), In = D(("rocket_fuel", 1)), Seconds = 60f, Cap = 20, PowerMake = 30f,
                      Info = "Burns 1 rocket fuel a minute for 30 kW when the grid needs it. Storm-proof. Costs 4 machine parts too." },
            new Def { Id = "shield", Name = "Shield generator", Model = "shield", Role = Role.Shield, Price = 2500, Radius = 2.6f, Category = "Power",
                      ItemCost = D(("machine_part", 20), ("basalt_fiber", 10)), PowerUse = 30f,
                      Info = "When a storm comes, it raises a dome (28 m) that keeps the storm out: no dust, clear air, nothing jams. Draws 30 kW while up." },
            new Def { Id = "cannon", Name = "Planet cannon", Model = "cannon", Role = Role.Cannon, Price = 25000, Radius = 3.4f, Category = "Power",
                      ItemCost = D(("alien_alloy", 10), ("machine_part", 40)), In = D(("rocket_fuel", 30)), Cap = 30, PowerUse = 60f,
                      Info = "The end of the job. Load 30 rocket fuel, give it 60 kW, and fire it at the Moon. Costs 10 alien alloy and 40 machine parts too." },
        };

        /// <summary>Things that stand on Mars but nobody builds (not in the build menu).</summary>
        public static readonly List<Def> World = new List<Def>
        {
            new Def { Id = "outpost", Name = "Alien trader outpost", Model = "outpost", Role = Role.Trader, Radius = 4f, Category = "World",
                      Info = "Trades alien alloy for what the crew makes. The offers change every 8 minutes." },
            new Def { Id = "meteor", Name = "Meteorite", Model = "meteor", Role = Role.Meteor, Radius = 2.5f, Category = "World",
                      Info = "Fell from the sky. Holds iron and, sometimes, alien alloy." },
        };

        public static Def Get(string id) => All.Find(d => d.Id == id) ?? World.Find(d => d.Id == id);
    }
}
