"""Configure only authored tree catalog dependencies after their first Godot import.

python environment_tree_import_settings.py PATH_TO_GAME
Run Godot --headless --import once more afterward. No implicit editor launch.
"""
import json, re, sys
from pathlib import Path

game=Path(sys.argv[1]).resolve()
catalog=json.loads((game/'assets/surface/trees/catalog.json').read_text())
def set_params(path,values):
    text=path.read_text(encoding='utf8')
    for key,value in values.items():
        pattern=re.compile(r'^'+re.escape(key)+r'=.*$',re.M)
        if not pattern.search(text):raise RuntimeError(f'{path}: missing {key}')
        text=pattern.sub(key+'='+value,text)
    path.write_text(text,encoding='utf8')

images=set();normals=set()
for row in catalog['species']:
    model=game/row['model'][6:]
    data=json.loads(model.read_text())
    images.update(model.parent/r['uri'] for r in data.get('images',[]))
    for material in data.get('materials',[]):
        if 'normalTexture' in material:
            i=data['textures'][material['normalTexture']['index']]['source']
            normals.add(model.parent/data['images'][i]['uri'])
    set_params(Path(str(model)+'.import'),{'meshes/generate_lods':'false','meshes/light_baking':'0'})
for path in images:
    set_params(Path(str(path)+'.import'),{'compress/mode':'2','mipmaps/generate':'true',
        'detect_3d/compress_to':'0','compress/normal_map':'1' if path in normals else '2'})
print('Tree imports:',len(catalog['species']),'models;',len(images),'shared GPU-compressed maps;',len(normals),'normal maps')
