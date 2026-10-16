# Loading and shadow budgets — 16 October 2026

The owner reported a stall at 97% and much lower frame rate than the early project (80–144 FPS,
possibly using Low), then requested proper shadow settings and a maximum shadow-light budget.

## Confirmed causes and changes

The final loading stage required 0.75 continuous seconds below 33.4 ms per frame with no deadline.
A fully ready game rendering below 30 FPS could therefore wait forever at 97%. It now gives up
waiting for smooth frames after five seconds, **only after every dependency is ready**. This does
not bypass asset loading. Regression tests cover the actual 97% plan position, slow frames,
unfinished dependencies, once-only timeout reporting, reset and normal smooth completion.

The reported session also logged a null scene-tree error in gib warm-up. Loading now owns those
preview nodes directly; it no longer asks a deferred live gib singleton for its scene tree.

Shadow settings now separate texture/filter quality, sun cascade count (1/2/4), directional
distance, maximum local shadow lights (0–8), and local influence distance. Low/Medium/High/Ultra/
Maximum use 1/2/2/4/4 sun cascades and local caps 0/1/2/4/6. The moon retains one cascade and a
maximum 300 m reach. Existing saves without the new cascade setting keep four until changed.

One shared local-light registry handles authored ship fixtures, attachment beams and torch light.
Selection respects active camera/world, visibility, energy, influence distance and priority;
the held light wins over ordinary lights. A small distance hysteresis avoids switching equally
near lights every frame. Shadow limits preserve illumination, and freed owners unregister.
The local shadow atlas is disabled when its budget is zero and bounded at 2048 otherwise.

Actual baked LightmapGI shadowmask is not implemented. This changing generated world has no
static lighting bake that could remain correct through moving sunlight or building destruction.

## Measurement method

RTX 2070 SUPER, standalone Godot 4.7.2 .NET, 1600×900 window, VSync/cap disabled. Each ground run
hosts its own server using a copy of the AB Test B save and the same player position. Original
saves and graphics preferences are not written. Camera yaw/pitch are zero, universe time is fixed
at `2026-10-16T10:00:00Z`, cloud drift is reset, input is isolated and all streaming must be ready.
Each sample is eight seconds after a two-second settle; screenshots are outside measured frames.
Other Godot/Blender work was coordinated and stopped. This is a stationary forest sample, not a
moving-world or multiplayer performance guarantee, and no original early-project capture exists.

`--environment-route <folder> --environment-breakdown` measures a baseline and temporarily removes
individual layers. `--environment-breakdown baseline` measures only the baseline; `shadows` measures
cascade/distance previews and restores the baseline. Reports retain raw frame/render samples,
settings, camera pose, actual caps, primitive/draw counts and readiness. Temporary previews reapply
uncapped timing after graphics callbacks. Cancelling input isolation now restores GUI/window state.

## Findings

The saved Custom configuration is much heavier than Low: SDFGI enabled, terrain detail160%, plant
distance140%, grass distance160%, model detail200%, render scale100%. Low is render scale75%,
reduced detail and no SDFGI/SSAO. Differences between these presets are not optimization gains.

The first Custom ablation baseline ranged from15.07 to16.52ms (60.5–66.4FPS), GPU14.33–16.03ms,
render CPU about2.2ms. Removing all grass saved roughly2.4ms GPU; shadows and SDFGI also cost time.
These removals are diagnostic only; grass and lighting remain in the game. Baseline drift means
small changes in the ablation pass are not precise attributable savings.

The same-binary Custom shadow comparison kept every non-shadow setting unchanged:

| Sun shadow setting | Mean frame | GPU | Draw calls | Submitted primitives |
|---|---:|---:|---:|---:|
| 4 cascades, 1,000 m, start |16.40ms|15.74ms|1,561|7,293,569|
| 2 cascades, 1,000 m |15.73ms|15.17ms|1,161|6,175,982|
| 2 cascades, 250 m |16.44ms|15.94ms|757|5,506,248|
| 1 cascade, 150 m |15.66ms|15.17ms|660|5,221,974|
| 4 cascades, 1,000 m, restored |16.43ms|15.94ms|1,561|7,293,569|

The controls reduce submitted shadow work, but this scene remains GPU-bound and shadow tuning
alone does not restore 80–144FPS at the saved Custom settings. A trial spatial tree-batching change
was rejected:15.07→15.36ms with more draw calls and only32,444 fewer primitives, no demonstrated win.

Final Low measured **185.0 FPS** (mean5.41ms, median5.35ms, p95 6.31ms, GPU4.48ms), with443 draw
calls and1,703,985 submitted primitives. A preliminary Low run measured189.8FPS. These are absolute
measurements of the new Low setup, not a before/after estimate against the original early project.
The final copied-save load reached play in19.91seconds and had no loading error.

## Validation and remaining limits

Final build, full-suite results and final Low measurements are recorded in DEVELOPMENT.md and the
[evidence folder](images/repairs/performance-2026-10-16). Engine checks passed59/59, including real
FxDirector torch registration, cap/priority changes, illumination, cleanup and capture-isolation
restoration. The SHADOWS UI screenshot is inspected and readable at1600×900. Shutdown texture/ObjectDB leak warnings still occur in ground runs; these predate
this repair and remain unresolved. This pass does not claim to finish all dense-canopy optimization.

Final full suite:397/398 passed; the existing melee hit-marker timing test failed to start its swing,
then passed its isolated rerun1/1. No related production/test behavior changed in this repair, and
this is not reported as a clean full-suite pass. The solution build had zero warnings/errors.

Reproduce the engine-level light lifecycle check without a save:

```text
Godot --path game -- --shadow-budget-check <report.json> --shot <preview.png>
Godot --path game --resolution 1600x900 -- --view ui --screen settings-shadows --shot <settings.png>
```
