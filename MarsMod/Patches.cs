using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime;
using MoonCleaners;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// All game hooks. Each one is applied on its own, so one failing hook (for example after a game
    /// update renamed something) only disables that feature and is reported in the log.
    /// </summary>
    internal static class Patches
    {
        static Harmony harmony;
        public static readonly List<string> Failed = new List<string>();
        public static int Ok;

        public static void ApplyAll()
        {
            harmony = new Harmony(Plugin.Guid);
            var me = typeof(Patches);

            // --- where saves go
            Patch(AccessTools.PropertyGetter(typeof(MoonSaveSlots), "Folder"), post: nameof(Folder_Post));
            Patch(AccessTools.Method(typeof(MoonSaveSlots), "SlotFolder", new[] { typeof(int) }), post: nameof(SlotPath_Post));
            Patch(AccessTools.Method(typeof(MoonSaveSlots), "BackupFolder", new[] { typeof(int) }), post: nameof(SlotPath_Post));
            Patch(AccessTools.Method(typeof(MoonSaveSlots), "Root", new[] { typeof(string) }), post: nameof(CurrentPath_Post));
            Patch(AccessTools.Method(typeof(MoonSaveSlots), "Backups", new[] { typeof(string) }), post: nameof(CurrentPath_Post));
            Patch(AccessTools.Method(typeof(MoonSaveSlots), "Delete", new[] { typeof(int) }), pre: nameof(Delete_Pre), post: nameof(Delete_Post));

            // --- menu: "MARS" label on Mars slots
            Patch(AccessTools.Method(typeof(MoonMainMenu), "SlotLine", new[] { typeof(int) }), post: nameof(SlotLine_Post));

            // --- starting and joining worlds
            // Ask on the start screen while the menu is idle (Play button / double-clicked slot)…
            Patch(AccessTools.Method(typeof(MoonMainMenu), "Play"), pre: nameof(MenuPlay_Pre));
            Patch(AccessTools.Method(typeof(MoonMainMenu), "PlayChosen"), pre: nameof(MenuPlay_Pre));
            // …and as a fallback right where the host's world really starts.
            Patch(AccessTools.Method(typeof(MoonSession), "StartShift"), pre: nameof(StartShift_Pre));
            Patch(AccessTools.Method(typeof(MoonSession), "ReloadShift"), pre: nameof(ReloadShift_Pre));
            Patch(AccessTools.Method(typeof(MoonSession), "JoinCrewShift"), pre: nameof(JoinCrewShift_Pre));

            // --- world creation: switch the planet settings before the terrain is built
            foreach (var m in new[] { "OnEnable", "Initialize", "Start" })
                Patch(AccessTools.Method(typeof(MoonWorld), m), pre: nameof(World_Pre));
            Patch(AccessTools.Method(typeof(MoonWorld), "OnDestroy"), post: nameof(WorldDestroyed_Post));

            // --- the new-hire contract text, swapped for the Mars contract before the game parses and prints it
            // (static string → string, called twice per intro; MarsProbe has traced this method without trouble)
            Patch(AccessTools.Method(typeof(MoonNewHire), "LoadText", new[] { typeof(string) }), post: nameof(LoadText_Post));

            // --- Mars machines in the game's own build menu (VanillaBuild). All take/return references or ints.
            Patch(AccessTools.Method(typeof(MoonWorld), "BuildDefinition", new[] { typeof(int) }), post: nameof(BuildDefinition_Post));
            Patch(AccessTools.Method(typeof(MoonDepotBuilder), "CardArt", new[] { typeof(string) }), post: nameof(CardArt_Post));
            Patch(AccessTools.Method(typeof(MoonDepotBuilder), "CanBuild", new[] { typeof(int) }), post: nameof(CanBuild_Post));
            Patch(AccessTools.Method(typeof(MoonDepotBuilder), "ProductionNote", new[] { typeof(int) }), post: nameof(ProductionNote_Post));
            Patch(AccessTools.PropertyGetter(typeof(MoonDepotBuilder), "PlacementValid"), post: nameof(PlacementValid_Post));
            Patch(AccessTools.PropertyGetter(typeof(MoonDepotBuilder), "PlacementReason"), post: nameof(PlacementReason_Post));
            // Our kinds never reach the game's save file: taken out before it is written, put back when it loads.
            Patch(AccessTools.Method(typeof(MoonDepotSave), "Write"), pre: nameof(SaveWrite_Pre), post: nameof(SaveWrite_Post));
            Patch(AccessTools.Method(typeof(MoonWorld), "RestoreDepots"), pre: nameof(RestoreDepots_Pre));

            // --- dust tiers: the bonus on top of the game's sector bounty (private void (int, string))
            Patch(AccessTools.Method(typeof(MoonWorld), "PaySection", new[] { typeof(int), typeof(string) }), pre: nameof(PaySection_Pre), post: nameof(PaySection_Post));
            // ...and the slower scrubbing in the higher tiers (public float (string))
            Patch(AccessTools.Method(typeof(MoonWorld), "CleaningDepthMultiplier", new[] { typeof(string) }), post: nameof(CleaningDepth_Post));

            // --- belt research, passive: logs the first canisters entering machines and cargo put on belts
            Patch(AccessTools.Method(typeof(MoonWorld), "EnterMachine", new[] { typeof(MoonDepotSnapshot), typeof(int), typeof(int) }), pre: nameof(EnterMachine_Pre), post: nameof(EnterMachine_Post));
            Patch(AccessTools.Method(typeof(MoonWorld), "EmitCargo"), post: nameof(EmitCargo_Post));

            // --- our per-frame tick rides on two game components that live for the whole session
            Patch(AccessTools.Method(typeof(MoonCrashHeartbeat), "Update"), post: nameof(Frame_Post));
            Patch(AccessTools.Method(typeof(MoonFramePacer), "Update"), post: nameof(Frame_Post));

            // --- diagnostics: log the order in which the start flow runs
            foreach (var (t, m) in new (Type, string)[]
            {
                (typeof(MoonMainMenu), "StartGame"), (typeof(MoonMainMenu), "ClickSlot"), (typeof(MoonMainMenu), "ReadyOrStart"),
                (typeof(MoonSteamLobby), "Create"), (typeof(MoonSteamLobby), "StartShift"), (typeof(MoonSteamLobby), "EnterShift"),
                (typeof(MoonSession), "Load"), (typeof(MoonSession), "ReturnToMenu"),
            })
                Patch(AccessTools.Method(t, m), post: nameof(Flow_Post));

            Plugin.Log.LogMessage($"Hooks: {Ok} ok, {Failed.Count} failed" + (Failed.Count > 0 ? ": " + string.Join("; ", Failed) : ""));
        }

        static void Patch(MethodBase target, string pre = null, string post = null)
        {
            if (target == null) { Failed.Add($"(missing method for {pre ?? post})"); return; }
            string name = target.DeclaringType?.Name + "." + target.Name;
            // Never hook methods that take or return game structs by value: on this Unity version that corrupts the runtime.
            bool Bad(Type t) => t.IsValueType && !t.IsPrimitive && !t.IsEnum && t != typeof(void);
            if (target is MethodInfo mi && Bad(mi.ReturnType) || Array.Exists(target.GetParameters(), p => Bad(p.ParameterType)))
            {
                Failed.Add(name + ": skipped, struct parameter");
                return;
            }
            try
            {
                harmony.Patch(target,
                    prefix: pre != null ? new HarmonyMethod(typeof(Patches).GetMethod(pre, BindingFlags.Static | BindingFlags.NonPublic)) : null,
                    postfix: post != null ? new HarmonyMethod(typeof(Patches).GetMethod(post, BindingFlags.Static | BindingFlags.NonPublic)) : null);
                Ok++;
            }
            catch (Exception e) { Failed.Add(name + ": " + e.Message); }
        }

        // ================================================================= Mars buildings in the game's build tool

        static void BuildDefinition_Post(MoonWorld __instance, int __0, ref MoonDepotBuildDefinition __result)
        {
            if (!VanillaBuild.IsOurs(__0) || !MarsState.CurrentIsMars) return;
            try
            {
                VanillaBuild.Install(__instance);
                var e = VanillaBuild.ByKind(__0);
                if (e?.Definition != null) __result = e.Definition;
            }
            catch (Exception e) { Log.Once("hook-builddef", "BuildDefinition hook: " + e.Message); }
        }

        static void CardArt_Post(string __0, ref Sprite __result)
        {
            if (__0 == null || !__0.StartsWith("marsmod.")) return;
            try { var s = VanillaBuild.CardArt(__0); if (s != null) __result = s; }
            catch (Exception e) { Log.Once("hook-card", "CardArt hook: " + e.Message); }
        }

        static void CanBuild_Post(int __0, ref bool __result)
        {
            if (VanillaBuild.IsOurs(__0) && MarsState.CurrentIsMars) __result = true;
        }

        static void ProductionNote_Post(int __0, ref string __result)
        {
            var e = VanillaBuild.ByKind(__0);
            if (e != null && MarsState.CurrentIsMars) __result = e.Def.Info;
        }

        static void PlacementValid_Post(MoonDepotBuilder __instance, ref bool __result)
        {
            try { if (__result && VanillaBuild.Block != null && __instance.ghostKind == VanillaBuild.GhostKind) __result = false; } catch { }
        }

        static void PlacementReason_Post(MoonDepotBuilder __instance, ref string __result)
        {
            try { if (VanillaBuild.Block != null && __instance.ghostKind == VanillaBuild.GhostKind) __result = VanillaBuild.Block; } catch { }
        }

        static void PaySection_Pre(MoonWorld __instance, string __1, out double __state) => __state = MarsWorld.InMarsWorld ? DustTiers.Before(__instance, __1) : double.NaN;
        static void PaySection_Post(MoonWorld __instance, int __0, string __1, double __state) => DustTiers.After(__instance, __0, __1, __state);

        static bool EnterMachine_Pre(MoonWorld __instance, MoonDepotSnapshot __0, int __2, ref bool __result)
        {
            try { return !Belts.Enter(__instance, __0, __2, ref __result); } catch { return true; }
        }
        static void EnterMachine_Post(MoonWorld __instance, MoonDepotSnapshot __0, int __1, int __2, bool __result) => BeltResearch.EnterMachine(__instance, __0, __1, __2, __result);
        static void EmitCargo_Post(MoonWorld __instance, MoonConveyorSnapshot __0, string __1, MoonCargo __2, double __3, bool __result) => BeltResearch.EmitCargo(__instance, __0, __1, __2, __3, __result, null);

        static void CleaningDepth_Post(string __0, ref float __result) => DustTiers.CleaningDepth(__0, ref __result);

        static void SaveWrite_Pre(MoonLooseDustSnapshot __1, out VanillaBuild.Taken __state)
        {
            __state = null;
            try { __state = VanillaBuild.BeforeWrite(__1); }
            catch (Exception e) { Plugin.Log.LogError("[vbuild] save hook: " + e.Message); }
        }

        static void SaveWrite_Post(MoonLooseDustSnapshot __1, VanillaBuild.Taken __state)
        {
            try { VanillaBuild.AfterWrite(__1, __state); }
            catch (Exception e) { Plugin.Log.LogError("[vbuild] save hook (after): " + e.Message); }
        }

        static void RestoreDepots_Pre(MoonWorld __instance, MoonLooseDustSnapshot __0)
        {
            try { VanillaBuild.BeforeRestore(__instance, __0); }
            catch (Exception e) { Plugin.Log.LogError("[vbuild] load hook: " + e.Message); }
        }

        // ================================================================= save paths

        static void Folder_Post(ref string __result)
        {
            if (MarsState.CurrentIsMars) __result = MarsState.Redirect(__result);
        }

        static void SlotPath_Post(int __0, ref string __result)
        {
            if (MarsState.IsMarsSlot(__0)) __result = MarsState.Redirect(__result);
        }

        static void CurrentPath_Post(ref string __result)
        {
            if (MarsState.CurrentIsMars) __result = MarsState.Redirect(__result);
        }

        static void Delete_Pre(int __0, out bool __state) => __state = MarsState.IsMarsSlot(__0);

        static void Delete_Post(int __0, bool __state)
        {
            if (__state) MarsState.Unmark(__0);
        }

        static void SlotLine_Post(int __0, ref string __result)
        {
            if (MarsState.IsMarsSlot(__0) && __result != null && !__result.Contains("MARS"))
                __result = "<color=#E8743B><b>MARS</b></color>  " + __result;
        }

        // ================================================================= start / join

        static bool passThrough;
        /// <summary>Empty slots the player already answered the Moon/Mars question for in this session.</summary>
        static readonly HashSet<int> decided = new HashSet<int>();
        public static void Forget(int slot) => decided.Remove(slot);

        static bool NeedsChoice(int slot)
        {
            if (slot < 0 || decided.Contains(slot) || MarsState.IsMarsSlot(slot)) return false;
            try { return !MoonSaveSlots.Used(slot); }
            catch (Exception e) { Plugin.Log.LogWarning("Used() failed: " + e.Message); return false; }
        }

        static bool MenuPlay_Pre(MoonMainMenu __instance, MethodBase __originalMethod)
        {
            int slot = -1;
            try { slot = __instance.chosenSlot; } catch { }
            LastPlaySlot = slot;
            string name = __originalMethod?.Name;
            Plugin.Log.LogInfo($"[flow] MoonMainMenu.{name} (chosen slot {slot}, active slot {MarsState.ActiveSlot})");
            if (!NeedsChoice(slot))
            {
                Lobby.HostingMars = MarsState.IsMarsSlot(slot);
                return true;
            }
            var menu = __instance;
            return !Choice.Ask(slot, s =>
            {
                decided.Add(s);
                try { if (name == "Play") menu.Play(); else menu.PlayChosen(); }
                catch (Exception e) { Plugin.Log.LogError($"Could not continue {name}: " + e); }
            });
        }

        static bool StartShift_Pre()
        {
            int slot = MarsState.ActiveSlot;
            Plugin.Log.LogInfo($"[flow] MoonSession.StartShift (active slot {slot}, passThrough={passThrough})");
            if (passThrough) { passThrough = false; return true; }

            if (NeedsChoice(slot) && Choice.Ask(slot, Resume))
                return false; // the dialog is up, we continue in Resume()

            BeginHostWorld(slot, "StartShift");
            return true;
        }

        static void Resume(int slot)
        {
            decided.Add(slot);
            BeginHostWorld(slot, "after choice");
            passThrough = true;
            try { MoonSession.StartShift(); }
            catch (Exception e) { passThrough = false; Plugin.Log.LogError("Could not continue starting the shift: " + e); }
        }

        static bool ReloadShift_Pre()
        {
            BeginHostWorld(MarsState.ActiveSlot, "ReloadShift");
            return true;
        }

        static void BeginHostWorld(int slot, string why)
        {
            MarsState.CurrentIsMars = MarsState.IsMarsSlot(slot);
            MarsState.CurrentProfile = MarsState.CurrentIsMars ? MarsState.ProfileOf(slot) : 0;
            MarsState.WorldStartedUtc = DateTime.UtcNow;
            Lobby.HostingMars = MarsState.CurrentIsMars;
            Lobby.FlagSelf();
            Lobby.RepublishVersion();
            MarsWorld.SyncWorldSettings();
            Lobby.Publish(MarsState.CurrentIsMars, MarsState.CurrentProfile);
            if (MarsState.CurrentIsMars)
            {
                var missing = Lobby.MembersWithoutMod();
                if (missing.Count > 0)
                    MarsWorld.Warn($"Crewmates without the Mars mod: {string.Join(", ", missing)}. They will see the Moon instead of Mars.");
            }
            Plugin.Log.LogMessage($"[{why}] Host starts slot {slot}: {(MarsState.CurrentIsMars ? "MARS (terrain profile " + MarsState.CurrentProfile + ")" : "Moon")}");
        }

        static bool JoinCrewShift_Pre()
        {
            DecideAsClient("JoinCrewShift");
            return true;
        }

        static void DecideAsClient(string why)
        {
            var host = Lobby.Read();
            MarsState.CurrentIsMars = host?.mars ?? false;
            MarsState.CurrentProfile = host?.profile ?? 0;
            MarsState.StormSeed = Lobby.HostStormSeed;
            MarsState.WorldStartedUtc = DateTime.UtcNow;
            if (host == null) Plugin.Log.LogMessage($"[{why}] Joining a host without the Mars mod: Moon");
            else
            {
                Plugin.Log.LogMessage($"[{why}] Joining a {(host.Value.mars ? "MARS" : "Moon")} host (host mod {host.Value.hostVersion}, ours {Plugin.Version})");
                if (host.Value.mars && host.Value.profile > MarsProfile.LatestTerrainProfile)
                    MarsWorld.Warn($"The host uses a newer Mars mod ({host.Value.hostVersion}). Please update, or the terrain won't match.");
            }
        }

        // ================================================================= world

        static void World_Pre(MoonWorld __instance, MethodBase __originalMethod)
        {
            try
            {
                string from = "MoonWorld." + __originalMethod?.Name;
                if (Lobby.IsClient) DecideAsClient(from);
                else
                {
                    bool mars = MarsState.IsMarsSlot(MarsState.ActiveSlot);
                    if (mars != MarsState.CurrentIsMars)
                    {
                        Plugin.Log.LogWarning($"[{from}] mode corrected to {(mars ? "Mars" : "Moon")} from the active slot");
                        MarsState.CurrentIsMars = mars;
                        MarsState.CurrentProfile = mars ? MarsState.ProfileOf(MarsState.ActiveSlot) : 0;
                    }
                }
                SettingsSwap.Ensure(__instance.settings, MarsState.CurrentIsMars, MarsState.CurrentProfile, from);
            }
            catch (Exception e) { Plugin.Log.LogError("World hook failed: " + e); }
        }

        /// <summary>The slot the player last pressed Play on (the intro's slot when MoonNewHire isn't up yet).</summary>
        public static int LastPlaySlot = -1;

        static void LoadText_Post(string path, ref string __result)
        {
            try
            {
                int slot = LastPlaySlot;
                try { var inst = MoonNewHire.instance; if (inst != null) slot = inst.slot; } catch { }
                if (!MarsState.IsMarsSlot(slot) || string.IsNullOrEmpty(__result) || path == null) return;
                if (path.EndsWith("PlayerContract"))
                {
                    __result = Contract.ForMars(__result);
                    Plugin.Log.LogMessage($"Mars contract loaded for slot {slot}");
                }
            }
            catch (Exception e) { Log.Once("loadtext-err", "Mars contract text failed: " + e.Message); }
        }

        static void WorldDestroyed_Post(MoonWorld __instance)
        {
            try
            {
                SettingsSwap.Restore(__instance.settings, "MoonWorld.OnDestroy");
                Look.RestoreAssets();
            }
            catch (Exception e) { Plugin.Log.LogWarning("Restore after world failed: " + e.Message); }
        }

        static void Frame_Post()
        {
            try { MarsWorld.Frame(); }
            catch (Exception e) { Log.Once("frame-err", "Mars frame tick failed: " + e); }
        }

        static void Flow_Post(MethodBase __originalMethod)
        {
            Plugin.Log.LogInfo($"[flow] {__originalMethod?.DeclaringType?.Name}.{__originalMethod?.Name} (active slot {MarsState.ActiveSlot})");
        }
    }

    /// <summary>The "Moon or Mars?" question, shown in the game's own dialog on the start screen.</summary>
    internal static class Choice
    {
        /// <summary>The Moon/Mars dialog while it is open.</summary>
        public static MoonSimpleDialog Current;

        /// <summary>Every frame: Esc closes our question and goes back to the slots.</summary>
        public static void Frame()
        {
            if (Current == null) return;
            try
            {
                if (!Current.Open) { Current = null; return; }
                if (BepInEx.UnityInput.Current.GetKeyDown(KeyCode.Escape))
                {
                    Current.Hide();
                    Current = null;
                    Plugin.Log.LogMessage("Moon/Mars question closed with Esc");
                }
            }
            catch { Current = null; }
        }

        public static bool Ask(int slot, Action<int> resume)
        {
            try
            {
                MoonMainMenu menu = null;
                foreach (var m in Look.All<MoonMainMenu>()) { if (Look.InScene(m.gameObject)) { menu = m; break; } }
                var dialog = menu?.dialog;
                if (dialog == null)
                {
                    Plugin.Log.LogWarning("No menu dialog found, the new slot starts as a Moon world");
                    return false;
                }
                // Continue on the next frame, after the dialog has closed itself.
                Action mars = () =>
                {
                    MarsState.MarkMars(slot);
                    MarsWorld.Defer(() => resume(slot));
                };
                Action moon = () =>
                {
                    // Esc closes the dialog through its cancel button: that means "back", not "Moon".
                    bool esc = false;
                    try { esc = BepInEx.UnityInput.Current.GetKey(KeyCode.Escape); } catch { }
                    if (esc) { Plugin.Log.LogMessage($"Slot {slot}: Moon/Mars question closed with Esc, back to the slots"); return; }
                    Plugin.Log.LogMessage($"Slot {slot}: player chose the Moon");
                    MarsWorld.Defer(() => resume(slot));
                };
                const string title = "WHERE DOES THIS CREW WORK?";
                string body = $"Slot {slot} starts a new world. The Moon is the classic job. Mars is red, dusty and has stronger gravity. This can't be changed later.  (Esc = back)";
                foreach (var t in new[] { title, body, "MARS", "MOON" }) Text.Exempt(t);
                Current = dialog;
                dialog.Show(title, body, "MARS",
                    DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(mars),
                    "MOON",
                    DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(moon));
                Plugin.Log.LogMessage($"Asked Moon or Mars for slot {slot}");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Could not show the Moon/Mars choice, starting as Moon: " + e);
                return false;
            }
        }
    }
}
