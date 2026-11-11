"""Mars shield generator: raises a dome against the storm. A heavy base with four capacitor pods, a mast
with a spinning ring and a glowing emitter orb on top. Footprint 3.6 x 3.6 m, 5.5 m tall."""
import sys, math, os
sys.path.insert(0, os.path.dirname(__file__))
from common import *
OUT = sys.argv[sys.argv.index("--") + 1]
reset()
W = 3.6
base(W, W, 0.22, 0.6)
cyl("core", 1.0, 0.9, (0, 0, 0.75), IV, verts=48, bevel=0.1)
cyl("core band", 1.04, 0.12, (0, 0, 0.9), OR, verts=48, bevel=0.02)
cyl("core top", 0.85, 0.15, (0, 0, 1.25), GR, verts=48, bevel=0.04)
for k in range(4):
    a = k / 4 * math.tau + math.pi / 4
    x, y = math.cos(a) * 1.25, math.sin(a) * 1.25
    cyl(f"capacitor {k}", 0.28, 1.1, (x, y, 0.85), GR, verts=24, bevel=0.05)
    cyl(f"capacitor glow {k}", 0.29, 0.08, (x, y, 1.1), "mars:shield", verts=24, bevel=0.01)
    cyl(f"capacitor cap {k}", 0.22, 0.08, (x, y, 1.43), OR, verts=24, bevel=0.02)
cyl("mast", 0.18, 3.2, (0, 0, 2.9), GR, verts=24, bevel=0.02)
for z in (1.6, 2.3, 3.6):
    cyl(f"mast sleeve {z}", 0.24, 0.16, (0, 0, z), IV, verts=24, bevel=0.02)
ring = [torus("emitter ring", 0.85, 0.06, (0, 0, 3.0), OR, major=48, minor=10)]
for k in range(3):
    a = k / 3 * math.tau
    ring.append(box(f"ring spoke {k}", (0.7, 0.06, 0.06), (math.cos(a) * 0.45, math.sin(a) * 0.45, 3.0), GR, bevel=0.01, rot=(0, 0, a)))
    ring.append(sphere(f"ring node {k}", (math.cos(a) * 0.85, math.sin(a) * 0.85, 3.0), 0.1, "mars:shield"))
spin_group("ring", ring)
cyl("emitter cup", 0.45, 0.3, (0, 0, 4.6), GR, verts=40, bevel=0.06)
torus("emitter lip", 0.45, 0.05, (0, 0, 4.76), IV, major=40, minor=8)
sphere("emitter orb", (0, 0, 5.05), 0.38, "mars:shield")
hazard_panel("hazard", 1.0, 0.18, (0, -1.03, 0.65), 0, stripe=0.1)
box("COL_body", (2.6, 2.6, 1.5), (0, 0, 0.75), GR, bevel=0)
box("COL_mast", (0.5, 0.5, 4.0), (0, 0, 3.0), GR, bevel=0)
save_and_preview(OUT, (0, 0, 2.4), 12, "Mars shield generator", views=((35, 22), (215, 18), (120, 8)))
print("DONE")
