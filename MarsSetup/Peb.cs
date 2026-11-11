using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MarsSetup
{
    /// <summary>
    /// Reads what a running game process was given when it was created: the DLL search path (DllPath), its
    /// image path and current folder. Windows looks for the game's DLLs in DllPath order, so this shows directly
    /// whether the game's own folder comes first.
    /// </summary>
    internal static partial class Program
    {
        [StructLayout(LayoutKind.Sequential)]
        struct PBI { public IntPtr Reserved1, PebBaseAddress, Reserved2a, Reserved2b, UniqueProcessId, Reserved3; }

        [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(IntPtr h, int cls, ref PBI pbi, int len, out int ret);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(int access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out IntPtr read);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        internal static string ProcessPaths(int pid)
        {
            IntPtr h = OpenProcess(0x0400 | 0x0010, false, pid);   // query information, read memory
            if (h == IntPtr.Zero) return "unreadable (open failed " + Marshal.GetLastWin32Error() + ")";
            try
            {
                var pbi = new PBI();
                if (NtQueryInformationProcess(h, 0, ref pbi, Marshal.SizeOf(pbi), out _) != 0) return "unreadable (query failed)";
                IntPtr parms = ReadPtr(h, pbi.PebBaseAddress + 0x20);
                string cur = ReadUs(h, parms + 0x38), dll = ReadUs(h, parms + 0x50), image = ReadUs(h, parms + 0x60);
                return $"DllPath=\"{dll}\" | ImagePathName=\"{image}\" | CurrentDirectory=\"{cur}\"";
            }
            catch (Exception e) { return "unreadable (" + e.Message + ")"; }
            finally { CloseHandle(h); }
        }

        static IntPtr ReadPtr(IntPtr h, IntPtr addr)
        {
            var b = new byte[8];
            if (!ReadProcessMemory(h, addr, b, 8, out _)) throw new Exception("read " + Marshal.GetLastWin32Error());
            return (IntPtr)BitConverter.ToInt64(b, 0);
        }

        static string ReadUs(IntPtr h, IntPtr addr)
        {
            var hdr = new byte[16];
            if (!ReadProcessMemory(h, addr, hdr, 16, out _)) return "?";
            int len = BitConverter.ToUInt16(hdr, 0);
            var buf = (IntPtr)BitConverter.ToInt64(hdr, 8);
            if (len == 0 || buf == IntPtr.Zero) return "";
            var s = new byte[len];
            if (!ReadProcessMemory(h, buf, s, len, out _)) return "?";
            return Encoding.Unicode.GetString(s);
        }
    }
}
