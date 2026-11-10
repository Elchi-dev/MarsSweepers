"""Mars smelter: turns hematite ore into iron plates. Footprint 4.4 x 5.2 m.
A porcelain furnace tower with graphite bands and a glowing kiln window, chimney stack, ore hopper,
control cabinet with a screen, plate output tray. Game conveyor ports on all four sides."""
import sys, math, os
sys.path.insert(0, os.path.dirname(__file__))
from common import *
from mathutils import Vector
OUT = sys.argv[sys.argv.index("--") + 1]
reset()
IV, GR, OR, CY, RB, HW = ("MC - Porcelain ivory polymer", "MC - Graphite chassis", "MC - Tangerine safety trim",
                          "MC - Cyan frosted diffuser", "MC - Rubber panel seals", "Solar - Brushed hardware")
W, D = 4.4, 5.2
chamfer_plinth("plinth", W, D, 0.2, 0.62, 0.0, GR)
chamfer_plinth("plinth lip", W + 0.04, D + 0.04, 0.05, 0.63, 0.2, OR)
chamfer_plinth("deck", W - 0.06, D - 0.06, 0.08, 0.6, 0.25, GR)
plinth_edge_lights("edge light", W + 0.04, D + 0.04, 0.63, 0.07)
# furnace tower
cyl("furnace", 1.05, 2.6, (0, 0.55, 1.63), IV, verts=48, bevel=0.06)
for z in (0.62, 1.55, 2.6):
    cyl(f"furnace band {z}", 1.09, 0.14, (0, 0.55, z), GR, verts=48, bevel=0.03)
cyl("furnace dome", 0.9, 0.3, (0, 0.55, 3.03), GR, verts=48, bevel=0.1)
# kiln window (glowing) facing front
box("kiln frame", (0.95, 0.2, 0.7), (0, 0.55 - 1.03, 1.1), GR, bevel=0.05)
box("kiln glow", (0.75, 0.2, 0.5), (0, 0.55 - 1.06, 1.1), "mars:furnace glow", bevel=0.03)
for k in range(4):
    box(f"kiln bar {k}", (0.04, 0.22, 0.52), ((k - 1.5) * 0.18, 0.55 - 1.09, 1.1), GR, bevel=0.005)
# chimney
cyl("chimney", 0.22, 2.4, (0.45, 0.85, 4.1), GR, verts=24, bevel=0.02)
torus("chimney ring", 0.27, 0.05, (0.45, 0.85, 4.6), OR, major=32, minor=10)
torus("chimney ring 2", 0.27, 0.05, (0.45, 0.85, 5.1), CY, major=32, minor=10)
cyl("chimney cap", 0.3, 0.08, (0.45, 0.85, 5.33), GR, verts=24, bevel=0.02)
# ore hopper on the left, feeding the furnace
bpy.ops.mesh.primitive_cone_add(radius1=0.25, radius2=0.7, depth=0.9, vertices=6, location=(-1.25, 0.6, 2.75))
finish(bpy.context.object, OR, bevel=0.03); bpy.context.object.name = "hopper"
box("hopper leg", (0.3, 0.3, 2.3), (-1.25, 0.6, 1.4), GR, bevel=0.04)
cyl("feed pipe", 0.12, 1.0, (-0.75, 0.6, 2.35), HW, verts=16, bevel=0.01, rot=(0, math.pi / 2, 0))
# control cabinet with screen
rounded_box("cabinet", (1.0, 0.8, 1.6), (1.45, -0.7, 1.13), IV, radius=0.15)
rounded_box("cabinet top", (1.05, 0.85, 0.08), (1.45, -0.7, 1.95), OR, radius=0.16)
box("screen frame", (0.8, 0.05, 0.55), (1.45, -1.11, 1.4), GR, bevel=0.03)
box("screen", (0.7, 0.05, 0.45), (1.45, -1.13, 1.4), "mars:screen", bevel=0.01)
for k, c in enumerate((OR, CY, GR)):
    cyl(f"button {k}", 0.05, 0.05, (1.25 + k * 0.2, -1.12, 0.85), c, verts=16, bevel=0.01, rot=(math.pi / 2, 0, 0))
# output tray with stacked iron plates
box("tray", (1.4, 1.1, 0.12), (0, -1.75, 0.45), GR, bevel=0.03)
for k in range(5):
    box(f"plate {k}", (0.9, 0.7, 0.05), (0.05 * (k % 2), -1.75, 0.55 + k * 0.06), "mars:iron plate", bevel=0.01, rot=(0, 0, 0.05 * k))
# pipes
cyl("pipe A", 0.08, 1.6, (-0.9, -0.2, 1.4), HW, verts=16, bevel=0.01)
cyl("pipe B", 0.08, 1.2, (-0.9, -0.2, 2.2), HW, verts=16, bevel=0.01, rot=(math.pi / 2, 0, 0))
# ports on all four sides
PORT_Z = 0.85
for i, (dx, dy) in enumerate(((0, -1), (1, 0), (0, 1), (-1, 0))):
    o = Vector((dx, dy, 0)); yaw = math.atan2(dy, dx) - math.pi / 2
    face = Vector((dx * (W / 2 - 0.25), dy * (D / 2 - 0.25), PORT_Z))
    if (dx, dy) == (0, -1): face = Vector((-1.2, -(D / 2 - 0.25), PORT_Z))
    box(f"port block {i}", (1.3, 0.6, 1.1) if dy else (0.6, 1.3, 1.1), face - o * 0.35 + Vector((0, 0, -0.03)), GR, bevel=0.05)
    conveyor_port(f"port {i}", face, o, yaw)
    empty(f"port_{i}", face + o * 0.2, (dx, dy, 0))
box("COL_body", (W, D, 3.2), (0, 0, 1.6), GR, bevel=0)
save_and_preview(OUT, (0, 0, 2.0), 15, "Mars smelter")
print("DONE")
