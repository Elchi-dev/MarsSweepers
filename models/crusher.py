"""Mars crusher: 1 basalt into 2 grit. A wide hopper on an ivory housing with a pair of heavy crushing
wheels (they turn while it runs), a grit chute and a small grit pile. Footprint 3.6 x 3.6 m."""
import sys, math, os
sys.path.insert(0, os.path.dirname(__file__))
from common import *
OUT = sys.argv[sys.argv.index("--") + 1]
reset()
W = 3.6
base(W, W)
rounded_box("housing", (2.2, 2.0, 1.6), (0, 0.2, 1.1), IV, radius=0.22)
for z in (0.45, 1.85):
    rounded_box(f"housing band {z}", (2.26, 2.06, 0.12), (0, 0.2, z), GR, radius=0.23)
bpy.ops.mesh.primitive_cone_add(radius1=0.6, radius2=1.15, depth=0.9, vertices=4, location=(0, 0.2, 2.4), rotation=(0, 0, math.pi / 4))
finish(bpy.context.object, OR, bevel=0.04); bpy.context.object.name = "hopper"
bpy.ops.mesh.primitive_cone_add(radius1=0.5, radius2=1.0, depth=0.6, vertices=4, location=(0, 0.2, 2.5), rotation=(0, 0, math.pi / 4))
finish(bpy.context.object, DARK, bevel=0.02); bpy.context.object.name = "hopper throat"
# crushing wheel on top, visible through the hopper: a toothed drum (spins around up)
wheel = [cyl("crusher drum", 0.5, 0.25, (0, 0.2, 2.15), HW, verts=32, bevel=0.03)]
for k in range(12):
    a = k / 12 * math.tau
    wheel.append(box(f"crusher tooth {k}", (0.16, 0.08, 0.28), (math.cos(a) * 0.5, 0.2 + math.sin(a) * 0.5, 2.15), GR, bevel=0.01, rot=(0, 0, a)))
spin_group("crusher", wheel)
for k in range(3):
    rock(f"basalt chunk {k}", (-0.3 + k * 0.3, 0.3 - k * 0.15, 2.7), 0.18, "mars:martian rock dark", 40 + k, squash=0.8)
# chute and grit pile in front
box("chute", (0.7, 0.9, 0.12), (0.65, -1.05, 0.95), GR, bevel=0.03, rot=(math.radians(-25), 0, 0))
for sx in (-1, 1):
    box(f"chute side {sx}", (0.04, 0.9, 0.2), (0.65 + sx * 0.33, -1.05, 1.02), OR, bevel=0.01, rot=(math.radians(-25), 0, 0))
rock("grit pile", (0.65, -1.45, 0.3), 0.45, "mars:fiber", 77, squash=0.45, detail=3, jitter=0.05)
# side motor
cyl("motor", 0.35, 0.8, (-1.35, 0.2, 1.35), GR, verts=32, bevel=0.05, rot=(0, math.pi / 2, 0))
cyl("motor cap", 0.3, 0.1, (-1.78, 0.2, 1.35), OR, verts=32, bevel=0.02, rot=(0, math.pi / 2, 0))
box("status", (0.25, 0.03, 0.06), (-0.5, -0.81, 1.5), CY, bevel=0.005)
hazard_panel("hazard", 1.0, 0.2, (-0.45, -0.82, 1.75), 0, stripe=0.1)
ports(W, W, sides=((-1, 0), (1, 0), (0, 1)))
box("COL_body", (2.6, 2.4, 2.8), (0, 0.1, 1.4), GR, bevel=0)
save_and_preview(OUT, (0, 0, 1.4), 11, "Mars crusher")
print("DONE")
