using System;
using System.Linq;
using MoonCleaners;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// The Mars ending. The game ends on the Moon with a beam and a blast in the sky; on Mars the crew builds
    /// the planet cannon, feeds it, powers it, and fires it at the Moon. The game's own ending sequence plays
    /// (MoonEnding, in its preview mode so the save is not marked as finished), with the Moon in the sky where
    /// Phobos normally hangs.
    /// </summary>
    internal static class Ending
    {
        public const int Countdown = 10;
        static float fireAt = -1f;
        static bool playing;
        static float startedAt;

        static Ending()
        {
            MarsNet.On(MarsNet.Kind.Ending, (from, r) => { if (from == Lobby.Owner()) Arm(r.ReadInt32()); });
        }

        public static void Init() { }

        /// <summary>Host: start the countdown for everyone.</summary>
        public static void Fire()
        {
            if (MarsNet.IsHost) MarsNet.Broadcast(MarsNet.Kind.Ending, w => w.Write(Countdown));
            Arm(Countdown);
        }

        static void Arm(int seconds)
        {
            fireAt = Time.realtimeSinceStartup + seconds;
            MarsWorld.Urgent($"PLANET CANNON CHARGING: {seconds} s. Look at the sky.");
            Plugin.Log.LogMessage($"[ending] countdown {seconds} s");
        }

        public static void Frame()
        {
            if (fireAt < 0f) return;
            float left = fireAt - Time.realtimeSinceStartup;
            if (left > 0f) return;
            fireAt = -1f;
            Play();
        }

        static void Play()
        {
            try
            {
                var e = Look.All<MoonEnding>().FirstOrDefault(x => Look.InScene(x.gameObject));
                if (e == null) { MarsWorld.Warn("The game's ending is not in this scene."); Plugin.Log.LogWarning("[ending] no MoonEnding in the scene"); return; }
                bool moon = Sky.ShowMoon(true);
                e.Play(true);
                playing = true; startedAt = Time.realtimeSinceStartup;
                Plugin.Log.LogMessage($"[ending] playing (moon in the sky: {moon})");
            }
            catch (Exception ex) { Plugin.Log.LogError("[ending] failed: " + ex); }
        }

        /// <summary>Back to Phobos after the sequence (or when leaving the world).</summary>
        public static void Reset()
        {
            fireAt = -1f;
            if (playing) { playing = false; Sky.ShowMoon(false); }
        }

        public static void After()
        {
            if (!playing || Time.realtimeSinceStartup - startedAt < 5f) return;
            bool running = false;
            try { running = MoonEnding.Playing; } catch { }
            if (!running) Reset();
        }
    }
}
