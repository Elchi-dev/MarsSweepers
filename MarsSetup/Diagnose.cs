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
                        L($"game process {p.Id} started {Safe(() => p.StartTime.ToString("HH:mm:ss"))}, command line: {CommandLine(p.Id)}, parent: {Parent(p.Id)}");
                        int pid = p.Id;
                        new Thread(() => Modules(pid)) { IsBackground = true }.Start();
                    }
                    Thread.Sleep(1000);
                }
                L($"{seenPids.Count} game process(es) seen in the first 90 s");
            }

            static void Modules(int pid)
            {
                // BepInEx needs a few seconds to start the runtime; look at what is loaded after 25 s.
                Thread.Sleep(25000);
                try
                {
                    var p = Process.GetProcessById(pid);
                    var mods = p.Modules.Cast<ProcessModule>().Select(m => m.FileName).ToList();
                    string win = mods.FirstOrDefault(m => Path.GetFileName(m).Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase));
                    string clr = mods.FirstOrDefault(m => Path.GetFileName(m).Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase));
                    L($"process {pid}: winhttp.dll loaded from {(win ?? "nowhere")}; coreclr.dll {(clr ?? "NOT loaded (BepInEx's .NET did not start)")}; {mods.Count} modules");
                    foreach (var m in mods.Where(m => m.StartsWith(game, StringComparison.OrdinalIgnoreCase)))
                        L($"process {pid} module from the game folder: {m.Substring(game.Length).TrimStart('\\')}");
                }
                catch (Exception e) { L($"process {pid}: modules unreadable ({e.Message}), it may have exited (Steam restarts games it did not start itself)"); }
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
