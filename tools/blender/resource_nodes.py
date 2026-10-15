"""Convert only the owned PSG node game meshes, preserving their metres, four LODs and convex hull.

Background Blender only; output is staged under .artifacts/resource-nodes until the coordinated installer runs.
No source files are saved or modified. Baked textures are converted separately by integrate_resource_nodes.py.
"""
import hashlib
import json
import math
from pathlib import Path
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path('D:/GameDev/Assets/PSG_ResourceNodes_R01')
OUTPUT = ROOT / '.artifacts/resource-nodes'
KINDS = {'Stone': 'stone-node', 'Metal': 'metal-node', 'Sulfur': 'sulphur-node', 'HQMetal': 'high-grade-node'}
RINGS = [.12, .30, .48, .66]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    (OUTPUT / 'world').mkdir(exist_ok=True)
    roster = json.loads((SOURCE / 'Reports/release_checksums.json').read_text())
    receipt = {'source': str(SOURCE), 'source_manifest_sha256': digest(SOURCE / 'Reports/release_checksums.json'),
               'method': 'Local Blender library import and glTF export; not a game-dev canonical package.',
               'license': 'Owner-authored work; baked Poly Haven CC0 photographic detail; see THIRD_PARTY_NOTICES.md.',
               'blender': bpy.app.version_string, 'assets': [], 'surfaces': {}}
    for source_kind, key in KINDS.items():
        receipt['surfaces'][key] = []
        for stage in range(1, 4):
            bpy.ops.wm.read_factory_settings(use_empty=True)
            stem = f'PSG_Node_{source_kind}_S{stage:02d}'
            relative = 'Source/' + stem + '.blend'
            source = SOURCE / relative
            assert digest(source) == roster[relative]['sha256'], f'Source changed: {source}'
            report_path = SOURCE / 'Reports' / (stem + '.json')
            report = json.loads(report_path.read_text())
            assert report['mesh_qa_pass'] and not report['hard_errors']
            names = [lod['name'] for lod in report['lods']] + [report['collision']['name']]
            with bpy.data.libraries.load(str(source), link=False) as (available, loaded):
                assert all(name in available.objects for name in names), names
                loaded.objects = names
            objects = list(loaded.objects)
            material_name = f'M_PSG_{source_kind}_{stage}'
            mat = bpy.data.materials.new(material_name)
            mat.use_nodes = True
            for index, obj in enumerate(objects):
                bpy.context.collection.objects.link(obj)
                obj.hide_set(False)
                obj.hide_render = False
                obj.hide_viewport = False
                assert obj.location.length < 1e-6 and all(abs(x - 1) < 1e-6 for x in obj.scale), obj.name
                obj.name = f'{key}_lod{index}' if index < 4 else f'{key}_hull0-convcolonly'
                obj.data.materials.clear()
                obj.data.materials.append(mat)
                # Source colors are geology masks, not final albedo. The complete appearance is in the bake.
                for attr in list(obj.data.color_attributes):
                    obj.data.color_attributes.remove(attr)
                obj.select_set(True)
                obj.data.calc_loop_triangles()
                if index < 4:
                    assert len(obj.data.loop_triangles) == report['lods'][index]['triangles']
            bpy.context.view_layer.objects.active = objects[0]
            bpy.context.view_layer.update()
            low = objects[0]
            tree = BVHTree.FromObject(low, bpy.context.evaluated_depsgraph_get())
            measured = []
            # glTF rotates Blender Z-up to Godot Y-up: (x,y,z) -> (x,z,-y).
            for radius in RINGS:
                ring = []
                for direction in range(32):
                    angle = direction * math.tau / 32
                    hit, _, _, _ = tree.ray_cast(Vector((math.cos(angle) * radius, -math.sin(angle) * radius, 10)), Vector((0, 0, -1)), 20)
                    ring.append(round(hit.z, 3) if hit is not None else None)
                measured.append(ring)
            receipt['surfaces'][key].append(measured)
            target = OUTPUT / 'world' / f'{key}-{stage}.glb'
            bpy.ops.export_scene.gltf(filepath=str(target), export_format='GLB', use_selection=True,
                export_image_format='NONE', export_materials='EXPORT', export_yup=True,
                export_animations=False, export_extras=False, export_texcoords=True,
                export_normals=True, export_tangents=True, export_cameras=False, export_lights=False)
            receipt['assets'].append({'key': key, 'source_kind': source_kind, 'stage': stage,
                'source': relative, 'source_sha256': digest(source), 'report_sha256': digest(report_path),
                'triangles': [len(obj.data.loop_triangles) for obj in objects[:4]],
                'dimensions_m': [round(float(low.dimensions.x), 6), round(float(low.dimensions.z), 6), round(float(low.dimensions.y), 6)],
                'material': material_name, 'glb': str(target.relative_to(OUTPUT)), 'glb_sha256': digest(target),
                'textures': report['textures']})
            print('NODE_READY', key, stage, receipt['assets'][-1]['triangles'], flush=True)
    (OUTPUT / 'conversion.json').write_text(json.dumps(receipt, indent=2) + '\n', encoding='utf-8')


if __name__ == '__main__':
    main()
