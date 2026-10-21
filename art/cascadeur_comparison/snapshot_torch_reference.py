"""Record native controls and inspect affected layer ownership."""
import json
from pathlib import Path
out=Path('D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur-v3-comparison')
assert app.current_scene().get_path_name().endswith('FPS-03-torch-v3-reference.casc')
mv=scene.model_viewer();bv=mv.behaviour_viewer();dv=mv.data_viewer()
joints={mv.get_object_name(bv.get_behaviour_owner(b)):bv.get_behaviour_owner(b) for b in bv.get_behaviours('Joint')}
fields={n:{k:bv.get_behaviour_data(bv.get_behaviour_by_name(o,'Transform'),k) for k in ('global_position','global_rotation','local_position','local_rotation')} for n,o in joints.items()}
points={mv.get_object_name(bv.get_behaviour_owner(b)):bv.get_behaviour_owner(b) for b in bv.get_behaviours('Point')}
point_fields={n:bv.get_behaviour_data(bv.get_behaviour_by_name(o,'Transform'),'global_position') for n,o in points.items()}
frames={str(f):{n:{k:(dv.get_data_value(d,f).to_rotation_matrix().tolist() if 'rotation' in k else dv.get_data_value(d,f).tolist()) for k,d in fs.items()} for n,fs in fields.items()} for f in range(dv.get_animation_size())}
point_frames={str(f):{n:dv.get_data_value(d,f).tolist() for n,d in point_fields.items()} for f in range(dv.get_animation_size())}
(out/'torch-native-reference-transforms.json').write_text(json.dumps({'joints':frames,'points':point_frames},indent=2))
objects={**joints,**points};lv=scene.layers_viewer()
print(json.dumps({n:{'layer':lv.layer_id_by_obj_id(objects[n]).to_string(),'keys':list(lv.layer(lv.layer_id_by_obj_id(objects[n])).key_frame_indices())} for n in ['hand_r','prop_torch','middle_01_r','hand_MainPoint_r','lowerarm_MainPoint_r','upperarm_MainPoint_r','hand_MainPoint_l']}))
