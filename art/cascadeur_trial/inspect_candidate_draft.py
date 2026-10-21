"""Diagnose native rig corrections without accepting or saving a candidate."""
from pathlib import Path
import json
import numpy as np
out=Path("D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur")
poses=json.loads((out / "candidate-requested-poses.json").read_text(encoding="utf-8"))
mv=scene.model_viewer();bv=mv.behaviour_viewer();dv=mv.data_viewer()
rows=[]
for b in bv.get_behaviours("Joint"):
    o=bv.get_behaviour_owner(b);name=mv.get_object_name(o);t=bv.get_behaviour_by_name(o,"Transform")
    gp=bv.get_behaviour_data(t,"global_position");gr=bv.get_behaviour_data(t,"global_rotation")
    for frame in range(43):
        p=dv.get_data_value(gp,frame);r=dv.get_data_value(gr,frame).to_rotation_matrix()
        err=float(np.linalg.norm(p-np.array(poses[frame][name]["position"])))
        angle=float(np.degrees(np.arccos(np.clip((np.trace(r.T@np.array(poses[frame][name]["rotation"]))-1)/2,-1,1))))
        rows.append({"joint":name,"frame":frame,"position_error_cm":err,"rotation_error_degrees":angle})
summary={name:{"position_error_cm":max(r["position_error_cm"] for r in rows if r["joint"]==name),
               "rotation_error_degrees":max(r["rotation_error_degrees"] for r in rows if r["joint"]==name)}
         for name in poses[0]}
(out / "candidate-native-corrections.json").write_text(json.dumps({"summary":summary,"samples":rows},indent=2)+"\n",encoding="utf-8")
print(json.dumps(sorted(summary.items(),key=lambda x:x[1]["rotation_error_degrees"],reverse=True)[:12]))
scene.set_current_frame(18)
viewport=app.current_scene().active_viewport().domain_viewport();camera=viewport.camera_struct()
camera.position=np.array([-250.,160.,360.],dtype=np.float32);camera.target=np.array([0.,93.,20.],dtype=np.float32)
viewport.set_camera_struct(camera);viewport.set_mode_visualizers(csc.view.ViewportMode.View)
params=csc.tools.RenderParameters();params.width=1280;params.height=960;params.samples=4
renderer=next(t for t in app.get_tools_manager().tools() if isinstance(t,csc.tools.RenderToFile))
renderer.take_image(app.current_scene(),params,str(out / "draft-native-corrections-contact.png"))
