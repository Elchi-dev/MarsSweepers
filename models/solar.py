"""Mars solar panel: a tilted array of dark cells on a brushed frame, on a small plinth with an inverter box.
Storms cut it to a quarter, dust on the cells lowers it further. Footprint 3.2 x 2.4 m."""
import sys, math, os
sys.path.insert(0, os.path.dirname(__file__))
from common import *
OUT = sys.argv[sys.argv.index("--") + 1]
reset()
W, D = 3.2, 2.4
base(W, D, 0.16, 0.35)
# post and hinge
box("post", (0.3, 0.3, 1.2), (0, 0.25, 0.85), GR, bevel=0.05)
cyl("hinge", 0.12, 1.2, (0, 0.25, 1.48), HW, verts=24, bevel=0.01, rot=(0, math.pi / 2, 0))
for sx in (-1, 1):
    box(f"strut {sx}", (0.08, 0.9, 0.08), (sx * 1.0, -0.15, 1.0), HW, bevel=0.02, rot=(math.radians(-35), 0, 0))
# panel
tilt = math.radians(28)
box("panel frame", (3.0, 2.0, 0.08), (0, 0.15, 1.55), HW, bevel=0.02, rot=(tilt, 0, 0))
box("panel back", (2.9, 1.9, 0.05), (0, 0.17, 1.51), GR, bevel=0.01, rot=(tilt, 0, 0))
import mathutils
R = mathutils.Matrix.Rotation(tilt, 3, "X")
for i in range(6):
    for j in range(4):
        local = mathutils.Vector(((i - 2.5) * 0.47, (j - 1.5) * 0.47, 0.045))
        p = R @ local + mathutils.Vector((0, 0.15, 1.55))
        box(f"cell {i}{j}", (0.43, 0.43, 0.02), p, "mars:solar cell" if (i + j) % 5 else GLASS, bevel=0.006, rot=(tilt, 0, 0))
# inverter box
rounded_box("inverter", (0.7, 0.45, 0.6), (1.05, 0.75, 0.6), IV, radius=0.08)
box("inverter band", (0.74, 0.49, 0.06), (1.05, 0.75, 0.86), OR, bevel=0.01)
box("inverter lamp", (0.2, 0.02, 0.05), (1.05, 0.52, 0.7), CY, bevel=0.005)
for k in range(3):
    box(f"inverter vent {k}", (0.4, 0.02, 0.03), (1.05, 0.52, 0.45 + k * 0.07), DARK, bevel=0.005)
cyl("cable", 0.04, 0.9, (0.6, 0.6, 0.35), RB, verts=12, bevel=0, rot=(0, math.pi / 2, 0))
box("COL_body", (W, D, 2.2), (0, 0, 1.1), GR, bevel=0)
save_and_preview(OUT, (0, 0, 1.0), 8.5, "Mars solar panel", views=((300, 35), (215, 22), (120, 10)))
print("DONE")
