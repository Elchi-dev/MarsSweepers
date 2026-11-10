using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using MoonCleaners;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// Mars-coloured dust everywhere it shows up besides the planet surface: canisters (Q), the dust in
    /// tool containers on the HUD, brush puffs, vehicle trails, footprints, deposit intake heaps.
    /// </summary>
    internal static class Dust
    {
        static Color Tint => MarsProfile.C(MarsProfile.DustTint, new Color(1f, .55f, .33f));

        // ---------------------------------------------------------- shared materials (restored for Moon worlds)

        static readonly string[] DustShaders = { "Moon Cleaners/Brush Dust Puff", "Moon Cleaners/Intake Suction" };
        static readonly string[] DustMaterialNames = { "dust", "canister" };

        public static void Materials()
        {
            int n = 0;
            try
            {
                foreach (var m in Look.All<Material>())
                {
                    string name = (m.name ?? "").ToLowerInvariant();
                    var sh = m.shader;
                    string shn = sh != null ? sh.name : "";
                    bool dustShader = Array.IndexOf(DustShaders, shn) >= 0;
                    if (shn.StartsWith("Hidden/")) continue; // e.g. the footprint stamp shaders write data, not colour
                    bool dustName = Array.Exists(DustMaterialNames, k => name.Contains(k)) &&
                                    !shn.StartsWith("UI/") && !shn.StartsWith("TextMeshPro") && !shn.Contains("Painted Moon") && !shn.Contains("Live Collector");
                    if (!dustShader && !dustName) continue;
                    Look.DumpProps(m, "Dust material");
                    if (Look.TintColorProps(m, Tint,
                        l => l.Contains("color") || l.Contains("tint") || l.Contains("base") || l.Contains("albedo"),
                        l => l.Contains("emission") || l.Contains("glow") || l.Contains("rim") || l.Contains("spec") || l.Contains("highlight")))
                        n++;
                }
            }
            catch (Exception e) { Log.Once("dustmat-err", "Dust materials failed: " + e.Message); }
            Log.Once("dustmat", $"Mars dust on {n} dust material(s) (canisters, puffs, intake)");
        }

        // ---------------------------------------------------------- static colours in game code

        static readonly (string type, string field)[] StaticColours =
        {
            ("MoonCleaners.MoonVehicleDust", "DriveDust"),
            ("MoonCleaners.MoonVehicleDust", "DriftDust"),
            ("MoonCleaners.MoonDustPrints", "PuffColour"),
            ("MoonCleaners.MoonMapScreen", "Backdrop"),          // the map's background: a warmer, Martian tone
        };
        static readonly Dictionary<string, Color> staticOriginals = new Dictionary<string, Color>();

        public static void Statics(bool mars)
        {
            foreach (var (type, field) in StaticColours)
            {
                try
                {
                    var p = typeof(MoonWorld).Assembly.GetType(type)?.GetProperty(field, BindingFlags.Public | BindingFlags.Static);
                    if (p == null || !p.CanWrite) continue;
                    string key = type + "." + field;
                    if (!staticOriginals.ContainsKey(key)) staticOriginals[key] = (Color)p.GetValue(null);
                    var orig = staticOriginals[key];
                    p.SetValue(null, mars ? Look.Mul(orig, Tint) : orig);
                }
                catch (Exception e) { Log.Once("static-" + field, $"Colour {type}.{field}: {e.Message}"); }
            }
            if (mars) Log.Once("dust-statics", "Mars dust for vehicle trails and footprints");
        }

        // ---------------------------------------------------------- per-world objects (rebuilt every world)

        static readonly HashSet<IntPtr> done = new HashSet<IntPtr>();

        public static void ResetWorld() => done.Clear();

        public static void Objects()
        {
            Canisters();
            HudGauges();
        }

        static void Canisters()
        {
            try
            {
                foreach (var v in Look.All<MoonDustCanisterView>())
                {
                    if (!done.Add(v.Pointer)) continue;
                    var r = v.dustRenderer;
                    if (r == null) continue;
                    // Each canister's dust renderer may own an instance material; colour all of them.
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        Look.DumpProps(m, "Canister dust material");
                        Look.TintColorProps(m, Tint,
                            l => l.Contains("color") || l.Contains("tint") || l.Contains("base") || l.Contains("albedo"),
                            l => l.Contains("emission") || l.Contains("glow") || l.Contains("rim") || l.Contains("spec"));
                    }
                }
                Log.Once("canister", "Mars dust in canisters");
            }
            catch (Exception e) { Log.Once("canister-err", "Canister dust failed: " + e.Message); }
        }

        /// <summary>HUD dust (tool container gauges and similar UI graphics). Their classes derive from Unity UI,
        /// which this mod is not compiled against, so we go through reflection.</summary>
        static readonly string[] UiDustTypes =
        {
            "MoonCleaners.MoonUiDustFill", "MoonCleaners.MoonUiDustSurface", "MoonCleaners.MoonDustGaugeGraphic",
            "MoonCleaners.MoonDustPanelGraphic", "MoonCleaners.MoonUiGlobe",
        };

        static void HudGauges()
        {
            int n = 0;
            foreach (var typeName in UiDustTypes)
            {
                try
                {
                    var t = typeof(MoonWorld).Assembly.GetType(typeName);
                    if (t == null) continue;
                    var arr = Resources.FindObjectsOfTypeAll(Il2CppType.From(t));
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var o = arr[i];
                        if (o == null || !done.Add(o.Pointer)) continue;
                        object w = Activator.CreateInstance(t, o.Pointer);
                        if (!Look.InScene(((Component)new Component(o.Pointer)).gameObject)) continue;
                        bool changed = false;
                        // Our own colour fields: "deep" and the like.
                        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        {
                            if (p.PropertyType != typeof(Color) || !p.CanWrite || !p.CanRead) continue;
                            p.SetValue(w, Look.Mul((Color)p.GetValue(w), Tint));
                            changed = true;
                        }
                        // The Graphic's own tint colour (white by default) carries the dust texture's colour.
                        if (typeName.Contains("Dust"))
                        {
                            var colour = t.GetProperty("color", BindingFlags.Public | BindingFlags.Instance);
                            if (colour != null && colour.CanWrite)
                            {
                                var c = (Color)colour.GetValue(w);
                                colour.SetValue(w, new Color(c.r * Tint.r, c.g * Tint.g, c.b * Tint.b, c.a));
                                changed = true;
                            }
                        }
                        if (changed)
                        {
                            t.GetMethod("SetAllDirty", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null)?.Invoke(w, null);
                            n++;
                        }
                    }
                }
                catch (Exception e) { Log.Once("hud-" + typeName, $"HUD dust {typeName}: {e.Message}"); }
            }
            if (n > 0) Plugin.Log.LogInfo($"Mars dust on {n} HUD element(s)");
        }
    }
}
