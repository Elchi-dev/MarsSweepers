using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.Win32;

namespace MarsSetup
{
    /// <summary>
    /// Finds out why BepInEx does or does not start, from facts on this PC instead of guesses:
    ///  * the running game process: its command line, its parent, and whether it loaded BepInEx's winhttp.dll
    ///    (from the game folder, not Windows') and the .NET runtime BepInEx brings (coreclr.dll);
    ///  * every loader file compared byte for byte with the BepInEx build in this setup;
    ///  * Windows' own records: Smart App Control, Code Integrity blocks, Defender detections.
    /// </summary>
    internal static partial class Program
    {
        internal static class Diagnose
        {
            public static readonly List<string> Lines = new List<string>();
            static void L(string s) { lock (Lines) Lines.Add(s); }

            /// <summary>Everything that can be checked without the game running.</summary>
            public static void Static()
            {
                lock (Lines) Lines.Clear();
                L("checked " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                Files();
                SteamFacts();
                foreach (var c in CompatFindings()) L("windows setting: " + c);
                if (CompatFindings().Count == 0) L("no compatibility layer, no compatibility assistant record and no image options for the game exe");
                WindowsFacts();
                Policy();
                CodeIntegrity();
                Defender();
            }

            /// <summary>On a game start: the static checks, then watch the process for 90 s.</summary>
            public static void Watch()
            {
                Static();
                var t = new Thread(() =>
                {
                    try { WatchProcess(); } catch (Exception e) { L("process watch failed: " + e.Message); }
                }) { IsBackground = true };
                t.Start();
            }

            static void Files()
            {
                try
                {
                    using (var z = new ZipArchive(Res("BepInEx.zip"), ZipArchiveMode.Read))
                    {
                        foreach (var name in new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "dotnet/coreclr.dll",
                                                     "dotnet/hostfxr.dll", "BepInEx/core/BepInEx.Unity.IL2CPP.dll", "BepInEx/core/BepInEx.Core.dll",
                                                     "BepInEx/core/BepInEx.Preloader.Core.dll", "BepInEx/core/Il2CppInterop.Runtime.dll" })
                        {
                            string path = P(name.Replace('/', Path.DirectorySeparatorChar));
                            var e = z.Entries.FirstOrDefault(x => x.FullName.Replace('\\', '/') == name);
                            if (!File.Exists(path)) { L($"file {name}: MISSING"); continue; }
                            string have; using (var s = File.OpenRead(path)) have = Sha(s);
                            string want = null; if (e != null) using (var s = e.Open()) want = Sha(s);
                            var fi = new FileInfo(path);
                            L($"file {name}: {(want == null ? "not in setup" : have == want ? "same as BepInEx be.791" : "DIFFERENT from BepInEx be.791")}, {fi.Length} bytes, changed {fi.LastWriteTime:yyyy-MM-dd HH:mm}{(HasMotw(path) ? ", marked as downloaded from the internet" : "")}");
                        }
                        int dot = Directory.Exists(P("dotnet")) ? Directory.GetFiles(P("dotnet")).Length : 0;
                        int dotWant = z.Entries.Count(x => x.FullName.Replace('\\', '/').StartsWith("dotnet/") && x.Length > 0 && x.FullName.Count(c => c == '/' || c == '\\') == 1);
                        L($"dotnet folder: {dot} files (BepInEx be.791 has {dotWant})");
                    }
                    var log = new FileInfo(P("BepInEx", "LogOutput.log"));
                    if (log.Exists) L($"BepInEx LogOutput.log last written {log.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                    foreach (var f in Directory.GetFiles(game))
                    {
                        string n = Path.GetFileName(f).ToLowerInvariant();
                        if (n.EndsWith(".dll") && !new[] { "baselib.dll", "dstorage.dll", "dstoragecore.dll", "gameassembly.dll", "unityplayer.dll", "winhttp.dll", "steam_api64.dll" }.Contains(n))
                            L("extra dll in the game folder: " + Path.GetFileName(f));
                    }
                }
                catch (Exception e) { L("file check failed: " + e.Message); }
            }

            /// <summary>Things about Windows that decide whether a game loads a winhttp.dll from its own folder.</summary>
            static void WindowsFacts()
            {
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                        L($"Windows {k?.GetValue("ProductName")} {k?.GetValue("DisplayVersion")} build {k?.GetValue("CurrentBuild")}.{k?.GetValue("UBR")}, channel {k?.GetValue("BuildBranch")}");
                }
                catch (Exception e) { L("Windows version unreadable: " + e.Message); }
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\KnownDLLs"))
                    {
                        var names = k?.GetValueNames().Select(n => (k.GetValue(n) ?? "").ToString()).ToList() ?? new List<string>();
                        bool known = names.Any(n => n.Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase));
                        L($"winhttp.dll is {(known ? "a KnownDLL: Windows always loads its own copy, the game folder's is ignored" : "not a KnownDLL")}; KnownDLLs: {string.Join(" ", names.Take(60))}");
                    }
                }
                catch (Exception e) { L("KnownDLLs unreadable: " + e.Message); }
                try
                {
                    foreach (var dll in new[] { "UnityPlayer.dll", "MoonSweepers.exe" })
                    {
                        var bytes = File.ReadAllBytes(P(dll));
                        var text = System.Text.Encoding.ASCII.GetString(bytes);
                        var hits = new[] { "winhttp.dll", "version.dll", "winmm.dll" }.Where(n => text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
                        L($"{dll} names: {string.Join(", ", hits)}");
                    }
                }
                catch (Exception e) { L("import check failed: " + e.Message); }
                try
                {
                    using (var s = new ManagementObjectSearcher("SELECT HotFixID, InstalledOn, Description FROM Win32_QuickFixEngineering"))
                    {
                        var fixes = s.Get().Cast<ManagementObject>().Select(o => $"{o["InstalledOn"]} {o["HotFixID"]} {o["Description"]}").OrderByDescending(x => x).Take(6);
                        L("latest Windows updates: " + string.Join(" | ", fixes));
                    }
                }
                catch (Exception e) { L("update list unreadable: " + e.Message); }
                try
                {
                    var q = new EventLogQuery("System", PathType.LogName,
                        "*[System[Provider[@Name='Microsoft-Windows-WindowsUpdateClient'] and (EventID=19) and TimeCreated[timediff(@SystemTime) <= 259200000]]]") { ReverseDirection = true };
                    int n = 0;
                    using (var r = new EventLogReader(q))
                        for (EventRecord e = r.ReadEvent(); e != null && n < 12; e = r.ReadEvent(), n++)
                        {
                            string m = ""; try { m = e.FormatDescription() ?? ""; } catch { }
                            L($"update installed {e.TimeCreated:yyyy-MM-dd HH:mm}: {m.Replace("\r", " ").Replace("\n", " ")}");
                        }
                    if (n == 0) L("no Windows updates installed in the last 3 days");
                }
                catch (Exception e) { L("update events unreadable: " + e.Message); }
            }

            /// <summary>Steam's own version and its update history: Steam starts the game, so a Steam update can change how.</summary>
            static void SteamFacts()
            {
                try
                {
                    string root = SteamLibraries().FirstOrDefault(r => File.Exists(Path.Combine(r, "steam.exe")));
                    if (root == null) { L("Steam folder not found"); return; }
                    var exe = new FileInfo(Path.Combine(root, "steam.exe"));
                    L($"steam.exe {FileVersionInfo.GetVersionInfo(exe.FullName).FileVersion}, changed {exe.LastWriteTime:yyyy-MM-dd HH:mm}");
                    foreach (var dll in new[] { "steamclient64.dll", "gameoverlayrenderer64.dll", "GameOverlayUI.exe" })
                    {
                        var f = new FileInfo(Path.Combine(root, dll));
                        if (f.Exists) L($"steam {dll}: changed {f.LastWriteTime:yyyy-MM-dd HH:mm}, {f.Length} bytes");
                    }
                    var manifest = Path.Combine(root, "package", "steam_client_win64.manifest");
                    if (File.Exists(manifest)) L($"steam client manifest: changed {File.GetLastWriteTime(manifest):yyyy-MM-dd HH:mm}, {File.ReadLines(manifest).Take(3).Aggregate((a, b) => a + " " + b).Replace("\t", " ")}");
                    var boot = Path.Combine(root, "logs", "bootstrap_log.txt");
                    if (File.Exists(boot))
                        foreach (var line in File.ReadAllLines(boot).Where(l => l.IndexOf("update", StringComparison.OrdinalIgnoreCase) >= 0 || l.IndexOf("version", StringComparison.OrdinalIgnoreCase) >= 0).Reverse().Take(15).Reverse())
                            L("steam bootstrap: " + line);
                }
                catch (Exception e) { L("steam facts failed: " + e.Message); }
            }

            static bool HasMotw(string path)
            {
                try { return File.Exists(path + ":Zone.Identifier"); } catch { return false; }
            }

            static void Policy()
            {
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CI\Policy"))
                    {
                        var v = k?.GetValue("VerifiedAndReputablePolicyState");
                        string s = v == null ? "not set" : v.ToString() == "0" ? "off" : v.ToString() == "1" ? "ON (blocks unsigned files it does not know)" : v.ToString() == "2" ? "evaluation mode" : v.ToString();
                        L("Smart App Control: " + s);
                    }
                }
                catch (Exception e) { L("Smart App Control state unreadable: " + e.Message); }
            }

            static void CodeIntegrity()
            {
                try
                {
                    var q = new EventLogQuery("Microsoft-Windows-CodeIntegrity/Operational", PathType.LogName,
                        "*[System[TimeCreated[timediff(@SystemTime) <= 604800000]]]") { ReverseDirection = true };
                    int shown = 0, seen = 0;
                    using (var r = new EventLogReader(q))
                        for (EventRecord e = r.ReadEvent(); e != null && seen < 5000; e = r.ReadEvent(), seen++)
                        {
                            string msg = "";
                            try { msg = e.FormatDescription() ?? ""; } catch { }
                            if (msg.IndexOf("Moon Sweepers", StringComparison.OrdinalIgnoreCase) < 0 && msg.IndexOf("winhttp", StringComparison.OrdinalIgnoreCase) < 0
                                && msg.IndexOf("coreclr", StringComparison.OrdinalIgnoreCase) < 0 && msg.IndexOf("BepInEx", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            L($"code integrity {e.TimeCreated:yyyy-MM-dd HH:mm} id {e.Id}: {msg.Replace("\r", " ").Replace("\n", " ")}");
                            if (++shown >= 12) break;
                        }
                    L($"code integrity log: {seen} events of the last 7 days read, {shown} about the game or BepInEx");
                }
                catch (Exception e) { L("code integrity log unreadable: " + e.Message); }
            }

            static void Defender()
            {
                try
                {
                    var psi = new ProcessStartInfo("powershell.exe",
                        "-NoProfile -NonInteractive -Command \"Get-MpThreatDetection | Select-Object -First 15 InitialDetectionTime,ActionSuccess,Resources | Format-List | Out-String -Width 400\"")
                    { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                    using (var p = Process.Start(psi))
                    {
                        string o = p.StandardOutput.ReadToEnd(), err = p.StandardError.ReadToEnd();
                        p.WaitForExit(20000);
                        var rel = o.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                        bool any = false;
                        foreach (var x in rel)
                            if (x.IndexOf("Moon Sweepers", StringComparison.OrdinalIgnoreCase) >= 0 || x.IndexOf("BepInEx", StringComparison.OrdinalIgnoreCase) >= 0 || x.IndexOf("winhttp", StringComparison.OrdinalIgnoreCase) >= 0)
                            { L("Defender detection: " + x); any = true; }
                        L(any ? "Defender: detections about the game folder above" : $"Defender: no detection about the game folder ({rel.Count} lines read{(err.Trim().Length > 0 ? ", " + err.Trim().Split('\n')[0] : "")})");
                    }
                }
                catch (Exception e) { L("Defender history unreadable: " + e.Message); }
            }

            static void WatchProcess()
            {
                string exe = Path.GetFileNameWithoutExtension(GameExe);
                var seenPids = new HashSet<int>();
                var until = DateTime.Now.AddSeconds(90);
                while (DateTime.Now < until)
                {
                    foreach (var p in Process.GetProcessesByName(exe))
                    {
                        if (!seenPids.Add(p.Id)) continue;
                        L($"game process {p.Id} image load policy at first sight: {ImageLoadPolicy(p)}");
                        L($"game process {p.Id} started {Safe(() => p.StartTime.ToString("HH:mm:ss"))}, command line: {CommandLine(p.Id)}, parent: {Parent(p.Id)}");
                        int pid = p.Id;
                        new Thread(() => Modules(pid)) { IsBackground = true }.Start();
                    }
                    Thread.Sleep(100);
                }
                L($"{seenPids.Count} game process(es) seen in the first 90 s");
            }

            static void Modules(int pid)
            {
                // Look early and again later: winhttp.dll is loaded within the first seconds, BepInEx's runtime soon after.
                // Every second for 20 s: a start that dies early still leaves its last module list behind.
                for (int i = 0; i < 20; i++)
                {
                    Thread.Sleep(i == 0 ? 700 : 1000);
                    if (!ModulesOnce(pid, i == 19 || i == 2)) { if (lastMods.TryGetValue(pid, out var last)) Dump(pid, last, "last seen before it exited"); return; }
                }
            }

            static readonly HashSet<string> listed = new HashSet<string>();

            /// <summary>All modules in load order: whatever loaded Windows' winhttp.dll before the game asked for it shows up before it.</summary>
            static void Dump(int pid, List<string> mods, string when)
            {
                string key = pid + "/" + mods.Count;
                if (!listed.Add(key)) return;
                string win32 = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                L($"process {pid} modules ({when}, {mods.Count}):");
                int i = 0;
                foreach (var m in mods)
                {
                    string where = m.StartsWith(game, StringComparison.OrdinalIgnoreCase) ? "game: " + m.Substring(game.Length).TrimStart('\\')
                                 : m.StartsWith(win32, StringComparison.OrdinalIgnoreCase) ? "windows: " + m.Substring(win32.Length).TrimStart('\\')
                                 : "OTHER: " + m;
                    L($"process {pid} module {i++:00}: {where}");
                }
            }

            static readonly Dictionary<int, List<string>> lastMods = new Dictionary<int, List<string>>();

            static bool ModulesOnce(int pid, bool report)
            {
                try
                {
                    var p = Process.GetProcessById(pid);
                    var mods = p.Modules.Cast<ProcessModule>().Select(m => m.FileName).ToList();
                    lastMods[pid] = mods;
                    if (!report) return true;
                    L($"process {pid} image load policy: {ImageLoadPolicy(p)}");
                    string win = mods.FirstOrDefault(m => Path.GetFileName(m).Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase));
                    string clr = mods.FirstOrDefault(m => Path.GetFileName(m).Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase));
                    L($"process {pid}: winhttp.dll loaded from {(win ?? "nowhere")}; coreclr.dll {(clr ?? "NOT loaded (BepInEx's .NET did not start)")}; {mods.Count} modules");
                    Dump(pid, mods, "loaded");
                    return true;
                }
                catch (Exception e) { L($"process {pid}: modules unreadable ({e.Message}), it may have exited (Steam restarts games it did not start itself)"); return false; }
            }

            static string CommandLine(int pid)
            {
                try
                {
                    using (var s = new ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + pid))
                        foreach (ManagementObject o in s.Get()) return (o["CommandLine"] as string) ?? "?";
                }
                catch (Exception e) { return "unreadable (" + e.Message + ")"; }
                return "?";
            }

            static string Parent(int pid)
            {
                try
                {
                    using (var s = new ManagementObjectSearcher("SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = " + pid))
                        foreach (ManagementObject o in s.Get())
                        {
                            int ppid = Convert.ToInt32(o["ParentProcessId"]);
                            try { return Process.GetProcessById(ppid).ProcessName + " (" + ppid + ")"; } catch { return ppid.ToString(); }
                        }
                }
                catch { }
                return "?";
            }

            static string Safe(Func<string> f) { try { return f(); } catch { return "?"; } }
        }
    }
}
