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
ASPECT = 9 / 16

preview.reset_scene()
preview.setup_render(width, int(width * ASPECT), 32)
preview.setup_sky(32.0, 160.0)

# Sites leave out their look-dev-only scenery (valleys, mountains, ice sheets) when exporting.
kit.EXPORT["enabled"] = True
col = bpy.data.collections.new("chart")
bpy.context.scene.collection.children.link(col)
built = []
for m in (mods[k] for k in keys) if keys else mods.values():
    b = sites.make(m)
    if tiers and b.meta.get("tier") not in tiers:
        continue
    group = bpy.data.collections.new(b.key)
    col.children.link(group)
    objs = [o for part in b.realise(group, 1) for o in part]
    # Exact bounds of what was built (world space, before moving it into its row); only what stands above ground.
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), 0.0))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    built.append((b, lo, hi, objs))
kit.EXPORT["enabled"] = False
built.sort(key=lambda r: max(r[2].x - r[1].x, r[2].z))


def pack(items, rows):
    """Splits the sorted monuments into `rows` rows of about equal width, in order."""
    widths = [(hi.x - lo.x) * 1.15 + 10 for _, lo, hi, _ in items]
    target = sum(widths) / rows
    out, row, acc = [], [], 0.0
    for it, w in zip(items, widths):
        if row and acc + w > target * 1.05 and len(out) < rows - 1:
            out.append(row)
            row, acc = [], 0.0
        row.append(it)
        acc += w
    out.append(row)
    return out


def measure(rows):
    w = max(sum((hi.x - lo.x) * 1.15 + 10 for _, lo, hi, _ in r) for r in rows)
    h = sum(max(hi.z for _, _, hi, _ in r) * 1.12 + 6 for r in rows)
    return w, h


# As many rows as make the chart's shape closest to the image's.
best = None
for n in range(1, min(8, len(built)) + 1):
    rows = pack(built, n)
    w, h = measure(rows)
    label = h * 0.035 * len(rows)
    score = abs(math.log((h + label) / w / ASPECT))
    if best is None or score < best[0]:
        best = (score, rows)
rows = best[1]
W, H = measure(rows)
size = max(1.0, (H / len(rows)) * 0.07)
band_h = size * 4.2
front = min(lo.y for _, lo, _, _ in built) - 30.0

placed = []
z = 0.0
for r, row in enumerate(rows):
    row_h = max(hi.z for _, _, hi, _ in row) * 1.12 + 6
    z_ground = -z - row_h
    x = 0.0
    for b, lo, hi, objs in row:
        w = hi.x - lo.x
        x += max(5.0, w * 0.075)
        cx = x - lo.x
        for o in objs:
            o.matrix_world.translation += Vector((cx, 0, z_ground))
        placed.append((b, cx + (lo.x + hi.x) / 2, z_ground, hi.z, w, hi.y - lo.y))
        x = cx + hi.x + max(5.0, w * 0.075)
    # A person and a Tin Finch at the start of every row.
    preview.figure((-6.0, 0.0, z_ground), 0.0)
    preview.tin_finch((-16.0, 0.0, z_ground), 90.0)
    z += row_h + band_h

# Materials (no baked occlusion in the chart; level 1 of every monument).
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

# Nothing below a row's ground line is drawn (shafts and footings would reach into the row below): every material
# turns transparent under its object's origin, which sits on the row's ground.
for mat in bpy.data.materials:
    if not mat.use_nodes or mat.name.startswith(("band", "label", "preview_")):
        continue
    nt = mat.node_tree
    node_out = next((n for n in nt.nodes if n.type == "OUTPUT_MATERIAL"), None)
    if node_out is None or not node_out.inputs[0].links:
        continue
    src = node_out.inputs[0].links[0].from_socket
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    pos = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(geo.outputs["Position"], pos.inputs[0])
    info = nt.nodes.new("ShaderNodeObjectInfo")
    loc = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(info.outputs["Location"], loc.inputs[0])
    below = nt.nodes.new("ShaderNodeMath")
    below.operation = "SUBTRACT"
    nt.links.new(pos.outputs["Z"], below.inputs[0])
    nt.links.new(loc.outputs["Z"], below.inputs[1])
    keep = nt.nodes.new("ShaderNodeMath")
    keep.operation = "GREATER_THAN"
    keep.inputs[1].default_value = -0.05
    nt.links.new(below.outputs[0], keep.inputs[0])
    clear = nt.nodes.new("ShaderNodeBsdfTransparent")
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(keep.outputs[0], mix.inputs[0])
    nt.links.new(clear.outputs[0], mix.inputs[1])
    nt.links.new(src, mix.inputs[2])
    nt.links.new(mix.outputs[0], node_out.inputs[0])
    if hasattr(mat, "surface_render_method"):
        mat.surface_render_method = "DITHERED"

# Each row stands on a dark band (in front of everything, so what is below ground is hidden) with the labels.
bm = bpy.data.materials.new("band")
bm.use_nodes = True
bm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.02, 0.022, 0.025, 1)
tm = bpy.data.materials.new("label")
tm.use_nodes = True
nt = tm.node_tree
em = nt.nodes.new("ShaderNodeEmission")
em.inputs["Color"].default_value = (0.92, 0.92, 0.9, 1)
em.inputs["Strength"].default_value = 1.2
nt.links.new(em.outputs[0], nt.nodes["Material Output"].inputs[0])
grounds = sorted({round(zg, 3) for _, _, zg, _, _, _ in placed})
for zg in grounds:
    band = bpy.data.meshes.new("band")
    band.from_pydata([(-60, front, zg - band_h), (W + 60, front, zg - band_h), (W + 60, front, zg), (-60, front, zg)], [],
                     [[0, 1, 2, 3]])
    bo = bpy.data.objects.new("band", band)
    band.materials.append(bm)
    bpy.context.scene.collection.objects.link(bo)
for b, cx, zg, h, w, d in placed:
    # A label only where it fits under its monument (the smallest ones are dots on the full chart).
    if len(b.title) * size * 0.5 > max(w * 1.15, 14.0):
        continue
    for line, dz, sz in ((b.title, size * 1.5, size), (f"{w:.0f} × {d:.0f} m, {h:.0f} m tall", size * 2.9, size * 0.72)):
        cu = bpy.data.curves.new(f"label_{b.key}", "FONT")
        cu.body = line
        cu.size = sz
        cu.align_x = "CENTER"
        t = bpy.data.objects.new(f"label_{b.key}", cu)
        t.location = (cx, front - 1.0, zg - dz)
        t.rotation_euler = (math.radians(90), 0, 0)
        cu.materials.append(tm)
        bpy.context.scene.collection.objects.link(t)

# Orthographic camera from the front, framing every row and its labels.
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
bpy.context.scene.collection.objects.link(cam)
bpy.context.scene.camera = cam
cam.data.type = "ORTHO"
total_h = z
span = max(W + 120, total_h / ASPECT) * 1.04
cam.data.ortho_scale = span
cam.data.clip_end = 200000
cam.location = (W / 2, -50000.0, -total_h / 2)
cam.rotation_euler = (math.radians(90), 0, 0)
preview.render(out)
