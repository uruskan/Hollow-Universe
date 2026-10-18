"""Bake the audition directly on its own bind skeleton and export two independent GLBs."""
from pathlib import Path
import sys, json, math, os, shutil
import bpy, bmesh
from mathutils import Matrix, Vector, Quaternion
ROOT=Path(__file__).resolve().parents[2]
sys.path[:0]=[str(Path(__file__).parent),str(ROOT/'art/lib'),str(ROOT/'art/characters')]
import build as B
import hollow_art as H
import authored as A
import motions as M
import fps_motions as F
import props
SOURCE=B.OUT
OUT=Path(os.environ.get('CHARACTER_TRIAL_OUT',str(ROOT/'.artifacts/character-trial-v2')))
OUT.mkdir(parents=True,exist_ok=True)
PROFILE=json.loads((Path(__file__).parent/'grip_profile.json').read_text())

def reuse_surface_normal(obj,label):
    """Same source topology/UVs; reuse the already baked portable normal map."""
    source=SOURCE/(label+'.png')
    if not source.is_file():
        bake_surface_normals(obj,label)
        return
    shutil.copy2(source,OUT/source.name)
    im=bpy.data.images.load(str(OUT/source.name),check_existing=True)
    im.colorspace_settings.name='Non-Color'
    for mat in obj.data.materials:
        nodes=mat.node_tree.nodes;links=mat.node_tree.links
        shader=next(n for n in nodes if n.type=='BSDF_PRINCIPLED')
        tex=nodes.new('ShaderNodeTexImage');tex.image=im
        normal=nodes.new('ShaderNodeNormalMap')
        links.new(tex.outputs['Color'],normal.inputs['Color'])
        links.new(normal.outputs['Normal'],shader.inputs['Normal'])

def bake_surface_normals(obj,label):
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=1
    try:
        prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='OPTIX';prefs.get_devices()
        for d in prefs.devices:d.use=d.type=='OPTIX'
        scene.cycles.device='GPU'
    except Exception:pass
    image=bpy.data.images.new(label,width=2048,height=2048,alpha=False)
    image.colorspace_settings.name='Non-Color'
    for m in obj.data.materials:
        node=m.node_tree.nodes.new('ShaderNodeTexImage');node.image=image
        m.node_tree.nodes.active=node;node.select=True
    H.select_only([obj]);bpy.ops.object.bake(type='NORMAL',margin=12,use_clear=True)
    image.filepath_raw=str(OUT/(label+'.png'));image.file_format='PNG';image.save()
    for m in obj.data.materials:
        n=m.node_tree.nodes;l=m.node_tree.links;p=n.get('Principled BSDF')
        tex=n.new('ShaderNodeTexImage');tex.image=image
        nm=n.new('ShaderNodeNormalMap');l.new(tex.outputs['Color'],nm.inputs['Color']);l.new(nm.outputs['Normal'],p.inputs['Normal'])

def finish_workwear():
    obj=bpy.data.objects['Workwear']
    m=B.material('Olive_Canvas',(.105,.135,.075),.82)
    n=m.node_tree.nodes;links=m.node_tree.links;p=n.get('Principled BSDF')
    noise=n.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=180;noise.inputs['Detail'].default_value=2
    bump=n.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.18;bump.inputs['Distance'].default_value=.00035
    links.new(noise.outputs['Fac'],bump.inputs['Height']);links.new(bump.outputs['Normal'],p.inputs['Normal'])
    obj.data.materials.append(m);idx=len(obj.data.materials)-1
    # The supplied outfit has disconnected shirt and jeans. Assign the entire shirt island.
    adjacency={v.index:set() for v in obj.data.vertices}
    for e in obj.data.edges:
        a,b=e.vertices;adjacency[a].add(b);adjacency[b].add(a)
    remaining=set(adjacency);shirt=set()
    while remaining:
        seed=remaining.pop();group={seed};queue=[seed]
        while queue:
            for j in adjacency[queue.pop()]:
                if j in remaining:remaining.remove(j);group.add(j);queue.append(j)
        if sum(obj.data.vertices[j].co.z for j in group)/len(group)>1.1:shirt.update(group)
    for p in obj.data.polygons:
        if p.vertices[0] in shirt:p.material_index=idx

def add_prop_bones(rig):
    H.select_only([rig]);bpy.ops.object.mode_set(mode='EDIT')
    for key in ('rock','hatchet','torch'):
        b=rig.data.edit_bones.new('prop_'+key);b.head=(0,0,0);b.tail=(0,0,.1)
        # A real hand parent keeps the attachment rigid between sampled keys too.
        b.parent=rig.data.edit_bones['hand_r'];b.use_deform=True
    bpy.ops.object.mode_set(mode='OBJECT')

def landmark(rig):
    bones=rig.data.bones
    height=bones['head'].tail_local.z+.035
    return {'hip_z':bones['pelvis'].head_local.z,'shoulder_z':bones['upperarm_l'].head_local.z,
            'ankle_l':tuple(bones['foot_l'].head_local),'ankle_r':tuple(bones['foot_r'].head_local),
            'height':height,'scale':height/1.8,
            'rest_heads':{b.name:tuple(b.head_local) for b in bones}}

def crop_arms(obj):
    groups={g.index:g.name for g in obj.vertex_groups}
    keep={v.index for v in obj.data.vertices if sum(g.weight for g in v.groups if groups[g.group].startswith(('upperarm','lowerarm','hand','thumb','index','middle','ring','pinky')))>.35}
    bm=bmesh.new();bm.from_mesh(obj.data);bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm,geom=[v for v in bm.verts if v.index not in keep],context='VERTS');bm.to_mesh(obj.data);bm.free()

def apply_globals(rig,model,g,prop,frame):
    local=model.local(g)
    for name,(q,loc) in local.items():
        p=rig.pose.bones[name];p.rotation_mode='QUATERNION'
        if frame>1 and q.dot(p.rotation_quaternion)<0:q.negate()
        p.rotation_quaternion=q;p.location=loc
        p.scale=(1,1,1) if not name.startswith('prop_') or name=='prop_'+str(prop) else (.00001,)*3
        p.keyframe_insert(data_path='location',frame=frame,group=name)
        p.keyframe_insert(data_path='rotation_quaternion',frame=frame,group=name)
        p.keyframe_insert(data_path='scale',frame=frame,group=name)

def global_pose(model,name,t,lm,fp):
    pose=F.sample(name,t,lm) if fp else M.sample(name,t,lm)
    g=model.evaluate(pose)
    if name in ('hatchet_chop','torch_equip'):
        # Keep the individually fitted curls/opposition through the entire action.
        for bone in model.order:
            if bone in PROFILE['finger_quaternions']:
                parent=model.parent[bone]
                local=model.rest[parent].inverted()@model.rest[bone]
                g[bone]=g[parent]@local@Quaternion(PROFILE['finger_quaternions'][bone]).to_matrix().to_4x4()
    # Object frame is explicitly anatomical; it is independent of the imported bone's roll.
    hand='hand_r';f0,n0,_=model.hand_frames['r'];delta=g[hand].to_3x3()@model.rest[hand].to_3x3().inverted()
    f=(delta@f0).normalized();normal=(delta@n0).normalized();up=f.cross(normal).normalized()
    origin=g[hand].translation+f*(.065*lm['scale'])+normal*(.035*lm['scale'])
    attachment=Matrix(PROFILE['hand_to_hatchet'])
    attachment.translation*=lm['scale']/PROFILE['scale']
    mat=g[hand]@attachment
    for key in ('rock','hatchet','torch'):
        g['prop_'+key]=mat.copy()
    if fp:
        # On the FPS grip the blade follows the metacarpals. Turning the prop
        # around its shaft keeps the wrist neutral instead of bending the hand
        # sideways to compensate for the calibrated TPS attachment's yaw.
        g['prop_hatchet'] = mat @ Matrix.Rotation(math.radians(77.08), 4, 'Z')
    rock=Matrix((normal,f.cross(normal).normalized(),f)).transposed().to_4x4()
    rock.translation=origin+normal*(.082*lm['scale']);g['prop_rock']=rock
    return pose,g

def bake(rig,fp=False):
    model=A.Rig(rig);lm=landmark(rig);rig.animation_data_create()
    actions=[];diag=[]
    for clip in M.CLIPS:
        name=clip['name'];act=bpy.data.actions.new(('FPS_' if fp else 'TPS_')+name);act.use_fake_user=True
        rig.animation_data.action=act
        count=round(clip['duration']*30)
        max_error=0.;min_elbow=999.;last_g=None;max_step=0.
        for i in range(count+1):
            pose,g=global_pose(model,name,i/30,lm,fp)
            for end,spec in pose.get('ik',{}).items():
                if end.startswith('hand'):max_error=max(max_error,(g[end].translation-Vector(spec['at'])).length)
            if last_g:
                for bone in ('lowerarm_l','lowerarm_r','calf_l','calf_r'):
                    max_step=max(max_step,(g[bone].translation-last_g[bone].translation).length)
            apply_globals(rig,model,g,clip['prop'],i+1);last_g=g
        # Every sample has a concrete pose. Linear interpolation preserves sampled contact trajectories.
        for layer in act.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for fc in bag.fcurves:
                        for k in fc.keyframe_points:k.interpolation='LINEAR'
        actions.append(act)
        diag.append({'clip':name,'frames':count+1,'max_hand_target_error_m':max_error,'max_joint_step_m':max_step})
    rig.animation_data.action=None
    for act in actions:
        track=rig.animation_data.nla_tracks.new();track.name=act.name
        strip=track.strips.new(act.name,1,act);track.mute=True
    (OUT/('fps-ik.json' if fp else 'tps-ik.json')).write_text(json.dumps(diag,indent=2))
    return actions

def export(rig,meshes,path):
    H.select_only([rig]+meshes,rig)
    bpy.context.scene.frame_set(1)
    bpy.ops.export_scene.gltf(filepath=str(path),export_format='GLB',use_selection=True,use_active_scene=True,
        export_animations=True,export_animation_mode='ACTIONS',export_anim_single_armature=False,
        export_force_sampling=True,export_frame_range=False,export_optimize_animation_size=False,
        export_skins=True,export_morph=False,export_yup=True,export_apply=False)

def composite_review(rig,actions):
    rig.animation_data.action=None
    # Eight sequential clips, with hold time between actions, remain easy to scrub in Blender.
    alltrack=rig.animation_data.nla_tracks.new();alltrack.name='REVIEW — eight clips'
    start=1
    for act,clip in zip(actions,M.CLIPS):
        strip=alltrack.strips.new(clip['name'],start,act);strip.extrapolation='HOLD_FORWARD'
        bpy.context.scene.timeline_markers.new(clip['name'],frame=start)
        start+=int(act.frame_range[1]-act.frame_range[0])+16
    bpy.context.scene.frame_start=1;bpy.context.scene.frame_end=start-16

def main(open_source=True):
    if open_source:
        bpy.ops.wm.open_mainfile(filepath=str(SOURCE/'source-body.blend'))
    finish_workwear()
    reuse_surface_normal(bpy.data.objects['Survivor_Body'],'survivor-skin-normal')
    reuse_surface_normal(bpy.data.objects['Workwear'],'survivor-workwear-normal')
    scene=bpy.context.scene;scene.name='TPS — Survivor audition';scene.render.fps=30
    rig=bpy.data.objects['Survivor_TPS_Rig'];add_prop_bones(rig)
    meshes=[o for o in scene.objects if o.type=='MESH' and any(m.type=='ARMATURE' and m.object==rig for m in o.modifiers)]
    prop_meshes=props.build_props(rig,landmark(rig)['scale']);meshes+=prop_meshes
    # Duplicate geometry before authoring; FPS retains the same hand topology and bind matrices.
    fps_rig=rig.copy();fps_rig.data=rig.data.copy();fps_rig.name='Survivor_FPS_Rig'
    fps_scene=bpy.data.scenes.new('FPS — Arms audition');fps_scene.world=scene.world.copy()
    fps_scene.render.fps=30;fps_scene.collection.objects.link(fps_rig)
    # Viewmodel mounting offset: keep the authored elbow geometry and all held
    # props together while placing the separate arms in the review camera.
    fps_rig.location=(0.,-.08,.20)
    fps_meshes=[]
    for obj in meshes:
        if obj.name not in ('Survivor_Body','Workwear') and obj not in prop_meshes:continue
        copy=obj.copy();copy.data=obj.data.copy();copy.name='FPS_'+obj.name
        fps_scene.collection.objects.link(copy);copy.parent=fps_rig
        for m in copy.modifiers:
            if m.type=='ARMATURE':m.object=fps_rig
        if obj not in prop_meshes:crop_arms(copy)
        if len(copy.data.vertices):fps_meshes.append(copy)
        else:bpy.data.objects.remove(copy,do_unlink=True)
    for obj in list(scene.objects):
        if obj.type in ('LIGHT','CAMERA') or obj.name=='StudioFloor':
            o=obj.copy();o.data=obj.data.copy();fps_scene.collection.objects.link(o)
            if obj.name=='FPS_review':
                fps_scene.camera=o;o.data.lens=22
                o.rotation_euler=Vector((0,-1,-.21)).to_track_quat('-Z','Y').to_euler()
    tps_actions=bake(rig);export(rig,meshes,OUT/'survivor-tps.glb')
    composite_review(rig,tps_actions)
    bpy.context.window.scene=fps_scene
    fps_actions=bake(fps_rig,True);export(fps_rig,fps_meshes,OUT/'survivor-fps.glb');composite_review(fps_rig,fps_actions)
    # Park each scene at its own rock grip for immediate review.
    for sc in (scene,fps_scene):
        sc.render.engine='CYCLES';sc.cycles.samples=24;sc.cycles.use_denoising=True
        sc.render.resolution_x=1280;sc.render.resolution_y=900;sc.render.resolution_percentage=100
        sc.view_settings.view_transform='AgX';sc.frame_set(1)
    bpy.context.window.scene=scene
    scene.camera=bpy.data.objects['TPS_review']
    scene.camera.data.lens=55
    # Leave headroom for the actual jump and overhead tool anticipation.
    target=Vector((0.,0.,1.08))
    scene.camera.location=target+(scene.camera.location-Vector((0.,0.,.96)))*1.13
    scene.camera.rotation_euler=(target-scene.camera.location).to_track_quat('-Z','Y').to_euler()
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Survivor-Audition.blend'),compress=True)
    (OUT/'clips.json').write_text(json.dumps(M.CLIPS,indent=2))
    fps_clips=json.loads(json.dumps(M.CLIPS))
    for clip in fps_clips:
        clip['fps_camera']={'required_hands':['l','r'],'visible_from_seconds':.25 if clip['name']=='torch_equip' else 0.,'visible_until_seconds':clip['duration']}
    (OUT/'fps-clips.json').write_text(json.dumps(fps_clips,indent=2))
    shutil.copy2(Path(__file__).parent/'grip_profile.json',OUT/'grip_profile.json')
    print('TRIAL_ANIMATION_COMPLETE',flush=True)

if __name__=='__main__':main()
