using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MarsMod
{
    /// <summary>
    /// Minimal glTF 2.0 binary (.glb) reader for the models made in Blender for the Mars mod. No Unity types
    /// in here (so it can be tested outside the game): it returns plain arrays. Converts glTF's right-handed
    /// coordinates to Unity's left-handed ones (X is mirrored, triangle winding reversed).
    /// Supported: node tree with TRS, meshes with POSITION/NORMAL/TEXCOORD_0, indices (u8/u16/u32), materials
    /// (name + PBR factors + emission). That is everything the Blender exporter writes for our models.
    /// </summary>
    internal sealed class GltfModel
    {
        public sealed class Node
        {
            public string Name;
            public float[] T = { 0, 0, 0 }, R = { 0, 0, 0, 1 }, S = { 1, 1, 1 };
            public int Mesh = -1;
            public List<int> Children = new List<int>();
        }

        public sealed class Primitive
        {
            public float[] Positions, Normals, UVs;   // xyz, xyz, uv
            public int[] Indices;
            public int Material = -1;
        }

        public sealed class MeshData { public string Name; public List<Primitive> Primitives = new List<Primitive>(); }

        public sealed class MaterialData
        {
            public string Name;
            public float[] BaseColor = { 1, 1, 1, 1 };
            public float Metallic = 0f, Roughness = 0.5f;
            public float[] Emissive = { 0, 0, 0 };
            public float EmissiveStrength = 1f;
        }

        public readonly List<Node> Nodes = new List<Node>();
        public readonly List<MeshData> Meshes = new List<MeshData>();
        public readonly List<MaterialData> Materials = new List<MaterialData>();
        public readonly List<int> Roots = new List<int>();

        public static GltfModel Load(byte[] glb)
        {
            if (glb.Length < 20 || BitConverter.ToUInt32(glb, 0) != 0x46546C67) throw new InvalidDataException("not a .glb file");
            int offset = 12;
            string json = null; byte[] bin = null;
            while (offset + 8 <= glb.Length)
            {
                int len = BitConverter.ToInt32(glb, offset);
                uint type = BitConverter.ToUInt32(glb, offset + 4);
                if (type == 0x4E4F534A) json = Encoding.UTF8.GetString(glb, offset + 8, len);
                else if (type == 0x004E4942) { bin = new byte[len]; Buffer.BlockCopy(glb, offset + 8, bin, 0, len); }
                offset += 8 + len;
            }
            if (json == null) throw new InvalidDataException("glb without JSON");
            var m = new GltfModel();
            using (var doc = JsonDocument.Parse(json)) m.Read(doc.RootElement, bin ?? new byte[0]);
            return m;
        }

        void Read(JsonElement root, byte[] bin)
        {
            var accessors = Arr(root, "accessors");
            var views = Arr(root, "bufferViews");

            foreach (var mj in Arr(root, "materials"))
            {
                var md = new MaterialData { Name = Str(mj, "name") ?? "material" };
                if (mj.TryGetProperty("pbrMetallicRoughness", out var pbr))
                {
                    if (pbr.TryGetProperty("baseColorFactor", out var bc)) md.BaseColor = Floats(bc);
                    if (pbr.TryGetProperty("metallicFactor", out var mf)) md.Metallic = mf.GetSingle();
                    if (pbr.TryGetProperty("roughnessFactor", out var rf)) md.Roughness = rf.GetSingle();
                }
                if (mj.TryGetProperty("emissiveFactor", out var ef)) md.Emissive = Floats(ef);
                if (mj.TryGetProperty("extensions", out var ext) && ext.TryGetProperty("KHR_materials_emissive_strength", out var es)
                    && es.TryGetProperty("emissiveStrength", out var esv)) md.EmissiveStrength = esv.GetSingle();
                Materials.Add(md);
            }

            foreach (var mj in Arr(root, "meshes"))
            {
                var mesh = new MeshData { Name = Str(mj, "name") };
                foreach (var pj in Arr(mj, "primitives"))
                {
                    if (pj.TryGetProperty("mode", out var mode) && mode.GetInt32() != 4) continue; // triangles only
                    var attr = pj.GetProperty("attributes");
                    var p = new Primitive
                    {
                        Positions = attr.TryGetProperty("POSITION", out var pos) ? FloatAccessor(accessors, views, bin, pos.GetInt32()) : new float[0],
                        Normals = attr.TryGetProperty("NORMAL", out var nor) ? FloatAccessor(accessors, views, bin, nor.GetInt32()) : null,
                        UVs = attr.TryGetProperty("TEXCOORD_0", out var uv) ? FloatAccessor(accessors, views, bin, uv.GetInt32()) : null,
                        Material = pj.TryGetProperty("material", out var mat) ? mat.GetInt32() : -1,
                    };
                    int count = p.Positions.Length / 3;
                    p.Indices = pj.TryGetProperty("indices", out var ix) ? IntAccessor(accessors, views, bin, ix.GetInt32()) : Sequence(count);
                    ToUnity(p);
                    mesh.Primitives.Add(p);
                }
                Meshes.Add(mesh);
            }

            foreach (var nj in Arr(root, "nodes"))
            {
                var n = new Node { Name = Str(nj, "name") ?? "node" };
                if (nj.TryGetProperty("translation", out var t)) n.T = Floats(t);
                if (nj.TryGetProperty("rotation", out var r)) n.R = Floats(r);
                if (nj.TryGetProperty("scale", out var s)) n.S = Floats(s);
                if (nj.TryGetProperty("matrix", out _)) throw new InvalidDataException($"node {n.Name}: matrix transforms are not supported, export with TRS");
                if (nj.TryGetProperty("mesh", out var me)) n.Mesh = me.GetInt32();
                foreach (var c in Arr(nj, "children")) n.Children.Add(c.GetInt32());
                // glTF → Unity: mirror X.
                n.T[0] = -n.T[0];
                n.R = new[] { n.R[0], -n.R[1], -n.R[2], n.R[3] };
                Nodes.Add(n);
            }

            int scene = root.TryGetProperty("scene", out var sc) ? sc.GetInt32() : 0;
            var scenes = Arr(root, "scenes");
            if (scenes.Count > scene) foreach (var r in Arr(scenes[scene], "nodes")) Roots.Add(r.GetInt32());
            else for (int i = 0; i < Nodes.Count; i++) Roots.Add(i);
        }

        static void ToUnity(Primitive p)
        {
            for (int i = 0; i < p.Positions.Length; i += 3) p.Positions[i] = -p.Positions[i];
            if (p.Normals != null) for (int i = 0; i < p.Normals.Length; i += 3) p.Normals[i] = -p.Normals[i];
            if (p.UVs != null) for (int i = 1; i < p.UVs.Length; i += 2) p.UVs[i] = 1f - p.UVs[i];
            for (int i = 0; i + 2 < p.Indices.Length; i += 3) { int t = p.Indices[i + 1]; p.Indices[i + 1] = p.Indices[i + 2]; p.Indices[i + 2] = t; }
        }

        // ------------------------------------------------------------------ accessors

        static float[] FloatAccessor(List<JsonElement> accessors, List<JsonElement> views, byte[] bin, int index)
        {
            var a = accessors[index];
            int count = a.GetProperty("count").GetInt32();
            int comps = Components(Str(a, "type"));
            int ctype = a.GetProperty("componentType").GetInt32();
            if (ctype != 5126) throw new InvalidDataException("only float vertex data is supported");
            var (start, stride) = View(a, views, comps * 4);
            var result = new float[count * comps];
            for (int i = 0; i < count; i++)
                for (int c = 0; c < comps; c++)
                    result[i * comps + c] = BitConverter.ToSingle(bin, start + i * stride + c * 4);
            return result;
        }

        static int[] IntAccessor(List<JsonElement> accessors, List<JsonElement> views, byte[] bin, int index)
        {
            var a = accessors[index];
            int count = a.GetProperty("count").GetInt32();
            int ctype = a.GetProperty("componentType").GetInt32();
            int size = ctype == 5121 ? 1 : ctype == 5123 ? 2 : 4;
            var (start, stride) = View(a, views, size);
            var result = new int[count];
            for (int i = 0; i < count; i++)
            {
                int o = start + i * stride;
                result[i] = size == 1 ? bin[o] : size == 2 ? BitConverter.ToUInt16(bin, o) : (int)BitConverter.ToUInt32(bin, o);
            }
            return result;
        }

        static (int start, int stride) View(JsonElement accessor, List<JsonElement> views, int elementSize)
        {
            var v = views[accessor.GetProperty("bufferView").GetInt32()];
            int start = (v.TryGetProperty("byteOffset", out var vo) ? vo.GetInt32() : 0) + (accessor.TryGetProperty("byteOffset", out var ao) ? ao.GetInt32() : 0);
            int stride = v.TryGetProperty("byteStride", out var bs) ? bs.GetInt32() : elementSize;
            return (start, stride);
        }

        static int Components(string type) => type switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, _ => throw new InvalidDataException("accessor type " + type) };
        static int[] Sequence(int n) { var r = new int[n]; for (int i = 0; i < n; i++) r[i] = i; return r; }
        static List<JsonElement> Arr(JsonElement e, string name) { var l = new List<JsonElement>(); if (e.TryGetProperty(name, out var a)) foreach (var x in a.EnumerateArray()) l.Add(x); return l; }
        static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetString() : null;
        static float[] Floats(JsonElement a) { var l = new List<float>(); foreach (var x in a.EnumerateArray()) l.Add(x.GetSingle()); return l.ToArray(); }
    }
}
