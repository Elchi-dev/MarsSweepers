using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// Moving air on Mars, drawn as a small CPU particle system (two dynamic meshes, no game hooks):
    ///
    ///  * Streaks: thin dust streaks racing along the wind, a third of them skimming the ground
    ///    (saltation, like real blowing sand). A few drift by even in calm weather.
    ///  * Puffs: big soft billows of dust rolling past in a storm, lit brighter on top, sorted back to front.
    ///  * Storm front: before a storm hits, a towering wall of billows rises on the horizon in the direction
    ///    the storm comes from and rolls towards you (the Mad Max "great beige wall").
    ///  * Sound: a looping, procedurally generated wind (low rumble + sandy hiss) that swells with gusts,
    ///    and a two-tone alarm when a storm is announced.
    /// </summary>
    internal static class Wind
    {
        const int StreakMax = 700, PuffMax = 140, FrontMax = 80, DevilParts = 26, DevilMax = DevilParts * 2;
        const float StreakRange = 24f, PuffRange = 70f;

        static GameObject root;
        static Mesh streakMesh, puffMesh;
        static Il2CppStructArray<Vector3> sVerts, pVerts;
        static Il2CppStructArray<Color32> sCols, pCols;

        struct P { public Vector3 pos, vel; public float life, maxLife, size, spin, angle, shade, seed; public bool ground, blocked; }
        static readonly P[] streaks = new P[StreakMax];
        static readonly P[] puffs = new P[PuffMax + FrontMax + DevilMax];
        static readonly float[] sortKeys = new float[PuffMax + FrontMax + DevilMax];
        static readonly int[] sortIdx = new int[PuffMax + FrontMax + DevilMax];
        static readonly System.Random rnd = new System.Random(4217);
        static float gust = 1f, windSpeed, t;
        static int rr, rrp;
        static bool built, failed;

        static object rumbleSrc, hissSrc, alarmSrc, alarmClip;

        static float Rand(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

        // ------------------------------------------------------------------ per frame

        public static void Tick(float dt, Camera cam)
        {
            if (failed || !MarsProfile.WindParticles.Value) { Hide(); Sound(0f); return; }
            if (!built && !Build()) return;
            float presence = Haze.Presence;
            bool show = presence > 0.01f;
            if (root.activeSelf != show) root.SetActive(show);
            if (!show) { Sound(0f); return; }

            t += dt;
            var camPos = cam.transform.position;
            root.transform.position = camPos;
            root.transform.rotation = Quaternion.identity;
            var up = Haze.Up;
            float storm = Haze.Storm, front = Haze.Front;

            // Gusts: slow noise on top of the storm's base speed (Ghost of Tsushima style rolling gusts).
            gust = 0.7f + 0.6f * Mathf.PerlinNoise(t * 0.23f, 3.7f) + 0.25f * Mathf.PerlinNoise(t * 1.1f, 9.1f);
            windSpeed = (1.2f + 22f * storm + 6f * front * (1f - storm)) * gust;
            var wind = Haze.WindDir * windSpeed;

            float light = Mathf.Lerp(0.16f, 1f, Haze.Daylight);
            var fog = Haze.FogColor;
            var dust = Color.Lerp(new Color(.80f, .60f, .44f), new Color(.70f, .50f, .35f), storm) * light;
            float L = Haze.Visibility;

            // Dust blown under a roof (into the base, under a shield) disappears: a few particles per frame.
            if (storm + front > 0.02f)
            {
                for (int k = 0; k < 24; k++) { rr = (rr + 1) % StreakMax; ref var q = ref streaks[rr]; if (q.maxLife > 0f && !q.blocked && Shelter.Roofed(q.pos, up)) q.blocked = true; }
                for (int k = 0; k < 4; k++) { rrp = (rrp + 1) % PuffMax; ref var q = ref puffs[rrp]; if (q.maxLife > 0f && !q.blocked && Shelter.Roofed(q.pos, up)) q.blocked = true; }
            }
            UpdateStreaks(dt, camPos, up, wind, storm, dust, presence, L);
            UpdatePuffs(dt, cam, camPos, up, wind, storm, front, dust, presence, L);
            Sound(presence);
        }

        static void UpdateStreaks(float dt, Vector3 camPos, Vector3 up, Vector3 wind, float storm, Color dust, float presence, float L)
        {
            float intensity = Mathf.Clamp01(0.05f + storm * 0.95f);
            int active = Mathf.RoundToInt(StreakMax * intensity);
            var ground = camPos - up * 1.7f;
            for (int i = 0; i < StreakMax; i++)
            {
                ref var p = ref streaks[i];
                int v = i * 4;
                if (i >= active) { Collapse(sVerts, sCols, v); p.life = 0f; continue; }
                p.life += dt;
                var rel = p.pos - camPos;
                var flat = rel - up * Vector3.Dot(rel, up);
                if (p.life >= p.maxLife || flat.sqrMagnitude > StreakRange * StreakRange || p.maxLife <= 0f)
                    SpawnStreak(ref p, camPos, ground, up, wind);

                var turb = new Vector3(Mathf.Sin(t * 3.1f + p.seed), Mathf.Sin(t * 2.3f + p.seed * 1.7f), Mathf.Cos(t * 2.7f + p.seed)) * (0.6f + windSpeed * 0.08f);
                p.vel = Vector3.Lerp(p.vel, wind * (p.ground ? 1.25f : 1f) + turb, dt * 3f);
                p.pos += p.vel * dt;

                var c = p.pos - camPos;
                float d = c.magnitude;
                float speed = p.vel.magnitude;
                var axis = (speed > 0.01f ? p.vel / speed : Haze.WindDir) * Mathf.Clamp(speed * 0.07f, 0.12f, 2.4f) * 0.5f;
                var toCam = d > 0.001f ? -c / d : Vector3.forward;
                var side = Vector3.Cross(axis, toCam);
                side = (side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.up) * p.size * 0.5f;
                sVerts[v] = c - axis - side; sVerts[v + 1] = c - axis + side;
                sVerts[v + 2] = c + axis + side; sVerts[v + 3] = c + axis - side;

                float lifeFade = Mathf.Clamp01(p.life / 0.4f) * Mathf.Clamp01((p.maxLife - p.life) / 0.6f);
                float near = Mathf.Clamp01((d - 0.6f) / 1.5f), far = Mathf.Clamp01(1f - d / StreakRange);
                float fogFade = Mathf.Exp(-d / Mathf.Max(4f, L * 1.5f));
                float a = p.blocked ? 0f : (0.25f + 0.55f * storm) * lifeFade * near * far * fogFade * presence * p.shade;
                var col = S.C32(B(dust.r * p.shade), B(dust.g * p.shade), B(dust.b * p.shade), B(a));
                sCols[v] = col; sCols[v + 1] = col; sCols[v + 2] = col; sCols[v + 3] = col;
            }
            streakMesh.vertices = sVerts;
            streakMesh.colors32 = sCols;
        }

        static void SpawnStreak(ref P p, Vector3 camPos, Vector3 ground, Vector3 up, Vector3 wind)
        {
            var east = Vector3.Cross(up, Vector3.forward); if (east.sqrMagnitude < 1e-4f) east = Vector3.right; east.Normalize();
            var north = Vector3.Cross(east, up);
            // Respawn mostly on the upwind side so the stream looks continuous.
            float ang = Rand(0f, Mathf.PI * 2f);
            float r = StreakRange * Mathf.Sqrt(Rand(0.02f, 1f));
            var offset = east * Mathf.Cos(ang) * r + north * Mathf.Sin(ang) * r;
            if (p.maxLife > 0f && wind.sqrMagnitude > 0.5f && Vector3.Dot(offset, wind) > 0f) offset -= 2f * Vector3.Project(offset, wind.normalized);
            p.ground = rnd.NextDouble() < 0.35;
            float h = p.ground ? Rand(0.02f, 0.5f) : Rand(0.2f, 9f);
            p.pos = ground + offset + up * h;
            p.blocked = Haze.Storm + Haze.Front > 0.02f && (Shelter.Roofed(p.pos, up) || Shields.Covered(p.pos));   // no dust under roofs or a shield dome
            p.vel = wind;
            p.life = 0f;
            p.maxLife = Rand(1.2f, 3.5f);
            p.size = p.ground ? Rand(0.02f, 0.05f) : Rand(0.025f, 0.07f);
            p.shade = Rand(0.75f, 1.15f);
            p.seed = Rand(0f, 100f);
        }

        static void UpdatePuffs(float dt, Camera cam, Vector3 camPos, Vector3 up, Vector3 wind, float storm, float front, Color dust, float presence, float L)
        {
            var right = cam.transform.right; var camUp = cam.transform.up;
            var ground = camPos - up * 1.7f;
            int stormActive = Mathf.RoundToInt(PuffMax * Mathf.Clamp01(storm * 1.1f));
            // The front shows while it approaches and fades once the storm has swallowed you.
            float frontVis = Mathf.SmoothStep(0f, 1f, front / 0.12f) * (1f - Mathf.SmoothStep(0.2f, 0.75f, storm));
            int frontActive = frontVis > 0.01f ? FrontMax : 0;

            int n = 0;
            for (int i = 0; i < PuffMax + FrontMax + DevilMax; i++)
            {
                ref var p = ref puffs[i];
                if (i >= PuffMax + FrontMax)
                {
                    // Dust devils: a spiral of billows, narrow at the ground, wide and thin at the top.
                    int j = i - PuffMax - FrontMax, k = j / DevilParts, part = j % DevilParts;
                    if (k >= DustDevils.Active.Count) { p.maxLife = 0f; sortKeys[i] = -1f; continue; }
                    var dv = DustDevils.Active[k];
                    float frac = part / (float)(DevilParts - 1);
                    var de = Vector3.Cross(dv.Up, Vector3.forward); if (de.sqrMagnitude < 1e-4f) de = Vector3.right; de.Normalize();
                    var dn = Vector3.Cross(dv.Up, de);
                    float ang = t * (3.2f - frac * 1.6f) + part * 2.39996f;
                    float radius = 0.7f + frac * frac * 7f;
                    p.pos = dv.Center + (de * Mathf.Cos(ang) + dn * Mathf.Sin(ang)) * radius + dv.Up * (0.6f + frac * 28f * Mathf.Lerp(0.6f, 1f, dv.Strength));
                    p.size = 1.4f + frac * 4.5f;
                    p.angle = ang * 0.5f; p.shade = 0.85f + 0.2f * frac; p.seed = part; p.blocked = false;
                    p.life = dv.Age; p.maxLife = DustDevils.Life; p.vel = new Vector3(dv.Strength, frac, 0f);
                    sortKeys[i] = (p.pos - camPos).sqrMagnitude;
                    n++;
                    continue;
                }
                bool isFront = i >= PuffMax;
                bool on = isFront ? (i - PuffMax) < frontActive : i < stormActive;
                if (!on) { p.maxLife = 0f; sortKeys[i] = -1f; continue; }

                if (isFront)
                {
                    // Billows stacked into a wall across the horizon, rolling towards the camera.
                    if (p.maxLife <= 0f) SpawnFront(ref p);
                    float dist = Mathf.Lerp(700f, 25f, Mathf.Pow(front, 1.4f)) + p.seed;
                    var side = Vector3.Cross(up, Haze.FrontDir).normalized;
                    var basePos = ground + Haze.FrontDir * dist + side * p.vel.x + up * (p.vel.y + p.size * 0.35f);
                    p.pos = basePos + Haze.WindDir * Mathf.Sin(t * 0.3f + p.seed) * 6f;
                    p.angle += p.spin * dt;
                    p.life += dt;
                }
                else
                {
                    p.life += dt;
                    var rel = p.pos - camPos;
                    var flat = rel - up * Vector3.Dot(rel, up);
                    if (p.life >= p.maxLife || flat.sqrMagnitude > PuffRange * PuffRange || p.maxLife <= 0f)
                        SpawnPuff(ref p, ground, up, wind);
                    p.pos += (wind * 0.9f + up * 0.4f * Mathf.Sin(t * 0.5f + p.seed)) * dt;
                    p.angle += p.spin * dt;
                }
                sortKeys[i] = (p.pos - camPos).sqrMagnitude;
                n++;
            }

            // Back to front, so overlapping billows blend correctly.
            for (int i = 0; i < sortIdx.Length; i++) sortIdx[i] = i;
            Array.Sort((float[])sortKeys.Clone(), sortIdx);
            Array.Reverse(sortIdx);

            for (int slot = 0; slot < sortIdx.Length; slot++)
            {
                int i = sortIdx[slot];
                int v = slot * 4;
                ref var p = ref puffs[i];
                if (sortKeys[i] < 0f) { Collapse(pVerts, pCols, v); continue; }
                bool isFront = i >= PuffMax && i < PuffMax + FrontMax, isDevil = i >= PuffMax + FrontMax;
                var c = p.pos - camPos;
                float d = c.magnitude;
                float size = p.size * (1f + 0.08f * Mathf.Sin(t * 0.7f + p.seed));
                float ca = Mathf.Cos(p.angle) * size, sa = Mathf.Sin(p.angle) * size;
                var ax = right * ca + camUp * sa;
                var ay = -right * sa + camUp * ca;
                pVerts[v] = c - ax - ay; pVerts[v + 1] = c - ax + ay; pVerts[v + 2] = c + ax + ay; pVerts[v + 3] = c + ax - ay;

                float a;
                if (isDevil)
                {
                    float near = Mathf.Clamp01((d - size * 0.4f) / (size * 0.8f));
                    a = 0.5f * p.vel.x * (1f - p.vel.y * 0.55f) * near * Mathf.Exp(-d / Mathf.Max(250f, L * 2f));
                }
                else if (isFront)
                {
                    float fadeIn = Mathf.Clamp01(p.life / 3f);
                    a = 0.85f * frontVis * fadeIn * Mathf.Exp(-d / Mathf.Max(400f, L * 2f));
                    if (d < Shelter.RoomRadius + size) a *= 1f - Shelter.Indoor;
                }
                else
                {
                    float lifeFade = Mathf.Clamp01(p.life / 2.5f) * Mathf.Clamp01((p.maxLife - p.life) / 2.5f);
                    float near = Mathf.Clamp01((d - size * 0.4f) / (size * 0.8f)); // fade before the camera cuts through
                    float far = Mathf.Clamp01(1f - d / PuffRange);
                    a = p.blocked ? 0f : (0.12f + 0.38f * storm) * lifeFade * near * far * Mathf.Exp(-d / Mathf.Max(6f, L * 2.5f));
                }
                a *= presence;
                float sh = p.shade;
                var top = S.C32(B(dust.r * sh * 1.08f), B(dust.g * sh * 1.08f), B(dust.b * sh * 1.08f), B(a));
                var bottom = S.C32(B(dust.r * sh * 0.78f), B(dust.g * sh * 0.76f), B(dust.b * sh * 0.75f), B(a));
                // Corners: 0 = -ax-ay, 1 = -ax+ay, 2 = +ax+ay, 3 = +ax-ay; brighter where "up" on screen.
                pCols[v] = Vector3.Dot(-ay - ax, camUp) > 0 ? top : bottom;
                pCols[v + 1] = Vector3.Dot(ay - ax, camUp) > 0 ? top : bottom;
                pCols[v + 2] = Vector3.Dot(ay + ax, camUp) > 0 ? top : bottom;
                pCols[v + 3] = Vector3.Dot(-ay + ax, camUp) > 0 ? top : bottom;
            }
            puffMesh.vertices = pVerts;
            puffMesh.colors32 = pCols;
        }

        static void SpawnPuff(ref P p, Vector3 ground, Vector3 up, Vector3 wind)
        {
            var east = Vector3.Cross(up, Vector3.forward); if (east.sqrMagnitude < 1e-4f) east = Vector3.right; east.Normalize();
            var north = Vector3.Cross(east, up);
            float ang = Rand(0f, Mathf.PI * 2f), r = PuffRange * Mathf.Sqrt(Rand(0.03f, 1f));
            var offset = east * Mathf.Cos(ang) * r + north * Mathf.Sin(ang) * r;
            if (p.maxLife > 0f && wind.sqrMagnitude > 0.5f && Vector3.Dot(offset, wind) > 0f) offset -= 2f * Vector3.Project(offset, wind.normalized);
            p.size = Rand(3f, 13f);
            p.pos = ground + offset + up * Rand(p.size * 0.2f, p.size * 1.2f);
            p.blocked = Shelter.Roofed(p.pos, up) || Shields.Covered(p.pos);
            p.life = 0f; p.maxLife = Rand(6f, 14f);
            p.spin = Rand(-0.25f, 0.25f); p.angle = Rand(0f, 6.3f);
            p.shade = Rand(0.8f, 1.1f); p.seed = Rand(0f, 100f);
        }

        static void SpawnFront(ref P p)
        {
            p.vel = new Vector3(Rand(-320f, 320f), Rand(-10f, 160f), 0f); // lateral offset, height
            p.size = Rand(45f, 120f) * (1f + Mathf.Max(0f, p.vel.y) / 200f);
            p.seed = Rand(-60f, 60f);
            p.spin = Rand(-0.05f, 0.05f); p.angle = Rand(0f, 6.3f);
            p.shade = Rand(0.7f, 1.0f);
            p.life = 0f; p.maxLife = 1f;
        }

        static void Collapse(Il2CppStructArray<Vector3> verts, Il2CppStructArray<Color32> cols, int v)
        {
            verts[v] = verts[v + 1] = verts[v + 2] = verts[v + 3] = Vector3.zero;
            var clear = S.C32(0, 0, 0, 0);
            cols[v] = cols[v + 1] = cols[v + 2] = cols[v + 3] = clear;
        }

        static byte B(float x) => (byte)Mathf.Clamp(Mathf.RoundToInt(x * 255f), 0, 255);

        // ------------------------------------------------------------------ building

        static bool Build()
        {
            try
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) { failed = true; Log.Once("wind-err", "Wind: Sprites/Default shader missing, no dust particles"); return false; }
                root = new GameObject();
                root.name = "Mars wind";
                streakMesh = QuadMesh("Mars dust streaks", StreakMax, out sVerts, out sCols, streak: true);
                puffMesh = QuadMesh("Mars dust billows", PuffMax + FrontMax + DevilMax, out pVerts, out pCols, streak: false);
                Renderer(streakMesh, MakeMat(shader, StreakTexture(), "Mars dust streaks"), 10);
                Renderer(puffMesh, MakeMat(shader, PuffTexture(), "Mars dust billows"), 5);
                BuildSound();
                built = true;
                Log.Once("wind", "Mars wind on (dust streaks, billows, storm front, wind sound)");
                return true;
            }
            catch (Exception e)
            {
                failed = true;
                Log.Once("wind-err", "Wind failed: " + e);
                Remove();
                return false;
            }
        }

        static Mesh QuadMesh(string name, int quads, out Il2CppStructArray<Vector3> verts, out Il2CppStructArray<Color32> cols, bool streak)
        {
            var mesh = new Mesh { name = name };
            verts = new Il2CppStructArray<Vector3>(quads * 4);
            cols = new Il2CppStructArray<Color32>(quads * 4);
            var uv = new Vector2[quads * 4];
            var tris = new int[quads * 6];
            for (int q = 0; q < quads; q++)
            {
                int v = q * 4;
                uv[v] = new Vector2(0, 0); uv[v + 1] = new Vector2(0, 1); uv[v + 2] = new Vector2(1, 1); uv[v + 3] = new Vector2(1, 0);
                int k = q * 6;
                tris[k] = v; tris[k + 1] = v + 1; tris[k + 2] = v + 2; tris[k + 3] = v; tris[k + 4] = v + 2; tris[k + 5] = v + 3;
            }
            mesh.vertices = verts;
            mesh.uv = new Il2CppStructArray<Vector2>(uv);
            mesh.colors32 = cols;
            mesh.triangles = new Il2CppStructArray<int>(tris);
            mesh.bounds = S.Box(Vector3.zero, 2000f);
            mesh.MarkDynamic();
            return mesh;
        }

        static void Renderer(Mesh mesh, Material mat, int order)
        {
            var go = new GameObject();
            go.name = mesh.name;
            go.transform.SetParent(root.transform, false);
            var mf = go.AddComponent(Il2CppInterop.Runtime.Il2CppType.Of<MeshFilter>()).Cast<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent(Il2CppInterop.Runtime.Il2CppType.Of<MeshRenderer>()).Cast<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingOrder = order;
        }

        static Material MakeMat(Shader shader, Texture2D tex, string name)
        {
            var m = new Material(shader) { name = name };
            if (tex != null) m.mainTexture = tex;
            if (m.HasProperty("_RendererColor")) m.SetColor("_RendererColor", Color.white);
            m.renderQueue = 3002;
            return m;
        }

        static Texture2D NewTex(int w, int h, string name)
        {
            var ctor = typeof(Texture2D).GetConstructor(new[] { typeof(int), typeof(int), typeof(TextureFormat), typeof(bool) });
            var tex = (Texture2D)ctor.Invoke(new object[] { w, h, TextureFormat.RGBA32, true });
            tex.name = name;
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        static Texture2D StreakTexture()
        {
            const int W = 64, H = 16;
            var tex = NewTex(W, H, "Mars dust streak");
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W, v = (y + 0.5f) / H * 2f - 1f;
                    float along = Mathf.Pow(u, 1.6f) * Mathf.Clamp01((1f - u) * 6f); // long tail, soft head
                    float across = Mathf.Exp(-v * v * 4f);
                    px[y * W + x] = S.C32(255, 255, 255, B(along * across));
                }
            tex.SetPixels32(new Il2CppStructArray<Color32>(px));
            tex.Apply(true, false);
            Keep(tex);
            return tex;
        }

        static Texture2D PuffTexture()
        {
            const int N = 128;
            var tex = NewTex(N, N, "Mars dust billow");
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float n = 0.5f * Noise(u * 2.2f, v * 2.2f) + 0.3f * Noise(u * 5f + 7f, v * 5f) + 0.2f * Noise(u * 11f, v * 11f + 3f);
                    float edge = 1f - Mathf.SmoothStep(0.35f, 1f, r + (n - 0.5f) * 0.55f);
                    float a = edge * Mathf.Lerp(0.55f, 1f, n);
                    float lightV = Mathf.Lerp(0.82f, 1f, (v + 1f) * 0.5f) * Mathf.Lerp(0.9f, 1.05f, n); // lit from above
                    px[y * N + x] = S.C32(B(lightV), B(lightV * 0.98f), B(lightV * 0.96f), B(a));
                }
            tex.SetPixels32(new Il2CppStructArray<Color32>(px));
            tex.Apply(true, false);
            Keep(tex);
            return tex;
        }

        static void Keep(UnityEngine.Object o)
        {
            UnityEngine.Object.DontDestroyOnLoad(o);
            o.hideFlags = HideFlags.DontUnloadUnusedAsset;
        }

        static float Noise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi, u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
            float H(int a, int b) { unchecked { uint h = (uint)((xi + a) * 73856093 ^ (yi + b) * 19349663); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return (h & 0xFFFF) / 65535f; } }
            return Mathf.Lerp(Mathf.Lerp(H(0, 0), H(1, 0), u), Mathf.Lerp(H(0, 1), H(1, 1), u), v);
        }

        // ------------------------------------------------------------------ sound

        static void BuildSound()
        {
            try
            {
                const int rate = 22050;
                rumbleSrc = Narrator.MakeSource(root, "Mars wind rumble", Narrator.MakeClip("Mars wind rumble", Rumble(rate, 8f), rate), loop: true);
                hissSrc = Narrator.MakeSource(root, "Mars wind hiss", Narrator.MakeClip("Mars wind hiss", Hiss(rate, 6f), rate), loop: true);
                alarmClip = Narrator.MakeClip("Mars storm alarm", AlarmSamples(rate), rate);
                alarmSrc = Narrator.MakeSource(root, "Mars storm alarm", alarmClip, loop: false);
                R.TrySet(rumbleSrc, "volume", 0f); R.TrySet(hissSrc, "volume", 0f);
                R.Call(rumbleSrc, "Play"); R.Call(hissSrc, "Play");
            }
            catch (Exception e) { Log.Once("wind-sound-err", "Wind sound failed: " + (e.InnerException ?? e).Message); }
        }

        static float lastRumble = -1f, lastHiss = -1f;

        static void Sound(float presence)
        {
            if (rumbleSrc == null) return;
            float vol = MarsProfile.WindVolume.Value * presence * Mathf.Lerp(1f, 0.3f, Shelter.Indoor); // muffled indoors
            float s = Haze.Storm, f = Haze.Front;
            float rumble = vol * Mathf.Clamp01(0.05f + 0.5f * s + 0.3f * f * (1f - s)) * gust;
            float hiss = vol * Mathf.Pow(s, 1.3f) * 0.45f * gust;
            if (Mathf.Abs(rumble - lastRumble) > 0.005f) { lastRumble = rumble; R.TrySet(rumbleSrc, "volume", rumble); R.TrySet(rumbleSrc, "pitch", 0.85f + 0.25f * gust * (0.6f + 0.4f * s)); }
            if (Mathf.Abs(hiss - lastHiss) > 0.005f) { lastHiss = hiss; R.TrySet(hissSrc, "volume", hiss); R.TrySet(hissSrc, "pitch", 0.9f + 0.2f * gust); }
        }

        public static void Alarm()
        {
            try { if (alarmSrc != null) { R.TrySet(alarmSrc, "volume", 0.55f * MarsProfile.WindVolume.Value + 0.15f); R.Call(alarmSrc, "Play"); } } catch { }
        }

        static float[] Rumble(int rate, float seconds)
        {
            int n = (int)(rate * seconds);
            var s = new float[n];
            var r = new System.Random(11);
            float b = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float white = (float)r.NextDouble() * 2f - 1f;
                b = (b + 0.02f * white) / 1.02f;               // brown noise
                lp += (b - lp) * 0.08f;                        // extra low-pass: distant roar
                float tt = i / (float)rate;
                float env = 0.65f + 0.35f * Mathf.Sin(tt * 2f * Mathf.PI / seconds) * Mathf.Sin(tt * 2f * Mathf.PI * 3f / seconds);
                s[i] = lp * env;
            }
            return Loopable(Normalize(s, 0.8f), rate);
        }

        static float[] Hiss(int rate, float seconds)
        {
            int n = (int)(rate * seconds);
            var s = new float[n];
            var r = new System.Random(23);
            float lo = 0f, band = 0f;
            for (int i = 0; i < n; i++)
            {
                float white = (float)r.NextDouble() * 2f - 1f;
                lo += (white - lo) * 0.12f;                    // remove the lowest part …
                float hp = white - lo;
                band += (hp - band) * 0.55f;                   // … and the sharpest top: sandy hiss
                float tt = i / (float)rate;
                float env = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(tt * 2f * Mathf.PI * 2f / seconds));
                s[i] = band * env;
            }
            return Loopable(Normalize(s, 0.5f), rate);
        }

        static float[] AlarmSamples(int rate)
        {
            var list = new System.Collections.Generic.List<float>();
            for (int rep = 0; rep < 3; rep++)
                foreach (float f in new[] { 880f, 660f })
                {
                    int n = (int)(rate * 0.2f);
                    for (int i = 0; i < n; i++)
                    {
                        float tt = i / (float)rate;
                        float env = Mathf.Clamp01(tt / 0.01f) * Mathf.Clamp01((0.2f - tt) / 0.03f);
                        list.Add(env * 0.5f * (Mathf.Sin(2f * Mathf.PI * f * tt) + 0.25f * Mathf.Sin(4f * Mathf.PI * f * tt)));
                    }
                    for (int i = 0; i < rate * 0.04f; i++) list.Add(0f);
                }
            return list.ToArray();
        }

        static float[] Normalize(float[] s, float peak)
        {
            float m = 1e-6f;
            foreach (var x in s) m = Math.Max(m, Math.Abs(x));
            for (int i = 0; i < s.Length; i++) s[i] *= peak / m;
            return s;
        }

        /// <summary>Crossfade the end into the start so the loop has no click.</summary>
        static float[] Loopable(float[] s, int rate)
        {
            int fade = rate / 2;
            var o = new float[s.Length - fade];
            for (int i = 0; i < o.Length; i++) o[i] = s[i];
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                o[i] = s[i] * k + s[s.Length - fade + i] * (1f - k);
            }
            return o;
        }

        // ------------------------------------------------------------------ misc

        static void Hide() { if (root != null && root.activeSelf) root.SetActive(false); }

        public static void Remove()
        {
            if (root != null) { try { UnityEngine.Object.Destroy(root); } catch { } }
            root = null; built = false;
            rumbleSrc = hissSrc = alarmSrc = null;
            lastRumble = lastHiss = -1f;
            for (int i = 0; i < streaks.Length; i++) streaks[i].maxLife = 0f;
            for (int i = 0; i < puffs.Length; i++) puffs[i].maxLife = 0f;
        }

        public static string Describe() =>
            !built ? (failed ? "failed (see log)" : "not built")
                   : $"speed {windSpeed:0.0} m/s towards the {Haze.Compass(Haze.WindDir)}, gust {gust:0.00}, sound {(rumbleSrc != null ? "on" : "off")}";
    }
}
