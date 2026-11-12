using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace MarsSetup
{
    internal static partial class Program
    {
        /// <summary>
        /// [17]: zips the two files that hold the game's compiled code (GameAssembly.dll and global-metadata.dat) onto
        /// the Desktop, so the mod's author can study how the game works from the player's own copy. The zip is for
        /// that analysis only: it never goes into the mod or its repository.
        /// </summary>
        static void PackGameCode()
        {
            Head("Game code for analysis");
            string asm = P("GameAssembly.dll");
            string data = Directory.GetDirectories(game, "*_Data").FirstOrDefault();
            string meta = data == null ? null : Path.Combine(data, "il2cpp_data", "Metadata", "global-metadata.dat");
            if (!File.Exists(asm) || meta == null || !File.Exists(meta)) { Err("GameAssembly.dll or global-metadata.dat not found in " + game); return; }
            string zip = Path.Combine(Desktop, "MoonSweepers_code_" + Stamp() + ".zip");
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                z.CreateEntryFromFile(asm, "GameAssembly.dll", CompressionLevel.Optimal);
                z.CreateEntryFromFile(meta, "global-metadata.dat", CompressionLevel.Optimal);
                var info = z.CreateEntry("version.txt");
                using (var w = new StreamWriter(info.Open()))
                {
                    w.WriteLine("GameAssembly.dll " + new FileInfo(asm).Length + " bytes, " + File.GetLastWriteTime(asm));
                    w.WriteLine("global-metadata.dat " + new FileInfo(meta).Length + " bytes");
                    w.WriteLine("unity player: " + (File.Exists(P("UnityPlayer.dll")) ? System.Diagnostics.FileVersionInfo.GetVersionInfo(P("UnityPlayer.dll")).FileVersion : "?"));
                }
            }
            Ok($"Packed: {zip} ({new FileInfo(zip).Length / 1024.0 / 1024.0:0.0} MB). Send it over the same way as the report.");
            Info("It stays between us: it is only read to understand the game, never put into the mod or its repository.");
        }
    }
}
