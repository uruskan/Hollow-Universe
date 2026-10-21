"""Author the proposed fresh TPS chop through Cascadeur's native rig update.

The baseline file is never overwritten. This saves a new unaccepted candidate
only after every joint target has been evaluated by Cascadeur and compared.
"""
from pathlib import Path
import json
import numpy as np

root = Path("D:/GameDev/Projects/HollowUniverseABTestB")
out = root / ".artifacts/character-trial-cascadeur"
target = out / "Survivor-TPS-04-candidate-chop.casc"
assert not target.exists()
assert app.current_scene().get_path_name().endswith("Survivor-TPS-03-baseline-chop.casc")
assert scene.data_viewer().get_animation_size() == 43
source = root / "art/cascadeur_trial/pose_plan.py"
namespace = {"__name__": "cascadeur_pose_plan"}
exec(compile(source.read_text(encoding="utf-8"), str(source), "exec"), namespace)
poses = namespace["build_poses"](
    json.loads((out / "skeleton-rest.json").read_text(encoding="utf-8")),
    json.loads((out / "baseline-animation-transforms.json").read_text(encoding="utf-8")))
assert len(poses) == 43
mv = scene.model_viewer(); bv = mv.behaviour_viewer(); dv = mv.data_viewer()
joints = {mv.get_object_name(bv.get_behaviour_owner(b)): bv.get_behaviour_owner(b)
          for b in bv.get_behaviours("Joint")}
assert len(joints) == 54
for pose in poses:
    assert set(pose) == set(joints)
    for item in pose.values():
        position=np.asarray(item["position"]);rotation=np.asarray(item["rotation"])
        assert position.shape == (3,) and rotation.shape == (3,3)
        assert np.isfinite(position).all() and np.isfinite(rotation).all()
        assert abs(np.linalg.det(rotation)-1.0) < 0.001
        assert np.max(np.abs(rotation.T @ rotation - np.eye(3))) < 0.001
fields = {name: {key: bv.get_behaviour_data(bv.get_behaviour_by_name(obj, "Transform"), key)
                 for key in ("global_position", "global_rotation")}
          for name,obj in joints.items()}

def modify(model, update, updater):
    de = model.data_editor(); le = model.layers_editor()
    layers = scene.layers_viewer().layer_ids_by_obj_ids(list(joints.values()))
    for frame, pose in enumerate(poses):
        actuals = set()
        for layer in layers:
            le.set_fixed_interpolation_or_key_if_need(layer, frame, True)
        for name, item in pose.items():
            pos_id=fields[name]["global_position"];rot_id=fields[name]["global_rotation"]
            de.set_data_value(pos_id,frame,np.asarray(item["position"],dtype=np.float32))
            de.set_data_value(rot_id,frame,csc.math.Rotation.from_rotation_matrix(
                np.asfortranarray(item["rotation"],dtype=np.float32)))
            actuals.update((pos_id,rot_id))
        model.set_fixed_interpolation_if_need(actuals, frame)
        updater.run_update(actuals,frame)
    le.normalize_sections(scene)

ok=scene.modify_update("Cascadeur trial: fresh TPS hatchet block",modify)
print("Fresh motion callback:",ok)
if not ok:
    raise RuntimeError("Animation modification failed; inspect event log")
max_position_error=0.0; max_rotation_error=0.0
for frame,pose in enumerate(poses):
    for name,item in pose.items():
        pos=dv.get_data_value(fields[name]["global_position"],frame)
        rot=dv.get_data_value(fields[name]["global_rotation"],frame).to_rotation_matrix()
        max_position_error=max(max_position_error,float(np.linalg.norm(pos-np.asarray(item["position"]))))
        max_rotation_error=max(max_rotation_error,float(np.max(np.abs(rot-np.asarray(item["rotation"])))))
report={"callback_ok":ok,"frames":43,"joints":54,
        "maximum_target_position_error_cm":max_position_error,
        "maximum_target_rotation_matrix_error":max_rotation_error,
        "visual_acceptance":False,"method":"Original FK/IK blocking applied through native Cascadeur rig updates; no AutoPhysics claim"}
(out / "candidate-authoring-validation.json").write_text(json.dumps(report,indent=2)+"\n",encoding="utf-8")
(out / "candidate-requested-poses.json").write_text(json.dumps(poses)+"\n",encoding="utf-8")
print(json.dumps(report))
assert max_position_error < 0.1, "Native solve changed authored pose; inspect before saving"
assert max_rotation_error < 0.01, "Native solve changed authored rotation; inspect before saving"
scene.set_current_frame(18)
app.current_scene().save(str(target))
