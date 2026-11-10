using System;
using System.Collections.Generic;
using System.Reflection;
using MoonCleaners;

namespace MarsMod
{
    /// <summary>
    /// Switches the game's MoonSettings asset between the Moon values and a Mars terrain profile.
    /// The asset lives in memory for the whole game session, so we keep the original Moon values
    /// and always put them back before a Moon world starts or when a Mars world ends.
    /// </summary>
    internal static class SettingsSwap
    {
        static readonly Dictionary<string, object> moonValues = new Dictionary<string, object>();
        static IntPtr capturedFrom = IntPtr.Zero;
        static IntPtr marsAppliedTo = IntPtr.Zero;
        public static int AppliedProfile;

        static PropertyInfo Prop(string name) =>
            typeof(MoonSettings).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

        public static void Ensure(MoonSettings s, bool mars, int profile, string from)
        {
            if (s == null) { Plugin.Log.LogWarning($"[{from}] MoonWorld has no settings yet"); return; }
            IntPtr ptr = s.Pointer;

            if (mars)
            {
                if (marsAppliedTo == ptr && AppliedProfile == profile) return;
                Capture(s);
                var values = MarsProfile.Terrain(profile);
                // Fields a newer profile changes but this (older) world doesn't: back to the Moon value.
                foreach (var kv in moonValues)
                    if (!values.ContainsKey(kv.Key)) try { Prop(kv.Key)?.SetValue(s, kv.Value); } catch { }
                var log = new List<string>();
                foreach (var kv in values)
                {
                    var p = Prop(kv.Key);
                    if (p == null) { Plugin.Log.LogWarning($"MoonSettings.{kv.Key} does not exist in this game version, skipped"); continue; }
                    try
                    {
                        object v = Convert.ChangeType(kv.Value, p.PropertyType);
                        p.SetValue(s, v);
                        log.Add($"{kv.Key} {moonValues[kv.Key]}→{v}");
                    }
                    catch (Exception e) { Plugin.Log.LogWarning($"MoonSettings.{kv.Key}: {e.Message}"); }
                }
                marsAppliedTo = ptr;
                AppliedProfile = profile;
                Plugin.Log.LogMessage($"[{from}] Mars terrain profile {profile} applied: " + string.Join(", ", log));
            }
            else
            {
                Restore(s, from);
            }
        }

        static void Capture(MoonSettings s)
        {
            if (capturedFrom == s.Pointer && moonValues.Count > 0) return;
            if (marsAppliedTo == s.Pointer) return; // never capture Mars values as "Moon"
            moonValues.Clear();
            foreach (var name in MarsProfile.Terrain(MarsProfile.LatestTerrainProfile).Keys)
            {
                var p = Prop(name);
                if (p != null) try { moonValues[name] = p.GetValue(s); } catch { }
            }
            capturedFrom = s.Pointer;
        }

        public static void Restore(MoonSettings s, string from)
        {
            if (s == null || marsAppliedTo == IntPtr.Zero) return;
            if (s.Pointer != marsAppliedTo) { marsAppliedTo = IntPtr.Zero; return; } // a fresh asset already has Moon values
            foreach (var kv in moonValues)
            {
                try { Prop(kv.Key)?.SetValue(s, kv.Value); } catch { }
            }
            marsAppliedTo = IntPtr.Zero;
            AppliedProfile = 0;
            Plugin.Log.LogMessage($"[{from}] Moon terrain values restored");
        }

        public static bool MarsActive => marsAppliedTo != IntPtr.Zero;
    }
}
