using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Il2CppInterop.Runtime;

namespace MarsProbe
{
    internal class ClassInfo
    {
        public IntPtr K;
        public string Name;
        public string Image;
        public bool IsMB, IsSO, IsAbstract, IsInterface, IsGeneric, IsValueType, IsEnum;
        public bool HasStatics;
        public bool IsGame;
    }

    /// <summary>
    /// Walks every class the IL2CPP runtime knows about and writes a readable catalogue:
    /// fields with offsets, methods with signatures and their address in GameAssembly.dll.
    /// This is the "decompiled signatures" view we will mod against.
    /// </summary>
    internal static class Catalog
    {
        public static readonly List<ClassInfo> GameClasses = new List<ClassInfo>();
        public static readonly List<string> ImageSummary = new List<string>();
        public static readonly SortedDictionary<string, List<string>> Interesting = new SortedDictionary<string, List<string>>();
        public static int TotalClasses, TotalGameMethods, TotalGameFields;
        public static long GameAssemblyBase, GameAssemblySize;

        public static readonly string[] Keywords =
        {
            "Planet", "Moon", "Mars", "World", "Sector", "Region", "Hex", "Tile", "Chunk", "Terrain", "Surface", "Crater",
            "Dust", "Sand", "Stone", "Rock", "Ore", "Mine", "Resource", "Material", "Item", "Capsule", "Cargo", "Inventory",
            "Machine", "Depot", "Build", "Belt", "Conveyor", "Splitter", "Merger", "Press", "Chamber", "Packager", "Catcher", "Recipe", "Craft", "Produc",
            "Robot", "Joonas", "JOONAS", "Vacuum", "Vehicle", "Garage", "Tool", "Brush", "Upgrade", "Matrix", "Shop", "Price", "Money", "Wallet", "Sell", "Ship", "Launch", "Bounty",
            "Save", "Slot", "Session", "Lobby", "Net", "Sync", "Rpc", "Steam", "Crew", "Player", "Host", "Client", "Chat",
            "Gravity", "Sky", "Sun", "Earth", "Day", "Night", "Clock", "Time", "Weather", "Storm", "Light", "Power", "Solar", "Energy",
            "Walker", "Suit", "Jetpack", "Boots", "Lamp", "Hud", "Menu", "Map", "Flag", "Landmark", "Station", "Teleport",
            "Achievement", "Mode", "Difficulty", "Settings", "Config", "Definition", "Database", "Registry",
        };

        static bool IsEngineImage(string img)
        {
            string[] pre = { "mscorlib", "System", "Mono.", "UnityEngine", "Unity.", "netstandard", "Il2Cpp", "__Generated", "Microsoft." };
            return pre.Any(p => img.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }

        public static IEnumerator<bool> Build()
        {
            Out.Begin("catalog");
            try
            {
                var mod = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
                    .FirstOrDefault(m => m.ModuleName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase));
                if (mod != null) { GameAssemblyBase = mod.BaseAddress.ToInt64(); GameAssemblySize = mod.ModuleMemorySize; }
            }
            catch { }

            var images = Il2.AllImages().ToList();
            var gameImages = new List<IntPtr>();
            var thirdParty = new List<IntPtr>();
            foreach (var img in images)
            {
                string name = Il2.Str(IL2CPP.il2cpp_image_get_name(img));
                uint count = IL2CPP.il2cpp_image_get_class_count(img);
                TotalClasses += (int)count;
                bool engine = IsEngineImage(name);
                ImageSummary.Add($"{name,-60} classes={count,6}  {(engine ? "engine/system" : "GAME or 3rd-party")}");
                if (!engine)
                {
                    if (name.StartsWith("Assembly-CSharp") || ImageHasMoonTypes(img)) gameImages.Add(img);
                    else thirdParty.Add(img);
                }
            }
            yield return true;

            using (var w = Out.Writer("types_game.txt"))
            {
                w.WriteLine("# Every class in the game's own assemblies, read from the live IL2CPP runtime.");
                w.WriteLine("# field lines:  [static|const|readonly] Type name @offset (= value for consts)");
                w.WriteLine("# method lines: [flags] Return Name(params)  rva=0x... (address in GameAssembly.dll)");
                w.WriteLine($"# GameAssembly.dll base=0x{GameAssemblyBase:X} size=0x{GameAssemblySize:X}");
                w.WriteLine();
                foreach (var img in gameImages)
                {
                    string imgName = Il2.Str(IL2CPP.il2cpp_image_get_name(img));
                    w.WriteLine("######## IMAGE " + imgName);
                    int done = 0;
                    foreach (var k in Il2.Classes(img).ToList())
                    {
                        string key = "catalog:" + Il2.ClassName(k);
                        if (Out.ShouldSkip(key)) { w.WriteLine("# SKIPPED (crashed last run): " + key); continue; }
                        Out.Begin(key);
                        try { DumpClass(w, k, imgName); }
                        catch (Exception e) { w.WriteLine("# ERROR " + e.GetType().Name + ": " + e.Message); }
                        Out.End(key);
                        if (++done % 40 == 0) { w.Flush(); yield return true; }
                    }
                }
            }
            yield return true;

            using (var w = Out.Writer("types_thirdparty.txt"))
            {
                w.WriteLine("# Class names in third-party (non-Unity, non-system) assemblies: networking, Steam, utilities…");
                foreach (var img in thirdParty)
                {
                    w.WriteLine("######## IMAGE " + Il2.Str(IL2CPP.il2cpp_image_get_name(img)));
                    foreach (var k in Il2.Classes(img))
                    {
                        try
                        {
                            IntPtr p = Il2.Parent(k);
                            w.WriteLine("  " + Il2.ClassName(k) + (p != IntPtr.Zero ? " : " + Il2.ClassName(p) : ""));
                        }
                        catch { }
                    }
                }
            }
            Out.End("catalog");
        }

        static bool ImageHasMoonTypes(IntPtr img)
        {
            foreach (var k in Il2.Classes(img))
            {
                string n = Il2.Str(IL2CPP.il2cpp_class_get_name(k));
                if (n.StartsWith("Moon")) return true;
            }
            return false;
        }

        static void DumpClass(StreamWriter w, IntPtr k, string img)
        {
            string name = Il2.ClassName(k);
            int flags = IL2CPP.il2cpp_class_get_flags(k);
            var ci = new ClassInfo
            {
                K = k,
                Name = name,
                Image = img,
                IsInterface = IL2CPP.il2cpp_class_is_interface(k),
                IsAbstract = (flags & 0x80) != 0,
                IsGeneric = IL2CPP.il2cpp_class_is_generic(k),
                IsValueType = IL2CPP.il2cpp_class_is_valuetype(k),
                IsEnum = IL2CPP.il2cpp_class_is_enum(k),
                IsGame = true,
            };
            ci.IsMB = !ci.IsValueType && Il2.IsSubclassOf(k, "UnityEngine.MonoBehaviour");
            ci.IsSO = !ci.IsValueType && Il2.IsSubclassOf(k, "UnityEngine.ScriptableObject");

            string kind = ci.IsInterface ? "interface" : ci.IsEnum ? "enum" : ci.IsValueType ? "struct" : (ci.IsAbstract ? "abstract class" : "class");
            if (ci.IsMB) kind += " [MonoBehaviour]";
            else if (ci.IsSO) kind += " [ScriptableObject]";

            var sb = new StringBuilder();
            sb.Append(kind).Append(' ').Append(name);
            IntPtr parent = Il2.Parent(k);
            if (parent != IntPtr.Zero && !ci.IsEnum && !ci.IsValueType) sb.Append(" : ").Append(Il2.ClassName(parent));
            var ifaces = new List<string>();
            IntPtr it = IntPtr.Zero, ik;
            while ((ik = IL2CPP.il2cpp_class_get_interfaces(k, ref it)) != IntPtr.Zero) ifaces.Add(Il2.ClassName(ik));
            if (ifaces.Count > 0) sb.Append(" implements ").Append(string.Join(", ", ifaces));
            w.WriteLine();
            w.WriteLine(sb.ToString());

            if (ci.IsEnum)
            {
                var map = Il2.EnumMap(k);
                foreach (var kv in map.OrderBy(x => x.Key)) w.WriteLine($"    {kv.Value} = {kv.Key}");
            }
            else
            {
                foreach (var f in Il2.Fields(k))
                {
                    TotalGameFields++;
                    string mods = f.IsLiteral ? "const " : f.IsStatic ? "static " : "";
                    if ((f.Flags & Il2.FIELD_INITONLY) != 0) mods += "readonly ";
                    string access = (f.Flags & 7) == 6 ? "public " : "";
                    string line = $"    {access}{mods}{Il2.TypeName(f.Type)} {f.Name}";
                    if (!f.IsStatic) line += $" @0x{f.Offset:X}";
                    if (f.IsLiteral)
                    {
                        try { line += " = " + Il2.FormatStatic(f, 0); } catch { }
                    }
                    if (f.IsStatic && !f.IsLiteral) ci.HasStatics = true;
                    w.WriteLine(line);
                }
            }

            IntPtr mit = IntPtr.Zero, m;
            while ((m = IL2CPP.il2cpp_class_get_methods(k, ref mit)) != IntPtr.Zero)
            {
                TotalGameMethods++;
                uint iflags = 0;
                uint mf = IL2CPP.il2cpp_method_get_flags(m, ref iflags);
                string mname = Il2.Str(IL2CPP.il2cpp_method_get_name(m));
                uint pc = IL2CPP.il2cpp_method_get_param_count(m);
                var ps = new List<string>();
                for (uint i = 0; i < pc; i++)
                    ps.Add(Il2.TypeName(IL2CPP.il2cpp_method_get_param(m, i)) + " " + Il2.Str(IL2CPP.il2cpp_method_get_param_name(m, i)));
                string mods = "";
                if ((mf & 7) == 6) mods += "public ";
                if ((mf & 0x10) != 0) mods += "static ";
                if ((mf & 0x400) != 0) mods += "abstract ";
                else if ((mf & 0x40) != 0) mods += "virtual ";
                string rva = "";
                long ptr = Marshal.ReadIntPtr(m).ToInt64();
                if (ptr != 0 && GameAssemblyBase != 0 && ptr >= GameAssemblyBase && ptr < GameAssemblyBase + GameAssemblySize)
                    rva = $"  rva=0x{ptr - GameAssemblyBase:X}";
                w.WriteLine($"    {mods}{Il2.TypeName(IL2CPP.il2cpp_method_get_return_type(m))} {mname}({string.Join(", ", ps)}){rva}");
            }

            GameClasses.Add(ci);

            string shortName = name.Contains('.') ? name.Substring(name.LastIndexOf('.') + 1) : name;
            foreach (var kw in Keywords)
            {
                if (shortName.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (!Interesting.TryGetValue(kw, out var l)) Interesting[kw] = l = new List<string>();
                    l.Add(name + (ci.IsMB ? " [MB]" : ci.IsSO ? " [SO]" : ci.IsEnum ? " [enum]" : ci.IsValueType ? " [struct]" : ""));
                }
            }
        }
    }
}
