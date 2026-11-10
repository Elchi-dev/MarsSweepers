using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace MarsMod
{
    /// <summary>
    /// Where the storm can't reach. Anything with a roof over it (a collider straight above) is sheltered:
    /// the Collector, other buildings, and later the storm shields and light-pole bubbles, as long as they
    /// have a collider. Used two ways:
    ///  * Indoor (0..1): the camera is inside a room (roof above + walls around). Then the near storm air,
    ///    the blowing dust and the wind sound fade out; the storm stays visible through windows and doors.
    ///  * Roofed(point): dust particles are never spawned under a roof and are removed if they drift under one.
    /// </summary>
    internal static class Shelter
    {
        public static float Indoor { get; private set; }
        /// <summary>Roughly how far the walls are when indoors (metres).</summary>
        public static float RoomRadius { get; private set; } = 30f;

        static MethodInfo raycast;
        static PropertyInfo hitDistance;
        static Type hitType;
        static bool looked, broken;
        static float next;
        static int rays;

        public static int RaysPerSecond { get; private set; }
        static float rayWindow;

        static bool Ensure()
        {
            if (looked) return raycast != null && !broken;
            looked = true;
            try
            {
                var physT = R.T("UnityEngine.Physics", "UnityEngine.PhysicsModule");
                raycast = physT?.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(x =>
                {
                    if (x.Name != "Raycast") return false;
                    var ps = x.GetParameters();
                    return ps.Length == 4 && ps[0].ParameterType == typeof(Vector3) && ps[2].ParameterType.IsByRef && ps[3].ParameterType == typeof(float);
                });
                if (raycast == null) { Log.Once("shelter", "Shelter: no Physics.Raycast, storms reach indoors"); return false; }
                hitType = raycast.GetParameters()[2].ParameterType.GetElementType();
                hitDistance = hitType.GetProperty("distance");
                return true;
            }
            catch (Exception e) { Log.Once("shelter", "Shelter failed: " + e.Message); raycast = null; return false; }
        }

        /// <summary>Cast a ray; true and the distance if something with a collider is hit.</summary>
        public static bool Ray(Vector3 origin, Vector3 dir, float max, out float dist)
        {
            dist = max;
            if (!Ensure()) return false;
            try
            {
                rays++;
                var args = new object[] { origin, dir, Activator.CreateInstance(hitType), max };
                if (!(bool)raycast.Invoke(null, args)) return false;
                dist = (float)hitDistance.GetValue(args[2]);
                return true;
            }
            catch (Exception e) { broken = true; Log.Once("shelter-ray", "Shelter raycast failed, switched off: " + (e.InnerException ?? e).Message); return false; }
        }

        /// <summary>Is there a roof (any collider) within 25 m straight above this point?</summary>
        public static bool Roofed(Vector3 p, Vector3 up) => Ray(p + up * 0.05f, up, 25f, out _);

        public static void Tick(float now, float dt, Vector3 camPos, Vector3 up)
        {
            if (now >= rayWindow) { RaysPerSecond = rays; rays = 0; rayWindow = now + 1f; }
            if (now >= next)
            {
                next = now + 0.25f;
                float target = 0f;
                if (Ray(camPos, up, 40f, out _))
                {
                    var east = Vector3.Cross(up, Vector3.forward); if (east.sqrMagnitude < 1e-4f) east = Vector3.right; east.Normalize();
                    var north = Vector3.Cross(east, up);
                    int hits = 0; float far = 0f;
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI / 4f;
                        var dir = east * Mathf.Cos(a) + north * Mathf.Sin(a);
                        if (Ray(camPos, dir, 35f, out float d)) { hits++; far = Mathf.Max(far, d); }
                    }
                    target = Mathf.Clamp01((hits - 3) / 4f);       // roof + walls on most sides = inside
                    if (hits > 0) RoomRadius = Mathf.Clamp(far * 1.15f, 4f, 40f);
                }
                indoorTarget = target;
            }
            Indoor = Mathf.MoveTowards(Indoor, indoorTarget, dt * 2.5f);
        }

        static float indoorTarget;
    }
}
