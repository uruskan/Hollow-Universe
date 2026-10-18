"""Native Cascadeur stages for isolated FPS weapon experiments.

Called through run_weapon_stage.py and the official application MCP server.
Every stage writes a new checkpoint; preserved Blender sources are read-only.
"""
from pathlib import Path
import json
import math
import numpy as np
from csc.view import camera_utils

OUT=Path('D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur-v3-comparison')
NATIVE=OUT/'weapon-native'
SCRIPTS=Path('D:/GameDev/Projects/HollowUniverseABTestB/art/cascadeur_comparison')
WEAPONS=('heavyaxe','pickaxe','pistol','rifle')

def paths(weapon):
    assert weapon in WEAPONS
    NATIVE.mkdir(exist_ok=True)
    return OUT/'weapon-inputs'/weapon, NATIVE/weapon

def open_bind(weapon):
    inputs,prefix=paths(weapon)
    assert not Path(str(prefix)+'-controls.casc').exists()
    manager=app.get_scene_manager()
    view=manager.create_application_scene();manager.set_current_scene(view)
    loader=app.get_tools_manager().get_tool('FbxSceneLoader').get_fbx_loader(view)
    loader.import_scene(str(inputs/'survivor-fps-bind.fbx'))
    print('Imported new weapon bind',weapon)

def rig_controls(weapon):
    import rig_mode.on as rig_on
    import rig_mode.off as rig_off
    from rig_mode.camera_context import Camera_context
    inputs,prefix=paths(weapon)
    target=Path(str(prefix)+'-controls.casc')
    assert not target.exists()
    view=app.current_scene();domain=view.domain_scene()
    mv=domain.model_viewer();bv=mv.behaviour_viewer();dv=mv.data_viewer()
    assert len(bv.get_behaviours('Joint'))>=56
    assert not bv.get_behaviours('RigInfo') and not bv.get_behaviours('AnimationInfo')
    def positions():
        return {mv.get_object_name(bv.get_behaviour_owner(b)):dv.get_data_value(
            bv.get_behaviour_data(bv.get_behaviour_by_name(bv.get_behaviour_owner(b),'Transform'),'global_position'),0).copy()
            for b in bv.get_behaviours('Joint')}
    before=positions()
    assert domain.modify_update_with_session('Weapons: enter rig mode',lambda m,u,r,s:
        rig_on.on(m,u,r,s,[0.,.5,0.],None,None,Camera_context(),Camera_context()))
    window=app.get_tools_manager().get_tool('RiggingToolWindowTool').editor(view)
    window.open_quick_rigging_tool()
    template=inputs/'Survivor-FPS.qrigcasc'
    window.load_template_by_fileName(str(template))
    loaded=json.loads(window.get_template_from_qrt())
    def mapped(doc):
        return {i['Bone name']:(i['Joint name'],i['Joint path']) for g in doc['Document']
            for s in g['Sections'] for i in s['Names'] if i.get('Joint name')}
    assert mapped(loaded)==mapped(json.loads(template.read_text(encoding='utf-8')))
    assert len(mapped(loaded))==51
    window.create_from_qrt_by_content(json.dumps(loaded))
    window.set_is_create_autoposing(False)
    assert domain.modify_update_with_session('Weapons: generate native controls',lambda m,u,r,s:
        rig_off.off(domain,m,u,r,s,True,False,Camera_context(),Camera_context()))
    after=positions()
    drift=max(float(np.linalg.norm(after[n]-v)) for n,v in before.items())
    assert drift<.1
    view.save(str(target))
    print(json.dumps({'weapon':weapon,'bind_drift_cm':drift,'joints':len(before),'path':str(target)}))

def import_ready(weapon,revision=''):
    inputs,prefix=paths(weapon)
    assert not revision or revision.isalnum()
    source=Path(str(prefix)+'-controls.casc');target=Path(str(prefix)+'-ready'+('-'+revision if revision else '')+'.casc')
    assert source.is_file() and not target.exists()
    manager=app.get_scene_manager();view=manager.create_application_scene();manager.set_current_scene(view)
    csc.app.ProjectLoader.load_from(str(source),view.domain_scene())
    loader=app.get_tools_manager().get_tool('FbxSceneLoader').get_fbx_loader(view)
    loader.import_animation(str(inputs/'survivor-fps-ready.fbx'),0)
    view.save(str(target))
    print('Saved weapon ready reference',target)

def snapshot_ready(weapon,revision=''):
    _,prefix=paths(weapon)
    assert not revision or revision.isalnum()
    suffix='-'+revision if revision else ''
    view=app.current_scene()
    source=Path(str(prefix)+'-ready'+suffix+'.casc')
    assert Path(view.get_path_name())==source
    domain=view.domain_scene();mv=domain.model_viewer();bv=mv.behaviour_viewer();dv=mv.data_viewer()
    joints={mv.get_object_name(bv.get_behaviour_owner(b)):bv.get_behaviour_owner(b) for b in bv.get_behaviours('Joint')}
    points={mv.get_object_name(bv.get_behaviour_owner(b)):bv.get_behaviour_owner(b) for b in bv.get_behaviours('Point')}
    fields={n:{k:bv.get_behaviour_data(bv.get_behaviour_by_name(o,'Transform'),k) for k in
        ('global_position','global_rotation','local_position','local_rotation')} for n,o in joints.items()}
    point_fields={n:bv.get_behaviour_data(bv.get_behaviour_by_name(o,'Transform'),'global_position') for n,o in points.items()}
    samples={str(f):{n:{k:(dv.get_data_value(d,f).to_rotation_matrix().tolist() if 'rotation' in k else
        dv.get_data_value(d,f).tolist()) for k,d in fs.items()} for n,fs in fields.items()} for f in range(dv.get_animation_size())}
    controls={str(f):{n:dv.get_data_value(d,f).tolist() for n,d in point_fields.items()} for f in range(dv.get_animation_size())}
    path=Path(str(prefix)+'-reference'+suffix+'.json')
    path.write_text(json.dumps({'joints':samples,'points':controls},indent=2))
    Path(str(prefix)+'-current-reference.json').write_text(json.dumps({'scene':str(source),'reference':str(path)}))
    print('Saved native weapon reference',path,'frames',len(samples))

def configure_camera(view):
    domain=view.domain_scene();bv=domain.model_viewer().behaviour_viewer()
    cameras=camera_utils.get_cameras(view)
    camera=next(c for c in cameras if c.name().startswith('FPS_review'))
    camera_utils.set_camera_active(view.active_viewport(),camera)
    cam=bv.get_behaviour_by_name(camera.id(),'Camera')
    def edit(model,update,updater):
        actuals=set()
        for name,value in [('field_of_view',math.radians(59.822266)),('aspect_ratio_width',896.),('aspect_ratio_height',630.)]:
            data=bv.get_behaviour_data(cam,name)
            model.data_editor().set_data_value(data,0,value);actuals.add(data)
        updater.run_update(actuals,0)
    assert domain.modify_update('Set FPS weapon review camera',edit)
    view.active_viewport().domain_viewport().set_mode_visualizers(csc.view.ViewportMode.View)

def author(weapon,clip,revision):
    inputs,prefix=paths(weapon)
    assert revision.isalnum()
    target=NATIVE/(clip+'-'+revision+'.casc')
    assert not target.exists(),'Never overwrite a candidate'
    manifest=json.loads((inputs/'weapon-manifest.json').read_text())
    (NATIVE/(clip+'-'+revision+'-source-manifest.json')).write_text(json.dumps(manifest,indent=2))
    pointer=Path(str(prefix)+'-current-reference.json')
    reference_paths=json.loads(pointer.read_text()) if pointer.exists() else {'reference':str(prefix)+'-reference.json','scene':str(prefix)+'-ready.casc'}
    reference=json.loads(Path(reference_paths['reference']).read_text())
    helper={'__name__':'weapon_pose_targets'}
    exec(compile((SCRIPTS/'weapons_pose_targets.py').read_text(),str(SCRIPTS/'weapons_pose_targets.py'),'exec'),helper)
    plan=helper['build_weapon_targets'](manifest,reference,clip)
    assert not plan['metadata']['requires_pose_revision'],'Revise wrist/elbow targets before native authoring'
    planned={int(f):v for f,v in plan['keyframes'].items()};keep=sorted(planned)
    manager=app.get_scene_manager();view=manager.create_application_scene();manager.set_current_scene(view)
    csc.app.ProjectLoader.load_from(reference_paths['scene'],view.domain_scene())
    domain=view.domain_scene();mv=domain.model_viewer();bv=mv.behaviour_viewer();dv=mv.data_viewer();lv=domain.layers_viewer()
    joints={mv.get_object_name(bv.get_behaviour_owner(b)):bv.get_behaviour_owner(b) for b in bv.get_behaviours('Joint')}
    points={mv.get_object_name(bv.get_behaviour_owner(b)):bv.get_behaviour_owner(b) for b in bv.get_behaviours('Point')}
    fields={n:{k:bv.get_behaviour_data(bv.get_behaviour_by_name(o,'Transform'),k) for k in
        ('global_position','global_rotation','local_position','local_rotation')} for n,o in joints.items()}
    point_fields={n:bv.get_behaviour_data(bv.get_behaviour_by_name(o,'Transform'),'global_position') for n,o in points.items()}
    part_names=list(plan['prop_keyframes'][0])
    def separate_parts(model,update,updater):
        le=model.layers_editor()
        for bone in part_names:
            basic=bv.get_behaviour_by_name(joints[bone],'Basic')
            assert bv.get_behaviour_object(basic,'parent')==joints[manifest['parts'][bone]['parent']]
            layer=le.create_layer('Weapon part '+bone,lv.root_id())
            model.move_objects_to_layer([joints[bone]],layer)
    if part_names:assert domain.modify_update('Give moving weapon parts separate FK layers',separate_parts)
    rig_layers=set(lv.layer_ids_by_obj_ids(list(joints.values())+list(points.values())))
    control_layers={lv.layer_id_by_obj_id(points[n]) for n in planned[keep[0]]}
    part_layers={lv.layer_id_by_obj_id(joints[n]) for n in part_names}
    assert not (control_layers & part_layers)
    def edit(model,update,updater,session):
        le=model.layers_editor()
        for layer in rig_layers:
            for frame in list(lv.layer(layer).key_frame_indices()):
                if frame not in keep:le.unset_section(frame,layer)
            for frame in keep:le.set_fixed_interpolation_or_key_if_need(layer,frame,True)
        model.fit_animation_size_by_layers()
        updater.generate_update()
        for frame,pose in planned.items():
            session.set_current_frame(frame);actuals=set()
            for name,position in pose.items():
                node=update.get_object_by_id(points[name]).root_group().node_deep('Position')
                node.set_value(np.asarray(position,dtype=np.float32),frame);actuals.add(node.data_id())
            local_keys={**plan['prop_keyframes'][frame],**plan['finger_keyframes'][frame]}
            for bone,values in local_keys.items():
                for name,value in values.items():
                    data=fields[bone][name]
                    native_value=(csc.math.Rotation.from_rotation_matrix(np.asarray(value,dtype=np.float32))
                                  if 'rotation' in name else np.asarray(value,dtype=np.float32))
                    model.data_editor().set_data_value(data,frame,native_value);actuals.add(data)
            updater.run_update(actuals,frame)
        # A held magazine/slide must not anticipate the following release.
        # Native clamped Bezier retains smooth motion without overshooting
        # equal hold keys or letting the grip start drifting off early.
        interpolation=(csc.layers.layer.Interpolation.CLAMPED_BEZIER if clip.endswith('_reload')
                       else csc.layers.layer.Interpolation.BEZIER)
        def bezier(section):section.interval.interpolation=interpolation
        def ik(section):
            bezier(section)
            section.interval.common.ik_fk=csc.layers.layer.IkFk.IK
            section.key.common.ik_fk=csc.layers.layer.IkFk.IK
        def fk(section):
            bezier(section)
            section.interval.common.ik_fk=csc.layers.layer.IkFk.FK
            section.key.common.ik_fk=csc.layers.layer.IkFk.FK
            section.interval.common.fixation=csc.layers.layer.Fixation.Free
        for layer in rig_layers:
            for frame in keep:
                le.change_section(frame,layer,ik if layer in control_layers else fk if layer in part_layers else bezier)
        le.normalize_sections(domain)
        interpolator=updater.get_interpolator();interpolator.reload();interpolator.interpolate()
    assert domain.modify_update_with_session('Author FPS '+clip+' with native controllers',edit)
    count=dv.get_animation_size();assert count==plan['end_frame']+1
    samples={str(f):{n:{k:(dv.get_data_value(d,f).to_rotation_matrix().tolist() if 'rotation' in k else
        dv.get_data_value(d,f).tolist()) for k,d in fs.items()} for n,fs in fields.items()} for f in range(count)}
    finite=all(np.isfinite(np.asarray(value)).all() for row in samples.values() for values in row.values() for value in values.values())
    bends={side:[] for side in ('r','l')}
    main_prop_error=0.;right_finger_error=0.
    for row in samples.values():
        for side in bends:
            p=lambda bone:np.asarray(row[bone+'_'+side]['global_position'])
            fore=p('hand')-p('lowerarm');hand=p('middle_01')-p('hand')
            bends[side].append(math.degrees(math.acos(float(np.clip(np.dot(fore,hand)/np.linalg.norm(fore)/np.linalg.norm(hand),-1,1)))))
        for key in ('local_position','local_rotation'):
            main_prop_error=max(main_prop_error,float(np.max(np.abs(np.asarray(row['prop_hatchet'][key])-reference['joints']['0']['prop_hatchet'][key]))))
        for name in joints:
            if name.endswith('_r') and name.startswith(('thumb_','index_','middle_','ring_','pinky_')):
                right_finger_error=max(right_finger_error,float(np.max(np.abs(np.asarray(row[name]['local_rotation'])-reference['joints']['0'][name]['local_rotation']))))
    key_error=max(float(np.linalg.norm(np.asarray(dv.get_data_value(point_fields[n],f))-p)) for f,pose in planned.items() for n,p in pose.items())
    axial=helper['audit_native_wrist_twist'](samples)
    configure_camera(view);domain.set_current_frame(0);view.save(str(target))
    report={'clip':clip,'revision':revision,'candidate':str(target),'frames':count,'fps':plan['fps'],'keys':keep,'reference_paths':reference_paths,
        'source_manifest_snapshot':str(NATIVE/(clip+'-'+revision+'-source-manifest.json')),
        'native_interpolation':'CLAMPED_BEZIER' if clip.endswith('_reload') else 'BEZIER',
        'wrist_axial_change_from_ready':axial,
        'maximum_wrist_axial_change_degrees':{s:v['maximum_degrees'] for s,v in axial.items()},
        'finite':bool(finite),'maximum_key_control_error_cm':key_error,'wrist_bend_degrees':bends,
        'maximum_wrist_bend_degrees':{s:max(v) for s,v in bends.items()},'main_prop_local_error':main_prop_error,
        'right_finger_local_error':right_finger_error,'visual_acceptance':False,'fbx_export_available':app.is_export_available()}
    (NATIVE/(clip+'-'+revision+'-validation.json')).write_text(json.dumps(report,indent=2))
    (NATIVE/(clip+'-'+revision+'-transforms.json')).write_text(json.dumps(samples))
    (NATIVE/(clip+'-'+revision+'-plan.json')).write_text(json.dumps(plan,indent=2))
    print(json.dumps({k:v for k,v in report.items() if k not in ('wrist_bend_degrees','wrist_axial_change_from_ready')}))
    assert finite and key_error<.1,'Native control solve needs revision'
    assert max(max(v) for v in bends.values())<30,'Interpolated wrist needs revision'
    assert max(v['maximum_degrees'] for v in axial.values())<75,'Axial wrist twist needs revision'
    assert main_prop_error<.02 and right_finger_error<.02,'Main grip needs revision'

def capture(weapon,clip,revision):
    """Serial asynchronous native captures; request completion is not capture success."""
    import time
    from PySide6.QtCore import QTimer
    from PySide6.QtGui import QGuiApplication,QWindow
    paths(weapon)
    view=app.current_scene();domain=view.domain_scene()
    assert Path(view.get_path_name())==NATIVE/(clip+'-'+revision+'.casc')
    stem=clip+'_'+revision+'_frames'
    folder=OUT/'candidates/fps'/stem
    take_number=1
    while folder.exists():
        take_number+=1;folder=OUT/'candidates/fps'/(stem+'_take'+str(take_number))
    folder.mkdir(parents=True)
    state_path=NATIVE/(clip+'-'+revision+'-capture.json')
    params=csc.tools.RenderParameters();params.width=896;params.height=630;params.samples=2
    renderer=next(t for t in app.get_tools_manager().tools() if isinstance(t,csc.tools.RenderToFile))
    configure_camera(view)
    for window in QGuiApplication.allWindows():
        if window.title().endswith(' - Cascadeur'):
            if window.visibility()==QWindow.Visibility.Minimized:window.showMaximized()
            window.raise_();window.requestActivate();window.requestUpdate()
    state={'clip':clip,'revision':revision,'frames':domain.data_viewer().get_animation_size(),'completed':0,
           'status':'running','method':'Serial native Cascadeur viewport stills','resolution':[896,630],
           'frame_folder':str(folder),'take':take_number,'viewport_modes':[]}
    def persist():state_path.write_text(json.dumps(state,indent=2))
    def fail(exc):state['status']='failed';state['error']=str(exc);persist()
    def request_frame():
        try:
            domain.set_current_frame(state['completed'])
            view.active_viewport().domain_viewport().set_mode_visualizers(csc.view.ViewportMode.View)
            QTimer.singleShot(150,take)
        except Exception as exc:fail(exc)
    def take():
        try:
            state['current_path']=str(folder/('%04d.png'%state['completed']));state['request_time']=time.monotonic()
            viewport=view.active_viewport().domain_viewport()
            viewport.set_mode_visualizers(csc.view.ViewportMode.View)
            state['viewport_modes'].append(str(viewport.mode_visualizers()))
            renderer.take_image(view,params,state['current_path']);QTimer.singleShot(150,poll)
        except Exception as exc:fail(exc)
    def poll():
        try:
            path=Path(state['current_path'])
            if path.is_file() and path.stat().st_size>100:
                with path.open('rb') as stream:stream.seek(-12,2);tail=stream.read()
                if tail[4:8]==b'IEND':
                    state['completed']+=1;persist()
                    if state['completed']==state['frames']:
                        state['status']='complete';persist();domain.set_current_frame(0)
                        print('Native capture complete',clip)
                    else:QTimer.singleShot(50,request_frame)
                    return
            if time.monotonic()-state['request_time']>8:raise RuntimeError('Native still did not complete within 8 seconds')
            QTimer.singleShot(100,poll)
        except Exception as exc:fail(exc)
    persist();QTimer.singleShot(300,request_frame)
    print('Serial native capture started',clip,state['frames'])

def finish(weapon,clip,revision):
    """Check saved animation persistence and close saved temporary weapon tabs."""
    paths(weapon);manager=app.get_scene_manager();target=NATIVE/(clip+'-'+revision+'.casc')
    view=app.current_scene();assert Path(view.get_path_name())==target
    csc.app.ProjectLoader.load_from(str(target),view.domain_scene())
    domain=view.domain_scene();mv=domain.model_viewer();bv=mv.behaviour_viewer();dv=mv.data_viewer()
    expected=json.loads((NATIVE/(clip+'-'+revision+'-transforms.json')).read_text())
    maximum=0.;maximum_rot=0.
    joints={mv.get_object_name(bv.get_behaviour_owner(b)):bv.get_behaviour_owner(b) for b in bv.get_behaviours('Joint')}
    for n,o in joints.items():
        transform=bv.get_behaviour_by_name(o,'Transform')
        pos=bv.get_behaviour_data(transform,'global_position');rot=bv.get_behaviour_data(transform,'global_rotation')
        for frame,row in expected.items():
            maximum=max(maximum,float(np.linalg.norm(np.asarray(dv.get_data_value(pos,int(frame)))-row[n]['global_position'])))
            maximum_rot=max(maximum_rot,float(np.max(np.abs(dv.get_data_value(rot,int(frame)).to_rotation_matrix()-np.asarray(row[n]['global_rotation'])))))
    assert maximum<.02 and maximum_rot<.002,'Saved candidate changed during reload'
    configure_camera(view);domain.set_current_frame(0);view.save(str(target))
    closed=[]
    for other in list(manager.scenes()):
        path=Path(other.get_path_name())
        if other!=view and path.parent==NATIVE and path.is_file():
            manager.remove_application_scene(other);closed.append(path.name)
    result={'clip':clip,'revision':revision,'frames':len(expected),'reload_position_error_cm':maximum,
            'reload_rotation_matrix_error':maximum_rot,'passed':True,'closed_saved_weapon_tabs':closed}
    (NATIVE/(clip+'-'+revision+'-reload.json')).write_text(json.dumps(result,indent=2))
    print(json.dumps(result))
