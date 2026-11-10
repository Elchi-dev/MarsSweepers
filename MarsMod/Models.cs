using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// Turns the Blender models (.glb) into game objects.
    ///
    ///  * Materials are matched BY NAME to the game's own materials (e.g. "collectortangerineenamel"), so a Mars
    ///    machine uses exactly the same shader and colours as the Collector. Names starting with "mars:" are
    ///    Mars-only materials, made from a game material as template with the colours from the model file.
    ///  * All static parts are merged into one mesh per material (fast to draw). Parts under a node named
    ///    "anim_…" stay separate so they can move (drill column, fans, gears).
    ///  * Empty nodes are markers: "port_N" = conveyor port (its direction is the belt direction),
    ///    "anchor_…" = effect anchors. Nodes named "COL_…" become box colliders and are not drawn.
    ///  * Models come from the mod itself (embedded) or from BepInEx/MarsMod/models/&lt;name&gt;.glb, which wins,
    ///    so a model can be replaced without a new build.
    /// </summary>
    internal static class Models
    {
        public sealed class Template
        {
            public string Name;
            public GameObject Root;                                   // inactive, never shown
            public readonly Dictionary<string, Transform> Markers = new Dictionary<string, Transform>();
            public readonly List<Transform> Animated = new List<Transform>();
            public Bounds Bounds;
            public int Triangles;
        }

        static readonly Dictionary<string, Template> cache = new Dictionary<string, Template>();
        static readonly Dictionary<string, Material> materialCache = new Dictionary<string, Material>();
        static GameObject library;

        static string Dir => Path.Combine(BepInEx.Paths.BepInExRootPath, "MarsMod", "models");

        public static IEnumerable<string> Available()
        {
            var names = new HashSet<string>();
            foreach (var r in typeof(Models).Assembly.GetManifestResourceNames())
                if (r.StartsWith("model.") && r.EndsWith(".glb")) names.Add(r.Substring(6, r.Length - 10));
            if (Directory.Exists(Dir)) foreach (var f in Directory.GetFiles(Dir, "*.glb")) names.Add(Path.GetFileNameWithoutExtension(f));
            return names.OrderBy(n => n);
        }

        public static Template Get(string name)
        {
            if (cache.TryGetValue(name, out var t) && t.Root != null) return t;
            byte[] data = null;
            string file = Path.Combine(Dir, name + ".glb");
            if (File.Exists(file)) data = File.ReadAllBytes(file);
            else using (var s = typeof(Models).Assembly.GetManifestResourceStream("model." + name + ".glb"))
                    if (s != null) { var ms = new MemoryStream(); s.CopyTo(ms); data = ms.ToArray(); }
            if (data == null) throw new FileNotFoundException("no model called " + name);
            var started = DateTime.UtcNow;
            t = Build(name, GltfModel.Load(data));
            cache[name] = t;
            Plugin.Log.LogInfo($"[models] {name}: {t.Triangles} triangles, {t.Markers.Count} markers, {t.Animated.Count} moving parts, built in {(DateTime.UtcNow - started).TotalMilliseconds:0} ms");
            return t;
        }

        /// <summary>A visible copy of a model.</summary>
        public static GameObject Spawn(string name, Vector3 position, Quaternion rotation)
        {
            var t = Get(name);
            var go = UnityEngine.Object.Instantiate(t.Root).Cast<GameObject>();
            go.name = t.Name;
            go.transform.SetPositionAndRotation(position, rotation);
            go.SetActive(true);
            spawned.Add(go);
            return go;
        }

        /// <summary>Animate an instance someone else made (the game, from one of our prefabs).</summary>
        public static void Track(GameObject go) { if (go != null && !spawned.Contains(go)) spawned.Add(go); }

        static readonly List<GameObject> spawned = new List<GameObject>();
        static readonly HashSet<IntPtr> stopped = new HashSet<IntPtr>();

        /// <summary>Moving parts of this instance turn only while it is running.</summary>
        public static void SetRunning(GameObject go, bool running)
        {
            if (running) stopped.Remove(go.Pointer); else stopped.Add(go.Pointer);
        }

        /// <summary>Every frame: turn the moving parts of spawned models (anim_spin_… spins around its up axis).</summary>
        public static void Animate(float dt)
        {
            for (int i = spawned.Count - 1; i >= 0; i--)
            {
                var go = spawned[i];
                if (go == null || go.WasCollected) { spawned.RemoveAt(i); continue; }
                if (stopped.Contains(go.Pointer)) continue;
                SpinChildren(go.transform, dt);
            }
        }

        static void SpinChildren(Transform t, float dt)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (c.name.StartsWith("anim_spin")) c.Rotate(0f, 90f * dt, 0f, Space.Self);
                SpinChildren(c, dt);
            }
        }

        // ------------------------------------------------------------------ building

        static Template Build(string name, GltfModel m)
        {
            if (library == null)
            {
                library = new GameObject(); library.name = "Mars model library";
                library.SetActive(false);                 // children stay inactive templates
                UnityEngine.Object.DontDestroyOnLoad(library);
            }
            var t = new Template { Name = name };
            t.Root = new GameObject(); t.Root.name = name;
            t.Root.transform.SetParent(library.transform, false);

            // 1. The node tree as transforms.
            var nodeGo = new Transform[m.Nodes.Count];
            void Make(int i, Transform parent)
            {
                var n = m.Nodes[i];
                var go = new GameObject(); go.name = n.Name;
                var tr = go.transform;
                tr.SetParent(parent, false);
                tr.localPosition = new Vector3(n.T[0], n.T[1], n.T[2]);
                tr.localRotation = new Quaternion(n.R[0], n.R[1], n.R[2], n.R[3]);
                tr.localScale = new Vector3(n.S[0], n.S[1], n.S[2]);
                nodeGo[i] = tr;
                foreach (var c in n.Children) Make(c, tr);
            }
            foreach (var r in m.Roots) Make(r, t.Root.transform);

            // 2. Groups: the root plus every "anim_" node. Each group gets one merged mesh.
            var groups = new List<Transform> { t.Root.transform };
            for (int i = 0; i < m.Nodes.Count; i++)
                if (m.Nodes[i].Name.StartsWith("anim_")) { groups.Add(nodeGo[i]); t.Animated.Add(nodeGo[i]); }
            Transform GroupOf(Transform tr)
            {
                for (var p = tr; p != null; p = p.parent)
                    foreach (var g in groups) if (g.Pointer == p.Pointer) return g;
                return t.Root.transform;
            }

            // 3. Collect geometry per group and material.
            var buckets = new Dictionary<IntPtr, Dictionary<int, Bucket>>();
            var colliders = new List<(Transform node, Vector3 min, Vector3 max)>();
            for (int i = 0; i < m.Nodes.Count; i++)
            {
                var n = m.Nodes[i];
                var tr = nodeGo[i];
                if (n.Mesh < 0)
                {
                    if (n.Name.StartsWith("port_") || n.Name.StartsWith("anchor_")) t.Markers[n.Name] = tr;
                    continue;
                }
                var mesh = m.Meshes[n.Mesh];
                if (n.Name.StartsWith("COL_"))
                {
                    Vector3 mn = Vector3.one * float.MaxValue, mx = Vector3.one * float.MinValue;
                    foreach (var p in mesh.Primitives)
                        for (int k = 0; k < p.Positions.Length; k += 3)
                        {
                            var v = new Vector3(p.Positions[k], p.Positions[k + 1], p.Positions[k + 2]);
                            mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v);
                        }
                    colliders.Add((tr, mn, mx));
                    continue;
                }
                var group = GroupOf(tr);
                var toGroup = group.worldToLocalMatrix * tr.localToWorldMatrix;
                var normalM = toGroup.inverse.transpose;
                if (!buckets.TryGetValue(group.Pointer, out var byMat)) buckets[group.Pointer] = byMat = new Dictionary<int, Bucket>();
                foreach (var p in mesh.Primitives)
                {
                    if (!byMat.TryGetValue(p.Material, out var b)) byMat[p.Material] = b = new Bucket();
                    int baseIndex = b.Verts.Count;
                    for (int k = 0; k < p.Positions.Length; k += 3)
                    {
                        b.Verts.Add(toGroup.MultiplyPoint3x4(new Vector3(p.Positions[k], p.Positions[k + 1], p.Positions[k + 2])));
                        b.Normals.Add(p.Normals != null ? normalM.MultiplyVector(new Vector3(p.Normals[k], p.Normals[k + 1], p.Normals[k + 2])).normalized : Vector3.up);
                    }
                    foreach (var ix in p.Indices) b.Tris.Add(baseIndex + ix);
                }
            }

            // 4. One mesh per group, a submesh per material.
            foreach (var g in groups)
            {
                if (!buckets.TryGetValue(g.Pointer, out var byMat) || byMat.Count == 0) continue;
                var keys = byMat.Keys.ToList();
                var verts = new List<Vector3>(); var normals = new List<Vector3>();
                var subTris = new List<int[]>();
                foreach (var k in keys)
                {
                    var b = byMat[k]; int off = verts.Count;
                    verts.AddRange(b.Verts); normals.AddRange(b.Normals);
                    subTris.Add(b.Tris.Select(x => x + off).ToArray());
                }
                var mesh = new Mesh { name = name + " " + g.name };
                if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.vertices = new Il2CppStructArray<Vector3>(verts.ToArray());
                mesh.normals = new Il2CppStructArray<Vector3>(normals.ToArray());
                mesh.subMeshCount = keys.Count;
                for (int s = 0; s < keys.Count; s++) mesh.SetTriangles(new Il2CppStructArray<int>(subTris[s]), s);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(false);
                t.Triangles += subTris.Sum(x => x.Length) / 3;

                var mats = new Il2CppReferenceArray<Material>(keys.Count);
                for (int s = 0; s < keys.Count; s++) mats[s] = MaterialFor(keys[s] >= 0 && keys[s] < m.Materials.Count ? m.Materials[keys[s]] : null);
                g.gameObject.AddComponent(Il2CppType.Of<MeshFilter>()).Cast<MeshFilter>().sharedMesh = mesh;
                var mr = g.gameObject.AddComponent(Il2CppType.Of<MeshRenderer>()).Cast<MeshRenderer>();
                mr.sharedMaterials = mats;
            }

            // 5. Colliders (boxes in the node's space), via the physics module.
            var boxT = R.T("UnityEngine.BoxCollider", "UnityEngine.PhysicsModule");
            if (boxT != null)
                foreach (var (node, mn, mx) in colliders)
                {
                    var bc = R.Add(node.gameObject, boxT);
                    R.Set(bc, "center", (mn + mx) * 0.5f);
                    R.Set(bc, "size", mx - mn);
                }

            // 6. Drop the now-empty helper objects (keep markers, animated groups and colliders).
            var keep = new HashSet<IntPtr>(t.Markers.Values.Select(x => x.Pointer).Concat(groups.Select(x => x.Pointer)).Concat(colliders.Select(c => c.node.Pointer)));
            void Prune(Transform tr)
            {
                for (int i = tr.childCount - 1; i >= 0; i--) Prune(tr.GetChild(i));
                if (tr.Pointer == t.Root.transform.Pointer) return;
                bool needed = keep.Contains(tr.Pointer) || tr.childCount > 0;
                if (!needed) UnityEngine.Object.DestroyImmediate(tr.gameObject);
            }
            Prune(t.Root.transform);

            // Bounds of the whole model (local space).
            var all = t.Root.GetComponentsInChildren(Il2CppType.Of<MeshFilter>(), true);
            bool any = false; Bounds bb = default;
            foreach (var c in all)
            {
                var mf = c.TryCast<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                if (!any) { bb = mb; any = true; } else bb.Encapsulate(mb);
            }
            t.Bounds = bb;
            return t;
        }

        sealed class Bucket { public readonly List<Vector3> Verts = new List<Vector3>(), Normals = new List<Vector3>(); public readonly List<int> Tris = new List<int>(); }

        // ------------------------------------------------------------------ materials

        static Material MaterialFor(GltfModel.MaterialData md)
        {
            string name = md?.Name ?? "collectordarkgrey";
            if (materialCache.TryGetValue(name, out var cached) && cached != null) return cached;
            Material result = null;
            if (!name.StartsWith("mars:")) result = GameMaterial(name);
            if (result == null)
            {
                // Mars-only (or missing) material: a copy of a game material with the model's colours.
                var template = GameMaterial("collectorbrightwhite") ?? GameMaterial("collectordarkgrey");
                result = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                result.name = name;
                if (md != null)
                {
                    var c = new Color(md.BaseColor[0], md.BaseColor[1], md.BaseColor[2], md.BaseColor.Length > 3 ? md.BaseColor[3] : 1f);
                    result.SetColor("_BaseColor", c);
                    if (result.HasProperty("_Color")) result.SetColor("_Color", c);
                    result.SetFloat("_Metallic", md.Metallic);
                    result.SetFloat("_Smoothness", 1f - md.Roughness);
                    if (md.Emissive[0] + md.Emissive[1] + md.Emissive[2] > 0.001f)
                    {
                        result.EnableKeyword("_EMISSION");
                        result.SetColor("_EmissionColor", new Color(md.Emissive[0], md.Emissive[1], md.Emissive[2]) * md.EmissiveStrength);
                    }
                }
                UnityEngine.Object.DontDestroyOnLoad(result);
                result.hideFlags = HideFlags.DontUnloadUnusedAsset;
                if (!name.StartsWith("mars:")) Log.Once("mat-miss-" + name, $"[models] game material '{name}' not found, using a look-alike");
            }
            materialCache[name] = result;
            return result;
        }

        static Material GameMaterial(string name)
        {
            foreach (var m in Look.All<Material>())
                if (m != null && m.name == name) return m;
            return null;
        }

        /// <summary>Forget game materials (they change when a world unloads).</summary>
        public static void WorldChanged()
        {
            foreach (var t in cache.Values) if (t.Root != null) UnityEngine.Object.Destroy(t.Root);
            cache.Clear();
            materialCache.Clear();
        }
    }
}
