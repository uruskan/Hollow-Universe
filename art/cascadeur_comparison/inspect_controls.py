"""Inspect controller ownership and available native camera/pose interfaces."""
import json
from pathlib import Path
out=Path('D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/character-trial-cascadeur-v3-comparison')
mv=scene.model_viewer();bv=mv.behaviour_viewer();dv=mv.data_viewer()
report={'objects':[],'interfaces':{}}
for o in mv.get_objects():
    n=mv.get_object_name(o)
    if any(s in n for s in ('hand_','lowerarm_','upperarm_','clavicle_','prop_','FPS_review','Trial_','Rock','Torch')):
        attrs=[]
        for d in dv.get_all_data_id(o):
            name=dv.get_data_name(d)
            if any(s in name.lower() for s in ('position','rotation','visible','fov','view','hide','kinematic','ik/fk')):
                try: value=str(dv.get_data_value(d,0))
                except RuntimeError: value='not stored as animation data'
                attrs.append([name,value])
        report['objects'].append({'name':n,'type':mv.get_object_type_name(o),'data':attrs})
for name in ('AutoPosingTool','ViewportsTool','CameraOrientationTool','ViewActionCreatorTool'):
    try:
        t=app.get_tools_manager().get_tool(name)
        e=t.editor(app.current_scene()) if hasattr(t,'editor') else t
        report['interfaces'][name]=[n for n in dir(e) if not n.startswith('_')]
    except Exception as e:report['interfaces'][name]=str(e)
(out/'controls-inspection.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps({'interfaces':report['interfaces'],'object_count':len(report['objects']),
                  'meshes':[mv.get_object_name(bv.get_behaviour_owner(b)) for b in bv.get_behaviours('MeshObject')],
                  'cameras':[c.name() for c in csc.view.camera_utils.get_cameras(app.current_scene())]}))
