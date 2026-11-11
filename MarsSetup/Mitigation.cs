using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace MarsSetup
{
    /// <summary>
    /// Windows starts MoonSweepers.exe with the exploit protection rule "prefer system images" (PreferSystem32):
    /// for every DLL, Windows' own folders are searched before the game's folder. BepInEx's loader is a
    /// winhttp.dll in the game folder, and Windows has its own winhttp.dll, so the loader is never used.
    /// This writes down where the rule comes from (Get-ProcessMitigation) and switches it off for the game only.
    /// </summary>
    internal static partial class Program
    {
        static void FixPreferSystem32()
        {
            if (!WaitForGameClosed()) return;
            if (File.Exists(P("winhttp.dll.off"))) UseWinhttpProxy();
            Head("Switching off \"prefer system images\" for Moon Sweepers");
            string dir = Path.Combine(Path.GetTempPath(), "MarsSetupMitigation");
            Directory.CreateDirectory(dir);
            string log = Path.Combine(dir, "mitigation.txt"), done = Path.Combine(dir, "done");
            try { File.Delete(log); File.Delete(done); } catch { }
            string ps1 = Path.Combine(dir, "fix.ps1");
            File.WriteAllText(ps1,
                "$o = '" + log + "'\r\n" +
                "'=== before: Get-ProcessMitigation -Name MoonSweepers.exe' | Out-File $o -Encoding utf8\r\n" +
                "Get-ProcessMitigation -Name MoonSweepers.exe | Out-String -Width 300 | Out-File $o -Append -Encoding utf8\r\n" +
                "'=== system defaults' | Out-File $o -Append -Encoding utf8\r\n" +
                "Get-ProcessMitigation -System | Out-String -Width 300 | Out-File $o -Append -Encoding utf8\r\n" +
                "'=== registry: Image File Execution Options entries for the game (all subkeys)' | Out-File $o -Append -Encoding utf8\r\n" +
                "Get-ChildItem 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Image File Execution Options' -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Name -like '*MoonSweepers*' -or (($_ | Get-ItemProperty -ErrorAction SilentlyContinue).FilterFullPath -like '*Moon Sweepers*') } | ForEach-Object { $_.Name; ($_ | Get-ItemProperty | Out-String -Width 300) } | Out-File $o -Append -Encoding utf8\r\n" +
                "'=== setting PreferSystem32 OFF for MoonSweepers.exe' | Out-File $o -Append -Encoding utf8\r\n" +
                "try { Set-ProcessMitigation -Name MoonSweepers.exe -Disable PreferSystem32 -ErrorAction Stop; 'ok' | Out-File $o -Append -Encoding utf8 } catch { $_ | Out-String | Out-File $o -Append -Encoding utf8 }\r\n" +
                "'=== after' | Out-File $o -Append -Encoding utf8\r\n" +
                "Get-ProcessMitigation -Name MoonSweepers.exe | Out-String -Width 300 | Out-File $o -Append -Encoding utf8\r\n" +
                "New-Item -ItemType File -Path '" + done + "' -Force | Out-Null\r\n");
            Info("Windows asks for permission once: exploit protection settings need it.");
            try
            {
                Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + ps1 + "\"")
                { Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
            }
            catch (Exception e) { Err("Not started (permission refused?): " + e.Message); return; }
            var until = DateTime.Now.AddSeconds(90);
            while (!File.Exists(done) && DateTime.Now < until) Thread.Sleep(300);
            if (!File.Exists(log)) { Err("No result came back."); return; }
            string text = File.ReadAllText(log);
            string keep = P("MarsProbe", "mitigation.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(keep));
            File.WriteAllText(keep, text);
            bool ok = System.Text.RegularExpressions.Regex.IsMatch(text, @"PreferSystem32 OFF for MoonSweepers\.exe\s*\r?\nok");
            if (ok) Ok("\"Prefer system images\" is now off for Moon Sweepers. Starting the game: the BepInEx console should open.");
            else { Warn("Windows did not accept the change. The details are in MarsProbe\\mitigation.txt and go into the report."); }
            RunAndCollect();
        }
    }
}
