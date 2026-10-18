# Survivor character audition

Requested on 17 October 2026 as a separate test of a new third-person character,
matching first-person arms and eight animations. User-selected set: idle, walk,
sprint, crouch walk, jump/land, rock strike, hatchet chop, torch equip.

This is an isolated art experiment, not an accepted replacement for the gameplay
characters. No game data, current animation libraries, player save or ground UI is
changed. The source-body cache and earlier grip studies remain in
`.artifacts/character-trial-v1/`; the preserved animation baseline lives in
`.artifacts/character-trial-v2/` and the current FPS revision in
`.artifacts/character-trial-v3/`.

The fresh human is built from MakeHuman/MPFB's CC0 mesh, rig weights and system
assets, with independently selected proportions. The short-sleeve workwear and
hiking shoes originate in that CC0 pack. Local skin sources are 2048px diffuse
maps; this does not claim scanned skin, motion capture or AAA character quality.
The eight performances are authored on this character's own bind skeleton using
analytical IK. FPS has separately composed hand trajectories and camera framing.
The existing `authored.py` mathematical solver is reused; existing retargeted game
clips and gameplay pose modifiers are not used.

Source asset pack:
`D:/GameDev/Assets/Downloads/makehuman-system-assets/makehuman_system_assets_cc0.zip`.
Asset headers document CC0 release in September 2020 by Data Collection AB,
Joel Palmius and Jonas Hauquier. No assets from another game are used.

Build with Blender 5.2.2, `BLENDER_USER_RESOURCES` set to
`D:/GameDev/Tools/blender-hollow-config`: run `build.py -- body`, then `animate.py`.
Use `--python-exit-code 1` so script errors fail the process. Run `validate.py`
after export. This local Blender route is used because `game-dev` is not installed.

Acceptance requires actual renders, moving playback and grip/deformation review.
GLB structural validation alone does not prove animation quality or Godot import.
Validation results and review locations will be recorded after execution.

The initial export failed the owner's visual grip review. On 18 October the
owner explicitly resumed all eight animations without further questions.
`grip_profile.json` captures the evaluated keyed TPS grip after the requested
6 cm movement toward the camera. Its finger rotations and rigid hand-to-tool
transform now drive both new exports; the earlier static studies remain intact.
This is a starting pose for the new performance review, not a record of final
visual acceptance.

`motions.py` authors TPS shoulder/elbow paths and locomotion. `fps_motions.py`
authors independent FPS trajectories on the separate cropped-arm mesh. The FPS
rig has a `(0, -0.08, 0.20)` metre mounting offset relative to the shared source
anatomy, paired with the saved 22 mm review camera. These are preview coordinates;
runtime placement and Godot import still need their own integration checks.

The v2 `.blend` contains two scenes, sixteen named actions and a marked sequential
review timeline in each scene. Each GLB contains eight sampled 30 fps clips.
`validate.py` checks both GLBs; `validate_animation.py` checks evaluated integer
and half-frame transforms, loops, rigid tool attachment and FPS framing. Its
warnings include fast intentional movement and raw interpolated quaternion norm
drift where the evaluated rotation remains orthonormal. Neither validator proves
skin contact, collision-free fingers, motion capture quality or natural movement.

Render the saved actions with `review.py -- --mode frames --view both` (896×630,
30 fps), and `--mode stills` for contact/midpoint PNGs. Then encode the per-clip
frame folders as MP4s and run `package_review.py --out .artifacts/character-trial-v2`
to create the offline paired playback page, manifest and artifact links.

## FPS wrist and reach revision — 18 October 2026

`refine_fps.py` opens the preserved v2 blend and creates v3. Torch and hatchet
palms now follow the solved forearm with small anatomical wrist offsets. The FPS
hatchet rotates around its shaft to face the blade forward independently of the
hand. Both rock palms solve around one shared contact frame. Rock and hatchet
have higher wind-ups and longer forward paths. Finger curl profiles, meshes,
camera and all TPS actions remain unchanged; the TPS action digest is checked.

Run Blender with `--python art/character_trial/refine_fps.py -- --diagnostics`,
then validate both GLBs and the saved blend. Render and encode the three revised
FPS actions (`rock_strike`, `hatchet_chop`, `torch_equip`) using `review.py` and
`encode_review.py`; retain the unchanged baseline media with their validation
receipts. Package with `package_review.py --out .artifacts/character-trial-v3
--baseline-url http://127.0.0.1:8874/index.html`.

Serve v3 with `serve_review.py --root .artifacts/character-trial-v3 --port 8873`
and v2 on port 8874. The local server supports byte ranges for seeking; media
URLs include content hashes so a replaced clip cannot reuse cached v2 video.
`fps-refinement.json` records measured before/after wrist and prop trajectories.
All 16 clips are packaged; the three revised FPS videos were rendered and decoded
again. This remains a Blender review candidate, not an in-game integration.
