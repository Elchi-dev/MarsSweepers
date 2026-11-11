"""Mars battery: an ivory cabinet with three graphite cells, cyan charge bars and heavy terminals.
Stores 120 kW·min. Footprint 1.8 x 1.8 m."""
import sys, math, os
sys.path.insert(0, os.path.dirname(__file__))
from common import *
OUT = sys.argv[sys.argv.index("--") + 1]
reset()
W = 1.8
base(W, W, 0.16, 0.3)
rounded_box("cabinet", (1.4, 1.2, 1.5), (0, 0.05, 1.0), IV, radius=0.14)
rounded_box("cabinet top", (1.45, 1.25, 0.08), (0, 0.05, 1.78), OR, radius=0.15)
rounded_box("cabinet foot", (1.45, 1.25, 0.1), (0, 0.05, 0.3), GR, radius=0.15)
for k in range(3):
    x = (k - 1) * 0.42
    box(f"cell {k}", (0.34, 0.06, 1.1), (x, -0.57, 1.0), GR, bevel=0.03)
    for b in range(5):
        box(f"charge {k} {b}", (0.22, 0.02, 0.1), (x, -0.605, 0.62 + b * 0.19), CY if b < 4 else RB, bevel=0.01)
for sx in (-1, 1):
    cyl(f"terminal {sx}", 0.09, 0.22, (sx * 0.38, 0.1, 1.93), OR if sx > 0 else GR, verts=20, bevel=0.02)
    cyl(f"terminal cap {sx}", 0.11, 0.05, (sx * 0.38, 0.1, 2.05), HW, verts=20, bevel=0.01)
for k in range(5):
    box(f"side fin {k}", (0.05, 0.9, 0.06), (0.73, 0.05, 0.55 + k * 0.22), GR, bevel=0.01)
    box(f"side fin b {k}", (0.05, 0.9, 0.06), (-0.73, 0.05, 0.55 + k * 0.22), GR, bevel=0.01)
hazard_panel("warning", 0.6, 0.18, (0, 0.66, 1.5), math.pi, stripe=0.08)
box("COL_body", (1.5, 1.3, 2.0), (0, 0.05, 1.0), GR, bevel=0)
save_and_preview(OUT, (0, 0, 1.0), 6.0, "Mars battery")
print("DONE")
