"""Mars light pole: a cheap early pole with a big floodlight head. In a dust storm the air around it clears up,
so a line of poles makes a path home. Footprint 1.0 x 1.0 m, 5 m tall. The lamp marker is where the mod puts the light."""
import sys, math, os
sys.path.insert(0, os.path.dirname(__file__))
from common import *
OUT = sys.argv[sys.argv.index("--") + 1]
reset()
IV, GR, OR, CY, RB, HW = ("MC - Porcelain ivory polymer", "MC - Graphite chassis", "MC - Tangerine safety trim",
                          "MC - Cyan frosted diffuser", "MC - Rubber panel seals", "Solar - Brushed hardware")
W = 1.0
chamfer_plinth("plinth", W, W, 0.16, 0.18, 0.0, GR)
chamfer_plinth("plinth lip", W + 0.03, W + 0.03, 0.04, 0.19, 0.16, OR)
plinth_edge_lights("edge light", W + 0.03, W + 0.03, 0.19, 0.06, step=0.32, size=(0.14, 0.025, 0.025))
# base housing with a battery hatch
rounded_box("base", (0.5, 0.5, 0.75), (0, 0, 0.58), IV, radius=0.1)
box("hatch", (0.3, 0.03, 0.36), (0, -0.255, 0.6), GR, bevel=0.015)
box("hatch lamp", (0.12, 0.02, 0.03), (0, -0.272, 0.72), CY, bevel=0.005)
for z in (0.25, 0.92):
    rounded_box(f"base band {z}", (0.54, 0.54, 0.06), (0, 0, z), GR, radius=0.11)
# mast: graphite tube with ivory sleeves and orange stripes
cyl("mast", 0.075, 3.9, (0, 0, 2.9), GR, verts=24, bevel=0.01)
for z, m in ((1.25, IV), (1.55, OR), (3.2, OR), (3.5, IV)):
    cyl(f"sleeve {z}", 0.095, 0.22 if m == IV else 0.08, (0, 0, z), m, verts=24, bevel=0.01)
# head: an arm forward with a wide floodlight
box("arm", (0.12, 0.85, 0.12), (0, -0.38, 4.75), GR, bevel=0.03)
rounded_box("head", (0.9, 0.5, 0.22), (0, -0.82, 4.68), IV, radius=0.08)
box("head trim", (0.94, 0.54, 0.05), (0, -0.82, 4.58), OR, bevel=0.015)
box("lamp", (0.78, 0.38, 0.03), (0, -0.82, 4.555), "mars:lamp", bevel=0.01)
for k in range(5):
    box(f"fin {k}", (0.04, 0.42, 0.06), (-0.32 + k * 0.16, -0.82, 4.82), GR, bevel=0.01)
# top: small solar cap and a beacon
cyl("cap", 0.16, 0.06, (0, 0, 4.88), GR, verts=24, bevel=0.01)
box("solar", (0.55, 0.45, 0.03), (0, 0.12, 5.02), "collectorbluelaminatedglass", bevel=0.01, rot=(math.radians(-18), 0, 0))
box("solar frame", (0.6, 0.5, 0.02), (0, 0.12, 5.0), HW, bevel=0.005, rot=(math.radians(-18), 0, 0))
sphere("beacon", (0, -0.05, 5.15), 0.07, OR)
empty("anchor_lamp", (0, -0.82, 4.5), (0, 0, -1))
box("COL_base", (0.6, 0.6, 1.0), (0, 0, 0.5), GR, bevel=0)
box("COL_mast", (0.2, 0.2, 4.0), (0, 0, 3.0), GR, bevel=0)
save_and_preview(OUT, (0, -0.3, 2.6), 9.5, "Mars light pole", views=((35, 20), (215, 18), (120, 6)))
print("DONE")
