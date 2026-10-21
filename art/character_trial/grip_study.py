"""Pause the full audition and expose one static grip for joint human review."""
from pathlib import Path
import bpy
from mathutils import Vector
OUT=Path(__file__).resolve().parents[2]/'.artifacts/character-trial-v1'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'Survivor-Audition.blend'))
scene=next(s for s in bpy.data.scenes if s.name.startswith('TPS'))
bpy.context.window.scene=scene
rig=bpy.data.objects['Survivor_TPS_Rig']
for track in rig.animation_data.nla_tracks:track.mute=True
action=bpy.data.actions['TPS_hatchet_chop'];rig.animation_data.action=action
rig.animation_data.action_slot=action.slots[0]
scene.frame_set(1);scene.frame_start=1;scene.frame_end=43
bpy.context.view_layer.update()
hand=rig.matrix_world@rig.pose.bones['hand_r'].head
target=hand+Vector((.025,-.035,.07))
cam=scene.camera
cam.location=target+Vector((-.50,-.70,.26))
cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.lens=58;cam.data.clip_start=.005
scene.render.resolution_x=1200;scene.render.resolution_y=1000
scene.render.resolution_percentage=100;scene.render.engine='CYCLES';scene.cycles.samples=24
try:
    prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='OPTIX';prefs.get_devices()
    for d in prefs.devices:d.use=d.type=='OPTIX'
    scene.cycles.device='GPU'
except Exception:pass
for o in bpy.context.view_layer.objects:o.select_set(False)
rig.select_set(True);bpy.context.view_layer.objects.active=rig
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            space=area.spaces.active;space.region_3d.view_perspective='CAMERA'
            space.shading.type='MATERIAL';space.overlay.show_overlays=False
            space.region_3d.view_camera_zoom=0
scene['review_status']='REJECTED GRIP — static inspection, not approved. Animation pass paused.'
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Hatchet-Grip-Study.blend'),compress=True)
scene.render.filepath=str(OUT/'grip-baseline-closeup.png');bpy.ops.render.render(write_still=True)
print('GRIP_STUDY_READY',flush=True)
