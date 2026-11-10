"""Mars ore deposit (a mine). A low mound of red rock with shiny hematite nodules breaking through,
and an orange survey stake with a cyan beacon so it can be spotted from far away. ~6 m across."""
import sys, math, os, random
sys.path.insert(0, os.path.dirname(__file__))
from common import *
OUT = sys.argv[sys.argv.index("--") + 1]
reset()
rnd = random.Random(1976)
# mound: a flattened rock sunk into the ground
rock("mound", (0, 0, -0.55), 3.1, "mars:martian rock", 1, squash=0.32, detail=3, jitter=0.035)
rock("mound top", (0.3, 0.2, -0.1), 1.9, "mars:martian rock dark", 2, squash=0.35, detail=3, jitter=0.05)
# boulders around
for k in range(9):
    a = k / 9 * math.tau + rnd.uniform(-0.2, 0.2); r = rnd.uniform(1.7, 2.8)
    rock(f"boulder {k}", (math.cos(a) * r, math.sin(a) * r, 0.1), rnd.uniform(0.35, 0.7), "mars:martian rock dark" if k % 3 == 0 else "mars:martian rock", 10 + k, squash=0.75)
# hematite nodules (botryoidal: clusters of shiny balls) and red ore veins
for c in range(6):
    a = rnd.uniform(0, math.tau); r = rnd.uniform(0.2, 1.5)
    cx, cy = math.cos(a) * r, math.sin(a) * r
    cz = 0.35 - r * 0.12
    for k in range(rnd.randint(4, 8)):
        sphere(f"nodule {c} {k}", (cx + rnd.uniform(-0.3, 0.3), cy + rnd.uniform(-0.3, 0.3), cz + rnd.uniform(-0.05, 0.25)),
               rnd.uniform(0.1, 0.24), "mars:hematite", seg=10)
    rock(f"vein {c}", (cx + 0.2, cy - 0.15, cz - 0.05), rnd.uniform(0.25, 0.4), "mars:hematite red", 30 + c, squash=0.6, detail=2)
# survey stake with beacon
cyl("stake", 0.06, 2.6, (2.3, -1.2, 1.2), "MC - Tangerine safety trim", verts=12, bevel=0.01)
cyl("stake band", 0.075, 0.18, (2.3, -1.2, 1.9), "MC - Graphite chassis", verts=16, bevel=0.01)
sphere("stake beacon", (2.3, -1.2, 2.58), 0.11, "MC - Cyan frosted diffuser")
box("stake flag", (0.02, 0.5, 0.3), (2.3, -0.93, 2.2), "MC - Tangerine safety trim", bevel=0.005)
box("COL_mound", (4.4, 4.4, 0.9), (0, 0, 0.3), "mars:martian rock", bevel=0)
save_and_preview(OUT, (0, 0, 0.4), 11, "Mars ore deposit", views=((35, 30), (200, 25), (110, 12)))
print("DONE")
