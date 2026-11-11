"""Mars planet cannon: the end of the job. A heavy turret ring on a wide plinth, a long barrel pointing at the
sky with glowing charge coils, four fuel tanks and capacitor stacks around it. Footprint 6.4 x 6.4 m, ~11 m tall."""
import sys, math, os
sys.path.insert(0, os.path.dirname(__file__))
from common import *
from mathutils import Vector, Matrix
OUT = sys.argv[sys.argv.index("--") + 1]
reset()
W = 6.4
base(W, W, 0.3, 1.0)
cyl("ring base", 2.3, 0.6, (0, 0, 0.6), GR, verts=64, bevel=0.08)
cyl("ring", 2.1, 0.5, (0, 0, 1.15), IV, verts=64, bevel=0.1)
torus("ring trim", 2.12, 0.07, (0, 0, 1.38), OR, major=64, minor=10)
for k in range(16):
    a = k / 16 * math.tau
    box(f"ring bolt {k}", (0.18, 0.1, 0.12), (math.cos(a) * 2.25, math.sin(a) * 2.25, 0.88), HW, bevel=0.02, rot=(0, 0, a))
# turret head
rounded_box("turret", (2.6, 2.4, 1.4), (0, 0, 2.1), IV, radius=0.35)
rounded_box("turret band", (2.66, 2.46, 0.14), (0, 0, 1.6), GR, radius=0.36)
for sx in (-1, 1):
    rounded_box(f"trunnion {sx}", (0.5, 1.2, 1.2), (sx * 1.45, 0, 2.4), GR, radius=0.2)
    cyl(f"trunnion pin {sx}", 0.3, 0.3, (sx * 1.75, 0, 2.5), OR, verts=32, bevel=0.03, rot=(0, math.pi / 2, 0))
# barrel: tilted 25 degrees from vertical, toward -Y
tilt = math.radians(25)
def along(t):
    return Vector((0, -math.sin(tilt) * t, 2.5 + math.cos(tilt) * t))
cyl("barrel", 0.55, 7.0, along(3.5), GR, verts=48, bevel=0.05, rot=(tilt, 0, 0))
cyl("barrel sleeve", 0.75, 2.0, along(1.2), IV, verts=48, bevel=0.1, rot=(tilt, 0, 0))
for k in range(6):
    torus(f"charge coil {k}", 0.62, 0.08, along(2.6 + k * 0.6), "mars:shield" if k % 2 == 0 else OR, rot=(tilt, 0, 0), major=48, minor=12)
cyl("muzzle", 0.7, 0.5, along(7.1), OR, verts=48, bevel=0.06, rot=(tilt, 0, 0))
cyl("muzzle glow", 0.45, 0.1, along(7.37), "mars:shield", verts=48, bevel=0, rot=(tilt, 0, 0))
# fuel tanks and capacitors around
for k in range(4):
    a = k / 4 * math.tau + math.pi / 4
    x, y = math.cos(a) * 2.55, math.sin(a) * 2.55
    if k % 2 == 0:
        cyl(f"fuel tank {k}", 0.45, 1.8, (x, y, 1.2), OR, verts=32, bevel=0.15)
        sphere(f"fuel cap {k}", (x, y, 2.1), 0.45, OR)
        cyl(f"fuel band {k}", 0.47, 0.08, (x, y, 1.0), GR, verts=32, bevel=0.01)
        sphere(f"fuel lamp {k}", (x, y, 2.6), 0.08, "mars:hydrogen")
    else:
        for j in range(3):
            cyl(f"capacitor {k} {j}", 0.32, 0.45, (x, y, 0.6 + j * 0.5), GR if j % 2 == 0 else IV, verts=24, bevel=0.04)
        cyl(f"capacitor glow {k}", 0.33, 0.06, (x, y, 1.85), "mars:shield", verts=24, bevel=0)
# console
rounded_box("console", (1.2, 0.6, 1.1), (1.7, -2.4, 0.85), IV, radius=0.1)
box("console screen", (0.9, 0.03, 0.4), (1.7, -2.72, 1.05), "mars:screen", bevel=0.01, rot=(math.radians(-15), 0, 0))
cyl("big red button", 0.12, 0.08, (1.7, -2.45, 1.45), "mars:hydrogen", verts=24, bevel=0.02)
hazard_panel("hazard", 2.0, 0.24, (0, -1.21, 1.55), 0, stripe=0.12)
box("COL_body", (5.0, 5.0, 2.8), (0, 0, 1.4), GR, bevel=0)
save_and_preview(OUT, (0, -1, 3.5), 20, "Mars planet cannon", views=((300, 22), (215, 18), (120, 8)))
print("DONE")
