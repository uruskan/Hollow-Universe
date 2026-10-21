"""Single native still after a candidate scene is selected."""
from pathlib import Path
out=Path('D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur-v3-comparison')
view=app.current_scene()
stem=Path(view.get_path_name()).stem
assert 'Cascadeur-A02' in stem
view.active_viewport().domain_viewport().set_mode_visualizers(csc.view.ViewportMode.View)
frame=scene.get_current_frame()
params=csc.tools.RenderParameters();params.width=896;params.height=630;params.samples=4
renderer=next(t for t in app.get_tools_manager().tools() if isinstance(t,csc.tools.RenderToFile))
path=out/(stem+'-frame-'+str(frame)+'.png')
renderer.take_image(view,params,str(path))
print('Requested native still:',str(path))
