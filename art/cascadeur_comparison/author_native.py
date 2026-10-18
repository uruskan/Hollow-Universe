"""Author sparse native Cascadeur controller poses and validate the solved rig.

Runs only through the official app script server. Input performances and all
Blender sources are read-only. Candidate acceptance remains a visual review.
"""
from pathlib import Path
import json
import math
import numpy as np
from csc.view import camera_utils

OUT=Path('D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur-v3-comparison')
SCRIPTS=Path('D:/GameDev/Projects/HollowUniverseABTestB/art/cascadeur_comparison')

def author(clip, revision='A02'):
    assert clip in ('hatchet','torch')
    source=OUT/('FPS-01-hatchet-v3-reference.casc' if clip=='hatchet' else 'FPS-03-torch-v3-reference.casc')
    assert revision.isalnum()
    target=OUT/(('FPS-HatchetChop-Cascadeur-' if clip=='hatchet' else 'FPS-TorchEquip-Cascadeur-')+revision+'.casc')
    assert not target.exists(), 'Never overwrite an existing candidate'
    reference=json.loads((OUT/(clip+'-native-reference-transforms.json')).read_text())
    manifest=json.loads((OUT/'inputs/input-manifest.json').read_text())
    helpers={'__name__':'comparison_pose_targets'}
    exec(compile((SCRIPTS/'pose_targets.py').read_text(),str(SCRIPTS/'pose_targets.py'),'exec'),helpers)
    plan=helpers['build_'+clip+'_targets'](manifest,reference)
    planned={int(f):v for f,v in plan['keyframes'].items()}
    keep=sorted(planned)
    manager=app.get_scene_manager()
    view=manager.create_application_scene();manager.set_current_scene(view)
    csc.app.ProjectLoader.load_from(str(source),view.domain_scene())
    domain=view.domain_scene()
    mv=domain.model_viewer();bv=mv.behaviour_viewer();dv=mv.data_viewer()
    joints={mv.get_object_name(bv.get_behaviour_owner(b)):bv.get_behaviour_owner(b) for b in bv.get_behaviours('Joint')}
    points={mv.get_object_name(bv.get_behaviour_owner(b)):bv.get_behaviour_owner(b) for b in bv.get_behaviours('Point')}
    fields={n:{k:bv.get_behaviour_data(bv.get_behaviour_by_name(o,'Transform'),k) for k in ('global_position','global_rotation','local_position','local_rotation')} for n,o in joints.items()}
    point_fields={n:bv.get_behaviour_data(bv.get_behaviour_by_name(o,'Transform'),'global_position') for n,o in points.items()}
    lv=domain.layers_viewer()
    rig_layers=set(lv.layer_ids_by_obj_ids(list(joints.values())+list(points.values())))
    control_layers={lv.layer_id_by_obj_id(points[n]) for n in planned[keep[0]]}
    layers_before={str(l):list(lv.layer(l).key_frame_indices()) for l in rig_layers}
    count=dv.get_animation_size()
    assert keep[0]==0 and keep[-1]==count-1
    def edit(model,update,updater,session):
        le=model.layers_editor()
        for layer in rig_layers:
            for frame in list(lv.layer(layer).key_frame_indices()):
                if frame not in keep:
                    le.unset_section(frame,layer)
            for frame in keep:
                le.set_fixed_interpolation_or_key_if_need(layer,frame,True)
        for frame,pose in planned.items():
            session.set_current_frame(frame)
            actuals=set()
            for name,position in pose.items():
                node=update.get_object_by_id(points[name]).root_group().node_deep('Position')
                node.set_value(np.asarray(position,dtype=np.float32),frame)
                actuals.add(node.data_id())
            updater.run_update(actuals,frame)
        def bezier(section):
            section.interval.interpolation=csc.layers.layer.Interpolation.BEZIER
        def native_ik(section):
            bezier(section)
            section.interval.common.ik_fk=csc.layers.layer.IkFk.IK
            section.key.common.ik_fk=csc.layers.layer.IkFk.IK
        for layer in rig_layers:
            for frame in keep[:-1]:
                le.change_section(frame,layer,native_ik if layer in control_layers else bezier)
        le.normalize_sections(domain)
        interpolator=updater.get_interpolator();interpolator.reload();interpolator.interpolate()
    assert domain.modify_update_with_session('Author '+clip+' with sparse native point controls',edit)
    def sample(frame):
        return {n:{k:(dv.get_data_value(d,frame).to_rotation_matrix().tolist() if 'rotation' in k else dv.get_data_value(d,frame).tolist()) for k,d in fs.items()} for n,fs in fields.items()}
    solved={str(f):sample(f) for f in range(count)}
    bends=[];finger_errors=[];prop_errors=[];finite=True
    for f in range(count):
        row=solved[str(f)]
        p=lambda n:np.asarray(row[n]['global_position'])
        fore=p('hand_r')-p('lowerarm_r');hand=p('middle_01_r')-p('hand_r')
        bends.append(math.degrees(math.acos(float(np.clip(np.dot(fore,hand)/np.linalg.norm(fore)/np.linalg.norm(hand),-1,1)))))
        for n in joints:
            finite &= all(np.isfinite(np.asarray(value)).all() for value in row[n].values())
            if n.startswith(('thumb_','index_','middle_','ring_','pinky_')):
                finger_errors.append(float(np.max(np.abs(np.asarray(row[n]['local_rotation'])-reference['joints'][str(f)][n]['local_rotation']))))
            if n.startswith('prop_'):
                for k in ('local_position','local_rotation'):
                    prop_errors.append(float(np.max(np.abs(np.asarray(row[n][k])-reference['joints'][str(f)][n][k]))))
    key_errors={str(f):{n:float(np.linalg.norm(np.asarray(dv.get_data_value(point_fields[n],f))-position)) for n,position in pose.items()} for f,pose in planned.items()}
    camera=next(c for c in camera_utils.get_cameras(view) if c.name()=='FPS_review.001')
    camera_utils.set_camera_active(view.active_viewport(),camera)
    view.active_viewport().domain_viewport().set_mode_visualizers(csc.view.ViewportMode.Mesh)
    domain.set_current_frame(18 if clip=='hatchet' else 36)
    # Camera values are native data nodes; retain the actual v3 render aspect.
    cam_b=bv.get_behaviour_by_name(camera.id(),'Camera')
    def camera_edit(model,update,updater):
        actuals=set()
        for name,value in [('field_of_view',math.radians(59.822266)),('aspect_ratio_width',896.0),('aspect_ratio_height',630.0)]:
            data=bv.get_behaviour_data(cam_b,name)
            model.data_editor().set_data_value(data,0,value);actuals.add(data)
        updater.run_update(actuals,0)
    camera_ok=domain.modify_update('Match preserved FPS camera framing',camera_edit)
    report={'clip':clip,'revision':revision,'method':'Sparse native point-controller poses and native Bezier IK interpolation; no joint-transform rebake or AutoPhysics claim',
            'candidate':str(target),'frames':count,'fps':30,'keyframes':keep,'native_layer_count':len(rig_layers),
            'finite':bool(finite),'wrist_bend_degrees_by_frame':bends,
            'maximum_wrist_bend_degrees':max(bends),'maximum_key_control_error_cm':max(v for r in key_errors.values() for v in r.values()),
            'maximum_finger_local_rotation_matrix_change':max(finger_errors),
            'maximum_prop_local_transform_change':max(prop_errors),'key_control_errors_cm':key_errors,
            'camera_configured':bool(camera_ok),'visual_acceptance':False,'fbx_export_available':app.is_export_available(),
            'plan':plan['metadata']}
    (OUT/(clip+'-'+revision+'-validation.json')).write_text(json.dumps(report,indent=2))
    (OUT/(clip+'-'+revision+'-transforms.json')).write_text(json.dumps(solved))
    (OUT/(clip+'-'+revision+'-control-plan.json')).write_text(json.dumps(plan,indent=2))
    view.save(str(target))
    print(json.dumps({k:v for k,v in report.items() if k not in ('wrist_bend_degrees_by_frame','key_control_errors_cm','plan')}))
    assert finite,'Non-finite animation data'
    assert report['maximum_key_control_error_cm']<0.1,'Native solve altered target points'
    assert max(bends)<25,'Wrist bend requires revision'
    assert max(finger_errors)<0.02 and max(prop_errors)<0.02,'Grip requires revision'
