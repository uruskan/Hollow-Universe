"""Select the completed isolated hatchet candidate for inspection/capture."""
from pathlib import Path
from csc.view import camera_utils
manager=app.get_scene_manager()
view=next(v for v in manager.scenes() if Path(v.get_path_name()).name=='FPS-HatchetChop-Cascadeur-A02.casc')
manager.set_current_scene(view)
camera=next(c for c in camera_utils.get_cameras(view) if c.name()=='FPS_review.001')
camera_utils.set_camera_active(view.active_viewport(),camera)
view.active_viewport().domain_viewport().set_mode_visualizers(csc.view.ViewportMode.View)
view.domain_scene().set_current_frame(0)
view.save(view.get_path_name())
print('Hatchet candidate selected')
