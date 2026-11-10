using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MoonCleaners;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// "research" in the dev terminal: everything we need to know to add Mars machines to the game's own
    /// build system, written to BepInEx/MarsMod/research (it goes into the report):
    ///  * every structure kind the game knows (definition, price, footprint, conveyor ports, prefab)
    ///  * the build menu catalogue (kinds, names, categories, card images)
    ///  * every machine prefab: parts, meshes, materials with all their properties
    ///  * reference renders of each machine from two angles, so new models can match the game's style
    /// </summary>
    internal static class Research
    {
        static string Dir => Path.Combine(BepInEx.Paths.BepInExRootPath, "MarsMod", "research");

        // ------------------------------------------------------------------ text dumps

        public static IEnumerable<string> Run()
        {
            var world = Commands.FindWorld();
            if (world == null) { yield return "Go into a world first."; yield break; }
            Directory.CreateDirectory(Dir);

            var defs = new StringBuilder();
            var prefabs = new StringBuilder();
            var materials = new Dictionary<IntPtr, Material>();
            var seenDefs = new HashSet<IntPtr>();
            renderQueue.Clear();
            int found = 0;

            defs.AppendLine("# kind → definition (from MoonWorld.BuildDefinition)");
            foreach (int k in Enumerable.Range(0, MoonDepotSave.MaxStructureKind + 1).Concat(new[] { MoonPlacementKinds() }))
            {
                MoonDepotBuildDefinition d = null;
                try { d = world.BuildDefinition(k); } catch (Exception e) { defs.AppendLine($"{k}: error {(e.InnerException ?? e).Message}"); continue; }
                if (d == null) continue;
                found++;
                defs.AppendLine($"{k}: {Describe(d)}");
                if (seenDefs.Add(d.Pointer) && d.prefab != null)
                {
                    prefabs.AppendLine($"######## kind {k} '{d.displayName}' prefab '{d.prefab.name}'");
                    Walk(d.prefab.transform, 0, prefabs, materials);
                    prefabs.AppendLine();
                    renderQueue.Add((k, d));
                }
            }

            defs.AppendLine();
            defs.AppendLine("# depot definition and its additionalStructures array (index → definition)");
            try
            {
                var depot = world.DepotDefinition;
                defs.AppendLine($"depot: {Describe(depot)}");
                var add = depot?.additionalStructures;
                if (add != null) for (int i = 0; i < add.Length; i++) defs.AppendLine($"  [{i}] {(add[i] == null ? "null" : add[i].name + " / " + add[i].displayName)}");
            }
            catch (Exception e) { defs.AppendLine("depot: " + e.Message); }

            defs.AppendLine();
            defs.AppendLine("# build menu catalogue");
            try
            {
                var bases = MoonDepotBuilder.baseStructures;
                defs.AppendLine($"baseStructures ({bases?.Length ?? 0}):");
                if (bases != null) foreach (var c in bases) defs.AppendLine($"  kind {c.kind}, '{c.name}', category '{c.category}', image '{c.image}'");
                var cats = MoonDepotBuilder.catalogCategories;
                if (cats != null) defs.AppendLine("catalogCategories: " + string.Join(" | ", cats));
                var secs = MoonDepotBuilder.structureSections;
                if (secs != null) defs.AppendLine("structureSections: " + string.Join(" | ", secs));
                foreach (var b in Look.All<MoonDepotBuilder>())
                {
                    if (!Look.InScene(b.gameObject)) continue;
                    var es = b.structureEntries;
                    defs.AppendLine($"builder '{b.gameObject.name}' structureEntries ({es?.Length ?? 0}):");
                    if (es != null) foreach (var c in es) defs.AppendLine($"  kind {c.kind}, '{c.name}', category '{c.category}', image '{c.image}'");
                }
            }
            catch (Exception e) { defs.AppendLine("catalogue: " + (e.InnerException ?? e).Message); }

            File.WriteAllText(Path.Combine(Dir, "structures.txt"), defs.ToString());
            File.WriteAllText(Path.Combine(Dir, "prefabs.txt"), prefabs.ToString());

            var mats = new StringBuilder();
            foreach (var m in materials.Values) DumpMaterial(m, mats);
            File.WriteAllText(Path.Combine(Dir, "materials.txt"), mats.ToString());

            yield return $"{found} structure kinds, {renderQueue.Count} prefabs, {materials.Count} materials written to BepInEx/MarsMod/research.";
            yield return "Rendering reference pictures of every machine now (a few seconds, the screen may stutter)…";
            renderIndex = 0; renderView = 0; rendering = true; frameWait = 0;
        }

        static int MoonPlacementKinds() => 1000; // ConveyorKind

        static string Describe(MoonDepotBuildDefinition d)
        {
            if (d == null) return "null";
            var sb = new StringBuilder();
            sb.Append($"'{d.name}' \"{d.displayName}\" price ${d.priceDollars} scale {d.scale} half {d.halfFootprint.x:0.##}x{d.halfFootprint.y:0.##} floor {d.floorOffset:0.##} height {d.height:0.##} reach {d.reach:0.##} tol {d.groundTolerance:0.##} onDust {d.standsOnDust}");
            sb.Append($" prefab '{(d.prefab != null ? d.prefab.name : "-")}' module {(d.module != null ? d.module.assetId : "-")} skins {d.skinPrefabs?.Length ?? 0} extra {d.additionalStructures?.Length ?? 0}");
            var ports = d.conveyorPorts;
            if (ports != null && ports.Length > 0)
            {
                sb.Append(" ports:");
                for (int i = 0; i < ports.Length; i++)
                {
                    var p = ports[i];
                    sb.Append($" [{p.name} {(p.output ? "OUT" : "in")} at {V(p.position)} dir {V(p.direction)}]");
                }
            }
            return sb.ToString();
        }

        static string V(Vector3 v) => $"({v.x:0.##},{v.y:0.##},{v.z:0.##})";

        static void Walk(Transform t, int depth, StringBuilder sb, Dictionary<IntPtr, Material> mats)
        {
            if (depth > 12) return;
            string pad = new string(' ', depth * 2);
            var comps = t.GetComponents(Il2CppType.Of<Component>());
            var names = new List<string>();
            foreach (var c in comps) if (c != null) names.Add(c.GetIl2CppType().Name);
            sb.Append($"{pad}{t.name} pos {V(t.localPosition)} rot {V(t.localEulerAngles)} scale {V(t.localScale)} [{string.Join(", ", names)}]");
            var mf = t.GetComponent(Il2CppType.Of<MeshFilter>())?.TryCast<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                var m = mf.sharedMesh;
                sb.Append($" mesh '{m.name}' verts {m.vertexCount} sub {m.subMeshCount} readable {m.isReadable} size {V(m.bounds.size)}");
            }
            var r = t.GetComponent(Il2CppType.Of<Renderer>())?.TryCast<Renderer>();
            if (r != null)
            {
                var sm = r.sharedMaterials;
                var mn = new List<string>();
                for (int i = 0; i < sm.Length; i++) { var m = sm[i]; if (m == null) continue; mn.Add(m.name); mats[m.Pointer] = m; }
                sb.Append(" mats [" + string.Join(", ", mn) + "]");
            }
            sb.AppendLine();
            for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, sb, mats);
        }

        static void DumpMaterial(Material m, StringBuilder sb)
        {
            var sh = m.shader;
            sb.AppendLine($"MATERIAL '{m.name}' shader '{(sh != null ? sh.name : "-")}' queue {m.renderQueue} keywords [{string.Join(" ", m.shaderKeywords ?? new Il2CppStringArray(0))}]");
            if (sh == null) return;
            int n = sh.GetPropertyCount();
            for (int i = 0; i < n; i++)
            {
                string name = sh.GetPropertyName(i);
                var type = sh.GetPropertyType(i).ToString();
                string val;
                try
                {
                    switch (type)
                    {
                        case "Color": var c = m.GetColor(name); val = $"{c.r:0.###} {c.g:0.###} {c.b:0.###} {c.a:0.###}"; break;
                        case "Vector": var v = m.GetVector(name); val = $"{v.x:0.###} {v.y:0.###} {v.z:0.###} {v.w:0.###}"; break;
                        case "Float": case "Range": val = m.GetFloat(name).ToString("0.###"); break;
                        case "Texture": var tx = m.GetTexture(name); val = tx == null ? "null" : $"'{tx.name}' {tx.width}x{tx.height}"; break;
                        default: val = "?"; break;
                    }
                }
                catch { val = "?"; }
                sb.AppendLine($"    {type,-8} {name} = {val}");
            }
        }

        // ------------------------------------------------------------------ reference renders

        static readonly List<(int kind, MoonDepotBuildDefinition def)> renderQueue = new List<(int, MoonDepotBuildDefinition)>();
        static bool rendering;
        static int renderIndex, renderView, frameWait;
        static GameObject stage, clone, camGo;
        static Camera cam;
        static RenderTexture rt;
        const int Layer = 31, Size = 512;

        /// <summary>Called every frame by MarsWorld.</summary>
        static float pendingAt = -1f;
        static Action<string> pendingSay;

        /// <summary>Terminal: set noon (so the renders are sunlit), then run two seconds later.</summary>
        public static void Request(Action<string> say)
        {
            if (Commands.FindWorld() == null) { say("Go into a world first."); return; }
            Commands.SetTime("noon", out string msg);
            say(msg);
            say("Research starts in 2 seconds…");
            pendingAt = Time.realtimeSinceStartup + 2f;
            pendingSay = say;
        }

        public static void Frame()
        {
            if (pendingAt > 0f && Time.realtimeSinceStartup >= pendingAt)
            {
                pendingAt = -1f;
                try { foreach (var l in Run()) { Plugin.Log.LogMessage("[research] " + l); pendingSay?.Invoke(l); } }
                catch (Exception e) { Plugin.Log.LogWarning("[research] failed: " + (e.InnerException ?? e)); }
            }
            if (!rendering) return;
            try
            {
                if (frameWait > 0) { frameWait--; return; }
                if (cam != null && cam.enabled)
                {
                    // The camera drew last frame: save the picture.
                    Save(renderQueue[renderIndex], renderView);
                    cam.enabled = false;
                    renderView++;
                    if (renderView >= 2) { renderView = 0; renderIndex++; DestroyClone(); }
                }
                if (renderIndex >= renderQueue.Count) { Finish(); return; }
                if (clone == null) MakeClone(renderQueue[renderIndex].def);
                Aim(renderView);
                cam.enabled = true;
                frameWait = 1;
            }
            catch (Exception e) { Plugin.Log.LogWarning("[research] render failed: " + (e.InnerException ?? e)); Finish(); }
        }

        static void MakeClone(MoonDepotBuildDefinition def)
        {
            if (stage == null)
            {
                stage = new GameObject(); stage.name = "Mars research stage";
                // Far out along the sun direction: sunlit, nothing else around.
                var sun = Haze.SunDir.sqrMagnitude > 0.5f ? Haze.SunDir : Vector3.up;
                stage.transform.position = sun.normalized * 9000f;
                camGo = new GameObject(); camGo.name = "Mars research camera";
                cam = camGo.AddComponent(Il2CppType.Of<Camera>()).Cast<Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.16f, 0.17f, 0.2f, 1f);
                cam.cullingMask = 1 << Layer;
                cam.fieldOfView = 28f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 400f;
                rt = new RenderTexture(Size, Size, 24);
                cam.targetTexture = rt;
            }
            clone = new GameObject(); clone.name = "Mars research clone";
            clone.transform.SetParent(stage.transform, false);
            clone.layer = Layer;
            if (def.prefab != null) Copy(def.prefab.transform, clone.transform);
        }

        /// <summary>Visual copy: only meshes and materials, none of the game's scripts.</summary>
        static void Copy(Transform src, Transform dstParent)
        {
            for (int i = 0; i < src.childCount; i++)
            {
                var s = src.GetChild(i);
                if (!s.gameObject.activeSelf) continue;
                var d = new GameObject(); d.name = s.name; d.layer = Layer;
                d.transform.SetParent(dstParent, false);
                d.transform.localPosition = s.localPosition; d.transform.localRotation = s.localRotation; d.transform.localScale = s.localScale;
                var mf = s.GetComponent(Il2CppType.Of<MeshFilter>())?.TryCast<MeshFilter>();
                var mr = s.GetComponent(Il2CppType.Of<MeshRenderer>())?.TryCast<MeshRenderer>();
                if (mf != null && mr != null && mf.sharedMesh != null)
                {
                    d.AddComponent(Il2CppType.Of<MeshFilter>()).Cast<MeshFilter>().sharedMesh = mf.sharedMesh;
                    d.AddComponent(Il2CppType.Of<MeshRenderer>()).Cast<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
                }
                Copy(s, d.transform);
            }
            // The root's own mesh, if any.
            if (src.parent == null || dstParent.name == "Mars research clone")
            {
                var mf = src.GetComponent(Il2CppType.Of<MeshFilter>())?.TryCast<MeshFilter>();
                var mr = src.GetComponent(Il2CppType.Of<MeshRenderer>())?.TryCast<MeshRenderer>();
                if (mf != null && mr != null && mf.sharedMesh != null && dstParent.GetComponent(Il2CppType.Of<MeshFilter>()) == null)
                {
                    dstParent.gameObject.AddComponent(Il2CppType.Of<MeshFilter>()).Cast<MeshFilter>().sharedMesh = mf.sharedMesh;
                    dstParent.gameObject.AddComponent(Il2CppType.Of<MeshRenderer>()).Cast<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
                }
            }
        }

        static void Aim(int view)
        {
            // Frame the clone's bounds.
            var rs = clone.GetComponentsInChildren(Il2CppType.Of<Renderer>());
            Bounds b = default; bool any = false;
            foreach (var c in rs) { var r = c.TryCast<Renderer>(); if (r == null) continue; if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            if (!any) { b.center = clone.transform.position; b.size = Vector3.one; }
            var up = stage.transform.position.normalized;
            clone.transform.rotation = Quaternion.FromToRotation(Vector3.up, up);
            var fwd = Vector3.Cross(up, Vector3.right).normalized; if (fwd.sqrMagnitude < 0.1f) fwd = Vector3.forward;
            var right = Vector3.Cross(up, fwd);
            Vector3 dir;
            switch (view)
            {
                case 0: dir = (fwd + right * 0.8f + up * 0.6f).normalized; break;       // front 3/4, from above
                case 1: dir = (-fwd - right * 0.8f + up * 0.45f).normalized; break;     // back 3/4
                default: dir = (right + up * 0.15f).normalized; break;                  // side, eye level
            }
            float radius = Mathf.Max(0.5f, b.extents.magnitude);
            float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.08f;
            camGo.transform.position = b.center + dir * dist;
            camGo.transform.rotation = Quaternion.LookRotation(b.center - camGo.transform.position, up);
            cam.farClipPlane = dist + radius * 3f;
        }

        static void Save((int kind, MoonDepotBuildDefinition def) item, int view)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var ctor = typeof(Texture2D).GetConstructor(new[] { typeof(int), typeof(int), typeof(TextureFormat), typeof(bool) });
            var tex = (Texture2D)ctor.Invoke(new object[] { Size, Size, TextureFormat.RGBA32, false });
            var r = default(Rect); r.width = Size; r.height = Size;
            tex.ReadPixels(r, 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            string safe = new string((item.def.displayName ?? item.def.name ?? "x").Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
            Png.Save(tex, Path.Combine(Dir, "renders", $"{item.kind:000}_{safe}_{view}.png"));
            UnityEngine.Object.Destroy(tex);
        }

        static void DestroyClone() { if (clone != null) UnityEngine.Object.Destroy(clone); clone = null; }

        static void Finish()
        {
            rendering = false;
            DestroyClone();
            if (stage != null) UnityEngine.Object.Destroy(stage);
            if (camGo != null) UnityEngine.Object.Destroy(camGo);
            if (rt != null) UnityEngine.Object.Destroy(rt);
            stage = camGo = null; cam = null; rt = null;
            Plugin.Log.LogMessage($"[research] done: {renderIndex} machines rendered into BepInEx/MarsMod/research/renders");
        }
    }

    internal static class Png
    {
        public static void Save(Texture2D tex, string path)
        {
            try
            {
                var conv = R.T("UnityEngine.ImageConversion", "UnityEngine.ImageConversionModule");
                var m = conv?.GetMethod("EncodeToPNG", new[] { typeof(Texture2D) });
                if (m == null) return;
                var bytes = m.Invoke(null, new object[] { tex }) as Il2CppStructArray<byte>;
                if (bytes == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, bytes.ToArray());
            }
            catch (Exception e) { Log.Once("png", "PNG save failed: " + (e.InnerException ?? e).Message); }
        }

        /// <summary>Load a PNG/JPG file into a new texture.</summary>
        public static Texture2D Load(byte[] data, string name)
        {
            var conv = R.T("UnityEngine.ImageConversion", "UnityEngine.ImageConversionModule");
            // LoadImage(Texture2D, byte[], bool): the overload taking the interop byte array (not the ReadOnlySpan one).
            var m = conv?.GetMethods().FirstOrDefault(x => x.Name == "LoadImage" && x.GetParameters().Length == 3
                         && x.GetParameters()[1].ParameterType == typeof(Il2CppStructArray<byte>))
                    ?? conv?.GetMethods().FirstOrDefault(x => x.Name == "LoadImage" && x.GetParameters().Length == 2
                         && x.GetParameters()[1].ParameterType == typeof(Il2CppStructArray<byte>));
            if (m == null) return null;
            var ctor = typeof(Texture2D).GetConstructor(new[] { typeof(int), typeof(int), typeof(TextureFormat), typeof(bool) });
            var tex = (Texture2D)ctor.Invoke(new object[] { 2, 2, TextureFormat.RGBA32, true });
            tex.name = name;
            var args = m.GetParameters().Length == 3 ? new object[] { tex, new Il2CppStructArray<byte>(data), false } : new object[] { tex, new Il2CppStructArray<byte>(data) };
            if (!(bool)m.Invoke(null, args)) throw new InvalidDataException("image could not be decoded");
            UnityEngine.Object.DontDestroyOnLoad(tex);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return tex;
        }
    }
}
