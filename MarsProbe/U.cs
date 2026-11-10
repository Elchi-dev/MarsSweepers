using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace MarsProbe
{
    /// <summary>Small helpers around Unity objects and reflection on the runtime interop assemblies.</summary>
    internal static class U
    {
        static readonly Dictionary<string, Type> typeCache = new Dictionary<string, Type>();

        /// <summary>Find a managed interop type by full name in any loaded assembly (e.g. "UnityEngine.Physics").</summary>
        public static Type T(string fullName)
        {
            if (typeCache.TryGetValue(fullName, out var t)) return t;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { t = a.GetType(fullName, false); } catch { t = null; }
                if (t != null) break;
            }
            if (t == null)
            {
                // Interop module assemblies are only loaded once something touches them.
                foreach (var asm in new[] { "UnityEngine.CoreModule", "UnityEngine.PhysicsModule", "Unity.RenderPipelines.Core.Runtime" })
                {
                    try { t = System.Reflection.Assembly.Load(asm).GetType(fullName, false); } catch { t = null; }
                    if (t != null) break;
                }
            }
            typeCache[fullName] = t;
            return t;
        }

        /// <summary>All live objects (scene objects AND loaded assets) of the il2cpp class k, exact class only if exact=true.</summary>
        public static List<IntPtr> FindAll(IntPtr k, bool exact)
        {
            var res = new List<IntPtr>();
            var il2Type = Il2CppType.TypeFromPointer(k, Il2.ClassName(k));
            var arr = Resources.FindObjectsOfTypeAll(il2Type);
            if (arr == null) return res;
            for (int i = 0; i < arr.Length; i++)
            {
                var o = arr[i];
                if (o == null) continue;
                IntPtr p = o.Pointer;
                if (exact && IL2CPP.il2cpp_object_get_class(p) != k) continue;
                res.Add(p);
            }
            return res;
        }

        public static List<IntPtr> FindAll(string assemblyFile, string ns, string cls)
        {
            IntPtr k = IL2CPP.GetIl2CppClass(assemblyFile, ns, cls);
            if (k == IntPtr.Zero) return new List<IntPtr>();
            return FindAll(k, false);
        }

        public static IntPtr Class(string assemblyFile, string ns, string cls) => IL2CPP.GetIl2CppClass(assemblyFile, ns, cls);

        public static string Name(IntPtr obj) => Il2.UnityName(obj);
        public static string Cls(IntPtr obj) => Il2.ClassName(IL2CPP.il2cpp_object_get_class(obj));

        /// <summary>Scene name of a GameObject, or null if it is an asset / prefab.</summary>
        public static string SceneOfGO(GameObject go)
        {
            try
            {
                var sc = go.scene;
                if (!sc.IsValid()) return null;
                return sc.name;
            }
            catch { return null; }
        }

        public static string SceneOfComponent(IntPtr comp)
        {
            try { return SceneOfGO(new Component(comp).gameObject); } catch { return null; }
        }

        public static string PathOf(Transform t, int max = 14)
        {
            var parts = new List<string>();
            int n = 0;
            while (t != null && n++ < max)
            {
                parts.Insert(0, Il2.UnityName(t.gameObject.Pointer));
                t = t.parent;
            }
            return string.Join("/", parts);
        }

        public static string V3(Vector3 v) => $"({Il2.FormatFloat(v.x)}, {Il2.FormatFloat(v.y)}, {Il2.FormatFloat(v.z)})";

        /// <summary>Format a managed value returned by reflection on interop types.</summary>
        public static string Managed(object o)
        {
            if (o == null) return "null";
            switch (o)
            {
                case string s: return "\"" + Out.Trunc(s, 160) + "\"";
                case bool b: return b ? "true" : "false";
                case float f: return Il2.FormatFloat(f);
                case double d: return Il2.FormatDouble(d);
                case Enum e: return e.ToString() + "(" + Convert.ToInt64(e) + ")";
                case Il2CppObjectBase ob:
                    try { return Il2.FormatObject(ob.Pointer, 0); } catch { return "<" + o.GetType().Name + ">"; }
            }
            var t = o.GetType();
            if (t.IsPrimitive) return Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture);
            if (t.IsValueType)
            {
                // Interop Unity structs (Vector3, Color…) are blittable with public fields.
                var fs = t.GetFields(BindingFlags.Public | BindingFlags.Instance);
                if (fs.Length > 0 && fs.Length <= 16)
                    return t.Name + "(" + string.Join(", ", fs.Select(f => f.Name + "=" + Managed(f.GetValue(o)))) + ")";
            }
            return Out.Trunc(o.ToString(), 160);
        }

        /// <summary>Read every simple public static property of a managed interop type (RenderSettings, Physics…).</summary>
        public static IEnumerable<string> StaticProps(string fullName, params string[] skip)
        {
            var t = T(fullName);
            if (t == null) { yield return $"  ({fullName} not loaded)"; yield break; }
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Static).OrderBy(p => p.Name))
            {
                if (p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
                if (skip.Contains(p.Name)) continue;
                string v;
                try { v = Managed(p.GetValue(null)); }
                catch (Exception e) { v = "<" + (e.InnerException ?? e).GetType().Name + ">"; }
                yield return $"  {fullName}.{p.Name} = {v}";
            }
        }

        public static object Call(object target, string name, params object[] args)
        {
            var t = target.GetType();
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != name || m.IsGenericMethodDefinition) continue;
                var ps = m.GetParameters();
                if (ps.Length != args.Length) continue;
                bool ok = true;
                for (int i = 0; i < ps.Length && ok; i++)
                    if (args[i] != null && !ps[i].ParameterType.IsInstanceOfType(args[i])) ok = false;
                if (ok) return m.Invoke(target, args);
            }
            throw new MissingMethodException(t.Name + "." + name);
        }

        public static object Get(object target, string name)
        {
            var p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p == null) throw new MissingMemberException(target.GetType().Name + "." + name);
            return p.GetValue(target);
        }

        /// <summary>Wrap a raw pointer in the managed interop type with the given full name.</summary>
        public static object Wrap(IntPtr p, string fullName)
        {
            var t = T(fullName);
            if (t == null) return null;
            return Activator.CreateInstance(t, p);
        }
    }
}
