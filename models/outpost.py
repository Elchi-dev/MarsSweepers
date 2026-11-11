"""Alien trader outpost: clearly not made by the crew. A smooth purple-grey pod on three curved legs, a ring
of glowing alloy, a trading counter with floating crystals, and a hovering antenna orb. ~5 m across."""
import sys, math, os
sys.path.insert(0, os.path.dirname(__file__))
from common import *
OUT = sys.argv[sys.argv.index("--") + 1]
reset()
SHELL, DARKS, ALLOY = "mars:alien shell", "mars:alien dark", "mars:alloy"
# landing pads and legs
from mathutils import Vector
for k in range(3):
    a = k / 3 * math.tau + math.pi / 2
    pad = Vector((math.cos(a) * 2.3, math.sin(a) * 2.3, 0.06))
    top = Vector((math.cos(a) * 1.0, math.sin(a) * 1.0, 2.0))
    cyl(f"pad {k}", 0.45, 0.12, pad, DARKS, verts=32, bevel=0.04)
    d = top - pad
    ob = cyl(f"leg {k}", 0.11, d.length, (pad + top) / 2, DARKS, verts=16, bevel=0.03)
    ob.rotation_euler = d.to_track_quat("Z", "Y").to_euler()
    sphere(f"foot joint {k}", pad + Vector((0, 0, 0.12)), 0.16, ALLOY)
# pod
sphere("pod", (0, 0, 2.6), 1.6, SHELL, seg=48)
bpy.context.object.scale = (1.0, 1.0, 0.62)
torus("alloy ring", 1.62, 0.09, (0, 0, 2.5), ALLOY, major=64, minor=12)
sphere("pod top", (0, 0, 3.45), 0.55, DARKS, seg=32)
for k in range(6):
    a = k / 6 * math.tau
    sphere(f"window {k}", (math.cos(a) * 1.45, math.sin(a) * 1.45, 2.75), 0.16, "mars:shield")
# counter in front (-Y) with floating crystals
rounded_box("counter", (1.8, 0.7, 0.9), (0, -2.0, 0.45), SHELL, radius=0.25)
box("counter top", (1.9, 0.8, 0.06), (0, -2.0, 0.93), ALLOY, bevel=0.02)
spin = []
for k in range(3):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=0.16, location=(-0.5 + k * 0.5, -2.0, 1.35 + 0.1 * (k % 2)))
    ob = bpy.context.object; ob.name = f"crystal {k}"; ob.scale = (0.7, 0.7, 1.4)
    finish(ob, ALLOY, bevel=0, smooth=False)
    spin_group(f"crystal {k}", [ob])
# antenna and hovering orb
cyl("antenna", 0.05, 1.2, (0, 0, 4.2), DARKS, verts=12, bevel=0)
orb = [sphere("orb", (0, 0, 5.1), 0.25, "mars:shield"), torus("orb ring", 0.4, 0.03, (0, 0, 5.1), ALLOY, rot=(0.6, 0, 0))]
spin_group("orb", orb)
box("COL_pod", (3.2, 3.2, 3.0), (0, 0, 2.4), DARKS, bevel=0)
box("COL_counter", (1.9, 0.8, 1.0), (0, -2.0, 0.5), DARKS, bevel=0)
save_and_preview(OUT, (0, -0.5, 2.2), 13, "Alien trader outpost", views=((300, 22), (215, 18), (120, 8)))
print("DONE")
