using System;
using System.IO;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// Easter egg: on Mars, the station's "only one coffee" poster becomes the Subnautica "Keep Calm" cat.
    /// Art © Unknown Worlds Entertainment, used as a free, non-commercial fan tribute under their fan content
    /// policy; this mod is not affiliated with or endorsed by Unknown Worlds.
    /// </summary>
    internal static class Poster
    {
        const string Target = "collectorposteronlyonecoffee";
        static Texture2D cat;
        static Texture original;
        static bool failed, applied;

        static float next;

        public static void Tick(bool onMars)
        {
            if (failed || (!onMars && !applied)) return;
            if (Time.realtimeSinceStartup < next) return;
            next = Time.realtimeSinceStartup + 4f;
            try
            {
                if (onMars && cat == null) cat = Load();
                foreach (var m in Look.All<Material>())
                {
                    if (m == null || m.name != Target || !m.HasProperty("_BaseMap")) continue;
                    var cur = m.GetTexture("_BaseMap");
                    if (onMars && cat != null)
                    {
                        if (cur != null && cur.Pointer == cat.Pointer) continue;
                        original = cur;
                        m.SetTexture("_BaseMap", cat);
                        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", cat);
                        if (!applied) Plugin.Log.LogMessage("Keep calm. (poster)");
                        applied = true;
                    }
                    else if (!onMars && applied && original != null && cur != null && cat != null && cur.Pointer == cat.Pointer)
                    {
                        m.SetTexture("_BaseMap", original);
                        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", original);
                    }
                }
                if (!onMars) applied = false;
            }
            catch (Exception e) { failed = true; Log.Once("poster-err", "Poster: " + (e.InnerException ?? e).Message); }
        }

        static Texture2D Load()
        {
            using (var s = typeof(Poster).Assembly.GetManifestResourceStream("poster_keep_calm.png"))
            {
                if (s == null) { failed = true; return null; }
                var ms = new MemoryStream(); s.CopyTo(ms);
                return Png.Load(ms.ToArray(), "poster_keep_calm (Mars mod)");
            }
        }
    }
}
