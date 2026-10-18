"""Original torn blue drafting-paper component; no copied game images or meshes."""
from pathlib import Path
import json,math,sys
import bpy,bmesh
import numpy as np
from mathutils import Vector
from mathutils.geometry import tessellate_polygon

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from hollow import ITEMS,TEXTURES,REPO
from hollow.export import collection,export_glb,convex_hull,select_only
from hollow.godot import write_material
from hollow.textures import write_png,write_texture_import,normal_from_height
from hollow.diagram_texture import Diagram

OUT=REPO/'.artifacts/torn-schematic-r01';OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True);bpy.context.scene.unit_settings.system='METRIC'
col=collection('TornSchematic');n=1024
v,u=np.mgrid[0:n,0:n].astype(np.float32)/n
rng=np.random.default_rng(163271);noise=rng.uniform(-1,1,(n,n)).astype(np.float32)
albedo=np.ones((n,n,4),np.float32);orm=np.ones_like(albedo)
shade=.94+noise*.045+.04*np.sin(u*37+v*23)+.025*np.sin(u*91-v*61)
albedo[:,:,:3]=np.array([.105,.255,.43])*shade[:,:,None]
orm[:,:,1]=.92;orm[:,:,2]=0
height=noise*.006
for left in (0,512):
    tile=albedo[:,left:left+512];d=Diagram(tile)
    for x in np.arange(.10,.95,.036):d.line((x,.08),(x,.94),(.27,.46,.62),.0007,.43)
    for y in np.arange(.08,.95,.030):d.line((.10,y),(.93,y),(.27,.46,.62),.0007,.43)
    d.box(.085,.065,.85,.88,color=(.40,.61,.75),width=.002)
    if left==0:d.schematic()
    else:
        d.box(.13,.13,.25,.07,color=(.34,.51,.64),width=.0015)
        for i in range(5):d.line((.14,.24+i*.022),(.44-i*.025,.24+i*.022),(.30,.47,.60),.001)
# Crease rubs, fibre noise and aged edges, distinct from white drafting lines.
fold=np.exp(-((v-.40)/.003)**2)*.075-np.exp(-((v-.405)/.004)**2)*.045
albedo[:,:,:3]+=fold[:,:,None];height+=fold*.15
paths={}
for key,pixels in [('albedo',np.clip(albedo,0,1)),('orm',orm),('normal',normal_from_height(height,1.4))]:
    paths[key]=write_png(TEXTURES/'torn-schematic'/f'torn_schematic_{key}.png',pixels)
    write_texture_import(paths[key],normal_map=key=='normal')
mp=write_material('torn-schematic','M_Hollow_TornSchematic',albedo=paths['albedo'],orm=paths['orm'],normal=paths['normal'],normal_scale=.4,vertex_colour=False)
mat=bpy.data.materials.new('M_Hollow_TornSchematic');mat.diffuse_color=(.1,.25,.43,1)
outline=[(-.117,.005),(-.104,.009),(-.097,.002),(-.077,.017),(-.067,.014),(-.069,.034),(-.058,.028),(-.050,.045),(-.029,.047),(-.023,.065),(-.032,.078),(-.011,.071),(.008,.099),(.013,.089),(.026,.102),(.024,.125),(.044,.117),(.054,.145),(.064,.139),(.078,.170),(.094,.162),(.090,.190),(.105,.185),(.109,.214),(.118,.226),(.116,.270),(.036,.273),(.025,.257),(.022,.278),(-.117,.280),(-.118,.226),(-.107,.223),(-.117,.210),(-.109,.175),(-.099,.167),(-.117,.157)]
objects=[]
for lod,cuts in [(0,5),(1,2),(2,0)]:
    points=[Vector((x,0,z)) for x,z in outline]
    triangles=tessellate_polygon([points]);lookup={tuple(p):i for i,p in enumerate(points)}
    me=bpy.data.meshes.new(f'TornSchematic_lod{lod}')
    # Blender 5.2 returns index triples; older supported releases return vectors.
    faces=[[p if isinstance(p,int) else lookup[tuple(p)] for p in t] for t in triangles]
    me.from_pydata(points,[],faces);me.update()
    bm=bmesh.new();bm.from_mesh(me)
    if cuts:bmesh.ops.subdivide_edges(bm,edges=list(bm.edges),cuts=cuts,use_grid_fill=True)
    for vertex in bm.verts:
        x,z=vertex.co.x,vertex.co.z
        vertex.co.y=.0025*math.sin(x*29+z*13)+.008*(x/.12)**4+.002*abs(z-.168)/.15
    bmesh.ops.recalc_face_normals(bm,faces=bm.faces)
    if sum(f.normal.y for f in bm.faces)>0:bmesh.ops.reverse_faces(bm,faces=bm.faces)
    bm.to_mesh(me);bm.free()
    obj=bpy.data.objects.new(me.name,me);col.objects.link(obj);me.materials.append(mat)
    select_only([obj]);mod=obj.modifiers.new('PaperThickness','SOLIDIFY');mod.thickness=.00065;mod.offset=0
    bpy.ops.object.modifier_apply(modifier=mod.name)
    uv=obj.data.uv_layers.new(name='UVMap')
    for face in obj.data.polygons:
        face.use_smooth=True
        for loop in face.loop_indices:
            vertex=obj.data.vertices[obj.data.loops[loop].vertex_index].co
            uu=(vertex.x+.125)/.25;vv=1-vertex.z/.285
            front=face.normal.y<0
            uv.data[loop].uv=(.006+uu*.488+(.5 if not front else 0),1-vv*.98-.01)
    objects.append(obj)
objects.append(convex_hull([objects[0]],'TornSchematic_hull0-convcolonly',col,max_faces=32))
verts=[v.co for v in objects[0].data.vertices]
report={'asset':'torn-schematic','dimensions_blender_xyz_m':[max(v[i] for v in verts)-min(v[i] for v in verts) for i in range(3)],'texture_size':n,'material_count':1,'lod_triangles':{o.name:sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects[:3]},'hull_triangles':sum(len(p.vertices)-2 for p in objects[-1].data.polygons)}
export_glb(ITEMS/'torn-schematic.glb',objects,{'M_Hollow_TornSchematic':mp})
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'torn-schematic-source.blend'))
(OUT/'geometry.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8');print(json.dumps(report),flush=True)
