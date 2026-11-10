using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Il2CppInterop.Runtime;

namespace MarsProbe
{
    /// <summary>
    /// Raw access to the IL2CPP runtime. We read objects straight from memory using the
    /// runtime's own field offsets instead of going through generated interop wrappers.
    /// That avoids calling game getters (side effects) and the interop struct wrappers that
    /// other modders found to corrupt the stack.
    /// </summary>
    internal static unsafe class Il2
    {
        public const int ObjHeader = 16;      // sizeof(Il2CppObject) on x64
        public const int ArrayData = 32;      // Il2CppArray: header(16) + bounds(8) + max_length(8)

        // Il2CppTypeEnum
        public const int T_BOOL = 0x02, T_CHAR = 0x03, T_I1 = 0x04, T_U1 = 0x05, T_I2 = 0x06, T_U2 = 0x07,
            T_I4 = 0x08, T_U4 = 0x09, T_I8 = 0x0a, T_U8 = 0x0b, T_R4 = 0x0c, T_R8 = 0x0d, T_STRING = 0x0e,
            T_PTR = 0x0f, T_VALUETYPE = 0x11, T_CLASS = 0x12, T_VAR = 0x13, T_ARRAY = 0x14, T_GENERICINST = 0x15,
            T_I = 0x18, T_U = 0x19, T_OBJECT = 0x1c, T_SZARRAY = 0x1d, T_MVAR = 0x1e;

        public const int FIELD_STATIC = 0x10, FIELD_INITONLY = 0x20, FIELD_LITERAL = 0x40;

        static readonly Dictionary<IntPtr, string> nameCache = new Dictionary<IntPtr, string>();
        static readonly Dictionary<IntPtr, bool> unityObjCache = new Dictionary<IntPtr, bool>();
        static readonly Dictionary<IntPtr, Dictionary<long, string>> enumCache = new Dictionary<IntPtr, Dictionary<long, string>>();
        static readonly Dictionary<IntPtr, List<FieldRec>> instFieldCache = new Dictionary<IntPtr, List<FieldRec>>();
        static int cachedPtrOffset = -2;
        static IntPtr staticBuf = Marshal.AllocHGlobal(4096);

        public struct FieldRec
        {
            public IntPtr Field;
            public string Name;
            public IntPtr Type;
            public int Flags;
            public uint Offset;
            public bool IsStatic => (Flags & FIELD_STATIC) != 0;
            public bool IsLiteral => (Flags & FIELD_LITERAL) != 0;
        }

        public static string Str(IntPtr p) => p == IntPtr.Zero ? "" : (Marshal.PtrToStringUTF8(p) ?? "");

        public static string ClassName(IntPtr k)
        {
            if (k == IntPtr.Zero) return "<null>";
            if (nameCache.TryGetValue(k, out var s)) return s;
            string n = Str(IL2CPP.il2cpp_class_get_name(k));
            IntPtr decl = IL2CPP.il2cpp_class_get_declaring_type(k);
            if (decl != IntPtr.Zero && decl != k) s = ClassName(decl) + "/" + n;
            else
            {
                string ns = Str(IL2CPP.il2cpp_class_get_namespace(k));
                s = ns.Length > 0 ? ns + "." + n : n;
            }
            nameCache[k] = s;
            return s;
        }

        public static string TypeName(IntPtr type) => type == IntPtr.Zero ? "?" : Str(IL2CPP.il2cpp_type_get_name(type));

        public static string ImageName(IntPtr k) => Str(IL2CPP.il2cpp_image_get_name(IL2CPP.il2cpp_class_get_image(k)));

        public static IntPtr Parent(IntPtr k) => IL2CPP.il2cpp_class_get_parent(k);

        public static bool IsSubclassOf(IntPtr k, string fullName)
        {
            for (IntPtr c = k; c != IntPtr.Zero; c = Parent(c))
                if (ClassName(c) == fullName) return true;
            return false;
        }

        public static bool IsUnityObject(IntPtr k)
        {
            if (unityObjCache.TryGetValue(k, out var b)) return b;
            b = IsSubclassOf(k, "UnityEngine.Object");
            unityObjCache[k] = b;
            return b;
        }

        public static List<FieldRec> Fields(IntPtr k)
        {
            var list = new List<FieldRec>();
            IntPtr iter = IntPtr.Zero, f;
            while ((f = IL2CPP.il2cpp_class_get_fields(k, ref iter)) != IntPtr.Zero)
            {
                list.Add(new FieldRec
                {
                    Field = f,
                    Name = Str(IL2CPP.il2cpp_field_get_name(f)),
                    Type = IL2CPP.il2cpp_field_get_type(f),
                    Flags = IL2CPP.il2cpp_field_get_flags(f),
                    Offset = IL2CPP.il2cpp_field_get_offset(f),
                });
            }
            return list;
        }

        /// <summary>Instance fields of k and all its parents, base class first, stopping at System.Object.</summary>
        public static List<FieldRec> InstanceFieldsDeep(IntPtr k)
        {
            if (instFieldCache.TryGetValue(k, out var l)) return l;
            var chain = new List<IntPtr>();
            for (IntPtr c = k; c != IntPtr.Zero; c = Parent(c))
            {
                string cn = ClassName(c);
                if (cn == "System.Object" || cn == "UnityEngine.Object" || cn == "UnityEngine.Component" ||
                    cn == "UnityEngine.Behaviour" || cn == "UnityEngine.MonoBehaviour" || cn == "UnityEngine.ScriptableObject") break;
                chain.Insert(0, c);
            }
            l = new List<FieldRec>();
            foreach (var c in chain)
                foreach (var f in Fields(c))
                    if (!f.IsStatic) l.Add(f);
            instFieldCache[k] = l;
            return l;
        }

        public static int ValueSize(IntPtr klass)
        {
            int s = IL2CPP.il2cpp_class_instance_size(klass) - ObjHeader;
            return s > 0 ? s : 8;
        }

        static int PrimSize(int t)
        {
            switch (t)
            {
                case T_BOOL: case T_I1: case T_U1: return 1;
                case T_CHAR: case T_I2: case T_U2: return 2;
                case T_I4: case T_U4: case T_R4: return 4;
                default: return 8;
            }
        }

        public static long ReadInteger(IntPtr addr, int size)
        {
            switch (size)
            {
                case 1: return Marshal.ReadByte(addr);
                case 2: return Marshal.ReadInt16(addr);
                case 4: return Marshal.ReadInt32(addr);
                default: return Marshal.ReadInt64(addr);
            }
        }

        public static string FormatFloat(float f) => f.ToString("G7", System.Globalization.CultureInfo.InvariantCulture);
        public static string FormatDouble(double d) => d.ToString("G10", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Format a value that lives at addr and has il2cpp type `type`.</summary>
        public static string FormatValue(IntPtr addr, IntPtr type, int depth, int structDepth = 0)
        {
            int t = IL2CPP.il2cpp_type_get_type(type);
            switch (t)
            {
                case T_BOOL: return Marshal.ReadByte(addr) != 0 ? "true" : "false";
                case T_CHAR: return "'" + (char)Marshal.ReadInt16(addr) + "'";
                case T_I1: return ((sbyte)Marshal.ReadByte(addr)).ToString();
                case T_U1: return Marshal.ReadByte(addr).ToString();
                case T_I2: return Marshal.ReadInt16(addr).ToString();
                case T_U2: return ((ushort)Marshal.ReadInt16(addr)).ToString();
                case T_I4: return Marshal.ReadInt32(addr).ToString();
                case T_U4: return ((uint)Marshal.ReadInt32(addr)).ToString();
                case T_I8: return Marshal.ReadInt64(addr).ToString();
                case T_U8: return ((ulong)Marshal.ReadInt64(addr)).ToString();
                case T_R4: return FormatFloat(BitConverter.Int32BitsToSingle(Marshal.ReadInt32(addr)));
                case T_R8: return FormatDouble(BitConverter.Int64BitsToDouble(Marshal.ReadInt64(addr)));
                case T_I: case T_U: case T_PTR: return "0x" + Marshal.ReadInt64(addr).ToString("X");
                case T_STRING: return FormatObject(Marshal.ReadIntPtr(addr), depth);
                case T_VALUETYPE:
                case T_GENERICINST:
                    {
                        IntPtr k = IL2CPP.il2cpp_class_from_type(type);
                        if (k == IntPtr.Zero) return "?";
                        if (!IL2CPP.il2cpp_class_is_valuetype(k)) return FormatObject(Marshal.ReadIntPtr(addr), depth);
                        if (IL2CPP.il2cpp_class_is_enum(k)) return FormatEnum(addr, k);
                        return FormatStruct(addr, k, depth, structDepth);
                    }
                case T_CLASS:
                case T_OBJECT:
                case T_ARRAY:
                case T_SZARRAY:
                    return FormatObject(Marshal.ReadIntPtr(addr), depth);
                case T_VAR:
                case T_MVAR:
                    return "<generic>";
                default:
                    return "<t" + t.ToString("X") + ">";
            }
        }

        public static string FormatEnum(IntPtr addr, IntPtr enumClass)
        {
            int size = ValueSize(enumClass);
            if (size > 8) size = 4;
            long v = ReadInteger(addr, size);
            var map = EnumMap(enumClass);
            if (map.TryGetValue(v, out var n)) return n + "(" + v + ")";
            return v.ToString();
        }

        public static Dictionary<long, string> EnumMap(IntPtr enumClass)
        {
            if (enumCache.TryGetValue(enumClass, out var map)) return map;
            map = new Dictionary<long, string>();
            int size = ValueSize(enumClass);
            if (size > 8) size = 4;
            foreach (var f in Fields(enumClass))
            {
                if (!f.IsStatic || !f.IsLiteral) continue;
                try
                {
                    *(long*)staticBuf = 0;
                    IL2CPP.il2cpp_field_static_get_value(f.Field, (void*)staticBuf);
                    long v = ReadInteger(staticBuf, size);
                    if (!map.ContainsKey(v)) map[v] = f.Name;
                }
                catch { }
            }
            enumCache[enumClass] = map;
            return map;
        }

        static string FormatStruct(IntPtr addr, IntPtr k, int depth, int structDepth)
        {
            string cn = ClassName(k);
            if (structDepth > 3) return cn + "{…}";
            var sb = new StringBuilder();
            sb.Append('(');
            int n = 0;
            foreach (var f in Fields(k))
            {
                if (f.IsStatic) continue;
                if (n++ > 0) sb.Append(", ");
                if (n > 16) { sb.Append("…"); break; }
                sb.Append(f.Name).Append('=');
                IntPtr fa = addr + (int)f.Offset - ObjHeader;
                sb.Append(FormatValue(fa, f.Type, depth - 1, structDepth + 1));
            }
            sb.Append(')');
            string shortName = cn.StartsWith("UnityEngine.") ? cn.Substring(12) : cn;
            return shortName + sb;
        }

        public static string UnityName(IntPtr obj)
        {
            try
            {
                if (cachedPtrOffset == -2)
                {
                    cachedPtrOffset = -1;
                    IntPtr uo = IL2CPP.GetIl2CppClass("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
                    if (uo != IntPtr.Zero)
                        foreach (var f in Fields(uo))
                            if (f.Name == "m_CachedPtr") cachedPtrOffset = (int)f.Offset;
                }
                if (cachedPtrOffset >= 0 && Marshal.ReadIntPtr(obj + cachedPtrOffset) == IntPtr.Zero) return "(destroyed)";
                return new UnityEngine.Object(obj).name;
            }
            catch (Exception e) { return "(name? " + e.GetType().Name + ")"; }
        }

        static string CountHint(IntPtr obj, IntPtr k)
        {
            foreach (var f in InstanceFieldsDeep(k))
            {
                if (f.Name == "_size" || f.Name == "_count" || f.Name == "count" || f.Name == "m_Count")
                {
                    if (IL2CPP.il2cpp_type_get_type(f.Type) == T_I4)
                        return " n=" + Marshal.ReadInt32(obj + (int)f.Offset);
                }
            }
            return "";
        }

        public static bool IsArrayClass(string className) => className.EndsWith("]");

        public static string FormatObject(IntPtr obj, int depth)
        {
            if (obj == IntPtr.Zero) return "null";
            IntPtr k = IL2CPP.il2cpp_object_get_class(obj);
            string cn = ClassName(k);
            if (cn == "System.String")
                return "\"" + Out.Trunc(IL2CPP.Il2CppStringToManaged(obj), 160) + "\"";
            if (IsArrayClass(cn))
                return FormatArray(obj, k, cn, depth);
            if (IsUnityObject(k))
                return "<" + cn + " '" + Out.Trunc(UnityName(obj), 80) + "'>";
            if (depth <= 0)
                return "<" + cn + CountHint(obj, k) + ">";

            var sb = new StringBuilder();
            sb.Append('<').Append(cn).Append(CountHint(obj, k)).Append(" {");
            int n = 0;
            foreach (var f in InstanceFieldsDeep(k))
            {
                if (n++ > 0) sb.Append(", ");
                if (n > 14) { sb.Append("…"); break; }
                sb.Append(f.Name).Append('=');
                sb.Append(FormatValue(obj + (int)f.Offset, f.Type, depth - 1));
                if (sb.Length > 3000) { sb.Append("…"); break; }
            }
            sb.Append("}>");
            return sb.ToString();
        }

        public static string FormatArray(IntPtr obj, IntPtr k, string cn, int depth, int maxItems = 8)
        {
            long len = Marshal.ReadInt64(obj + 24);
            var sb = new StringBuilder();
            sb.Append(cn.Substring(0, cn.Length - 2)).Append('[').Append(len).Append(']');
            if (len <= 0) return sb.ToString();
            IntPtr ek = IL2CPP.il2cpp_class_get_element_class(k);
            if (ek == IntPtr.Zero) return sb.ToString();
            IntPtr et = IL2CPP.il2cpp_class_get_type(ek);
            bool vt = IL2CPP.il2cpp_class_is_valuetype(ek);
            int es = vt ? ValueSize(ek) : 8;
            int t = IL2CPP.il2cpp_type_get_type(et);
            if (vt && t >= T_BOOL && t <= T_R8) es = PrimSize(t);
            sb.Append(" {");
            long show = Math.Min(len, maxItems);
            for (long i = 0; i < show; i++)
            {
                if (i > 0) sb.Append(", ");
                IntPtr ea = obj + ArrayData + (int)(i * es);
                sb.Append(FormatValue(ea, et, depth - 1));
                if (sb.Length > 2000) { sb.Append("…"); break; }
            }
            if (len > show) sb.Append(", …");
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>Read a static field into the shared buffer and format it. Caller makes sure the class is initialised.</summary>
        public static string FormatStatic(FieldRec f, int depth)
        {
            IntPtr fk = IL2CPP.il2cpp_class_from_type(f.Type);
            int t = IL2CPP.il2cpp_type_get_type(f.Type);
            int size = 8;
            if ((t == T_VALUETYPE || t == T_GENERICINST) && fk != IntPtr.Zero && IL2CPP.il2cpp_class_is_valuetype(fk)) size = ValueSize(fk);
            if (size > 4000) return "<struct too large: " + size + " bytes>";
            for (int i = 0; i < size + 8 && i < 4096; i += 8) *(long*)(staticBuf + i) = 0;
            IL2CPP.il2cpp_field_static_get_value(f.Field, (void*)staticBuf);
            return FormatValue(staticBuf, f.Type, depth);
        }

        public static IEnumerable<IntPtr> AllImages()
        {
            uint size = 0;
            IntPtr domain = IL2CPP.il2cpp_domain_get();
            IntPtr* asms = IL2CPP.il2cpp_domain_get_assemblies(domain, ref size);
            var list = new List<IntPtr>();
            for (uint i = 0; i < size; i++) list.Add(IL2CPP.il2cpp_assembly_get_image(asms[i]));
            return list;
        }

        public static IEnumerable<IntPtr> Classes(IntPtr image)
        {
            uint n = IL2CPP.il2cpp_image_get_class_count(image);
            for (uint i = 0; i < n; i++)
            {
                IntPtr k = IL2CPP.il2cpp_image_get_class(image, i);
                if (k != IntPtr.Zero) yield return k;
            }
        }
    }
}
