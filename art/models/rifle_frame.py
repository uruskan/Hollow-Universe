"""Original cosmetic rifle component, based on visual observation only.

Dark stamped housing and amber timber brace with ribbed cover plates. This is a
fictional presentation asset, with no functional internals or working mechanism.
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

OUT=REPO/'.artifacts/rifle-frame-r01';OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system='METRIC';col=collection('RifleFrame')
regions={'steel':(.01,.01,.98,.57),'wood':(.01,.61,.98,.26),'edge':(.01,.90,.98,.08)}
n=1024;v,u=np.mgrid[0:n,0:n].astype(np.float32)/n
rng=np.random.default_rng(162217);noise=rng.uniform(-1,1,(n,n)).astype(np.float32)
cloud=np.zeros((n,n),np.float32)
for freq,weight in [(9,.4),(28,.28),(73,.16),(205,.06)]:
    for j in range(3):
        angle=rng.uniform(0,math.tau)
        cloud+=weight*np.sin((u*math.cos(angle)+v*math.sin(angle))*freq+rng.uniform(0,7))/3
albedo=np.ones((n,n,4),np.float32);orm=np.ones_like(albedo)
shade=.90+cloud*.35+noise*.065
albedo[:,:,:3]=np.array([.24,.255,.265])*shade[:,:,None]
orm[:,:,1]=.56+cloud*.16;orm[:,:,2]=.78
height=cloud*.025+noise*.015
for i in range(95):
    sx,sy=rng.uniform(.02,.98),rng.uniform(.02,.57)
    length=rng.uniform(.005,.06);width=rng.uniform(.00025,.0009)
    slope=rng.uniform(-.4,.4)
    line=np.exp(-((v-sy-(u-sx)*slope)/width)**2)*np.clip(1-abs(u-sx)/length,0,1)
    albedo[:,:,:3]+=line[:,:,None]*.17;orm[:,:,1]-=line*.13;height-=line*.04
# Warm varnished timber, worn through in irregular places; no green tint.
wood=(v>.60)&(v<.88)
grain=np.sin(v*1870+np.sin(u*15)*3+cloud*6)*.5+.5
tone=.92+cloud*.33+noise*.04-grain**14*.18
albedo[wood,:3]=(np.array([.63,.315,.095])*tone[:,:,None])[wood]
orm[wood,1]=.70;orm[wood,2]=0;height[wood]=(grain*.018+noise*.009)[wood]
for i in range(23):
    sx,sy=rng.uniform(.03,.97),rng.uniform(.62,.86)
    line=np.exp(-((v-sy-.0005*np.sin(u*75+i))/.00055)**2)*np.clip(1-abs(u-sx)/rng.uniform(.02,.13),0,1)*wood
    albedo[:,:,:3]*=1-line[:,:,None]*.35;height-=line*.05
edge=v>.89
albedo[edge,:3]=(np.array([.36,.37,.375])*(.92+noise*.09+cloud*.18)[:,:,None])[edge]
orm[edge,1]=.49;orm[edge,2]=.88;height[edge]=(noise*.004)[edge]
paths={}
for key,pixels in [('albedo',np.clip(albedo,0,1)),('orm',np.clip(orm,0,1)),('normal',normal_from_height(height,2.4))]:
    paths[key]=write_png(TEXTURES/'rifle-frame'/f'rifle_frame_{key}.png',pixels)
    write_texture_import(paths[key],normal_map=key=='normal')
mp=write_material('rifle-frame','M_Hollow_RifleFrame',albedo=paths['albedo'],orm=paths['orm'],normal=paths['normal'],normal_scale=.55,vertex_colour=False)
mat=bpy.data.materials.new('M_Hollow_RifleFrame');mat.diffuse_color=(.23,.24,.25,1)
objects=[]
for lod in range(3):
    mesh=ComponentMesh(col,mat,regions,lod)
    body=[(-.191,.009),(-.184,.002),(.074,.002),(.083,.012),(.080,.063),(.030,.077),(-.144,.077),(-.176,.066),(-.188,.043)]
    mesh.profile('StampedHousing',body,-.027,.027,'steel',.003)
    mesh.profile('RaisedSidePanel',[(-.153,.023),(-.146,.019),(.012,.019),(.040,.052),(.024,.065),(-.137,.065)],-.0285,-.026,'steel',.001)
    mesh.profile('RearEdge',[(-.191,.009),(-.184,.002),(-.177,.006),(-.180,.040),(-.173,.058),(-.179,.062),(-.188,.043)],-.028,.028,'steel',.001)
    mesh.box('TopRidge',(-.065,0,.077),(.18,.021,.010),'steel',.002)
    # The short warm timber brace and ribs carry the reference's identifying colour block.
    brace=[(.060,.007),(.170,.007),(.187,.018),(.187,.078),(.177,.094),(.068,.094),(.060,.086)]
    mesh.profile('FrontWoodBrace',brace,-.034,-.022,'wood',.003)
    mesh.profile('RearWoodBrace',brace,.022,.034,'wood',.003)
    mesh.lathe_x('UpperCasing',[(.034,.003),(.034,.020),(.051,.026),(.172,.026),(.179,.020),(.179,.003)],(0,.068),'steel')
    for x in (.074,.099,.124,.149):
        mesh.lathe_x('UpperCasingBand',[(x-.005,.025),(x-.003,.029),(x+.003,.029),(x+.005,.025)],(0,.068),'steel')
    cover=[(.074,.017),(.174,.017),(.179,.022),(.179,.068),(.174,.075),(.074,.075),(.069,.068),(.069,.023)]
    for side in (-1,1):
        low,high=sorted((side*.035,side*.039))
        mesh.profile('RibbedSideCover',cover,low,high,'steel',.002)
        for x in (.091,.110,.129,.148,.165):
            mesh.box('StampedRib',(x,side*.040,.046),(.005,.006,.052),'edge',.002)
        for x,z in [(.079,.024),(.079,.068),(.171,.024),(.171,.068)]:
            mesh.box('CoverFastener',(x,side*.041,z),(.0045,.003,.0045),'edge',.0015)
    mesh.profile('RaisedSlideDetail',[(-.109,.035),(-.078,.035),(-.047,.064),(-.028,.065),(-.028,.071),(-.053,.072),(-.084,.042),(-.109,.041)],-.031,-.028,'steel',.001)
    # Shallow non-functional side markings and rivets, all original layout.
    for x in (-.158,-.120,.022):
        mesh.box('SideRivet',(x,-.030,.013),(.004,.003,.004),'edge',.001)
    for x in (-.146,-.125,-.104):mesh.box('TopNotch',(x,0,.084),(.008,.029,.006),'steel',.001)
    mesh.box('SideBadge',(-.027,-.031,.047),(.020,.003,.015),'steel',.003)
    objects.append(mesh.finish(f'RifleFrame_lod{lod}'))
objects.append(convex_hull([objects[0]],'RifleFrame_hull0-convcolonly',col,max_faces=40))
verts=[v.co for v in objects[0].data.vertices]
report={'asset':'rifle-frame','dimensions_blender_xyz_m':[max(v[i] for v in verts)-min(v[i] for v in verts) for i in range(3)],'texture_size':n,'material_count':1,'lod_triangles':{o.name:sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects[:3]},'hull_triangles':sum(len(p.vertices)-2 for p in objects[-1].data.polygons)}
export_glb(ITEMS/'rifle-frame.glb',objects,{'M_Hollow_RifleFrame':mp})
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'rifle-frame-source.blend'))
(OUT/'geometry.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
print(json.dumps(report),flush=True)
