"""Mars fuel refinery: hydrogen and oxygen into rocket fuel. A tall distillation column with orange rings,
a big spherical fuel tank on legs, a small flare stack with a glow and pipework. Footprint 4.4 x 4.0 m."""
import sys, math, os
sys.path.insert(0, os.path.dirname(__file__))
from common import *
OUT = sys.argv[sys.argv.index("--") + 1]
reset()
W, D = 4.4, 4.0
base(W, D, 0.2, 0.55)
rounded_box("pump house", (1.6, 1.2, 0.9), (-0.9, -0.8, 0.7), IV, radius=0.15)
box("pump house band", (1.64, 1.24, 0.08), (-0.9, -0.8, 1.05), OR, bevel=0.02)
# column
cyl("column", 0.42, 4.2, (-1.1, 0.8, 2.35), IV, verts=40, bevel=0.06)
for k in range(6):
    torus(f"column ring {k}", 0.45, 0.04, (-1.1, 0.8, 0.8 + k * 0.62), OR if k % 2 == 0 else GR, major=40, minor=8)
sphere("column top", (-1.1, 0.8, 4.45), 0.42, GR)
box("ladder", (0.3, 0.04, 3.6), (-1.1, 0.35, 2.3), HW, bevel=0.005)
for k in range(12):
    box(f"rung {k}", (0.3, 0.06, 0.025), (-1.1, 0.33, 0.7 + k * 0.3), HW, bevel=0.003)
# fuel sphere on legs
sphere("fuel sphere", (0.9, 0.5, 2.0), 1.0, OR)
torus("sphere belt", 1.01, 0.05, (0.9, 0.5, 2.0), GR, major=48, minor=8)
for k in range(4):
    a = k / 4 * math.tau + math.pi / 4
    cyl(f"sphere leg {k}", 0.07, 1.6, (0.9 + math.cos(a) * 0.75, 0.5 + math.sin(a) * 0.75, 1.0), GR, verts=12, bevel=0.01)
sphere("sphere lamp", (0.9, 0.5, 3.02), 0.08, "mars:hydrogen")
# flare stack
cyl("flare", 0.08, 2.0, (1.7, -1.4, 1.3), HW, verts=12, bevel=0.01)
cyl("flare tip", 0.12, 0.2, (1.7, -1.4, 2.35), GR, verts=16, bevel=0.02)
sphere("flare glow", (1.7, -1.4, 2.5), 0.1, "mars:furnace glow")
# pipes
cyl("pipe A", 0.07, 1.8, (-0.1, 0.65, 1.3), HW, verts=16, bevel=0.01, rot=(0, math.pi / 2, 0))
cyl("pipe B", 0.07, 1.2, (-0.1, -0.2, 0.8), HW, verts=16, bevel=0.01, rot=(math.pi / 2, 0, 0))
box("screen", (0.6, 0.03, 0.3), (-0.9, -1.42, 0.75), "mars:screen", bevel=0.005)
ports(W, D, sides=((-1, 0), (1, 0), (0, -1)), front_x=0.4)
box("COL_body", (3.4, 3.0, 3.2), (0, 0.1, 1.6), GR, bevel=0)
save_and_preview(OUT, (0, 0, 1.8), 12.5, "Mars fuel refinery")
print("DONE")
