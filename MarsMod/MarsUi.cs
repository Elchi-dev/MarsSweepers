using System;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace MarsMod
{
    /// <summary>Small screen-space UI (canvas + text boxes) built from Unity UI types by reflection.</summary>
    internal static class MarsUi
    {
        static GameObject root;
        internal static object font;
        static Type tmpT, imageT;

        public sealed class Box
        {
            public GameObject Go; public object Text; object bg;
            string last;
            public void Set(string text)
            {
                if (Go == null) return;
                bool show = !string.IsNullOrEmpty(text);
                if (Go.activeSelf != show) Go.SetActive(show);
                if (!show || text == last) return;
                last = text;
                R.TrySet(Text, "text", text);
            }
        }

        static bool Ensure()
        {
            if (root != null) return true;
            try
            {
                var canvasT = R.T("UnityEngine.Canvas", "UnityEngine.UIModule");
                var scalerT = R.T("UnityEngine.UI.CanvasScaler", "UnityEngine.UI");
                imageT = R.T("UnityEngine.UI.Image", "UnityEngine.UI");
                tmpT = R.T("TMPro.TextMeshProUGUI", "Unity.TextMeshPro");
                root = new GameObject(); root.name = "Mars HUD";
                UnityEngine.Object.DontDestroyOnLoad(root);
                var canvas = R.Add(root, canvasT);
                R.Set(canvas, "renderMode", 0);
                R.Set(canvas, "sortingOrder", 31000);
                var scaler = R.Add(root, scalerT);
                R.TrySet(scaler, "uiScaleMode", 1);
                R.TrySet(scaler, "referenceResolution", new Vector2(1920, 1080));
                R.TrySet(scaler, "matchWidthOrHeight", 0.5f);
                var any = Resources.FindObjectsOfTypeAll(Il2CppType.From(tmpT));
                for (int i = 0; i < any.Length && font == null; i++) font = R.Get(Activator.CreateInstance(tmpT, any[i].Pointer), "font");
                return true;
            }
            catch (Exception e) { Plugin.Log.LogError("[hud] " + e); root = null; return false; }
        }

        /// <summary>A game menu (build menu, upgrade tree, map, pause) is open: our HUD steps aside.</summary>
        public static bool GameMenuOpen
        {
            get
            {
                float now = Time.realtimeSinceStartup;
                if (now - menuCheckedAt < 0.2f) return menuOpen;
                menuCheckedAt = now;
                menuOpen = false;
                try { var w = Commands.FindWalker(); menuOpen = w != null && w.MenuBlocked; } catch { }
                return menuOpen;
            }
        }
        static bool menuOpen; static float menuCheckedAt = -1f;

        /// <summary>A text box anchored at (ax, ay) of the screen (0..1), pivoted there, with a dark panel behind.</summary>
        public static Box Make(string name, float ax, float ay, float width, float height, float size, int align, bool panel = true)
        {
            var b = new Box();
            if (!Ensure()) return b;
            var go = new GameObject(); go.name = name;
            var rt = go.AddComponent(Il2CppType.Of<RectTransform>()).Cast<RectTransform>();
            rt.SetParent(root.transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(ax, ay);
            rt.pivot = new Vector2(ax, ay);
            rt.sizeDelta = new Vector2(width, height);
            if (panel)
            {
                var img = R.Add(go, imageT);
                R.Set(img, "color", new Color(0.07f, 0.04f, 0.035f, 0.82f));
                R.TrySet(img, "raycastTarget", false);
            }
            var tgo = new GameObject(); tgo.name = "text";
            var trt = tgo.AddComponent(Il2CppType.Of<RectTransform>()).Cast<RectTransform>();
            trt.SetParent(rt, false);
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(16, 10); trt.offsetMax = new Vector2(-16, -10);
            var t = R.Add(tgo, tmpT);
            if (font != null) R.TrySet(t, "font", font);
            R.TrySet(t, "fontSize", size);
            R.TrySet(t, "richText", true);
            R.TrySet(t, "color", new Color(1f, 0.9f, 0.8f, 1f));
            R.TrySet(t, "alignment", align);
            R.TrySet(t, "raycastTarget", false);
            go.SetActive(false);
            b.Go = go; b.Text = t;
            return b;
        }
    }
}
