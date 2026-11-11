using System;
using MoonCleaners;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// Dust tiers. Mars sectors are not all the same: most hold fine dust, some hold drifts, crust and, rarely,
    /// magnetite crust. The tier comes from smooth noise over the planet (so tiers form zones, the same on every
    /// PC) and climbs with the distance from the home Collector, so the far side of the planet is worth the trip.
    /// When a sector is finished the host pays the game's normal bounty, then this adds the tier's bonus on top.
    /// Thicker dust is not simulated yet (the game's dust is one thickness everywhere); the tier is the reward.
    /// </summary>
    internal static class DustTiers
    {
        public static readonly string[] Names = { "Fine dust", "Dust drift", "Dust crust", "Magnetite crust" };
        public static readonly float[] Bonus = { 1f, 1.6f, 2.5f, 4f };
        static readonly string[] Colours = { "#C9B8A6", "#FFC266", "#FF9A5A", "#C58BFF" };

        static Vector3 home = Vector3.up;
        static int seed;

        public static void Setup(Vector3 homeCollector, int worldSeed) { home = homeCollector.normalized; seed = worldSeed; }

        static float Noise(Vector3 d)
        {
            // Three octaves of rotated sine waves: cheap, smooth, deterministic.
            float s = (seed & 1023) * 0.017f;
            float n = Mathf.Sin(d.x * 3.1f + s) * Mathf.Sin(d.y * 2.7f - s * 0.5f) * Mathf.Sin(d.z * 3.3f + s * 1.3f);
            n += 0.5f * Mathf.Sin(d.x * 7.3f - d.z * 5.1f + s) * Mathf.Sin(d.y * 6.7f + d.x * 2.2f);
            n += 0.25f * Mathf.Sin(d.x * 15.1f + d.y * 11.3f - s) * Mathf.Sin(d.z * 13.7f - d.y * 4.1f);
            return n / 1.75f;   // about -1..1
        }

        public static int TierAt(Vector3 direction)
        {
            var d = direction.normalized;
            float far = Vector3.Angle(d, home) / 180f;          // 0 at home, 1 on the far side
            float v = Noise(d) * 0.65f + far * 0.9f;
            if (v > 0.95f) return 3;
            if (v > 0.6f) return 2;
            if (v > 0.25f) return 1;
            return 0;
        }

        public static int TierOf(MoonWorld world, int region)
        {
            try { return TierAt(world.Regions.Centers[region]); } catch { return 0; }
        }

        public static string Label(int tier) => $"<color={Colours[tier]}>{Names[tier]}</color>" + (tier > 0 ? $" (bounty x{Bonus[tier]:0.#})" : "");

        // ------------------------------------------------------------------ host: the bonus when a sector is paid

        public static double Before(MoonWorld world, string cleaner)
        {
            try { return Il2CppSystem.Decimal.ToDouble(world.WalletDollars(cleaner)); } catch { return double.NaN; }
        }

        public static void After(MoonWorld world, int region, string cleaner, double before)
        {
            if (!MarsWorld.InMarsWorld || !MarsNet.IsHost || double.IsNaN(before)) return;
            try
            {
                double paid = Il2CppSystem.Decimal.ToDouble(world.WalletDollars(cleaner)) - before;
                int tier = TierOf(world, region);
                if (paid <= 0 || tier == 0) return;
                double extra = Math.Round(paid * (Bonus[tier] - 1f), 2);
                Buildings.Earn(world, cleaner, extra);
                Plugin.Log.LogMessage($"[tiers] sector {region}: {Names[tier]}, bounty ${paid:0.##} + ${extra:0.##}");
                if (tier >= 2) MarsWorld.Notice($"{Names[tier]} sector: +${extra:0} bonus");
            }
            catch (Exception e) { Log.Once("tier-pay", "[tiers] bonus failed: " + e.Message); }
        }

        // ------------------------------------------------------------------ everyone: tell the player when the ground changes

        static int shown = -1, pending = -1;
        static float next;

        public static void Frame(Vector3 player)
        {
            if (Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + 1.5f;
            int t = TierAt(player);
            if (t == shown) { pending = -1; return; }
            if (shown >= 0 && t != pending) { pending = t; return; }   // the same new tier twice in a row: no flicker at an edge
            pending = -1;
            bool first = shown < 0;
            shown = t;
            if (!first || t > 0) MarsWorld.Notice("Ground: " + Label(t));
        }

        public static void Reset() { shown = -1; pending = -1; }
    }
}
