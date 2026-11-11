using System;
using System.Linq;
using MoonCleaners;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// The sky shader draws Earth from a texture ("_Earth"). On Mars that slot shows Phobos instead:
    /// a lumpy, cratered, grey-brown moon generated here, with the big Stickney crater on one side.
    /// </summary>
    internal static class Sky
    {
        static Texture2D phobos, moon;

        /// <summary>For the ending: the sky shows the Moon (grey, with dark maria) where Phobos was.</summary>
        public static bool ShowMoon(bool on)
        {
            try
            {
                var s = Look.All<MoonSolarSystem>().FirstOrDefault(x => Look.InScene(x.gameObject));
                var sky = s?.runtimeSky;
                if (sky == null || !sky.HasProperty("_Earth")) return false;
                if (on && moon == null) moon = MakeBody(1969, 30, 0.62f, 0.62f, 0.64f, true, "The Moon (Mars mod)");
                var tex = on ? moon : phobos;
                if (tex == null) return false;
                sky.SetTexture("_Earth", tex);
                return true;
            }
            catch (Exception e) { Log.Once("sky-moon", "[sky] moon swap failed: " + e.Message); return false; }
        }

        public static void Apply(MoonSolarSystem s)
        {
            var sky = s.runtimeSky;
            if (sky == null) return;
            Look.DumpProps(sky, "Sky material");
            if (!sky.HasProperty("_Earth")) return;
            if (phobos == null) phobos = MakeBody(1877, 40, 0.46f, 0.40f, 0.36f, false, "Phobos (Mars mod)");
            if (phobos != null && sky.GetTexture("_Earth") != phobos)
            {
                sky.SetTexture("_Earth", phobos);
                Log.Once("phobos", "Phobos is in the sky");
            }
        }

        static Texture2D MakeBody(int seed, int craterCount, float cr, float cg, float cb, bool maria, string name)
        {
            try
            {
                const int W = 256, H = 128;
                // The Unity stub this mod compiles against lacks Texture2D's constructors; the real one has them.
                var ctor = typeof(Texture2D).GetConstructor(new[] { typeof(int), typeof(int), typeof(TextureFormat), typeof(bool) });
                if (ctor == null) { Log.Once("phobos-err", "Texture2D constructor not found"); return null; }
                var tex = (Texture2D)ctor.Invoke(new object[] { W, H, TextureFormat.RGBA32, false });
                tex.name = name;
                tex.wrapMode = TextureWrapMode.Repeat;
                var rnd = new System.Random(seed);
                // A few craters on the sphere, given as (longitude, latitude, radius in radians, depth).
                var craters = new (float lon, float lat, float r, float d)[craterCount];
                craters[0] = maria ? (2.2f, -0.6f, 0.3f, 0.5f) : (0.4f, 0.1f, 0.55f, 0.55f); // Tycho-like / Stickney
                for (int i = 1; i < craters.Length; i++)
                    craters[i] = ((float)(rnd.NextDouble() * Math.PI * 2), (float)((rnd.NextDouble() - 0.5) * Math.PI * 0.9),
                                  (float)(0.04 + rnd.NextDouble() * 0.16), (float)(0.15 + rnd.NextDouble() * 0.3));

                for (int y = 0; y < H; y++)
                {
                    float lat = ((y + 0.5f) / H - 0.5f) * Mathf.PI;
                    for (int x = 0; x < W; x++)
                    {
                        float lon = (x + 0.5f) / W * Mathf.PI * 2f;
                        var p = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                        float shade = 0.55f + 0.25f * Noise(p * 3f) + 0.12f * Noise(p * 9f) + 0.06f * Noise(p * 23f);
                        foreach (var c in craters)
                        {
                            var q = new Vector3(Mathf.Cos(c.lat) * Mathf.Cos(c.lon), Mathf.Sin(c.lat), Mathf.Cos(c.lat) * Mathf.Sin(c.lon));
                            float ang = Mathf.Acos(Mathf.Clamp(Vector3.Dot(p, q), -1f, 1f));
                            if (ang > c.r * 1.25f) continue;
                            float t = ang / c.r;
                            if (t < 1f) shade -= c.d * (1f - t * t) * 0.6f;             // bowl
                            else shade += c.d * 0.35f * (1f - (t - 1f) / 0.25f);        // bright rim
                        }
                        if (maria) shade -= 0.28f * Mathf.Clamp01(Noise(p * 1.6f + Vector3.one * 7f) * 3f + 0.2f);   // dark seas
                        shade = Mathf.Clamp01(shade);
                        tex.SetPixel(x, y, new Color(cr * shade + 0.05f, cg * shade + 0.04f, cb * shade + 0.03f, 1f));
                    }
                }
                tex.Apply(false, false);
                UnityEngine.Object.DontDestroyOnLoad(tex);
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return tex;
            }
            catch (Exception e) { Log.Once("phobos-err", "Could not make Phobos: " + e.Message); return null; }
        }

        static float Hash(int x, int y, int z)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093 ^ y * 19349663 ^ z * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        /// <summary>Smooth 3D value noise in -0.5..0.5.</summary>
        static float Noise(Vector3 p)
        {
            int xi = Mathf.FloorToInt(p.x), yi = Mathf.FloorToInt(p.y), zi = Mathf.FloorToInt(p.z);
            float xf = p.x - xi, yf = p.y - yi, zf = p.z - zi;
            float u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf), w = zf * zf * (3 - 2 * zf);
            float L(int a, int b, int c) => Hash(xi + a, yi + b, zi + c);
            float x00 = Mathf.Lerp(L(0, 0, 0), L(1, 0, 0), u), x10 = Mathf.Lerp(L(0, 1, 0), L(1, 1, 0), u);
            float x01 = Mathf.Lerp(L(0, 0, 1), L(1, 0, 1), u), x11 = Mathf.Lerp(L(0, 1, 1), L(1, 1, 1), u);
            return Mathf.Lerp(Mathf.Lerp(x00, x10, v), Mathf.Lerp(x01, x11, v), w) - 0.5f;
        }
    }
}
