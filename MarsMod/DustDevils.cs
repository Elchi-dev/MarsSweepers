using System;
using System.Collections.Generic;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// Dust devils: small whirlwinds that wander across the plains around the base on calm days. Where and when they
    /// are comes from the world seed and the wall clock (UTC), so every PC sees the same devil without sending
    /// anything. A devil that passes over a Mars machine standing outside dusts it up a little (the host applies it);
    /// shields and roofs keep them off. The particles ride on the wind system's billows.
    /// </summary>
    internal static class DustDevils
    {
        public const float SlotMinutes = 3f, Life = 150f, Reach = 6f;
        public sealed class Devil { public Vector3 Center, Up; public float Age, Strength; public int Id; }

        public static readonly List<Devil> Active = new List<Devil>();
        static Vector3 home = Vector3.up;
        static int seed;

        public static void Setup(Vector3 homeCollector, int worldSeed) { home = homeCollector; seed = worldSeed; }

        static double Minutes => (DateTime.UtcNow - new DateTime(2026, 1, 1)).TotalMinutes;

        /// <summary>Every frame on every PC: which devils are alive right now and where.</summary>
        public static void Frame()
        {
            Active.Clear();
            if (!MarsWorld.InMarsWorld || home.sqrMagnitude < 1f || Haze.Storm > 0.15f || Haze.Front > 0.05f) return;
            var terrain = Commands.World?.Terrain;
            if (terrain == null) return;
            AddForced(terrain);
            double m = Minutes;
            long slot = (long)(m / SlotMinutes);
            for (long s = slot - 1; s <= slot; s++)
            {
                var rnd = new System.Random(unchecked((int)(s * 2654435761L) ^ seed ^ 0x44455649));
                if (rnd.NextDouble() > 0.45) continue;                               // most slots have none
                float age = (float)((m - s * SlotMinutes) * 60.0);
                if (age < 0f || age > Life) continue;
                var up = home.normalized;
                var e = Vector3.Cross(up, Vector3.forward); if (e.sqrMagnitude < 1e-4f) e = Vector3.right; e.Normalize();
                var n = Vector3.Cross(up, e);
                float az = (float)(rnd.NextDouble() * Math.PI * 2), r = 40f + (float)rnd.NextDouble() * 260f;
                float heading = (float)(rnd.NextDouble() * Math.PI * 2), speed = 2f + (float)rnd.NextDouble() * 2.5f;
                // A gently curving path.
                float curve = ((float)rnd.NextDouble() - 0.5f) * 0.01f;
                float h = heading + curve * age;
                var start = home + (e * Mathf.Cos(az) + n * Mathf.Sin(az)) * r;
                var along = (e * Mathf.Cos(h) + n * Mathf.Sin(h)) * (speed * age);
                var dir = (start + along).normalized;
                var g = terrain.SurfacePoint(dir);
                float strength = Mathf.Clamp01(age / 12f) * Mathf.Clamp01((Life - age) / 12f) * (1f - Haze.Storm * 4f);
                if (strength <= 0.01f) continue;
                Active.Add(new Devil { Center = g, Up = dir, Age = age, Strength = strength, Id = (int)s });
            }
        }

        /// <summary>Host, with the production tick: devils dust up the machines they pass.</summary>
        public static bool DustMachines(List<Buildings.Rec> recs, float dt)
        {
            bool changed = false;
            if (Active.Count == 0) return false;
            foreach (var d in Active)
                foreach (var b in recs)
                {
                    if (!Buildings.Jams(b) || (b.Pos - d.Center).sqrMagnitude > Reach * Reach) continue;
                    if (Shields.Covered(b.Pos) || Buildings.Roofed(b)) continue;
                    float before = b.Dust;
                    b.Dust = Mathf.Min(1f, b.Dust + dt * 0.05f * d.Strength);
                    if ((int)(before * 10) != (int)(b.Dust * 10)) changed = true;
                }
            return changed;
        }

        static int warned = -1;

        /// <summary>A heads-up when one comes close.</summary>
        public static void Warn(Vector3 player)
        {
            foreach (var d in Active)
                if (d.Id != warned && (d.Center - player).sqrMagnitude < 120f * 120f)
                {
                    warned = d.Id;
                    MarsWorld.Notice($"Dust devil {Vector3.Distance(d.Center, player):0} m to the {Haze.Compass((d.Center - player).normalized)}.");
                }
        }

        static Vector3 forcedAt; static float forcedSince = -1f;

        /// <summary>Test: a devil that starts 40 m away and crosses past the player (this PC only; on the host it dusts machines too).</summary>
        public static string Force(Vector3 player) { forcedAt = player; forcedSince = Time.realtimeSinceStartup; return "A dust devil spins up 40 m away."; }

        static void AddForced(MoonCleaners.MoonTerrain terrain)
        {
            if (forcedSince < 0f) return;
            float age = Time.realtimeSinceStartup - forcedSince;
            if (age > 60f) { forcedSince = -1f; return; }
            var up = forcedAt.normalized;
            var e = Vector3.Cross(up, Vector3.forward); if (e.sqrMagnitude < 1e-4f) e = Vector3.right; e.Normalize();
            var dir = (forcedAt + e * (-40f + age * 1.5f) + Vector3.Cross(up, e) * 8f).normalized;
            float strength = Mathf.Clamp01(age / 5f) * Mathf.Clamp01((60f - age) / 5f);
            Active.Add(new Devil { Center = terrain.SurfacePoint(dir), Up = dir, Age = age, Strength = strength, Id = -2 });
        }

        public static void Reset() { Active.Clear(); warned = -1; forcedSince = -1f; }
    }
}
