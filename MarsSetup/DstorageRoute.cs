using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace MarsSetup
{
    /// <summary>
    /// Since 2026-11-10 Windows starts Moon Sweepers on the affected PC with "prefer system images" (PreferSystem32),
    /// and switching it off for the exe does not change the running process (others report the same since a
    /// Windows 11 update). With that rule, any DLL name Windows also has in System32 (winhttp.dll, version.dll, ...)
    /// comes from System32. dstorage.dll has no System32 copy, so the game always loads it from its own folder.
    /// This route puts a small forwarding dstorage.dll there (native/dstorage-proxy): it hands every DirectStorage
    /// call to the game's own file, renamed dstorage_original.dll, and loads BepInEx's Doorstop by its full path.
    /// </summary>
    internal static partial class Program
    {
        static string DoorstopDir => P("BepInEx", "doorstop");

        static readonly HashSet<string> ForwardedExports = new HashSet<string>
            { "DStorageCreateCompressionCodec", "DStorageGetFactory", "DStorageSetConfiguration", "DStorageSetConfiguration1" };

        static void UseDstorageRoute()
        {
            if (!WaitForGameClosed()) return;
            if (ApplyDstorageRoute()) RunAndCollect();
        }

        /// <summary>Set up the dstorage.dll route (also done by every install). False when it could not be set up.</summary>
        static bool ApplyDstorageRoute()
        {
            try
            {
                if (File.Exists(P("winhttp.dll.off"))) UseWinhttpProxy();
                string dst = P("dstorage.dll"), orig = P("dstorage_original.dll");
                if (!File.Exists(orig))
                {
                    if (!File.Exists(dst)) { Err("The game's dstorage.dll is missing. Let Steam verify the game files first."); return false; }
                    var names = PeExports(File.ReadAllBytes(dst));
                    var unknown = names.Where(n => !ForwardedExports.Contains(n)).ToList();
                    if (names.Count == 0 || unknown.Count > 0) { Err("The game's dstorage.dll has functions this route does not forward (" + string.Join(", ", unknown) + "). Not changed."); return false; }
                    File.Move(dst, orig);
                }
                using (var s = Res("dstorage-proxy.dll")) using (var f = File.Create(dst)) s.CopyTo(f);
                Directory.CreateDirectory(DoorstopDir);
                using (var z = new ZipArchive(Res("BepInEx.zip"), ZipArchiveMode.Read))
                {
                    z.Entries.First(e => e.FullName == "winhttp.dll").ExtractToFile(Path.Combine(DoorstopDir, "winhttp.dll"), true);
                    string ini;
                    using (var r = new StreamReader(z.Entries.First(e => e.FullName == "doorstop_config.ini").Open())) ini = r.ReadToEnd();
                    // Absolute paths: Doorstop may read the config next to itself or next to the game.
                    ini = ini.Replace(@"target_assembly = BepInEx\core\BepInEx.Unity.IL2CPP.dll", @"target_assembly = " + P("BepInEx", "core", "BepInEx.Unity.IL2CPP.dll"))
                             .Replace(@"coreclr_path = dotnet\coreclr.dll", @"coreclr_path = " + P("dotnet", "coreclr.dll"))
                             .Replace("corlib_dir = dotnet", "corlib_dir = " + P("dotnet"));
                    File.WriteAllText(Path.Combine(DoorstopDir, "doorstop_config.ini"), ini, new UTF8Encoding(false));
                    File.WriteAllText(P("doorstop_config.ini"), ini, new UTF8Encoding(false));
                }
                Ok("BepInEx starts through dstorage.dll (the game's own file is kept as dstorage_original.dll). [16] undoes it.");
                return true;
            }
            catch (Exception e) { Err(e.Message); return false; }
        }

        static void UndoDstorageRoute()
        {
            if (!WaitForGameClosed()) return;
            try
            {
                if (File.Exists(P("dstorage_original.dll")))
                {
                    if (File.Exists(P("dstorage.dll"))) File.Delete(P("dstorage.dll"));
                    File.Move(P("dstorage_original.dll"), P("dstorage.dll"));
                }
                using (var z = new ZipArchive(Res("BepInEx.zip"), ZipArchiveMode.Read))
                    z.Entries.First(e => e.FullName == "doorstop_config.ini").ExtractToFile(P("doorstop_config.ini"), true);
                Ok("The game's own dstorage.dll is back.");
            }
            catch (Exception e) { Err(e.Message); }
        }

        /// <summary>Names exported by a PE file (x64).</summary>
        static List<string> PeExports(byte[] b)
        {
            var list = new List<string>();
            int pe = BitConverter.ToInt32(b, 0x3C);
            int sections = BitConverter.ToUInt16(b, pe + 6), optSize = BitConverter.ToUInt16(b, pe + 20), opt = pe + 24;
            int exportRva = BitConverter.ToInt32(b, opt + 112);
            if (exportRva == 0) return list;
            int Off(int rva)
            {
                for (int i = 0; i < sections; i++)
                {
                    int s = opt + optSize + i * 40;
                    int va = BitConverter.ToInt32(b, s + 12), size = Math.Max(BitConverter.ToInt32(b, s + 8), BitConverter.ToInt32(b, s + 16)), raw = BitConverter.ToInt32(b, s + 20);
                    if (rva >= va && rva < va + size) return rva - va + raw;
                }
                return -1;
            }
            int ex = Off(exportRva);
            int count = BitConverter.ToInt32(b, ex + 24), namesRva = BitConverter.ToInt32(b, ex + 32);
            int names = Off(namesRva);
            for (int i = 0; i < count; i++)
            {
                int o = Off(BitConverter.ToInt32(b, names + i * 4)), e = o;
                while (b[e] != 0) e++;
                list.Add(Encoding.ASCII.GetString(b, o, e - o));
            }
            return list;
        }
    }
}
