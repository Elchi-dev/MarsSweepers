using System;
using System.Collections.Generic;
using System.IO;

namespace MarsMod
{
    /// <summary>
    /// Crash guard for risky hooks. Before the first real call of a guarded hook we leave a small file,
    /// after it returns we delete it. If the game died in between, the file is still there at the next start
    /// and that hook switches itself off, so a bad hook can never make the game unplayable.
    /// </summary>
    internal static class Guard
    {
        static string dir;
        static readonly HashSet<string> disabled = new HashSet<string>();
        static readonly HashSet<string> proven = new HashSet<string>();

        public static void Init()
        {
            dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "MarsMod", "guard");
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "*.running"))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (name == "startup")
                {
                    File.Delete(f);
                    startupCrashed = true;
                    Plugin.Log.LogError("The last start of the game crashed before the Mars mod was running. Starting in SAFE MODE.");
                    continue;
                }
                File.Move(f, Path.Combine(dir, name + ".disabled"), true);
                Plugin.Log.LogError($"The game stopped inside the '{name}' hook last time. It is switched off now. Please send the report.");
            }
            foreach (var f in Directory.GetFiles(dir, "*.disabled")) disabled.Add(Path.GetFileNameWithoutExtension(f));
        }

        public static bool Off(string name) => disabled.Contains(name);

        static bool startupCrashed;
        public static bool StartupCrashed() => startupCrashed;

        public static void Begin(string name)
        {
            if (proven.Contains(name)) return;
            try { File.WriteAllText(Path.Combine(dir, name + ".running"), DateTime.UtcNow.ToString("O")); } catch { }
        }

        public static void End(string name)
        {
            if (!proven.Add(name)) return;
            try { File.Delete(Path.Combine(dir, name + ".running")); } catch { }
        }
    }
}
