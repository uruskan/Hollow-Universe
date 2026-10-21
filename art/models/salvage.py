"""Original compact rusty scrap assortment, studied from Rust's public Scrap icon.

No downloaded meshes or pixels. One atlas/material, hand-built LODs and a small
deterministic vertex-occlusion pass; no texture baking, simulation or providers.
"""
from pathlib import Path
import json, math, sys
import bpy
import numpy as np
from mathutils import Vector, Matrix

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from hollow import ITEMS, TEXTURES, REPO
from hollow.export import collection, export_glb, convex_hull, vertex_ao, set_export_colour
from hollow.godot import write_material
from hollow.textures import write_png, write_texture_import, normal_from_height
from hollow.component_mesh import ComponentMesh

OUT = REPO / '.artifacts/salvage-r01'
OUT.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
col = collection('Salvage')
regions = {'rust':(.01,.01,.98,.51), 'steel':(.01,.55,.98,.18),
           'sheet':(.01,.76,.98,.14), 'dark':(.01,.93,.98,.06)}
n = 1024
v,u = np.mgrid[0:n,0:n].astype(np.float32)/n
rng = np.random.default_rng(150271)
noise = rng.uniform(-1,1,(n,n)).astype(np.float32)
cloud = np.zeros((n,n),np.float32)
# Smooth random fields avoid periodic stripes or a repeated rust pattern.
for cells,weight in [(6,.70),(18,.35),(47,.16),(127,.07)]:
    grid=rng.uniform(-1,1,(cells+1,cells+1)).astype(np.float32)
    xx,yy=u*cells,v*cells;ix=xx.astype(int);iy=yy.astype(int)
    fx,fy=xx-ix,yy-iy;fx=fx*fx*(3-2*fx);fy=fy*fy*(3-2*fy)
    field=(grid[iy,ix]*(1-fx)+grid[iy,ix+1]*fx)*(1-fy)+(grid[iy+1,ix]*(1-fx)+grid[iy+1,ix+1]*fx)*fy
    cloud+=field*weight
rust = np.clip(.70+cloud*1.45+noise*.10,.16,1)
albedo = np.ones((n,n,4),np.float32)
orm = np.ones_like(albedo)
height = cloud*.035+noise*.014-rust*.018
albedo[:,:,:3] = np.array([.31,.30,.27])*(1-rust[:,:,None])+np.array([.27,.125,.065])*rust[:,:,None]
albedo[:,:,:3] *= (.96+noise*.08)[:,:,None]
orm[:,:,1] = .68+rust*.24
orm[:,:,2] = .58*(1-rust*.98)
steel = (v>.54)&(v<.74)
albedo[steel,:3] = ((np.array([.44,.41,.36])*(1-rust[:,:,None]*.38)+np.array([.28,.145,.075])*rust[:,:,None]*.38)*(.94+noise*.065+cloud*.15)[:,:,None])[steel]
orm[steel,1] = (.60+rust*.15)[steel];orm[steel,2] = .45
sheet = (v>.75)&(v<.91)
albedo[sheet,:3] = (np.array([.33,.15,.08])*(.93+cloud*.55+noise*.08)[:,:,None])[sheet]
orm[sheet,1] = .84;orm[sheet,2] = .12
dark = v>.92
albedo[dark,:3] = (np.array([.125,.115,.10])*(.96+noise*.07)[:,:,None])[dark]
orm[dark,1] = .82;orm[dark,2] = .3
# Small bright scrapes interrupt the oxide; there are no photographic inputs.
for _ in range(70):
    x,y = rng.uniform(.02,.98),rng.uniform(.015,.89)
    width,length = rng.uniform(.0004,.0011),rng.uniform(.006,.05)
    stroke = np.exp(-((v-y-(u-x)*.25)/width)**2)*np.clip(1-abs(u-x)/length,0,1)
    albedo[:,:,:3] += stroke[:,:,None]*.15
    orm[:,:,1] -= stroke*.14;orm[:,:,2] += stroke*.15;height -= stroke*.035
paths = {}
for key,pixels in [('albedo',np.clip(albedo,0,1)),('orm',np.clip(orm,0,1)),('normal',normal_from_height(height,2.2))]:
    paths[key] = write_png(TEXTURES/'salvage'/f'salvage_{key}.png',pixels)
    write_texture_import(paths[key],normal_map=key=='normal')
mp = write_material('salvage','M_Hollow_Salvage',albedo=paths['albedo'],orm=paths['orm'],normal=paths['normal'],normal_scale=.5,vertex_colour=True,vertex_colour_srgb=False)
mat = bpy.data.materials.new('M_Hollow_Salvage');mat.diffuse_color=(.36,.25,.16,1)


def transform(obj, centre, rotation=None):
    rotation = rotation or Matrix.Identity(3)
    for vertex in obj.data.vertices: vertex.co = rotation@vertex.co+Vector(centre)
    obj.data.update()
    return obj


def washer(mesh, name, profiles, centre, segments, region, rotation=None, teeth=0):
    """Closed annular profile with a true open centre; optional cosmetic teeth."""
    vertices,faces = [],[]
    for radius,z,toothed in profiles:
        for j in range(segments):
            a = math.tau*j/segments
            rr = radius + (.009 if toothed and j%4 in (1,2) else 0)
            vertices.append((rr*math.cos(a),rr*math.sin(a),z))
    for k in range(len(profiles)):
        kn = (k+1)%len(profiles)
        for j in range(segments):
            jn=(j+1)%segments
            faces.append((k*segments+j,k*segments+jn,kn*segments+jn,kn*segments+j))
    obj=mesh.mesh(name,vertices,faces,region)
    return transform(obj,centre,rotation)


def bent_pipe(mesh, points, radius, wall):
    count=(24,16,10)[mesh.lod]
    pts=[Vector(p) for p in points];vertices=[];faces=[]
    for i,p in enumerate(pts):
        tangent=(pts[min(i+1,len(pts)-1)]-pts[max(i-1,0)]).normalized()
        axis=tangent.cross(Vector((0,1,0))).normalized();other=tangent.cross(axis).normalized()
        for r in (radius,radius-wall):
            for j in range(count):
                a=math.tau*j/count
                vertices.append(tuple(p+r*(math.cos(a)*axis+math.sin(a)*other)))
    for i in range(len(pts)-1):
        for side in (0,1):
            for j in range(count):
                a=i*count*2+side*count+j;b=i*count*2+side*count+(j+1)%count
                faces.append((a,b,b+count*2,a+count*2))
    for i in (0,len(pts)-1):
        for j in range(count):
            a=i*count*2+j;b=i*count*2+(j+1)%count
            faces.append((a,b,b+count,a+count))
    return mesh.mesh('BentRearPipe',vertices,faces,'rust',True)


objects=[]
for lod in range(3):
    mesh=ComponentMesh(col,mat,regions,lod)
    # Rear bracket and elbow keep a ragged, compact outline behind the gear.
    bracket=mesh.profile('BrokenBracket',[(-.17,-.09),(.14,-.09),(.16,.035),(.075,.07),(-.14,.06),(-.165,.03)],-.003,.003,'rust',.001)
    transform(bracket,(0,.055,.037),Matrix.Rotation(math.pi/2,3,'X'))
    bent_pipe(mesh,[(-.17,.12,.057),(-.05,.12,.057),(.09,.12,.057),(.12,.12,.062),(.138,.12,.079),(.14,.12,.112)],.021,.004)
    # An open, worn gear leans on the bracket. Its rim stays bright at icon size.
    count=(120,96,72)[lod]
    rot=Matrix.Rotation(math.radians(10),3,'X')@Matrix.Rotation(math.radians(-7),3,'Y')
    centre=(-.043,.015,.065)
    washer(mesh,'GearBody',[(.028,-.012,False),(.092,-.012,False),(.092,.011,False),(.028,.011,False)],centre,count,'rust',rot)
    washer(mesh,'GearTeeth',[(.088,-.011,False),(.099,-.009,True),(.099,.010,True),(.088,.012,False)],centre,count,'steel',rot,teeth=1)
    washer(mesh,'GearFaceLip',[(.084,.011,False),(.087,.012,False),(.087,.014,False),(.084,.014,False)],centre,count,'steel',rot)
    washer(mesh,'GearBoreLip',[(.025,-.013,False),(.030,-.013,False),(.032,.013,False),(.029,.020,False),(.025,.020,False)],centre,(48,32,20)[lod],'steel',rot)
    # Foreground pipe has real open ends and visible wall thickness.
    pipe=mesh.lathe_x('FrontHollowPipe',[(-.18,.018),(-.18,.022),(-.175,.026),(.11,.026),(.117,.023),(.117,.018),(-.18,.018)],(0,0),'rust',segments=(28,20,12)[lod])
    # Its recessed wall needs dark oxide: bright reflected metal can read as a cap.
    uv=pipe.data.uv_layers.active
    for face in pipe.data.polygons:
        if all(math.hypot(pipe.data.vertices[i].co.y,pipe.data.vertices[i].co.z)<.0181 for i in face.vertices):
            for loop in face.loop_indices:
                p=pipe.data.vertices[pipe.data.loops[loop].vertex_index].co
                uv.data[loop].uv=(.025+.95*(p.x+.18)/.297,1-(.935+.05*(math.atan2(p.z,p.y)/math.tau+.5)))
    transform(pipe,(-.008,-.105,.029),Matrix.Rotation(math.radians(-9),3,'Z'))
    # Corrugated broken sheet on the right, six broad ridges, real thin edges.
    steps=(48,30,18)[lod];vertices=[];faces=[]
    for layer in (0,.002):
        for row in (0,1):
            for j in range(steps+1):
                t=j/steps;x=.012+t*.168
                y=-.115+row*.175
                y+=(.004*math.sin(j*2.3)+.002*math.sin(j*.8)) if row==0 else .002*math.sin(j*1.7)
                z=.044+.008*math.cos(t*math.tau*5.5)+.012*row+.008*t+layer
                vertices.append((x,y,z))
    width=steps+1
    for layer in (0,1):
        for j in range(steps):
            a=layer*2*width+j;faces.append((a,a+1,a+width+1,a+width))
    for row in (0,1):
        for j in range(steps):
            a=row*width+j;faces.append((a,a+1,a+1+2*width,a+2*width))
    for j in (0,steps):faces.append((j,j+width,j+3*width,j+2*width))
    mesh.mesh('CorrugatedOffcut',vertices,faces,'sheet',True)
    # A stout open coil rests behind the plate, separate from the toothed wheel.
    turns=5.7;steps=(132,84,52)[lod]
    points=[]
    for j in range(steps+1):
        t=j/steps;a=t*math.tau*turns
        points.append((.089+.028*math.cos(a),.002+t*.137,.079+.028*math.sin(a)))
    mesh.cable('WornCoil',points,.007,'steel',closed=False,sides=(8,6,4)[lod])
    # Two hexagonal nuts have open bores at every LOD, distinct from flat shards.
    for x,y,radius,angle in [(-.137,-.150,.024,.3),(-.083,-.151,.019,-.2)]:
        segments=24 if lod==0 else 12
        vertices=[];faces=[]
        for radius_kind,z in [('inner',0),('outer',0),('outer',.017),('inner',.017)]:
            for j in range(segments):
                a=math.tau*j/segments+angle
                rr=radius*.42 if radius_kind=='inner' else radius*math.cos(math.pi/6)/math.cos((a-angle+math.pi/6)%(math.pi/3)-math.pi/6)
                vertices.append((x+rr*math.cos(a),y+rr*math.sin(a),z+.003))
        for ring in range(4):
            for j in range(segments):
                a=ring*segments+j;b=ring*segments+(j+1)%segments
                faces.append((a,b,((ring+1)%4)*segments+(j+1)%segments,((ring+1)%4)*segments+j))
        mesh.mesh('LooseHexNut',vertices,faces,'rust')
    # One small wire loop peeks from the debris, subordinate to the reference parts.
    points=[(-.10+.045*math.cos(a),.055+.036*math.sin(a),.025) for a in np.linspace(0,math.tau,(28,18,12)[lod],endpoint=False)]
    mesh.cable('WireLoop',points,.0025,'dark')
    obj=mesh.finish(f'Salvage_lod{lod}')
    ao=vertex_ao([obj],samples=8,distance=.11,floor=False)[obj]
    colors=np.ones((len(ao),4),np.float32);colors[:,:3]=(.68+.32*ao)[:,None]
    set_export_colour(obj,colors)
    objects.append(obj)
objects.append(convex_hull([objects[0]],'Salvage_hull0-convcolonly',col,max_faces=40))
bottom=min(v.co.z for v in objects[0].data.vertices)
for obj in objects:
    for vertex in obj.data.vertices:vertex.co.z-=bottom
vertices=[v.co for v in objects[0].data.vertices]
report={'asset':'salvage','dimensions_blender_xyz_m':[max(v[i] for v in vertices)-min(v[i] for v in vertices) for i in range(3)],'texture_size':n,'material_count':1,'lod_triangles':{o.name:sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects[:3]},'hull_triangles':sum(len(p.vertices)-2 for p in objects[-1].data.polygons),'vertex_ao_samples':8}
export_glb(ITEMS/'salvage.glb',objects,{'M_Hollow_Salvage':mp})
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'salvage-source.blend'))
(OUT/'geometry.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report),flush=True)
