using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MarsProbe
{
    /// <summary>
    /// Hands-on capability tests for the Mars mod, triggered by keys:
    ///   F9  spawn a red test cube in front of you  (can we create objects, materials and URP shaders?)
    ///   F10 toggle a Mars-red tint on the biggest surface materials  (can we recolour the planet?)
    /// Both are local only and vanish when you leave the world.
    /// </summary>
    internal static class Tests
    {
        public static readonly List<string> Results = new List<string>();
        static int cubes;

        static Camera FindCamera()
        {
            try { var c = Camera.main; if (c != null) return c; } catch { }
            foreach (var p in U.FindAll("UnityEngine.CoreModule.dll", "UnityEngine", "Camera"))
            {
                var c = new Camera(p);
                if (c.enabled && c.gameObject.activeInHierarchy && U.SceneOfGO(c.gameObject) != null) return c;
            }
            return null;
        }

        public static void SpawnCube()
        {
            try
            {
                var cam = FindCamera();
                if (cam == null) { Result("F9 cube: FAIL no camera found"); return; }
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "MarsProbe Test Cube " + (++cubes);
                var t = go.transform;
                t.position = cam.transform.position + cam.transform.forward * 4f;
                t.localScale = new Vector3(1.2f, 1.2f, 1.2f);

                string used = null;
                foreach (var sn in new[] { "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Universal Render Pipeline/Unlit", "Unlit/Color", "Standard" })
                {
                    var sh = Shader.Find(sn);
                    if (sh == null) continue;
                    var mat = new Material(sh);
                    var red = new Color(0.85f, 0.25f, 0.1f, 1f);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", red);
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", red);
                    go.GetComponent(Il2CppInterop.Runtime.Il2CppType.Of<Renderer>()).Cast<Renderer>().material = mat;
                    used = sn;
                    break;
                }
                Result($"F9 cube #{cubes}: OK at {U.V3(t.position)} (camera '{U.PathOf(cam.transform)}', shader {(used ?? "NONE FOUND - default material")})");
            }
            catch (Exception e) { Result("F9 cube: FAIL " + e.GetType().Name + ": " + e.Message); }
        }

        static readonly List<(Material mat, string prop, Color orig)> tinted = new List<(Material, string, Color)>();

        public static void ToggleTint()
        {
            try
            {
                if (tinted.Count > 0)
                {
                    foreach (var (mat, prop, orig) in tinted) { try { mat.SetColor(prop, orig); } catch { } }
                    Result($"F10 tint: restored {tinted.Count} colour properties");
                    tinted.Clear();
                    return;
                }

                var vol = new Dictionary<IntPtr, double>();
                foreach (var p in U.FindAll("UnityEngine.CoreModule.dll", "UnityEngine", "Renderer"))
                {
                    try
                    {
                        var r = new Renderer(p);
                        if (!r.enabled || U.SceneOfGO(r.gameObject) == null) continue;
                        var s = r.bounds.size;
                        double v = Math.Max(1e-6, (double)s.x * s.y * s.z);
                        foreach (var m in r.sharedMaterials)
                            if (m != null) vol[m.Pointer] = (vol.TryGetValue(m.Pointer, out var o) ? o : 0) + v;
                    }
                    catch { }
                }
                var top = vol.OrderByDescending(kv => kv.Value).Take(3).Select(kv => new Material(kv.Key)).ToList();
                var mars = new Color(1f, 0.42f, 0.25f, 1f);
                var changed = new List<string>();
                foreach (var mat in top)
                {
                    var sh = mat.shader;
                    if (sh == null) continue;
                    int count = (int)U.Call(sh, "GetPropertyCount");
                    for (int i = 0; i < count; i++)
                    {
                        if (U.Call(sh, "GetPropertyType", i).ToString() != "Color") continue;
                        string pn = (string)U.Call(sh, "GetPropertyName", i);
                        string l = pn.ToLowerInvariant();
                        if (!(l.Contains("color") || l.Contains("tint") || l.Contains("albedo"))) continue;
                        if (l.Contains("emission") || l.Contains("spec")) continue;
                        var orig = mat.GetColor(pn);
                        tinted.Add((mat, pn, orig));
                        mat.SetColor(pn, new Color(orig.r * mars.r, orig.g * mars.g, orig.b * mars.b, orig.a));
                        changed.Add($"{Il2.UnityName(mat.Pointer)}.{pn}");
                    }
                }
                Result(changed.Count > 0
                    ? "F10 tint: ON, changed " + string.Join(", ", changed) + "  (press F10 again to restore)"
                    : "F10 tint: found no colour properties on the top materials: " + string.Join(", ", top.Select(m => Il2.UnityName(m.Pointer))));
            }
            catch (Exception e) { Result("F10 tint: FAIL " + e.GetType().Name + ": " + e.Message); }
        }

        static void Result(string s)
        {
            Results.Add($"[{Time.realtimeSinceStartup:F0}s] {s}");
            Out.Report("TEST " + s);
            Plugin.Msg(s);
        }
    }
}
