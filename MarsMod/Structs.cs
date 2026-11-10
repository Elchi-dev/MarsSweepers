using UnityEngine;

namespace MarsMod
{
    /// <summary>The Unity stubs this mod compiles against lack some struct constructors; the fields are there.</summary>
    internal static class S
    {
        public static Color32 C32(byte r, byte g, byte b, byte a)
        {
            var c = default(Color32);
            c.r = r; c.g = g; c.b = b; c.a = a;
            return c;
        }

        public static Bounds Box(Vector3 centre, float size)
        {
            var b = default(Bounds);
            b.center = centre;
            b.size = Vector3.one * size;
            return b;
        }
    }
}
