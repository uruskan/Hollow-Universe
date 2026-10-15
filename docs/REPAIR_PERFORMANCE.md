# Gathering hit-frame investigation — L23

Status: source tracing and opt-in diagnostics only. No performance improvement is claimed until the coordinated
renderer run measures the hit path. No gameplay invalidation rule has changed in this package.

## Confirmed source behavior

`NaturalState.Apply` records every received natural state in `Changed`, including health, weak-spot and hit-counter
updates. Its separate `PresenceChanged` event already distinguishes actual depletion/respawn.
`NatureView.TakeChanges` nevertheless schedules a nearby vegetation redraw on every changed object within range,
and marks the outcrop tile dirty even when the hit was on a tree. Nearby local hits are scheduled immediately.

`Vegetation.Refresh(near: true)` dirties the complete near set. A worker computes the set, then `ApplyNear` calls
`PlaceNear` on the main thread. That clears and copies every LOD list, creates arrays and assigns the MultiMesh
instance counts and buffers. The per-tile request therefore causes a whole-near-set upload. The rendered plant-kind
mapping only depends on presence and falling state, not health. This is definite repeated work, but its share of the
reported stutter is not yet measured.

The same changed tile makes `BuildOutcrops` query natural objects and rebuild dictionaries/lists. Existing nodes
are reused when their stage is unchanged. Stage transitions may instantiate models, but an ordinary tree hit does
not need this tile reconstruction. Marker position updates do need to follow each accepted weak-spot change.

The current tree shake assigns two cached global shader parameters. It does not create a mesh or clone a material.
Tree fall creates its own single-instance MultiMesh only on depletion and uses the existing flora mesh; this remains
a separate first-depletion cost to measure.

The live FX director creates particle batches, decal pools, lights, flashes and tracers at installation. FX texture
and debris meshes are cached; the loading plan also draws FX and marker styles. Ordinary shake should not be blamed
for resource loads. Loading warmup has a time limit, however: `.artifacts/repair-shore-day.log` previously reported
11 of 296 items not shown at 40 seconds. That log does not prove the gathering effects were missed. Pipeline deltas
in the actual profile will distinguish unfinished/novel pipeline work from repeated CPU work.

## Diagnostics now available

All new timing output is enabled by `GroundMeasure`/`FrameEvents` only:

- Fresh natural hits record kind, seen-hit ordinal, stage transition and depleted state.
- Changed frames record each NatureView stage, including sub-four-millisecond work, and immediate FX/sound cue time.
- Completed near-set uploads record elapsed main-thread time, allocated bytes and drawn instance count. The optional
  measurement properties live in Vegetation; it does not depend on the ground profiler.
- `GroundMeasure` reports new draw/surface/mesh pipeline compilations, initializes its GC comparison baseline, and
  includes up to 80 hit/upload frame pairs with elapsed time. Existing worst-frame, CPU/GPU, allocation and server
  tick reports remain available. Adjacent frame events are retained because work often appears in the next delta.

`GroundMeasure.AutoStart = false` gates the run until `StartCapture()` is called. A tour should wait for
`IsMeasuring` before beginning its first action. `Complete` signals that the report is finished;
`QuitWhenComplete = false` leaves process exit to the caller. Default ordinary `--measure` behavior still starts
when the world is ready and quits at completion.

## Coordinated reproduction and decision

The root-owned chop profile must teleport to the same known tree, hold the same quality/resolution/time/camera,
finish streaming and settle before starting the timer. Screenshot readbacks and PNG/JPEG encoding must be suppressed
inside this measurement. Use a fresh isolated save so prior depletion does not change the subject. Record an idle
control at the same location and then repeated nonterminal axe hits plus the terminal fall; distinguish those three
phases rather than folding them into one average. Keep diagnostics enabled for both baseline and candidate because
the report itself allocates strings on hit frames.

If repeated nonterminal spikes align with near-set uploads and/or outcrop rebuilds, the bounded candidate is to
invalidate plant rendering only when its rendered presence/kind changes, and outcrop rendering only when model
presence/stage changes. Keep hit FX, sound, shake and weak-spot updates on every accepted hit. Verify depletion,
respawn, first-time streamed overrides, falling-tree removal and stage transitions before accepting that change.
If only the first hit shows pipeline growth, investigate warmup coverage instead. If FX/sound or server ticks
dominate, follow the measured stage rather than changing vegetation speculatively.

Validation so far: `git diff --check` passes for the diagnostic source. Build, renderer profile, measured comparison
and any behavioral fix remain pending the root's shared checkpoint.

## Streaming blocker found during profile preparation

`profile-idle-2` never entered its measurement window. The player was alive and stationary after teleport, the
loading overlay was gone, and `SurfaceView.IsReady` stayed false for 90 seconds. This failed preparation is not a
performance result. The readiness log now breaks out terrain, vegetation, grass, cliffs and monuments, including
their pending jobs, so an incomplete subsystem is visible instead of only the combined flag.

Inspection found a concrete vegetation request-address collision. `TileKey.Packed` uses bits 61–63 for cube face,
but the old request cache XORed bit 63 to mark full scatter versus trees-only scatter. Full requests on faces 0/1
therefore collided with trees-only requests on faces 4/5 (and vice versa). Far tiles requested before movement could
permanently suppress new near-tile jobs, leaving missing tiles even with an empty queue. `ScatterRequestSet` now
deduplicates the tuple `(complete packed tile, full)` without modifying any address bits. The production helper is
engine-free and has regressions covering all six faces, deepest supported addresses, duplicate calls and concurrent
callers. Generation and rendered content are unchanged; the previously suppressed work can complete.

Build 15 compiled cleanly and both `SurfaceStreamingTests` passed. The same location then reached readiness in
`profile-idle-3` and `profile-chop-3`: zero missing plants, zero pending jobs, all five streaming subsystems idle.
Both runs started measurement after the unchanged two-second stability gate and exited successfully. This confirms
the fix resolves the observed preparation blocker; no readiness gate was relaxed.

## First measured idle/chop runs (15 October)

Both runs used build 15, 1600×900, High, RTX 2070 SUPER, in-process host, name `RepairProfile`, initial latitude
−35.795848/longitude −142.695460, hour 10, clouds 0.25, mute, fresh databases and a 20-second measurement. The tour
selected pine `11673363219493290563` at `(-798.76935,-722.16473,661.1259)` in both runs. Screenshots were suppressed
inside the window; the camera stayed at the tree's hit point, with only weak-spot aim corrections during chopping.

| Run | Frames | Mean | p99 | Worst | Mean GPU |
| --- | ---: | ---: | ---: | ---: | ---: |
| `.artifacts/profile-idle-3.log` | 1651 | 12.12 ms | 27.67 ms | 41.30 ms | 11.49 ms |
| `.artifacts/profile-chop-3.log` | 1488 | 13.44 ms | 30.26 ms | 38.87 ms | 12.74 ms |

The chopping comparison is **provisional**: a separate headless Godot import, not launched by this profiling task,
started at 22:52:35 while the chopping process ran 22:51:58–22:53:06. Its parent command copied this project's import
cache into another working directory and wrote `terrain-import.log`. A clean comparison must exclude that competing
CPU/disk workload. These numbers must not be presented as a measured improvement or as general gameplay FPS.

Within the chop log, seven nonterminal hits each rebuilt all 740 nearby plant instances, uploading 49.1 KB of managed
buffers in 0.17–0.30 ms. They also rebuilt the outcrop tile (~0.15–0.17 ms after the first hit), although the tree's
stage/presence did not change. The eighth hit felled the tree and correctly rebuilt 739 remaining instances. The first
hit recorded 4.25 ms changes, 1.08 ms redraw, 1.22 ms outcrops and 0.89 ms markers; later accepted-hit processing was
much smaller. No new pipeline compilation was recorded in the measured hit frames. The worst frames had no gather
events, so this evidence does not establish the cause of every reported freeze.

The bounded candidate is to retain every hit cue/weak-spot update while invalidating vegetation on presence changes
and outcrop models on presence/stage changes. It should remove the seven unnecessary uploads, not promise a large
overall frame-rate gain. Verify actual fall, respawn and newly streamed depleted overrides, then repeat a clean pair.

## Candidate staged after the first profile

`NatureView` now passes every changed nearby object through the existing hit/cue/weak-spot path, then compares only
its rendered presence and model stage with `NaturalRenderState`. Generated intact stage zero is the initial state,
so first streamed depleted/staged overrides still invalidate what was drawn. Healthy plants and ordinary tree hits
keep no extra tracker entry. Plant buffers rebuild only when standing presence changes; model tiles rebuild only
when their presence or visible stage changes. Picking bushes still changes their plant representation to bare and
regrowth restores it. Movement continues to build newly visible tiles from the complete current natural state.

The standing tree remains hidden while its separate falling presentation exists. A normal fall ending while the
tree is still depleted needs no second upload. If the server respawns it early, ending the fall explicitly queues
the standing copy's return. Async distant-impostor refreshes also retain a follow-up request when another presence
change arrives during their worker job, instead of dropping that newer state.

Four engine-free `NaturalRenderTests` exercise actual natural overrides/stage calculations, depletion and respawn,
real berry picking/regrowth, first far/near streamed overrides, repeated health/weak-spot updates, coalesced updates,
and normal versus early-respawn fall completion. Build 16 passed, and all four render-state tests plus the two
scatter-request regressions passed (6/6).

The build-16 candidate run `.artifacts/profile-chop-4.log` reached the same ready state and same pine as the baseline,
completed eight accepted hits, awarded the same 81 wood and exited successfully. Hits 1–7 retained wood-hit effects,
weak-spot effects/cues and the X streak, with **zero plant-buffer uploads and zero outcrop-tile rebuilds**. Hit 8
recorded depletion, a falling tree and exactly one replacement of the near set with 739 standing instances. The
paired frame log repeats each event alongside its following frame; those repeats are not additional uploads.

This validates the bounded reduction in repeated work. It does **not** validate an overall frame-rate improvement:
no other Godot process existed immediately before launch, but a separate `GeologyReview2` screenshot process
(port 27381, `geology-cliff-review.png`) started at 23:07:39 during this profile and remained running after it exited.
No process belonging to that other task was stopped. The candidate reported 620 frames, mean 32.27 ms, p99/worst
150.00 ms and mean GPU 12.33 ms; repeated 150 ms deltas and 867 server ticks during the nominal 20-second window make
the competing-workload limit especially clear. These timing values are retained for audit, not used for an FPS claim.
The first hit's cue/marker preparation still cost 11.6 ms in total in this run; it is a separate remaining profiling
candidate. A clean before/after timing comparison and the wider cause of intermittent stutter remain unverified.
