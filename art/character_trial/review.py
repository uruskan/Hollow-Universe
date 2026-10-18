"""Render saved v2 actions with their saved cameras, without rebuilding poses.

CLI: blender --background --factory-startup --python-exit-code 1 --python
     art/character_trial/review.py -- --mode frames --view fps --clip idle

--out is the artifact directory containing Survivor-Audition.blend and manifests.
For a live scene, import and call main(open_source=False), optionally with
out=..., mode="frames", view="fps", clip="idle". No .blend is saved.
Frames cover [start,end), exactly duration*30 samples. frames.json is authoritative;
unrelated existing images are retained.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
import math
from pathlib import Path
import sys
import traceback
import bpy

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_OUT = ROOT / ".artifacts" / "character-trial-v2"
EXPECTED = ("idle", "walk", "sprint", "crouch_walk", "jump_land",
            "rock_strike", "hatchet_chop", "torch_equip")
FPS, SAMPLES = 30, 16
RESOLUTION = (896, 630)


def timestamp():
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(json.dumps(value, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    temporary.replace(path)


def checkpoint(out, status):
    status["updated_utc"] = timestamp()
    write_json(out / "render-status.json", status)


def read_clips(out, view):
    path = out / ("fps-clips.json" if view == "fps" else "clips.json")
    if view == "fps" and not path.is_file():
        path = out / "clips.json"
    clips = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(clips, list) or len(clips) != 8 or {c.get("name") for c in clips} != set(EXPECTED):
        raise ValueError(f"{path}: expected the eight agreed animation names")
    return clips


def select_scene(view):
    scenes = [scene for scene in bpy.data.scenes if scene.name.startswith(view.upper())]
    if len(scenes) != 1:
        raise ValueError(f"Expected exactly one {view.upper()} scene, found {len(scenes)}")
    scene = scenes[0]
    bpy.context.window.scene = scene
    if scene.camera is None:
        raise ValueError(f"{scene.name}: no saved active camera")
    rigs = [obj for obj in scene.objects if obj.type == "ARMATURE"]
    if len(rigs) != 1:
        raise ValueError(f"{scene.name}: expected one armature, found {len(rigs)}")
    if abs(scene.render.fps / scene.render.fps_base - FPS) > 1.e-6:
        raise ValueError(f"{scene.name}: saved action timing is not {FPS} fps")
    return scene, rigs[0]


def select_action(rig, name):
    action = bpy.data.actions.get(name)
    if action is None:
        raise ValueError(f"Missing saved action {name}")
    rig.animation_data_create()
    slot = None
    for track in rig.animation_data.nla_tracks:
        track.mute = True
        track.is_solo = False
        for strip in track.strips:
            if strip.action == action and strip.action_slot is not None:
                slot = strip.action_slot
    if slot is None:
        candidates = [slot for slot in action.slots if slot.target_id_type == "OBJECT"]
        if len(candidates) != 1:
            raise ValueError(f"{name}: no unique object action slot for {rig.name}")
        slot = candidates[0]
    rig.animation_data.use_nla = False
    rig.animation_data.action = action
    rig.animation_data.action_slot = slot
    return action, slot


def configure_render(scene):
    # Camera transform, lens, sensor, shifts and clipping stay exactly as saved.
    scene.render.engine = "BLENDER_EEVEE"
    scene.eevee.taa_render_samples = SAMPLES
    scene.eevee.use_raytracing = False
    scene.render.resolution_x, scene.render.resolution_y = RESOLUTION
    scene.render.resolution_percentage = 100
    scene.render.pixel_aspect_x = scene.render.pixel_aspect_y = 1.
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    scene.render.image_settings.color_depth = "8"
    scene.render.use_file_extension = True


def action_range(action, clip):
    first, last = map(float, action.frame_range)
    if (not all(math.isfinite(v) for v in (first, last)) or last <= first
            or any(abs(v - round(v)) > 1.e-6 for v in (first, last))):
        raise ValueError(f"{action.name}: invalid/non-integer sampled action range {first}..{last}")
    first, last = round(first), round(last)
    duration = (last - first) / FPS
    if abs(duration - float(clip["duration"])) > .001 / FPS:
        raise ValueError(f"{action.name}: saved duration {duration:g} differs from manifest {clip['duration']}")
    return first, last, duration


def still_frame(clip, first, last):
    contact = clip.get("contact")
    if isinstance(contact, list):
        contact = next((value for value in contact if isinstance(value, (int, float))), None)
    if contact is None:
        return round((first + last) * .5), "action midpoint"
    if not isinstance(contact, (int, float)) or not math.isfinite(contact):
        raise ValueError(f"{clip['name']}: invalid contact time")
    frame = first + round(contact * FPS)
    if not first <= frame <= last:
        raise ValueError(f"{clip['name']}: contact time lies outside the saved action")
    return frame, "manifest contact"


def validate_png(path):
    if not path.is_file():
        raise RuntimeError(f"Render produced no file: {path}")
    with path.open("rb") as stream:
        if stream.read(8) != b"\x89PNG\r\n\x1a\n":
            raise RuntimeError(f"Render output is not a PNG: {path}")


def render_frame(scene, frame, path):
    scene.frame_set(frame)
    bpy.context.view_layer.update()
    scene.render.filepath = str(path)
    result = bpy.ops.render.render(write_still=True)
    if "FINISHED" not in result:
        raise RuntimeError(f"Render did not finish at frame {frame}: {result}")
    validate_png(path)


def render_animation(scene, first, last, directory, before_frame, completed_frame):
    """One native animation render per clip; callbacks rename/checkpoint each PNG."""
    directory = directory.resolve()
    saved = (scene.frame_start, scene.frame_end, scene.frame_step, scene.render.filepath,
             scene.render.use_overwrite, scene.render.use_placeholder)
    written = set()
    callback_errors = []

    def render_pre(render_scene, *unused):
        if render_scene != scene or callback_errors:
            return
        try:
            frame = int(render_scene.frame_current)
            if not first <= frame < last:
                raise RuntimeError(f"Native render requested unexpected frame {frame}")
            before_frame(frame)
        except BaseException as exc:
            callback_errors.append((exc, traceback.format_exc()))

    def render_write(render_scene, *unused):
        if render_scene != scene or callback_errors:
            return
        try:
            frame = int(render_scene.frame_current)
            if not first <= frame < last or frame in written:
                raise RuntimeError(f"Unexpected/duplicate native output frame {frame}")
            source = Path(render_scene.render.frame_path(frame=frame)).resolve()
            expected = (directory / f"native-{frame:04d}.png").resolve()
            destination = (directory / f"{frame-first:04d}.png").resolve()
            if (source != expected or source.parent != directory or destination.parent != directory
                    or source == destination):
                raise RuntimeError(f"Unexpected native render output path: {source}")
            validate_png(source)
            # Both paths are verified files in this clip's generated-output folder.
            # The disjoint native-/numeric names cannot overwrite another source frame.
            source.replace(destination)
            completed_frame(frame, destination)
            written.add(frame)
        except BaseException as exc:
            # Blender may swallow handler exceptions. Retain and re-raise after the
            # operator returns, rather than incorrectly marking the clip complete.
            callback_errors.append((exc, traceback.format_exc()))

    try:
        scene.frame_start, scene.frame_end, scene.frame_step = first, last - 1, 1
        scene.render.filepath = str(directory / "native-####")
        scene.render.use_overwrite = True
        scene.render.use_placeholder = False
        bpy.app.handlers.render_pre.append(render_pre)
        bpy.app.handlers.render_write.append(render_write)
        result = bpy.ops.render.render(animation=True)
        if callback_errors:
            exc, trace = callback_errors[0]
            raise RuntimeError(f"Native render callback failed: {type(exc).__name__}: {exc}\n{trace}") from exc
        if "FINISHED" not in result:
            raise RuntimeError(f"Native animation render did not finish: {result}")
        if written != set(range(first, last)):
            raise RuntimeError(f"Native render wrote {len(written)} of {last-first} required frames")
    finally:
        if render_pre in bpy.app.handlers.render_pre:
            bpy.app.handlers.render_pre.remove(render_pre)
        if render_write in bpy.app.handlers.render_write:
            bpy.app.handlers.render_write.remove(render_write)
        (scene.frame_start, scene.frame_end, scene.frame_step, scene.render.filepath,
         scene.render.use_overwrite, scene.render.use_placeholder) = saved


def options(argv, out, mode, view, clip):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT)
    parser.add_argument("--mode", choices=("stills", "frames"), default="stills")
    parser.add_argument("--view", choices=("tps", "fps", "both"), default="both")
    parser.add_argument("--clip", choices=EXPECTED)
    args = parser.parse_args([] if argv is None else argv)
    if out is not None:
        args.out = Path(out)
    if mode is not None:
        args.mode = mode
    if view is not None:
        args.view = view
    if clip is not None:
        args.clip = clip
    if args.mode not in ("stills", "frames") or args.view not in ("tps", "fps", "both"):
        raise ValueError("Invalid review mode or view")
    if args.clip is not None and args.clip not in EXPECTED:
        raise ValueError(f"Unknown review clip: {args.clip}")
    args.out = args.out.resolve()
    return args


def main(open_source=True, *, out=None, mode=None, view=None, clip=None, argv=None):
    args = options(argv, out, mode, view, clip)
    args.out.mkdir(parents=True, exist_ok=True)
    source = args.out / "Survivor-Audition.blend"
    status = {
        "schema_version": 1, "state": "running", "started_utc": timestamp(),
        "mode": args.mode, "requested_view": args.view, "requested_clip": args.clip,
        "source_blend": str(source), "open_source": bool(open_source),
        "renderer": "BLENDER_EEVEE", "resolution": list(RESOLUTION), "samples": SAMPLES,
        "fps": FPS, "camera_policy": "Saved camera unchanged", "clips": [],
        "clips_planned": 0, "clips_completed": 0, "frames_planned": 0,
        "frames_completed": 0, "active_clip": None, "active_frame": None, "error": None,
    }
    checkpoint(args.out, status)
    active = manifest = manifest_path = None
    try:
        if open_source:
            bpy.ops.wm.open_mainfile(filepath=str(source))
        status["loaded_blend"] = bpy.data.filepath
        status["blender_version"] = bpy.app.version_string
        views = ("tps", "fps") if args.view == "both" else (args.view,)
        jobs = []
        for current_view in views:
            scene, rig = select_scene(current_view)
            for metadata in read_clips(args.out, current_view):
                if args.clip is not None and metadata["name"] != args.clip:
                    continue
                action, slot = select_action(rig, current_view.upper() + "_" + metadata["name"])
                first, last, duration = action_range(action, metadata)
                count = last - first if args.mode == "frames" else 1
                entry = {"view": current_view, "clip": metadata["name"], "action": action.name,
                         "slot": slot.identifier, "scene": scene.name, "camera": scene.camera.name,
                         "source_frame_range": [first, last], "duration_seconds": duration,
                         "frames_planned": count, "frames_completed": 0, "state": "pending"}
                status["clips"].append(entry)
                status["frames_planned"] += count
                jobs.append((current_view, scene, rig, metadata, first, last, entry))
        status["clips_planned"] = len(jobs)
        checkpoint(args.out, status)
        for current_view, scene, rig, metadata, first, last, active in jobs:
            bpy.context.window.scene = scene
            select_action(rig, active["action"])
            configure_render(scene)
            scene.frame_start, scene.frame_end = first, last
            active["state"] = "running"
            status["active_clip"] = current_view + "/" + metadata["name"]
            manifest = manifest_path = None
            if args.mode == "stills":
                directory = args.out / "review" / current_view
                frame, reason = still_frame(metadata, first, last)
                frames = [(frame, directory / (metadata["name"] + ".png"))]
                active["still_selection"] = reason
            else:
                directory = args.out / "frames" / current_view / metadata["name"]
                frames = [(frame, directory / f"{index:04d}.png")
                          for index, frame in enumerate(range(first, last))]
                manifest_path = directory / "frames.json"
                manifest = {"schema_version": 1, "view": current_view, "clip": metadata["name"],
                            "action": active["action"], "slot": active["slot"], "camera": scene.camera.name,
                            "source_blend": status["loaded_blend"], "source_frame_range": [first, last],
                            "fps": FPS, "duration_seconds": (last - first) / FPS,
                            "loop": bool(metadata.get("loop")), "end_frame_excluded": True,
                            "resolution": list(RESOLUTION), "frames_planned": len(frames),
                            "complete": False, "frames": []}
                write_json(manifest_path, manifest)
            directory.mkdir(parents=True, exist_ok=True)
            active["output_directory"] = str(directory)
            print(f"REVIEW BEGIN {status['active_clip']}: {len(frames)} {args.mode} frame(s)", flush=True)
            checkpoint(args.out, status)
            def before_frame(frame):
                status["active_frame"] = frame
                active["current_frame"] = frame
                checkpoint(args.out, status)

            def completed_frame(frame, path):
                active["frames_completed"] += 1
                active["last_output"] = str(path)
                status["frames_completed"] += 1
                if manifest is not None:
                    manifest["frames"].append({"index": frame - first, "file": path.name,
                                               "source_frame": frame, "time_seconds": (frame - first) / FPS})
                    write_json(manifest_path, manifest)
                checkpoint(args.out, status)

            if args.mode == "frames":
                render_animation(scene, first, last, directory, before_frame, completed_frame)
            else:
                frame, path = frames[0]
                before_frame(frame)
                render_frame(scene, frame, path)
                completed_frame(frame, path)
            if manifest is not None:
                manifest["complete"] = True
                write_json(manifest_path, manifest)
            active["state"] = "complete"
            status["clips_completed"] += 1
            checkpoint(args.out, status)
            print(f"REVIEW COMPLETE {status['active_clip']}", flush=True)
        status["state"] = "complete"
        status["active_clip"] = status["active_frame"] = None
        status["finished_utc"] = timestamp()
        checkpoint(args.out, status)
        print(f"TRIAL_REVIEW_COMPLETE {status['frames_completed']} frames, {status['clips_completed']} clips", flush=True)
        return status
    except BaseException as exc:
        status["state"] = "failed"
        status["error"] = {"type": type(exc).__name__, "message": str(exc), "traceback": traceback.format_exc()}
        status["finished_utc"] = timestamp()
        if active is not None:
            active["state"] = "failed"
        if manifest is not None and manifest_path is not None:
            manifest["error"] = status["error"]
            write_json(manifest_path, manifest)
        checkpoint(args.out, status)
        print(f"TRIAL_REVIEW_FAILED {type(exc).__name__}: {exc}", flush=True)
        raise


if __name__ == "__main__":
    cli = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    main(argv=cli)
