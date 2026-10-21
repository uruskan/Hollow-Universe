"""Holes in a code-built model (look-dev only): every LOD0 mesh of a saved source .blend drawn with back faces culled, as Godot
draws them, in Workbench's studio light against pure magenta, from eight sides, from above and from below, on one contact sheet
(<prefix>_all.png). Magenta inside the silhouette is a hole or a panel turned inside out.

    blender -b --factory-startup <source.blend> --python art/models/hole_check.py -- <absolute out prefix> [names...]

Names given (without _lod0) are drawn each alone on a sheet of its own (<prefix>_<name>.png), for parts that overlap others in the
source, such as modules at one socket.
"""
import math
import sys

import bpy
import numpy as np
from mathutils import Vector

args = sys.argv[sys.argv.index('--') + 1:]
out, names = args[0], args[1:]
W, H = 720, 480
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.render.resolution_x, scene.render.resolution_y = W, H
shading = scene.display.shading
shading.light = 'STUDIO'
shading.color_type = 'SINGLE'
shading.single_color = (.6, .6, .6)
shading.show_backface_culling = True
shading.show_cavity = True
world = bpy.data.worlds.new('Magenta')
world.color = (1, 0, 1)
scene.world = world
cam = bpy.data.objects.new('Camera', bpy.data.cameras.new('Camera'))
cam.data.lens = 35
scene.collection.objects.link(cam)
scene.camera = cam
views = [(math.radians(a), math.radians(15)) for a in range(0, 360, 45)] + [(0.3, math.radians(80)), (0.3, math.radians(-75))]
meshes = [o for o in bpy.data.objects if o.type == 'MESH']


def drawn(o, name):
    keep = (o.name.endswith('_lod0') or '_lod' not in o.name) and 'hull' not in o.name and 'Blur' not in o.name and 'Glass' not in o.name
    return keep and (name is None or o.name.removesuffix('_lod0') == name)


def sheet(name, path):
    shown = [o for o in meshes if drawn(o, name)]
    for o in meshes:
        o.hide_render = o not in shown
    points = [o.matrix_world @ v.co for o in shown for v in o.data.vertices]
    lo = Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
    hi = Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
    centre, size = (lo + hi) / 2, max((hi - lo).length, 0.2)
    tiles = np.zeros((2 * H, 5 * W, 4), dtype=np.float32)
    for i, (yaw, pitch) in enumerate(views):
        d = Vector((math.cos(yaw) * math.cos(pitch), math.sin(yaw) * math.cos(pitch), math.sin(pitch)))
        cam.location = centre + d * size * 0.75
        cam.rotation_euler = (centre - cam.location).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = f'{path}_view.png'
        bpy.ops.render.render(write_still=True)
        image = bpy.data.images.load(scene.render.filepath)
        pixels = np.array(image.pixels[:], dtype=np.float32).reshape(H, W, 4)
        bpy.data.images.remove(image)
        row, column = 1 - i // 5, i % 5
        tiles[row * H:(row + 1) * H, column * W:(column + 1) * W] = pixels
    result = bpy.data.images.new('Sheet', 5 * W, 2 * H)
    result.pixels = tiles.ravel()
    result.filepath_raw = f'{path}.png'
    result.file_format = 'PNG'
    result.save()
    bpy.data.images.remove(result)
    print('[holes]', name or 'all', len(shown), 'meshes →', f'{path}.png', flush=True)


if names:
    for name in names:
        sheet(name, f'{out}_{name}')
else:
    sheet(None, f'{out}_all')
