using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MarsSetup
{
    /// <summary>
    /// Windows settings attached to MoonSweepers.exe that change how it loads DLLs. A compatibility layer (the
    /// Compatibility tab, or one Windows' Program Compatibility Assistant sets by itself after a crash) or an
    /// Exploit protection rule makes Windows give the game its own copies of winhttp.dll and version.dll from
    /// System32, so BepInEx's loader in the game folder is never used.
    /// </summary>
    internal static partial class Program
    {
        const string Layers = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
        const string PcaStore = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store";
        const string Ifeo = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\MoonSweepers.exe";

        static bool IsGame(string valueName) => valueName != null && valueName.EndsWith("\\" + GameExe, StringComparison.OrdinalIgnoreCase);

        /// <summary>What Windows has stored for the game exe (empty when nothing).</summary>
        internal static List<string> CompatFindings()
        {
            var f = new List<string>();
            foreach (var (hive, name) in new[] { (Registry.CurrentUser, "HKCU"), (Registry.LocalMachine, "HKLM") })
            {
                try
                {
                    using (var k = hive.OpenSubKey(Layers))
                        if (k != null) foreach (var v in k.GetValueNames().Where(IsGame)) f.Add($"{name} compatibility layer on {v}: \"{k.GetValue(v)}\"");
                }
                catch (Exception e) { f.Add($"{name} layers unreadable: {e.Message}"); }
            }
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(PcaStore))
                    if (k != null) foreach (var v in k.GetValueNames().Where(IsGame)) f.Add($"Program Compatibility Assistant remembers {v}");
            }
            catch { }
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(Ifeo))
                    if (k != null) foreach (var v in k.GetValueNames()) f.Add($"Image File Execution Options for {GameExe}: {v} = {Fmt(k.GetValue(v))}");
            }
            catch (Exception e) { f.Add("image options unreadable: " + e.Message); }
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\kernel"))
                {
                    var v = k?.GetValue("MitigationOptions");
                    if (v != null) f.Add("system-wide exploit protection defaults (MitigationOptions) = " + Fmt(v));
                }
            }
            catch { }
            return f;
        }

        static string Fmt(object v) => v is byte[] b ? BitConverter.ToString(b) : (v?.ToString() ?? "null");

        /// <summary>Remove what belongs to the game exe (saved to a file first). HKLM parts need one admin prompt.</summary>
        internal static void FixCompat()
        {
            var found = CompatFindings();
            var fixable = found.Where(x => !x.Contains("unreadable") && !x.StartsWith("system-wide")).ToList();
            if (fixable.Count == 0) return;
            Head("Windows settings on the game that stop BepInEx");
            foreach (var x in found) Warn(x);
            string dir = P("MarsMod_backup"); Directory.CreateDirectory(dir);
            string backup = Path.Combine(dir, "windows-settings-" + Stamp() + ".txt");
            File.WriteAllLines(backup, found);
            Info("Saved what was there to " + backup);
            int removed = 0;
            foreach (var path in new[] { Layers, PcaStore })
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(path, true))
                        if (k != null) foreach (var v in k.GetValueNames().Where(IsGame)) { k.DeleteValue(v, false); removed++; }
                }
                catch (Exception e) { Warn("Could not remove from HKCU: " + e.Message); }
            }
            var admin = new List<string>();
            try { using (var k = Registry.LocalMachine.OpenSubKey(Layers)) if (k != null) foreach (var v in k.GetValueNames().Where(IsGame)) admin.Add($"reg delete \"HKLM\\{Layers}\" /v \"{v}\" /f"); } catch { }
            try { using (var k = Registry.LocalMachine.OpenSubKey(Ifeo)) if (k != null) admin.Add($"reg delete \"HKLM\\{Ifeo}\" /f"); } catch { }
            if (admin.Count > 0)
            {
                Info("Some of it is stored for all users: Windows asks for permission once to remove it.");
                try
                {
                    var psi = new ProcessStartInfo("cmd.exe", "/c " + string.Join(" & ", admin)) { Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
                    using (var p = Process.Start(psi)) p.WaitForExit(30000);
                    removed += admin.Count;
                }
                catch (Exception e) { Warn("Not removed (permission refused?): " + e.Message); }
            }
            var left = CompatFindings().Where(x => !x.Contains("unreadable") && !x.StartsWith("system-wide")).ToList();
            if (left.Count == 0) Ok($"Removed {removed} Windows setting(s) from the game. BepInEx can load again.");
            else foreach (var x in left) Warn("Still there: " + x);
            if (File.Exists(P("winhttp.dll.off"))) UseWinhttpProxy();
        }

        // ------------------------------------------------------------------ the running process

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetProcessMitigationPolicy(IntPtr hProcess, int policy, out uint buffer, int length);

        /// <summary>The image load policy Windows applied to a running game process.</summary>
        internal static string ImageLoadPolicy(Process p)
        {
            try
            {
                if (!GetProcessMitigationPolicy(p.Handle, 10, out uint f, 4)) return "unreadable (" + Marshal.GetLastWin32Error() + ")";
                var parts = new List<string>();
                if ((f & 1) != 0) parts.Add("no remote images");
                if ((f & 2) != 0) parts.Add("no low-label images");
                if ((f & 4) != 0) parts.Add("PREFER SYSTEM32 IMAGES (the game folder's winhttp.dll/version.dll lose)");
                return parts.Count == 0 ? "normal" : string.Join(", ", parts);
            }
            catch (Exception e) { return "unreadable (" + e.Message + ")"; }
        }
    }
}
