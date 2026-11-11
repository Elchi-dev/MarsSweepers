using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// Mars Sweepers: a second world for Moon Sweepers.
    /// When you start a new save slot you pick Moon or Mars. Mars worlds use their own save folder,
    /// their own terrain, Mars gravity and a Mars look. Moon worlds stay exactly vanilla.
    /// </summary>
    [BepInPlugin(Guid, "Mars Sweepers", Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "marsmod.mars";
        public const string Version = "0.9.15";

        internal static new ManualLogSource Log;
        /// <summary>The previous start crashed before the mod's first frame: load only the essentials.</summary>
        internal static bool SafeMode;

        public override void Load()
        {
            Log = base.Log;
            MarsProfile.BindGlobal(Config);
            try { if (System.IO.File.Exists(MarsProfile.DefaultsPath)) System.IO.File.Delete(MarsProfile.DefaultsPath); } catch { }
            MarsProfile.UseWorld(MarsProfile.DefaultsPath);
            Config.Save();

            try { MarsState.Init(Application.persistentDataPath); }
            catch (Exception e) { Log.LogError("Mars mod could not find the save folder, disabled: " + e); return; }

            Terminal.Init();
            MarsData.Init();
            Buildings.Init();
            Market.Init();
            Guard.Init();
            SafeMode = Guard.StartupCrashed();
            Guard.Begin("startup");
            Patches.ApplyAll();

            Log.LogMessage($"Mars Sweepers {Version} ready. Mars saves: {MarsState.MarsRoot}");
        }
    }
}
