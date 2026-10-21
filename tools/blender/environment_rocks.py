"""Owned rock scans -> instanced Godot meshes. Run with Blender --background --python.

Uses the material-channel and transform preservation approach from RustMVP's
tools/planet/rust_reference_preview/prepare_local_cliffs.py. No Rust map/art is read.
Original library bytes are read only. No providers, downloads or bundled scripts run.
"""
import bpy
import hashlib
import json
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path('D:/GameDev/Assets')
DEST = ROOT / 'game/assets/surface/rocks'


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def reduce(source, budget):
    obj = source.copy()
    obj.data = source.data.copy()
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    obj.data.calc_loop_triangles()
    if len(obj.data.loop_triangles) > budget:
        mod = obj.modifiers.new('Triangle budget', 'DECIMATE')
        mod.ratio = budget / len(obj.data.loop_triangles)
        bpy.ops.object.modifier_apply(modifier=mod.name)
    obj.data.calc_loop_triangles()
    return obj


def bounds(obj):
    return (Vector([min(v.co[a] for v in obj.data.vertices) for a in range(3)]),
            Vector([max(v.co[a] for v in obj.data.vertices) for a in range(3)]))


def bake(obj):
    obj.data.transform(obj.matrix_world)
    obj.parent = None
    obj.matrix_world = Matrix.Identity(4)
    obj.data.update()


def main():
    DEST.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    inputs = [('rock_moss_set_02', SOURCE/'rock_moss_set_02_4k.gltf/rock_moss_set_02_4k.gltf', 'CC0-1.0'),
              ('tundra_boulder', SOURCE/'tundra_boulder_vixsch3dw_high.glb', 'Fab Standard License'),
              ('tundra_lichened', SOURCE/'tundra_lichened_boulder_vivvbfhaw_high.glb', 'Fab Standard License')]
    receipt = {'method': 'uniform normalization; source UVs, tangent normals and roughness retained',
               'collision': 'existing conservative boulder sphere retained; not a triangle-exact collider',
               'sources': [], 'variants': []}
    output = []
    for family, path, license_name in inputs:
        previous = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=str(path))
        sources = sorted([o for o in bpy.data.objects if o not in previous and o.type == 'MESH'], key=lambda o: o.name)
        for material in {slot.material for obj in sources for slot in obj.material_slots if slot.material}:
            for node in material.node_tree.nodes:
                if node.type == 'TEX_IMAGE' and node.image and not node.image.name.startswith(family + '_'):
                    node.image.name = family + '_' + node.image.name
        dependencies = [path]
        if path.suffix == '.gltf':
            data = json.loads(path.read_text())
            dependencies += [path.parent/row['uri'] for key in ('buffers', 'images') for row in data.get(key, []) if 'uri' in row]
        receipt['sources'].append({'family': family, 'license': license_name,
            'files': [{'path': str(p), 'sha256': digest(p)} for p in dependencies]})
        for number, source in enumerate(sources):
            bake(source)
            lo, hi = bounds(source)
            centre = Vector(((lo.x+hi.x)/2, (lo.y+hi.y)/2, lo.z))
            radius = max(Vector((v.co.x-centre.x, v.co.y-centre.y)).length for v in source.data.vertices)
            # Preserve aspect ratio. Fit the existing gameplay envelope (0.65 m exposed,
            # 0.95 m radius) instead of stretching every scan into a flattened ellipsoid.
            scale = min(.92/radius, .77/(hi.z-lo.z))
            for vertex in source.data.vertices:
                vertex.co = (vertex.co-centre)*scale
            source.data.update()
            key = f'{family}_{number:02d}'
            levels = []
            for lod, budget in enumerate((6500, 900)):
                obj = reduce(source, budget)
                obj.name = key + '_lod' + str(lod)
                output.append(obj)
                lo2, hi2 = bounds(obj)
                levels.append({'node': obj.name, 'triangles': len(obj.data.loop_triangles),
                               'height': hi2.z, 'extent': max(abs(lo2.x), abs(lo2.y), abs(hi2.x), abs(hi2.y))})
            receipt['variants'].append({'key': key, 'source_mesh': source.name, 'uniform_scale': scale, 'levels': levels})
            bpy.data.objects.remove(source, do_unlink=True)
    # One shared 4K atlas for seven moss rocks, plus each boulder's supplied PBR maps.
    # Both LODs share the same textures in one GLB; no per-variant material duplication.
    for img in bpy.data.images:
        if max(img.size) > 4096:
            ratio = 4096/max(img.size)
            img.scale(round(img.size[0]*ratio), round(img.size[1]*ratio))
    bpy.ops.object.select_all(action='DESELECT')
    for obj in output: obj.select_set(True)
    target = DEST/'authored_rocks.gltf'
    bpy.ops.export_scene.gltf(filepath=str(target), export_format='GLTF_SEPARATE', use_selection=True,
        export_yup=True, export_image_format='AUTO', export_materials='EXPORT', export_extras=False)
    receipt['model'] = 'res://assets/surface/rocks/authored_rocks.gltf'
    receipt['sha256'] = digest(target)
    exported = json.loads(target.read_text())
    dependencies = [target] + [DEST/row['uri'] for field in ('buffers', 'images') for row in exported.get(field, []) if 'uri' in row]
    receipt['outputs'] = [{'file': p.relative_to(ROOT).as_posix(), 'sha256': digest(p)} for p in dependencies]
    (DEST/'catalog.json').write_text(json.dumps(receipt, indent=2), encoding='utf8')
    print('ROCKS_READY', len(receipt['variants']), target, flush=True)


if __name__ == '__main__':
    main()
