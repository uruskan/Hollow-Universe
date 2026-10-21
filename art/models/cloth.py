"""Original folded plant-fibre cloth. Bounded geometric authoring; no cloth simulation.

blender -b --factory-startup -t 2 --python art/models/cloth.py
Writes only cloth assets. Editable source and measured report: .artifacts/cloth-r01/.
"""
from pathlib import Path
import json
import math
import sys
import bpy
import bmesh
import numpy as np
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from hollow import ITEMS, TEXTURES, REPO
from hollow.export import collection, export_glb, decimated, convex_hull, select_only
from hollow.godot import write_material
from hollow.textures import write_png, write_texture_import, normal_from_height

OUT=REPO/'.artifacts/cloth-r01'
OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system='METRIC'
bpy.context.scene.render.threads_mode='FIXED'
bpy.context.scene.render.threads=2
col=collection('Cloth')

# Original tileable plain weave: broad yarn crossings, fine irregular fibres,
# neutral ambient occlusion, and non-metallic roughness. 4 cm physical repeat.
n=512
y,x=np.mgrid[0:n,0:n].astype(np.float32)/n
warp=np.sin(x*math.tau*24)
weft=np.sin(y*math.tau*24)
cross=np.cos(x*math.tau*12)*np.cos(y*math.tau*12)
height=.42+.11*np.maximum(warp,weft)+.025*cross
fibres=.018*np.sin(x*math.tau*173+y*math.tau*7)+.012*np.cos(y*math.tau*151-x*math.tau*3)
height+=fibres
slubs=.035*np.sin(x*math.tau*3)*np.sin(y*math.tau*5)+.025*np.cos(x*math.tau*7+y*math.tau*2)
shade=.82+.065*(height-.3)+slubs+.025*np.sin(x*math.tau*2+y*math.tau*3)
albedo=np.ones((n,n,4),np.float32)
albedo[:,:,:3]=shade[:,:,None]*np.array([.65,.57,.43],np.float32)
orm=np.ones_like(albedo);orm[:,:,1]=np.clip(.92-.12*height,0,1);orm[:,:,2]=0
texture_dir=TEXTURES/'cloth'
paths={}
for key,pixels in [('albedo',albedo),('orm',orm),('normal',normal_from_height(height,1.0))]:
    paths[key]=write_png(texture_dir/f'cloth_{key}.png',pixels)
    write_texture_import(paths[key],normal_map=key=='normal')
material_path=write_material('cloth','M_Hollow_Cloth',albedo=paths['albedo'],orm=paths['orm'],
    normal=paths['normal'],normal_scale=.65,vertex_colour=True,vertex_colour_srgb=False)
mat=bpy.data.materials.new('M_Hollow_Cloth');mat.diffuse_color=(.58,.49,.35,1)
mat.use_nodes=True
principled=mat.node_tree.nodes.get('Principled BSDF')
principled.inputs['Roughness'].default_value=.88
for key in ('albedo','orm','normal'):
    node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=bpy.data.images.load(str(paths[key]))
    if key!='albedo':node.image.colorspace_settings.name='Non-Color'
    if key=='albedo':mat.node_tree.links.new(node.outputs['Color'],principled.inputs['Base Color'])
    elif key=='normal':
        norm=mat.node_tree.nodes.new('ShaderNodeNormalMap');norm.inputs['Strength'].default_value=.65
        mat.node_tree.links.new(node.outputs['Color'],norm.inputs['Color']);mat.node_tree.links.new(norm.outputs['Normal'],principled.inputs['Normal'])
    else:
        sep=mat.node_tree.nodes.new('ShaderNodeSeparateColor');mat.node_tree.links.new(node.outputs['Color'],sep.inputs[0])
        mat.node_tree.links.new(sep.outputs[1],principled.inputs['Roughness'])

# One loose folded rag, with broad draping wrinkles and an irregular exposed end.
# Rust's public cloth icon is a visual reference; no source pixels or geometry are used.
def bezier(a,b,c,d,t):
    return (1-t)**3*Vector(a)+3*(1-t)**2*t*Vector(b)+3*(1-t)*t*t*Vector(c)+t**3*Vector(d)
segments=[
    ((-.145,.012),(-.06,.012),(.06,.015),(.115,.017),22),
    ((.115,.017),(.145,.018),(.132,.055),(.108,.057),12),
    ((.108,.057),(.04,.052),(-.045,.023),(-.098,.031),22),
]
path=[]
for si,(a,b,c,d,count) in enumerate(segments):
    for j in range(count):
        p=bezier(a,b,c,d,j/count)
        path.append((p.x,p.y,si,j/count))
p=segments[-1][3];path.append((p[0],p[1],len(segments)-1,1.0))
nv=len(path);nu=25
verts=[];uvs=[];colors=[]
lengths=[0.0]
for a,b in zip(path,path[1:]):lengths.append(lengths[-1]+math.hypot(a[0]-b[0],a[1]-b[1]))
for v,(py,pz,seg,t) in enumerate(path):
    for u in range(nu):
        s=u/(nu-1);px=(s-.5)*(.285+.018*math.sin(v*.08))
        edge=.005*math.sin(v*.48+s*2.1)+.002*math.cos(v*1.71+s*5)
        px+=edge*(abs(s-.5)*2)**6
        # Soft wrinkles spread across the width and continue around the folds.
        amplitude=.007
        wrinkle=amplitude*(math.sin(s*math.tau*2.3+py*14)+.38*math.sin(s*math.tau*4.1-py*21))
        # Last panel has a diagonal edge, a relaxed lifted corner and a sagging crease.
        py_local=py
        if seg==2:
            py_local+=t*t*(.067*s-.027+.008*math.sin(s*17)+.004*math.sin(s*41))
            pz_local=pz+.022*(s**5)*(t**3)+.007*math.sin(s*6+t*4)*t
        elif seg==0:
            py_local+=(1-t)**3*(.016*math.sin(s*17)+.007*math.sin(s*43))
            pz_local=pz
        else:pz_local=pz
        verts.append((px,py_local,pz_local+wrinkle))
        uvs.append((px/.04,lengths[v]/.04))
        # Narrow woven selvedges, not separate floating straps or painted shadows.
        selvedge=.83 if s<.05 or s>.95 else 1.0
        wear=.83+.10*math.sin(s*11+v*.09)*math.cos(s*5-v*.17)
        tint=selvedge*wear
        colors.append((tint,tint*.98,tint*.94,1))
faces=[]
for v in range(nv-1):
    for u in range(nu-1):
        # Two short irregular edge notches on the exposed flap, fully modeled.
        if v>=nv-3 and u in (5,6,17):continue
        k=v*nu+u;faces.append((k,k+1,k+nu+1,k+nu))
mesh=bpy.data.meshes.new('FoldedCloth');mesh.from_pydata(verts,[],faces);mesh.update()
obj=bpy.data.objects.new('Cloth_lod0',mesh);col.objects.link(obj);obj.data.materials.append(mat)
uv=mesh.uv_layers.new(name='UVMap')
for poly in mesh.polygons:
    poly.use_smooth=True
    for li in poly.loop_indices:uv.data[li].uv=uvs[mesh.loops[li].vertex_index]
color=mesh.color_attributes.new(name='Col',type='FLOAT_COLOR',domain='POINT')
for i,c in enumerate(colors):color.data[i].color=c
mesh.color_attributes.active_color=color
bm=bmesh.new();bm.from_mesh(mesh)
bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
bm.to_mesh(mesh);bm.free()
# Simplify the open sheet first, then give every LOD its thickness. Decimating an
# already closed thin shell can fold the underside through the visible surface.
lod1=decimated(obj,'Cloth_lod1',.38,col)
lod2=decimated(obj,'Cloth_lod2',.13,col)
for sheet in (obj,lod1,lod2):
    select_only([sheet])
    solid=sheet.modifiers.new('Woven fabric thickness','SOLIDIFY');solid.thickness=.0015;solid.offset=0
    bpy.ops.object.modifier_apply(modifier=solid.name)

# Short irregular edge fibres only at the exposed top end, integrated into the
# same material/mesh. Tubes avoid alpha cards and remain sparse at slot size.
parts=[obj]
for i in range(19):
    s=(i+.3)/19
    if round(s*(nu-1))==6:continue
    idx=(nv-1)*nu+round(s*(nu-1));start=Vector(verts[idx])
    end=start+Vector((.0013*math.sin(i*1.7),-.003-.002*(.5+.5*math.sin(i*2.1)),.0006*math.cos(i)))
    delta=end-start
    bpy.ops.mesh.primitive_cone_add(vertices=4,radius1=.00022,radius2=.00009,depth=delta.length,location=(start+end)/2)
    o=bpy.context.object;o.rotation_euler=delta.to_track_quat('Z','Y').to_euler();o.data.materials.append(mat)
    ca=o.data.color_attributes.new(name='Col',type='FLOAT_COLOR',domain='POINT')
    for value in ca.data:value.color=(.92,.92,.92,1)
    o.data.color_attributes.active_color=ca
    parts.append(o)
select_only(parts);bpy.context.view_layer.objects.active=obj;bpy.ops.object.join()
obj.name='Cloth_lod0'
bm=bmesh.new();bm.from_mesh(obj.data);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(obj.data);bm.free()
# Bottom-centre footprint pivot, shared by all detail levels.
lo=Vector(tuple(min(v.co[i] for v in obj.data.vertices) for i in range(3)))
hi=Vector(tuple(max(v.co[i] for v in obj.data.vertices) for i in range(3)))
offset=Vector(((lo.x+hi.x)/2,(lo.y+hi.y)/2,lo.z))
for sheet in (obj,lod1,lod2):
    for v in sheet.data.vertices:v.co-=offset
hull=convex_hull([obj],'Cloth_hull0-convcolonly',col,max_faces=48)
objects=[obj,lod1,lod2,hull]
report={'asset':'cloth','origin':'Original authored geometry and procedural weave; no external assets',
    'dimensions_blender_xyz_m':list(hi-lo),'texture_size':512,'texture_repeat_m':.04,
    'fabric_thickness_m':.0015,'lod_triangles':{},'hull_triangles':sum(len(p.vertices)-2 for p in hull.data.polygons)}
for o in objects[:3]:
    report['lod_triangles'][o.name]=sum(len(p.vertices)-2 for p in o.data.polygons)
    assert all(math.isfinite(c) for v in o.data.vertices for c in v.co)
    assert len(o.data.vertices)>0
export_glb(ITEMS/'cloth.glb',objects,{'M_Hollow_Cloth':material_path})
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'cloth-source.blend'))
(OUT/'geometry.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report),flush=True)
