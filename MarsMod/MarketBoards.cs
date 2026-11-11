using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// The market board's screen shows the live Mars prices in the world (a 3D TextMeshPro on the board's
    /// anchor_screen marker), refreshed every 3 seconds on every PC.
    /// </summary>
    internal static class MarketBoards
    {
        static Type tmp3d;
        static readonly Dictionary<int, (GameObject go, object text)> texts = new Dictionary<int, (GameObject, object)>();
        static float next;

        public static void Frame(float now)
        {
            if (now < next) return;
            next = now + 3f;
            try
            {
                if (tmp3d == null) tmp3d = R.T("TMPro.TextMeshPro", "Unity.TextMeshPro");
                if (tmp3d == null) return;
                string body = Text();
                foreach (var b in Buildings.Data.List)
                {
                    if (b.Type != "market") continue;
                    var view = Buildings.ViewOf(b.Id);
                    if (view == null) continue;
                    if (!texts.TryGetValue(b.Id, out var t) || t.go == null || t.go.WasCollected) { t = Make(view); if (t.go == null) continue; texts[b.Id] = t; }
                    R.TrySet(t.text, "text", body);
                }
                foreach (var id in texts.Keys.ToList()) if (!Buildings.Data.List.Any(b => b.Id == id)) texts.Remove(id);
            }
            catch (Exception e) { Log.Once("board-text", "[market] board text: " + e.Message); }
        }

        static string Text()
        {
            var lines = MarsItems.All.Keys.Select(id => $"{MarsItems.Name(id),-14} ${Market.Price(id),6:0.0} {Market.Arrow(id)}");
            return "<b><color=#E8743B>MARS MARKET</color></b>\n" + string.Join("\n", lines);
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var f = Find(t.GetChild(i), name); if (f != null) return f; }
            return null;
        }

        static (GameObject, object) Make(GameObject view)
        {
            var anchor = Find(view.transform, "anchor_screen");
            if (anchor == null) return (null, null);
            var go = new GameObject(); go.name = "Mars market screen text";
            go.transform.SetParent(view.transform, false);
            var outward = anchor.up;
            go.transform.position = anchor.position + outward * 0.02f;
            go.transform.rotation = Quaternion.LookRotation(-outward, view.transform.up);
            var t = R.Add(go, tmp3d);
            if (MarsUi.font != null) R.TrySet(t, "font", MarsUi.font);
            R.TrySet(t, "enableAutoSizing", true);
            R.TrySet(t, "fontSizeMin", 0.05f);
            R.TrySet(t, "fontSizeMax", 3f);
            R.TrySet(t, "richText", true);
            R.TrySet(t, "enableWordWrapping", false);
            R.TrySet(t, "color", new Color(0.75f, 0.95f, 1f, 1f));
            R.TrySet(t, "alignment", 514);   // centre
            var rtc = go.GetComponent(Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>());
            if (rtc != null) rtc.Cast<RectTransform>().sizeDelta = new Vector2(1.9f, 1.05f);
            return (go, t);
        }

        public static void Reset() => texts.Clear();
    }
}
