"""Mars fuel generator: burns rocket fuel for 30 kW when the grid runs short. Graphite engine block under an
ivory hood, an orange fuel tank, a radiator with a spinning fan and an exhaust stack. Footprint 3.6 x 2.6 m."""
import sys, math, os
sys.path.insert(0, os.path.dirname(__file__))
from common import *
OUT = sys.argv[sys.argv.index("--") + 1]
reset()
W, D = 3.6, 2.6
base(W, D, 0.2, 0.45)
rounded_box("engine", (2.0, 1.6, 1.1), (-0.3, 0.1, 0.85), GR, radius=0.12)
rounded_box("hood", (2.1, 1.7, 0.5), (-0.3, 0.1, 1.6), IV, radius=0.2)
box("hood stripe", (2.12, 0.25, 0.02), (-0.3, 0.1, 1.86), OR, bevel=0.005)
for k in range(6):
    box(f"hood vent {k}", (0.05, 1.2, 0.03), (-1.0 + k * 0.28, 0.1, 1.86), DARK, bevel=0.005)
# fuel tank on the right
cyl("fuel tank", 0.45, 1.5, (1.25, 0.2, 0.95), OR, verts=40, bevel=0.06, rot=(math.pi / 2, 0, 0))
for y in (-0.35, 0.75):
    cyl(f"tank strap {y}", 0.47, 0.06, (1.25, y, 0.95), GR, verts=40, bevel=0.01, rot=(math.pi / 2, 0, 0))
cyl("filler", 0.08, 0.25, (1.25, -0.2, 1.45), HW, verts=16, bevel=0.01)
sphere("fuel lamp", (1.25, -0.56, 1.25), 0.06, "mars:hydrogen")
# radiator with fan at the back
box("radiator", (1.6, 0.25, 1.2), (-0.3, 1.05, 1.0), GR, bevel=0.04)
for k in range(10):
    box(f"radiator fin {k}", (0.03, 0.06, 1.0), (-1.0 + k * 0.155, 1.19, 1.0), HW, bevel=0.003)
fan = [cyl("fan hub", 0.08, 0.1, (-0.3, 0.0, 2.0), HW, verts=16, bevel=0.01)]
for k in range(5):
    a = k / 5 * math.tau
    fan.append(box(f"fan blade {k}", (0.42, 0.12, 0.02), (-0.3 + math.cos(a) * 0.24, math.sin(a) * 0.24, 2.0), GR, bevel=0.01, rot=(0.25, 0, a)))
torus("fan ring", 0.5, 0.04, (-0.3, 0.0, 2.0), OR)
for sx in (-1, 1):
    cyl(f"fan post {sx}", 0.03, 0.3, (-0.3 + sx * 0.5, 0.0, 1.9), HW, verts=8, bevel=0)
spin_group("fan", fan, centre=(-0.3, 0.0))
# exhaust
cyl("exhaust", 0.13, 1.6, (-1.1, -0.5, 2.2), HW, verts=20, bevel=0.01)
cyl("exhaust cap", 0.17, 0.12, (-1.1, -0.5, 3.0), GR, verts=20, bevel=0.02)
torus("exhaust ring", 0.16, 0.03, (-1.1, -0.5, 2.7), OR)
# control panel front
box("panel", (0.7, 0.06, 0.5), (-0.3, -0.73, 1.0), GR, bevel=0.02)
box("panel screen", (0.5, 0.03, 0.25), (-0.3, -0.77, 1.08), "mars:screen", bevel=0.005)
for k, c in enumerate((OR, CY)):
    cyl(f"button {k}", 0.04, 0.04, (-0.45 + k * 0.3, -0.77, 0.86), c, verts=12, bevel=0.005, rot=(math.pi / 2, 0, 0))
box("COL_body", (W - 0.3, D - 0.3, 2.0), (0, 0, 1.0), GR, bevel=0)
save_and_preview(OUT, (0, 0, 1.2), 9.0, "Mars fuel generator")
print("DONE")
