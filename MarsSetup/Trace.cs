using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;

namespace MarsSetup
{
    /// <summary>
    /// Records, with Microsoft's Process Monitor, every place Windows looks for winhttp.dll and version.dll while the
    /// game starts, and what Windows answered for each (loaded, not found, access denied, ...). That is the actual
    /// reason the game ends up with Windows' copy instead of BepInEx's, read from Windows itself.
    /// </summary>
    internal static partial class Program
    {
        static void TraceLoader()
        {
            if (!WaitForGameClosed()) return;
            if (File.Exists(P("winhttp.dll.off"))) UseWinhttpProxy();
            string dir = Path.Combine(Path.GetTempPath(), "MarsSetupTrace");
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            Directory.CreateDirectory(dir);
            string procmon = Path.Combine(dir, "Procmon64.exe");
            string pml = Path.Combine(dir, "start.pml"), csv = Path.Combine(dir, "start.csv");
            string ready = Path.Combine(dir, "ready"), stop = Path.Combine(dir, "stop"), done = Path.Combine(dir, "done");

            Head("Recording how Windows loads the game (Process Monitor by Microsoft)");
            try
            {
                Info("Downloading Process Monitor from live.sysinternals.com …");
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                using (var wc = new WebClient()) wc.DownloadFile("https://live.sysinternals.com/Procmon64.exe", procmon);
            }
            catch (Exception e) { Err("Download failed: " + e.Message); return; }

            string ps1 = Path.Combine(dir, "trace.ps1");
            File.WriteAllText(ps1,
                "$pm = '" + procmon + "'\r\n" +
                "Start-Process $pm -ArgumentList '/AcceptEula','/Quiet','/Minimized','/BackingFile','" + pml + "'\r\n" +
                "Start-Sleep -Seconds 4\r\n" +
                "New-Item -ItemType File -Path '" + ready + "' -Force | Out-Null\r\n" +
                "$t = 0; while (-not (Test-Path '" + stop + "') -and $t -lt 180) { Start-Sleep -Seconds 1; $t++ }\r\n" +
                "Start-Process $pm -ArgumentList '/AcceptEula','/Terminate' -Wait\r\n" +
                "Start-Sleep -Seconds 2\r\n" +
                "Start-Process $pm -ArgumentList '/AcceptEula','/Quiet','/OpenLog','" + pml + "','/SaveAs','" + csv + "' -Wait\r\n" +
                "New-Item -ItemType File -Path '" + done + "' -Force | Out-Null\r\n");
            Info("Windows asks for permission once: Process Monitor needs it to see how programs load files.");
            try
            {
                Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + ps1 + "\"")
                { Verb = "runas", UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
            }
            catch (Exception e) { Err("Not started (permission refused?): " + e.Message); return; }

            var until = DateTime.Now.AddSeconds(40);
            while (!File.Exists(ready) && DateTime.Now < until) Thread.Sleep(300);
            if (!File.Exists(ready)) { Err("Process Monitor did not start."); return; }

            Info("Recording. Starting the game through Steam …");
            Process.Start(new ProcessStartInfo("steam://rungameid/" + SteamAppId) { UseShellExecute = true });
            var seen = DateTime.Now.AddSeconds(90);
            while (!GameRunning() && DateTime.Now < seen) Thread.Sleep(300);
            Info(GameRunning() ? "Game is running. Recording 20 more seconds …" : "The game did not show up within 90 s, stopping.");
            Thread.Sleep(20000);
            File.WriteAllText(stop, "");
            Info("Saving the recording (can take a minute) …");
            until = DateTime.Now.AddMinutes(6);
            while (!File.Exists(done) && DateTime.Now < until) Thread.Sleep(500);
            if (!File.Exists(csv)) { Err("The recording could not be saved."); return; }

            var keep = new List<string>();
            string header = null;
            foreach (var line in File.ReadLines(csv))
            {
                if (header == null) { header = line; keep.Add(line); continue; }
                if (line.IndexOf("MoonSweepers.exe", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (line.IndexOf("winhttp", StringComparison.OrdinalIgnoreCase) < 0 && line.IndexOf("version.dll", StringComparison.OrdinalIgnoreCase) < 0
                    && line.IndexOf("doorstop", StringComparison.OrdinalIgnoreCase) < 0 && line.IndexOf("UnityPlayer.dll", StringComparison.OrdinalIgnoreCase) < 0
                    && line.IndexOf("Process Create", StringComparison.OrdinalIgnoreCase) < 0 && line.IndexOf("Process Start", StringComparison.OrdinalIgnoreCase) < 0
                    && line.IndexOf("Load Image", StringComparison.OrdinalIgnoreCase) < 0) continue;
                keep.Add(line);
                if (keep.Count > 4000) break;
            }
            string outFile = P("MarsProbe", "loader-trace.csv");
            Directory.CreateDirectory(Path.GetDirectoryName(outFile));
            File.WriteAllLines(outFile, keep);
            Head("What Windows answered when the game looked for its loader");
            foreach (var l in keep.Skip(1).Where(l => l.IndexOf("winhttp", StringComparison.OrdinalIgnoreCase) >= 0).Take(25))
                Info(Short(l));
            try { File.Delete(pml); File.Delete(csv); } catch { }
            Info("Close the game when you like; I'll build the report (it includes the recording).");
            while (GameRunning()) Thread.Sleep(1000);
            Thread.Sleep(1500);
            Collect();
        }

        static string Short(string csvLine)
        {
            // "Time","Process Name","PID","Operation","Path","Result","Detail"
            var parts = csvLine.Split(new[] { "\",\"" }, StringSplitOptions.None).Select(p => p.Trim('"')).ToArray();
            return parts.Length >= 6 ? $"{parts[3]}  {parts[4]}  ->  {parts[5]}" : csvLine;
        }
    }
}
