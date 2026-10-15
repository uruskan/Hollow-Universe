"""Prepare the owner's local Poly Haven scans: bounded LODs, shared collision, and a provenance receipt.

Run only in a background Blender process. Originals are never edited. No network or external code is executed.
"""
import bpy
import hashlib
import json
from pathlib import Path
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path('D:/GameDev/Assets')
DEST = ROOT / 'game/assets/surface/cliffs'
COLLISION = ROOT / 'src/Hollow.Surface/Data/cliffs.json'
SOURCES = [('coastal_cliff_04', 22.0), ('namaqualand_cliff_02', 18.0)]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def reduced(source, triangles):
    obj = source.copy()
    obj.data = source.data.copy()
    bpy.context.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    obj.data.calc_loop_triangles()
    count = len(obj.data.loop_triangles)
    if count > triangles:
        modifier = obj.modifiers.new('Budget', 'DECIMATE')
        modifier.ratio = triangles / count
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.data.calc_loop_triangles()
    return obj


def main():
    DEST.mkdir(parents=True, exist_ok=True)
    COLLISION.parent.mkdir(parents=True, exist_ok=True)
    receipt = {'license': 'CC0-1.0', 'license_url': 'https://polyhaven.com/license', 'assets': []}
    colliders = []
    for key, width in SOURCES:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        path = SOURCE / (key + '_4k.gltf') / (key + '_4k.gltf')
        bpy.ops.import_scene.gltf(filepath=str(path))
        meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
        assert len(meshes) == 1, (key, len(meshes))
        source = meshes[0]
        bpy.context.view_layer.objects.active = source
        source.select_set(True)
        bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
        lo = Vector([min(v.co[a] for v in source.data.vertices) for a in range(3)])
        hi = Vector([max(v.co[a] for v in source.data.vertices) for a in range(3)])
        scale = width / (hi.x - lo.x)
        centre = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
        for vertex in source.data.vertices:
            vertex.co = (vertex.co - centre) * scale
        source.data.update()
        # Keep the source maps' PBR bindings and OpenGL normal convention. 2K maps are enough for these
        # 18–22 m formations; texture arrays or repeated 4K copies are not needed for each instance.
        for img in bpy.data.images:
            if img.size[0] > 2048 or img.size[1] > 2048:
                ratio = 2048 / max(img.size)
                img.scale(round(img.size[0] * ratio), round(img.size[1] * ratio))
        files = []
        for lod, budget in enumerate((12000, 2500)):
            obj = reduced(source, budget)
            obj.name = key
            target = DEST / (key + '_lod' + str(lod) + '.glb')
            bpy.ops.export_scene.gltf(filepath=str(target), export_format='GLB', use_selection=True,
                export_yup=True, export_image_format='AUTO', export_materials='EXPORT', export_extras=False)
            files.append({'file': target.relative_to(ROOT).as_posix(), 'sha256': digest(target),
                          'triangles': len(obj.data.loop_triangles)})
            bpy.data.objects.remove(obj, do_unlink=True)
        collider = reduced(source, 700)
        # Blender Z-up to glTF/Godot Y-up, a proper rotation, so winding stays counter-clockwise.
        positions = [[round(v.co.x, 6), round(v.co.z, 6), round(-v.co.y, 6)] for v in collider.data.vertices]
        indices = [i for tri in collider.data.loop_triangles for i in tri.vertices]
        height = max(p[1] for p in positions)
        colliders.append({'key': key, 'height': height, 'positions': positions, 'indices': indices})
        receipt['assets'].append({'key': key, 'source': str(path), 'source_sha256': digest(path),
            'source_url': 'https://polyhaven.com/a/' + key, 'width': width, 'height': height,
            'collision_triangles': len(indices) // 3, 'outputs': files})
        print('CLIFF_READY', key, files, 'collision', len(indices) // 3, flush=True)
    COLLISION.write_text(json.dumps(colliders, separators=(',', ':')), encoding='utf8')
    receipt['collision_sha256'] = digest(COLLISION)
    (DEST / 'provenance.json').write_text(json.dumps(receipt, indent=2), encoding='utf8')


if __name__ == '__main__':
    main()
