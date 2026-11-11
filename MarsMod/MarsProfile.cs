using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// Everything that makes Mars Mars.
    ///
    /// Terrain values are fixed per "terrain profile" and NOT configurable: every crew member must
    /// generate exactly the same planet, and an existing Mars save must keep its terrain forever.
    /// A new profile number is added when Mars terrain changes in a later version; old saves keep theirs.
    ///
    /// Look values (colours, fog, sky) are only visual and can be tuned in BepInEx/config/marsmod.cfg.
    /// </summary>
    internal static class MarsProfile
    {
        public const int LatestTerrainProfile = 3;

        /// <summary>MoonSettings field name → Mars value. Fields not listed keep the game's Moon value.</summary>
        public static Dictionary<string, object> Terrain(int profile)
        {
            var d = Profile1();
            if (profile < 2) return d;
            // Profile 2, "Red Planet" (0.5.2 worlds): different landforms than the Moon, but the home valley and the
            // base site are left exactly as the game builds them (changing the spawn valley in 0.5.1 put the
            // floor inside the base, broke the spawn point and made the rim unclimbable).
            d["landformScale"] = 1.3f;            // Moon 1: broader features
            d["terrainHeight"] = 78f;
            d["landscapeVariety"] = 1.5f;
            d["mountainHeight"] = 290f;
            d["mountainRanges"] = 9;
            d["rilleCount"] = 10;                 // more canyons
            d["craterDensity"] = 0.3f;
            d["freshCraterDensity"] = 0.25f;
            d["largeCraterRadius"] = 140f;
            d["craterDepthRatio"] = 0.12f;
            if (profile < 3) return d;
            // Profile 3 (0.5.3): a Mars home valley after all, built around what the base needs.
            // The base sits in the spawn crater: its depth, floor radius and the clear radius stay exactly the
            // Moon's (0.5.1 made the crater shallower, which pushed ground up into the base and onto the spawn).
            // The valley around it is wider and its rim taller, but rim height grows with the wall width so the
            // average slope stays the Moon's (~19°, well under the 30° you can walk), and the roughness is only
            // raised a little so the rim doesn't turn into cliffs. The way out faces south-east.
            d["spawnValleyRadius"] = 290f;        // Moon 255
            d["spawnValleyWallWidth"] = 175f;     // Moon 130
            d["spawnValleyRimHeight"] = 60f;      // Moon 45  (60/175 ≈ 45/130)
            d["spawnValleyPeakHeight"] = 85f;     // Moon 60
            d["spawnValleyOuterWidth"] = 220f;    // Moon 180
            d["spawnValleyPassAzimuth"] = 135f;   // Moon 270
            d["spawnValleyPassWidth"] = 75f;      // Moon 55: a wider way out
            d["spawnValleyRoughness"] = 0.42f;    // Moon 0.3
            return d;
        }

        static Dictionary<string, object> Profile1()
        {
            // Profile 1, "Red Moon": same 2 km radius (so regions, saves and the base layout stay valid),
            // a new seed, Mars gravity, taller and more mountain ranges, fewer craters, more canyons (rilles).
            return new Dictionary<string, object>
            {
                ["seed"] = 19761,                 // Viking 1 landed in 1976 :3
                ["gravity"] = 3.71f,              // Moon 1.62
                ["jumpHeight"] = 1.6f,            // Moon 2.5: heavier steps on Mars
                ["terrainHeight"] = 72f,          // Moon 50
                ["landscapeVariety"] = 1.35f,     // Moon 1
                ["mountainHeight"] = 260f,        // Moon 170
                ["mountainRanges"] = 7,           // Moon 5
                ["rilleCount"] = 6,               // Moon 3: canyons
                ["craterDensity"] = 0.5f,         // Moon 1: Mars has fewer craters, wind erodes them
                ["freshCraterDensity"] = 0.4f,    // Moon 1
                ["largeCraterRadius"] = 110f,     // Moon 78
                ["craterDepthRatio"] = 0.15f,     // Moon 0.18: shallower, filled with sand
            };
        }

        // ----------------------------------------------------------------- look (configurable)
        public static ConfigEntry<string> DustTint, GroundTint, ShadowTint, MareDustTint, MareGroundTint;
        public static ConfigEntry<bool> HazeOn, StormsOn;
        public static ConfigEntry<float> HazeStrength;
        public static ConfigEntry<string> HazeDayColor;
        public static ConfigEntry<float> FogQuality, WindVolume;
        public static ConfigEntry<bool> WindParticles, MarketTicker;

        /// <summary>Move a setting to its new default if it still holds the old default (so tuning updates reach players).</summary>
        static ConfigEntry<string> Migrate(ConfigEntry<string> e, string oldDefault)
        {
            if (e.Value.Replace(" ", "") == oldDefault.Replace(" ", "")) e.Value = (string)e.DefaultValue;
            return e;
        }
        public static ConfigEntry<string> DaySky, DayEquator, DayGround, FillColor, NightTint, SunColor, GlobeTint;
        public static ConfigEntry<float> SunIntensity, SunAngularRadius, PhobosSize, PhobosSpeed, NightLight;
        public static ConfigEntry<bool> TintGlobe;
        public static ConfigEntry<float> VoiceVolume;

        /// <summary>Settings that belong to this PC/player (performance, volume): the global BepInEx config.</summary>
        public static void BindGlobal(ConfigFile cfg)
        {
            const string P = "ThisPC";
            FogQuality = cfg.Bind(P, "FogShellSpacing", 1.18f, "Spacing of the fog layers: smaller = smoother fog but slower (1.1 smooth … 1.3 fast).");
            WindParticles = cfg.Bind(P, "WindParticles", true, "Blowing dust, streaks and the storm front.");
            WindVolume = cfg.Bind(P, "WindVolume", 0.7f, "Volume of the wind sound (0 to 1).");
            MarketTicker = cfg.Bind(P, "MarketTicker", true, "Show the Mars market prices at the top of the screen.");
        }

        // ----------------------------------------------------------------- per-world settings

        /// <summary>File the world settings currently come from.</summary>
        public static string WorldConfigPath { get; private set; }
        static ConfigFile worldCfg;

        /// <summary>
        /// Every Mars world has its own settings file next to its save (MarsSessions/slot-N/marsmod-world.cfg),
        /// so a new save starts with the defaults and changes made in one world never leak into another.
        /// Outside a Mars world a throw-away file with the defaults is used.
        /// </summary>
        public static void UseWorld(string path)
        {
            if (path == WorldConfigPath && worldCfg != null) return;
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                worldCfg = new ConfigFile(path, true);
                BindWorld(worldCfg);
                WorldConfigPath = path;
                Plugin.Log.LogInfo("[config] world settings: " + path);
            }
            catch (Exception e) { Plugin.Log.LogError("[config] could not use " + path + ": " + e.Message); }
        }

        public static string DefaultsPath => System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "MarsMod", "cache", "no-world-defaults.cfg");

        public static ConfigEntry<int> StormSeed;

        public static void BindWorld(ConfigFile cfg)
        {
            StormSeed = cfg.Bind("World", "StormSeed", 0, "This world's storm schedule (picked once when the world is first played; the crew gets it from the host).");
            const string S = "Look.Surface";
            DustTint = Migrate(cfg.Bind(S, "DustTint", "1.00, 0.68, 0.48", "Colour multiplier of the dust layer (r, g, b). Moon: 1, 1, 1"), "1.00, 0.55, 0.33");
            GroundTint = Migrate(cfg.Bind(S, "GroundTint", "0.90, 0.60, 0.44", "Colour multiplier of the cleaned ground. Moon: 1, 1, 1"), "0.86, 0.46, 0.30");
            ShadowTint = Migrate(cfg.Bind(S, "ShadowTint", "0.60, 0.45, 0.42", "Colour of shadows. Moon: 0.74, 0.76, 0.82"), "0.52, 0.30, 0.27");
            MareDustTint = Migrate(cfg.Bind(S, "MareDustTint", "0.86, 0.56, 0.40", "Dust colour in the dark plains. Moon: 0.84, 0.86, 0.93"), "0.80, 0.42, 0.28");
            MareGroundTint = Migrate(cfg.Bind(S, "MareGroundTint", "0.94, 0.64, 0.47", "Ground colour in the dark plains. Moon: 0.98, 0.99, 1"), "0.92, 0.52, 0.35");

            const string A = "Look.Atmosphere";
            HazeOn = cfg.Bind(A, "Haze", true, "Dusty haze over distant terrain, and a butterscotch sky during the day.");
            HazeStrength = cfg.Bind(A, "HazeStrength", 0.5f, "How much the haze covers the far distance and the sky (0 = none, 0.9 = very thick).");
            HazeDayColor = cfg.Bind(A, "HazeDayColor", "0.84, 0.68, 0.54", "Colour of the daytime haze and sky (0-1 or 0-255). Night, dusk and storm colours follow from it.");
            StormsOn = cfg.Bind(A, "DustStorms", true, "Dust storms now and then (the same for the whole crew). Test with 'storm on' in the dev terminal (Ctrl+Shift+K).");
            DaySky = Migrate(cfg.Bind(A, "AmbientSky", "0.74, 0.66, 0.60", "Ambient light from above during the day. Moon: 0.61, 0.63, 0.68"), "0.80, 0.60, 0.47");
            DayEquator = Migrate(cfg.Bind(A, "AmbientHorizon", "0.70, 0.60, 0.53", "Ambient light from the horizon. Moon: 0.53, 0.545, 0.58"), "0.70, 0.50, 0.40");
            DayGround = Migrate(cfg.Bind(A, "AmbientGround", "0.50, 0.42, 0.38", "Ambient light from the ground. Moon: 0.43, 0.44, 0.47"), "0.52, 0.36, 0.30");
            FillColor = Migrate(cfg.Bind(A, "FillColor", "1.00, 0.88, 0.78", "Colour of the soft fill light. Moon: 0.65, 0.77, 1"), "1.00, 0.78, 0.62");
            NightTint = Migrate(cfg.Bind(A, "NightTint", "0.90, 0.90, 1.00", "Night colour tint. Moon: 0.82, 0.88, 1"), "1.00, 0.84, 0.74");
            SunColor = Migrate(cfg.Bind(A, "SunColor", "1.00, 0.96, 0.90", "Colour of the sunlight."), "1.00, 0.93, 0.85");
            SunIntensity = cfg.Bind(A, "SunIntensity", 1.45f, "Sunlight strength. Moon: 1.7 (Mars is further from the Sun).");
            SunAngularRadius = cfg.Bind(A, "SunSize", 0.37f, "Size of the sun disc in degrees. Moon: 0.55");
            NightLight = cfg.Bind(A, "NightLight", 1.0f, "How bright Mars nights are (0 = pitch black, 1 = like Moon nights, 1.5 = brighter).");
            PhobosSize = cfg.Bind(A, "PhobosSize", 0.75f, "Size of Phobos in the sky in degrees (it takes Earth's place). Earth on the Moon: 4.12");
            PhobosSpeed = cfg.Bind(A, "PhobosSpeed", 0.9f, "How fast Phobos crosses the sky (degrees per second). Earth on the Moon: 0.2");

            const string K = "Look.Displays";
            TintGlobe = cfg.Bind(K, "TintPlanetDisplays", true, "Make the planet on the Collector globe and the map red too.");
            VoiceVolume = cfg.Bind("Narrator", "Volume", 1.0f, "Volume of the narrator WAV files in BepInEx/MarsMod/voice (0 to 1).");
            GlobeTint = cfg.Bind(K, "GlobeTint", "1.00, 0.50, 0.32", "Multiplier for the planet on the globe and map.");
        }

        public static Color C(ConfigEntry<string> e, Color fallback)
        {
            try
            {
                var p = e.Value.Split(',');
                float f(int i) => float.Parse(p[i].Trim(), CultureInfo.InvariantCulture);
                float r = f(0), g = f(1), b = f(2), a = p.Length > 3 ? f(3) : 1f;
                // Accept both 0-1 and 0-255 colours ("0, 0, 255" is pure blue).
                if (r > 1f || g > 1f || b > 1f) { r /= 255f; g /= 255f; b /= 255f; if (a > 1f) a /= 255f; }
                return new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), Mathf.Clamp01(a));
            }
            catch { return fallback; }
        }
    }
}
