# Cascadeur / Blender v3 comparison

This isolated experiment owns only `art/cascadeur_comparison/` and
`.artifacts/character-trial-cascadeur-v3-comparison/`. The Blender v1/v2/v3
deliverables, `art/character_trial/`, and the other chat's `cascadeur_trial`
files are inputs, never output destinations.

The user wants Blender closed whenever it is no longer needed. Preserve any
unsaved work before closing a live session; background Blender jobs must exit
when finished. These native Cascadeur authoring scripts do not launch Blender.

## Review candidates

- `FPS-HatchetChop-Cascadeur-A02.casc`: 43 frames at 30 fps, 10 pose keys.
- `FPS-TorchEquip-Cascadeur-A02.casc`: 49 frames at 30 fps, 9 pose keys.

Both are in the comparison artifact directory. A02 is the candidate to review;
A01 and the numbered scenes are diagnostic checkpoints. These are FPS arm
experiments. TPS and all original Blender clips remain unchanged.

`pose_targets.py` computes feasible shoulder, elbow, hand and elbow-hinge
controller targets. `author_native.py` applies sparse keys through Cascadeur's
native point controls and Bezier IK interpolation. Finger rotations and tool
attachment transforms retain the original grip. This is native control-rig
authoring, not an AutoPhysics or AutoPosing result.

The imported control rig distorted the hatchet wrist at contact: the unchanged
v3 source measured 5 degrees while its initial Cascadeur import measured
48.47 degrees. Candidate targets restore the source hand orientation before
changing the motion. The shared elbow additional point must be perpendicular
to both arm segments; treating it as an independent forearm twist point
overconstrains the rig (the rejected A01 experiment).

A02 all-frame validation: finite transforms; maximum wrist bend 13.15 degrees
for hatchet and 5.99 degrees for torch; maximum keyed controller error below
0.009 cm; finger and prop local transform changes below 0.000002 matrix units.
These are technical checks, not a claim of artistic acceptance. Native viewport
captures provide visual inspection. The camera position/lens/aspect match the
v3 FPS view, but Cascadeur's materials and lighting differ.

## Additional FPS weapon scope

The next requested set extends the existing hatchet and torch candidates with
six weapon actions: `heavyaxe_chop`, `pickaxe_swing`, `pistol_fire`,
`pistol_reload`, `rifle_fire`, and `rifle_reload`. These are new candidates,
not locomotion clips. Their generation, capture and visual acceptance must be
reported separately as each finishes; this scope list does not establish that
the six animations are complete.

The six additional actions have no Blender v3 baseline. The comparison page
shows that absence explicitly and plays a candidate alone when its media is
available. Hatchet and torch retain their existing paired comparisons. Missing
candidate videos remain labelled pending. Original Blender sources, existing
A02 scenes and media are preserved inputs.

Weapon parts such as a magazine, slide or bolt can have independent local
transforms beneath the weapon parent. Reload validation must inspect the part
trajectory and support-hand contact as well as the wrist and grip. A changed
part transform is intentional during reload and must not be confused with a
broken fixed weapon grip. Numeric checks and a successful export do not prove
that the motion is visually convincing.

The viewer's repeat transport waits for seek completion and decoded frames on
both panels before resuming. Playback intent survives the temporary
`readyState` drop during a loop restart; pausing or switching clips cancels an
obsolete pending resume. The focused regression command is:

```powershell
node art/cascadeur_comparison/test_transport.cjs
```

Seven checks pass using independent event-driven media doubles: repeated
asynchronous seeks, repeat disabled, all three playback rates, missing paired
media, clip changes or pause during a seek, buffering, and decoder failure.
These checks verify transport logic; they are not a real-browser playback or
codec test. The earlier browser-access restrictions still limit that claim.

## Reproduction and evidence

`prepare_inputs.py` runs in background Blender to export fresh input copies and
validate FBX round trips. It never saves the source blend. The current export
has already been completed and validated; do not rerun merely to review clips.

Run app scripts through the official local Cascadeur MCP server:

```powershell
C:/Python314/python.exe art/cascadeur_comparison/mcp_client.py art/cascadeur_comparison/SCRIPT.py --receipt UNIQUE-NAME
```

Every invocation records its exact code, hash and server response. Inspect an
uncertain response before retrying a mutation. Candidate authoring refuses to
overwrite its existing target; choose a new revision deliberately.

`capture_sequence.py` serializes individual native still captures, allowing
Cascadeur to return to its event loop between each frame. It deliberately does
not invoke the native image-sequence renderer that crashed in the earlier
trial. Native capture requires an exposed app window; a returned request alone
does not establish that a file was written. Capture-state JSON records actual
completed PNGs. The supplied Free edition reports FBX export unavailable;
editable CASC files and viewport previews are the comparison deliverables.

The completed local comparison is `review/index.html`, with both FPS candidate
videos and verified v3 copies. Local preview-server startup was rejected by
automatic approval policy, and the in-app browser disallows `file:` URLs.
No comparison server was started. Open this HTML locally with a browser if
needed, or inspect the two open Cascadeur scenes and the candidate MP4 files.
Media decode and static JavaScript checks pass; in-browser interaction was
not validated under these restrictions.

`preserved-baselines.json` records 1,172 baseline file hashes and
`preservation-final.json` verifies they are unchanged. Per-candidate validation
and solved joint transforms are stored beside the CASC files. No game assets
or C# code are installed or changed by this experiment.
