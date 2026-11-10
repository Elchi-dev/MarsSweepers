using System;
using System.IO;
using MoonCleaners;

namespace MarsMod
{
    /// <summary>
    /// Which world we are in, and where Mars saves live.
    ///
    /// Moon saves:  &lt;LocalLow&gt;/DragDropGames/MoonSweepers/MoonSessions/slot-N/…   (untouched)
    /// Mars saves:  &lt;LocalLow&gt;/DragDropGames/MoonSweepers/MarsSessions/slot-N/…
    /// Mars backups: …/MarsSessions/Backups/…
    /// A slot is a Mars slot when MarsSessions/slot-N/marsmod.json exists.
    /// </summary>
    internal static class MarsState
    {
        public static string Persistent;   // forward slashes, no trailing slash
        public static string MoonRoot => Persistent + "/MoonSessions";
        public static string MoonBackups => Persistent + "/Backups";
        public static string MarsRoot => Persistent + "/MarsSessions";

        /// <summary>The world being started or played is Mars. Set when a shift starts or is joined.</summary>
        public static bool CurrentIsMars;
        /// <summary>Per-world storm schedule seed (host: from the world's settings; crew: from the lobby).</summary>
        public static int StormSeed;
        /// <summary>The terrain profile of the current Mars world (stored in the slot marker, sent to crew).</summary>
        public static int CurrentProfile = MarsProfile.LatestTerrainProfile;
        public static DateTime WorldStartedUtc;

        public static void Init(string persistentDataPath)
        {
            Persistent = Norm(persistentDataPath).TrimEnd('/');
            Directory.CreateDirectory(MarsRoot);
        }

        public static string Norm(string p) => (p ?? "").Replace('\\', '/');

        public static string SlotDir(int slot) => $"{MarsRoot}/slot-{slot}";
        static string Marker(int slot) => SlotDir(slot) + "/marsmod.json";

        public static bool IsMarsSlot(int slot)
        {
            try { return slot >= 0 && File.Exists(Marker(slot)); } catch { return false; }
        }

        public static int ProfileOf(int slot)
        {
            try
            {
                string json = File.ReadAllText(Marker(slot));
                var m = System.Text.RegularExpressions.Regex.Match(json, "\"terrainProfile\"\\s*:\\s*(\\d+)");
                return m.Success ? int.Parse(m.Groups[1].Value) : 1;
            }
            catch { return 1; }
        }

        public static void MarkMars(int slot)
        {
            Directory.CreateDirectory(SlotDir(slot));
            File.WriteAllText(Marker(slot),
                "{\n" +
                "  \"mode\": \"mars\",\n" +
                $"  \"terrainProfile\": {MarsProfile.LatestTerrainProfile},\n" +
                $"  \"createdUtc\": \"{DateTime.UtcNow:O}\",\n" +
                $"  \"modVersion\": \"{Plugin.Version}\",\n" +
                "  \"note\": \"This slot is a Mars world made with the Mars mod. Without the mod installed it is simply ignored.\"\n" +
                "}\n");
            Plugin.Log.LogMessage($"Slot {slot} is now a Mars slot ({Marker(slot)})");
        }

        /// <summary>True the first time a Mars slot is entered (remembered with a small file next to the marker).</summary>
        public static bool FirstVisit(int slot)
        {
            try
            {
                string f = SlotDir(slot) + "/welcomed";
                if (File.Exists(f)) return false;
                Directory.CreateDirectory(SlotDir(slot));
                File.WriteAllText(f, DateTime.UtcNow.ToString("O"));
                return true;
            }
            catch { return false; }
        }

        /// <summary>Remove our Mars data for a slot (the player deleted it in the menu).</summary>
        public static void Unmark(int slot)
        {
            try
            {
                if (Directory.Exists(SlotDir(slot))) Directory.Delete(SlotDir(slot), true);
                Plugin.Log.LogMessage($"Mars slot {slot} deleted");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Could not delete Mars slot {slot}: {e.Message}"); }
        }

        /// <summary>Map a Moon save path to the matching Mars path. Paths outside the save folders are returned unchanged.</summary>
        public static string Redirect(string path)
        {
            if (string.IsNullOrEmpty(path) || Persistent == null) return path;
            string n = Norm(path);
            if (n.StartsWith(MarsRoot, StringComparison.OrdinalIgnoreCase)) return path;
            if (Starts(n, MoonRoot)) return MarsRoot + n.Substring(MoonRoot.Length);
            if (Starts(n, MoonBackups)) return MarsRoot + "/Backups" + n.Substring(MoonBackups.Length);
            return path;
        }

        static bool Starts(string n, string root) =>
            n.Equals(root, StringComparison.OrdinalIgnoreCase) || n.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);

        public static int ActiveSlot
        {
            get { try { return MoonSaveSlots.Active; } catch { return -1; } }
        }
    }
}
