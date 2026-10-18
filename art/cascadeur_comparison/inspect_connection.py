"""Read live scene/edition facts without modifying any Cascadeur scene."""
import json
from pathlib import Path

out = Path('D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur-v3-comparison')
mv = scene.model_viewer()
bv = mv.behaviour_viewer()
report = {
    'export_available': bool(app.is_export_available()),
    'pro_features_available': bool(app.is_pro_features_available()),
    'current_path': app.current_scene().get_path_name(),
    'frame': scene.get_current_frame(),
    'animation_size': scene.data_viewer().get_animation_size(),
    'tabs': [{'name': s.name(), 'path': s.get_path_name()} for s in app.get_scene_manager().scenes()],
    'objects': len(mv.get_objects()),
    'joints': [mv.get_object_name(bv.get_behaviour_owner(b)) for b in bv.get_behaviours('Joint')],
    'native_tools': [t.name() if hasattr(t, 'name') else type(t).__name__
                     for t in app.get_tools_manager().tools()],
}
(out / 'connection-inspection.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report))
