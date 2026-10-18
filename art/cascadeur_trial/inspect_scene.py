"""Read live scene state, persist an inspection report, and print a summary."""
import json
from collections import Counter
from pathlib import Path

out = Path("D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur")
mv = scene.model_viewer()
bv = mv.behaviour_viewer()
dv = mv.data_viewer()
frame = scene.get_current_frame()
joints = []
for joint in bv.get_behaviours("Joint"):
    owner = bv.get_behaviour_owner(joint)
    transform = bv.get_behaviour_by_name(owner, "Transform")
    position = dv.get_data_value(bv.get_behaviour_data(transform, "global_position"), frame)
    basic = bv.get_behaviour_by_name(owner, "Basic")
    parent = bv.get_behaviour_object(basic, "parent")
    joints.append({"name": mv.get_object_name(owner),
                   "parent": None if parent.is_null() else mv.get_object_name(parent),
                   "position_cm": [float(x) for x in position.flatten()]})
types = Counter(mv.get_object_type_name(o) for o in mv.get_objects())
behaviours = Counter(bv.get_behaviour_name(b) for o in mv.get_objects()
                     for b in bv.get_behaviours(o))
report = {"export_available": app.is_export_available(),
          "pro_features_available": app.is_pro_features_available(),
          "frame": frame, "path": app.current_scene().get_path_name(),
          "tabs": [{"name": s.name(), "path": s.get_path_name()} for s in app.get_scene_manager().scenes()],
          "objects": len(mv.get_objects()), "object_types": dict(types),
          "behaviours": dict(behaviours), "joints": joints}
(out / "scene-inspection-latest.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps({k: v for k, v in report.items() if k != "joints"}))
