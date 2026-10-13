"""Scale charts of the monuments (MONUMENTS.md §8.5): monuments side by side on one ground line, seen from the front
with an orthographic camera, smallest first, each labelled with its name and size, a person and a Tin Finch at the
start for scale.

    blender -b --factory-startup --python art/monuments/scale_chart.py -- --out docs/images/monuments/scale-all.jpg
    blender -b --factory-startup --python art/monuments/scale_chart.py -- --tiers roadside,small --out ...
    blender -b --factory-startup --python art/monuments/scale_chart.py -- grid-substation atmosphere-processor --out ...
"""
import math
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent))

import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

import monkit  # noqa: E402,F401
from monkit import GAME, kit, preview, sites  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
keys, out, tiers, width = [], None, None, 3200
i = 0
while i < len(argv):
    a = argv[i]
    if a == "--out":
        out = Path(argv[i + 1])
        i += 2
        continue
    if a == "--tiers":
        tiers = set(argv[i + 1].split(","))
        i += 2
        continue
    if a == "--width":
        width = int(argv[i + 1])
        i += 2
        continue
    keys.append(a)
    i += 1
if out is None:
    out = Path("docs/images/monuments/scale-all.jpg")
if not out.is_absolute():
    out = GAME.parent / out

mods = sites.discover()

preview.reset_scene()
preview.setup_render(width, int(width * 0.36), 32)
preview.setup_sky(32.0, 160.0)

col = bpy.data.collections.new("chart")
bpy.context.scene.collection.children.link(col)
builders = []
for m in (mods[k] for k in keys) if keys else mods.values():
    b = sites.make(m)
    t = b.meta.get("tier")
    if tiers and t not in tiers:
        continue
    group = bpy.data.collections.new(b.key)
    col.children.link(group)
    objs = [o for part in b.realise(group, 1) for o in part]
    # Exact bounds of what was built (world space, before moving it into the row).
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    builders.append((max(hi.x - lo.x, hi.z), b, lo, hi, objs))
builders.sort(key=lambda r: r[0])

x = 0.0
gap_min = 8.0
placed = []
for _, b, lo, hi, objs in builders:
    w = hi.x - lo.x
    gap = max(gap_min, w * 0.12)
    x += gap
    cx = x - lo.x
    for o in objs:
        o.matrix_world.translation += Vector((cx, 0, 0))
    placed.append((b, cx + (lo.x + hi.x) / 2, hi.z, w, hi.y - lo.y))
    x = cx + hi.x
total = x + gap_min

# Materials (no baked occlusion in the chart; the levels used are level 1).
from hollow.export import set_export_colour  # noqa: E402
import numpy as np  # noqa: E402
used = set()
for o in bpy.data.objects:
    if o.type == "MESH" and o.data.color_attributes.get("Tint"):
        me = o.data
        tint = np.empty(len(me.vertices) * 4, np.float32)
        me.color_attributes["Tint"].data.foreach_get("color", tint)
        set_export_colour(o, tint.reshape(-1, 4), "POINT")
    if o.type == "MESH":
        used |= {m.name for m in o.data.materials if m}
from monkit.surfaces import MATERIALS  # noqa: E402
for name in used & set(MATERIALS):
    preview.surface_material(name)

preview.ground(size=max(20000.0, total * 3))
preview.figure((gap_min * 0.3, -2.0, 0), 0.0)
preview.tin_finch((gap_min * 0.3 - 6.0, 4.0, 0), 90.0)

# Labels under the ground line, on a dark band.
tallest = max(h for _, _, h, _, _ in placed)
band = bpy.data.meshes.new("band")
band_h = tallest * 0.16 + 4
band.from_pydata([(-total, -50, -band_h), (total * 2, -50, -band_h), (total * 2, -50, -0.05), (-total, -50, -0.05)], [],
                 [[0, 1, 2, 3]])
bo = bpy.data.objects.new("band", band)
bpy.context.scene.collection.objects.link(bo)
bm = bpy.data.materials.new("band")
bm.use_nodes = True
bm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.02, 0.022, 0.025, 1)
band.materials.append(bm)
tm = bpy.data.materials.new("label")
tm.use_nodes = True
nt = tm.node_tree
em = nt.nodes.new("ShaderNodeEmission")
em.inputs["Color"].default_value = (0.92, 0.92, 0.9, 1)
em.inputs["Strength"].default_value = 1.2
nt.links.new(em.outputs[0], nt.nodes["Material Output"].inputs[0])
size = max(1.2, tallest * 0.022)
for b, cx, h, w, d in placed:
    for line, z, s in ((b.title, -size * 1.6, size), (f"{w:.0f} × {d:.0f} m, {h:.0f} m tall", -size * 3.1, size * 0.7)):
        cu = bpy.data.curves.new(f"label_{b.key}", "FONT")
        cu.body = line
        cu.size = s
        cu.align_x = "CENTER"
        t = bpy.data.objects.new(f"label_{b.key}", cu)
        t.location = (cx, -55.0, z)
        t.rotation_euler = (math.radians(90), 0, 0)
        cu.materials.append(tm)
        bpy.context.scene.collection.objects.link(t)

# Orthographic camera from the front, framing everything above and the labels below.
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
bpy.context.scene.collection.objects.link(cam)
bpy.context.scene.camera = cam
cam.data.type = "ORTHO"
aspect = 0.36
span = max(total + 40, (tallest + band_h) / aspect * 1.08)
cam.data.ortho_scale = span
cam.data.clip_end = 100000
cam.location = (total / 2, -5000.0, (tallest - band_h) / 2 + span * aspect * 0.02)
cam.rotation_euler = (math.radians(90), 0, 0)
preview.render(out)
