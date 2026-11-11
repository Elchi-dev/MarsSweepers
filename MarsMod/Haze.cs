using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MoonCleaners;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// Mars atmosphere (0.5): the planet shader ignores Unity's fog, so the air is built from geometry around
    /// the camera:
    ///
    ///  * Sky dome: a big sphere behind everything with a per-vertex Mars sky (butterscotch near the horizon,
    ///    darker overhead, bluish around the sun, blue glow at dusk, almost clear at night so the stars stay).
    ///  * Fog shells: many thin, nearly transparent spheres spaced geometrically. Each one adds the fog of the
    ///    stretch of air in front of it, so together they follow real exponential fog (transmittance e^-d/L)
    ///    in small steps instead of one hard wall. Overhead the air thins out (per-vertex height falloff), so
    ///    the sky is not painted flat brown. Only shells that matter for the current visibility are drawn.
    ///  * Storms shrink the visibility length L from ~1.4 km to ~9 m, bring a dust front from one direction
    ///    first (see Wind.cs for the moving dust and the sound), and drown the sky.
    ///
    /// The storm schedule depends only on the shared game clock and the world, so a whole crew gets the same
    /// storm without any network messages.
    /// </summary>
    internal static class Haze
    {
        // ------------------------------------------------------------------ state seen by other parts

        /// <summary>0 = clear, 1 = full dust storm.</summary>
        public static float Storm { get; private set; }
        /// <summary>0..1 while a storm front is approaching (1 = it arrives now), else 0.</summary>
        public static float Front { get; private set; }
        /// <summary>Direction the storm comes FROM (world space, tangent to the planet at the camera).</summary>
        public static Vector3 FrontDir { get; private set; } = Vector3.forward;
        /// <summary>Direction the wind blows TO.</summary>
        public static Vector3 WindDir => -FrontDir;
        /// <summary>Current e-folding visibility length in metres.</summary>
        public static float Visibility { get; private set; } = 1400f;
        /// <summary>0 = night, 1 = day (from the game's own sun).</summary>
        public static float Daylight { get; private set; } = 1f;
        public static float SunHeight { get; private set; }
        public static Vector3 SunDir { get; private set; } = Vector3.up;
        /// <summary>1 normally, 0 when the camera is far above the ground (map view): everything fades out.</summary>
        public static float Presence { get; private set; } = 1f;
        public static Color FogColor { get; private set; }
        public static Vector3 Up { get; private set; } = Vector3.up;

        public static float? Override;
        public static float RampPerSecond = 0.12f;

        // ------------------------------------------------------------------ objects

        static GameObject root;
        static readonly List<Shell> shells = new List<Shell>();
        static Shell dome;
        static Vector3[] unitVerts;
        static Il2CppStructArray<Vector3> vertArray;
        static Il2CppStructArray<int> triArray;
        static Il2CppStructArray<Color32> colours;
        static Shader shader;
        static bool vertexColours;
        static float testUntil = -1f, nextColours, nextPresence;
        static float lastColourStorm = -1f;

        class Shell
        {
            public GameObject go;
            public Material mat;
            public Mesh mesh;
            public float radius, inner;
            public bool active = true;
        }

        const int LatSteps = 24, LonSteps = 48;
        const float DomeRadius = 3400f, MaxShell = 3200f, MinShell = 1.5f;

        // ------------------------------------------------------------------ per frame

        public static void Tick(float dt, MoonSolarSystem solar)
        {
            if (!MarsProfile.HazeOn.Value) { Remove(); return; }
            var cam = Camera.main;
            if (cam == null) return;
            if (root == null && !Build()) return;

            float now = Time.realtimeSinceStartup;
            var camPos = cam.transform.position;
            Up = camPos.sqrMagnitude > 1f ? camPos.normalized : Vector3.up;
            root.transform.position = camPos;
            root.transform.rotation = Quaternion.FromToRotation(Vector3.up, Up);

            ReadSun(solar);
            UpdateStorm(dt, now);
            Shelter.Tick(now, dt, camPos, Up);
            if (now >= nextPresence) { nextPresence = now + 0.2f; Presence = Mathf.MoveTowards(Presence, TargetPresence(cam, camPos), 0.5f); }
            bool show = Presence > 0.01f;
            if (root.activeSelf != show) root.SetActive(show);
            if (!show) { try { Wind.Tick(dt, cam); } catch { } return; }

            // Visibility: clear Mars air ~1.4 km (HazeStrength 0.5), a full storm ~9 m.
            float clearL = 700f / Mathf.Max(0.05f, MarsProfile.HazeStrength.Value);
            float s = Storm * (1f - 0.8f * LightPoles.Clear(camPos)) * (Shields.Covered(camPos) ? 0.08f : 1f);
            Visibility = Mathf.Exp(Mathf.Lerp(Mathf.Log(clearL), Mathf.Log(9f), Mathf.Pow(s, 0.8f)));
            float L = Visibility;

            FogColor = ComputeFogColour(s);
            bool test = now < testUntil;
            var baseCol = test ? new Color(1f, 0f, 1f) : FogColor;

            // Shells: alpha of each = fog of the air between it and the next smaller shell.
            float lo = L / 30f, hi = L * 6f;
            foreach (var sh in shells)
            {
                bool on = sh.radius >= lo && sh.inner <= hi;
                float a = on ? 1f - Mathf.Exp(-(sh.radius - (sh.inner < lo ? 0f : sh.inner)) / L) : 0f;
                a *= Presence;
                // Indoors: no storm air inside the room (what's beyond the walls is hidden by them anyway).
                if (sh.radius < Shelter.RoomRadius) a *= 1f - Shelter.Indoor;
                if (test) a = Mathf.Max(a, 0.06f);
                on = a > 0.002f;
                if (on != sh.active) { sh.active = on; sh.go.SetActive(on); }
                if (!on) continue;
                var c = baseCol; c.a = a;
                sh.mat.SetColor("_Color", c);
                sh.mat.SetColor("_BaseColor", c);
            }
            var dc = test ? new Color(1f, 0f, 1f, 1f) : Color.white;
            dc.a = Presence;
            dome.mat.SetColor("_Color", dc);
            dome.mat.SetColor("_BaseColor", dc);

            try { Wind.Tick(dt, cam); } catch (Exception e) { Log.Once("wind-tick", "Wind tick: " + e.Message); }

            if (vertexColours && (now >= nextColours || Mathf.Abs(s - lastColourStorm) > 0.03f))
            {
                nextColours = now + 0.5f;
                lastColourStorm = s;
                PaintDome(s);
                foreach (var sh in shells) if (sh.active) PaintShell(sh, s);
            }
        }

        static void ReadSun(MoonSolarSystem solar)
        {
            float sun = 30f;
            try { if (solar != null) sun = solar.SunHeight; } catch { }
            SunHeight = sun;
            try { Daylight = Mathf.Clamp01(MoonSolarSystem.Daylight); }
            catch { Daylight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-8f, 12f, sun)); }
            // The game's Daylight can stay high at dusk; follow the sun height too.
            Daylight = Mathf.Min(Daylight, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-10f, 10f, sun)));
            try { if (solar != null && solar.sunlight != null) SunDir = -solar.sunlight.transform.forward; } catch { }
        }

        static float TargetPresence(Camera cam, Vector3 camPos)
        {
            // Map / orbit views move the main camera far up: no air there, or you see nothing.
            try
            {
                var world = Commands.FindWorld();
                if (world == null) return 1f;
                var ground = world.Terrain.SurfacePoint(Up);
                float alt = camPos.magnitude - ground.magnitude;
                float walkerDist = 0f;
                var w = Commands.FindWalker();
                if (w != null) walkerDist = Vector3.Distance(w.transform.position, camPos);
                float far = Mathf.Max(alt, walkerDist);
                return 1f - Mathf.Clamp01((far - 60f) / 140f);
            }
            catch { return 1f; }
        }

        // ------------------------------------------------------------------ colours

        static Color ComputeFogColour(float s)
        {
            float D = Daylight;
            var day = MarsProfile.C(MarsProfile.HazeDayColor, new Color(.84f, .68f, .54f));
            var night = new Color(.055f, .055f, .07f);
            var col = Color.Lerp(night, day, D);
            float twilight = Mathf.Clamp01(1f - Mathf.Abs(SunHeight - 1f) / 9f);
            col = Color.Lerp(col, new Color(.42f, .45f, .56f), twilight * 0.45f);
            var storm = Color.Lerp(new Color(.09f, .065f, .05f), new Color(.70f, .50f, .35f), D);
            return Color.Lerp(col, storm, s);
        }

        static void PaintShell(Shell sh, float s)
        {
            float H = Mathf.Lerp(220f, 5000f, s);                // how fast the air thins upwards
            var sunLocal = Quaternion.Inverse(root.transform.rotation) * SunDir;
            float D = Daylight;
            for (int i = 0; i < unitVerts.Length; i++)
            {
                var v = unitVerts[i];
                float h = sh.radius * v.y;                          // height of this point above the camera
                float dens = h <= 0f ? 1f : Mathf.Exp(-h / H);
                // Mars: around the sun the dust scatters bluish-white, away from it warmer and dimmer.
                float cosA = Vector3.Dot(v, sunLocal);
                float halo = Mathf.Exp((cosA - 1f) / 0.12f) * D * (1f - s * 0.7f);
                float r = Mathf.Lerp(1f, .86f, halo) * Mathf.Lerp(.93f, 1f, (cosA + 1f) * 0.5f);
                float g = Mathf.Lerp(1f, .94f, halo) * Mathf.Lerp(.95f, 1f, (cosA + 1f) * 0.5f);
                float b = Mathf.Lerp(.97f, 1f, halo);
                colours[i] = S.C32(B(r), B(g), B(b), B(dens));
            }
            sh.mesh.colors32 = colours;
        }

        static void PaintDome(float s)
        {
            var sunLocal = Quaternion.Inverse(root.transform.rotation) * SunDir;
            float D = Daylight, sunElev = SunHeight;
            float twilight = Mathf.Clamp01(1f - Mathf.Abs(sunElev - 0f) / 12f);
            var dayCol = MarsProfile.C(MarsProfile.HazeDayColor, new Color(.84f, .68f, .54f));
            var zenith = new Color(dayCol.r * .72f, dayCol.g * .66f, dayCol.b * .68f);
            var stormCol = Color.Lerp(new Color(.08f, .06f, .045f), new Color(.62f, .44f, .31f), D);
            for (int i = 0; i < unitVerts.Length; i++)
            {
                var v = unitVerts[i];
                float e = Mathf.Max(0f, v.y);
                float w = Mathf.Pow(1f - e, 3f);                       // 1 at the horizon, 0 overhead
                float cosA = Vector3.Dot(v, sunLocal);
                float near = Mathf.Exp((cosA - 1f) / 0.05f);          // tight glow around the sun
                float wide = Mathf.Exp((cosA - 1f) / 0.35f);

                // Day: butterscotch, brighter and paler at the horizon, bluish-white around the sun.
                var c = Color.Lerp(zenith, dayCol, w);
                c = Color.Lerp(c, new Color(.82f, .84f, .88f), near * 0.85f);
                c = Color.Lerp(c, new Color(.74f, .74f, .78f), wide * 0.25f);
                float a = Mathf.Lerp(0.80f, 0.97f, w);

                // Night: clear sky with a faint dark band at the horizon.
                var nc = new Color(.045f, .042f, .05f);
                float na = 0.38f * w * w;
                c = Color.Lerp(nc, c, D); a = Mathf.Lerp(na, a, D);

                // Dusk and dawn: the blue Martian sunset glow around the sun, low in the sky.
                float glow = Mathf.Exp((cosA - 1f) / 0.25f) * Mathf.Lerp(1f, 0.3f, e) * twilight;
                c = Color.Lerp(c, new Color(.36f, .52f, .80f), glow * 0.85f);
                a = Mathf.Max(a, glow * 0.85f);

                // Storm: one dull reddish ceiling, the sun only a faint bluish smudge.
                c = Color.Lerp(c, stormCol + new Color(.10f, .12f, .16f) * near * D, s);
                a = Mathf.Lerp(a, 1f, s);

                if (v.y < -0.02f) a = 1f; // below the horizon: solid (hidden by the ground anyway)
                colours[i] = S.C32(B(c.r), B(c.g), B(c.b), B(a));
            }
            dome.mesh.colors32 = colours;
        }

        static byte B(float x) => (byte)Mathf.Clamp(Mathf.RoundToInt(x * 255f), 0, 255);

        // ------------------------------------------------------------------ building

        static bool Build()
        {
            try
            {
                shader = Shader.Find("Sprites/Default");
                vertexColours = shader != null;
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) { Log.Once("haze-err", "Haze: no usable shader"); return false; }

                BuildSphere();
                root = new GameObject();
                root.name = "Mars air";
                shells.Clear();

                dome = MakeShell(DomeRadius, 0f, -300);
                float q = Mathf.Clamp(MarsProfile.FogQuality.Value, 1.08f, 1.5f);
                var radii = new List<float>();
                for (float r = MinShell; r <= MaxShell; r *= q) radii.Add(r);
                for (int i = radii.Count - 1; i >= 0; i--)             // biggest first = drawn first
                    shells.Add(MakeShell(radii[i], i > 0 ? radii[i - 1] : 0f, -299 + (radii.Count - 1 - i)));

                if (vertexColours) { PaintDome(0f); foreach (var sh in shells) PaintShell(sh, 0f); }
                Log.Once("haze", $"Mars air on: sky dome + {shells.Count} fog shells ({(vertexColours ? "vertex-coloured" : "flat fallback")}), visibility {700f / Mathf.Max(0.05f, MarsProfile.HazeStrength.Value):0} m");
                return true;
            }
            catch (Exception e)
            {
                Log.Once("haze-err", "Haze failed: " + e);
                Remove();
                return false;
            }
        }

        static void BuildSphere()
        {
            if (unitVerts != null) return;
            int n = (LatSteps + 1) * (LonSteps + 1);
            unitVerts = new Vector3[n];
            int k = 0;
            for (int la = 0; la <= LatSteps; la++)
            {
                float lat = Mathf.Lerp(-Mathf.PI / 2f, Mathf.PI / 2f, la / (float)LatSteps);
                for (int lo = 0; lo <= LonSteps; lo++)
                {
                    float lon = lo / (float)LonSteps * Mathf.PI * 2f;
                    unitVerts[k++] = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                }
            }
            var tris = new List<int>();
            for (int la = 0; la < LatSteps; la++)
                for (int lo = 0; lo < LonSteps; lo++)
                {
                    int a = la * (LonSteps + 1) + lo, b = a + 1, c = a + LonSteps + 1, d = c + 1;
                    // Wound to face inwards (the camera is inside).
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            vertArray = new Il2CppStructArray<Vector3>(unitVerts);
            triArray = new Il2CppStructArray<int>(tris.ToArray());
            colours = new Il2CppStructArray<Color32>(n);
        }

        static Shell MakeShell(float radius, float inner, int order)
        {
            var go = new GameObject();
            go.name = radius >= DomeRadius ? "Mars sky dome" : $"Mars air {radius:0.#} m";
            go.transform.SetParent(root.transform, false);
            go.transform.localScale = Vector3.one * radius;
            var mesh = new Mesh();
            mesh.name = go.name;
            mesh.vertices = vertArray;
            mesh.triangles = triArray;
            mesh.colors32 = new Il2CppStructArray<Color32>(unitVerts.Length);
            mesh.bounds = S.Box(Vector3.zero, 2.2f);
            mesh.MarkDynamic();
            var mf = go.AddComponent(Il2CppInterop.Runtime.Il2CppType.Of<MeshFilter>()).Cast<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent(Il2CppInterop.Runtime.Il2CppType.Of<MeshRenderer>()).Cast<MeshRenderer>();
            var mat = new Material(shader) { name = go.name };
            if (mat.HasProperty("_RendererColor")) mat.SetColor("_RendererColor", Color.white);
            if (!vertexColours) SetupUnlitTransparent(mat);
            mat.renderQueue = 3001;
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingOrder = order;
            return new Shell { go = go, mat = mat, mesh = mesh, radius = radius, inner = inner };
        }

        public static void SetupUnlitTransparent(Material m)
        {
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", 5f); m.SetFloat("_DstBlend", 10f);
            m.SetFloat("_SrcBlendAlpha", 1f); m.SetFloat("_DstBlendAlpha", 10f);
            m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 0f); m.SetFloat("_ZTest", 4f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
        }

        public static void Remove()
        {
            if (root != null) { try { UnityEngine.Object.Destroy(root); } catch { } }
            root = null;
            shells.Clear();
            dome = null;
            Storm = 0f; Front = 0f;
            lastColourStorm = -1f;
            // Storm tests from the terminal belong to the world they were made in.
            manual = Manual.None; Override = null; RampPerSecond = 0.12f;
            warned = false; inStormNoticed = false;
            Wind.Remove();
        }

        // ------------------------------------------------------------------ storms

        enum Manual { None, Arriving, Fixed }
        static Manual manual = Manual.None;
        static float manualStart, manualFront = 20f;
        static bool warned, inStormNoticed;
        static long frontForHour = long.MinValue;
        static float windAzimuth;

        static void UpdateStorm(float dt, float now)
        {
            float target;
            float front = 0f;
            double startsIn;
            float scheduled = Scheduled(Clock(), out startsIn, out long hour, out float azimuth);

            if (manual == Manual.Arriving)
            {
                float t = now - manualStart;
                front = Mathf.Clamp01(t / manualFront);
                target = t >= manualFront ? 1f : 0f;
            }
            else if (Override.HasValue) target = Override.Value;
            else
            {
                target = scheduled;
                if (startsIn < 75) front = 1f - (float)startsIn / 75f;
                if (hour != frontForHour) { frontForHour = hour; windAzimuth = azimuth; }
            }

            Front = front;
            Storm = Mathf.MoveTowards(Storm, target, dt * RampPerSecond);
            FrontDir = TangentDir(windAzimuth);

            // Announcements, like Mad Max's "storm approaching, seek shelter".
            if (front > 0f && !warned)
            {
                warned = true;
                int secs = manual == Manual.Arriving ? Mathf.RoundToInt(manualFront) : Mathf.RoundToInt((float)startsIn);
                MarsWorld.Urgent($"DUST STORM WARNING: a storm front is coming from the {Compass(FrontDir)}. It hits in about {secs} s. Get back to the base!");
                Wind.Alarm();
                Plugin.Log.LogMessage($"Dust storm warning (front from the {Compass(FrontDir)})");
            }
            if (front <= 0f && Storm < 0.05f && target < 0.05f) warned = false;
            if (Storm > 0.6f && !inStormNoticed)
            {
                inStormNoticed = true;
                MarsWorld.Urgent("You are inside a dust storm. Visibility is a few metres. Stay close to the base.");
                Plugin.Log.LogMessage("Dust storm: inside");
            }
            if (Storm < 0.08f && inStormNoticed)
            {
                inStormNoticed = false;
                MarsWorld.Notice("The dust storm has passed.");
                Plugin.Log.LogMessage("Dust storm: cleared");
            }
        }

        /// <summary>A direction tangent to the planet at the camera, by compass azimuth (0 = north, 90 = east).</summary>
        static Vector3 TangentDir(float azimuthDeg)
        {
            var up = Up;
            var east = Vector3.Cross(Vector3.up, up);
            if (east.sqrMagnitude < 1e-4f) east = Vector3.right;
            east.Normalize();
            var north = Vector3.Cross(up, east).normalized;
            float a = azimuthDeg * Mathf.Deg2Rad;
            return (north * Mathf.Cos(a) + east * Mathf.Sin(a)).normalized;
        }

        public static string Compass(Vector3 dir)
        {
            var up = Up;
            var east = Vector3.Cross(Vector3.up, up);
            if (east.sqrMagnitude < 1e-4f) east = Vector3.right;
            east.Normalize();
            var north = Vector3.Cross(up, east).normalized;
            float az = Mathf.Atan2(Vector3.Dot(dir, east), Vector3.Dot(dir, north)) * Mathf.Rad2Deg;
            if (az < 0) az += 360f;
            string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            return names[Mathf.RoundToInt(az / 45f) % 8];
        }

        static double Clock()
        {
            try { if (MoonSharedClock.Ready) return MoonSharedClock.Now; } catch { }
            return (DateTime.UtcNow - new DateTime(2026, 1, 1)).TotalSeconds;
        }

        /// <summary>Storm level from the schedule: 45-minute slots, ~2 in 3 slots have a storm of 4 to 8 minutes
        /// (on average one storm every ~70 minutes of play).</summary>
        static float Scheduled(double now, out double startsIn, out long hourOut, out float azimuth)
        {
            startsIn = double.MaxValue;
            hourOut = 0; azimuth = windAzimuth;
            if (!MarsProfile.StormsOn.Value) return 0f;
            const double Slot = 2700.0;
            long hour = (long)Math.Floor(now / Slot);
            float level = 0f;
            for (long h = hour - 1; h <= hour + 1; h++)
            {
                uint x = Mix((uint)h ^ (uint)MarsState.StormSeed * 2654435761u ^ 0x6D617273u); // "mars"
                if ((x % 100) >= 66) continue;
                double start = h * Slot + 180 + (x >> 8) % 1800;             // minute 3 to 33 of the slot
                double length = 240 + (x >> 20) % 240;                       // 4 to 8 minutes
                double t = now - start;
                if (t < -75) { startsIn = Math.Min(startsIn, -t); continue; }
                if (t < 0) { startsIn = Math.Min(startsIn, -t); hourOut = h; azimuth = (x >> 4) % 360; continue; }
                if (t > length) continue;
                hourOut = h; azimuth = (x >> 4) % 360;
                float ramp = Mathf.Clamp01((float)Math.Min(t, length - t) / 60f); // one minute in and out
                level = Math.Max(level, ramp);
            }
            return level;
        }

        static uint Mix(uint h)
        {
            unchecked { h ^= h >> 16; h *= 0x7feb352d; h ^= h >> 15; h *= 0x846ca68b; h ^= h >> 16; return h; }
        }

        // ------------------------------------------------------------------ terminal

        public static string Command(string arg)
        {
            float now = Time.realtimeSinceStartup;
            switch (arg)
            {
                case "on": case "warn":
                    manual = Manual.Arriving; manualStart = now; manualFront = 20f; Override = null;
                    windAzimuth = UnityEngine.Random.Range(0f, 360f); warned = false; RampPerSecond = 0.12f;
                    return "Storm front incoming: watch the horizon, it hits in 20 s. ('storm now' skips the wait)";
                case "now":
                    manual = Manual.Fixed; Override = 1f; RampPerSecond = 0.5f; warned = true;
                    return "Storm right now.";
                case "off":
                    manual = Manual.Fixed; Override = 0f; RampPerSecond = 0.12f;
                    return "Storm dying down (~8 s). 'storm auto' returns to the schedule.";
                case "stop":
                    manual = Manual.Fixed; Override = 0f; Storm = 0f; Front = 0f;
                    return "Storm gone. 'storm auto' returns to the schedule.";
                case "auto":
                    manual = Manual.None; Override = null; RampPerSecond = 0.12f;
                    return "Storms follow the shared schedule again.";
            }
            if (float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v))
            {
                manual = Manual.Fixed; Override = Mathf.Clamp01(v); RampPerSecond = 0.3f;
                return $"Storm level fixed at {Override:0.00}.";
            }
            return "Usage: storm on | now | off | stop | auto | <0..1>";
        }

        public static string Test()
        {
            testUntil = Time.realtimeSinceStartup + 10f;
            return root == null
                ? "The Mars air does not exist right now (not in a Mars world, or haze is off)."
                : "Haze test: the distance and sky turn MAGENTA for 10 seconds.";
        }

        public static string Info()
        {
            Scheduled(Clock(), out double startsIn, out _, out _);
            string next = startsIn < 1e9 ? $"next scheduled storm in {startsIn / 60:0.0} min" : "no storm scheduled soon";
            string mode = manual == Manual.Arriving ? "arriving (manual)" : Override.HasValue ? $"fixed at {Override:0.00}" : "schedule";
            return $"storm {Storm:0.00}, front {Front:0.00} from the {Compass(FrontDir)} ({mode}), {next}, visibility {Visibility:0} m";
        }

        public static IEnumerable<string> Debug()
        {
            var cam = Camera.main;
            yield return $"camera     {(cam == null ? "none" : cam.name + $" far={cam.farClipPlane:0}")}";
            if (root == null) { yield return "air        not built (not in a Mars world, or haze off)"; yield break; }
            int on = 0; foreach (var s in shells) if (s.active) on++;
            yield return $"air        {shells.Count} shells ({on} drawn) + dome, shader {(vertexColours ? "Sprites/Default" : "URP Unlit")}, presence {Presence:0.00}";
            yield return $"light      daylight {Daylight:0.00}, sun height {SunHeight:0.0}°, fog colour {FogColor.r:0.00} {FogColor.g:0.00} {FogColor.b:0.00}";
            yield return $"wind       {Wind.Describe()}";
            yield return $"shelter    indoor {Shelter.Indoor:0.00}, room ~{Shelter.RoomRadius:0} m, {Shelter.RaysPerSecond} rays/s";
            yield return Info();
        }
    }
}
