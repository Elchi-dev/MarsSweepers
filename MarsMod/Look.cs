using System;
using System.Collections.Generic;
using System.Text;
using Il2CppInterop.Runtime;
using MoonCleaners;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// The Mars look: red surface, warm light, Phobos in the sky, red planet displays (Collector globe and
    /// map), Mars dust everywhere. Shared asset materials are also used by Moon worlds, so we remember their
    /// original colours and put them back as soon as a Moon world (or the menu) is active.
    /// </summary>
    internal static class Look
    {
        const string SurfaceShader = "Moon Cleaners/Painted Moon";
        const string GlobeShader = "Moon Cleaners/Live Collector Moon";

        // material pointer -> (material, property -> original colour)
        static readonly Dictionary<IntPtr, (Material mat, Dictionary<string, Color> orig)> touched = new Dictionary<IntPtr, (Material, Dictionary<string, Color>)>();
        static readonly HashSet<IntPtr> dumped = new HashSet<IntPtr>();
        static float nextMaterialScan, nextObjectScan;
        public static bool Applied;

        public static void Set(Material m, string prop, Color value)
        {
            if (m == null || !m.HasProperty(prop)) return;
            if (!touched.TryGetValue(m.Pointer, out var e))
            {
                e = (m, new Dictionary<string, Color>());
                touched[m.Pointer] = e;
            }
            if (!e.orig.ContainsKey(prop)) e.orig[prop] = m.GetColor(prop);
            m.SetColor(prop, value);
        }

        public static Color Orig(Material m, string prop)
        {
            if (touched.TryGetValue(m.Pointer, out var e) && e.orig.TryGetValue(prop, out var c)) return c;
            return m.GetColor(prop);
        }

        public static Color Mul(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a);

        public static void Apply(float now)
        {
            if (!Applied)
            {
                Applied = true;
                Dust.Statics(true);
            }
            if (now >= nextMaterialScan)
            {
                nextMaterialScan = now + 10f;
                Surface();
                Globes();
                Dust.Materials();
            }
            if (now >= nextObjectScan)
            {
                nextObjectScan = now + 3f;
                Dust.Objects();
            }
            Solar();
        }

        static void Surface()
        {
            try
            {
                int n = 0;
                foreach (var m in All<Material>())
                {
                    var sh = m.shader;
                    if (sh == null || sh.name != SurfaceShader) continue;
                    Set(m, "_DustTint", MarsProfile.C(MarsProfile.DustTint, new Color(1f, .55f, .33f)));
                    Set(m, "_GroundTint", MarsProfile.C(MarsProfile.GroundTint, new Color(.86f, .46f, .3f)));
                    Set(m, "_ShadowTint", MarsProfile.C(MarsProfile.ShadowTint, new Color(.52f, .3f, .27f)));
                    Set(m, "_MareDustTint", MarsProfile.C(MarsProfile.MareDustTint, new Color(.8f, .42f, .28f)));
                    Set(m, "_MareGroundTint", MarsProfile.C(MarsProfile.MareGroundTint, new Color(.92f, .52f, .35f)));
                    n++;
                }
                Log.Once("surface", $"Mars surface colours on {n} material(s)");
            }
            catch (Exception e) { Log.Once("surface-err", "Surface tint failed: " + e); }
        }

        /// <summary>The planet displays: the globe in every Collector and the globe of the map screen.
        /// Only the planet's dust and ground colours change; the map's status colours (untouched,
        /// touched, cleared, stations) keep their meaning.</summary>
        static void Globes()
        {
            if (!MarsProfile.TintGlobe.Value) return;
            try
            {
                var tint = MarsProfile.C(MarsProfile.GlobeTint, new Color(1f, .5f, .32f));
                int n = 0;
                foreach (var m in All<Material>())
                {
                    var sh = m.shader;
                    if (sh == null || sh.name != GlobeShader) continue;
                    Set(m, "_DustTint", Mul(Orig(m, "_DustTint"), tint));
                    Set(m, "_GroundTint", Mul(Orig(m, "_GroundTint"), tint));
                    n++;
                }
                foreach (var map in All<MoonMapScreen>())
                {
                    if (!InScene(map.gameObject)) continue;
                    var m = map.globeMaterial;
                    if (m == null) continue;
                    DumpProps(m, "Map globe material");
                    Set(m, "_DustTint", Mul(Orig(m, "_DustTint"), tint));
                    Set(m, "_GroundTint", Mul(Orig(m, "_GroundTint"), tint));
                    n++;
                }
                Log.Once("globe", $"Planet displays (Collector globe, map) in Mars colours ({n} material(s))");
            }
            catch (Exception e) { Log.Once("globe-err", "Globe tint failed: " + e); }
        }

        static void Solar()
        {
            try
            {
                foreach (var s in All<MoonSolarSystem>())
                {
                    if (!InScene(s.gameObject)) continue;
                    // Earth's slot in the sky shows Phobos: bigger, lumpy, grey, and racing across the sky.
                    s.earthAngularRadius = MarsProfile.PhobosSize.Value;
                    s.earthDegreesPerSecond = MarsProfile.PhobosSpeed.Value;
                    // Nights: no Earthshine on Mars, but starlight and the two moons keep it dark rather than black.
                    s.earthshineIntensity = MarsProfile.NightLight.Value * 0.45f;
                    s.fullEarthAmbient = MarsProfile.NightLight.Value * 0.18f;
                    if (s.earthshine != null) s.earthshine.color = new Color(1f, .82f, .7f);
                    s.sunAngularRadius = MarsProfile.SunAngularRadius.Value;
                    s.daySky = MarsProfile.C(MarsProfile.DaySky, new Color(.8f, .6f, .47f));
                    s.dayEquator = MarsProfile.C(MarsProfile.DayEquator, new Color(.7f, .5f, .4f));
                    s.dayGround = MarsProfile.C(MarsProfile.DayGround, new Color(.52f, .36f, .3f));
                    s.fillColor = MarsProfile.C(MarsProfile.FillColor, new Color(1f, .78f, .62f));
                    s.nightTint = MarsProfile.C(MarsProfile.NightTint, new Color(1f, .84f, .74f));
                    float storm = Haze.Storm;
                    s.sunIntensity = MarsProfile.SunIntensity.Value * (1f - 0.6f * storm);
                    if (s.sunlight != null) s.sunlight.color = Color.Lerp(MarsProfile.C(MarsProfile.SunColor, new Color(1f, .93f, .85f)), new Color(.9f, .62f, .42f), storm);
                    Sky.Apply(s);
                    Log.Once("solar", "Mars sun, Phobos and ambient light applied");
                }
            }
            catch (Exception e) { Log.Once("solar-err", "Sky/sun change failed: " + e); }
        }

        /// <summary>The planet floating in the night sky of the new-hire intro turns red for Mars slots.</summary>
        public static void IntroPlanet()
        {
            try
            {
                var tint = MarsProfile.C(MarsProfile.GlobeTint, new Color(1f, .5f, .32f));
                foreach (var m in All<Material>())
                {
                    var sh = m.shader;
                    if (sh == null || sh.name != "Moon Cleaners/UI Night Moon") continue;
                    DumpProps(m, "Intro planet material");
                    TintColorProps(m, tint, l => true, l => l.Contains("ring") || l.Contains("glow") || l.Contains("rim") || l.Contains("sky"));
                }
                Log.Once("intro-planet", "Mars planet in the intro night sky");
            }
            catch (Exception e) { Log.Once("intro-planet-err", "Intro planet tint failed: " + e.Message); }
        }

        /// <summary>Multiply every matching colour property by tint, always starting from the original value.</summary>
        public static bool TintColorProps(Material m, Color tint, Func<string, bool> include, Func<string, bool> exclude)
        {
            var sh = m.shader;
            if (sh == null) return false;
            bool any = false;
            int count = sh.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                if (sh.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Color) continue;
                string name = sh.GetPropertyName(i);
                string l = name.ToLowerInvariant();
                if (!include(l) || exclude(l)) continue;
                Set(m, name, Mul(Orig(m, name), tint));
                any = true;
            }
            return any;
        }

        /// <summary>Log every property of a material once, so the next version can target the right ones.</summary>
        public static void DumpProps(Material m, string what)
        {
            if (!dumped.Add(m.Pointer)) return;
            try
            {
                var sh = m.shader;
                var sb = new StringBuilder($"[diag] {what} '{m.name}' shader='{(sh != null ? sh.name : "null")}': ");
                if (sh != null)
                {
                    int count = sh.GetPropertyCount();
                    for (int i = 0; i < count; i++)
                    {
                        string n = sh.GetPropertyName(i);
                        var t = sh.GetPropertyType(i);
                        sb.Append(n).Append('(').Append(t).Append(')');
                        if (t == UnityEngine.Rendering.ShaderPropertyType.Color) { var c = m.GetColor(n); sb.Append($"={c.r:F2},{c.g:F2},{c.b:F2}"); }
                        else if (t == UnityEngine.Rendering.ShaderPropertyType.Float || t == UnityEngine.Rendering.ShaderPropertyType.Range) sb.Append('=').Append(m.GetFloat(n).ToString("G4"));
                        sb.Append("; ");
                    }
                }
                Plugin.Log.LogInfo(sb.ToString());
            }
            catch (Exception e) { Plugin.Log.LogInfo($"[diag] {what}: dump failed {e.Message}"); }
        }

        /// <summary>Put every shared material we recoloured back to its Moon colours.</summary>
        public static void RestoreAssets()
        {
            if (Applied) Dust.Statics(false);
            int n = 0;
            foreach (var kv in touched)
            {
                try
                {
                    var m = kv.Value.mat;
                    if (m == null) continue;
                    foreach (var p in kv.Value.orig) m.SetColor(p.Key, p.Value);
                    n++;
                }
                catch { }
            }
            touched.Clear();
            Dust.ResetWorld();
            Applied = false;
            nextMaterialScan = nextObjectScan = 0;
            if (n > 0) Plugin.Log.LogMessage($"Moon colours restored on {n} material(s)");
        }

        public static IEnumerable<T> All<T>() where T : UnityEngine.Object
        {
            var arr = Resources.FindObjectsOfTypeAll(Il2CppType.Of<T>());
            for (int i = 0; i < arr.Length; i++)
            {
                var o = arr[i];
                if (o == null) continue;
                yield return o.Cast<T>();
            }
        }

        public static bool InScene(GameObject go)
        {
            try { return go != null && go.scene.IsValid(); } catch { return false; }
        }
    }

    internal static class Log
    {
        static readonly HashSet<string> once = new HashSet<string>();
        public static void Once(string key, string message)
        {
            if (once.Add(key)) Plugin.Log.LogMessage(message);
        }
        public static void Reset(string key) => once.Remove(key);
        public static bool Seen(string key) => once.Contains(key);
        public static void ResetWorld()
        {
            foreach (var k in new[] { "terrain-check", "surface", "globe", "solar", "welcome", "save-ok", "save-leak", "dustmat", "canister", "haze", "phobos" }) once.Remove(k);
        }
    }
}
