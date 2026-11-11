"""Mars logistics hub: a drone base. A porcelain control house with a turning radar dish on top and a landing
pad with two small cargo drones. The drones carry what the machines around it make. Footprint 3.6 x 3.0 m."""
import sys, math, os
sys.path.insert(0, os.path.dirname(__file__))
from common import *
OUT = sys.argv[sys.argv.index("--") + 1]
reset()
W, D = 3.6, 3.0
base(W, D, 0.18, 0.4)
rounded_box("house", (1.4, 1.6, 1.5), (-0.85, 0.3, 1.05), IV, radius=0.18)
rounded_box("house roof", (1.46, 1.66, 0.1), (-0.85, 0.3, 1.84), OR, radius=0.19)
box("door", (0.6, 0.04, 1.0), (-0.85, -0.51, 0.85), GR, bevel=0.03)
box("door lamp", (0.3, 0.03, 0.05), (-0.85, -0.53, 1.45), CY, bevel=0.005)
box("screen", (0.5, 0.03, 0.3), (-1.56, 0.3, 1.2), "mars:screen", bevel=0.01, rot=(0, 0, math.pi / 2))
# radar dish (turns)
cyl("radar post", 0.08, 0.6, (-0.85, 0.3, 2.15), GR, verts=16, bevel=0.01)
dish = [cyl("radar hub", 0.14, 0.18, (-0.85, 0.3, 2.5), HW, verts=20, bevel=0.02)]
bpy.ops.mesh.primitive_uv_sphere_add(radius=0.55, location=(-0.85, 0.3 - 0.25, 2.75), segments=32, ring_count=16)
ob = bpy.context.object; ob.name = "radar dish"; ob.scale = (1, 0.35, 1); ob.rotation_euler = (math.radians(-20), 0, 0)
finish(ob, IV, bevel=0); dish.append(ob)
dish.append(cyl("radar feed", 0.03, 0.5, (-0.85, 0.3 - 0.45, 2.85), GR, verts=8, bevel=0, rot=(math.radians(70), 0, 0)))
spin_group("radar", dish, centre=(-0.85, 0.3))
# landing pad with two drones
cyl("pad", 0.85, 0.08, (0.85, 0.2, 0.3), GR, verts=40, bevel=0.02)
torus("pad ring", 0.8, 0.03, (0.85, 0.2, 0.35), OR, major=40, minor=8)
box("pad mark", (0.9, 0.12, 0.012), (0.85, 0.2, 0.345), IV, bevel=0.002)
box("pad mark 2", (0.12, 0.9, 0.012), (0.85, 0.2, 0.345), IV, bevel=0.002)
for k, (x, y) in enumerate(((0.65, 0.45), (1.1, -0.05))):
    rounded_box(f"drone body {k}", (0.34, 0.34, 0.14), (x, y, 0.47), IV, radius=0.06)
    box(f"drone crate {k}", (0.2, 0.2, 0.14), (x, y, 0.35), OR, bevel=0.02)
    for sx in (-1, 1):
        for sy in (-1, 1):
            box(f"drone arm {k}{sx}{sy}", (0.04, 0.22, 0.03), (x + sx * 0.14, y + sy * 0.14, 0.5), GR, bevel=0.005, rot=(0, 0, math.atan2(sy, sx) + math.pi / 2))
            cyl(f"drone rotor {k}{sx}{sy}", 0.09, 0.015, (x + sx * 0.24, y + sy * 0.24, 0.53), HW, verts=16, bevel=0)
    sphere(f"drone lamp {k}", (x, y - 0.17, 0.47), 0.03, CY)
cyl("antenna", 0.025, 1.4, (-0.3, 0.95, 2.4), HW, verts=8, bevel=0)
sphere("antenna tip", (-0.3, 0.95, 3.12), 0.05, OR)
box("COL_house", (1.5, 1.7, 1.9), (-0.85, 0.3, 0.95), GR, bevel=0)
save_and_preview(OUT, (0, 0, 1.1), 8.5, "Mars logistics hub")
print("DONE")
