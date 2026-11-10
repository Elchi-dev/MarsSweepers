using System;
using System.Reflection;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// Small reflection helpers for Unity types this mod is not compiled against (UI, TextMeshPro, Physics,
    /// Audio, Input System). Types are looked up by exact name and assembly; no assembly-wide scans.
    /// </summary>
    internal static class R
    {
        public static Type T(string fullName, string assembly) => Type.GetType(fullName + ", " + assembly, false);

        /// <summary>Add a Unity built-in component (not one of ours) and return it wrapped in its interop type.</summary>
        public static object Add(GameObject go, Type t)
        {
            var c = go.AddComponent(Il2CppType.From(t));
            return c == null ? null : Activator.CreateInstance(t, c.Pointer);
        }

        public static object GetComp(GameObject go, Type t)
        {
            var c = go.GetComponent(Il2CppType.From(t));
            return c == null ? null : Activator.CreateInstance(t, c.Pointer);
        }

        public static void Set(object o, string prop, object value)
        {
            var p = o.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            if (p == null || !p.CanWrite) throw new MissingMemberException(o.GetType().Name + "." + prop);
            if (value != null && p.PropertyType.IsEnum && !(value.GetType().IsEnum)) value = Enum.ToObject(p.PropertyType, value);
            p.SetValue(o, value);
        }

        public static bool TrySet(object o, string prop, object value)
        {
            try { Set(o, prop, value); return true; } catch { return false; }
        }

        public static object Get(object o, string prop)
        {
            var p = o.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            return p?.GetValue(o);
        }

        public static object Call(object o, string method, params object[] args)
        {
            foreach (var m in o.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != method || m.GetParameters().Length != args.Length || m.IsGenericMethodDefinition) continue;
                return m.Invoke(o, args);
            }
            throw new MissingMethodException(o.GetType().Name + "." + method);
        }
    }
}
