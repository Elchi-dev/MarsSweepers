using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using MoonCleaners;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// Runs every frame: keeps the Mars look and haze on, shows Mars messages, checks where saves land.
    /// It is driven by a hook on a game component that lives for the whole session (the crash-report
    /// heartbeat and the frame pacer), not by a component of our own: adding our own component to the
    /// scene crashed the game in 0.3.0.
    /// </summary>
    internal static class MarsWorld
    {
        static float next, menuSince = -1f, nextSaveCheck, nextPublish, nextNotice, lastFrame;
        static bool inWorldLastTick, slotsLogged, marsWorld, startedOk;
        static int lastFrameCount = -1;
        static float nextLoadingTint, walkerSince = -1f, introEndedAt = -1f;
        static int introEndedSlot = -1;

        /// <summary>A Mars world is being played right now.</summary>
        public static bool InMarsWorld => marsWorld;
        static bool goalsShown;
        /// <summary>A Mars world is loading (loading screen up).</summary>
        public static bool LoadingMars { get; private set; }
        /// <summary>The player has landed and can see the HUD (messages wait for this).</summary>
        public static bool PlayerReady { get; private set; }
        static MoonSolarSystem solar;

        static readonly Queue<Action> deferred = new Queue<Action>();
        static readonly Queue<(string text, bool warning)> notices = new Queue<(string, bool)>();

        /// <summary>Run something on the next frame (e.g. after a dialog has fully closed).</summary>
        public static void Defer(Action a) { lock (deferred) deferred.Enqueue(a); }

        /// <summary>Show a message on the player's HUD (queued, one at a time).</summary>
        public static void Notice(string text) { lock (notices) notices.Enqueue((text, false)); }

        /// <summary>Show this next, right away (storm warnings).</summary>
        public static void Urgent(string text)
        {
            lock (notices)
            {
                var rest = notices.ToArray();
                notices.Clear();
                notices.Enqueue((text, false));
                foreach (var n in rest) notices.Enqueue(n);
            }
            nextNotice = 0f;
        }

        public static void Warn(string text)
        {
            Plugin.Log.LogWarning(text);
            lock (notices) notices.Enqueue((text, true));
        }

        /// <summary>Called from the game's own per-frame hooks; runs at most once per frame.</summary>
        public static void Frame()
        {
            int frame = Time.frameCount;
            if (frame == lastFrameCount) return;
            lastFrameCount = frame;
            if (!startedOk)
            {
                startedOk = true;
                Guard.End("startup");
                Plugin.Log.LogMessage("Mars mod is running");
            }
            Update();
        }

        static void Update()
        {
            while (true)
            {
                Action a;
                lock (deferred) { if (deferred.Count == 0) break; a = deferred.Dequeue(); }
                try { a(); } catch (Exception e) { Plugin.Log.LogError("Deferred action failed: " + e); }
            }

            float now = Time.realtimeSinceStartup;
            float dt = Mathf.Clamp(now - lastFrame, 0f, 0.5f);
            lastFrame = now;

            // Mars wording without hooking the game: rewrite visible texts and the intro slides directly.
            try { Terminal.Frame(now); Commands.Frame(now, dt); }
            catch (Exception e) { Log.Once("terminal-frame", "Dev terminal failed: " + e); }

            try { Text.Frame(now); }
            catch (Exception e) { Log.Once("text-frame", "Mars text pass failed: " + e.Message); }

            try { Economy.Tick(now); } catch { }
            try { VanillaBuild.Frame(); } catch (Exception e) { Log.Once("vbuild-frame", "Mars build menu: " + e.Message); }
            // Every frame while on Mars: buildings, the build menu, and the haze following the camera.
            if (marsWorld)
            {
                try { VanillaBuild.PlacementFrame(); Buildings.Tick(now, dt); Goals.Tick(now); Meteors.Tick(now); Market.Tick(now); LightPoles.Tick(); Shields.Frame(dt); Ending.Frame(); Ending.After(); if (Camera.main != null) DustTiers.Frame(Camera.main.transform.position); BuildMode.Frame(now, dt); }
                catch (Exception e) { Log.Once("build-frame", "Mars buildings: " + e); }
                try
                {
                    Haze.Tick(dt, solar);
                }
                catch (Exception e) { Log.Once("haze-tick", "Haze tick: " + e.Message); }
            }

            // Loading screen: dress it for Mars quickly (the loading planet and dust), the 1 s tick is too slow.
            if (now >= nextLoadingTint)
            {
                nextLoadingTint = now + 0.3f;
                try
                {
                    bool inGame = MoonSession.InGame, inMenu = MoonSession.InMenu, loading = MoonSession.Loading;
                    bool screen = loading || (!inMenu && !inGame);
                    bool mars = MarsState.CurrentIsMars || (!Lobby.IsClient && MarsState.IsMarsSlot(MarsState.ActiveSlot) && Patches.LastPlaySlot == MarsState.ActiveSlot);
                    LoadingMars = screen && mars;
                    if (LoadingMars) { Look.IntroPlanet(); Dust.Objects(); }
                }
                catch (Exception e) { Log.Once("loading-tint", "Loading screen tint: " + e.Message); }
            }

            Choice.Frame();
            MarsNet.Poll();
            try { Models.Animate(dt); } catch { }
            MarsData.Tick(now);
            try { Research.Frame(); } catch (Exception e) { Log.Once("research-frame", "Research: " + e.Message); }
            try { Logo.Tick(now, marsWorld); }
            catch (Exception e) { Log.Once("logo-tick", "Logo: " + e.Message); }

            if (now < next) return;
            next = now + 1f;
            try { Tick(now); }
            catch (Exception e) { Plugin.Log.LogWarning("MarsWorld tick: " + e.Message); }
        }

        static void Tick(float now)
        {
            bool inGame = false, inMenu = false, loading = false;
            try { inGame = MoonSession.InGame; inMenu = MoonSession.InMenu; loading = MoonSession.Loading; } catch { }

            SyncWorldSettings();

            if (MoonSteamLobby.InLobby) Lobby.FlagSelf();

            if (inMenu)
            {
                LeaveMarsWorld();
                if (loading)
                {
                    introEndedAt = -1f;
                    // A world is loading: keep the mode it was started with, and dress the loading screen for Mars.
                    menuSince = -1f;
                    if (MarsState.CurrentIsMars) { Look.IntroPlanet(); Dust.Objects(); }
                    return;
                }
                if (menuSince < 0) menuSince = now;
                if (!slotsLogged) { slotsLogged = true; LogSlots(); }
                try
                {
                    if (Text.IntroIsMars && MoonNewHire.Active) Look.IntroPlanet();
                    else if (Text.IntroIsMars) { Text.IntroIsMars = false; Look.RestoreAssets(); introEndedAt = now; introEndedSlot = Patches.LastPlaySlot; }
                    // The Mars intro was cancelled (Esc) and no world started: the slot is free to choose again.
                    if (introEndedAt > 0f && now - introEndedAt > 6f)
                    {
                        int s = introEndedSlot; introEndedAt = -1f;
                        if (s >= 0 && MarsState.IsMarsSlot(s) && !MoonSaveSlots.Used(s) && !MoonNewHire.Active)
                        {
                            MarsState.Unmark(s);
                            Patches.Forget(s);
                            Plugin.Log.LogMessage($"Mars intro for slot {s} was cancelled: the slot asks Moon or Mars again");
                        }
                    }
                }
                catch { }
                // Give a save that started while leaving the world time to finish before we go back to Moon paths.
                if ((MarsState.CurrentIsMars || Lobby.HostingMars) && now - menuSince > 10f)
                {
                    MarsState.CurrentIsMars = false;
                    Lobby.HostingMars = false;
                    Lobby.ResetPublished();
                    Plugin.Log.LogMessage("Back in the menu: Mars mode off");
                }
                if (Look.Applied && !Text.IntroIsMars) Look.RestoreAssets();
                inWorldLastTick = false;
                return;
            }
            menuSince = -1f;
            if (!inGame) return;
            Text.IntroIsMars = false;

            if (!inWorldLastTick)
            {
                inWorldLastTick = true;
                Log.ResetWorld();
                nextSaveCheck = now + 45f;
                solar = null;
            }

            if (!MarsState.CurrentIsMars)
            {
                LeaveMarsWorld();
                Poster.Tick(false);
                if (Look.Applied) Look.RestoreAssets();
                return;
            }

            if (!marsWorld) MarsData.Open(MarsState.SlotDir(MarsState.ActiveSlot), !Lobby.IsClient);
            marsWorld = true;
            try { VanillaBuild.Install(Commands.FindWorld()); } catch (Exception e) { Log.Once("vbuild-install", "Mars build install: " + e.Message); }
            if (solar == null) solar = Look.All<MoonSolarSystem>().FirstOrDefault(s => Look.InScene(s.gameObject));
            Look.Apply(now);
            Poster.Tick(true);
            // Wait until the player has really landed before talking (the world reports "in game" earlier).
            var walker = Commands.FindWalker();
            bool grounded = false;
            try { grounded = walker != null && walker.Grounded; } catch { }
            if (walker == null || MoonSession.Loading) { walkerSince = -1f; PlayerReady = false; }
            else if (walkerSince < 0f && grounded) walkerSince = now;
            PlayerReady = walkerSince >= 0f && now - walkerSince > 4f;
            if (PlayerReady) Greet();
            if (PlayerReady && !goalsShown && MarsData.Loaded) { goalsShown = true; Goals.Opened(); }
            if (PlayerReady) { try { Mines.Ensure(); } catch (Exception e) { Log.Once("mines", "Mines: " + e.Message); } }
            if (PlayerReady && !Log.Seen("terrain-check"))
            {
                Log.Once("terrain-check", "Terrain check:");
                try { foreach (var l in Commands.TerrainReport()) Plugin.Log.LogMessage("[terrain] " + l); }
                catch (Exception e) { Plugin.Log.LogWarning("[terrain] check failed: " + e.Message); }
            }

            if (now >= nextPublish && !Lobby.IsClient)
            {
                nextPublish = now + 10f;
                Lobby.Publish(true, MarsState.CurrentProfile);
            }
            if (now >= nextSaveCheck && !Lobby.IsClient)
            {
                nextSaveCheck = now + 30f;
                CheckSaves();
            }
            if (now >= nextNotice && PlayerReady) ShowNextNotice(now);
        }

        /// <summary>Per-world settings: the Mars world being played (or loaded) uses its own file, everything else the defaults.</summary>
        public static void SyncWorldSettings()
        {
            MarsProfile.UseWorld(!MarsState.CurrentIsMars ? MarsProfile.DefaultsPath
                : Lobby.IsClient ? MarsState.MarsRoot + "/joined-world.cfg"
                : MarsState.SlotDir(MarsState.ActiveSlot) + "/marsmod-world.cfg");
            if (MarsState.CurrentIsMars && !Lobby.IsClient)
            {
                if (MarsProfile.StormSeed.Value == 0) MarsProfile.StormSeed.Value = new System.Random().Next(1, int.MaxValue);
                MarsState.StormSeed = MarsProfile.StormSeed.Value;
            }
        }

        static void LeaveMarsWorld()
        {
            PlayerReady = false; walkerSince = -1f;
            if (!marsWorld) return;
            marsWorld = false;
            BuildMode.Reset();
            Buildings.ClearViews();
            Shields.Clear();
            DustTiers.Reset();
            Ending.Reset();
            Goals.Reset(); goalsShown = false;
            Meteors.Reset();
            VanillaBuild.Reset();
            Market.Reset();
            Mines.Clear();
            MarsData.Close();
            Models.WorldChanged();
            solar = null;
            Haze.Remove();
        }

        static void LogSlots()
        {
            try
            {
                // Diagnostics: how the game numbers its slots and folders.
                var parts = new List<string>();
                for (int i = 0; i <= MoonSaveSlots.Count; i++)
                    parts.Add($"{i}:{MoonSaveSlots.SlotFolder(i)}{(MarsState.IsMarsSlot(i) ? " (MARS)" : "")}");
                Plugin.Log.LogInfo($"[diag] active slot {MarsState.ActiveSlot}, folder {MoonSaveSlots.Folder}; slots: " + string.Join(" | ", parts));
            }
            catch (Exception e) { Plugin.Log.LogInfo("[diag] slot listing failed: " + e.Message); }
        }

        /// <summary>The first time a Mars slot is played the crew gets the story; afterwards a random greeting.</summary>
        static void Greet()
        {
            if (Log.Seen("welcome")) return;
            Log.Once("welcome", "Mars world is live");
            bool first = !Lobby.IsClient && MarsState.FirstVisit(MarsState.ActiveSlot);
            Notice(Text.Welcome(first));
            if (first) Narrator.Play("welcome.wav");
            if (first) Commands.SetTime("morning", out _);   // a new Mars world starts in the morning sun
            if (first)
            {
                Notice("Mars gravity is 3.71 m/s². Jumps are shorter, falls are harder.");
                Notice("Watch the sky: dust storms roll in now and then.");
            }
        }

        static void ShowNextNotice(float now)
        {
            (string text, bool warning) n;
            lock (notices) { if (notices.Count == 0) return; n = notices.Peek(); }
            try
            {
                MoonWalker walker = Look.All<MoonWalker>().FirstOrDefault(w => Look.InScene(w.gameObject));
                if (walker == null) return;
                var notice = MoonHudNotice.For(walker);
                if (notice == null) return;
                notice.Show(n.warning ? "MARS MOD: " + n.text : n.text, n.warning ? 10f : 7f);
                lock (notices) notices.Dequeue();
                nextNotice = now + (n.warning ? 10f : 7.5f);
            }
            catch (Exception e)
            {
                Log.Once("notice-err", "HUD notice failed: " + e.Message);
                lock (notices) notices.Dequeue();
            }
        }

        /// <summary>A Mars save must land in MarsSessions, never in the Moon slot folder.</summary>
        static void CheckSaves()
        {
            try
            {
                int slot = MarsState.ActiveSlot;
                DateTime since = MarsState.WorldStartedUtc.AddSeconds(-5);
                string moon = $"{MarsState.MoonRoot}/slot-{slot}";
                string mars = MarsState.SlotDir(slot);
                DateTime Newest(string dir) => Directory.Exists(dir)
                    ? Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max()
                    : DateTime.MinValue;

                if (Newest(moon) > since)
                {
                    if (!Log.Seen("save-leak")) Warn("The save went to the MOON slot folder. Please send the report.");
                    Log.Once("save-leak", $"WARNING: the Mars world wrote into the Moon slot folder {moon}. Tell the mod author!");
                }
                else if (Newest(mars) > since)
                {
                    Log.Once("save-ok", $"Save check OK: the Mars world saves into {mars}");
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Save check failed: " + e.Message); }
        }
    }
}
