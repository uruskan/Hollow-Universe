"""Original cosmetic receiver: rusty housing, pale timber brace and bindings.

Observed Rust Semi Automatic Body icon before authoring. No game files copied.
Run with Blender in background mode, two threads; no baking or simulation.
"""
from pathlib import Path
import json
import math
import sys
import bpy
import numpy as np

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from hollow import ITEMS,TEXTURES,REPO
from hollow.export import collection,export_glb,convex_hull
from hollow.godot import write_material
from hollow.textures import write_png,write_texture_import,normal_from_height
from hollow.component_mesh import ComponentMesh

OUT=REPO/'.artifacts/receiver-r01';OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system='METRIC'
col=collection('Receiver')
regions={'rust':(.01,.01,.98,.44),'wood':(.01,.48,.98,.30),
         'dark':(.01,.81,.98,.08),'cord':(.01,.92,.98,.07)}
n=1024;v,u=np.mgrid[0:n,0:n].astype(np.float32)/n
rng=np.random.default_rng(161213)
noise=rng.uniform(-1,1,(n,n)).astype(np.float32)
cloud=np.zeros((n,n),np.float32)
for freq,weight in [(12,.4),(39,.22),(107,.12),(231,.06)]:
    for j in range(3):
        angle=rng.uniform(0,math.tau)
        cloud+=weight*np.sin((u*math.cos(angle)+v*math.sin(angle))*freq+rng.uniform(0,7))/3
albedo=np.ones((n,n,4),np.float32);orm=np.ones_like(albedo);height=np.zeros((n,n),np.float32)
rust=np.clip(.45+cloud*2.3+noise*.12,0,1)
steel=np.array([.37,.365,.34]);oxide=np.array([.36,.19,.105])
albedo[:,:,:3]=(1-rust[:,:,None])*steel+rust[:,:,None]*oxide
albedo[:,:,:3]*=(.93+noise*.11)[:,:,None]
orm[:,:,1]=.51+rust*.29;orm[:,:,2]=.88*(1-rust*.92)
height=cloud*.035+noise*.018-rust*.035
# Pale sun-bleached timber. Long non-periodic fibres, dark checks and rough saw ends.
wood=(v>.46)&(v<.79)
grain=np.sin(v*1640+np.sin(u*11)*2+cloud*5)*.5+.5
fibres=np.sin(v*3910+np.sin(u*19)*3)*.5+.5
shade=.91+cloud*.28+noise*.05-grain**18*.16-fibres**16*.05
albedo[wood,:3]=(np.array([.65,.585,.46])*shade[:,:,None])[wood]
orm[wood,1]=.84;orm[wood,2]=0;height[wood]=(.025*grain+.006*noise)[wood]
for i in range(34):
    sx,sy=rng.uniform(.02,.98),rng.uniform(.49,.77)
    length=rng.uniform(.015,.20);width=rng.uniform(.0003,.0012)
    line=np.exp(-((v-sy-.0006*np.sin(u*73+i))/width)**2)*np.clip(1-abs(u-sx)/length,0,1)*wood
    albedo[:,:,:3]*=1-line[:,:,None]*.55;height-=line*.08
# Scattered bare-metal scrapes over the rusty housing, not a uniform border.
for i in range(75):
    sx,sy=rng.uniform(.02,.98),rng.uniform(.02,.44)
    length=rng.uniform(.004,.05);width=rng.uniform(.0003,.001)
    scratch=np.exp(-((v-sy-(u-sx)*.18)/width)**2)*np.clip(1-abs(u-sx)/length,0,1)
    albedo[:,:,:3]+=scratch[:,:,None]*.20;orm[:,:,1]-=scratch*.12;orm[:,:,2]+=scratch*.08
dark=(v>.80)&(v<.90);cord=v>.91
albedo[dark,:3]=(np.array([.18,.185,.17])*(.95+noise*.12+cloud*.3)[:,:,None])[dark]
orm[dark,1]=.62;orm[dark,2]=.8;height[dark]=(noise*.008)[dark]
albedo[cord,:3]=(np.array([.58,.49,.30])*(.93+noise*.06+np.sin(u*1100+v*600)*.06)[:,:,None])[cord]
orm[cord,1]=.88;orm[cord,2]=0;height[cord]=(np.sin(u*1100+v*600)*.02)[cord]
paths={}
for key,pixels in [('albedo',np.clip(albedo,0,1)),('orm',np.clip(orm,0,1)),('normal',normal_from_height(height,2.5))]:
    paths[key]=write_png(TEXTURES/'receiver'/f'receiver_{key}.png',pixels)
    write_texture_import(paths[key],normal_map=key=='normal')
mp=write_material('receiver','M_Hollow_Receiver',albedo=paths['albedo'],orm=paths['orm'],normal=paths['normal'],normal_scale=.6,vertex_colour=False)
mat=bpy.data.materials.new('M_Hollow_Receiver');mat.diffuse_color=(.4,.32,.23,1)

objects=[]
for lod in range(3):
    mesh=ComponentMesh(col,mat,regions,lod)
    # A deliberately fictional salvaged housing; no functional internal mechanism.
    mesh.lathe_x('RustyHousing',[(-.174,.005),(-.174,.021),(-.165,.025),(.163,.025),(.171,.028),(.181,.028),(.184,.025),(.184,.019),(.172,.019),(.171,.003)],(0,.075),'rust')
    mesh.lathe_x('DarkEndLip',[(.177,.0281),(.181,.0288),(.184,.026),(.184,.023),(.181,.026),(.177,.027)],(0,.075),'dark')
    mesh.box('RearCasing',(-.128,0,.060),(.087,.052,.042),'rust',.004)
    mesh.box('TopSeam',(-.083,0,.076),(.010,.053,.038),'dark',.001)
    timber=[(-.174,.003),(-.116,.001),(-.111,.004),(.145,.004),(.152,.011),(.150,.049),(.142,.062),(-.080,.054),(-.083,.051),(-.175,.049),(-.181,.036),(-.177,.028),(-.183,.025)]
    mesh.profile('FrontTimberBrace',timber,-.039,-.022,'wood',.001)
    mesh.profile('RearTimberBrace',[(x+.002,z+.003) for x,z in timber],.021,.037,'wood',.001)
    # Two dark wrap-around clips and tan ties keep the mixed construction readable.
    for x in (.108,.139):
        path=[(-.041,.003),(-.041,.055),(-.027,.082),(-.018,.098),(0,.102),(.018,.098),(.027,.082),(.039,.055),(.039,.003)]
        vertices=[]
        for thickness in (0,.002):
            for side in (-1,1):
                vertices += [(x+side*.007,y+(thickness if y>0 else -thickness),z+thickness*.35) for y,z in path]
        count=len(path);faces=[]
        for ring in range(4):
            nxt=(ring+1)%4
            # Ring order follows the strip's rectangular cross section.
            a,b=[(0,1),(1,3),(3,2),(2,0)][ring]
            faces += [(a*count+i,a*count+(i+1)%count,b*count+(i+1)%count,b*count+i) for i in range(count)]
        mesh.mesh('ContinuousClip',vertices,faces,'dark')
        if lod<2:
            for z in (.015,.027,.039,.051):mesh.box('ClipRib',(x,-.044,z),(.010,.002,.002),'rust',.0004)
    for x,drift in [(-.144,.006),(-.136,-.005),(.119,.014),(.129,-.009)]:
        pts=[(x,-.044,.008),(x+drift,-.044,.051),(x+drift*.6,-.026,.094),(x,0,.104),(x-drift,.038,.078),(x-drift,.041,.016),(x,.028,.001),(x,-.030,.001)]
        # Extra interpolated points give a soft angular cord silhouette at close range.
        if lod==0:
            pts=[p for i,a in enumerate(pts) for p in (a,tuple((a[j]+pts[(i+1)%len(pts)][j])*.5 for j in range(3)))]
        mesh.cable('TanBinding',pts,.0018,'cord')
    mesh.box('DarkHousingSlot',(-.025,-.025,.078),(.049,.0018,.010),'dark',.002)
    mesh.box('RearTab',(-.166,0,.093),(.013,.012,.034),'dark',.0015)
    # Visible nail-like patches on the timber, purely cosmetic.
    for x in (-.151,.130):
        for side in (-1,1):mesh.box('BracePin',(x,side*.040,.023),(.004,.002,.004),'dark',.001)
    obj=mesh.finish(f'Receiver_lod{lod}');objects.append(obj)
objects.append(convex_hull([objects[0]],'Receiver_hull0-convcolonly',col,max_faces=40))
verts=[v.co for v in objects[0].data.vertices]
report={'asset':'receiver','dimensions_blender_xyz_m':[max(v[i] for v in verts)-min(v[i] for v in verts) for i in range(3)],'texture_size':n,'material_count':1,'lod_triangles':{o.name:sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects[:3]},'hull_triangles':sum(len(p.vertices)-2 for p in objects[-1].data.polygons)}
export_glb(ITEMS/'receiver.glb',objects,{'M_Hollow_Receiver':mp})
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'receiver-source.blend'))
(OUT/'geometry.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report),flush=True)
