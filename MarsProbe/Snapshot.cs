using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MarsProbe
{
    /// <summary>
    /// One snapshot = a full picture of the running game at one moment: scenes, every game
    /// component and definition with its field values, statics, rendering, textures, saves.
    /// Each part runs as a coroutine step so the game keeps rendering while we dump.
    /// </summary>
    internal static class Snapshot
    {
        public static int Count;

        public static IEnumerator<bool> Run(string label, bool includeStatics)
        {
            int n = ++Count;
            string dir = $"snap{n:00}_{Sanitize(label)}";
            Plugin.Msg($"Snapshot {n} ({label}) started. The game may stutter for a few seconds.");
            Out.Report("");
            Out.Report($"=== SNAPSHOT {n}: {label}  (t={Time.realtimeSinceStartup:F0}s, scene='{SafeActiveScene()}') ===");

            foreach (var step in new (string, Func<string, IEnumerator<bool>>)[]
            {
                ("env", Env),
                ("hierarchy", Hierarchy),
                ("monobehaviours", MonoBehaviours),
                ("definitions", Definitions),
                ("statics", includeStatics ? Statics : null),
                ("rendering", Rendering),
                ("assets", Assets),
                ("saves", n == 1 || label.StartsWith("manual") ? Saves : null),
            })
            {
                if (step.Item2 == null) continue;
                string key = "snap:" + step.Item1;
                if (Out.ShouldSkip(key)) { Out.Report($"  {step.Item1}: SKIPPED (crashed in an earlier run)"); continue; }
                Out.Begin(key);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                IEnumerator<bool> it = null;
                try { it = step.Item2(dir); } catch (Exception e) { Out.Report($"  {step.Item1}: FAILED to start: {e}"); }
                while (it != null)
                {
                    bool more;
                    try { more = it.MoveNext(); }
                    catch (Exception e) { Out.Report($"  {step.Item1}: ERROR {e.GetType().Name}: {e.Message}"); Plugin.Log.LogWarning($"Snapshot step {step.Item1}: {e}"); break; }
                    if (!more) break;
                    yield return true;
                }
                Out.End(key);
                Out.Report($"  {step.Item1}: done in {sw.ElapsedMilliseconds} ms");
            }
            Tracer.WriteCounts(Path.Combine(dir, "trace_counts.txt"));
            Plugin.Msg($"Snapshot {n} ({label}) finished -> MarsProbe/Report/{dir}");
        }

        static string Sanitize(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray());

        public static string SafeActiveScene()
        {
            try { return SceneManager.GetActiveScene().name; } catch { return "?"; }
        }

        // ------------------------------------------------------------------ env
        static IEnumerator<bool> Env(string dir)
        {
            using var w = Out.Writer(Path.Combine(dir, "env.txt"));
            void L(string s) => w.WriteLine(s);
            void Try(string name, Func<object> f)
            {
                try { L($"  {name} = {U.Managed(f())}"); } catch (Exception e) { L($"  {name} = <{e.GetType().Name}>"); }
            }
            L("[Application]");
            Try("version", () => Application.version);
            Try("unityVersion", () => Application.unityVersion);
            Try("productName", () => Application.productName);
            Try("companyName", () => Application.companyName);
            Try("identifier", () => Application.identifier);
            Try("platform", () => Application.platform);
            Try("dataPath", () => Application.dataPath);
            Try("persistentDataPath", () => Application.persistentDataPath);
            Try("targetFrameRate", () => Application.targetFrameRate);
            Try("runInBackground", () => Application.runInBackground);
            L("[System]");
            Try("os", () => SystemInfo.operatingSystem);
            Try("cpu", () => SystemInfo.processorType);
            Try("cores", () => SystemInfo.processorCount);
            Try("ramMB", () => SystemInfo.systemMemorySize);
            Try("gpu", () => SystemInfo.graphicsDeviceName);
            Try("gpuApi", () => SystemInfo.graphicsDeviceType);
            Try("vramMB", () => SystemInfo.graphicsMemorySize);
            Try("screen", () => Screen.width + "x" + Screen.height);
            L("[Time]");
            foreach (var l in U.StaticProps("UnityEngine.Time")) L(l);
            L("[Physics]");
            foreach (var l in U.StaticProps("UnityEngine.Physics")) L(l);
            L("[RenderSettings]");
            foreach (var l in U.StaticProps("UnityEngine.RenderSettings")) L(l);
            L("[QualitySettings]");
            foreach (var l in U.StaticProps("UnityEngine.QualitySettings", "names")) L(l);
            L("[GraphicsSettings]");
            foreach (var l in U.StaticProps("UnityEngine.Rendering.GraphicsSettings")) L(l);
            yield break;
        }

        // ------------------------------------------------------------------ hierarchy
        static IEnumerator<bool> Hierarchy(string dir)
        {
            using var w = Out.Writer(Path.Combine(dir, "hierarchy.txt"));
            int budget = 30000;
            int sceneCount = SceneManager.sceneCount;
            var roots = new List<(string scene, GameObject go)>();
            for (int i = 0; i < sceneCount; i++)
            {
                var sc = SceneManager.GetSceneAt(i);
                w.WriteLine($"######## SCENE '{sc.name}' path='{sc.path}' loaded={sc.isLoaded} roots={sc.rootCount}");
                if (!sc.isLoaded) continue;
                foreach (var go in sc.GetRootGameObjects()) roots.Add((sc.name, go));
            }
            // DontDestroyOnLoad objects are not in any SceneManager scene: find them via their transforms.
            try
            {
                foreach (var p in U.FindAll("UnityEngine.CoreModule.dll", "UnityEngine", "Transform"))
                {
                    var t = new Transform(p);
                    if (t.parent != null) continue;
                    string sn = U.SceneOfGO(t.gameObject);
                    if (sn == "DontDestroyOnLoad") roots.Add((sn, t.gameObject));
                }
            }
            catch (Exception e) { w.WriteLine("# DontDestroyOnLoad scan failed: " + e.Message); }

            string lastScene = null;
            int k = 0;
            foreach (var (scene, go) in roots)
            {
                if (scene != lastScene) { w.WriteLine(); w.WriteLine($"######## ROOTS OF '{scene}'"); lastScene = scene; }
                Node(w, go.transform, 0, ref budget);
                if (budget <= 0) { w.WriteLine("# … node budget exhausted"); break; }
                if (++k % 20 == 0) yield return true;
            }
            Out.Report($"  hierarchy: {sceneCount} scene(s), {roots.Count} root objects, {30000 - budget} nodes written");
        }

        static readonly Dictionary<IntPtr, string> compListCache = new Dictionary<IntPtr, string>();

        static string Components(GameObject go)
        {
            try
            {
                var comps = go.GetComponents(Il2CppType.Of<Component>());
                var names = new List<string>();
                for (int i = 0; i < comps.Length; i++)
                {
                    var c = comps[i];
                    if (c == null) { names.Add("<missing script>"); continue; }
                    string cn = U.Cls(c.Pointer);
                    if (cn == "UnityEngine.Transform") continue;
                    if (cn.StartsWith("UnityEngine.")) cn = cn.Substring(12);
                    names.Add(cn);
                }
                return names.Count > 0 ? " [" + string.Join(", ", names) + "]" : "";
            }
            catch (Exception e) { return " [components? " + e.GetType().Name + "]"; }
        }

        static void Node(StreamWriter w, Transform t, int depth, ref int budget)
        {
            if (budget-- <= 0) return;
            var go = t.gameObject;
            string ind = new string(' ', depth * 2);
            string pos = depth == 0 ? " pos=" + U.V3(t.position) : "";
            w.WriteLine($"{ind}{Il2.UnityName(go.Pointer)}{(go.activeSelf ? "" : " (inactive)")}{pos}{Components(go)}");
            int cc = t.childCount;
            if (depth >= 9) { if (cc > 0) w.WriteLine($"{ind}  … {cc} children (depth limit)"); return; }
            int show = cc > 40 ? 12 : cc;
            for (int i = 0; i < show; i++) Node(w, t.GetChild(i), depth + 1, ref budget);
            if (cc > show)
            {
                var groups = new Dictionary<string, int>();
                for (int i = show; i < cc; i++)
                {
                    string nm = Il2.UnityName(t.GetChild(i).gameObject.Pointer);
                    nm = System.Text.RegularExpressions.Regex.Replace(nm, @"[\s_\-]*(\(\d+\)|\d+)$", "");
                    groups[nm] = groups.TryGetValue(nm, out var c) ? c + 1 : 1;
                }
                w.WriteLine($"{ind}  … {cc - show} more children: " + string.Join(", ", groups.OrderByDescending(g => g.Value).Take(20).Select(g => $"{g.Key}×{g.Value}")));
            }
        }

        // ------------------------------------------------------------------ game components
        static IEnumerator<bool> MonoBehaviours(string dir)
        {
            using var w = Out.Writer(Path.Combine(dir, "components.txt"));
            using var idx = Out.Writer(Path.Combine(dir, "components_index.txt"));
            w.WriteLine("# Live game MonoBehaviours with all their field values (one level into referenced plain objects).");
            w.WriteLine("# Up to 6 scene instances and 2 prefab/asset instances per class.");
            idx.WriteLine("# class  sceneInstances  assetInstances");
            int classes = 0, live = 0;
            var byClass = GroupByClass("MonoBehaviour");
            yield return true;
            foreach (var ci in Catalog.GameClasses.Where(c => c.IsMB && !c.IsGeneric && !c.IsAbstract))
            {
                string key = "mb:" + ci.Name;
                if (Out.ShouldSkip(key)) { w.WriteLine("# SKIPPED " + key); continue; }
                Out.Begin(key);
                try
                {
                    var objs = byClass.TryGetValue(ci.K, out var found) ? found : new List<IntPtr>();
                    var scene = new List<(IntPtr, string)>();
                    var asset = new List<IntPtr>();
                    foreach (var p in objs)
                    {
                        string sn = U.SceneOfComponent(p);
                        if (sn != null) scene.Add((p, sn)); else asset.Add(p);
                    }
                    idx.WriteLine($"{ci.Name,-70} {scene.Count,6} {asset.Count,6}");
                    if (objs.Count > 0)
                    {
                        classes++; live += scene.Count;
                        w.WriteLine();
                        w.WriteLine($"######## {ci.Name}   scene={scene.Count} assets={asset.Count}");
                        int i = 0;
                        foreach (var (p, sn) in scene.Take(6)) DumpInstance(w, p, ci.K, $"#{i++} scene='{sn}' path='{SafePath(p)}' enabled={SafeEnabled(p)}", 1);
                        foreach (var p in asset.Take(2)) DumpInstance(w, p, ci.K, $"#{i++} ASSET/PREFAB path='{SafePath(p)}'", 1);
                    }
                }
                catch (Exception e) { w.WriteLine($"# {ci.Name}: ERROR {e.GetType().Name}: {e.Message}"); }
                Out.End(key);
                yield return true;
            }
            Out.Report($"  monobehaviours: {classes} game component classes have instances, {live} live scene instances");
        }

        /// <summary>One FindObjectsOfTypeAll for the Unity base class, grouped by each object's exact il2cpp class.</summary>
        static Dictionary<IntPtr, List<IntPtr>> GroupByClass(string unityBase)
        {
            var d = new Dictionary<IntPtr, List<IntPtr>>();
            foreach (var p in U.FindAll("UnityEngine.CoreModule.dll", "UnityEngine", unityBase))
            {
                IntPtr k = IL2CPP.il2cpp_object_get_class(p);
                if (!d.TryGetValue(k, out var l)) d[k] = l = new List<IntPtr>();
                l.Add(p);
            }
            return d;
        }

        static string SafePath(IntPtr comp)
        {
            try { return U.PathOf(new Component(comp).transform); } catch { return "?"; }
        }

        static string SafeEnabled(IntPtr comp)
        {
            try { var b = new Behaviour(comp); return (b.enabled && b.gameObject.activeInHierarchy).ToString(); } catch { return "?"; }
        }

        public static void DumpInstance(StreamWriter w, IntPtr obj, IntPtr k, string header, int depth)
        {
            w.WriteLine($"  -- {header}");
            foreach (var f in Il2.InstanceFieldsDeep(k))
            {
                string v;
                try { v = Il2.FormatValue(obj + (int)f.Offset, f.Type, depth); }
                catch (Exception e) { v = "<" + e.GetType().Name + ">"; }
                w.WriteLine($"     {f.Name} = {v}");
            }
        }

        // ------------------------------------------------------------------ ScriptableObject definitions
        static IEnumerator<bool> Definitions(string dir)
        {
            using var w = Out.Writer(Path.Combine(dir, "definitions.txt"));
            w.WriteLine("# Every loaded game ScriptableObject (tool/building/upgrade definitions etc.) with field values.");
            int classes = 0, total = 0;
            var byClass = GroupByClass("ScriptableObject");
            yield return true;
            foreach (var ci in Catalog.GameClasses.Where(c => c.IsSO && !c.IsGeneric && !c.IsAbstract))
            {
                string key = "so:" + ci.Name;
                if (Out.ShouldSkip(key)) { w.WriteLine("# SKIPPED " + key); continue; }
                Out.Begin(key);
                try
                {
                    var objs = byClass.TryGetValue(ci.K, out var found) ? found : new List<IntPtr>();
                    if (objs.Count > 0)
                    {
                        classes++; total += objs.Count;
                        w.WriteLine();
                        w.WriteLine($"######## {ci.Name}   count={objs.Count}");
                        int i = 0;
                        foreach (var p in objs.Take(300))
                        {
                            DumpInstance(w, p, ci.K, $"#{i++} '{Il2.UnityName(p)}'", 2);
                            if (i % 50 == 0) w.Flush();
                        }
                    }
                }
                catch (Exception e) { w.WriteLine($"# {ci.Name}: ERROR {e.GetType().Name}: {e.Message}"); }
                Out.End(key);
                yield return true;
            }
            Out.Report($"  definitions: {classes} ScriptableObject classes, {total} assets");
        }

        // ------------------------------------------------------------------ statics
        static IEnumerator<bool> Statics(string dir)
        {
            using var w = Out.Writer(Path.Combine(dir, "statics.txt"));
            w.WriteLine("# Static fields of game classes (singletons, settings, caches).");
            int n = 0;
            foreach (var ci in Catalog.GameClasses.Where(c => c.HasStatics && !c.IsGeneric && !c.IsInterface))
            {
                string key = "static:" + ci.Name;
                if (Out.ShouldSkip(key)) { w.WriteLine("# SKIPPED " + key); continue; }
                Out.Begin(key);
                try
                {
                    IL2CPP.il2cpp_runtime_class_init(ci.K);
                    w.WriteLine(ci.Name);
                    foreach (var f in Il2.Fields(ci.K))
                    {
                        if (!f.IsStatic || f.IsLiteral) continue;
                        string v;
                        try { v = Il2.FormatStatic(f, 1); } catch (Exception e) { v = "<" + e.GetType().Name + ">"; }
                        w.WriteLine($"     {f.Name} = {v}");
                    }
                }
                catch (Exception e) { w.WriteLine($"# {ci.Name}: ERROR {e.GetType().Name}: {e.Message}"); }
                Out.End(key);
                if (++n % 25 == 0) { w.Flush(); yield return true; }
            }
        }

        // ------------------------------------------------------------------ rendering
        class MatAgg { public IntPtr Mat; public string Name, Shader; public int Renderers; public double Volume; public List<string> Samples = new List<string>(); }

        static IEnumerator<bool> Rendering(string dir)
        {
            using var w = Out.Writer(Path.Combine(dir, "rendering.txt"));
            var mats = new Dictionary<IntPtr, MatAgg>();
            var meshes = new List<(int verts, string line)>();
            int renderers = 0;

            var rlist = U.FindAll("UnityEngine.CoreModule.dll", "UnityEngine", "Renderer");
            int i = 0;
            foreach (var p in rlist)
            {
                if (++i % 400 == 0) yield return true;
                Renderer r;
                try
                {
                    r = new Renderer(p);
                    if (U.SceneOfGO(r.gameObject) == null) continue;
                }
                catch { continue; }
                renderers++;
                try
                {
                    var b = r.bounds;
                    double vol = Math.Max(1e-6, (double)b.size.x * b.size.y * b.size.z);
                    var sm = r.sharedMaterials;
                    for (int m = 0; m < sm.Length; m++)
                    {
                        var mat = sm[m];
                        if (mat == null) continue;
                        if (!mats.TryGetValue(mat.Pointer, out var a))
                        {
                            a = new MatAgg { Mat = mat.Pointer, Name = Il2.UnityName(mat.Pointer) };
                            try { a.Shader = mat.shader != null ? mat.shader.name : "null"; } catch { a.Shader = "?"; }
                            mats[mat.Pointer] = a;
                        }
                        a.Renderers++;
                        a.Volume += vol;
                        if (a.Samples.Count < 4) a.Samples.Add(Il2.UnityName(r.gameObject.Pointer));
                    }
                }
                catch { }
            }
            yield return true;

            foreach (var p in U.FindAll("UnityEngine.CoreModule.dll", "UnityEngine", "MeshFilter"))
            {
                try
                {
                    var mf = new MeshFilter(p);
                    if (U.SceneOfGO(mf.gameObject) == null) continue;
                    var mesh = mf.sharedMesh;
                    if (mesh == null) continue;
                    int vc = mesh.vertexCount;
                    meshes.Add((vc, $"verts={vc,8} sub={mesh.subMeshCount} readable={mesh.isReadable} size={U.V3(mesh.bounds.size)} mesh='{Il2.UnityName(mesh.Pointer)}' at '{U.PathOf(mf.transform)}' worldPos={U.V3(mf.transform.position)} scale={U.V3(mf.transform.lossyScale)}"));
                }
                catch { }
            }
            yield return true;

            w.WriteLine($"# {renderers} scene renderers, {mats.Count} distinct materials, {meshes.Count} scene mesh filters");
            w.WriteLine();
            w.WriteLine("######## MATERIALS by total bounds volume (the planet surface should be near the top)");
            var byVol = mats.Values.OrderByDescending(a => a.Volume).ToList();
            foreach (var a in byVol.Take(40))
                w.WriteLine($"  vol={a.Volume,14:E2} renderers={a.Renderers,5} mat='{a.Name}' shader='{a.Shader}' e.g. {string.Join(" | ", a.Samples)}");
            w.WriteLine();
            w.WriteLine("######## MATERIALS by renderer count");
            foreach (var a in mats.Values.OrderByDescending(a => a.Renderers).Take(40))
                w.WriteLine($"  renderers={a.Renderers,5} mat='{a.Name}' shader='{a.Shader}' e.g. {string.Join(" | ", a.Samples)}");
            yield return true;

            w.WriteLine();
            w.WriteLine("######## MATERIAL DETAILS (top 30 by volume + top 15 by count)");
            var detail = byVol.Take(30).Concat(mats.Values.OrderByDescending(a => a.Renderers).Take(15)).Distinct().ToList();
            foreach (var a in detail)
            {
                MaterialDetail(w, a.Mat);
                yield return true;
            }

            w.WriteLine();
            w.WriteLine("######## BIGGEST SCENE MESHES");
            foreach (var m in meshes.OrderByDescending(m => m.verts).Take(40)) w.WriteLine("  " + m.line);

            w.WriteLine();
            w.WriteLine("######## LIGHTS");
            foreach (var p in U.FindAll("UnityEngine.CoreModule.dll", "UnityEngine", "Light"))
            {
                try
                {
                    var l = new Light(p);
                    if (U.SceneOfGO(l.gameObject) == null) continue;
                    w.WriteLine($"  '{U.PathOf(l.transform)}' type={l.type} enabled={l.enabled} color={U.Managed(l.color)} intensity={Il2.FormatFloat(l.intensity)} shadows={l.shadows} range={Il2.FormatFloat(l.range)} rot={U.V3(l.transform.eulerAngles)}");
                }
                catch (Exception e) { w.WriteLine("  light? " + e.GetType().Name); }
            }

            w.WriteLine();
            w.WriteLine("######## CAMERAS");
            foreach (var p in U.FindAll("UnityEngine.CoreModule.dll", "UnityEngine", "Camera"))
            {
                try
                {
                    var c = new Camera(p);
                    if (U.SceneOfGO(c.gameObject) == null) continue;
                    w.WriteLine($"  '{U.PathOf(c.transform)}' enabled={c.enabled} fov={Il2.FormatFloat(c.fieldOfView)} near={Il2.FormatFloat(c.nearClipPlane)} far={Il2.FormatFloat(c.farClipPlane)} clear={c.clearFlags} bg={U.Managed(c.backgroundColor)} mask=0x{c.cullingMask:X} depth={Il2.FormatFloat(c.depth)} pos={U.V3(c.transform.position)}");
                }
                catch (Exception e) { w.WriteLine("  camera? " + e.GetType().Name); }
            }

            w.WriteLine();
            w.WriteLine("######## POST-PROCESSING VOLUMES");
            try
            {
                IntPtr vk = IntPtr.Zero;
                foreach (var img in Il2.AllImages())
                {
                    string iname = Il2.Str(IL2CPP.il2cpp_image_get_name(img));
                    if (!iname.StartsWith("Unity.RenderPipelines")) continue;
                    foreach (var k in Il2.Classes(img)) if (Il2.ClassName(k) == "UnityEngine.Rendering.Volume") { vk = k; break; }
                    if (vk != IntPtr.Zero) break;
                }
                if (vk == IntPtr.Zero) w.WriteLine("  (no UnityEngine.Rendering.Volume class)");
                else
                    foreach (var p in U.FindAll(vk, false))
                    {
                        w.WriteLine($"  Volume on '{SafePath(p)}' scene='{U.SceneOfComponent(p)}'");
                        DumpInstance(w, p, vk, "fields", 2);
                    }
            }
            catch (Exception e) { w.WriteLine("  volumes? " + e.Message); }
            Out.Report($"  rendering: {renderers} renderers, {mats.Count} materials; top material by volume: {(byVol.Count > 0 ? byVol[0].Name + " / " + byVol[0].Shader : "-")}");
        }

        public static void MaterialDetail(StreamWriter w, IntPtr matPtr)
        {
            try
            {
                var mat = new Material(matPtr);
                var sh = mat.shader;
                w.WriteLine($"  MATERIAL '{Il2.UnityName(matPtr)}' shader='{(sh != null ? sh.name : "null")}' queue={mat.renderQueue}");
                try { w.WriteLine("    keywords: " + string.Join(" ", mat.shaderKeywords)); } catch { }
                if (sh == null) return;
                int count = (int)U.Call(sh, "GetPropertyCount");
                for (int i = 0; i < count; i++)
                {
                    string pn = (string)U.Call(sh, "GetPropertyName", i);
                    string pt = U.Call(sh, "GetPropertyType", i).ToString();
                    string val;
                    try
                    {
                        switch (pt)
                        {
                            case "Color": val = U.Managed(mat.GetColor(pn)); break;
                            case "Vector": val = U.Managed(mat.GetVector(pn)); break;
                            case "Float":
                            case "Range": val = Il2.FormatFloat(mat.GetFloat(pn)); break;
                            case "Int": val = U.Managed(U.Call(mat, "GetInt", pn)); break;
                            case "Texture":
                                var tex = mat.GetTexture(pn);
                                val = tex == null ? "null" : $"'{Il2.UnityName(tex.Pointer)}' {tex.width}x{tex.height} ({U.Cls(tex.Pointer)})";
                                break;
                            default: val = "?"; break;
                        }
                    }
                    catch (Exception e) { val = "<" + e.GetType().Name + ">"; }
                    w.WriteLine($"    {pt,-8} {pn} = {val}");
                }
            }
            catch (Exception e) { w.WriteLine("    material detail failed: " + e.GetType().Name + " " + e.Message); }
        }

        // ------------------------------------------------------------------ assets
        static IEnumerator<bool> Assets(string dir)
        {
            using var w = Out.Writer(Path.Combine(dir, "assets.txt"));

            w.WriteLine("######## TEXTURES (largest 80)");
            var texs = new List<(long px, string line)>();
            foreach (var p in U.FindAll("UnityEngine.CoreModule.dll", "UnityEngine", "Texture"))
            {
                try
                {
                    var t = new Texture(p);
                    long px = (long)t.width * t.height;
                    texs.Add((px, $"{t.width,5}x{t.height,-5} {U.Cls(p).Replace("UnityEngine.", ""),-16} dim={t.dimension} mips={t.mipmapCount} '{Il2.UnityName(p)}'"));
                }
                catch { }
            }
            foreach (var t in texs.OrderByDescending(t => t.px).Take(80)) w.WriteLine("  " + t.line);
            w.WriteLine($"  ({texs.Count} textures loaded)");
            yield return true;

            w.WriteLine();
            w.WriteLine("######## MESH ASSETS (largest 60)");
            var ms = new List<(int v, string line)>();
            foreach (var p in U.FindAll("UnityEngine.CoreModule.dll", "UnityEngine", "Mesh"))
            {
                try
                {
                    var m = new Mesh(p);
                    ms.Add((m.vertexCount, $"verts={m.vertexCount,8} sub={m.subMeshCount} readable={m.isReadable} size={U.V3(m.bounds.size)} '{Il2.UnityName(p)}'"));
                }
                catch { }
            }
            foreach (var m in ms.OrderByDescending(m => m.v).Take(60)) w.WriteLine("  " + m.line);
            w.WriteLine($"  ({ms.Count} meshes loaded)");
            yield return true;

            w.WriteLine();
            w.WriteLine("######## SHADERS (all loaded)");
            var shaders = new SortedSet<string>();
            foreach (var p in U.FindAll("UnityEngine.CoreModule.dll", "UnityEngine", "Shader")) { try { shaders.Add(Il2.UnityName(p)); } catch { } }
            foreach (var s in shaders) w.WriteLine("  " + s);

            w.WriteLine();
            w.WriteLine("######## COMPUTE SHADERS");
            foreach (var p in U.FindAll("UnityEngine.CoreModule.dll", "UnityEngine", "ComputeShader")) { try { w.WriteLine("  " + Il2.UnityName(p)); } catch { } }

            w.WriteLine();
            w.WriteLine("######## MATERIAL ASSETS (names + shader)");
            int mc = 0;
            foreach (var p in U.FindAll("UnityEngine.CoreModule.dll", "UnityEngine", "Material"))
            {
                try { var m = new Material(p); w.WriteLine($"  '{Il2.UnityName(p)}' shader='{(m.shader != null ? m.shader.name : "null")}'"); mc++; } catch { }
                if (mc % 300 == 0) yield return true;
            }
            yield return true;

            w.WriteLine();
            w.WriteLine("######## PREFAB ROOTS (GameObjects that are loaded but not in a scene)");
            int pc = 0;
            foreach (var p in U.FindAll("UnityEngine.CoreModule.dll", "UnityEngine", "GameObject"))
            {
                bool wrote = false;
                try
                {
                    var go = new GameObject(p);
                    if (U.SceneOfGO(go) == null && go.transform.parent == null)
                    {
                        w.WriteLine($"  '{Il2.UnityName(p)}'{Components(go)} children={go.transform.childCount}");
                        wrote = true;
                    }
                }
                catch { }
                if (!wrote) continue;
                if (++pc >= 4000) { w.WriteLine("  … cap reached"); break; }
                if (pc % 200 == 0) yield return true;
            }
            Out.Report($"  assets: {texs.Count} textures, {ms.Count} meshes, {shaders.Count} shaders, {pc} prefab roots");
        }

        // ------------------------------------------------------------------ saves
        static IEnumerator<bool> Saves(string dir)
        {
            using var w = Out.Writer(Path.Combine(dir, "saves.txt"));
            string root = Application.persistentDataPath;
            w.WriteLine("# persistentDataPath = " + root);
            if (!Directory.Exists(root)) { w.WriteLine("# (missing)"); yield break; }
            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f)).OrderBy(f => f.FullName).ToList();
            foreach (var f in files)
                w.WriteLine($"  {f.Length,12:N0}  {f.LastWriteTime:yyyy-MM-dd HH:mm}  {Path.GetRelativePath(root, f.FullName)}");
            yield return true;

            w.WriteLine();
            w.WriteLine("######## PREVIEWS (first 1.5 KB of the most recent files, logs excluded)");
            foreach (var f in files.Where(f => !f.Name.EndsWith(".log") && !f.FullName.Contains("Crash")).OrderByDescending(f => f.LastWriteTime).Take(8))
            {
                w.WriteLine();
                w.WriteLine($"--- {Path.GetRelativePath(root, f.FullName)} ({f.Length:N0} bytes)");
                try
                {
                    byte[] buf = new byte[Math.Min(1536, f.Length)];
                    using (var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) fs.Read(buf, 0, buf.Length);
                    int printable = buf.Count(b => b == 9 || b == 10 || b == 13 || (b >= 32 && b < 127));
                    if (buf.Length > 0 && printable > buf.Length * 0.9) w.WriteLine(Encoding.UTF8.GetString(buf));
                    else
                    {
                        w.WriteLine($"(binary; first bytes as hex)");
                        for (int i = 0; i < Math.Min(buf.Length, 512); i += 32)
                        {
                            var seg = buf.Skip(i).Take(32).ToArray();
                            w.WriteLine($"  {i:X4}: {BitConverter.ToString(seg).Replace("-", " "),-96} {new string(seg.Select(b => b >= 32 && b < 127 ? (char)b : '.').ToArray())}");
                        }
                    }
                }
                catch (Exception e) { w.WriteLine("  (unreadable: " + e.Message + ")"); }
            }
        }
    }
}
