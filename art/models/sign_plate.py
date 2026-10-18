"""Original overlapping salvaged road signs, inspired by the observed Rust icon.

blender -b --factory-startup -t 2 --python art/models/sign_plate.py
No external game pixels/meshes, baking, simulation or paid service. Lettering uses
the repository's Barlow font (SIL OFL); geometry and wear are authored here.
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
from hollow import ITEMS, TEXTURES, REPO, GAME
from hollow.export import collection, export_glb, convex_hull
from hollow.godot import write_material
from hollow.textures import write_png, write_texture_import, normal_from_height

OUT = REPO / '.artifacts/sign-plate-r01'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
col = collection('SignPlate')


def rectangle(width, height, corner):
    x, z, c = width/2, height/2, corner
    return [(-x+c,-z), (x-c,-z), (x,-z+c), (x,z-c), (x-c,z), (-x+c,z), (-x,z-c), (-x,-z+c)]


panels = [
    dict(name='stop', width=.43, height=.43, corner=.126,
         location=(-.025,.010,.225), angle=-7, bend=.010, hole=(0,-.177),
         atlas=(.015,.015,.470,.470), colour=(.49,.055,.073)),
    dict(name='warning', width=.315, height=.165, corner=.009,
         location=(-.097,-.006,.428), angle=7, bend=.007, hole=(-.133,0),
         atlas=(.015,.515,.470,.270), colour=(.77,.76,.69)),
    dict(name='arrow', width=.240, height=.365, corner=.012,
         location=(.135,-.020,.455), angle=-4, bend=-.012, hole=(0,.155),
         atlas=(.515,.015,.470,.755), colour=(.75,.57,.035)),
]
for panel in panels: panel['outline'] = rectangle(panel['width'],panel['height'],panel['corner'])

n = 1024
albedo = np.ones((n,n,4),np.float32)
albedo[:,:,:3] = [.16,.17,.17]
orm = np.ones_like(albedo); orm[:,:,1] = .7; orm[:,:,2] = .85
heightmap = np.zeros((n,n),np.float32)
font_path = GAME/'assets/fonts/Barlow/BarlowCondensed-Bold.ttf'
font = bpy.data.fonts.load(str(font_path))


def paint_text(pixels, text, box, color):
    """Rasterize our bundled font's triangles into a texture, without a renderer."""
    curve = bpy.data.curves.new('TemporarySignLettering','FONT')
    curve.body = text; curve.font = font; curve.size = 1; curve.resolution_u = 8
    obj = bpy.data.objects.new('TemporarySignLettering',curve)
    bpy.context.scene.collection.objects.link(obj)
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True)
    bpy.context.view_layer.objects.active = obj; bpy.ops.object.convert(target='MESH')
    me = obj.data; me.calc_loop_triangles()
    points = np.array([(v.co.x,v.co.y) for v in me.vertices],np.float32)
    lo, hi = points.min(axis=0), points.max(axis=0)
    x,y,w,h = box; ph,pw = pixels.shape[:2]
    points = (points-lo)/(hi-lo)
    points[:,0] = (x+points[:,0]*w)*pw
    points[:,1] = (y+(1-points[:,1])*h)*ph
    for triangle in me.loop_triangles:
        a,b,c = points[list(triangle.vertices)]
        xmin=max(0,int(min(a[0],b[0],c[0]))); xmax=min(pw-1,int(max(a[0],b[0],c[0]))+1)
        ymin=max(0,int(min(a[1],b[1],c[1]))); ymax=min(ph-1,int(max(a[1],b[1],c[1]))+1)
        if xmax<xmin or ymax<ymin: continue
        yy,xx=np.mgrid[ymin:ymax+1,xmin:xmax+1]; xx=xx+.5; yy=yy+.5
        e0=(b[0]-a[0])*(yy-a[1])-(b[1]-a[1])*(xx-a[0])
        e1=(c[0]-b[0])*(yy-b[1])-(c[1]-b[1])*(xx-b[0])
        e2=(a[0]-c[0])*(yy-c[1])-(a[1]-c[1])*(xx-c[0])
        mask=((e0>=0)&(e1>=0)&(e2>=0))|((e0<=0)&(e1<=0)&(e2<=0))
        pixels[ymin:ymax+1,xmin:xmax+1,:3][mask] = color
    bpy.data.objects.remove(obj,do_unlink=True); bpy.data.meshes.remove(me)


def value_noise(rng, h, w, rows, columns):
    grid=rng.uniform(0,1,(rows,columns))
    horizontal=np.array([np.interp(np.linspace(0,columns-1,w),np.arange(columns),r) for r in grid])
    return np.array([np.interp(np.linspace(0,rows-1,h),np.arange(rows),c) for c in horizontal.T]).T


for number,p in enumerate(panels):
    ax,ay,aw,ah=p['atlas']; left,top,right,bottom=[round(q*n) for q in (ax,ay,ax+aw,ay+ah)]
    tile=albedo[top:bottom,left:right]; th,tw=tile.shape[:2]
    vv,uu=np.mgrid[0:th,0:tw].astype(np.float32)
    uu=(uu+.5)/tw; vv=(vv+.5)/th
    px=(uu-.5)*p['width']; pz=(.5-vv)*p['height']
    distance=np.ones((th,tw),np.float32)
    for i,(x,z) in enumerate(p['outline']):
        bx,bz=p['outline'][(i+1)%len(p['outline'])]; dx,dz=bx-x,bz-z
        t=np.clip(((px-x)*dx+(pz-z)*dz)/(dx*dx+dz*dz),0,1)
        distance=np.minimum(distance,np.hypot(px-x-t*dx,pz-z-t*dz))
    tile[:,:,:3]=p['colour']
    ink=[.025,.029,.027]; ivory=[.80,.79,.73]
    if p['name']=='stop':
        tile[distance<.014,:3]=ivory; tile[distance<.004,:3]=ink
        paint_text(tile,'STOP',(.13,.41,.74,.25),ivory)
    elif p['name']=='warning':
        tile[distance<.006,:3]=ink
        banner=(uu>.035)&(uu<.965)&(vv>.05)&(vv<.35)
        tile[banner,:3]=[.48,.045,.055]
        paint_text(tile,'CAUTION',(.13,.095,.74,.20),ivory)
        paint_text(tile,'KEEP',(.28,.41,.44,.23),ink)
        paint_text(tile,'CLEAR',(.19,.67,.62,.23),ink)
    else:
        tile[distance<.007,:3]=ink
        shaft=(np.abs(uu-.5)<.145)&(vv>.29)&(vv<.83)
        arrowhead=(vv>.14)&(vv<.39)&(np.abs(uu-.5)<(vv-.14)*1.58)
        tile[shaft|arrowhead,:3]=ink
    rng=np.random.default_rng(160713+number)
    noise=rng.uniform(-1,1,(th,tw))
    coarse=value_noise(rng,th,tw,18,21)
    flecks=value_noise(rng,th,tw,94,97)
    hole_distance=np.hypot(px-p['hole'][0],pz-p['hole'][1])
    edge=np.exp(-distance/.009)
    chipped=np.clip((flecks+.23*noise-.48)*3.4,0,1)*np.clip(edge*1.65,0,1)
    # A few isolated chips, then variable-length scrapes. Keep lettering legible.
    for j in range(11):
        sx,sz=rng.uniform(-p['width']*.45,p['width']*.45),rng.uniform(-p['height']*.45,p['height']*.45)
        mark=np.exp(-((px-sx)/rng.uniform(.001,.007))**2-((pz-sz)/rng.uniform(.001,.004))**2)
        chipped=np.maximum(chipped,mark*np.clip((flecks-.3)*2,0,1))
    scratched=np.zeros((th,tw),np.float32)
    for j in range(37):
        sx,sy=rng.uniform(.05,.95),rng.uniform(.07,.96)
        angle=rng.uniform(-1.4,1.4); dx,dy=uu-sx,vv-sy
        along=dx*math.cos(angle)+dy*math.sin(angle); across=-dx*math.sin(angle)+dy*math.cos(angle)
        line=np.exp(-(across/rng.uniform(.0005,.0013))**2)*np.clip(1-np.abs(along)/rng.uniform(.008,.11),0,1)
        scratched=np.maximum(scratched,line*rng.uniform(.25,.85))
    chipped=np.maximum(chipped,scratched)
    if p['name']=='stop':
        # Original broad scrape marks, broken at their edges; no copied graffiti.
        for x0,y0,x1,y1,width in [(.09,.62,.29,.83,.005),(.27,.91,.56,.72,.006),(.59,.84,.84,.61,.004)]:
            dx,dy=x1-x0,y1-y0;t=np.clip(((uu-x0)*dx+(vv-y0)*dy)/(dx*dx+dy*dy),0,1)
            d=np.hypot(uu-x0-t*dx,vv-y0-t*dy)
            mark=np.clip(1-d/(width*(.5+flecks)),0,1)*np.clip(flecks*1.8,0,1)
            chipped=np.maximum(chipped,mark)
    rust=np.clip((coarse-.54)*3.5,0,.65)*(edge+.6*np.exp(-((hole_distance-.007)/.005)**2))
    substrate=np.array([.49,.50,.47])*(1-rust[:,:,None])+np.array([.25,.13,.055])*rust[:,:,None]
    tile[:,:,:3] *= (1+.045*noise+.10*(coarse-.5))[:,:,None]
    tile[:,:,:3] = tile[:,:,:3]*(1-chipped[:,:,None])+substrate*chipped[:,:,None]
    orm[top:bottom,left:right,1]=.67+.08*coarse-.11*chipped
    orm[top:bottom,left:right,2]=.03+.85*chipped*(1-rust)
    heightmap[top:bottom,left:right]=noise*.008-chipped*.075

# The unpainted backs and thin rolled edges share two padded atlas strips.
for region,colour,rough in [((.015,.825,.470,.160),[.36,.37,.35],.69),((.535,.825,.430,.160),[.12,.13,.13],.54)]:
    ax,ay,aw,ah=region; l,t,r,b=[round(q*n) for q in (ax,ay,ax+aw,ay+ah)]
    rng=np.random.default_rng(round(ax*1000)+160)
    noise=rng.uniform(-1,1,(b-t,r-l)); variation=value_noise(rng,b-t,r-l,10,35)
    albedo[t:b,l:r,:3]=np.array(colour)*(1+noise[:,:,None]*.06+(variation[:,:,None]-.5)*.18)
    orm[t:b,l:r,1]=rough; orm[t:b,l:r,2]=.88
    heightmap[t:b,l:r]=noise*.015

paths={}
for key,pixels in [('albedo',albedo),('orm',orm),('normal',normal_from_height(heightmap,2.4))]:
    paths[key]=write_png(TEXTURES/'sign-plate'/f'sign_plate_{key}.png',pixels)
    write_texture_import(paths[key],normal_map=key=='normal')
material_path=write_material('sign-plate','M_Hollow_SignPlate',albedo=paths['albedo'],
    orm=paths['orm'],normal=paths['normal'],normal_scale=.6,vertex_colour=False)
mat=bpy.data.materials.new('M_Hollow_SignPlate');mat.use_nodes=True
shader=mat.node_tree.nodes.get('Principled BSDF')
for key in ('albedo','orm','normal'):
    tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=bpy.data.images.load(str(paths[key]))
    if key!='albedo':tex.image.colorspace_settings.name='Non-Color'
    if key=='albedo':mat.node_tree.links.new(tex.outputs['Color'],shader.inputs['Base Color'])
    elif key=='normal':
        node=mat.node_tree.nodes.new('ShaderNodeNormalMap');node.inputs['Strength'].default_value=.6
        mat.node_tree.links.new(tex.outputs['Color'],node.inputs['Color']);mat.node_tree.links.new(node.outputs['Normal'],shader.inputs['Normal'])
    else:
        node=mat.node_tree.nodes.new('ShaderNodeSeparateColor');mat.node_tree.links.new(tex.outputs['Color'],node.inputs[0])
        mat.node_tree.links.new(node.outputs[1],shader.inputs['Roughness']);mat.node_tree.links.new(node.outputs[2],shader.inputs['Metallic'])


def cross(a,b):return a[0]*b[1]-a[1]*b[0]


def radial_boundary(p,angle):
    centre=np.array(p['hole']);direction=np.array([math.cos(angle),math.sin(angle)]);hits=[]
    for i,point in enumerate(p['outline']):
        a=np.array(point);edge=np.array(p['outline'][(i+1)%len(p['outline'])])-a
        denominator=cross(direction,edge)
        if abs(denominator)<1e-10:continue
        d=cross(a-centre,edge)/denominator;f=cross(a-centre,direction)/denominator
        if d>0 and -.00001<=f<=1.00001:hits.append(d)
    return min(hits)


objects=[]
for lod,steps,radials in [(0,48,[0,.18,.38,.60,.80,1]),(1,30,[0,.32,.67,1]),(2,18,[0,.50,1])]:
    vertices,faces,uvs,groups=[],[],[],[]
    for panel_index,p in enumerate(panels):
        cx,cz=p['hole'];w,h=p['width'],p['height'];angle=math.radians(p['angle'])
        angles=sorted(set([round(math.atan2(z-cz,x-cx),7) for x,z in p['outline']]+[round(-math.pi+i*math.tau/steps,7) for i in range(steps)]))
        rings=[];local_points=[]
        for side in (-1,1):
            for f in radials:
                ring=[]
                for a in angles:
                    radius=.0045+(radial_boundary(p,a)-.0045)*f
                    x,z=cx+math.cos(a)*radius,cz+math.sin(a)*radius
                    # Shallow corner bends, slight dish and a local crease; no physics.
                    bend=p['bend']*max(0,(x/w+.08)/.58)**2*(.35+(z/h+.5)*.65)
                    bend+=.0025*math.sin(x/w*3)*math.cos(z/h*3)
                    bend+=.004*max(0,-z/h-.31)*max(0,-x/w+.1)
                    position=(x*math.cos(angle)-z*math.sin(angle)+p['location'][0],
                        p['location'][1]+bend+side*.0012,
                        x*math.sin(angle)+z*math.cos(angle)+p['location'][2])
                    ring.append(len(vertices));vertices.append(position);local_points.append((x,z))
                rings.append(ring)
        def bridge(a,b,face_kind):
            for j in range(len(angles)):
                nj=(j+1)%len(angles);face=(a[j],a[nj],b[nj],b[j]);faces.append(face)
                face_uv=[]
                for idx in face:
                    # Panel-local coordinates can be recovered without an inverse transform.
                    x,z=local_points[idx-panel_vertex_start]
                    u=x/w+.5;v=.5-z/h
                    if face_kind=='front': ax,ay,aw,ah=p['atlas']
                    elif face_kind=='back':ax,ay,aw,ah=.025,.835,.450,.140
                    else:
                        ax,ay,aw,ah=.545,.835,.410,.140
                        u=(j+(1 if idx in (a[nj],b[nj]) else 0))/len(angles)
                        v=.25 if idx-panel_vertex_start<len(radials)*len(angles) else .75
                    face_uv.append((ax+aw*u,1-(ay+ah*v)))
                uvs.append(face_uv);groups.append(face_kind)
        panel_vertex_start=len(vertices)-len(local_points)
        nr=len(radials)
        for side in range(2):
            for k in range(nr-1):bridge(rings[side*nr+k],rings[side*nr+k+1],'front' if side==0 else 'back')
        bridge(rings[0],rings[nr],'wall');bridge(rings[nr-1],rings[2*nr-1],'wall')
    mesh=bpy.data.meshes.new(f'SignPlate_lod{lod}');mesh.from_pydata(vertices,[],faces);mesh.update()
    uv=mesh.uv_layers.new(name='UVMap')
    for polygon,coords,group in zip(mesh.polygons,uvs,groups):
        for loop,point in zip(polygon.loop_indices,coords):uv.data[loop].uv=point
        polygon.use_smooth=group!='wall'
    bm=bmesh.new();bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm,faces=bm.faces)
    # Separate smooth broad faces from the narrow edges without rounding the sheet.
    seams=[e for e in bm.edges if len(e.link_faces)==2 and e.link_faces[0].smooth!=e.link_faces[1].smooth]
    bmesh.ops.split_edges(bm,edges=seams)
    bm.to_mesh(mesh);bm.free()
    obj=bpy.data.objects.new(mesh.name,mesh);col.objects.link(obj);mesh.materials.append(mat);objects.append(obj)

lo=Vector(tuple(min(v.co[i] for v in objects[0].data.vertices) for i in range(3)))
hi=Vector(tuple(max(v.co[i] for v in objects[0].data.vertices) for i in range(3)))
offset=Vector(((lo.x+hi.x)/2,(lo.y+hi.y)/2,lo.z))
for obj in objects:
    for vertex in obj.data.vertices:vertex.co-=offset
objects.append(convex_hull([objects[0]],'SignPlate_hull0-convcolonly',col,max_faces=48))
report={'asset':'sign-plate','dimensions_blender_xyz_m':list(hi-lo),'texture_size':n,'material_count':1,
    'lod_triangles':{o.name:sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects[:3]},
    'hull_triangles':sum(len(p.vertices)-2 for p in objects[-1].data.polygons),
    'font':font_path.relative_to(REPO).as_posix()}
export_glb(ITEMS/'sign-plate.glb',objects,{'M_Hollow_SignPlate':material_path})
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'sign-plate-source.blend'))
(OUT/'geometry.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report),flush=True)
