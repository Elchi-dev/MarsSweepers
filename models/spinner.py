"""Mars fiber spinner: melts basalt grit and spins it into fiber. A small glowing melt pot feeds a big
spool that turns while it runs, wound with dark basalt fiber. Footprint 3.6 x 3.6 m."""
import sys, math, os
sys.path.insert(0, os.path.dirname(__file__))
from common import *
OUT = sys.argv[sys.argv.index("--") + 1]
reset()
W = 3.6
base(W, W)
rounded_box("body", (2.4, 1.4, 1.0), (0, 0.6, 0.85), IV, radius=0.2)
rounded_box("body band", (2.46, 1.46, 0.1), (0, 0.6, 1.3), OR, radius=0.21)
# melt pot
cyl("melt pot", 0.5, 0.9, (-0.7, 0.75, 1.8), GR, verts=40, bevel=0.06)
cyl("melt rim", 0.53, 0.1, (-0.7, 0.75, 2.25), OR, verts=40, bevel=0.02)
cyl("melt", 0.42, 0.05, (-0.7, 0.75, 2.27), "mars:furnace glow", verts=40, bevel=0)
cyl("nozzle", 0.06, 0.5, (-0.25, 0.4, 1.9), HW, verts=12, bevel=0.01, rot=(0, math.pi / 2, 0))
# spool: vertical drum with fiber windings (spins)
spool = [cyl("spool core", 0.25, 1.4, (0.6, -0.1, 1.55), HW, verts=32, bevel=0.02),
         cyl("spool flange low", 0.7, 0.06, (0.6, -0.1, 0.9), OR, verts=48, bevel=0.02),
         cyl("spool flange high", 0.7, 0.06, (0.6, -0.1, 2.2), OR, verts=48, bevel=0.02),
         cyl("spool fiber", 0.55, 1.2, (0.6, -0.1, 1.55), "mars:fiber", verts=48, bevel=0.04)]
for k in range(6):
    spool.append(torus(f"fiber wind {k}", 0.56, 0.02, (0.6, -0.1, 1.05 + k * 0.2), GR, major=48, minor=6))
for k in range(4):
    a = k / 4 * math.tau
    spool.append(box(f"spoke {k}", (0.5, 0.05, 0.04), (0.6 + math.cos(a) * 0.4, -0.1 + math.sin(a) * 0.4, 2.25), GR, bevel=0.01, rot=(0, 0, a)))
spin_group("spool", spool)
cyl("spool axle", 0.08, 0.3, (0.6, -0.1, 2.35), GR, verts=16, bevel=0.01)
# guide arm
box("guide arm", (0.1, 0.1, 1.5), (1.45, -0.1, 1.5), GR, bevel=0.02)
box("guide", (0.7, 0.08, 0.08), (1.15, -0.1, 1.9), HW, bevel=0.02)
sphere("status lamp", (1.45, -0.1, 2.3), 0.09, CY)
ports(W, W, sides=((-1, 0), (0, -1), (0, 1)), front_x=-0.8)
box("COL_body", (2.6, 2.6, 2.4), (0, 0.1, 1.2), GR, bevel=0)
save_and_preview(OUT, (0, 0, 1.3), 10.5, "Mars fiber spinner")
print("DONE")
