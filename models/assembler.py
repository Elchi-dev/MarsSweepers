"""Mars assembler: iron plates and basalt fiber into machine parts. A glass-walled work cell on a
porcelain base with a robot arm that turns while it works, a parts rack and a control cabinet.
Footprint 4.4 x 4.4 m."""
import sys, math, os
sys.path.insert(0, os.path.dirname(__file__))
from common import *
OUT = sys.argv[sys.argv.index("--") + 1]
reset()
W = 4.4
base(W, W, 0.2, 0.62)
rounded_box("cell base", (2.8, 2.8, 0.6), (0, 0.2, 0.55), IV, radius=0.18)
rounded_box("cell base band", (2.86, 2.86, 0.1), (0, 0.2, 0.85), OR, radius=0.19)
# glass cell: four corner posts, glass walls, a roof frame
for sx in (-1, 1):
    for sy in (-1, 1):
        box(f"cell post {sx}{sy}", (0.14, 0.14, 1.9), (sx * 1.33, 0.2 + sy * 1.33, 1.85), GR, bevel=0.03)
for i, (dx, dy) in enumerate(((0, -1), (1, 0), (0, 1), (-1, 0))):
    size = (2.5, 0.04, 1.75) if dy else (0.04, 2.5, 1.75)
    box(f"cell glass {i}", size, (dx * 1.33, 0.2 + dy * 1.33, 1.85), GLASS, bevel=0.005)
rounded_box("cell roof", (2.86, 2.86, 0.14), (0, 0.2, 2.85), GR, radius=0.12)
box("roof light", (1.6, 1.6, 0.03), (0, 0.2, 2.77), "mars:lamp", bevel=0.01)
# robot arm (spins)
arm = [cyl("arm base", 0.32, 0.25, (0, 0.2, 1.05), OR, verts=32, bevel=0.03),
       box("arm upper", (0.16, 0.16, 0.9), (0.15, 0.2, 1.55), IV, bevel=0.05, rot=(0, math.radians(18), 0)),
       sphere("arm elbow", (0.3, 0.2, 2.0), 0.13, GR),
       box("arm lower", (0.12, 0.12, 0.75), (0.6, 0.2, 1.85), IV, bevel=0.04, rot=(0, math.radians(-60), 0)),
       cyl("arm tool", 0.06, 0.2, (0.92, 0.2, 1.62), CY, verts=12, bevel=0.01)]
spin_group("arm", arm, centre=(0, 0.2))
# workpiece on a fixture
box("fixture", (0.5, 0.5, 0.15), (0.0, 0.2, 0.93), GR, bevel=0.02)
cyl("workpiece gear", 0.2, 0.08, (0.0, 0.2, 1.05), "mars:iron plate", verts=24, bevel=0.01)
# parts rack outside
box("rack", (0.5, 1.2, 1.2), (-1.8, -0.9, 0.9), GR, bevel=0.03)
for k in range(3):
    box(f"rack shelf {k}", (0.55, 1.25, 0.04), (-1.8, -0.9, 0.5 + k * 0.4), OR, bevel=0.01)
    for j in range(3):
        cyl(f"part {k}{j}", 0.1, 0.07, (-1.8, -1.25 + j * 0.35, 0.57 + k * 0.4), "mars:iron plate", verts=16, bevel=0.01)
# cabinet
rounded_box("cabinet", (0.9, 0.6, 1.4), (1.6, -1.55, 0.95), IV, radius=0.12)
box("screen", (0.6, 0.03, 0.4), (1.6, -1.86, 1.25), "mars:screen", bevel=0.01)
box("cabinet top", (0.95, 0.65, 0.06), (1.6, -1.55, 1.68), OR, bevel=0.02)
ports(W, W, sides=((-1, 0), (1, 0), (0, 1), (0, -1)), front_x=-0.2)
box("COL_body", (3.0, 3.0, 3.0), (0, 0.2, 1.5), GR, bevel=0)
save_and_preview(OUT, (0, 0, 1.5), 12, "Mars assembler")
print("DONE")
