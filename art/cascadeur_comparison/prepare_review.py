"""Select a final candidate and prepare clean, editable FPS review framing."""
from pathlib import Path
from csc.view import camera_utils
view=app.current_scene()
assert Path(view.get_path_name()).name in ('FPS-HatchetChop-Cascadeur-A02.casc','FPS-TorchEquip-Cascadeur-A02.casc')
camera=next(c for c in camera_utils.get_cameras(view) if c.name()=='FPS_review.001')
camera_utils.set_camera_active(view.active_viewport(),camera)
view.active_viewport().domain_viewport().set_mode_visualizers(csc.view.ViewportMode.View)
view.save(view.get_path_name())
print('Review scene',view.get_path_name(),'frame',scene.get_current_frame())
