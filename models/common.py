"""Shared Blender helpers for Mars Sweepers models.

Style rules (matching the game's own stations and machines):
  * flat PBR colours, no textures; every material is named exactly like the game material it maps to,
    so the mod swaps in the real game material at runtime (same shader, same look in game lighting)
  * chunky, friendly shapes with generous bevels (2-3 segments), no razor-sharp edges
  * dark vent slats, orange enamel trim, ivory/white panels, graphite chassis, satin aluminium details
  * 1 Blender unit = 1 metre, +Z up, model front faces -Y (exported to glTF Y-up)
"""
import bpy, bmesh, math
from mathutils import Vector, Matrix

# name: (base colour linear RGB, metallic, roughness, emission)
# Exact game material names and values (from the research dump, shader "Moon Cleaners/Station finish").
# Colours are sRGB as stored in the game; converted to linear for Blender previews.
PALETTE = {
    "MC - Porcelain ivory polymer": ((0.916, 0.926, 0.931), 0.0, 0.27, None),
    "MC - Graphite chassis":        ((0.200, 0.245, 0.283), 0.28, 0.33, None),
    "MC - Tangerine safety trim":   ((0.978, 0.496, 0.172), 0.0, 0.29, None),
    "MC - Cyan frosted diffuser":   ((0.206, 0.722, 0.854), 0.0, 0.25, ((0.206, 0.722, 0.854), 4.0)),
    "MC - Rubber panel seals":      ((0.143, 0.156, 0.176), 0.0, 0.70, None),
    "Solar - Brushed hardware":     ((0.547, 0.601, 0.634), 0.75, 0.50, None),
    "Conveyor port - Graphite":     ((0.274, 0.282, 0.290), 0.2, 0.50, None),
    "Conveyor port - Orange enamel":((0.918, 0.455, 0.141), 0.0, 0.40, None),
    "Conveyor port - Throat dark":  ((0.118, 0.118, 0.125), 0.0, 0.85, None),
    "Conveyor port - Curtain strip":((0.306, 0.318, 0.337), 0.0, 0.45, None),
    "Pressure chamber - Sight glass": ((0.75, 0.85, 0.95), 0.0, 0.05, None),
    "collectorbluelaminatedglass":  ((0.18, 0.30, 0.45), 0.0, 0.08, None),
    "collectorintakedarkness":      ((0.133, 0.133, 0.141), 0.0, 0.80, None),
    # Mars-only materials (the mod creates them from these values)
    "mars:hematite ore":            ((0.32, 0.12, 0.08), 0.35, 0.55, None),
    "mars:hazard black":            ((0.06, 0.06, 0.065), 0.0, 0.6, None),
    "mars:hazard yellow":           ((0.95, 0.68, 0.10), 0.0, 0.45, None),
    "mars:martian rock":            ((0.50, 0.30, 0.21), 0.0, 0.85, None),
    "mars:martian rock dark":       ((0.33, 0.20, 0.15), 0.0, 0.80, None),
    "mars:hematite":                ((0.30, 0.27, 0.28), 0.85, 0.25, None),
    "mars:hematite red":            ((0.52, 0.16, 0.10), 0.3, 0.45, None),
    "mars:furnace glow":            ((1.0, 0.45, 0.12), 0.0, 0.4, ((1.0, 0.42, 0.08), 6.0)),
    "mars:iron plate":              ((0.62, 0.64, 0.67), 0.9, 0.3, None),
    "mars:lamp":                    ((1.0, 0.93, 0.80), 0.0, 0.3, ((1.0, 0.90, 0.72), 9.0)),
    "mars:screen":                  ((0.06, 0.20, 0.26), 0.0, 0.2, ((0.206, 0.722, 0.854), 1.6)),
}

def srgb_to_linear(c):
    return tuple((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def mat(name):
    m = bpy.data.materials.get(name)
    if m: return m
    col, metal, rough, emit = PALETTE[name]
    col = srgb_to_linear(col)
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*col, 1)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    if emit:
        b.inputs["Emission Color"].default_value = (*srgb_to_linear(emit[0]), 1)
        b.inputs["Emission Strength"].default_value = emit[1]
    return m

def finish(ob, material, bevel=0.03, segments=3, smooth=True, harden=True):
    """Give an object its material and the game's soft bevelled edges."""
    ob.data.materials.clear()
    ob.data.materials.append(mat(material))
    if bevel > 0:
        md = ob.modifiers.new("bevel", "BEVEL")
        md.width = bevel; md.segments = segments; md.limit_method = "ANGLE"; md.angle_limit = math.radians(35)
        md.harden_normals = harden
    if smooth:
        for p in ob.data.polygons: p.use_smooth = True
        try:
            md = ob.modifiers.new("wn", "WEIGHTED_NORMAL"); md.keep_sharp = True
        except Exception: pass
    return ob

def box(name, size, loc, material, bevel=0.03, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc, rotation=rot)
    ob = bpy.context.object; ob.name = name
    ob.scale = size
    bpy.ops.object.transform_apply(scale=True)
    return finish(ob, material, bevel)

def cyl(name, r, h, loc, material, verts=32, bevel=0.02, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cylinder_add(radius=r, depth=h, vertices=verts, location=loc, rotation=rot)
    ob = bpy.context.object; ob.name = name
    return finish(ob, material, bevel)

def rounded_box(name, size, loc, material, radius=0.15, rot=(0, 0, 0)):
    """A box with big rounded vertical edges (the stations' signature shape)."""
    ob = box(name, size, loc, material, bevel=0, rot=rot)
    md = ob.modifiers.new("round", "BEVEL")
    md.width = radius; md.segments = 6; md.limit_method = "ANGLE"; md.angle_limit = math.radians(35)
    md.harden_normals = True
    for p in ob.data.polygons: p.use_smooth = True
    return ob

def torus(name, R, r, loc, material, rot=(0, 0, 0), major=48, minor=16):
    bpy.ops.mesh.primitive_torus_add(major_radius=R, minor_radius=r, location=loc, rotation=rot,
                                     major_segments=major, minor_segments=minor)
    ob = bpy.context.object; ob.name = name
    return finish(ob, material, bevel=0)

def empty(name, loc, direction):
    """Marker for the mod (conveyor port, effect anchor…). direction = flow direction (unit vector)."""
    o = bpy.data.objects.new(name, None)
    o.empty_display_type = "SINGLE_ARROW"; o.empty_display_size = 0.6
    o.location = loc
    d = Vector(direction).normalized()
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(d)
    bpy.context.scene.collection.objects.link(o)
    return o

def parent_all(root_name, objs):
    root = bpy.data.objects.new(root_name, None)
    bpy.context.scene.collection.objects.link(root)
    for o in objs:
        if o.parent is None and o is not root: o.parent = root
    return root

def apply_modifiers():
    for ob in bpy.context.scene.objects:
        if ob.type != "MESH": continue
        bpy.context.view_layer.objects.active = ob
        for md in list(ob.modifiers):
            try: bpy.ops.object.modifier_apply(modifier=md.name)
            except Exception: pass

def export_glb(path):
    apply_modifiers()
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", export_apply=True, export_yup=True,
                              export_extras=True, export_materials="EXPORT", export_normals=True)

# ---------------------------------------------------------------- preview rendering

def preview(path_prefix, target=(0, 0, 1.6), dist=11, views=((35, 28), (215, 22), (120, 8)), res=900, samples=48):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    try: sc.cycles.denoiser = "OPENIMAGEDENOISE"
    except Exception: pass
    sc.render.resolution_x = res; sc.render.resolution_y = res
    sc.view_settings.view_transform = "Standard"   # closest to the game's look (URP, neutral tonemap)
    sc.view_settings.look = "None"
    # world: soft butterscotch Mars sky light
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
    bg = w.node_tree.nodes["Background"]; bg.inputs[0].default_value = (0.62, 0.50, 0.40, 1); bg.inputs[1].default_value = 0.45
    # sun
    bpy.ops.object.light_add(type="SUN", rotation=(math.radians(50), math.radians(10), math.radians(35)))
    sun = bpy.context.object; sun.data.energy = 3.2; sun.data.angle = math.radians(3); sun.data.color = (1.0, 0.95, 0.88)
    # ground: Mars dust
    bpy.ops.mesh.primitive_plane_add(size=2000, location=(0, 0, 0))
    g = bpy.context.object; g.name = "_ground"
    gm = bpy.data.materials.new("_ground"); gm.use_nodes = True
    gb = gm.node_tree.nodes["Principled BSDF"]; gb.inputs["Base Color"].default_value = (0.42, 0.24, 0.15, 1); gb.inputs["Roughness"].default_value = 0.95
    g.data.materials.append(gm)
    bpy.ops.object.camera_add(); cam = bpy.context.object; sc.camera = cam; cam.data.lens = 50
    t = Vector(target)
    for i, (az, el) in enumerate(views):
        a, e = math.radians(az), math.radians(el)
        cam.location = t + Vector((math.cos(a) * math.cos(e), math.sin(a) * math.cos(e), math.sin(e))) * dist
        d = t - cam.location
        cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
        sc.render.filepath = f"{path_prefix}_{i}.png"
        bpy.ops.render.render(write_still=True)


def hazard_panel(name, width, height, loc, yaw, stripe=0.12, depth=0.02):
    """Yellow/black diagonal hazard stripes on a flat panel facing outwards (-Y before yaw)."""
    import bmesh
    def clip(poly, w, h):
        def cut(pts, inside, inter):
            out = []
            for i in range(len(pts)):
                a, b = pts[i - 1], pts[i]
                ia, ib = inside(a), inside(b)
                if ib:
                    if not ia: out.append(inter(a, b))
                    out.append(b)
                elif ia: out.append(inter(a, b))
            return out
        def lerp(a, b, t): return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)
        for axis, lim, keep_less in ((0, 0, False), (0, w, True), (1, 0, False), (1, h, True)):
            ins = (lambda p, ax=axis, l=lim, k=keep_less: p[ax] <= l if k else p[ax] >= l)
            def inter(a, b, ax=axis, l=lim):
                t = (l - a[ax]) / (b[ax] - a[ax]); return lerp(a, b, t)
            poly = cut(poly, ins, inter)
            if not poly: return poly
        return poly
    objs = []
    for colour in ("mars:hazard yellow", "mars:hazard black"):
        me = bpy.data.meshes.new(name + colour); bm = bmesh.new()
        k0 = 0 if colour.endswith("yellow") else 1
        n = int((width + height) / stripe) + 2
        for k in range(-n, n):
            if (k % 2) != k0: continue
            x0 = k * stripe
            para = [(x0, 0), (x0 + stripe, 0), (x0 + stripe + height, height), (x0 + height, height)]
            poly = clip(para, width, height)
            if len(poly) < 3: continue
            vs = [bm.verts.new((p[0] - width / 2, 0, p[1] - height / 2)) for p in poly]
            f = bm.faces.new(vs)
        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
        geo = bmesh.ops.extrude_face_region(bm, geom=bm.faces[:])
        bmesh.ops.translate(bm, vec=(0, depth, 0), verts=[v for v in geo["geom"] if isinstance(v, bmesh.types.BMVert)])
        bm.to_mesh(me); bm.free()
        ob = bpy.data.objects.new(name + " " + colour, me); bpy.context.scene.collection.objects.link(ob)
        ob.location = loc; ob.rotation_euler = (0, 0, yaw)
        finish(ob, colour, bevel=0, smooth=False)
        objs.append(ob)
    return objs


def chamfer_plinth(name, w, d, h, chamfer, z0, material):
    """The game's machine plinth: a low slab with chamfered corners (octagon-ish outline)."""
    import bmesh
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    hw, hd, c = w / 2, d / 2, chamfer
    outline = [(-hw + c, -hd), (hw - c, -hd), (hw, -hd + c), (hw, hd - c), (hw - c, hd), (-hw + c, hd), (-hw, hd - c), (-hw, -hd + c)]
    bot = [bm.verts.new((x, y, z0)) for x, y in outline]
    top = [bm.verts.new((x, y, z0 + h)) for x, y in outline]
    bm.faces.new(list(reversed(bot))); bm.faces.new(top)
    for i in range(8):
        j = (i + 1) % 8
        bm.faces.new([bot[i], bot[j], top[j], top[i]])
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    return finish(ob, material, bevel=0.03, segments=2)

def plinth_edge_lights(prefix, w, d, chamfer, z, step=0.55, size=(0.22, 0.03, 0.03), material="MC - Cyan frosted diffuser"):
    """Little cyan light dashes along the plinth's bottom edge (game signature)."""
    objs = []
    hw, hd, c = w / 2 + 0.005, d / 2 + 0.005, chamfer
    for sx, sy, length, horizontal in ((0, -1, w - 2 * c, True), (0, 1, w - 2 * c, True), (-1, 0, d - 2 * c, False), (1, 0, d - 2 * c, False)):
        n = int(length / step)
        for k in range(n):
            t = (k + 0.5) / n - 0.5
            if horizontal: loc = (t * length, sy * hd, z); rot = (0, 0, 0)
            else: loc = (sx * hw, t * length, z); rot = (0, 0, math.pi / 2)
            objs.append(box(f"{prefix} {sx}{sy}{k}", size, loc, material, bevel=0.008, rot=rot))
    return objs

def conveyor_port(prefix, pos, outward, yaw, z_centre=0.85):
    """The game's conveyor port (as on the garage / stone press): graphite frame, orange enamel ring,
    dark throat with rubber curtain strips. pos = point on the machine face, outward = facing direction."""
    from mathutils import Vector
    o = Vector(outward).normalized()
    p = Vector(pos)
    objs = []
    objs.append(box(f"{prefix} frame", (1.36, 0.21, 1.04), p + o * 0.0, "Conveyor port - Graphite", bevel=0.05, rot=(0, 0, yaw)))
    objs.append(box(f"{prefix} enamel", (1.26, 0.05, 0.94), p + o * 0.12, "Conveyor port - Orange enamel", bevel=0.02, rot=(0, 0, yaw)))
    objs.append(box(f"{prefix} throat", (1.1, 0.05, 0.8), p + o * 0.14, "Conveyor port - Throat dark", bevel=0.01, rot=(0, 0, yaw)))
    side = Vector((-o.y, o.x, 0))
    for k in range(7):
        t = (k - 3) * 0.15
        objs.append(box(f"{prefix} curtain {k}", (0.12, 0.012, 0.7), p + o * 0.175 + side * t + Vector((0, 0, -0.04)),
                        "Conveyor port - Curtain strip" if k % 2 == 0 else "MC - Rubber panel seals", bevel=0.004, rot=(0, 0, yaw)))
    return objs


def rock(name, loc, radius, material, seed, squash=0.7, detail=2, jitter=0.18):
    """A smooth, slightly faceted boulder (like the game's rocks)."""
    import random
    rnd = random.Random(seed)
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=detail, radius=radius, location=loc)
    ob = bpy.context.object; ob.name = name
    for v in ob.data.vertices:
        n = v.co.normalized()
        v.co *= 1 + rnd.uniform(-jitter, jitter)
        v.co.z *= squash
    ob.rotation_euler = (rnd.uniform(-0.3, 0.3), rnd.uniform(-0.3, 0.3), rnd.uniform(0, 6.28))
    return finish(ob, material, bevel=0, smooth=True)

def sphere(name, loc, radius, material, seg=24):
    bpy.ops.mesh.primitive_uv_sphere_add(radius=radius, location=loc, segments=seg, ring_count=seg // 2)
    ob = bpy.context.object; ob.name = name
    return finish(ob, material, bevel=0)

def save_and_preview(out, target, dist, root_name, views=((35, 28), (215, 22), (120, 10))):
    for o in bpy.context.scene.objects:
        if o.name.startswith("COL_"): o.hide_render = True
    parent_all(root_name, [o for o in bpy.context.scene.objects if o.parent is None])
    export_glb(out + ".glb")
    for o in bpy.context.scene.objects:
        if o.name.startswith("COL_"): o.hide_set(True)
    preview(out + "_preview", target=target, dist=dist, views=views)
