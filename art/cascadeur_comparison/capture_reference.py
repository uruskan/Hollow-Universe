"""Check v3 FPS camera and imported grip before native edits."""
from pathlib import Path
import json
from csc.view import camera_utils
out=Path('D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur-v3-comparison')
assert app.current_scene().get_path_name().endswith('FPS-01-hatchet-v3-reference.casc')
scene.set_current_frame(18)
view=app.current_scene()
camera=next(c for c in camera_utils.get_cameras(view) if c.name()=='FPS_review.001')
camera_utils.set_camera_active(view.active_viewport(),camera)
assert camera_utils.is_camera_active(view.active_viewport(),camera)
view.active_viewport().domain_viewport().set_mode_visualizers(csc.view.ViewportMode.View)
bv=scene.behaviour_viewer();dv=scene.data_viewer()
cam=bv.get_behaviour_by_name(camera.id(),'Camera')
print('Camera:',camera.name())
print('Camera properties:',bv.get_behaviour_property_names(cam))
for name in ('field_of_view','aspect_ratio_width','aspect_ratio_height'):
    print(name,dv.get_data_value(bv.get_behaviour_data(cam,name),0))
params=csc.tools.RenderParameters(); params.width=896;params.height=630;params.samples=4
renderer=next(t for t in app.get_tools_manager().tools() if isinstance(t,csc.tools.RenderToFile))
renderer.take_image(view,params,str(out/'FPS-hatchet-reference-contact.png'))
print('Reference image requested')
