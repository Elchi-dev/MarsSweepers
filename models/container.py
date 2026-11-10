"""Mars container port: belts (or players) bring Mars materials in, it stores them digitally for the whole
crew (an uplink dish on top). A big screen shows the stock. Footprint 3.4 x 3.4 m. Ports on all four sides."""
import sys, math, os
sys.path.insert(0, os.path.dirname(__file__))
from common import *
from mathutils import Vector
OUT = sys.argv[sys.argv.index("--") + 1]
reset()
IV, GR, OR, CY, RB, HW = ("MC - Porcelain ivory polymer", "MC - Graphite chassis", "MC - Tangerine safety trim",
                          "MC - Cyan frosted diffuser", "MC - Rubber panel seals", "Solar - Brushed hardware")
W = 3.4
chamfer_plinth("plinth", W, W, 0.2, 0.5, 0.0, GR)
chamfer_plinth("plinth lip", W + 0.04, W + 0.04, 0.05, 0.51, 0.2, OR)
chamfer_plinth("deck", W - 0.06, W - 0.06, 0.08, 0.48, 0.25, GR)
plinth_edge_lights("edge light", W + 0.04, W + 0.04, 0.51, 0.07, step=0.5)
# main body: an ivory block with rounded edges and graphite bands
rounded_box("body", (2.2, 2.2, 1.9), (0, 0, 1.28), IV, radius=0.3)
for z in (0.62, 1.95):
    rounded_box(f"band {z}", (2.26, 2.26, 0.12), (0, 0, z), GR, radius=0.31)
rounded_box("roof", (2.0, 2.0, 0.14), (0, 0, 2.3), GR, radius=0.26)
# big stock screen on the front, smaller ones on the sides
box("screen frame", (1.5, 0.06, 0.8), (0, -1.12, 1.45), GR, bevel=0.03)
box("screen", (1.38, 0.05, 0.68), (0, -1.14, 1.45), "mars:screen", bevel=0.01)
for k in range(3):
    box(f"screen bar {k}", (0.9 - k * 0.2, 0.02, 0.06), (-0.15 - k * 0.1, -1.17, 1.65 - k * 0.16), CY, bevel=0.003)
# stacked crate icon on the sides (orange)
for sx in (-1, 1):
    for k in range(3):
        box(f"crate {sx}{k}", (0.05, 0.36, 0.28), (sx * 1.13, (k - 1) * 0.4, 1.55 if k != 1 else 1.86), OR, bevel=0.02)
# uplink dish
cyl("mast", 0.07, 0.9, (0.5, 0.5, 2.8), HW, verts=16, bevel=0.01)
bpy.ops.mesh.primitive_cone_add(radius1=0.08, radius2=0.6, depth=0.22, vertices=40, location=(0.5, 0.5, 3.35),
                                rotation=(math.radians(-35), 0, math.radians(45)))
finish(bpy.context.object, IV, bevel=0.02); bpy.context.object.name = "dish"
cyl("dish feed", 0.025, 0.45, (0.5, 0.5, 3.5), HW, verts=8, bevel=0)
sphere("dish lamp", (0.5, 0.5, 3.74), 0.06, CY)
# intake funnel on top-front
bpy.ops.mesh.primitive_cone_add(radius1=0.18, radius2=0.42, depth=0.4, vertices=24, location=(-0.55, -0.4, 2.55))
finish(bpy.context.object, OR, bevel=0.02); bpy.context.object.name = "funnel"
PORT_Z = 0.85
for i, (dx, dy) in enumerate(((0, -1), (1, 0), (0, 1), (-1, 0))):
    o = Vector((dx, dy, 0)); yaw = math.atan2(dy, dx) - math.pi / 2
    face = Vector((dx * 1.2, dy * 1.2, PORT_Z))
    conveyor_port(f"port {i}", face, o, yaw)
    empty(f"port_{i}", face + o * 0.2, (dx, dy, 0))
box("COL_body", (W, W, 2.4), (0, 0, 1.2), GR, bevel=0)
save_and_preview(OUT, (0, 0, 1.5), 11, "Mars container port")
print("DONE")
