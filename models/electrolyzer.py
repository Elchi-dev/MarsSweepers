"""Mars electrolyzer: splits water ice into oxygen and hydrogen. An ice melt tank, a stack of glowing
cells between two gas tanks (oxygen in ivory with cyan bands, hydrogen in orange), lots of pipe.
Footprint 4.0 x 3.6 m."""
import sys, math, os
sys.path.insert(0, os.path.dirname(__file__))
from common import *
OUT = sys.argv[sys.argv.index("--") + 1]
reset()
W, D = 4.0, 3.6
base(W, D, 0.2, 0.5)
# melt tank (frosty)
cyl("melt tank", 0.55, 1.3, (-1.2, 0.6, 1.0), "mars:frost", verts=40, bevel=0.06)
cyl("melt lid", 0.58, 0.1, (-1.2, 0.6, 1.7), GR, verts=40, bevel=0.02)
for k in range(3):
    rock(f"ice block {k}", (-1.3 + k * 0.15, 0.55 + (k % 2) * 0.15, 1.85), 0.16, "mars:ice", 60 + k, squash=0.9, jitter=0.1)
# cell stack in the middle
rounded_box("stack frame", (1.0, 1.2, 1.7), (0, 0.4, 1.2), GR, radius=0.1)
for k in range(8):
    box(f"cell plate {k}", (1.04, 1.05, 0.06), (0, 0.4, 0.5 + k * 0.19), IV, bevel=0.01)
    box(f"cell glow {k}", (1.06, 0.9, 0.025), (0, 0.4, 0.595 + k * 0.19), CY, bevel=0.003)
cyl("stack cap", 0.4, 0.15, (0, 0.4, 2.15), OR, verts=32, bevel=0.03)
# gas tanks
cyl("oxygen tank", 0.48, 1.9, (1.3, 0.9, 1.3), IV, verts=40, bevel=0.2)
sphere("oxygen dome", (1.3, 0.9, 2.25), 0.48, IV)
for z in (0.7, 1.9):
    cyl(f"oxygen band {z}", 0.5, 0.08, (1.3, 0.9, z), CY, verts=40, bevel=0.01)
cyl("hydrogen tank", 0.42, 1.6, (1.3, -0.4, 1.15), OR, verts=40, bevel=0.18)
sphere("hydrogen dome", (1.3, -0.4, 1.95), 0.42, OR)
for z in (0.6, 1.6):
    cyl(f"hydrogen band {z}", 0.44, 0.08, (1.3, -0.4, z), GR, verts=40, bevel=0.01)
# pipes
cyl("pipe melt", 0.07, 0.9, (-0.6, 0.6, 1.5), HW, verts=16, bevel=0.01, rot=(0, math.pi / 2, 0))
cyl("pipe ox", 0.06, 0.9, (0.75, 0.75, 1.9), HW, verts=16, bevel=0.01, rot=(0, math.pi / 2, 0))
cyl("pipe h2", 0.06, 0.9, (0.75, -0.1, 1.5), HW, verts=16, bevel=0.01, rot=(0, math.pi / 2, 0))
for k, z in enumerate((1.9, 1.5)):
    torus(f"valve {k}", 0.1, 0.025, (0.9, 0.75 if k == 0 else -0.1, z + 0.12), OR, major=24, minor=8)
# control panel
box("panel", (0.9, 0.08, 0.6), (-0.6, -1.0, 1.0), GR, bevel=0.02)
box("screen", (0.7, 0.03, 0.35), (-0.6, -1.05, 1.08), "mars:screen", bevel=0.005)
hazard_panel("hazard", 0.9, 0.16, (-0.6, -1.05, 0.65), 0, stripe=0.08)
ports(W, D, sides=((-1, 0), (0, -1), (0, 1)), front_x=0.6)
box("COL_body", (3.2, 2.8, 2.8), (0, 0.2, 1.4), GR, bevel=0)
save_and_preview(OUT, (0, 0, 1.4), 11, "Mars electrolyzer")
print("DONE")
