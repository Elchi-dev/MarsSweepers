using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace MarsSetup
{
    internal static partial class Program
    {
        const string SteamAppId = "5335510";
        const string GameExe = "MoonSweepers.exe";
        const string PatcherPage = "https://www.nexusmods.com/moonsweepers/mods/9?tab=files";
        const string PatcherDll = "MoonSweepers.ModEngine.dll";
        static string game;               // game folder
        static string settingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MarsMod");

        static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            Console.Title = "Mars Sweepers setup";
            Banner();

            try
            {
                string argPath = args.FirstOrDefault(a => Directory.Exists(a) || File.Exists(a));
                bool argIsGame = argPath != null && (File.Exists(Path.Combine(argPath, GameExe)) || Path.GetFileName(argPath).Equals(GameExe, StringComparison.OrdinalIgnoreCase));
                if (argPath != null && !argIsGame && IsPatcherSource(argPath))
                {
                    // Someone dragged the patcher download onto the exe.
                    game = FindGame();
                    if (game == null) return Fail("Could not find Moon Sweepers.");
                    InstallPatcherFrom(argPath);
                    Pause();
                    return 0;
                }

                game = FindGame(argPath);
                if (game == null) return Fail("Could not find Moon Sweepers. Start the exe again and paste the game folder when asked.");
                Info("Game folder: " + game);

                while (true)
                {
                    Console.WriteLine();
                    Status();
                    Console.WriteLine();
                    Head("What do you want to do?");
                    Console.WriteLine("  [1] Install / update everything, start the game, build the report when you close it");
                    Console.WriteLine("  [2] Start the game (updates the mod first if needed), report when you close it");
                    Console.WriteLine("  [3] Build the report now (game must be closed)");
                    Console.WriteLine("  [4] Remove MarsProbe only (the Mars mod stays)");
                    Console.WriteLine("  [5] Remove the Mars mod (Mars saves are kept, Moon saves are never touched)");
                    Console.WriteLine("  [6] Remove ALL modding from the game (BepInEx and every mod; moved to a backup folder)");
                    Console.WriteLine("  [7] Open the game folder");
                    Console.WriteLine("  [8] Open the Mars saves folder");
                    Console.WriteLine("  [9] Check why mods do not load (also runs by itself on every game start)");
                    Console.WriteLine("  [10] Start BepInEx through version.dll instead of winhttp.dll (when the game loads Windows' own winhttp.dll)");
                    Console.WriteLine("  [11] Back to winhttp.dll (BepInEx's normal way)");
                    Console.WriteLine("  [0] Exit");
                    Console.Write("> ");
                    string c = (Console.ReadLine() ?? "").Trim();
                    Console.WriteLine();
                    switch (c)
                    {
                        case "1":
                            if (Install() && Ask("Start the game now and build the report automatically when you close it?", true))
                                RunAndCollect();
                            break;
                        case "2":
                            if (!ModCurrent() || !File.Exists(ProbePath)) { Info("This setup carries a newer Mars mod: installing it first."); if (!Install()) break; }
                            FixCompat();
                            RunAndCollect();
                            break;
                        case "3": Collect(); break;
                        case "4": RemoveProbe(); break;
                        case "5": RemoveMars(); break;
                        case "6": RemoveAll(); break;
                        case "7": Process.Start("explorer.exe", "\"" + game + "\""); break;
                        case "8": Directory.CreateDirectory(MarsSaveDir); Process.Start("explorer.exe", "\"" + MarsSaveDir + "\""); break;
                        case "9": Diagnose.Static(); foreach (var l in Diagnose.Lines) Info(l); break;
                        case "10": UseVersionProxy(); break;
                        case "11": UseWinhttpProxy(); break;
                        case "0": case "": return 0;
                    }
                }
            }
            catch (UnauthorizedAccessException e)
            {
                return Fail("Windows refused to write a file: " + e.Message + "\nClose the game and try again. If it keeps happening, right-click MarsSetup.exe > Run as administrator.");
            }
            catch (Exception e)
            {
                return Fail("Something went wrong:\n" + e);
            }
        }

        // ================================================================== UI helpers
        static void Banner()
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(@"
   __  __                  ____
  |  \/  | __ _ _ __ ___  / ___|_      _____  ___ _ __   ___ _ __ ___
  | |\/| |/ _` | '__/ __| \___ \ \ /\ / / _ \/ _ \ '_ \ / _ \ '__/ __|
  | |  | | (_| | |  \__ \  ___) \ V  V /  __/  __/ |_) |  __/ |  \__ \
  |_|  |_|\__,_|_|  |___/ |____/ \_/\_/ \___|\___| .__/ \___|_|  |___/
                                                 |_|   setup v0.9.9   ");
            Console.ResetColor();
        }

        static void Head(string s) { Console.ForegroundColor = ConsoleColor.Cyan; Console.WriteLine(s); Console.ResetColor(); }
        static void Info(string s) { Console.WriteLine("  " + s); }
        static void Ok(string s) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine("  OK  " + s); Console.ResetColor(); }
        static void Warn(string s) { Console.ForegroundColor = ConsoleColor.Yellow; Console.WriteLine("  !!  " + s); Console.ResetColor(); }
        static void Err(string s) { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine("  XX  " + s); Console.ResetColor(); }
        static int Fail(string s) { Err(s); Pause(); return 1; }
        static void Pause() { Console.WriteLine(); Console.WriteLine("Press Enter to close."); Console.ReadLine(); }

        static bool Ask(string q, bool def)
        {
            Console.Write("  " + q + (def ? " [Y/n] " : " [y/N] "));
            string a = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
            if (a == "") return def;
            return a.StartsWith("y") || a.StartsWith("j");
        }

        static string Stamp() => DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        // ================================================================== paths
        static string P(params string[] parts) => Path.Combine(new[] { game }.Concat(parts).ToArray());
        static string BepCore => P("BepInEx", "core", "BepInEx.Unity.IL2CPP.dll");
        static string PatcherPath => P("BepInEx", "patchers", "MoonSweepers.ModEngine", PatcherDll);
        static string ProbePath => P("BepInEx", "plugins", "MarsProbe", "MarsProbe.dll");
        static string MarsPath => P("BepInEx", "plugins", "MarsMod", "MarsMod.dll");

        [DllImport("shell32.dll")]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);

        static string KnownFolder(string guid, string fallback)
        {
            try
            {
                if (SHGetKnownFolderPath(new Guid(guid), 0, IntPtr.Zero, out IntPtr p) == 0)
                {
                    string s = Marshal.PtrToStringUni(p);
                    Marshal.FreeCoTaskMem(p);
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { }
            return fallback;
        }

        static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        static string Downloads => KnownFolder("374DE290-123F-4565-9164-39C4925E467B", Path.Combine(UserProfile, "Downloads"));
        static string LocalLow => KnownFolder("A520A1A4-1780-4FF6-BD18-167343C5AF16", Path.Combine(UserProfile, "AppData", "LocalLow"));
        static string SaveDir => Path.Combine(LocalLow, "DragDropGames", "MoonSweepers");
        static string MarsSaveDir => Path.Combine(SaveDir, "MarsSessions");
        static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        // ================================================================== finding the game
        static string FindGame(string hint = null)
        {
            var tried = new List<string>();
            bool Good(string d) => !string.IsNullOrEmpty(d) && File.Exists(Path.Combine(d, GameExe));

            if (hint != null)
            {
                string d = File.Exists(hint) ? Path.GetDirectoryName(hint) : hint;
                if (Good(d)) return Remember(d);
            }

            string saved = Path.Combine(settingsDir, "gamepath.txt");
            if (File.Exists(saved))
            {
                string d = File.ReadAllText(saved).Trim();
                if (Good(d)) return d;
            }

            foreach (var lib in SteamLibraries())
            {
                string manifest = Path.Combine(lib, "steamapps", "appmanifest_" + SteamAppId + ".acf");
                string installdir = "Moon Sweepers";
                if (File.Exists(manifest))
                {
                    var m = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
                    if (m.Success) installdir = m.Groups[1].Value;
                }
                string d = Path.Combine(lib, "steamapps", "common", installdir);
                tried.Add(d);
                if (Good(d)) return Remember(d);
            }

            Warn("I could not find Moon Sweepers automatically.");
            Info("In Steam: right-click Moon Sweepers > Manage > Browse local files, then copy the folder path from the address bar.");
            while (true)
            {
                Console.Write("  Paste the game folder (or drag MoonSweepers.exe in here), Enter to give up: ");
                string s = (Console.ReadLine() ?? "").Trim().Trim('"');
                if (s == "") return null;
                if (File.Exists(s)) s = Path.GetDirectoryName(s);
                if (Good(s)) return Remember(s);
                Err("No " + GameExe + " in that folder.");
            }
        }

        static string Remember(string d)
        {
            try { Directory.CreateDirectory(settingsDir); File.WriteAllText(Path.Combine(settingsDir, "gamepath.txt"), d); } catch { }
            return d;
        }

        static IEnumerable<string> SteamLibraries()
        {
            var roots = new List<string>();
            foreach (var (hive, key, val) in new[]
            {
                (Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                (Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
            })
            {
                try
                {
                    using (var k = hive.OpenSubKey(key))
                    {
                        var v = k?.GetValue(val) as string;
                        if (!string.IsNullOrEmpty(v)) roots.Add(v.Replace('/', '\\'));
                    }
                }
                catch { }
            }
            roots.Add(@"C:\Program Files (x86)\Steam");
            roots.Add(@"C:\Program Files\Steam");

            var libs = new List<string>();
            foreach (var r in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(r)) continue;
                libs.Add(r);
                string vdf = Path.Combine(r, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
            }
            foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            {
                libs.Add(Path.Combine(d.RootDirectory.FullName, "SteamLibrary"));
                libs.Add(Path.Combine(d.RootDirectory.FullName, "Steam"));
                libs.Add(Path.Combine(d.RootDirectory.FullName, "Games", "Steam"));
                libs.Add(Path.Combine(d.RootDirectory.FullName, "Games", "SteamLibrary"));
            }
            return libs.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        // ================================================================== status
        static void Status()
        {
            Head("Current state");
            bool bep = File.Exists(BepCore);
            string bepState = !bep ? "not installed" : SameAsEmbedded("BepInEx/core/BepInEx.Unity.IL2CPP.dll", BepCore) ? "6.0.0-be.791 (correct build)" : "installed, but a DIFFERENT build";
            Line("BepInEx", bepState, bep);
            Line("MoonSweepers BepInEx Patcher", File.Exists(PatcherPath) ? "installed" : "missing", File.Exists(PatcherPath));
            Line("Mars mod", !File.Exists(MarsPath) ? "not installed" : ModCurrent() ? "installed, this version" : "installed, OLDER version: pick [1] to update", File.Exists(MarsPath) && ModCurrent());
            Line("MarsProbe (debug)", File.Exists(ProbePath) ? "installed" : "not installed", File.Exists(ProbePath));
            int marsSlots = Directory.Exists(MarsSaveDir) ? Directory.GetFiles(MarsSaveDir, "marsmod.json", SearchOption.AllDirectories).Length : 0;
            Line("Mars save slots", marsSlots.ToString(), true);
            bool interop = Directory.Exists(P("BepInEx", "interop")) && Directory.GetFiles(P("BepInEx", "interop"), "Assembly-CSharp.dll").Length > 0;
            Line("Interop files (made on first start)", interop ? "generated" : "not yet", interop);
            Line("MarsProbe report", Directory.Exists(P("MarsProbe", "Report")) ? "exists" : "none yet", Directory.Exists(P("MarsProbe", "Report")));
            foreach (var w in LoaderProblems()) Warn(w);
            foreach (var c in CompatFindings()) Warn("Windows setting on the game: " + c + " (pick [1] to remove it)");
            if (GameRunning()) Warn("Moon Sweepers is running right now.");
        }

        /// <summary>Things that stop BepInEx from starting at all (no console window, no mods).</summary>
        static IEnumerable<string> LoaderProblems()
        {
            var list = new List<string>();
            try
            {
                bool viaVersion = File.Exists(P("winhttp.dll.off")) && File.Exists(P("version.dll"));
                if (viaVersion) list.Add("BepInEx is set to start through version.dll ([10]); [11] switches back to winhttp.dll.");
                else if (!File.Exists(P("winhttp.dll"))) list.Add("winhttp.dll is missing from the game folder: BepInEx cannot start. Pick [1].");
                string ini = P("doorstop_config.ini");
                if (File.Exists(ini) && Regex.IsMatch(File.ReadAllText(ini), @"(?im)^\s*enabled\s*=\s*false")) list.Add("doorstop_config.ini has 'enabled = false': BepInEx is switched off. Pick [1] to switch it on again.");
                if (!File.Exists(P("dotnet", "coreclr.dll"))) list.Add("dotnet\\coreclr.dll is missing: BepInEx cannot start. Pick [1].");
                foreach (var proxy in new[] { "version.dll", "winmm.dll", "dinput8.dll", "dxgi.dll", "d3d11.dll" })
                    if (File.Exists(P(proxy)) && !(proxy == "version.dll" && File.Exists(P("winhttp.dll.off")))) list.Add($"{proxy} in the game folder: another mod loader may be hooked in there and can stop BepInEx.");
                string opts = LaunchOptions();
                if (!string.IsNullOrWhiteSpace(opts))
                {
                    if (opts.IndexOf("doorstop", StringComparison.OrdinalIgnoreCase) >= 0)
                        list.Add($"Steam launch options switch BepInEx off: \"{opts}\". Remove them: Steam, Moon Sweepers, Properties, General, Launch options.");
                    else list.Add($"Steam launch options are set: \"{opts}\" (fine unless they start something else).");
                }
            }
            catch { }
            return list;
        }

        /// <summary>The game's launch options in Steam (every Steam user on this PC).</summary>
        static string LaunchOptions()
        {
            var found = new List<string>();
            foreach (var root in SteamLibraries().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string ud = Path.Combine(root, "userdata");
                if (!Directory.Exists(ud)) continue;
                foreach (var cfg in Directory.GetFiles(ud, "localconfig.vdf", SearchOption.AllDirectories))
                {
                    try
                    {
                        string t = File.ReadAllText(cfg);
                        int i = t.IndexOf("\"" + SteamAppId + "\"", StringComparison.Ordinal);
                        while (i >= 0)
                        {
                            string chunk = t.Substring(i, Math.Min(4000, t.Length - i));
                            int close = chunk.IndexOf("\n\t\t\t\t}", StringComparison.Ordinal);
                            var m = Regex.Match(close > 0 ? chunk.Substring(0, close) : chunk, "\"LaunchOptions\"\\s+\"([^\"]*)\"");
                            if (m.Success && m.Groups[1].Value.Trim().Length > 0) found.Add(m.Groups[1].Value);
                            i = t.IndexOf("\"" + SteamAppId + "\"", i + 1, StringComparison.Ordinal);
                        }
                    }
                    catch { }
                }
            }
            return string.Join(" | ", found.Distinct());
        }

        static void Line(string what, string state, bool good)
        {
            Console.Write("  " + what.PadRight(40));
            Console.ForegroundColor = good ? ConsoleColor.Green : ConsoleColor.DarkYellow;
            Console.WriteLine(state);
            Console.ResetColor();
        }

        static bool GameRunning() => Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GameExe)).Length > 0;

        static bool WaitForGameClosed()
        {
            if (!GameRunning()) return true;
            Warn("Moon Sweepers is running. Please close it first.");
            Info("Waiting… (press Esc to cancel)");
            while (GameRunning())
            {
                if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape) return false;
                Thread.Sleep(500);
            }
            Thread.Sleep(1500);
            return true;
        }

        // ================================================================== embedded files
        // Report listing: never let one odd file (gone meanwhile, or a path longer than Windows likes) stop the report.
        static string SizeOf(string f) { try { return new FileInfo(f).Length.ToString("N0"); } catch { return "?"; } }
        static string WhenOf(string f) { try { return File.GetLastWriteTime(f).ToString("yyyy-MM-dd HH:mm"); } catch { return "?"; } }

        static Stream Res(string name) => typeof(Program).Assembly.GetManifestResourceStream(name) ?? throw new FileNotFoundException("embedded " + name);

        static string Sha(Stream s) { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(s)); }

        /// <summary>
        /// BepInEx starts through Doorstop, a small DLL the game loads by name from its own folder. Normally it is called
        /// winhttp.dll. If something in the game process has already loaded Windows' own winhttp.dll by the time the
        /// game asks for it, Windows hands out that copy and Doorstop never runs. The same Doorstop file also works
        /// under the name version.dll (it carries version.dll's functions too), which the game asks for as well.
        /// </summary>
        static void UseVersionProxy()
        {
            if (!WaitForGameClosed()) return;
            try
            {
                if (!File.Exists(P("winhttp.dll"))) { Warn("winhttp.dll (Doorstop) is not in the game folder. Pick [1] first."); return; }
                File.Copy(P("winhttp.dll"), P("version.dll"), true);
                if (File.Exists(P("winhttp.dll.off"))) File.Delete(P("winhttp.dll.off"));
                File.Move(P("winhttp.dll"), P("winhttp.dll.off"));
                Ok("BepInEx now starts through version.dll (winhttp.dll kept as winhttp.dll.off). [11] switches back.");
            }
            catch (Exception e) { Err(e.Message); }
        }

        static void UseWinhttpProxy()
        {
            if (!WaitForGameClosed()) return;
            try
            {
                if (File.Exists(P("winhttp.dll.off")))
                {
                    if (File.Exists(P("winhttp.dll"))) File.Delete(P("winhttp.dll"));
                    File.Move(P("winhttp.dll.off"), P("winhttp.dll"));
                }
                if (File.Exists(P("version.dll"))) File.Delete(P("version.dll"));
                Ok("BepInEx starts through winhttp.dll again.");
            }
            catch (Exception e) { Err(e.Message); }
        }

        /// <summary>The installed MarsMod.dll is exactly the one in this setup.</summary>
        static bool ModCurrent()
        {
            try
            {
                if (!File.Exists(MarsPath)) return false;
                string a; using (var s = Res("MarsMod.dll")) a = Sha(s);
                string b; using (var s = File.OpenRead(MarsPath)) b = Sha(s);
                return a == b;
            }
            catch { return false; }
        }

        static bool SameAsEmbedded(string entryName, string file)
        {
            try
            {
                using (var z = new ZipArchive(Res("BepInEx.zip"), ZipArchiveMode.Read))
                {
                    var e = z.Entries.FirstOrDefault(x => x.FullName.Replace('\\', '/') == entryName);
                    if (e == null) return false;
                    string a; using (var s = e.Open()) a = Sha(s);
                    string b; using (var s = File.OpenRead(file)) b = Sha(s);
                    return a == b;
                }
            }
            catch { return false; }
        }

        static void ExtractTo(ZipArchive z, string root, Func<string, string> map = null)
        {
            string full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var e in z.Entries)
            {
                string name = e.FullName.Replace('\\', '/');
                if (map != null) { name = map(name); if (name == null) continue; }
                if (name.Length == 0) continue;
                string dest = Path.GetFullPath(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));
                if (!dest.StartsWith(full, StringComparison.OrdinalIgnoreCase)) continue;   // zip slip guard
                if (name.EndsWith("/")) { Directory.CreateDirectory(dest); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                e.ExtractToFile(dest, true);
            }
        }

        // ================================================================== install
        static bool Install()
        {
            Head("Installing");
            if (!WaitForGameClosed()) return false;

            BackupSaves();
            FixCompat();

            // ---- BepInEx
            bool haveBep = File.Exists(BepCore);
            if (haveBep && SameAsEmbedded("BepInEx/core/BepInEx.Unity.IL2CPP.dll", BepCore))
            {
                // Same build: still put back its loader files (winhttp.dll, doorstop_config.ini, dotnet/, core/),
                // in case something changed or disabled them. Settings, plugins and interop files stay.
                using (var z = new ZipArchive(Res("BepInEx.zip"), ZipArchiveMode.Read))
                    foreach (var e in z.Entries)
                    {
                        string n = e.FullName.Replace('\\', '/');
                        if (n.EndsWith("/") || n.StartsWith("BepInEx/config/")) continue;
                        if (n == "winhttp.dll" && File.Exists(P("winhttp.dll.off"))) continue;   // started through version.dll ([10])
                        string dst = Path.Combine(game, n.Replace('/', Path.DirectorySeparatorChar));
                        try { Directory.CreateDirectory(Path.GetDirectoryName(dst)); e.ExtractToFile(dst, true); }
                        catch (Exception ex) { Warn("Could not refresh " + n + ": " + ex.Message); }
                    }
                Ok("BepInEx 6.0.0-be.791 is installed (loader files refreshed)");
            }
            else
            {
                if (haveBep)
                {
                    Warn("A different BepInEx build is installed. The patcher only works with 6.0.0-be.791.");
                    if (!Ask("Replace it? (your plugins and configs stay, the old core files are backed up)", true)) return false;
                    string bak = P("MarsMod_backup", "BepInEx_core_" + Stamp());
                    CopyDir(P("BepInEx", "core"), Path.Combine(bak, "BepInEx", "core"));
                    foreach (var f in new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version" })
                        if (File.Exists(P(f))) File.Copy(P(f), Path.Combine(bak, f), true);
                    Info("Old core backed up to " + bak);
                }
                Info("Extracting BepInEx 6.0.0-be.791 (Unity.IL2CPP win-x64)…");
                using (var z = new ZipArchive(Res("BepInEx.zip"), ZipArchiveMode.Read)) ExtractTo(z, game);
                Ok("BepInEx installed");
            }

            // ---- Patcher
            if (File.Exists(PatcherPath)) Ok("MoonSweepers BepInEx Patcher is already installed");
            else if (!GetPatcher()) Warn("Continuing without the patcher. No mod (MarsProbe included) loads until it is installed: run me again and pick [1].");

            // ---- Mars mod
            Directory.CreateDirectory(Path.GetDirectoryName(MarsPath));
            using (var s = Res("MarsMod.dll"))
            using (var f = File.Create(MarsPath)) s.CopyTo(f);
            Ok("Mars mod installed");
            // 0.5: look settings moved into each Mars world (MarsSessions/slot-N/marsmod-world.cfg).
            // Drop the old global file once so it doesn't show settings that no longer do anything.
            try
            {
                string oldCfg = P("BepInEx", "config", "marsmod.mars.cfg");
                if (File.Exists(oldCfg) && File.ReadAllText(oldCfg).Contains("[Look."))
                {
                    File.Delete(oldCfg);
                    Ok("Old shared Mars settings removed (every Mars world now has its own settings)");
                }
            }
            catch (Exception e) { Warn("Could not tidy the old Mars settings: " + e.Message); }
            // A new version gets a fresh chance for hooks that switched themselves off after a crash.
            try { if (Directory.Exists(P("BepInEx", "MarsMod", "guard"))) Directory.Delete(P("BepInEx", "MarsMod", "guard"), true); } catch { }

            // ---- Narrator voice (see NOTICE). Your own WAV files are never overwritten.
            try
            {
                string vdir = P("BepInEx", "MarsMod", "voice");
                Directory.CreateDirectory(vdir);
                string marker = Path.Combine(vdir, "default-voice.txt");
                bool ours = File.Exists(marker) || Directory.GetFiles(vdir, "*.wav").Length == 0;
                int n = 0;
                using (var z = new ZipArchive(Res("voice.zip"), ZipArchiveMode.Read))
                    foreach (var e in z.Entries)
                    {
                        string dst = Path.Combine(vdir, e.Name);
                        if (File.Exists(dst) && !ours) continue;
                        using (var s = e.Open()) using (var f = File.Create(dst)) s.CopyTo(f);
                        n++;
                    }
                if (ours) File.WriteAllText(marker, "These narrator files were installed by MarsSetup (voice: Chatterbox TTS, see NOTICE).\r\nDelete this file if you replace them with your own, so setup never overwrites yours.\r\n");
                Ok(n > 0 ? $"Narrator voice installed ({n} files)" : "Narrator: keeping your own voice files");
            }
            catch (Exception e) { Warn("Narrator voice not installed: " + e.Message); }

            // ---- MarsProbe (debug helper, writes the report)
            Directory.CreateDirectory(Path.GetDirectoryName(ProbePath));
            using (var s = Res("MarsProbe.dll"))
            using (var f = File.Create(ProbePath)) s.CopyTo(f);
            Ok("MarsProbe installed");

            // ---- BepInEx settings: console window + flush log lines immediately + include Unity's own log
            ConfigureBepInEx();

            Console.WriteLine();
            Head("Good to know before you start the game");
            Info("* The first start takes 1-2 minutes longer and needs internet: BepInEx downloads Unity libraries");
            Info("  and the patcher generates its interop files. Later starts are only ~15 s slower.");
            Info("* A black BepInEx console window opens next to the game. Do NOT close it, that closes the game.");
            Info("* In game: Ctrl+Shift+K opens the Mars dev terminal ('help' lists the commands).");
            return File.Exists(PatcherPath);
        }

        static void BackupSaves()
        {
            try
            {
                if (!Directory.Exists(SaveDir)) { Info("No save folder found yet (" + SaveDir + ")"); return; }
                string dst = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MarsMod", "SaveBackups", Stamp());
                CopyDir(SaveDir, dst);
                Ok("Saves backed up to " + dst);
            }
            catch (Exception e) { Warn("Save backup failed: " + e.Message); }
        }

        static void CopyDir(string src, string dst)
        {
            if (!Directory.Exists(src)) return;
            src = Path.GetFullPath(src).TrimEnd(Path.DirectorySeparatorChar);
            dst = Path.GetFullPath(dst).TrimEnd(Path.DirectorySeparatorChar);
            if (src.Equals(dst, StringComparison.OrdinalIgnoreCase)) return;   // never copy a folder onto itself
            foreach (var d in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(d.Replace(src, dst));
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                try { File.Copy(f, f.Replace(src, dst), true); } catch (IOException) { /* file in use (logs) */ }
            }
        }

        static void ConfigureBepInEx()
        {
            string cfg = P("BepInEx", "config", "BepInEx.cfg");
            Directory.CreateDirectory(Path.GetDirectoryName(cfg));
            var lines = File.Exists(cfg) ? File.ReadAllLines(cfg).ToList() : new List<string>();
            SetIni(lines, "Logging.Console", "Enabled", "true");
            SetIni(lines, "Logging.Disk", "InstantFlushing", "true");
            SetIni(lines, "Logging.Disk", "WriteUnityLog", "true");
            SetIni(lines, "Logging", "UnityLogListening", "true");
            File.WriteAllLines(cfg, lines);
            Ok("BepInEx logging configured (console window on, instant log flushing, Unity log included)");
        }

        static void SetIni(List<string> lines, string section, string key, string value)
        {
            int sec = lines.FindIndex(l => l.Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase));
            if (sec < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim() != "") lines.Add("");
                lines.Add("[" + section + "]");
                lines.Add(key + " = " + value);
                return;
            }
            int end = lines.FindIndex(sec + 1, l => l.TrimStart().StartsWith("["));
            if (end < 0) end = lines.Count;
            for (int i = sec + 1; i < end; i++)
            {
                var m = Regex.Match(lines[i], @"^\s*([^#=]+?)\s*=");
                if (m.Success && m.Groups[1].Value.Equals(key, StringComparison.OrdinalIgnoreCase)) { lines[i] = key + " = " + value; return; }
            }
            lines.Insert(end, key + " = " + value);
        }

        // ================================================================== patcher (from Nexus, needs the player's login)
        static bool GetPatcher()
        {
            Info("The MoonSweepers BepInEx Patcher by boverson777 is needed; stock BepInEx can't load mods in this game.");
            Info("It is hosted on Nexus Mods, which needs your (free) login to download, so I can't fetch it myself.");

            string found = FindPatcherInDownloads(DateTime.MinValue);
            if (found != null)
            {
                Ok("Found it in your Downloads: " + Path.GetFileName(found));
                return InstallPatcherFrom(found);
            }

            Info("");
            Info("Opening the Nexus page. Click 'Manual download' on the main file (not Vortex).");
            Info("I'll pick the zip up from your Downloads folder by myself as soon as it arrives.");
            Info("You can also type/paste or drag the downloaded file or an extracted folder in here and press Enter.");
            Info("Press Esc to skip.");
            try { Process.Start(new ProcessStartInfo(PatcherPage) { UseShellExecute = true }); } catch { Warn("Open this yourself: " + PatcherPage); }

            DateTime since = DateTime.Now.AddMinutes(-1);
            var typed = new StringBuilder();
            Console.Write("  > ");
            while (true)
            {
                string f = FindPatcherInDownloads(since);
                if (f != null)
                {
                    Console.WriteLine();
                    Thread.Sleep(1000); // let the browser finish writing
                    Ok("Download detected: " + Path.GetFileName(f));
                    return InstallPatcherFrom(f);
                }
                string other = FindOtherArchive(since);
                if (other != null)
                {
                    Console.WriteLine();
                    Warn(Path.GetFileName(other) + " is not a .zip. Please extract it (right-click > Extract All, or 7-Zip),");
                    Warn("then drag the extracted folder in here and press Enter.");
                    since = DateTime.Now;
                    Console.Write("  > ");
                }
                while (Console.KeyAvailable)
                {
                    var k = Console.ReadKey(true);
                    if (k.Key == ConsoleKey.Escape) { Console.WriteLine(); return false; }
                    if (k.Key == ConsoleKey.Enter)
                    {
                        Console.WriteLine();
                        string path = typed.ToString().Trim().Trim('"');
                        typed.Clear();
                        if (path.Length > 0)
                        {
                            if (IsPatcherSource(path)) return InstallPatcherFrom(path);
                            Err("No " + PatcherDll + " found in " + path);
                        }
                        Console.Write("  > ");
                    }
                    else if (k.Key == ConsoleKey.Backspace) { if (typed.Length > 0) { typed.Length--; Console.Write("\b \b"); } }
                    else if (k.KeyChar != '\0') { typed.Append(k.KeyChar); Console.Write(k.KeyChar); }
                }
                Thread.Sleep(300);
            }
        }

        static string FindPatcherInDownloads(DateTime since)
        {
            try
            {
                if (!Directory.Exists(Downloads)) return null;
                foreach (var f in new DirectoryInfo(Downloads).GetFiles("*.zip").Where(f => f.LastWriteTime >= since).OrderByDescending(f => f.LastWriteTime).Take(40))
                    if (ZipHasPatcher(f.FullName)) return f.FullName;
            }
            catch { }
            return null;
        }

        static string FindOtherArchive(DateTime since)
        {
            try
            {
                return new DirectoryInfo(Downloads).GetFiles().Where(f => f.LastWriteTime >= since &&
                        (f.Extension == ".7z" || f.Extension == ".rar") &&
                        (f.Name.IndexOf("patcher", StringComparison.OrdinalIgnoreCase) >= 0 || f.Name.IndexOf("moonsweepers", StringComparison.OrdinalIgnoreCase) >= 0))
                    .Select(f => f.FullName).FirstOrDefault();
            }
            catch { return null; }
        }

        static bool ZipHasPatcher(string zip)
        {
            try { using (var z = ZipFile.OpenRead(zip)) return z.Entries.Any(e => e.Name.Equals(PatcherDll, StringComparison.OrdinalIgnoreCase)); }
            catch { return false; }
        }

        static bool IsPatcherSource(string path)
        {
            if (File.Exists(path)) return path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? ZipHasPatcher(path) : Path.GetFileName(path).Equals(PatcherDll, StringComparison.OrdinalIgnoreCase);
            if (Directory.Exists(path)) return Directory.GetFiles(path, PatcherDll, SearchOption.AllDirectories).Length > 0;
            return false;
        }

        static bool InstallPatcherFrom(string src)
        {
            string full = Path.GetFullPath(src);
            if (game != null && full.StartsWith(Path.GetFullPath(game), StringComparison.OrdinalIgnoreCase) && !full.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                Err("That is inside the game folder already. Point me at the downloaded zip or the extracted download instead.");
                return false;
            }
            if (!WaitForGameClosed()) return false;
            string target = Path.GetDirectoryName(PatcherPath);
            Directory.CreateDirectory(target);
            if (src.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using (var z = ZipFile.OpenRead(src))
                {
                    var dll = z.Entries.First(e => e.Name.Equals(PatcherDll, StringComparison.OrdinalIgnoreCase));
                    string dllPath = dll.FullName.Replace('\\', '/');
                    int bep = dllPath.IndexOf("BepInEx/", StringComparison.OrdinalIgnoreCase);
                    if (bep >= 0)
                    {
                        // The zip already has the game-folder layout: keep the author's layout.
                        string prefix = dllPath.Substring(0, bep);
                        ExtractTo(z, game, n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? n.Substring(prefix.Length) : null);
                    }
                    else
                    {
                        string dir = dllPath.Contains("/") ? dllPath.Substring(0, dllPath.LastIndexOf('/') + 1) : "";
                        ExtractTo(z, target, n => n.StartsWith(dir, StringComparison.OrdinalIgnoreCase) ? n.Substring(dir.Length) : null);
                    }
                }
            }
            else
            {
                string dllFile = File.Exists(src) ? src : Directory.GetFiles(src, PatcherDll, SearchOption.AllDirectories).First();
                string from = Path.GetDirectoryName(dllFile);
                string norm = from.Replace('\\', '/') + "/";
                int bep = norm.IndexOf("/BepInEx/", StringComparison.OrdinalIgnoreCase);
                if (bep >= 0) CopyDir(Path.Combine(from.Substring(0, bep + 1), "BepInEx"), P("BepInEx"));
                else CopyDir(from, target);
            }
            if (File.Exists(PatcherPath)) { Ok("Patcher installed to BepInEx\\patchers\\MoonSweepers.ModEngine"); return true; }
            Err("The patcher files did not end up where expected. Please send me a screenshot of the zip's contents.");
            return false;
        }

        // ================================================================== run + collect
        static void RunAndCollect()
        {
            if (!File.Exists(PatcherPath)) { Warn("The patcher is missing, so MarsProbe won't load. Pick [1] first."); return; }
            if (GameRunning()) Info("The game is already running, I'll wait for it to close.");
            else
            {
                Info("Starting Moon Sweepers through Steam…");
                Process.Start(new ProcessStartInfo("steam://rungameid/" + SteamAppId) { UseShellExecute = true });
                Info("Waiting for the game window (first modded start can take a while)…");
                var until = DateTime.Now.AddMinutes(4);
                while (!GameRunning() && DateTime.Now < until) Thread.Sleep(1000);
                if (!GameRunning()) { Warn("The game didn't start. Start it from Steam yourself, I'll keep waiting."); while (!GameRunning()) Thread.Sleep(1000); }
            }
            Diagnose.Watch();

            Console.WriteLine();
            Head("Mars 0.9.9: one test round for everything");
            Info(" FIRST CHECK: a black BepInEx console window must open next to the game. No window = no mods:");
            Info("   stay in the main menu for 30 s (I am looking at the game meanwhile), then quit and send the report.");
            Info(" MOON first (your most built-up Moon world, with belts, packager, press and solar if you have them):");
            Info("  1. Load it and play normally for 2 to 3 minutes, ideally near running belts. After a minute the");
            Info("     mod writes its research files by itself; typing 'collect' (Ctrl+Shift+K) does it again.");
            Info(" Then MARS: quit to the menu, pick an empty slot, Play, answer Mars (or load your Mars slot).");
            Info("  No question? Terminal in the menu: 'mars slot 0' makes empty slot 0 a Mars slot.");
            Info("  2. 'money', then with the build tool: Ore drill ON a mine, Smelter, Container port, Light pole.");
            Info("  3. E on the machines, Shift+E on the container. 'storm now': machines outside jam, hold E to scrub.");
            Info("     Stand by the light pole in the storm. Watch the market ticker at the top.");
            Info("  4. Save, quit to the menu, load Mars again: machines still there? Type 'collect' once more.");
            Info("  5. Quit the game. I'll build the report. Screenshots of anything odd help a lot.");
            Info("Screenshots are very welcome, especially of the intro, the sky and a storm!");
            Console.WriteLine();
            var start = DateTime.Now;
            while (GameRunning())
            {
                Console.Write($"\r  Game running for {(DateTime.Now - start):mm\\:ss}…   ");
                Thread.Sleep(1000);
            }
            Console.WriteLine();
            Head("What the game loaded");
            foreach (var l in Diagnose.Lines.ToList().Where(x => x.StartsWith("process") || x.StartsWith("game process")))
                Info(l);
            Thread.Sleep(2000);
            Collect();
        }

        static void Collect()
        {
            Head("Building the report");
            if (!WaitForGameClosed()) return;
            string zipPath = Path.Combine(Desktop, "MarsProbe_Report_" + Stamp() + ".zip");
            int files = 0;
            using (var z = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                void Add(string file, string entry)
                {
                    try
                    {
                        if (!File.Exists(file)) return;
                        using (var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var dst = z.CreateEntry(entry.Replace('\\', '/'), CompressionLevel.Optimal).Open())
                            src.CopyTo(dst);
                        files++;
                    }
                    catch (Exception e) { Warn("Skipped " + entry + ": " + e.Message); }
                }
                void AddDir(string dir, string prefix, Func<string, bool> filter = null)
                {
                    if (!Directory.Exists(dir)) return;
                    foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                        if (filter == null || filter(f)) Add(f, prefix + "/" + f.Substring(dir.Length + 1));
                }

                AddDir(P("MarsProbe"), "MarsProbe");
                Add(P("BepInEx", "LogOutput.log"), "BepInEx/LogOutput.log");
                foreach (var extra in Directory.Exists(P("BepInEx")) ? Directory.GetFiles(P("BepInEx"), "LogOutput*.log") : new string[0])
                    if (Path.GetFileName(extra) != "LogOutput.log") Add(extra, "BepInEx/" + Path.GetFileName(extra));
                Add(P("BepInEx", "ErrorLog.log"), "BepInEx/ErrorLog.log");
                AddDir(P("BepInEx", "config"), "BepInEx/config");
                AddDir(P("BepInEx", "MarsMod"), "BepInEx/MarsMod", f => !f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase));
                Add(Path.Combine(SaveDir, "Player.log"), "UnityPlayer/Player.log");
                if (Directory.Exists(MarsSaveDir))
                    foreach (var f in Directory.GetFiles(MarsSaveDir, "*", SearchOption.AllDirectories))
                    {
                        string n = Path.GetFileName(f);
                        if (n.StartsWith("mars", StringComparison.OrdinalIgnoreCase) && (n.EndsWith(".json") || n.EndsWith(".cfg") || n.EndsWith(".bak")))
                            Add(f, "MarsSessions/" + f.Substring(MarsSaveDir.Length + 1));
                    }
                Add(Path.Combine(SaveDir, "Player-prev.log"), "UnityPlayer/Player-prev.log");
                try
                {
                    string crashes = Path.Combine(Path.GetTempPath(), "DragDropGames", "MoonSweepers", "Crashes");
                    if (Directory.Exists(crashes))
                        foreach (var dir in Directory.GetDirectories(crashes).OrderByDescending(Directory.GetLastWriteTime).Take(3))
                            foreach (var f in Directory.GetFiles(dir).Where(f => !f.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase) || new FileInfo(f).Length < 4_000_000))
                                Add(f, "Crashes/" + Path.GetFileName(dir) + "/" + Path.GetFileName(f));
                }
                catch { }

                // The game's own interop assemblies: the C# view of the game's classes (no game code, only signatures).
                string[] skip = { "UnityEngine", "Il2Cppmscorlib", "Il2CppSystem", "Il2CppMono", "System", "mscorlib", "netstandard", "Unity." };
                AddDir(P("BepInEx", "interop"), "interop", f => !skip.Any(s => Path.GetFileName(f).StartsWith(s, StringComparison.OrdinalIgnoreCase)) && f.EndsWith(".dll"));

                var sb = new StringBuilder();
                sb.AppendLine("MarsSetup report " + DateTime.Now);
                sb.AppendLine("Game folder: " + game);
                sb.AppendLine("Windows: " + Environment.OSVersion + (Environment.Is64BitOperatingSystem ? " x64" : ""));
                sb.AppendLine("Steam launch options: " + LaunchOptions());
                if (Diagnose.Lines.Count == 0) Diagnose.Static();
                foreach (var l in Diagnose.Lines.ToList()) sb.AppendLine("diag: " + l);
                foreach (var w in LoaderProblems()) sb.AppendLine("Loader check: " + w);
                try { sb.AppendLine("doorstop_config.ini:"); sb.AppendLine(File.ReadAllText(P("doorstop_config.ini"))); } catch { }
                try { sb.AppendLine("dotnet files: " + Directory.GetFiles(P("dotnet")).Length); } catch { }
                foreach (var lib in SteamLibraries())
                {
                    string man = Path.Combine(lib, "steamapps", "appmanifest_" + SteamAppId + ".acf");
                    if (File.Exists(man)) { sb.AppendLine("Steam manifest " + man + ":"); sb.AppendLine(File.ReadAllText(man)); break; }
                }
                sb.AppendLine("Game folder files:");
                foreach (var f in Directory.GetFiles(game)) sb.AppendLine($"  {SizeOf(f),12}  {Path.GetFileName(f)}");
                foreach (var d in Directory.GetDirectories(game)) sb.AppendLine($"  {"<dir>",12}  {Path.GetFileName(d)}");
                sb.AppendLine("BepInEx folder:");
                if (Directory.Exists(P("BepInEx")))
                    foreach (var f in Directory.GetFiles(P("BepInEx"), "*", SearchOption.AllDirectories).Where(f => !f.Replace('\\', '/').Contains("/cache/") && !f.Replace('\\', '/').Contains("/unity-libs/")))
                        sb.AppendLine($"  {SizeOf(f),12}  {f.Substring(game.Length + 1)}");
                if (Directory.Exists(SaveDir))
                {
                    sb.AppendLine("Save folder " + SaveDir + ":");
                    try
                    {
                        foreach (var f in Directory.GetFiles(SaveDir, "*", SearchOption.AllDirectories).Where(f => !f.Contains("chunks")))
                            sb.AppendLine($"  {SizeOf(f),12}  {WhenOf(f)}  {f.Substring(SaveDir.Length + 1)}");
                    }
                    catch (Exception ex) { sb.AppendLine("  (listing stopped: " + ex.Message + ")"); }
                }
                var e2 = z.CreateEntry("setup_info.txt");
                using (var w = new StreamWriter(e2.Open(), new UTF8Encoding(false))) w.Write(sb.ToString());
            }
            long size = new FileInfo(zipPath).Length;
            Ok($"Report ready: {zipPath} ({size / 1024.0 / 1024.0:F1} MB, {files} files)");
            if (!File.Exists(P("MarsProbe", "Report", "report.txt")))
                Warn("MarsProbe wrote no report this time, so it probably didn't load. The zip still has the logs that tell us why.");
            Info("Send that zip over.");
            try { Process.Start("explorer.exe", "/select,\"" + zipPath + "\""); } catch { }
        }

        // ================================================================== removal
        static void RemoveProbe()
        {
            if (!WaitForGameClosed()) return;
            try { if (Directory.Exists(Path.GetDirectoryName(ProbePath))) Directory.Delete(Path.GetDirectoryName(ProbePath), true); } catch (Exception e) { Err(e.Message); return; }
            try { File.Delete(P("BepInEx", "config", "marsmod.marsprobe.cfg")); } catch { }
            if (Directory.Exists(P("MarsProbe")) && Ask("Also delete the MarsProbe report folder?", false))
                Directory.Delete(P("MarsProbe"), true);
            Ok("MarsProbe removed. BepInEx and the patcher are still installed.");
        }

        static void RemoveMars()
        {
            if (!WaitForGameClosed()) return;
            try { if (Directory.Exists(Path.GetDirectoryName(MarsPath))) Directory.Delete(Path.GetDirectoryName(MarsPath), true); } catch (Exception e) { Err(e.Message); return; }
            Ok("Mars mod removed. Your Mars saves stay in " + MarsSaveDir + " and come back when you reinstall.");
        }

        static void RemoveAll()
        {
            Warn("This removes BepInEx, the patcher, MarsProbe and every other BepInEx mod from the game folder.");
            Warn("Nothing is deleted: it all moves to a MarsMod_removed_<date> folder inside the game folder.");
            if (!Ask("Continue?", false)) return;
            if (!WaitForGameClosed()) return;
            string bak = P("MarsMod_removed_" + Stamp());
            Directory.CreateDirectory(bak);
            foreach (var f in new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt" })
                if (File.Exists(P(f))) File.Move(P(f), Path.Combine(bak, f));
            foreach (var d in new[] { "BepInEx", "dotnet", "MarsProbe" })
                if (Directory.Exists(P(d))) Directory.Move(P(d), Path.Combine(bak, d));
            Ok("Done. The game is vanilla again. The removed files are in " + bak);
        }
    }
}
