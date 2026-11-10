using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using MoonCleaners;

namespace MarsMod
{
    /// <summary>
    /// Mars wording, done WITHOUT hooking game functions (hooking the text functions crashed the game in
    /// 0.3.0 to 0.3.3): in Mars worlds and during a Mars intro, visible texts are rewritten in place a few times
    /// per second ("the Moon" → "Mars", "lunar" → "Martian" …), and the intro's slide list is swapped for
    /// the Mars story as soon as the game has loaded it.
    /// </summary>
    internal static class Text
    {
        /// <summary>The new-hire intro running right now belongs to a Mars slot.</summary>
        public static bool IntroIsMars;

        /// <summary>Only rewrite texts inside a Mars world, its loading screen or its intro (never the main menu).</summary>
        public static bool Active => IntroIsMars || MarsWorld.InMarsWorld || MarsWorld.LoadingMars;

        /// <summary>Never rewrite this text (our own UI that mentions the Moon on purpose).</summary>
        public static void Exempt(string s) { if (!string.IsNullOrEmpty(s)) exempt.Add(s); }

        // ------------------------------------------------------------------ per-frame work (no hooks)

        static float nextScan;
        static IntPtr slidesDoneFor;
        static readonly Dictionary<IntPtr, string> lastWritten = new Dictionary<IntPtr, string>();
        static readonly List<(Type type, PropertyInfo text)> tmpTypes = new List<(Type, PropertyInfo)>();
        static bool tmpLooked;

        /// <summary>Called every frame by MarsWorld (in the menu and in worlds).</summary>
        public static void Frame(float now)
        {
            DetectIntro();
            if (!Active) { if (lastWritten.Count > 0) lastWritten.Clear(); return; }
            if (IntroIsMars) { SwapSlides(); NarrateSlide(); PatchContract(); }
            if (now >= nextScan)
            {
                nextScan = now + 0.4f;
                RewriteVisibleTexts();
            }
        }

        static void DetectIntro()
        {
            bool active = false;
            try { active = MoonNewHire.Active; } catch { }
            if (!active) { IntroIsMars = false; return; }
            if (IntroIsMars) return;
            try
            {
                var inst = MoonNewHire.instance;
                if (inst != null && MarsState.IsMarsSlot(inst.slot))
                {
                    IntroIsMars = true;
                    slidesDoneFor = IntPtr.Zero;
                    contractDoneFor = IntPtr.Zero;
                    Narrator.ResetIntro();
                    Plugin.Log.LogMessage($"Mars intro for slot {inst.slot}");
                }
            }
            catch (Exception e) { Log.Once("intro-detect", "Intro detection failed: " + e.Message); }
        }

        /// <summary>Replace the intro's slide list with the Mars story once the game has filled it.</summary>
        static void SwapSlides()
        {
            try
            {
                var inst = MoonNewHire.instance;
                if (inst == null || inst.Pointer == slidesDoneFor) return;
                var prop = typeof(MoonNewHire).GetProperty("slides", BindingFlags.Public | BindingFlags.Instance);
                object list = prop?.GetValue(inst);
                if (list == null) return;
                var lt = list.GetType();
                int count = (int)lt.GetProperty("Count").GetValue(list);
                if (count == 0) return; // not loaded yet

                var lines = new List<string>();
                var item = lt.GetProperty("Item");
                for (int i = 0; i < count; i++)
                {
                    if (item?.GetValue(list, new object[] { i }) is System.Collections.IEnumerable arr)
                        foreach (var l in arr) lines.Add(l?.ToString());
                    lines.Add("");
                }
                Dump("IntroSlides", string.Join("\n", lines));

                lt.GetMethod("Clear").Invoke(list, null);
                var add = lt.GetMethod("Add");
                foreach (var slide in MarsSlides)
                    add.Invoke(list, new object[] { new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray(slide) });
                slidesDoneFor = inst.Pointer;
                Plugin.Log.LogMessage($"Mars intro slides in place ({count} original slides replaced by {MarsSlides.Length})");
            }
            catch (Exception e) { Log.Once("slides-err", "Swapping the intro slides failed: " + e.Message); slidesDoneFor = MoonNewHire.instance?.Pointer ?? IntPtr.Zero; }
        }

        /// <summary>
        /// The contract is printed into textures from its own data (title, clauses …), so rewriting the texts on
        /// screen does not reach it. Instead the contract data itself is rewritten, then the pages are re-printed.
        /// </summary>
        static IntPtr contractDoneFor;

        static void PatchContract()
        {
            MoonContractClipboard clip = null;
            try { clip = MoonNewHire.Clipboard; } catch { }
            if (clip == null) return;
            MoonContractPages pages = null;
            try { pages = clip.pages; } catch { }
            if (pages == null || pages.Pointer == contractDoneFor) return;
            contractDoneFor = pages.Pointer;
            var dump = new System.Text.StringBuilder();
            int changed = 0;
            try
            {
                var c = pages.contract;
                if (c == null) { Log.Once("contract-none", "Contract: no contract data found"); return; }
                string Fix(string label, string v)
                {
                    dump.AppendLine("[" + label + "] " + v);
                    string m = Marsify(v);
                    if (m != v) changed++;
                    return m;
                }
                c.title = Fix("title", c.title);
                c.subtitle = Fix("subtitle", c.subtitle);
                c.note = Fix("note", c.note);
                c.division = Fix("division", c.division);
                c.footer = Fix("footer", c.footer);
                var blocks = c.blocks;
                if (blocks != null)
                    for (int i = 0; i < blocks.Count; i++)
                    {
                        var b = blocks[i];
                        string t = b.text;
                        dump.AppendLine($"[block {i} kind '{b.kind}' {b.number}] {t}");
                        string m = Marsify(t);
                        if (m != t) { b.text = m; blocks[i] = b; changed++; }
                    }
                var buttons = c.buttons;
                if (buttons != null)
                    for (int i = 0; i < buttons.Count; i++)
                    {
                        string t = buttons[i];
                        dump.AppendLine($"[button {i}] {t}");
                        string m = Marsify(t);
                        if (m != t) { buttons[i] = m; changed++; }
                    }
            }
            catch (Exception e) { Log.Once("contract-data-err", "Contract data rewrite failed: " + (e.InnerException ?? e).Message); }
            Dump("Contract", dump.ToString());

            try { pages.dirty = true; pages.Refresh(); }
            catch (Exception e) { Log.Once("contract-refresh-err", "Contract re-print failed: " + (e.InnerException ?? e).Message); }
            Plugin.Log.LogMessage($"Mars contract: {changed} text(s) rewritten in the contract data, pages re-printed");
        }

        /// <summary>Tell the narrator which Mars slide is on screen (matched by its first line).</summary>
        static void NarrateSlide()
        {
            try
            {
                var inst = MoonNewHire.instance;
                if (inst == null || inst.Pointer != slidesDoneFor) return;
                var lineOne = R.Get(inst, "lineOne");
                string shown = lineOne != null ? R.Get(lineOne, "text") as string : null;
                if (string.IsNullOrEmpty(shown)) return;
                for (int i = 0; i < MarsSlides.Length; i++)
                    if (shown.Contains(MarsSlides[i][0])) { Narrator.Slide(i); return; }
            }
            catch { }
        }

        /// <summary>Rewrite every visible TextMeshPro text that mentions the Moon.</summary>
        static readonly HashSet<string> introTexts = new HashSet<string>();
        static readonly System.Text.StringBuilder introDump = new System.Text.StringBuilder();
        static bool changedThisPass;

        static void RewriteVisibleTexts()
        {
            changedThisPass = false;
            if (!tmpLooked)
            {
                tmpLooked = true;
                foreach (var n in new[] { "TMPro.TextMeshProUGUI", "TMPro.TextMeshPro" })
                {
                    var t = Type.GetType(n + ", Unity.TextMeshPro");
                    var p = t?.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
                    if (t != null && p != null) tmpTypes.Add((t, p));
                }
                Log.Once("tmp", $"Mars text pass ready ({tmpTypes.Count} text types)");
            }
            foreach (var (t, textProp) in tmpTypes)
            {
                var arr = UnityEngine.Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.From(t));
                for (int i = 0; i < arr.Length; i++)
                {
                    var o = arr[i];
                    if (o == null) continue;
                    try
                    {
                        var comp = new UnityEngine.Component(o.Pointer);
                        if (!Look.InScene(comp.gameObject)) continue;
                        // During the intro the contract pages are rendered from hidden texts, so include those too.
                        if (!IntroIsMars && !comp.gameObject.activeInHierarchy) continue;
                        object w = Activator.CreateInstance(t, o.Pointer);
                        string current = textProp.GetValue(w) as string;
                        if (string.IsNullOrEmpty(current)) continue;
                        if (lastWritten.TryGetValue(o.Pointer, out var mine) && mine == current) continue;
                        string mars = Marsify(current);
                        if (IntroIsMars && introTexts.Add(current)) introDump.AppendLine("--- " + comp.gameObject.name).AppendLine(current);
                        if (mars != current) { textProp.SetValue(w, mars); changedThisPass = true; }
                        lastWritten[o.Pointer] = mars;
                    }
                    catch { }
                }
            }
            if (lastWritten.Count > 20000) lastWritten.Clear();
            if (IntroIsMars)
            {
                if (introDump.Length > 0) Dump("IntroTexts", introDump.ToString());
                if (changedThisPass)
                {
                    // Ask the contract to draw its pages again with the new words.
                    try
                    {
                        var clip = MoonNewHire.Clipboard;
                        if (clip != null && clip.pages != null) { clip.pages.dirty = true; Log.Once("contract", "Mars contract text applied"); }
                    }
                    catch (Exception e) { Log.Once("contract-err", "Contract refresh failed: " + e.Message); }
                }
            }
        }

        static readonly Dictionary<string, string> cache = new Dictionary<string, string>();
        static string cachedLanguage;

        // Order matters: longer phrases first. Protected names are swapped out first and put back last.
        static readonly (Regex re, string to)[] English = Rules(
            (@"\bMoon Sweepers\b", "\u0001"),                       // the game's title stays
            (@"\bthe Moon's\b", "Mars's"), (@"\bThe Moon's\b", "Mars's"),
            (@"\b[Tt]he [Mm]oon\b", "Mars"), (@"\bTHE MOON\b", "MARS"),
            (@"\bLunar Surface Department\b", "Martian Surface Department"),
            (@"\blunar\b", "Martian"), (@"\bLunar\b", "Martian"), (@"\bLUNAR\b", "MARTIAN"),
            (@"\b[Mm]oon ?dust\b", "Mars dust"),
            (@"\bMoon Cleaners\b", "Mars Cleaners"),
            (@"\bMoon\b", "Mars"), (@"\bMOON\b", "MARS"), (@"\bmoon\b", "Mars"),
            (@"\bLSD-01\b", "MSD-01"),
            ("\u0001", "Moon Sweepers"));

        static readonly (Regex re, string to)[] German = Rules(
            (@"\bMoon Sweepers\b", "\u0001"),
            (@"\bdes Mondes\b", "des Mars"), (@"\bdem Mond\b", "dem Mars"), (@"\bden Mond\b", "den Mars"), (@"\bder Mond\b", "der Mars"),
            (@"\bMondes\b", "Mars"), (@"\bMond\b", "Mars"), (@"\bMOND\b", "MARS"),
            (@"\bMond(?=[a-zäöüß])", "Mars"),                          // Mondstaub → Marsstaub, Mondbasis → Marsbasis
            (@"\bmondst", "marsst"), (@"\blunar(e[nrms]?)?\b", "marsianisch$1"),
            ("\u0001", "Moon Sweepers"));

        static readonly (Regex re, string to)[] Other = Rules(
            (@"\bMoon Sweepers\b", "\u0001"),
            (@"\b[Ll]a Lune\b", "Mars"), (@"\bLune\b", "Mars"), (@"\blunaires?\b", "martien"),          // French
            (@"\b[Ll]a Luna\b", "Marte"), (@"\bLuna\b", "Marte"), (@"\blunares?\b", "marciano"),         // Spanish / Italian
            (@"\b[Aa] Lua\b", "Marte"), (@"\bLua\b", "Marte"),                                              // Portuguese
            (@"Księżyc\w*", "Mars"),                                                                        // Polish
            (@"Лун[аыеуой]\b", "Марс"), (@"\bлун[аыеуой]\b", "Марс"),                                        // Russian
            ("\u0001", "Moon Sweepers"));

        static (Regex, string)[] Rules(params (string pattern, string to)[] r) =>
            r.Select(x => (new Regex(x.pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant), x.to)).ToArray();

        /// <summary>Our own Mars texts: they mention the Moon on purpose and must not be rewritten.</summary>
        static readonly HashSet<string> exempt = new HashSet<string>();

        /// <summary>Marsify a long text, leaving our own Mars sentences (which mention the Moon on purpose) alone.</summary>
        public static string MarsifyText(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var kept = new List<string>();
            foreach (var line in Contract.OwnLines())
                if (s.Contains(line)) { s = s.Replace(line, "\u0002" + kept.Count + "\u0003"); kept.Add(line); }
            s = Marsify(s);
            for (int i = 0; i < kept.Count; i++) s = s.Replace("\u0002" + i + "\u0003", kept[i]);
            return s;
        }

        public static string Marsify(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (exempt.Contains(s)) return s;
            string lang = "";
            try { lang = (MoonLoc.Code ?? "").ToLowerInvariant(); } catch { }
            if (lang != cachedLanguage) { cache.Clear(); cachedLanguage = lang; }
            if (cache.TryGetValue(s, out var done)) return done;

            string r = s;
            foreach (var (re, to) in English) r = re.Replace(r, to);
            if (lang.StartsWith("de") || lang.StartsWith("german")) foreach (var (re, to) in German) r = re.Replace(r, to);
            else if (!lang.StartsWith("en")) foreach (var (re, to) in Other) r = re.Replace(r, to);

            if (cache.Count > 5000) cache.Clear();
            cache[s] = r;
            return r;
        }

        // ------------------------------------------------------------------ intro and contract

        static readonly string[][] MarsSlides =
        {
            new[] { "Congratulations. The Moon is clean.", "Every last sector. The Lunar Surface Department has never been prouder." },
            new[] { "So proud, in fact, that they have found you a new job.", "One planet further out." },
            new[] { "Mars. 144 million square kilometres of red dust.", "The Martian Surface Department has no staff yet. Now it has you." },
            new[] { "Gravity is stronger here. The dust is redder. Much redder.", "The wind has opinions. Bring a scarf." },
            new[] { "Your equipment has already been shipped ahead.", "It is a toothbrush. Budget cuts." },
            new[] { "Sign the contract and board the capsule.", "Mars isn't going to clean itself." },
        };

        static Text()
        {
            foreach (var slide in MarsSlides)
            {
                foreach (var line in slide) exempt.Add(line);
                exempt.Add(string.Join("\n", slide));
                exempt.Add(string.Join(" ", slide));
            }
            foreach (var w in Welcomes) exempt.Add(w);
            foreach (var c in Contract.OwnLines()) exempt.Add(c);
            exempt.Add(FirstWelcome);
        }

        static void Dump(string name, string text)
        {
            try
            {
                string dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "MarsMod", "originals");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, name + ".txt"), text ?? "(null)");
            }
            catch { }
        }

        // ------------------------------------------------------------------ welcome notices

        static readonly string[] Welcomes =
        {
            "Welcome to Mars, recruit. The Moon was the warm-up.",
            "Martian Surface Department, day one. Same toothbrush, bigger planet.",
            "Gravity 3.71 m/s². Your knees have been informed.",
            "Mars: 0% clean. The Moon sends its regards.",
            "Red dust everywhere. You'll get used to it. Probably.",
            "Shift report: still Mars. Still dusty. Get sweeping.",
        };

        const string FirstWelcome = "Welcome to Mars! The Moon is done, so they sent you here. Grab the toothbrush.";

        public static string Welcome(bool firstVisit) =>
            firstVisit ? FirstWelcome : Welcomes[new Random().Next(Welcomes.Length)];
    }
}
