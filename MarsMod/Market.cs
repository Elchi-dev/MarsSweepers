using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// The Mars market: what Mars materials sell for moves. Every 20 s each price drifts a little and is pulled back
    /// towards its normal value; now and then a boom or a crash hits one material for a few minutes; and selling a
    /// lot at once pushes your own price down for a while (it recovers). The host runs it and saves it with the
    /// world; the crew gets every change. Selling always uses the current price.
    /// </summary>
    internal static class Market
    {
        public sealed class State
        {
            public Dictionary<string, float> Factor = new Dictionary<string, float>();     // random walk around 1
            public Dictionary<string, float> Pressure = new Dictionary<string, float>();   // from our own selling, 0..0.6
            public Dictionary<string, float> Last = new Dictionary<string, float>();       // price one step ago (ticker arrows)
            public string EventItem = "";
            public float EventFactor = 1f;
            public double EventUntil;                                                       // world minutes
            public int EventId;
            public double Minutes;                                                          // market time, advances with play
        }

        const string Key = "market";
        const float StepSeconds = 20f;
        public static State Data = new State();
        static float nextStep;
        static int seenEvent = -1;
        static readonly System.Random rnd = new System.Random();
        static MarsUi.Box ticker;

        static Market()
        {
            MarsData.Changed += key =>
            {
                if (key == "" || key == Key) { Data = MarsData.Get<State>(Key); Announce(); }
            };
        }

        public static void Init() { }

        public static float Price(string item)
        {
            float b = MarsItems.Sell(item);
            if (b <= 0f) return 0f;
            float f = Data.Factor.TryGetValue(item, out var v) ? v : 1f;
            float p = Data.Pressure.TryGetValue(item, out var q) ? q : 0f;
            float e = Data.EventItem == item && Data.Minutes < Data.EventUntil ? Data.EventFactor : 1f;
            return Mathf.Max(b * 0.25f, b * f * e * (1f - p));
        }

        /// <summary>Host: sell n of an item now. Returns the money; the price drops a little with every unit sold.</summary>
        public static double Sell(string item, int n)
        {
            double money = 0;
            for (int i = 0; i < n; i++)
            {
                money += Price(item);
                Data.Pressure[item] = Mathf.Min(0.6f, (Data.Pressure.TryGetValue(item, out var q) ? q : 0f) + 0.003f);
            }
            MarsData.Set(Key, Data);
            return money;
        }

        public static void Tick(float now)
        {
            if (!MarsData.Loaded) return;
            if (MarsNet.IsHost && now >= nextStep)
            {
                bool first = nextStep == 0f;
                nextStep = now + StepSeconds;
                if (!first) Step();
            }
            Ticker();
        }

        static float Gauss() => (float)(Math.Sqrt(-2.0 * Math.Log(1.0 - rnd.NextDouble())) * Math.Sin(2.0 * Math.PI * rnd.NextDouble()));

        static void Step()
        {
            Data.Minutes += StepSeconds / 60.0;
            foreach (var id in MarsItems.All.Keys)
            {
                Data.Last[id] = Price(id);
                float f = Data.Factor.TryGetValue(id, out var v) ? v : 1f;
                f += 0.06f * (1f - f) + 0.035f * Gauss();
                Data.Factor[id] = Mathf.Clamp(f, 0.55f, 1.6f);
                if (Data.Pressure.TryGetValue(id, out var q)) Data.Pressure[id] = q * 0.9f;   // recovers in a few minutes
            }
            if (Data.Minutes >= Data.EventUntil && rnd.NextDouble() < 0.025)   // about one event every 13 minutes
            {
                var ids = MarsItems.All.Keys.ToList();
                Data.EventItem = ids[rnd.Next(ids.Count)];
                bool boom = rnd.NextDouble() < 0.55;
                Data.EventFactor = boom ? 1.6f + (float)rnd.NextDouble() * 0.6f : 0.45f + (float)rnd.NextDouble() * 0.15f;
                Data.EventUntil = Data.Minutes + 3 + rnd.NextDouble() * 4;
                Data.EventId++;
            }
            MarsData.Set(Key, Data);
            Announce();
        }

        static void Announce()
        {
            if (Data.EventId == seenEvent) return;
            bool first = seenEvent < 0;
            seenEvent = Data.EventId;
            if (first || Data.Minutes >= Data.EventUntil || string.IsNullOrEmpty(Data.EventItem)) return;
            string name = MarsItems.Name(Data.EventItem);
            int pct = Mathf.RoundToInt((Data.EventFactor - 1f) * 100f);
            int mins = Mathf.Max(1, Mathf.RoundToInt((float)(Data.EventUntil - Data.Minutes)));
            MarsWorld.Urgent(pct > 0
                ? $"MARKET: {name} boom! Prices up {pct}% for about {mins} minutes. Sell now."
                : $"MARKET: {name} crash! Prices down {-pct}% for about {mins} minutes. Better keep it in storage.");
        }

        public static string Arrow(string item)
        {
            float now = Price(item);
            if (!Data.Last.TryGetValue(item, out var before) || before <= 0f) return "";
            float d = (now - before) / before;
            return d > 0.01f ? "<color=#7CFF9A>▲</color>" : d < -0.01f ? "<color=#FF6B5A>▼</color>" : "<color=#9A8F86>•</color>";
        }

        static void Ticker()
        {
            if (ticker == null) ticker = MarsUi.Make("Mars market ticker", 0.5f, 0.985f, 1500, 34, 16, 514);
            bool show = MarsWorld.InMarsWorld && MarsWorld.PlayerReady && !Terminal.Open && !MarsUi.GameMenuOpen && MarsProfile.MarketTicker.Value;
            if (!show) { ticker.Set(null); return; }
            var sb = new StringBuilder("<color=#E8743B><b>MARKET $</b></color>  ");
            foreach (var id in MarsItems.All.Keys)
            {
                bool ev = Data.EventItem == id && Data.Minutes < Data.EventUntil;
                sb.Append(ev ? (Data.EventFactor > 1f ? "<color=#7CFF9A>" : "<color=#FF6B5A>") : "<color=#F2E6DA>");
                sb.Append($"{MarsItems.Short(id)} {Price(id):0.0}</color>{Arrow(id)}  ");
            }
            ticker.Set(sb.ToString().TrimEnd());
        }

        public static IEnumerable<string> Report()
        {
            foreach (var id in MarsItems.All.Keys)
            {
                float p = Price(id), b = MarsItems.Sell(id);
                float pr = Data.Pressure.TryGetValue(id, out var q) ? q : 0f;
                yield return $"  {MarsItems.Name(id),-14} ${p,6:0.00}  (normal ${b})  {(p / b - 1f) * 100f:+0;-0}%" + (pr > 0.02f ? $", {pr * 100f:0}% lower from recent selling" : "");
            }
            if (Data.Minutes < Data.EventUntil && !string.IsNullOrEmpty(Data.EventItem))
                yield return $"  event: {MarsItems.Name(Data.EventItem)} x{Data.EventFactor:0.00} for {Data.EventUntil - Data.Minutes:0.0} more minutes";
            yield return $"  market time {Data.Minutes:0} minutes. Prices move every {StepSeconds:0} s.";
        }

        public static void Reset() { Data = new State(); nextStep = 0f; seenEvent = -1; ticker?.Set(null); }
    }
}
