"""Measure a native point-controller solve in an isolated disposable scene."""
import json
from pathlib import Path
import numpy as np
out=Path('D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur-v3-comparison')
target=out/'FPS-02-hand-controller-probe.casc'
assert not target.exists()
assert app.current_scene().get_path_name().endswith('FPS-01-hatchet-v3-reference.casc')
app.current_scene().save(str(target))
mv=scene.model_viewer();bv=mv.behaviour_viewer();dv=mv.data_viewer()
objects={mv.get_object_name(o):o for o in mv.get_objects()}
joints={mv.get_object_name(bv.get_behaviour_owner(b)):bv.get_behaviour_owner(b) for b in bv.get_behaviours('Joint')}
fields={n:{k:bv.get_behaviour_data(bv.get_behaviour_by_name(o,'Transform'),k) for k in ('global_position','global_rotation','local_position','local_rotation')} for n,o in joints.items()}
def snapshot(frame):
    return {n:{k:(dv.get_data_value(d,frame).to_rotation_matrix().tolist() if 'rotation' in k else dv.get_data_value(d,frame).tolist()) for k,d in fs.items()} for n,fs in fields.items()}
frames={str(f):snapshot(f) for f in range(dv.get_animation_size())}
points={mv.get_object_name(bv.get_behaviour_owner(b)):bv.get_behaviour_owner(b) for b in bv.get_behaviours('Point')}
point_fields={n:bv.get_behaviour_data(bv.get_behaviour_by_name(o,'Transform'),'global_position') for n,o in points.items()}
point_frames={str(f):{n:dv.get_data_value(d,f).tolist() for n,d in point_fields.items()} for f in range(dv.get_animation_size())}
(out/'hatchet-native-reference-transforms.json').write_text(json.dumps({'joints':frames,'points':point_frames},indent=2))
frame=18
before=snapshot(frame)
names=['hand_MainPoint_r','hand_DirectionPoint_r','hand_AdditionalPoint_r']
requested={n:np.array(point_frames[str(frame)][n])+np.array([0.,0.,3.]) for n in names}
def modify(model,update,updater,session):
    session.set_current_frame(frame)
    actuals=set()
    for n in names:
        layer=scene.layers_viewer().layer_id_by_obj_id(points[n])
        model.layers_editor().set_fixed_interpolation_or_key_if_need(layer,frame,True)
        node=update.get_object_by_id(points[n]).root_group().node_deep('Position')
        node.set_value(np.asarray(requested[n],dtype=np.float32),frame)
        actuals.add(node.data_id())
    updater.run_update(actuals,frame)
assert scene.modify_update_with_session('Probe hand point controls',modify)
after=snapshot(frame)
report={'frame':frame,'before':before,'after':after,'point_error_cm':{n:float(np.linalg.norm(np.asarray(dv.get_data_value(point_fields[n],frame))-requested[n])) for n in names},'joint_position_delta_cm':{n:(np.array(after[n]['global_position'])-np.array(before[n]['global_position'])).tolist() for n in before},'local_rotation_difference':{n:float(np.max(np.abs(np.array(after[n]['local_rotation'])-np.array(before[n]['local_rotation'])))) for n in before}}
(out/'hand-controller-probe.json').write_text(json.dumps(report,indent=2))
app.current_scene().save(str(target))
print(json.dumps({k:v for k,v in report.items() if k not in ('before','after')}))
