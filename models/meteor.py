"""Meteorite: a charred, still glowing rock in a fresh little crater, with purple alloy crystals poking out.
About 4 m across including the crater rim."""
import sys, math, os, random
sys.path.insert(0, os.path.dirname(__file__))
from common import *
OUT = sys.argv[sys.argv.index("--") + 1]
reset()
rnd = random.Random(2026)
# crater rim: a ring of pushed-up red rock
for k in range(14):
    a = k / 14 * math.tau + rnd.uniform(-0.1, 0.1); r = rnd.uniform(1.7, 2.1)
    rock(f"rim {k}", (math.cos(a) * r, math.sin(a) * r, 0.0), rnd.uniform(0.35, 0.6), "mars:martian rock" if k % 3 else "mars:martian rock dark", 100 + k, squash=0.45)
rock("crater floor", (0, 0, -0.35), 1.8, "mars:martian rock dark", 7, squash=0.15, detail=3, jitter=0.05)
# the meteorite: dark, with glowing cracks
rock("meteorite", (0, 0, 0.45), 0.85, "mars:alien dark", 11, squash=0.8, detail=3, jitter=0.12)
for k in range(7):
    a = rnd.uniform(0, math.tau); z = rnd.uniform(0.2, 0.9)
    box(f"crack {k}", (0.6, 0.09, 0.09), (math.cos(a) * 0.84, math.sin(a) * 0.84, z), "mars:furnace glow", bevel=0.01, rot=(rnd.uniform(-0.6, 0.6), 0, a + math.pi / 2))
for k in range(4):
    a = rnd.uniform(0, math.tau)
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=0.14, location=(math.cos(a) * 0.55, math.sin(a) * 0.55, 0.95 + rnd.uniform(-0.1, 0.1)))
    ob = bpy.context.object; ob.name = f"alloy crystal {k}"; ob.scale = (0.6, 0.6, 1.6); ob.rotation_euler = (rnd.uniform(-0.5, 0.5), rnd.uniform(-0.5, 0.5), 0)
    finish(ob, "mars:alloy", bevel=0, smooth=False)
box("COL_rock", (1.6, 1.6, 1.4), (0, 0, 0.5), "mars:alien dark", bevel=0)
save_and_preview(OUT, (0, 0, 0.5), 7.5, "Meteorite", views=((35, 30), (200, 25), (110, 12)))
print("DONE")
