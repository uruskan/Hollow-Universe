"""Read transforms and generated-control relationships, without pose edits."""
from pathlib import Path
import json

out = Path("D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur")
mv = scene.model_viewer(); bv = mv.behaviour_viewer(); dv = mv.data_viewer()
def serialize(v):
    if hasattr(v, "tolist"):
        return v.tolist()
    if isinstance(v, csc.math.Rotation):
        return v.to_rotation_matrix().tolist()
    return str(v)
joints = {}
for b in bv.get_behaviours("Joint"):
    o = bv.get_behaviour_owner(b); t = bv.get_behaviour_by_name(o, "Transform")
    joints[mv.get_object_name(o)] = {
        str(frame): {prop: serialize(dv.get_data_value(bv.get_behaviour_data(t, prop), frame))
                    for prop in ("local_position", "local_rotation", "global_position", "global_rotation")}
        for frame in (0, 6, 10, 14, 18, 22, 30, 42)}
controls = []
for b in bv.get_behaviours("RigAdditionalInfo"):
    row = {"box": mv.get_object_name(bv.get_behaviour_owner(b))}
    for prop in ("joint", "main_point", "direction_point", "additional_point", "limb_direction"):
        ref = bv.get_behaviour_reference(b, prop)
        row[prop] = None if ref.is_null() else mv.get_object_name(bv.get_behaviour_owner(ref))
    controls.append(row)
report={"path":app.current_scene().get_path_name(), "frames":dv.get_animation_size(),
        "joints":joints,"controls":controls}
(out / "baseline-animation-transforms.json").write_text(json.dumps(report,indent=2)+"\n", encoding="utf-8")
print("Animation frames:",report["frames"],"mapped control boxes:",len(controls))
