using System;
using System.IO;
using System.Linq;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MoonCleaners;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// The pause menu logo on Mars, "corrected" by the Martian Surface Department: the word MOON turns
    /// dusty orange and gets a fat marker line through it, "MARS" is scribbled above it, and a small
    /// footnote admits it's the same toothbrush. A logo-mutilation gag in the spirit of Brøderbund's
    /// sliced "ø" or Bubsy's "purr-sonality". Only the copy shown on Mars is changed; the Moon keeps its logo.
    /// </summary>
    internal static class Logo
    {
        static Texture original;
        static Texture2D mars;
        static object rawImage;      // UnityEngine.UI.RawImage
        static GameObject overlay;
        static Rect moonBox;         // the word MOON, in 0..1 texture coordinates
        static bool failed;
        static float next, nextMenuScan;

        public static void Tick(float now, bool onMars)
        {
            if (failed || now < next || (!onMars && mars == null)) return;
            // Cheap once the logo is found: check often so the swap is never visible.
            next = now + (rawImage != null ? 0.1f : 1f);
            try
            {
                // Prepare the Mars logo as soon as we're on Mars, from the pause menu's own logo texture,
                // and hand it to the pause menu before it's ever opened.
                if (now >= nextMenuScan) foreach (var pm in Look.All<MoonPauseMenu>())
                {
                    if (!Look.InScene(pm.gameObject)) continue;
                    var tex = pm.logo;
                    if (onMars)
                    {
                        if (mars == null && tex != null) Build(tex);
                        if (mars != null && (tex == null || tex.Pointer != mars.Pointer)) pm.logo = mars;
                    }
                    else if (mars != null && original != null && tex != null && tex.Pointer == mars.Pointer) pm.logo = original.TryCast<Texture2D>();
                }
                if (now >= nextMenuScan) nextMenuScan = now + 1f;
                if (rawImage == null || ((Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)rawImage).WasCollected) Find();
                if (rawImage == null) return;
                var current = R.Get(rawImage, "texture") as Texture;
                if (onMars)
                {
                    if (mars == null) Build(current);
                    if (mars != null && (current == null || current.Pointer != mars.Pointer)) R.Set(rawImage, "texture", mars);
                    if (overlay == null && mars != null) Scribble();
                    if (overlay != null && !overlay.activeSelf) overlay.SetActive(true);
                }
                else
                {
                    if (original != null && current != null && mars != null && current.Pointer == mars.Pointer) R.Set(rawImage, "texture", original);
                    if (overlay != null && overlay.activeSelf) overlay.SetActive(false);
                }
            }
            catch (Exception e) { failed = true; Log.Once("logo-err", "Mars logo failed: " + (e.InnerException ?? e).Message); }
        }

        static void Find()
        {
            rawImage = null;
            var t = R.T("UnityEngine.UI.RawImage", "UnityEngine.UI");
            if (t == null) return;
            var all = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.From(t));
            for (int i = 0; i < all.Length; i++)
            {
                var c = new Component(all[i].Pointer);
                if (c.gameObject.name != "Moon Sweepers logo" || !Look.InScene(c.gameObject)) continue;
                if (c.transform.parent == null || c.transform.parent.name != "Home") continue; // the pause menu one
                rawImage = Activator.CreateInstance(t, all[i].Pointer);
                overlay = null;
                return;
            }
        }

        static void Build(Texture src)
        {
            if (src == null) return;
            original = src;
            int w = src.width, h = src.height;
            // The logo is not CPU-readable: copy it on the GPU first.
            var rt = RenderTexture.GetTemporary(w, h, 0);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var ctor = typeof(Texture2D).GetConstructor(new[] { typeof(int), typeof(int), typeof(TextureFormat), typeof(bool) });
            var tex = (Texture2D)ctor.Invoke(new object[] { w, h, TextureFormat.RGBA32, false });
            var r = default(Rect); r.x = 0; r.y = 0; r.width = w; r.height = h;
            tex.ReadPixels(r, 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            SavePng(tex, "PauseLogo.png");

            var px = tex.GetPixels32();
            var p = new Color32[px.Length];
            for (int i = 0; i < p.Length; i++) p[i] = px[i];
            moonBox = FindMoonWord(p, w, h);
            Plugin.Log.LogInfo($"[logo] {w}x{h}, MOON at x {moonBox.x:0.00}-{moonBox.xMax:0.00}, y {moonBox.y:0.00}-{moonBox.yMax:0.00}");

            // 1. MOON goes dusty orange, as if Mars dust settled on it.
            int x0 = (int)(moonBox.x * w), x1 = (int)(moonBox.xMax * w), y0 = (int)(moonBox.y * h), y1 = (int)(moonBox.yMax * h);
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    var c = p[y * w + x];
                    if (c.a < 8) continue;
                    float lum = (c.r * 0.3f + c.g * 0.59f + c.b * 0.11f) / 255f;
                    float k = 0.75f;
                    p[y * w + x] = S.C32(Mix(c.r, 232 * (0.35f + lum), k), Mix(c.g, 116 * (0.35f + lum), k), Mix(c.b, 59 * (0.35f + lum), k), c.a);
                }
            // 2. A fat red marker line through it, corner to corner, slightly wobbly like a hand did it.
            float bw = x1 - x0, bh = y1 - y0, thick = Mathf.Max(3f, bh * 0.13f);
            var a = new Vector2(x0 - bw * 0.04f, y0 + bh * 0.12f);
            var b = new Vector2(x1 + bw * 0.04f, y1 - bh * 0.05f);
            int minY = Mathf.Max(0, y0 - (int)thick * 2), maxY = Mathf.Min(h, y1 + (int)thick * 2);
            int minX = Mathf.Max(0, x0 - (int)(bw * 0.1f)), maxX = Mathf.Min(w, x1 + (int)(bw * 0.1f));
            for (int y = minY; y < maxY; y++)
                for (int x = minX; x < maxX; x++)
                {
                    var q = new Vector2(x, y);
                    var ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / ab.sqrMagnitude);
                    var closest = a + ab * t + new Vector2(0, Mathf.Sin(t * 9f) * thick * 0.18f);
                    float d = Vector2.Distance(q, closest);
                    float half = thick * (0.8f + 0.2f * Mathf.Sin(t * 23f)) * 0.5f;
                    float cover = Mathf.Clamp01(half + 0.75f - d);   // anti-aliased edge
                    if (cover <= 0f) continue;
                    var c = p[y * w + x];
                    byte ink = (byte)(cover * 240);
                    p[y * w + x] = S.C32(Mix(c.r, 214, cover), Mix(c.g, 52, cover), Mix(c.b, 30, cover), (byte)Mathf.Max(c.a, ink));
                }
            tex.SetPixels32(new Il2CppStructArray<Color32>(p));
            tex.Apply();
            tex.name = "Mars Sweepers logo";
            UnityEngine.Object.DontDestroyOnLoad(tex);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            SavePng(tex, "PauseLogoMars.png");
            mars = tex;
        }

        static byte Mix(byte from, float to, float k) => (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(from, to, k)), 0, 255);

        /// <summary>Find the word MOON: the top text line if the logo has two lines, else the left word.</summary>
        static Rect FindMoonWord(Color32[] p, int w, int h)
        {
            var rows = new int[h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (p[y * w + x].a > 128) rows[y]++;
            // Bands of rows with ink (y = 0 is the bottom of the texture).
            var bands = new System.Collections.Generic.List<(int a, int b)>();
            int start = -1, minInk = Math.Max(1, w / 200);
            for (int y = 0; y <= h; y++)
            {
                bool ink = y < h && rows[y] >= minInk;
                if (ink && start < 0) start = y;
                if (!ink && start >= 0) { if (y - start > h / 20) bands.Add((start, y)); start = -1; }
            }
            int top0, top1;
            if (bands.Count >= 2) { var t = bands[bands.Count - 1]; top0 = t.a; top1 = t.b; }      // top line
            else if (bands.Count == 1) { top0 = bands[0].a; top1 = bands[0].b; }
            else return new Rect(0.05f, 0.5f, 0.45f, 0.45f);

            var cols = new int[w];
            for (int y = top0; y < top1; y++) for (int x = 0; x < w; x++) if (p[y * w + x].a > 128) cols[x]++;
            int left = 0; while (left < w && cols[left] == 0) left++;
            int right = w - 1; while (right > left && cols[right] == 0) right--;
            if (bands.Count == 1)
            {
                // One line: MOON is the part left of the widest gap.
                int gapStart = -1, bestA = -1, bestLen = 0;
                for (int x = left; x <= right; x++)
                {
                    if (cols[x] == 0 && gapStart < 0) gapStart = x;
                    if (cols[x] != 0 && gapStart >= 0) { if (x - gapStart > bestLen) { bestLen = x - gapStart; bestA = gapStart; } gapStart = -1; }
                }
                if (bestA > 0) right = bestA;
            }
            return new Rect(left / (float)w, top0 / (float)h, (right - left + 1) / (float)w, (top1 - top0) / (float)h);
        }

        /// <summary>"MARS" in orange marker above the crossed-out word, plus a footnote.</summary>
        static void Scribble()
        {
            var tmpT = R.T("TMPro.TextMeshProUGUI", "Unity.TextMeshPro");
            var comp = (Component)Activator.CreateInstance(typeof(Component), ((Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)rawImage).Pointer);
            var parent = comp.transform;
            overlay = new GameObject();
            overlay.name = "Mars correction";
            var ort = overlay.AddComponent(Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>()).Cast<RectTransform>();
            ort.SetParent(parent, false);
            ort.anchorMin = Vector2.zero; ort.anchorMax = Vector2.one; ort.offsetMin = Vector2.zero; ort.offsetMax = Vector2.zero;

            var parentRt = parent.Cast<RectTransform>();
            float pw = parentRt.rect.width, ph = parentRt.rect.height;

            Label(tmpT, ort, "MARS!", new Vector2(moonBox.center.x, Mathf.Min(1.02f, moonBox.yMax + 0.06f)), moonBox.width * 1.1f, moonBox.height * 0.75f,
                  new Color(0.91f, 0.45f, 0.23f), 9f, true, pw, ph);
            Label(tmpT, ort, "*same toothbrush, new planet", new Vector2(0.78f, -0.06f), 0.6f, 0.12f,
                  new Color(0.95f, 0.62f, 0.42f), -3f, false, pw, ph);
        }

        static void Label(Type tmpT, RectTransform parent, string text, Vector2 centre, float wFrac, float hFrac, Color colour, float tilt, bool bold, float pw, float ph)
        {
            var go = new GameObject();
            go.name = "Mars scribble";
            var rt = go.AddComponent(Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>()).Cast<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = centre; rt.anchorMax = centre;
            rt.sizeDelta = new Vector2(pw * wFrac, ph * hFrac);
            rt.localRotation = Quaternion.Euler(0f, 0f, tilt);
            var tmp = R.Add(go, tmpT);
            var any = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.From(tmpT));
            for (int i = 0; i < any.Length; i++)
            {
                var font = R.Get(Activator.CreateInstance(tmpT, any[i].Pointer), "font");
                if (font != null) { R.TrySet(tmp, "font", font); break; }
            }
            R.TrySet(tmp, "enableAutoSizing", true);
            R.TrySet(tmp, "fontSizeMin", 8f);
            R.TrySet(tmp, "fontSizeMax", 400f);
            R.TrySet(tmp, "alignment", 514);          // centre
            R.TrySet(tmp, "raycastTarget", false);
            R.TrySet(tmp, "color", colour);
            R.TrySet(tmp, "text", bold ? "<b><i>" + text + "</i></b>" : "<i>" + text + "</i>");
        }

        static void SavePng(Texture2D tex, string name)
        {
            try
            {
                var conv = R.T("UnityEngine.ImageConversion", "UnityEngine.ImageConversionModule");
                var m = conv?.GetMethod("EncodeToPNG", new[] { typeof(Texture2D) });
                if (m == null) return;
                var bytes = m.Invoke(null, new object[] { tex }) as Il2CppStructArray<byte>;
                if (bytes == null) return;
                string dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "MarsMod", "originals");
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, name), bytes.ToArray());
            }
            catch { }
        }
    }
}
