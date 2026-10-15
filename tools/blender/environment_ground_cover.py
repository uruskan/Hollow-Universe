"""Normalize owner-supplied Fab vegetation into metre-scale meshes and bounded authored LODs.

Run in background Blender. Sources and original 4K images are never modified. No network/provider calls.
"""
import bpy
import argparse
import hashlib
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path('D:/GameDev/Assets')
parser = argparse.ArgumentParser()
parser.add_argument('--output', type=Path, default=ROOT / 'game/assets/surface/ground-cover')
options = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
DEST = options.output
FAMILIES = [
    ('wild', 'wild_grass_vlkhcbxia_ue_high', 'vlkhcbxia', 'BCA', [1, 2, 3]),
    ('cotton', 'cotton_grass_wlykecdia_ue_high', 'wlykecdia', 'BCF', [1, 2, 3]),
    ('tundra', 'tundra_grass_wk2oaeyqx_ue_high', 'wk2oaeyqx', 'ABC', [1, 3, 4]),
    ('fern', 'bracken_fern_seeow_ue_high', 'seEoW', 'ACE', [0, 0, 1]),
]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    DEST.mkdir(parents=True, exist_ok=True)
    receipt = {'source': 'Owner-supplied Quixel/Fab plant archives; originals retained outside the game',
               'textureResolution': 2048, 'lodDistances': [8, 22, 42],
               'farDistanceNote': '42 metres multiplied by the selected graphics grass distance', 'families': []}
    for family, folder, identifier, letters, lods in FAMILIES:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        source = SOURCE / folder
        gltf = next((source / 'standard').glob('*.gltf'))
        extraction = json.loads((source / '_EXTRACTION_PROVENANCE.json').read_text())
        checked = []
        for item in extraction['files']:
            path = source / item['path']
            if path.suffix.lower() in ('.gltf', '.bin', '.png'):
                assert sha(path) == item['sha256'], str(path)
                checked.append({'path': str(path), 'sha256': item['sha256']})
        bpy.ops.import_scene.gltf(filepath=str(gltf))
        imported = {o.name: o for o in bpy.context.scene.objects if o.type == 'MESH'}
        selected = []
        variants = []
        for vi, letter in enumerate(letters):
            levels = []
            for band, source_lod in enumerate(lods):
                name = f'SM_{identifier}_Var{letter}' + (f'_LOD{source_lod}' if source_lod else '')
                original = imported[name]
                obj = original.copy()
                obj.data = original.data.copy()
                bpy.context.collection.objects.link(obj)
                obj.name = f'{family}_v{vi}_lod{band}'
                bpy.ops.object.select_all(action='DESELECT')
                obj.select_set(True)
                bpy.context.view_layer.objects.active = obj
                bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
                obj.data.calc_loop_triangles()
                lo = [min(v.co[a] for v in obj.data.vertices) for a in range(3)]
                hi = [max(v.co[a] for v in obj.data.vertices) for a in range(3)]
                # Blender is Z up; GLB export/Godot is Y up. Source centimetre scaling was already applied above.
                levels.append({'node': obj.name, 'sourceNode': name, 'triangles': len(obj.data.loop_triangles),
                               'height': hi[2] - lo[2], 'width': max(hi[0] - lo[0], hi[1] - lo[1]),
                               'billboard': band == 2})
                selected.append(obj)
            variants.append({'variant': vi, 'levels': levels})
        for image in bpy.data.images:
            if image.source == 'FILE' and image.size[0] > 0:
                image.scale(min(2048, image.size[0]), min(2048, image.size[1]))
                image.pack()
        bpy.ops.object.select_all(action='DESELECT')
        for obj in selected:
            obj.select_set(True)
        output = DEST / f'{family}.glb'
        bpy.ops.export_scene.gltf(filepath=str(output), export_format='GLB', use_selection=True,
                                  export_materials='EXPORT', export_image_format='AUTO', export_cameras=False,
                                  export_lights=False, export_extras=False, export_yup=True)
        record = {'key': family, 'model': f'res://assets/surface/ground-cover/{family}.glb',
                  'sourceArchive': extraction['source_archive'], 'archiveSha256': extraction['source']['sha256'],
                  'inputs': checked, 'variants': variants, 'outputSha256': sha(output), 'outputBytes': output.stat().st_size}
        receipt['families'].append(record)
        print(f'{family}: {len(selected)} meshes, {output.stat().st_size:,} bytes; '
              f'near {[v["levels"][0]["triangles"] for v in variants]} triangles', flush=True)
    (DEST / 'catalog.json').write_text(json.dumps(receipt, indent=2) + '\n')


main()
