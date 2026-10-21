"""Isolated survivor character audition. Runs in Blender 5.2.2; never writes game assets.

blender --background --factory-startup --threads 2 --python art/character_trial/build.py -- body
"""
from pathlib import Path
import sys, os, json, math, hashlib
import bpy, bmesh
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / '.artifacts/character-trial-v1'
OUT.mkdir(parents=True, exist_ok=True)
sys.path[:0] = [str(ROOT/'art/lib'), str(ROOT/'art/characters'), str(Path(__file__).parent)]
import hollow_art as H
import authored as A
DATA = Path('D:/GameDev/Tools/blender-hollow-config/extensions/.user/user_default/mpfb/data')

def material(name, color, rough=.6, metal=0):
    m=bpy.data.materials.new(name); m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value=(*color,1)
    p.inputs['Roughness'].default_value=rough; p.inputs['Metallic'].default_value=metal
    return m

def textured(name,path,rough=.55,alpha=False,skin=False):
    m=material(name,(.6,.6,.6),rough); n=m.node_tree.nodes; links=m.node_tree.links
    p=n.get('Principled BSDF'); tex=n.new('ShaderNodeTexImage')
    tex.image=bpy.data.images.load(str(path),check_existing=True)
    links.new(tex.outputs['Color'],p.inputs['Base Color'])
    if alpha:
        links.new(tex.outputs['Alpha'],p.inputs['Alpha']); m.surface_render_method='DITHERED'
    if skin:
        p.inputs['Subsurface Weight'].default_value=.07
        p.inputs['Subsurface Radius'].default_value=(1,.42,.23)
        p.inputs['Specular IOR Level'].default_value=.3
        noise=n.new('ShaderNodeTexNoise'); noise.inputs['Scale'].default_value=260
        noise.inputs['Detail'].default_value=2
        bump=n.new('ShaderNodeBump'); bump.inputs['Strength'].default_value=.13
        bump.inputs['Distance'].default_value=.00015
        links.new(noise.outputs['Fac'],bump.inputs['Height']); links.new(bump.outputs['Normal'],p.inputs['Normal'])
    return m

def clean_materials(obj):
    for i,old in enumerate(list(obj.data.materials)):
        if old is None: continue
        imgs=[n.image for n in old.node_tree.nodes if n.type=='TEX_IMAGE' and n.image] if old.use_nodes else []
        diffuse=next((im for im in imgs if any(s in im.name.lower() for s in ('diffuse','albedo'))), imgs[0] if imgs else None)
        if diffuse:
            obj.data.materials[i]=textured(old.name+'_PBR',bpy.path.abspath(diffuse.filepath),.7,any(s in obj.name.lower() for s in ('brow','hair','lash')))
            normal=next((im for im in imgs if 'normal' in im.name.lower()),None)
            if normal:
                m=obj.data.materials[i]; n=m.node_tree.nodes; links=m.node_tree.links
                tex=n.new('ShaderNodeTexImage');tex.image=normal;normal.colorspace_settings.name='Non-Color'
                nm=n.new('ShaderNodeNormalMap');links.new(tex.outputs['Color'],nm.inputs['Color'])
                links.new(nm.outputs['Normal'],n.get('Principled BSDF').inputs['Normal'])
        else:
            obj.data.materials[i]=material(old.name+'_PBR',tuple(old.diffuse_color)[:3],.65)

def smooth(obj):
    for p in obj.data.polygons:p.use_smooth=True

def camera(name,at,target,lens=52):
    d=bpy.data.cameras.new(name); o=bpy.data.objects.new(name,d); bpy.context.collection.objects.link(o)
    o.location=at; o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler(); d.lens=lens
    return o

def stage():
    scene=bpy.context.scene; scene.render.engine='CYCLES'; scene.cycles.samples=32
    scene.cycles.use_denoising=True; scene.render.resolution_x=1200; scene.render.resolution_y=1200
    scene.render.resolution_percentage=100; scene.render.image_settings.file_format='PNG'
    if scene.world is None: scene.world=bpy.data.worlds.new('StudioWorld')
    scene.world.color=(.22,.22,.22)
    scene.world.use_nodes=True; scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(.17,.19,.24,1)
    scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=.45
    scene.view_settings.view_transform='AgX'
    for name,loc,power,size,color in [('Key',(-3,-4,5),850,4,(1,.88,.76)),('Fill',(4,-2,3),550,3,(.75,.85,1)),('Rim',(0,3,4),1000,2,(.8,.87,1))]:
        d=bpy.data.lights.new(name,'AREA');d.energy=power;d.shape='DISK';d.size=size;d.color=color
        o=bpy.data.objects.new(name,d);bpy.context.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
    bpy.ops.mesh.primitive_plane_add(size=200); floor=bpy.context.object;floor.name='StudioFloor';floor.data.materials.append(material('Stage',(0.055,.071,.083),.8))
    scene.camera=camera('TPS_review',(2.7,-4.7,2.3),(0,0,.96),65)
    camera('Portrait',(1,-2.1,1.76),(0,0,1.57),70)
    fp=camera('FPS_review',(0,-.04,1.68),(0,-1,1.64),27)
    fp.data.clip_start=.015

def body():
    H.fresh_scene(); H.enable_mpfb(); hs,ts,ls=H.mpfb()
    macros=dict(H.MALE);macros.update(age=.63,muscle=.62,weight=.48,height=.52,proportions=.55)
    macros['race']={'caucasian':.55,'african':.30,'asian':.15}
    mesh,rig=H.make_human(macros,[('head/head-square.target.gz',.22),('chin/chin-bones-incr.target.gz',.2)])
    rig.name='Survivor_TPS_Rig';mesh.name='Survivor_Body'
    assets=[('Eyes','eyes/high-poly/high-poly.mhclo','Eyes'),('Brows','eyebrows/eyebrow001/eyebrow001.mhclo','Eyebrows'),('Hair','hair/short02/short02.mhclo','Hair'),('Workwear','clothes/male_casualsuit06/male_casualsuit06.mhclo','Clothes'),('Boots','clothes/shoes02/shoes02.mhclo','Clothes')]
    objects=[mesh]
    for name,rel,kind in assets:
        obj=hs.add_mhclo_asset(str(DATA/rel),mesh,asset_type=kind,subdiv_levels=0,material_type='MAKESKIN')
        obj.name=name;objects.append(obj);clean_materials(obj);smooth(obj)
    ts.bake_targets(mesh)
    # Apply wardrobe occlusion, then strip the helper cages. Original anatomical topology retained.
    for mod in list(mesh.modifiers):
        if mod.type=='MASK':
            H.apply_modifier(mesh,mod.name)
    if 'body' in mesh.vertex_groups:
        gi=mesh.vertex_groups['body'].index
        keep={v.index for v in mesh.data.vertices if any(g.group==gi and g.weight>.5 for g in v.groups)}
        bm=bmesh.new();bm.from_mesh(mesh.data);bm.verts.ensure_lookup_table()
        bmesh.ops.delete(bm,geom=[v for v in bm.verts if v.index not in keep],context='VERTS');bm.to_mesh(mesh.data);bm.free()
    mesh.data.materials.clear();mesh.data.materials.append(textured('Survivor_Skin',DATA/'skins/young_caucasian_male2/young_lightskinned_male_diffuse2.png',.54,skin=True))
    for obj in objects:
        for mod in list(obj.modifiers):
            if mod.type not in ('ARMATURE',):obj.modifiers.remove(mod)
        H.limit_and_normalise_weights(obj);smooth(obj)
    stage()
    bpy.context.view_layer.update()
    # A straight-ahead relaxed inspection pose; no retargeting or game pose modifiers.
    model=A.Rig(rig)
    (OUT/'rest-bones.json').write_text(json.dumps({b.name:{'head':list(b.head_local),'tail':list(b.tail_local)} for b in rig.data.bones},indent=2))
    meta={'blender':bpy.app.version_string,'body_height':round(mesh.dimensions.z,4),'source':'MakeHuman/MPFB CC0 system assets','objects':{o.name:{'vertices':len(o.data.vertices),'triangles':H.triangles(o)} for o in objects}}
    (OUT/'body-info.json').write_text(json.dumps(meta,indent=2))
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'source-body.blend'),compress=True)
    bpy.context.scene.render.filepath=str(OUT/'body-initial.png');bpy.ops.render.render(write_still=True)
    print('TRIAL_BODY_COMPLETE',json.dumps(meta),flush=True)

if __name__=='__main__':
    args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else ['body']
    if args[0]=='body': body()
