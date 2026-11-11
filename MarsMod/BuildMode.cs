using System;
using System.Linq;
using System.Text;
using BepInEx;
using Il2CppInterop.Runtime;
using MoonCleaners;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// Using Mars buildings, plus the old N build menu as a fallback when the game's build menu can't take our machines:
    ///   N                open / close the fallback Mars build menu (1-3 or Up/Down + Enter to pick)
    ///   while placing    T rotates, Enter or middle mouse builds, N or Backspace cancels
    ///   looking at one   E (the game's interact key) uses it, Shift+E the second action, Delete twice removes it
    /// </summary>
    internal static class BuildMode
    {
        enum Mode { None, Menu, Placing }
        static Mode mode;
        static int menuIndex;
        static MarsCatalog.Def placing;
        static GameObject ghost;
        static Material ghostOk, ghostBad;
        static float yaw, removeArmedUntil;
        static int removeArmedId = -1;
        static MarsUi.Box menuBox, hintBox, promptBox, packBox, goalBox;
        static string goalText;
        static float nextGoal;
        static string lastReason;

        public static bool Busy => mode != Mode.None;

        public static void Reset()
        {
            Cancel();
            menuBox?.Set(null); hintBox?.Set(null); promptBox?.Set(null); packBox?.Set(null); goalBox?.Set(null); goalText = null;
        }

        public static void Frame(float now, float dt)
        {
            if (menuBox == null)
            {
                menuBox = MarsUi.Make("Mars build menu", 0.5f, 0.56f, 820, 430, 24, 257);
                hintBox = MarsUi.Make("Mars build hint", 0.5f, 0.13f, 1000, 92, 22, 514);
                promptBox = MarsUi.Make("Mars prompt", 0.5f, 0.24f, 1000, 92, 22, 514);
                packBox = MarsUi.Make("Mars backpack", 0.985f, 0.66f, 380, 210, 20, 257);
                goalBox = MarsUi.Make("Mars goal", 0.985f, 0.93f, 380, 78, 16, 257);
            }
            var kb = UnityInput.Current;
            bool ui = !Terminal.Open && MarsWorld.PlayerReady;
            if (!ui) { menuBox.Set(null); hintBox.Set(null); promptBox.Set(null); packBox.Set(null); goalBox.Set(null); goalText = null; return; }
            if (now >= nextGoal)
            {
                nextGoal = now + 1f;
                string g = $"<color=#E8743B><b>MARS</b></color>  {Goals.Current()}\n<size=14>Ground: {DustTiers.Label(DustTiers.Here)}</size>";
                if (g != goalText) { goalText = g; goalBox.Set(g); }
            }

            if (kb.GetKeyDown(KeyCode.N))
            {
                if (VanillaBuild.Ready && mode == Mode.None) MarsWorld.Urgent("Mars machines are in the normal build menu now: build tool → Production / Logistics / Power.");
                else if (mode == Mode.None) OpenMenu(); else Cancel();
            }

            switch (mode)
            {
                case Mode.Menu: MenuFrame(kb); break;
                case Mode.Placing: PlaceFrame(kb, dt); break;
                default: menuBox.Set(null); hintBox.Set(null); Interact(kb, now); break;
            }
            Backpack();
        }

        // ------------------------------------------------------------------ menu

        static void OpenMenu() { mode = Mode.Menu; menuIndex = Mathf.Clamp(menuIndex, 0, MarsCatalog.All.Count - 1); Block(true); }

        static void MenuFrame(IInputSystem kb)
        {
            Block(true);
            int n = MarsCatalog.All.Count;
            for (int i = 0; i < Math.Min(9, n); i++) if (kb.GetKeyDown(KeyCode.Alpha1 + i) || kb.GetKeyDown(KeyCode.Keypad1 + i)) { menuIndex = i; StartPlacing(MarsCatalog.All[i]); return; }
            if (kb.GetKeyDown(KeyCode.UpArrow)) menuIndex = (menuIndex + n - 1) % n;
            if (kb.GetKeyDown(KeyCode.DownArrow)) menuIndex = (menuIndex + 1) % n;
            if (kb.GetKeyDown(KeyCode.Return) || kb.GetKeyDown(KeyCode.KeypadEnter)) { StartPlacing(MarsCatalog.All[menuIndex]); return; }
            if (kb.GetKeyDown(KeyCode.Escape) || kb.GetKeyDown(KeyCode.Backspace)) { Cancel(); return; }

            double money = Buildings.Wallet();
            var sb = new StringBuilder();
            sb.Append("<size=30><b><color=#E8743B>MARS BUILD MENU</color></b></size>      <color=#9A8F86>money</color> <b>$" + money.ToString("N0") + "</b>\n\n");
            for (int i = 0; i < n; i++)
            {
                var d = MarsCatalog.All[i];
                bool sel = i == menuIndex;
                string price = money + 0.01 >= d.Price ? $"${d.Price}" : $"<color=#FF6B5A>${d.Price}</color>";
                sb.Append(sel ? "<color=#E8743B>▶</color> " : "   ");
                sb.Append($"<b>{i + 1}  {d.Name}</b>   {price}\n");
                sb.Append($"      <size=19><color=#C9B8A8>{d.Info}</color></size>\n\n");
            }
            sb.Append("<size=18><color=#9A8F86>1-" + n + " or ↑↓ + Enter to build · N or Esc to close</color></size>");
            menuBox.Set(sb.ToString());
        }

        // ------------------------------------------------------------------ placing

        static void StartPlacing(MarsCatalog.Def def)
        {
            Block(false);
            menuBox.Set(null);
            placing = def;
            mode = Mode.Placing;
            if (ghost != null) UnityEngine.Object.Destroy(ghost);
            try
            {
                ghost = Models.Spawn(def.Model, Vector3.zero, Quaternion.identity);
                ghost.name = "Mars build ghost";
                Ghostify(ghost);
            }
            catch (Exception e) { Plugin.Log.LogWarning("[build] ghost failed: " + e.Message); Cancel(); }
        }

        static void PlaceFrame(IInputSystem kb, float dt)
        {
            var cam = Camera.main;
            if (cam == null || ghost == null) { Cancel(); return; }
            if (kb.GetKeyDown(KeyCode.Backspace) || kb.GetKeyDown(KeyCode.Escape)) { Cancel(); return; }
            if (kb.GetKeyDown(KeyCode.T)) yaw = (yaw + 90f) % 360f;

            Vector3 aim;
            if (!Commands.Look2(out aim) || Vector3.Distance(aim, cam.transform.position) > 30f)
                aim = cam.transform.position + cam.transform.forward * 18f;
            var world = Commands.FindWorld();
            try { aim = world.Terrain.SurfacePoint(aim.normalized); } catch { }
            var up = aim.normalized;
            var fwd = Vector3.ProjectOnPlane(cam.transform.forward, up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(Vector3.forward, up);
            var rot = Quaternion.AngleAxis(yaw, up) * Quaternion.LookRotation(-fwd.normalized, up);  // front faces the player

            string why = Buildings.CanPlace(placing, aim, out var mine);
            if (mine != null) { aim = mine.Position; up = aim.normalized; rot = Quaternion.AngleAxis(yaw, up) * Quaternion.LookRotation(-Vector3.ProjectOnPlane(cam.transform.forward, up).normalized, up); }
            double money = Buildings.Wallet();
            if (why == null && money + 0.01 < placing.Price) why = $"Not enough money (${placing.Price}).";
            ghost.transform.SetPositionAndRotation(aim, rot);
            if (why != lastReason) { SetGhost(why == null); lastReason = why; }

            hintBox.Set($"<b>{placing.Name}</b>  ${placing.Price}   " + (why == null ? "<color=#7CFF9A>ready</color>" : $"<color=#FF6B5A>{why}</color>") +
                        "\n<size=18><color=#C9B8A8>[T] rotate   [Enter] or [middle mouse] build   [N] / [Backspace] cancel</color></size>");

            bool place = kb.GetKeyDown(KeyCode.Return) || kb.GetKeyDown(KeyCode.KeypadEnter) || kb.GetMouseButtonDown(2);
            if (place && why == null)
            {
                var p = aim; var q = rot; var id = placing.Id;
                Buildings.Ask(Buildings.Op.Place, w => { w.Write(id); w.Write(p.x); w.Write(p.y); w.Write(p.z); w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); });
                // Stay in placing mode with shift held, like the game's build tool; otherwise done.
                if (!(kb.GetKey(KeyCode.LeftShift) || kb.GetKey(KeyCode.RightShift))) Cancel();
            }
            else if (place) MarsWorld.Urgent(why);
        }

        static void Ghostify(GameObject go)
        {
            if (ghostOk == null)
            {
                var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
                ghostOk = new Material(sh) { name = "Mars ghost ok" }; ghostOk.color = new Color(0.35f, 1f, 0.55f, 0.35f);
                ghostBad = new Material(sh) { name = "Mars ghost bad" }; ghostBad.color = new Color(1f, 0.3f, 0.25f, 0.35f);
                foreach (var m in new[] { ghostOk, ghostBad })
                {
                    if (m.HasProperty("_RendererColor")) m.SetColor("_RendererColor", Color.white);
                    m.renderQueue = 3100;
                    UnityEngine.Object.DontDestroyOnLoad(m);
                }
            }
            var boxT = R.T("UnityEngine.Collider", "UnityEngine.PhysicsModule");
            if (boxT != null)
                foreach (var c in go.GetComponentsInChildren(Il2CppType.From(boxT), true)) UnityEngine.Object.Destroy(c);
            lastReason = "init";
        }

        static void SetGhost(bool ok)
        {
            if (ghost == null) return;
            foreach (var c in ghost.GetComponentsInChildren(Il2CppType.Of<Renderer>(), true))
            {
                var r = c.TryCast<Renderer>(); if (r == null) continue;
                var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Material>(r.sharedMaterials.Length);
                for (int i = 0; i < arr.Length; i++) arr[i] = ok ? ghostOk : ghostBad;
                r.sharedMaterials = arr;
            }
        }

        public static void Cancel()
        {
            if (ghost != null) UnityEngine.Object.Destroy(ghost);
            ghost = null; placing = null;
            if (mode == Mode.Menu) Block(false);
            mode = Mode.None;
            hintBox?.Set(null); menuBox?.Set(null);
        }

        /// <summary>Terminal: jump straight into placing a building (game tool when available, else our own).</summary>
        public static string Place(string id)
        {
            var def = MarsCatalog.Get(id);
            if (def == null) return "Buildings: " + string.Join(", ", MarsCatalog.All.Select(d => d.Id));
            var e = VanillaBuild.ByType(id);
            if (VanillaBuild.Ready && e != null)
            {
                MarsWorld.Defer(() =>
                {
                    try
                    {
                        var w = Commands.FindWalker();
                        var builder = w?.GetComponent(Il2CppType.Of<MoonDepotBuilder>())?.TryCast<MoonDepotBuilder>();
                        if (builder == null || !builder.BeginPlacementFromShop(e.Kind)) MarsWorld.Urgent("The build tool would not start (take out the build tool first?).");
                    }
                    catch (Exception ex) { MarsWorld.Urgent("Build tool: " + ex.Message); }
                });
                return $"Placing a {def.Name} with the game's build tool.";
            }
            MarsWorld.Defer(() => StartPlacing(def));
            return $"Placing a {def.Name}: aim, [T] rotate, [Enter] build.";
        }

        // ------------------------------------------------------------------ using buildings

        static void Interact(IInputSystem kb, float now)
        {
            var cam = Camera.main;
            if (cam == null) { promptBox.Set(null); return; }
            var b = Buildings.Target(cam.transform.position, cam.transform.forward, 5f);
            if (b == null) { promptBox.Set(null); removeArmedId = -1; return; }

            bool armed = removeArmedId == b.Id && now < removeArmedUntil;
            string remove = b.Vanilla ? "" : armed ? "   <color=#FF6B5A>[Delete] again to remove</color>" : "   [Delete] remove";
            bool scrub = Buildings.Jams(b) && b.Dust >= 0.3f;
            bool held = kb.GetKey(KeyCode.E) || kb.GetKey(KeyCode.F);
            if (scrub && held && cleanId == b.Id) cleanHeld += Time.deltaTime; else cleanHeld = 0f;
            cleanId = b.Id;
            string bar = "";
            if (scrub && cleanHeld > 0f)
            {
                int filled = Mathf.Clamp(Mathf.RoundToInt(cleanHeld / Buildings.CleanSeconds * 20f), 0, 20);
                bar = "\n<color=#7FD8F0>" + new string('█', filled) + "</color><color=#5A524C>" + new string('█', 20 - filled) + "</color>";
            }
            promptBox.Set(Buildings.Describe(b) + Buildings.DustLine(b) + "\n<size=18><color=#C9B8A8>" + Buildings.Hint(b, UseLabel()) + remove + "</color></size>" + bar);
            if (scrub)
            {
                if (cleanHeld >= Buildings.CleanSeconds) { int cid = b.Id; Buildings.Ask(Buildings.Op.Clean, w => w.Write(cid)); cleanHeld = 0f; }
                return;
            }

            bool use = false;
            try { use = MoonInput.Interact.Pressed; } catch { }
            if (!use) use = kb.GetKeyDown(KeyCode.F);
            if (use)
            {
                bool alt = kb.GetKey(KeyCode.LeftShift) || kb.GetKey(KeyCode.RightShift);
                int id = b.Id;
                Buildings.Ask(alt ? Buildings.Op.UseAlt : Buildings.Op.Use, w => w.Write(id));
            }
            if (!b.Vanilla && kb.GetKeyDown(KeyCode.Delete))
            {
                if (armed) { int id = b.Id; Buildings.Ask(Buildings.Op.Remove, w => w.Write(id)); removeArmedId = -1; }
                else { removeArmedId = b.Id; removeArmedUntil = now + 2.5f; }
            }
        }

        static int cleanId = -1;
        static float cleanHeld;
        static string useLabel;
        static string UseLabel()
        {
            if (useLabel != null) return useLabel;
            try { useLabel = MoonInput.Interact.ShortLabel; } catch { }
            if (string.IsNullOrEmpty(useLabel)) useLabel = "E";
            return useLabel;
        }

        static void Backpack()
        {
            var pack = Buildings.Backpack(Buildings.Me);
            bool any = Buildings.Total(pack) > 0;
            bool showStore = mode == Mode.Menu || Buildings.Total(Buildings.Items.Storage) > 0 && mode != Mode.None;
            if (!any && !showStore) { packBox.Set(null); return; }
            var sb = new StringBuilder("<b><color=#E8743B>MARS BACKPACK</color></b>\n");
            if (!any) sb.Append("<color=#9A8F86>empty</color>\n");
            foreach (var kv in pack) if (kv.Value > 0) sb.Append($"{MarsItems.Name(kv.Key)}  <b>×{kv.Value}</b>\n");
            if (showStore) sb.Append($"\n<b><color=#7FD8F0>CREW STORAGE</color></b>\n{MarsItems.List(Buildings.Items.Storage)}");
            packBox.Set(sb.ToString());
        }

        // ------------------------------------------------------------------ input

        static void Block(bool on)
        {
            try { MoonInput.Typing = on; } catch { }
            try { foreach (var w in Look.All<MoonWalker>()) if (Look.InScene(w.gameObject)) w.UIBlocked = on; } catch { }
        }
    }
}
