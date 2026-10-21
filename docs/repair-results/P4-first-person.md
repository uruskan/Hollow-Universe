# P4 first-person tools and optics

Recovery checkpoint, 16 October 2026. Work is **in progress**, not visually accepted.
The coordinator owns integration/status documentation and runtime scheduling.

**Latest:** the coordinator completed the incremental holo import, both-body editor checks
and three representative captures on the 19:49 build. Checks passed **16 actions / 372 poses
per body**; all four saved settings files are unchanged. Hosted contact, physical mouse input,
full-cycle motion and holo ADS quality remain unverified. See the native review below.

## Recovered source

- Accepted `metal-hatchet` profile and skeletal motion are unchanged.
- Before the restart, only the holo source/export edits reached disk. The exported GLB
  is structurally valid and remains 88,420 bytes. Compared with HEAD, its complete binary
  payload is identical and its only JSON difference is node 8, `socket_eye`: X changes
  from approximately -0.12 m to -0.24 m. Meshes, materials, rail/mount and remote geometry
  are unchanged. SHA-256: `af26391b864b1bff69570a27a3aac91fbaec1eb91ec7738f4f255310c2400d45`.
  `art/models/attachments.py` contains the matching source socket change.
- The prior analytic projection measured a 76 × 70.5 × 44 mm housing and 28 × 36 mm lens.
  At 60° vertical viewmodel FOV the longer relief predicts housing height falling from
  58.7% to 27.2% of image height and aperture from 20.8% to 11.5%. These are geometry
  estimates, not rendered or gameplay acceptance. ADS hands/alignment still require capture.

## Current source work

- Procedural profiles now have the same optional complete-rig composition mechanism as
  skeletal profiles: solve the authored arms/item first, then apply one rigid transform to
  both. It fades out for aim so the optical eye remains aligned. Contact screen-ray fitting
  accounts for that final composition. Default identity retains existing profiles.
- Workshop placement exposes separate complete-rig position/rotation for procedural profiles.
- Workshop gizmo materials now enable their configured FOV override, matching the mouse
  projection. Previously the visible handle used the world-camera FOV while picking used
  the viewmodel FOV. Visible shafts can be dragged; changing Shift during a drag affects
  subsequent motion instead of rescaling the entire drag and snapping backward.

## Remaining

Both pickaxe candidates bind the existing `chop` source with the accepted axe's whole-rig
composition. Their grip sockets share the same authored axes; their working tips remain on
the forward +X side of the head. The art marker maps the same source contact pose to each
tool's unchanged gameplay strike tick, and their existing swing durations are preserved.
This is a candidate reuse of source motion, not proof of good pickaxe contact or complete
equip/sprint transitions (the source path holds its carry for those actions). The stone axe
keeps its existing motion with a `(0, -0.035, -0.3)` m rigid composition offset. No mesh scaling.
Only these three profiles change; the accepted metal axe is unchanged.

The workshop now has `--editor-aspect 4:3|16:9|21:9` for sheet/animation captures. Animation
manifests include body, both FOVs, rig transform and a serialized-profile SHA-256. New runtime
checks exercise shaft picking, Shift pressed mid-drag, and whole-rig invariance of solved
bone rotations, palm contact and reach correction. Their later native run is recorded below.

The coordinator will run the first integrated build/focused checks; no duplicate P4 build.
Request import/capture slots separately. Inspect both bodies, aspect ratios, accepted axe regression, carry/equip/full swing,
actual close/normal/out-of-range hit/miss and holo hip/ADS/TPS views. Physical mouse validation
is required for the original workshop complaint. No gameplay reach, strike timing, damage,
combat identity/protocol, FirstPersonView or ThingsView edits belong to P4.

At the initial recovery checkpoint, P4 had performed no post-restart build, engine-free test,
Godot import/run, Blender/GPU capture or commit. Subsequent coordinator validation is recorded
below. Existing staged admin-camera and unrelated working changes were preserved.

## Coordinated validation attempt

The coordinator's integrated single-worker solution build on 16 October at 15:35 was stopped
by its memory guard after 34.69 seconds: available RAM reached 1.566 GiB (commit 74%).
Core/server/tests/game DLLs were emitted, but there was no completed solution summary.
This is **not a build pass** and the partial outputs are not fresh validation. No tests or
GPU checks followed. Retained log:
`.artifacts/recovery/integrated-checks/20261016-153554-build.stdout.log`.

P4 source remains at the frozen candidate described above. Light source review is permitted,
but another runtime attempt requires an explicit coordinator slot and at least 5.5 GiB free
RAM at start, stopping the owned job below 2 GiB free or above 85% commit. No automatic retry,
additional agents or changes to the user's applications are authorized by this checkpoint.

The coordinator's later incremental solution build **passed with 0 warnings and 0 errors**
at 17:18, in 12.87 seconds. Minimum available RAM was 5.983 GiB; peak commit was 48%.
Log: `.artifacts/recovery/integrated-checks/20261016-171805-build.stdout.log`.
Source manifest: `.artifacts/recovery/source-freeze-20261016T141805Z.json`.
P4 implementation/data remain frozen for the coordinator's focused test batches. This build
pass does not validate the pending editor/runtime checks or visual quality.

The 19:21 shared build also passed (log
`.artifacts/recovery/integrated-checks/20261016-192147-build.stdout.log`). The coordinator's
foundation batch passed every Held/HeldPresentation check, including all 12 skeletal bindings
on both bodies. The overall batch was **61/64**, with three failures outside P4; it must not
be represented as a clean full-suite pass. Log:
`.artifacts/recovery/integrated-checks/20261016-192939-foundation.stdout.log`.

The subsequent exclusive P4 native slot did **not start**: preflight available RAM was
5.490 GiB, then 5.098 GiB (57% commit), below the required 5.5 GiB floor. No import, editor
check or capture was launched; the coordinator was informed and the slot returned unconsumed.
The guarded runner is saved at `.artifacts/p4-first-person/Run-Validation.ps1`; settings hashes
are in `settings-before.json` beside it. The 19:21 game DLL SHA-256 is
`a465875bf92c350c1b771479c2c0d60eda0ed398dd4caa51f017e35d8ae7cfa8`.
At that blocked attempt the holo cache remained stale: imported source MD5 `cc0efb2dffe5774a744b464dab8b9d9e`
versus current GLB MD5 `af96f0f9b48a74a410035ae42b0dbb8b`. Refresh it before any holo capture.

## Native checks and image review — coordinator's 19:49 build

The coordinator ran the saved runner serially after headroom recovered. Receipts identify
the **20261016-194953 build**, not the earlier 19:21 binary. Actual game DLL SHA-256:
`6e9f640eb671e2a84002b3de500d4de213141ba5193bd677f86778a4ae7ec407`.
Every job has its arguments, source/DLL hashes, memory samples, stdout, stderr and receipt in
`.artifacts/p4-first-person/`, using the job names below. Review confirmed no P4 source or
asset hash deviation since the male check, and the accepted metal-hatchet profile remains
identical to HEAD.

| Job | Result | Seconds | Minimum free GiB | Maximum commit |
| --- | --- | ---: | ---: | ---: |
| import | Exit 0; only `holo-sight.glb` reimported | 4.83 | 5.018 | 56% |
| editor-male | PASS, 16 actions / 372 FPS/TPS poses | 9.69 | 4.231 | 60% |
| editor-female | PASS, 16 actions / 372 FPS/TPS poses | 9.61 | 4.915 | 58% |
| accepted-axe | Sheet saved, exit 0 | 7.15 | 6.312 | 54% |
| pickaxe-candidate | Sheet saved, exit 0 | 7.13 | 6.277 | 54% |
| holo-mount | Image saved, exit 0 | 4.76 | 6.964 | 52% |

The normal incremental scan imported exactly one asset; no cache deletion, forced full
reimport or mesh regeneration occurred. The holo cache's `source_md5` now matches the GLB:
`af96f0f9b48a74a410035ae42b0dbb8b`. Its imported scene `dest_md5` is
`53844ac6b166a628c610f6a95c1e3d8a`.

Both checks exercised numeric controls, live preview, undo/redo, temporary save/reload,
legacy key editing, source-bone fidelity/isolation, gameplay-marker mapping and holding-hand
attachment. New shaft/Shift-drag and complete-rig invariance checks also executed. Neither
body had a connected holding-hand sample above the 25 mm attachment limit. Female stone axe
still needed a **0.0537 m authored reach correction at swing phase 0.2**; that remains an
editor warning, not a solved framing result. The source swings also have off-frame grip
samples; no grip-screen-margin threshold was requested. Finite transforms do not mean the
hands or tools stay on screen.

Inspected the actual [accepted axe sheet](../images/repair-p4/accepted-axe.png),
[pickaxe candidate sheet](../images/repair-p4/pickaxe-candidate.png) and
[mounted holo](../images/repair-p4/holo-mount.png). Both male tool sheets use 16:9 tiles and
show carry/end and the authored event pose, but the tool and hands leave the frame at art
phases **20% and 65%**. This also occurs on the preserved accepted axe; its performance has
not been changed to conceal that finding. The event poses appear against an editor fixture;
they do not prove a real server hit or close/normal/out-of-range contact. The sheet columns
sample `0, 20%, event 72.8%, 65%, 100%`, so they are not chronologically ordered around the
event and must not be presented as a motion recording. The mounted holo appears on the
rifle's upper rail; this external image cannot establish the new ADS eye relief, aperture
readability, reticle alignment or hand placement.

The two editor checks and axe/pick sheet runs each report **one ObjectDB instance leaked at
exit**. Import and holo-mount stderr are empty. No memory or timeout guard fired.

Compared the four `.cfg` files with `settings-before.json`: same file set, **4/4 identical
SHA-256**, no changed settings. Evidence: `settings-after-review.json` and
`settings-review.receipt.json` in `.artifacts/p4-first-person/`.

### Remaining acceptance

- Reproduce workshop dragging with actual mouse input; injected UI-event checks alone do
  not close the owner's usability report.
- Inspect female renders and other aspect ratios (4:3 and 21:9); the current representative
  tool sheets cover only male 16:9. Review stone axe/pick and broader catalogue composition.
- Record complete equip/carry/swing/recovery cycles, both bodies. Source equip/sprint
  currently hold the carry pose; these are not newly authored transitions.
- Hosted close/normal/out-of-range pickaxe hits and misses, including working-tip alignment
  and authoritative damage timing, plus remote views. Do not alter reach or damage to fake contact.
- Actual holo hip-to-ADS transition, aperture/reticle/eye/hand alignment and remote mounting;
  the external mounted image is only a geometry check.
- Owner review remains necessary. P4 is a technically checked candidate, **not a completed
  visual repair**. No live candidate changes or extra runtime jobs were made during this review.
