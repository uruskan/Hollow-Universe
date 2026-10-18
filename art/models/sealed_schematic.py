"""Original complete blue service drawing in a thin sealed document sleeve.

The laminate is an opaque PBR surface: no transparent overlap or sorting cost.
All drafting marks, surface noise and geometry are authored here, not traced.
"""
from pathlib import Path
import json, math, sys
import bpy, bmesh
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from hollow import ITEMS, TEXTURES, REPO
from hollow.export import collection, export_glb, convex_hull, select_only
from hollow.godot import write_material
from hollow.textures import write_png, write_texture_import, normal_from_height
from hollow.diagram_texture import Diagram

OUT = REPO / '.artifacts/sealed-schematic-r01'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
col = collection('SealedSchematic')
n = 1024
v, u = np.mgrid[0:n, 0:n].astype(np.float32) / n
rng = np.random.default_rng(164271)
noise = rng.uniform(-1, 1, (n, n)).astype(np.float32)
albedo = np.ones((n, n, 4), np.float32)
orm = np.ones_like(albedo)
shade = .98 + noise * .025 + .025 * np.sin(u * 49 + v * 17)
albedo[:, :, :3] = np.array([.105, .255, .43]) * shade[:, :, None]
orm[:, :, 1] = .57
orm[:, :, 2] = 0
height = noise * .0015
for left in (0, 512):
    tile = albedo[:, left:left + 512]
    d = Diagram(tile)
    vv, uu = np.mgrid[0:n, 0:512].astype(np.float32)
    uu /= 511; vv /= 1023
    edge = np.minimum.reduce([uu, 1 - uu, vv, 1 - vv])
    # Heat-sealed rim around an intact blue sheet, with a broad top closure.
    rim = (edge < .026) | (vv < .047)
    tile[rim, :3] = np.array([.28, .39, .47]) * (1 + noise[:, left:left + 512][rim, None] * .06)
    orm[:, left:left + 512, 1][rim] = .38
    height[:, left:left + 512][rim] += .008
    for x in np.arange(.085, .94, .032):
        d.line((x, .085), (x, .925), (.26, .44, .60), .00065, .48)
    for y in np.arange(.085, .94, .026):
        d.line((.075, y), (.925, y), (.26, .44, .60), .00065, .48)
    d.box(.055, .068, .89, .88, color=(.41, .59, .71), width=.0016)
    d.box(.018, .020, .964, .961, color=(.48, .61, .68), width=.0017)
    d.line((.025, .045), (.975, .045), (.10, .22, .32), .002)
    if left == 0:
        # Invented technical illustration: a finned service cassette and inset view.
        c = (.66, .81, .87)
        d.path([(.22,.25),(.65,.25),(.78,.34),(.78,.57),(.65,.65),(.22,.65),(.15,.57),(.15,.34)], c, .0024, True)
        d.box(.25,.31,.37,.28,color=c,width=.0018)
        for y in np.arange(.34,.58,.035):
            d.line((.27,y),(.60,y),c,.0014)
        for x in (.19,.68):
            for y in (.34,.56): d.circle(x,y,.010,.008,color=c,width=.0017)
        d.box(.36,.20,.14,.05,color=c,width=.0016)
        for x in (.38,.42,.46): d.line((x,.20),(x,.16),c,.0014)
        d.dimension((.15,.70),(.78,.70),color=c,width=.0012)
        d.dimension((.11,.25),(.11,.65),color=c,width=.0012)
        d.path([(.71,.40),(.83,.27),(.90,.27)],c,.0012)
        d.circle(.76,.14,.081,.055,color=c,width=.0015)
        d.circle(.76,.14,.044,.030,color=c,width=.0012)
        d.line((.66,.14),(.86,.14),c,.001)
        d.line((.76,.07),(.76,.21),c,.001)
        d.box(.11,.77,.38,.13,color=c,width=.0012)
        for row in range(5): d.line((.13,.79+row*.022),(.46-row*.025,.79+row*.022),c,.001,.72)
        d.box(.57,.78,.32,.12,color=c,width=.0012)
        d.line((.57,.82),(.89,.82),c,.0012)
        d.line((.68,.78),(.68,.90),c,.0012)
        # Three small registration bars make the complete sheet identifiable.
        for x in (.15,.18,.21):d.box(x,.113,.012,.042,color=c,width=.0022)
    else:
        d.box(.12,.14,.37,.13,color=(.35,.51,.64),width=.0014)
        for row in range(5):d.line((.14,.16+row*.019),(.45-row*.018,.16+row*.019),(.35,.51,.64),.001)
        d.box(.66,.80,.20,.07,color=(.35,.51,.64),width=.0014)
    # Subtle lamination scuffs and shallow fold rub, never a painted highlight.
    crease = np.exp(-((vv-.53)/.003)**2)*.025 - np.exp(-((vv-.536)/.004)**2)*.012
    tile[:, :, :3] += crease[:, :, None]
    height[:, left:left+512] += crease * .02
    for i in range(18):
        x,y = rng.uniform(.06,.93), rng.uniform(.08,.92)
        d.line((x,y),(min(.94,x+.03),y+.009),(.39,.53,.64),.0006,.18)

paths = {}
for key, pixels in [('albedo',np.clip(albedo,0,1)),('orm',orm),('normal',normal_from_height(height,1.2))]:
    paths[key] = write_png(TEXTURES/'sealed-schematic'/f'sealed_schematic_{key}.png',pixels)
    write_texture_import(paths[key],normal_map=key=='normal')
mp = write_material('sealed-schematic','M_Hollow_SealedSchematic',albedo=paths['albedo'],orm=paths['orm'],normal=paths['normal'],normal_scale=.25,vertex_colour=False)
mat = bpy.data.materials.new('M_Hollow_SealedSchematic')
mat.diffuse_color = (.105,.255,.43,1)
objects = []
for lod, (nx,nz) in enumerate([(20,28),(10,14),(5,7)]):
    verts, faces = [], []
    for iz in range(nz+1):
        t = iz/nz
        for ix in range(nx+1):
            s = ix/nx
            x = (s-.5)*.24
            z = t*.30
            # Soft irregular sheet edges, intact corners, smooth asymmetric curl.
            x += (abs(s-.5)*2)**8*.0009*math.sin(t*19)
            z += (abs(t-.5)*2)**8*.0011*math.sin(s*13)
            y = .004*math.sin(s*4+t*2)+.009*(2*s-1)**4+.004*(2*t-1)**4
            y += .0015*math.tanh((t-.47)*35)
            verts.append((x,y,z))
    for iz in range(nz):
        for ix in range(nx):
            a = iz*(nx+1)+ix
            faces.append((a,a+1,a+nx+2,a+nx+1))
    me = bpy.data.meshes.new(f'SealedSchematic_lod{lod}')
    me.from_pydata(verts,[],faces);me.update()
    obj = bpy.data.objects.new(me.name,me);col.objects.link(obj);me.materials.append(mat)
    select_only([obj])
    mod = obj.modifiers.new('SleeveThickness','SOLIDIFY');mod.thickness=.0012;mod.offset=0
    bpy.ops.object.modifier_apply(modifier=mod.name)
    uv = obj.data.uv_layers.new(name='UVMap')
    for face in obj.data.polygons:
        face.use_smooth = abs(face.normal.y)>.7
        for loop in face.loop_indices:
            p = obj.data.vertices[obj.data.loops[loop].vertex_index].co
            uu = min(1,max(0,(p.x+.12)/.24))
            vv = min(1,max(0,p.z/.30))
            uv.data[loop].uv = (.005+uu*.49+(.5 if face.normal.y>0 else 0),.005+vv*.99)
    objects.append(obj)
objects.append(convex_hull([objects[0]],'SealedSchematic_hull0-convcolonly',col,max_faces=32))
# Bottom-centre pivot shared across all LODs and collision.
bottom = min(v.co.z for v in objects[0].data.vertices)
for obj in objects:
    for vertex in obj.data.vertices: vertex.co.z -= bottom
vertices = [v.co for v in objects[0].data.vertices]
report = {'asset':'sealed-schematic','dimensions_blender_xyz_m':[max(v[i] for v in vertices)-min(v[i] for v in vertices) for i in range(3)],'texture_size':n,'material_count':1,'lod_triangles':{o.name:sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects[:3]},'hull_triangles':sum(len(p.vertices)-2 for p in objects[-1].data.polygons)}
export_glb(ITEMS/'sealed-schematic.glb',objects,{'M_Hollow_SealedSchematic':mp})
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'sealed-schematic-source.blend'))
(OUT/'geometry.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report),flush=True)
