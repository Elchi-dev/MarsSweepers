"""Mars ore drill, v2 (style matched to the game's machines: chamfered graphite plinth with cyan edge lights,
porcelain-ivory pods with graphite bands, tangerine trim, game conveyor ports on all four sides).
Footprint 4.4 x 4.4 m, ~6 m tall. Sits on a mine; the belt that touches a port decides in/out."""
import sys, math, os
sys.path.insert(0, os.path.dirname(__file__))
from common import *
from mathutils import Vector

OUT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else "/tmp/drill"
reset()
IV, GR, OR, CY, RB, HW = ("MC - Porcelain ivory polymer", "MC - Graphite chassis", "MC - Tangerine safety trim",
                          "MC - Cyan frosted diffuser", "MC - Rubber panel seals", "Solar - Brushed hardware")
W = 4.4

# ---- plinth: graphite slab with chamfered corners, a tangerine lip and cyan lights along the bottom edge
chamfer_plinth("plinth", W, W, 0.2, 0.62, 0.0, GR)
chamfer_plinth("plinth lip", W + 0.04, W + 0.04, 0.05, 0.63, 0.2, OR)
chamfer_plinth("deck", W - 0.06, W - 0.06, 0.08, 0.6, 0.25, GR)
plinth_edge_lights("edge light", W + 0.04, W + 0.04, 0.63, 0.07)

# ---- two machinery pods
for sx in (-1, 1):
    x = sx * 1.22
    rounded_box(f"pod {sx}", (0.95, 2.7, 1.95), (x, 0, 0.33 + 0.975), IV, radius=0.3)
    for z in (0.95, 1.98):
        rounded_box(f"pod band {sx} {z}", (1.0, 2.75, 0.13), (x, 0, z), GR, radius=0.3)
    rounded_box(f"pod foot trim {sx}", (1.0, 2.75, 0.07), (x, 0, 0.4), OR, radius=0.3)
    rounded_box(f"pod roof {sx}", (0.85, 2.6, 0.1), (x, 0, 2.33), GR, radius=0.24)
    for sy in (-1, 1):
        # vent grille on the pod's front/back face
        box(f"vent frame {sx}{sy}", (0.62, 0.05, 0.5), (x, sy * 1.37, 1.48), GR, bevel=0.02)
        for k in range(4):
            box(f"vent slat {sx}{sy}{k}", (0.54, 0.05, 0.05), (x, sy * 1.395, 1.32 + k * 0.11), "collectorintakedarkness", bevel=0.012)
        # round pressure gauge, like the game's portholes
        g = Vector((x, sy * 1.37, 0.68))
        torus(f"gauge ring {sx}{sy}", 0.15, 0.03, g + Vector((0, sy * 0.02, 0)), GR, rot=(math.pi / 2, 0, 0), major=32, minor=10)
        cyl(f"gauge glass {sx}{sy}", 0.13, 0.02, g + Vector((0, sy * 0.025, 0)), "Pressure chamber - Sight glass", verts=32, bevel=0, rot=(math.pi / 2, 0, 0))
        box(f"gauge needle {sx}{sy}", (0.015, 0.012, 0.1), g + Vector((0.02, sy * 0.04, 0.03)), OR, bevel=0, rot=(0, math.radians(35 * sx), 0))

# ---- gantry bridge
rounded_box("bridge", (3.4, 1.3, 0.48), (0, 0, 2.6), GR, radius=0.16)
rounded_box("bridge lip", (3.45, 1.35, 0.06), (0, 0, 2.86), OR, radius=0.17)
rounded_box("bridge top", (3.3, 1.2, 0.06), (0, 0, 2.9), GR, radius=0.15)
for sy in (-1, 1):
    hazard_panel(f"bridge hazard {sy}", 2.0, 0.24, (0, sy * 0.665, 2.58), 0 if sy < 0 else math.pi, stripe=0.12)

# ---- the drill (spins in game: everything under anim_spin_drill)
spin = []
spin.append(cyl("drill column", 0.2, 5.0, (0, 0, 2.0), GR, verts=32, bevel=0.02))
spin.append(cyl("rotary table", 0.6, 0.2, (0, 0, 0.95), HW, verts=48, bevel=0.03))
spin.append(torus("gear ring", 0.64, 0.065, (0, 0, 1.08), OR))
for k in range(24):
    a = k / 24 * math.tau
    spin.append(box(f"gear tooth {k}", (0.09, 0.06, 0.11), (math.cos(a) * 0.715, math.sin(a) * 0.715, 1.08), OR, bevel=0.01, rot=(0, 0, a)))
for k in range(5):
    spin.append(cyl(f"auger {k}", 0.33, 0.035, (0, 0, 0.48 + k * 0.08), HW, verts=24, bevel=0.008, rot=(math.radians(10), 0, k * 1.25)))
cyl("collar", 0.46, 0.12, (0, 0, 0.39), GR, verts=40, bevel=0.03)
anim = bpy.data.objects.new("anim_spin_drill", None); bpy.context.scene.collection.objects.link(anim)
for o in spin: o.parent = anim

# ---- mast and motor
for sx in (-1, 1):
    for sy in (-1, 1):
        cyl(f"mast post {sx}{sy}", 0.055, 1.75, (sx * 0.42, sy * 0.42, 3.8), HW, verts=16, bevel=0.01)
for z in (3.3, 4.0):
    rounded_box(f"mast ring {z}", (1.0, 1.0, 0.07), (0, 0, z), OR, radius=0.13)
rounded_box("motor", (1.15, 1.15, 0.85), (0, 0, 4.98), IV, radius=0.32)
rounded_box("motor band", (1.2, 1.2, 0.12), (0, 0, 4.7), GR, radius=0.33)
for k in range(3):
    box(f"motor vent {k}", (0.5, 0.05, 0.05), (0, -0.6, 5.0 + k * 0.1), "collectorintakedarkness", bevel=0.012)
cyl("motor cap", 0.42, 0.1, (0, 0, 5.44), GR, verts=40, bevel=0.03)
bpy.ops.mesh.primitive_uv_sphere_add(radius=0.16, location=(0, 0, 5.52), segments=32, ring_count=16)
finish(bpy.context.object, CY, bevel=0); bpy.context.object.name = "status lamp"
cyl("antenna", 0.015, 0.7, (0.32, 0.32, 5.8), HW, verts=8, bevel=0)
bpy.ops.mesh.primitive_uv_sphere_add(radius=0.04, location=(0.32, 0.32, 6.15), segments=16, ring_count=8)
finish(bpy.context.object, OR, bevel=0); bpy.context.object.name = "antenna tip"

# ---- hoses (rubber) from the pods up to the motor
for sx in (-1, 1):
    cu = bpy.data.curves.new(f"hose {sx}", "CURVE"); cu.dimensions = "3D"; cu.bevel_depth = 0.05; cu.bevel_resolution = 4
    sp = cu.splines.new("BEZIER"); sp.bezier_points.add(1)
    a, b = sp.bezier_points
    a.co = (sx * 1.0, 0.5, 2.38); a.handle_left = a.co; a.handle_right = (sx * 1.0, 0.5, 3.7)
    b.co = (sx * 0.6, 0.25, 4.85); b.handle_right = b.co; b.handle_left = (sx * 1.15, 0.25, 4.8)
    ob = bpy.data.objects.new(f"hose {sx}", cu); bpy.context.scene.collection.objects.link(ob)
    bpy.context.view_layer.objects.active = ob; ob.select_set(True)
    bpy.ops.object.convert(target="MESH")
    finish(bpy.context.object, RB, bevel=0)

# ---- conveyor ports on all four sides (belt height 0.85 m like every game machine)
PORT_Z = 0.85
for i, (dx, dy) in enumerate(((0, -1), (1, 0), (0, 1), (-1, 0))):
    o = Vector((dx, dy, 0))
    yaw = math.atan2(dy, dx) - math.pi / 2
    if dx != 0:   # on the pod's outer face
        face = Vector((dx * 1.79, 0, PORT_Z))
    else:         # in the gap between the pods, on a chute block
        face = Vector((0, dy * 1.82, PORT_Z))
        box(f"chute {i}", (1.3, 0.75, 1.0), Vector((0, dy * 1.45, 0.83)), GR, bevel=0.05)
    conveyor_port(f"port {i}", face, o, yaw)
    empty(f"port_{i}", face + o * 0.2, (dx, dy, 0))

# ---- collision (not drawn)
for o in (box("COL_body", (W, W, 2.4), (0, 0, 1.2), GR, bevel=0), box("COL_mast", (1.2, 1.2, 3.2), (0, 0, 4.2), GR, bevel=0)):
    o.hide_render = True
empty("anchor_drill_bit", (0, 0, 0.0), (0, 0, -1))
empty("anchor_lamp", (0, 0, 5.52), (0, 0, 1))

parent_all("Mars ore drill", [o for o in bpy.context.scene.objects if o.parent is None])
export_glb(OUT + ".glb")
for o in bpy.context.scene.objects:
    if o.name.startswith("COL_"): o.hide_set(True)
preview(OUT + "_preview", target=(0, 0, 2.4), dist=14.5)
print("DONE")
