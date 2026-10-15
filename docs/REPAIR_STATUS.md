# Quality repair campaign

Current requirements: [17 owner reports](reports/current-17-issues.md), plus
[30 inherited reports](reports/legacy-30-issues.txt). The old investigation is a source of hypotheses,
not evidence that a bug remains or has been fixed. Campaign starting baseline: commit 27487dcf, 298 passing tests.
Latest compiled checkpoint: build 18, zero warnings/errors; Release server also builds cleanly.
The shared full run passed 384/384 on build16. Subsequent renderer/capture changes built cleanly and the final
attachment/render-state/streaming focused run passed 8/8. Visual acceptance is recorded separately below.

Owner priority, 16 October 2026: **audio first**. The source/routing work and engine audit did not finish sound
design or listening acceptance. The owner also confirmed grass is present, while rejecting the overall visual
quality. The current-build restored Maximum/night session was verified; it differs from prior controlled
High/daylight captures. A moonlight/cloud-shadow correction is staged only in `.artifacts/cloud-shadow-stage`,
not installed or validated. The copied-save follow-up capture disconnected and is not acceptance evidence.
The complete continuation prompt, including all 17 current and 30 legacy reports, is
[HANDOFF_AUDIO_FIRST_2026-10-16.txt](HANDOFF_AUDIO_FIRST_2026-10-16.txt).

| Package | Owner | Reports | State |
| --- | --- | --- | --- |
| Ship motion, mouse control, all-seat orbit camera, fuel inventory | engine_reuse_audit | R1, R3–5, L27–29 | Simulation, persistence and strict prediction regressions pass. Corrected actual ship tour passed fuel opening/filter, boarding, cockpit/V/drag/zoom, W throttle, airborne exit and unattended powered flight. Passenger/turret camera isolation still needs its live check. |
| Terrain, vegetation, water, resources and monuments | survival_progression | R2, R6, R10–11; L3 | Four ores, scanned cliffs, six monument families and R01 substation installed. Controlled build14 captures accepted the bounded reduction in dry-ground sheen and grass coloring while retaining coverage; underwater underside accepted. Large legacy grass blades, coarse trees/rocks and excessive night stars still limit realism. |
| Inventory rotation, H latch, protection stats, radial text, display modes | client_quality | R7, R15, R17; L20, L25 | Implemented; expanded actual UI input check passed 47/47, including the live attachment action provider, and all three native window modes passed. |
| Held poses, actions, bow, animation editor | coordinated “Clarify the problem” chat | R8; L16, L18, L22 | Final held/bow/rifle presentation accepted by the coordinated review. Both bodies passed 175 actions / 2,762 sampled poses each; all 56 rifle reload frames inspected. Optic ADS is a separate new integration gate. |
| Salvaged axe | coordinated “Add salvaged axe” chat | Additional owner request | Integrated model, icons, recipe/research/loot, audio and held profile; eight focused checks and actual captures passed. |
| Immunity, drowning, animal frames/attack feasibility | root | R10, R14, R16; L30 | Survival/animal/network regressions pass. Herd and chasing-pack captures show upright, grounded animals on the planet frame; this is not final animal art or every attack scenario. Original native crash cause is not proven without its stack trace. |
| Noclip speed and persistent F1 bindings | engine_reuse_audit | R12; L29 | Implemented; admin permissions, input and prediction checks passed. |
| Sound library and surface footsteps | client_quality | R9; L19, L21 | All 263 source hashes audited; contact/frame routing corrected and 22 dedicated action variants added. Engine check loaded 235 events/783 files with zero problems. Audition and recorded animal voices remain. |
| Building placement, support and meshes | engine_reuse_audit | L2, L4–11, L14 | Structural support, placement, network and fit regressions pass. Actual stone hut and 45 LOD0 joint views across all five grades captured; 73 diagnostic images retained. Solid-grade joints are visually continuous. Twig aliasing/residual flicker and targeted window/stair/roof/placement checks remain. |
| Bed names and draggable attachment sockets | client_quality; engine_reuse_audit optics/light | L12, L17 | Four attachment and three rename integration tests pass; actual UI47 and inspected captures passed. Redundant live REMOVE action is filtered. Build17 actual holo/scope ADS and true-night light off/on/detach passed and six frames were visually accepted; full rear field, native eye and both hands verified. Final focused 8/8 includes both attachment tests. Male/female base-gun regressions each pass 12 actions / 200 FPS/TPS samples. [Optics evidence](REPAIR_ATTACHMENTS.md). Friend-assignment action has not been added. |
| Player corpse lifecycle | engine_reuse_audit | L15 | Eight integration and three lifecycle tests pass, including roof/deployable/ship support, exact loot, harvesting, restart and late join. Build15 live death/respawn/body/loot/harvest/health-bar/same-container backpack flow passed and frames were accepted. |
| Common damaged-target health bar | client_quality | L24 | All three target-health tests and UI-lab layout pass. Build15 actual melee harvesting acquired a damaged corpse and displayed the shared bar; its frame was accepted. Other target families retain shared-rule test coverage, not blanket visual acceptance. |
| Surface streaming readiness and capture isolation | client_quality; root | Shared validation infrastructure | Fixed full/tree-only scatter-request collisions with cube-face bits; two regressions pass and the previously stuck teleport now settles in actual profiles. Standalone screenshot input/window/session isolation enabled controlled environment evidence. |
| Tree-hit stalls and feedback delay | client_quality | L23; L3 performance | Build16 removes evidenced redundant redraws: seven ordinary hits produce zero plant uploads/outcrop rebuilds; terminal hit retains one upload, cues, weak spots and fall. Four render-state plus two streaming tests pass. Competing external renders invalidate overall FPS comparisons; first-hit setup and wider stutter remain open. |
| Whole-world map, vehicle workbench, rocket trajectories | Existing shared systems | L1, L13, L26 | Whole-world projection/image tests, specialized workbench network test and regular/HV rocket simulation tests pass in the shared full run. Current live map/station/rocket presentation needs revalidation, not speculative rewrites. |
| Legacy-report audit | engine_reuse_audit | R13 | All 30 legacy and 17 current reports mapped to implementation/evidence or the remaining work below; this is an audit, not closure of all reports. |
| Snapshot bandwidth integration | root | Shared regression | Exact generated state/header sizing; original packet budget and priority service requirement preserved. Network tests pass. |

Do not call an item complete based on compilation or data-file presence. Verify simulation behavior,
network/persistence behavior where relevant, and rendered/interactive behavior for presentation changes.
Root coordinates builds, imports and captures because all agents share this checkout.

## Evidence and limits

- Baseline: 298/298 before the repair campaign.
- A shared full run after the first integration wave passed 330/334. Its four failures were architecture host size,
  faster-hull turret stabilization, a resource validation fixture, and snapshot-budget accounting. Each was repaired;
  the subsequent focused run passed **68/68**, including all selected network suites, ship fuel, attachments,
  building fit, content and architecture. Later full and focused checkpoints are listed separately below.
- The next focused run passed **6/6**: three rename authority/persistence/network tests and three staged corpse-rule tests.
- The earlier shared full run passed **367/369** in 414.4 s (`.artifacts/repair-full1.log`). Its two failures were
  the old one-phase corpse lifetime fixture and a target-health fixture expecting a procedurally present barrel.
  Both fixtures were corrected without weakening their behavior assertions. The following coordinated build and
  focused run passed **25/25** (`.artifacts/repair-focused7.log`), including those two cases, all eleven corpse
  lifecycle/integration cases and all three target-health cases. The subsequent shared full run passed
  **376/376 in 418.2 s** (`.artifacts/repair-full2.log`). Build 13 passed with zero warnings/errors.
- The separately integrated optics/light package passed **2/2** new tests after build 13
  (`.artifacts/repair-attachments-focused.log`): every GLB optic LOD has a real unobstructed aperture, and two clients
  agree on fitted optics/lights through attachment, detachment, holster and death. This does not establish rendered quality.
- Actual UI input passed **47/47** on build15 (`.artifacts/repair-ui-check3.log`), expanding the earlier 45 checks
  with the live attachment action provider so redundant REMOVE actions cannot hide behind a mock. Attachment
  drag, bed-name input, inventory rotation, H transfers and map gestures pass. Name, attachment and target-health
  screenshots are UI-lab evidence; the separate actual corpse bar capture below supplies in-world acquisition evidence.
- `.artifacts/repair-ships-tour1.log` had false-positive V/drag assertions and a wheel failure; those claims remain
  discarded. The corrected `.artifacts/repair-ships-tour2.log` passed actual focused/captured input: initial cockpit,
  V into orbit, independent yaw/pitch drag, wheel distance 6.00→7.06 m, W throttle without boost, airborne Space exit
  and retained powered motion. It also reconfirmed fuel-cap opening/filter and boarding, with actual screenshots.
- `.artifacts/repair-corpses-tour1.log` stopped at loot opening and was not counted as complete. The corrected
  `.artifacts/repair-gates15.log` passed death, respawn, body loot, real melee harvest, shared health bar, same-entity
  backpack and preserved loot. Root accepted the six retained frames in `docs/images/abtest-b/repairs/corpses`.
- Build15 actual holo ADS passed native-eye error 0.0000 m, axis alignment 1.000000, material and real lens/body
  ray checks; its image was accepted. Scope ADS failed at 0.0646 m eye error because the arm reach solver moved the
  aligned gun. A fitted-optic-only shoulder accommodation is prepared for build16, with native-eye and 25 mm hand
  contact guards; scope and night-light acceptance remain open.
- A separately arriving, untracked `tests/Hollow.Tests/ShipThrottleTests.cs` adds four high-frame-rate lever/wire
  regressions and preserves the already implemented `PilotSteering.AdjustThrottle` production fix. Its accompanying
  build and **46/46** Ship/Architecture run passed (`.artifacts/ship-throttle-build.log`,
  `.artifacts/ship-throttle-tests.log`); these files arrived after the shared full run. Their writer is not established
  by repository inspection. This is additional focused evidence, not another full run or live cockpit keyboard test.
- Five real world frames cover daytime stream, underwater, scanned cliff, supplied substation and night stream.
  They verify concrete corrections, not final environment quality. They exposed uniform painted vegetation,
  underwater horizon artifacts, coarse legacy trees/rocks and an over-bright star field; follow-up status is below.
- The follow-up `.artifacts/environment-underwater-underside.png` was accepted for its continuous rippled underside.
  The later controlled build14 captures `environment-stream-day-material-locked`, `environment-stream-day-bare-before-locked`
  and `environment-stream-day-bare-after-locked` passed readiness at matching 1600×900/window/camera/player settings.
  Root accepted reduced dry-soil sheen, restrained grass color and retained coverage as a bounded material checkpoint.
  The earlier mismatched/uncontrolled before/after captures remain invalid evidence. Preserved images and input hashes
  are listed in [REPAIR_ENVIRONMENT.md](REPAIR_ENVIRONMENT.md); large legacy base blades, coarse trees/rocks and excessive
  stars prevent a final realism claim. The engine sound audit (`.artifacts/repair-sound-check.log`) loaded 235 events
  and 783 files with zero problems; it is not a human listening acceptance of the mix.
- Build15 fixed a proven vegetation request collision: full-scatter mode occupied an existing cube-face address bit.
  Two `SurfaceStreamingTests` pass, and `.artifacts/profile-idle-3.log`/`profile-chop-3.log` reached the same tree with
  every streaming subsystem idle, replacing the 90-second readiness failure in `profile-idle-2.log`.
- Build16 compiled with zero warnings/errors (`.artifacts/repair-build16.log`). Four `NaturalRenderTests` plus the
  two streaming regressions passed **6/6**. `.artifacts/profile-chop-4.log` confirms seven ordinary hits with no plant
  uploads or outcrop rebuilds, preserved FX/weak-spot cues, and terminal depletion with one 739-instance upload,
  falling tree and unchanged 81 wood yield. Depletion/respawn, model stages, streamed overrides, berry regrowth and
  early-respawn fall completion also have engine-free coverage. An external import overlapped chop3 and an external
  `GeologyReview2` render overlapped chop4, so no whole-run FPS improvement is claimed. See
  [REPAIR_PERFORMANCE.md](REPAIR_PERFORMANCE.md) for timing values and limits.
- The final shared full run passed **384/384 in 429.0 s** (`.artifacts/repair-full3.log`, build16). Build17 and build18
  subsequently passed with zero warnings/errors; the Release server build also passed. The final focused run passed
  **8/8** (`.artifacts/repair-final-focused.log`) after the renderer changes.
- Build17's actual optics tour passed and was visually accepted: full rear-lens world image, native eye and hand
  contacts, reticle, light on/off/detach, verified midnight (sun elevation −63.98°) and ADS render shutdown. Both
  bodies also passed the base pistol/rifle editor regression (12 actions, 200 FPS/TPS samples each). Six accepted
  frames and provenance are under `docs/images/abtest-b/repairs/attachments`.
- All nine supplied salvage/container models were captured in Godot and visually reviewed. Their native frames and
  contact sheet are under `docs/images/abtest-b/repairs/assets`; this establishes integration, not completion of
  every other supplied environment package or every asset's final art pass.
- The building corner-tile retention fix made its previously stalled tour complete in 18.1 s. All 73 expected images
  decoded successfully. The static control was unchanged; camera perturbations changed 134,693 pixels, dominated
  by thin twig silhouettes. This is not a zero-flicker claim. See `REPAIR_BUILDING.md` for inspected scope and limits.
- The final build18 wolf capture passed actual readiness with all four wolves chasing at 15.7–17.9 m and no fallback.
  Its framing and the earlier herd capture were inspected for grounded radial orientation. These remain the inherited
  animal meshes; high-quality anatomy/textures and additional attack scenes are separate work.
- The prior legacy investigation is not current truth. The HV/regular rocket trajectories already differ in data
  and have specific live-simulation tests; the vehicle workbench already opens a real technology tree. These should
  be revalidated in the game instead of being rewritten from the historical report alone.
- No claim is made that the whole game/design document or all 47 reports are finished. Hit-frame performance,
  subjective weapon/audio feel, broad environment art direction and final runtime regressions remain tracked work.

## Remaining work, in priority order

1. **Finish the audio listening and action-feedback pass (R9/L19/L21).** Source hashes and event routing are
   checked, but hit/headshot prominence, timing and overall weapon feel have not been accepted by listening.
   Animal voices remain synthetic; species-specific recorded replacements, exact surface contacts and broader
   per-action listening remain outstanding. Use real gameplay recordings, not file-loading success, as evidence.
2. **Continue the environment art pass.** Dense coverage and varied authored plants, reduced dry-ground sheen,
   underwater corrections, resources, cliffs and monuments have accepted implementation captures. Large flat legacy
   blades, coarse tree bark/crowns, legacy rocks, repetitive arrangement and excessive night stars still limit realism.
   Retain coverage while improving those assets and their biome placement. The full suite, nine supplied item/container
   captures, corpse flow and actual optics gates above have passed; they do not establish finished environment quality.
3. **Isolate the remaining tree-hit/terrain stalls (L23).** Redundant ordinary-hit buffer uploads are removed and
   actual hit/depletion behavior is verified. First-hit cue/marker setup still needs clean profiling, and unrelated
   worst-frame stalls remain unexplained. Repeat idle/chop timing without competing imports/renders before making
   any overall performance claim. The reported 30–80 FPS terrain concern has no accepted before/after measurement.
4. **Continue weapon feel (L21).** Socket drag/network authority and actual fitted holo/scope/light presentation are
   verified; see [REPAIR_ATTACHMENTS.md](REPAIR_ATTACHMENTS.md). Recoil recovery remains inherited. Replacing it or
   claiming pre-2022 Rust feel requires a deliberate gameplay/art/audio pass, not an inventory test. Scope rendering
   is bounded to one 512² viewport while aiming; its cost under broader combat scenes remains to be measured.
5. **Close remaining gameplay visual/input gates:** targeted building windows, stairs, roofs, all LODs and placement
   previews (L2/L4–11/L14); animal attacks on foundations and across additional planet frames (R16);
   passenger/turret orbit/zoom isolation (R5/L28). Actual damaged-corpse HUD acquisition, complete
   death/respawn/harvest/backpack flow, and the pilot/unattended-flight live sequence have passed. Simulation passes
   remain valid while the remaining presentation checks are open.
6. **Retain the smaller legacy gaps explicitly:** L10's arbitrary positional snapping to every nearby wall/object
   is not implemented; current improvement is measured fit and support-relative rotation. L12's requested optional
   friend-assignment menu entry is absent (bed naming itself is verified). L1/L13/L26 already have implemented and
   tested behavior; use their existing live routes to recheck the report before making another implementation.

The original native crash in R14/L30 has no supplied stack trace. Central god-mode immunity and shipwreck handling
are tested; that evidence does not establish the cause or absence of every native crash.

## Building capture handoff

Use an isolated hosted ground run with `--tour-steps build,base,place` for the shortest existing actual-base pass.
`GroundTour.Build` makes adjoining foundations and walls with a doorway ghost (`build` shot); `Base` makes a stone
hut with an installed door/lock (`base`, `keypad`); `Place` covers valid/refused furnace fit, garage-door preview,
cupboard and code-lock ghosts. These steps use the normal client/server and placement pipeline. `look` additionally
captures a raised free foundation, and `interact` captures an actual garage opening/closing. The step named
`foundations` tests code-lock/cupboard interaction gates, not foundation geometry.

None of these hosted shots specifically looks inside a window, across stacked floor joints, at three triangular
door bays, or at a campfire/workbench fitted against nearby pieces. Do not treat the exterior hut as complete mesh
proof. Existing `--view build --scene house` supplies a mixed-grade two-storey house with windows and a triangle
porch for close camera inspection; `--view build --scene joints --tour <folder>` supplies all five grades' joint
views and a moving-camera flicker comparison. These are useful mesh diagnostics, separate from the hosted pass;
the exact triangle-middle-lock and deployment-fit cases still need targeted live coverage.

Details: [ships](REPAIR_SHIPS.md), [survival](REPAIR_SURVIVAL.md), [client](REPAIR_CLIENT.md),
[assets](REPAIR_ASSETS.md), [resource nodes](REPAIR_RESOURCE_NODES.md), [environment](REPAIR_ENVIRONMENT.md),
[audio](REPAIR_AUDIO.md), [building](REPAIR_BUILDING.md), [network](REPAIR_NETWORK.md), [corpses](REPAIR_CORPSES.md).
