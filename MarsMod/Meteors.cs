using System;
using System.Linq;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// Meteorites. Every 15 to 30 minutes the host drops one somewhere 150 to 700 m from the Collector and tells the
    /// crew where. It glows, holds iron plates' worth of hematite and often an alien alloy or two; whoever reaches
    /// it first takes it all with the interact key, and it is gone. At most three lie around at once.
    /// </summary>
    internal static class Meteors
    {
        public const string Owner = "sky";
        static float nextAt = -1f;
        static readonly System.Random rnd = new System.Random();

        public static void Tick(float now)
        {
            if (!MarsData.Loaded || !MarsNet.IsHost || Mines.All.Count == 0) return;
            if (nextAt < 0f) { nextAt = now + 60f * (15f + (float)rnd.NextDouble() * 15f); return; }
            if (now < nextAt) return;
            nextAt = now + 60f * (15f + (float)rnd.NextDouble() * 15f);
            if (Buildings.Data.List.Count(b => b.Owner == Owner) >= 3) return;
            Drop(null);
        }

        /// <summary>Host: drop one now (near = at that point, otherwise somewhere around the base).</summary>
        public static string Drop(Vector3? near)
        {
            var world = Commands.FindWorld();
            if (world == null) return "No world.";
            var home = Mines.Home.sqrMagnitude > 1f ? Mines.Home : Vector3.up * world.settings.radius;
            Vector3 p = Vector3.zero;
            for (int tries = 0; tries < 40; tries++)
            {
                Vector3 dir;
                if (near.HasValue) dir = (near.Value + Vector3.Cross(near.Value.normalized, Vector3.right).normalized * 12f).normalized;
                else
                {
                    var up = home.normalized;
                    var e = Vector3.Cross(up, Vector3.forward); if (e.sqrMagnitude < 1e-4f) e = Vector3.right; e.Normalize();
                    var n = Vector3.Cross(up, e);
                    float az = (float)(rnd.NextDouble() * Math.PI * 2), r = 150f + (float)rnd.NextDouble() * 550f;
                    dir = (home + (e * Mathf.Cos(az) + n * Mathf.Sin(az)) * r).normalized;
                }
                p = world.Terrain.SurfacePoint(dir);
                if (Buildings.Data.List.All(b => (b.Pos - p).sqrMagnitude > 20f * 20f)) break;
            }
            var q = Quaternion.FromToRotation(Vector3.up, p.normalized) * Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
            var rec = new Buildings.Rec { Id = Buildings.Data.NextId++, Type = "meteor", Owner = Owner, Px = p.x, Py = p.y, Pz = p.z, Qx = q.x, Qy = q.y, Qz = q.z, Qw = q.w };
            rec.Items["hematite"] = 20 + rnd.Next(30);
            int alloy = rnd.NextDouble() < 0.6 ? 1 + (rnd.NextDouble() < 0.3 ? 1 : 0) : 0;
            if (alloy > 0) rec.Items["alien_alloy"] = alloy;
            Buildings.Data.List.Add(rec);
            Buildings.Changed();
            float dist = Vector3.Distance(p, home);
            string text = $"METEORITE DOWN {dist:0} m from the Collector, to the {Haze.Compass((p - home).normalized)}. First come, first served.";
            MarsWorld.Urgent(text);
            MarsNet.Broadcast(MarsNet.Kind.Notice, w => w.Write(text));
            Plugin.Log.LogMessage($"[meteor] #{rec.Id} at {dist:0} m, alloy {alloy}");
            return null;
        }

        /// <summary>Someone took it: everything into their backpack, the meteorite is gone.</summary>
        public static string Take(Buildings.Rec b, string player)
        {
            var pack = Buildings.Backpack(player);
            string got = MarsItems.List(b.Items);
            foreach (var kv in b.Items) Buildings.AddTo(pack, kv.Key, kv.Value);
            b.Items.Clear();
            Buildings.Data.List.Remove(b);
            Buildings.Changed();
            return "Took the meteorite apart: " + got + ".";
        }

        public static void Decorate(GameObject go)
        {
            try
            {
                var holder = new GameObject(); holder.name = "meteor glow";
                holder.transform.SetParent(go.transform, false);
                holder.transform.localPosition = new Vector3(0f, 1.2f, 0f);
                var l = holder.AddComponent(Il2CppInterop.Runtime.Il2CppType.Of<Light>()).Cast<Light>();
                l.type = LightType.Point; l.color = new Color(1f, 0.45f, 0.15f); l.range = 22f; l.intensity = 4f; l.shadows = LightShadows.None;
            }
            catch (Exception e) { Log.Once("meteor-glow", "[meteor] glow failed: " + e.Message); }
        }

        public static void Reset() { nextAt = -1f; }
    }
}
