using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// Shield generators. When a storm is near and the grid gives a shield its full power, it raises a dome. What
    /// the dome covers is found by a flood fill: from the generator outwards in 2 m steps over the ground, never
    /// further than 28 m, never over a wall or a cliff it would have to pass through. Inside the covered cells the
    /// storm is gone: clear air, no blowing dust, nothing jams, solar panels get their full sun. The cells are
    /// worked out on every PC from the terrain, so only "up or down" travels over the network.
    /// </summary>
    internal static class Shields
    {
        public const float Radius = 28f, Cell = 2f;

        sealed class Coverage
        {
            public Vector3 Center, Up, East, North;
            public HashSet<long> Cells = new HashSet<long>();
            public GameObject Dome;
            public float Shown;
        }

        static readonly Dictionary<int, Coverage> cover = new Dictionary<int, Coverage>();
        static Material domeMat;
        static int previewId = -1;
        static float previewUntil;

        /// <summary>Looking at a shield shows its dome faintly for a moment, so the crew sees what it would cover.</summary>
        public static void Preview(Buildings.Rec b) { previewId = b.Id; previewUntil = Time.realtimeSinceStartup + 0.5f; }

        /// <summary>A storm is on its way or here, so shields should be up.</summary>
        public static bool Wanted => Haze.Storm > 0.02f || Haze.Front > 0.02f;

        public static bool IsUp(Buildings.Rec b) => b.Active;

        static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

        /// <summary>Inside the dome of a shield that is up.</summary>
        public static bool Covered(Vector3 p)
        {
            foreach (var b in Buildings.Data.List)
            {
                if (b.Type != "shield" || !b.Active) continue;
                if ((p - b.Pos).sqrMagnitude > Radius * Radius) continue;
                var c = Get(b);
                if (c == null) continue;
                var d = p - c.Center;
                int x = Mathf.RoundToInt(Vector3.Dot(d, c.East) / Cell), y = Mathf.RoundToInt(Vector3.Dot(d, c.North) / Cell);
                if (c.Cells.Contains(Key(x, y))) return true;
            }
            return false;
        }

        static Coverage Get(Buildings.Rec b)
        {
            if (cover.TryGetValue(b.Id, out var c) && (c.Center - b.Pos).sqrMagnitude < 0.25f) return c;
            try { c = Fill(b.Pos); } catch (Exception e) { Log.Once("shield-fill", "[shield] coverage failed: " + e.Message); return null; }
            if (cover.TryGetValue(b.Id, out var old) && old.Dome != null) UnityEngine.Object.Destroy(old.Dome);
            cover[b.Id] = c;
            Plugin.Log.LogMessage($"[shield] #{b.Id} covers {c.Cells.Count} cells ({c.Cells.Count * Cell * Cell:0} m²)");
            return c;
        }

        static Coverage Fill(Vector3 center)
        {
            var c = new Coverage { Center = center, Up = center.normalized };
            c.East = Vector3.Cross(c.Up, Vector3.forward); if (c.East.sqrMagnitude < 1e-4f) c.East = Vector3.right; c.East.Normalize();
            c.North = Vector3.Cross(c.East, c.Up);
            var terrain = Commands.FindWorld()?.Terrain;
            float baseR = center.magnitude;
            Vector3 Ground(int x, int y)
            {
                var flat = center + c.East * (x * Cell) + c.North * (y * Cell);
                return terrain != null ? terrain.SurfacePoint(flat.normalized) : flat;
            }
            var q = new Queue<(int x, int y, Vector3 g)>();
            q.Enqueue((0, 0, Ground(0, 0)));
            c.Cells.Add(Key(0, 0));
            int max = (int)(Radius / Cell);
            var steps = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            while (q.Count > 0)
            {
                var (x, y, g) = q.Dequeue();
                foreach (var (dx, dy) in steps)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx * nx + ny * ny > max * max || c.Cells.Contains(Key(nx, ny))) continue;
                    var ng = Ground(nx, ny);
                    float rise = ng.magnitude - g.magnitude, height = ng.magnitude - baseR;
                    if (Mathf.Abs(rise) > 1.6f || height > 6f || height < -8f) continue;           // a cliff or a pit: the dome stops there
                    var up = g.normalized;
                    var dir = (ng - g); float len = dir.magnitude;
                    if (len > 0.01f && Shelter.Ray(g + up * 1.2f, dir / len, len, out _)) continue;   // a wall between the cells
                    c.Cells.Add(Key(nx, ny));
                    q.Enqueue((nx, ny, ng));
                }
            }
            return c;
        }

        /// <summary>Every frame on every PC: show or hide the domes.</summary>
        public static void Frame(float dt)
        {
            foreach (var b in Buildings.Data.List)
            {
                if (b.Type != "shield") continue;
                var c = Get(b);
                if (c == null) continue;
                float target = b.Active ? 1f : b.Id == previewId && Time.realtimeSinceStartup < previewUntil ? 0.45f : 0f;
                c.Shown = Mathf.MoveTowards(c.Shown, target, dt * 1.2f);
                if (c.Shown <= 0.001f) { if (c.Dome != null && c.Dome.activeSelf) c.Dome.SetActive(false); continue; }
                if (c.Dome == null) c.Dome = MakeDome(c);
                if (c.Dome == null) continue;
                if (!c.Dome.activeSelf) c.Dome.SetActive(true);
                c.Dome.transform.localScale = Vector3.one * (Radius * Mathf.SmoothStep(0.2f, 1f, Mathf.Clamp01(c.Shown / 0.45f)));
                var col = new Color(0.45f, 0.85f, 1f, 0.10f * c.Shown);
                domeMat.color = col;
            }
            foreach (var id in cover.Keys.ToList())
                if (!Buildings.Data.List.Any(b => b.Id == id)) { if (cover[id].Dome != null) UnityEngine.Object.Destroy(cover[id].Dome); cover.Remove(id); }
        }

        static Mesh hemi;

        /// <summary>A unit half sphere (open at the bottom), drawn from both sides.</summary>
        static Mesh Hemisphere()
        {
            if (hemi != null) return hemi;
            const int rings = 12, segs = 40;
            var v = new List<Vector3>(); var t = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float a = r / (float)rings * Mathf.PI * 0.5f;
                for (int s2 = 0; s2 <= segs; s2++)
                {
                    float b = s2 / (float)segs * Mathf.PI * 2f;
                    v.Add(new Vector3(Mathf.Cos(a) * Mathf.Cos(b), Mathf.Sin(a), Mathf.Cos(a) * Mathf.Sin(b)));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s2 = 0; s2 < segs; s2++)
                {
                    int i = r * (segs + 1) + s2, j = i + segs + 1;
                    t.Add(i); t.Add(j); t.Add(i + 1);
                    t.Add(i + 1); t.Add(j); t.Add(j + 1);
                }
            hemi = new Mesh { name = "Mars shield dome" };
            hemi.vertices = v.ToArray();
            hemi.triangles = t.ToArray();
            hemi.RecalculateBounds();
            UnityEngine.Object.DontDestroyOnLoad(hemi);
            return hemi;
        }

        static GameObject MakeDome(Coverage c)
        {
            try
            {
                if (domeMat == null)
                {
                    var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
                    domeMat = new Material(sh) { name = "Mars shield dome" };
                    Haze.SetupUnlitTransparent(domeMat);
                    domeMat.renderQueue = 3050;
                    UnityEngine.Object.DontDestroyOnLoad(domeMat);
                }
                var go = new GameObject(); go.name = "Mars shield dome";
                var mf = go.AddComponent(Il2CppType.Of<MeshFilter>()).Cast<MeshFilter>();
                mf.sharedMesh = Hemisphere();
                var mr = go.AddComponent(Il2CppType.Of<MeshRenderer>()).Cast<MeshRenderer>();
                mr.sharedMaterial = domeMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                go.transform.position = c.Center - c.Up * 2f;
                go.transform.rotation = Quaternion.FromToRotation(Vector3.up, c.Up);
                return go;
            }
            catch (Exception e) { Log.Once("shield-dome", "[shield] dome failed: " + e.Message); return null; }
        }

        public static void Clear()
        {
            foreach (var c in cover.Values) if (c.Dome != null) UnityEngine.Object.Destroy(c.Dome);
            cover.Clear();
        }
    }
}
