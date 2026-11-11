using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// Light poles: a lamp that is on at night and in storms, and storm air that clears up around each pole, so
    /// the crew can light a path back to the base. The clearing is local to whoever stands near a pole (it only
    /// changes how far you can see), so it needs no network traffic.
    /// </summary>
    internal static class LightPoles
    {
        public const float Radius = 22f;
        static readonly List<Light> lights = new List<Light>();

        /// <summary>0 far from every pole, 1 right under one.</summary>
        public static float Clear(Vector3 at)
        {
            float best = 0f;
            foreach (var b in Buildings.Data.List)
            {
                if (b.Type != "lightpole") continue;
                float d = Vector3.Distance(b.Pos, at);
                if (d >= Radius) continue;
                best = Mathf.Max(best, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Radius * 0.55f, Radius, d)));
            }
            return best;
        }

        /// <summary>Give a pole the game made from our prefab its lamp (once).</summary>
        public static void Attach(GameObject pole)
        {
            try
            {
                var anchor = Find(pole.transform, "anchor_lamp") ?? pole.transform;
                if (anchor.GetComponent(Il2CppType.Of<Light>()) != null) return;
                var l = anchor.gameObject.AddComponent(Il2CppType.Of<Light>()).Cast<Light>();
                l.type = LightType.Spot;
                l.color = new Color(1f, 0.9f, 0.74f);
                l.range = 30f;
                l.spotAngle = 120f;
                l.intensity = 0f;
                l.shadows = LightShadows.None;
                anchor.rotation = Quaternion.LookRotation(-pole.transform.up, pole.transform.forward);
                lights.Add(l);
            }
            catch (Exception e) { Log.Once("pole-light", "[poles] lamp failed: " + e.Message); }
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var f = Find(t.GetChild(i), name); if (f != null) return f; }
            return null;
        }

        public static void Tick()
        {
            float want = Mathf.Clamp01(Mathf.Max(1f - Haze.Daylight * 1.6f, Haze.Storm * 1.3f)) * 6f;
            for (int i = lights.Count - 1; i >= 0; i--)
            {
                var l = lights[i];
                if (l == null || l.WasCollected) { lights.RemoveAt(i); continue; }
                l.intensity = Mathf.MoveTowards(l.intensity, want, Time.deltaTime * 4f);
                l.enabled = l.intensity > 0.01f;
            }
        }
    }
}
