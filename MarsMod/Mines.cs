using System;
using System.Collections.Generic;
using System.Linq;
using MoonCleaners;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// Ore deposits. Every client works them out itself from the world's seed and the terrain (both identical
    /// for the whole crew), so mines need no network traffic. There is always one "home mine" a short walk
    /// from the main Collector; the rest are scattered over the planet by the seed, away from stations and on
    /// flat enough ground. Mines never run out.
    /// </summary>
    internal static class Mines
    {
        public sealed class Mine
        {
            public int Id;
            public Vector3 Position, Up;
            public string Ore;
            public bool Home;
            public GameObject View;
        }

        public static readonly List<Mine> All = new List<Mine>();
        /// <summary>Where the alien traders land (worked out with the mines, the same on every PC).</summary>
        public static readonly List<(Vector3 pos, Quaternion rot)> AlienSites = new List<(Vector3, Quaternion)>();
        static int builtForSeed = int.MinValue;
        const int Count = 36;

        public static void Ensure()
        {
            int seed = MarsState.StormSeed ^ 0x4D494E45; // "MINE"
            if (builtForSeed == seed && All.Count > 0 && All.All(m => m.View != null)) return;
            var world = Commands.FindWorld();
            if (world == null) return;
            Clear();
            try { Generate(world, seed); builtForSeed = seed; }
            catch (Exception e) { Plugin.Log.LogError("[mines] generation failed: " + e); Clear(); }
        }

        public static void Clear()
        {
            foreach (var m in All) if (m.View != null) UnityEngine.Object.Destroy(m.View);
            All.Clear();
            AlienSites.Clear();
            builtForSeed = int.MinValue;
        }

        static void Generate(MoonWorld world, int seed)
        {
            var t = world.Terrain;
            var stations = Look.All<MoonCollector>().Where(c => Look.InScene(c.gameObject)).Select(c => c.transform.position).ToList();
            var main = Look.All<MoonCollector>().Where(c => Look.InScene(c.gameObject)).FirstOrDefault(c => c.gameObject.name == "TheCollector");
            Vector3 home = main != null ? main.transform.position : (stations.Count > 0 ? stations[0] : Vector3.up * world.settings.radius);

            float Slope(Vector3 dir, float r)
            {
                var p = t.SurfacePoint(dir);
                var up = dir.normalized;
                var e = Vector3.Cross(up, Vector3.forward); if (e.sqrMagnitude < 1e-4f) e = Vector3.right; e.Normalize();
                var n = Vector3.Cross(up, e);
                float worst = 0f;
                foreach (var off in new[] { e, -e, n, -n })
                {
                    var q = t.SurfacePoint((p + off * r).normalized);
                    worst = Mathf.Max(worst, Mathf.Abs(q.magnitude - p.magnitude) / r);
                }
                return Mathf.Atan(worst) * Mathf.Rad2Deg;
            }

            // 1. Home mine: on a ring 34-60 m around the main Collector, the flattest reachable spot.
            DustTiers.Setup(home, seed);
            var rnd = new System.Random(seed);
            {
                var up = home.normalized;
                var e = Vector3.Cross(up, Vector3.forward); if (e.sqrMagnitude < 1e-4f) e = Vector3.right; e.Normalize();
                var n = Vector3.Cross(up, e);
                float startAz = (float)rnd.NextDouble() * 360f;
                Vector3 best = Vector3.zero; float bestScore = float.MaxValue;
                for (float r = 34f; r <= 60f; r += 6.5f)
                    for (int k = 0; k < 24; k++)
                    {
                        float az = (startAz + k * 15f) * Mathf.Deg2Rad;
                        var dir = (home + (e * Mathf.Cos(az) + n * Mathf.Sin(az)) * r).normalized;
                        var p = t.SurfacePoint(dir);
                        if (stations.Any(s => Vector3.Distance(s, p) < 26f)) continue;
                        float slope = Slope(dir, 3f);
                        float climb = Mathf.Abs(p.magnitude - home.magnitude);
                        float score = slope + climb * 0.8f + r * 0.05f;
                        if (score < bestScore) { bestScore = score; best = p; }
                    }
                if (best != Vector3.zero) All.Add(new Mine { Id = 0, Position = best, Up = best.normalized, Ore = "hematite", Home = true });
            }

            // 2. The rest, scattered by the seed.
            int tries = 0;
            while (All.Count < Count + 1 && tries++ < 4000)
            {
                var dir = new Vector3((float)rnd.NextDouble() * 2f - 1f, (float)rnd.NextDouble() * 2f - 1f, (float)rnd.NextDouble() * 2f - 1f);
                string ore = rnd.NextDouble() < 0.7 ? "hematite" : "basalt";
                if (dir.sqrMagnitude < 0.05f || dir.sqrMagnitude > 1f) continue;
                dir.Normalize();
                var p = t.SurfacePoint(dir);
                if (stations.Any(s => Vector3.Distance(s, p) < 80f)) continue;
                if (All.Any(m => Vector3.Distance(m.Position, p) < 160f)) continue;
                if (Slope(dir, 3f) > 14f) continue;
                if (Mathf.Abs(dir.y) > 0.72f) ore = "ice";      // the polar caps hold water ice
                All.Add(new Mine { Id = All.Count, Position = p, Up = dir, Ore = ore });
            }

            // 3. Alien trader outposts: 3 sites 250-900 m from the Collector, flat, away from mines and stations.
            {
                var ar = new System.Random(seed ^ 0x414C4945);
                var up = home.normalized;
                var e = Vector3.Cross(up, Vector3.forward); if (e.sqrMagnitude < 1e-4f) e = Vector3.right; e.Normalize();
                var n = Vector3.Cross(up, e);
                int at = 0;
                while (AlienSites.Count < 3 && at++ < 600)
                {
                    float az = (float)(ar.NextDouble() * Math.PI * 2), r = 250f + (float)ar.NextDouble() * 650f;
                    var dir = (home + (e * Mathf.Cos(az) + n * Mathf.Sin(az)) * r).normalized;
                    var p = t.SurfacePoint(dir);
                    if (Slope(dir, 4f) > 8f) continue;
                    if (stations.Any(s0 => Vector3.Distance(s0, p) < 120f) || All.Any(m => Vector3.Distance(m.Position, p) < 60f)) continue;
                    if (AlienSites.Any(x => Vector3.Distance(x.pos, p) < 300f)) continue;
                    var fwd = Vector3.ProjectOnPlane(home - p, dir); if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(Vector3.right, dir);
                    AlienSites.Add((p, Quaternion.LookRotation(fwd.normalized, dir)));
                }
            }

            // 4. Views.
            foreach (var m in All)
            {
                float yaw = (m.Id * 137.5f) % 360f;
                var fwd = Vector3.ProjectOnPlane(Vector3.forward, m.Up); if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(Vector3.right, m.Up);
                var rot = Quaternion.AngleAxis(yaw, m.Up) * Quaternion.LookRotation(fwd.normalized, m.Up);
                m.View = Models.Spawn(m.Ore == "ice" ? "mine_ice" : "mine", m.Position - m.Up * 0.12f, rot);
                m.View.name = $"Mars mine {m.Id} ({m.Ore})";
            }
            Plugin.Log.LogMessage($"[mines] {All.Count} mines (home mine {(All.Count > 0 && All[0].Home ? Vector3.Distance(All[0].Position, home).ToString("0") + " m from the Collector" : "missing")})");
        }

        public static Mine Nearest(Vector3 p, float maxDist)
        {
            Mine best = null; float bd = maxDist;
            foreach (var m in All) { float d = Vector3.Distance(m.Position, p); if (d < bd) { bd = d; best = m; } }
            return best;
        }

        public static Mine ById(int id) => All.FirstOrDefault(m => m.Id == id);
    }
}
