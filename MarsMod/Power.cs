using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static MarsMod.MarsCatalog;

namespace MarsMod
{
    /// <summary>
    /// The Mars power grid. Every building that makes, stores or uses power is a node; nodes within 25 m of each
    /// other are connected (light poles carry it further), and each connected group is one grid. Solar panels
    /// feed it by daylight and suffer in storms and under dust, batteries buffer, fuel generators start when the
    /// grid runs short. Machines on a weak grid slow down; with no power at all, extractors and recipe machines
    /// still crawl at 20 % (their backup cells), the shield needs its full 30 kW.
    /// The host computes it; each record carries its share (Supply) so the crew can read it.
    /// </summary>
    internal static class Power
    {
        public const float Link = 25f;
        public const float BackupCells = 0.2f;

        public sealed class Grid { public List<Buildings.Rec> Nodes = new List<Buildings.Rec>(); public float Make, Use, Stored, Capacity; }
        public static readonly List<Grid> Grids = new List<Grid>();

        public static bool IsNode(Def d) => d.PowerUse > 0 || d.PowerMake > 0 || d.Battery > 0 || d.Role == Role.Light;

        public static float SolarNow(Buildings.Rec b, Def def)
        {
            float day = Mathf.Clamp01(Haze.Daylight);
            float storm = Mathf.Clamp01(Haze.Storm);
            if (Shields.Covered(b.Pos)) storm = 0f;
            return def.PowerMake * day * (1f - 0.75f * storm) * (1f - 0.8f * b.Dust);
        }

        /// <summary>Host: one step of every grid. Sets Supply on every node (0..1).</summary>
        public static void Step(List<Buildings.Rec> recs, float dt)
        {
            Grids.Clear();
            var nodes = recs.Where(r => { var d = Get(r.Type); return d != null && IsNode(d); }).ToList();
            var seen = new HashSet<int>();
            foreach (var start in nodes)
            {
                if (seen.Contains(start.Id)) continue;
                var g = new Grid();
                var queue = new Queue<Buildings.Rec>(); queue.Enqueue(start); seen.Add(start.Id);
                while (queue.Count > 0)
                {
                    var n = queue.Dequeue(); g.Nodes.Add(n);
                    foreach (var o in nodes)
                        if (!seen.Contains(o.Id) && (o.Pos - n.Pos).sqrMagnitude <= Link * Link) { seen.Add(o.Id); queue.Enqueue(o); }
                }
                Grids.Add(g);
            }
            float dtm = dt / 60f;
            foreach (var g in Grids)
            {
                float make = 0f, use = 0f;
                var batteries = new List<(Buildings.Rec r, Def d)>();
                var gens = new List<(Buildings.Rec r, Def d)>();
                var shields = new List<(Buildings.Rec r, Def d)>();
                foreach (var n in g.Nodes)
                {
                    var d = Get(n.Type);
                    if (d.Role == Role.Solar) make += SolarNow(n, d);
                    if (d.Role == Role.Battery) batteries.Add((n, d));
                    if (d.Role == Role.Generator) gens.Add((n, d));
                    if (d.Role == Role.Shield) { if (Machines.Busy(n, d)) shields.Add((n, d)); continue; }
                    if (d.PowerUse > 0 && Machines.Busy(n, d)) use += d.PowerUse;
                }
                float stored = batteries.Sum(x => x.r.Energy), cap = batteries.Sum(x => x.d.Battery);
                // Generators: keep running what burns, start more while the grid is short or the batteries are low.
                foreach (var (r, d) in gens)
                {
                    bool needed = make < use || (cap > 0 && stored < cap * 0.5f);
                    if (r.Burn <= 0f && needed && Buildings.Count(r.Items, "rocket_fuel") > 0)
                    {
                        Buildings.AddTo(r.Items, "rocket_fuel", -1);
                        r.Burn = d.Seconds;
                    }
                    if (r.Burn > 0f) { make += d.PowerMake; r.Burn = Mathf.Max(0f, r.Burn - dt); }
                }
                // A shield only draws when the grid can carry it fully; otherwise it stays off and takes nothing.
                var unpowered = new List<Buildings.Rec>();
                {
                    float stored0 = batteries.Sum(x => x.r.Energy), cap0 = batteries.Sum(x => x.d.Battery);
                    float avail = make + stored0 / Mathf.Max(dtm, 1e-4f) - use;
                    // A shield that is down only comes up with spare power or batteries at least 10 % full, so it does not flicker.
                    float availToStart = make - use + (cap0 > 0 && stored0 >= cap0 * 0.1f ? stored0 / Mathf.Max(dtm, 1e-4f) : 0f);
                    foreach (var (r, d) in shields)
                    {
                        float a = r.Active ? avail : Mathf.Min(avail, availToStart);
                        if (a >= d.PowerUse) { use += d.PowerUse; avail -= d.PowerUse; availToStart -= d.PowerUse; }
                        else unpowered.Add(r);
                    }
                }
                float supply;
                if (make >= use)
                {
                    supply = 1f;
                    float spare = (make - use) * dtm;
                    foreach (var (r, d) in batteries.OrderBy(x => x.r.Energy / x.d.Battery))
                    {
                        float room = d.Battery - r.Energy;
                        float put = Mathf.Min(room, spare / Math.Max(1, batteries.Count) * 1.5f);
                        r.Energy += put; spare -= put;
                        if (spare <= 0f) break;
                    }
                }
                else
                {
                    float need = (use - make) * dtm, got = 0f;
                    foreach (var (r, d) in batteries.OrderByDescending(x => x.r.Energy))
                    {
                        float take = Mathf.Min(r.Energy, need - got);
                        r.Energy -= take; got += take;
                        if (got >= need) break;
                    }
                    supply = use <= 0f ? 1f : Mathf.Clamp01((make + got / Mathf.Max(dtm, 1e-4f)) / use);
                }
                g.Make = make; g.Use = use; g.Stored = batteries.Sum(x => x.r.Energy); g.Capacity = cap;
                foreach (var n in g.Nodes) n.Supply = supply;
                foreach (var r in unpowered) r.Supply = 0f;
            }
            if (Cheats.InfinitePower)
            {
                foreach (var r in recs) { r.Supply = 1f; var d = Get(r.Type); if (d != null && d.Battery > 0) r.Energy = d.Battery; }
                return;
            }
            // Consumers with no grid at all.
            foreach (var r in recs)
            {
                var d = Get(r.Type);
                if (d == null || d.PowerUse <= 0) continue;
                if (!Grids.Any(g => g.Nodes.Contains(r))) r.Supply = 0f;
            }
        }

        /// <summary>The speed factor a consumer runs at.</summary>
        public static float Factor(Buildings.Rec b, Def d)
        {
            if (d.PowerUse <= 0f) return 1f;
            if (d.Role == Role.Shield) return b.Supply >= 0.99f ? 1f : 0f;
            return Mathf.Max(BackupCells, b.Supply);
        }

        public static string Line(Buildings.Rec b, Def d)
        {
            if (d.PowerUse > 0f)
            {
                if (b.Supply >= 0.99f) return $"  ·  <color=#7CFF9A>{d.PowerUse:0} kW ok</color>";
                if (b.Supply <= 0.01f) return d.Role == Role.Shield ? "  ·  <color=#FF6B5A>no power</color>" : $"  ·  <color=#FF6B5A>no power, backup cells ({BackupCells * 100f:0}%)</color>";
                return $"  ·  <color=#FFC266>power {b.Supply * 100f:0}%</color>";
            }
            var g = Grids.FirstOrDefault(x => x.Nodes.Contains(b));
            if (g != null && (d.PowerMake > 0 || d.Battery > 0 || d.Role == Role.Light))
                return $"  ·  grid {g.Make:0.0}/{g.Use:0.0} kW" + (g.Capacity > 0 ? $", stored {g.Stored:0}/{g.Capacity:0}" : "");
            return "";
        }

        public static IEnumerable<string> Report()
        {
            if (Grids.Count == 0) { yield return "No power grid yet (only the host sees the grids)."; yield break; }
            int i = 1;
            foreach (var g in Grids)
                yield return $"  grid {i++}: {g.Nodes.Count} buildings, making {g.Make:0.0} kW, using {g.Use:0.0} kW, stored {g.Stored:0}/{g.Capacity:0} kW·min";
        }
    }
}
