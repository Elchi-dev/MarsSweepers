using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using Il2CppInterop.Runtime;
using MoonCleaners;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// In-game dev terminal. Ctrl + Shift + K opens and closes it (Esc closes too).
    /// While it is open the game ignores the keyboard, like when typing in the chat.
    /// "help" lists the commands. Mod log messages show up here as well.
    /// </summary>
    internal static class Terminal
    {
        const int MaxLines = 3000, ShownLines = 22;
        static readonly List<string> lines = new List<string>();
        static readonly List<string> history = new List<string>();
        static readonly Queue<char> typed = new Queue<char>();
        static string input = "";
        static int caret;               // cursor position inside input
        static int historyIndex = -1, scroll;
        static bool open, dirty = true, charHookTried, charHooked;
        static float blinkAt;
        static readonly Dictionary<KeyCode, float> repeatAt = new Dictionary<KeyCode, float>();

        static GameObject root;
        static object tmp;              // TextMeshProUGUI
        static IntPtr hookedKeyboard;

        public static bool Open => open;

        // ------------------------------------------------------------------ log capture

        class Listener : ILogListener
        {
            public LogLevel LogLevelFilter => LogLevel.All;
            public void LogEvent(object sender, LogEventArgs e)
            {
                string src = e.Source?.SourceName ?? "";
                if (src != "Mars Sweepers" && src != "MarsProbe") return;
                if (e.Level == LogLevel.Debug) return;
                string data = e.Data?.ToString() ?? "";
                if (data.StartsWith("[terminal]")) return; // the terminal's own echo for the log file
                string col = e.Level <= LogLevel.Error ? "#FF6B5A" : e.Level == LogLevel.Warning ? "#FFC266" : "#9A8F86";
                Add($"<color={col}>{Esc(data)}</color>");
            }
            public void Dispose() { }
        }

        public static void Init()
        {
            BepInEx.Logging.Logger.Listeners.Add(new Listener());
            Add("<color=#E8743B><b>MARS DEV TERMINAL</b></color>  type <b>help</b> for commands, Esc or Ctrl+Shift+K to close");
        }

        public static void Add(string line)
        {
            lock (lines)
            {
                foreach (var l in (line ?? "").Split('\n')) { lines.Add(l); if (scroll > 0) scroll++; }
                while (lines.Count > MaxLines) lines.RemoveAt(0);
                scroll = Math.Min(scroll, Math.Max(0, lines.Count - ShownLines));
            }
            dirty = true;
        }

        static string Esc(string s) => (s ?? "").Replace("<", "<​");

        static void Out(string s) => Add("<color=#F2E6DA>" + Esc(s) + "</color>");

        // ------------------------------------------------------------------ per frame

        public static void Frame(float now)
        {
            var kb = UnityInput.Current;
            if (kb == null) return;
            bool ctrl = kb.GetKey(KeyCode.LeftControl) || kb.GetKey(KeyCode.RightControl);
            bool shift = kb.GetKey(KeyCode.LeftShift) || kb.GetKey(KeyCode.RightShift);
            if (ctrl && shift && kb.GetKeyDown(KeyCode.K)) { SetOpen(!open); return; }
            if (!open) { lock (typed) typed.Clear(); return; }

            KeepGameInputBlocked();
            if (kb.GetKeyDown(KeyCode.Escape)) { SetOpen(false); return; }

            // Text: from the keyboard's character stream (respects your keyboard layout), else from key polling.
            lock (typed)
                while (typed.Count > 0) Insert(typed.Dequeue().ToString());
            if (!charHooked && !ctrl) PollLetters(kb, shift);
            caret = Math.Max(0, Math.Min(caret, input.Length));

            // Editing keys (held keys repeat).
            if (Repeat(kb, KeyCode.Backspace, now))
            {
                if (ctrl) { int w = WordLeft(); input = input.Remove(w, caret - w); caret = w; }
                else if (caret > 0) { input = input.Remove(caret - 1, 1); caret--; }
                dirty = true;
            }
            if (Repeat(kb, KeyCode.Delete, now) && caret < input.Length) { input = input.Remove(caret, 1); dirty = true; }
            if (Repeat(kb, KeyCode.LeftArrow, now)) { caret = ctrl ? WordLeft() : Math.Max(0, caret - 1); dirty = true; Blink(now); }
            if (Repeat(kb, KeyCode.RightArrow, now)) { caret = ctrl ? WordRight() : Math.Min(input.Length, caret + 1); dirty = true; Blink(now); }
            if (kb.GetKeyDown(KeyCode.Home)) { caret = 0; dirty = true; Blink(now); }
            if (kb.GetKeyDown(KeyCode.End)) { caret = input.Length; dirty = true; Blink(now); }

            // Clipboard: Ctrl+V pastes, Ctrl+C copies the line (or the last answers if the line is empty),
            // Ctrl+Shift+C copies everything in the terminal, Ctrl+X cuts the line.
            if (ctrl && kb.GetKeyDown(KeyCode.V))
            {
                string clip = Clipboard;
                if (!string.IsNullOrEmpty(clip)) Insert(clip.Replace("\r", "").Replace('\n', ' ').Replace('\t', ' '));
            }
            if (ctrl && kb.GetKeyDown(KeyCode.C))
            {
                if (shift) { Clipboard = PlainLog(int.MaxValue); Add("<color=#9A8F86>(whole terminal copied)</color>"); }
                else if (input.Length > 0) { Clipboard = input; Add("<color=#9A8F86>(line copied)</color>"); }
                else { Clipboard = PlainLog(LastAnswerLines()); Add("<color=#9A8F86>(last answer copied)</color>"); }
            }
            if (ctrl && kb.GetKeyDown(KeyCode.X) && input.Length > 0) { Clipboard = input; input = ""; caret = 0; dirty = true; }
            if (ctrl && kb.GetKeyDown(KeyCode.L)) Clear();

            if (kb.GetKeyDown(KeyCode.Return) || kb.GetKeyDown(KeyCode.KeypadEnter)) Submit();
            if (kb.GetKeyDown(KeyCode.UpArrow) && history.Count > 0)
            {
                historyIndex = historyIndex < 0 ? history.Count - 1 : Math.Max(0, historyIndex - 1);
                input = history[historyIndex]; caret = input.Length; dirty = true;
            }
            if (kb.GetKeyDown(KeyCode.DownArrow) && historyIndex >= 0)
            {
                historyIndex++;
                input = historyIndex < history.Count ? history[historyIndex] : "";
                if (historyIndex >= history.Count) historyIndex = -1;
                caret = input.Length; dirty = true;
            }
            if (kb.GetKeyDown(KeyCode.PageUp)) { scroll = Math.Min(scroll + 10, Math.Max(0, lines.Count - ShownLines)); dirty = true; }
            if (kb.GetKeyDown(KeyCode.PageDown)) { scroll = Math.Max(0, scroll - 10); dirty = true; }
            if (ctrl && kb.GetKeyDown(KeyCode.PageUp)) { scroll = Math.Max(0, lines.Count - ShownLines); dirty = true; }
            if (ctrl && kb.GetKeyDown(KeyCode.PageDown)) { scroll = 0; dirty = true; }
            float wheel = 0f;
            try { wheel = kb.mouseScrollDelta.y; } catch { }
            if (wheel != 0f)
            {
                int step = Math.Max(1, (int)Math.Round(Math.Abs(wheel) * 3f));
                scroll = wheel > 0 ? Math.Min(scroll + step, Math.Max(0, lines.Count - ShownLines)) : Math.Max(0, scroll - step);
                dirty = true;
            }

            if (now > blinkAt) { blinkAt = now + 0.5f; dirty = true; }
            if (dirty) Render(now);
        }

        static void Insert(string text)
        {
            caret = Math.Max(0, Math.Min(caret, input.Length));
            input = input.Insert(caret, text);
            caret += text.Length;
            dirty = true;
        }

        static bool Repeat(IInputSystem kb, KeyCode k, float now)
        {
            if (kb.GetKeyDown(k)) { repeatAt[k] = now + 0.42f; return true; }
            if (kb.GetKey(k) && repeatAt.TryGetValue(k, out float at) && now >= at) { repeatAt[k] = now + 0.035f; return true; }
            return false;
        }

        static void Blink(float now) => blinkAt = now + 0.5f; // keep the cursor visible while moving it

        static int WordLeft()
        {
            int i = Math.Min(caret, input.Length);
            while (i > 0 && input[i - 1] == ' ') i--;
            while (i > 0 && input[i - 1] != ' ') i--;
            return i;
        }

        static int WordRight()
        {
            int i = caret;
            while (i < input.Length && input[i] == ' ') i++;
            while (i < input.Length && input[i] != ' ') i++;
            return i;
        }

        // ------------------------------------------------------------------ clipboard

        static System.Reflection.PropertyInfo clipProp;
        static bool clipLooked;

        static string Clipboard
        {
            get
            {
                try { return ClipProp()?.GetValue(null) as string ?? ""; }
                catch (Exception e) { Log.Once("clip", "[terminal] clipboard unavailable: " + (e.InnerException ?? e).Message); return ""; }
            }
            set
            {
                try { ClipProp()?.SetValue(null, value ?? ""); }
                catch (Exception e) { Log.Once("clip", "[terminal] clipboard unavailable: " + (e.InnerException ?? e).Message); }
            }
        }

        static System.Reflection.PropertyInfo ClipProp()
        {
            if (!clipLooked)
            {
                clipLooked = true;
                clipProp = R.T("UnityEngine.GUIUtility", "UnityEngine.IMGUIModule")?.GetProperty("systemCopyBuffer");
                if (clipProp == null) Plugin.Log.LogWarning("[terminal] no clipboard access in this game build");
            }
            return clipProp;
        }

        static readonly System.Text.RegularExpressions.Regex Tags = new System.Text.RegularExpressions.Regex("<[^>]+>");

        static string PlainLog(int lastLines)
        {
            lock (lines)
            {
                int start = Math.Max(0, lines.Count - lastLines);
                return string.Join("\r\n", lines.Skip(start).Select(l => Tags.Replace(l, "").Replace("<\u200B", "<")));
            }
        }

        /// <summary>Number of lines from the last command prompt to the end.</summary>
        static int LastAnswerLines()
        {
            lock (lines)
                for (int i = lines.Count - 1; i >= 0; i--)
                    if (lines[i].Contains("mars></color> ")) return lines.Count - i;
            return 20;
        }

        static void SetOpen(bool value)
        {
            if (value)
            {
                if (Guard.Off("terminal-ui")) { Plugin.Log.LogWarning("The dev terminal crashed the game once and is switched off. Reinstall with MarsSetup to try again."); return; }
                Guard.Begin("terminal-ui");
                bool ok = EnsureUi();
                Guard.End("terminal-ui");
                if (!ok) { Plugin.Log.LogWarning("The dev terminal could not be built (see log)."); return; }
            }
            open = value;
            try { root.SetActive(open); } catch { }
            if (open) { HookChars(); scroll = 0; dirty = true; lock (typed) typed.Clear(); }
            else ReleaseGameInput();
        }

        // ------------------------------------------------------------------ game input

        static void KeepGameInputBlocked()
        {
            try { MoonInput.Typing = true; } catch { }
            try { foreach (var w in Look.All<MoonWalker>()) if (Look.InScene(w.gameObject)) w.UIBlocked = true; } catch { }
        }

        static void ReleaseGameInput()
        {
            try { MoonInput.Typing = false; } catch { }
            try { foreach (var w in Look.All<MoonWalker>()) if (Look.InScene(w.gameObject)) w.UIBlocked = false; } catch { }
        }

        // ------------------------------------------------------------------ typing

        static void OnChar(char c)
        {
            if (!open || c < 32 || c == 127) return;
            lock (typed) typed.Enqueue(c);
        }

        /// <summary>Subscribe to the Input System keyboard's text stream (handles QWERTZ, AZERTY, shift…).</summary>
        static void HookChars()
        {
            try
            {
                var kbType = R.T("UnityEngine.InputSystem.Keyboard", "Unity.InputSystem");
                var current = kbType?.GetProperty("current")?.GetValue(null) as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
                if (current == null) { if (!charHookTried) Plugin.Log.LogInfo("[terminal] no Input System keyboard, using key polling"); charHookTried = true; return; }
                if (current.Pointer == hookedKeyboard) return;
                var add = kbType.GetMethod("add_onTextInput");
                var delType = add.GetParameters()[0].ParameterType;
                var convert = typeof(DelegateSupport).GetMethod("ConvertDelegate").MakeGenericMethod(delType);
                var del = convert.Invoke(null, new object[] { new Action<char>(OnChar) });
                add.Invoke(current, new[] { del });
                hookedKeyboard = current.Pointer;
                charHooked = true;
                Plugin.Log.LogInfo("[terminal] keyboard text input connected");
            }
            catch (Exception e)
            {
                if (!charHookTried) Plugin.Log.LogInfo("[terminal] keyboard text input unavailable, using key polling: " + (e.InnerException ?? e).Message);
                charHookTried = true;
            }
        }

        static void PollLetters(IInputSystem kb, bool shift)
        {
            for (KeyCode k = KeyCode.A; k <= KeyCode.Z; k++)
                if (kb.GetKeyDown(k)) { char c = (char)('a' + (k - KeyCode.A)); Insert((shift ? char.ToUpperInvariant(c) : c).ToString()); }
            for (KeyCode k = KeyCode.Alpha0; k <= KeyCode.Alpha9; k++)
                if (kb.GetKeyDown(k)) Insert(((char)('0' + (k - KeyCode.Alpha0))).ToString());
            for (KeyCode k = KeyCode.Keypad0; k <= KeyCode.Keypad9; k++)
                if (kb.GetKeyDown(k)) Insert(((char)('0' + (k - KeyCode.Keypad0))).ToString());
            if (kb.GetKeyDown(KeyCode.Space)) Insert(" ");
            if (kb.GetKeyDown(KeyCode.Period) || kb.GetKeyDown(KeyCode.KeypadPeriod)) Insert(".");
            if (kb.GetKeyDown(KeyCode.Minus) || kb.GetKeyDown(KeyCode.KeypadMinus)) Insert("-");
            if (kb.GetKeyDown(KeyCode.Comma)) Insert(",");
        }

        // ------------------------------------------------------------------ UI

        static bool EnsureUi()
        {
            if (root != null) return true;
            try
            {
                var canvasT = R.T("UnityEngine.Canvas", "UnityEngine.UIModule");
                var scalerT = R.T("UnityEngine.UI.CanvasScaler", "UnityEngine.UI");
                var imageT = R.T("UnityEngine.UI.Image", "UnityEngine.UI");
                var tmpT = R.T("TMPro.TextMeshProUGUI", "Unity.TextMeshPro");
                if (canvasT == null || imageT == null || tmpT == null) { Plugin.Log.LogWarning("[terminal] UI types not found"); return false; }

                root = new GameObject();
                root.name = "Mars dev terminal";
                UnityEngine.Object.DontDestroyOnLoad(root);
                var canvas = R.Add(root, canvasT);
                R.Set(canvas, "renderMode", 0);          // screen space overlay
                R.Set(canvas, "sortingOrder", 32000);
                if (scalerT != null)
                {
                    var scaler = R.Add(root, scalerT);
                    R.TrySet(scaler, "uiScaleMode", 1);  // scale with screen size
                    R.TrySet(scaler, "referenceResolution", new Vector2(1920, 1080));
                    R.TrySet(scaler, "matchWidthOrHeight", 0.5f);
                }

                var panel = Child(root.transform, "Panel", new Vector2(0, 0.45f), new Vector2(1, 1), new Vector2(24, 0), new Vector2(-24, -24));
                var img = R.Add(panel, imageT);
                R.Set(img, "color", new Color(0.07f, 0.035f, 0.03f, 0.9f));
                R.TrySet(img, "raycastTarget", false);

                var textGo = Child(panel.transform, "Text", Vector2.zero, Vector2.one, new Vector2(18, 12), new Vector2(-18, -12));
                tmp = R.Add(textGo, tmpT);
                // Borrow the game's font so text looks like the rest of the HUD.
                var any = Resources.FindObjectsOfTypeAll(Il2CppType.From(tmpT));
                for (int i = 0; i < any.Length; i++)
                {
                    if (any[i] == null || any[i].Pointer == ((Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)tmp).Pointer) continue;
                    var font = R.Get(Activator.CreateInstance(tmpT, any[i].Pointer), "font");
                    if (font != null) { R.TrySet(tmp, "font", font); break; }
                }
                R.TrySet(tmp, "fontSize", 21f);
                R.TrySet(tmp, "richText", true);
                R.TrySet(tmp, "color", new Color(1f, 0.88f, 0.76f, 1f));
                R.TrySet(tmp, "alignment", 1025);        // bottom left
                R.TrySet(tmp, "raycastTarget", false);
                root.SetActive(false);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("[terminal] UI failed: " + e);
                try { if (root != null) UnityEngine.Object.Destroy(root); } catch { }
                root = null;
                return false;
            }
        }

        static GameObject Child(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var go = new GameObject();
            go.name = name;
            var rt = go.AddComponent(Il2CppType.Of<RectTransform>()).Cast<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
            return go;
        }

        static void Render(float now)
        {
            dirty = false;
            if (tmp == null) return;
            var sb = new StringBuilder();
            lock (lines)
            {
                int end = Math.Max(0, lines.Count - scroll);
                int start = Math.Max(0, end - ShownLines);
                for (int i = start; i < end; i++) sb.Append(lines[i]).Append('\n');
            }
            if (scroll > 0) sb.Append($"<color=#9A8F86>… {scroll} newer lines below (wheel / Page Down, Ctrl+Page Down jumps to the end)</color>\n");
            bool cursor = now < blinkAt - 0.25f || ((int)(now * 2)) % 2 == 0;
            int c = Math.Max(0, Math.Min(caret, input.Length));
            // A thin bar between characters; when blinking off it stays in place but invisible, so text doesn't jump.
            string bar = cursor ? "<color=#E8743B>|</color>" : "<alpha=#00>|<alpha=#FF>";
            sb.Append("<color=#E8743B>mars></color> ").Append(Esc(input.Substring(0, c))).Append(bar).Append(Esc(input.Substring(c)));
            try { R.Set(tmp, "text", sb.ToString()); } catch { }
        }

        // ------------------------------------------------------------------ commands

        static void Submit()
        {
            string cmd = input.Trim();
            input = ""; caret = 0; historyIndex = -1; scroll = 0; dirty = true;
            if (cmd.Length == 0) return;
            history.Add(cmd);
            if (history.Count > 50) history.RemoveAt(0);
            Add("<color=#E8743B>mars></color> " + Esc(cmd));
            try { Commands.Run(cmd, l => { Out(l); Plugin.Log.LogInfo("[terminal]   " + l); }); }
            catch (Exception e) { Add("<color=#FF6B5A>" + Esc((e.InnerException ?? e).Message) + "</color>"); }
        }

        public static void Clear() { lock (lines) lines.Clear(); dirty = true; }
        public static void CloseNow() { if (open) SetOpen(false); }
    }
}
