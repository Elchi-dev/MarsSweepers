using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace MarsSetup
{
    /// <summary>What the player window (Installer.cs) asks of the install code.</summary>
    internal static partial class Program
    {
        internal sealed class State { public string Game; public bool Bep, Patcher, Mod, ModOld, FirstStart; }

        internal static volatile string GuiPicked;

        internal static bool GuiFindGame(string hint)
        {
            game = FindGame(hint);
            if (game != null) Info("Game folder: " + game);
            return game != null;
        }

        internal static State GuiState()
        {
            var s = new State { Game = game };
            if (game == null) return s;
            s.Bep = File.Exists(BepCore) && SameAsEmbedded("BepInEx/core/BepInEx.Unity.IL2CPP.dll", BepCore);
            s.Patcher = File.Exists(PatcherPath);
            s.Mod = File.Exists(MarsPath);
            s.ModOld = s.Mod && !ModCurrent();
            s.FirstStart = !(Directory.Exists(P("BepInEx", "interop")) && Directory.GetFiles(P("BepInEx", "interop"), "Assembly-CSharp.dll").Length > 0);
            return s;
        }

        internal static void GuiInstall()
        {
            if (Install()) Ok("All set. Press Play.");
            else if (!File.Exists(PatcherPath)) Warn("Not finished: the patcher is still missing. Press Install again when you have it.");
        }

        internal static void GuiPlay()
        {
            if (!File.Exists(PatcherPath) || !File.Exists(BepCore)) { Warn("Install first."); return; }
            if (!ModCurrent() || !File.Exists(P("dstorage_original.dll")))
            {
                Info("Updating before the start…");
                if (!Install()) return;
            }
            if (GameRunning()) { Info("Moon Sweepers is already running."); return; }
            Info("Starting Moon Sweepers through Steam…");
            Process.Start(new ProcessStartInfo("steam://rungameid/" + SteamAppId) { UseShellExecute = true });
            var until = DateTime.Now.AddMinutes(3);
            while (!GameRunning() && DateTime.Now < until) Thread.Sleep(1000);
            if (!GameRunning()) { Warn("The game did not start yet. Start it from Steam as usual; the mod is installed."); return; }
            Ok("The game is starting. In the main menu, a new world can be made a Mars world. Have fun!");
            Info("In game: Ctrl+Shift+K opens the Mars terminal ('help' lists everything).");
        }

        internal static void GuiOpenGame() { if (game != null) Process.Start("explorer.exe", "\"" + game + "\""); }
        internal static void GuiOpenSaves() { Directory.CreateDirectory(MarsSaveDir); Process.Start("explorer.exe", "\"" + MarsSaveDir + "\""); }
        internal static void GuiRemoveMars() { if (game != null) RemoveMars(); }
        internal static void GuiRemoveAll() { if (game != null) RemoveAll(); }
        internal static bool GuiIsPatcher(string path) => IsPatcherSource(path);
        internal static void GuiPatcherFrom(string path) { if (game != null) InstallPatcherFrom(path); }

        /// <summary>Everything the window shows also goes to %AppData%\MarsMod\setup.log, for when someone asks for help.</summary>
        internal static void GuiLog(int kind, string text)
        {
            try { Directory.CreateDirectory(settingsDir); File.AppendAllText(Path.Combine(settingsDir, "setup.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {"HIOWE"[kind]} {text}{Environment.NewLine}"); } catch { }
        }

        internal static void GuiLogError(Exception e)
        {
            try { Directory.CreateDirectory(settingsDir); File.AppendAllText(Path.Combine(settingsDir, "setup-errors.log"), DateTime.Now + " " + e + Environment.NewLine); } catch { }
        }

        /// <summary>The window's way to get the patcher: open Nexus, then pick the download up from Downloads by itself.</summary>
        static bool GetPatcherGui()
        {
            Head("One file from Nexus Mods");
            Info("Moon Sweepers needs the 'MoonSweepers BepInEx Patcher' by boverson777 to load any mod.");
            Info("Nexus needs a free login to download it, so you have to click the download yourself:");
            Info("on the page that opens, press 'Manual download' on the main file (not Vortex).");
            string found = FindPatcherInDownloads(DateTime.MinValue);
            if (found != null) { Ok("Found it in your Downloads: " + Path.GetFileName(found)); return InstallPatcherFrom(found); }
            try { Process.Start(new ProcessStartInfo(PatcherPage) { UseShellExecute = true }); } catch { Warn("Open this page yourself: " + PatcherPage); }
            Info("Waiting for the download (it is picked up from your Downloads folder by itself)…");
            GuiPicked = null;
            Installer.Current?.ShowPatcherWait(true);
            DateTime since = DateTime.Now.AddMinutes(-1), until = DateTime.Now.AddMinutes(20);
            bool warnedArchive = false;
            try
            {
                while (DateTime.Now < until && !Cancel)
                {
                    string picked = GuiPicked;
                    if (picked != null)
                    {
                        GuiPicked = null;
                        if (IsPatcherSource(picked)) return InstallPatcherFrom(picked);
                        Err("That file does not contain " + PatcherDll + ".");
                    }
                    string f = FindPatcherInDownloads(since);
                    if (f != null) { Thread.Sleep(1000); Ok("Download found: " + Path.GetFileName(f)); return InstallPatcherFrom(f); }
                    string other = FindOtherArchive(since);
                    if (other != null && !warnedArchive)
                    {
                        warnedArchive = true;
                        Warn(Path.GetFileName(other) + " is not a .zip: extract it (right-click > Extract All), then press 'I downloaded it…' and pick MoonSweepers.ModEngine.dll.");
                    }
                    Thread.Sleep(500);
                }
            }
            finally { Installer.Current?.ShowPatcherWait(false); }
            Warn("Stopped waiting for the patcher.");
            return false;
        }
    }
}
