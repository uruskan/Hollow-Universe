# P2 — Terrain materials and vegetation

Status: the reviewed two-file grass patch passed its solution build and all seven focused
checks. After the preserved r2 assembly-location failure, the separately reviewed r3
harness passed its scratch build and one native run: **287 assertions, fourteen captures
and 84 controlled draws passed**. Both loaded module bindings, production mesh readbacks,
native shader TIME control and seven raster comparisons passed. All 10,395 input pins
and 193 previously published artifacts remain unchanged.
**Visual quality, full-world and GPU performance acceptance remain open.** Closeups show
angular card silhouettes and speckled tip fragments; these are explicitly handed to the
coordinator for review. The heavy slot is released. Prior evidence is preserved.
Coordinator: `01a14455-2b1c-7e62-be75-bc958fc1779a`.
Worker: `01a1447b-26d5-7211-aeed-ec8f7e9c4dc8`.

## Scope and ownership

This package addresses N3/N6 and environment portions of N5/N12, R2/R6/R10 and L3.
It preserves the ground UI, terrain heights, terrain save identity and source scan channels.
P1 owns cliffs/scanned-rock presentation and placement. The Earth-area worker owns terrain
generation. Shared settings, Scatter and FloraLibrary.cs require transfer.

Prospective owned files: GroundLayers.cs, GrassField.cs, GrassField.Authored.cs,
FloraLibrary.Trees.cs and terrain/grass/authored-cover/authored-tree/foliage/water/underwater/
impostor shaders. Subagents own disjoint subsets. Initial files were clean and copied with
SHA-256 hashes to `.artifacts/p2-environment/baseline-source/manifest.json` before edits.

## Initial evidence and findings

Original owner screenshots 3, 4 and 8 were inspected. Screenshot 4 shows recurring gravel
stones; the monument pad selects the gravel scan at four metres per repeat. The shader's
dominant fixed native repeat and weaker fixed rotated copy retain that periodic pattern.
Terrain cube-face coordinates do not reset per chunk. This supports a texture-periodicity
diagnosis; it does not claim every possible terrain seam has been excluded in gameplay.

The native albedo/normal arrays retain 4K channels and roughness/height remain packed at 2K.
The candidate will preserve physical material scale and use matching sampling for PBR channels.
Existing rotated normal samples need to be transformed back into the surface basis.

Water refraction currently offsets screen coordinates with cube-face wave components.
Those axes change relative to the camera. A narrow candidate transforms the existing
world-space ripple tilt into the camera frame. Its visual acceptance remains pending.

Original screenshot 8 also exposes repeated procedural broadleaf bark and coarse branch
forks. Earlier captures retain medium/far foliage silhouette and brightness differences.
These are open art issues; a shader correction alone cannot close them.

## Validation plan and current limits

A coordinated baseline build and hosted route slot has been requested. No P2 runtime,
build, import or performance claim has been made. Candidate work is initially staged
outside live game sources while waiting. The current repository has no `.game-dev/adapter.json`;
the existing hosted EnvironmentRoute harness provides the game-owned evidence workflow.

Historical environment captures used older Earth geography. Earth revision 2 now selects
the wellhead route (786.5 m), starting at latitude -30.486486, longitude 43.059333. Its
water and cliff stops currently share an ocean-edge position; actual traversal and useful
view coverage must be checked. Old-route timings cannot serve as a matched baseline.

Required evidence remains: real hosted ground/elevated day/night views, slopes and biome
coverage; root/LOD/material/shoreline inspection; same-settings raw CPU/GPU/draw and frame
mean/median/P95/P99 samples; solution build and appropriate focused tests. Coordinator
schedules the final integrated suite. Numeric checks do not constitute visual acceptance.

## Recovery checkpoint — 16 October 2026

Lead-only recovery follows `docs/repair-tasks/RECOVERY.md`. All thirteen live P2 source files
match their pre-crash baseline hashes. The terrain candidate (`0D73A7B8…`) and water candidate
(`E21D56C2…`) survived intact, along with their earlier CPU diagnostic reports. Those reports
are retained pre-crash results, not newly executed validation.

Three recently written scratch files contained only NUL bytes after the crash: Launch-Route.ps1
(2,662 bytes), staged-route/EnvironmentRoute.cs (21,775) and EnvironmentRoute.Details.cs (1,532).
Copies are retained under `.artifacts/p2-environment/crash-nul-files/`. The live route source was
intact. The scratch launcher and diagnostic changes were reconstructed; no user files or other
owners' changes were reverted. No grass candidate had reached disk before the crash.

The coordinator transferred EnvironmentRoute.cs/Performance.cs, EarthEnvironmentRoute.cs and
EnvironmentRouteTests.cs to P2. The selector now uses P1's semantic `CliffPlacement.Downslope`,
with an exposed-face assertion. Opt-in `--environment-detail-views` adds a ground-level stationary
CPU/GPU/draw/frame sample and explicitly labelled root/elevated views at the walked checkpoints.
Camera lift, pitch and frame limits are restored. These source changes have not been built/run.

The staged grass candidate separates authored lean/curl from root spread in UV2, scales growth
and wind with live blade height, removes a second height reduction, conforms card roots to the
sampled triangle plane, and tapers the analytic blade tips. Authored crowns receive local ground
gradients for root conformance and retain their source crowns. The former additional 25–30 mm
burial is reduced to 5 mm; source negative pivots remain intact. Cell/density/card counts, cover
budgets, source textures and gameplay terrain are unchanged by this candidate. Actual visibility,
root/LOD quality and cost are unvalidated; it remains outside the live game files.

Next bounded job requested: after P1/Earth source freeze, one serialized single-worker solution
and Release-server build plus focused route/architecture checks; separately, one Maximum/day
fresh-save route baseline with memory monitoring. No night/candidate runs until that succeeds.
Runtime is not granted during recovery; no build, test, import, renderer or Git-index operation
has been started by P2. Broadleaf/bush art, medium/far silhouettes and night/shoreline quality
remain open and are not claimed as fixed by these narrow staged repairs.

The coordinator's integrated build attempt at 15:35 was stopped by its memory guard after
34.69 seconds with 1.566 GiB available RAM (74% committed). The log
`.artifacts/recovery/integrated-checks/20261016-153554-build.stdout.log` contains intermediate
DLL output but no completed solution summary. This is **not a build pass**; no subsequent
test or GPU validation occurred. P2 source and staged candidates have not changed since
the submitted source checkpoint; this note is the only P2 change following that attempt.
Runtime remains ungranted, with no automatic retry and no use of partial outputs as validation.

## Maximum/day baseline attempt — 16 October 2026, 20:18 TRT

This supersedes the earlier pending-build/runtime statements above. The coordinator reported
the shared `20261016-194953-build` passed with zero warnings/errors and the
`20261016-195242-foundation` rerun passed 64/64 after the P1 and clan-base fixture repairs.
P1 geometry was frozen, and one exclusive bounded native baseline slot was granted.

The exact invocation was:

```powershell
& 'D:\GameDev\Projects\HollowUniverseABTestB\.artifacts\p2-environment\Launch-Route.ps1' -Label baseline-max-day-r3 -Latitude -30.486486 -Longitude 43.059333 -Quality maximum -Hour 10 -Port 27182 -InProcess -MaxSeconds 480
```

The freshly built Debug client/server used actual localhost networking, a fresh isolated
`baseline-max-day-r3.db`, native 1600x900 Forward+ on the RTX 2070 SUPER, and unchanged
visual sources. All thirteen P2 baseline hashes still match. No candidate was installed,
and no build, test, import, second native job, agent or Git-index operation ran in this slot.

**The attempt is incomplete, not a baseline or visual/performance pass.** Initial free
memory was 7.651 GiB / 48% commit. During initial loading, the server logged the player's
`Timeout`; the client later logged `PeerNotFound` and remained disconnected with loading
active. Before any ready checkpoint, a secondary memory check observed approximately
1.80 GiB free. The lead verified PID 25208's executable path and stopped only that owned
Godot process tree. Total launch duration was 49.79 seconds. The watchdog's approximately
2.4-second samples reached 2.034 GiB / 83% commit; the secondary check caught the lower
value between samples. Its original receipt therefore has `stopReason: null` and engine
exit code -1. `intervention.json` records the external stop without rewriting that receipt.

Evidence is in `docs/images/repairs/p2-environment/baseline-max-day-r3/`: `launch.json`
contains arguments and source/runtime hashes; `stdout.log`, empty `stderr.log`, `memory.csv`,
`receipt.json` and `intervention.json` retain the failed attempt. There are **zero screenshots,
no environment-route.json, and no CPU/GPU/frame profiles**. The route was not selected or
walked; no beach/woodland/root/elevated/water/cliff/monument coverage can be accepted.
The latest tested route's planned length was 628 m, superseding the older 786.5 m figure.

Read-only diagnosis establishes a 10-second disconnect timeout on both client and server.
The client's manual network poll/update runs in `GroundView._Process` via `GameClient.Update`;
therefore a long main-thread loading stall can prevent network servicing. The logs are
consistent with this failure mode, but do not identify the exact stalled call or memory
allocator. The ground texture-array log reported 3.49 seconds; that alone does not explain
the timeout or establish the cause of the total memory growth. No timeout or quality limit
was relaxed. A loading-stage/memory diagnosis is needed before another baseline attempt.

Cleanup verified no remaining Godot/server/build/test process and UDP 27182 unbound.
Free memory recovered to 7.275 GiB / 50% commit. The heavy slot is released; no retry,
candidate or night run was started. All earlier visual issues and candidate acceptance
requirements remain open.

## Renderer startup review — read-only follow-up

No further native process or test was started. The original failed-run receipt and intervention
supplement remain intact. This review changes only this report and scratch inventory/estimates.

### Route/view duplication ruled out in the inspected path

`Main._Ready` returns after adding GroundMode for `--view ground`, before creating the galaxy,
system and standalone surface viewers. `--host` calls Start directly, bypassing ShowMenu and
its background world. GroundView constructs one driven SurfaceView, with OpenWorld guarded
by its camera reference. The in-process server is engine-free and does not create another
renderer. EnvironmentRoute references that existing view; profile/detail flags take effect
only after initial readiness. Neither ran here. There is an intentional temporary offscreen
tree-impostor viewport, containing tree variants rather than a second Earth.

### Allocations identified before readiness

| Path | Source/file evidence | Limit of the finding |
| --- | --- | --- |
| GroundLayers.Load | One static set of three texture arrays. The 48 native compressed, mipmapped imports total **768.0 MiB**. Load retains all 48 readback Images while creating the arrays; these temporary Images/containers are not explicitly disposed at the end of Load. | Source textures are disposed individually, and GroundWarmup deliberately does not retain their file paths. This is transient CPU/GPU duplication and a lifetime measurement target, not evidence of two persistent layer sets or a proven leak. Its logged call took 3.49 s. |
| FloraLibrary.Build / FloraTextures.Dilate | A 2048² RGBA float atlas is **64 MiB**. Six dilation passes each clone the whole array: input plus clones allocate **448 MiB cumulatively**, before mipmaps/packing. Procedural geometry for authored replacement tree kinds is also built before Create skips those kinds. | These are source-derived payload counts, not simultaneous live memory. Collection timing and generation costs were not recorded. |
| ImpostorBaker.Bake / Finish | Current catalogue gives 11 rows: four procedural broadleaf, three authored fir, three birch, one pine. The 2048×2816 readback becomes an **88 MiB** float array. Four full clones per pass, across colour and normal, allocate **880 MiB cumulatively**, before mipmaps/byte/native copies. Two final RGBA8 atlases with mipmaps hold about **58.7 MiB** of pixel payload. | Bake completed by the final idle-state log. Its temporary viewport and meshes are queued for freeing; Image wrappers are not explicitly disposed. No per-pass memory or stall measurement exists. |
| Authored vegetation | FloraLibrary.Create synchronously loads the three tree catalogues and the rock catalogue on the main thread, duplicates meshes for shader material substitution, and retains their referenced scan textures. Ground-cover setup loads its four families and reuses native textures. Imports total trees 179.67 MiB, rocks 129.35 MiB, cover 85.87 MiB. | These are imported-file sizes, not measured allocations. No per-instance texture duplication appears in these paths. Original scene meshes and converted meshes may overlap until temporary references are released. |
| Terrain / cliffs / monuments | Final log has 417 drawn terrain tiles, five cliffs, 13 monument instances and completed grass/vegetation. Terrain retains tile grids; upload clears its temporary mesh-data field. Cliffs request nearby near/far scene resources; monuments cache by path and instantiate within 850 m. | Drawn counts are not byte counts. The entire cliff import inventory is 297.52 MiB, but the run does not identify which models were resident. Monument scene loading is synchronous and has no per-call elapsed-time record here. |
| Full-game warm-up | RenderWarmup requests six distinct files concurrently and retains loaded Resources for the game. It also retains shown warm-up nodes until its holder is freed. The catalogue includes characters, building art, items, gibs, effects, animals, ships and sounds, even when absent from this fresh scene. Its 12 ms budget is checked between items, so one synchronous item can exceed it. | This is the ordinary loading path, not added by route options. The run lacks warm-up counts/costs, so its contribution cannot be separated from world streaming. P8 owns the broader loading diagnosis. |

The two foliage dilation paths alone allocate **1,328 MiB cumulatively** in large float buffers,
excluding mip chains and other copies. This must not be reported as 1,328 MiB of live memory.
GroundMode selects SustainedLowLatency before startup; these large temporary allocations make
collection/lifetime timing worth measuring, but no GC cause is established by the failed run.
Maximum additionally enables SDFGI (six cascades at EffectQuality 2), MSAA4, SSIL, SSR and fog.
Their render-target costs are unmeasured. Settings and quality remain unchanged.

The inventory `.artifacts/p2-environment/surface-import-size-inventory.json` lists 144 imported
surface files totalling 1,460.419 MiB on disk. The separate `renderer-allocation-estimates.json`
records dimensions, arithmetic and caveats. These inventories do not attribute the observed
approximately 5.85 GiB free-memory decline to any one subsystem.

### Concrete next evidence, for coordinator/P8 ownership

The existing LoadTrace already reports world-opening phase times, warm-up costly items,
pipeline builds, GC/JIT pauses and final video/static/managed memory, but was absent from
this route invocation. **Its --load-trace duration also quits the application**, so use it
as an explicitly bounded startup diagnostic rather than silently adding it to a full route.
Stage-boundary memory/cost samples around GroundLayers.Load, FloraLibrary.Build/Create,
each impostor Finish pass, authored-cover setup and monument first loads would distinguish
the renderer candidates while preserving final pixels and the ordinary 10-second timeout.

Potential quality-preserving repairs to evaluate after attribution are explicit lifetime
management for temporary Images and reuse of a single dilation scratch buffer per job.
They are proposals only: no live code, cache policy, density, LOD, quality or timeout changed.
There is still no accepted baseline, no retry, and no candidate installation.

## Ground-layer temporary lifetime repair — source-only grant

The coordinator subsequently granted only GroundLayers.cs lifetime work, the route-wrapper
preflight update, and a scratch validation harness. This is the sole new live renderer edit;
the deferred terrain/grass/water/foliage candidates remain staged. P8's FloraTextures and
transport changes were not edited. The other twelve files in the P2 baseline manifest
still match their original hashes.

Ownership review used the installed GodotSharp **4.7.2** XML API documentation plus official
upstream implementation. [Texture2D.GetImage](https://docs.godotengine.org/en/stable/classes/class_texture2d.html#class-texture2d-method-get-image)
returns a new readback Image. [ImageTextureLayered](https://raw.githubusercontent.com/godotengine/godot/master/scene/resources/image_texture.cpp)
converts the input array to native Ref<Image> values, creates a rendering texture and retains
its RID/metadata, rather than the caller's C# array. The
[C# Array implementation](https://raw.githubusercontent.com/godotengine/godot/master/modules/mono/glue/GodotSharp/GodotSharp/Core/Array.cs)
shows that Array<T> is not IDisposable: its explicit untyped conversion returns the underlying
disposable reference container. [GodotObject.Dispose](https://raw.githubusercontent.com/godotengine/godot/master/modules/mono/glue/GodotSharp/GodotSharp/Core/GodotObject.base.cs)
releases the managed wrapper's native ownership. These upstream sources establish the intended
ownership model; only native validation against the installed engine can confirm this repair.

### Actual change and hashes

`game/scripts/Surface/GroundLayers.cs` now assembles/uploads one channel at a time. A finally
block clears each temporary native array and disposes every owned Image wrapper; the untyped
container is disposed by its using scope. Non-OK upload results throw with the channel/error.
The failed channel texture and any earlier successful arrays are disposed on failure. The
static cache is published only after all three uploads succeed, allowing a clean retry after
an exception. No image resize, decompression, conversion, pixel edit, mip generation, layer
reordering, metres/flip change or final-cache policy change was added.

The old explicitly retained readback payload was 768 MiB across all channels. A channel at a
time limits this loader's owned readback payload to approximately 341.3 MiB (16 native 4K
compressed layers). This is a reference-lifetime bound, **not a measured process-memory saving**:
the renderer/driver can retain queued upload references and copies independently.

| File/evidence | SHA-256 |
| --- | --- |
| GroundLayers.cs before | `ACDDB6A33CB56EE271B30E85F37F17E78CB226D4C9A22D4E239FE18DD8F2C2F8` |
| GroundLayers.cs after | `D79404A1C628B052976BF249F840A210B4DA89F207212F2DC5C69CFF86D5F2D8` |
| `.artifacts/p2-environment/ground-layers-lifetime.patch` | `81F80B0A510D6AE095DADAFD1E9280D6FEC604F3E323F076CCC12CD8ED3812BF` |
| `.artifacts/p2-environment/Launch-Route.ps1` | `E596D03BA2BB01E78B62661B196683CC8E24DD23079E93441385B5D05AB8BBC0` |

The production diff is **51 insertions / 13 deletions in one file**. The route wrapper now
requires **8 GiB free for Maximum** (High remains 5.5 GiB), records that floor in launch.json,
and excludes `blender*` alongside build/test/Godot processes. The less-than-2-GiB / at-least-85%
commit stops and normal game timeouts are unchanged. This wrapper has not been executed again.

### Prepared bounded validation

`.artifacts/p2-environment/layer-check/` contains exactly these authored files:
`LayerCheck.csproj`, `LayerCheck.cs`, `LayerCheck.tscn`, `project.godot`, `Prepare.ps1`,
`Launch.ps1`, and `README.md`. Their hashes, the wrapper, production source and exact patch
are in `.artifacts/p2-environment/ground-layers-lifetime-manifest.json`.

This separate project links the actual production GroundLayers.cs and uses a 128×128 native
Forward+ viewport. It does not reference the full game project or instantiate its Main,
world, server, vegetation or gameplay warm-up. Preparation creates scratch junctions to the
existing assets/import cache; **no new import is needed or allowed**. Main.cs is untouched.
Preparation itself has not been executed yet.

The proposed check verifies repeated Warm cache identity; all 48 source/array readbacks'
exact bytes, dimensions, format, compression and mip chain; public file ordering and shader
metres/flips. It then samples every uploaded layer against its original texture after the
temporary Image/container scope has ended. Red positive-control frames prove the comparison
shader draws, followed by 48 zero-difference frames. Reports retain hashes, per-layer results
and stage memory; the wrapper retains raw stdout/stderr and external memory samples.

After separate build/runtime slots are granted, the commands in the harness README prepare
the aliases, build its small standalone project with one MSBuild worker, then launch label
`layer-lifetime-r1`. The proposed isolated-native budget is **90 seconds maximum**, 5.5 GiB
starting headroom, the same 2 GiB / 85% stop thresholds, and no concurrent Blender/build/Godot.
Expected native duration is 10–45 seconds and roughly 2 GiB transient headroom, **unmeasured**.
This is not a reduction of the 8 GiB floor for a full Maximum world startup.

Static verification only: the scoped `git diff --check` passed; all three PowerShell scripts
parsed with zero syntax errors; inspected source/harness files contain no NULs. No solution
or harness build, test, import, native render, new agent, or Git-index operation ran. The
harness is not yet compiled. Native upload/read-failure injection, actual peak-memory savings,
full-world startup, and visual acceptance remain unvalidated. Existing build/test passes
precede this source change and cannot validate it.

## Isolated layer validation — 16 October 2026, 21:34 TRT

The coordinator granted one exclusive sequence: scratch preparation, a 60-second bounded
single-worker harness build, then one 90-second native check if resources permitted. No
production edit occurred during this validation. Both jobs completed; **the heavy slot is
released**. The shared solution build remains the coordinator's next step.

Before starting, the two process checks were corrected from `blender*` to exact process name
`blender`: the wildcard also matched lightweight `blender-mcp.exe` connector helpers.
No user process was stopped. The route wrapper still enforces 8 GiB for Maximum and keeps
the original 2 GiB / 85% commit stops and normal game timeouts. Current wrapper hashes:

- `.artifacts/p2-environment/Launch-Route.ps1`:
  `94BAF6A3C89722D78B0E63219D7F46D2A20E7397EBDDB386A16E9B0348759A1C`
- `.artifacts/p2-environment/layer-check/Launch.ps1`:
  `AC0242F6CC302972C7ADBD7A5F56CBBAE1A65B466C07FD042A9DDEA0554E72AF`

Prepare.ps1 successfully created the two scratch junctions to existing assets/imports; no
editor or import ran. A new scratch `layer-check/Build.ps1` captured input hashes before and
after compilation, disabled build servers/shared compilation, and used one MSBuild worker.
The build passed **zero warnings / zero errors**, with zero changed inputs, elapsed **4.17 s**
(MSBuild reported 2.89 s), minimum **6.258 GiB free**, maximum **51% commit**. Original logs,
input manifests, memory samples and receipt are retained in
`.artifacts/p2-environment/layer-check-runs/build-lifetime-r1/`.

Fresh native preflight was **6.351 GiB free / 50% commit**. The single run
`layer-check/Launch.ps1 -Label layer-lifetime-r1` used Godot 4.7.2 .NET, Vulkan Forward+ on
the RTX 2070 SUPER, in the isolated 128×128 project. It exited zero in **8.4065 s** externally
(5.555 s inside the check; array assembly logged 2.773 s), with minimum **5.188 GiB free**,
maximum **57% commit**, and no guard stop. stderr is empty.

**All 48 layers passed**: 16 albedo, 16 normal, 16 roughness/height. Every source/array raw
payload SHA matched; dimensions, formats, compression and mip counts matched. File order,
metres/flips and repeated-Warm cache identity checks passed. All 48 post-disposal shader
comparisons had zero maximum error byte, and all three red positive controls passed. Thus
the uploaded resources remain readable and sampleable after the temporary image/container
cleanup on this installed engine. This does not claim a full-world startup pass.

The exact production source hash was unchanged before, during and after validation:
`D79404A1C628B052976BF249F840A210B4DA89F207212F2DC5C69CFF86D5F2D8`.
The native harness assembly hash was
`8AACC2573713386FB7B35442CEBFAC664F720D2BBCB15B68D111376FA54A091E`.
The native evidence folder is
`.artifacts/p2-environment/layer-check-runs/layer-lifetime-r1/` (`launch.json`, `result.json`,
stdout/stderr, `memory.csv`, `receipt.json`). Its per-stage process/managed/video samples are
retained; these are samples, not guaranteed process peaks. The initial video counter was
zero before rendering had reported it, so that value is not evidence of zero allocations.

Cleanup verified no remaining Godot, dotnet/MSBuild, Hollow runtime/test or actual Blender
process. Available memory recovered to **6.555 GiB / 50% commit**. The earlier failed world
baseline receipt/supplement and the original source-only hash manifest were not overwritten.
No world startup, second native run, visual candidate installation, agent or Git-index
operation occurred. Native error-path fault injection, measured old-versus-new peak-memory
savings, the shared solution build and the full Maximum route remain outstanding.

## Maximum world route r4 — 17 October 2026, 15:46–15:52 TRT

**Technical route passed; visual acceptance remains open; heavy slot released.** One native
attempt ran against the coordinator's already-built transport, dilation and GroundLayers
lifetime repairs. No source edit, build, import, quality/LOD change, candidate installation,
new agent, process termination or retry occurred. This validates this changed-code checkpoint;
it is not an isolated before/after measurement of any one repair.

The fresh guard is `.artifacts/p2-environment/baseline-max-day-r4/Run-EnvironmentRoute-r4.ps1`,
SHA-256 `BE2A1255C2852AB6EC96EEC82CFD23FB2988DB56CA13E23681333A6F1A8957D6`.
It narrowly adapts the verified coplanar-r2 guard, retaining its exact Python service
classifier, primary/secondary error receipts, PID/start-time ownership and cleanup logic.
The command changes only the executable/route and fresh output/database paths; its route
arguments were checked against r3, with only `baseline-max-day-r3` replaced by r4. The
480-second bound, 8 GiB / 80% start and 2 GiB / 85% stop limits were retained as assigned.
Fresh-path, environment-override and UDP-port checks passed. All eight lightweight fixture
groups passed before launch and are bound to the guard hash. The original wrapper and r3
evidence remain unchanged. Guard diff, fixtures, snapshot and inspection scripts are retained.

Preflight at 15:46:33 was **8.976 GiB available / 38% commit**, without competing jobs.
Godot exited **0** after **350.475 seconds** externally; the route clock records **339.952 s**.
There was no guard stop, primary error or secondary error. All **267 resource samples** are
retained: minimum **3.3355 GiB**, maximum **75% commit**. The owned process exited normally;
no owned identity or other native game/build/render process remained when checked. Later
read-only inspection measured **8.607 GiB / 40%**. These are sampled bounds, not guaranteed peaks.

Actual `environment-route.json` reports `technicalChecksPassed=true`, `failure=null`,
`visualQualityAccepted=false`, mode `connected-walk`. The **628.031 m** plan visited beach,
woodland, clearing, water, cliff and wellhead monument, with 445 trajectory samples. Water
and cliff intentionally share a position with different viewing directions. All **18 PNGs**
exist at **1600×900**, match their recorded SHA-256, and report ready streaming, OnGround,
planet frame 0 and cheats None. The first beach capture is at 34.568 route-clock seconds.
The captured ground HUD retains the rock and torch. Elevated views use the harness's
explicit 9 m diagnostic camera lift and are not ordinary eye-height evidence.

The runtime settings show Maximum (preset 4), render scale 1, SDFGI/SSAO/SSIL/reflections
enabled, grass density 1 and unchanged detail settings. Six stationary samples used eight
seconds each, frame cap 0 and vsync disabled, with no layer ablation. Screenshots are excluded
from those measurements. Values below are milliseconds; renderer CPU is not total main-thread CPU.

| Stationary view | Mean frame | p95 frame | Mean GPU | Mean renderer CPU |
| --- | ---: | ---: | ---: | ---: |
| Beach | 26.06 | 36.91 | 25.53 | 2.09 |
| Woodland | 30.20 | 31.30 | 29.63 | 2.36 |
| Clearing | 28.77 | 30.14 | 28.22 | 2.22 |
| Water | 24.93 | 26.74 | 24.36 | 2.06 |
| Cliff | 25.24 | 27.22 | 24.67 | 2.10 |
| Monument | 25.03 | 26.72 | 24.47 | 2.10 |

Stationary means correspond to roughly **33–40 FPS**, with 2,196–2,561 draw calls and
8.27–15.22 million submitted primitives. The maximum reported video-memory counter was
6,382,868,688 bytes. GPU time closely follows frame time in these samples; this is evidence
of substantial GPU cost, not an isolated cause analysis. Walking profiles retain the first
up-to-20 seconds per leg, not each entire leg. The zero-frame cliff leg is the shared water
position, so its zero timings are not a performance result. Raw timings and renderer samples
remain in the route JSON; no FPS improvement claim can be made against the failed r3 startup.

Visual review covered all six normal views, woodland roots, and beach/woodland/clearing
elevated views. Terrain repetition remains visible as parallel bands on sand and soil.
Woodland/clearing grass contains conspicuous rows of short, clipped-looking green cards;
tree silhouettes remain sparse and jagged. Water shows a stepped distant edge and strongly
patterned white foam. The wellhead's light ground patch has a conspicuous hard, scalloped
border. These are observations from the unchanged baseline, not proof of a particular cause
or acceptance of the staged terrain/grass/water candidates. No night route ran.

stderr contains **seven leaked Texture RIDs** and **two leaked ObjectDB instances** at exit.
They remain unresolved; this run did not include verbose shutdown diagnosis. stdout has
five clock-adjustment notices (16, 13, 11, 11 and 13 ticks), but no route timeout or lost connection;
the server saved and stopped normally after the completed route. The warnings should be
included in P8's queued lifetime/shutdown investigation rather than hidden by exit 0.

Preservation passed for **6,275 protected paths**: the snapshot's 6,274 plus the snapshot
itself. This includes all 158 frozen inputs, 19 actual client/dependency DLLs, 1,723 import
settings, 3,498 imported-cache files, runtime sources/data, four user preference files, old
r3 evidence/database and Git index. Inventory comparisons also found no added/removed
import settings, cache entries, runtime sources or preferences. HEAD remains
`9997b375c6c79ec22380550668b782e15be79cc6`; the 25-path staged patch remains
`64C26E0E8840CF961EE77CCFFB4A3800F84C2AD90A7D2C4ED68A4B8FE89A7E29`.

Evidence: `docs/images/repairs/p2-environment/baseline-max-day-r4/` holds the 18 original PNGs
and route JSON (SHA-256 `E3EC122D86A5BEA8515B44CB506F0FBB9D5E18447AF011E90719CB1014331A64`).
`.artifacts/p2-environment/baseline-max-day-r4/run/` holds stdout/stderr, resource/event logs,
the finalized receipt (SHA-256 `5D90A0F10777BECDB4661012EFABBAEAB53FEAC731EC1CF91D0E1F678CF98C5D`),
full `inspection.json` and compact `inspection-summary.json`. The full inspection is retained;
the compact version omits bulky raw renderer/frame arrays already present in the route JSON.
Coordinator-owned development/backlog updates and any next candidate validation remain with
the coordinator. This chat is idle after this one completed attempt.

## Candidate review — 17 October 2026, after r4

**Recommend one two-file first batch: procedural grass stature and root attachment.**
This is source review and a fresh scratch proposal only. It is **not installed, compiled,
tested, rendered or visually accepted**. The coordinator retains all execution/integration
decisions; P7 owns the current heavy slot. No new agent or chat was started.

The r4 woodland/clearing/monument views make the grass issue directly observable. Current
`TuftMesh` supplies unit-height lean/curl in XZ, while the shader shortens Y without shortening
that authored growth. Dynamic lean/wind/trampling also fail to follow distance shrink fully,
and the 30 mm procedural burial hides a substantial part of short blades. These mechanisms
support a targeted repair; they do not prove every visible row has the same cause.

Fresh proposal: `.artifacts/p2-environment/candidate-review-r1/grass-first.patch`, SHA-256
`9A73C9617BA432E90F803F9CD32E222637E42DE4103FDB6AFF329B561BB078C6`.
`proposal.json` records the exact inputs, decisions, checks and comparison limits; complete
candidate files are under its sibling `candidate/game/`. Original staged candidates are preserved.

| Production target | Current SHA-256 | Proposed SHA-256 |
| --- | --- | --- |
| `game/scripts/Surface/GrassField.cs` | `93F3B53E91903737C881F5F9952B23F514FB9D61F81730F7D335B714A3A6B843` | `4030D1F2210B403CF74F296B86D7FDA1E9530983A6D8348B2DBFB1E5866D5A4B` |
| `game/shaders/surface_grass.gdshader` | `51130F391CF8CC0A43723874A4911161DB2425EC162FD467DC2BFCC3AA3A67F7` | `6A713E5B81EE6E5A5FCA0F468B675B69503B3A26FEF4551CF5D41158447B31A3` |

The C# hunks start at current lines **247, 278, 292**: add a UV2 array storing only lean/curl
XZ, without changing original positions, normals, UV0, colors, indices or RNG calls. Shader
hunks start at **101, 112, 227, 287**: upright roots use the lattice triangle plane; UV2 separates
root spread/blade width from growth; growth and dynamic bend follow live height; burial becomes
5 mm; analytic blade width tapers against its own ragged top. These two files must move together.

The proposal narrows the retained grass bundle. It **keeps the current**
`0.68 + 0.2 * patchy` height multiplier, preserves litter's bilinear placement and 4 mm lift,
and leaves authored crowns untouched. Card/cell/density/keep rules, budgets, LOD/radii,
normal shading, source atlas, opacity pass and graphics settings remain as currently configured.
The expected shared tuft mesh is still 40 vertices / 90 indices / 30 triangles; UV2 adds
320 raw attribute bytes once, not a per-instance buffer. These are source-derived expectations,
not executed test results. More exposed blades and added vertex ALU can cost GPU time even
with identical draw/triangle counts. Current stationary means are already 24.93–30.20 ms.

Conventions reviewed: UV0.y and COLOR.a both encode root=0/tip=1; UV2 is zero at the foot and
contains no root offset or card width. The two interpolation branches follow TileMesh's
a-b-d/a-d-c diagonal. Grass's TriangleHeight interpolates radial heights before reconstructing
positions, so exact attachment to the rendered/morphed mesh is **not proven** merely by this
algebra. Roots on slopes, distance shrink, wind/trampling, seam cells and distant blade coverage
need native inspection. Existing upward-biased shading normals remain; there is no new
grass normal-map or PBR-channel transformation.

The original authored-cover proposal is deferred. Its writer repacks INSTANCE_CUSTOM from
RGB tint to scalar tint plus local slope gradients; its shader must be paired with that writer.
Scale/yaw projection is algebraically consistent, but root deformation fades out toward the
crown and is skipped by billboards. Native pivot, root and LOD-transition evidence is missing.
The first batch avoids that independent contract change and preserves source negative pivots.

The terrain candidate keeps albedo/normal/roughness-height on matching transformed UVs,
gradients and blend weights, with normal flips decoded before inverse rotation. GroundLayers
still supplies roughness in R and height in G. Its existing lifetime repair is compatible and
must remain unchanged. The candidate's six scan fetches equal the old close view, but exceed
the old five-fetch middle view; hashes/rotations add ALU and mip/seam behavior needs native
proof. It also cannot remove the wellhead's hard material-mask border. Defer it from this batch.
The water candidate corrects the coordinate frame of refraction, but does not address r4's
stepped horizon or foam pattern. Its retained CPU result is not a native pass. The optional
foliage-opaque candidate explicitly removes alpha-to-coverage smoothing and is **excluded**
under the quality-preservation constraint.

Current grass/terrain/water/authored-cover sources still match their original baseline copies;
their retained diffs do not overwrite the integrated background transport, dilation-buffer
reuse or GroundLayers lifetime changes. The 158-input freeze remains the integration boundary.

**Proposed next step, requiring a new coordinator assignment:** inspect/apply exactly the
two-file patch, then one bounded solution build and existing Architecture/EnvironmentRoute
checks. A separately bounded native scratch check should inspect the actual production
TuftMesh arrays and render the actual shader on flat/sloped triangle patches at multiple
yaws and shrink distances, including wind/trampling and unchanged litter. Do not substitute
a copied CPU formula for native shader evidence. Then schedule a separate Maximum/day
comparison using r4's settings, fresh output/save and the existing 8 GiB / 80% start,
2 GiB / 85% stop and 480-second limits. No automatic retry or quality reduction is proposed.

**Same-pose limit:** the existing route stops within 1.25 m on variable frames. Repeating
its CLI matches settings/region, but does not guarantee identical character/camera positions
or shader TIME. Strict paired capture requires a separately reviewed scratch replay of r4's
full camera basis/position/FOV, matching character/trample position and populated lattice,
sky and wind/time inputs. Label diagnostic camera replay separately from ordinary walking.
Without that, compare the recorded pose deltas and representative silhouettes/timings, and
leave exact same-pose acceptance open. Primary comparisons are woodland normal/roots/elevated,
clearing normal/elevated and monument normal; all six stops still need collateral inspection.
Keep raw six-station frame/GPU/render-CPU/draw/primitive/video samples. No candidate validation
was executed during this review.

## Grass integration — 17 October 2026, 17:35 TRT

**Exact two-file patch applied; build/tests not launched; heavy slot released.** The coordinator
granted integration against the reconciled ship-controls checkpoint
`.artifacts/recovery/source-freeze-ship-tps-20261017.json` (163 inputs, SHA-256
`836873424C08A085C1749182F26C71B0447D93EAF4E8A732D3C49045F6964872`). All 163 matched
before integration. The independent ship changes were preserved.

Fresh `git apply --check` passed and the exact approved patch applied. Actual source hashes:

- `game/scripts/Surface/GrassField.cs`: `4030D1F2210B403CF74F296B86D7FDA1E9530983A6D8348B2DBFB1E5866D5A4B`.
- `game/shaders/surface_grass.gdshader`: `6A713E5B81EE6E5A5FCA0F468B675B69503B3A26FEF4551CF5D41158447B31A3`.

Both equal the reviewed candidate bytes. Original proposals, before-images and the shared
index are preserved. The 161 other frozen files remain unchanged; the two authorized updates
are the only unique changed protected paths.

Preparation then failed its post-integration hash check. The snapshot contained both forward-
and backslash spellings of each target. It updated the expected hashes under the forward-slash
keys but left the backslash aliases pointing to the old hashes. Thus `game\scripts\Surface\GrassField.cs`
produced a false drift failure after successful application. Read-only inspection found exactly
four changed entries representing the same two authorized files and **zero unrelated drift**.
The original failure/snapshot were retained; no path-normalization repair or retry followed.

Both fresh guard revisions passed all **eight lightweight fixture groups** before integration.
They preserve the reviewed exact helper classifier, owned PID/start identities, durable
primary/secondary handling, 300-second ceilings and 5.5/80 start / 2/85 stop thresholds.
An earlier outer shell check incorrectly treated an unset LASTEXITCODE from successful
PowerShell fixtures as failure; its saved fixture proof was verified, without rerunning it.
That orchestration error is recorded separately in `preparation-note.json`.

No build, test, import or native renderer ran; there are no compiler/test exit codes or runtime
resource bounds to report. All **896 existing build-output files** have identical before/after
hashes and inventories. The client remains the pre-patch ship-controls build:
SHA-256 `0C502CB0E0084103782F0BD4A4FE6808EC8C157FF9A6297C1B86CEE53F75E61A`, MVID
`f167402c-c435-487d-9f79-68d3eb569c1e`. The existing test assembly remains
`AE3FEC1345CB51391294DA7ECB62488E4ED028AA279EADC0125C33FE9B6495DD`, MVID
`b4bb6266-5ccc-4665-af1e-6e7a7d4dbcee`. These binaries do **not** validate the integrated
grass changes; the new shader's UV2 producer still needs compilation.

Evidence is retained in `.artifacts/p2-environment/grass-build-r1/` and `grass-tests-r1/`:
source-before manifest/copies, unchanged original patch, application logs, guard sources and
fixture results, build-output hashes/MVIDs, `integration-stop.json`, and
`preservation-summary.json`. No guard execution receipt exists because no check was launched.
The next coordinator action is review of a narrow snapshot path-normalization correction
and, if granted, fresh guarded build/test evidence. **Do not reapply the already integrated patch.**

## Manifest correction and memory refusal — 17 October 2026, 17:44 TRT

Root reviewed the alias mismatch and granted a scratch-only reconciliation before the first
actual build. `grass-build-r1/Reconcile-Manifests-r2.ps1` now resolves absolute Windows paths
with `OrdinalIgnoreCase` identity and rejects conflicting alias hashes before any authorized
expectation update. The 12,623 original entries resolve to 8,821 identities with no conflicts.
Only the two exact approved grass paths receive their reviewed candidate hashes. No production
file was written and the patch was not reapplied. The failed preparation, original snapshot,
source before-images and all prior evidence remain intact and pinned.

The new `before.json` has 8,831 protected inputs, including the corrected preparation,
reconciliation record and original failure evidence. Both unchanged guards still match their
saved eight-group fixture proofs, which were reused without rerunning fixtures. All 896
build-output hashes and inventory entries matched immediately before the attempted launch.

The build guard ran once and **refused before launching dotnet** at 14:43:37 UTC:

- Available memory: **5.0365 GiB**, below the required 5.5 GiB; commit: **51%**.
- No competing process. No owned process was started or stopped.
- Status: `guard-stopped`; outer wrapper exit 1; compiler exit code null.
- Two preflight/final samples: minimum **5.0140 GiB**, maximum **51%** commit.
- Primary refusal was saved with its location and stack; **zero secondary errors**.
- Receipt and independent finalization match exactly; **8,832 inputs** including the
  execution manifest passed the final preservation check.

No build, tests, import or native render launched. No test execution manifest was created
because the conditional successful-build prerequisite was not met. All 896 build-output
files remain unchanged, including the client/test SHA-256 hashes and MVIDs listed above.
HEAD and the original staged patch SHA-256 are unchanged. The heavy slot is released;
no retry followed. A fresh coordinated grant and adequate memory are required for the first
actual compilation. The source patch remains integrated and must not be reapplied.

Compact evidence: `.artifacts/p2-environment/grass-build-r1/handoff-r2.json`, SHA-256
`E190567C80722D0CA549C798662B6F4E6AB952E0B4534398150B846A2598D32B`.
The refusal receipt and finalization each hash to
`5C40F5D2747512EF478719322EB6AC1BBBD68DA9F4166C8763CDD56622A04A7B`;
the corrected manifest hashes to
`7E160C331E90DDD70FB8403C0255806165EFC37E9EF3102B272395800F8C508D`.

## First actual build and focused checks passed — 17 October 2026, 17:59 TRT

After memory materially recovered, root granted one fresh attempt under `grass-build-r2`
and conditional `grass-tests-r2`. Both completed successfully, sequentially, with the
byte-identical guards and their reused eight-group fixture proofs. No fixture suite was
rerun, no guard behavior changed, and no production source was edited or reapplied.
Fresh manifests use the reviewed absolute Windows path identity and preserve all r1 evidence.
All 163 files in the explicit pending-grass source freeze match.

| Check | Result | Guard duration | Start available / commit | Minimum available / maximum commit |
|---|---|---|---|---|
| Single-worker solution build, no build servers | Exit 0; 0 warnings, 0 errors | 38.184 s | 6.8183 GiB / 45% | 5.9982 GiB / 48% |
| No-build Architecture + EnvironmentRoute | Exit 0; 7/7 passed | 2.950 s | 6.4408 GiB / 46% | 6.4408 GiB / 46% |

The build recorded 29 resource samples and verified 8,854 protected inputs unchanged.
The tests recorded four samples and verified 9,760 inputs unchanged, including all newly
built outputs. Both have empty stderr, no primary/secondary error, no guard stop or process
termination, and identical receipt/finalization records. No owned process remains running.
The five architecture checks and two environment-route checks passed in 1.6 s of reported
test time. The route check confirms sampled heightfield feasibility over the 628.0 m route;
it does not validate hosted object collisions, shader output, or visual quality.

The output inventory remains **896 files**: the build changed **265 hashes** with no added
or removed output files; the no-build tests changed no output bytes or inventory. Exact
before/after hashes are in `grass-build-r2/expected-output-changes.json`. The current modules are:

- Client SHA-256 `8C8B4529DE908ACB29F515154564C68099BEAAB1E4C85020C377AF989D635234`,
  MVID `356e7687-a76d-49c3-8293-781d0f5db768`.
- Tests SHA-256 `608CD78F980C75E0B9AAC9206D7887F44BCDB3349733AA131FF6099D665F5850`,
  MVID `6a518571-0972-44b8-a049-005e2a83c2e4`.

The prior ship-controls module hashes/MVIDs remain in the original snapshots. Source,
asset, import-cache and preference inventories remain unchanged. HEAD remains
`9eecdbb55cd7555572b09e43eccbe8d4055771e1`; the 25-path staged patch still hashes to
`64C26E0E8840CF961EE77CCFFB4A3800F84C2AD90A7D2C4ED68A4B8FE89A7E29`.

Compact handoff: `.artifacts/p2-environment/grass-build-r2/handoff.json`, SHA-256
`16700938DBBA985935752DFFC122707D7D091D097B64529A885F08FBF32284C1`.
Build receipt SHA-256: `2DE6972C091379A0D0D821DD71BE6C52FB212EDF247CBDCBF09B5ADEC40AE7C4`.
Test receipt SHA-256: `28AEB99CE437BFFC768B95E7724A1E879A1C1B1BDEEE1C6BA8D014F8213122CB`.
The heavy slot is released. No import, engine/native render, shader compilation, paired
visual comparison or GPU-performance validation was run. Those acceptance steps remain open;
the historical r4 baseline is not candidate visual evidence.

## Isolated native grass preparation — 17 October 2026, 18:24 TRT

Prepared `.artifacts/p2-environment/grass-native-prep-r1/` for coordinator review only.
P2 launched no solution/scratch build, native process or import. The C# fixture has not
been compiled; its planned native assertions and captures have not run.

`GrassCheck.cs` invokes the actual compiled `GrassField.TuftMesh` and `LitterMesh` via
reflection. Native array readbacks will check retained positions/normals/UV0/colors/indices,
deterministic layout, roots/tips and UV2 centerline growth. `RetainedMeshes.cs` contains exact
pre-patch method bodies solely as geometry references. No duplicated CPU shader is offered
as render proof. The production shader/includes are byte-identical copies; full production
atlas/texture sizes, imported grass BPTC mips, compiled noise and mean-color routines remain.

The planned 16-cell Forward+ scene uses explicit flat/sloped/folded triangle patches, patch
yaws 0/45/90 degrees and production density/card/segment settings. Fourteen 1600×900 captures
cover calm/repeat/grass-hidden control, roots, slopes, distance shrink, wind, time stepping,
trample and litter. Exact image assertions check repeats and candidate/retained-shader litter
equivalence; response assertions check wind/time/trample. These planned checks do not prove
artistic correctness or attachment to morphed world terrain or footprints crossing a crease.

At root's request, time control measures the first zero-step HDR shader `TIME` sample as an
immutable baseline, then verifies baseline plus explicit render steps. Captures distinguish
measured absolute time from relative steps. Exact pixel equality is unchanged. Earlier
source/readme/runtime pins are retained under `revisions/before-time-baseline-r1/`; archived
C# is excluded from compilation. Atmosphere and seed are explicit fixture inputs, not a
Maximum world/day/night replay. No GPU benchmark or game-dev sealed-run claim is made.

`proposed-commands.json` and `README.txt` contain exact sequential commands: one isolated
single-worker build with local cached dependencies, then one native Forward+ fixture only
after build success and separate grants. No explicit import/editor step is proposed.
Both adapted guards passed **eight lightweight fixture groups**. Only command, scratch
depth and labels changed; resource thresholds, exact helper classification, PID/start
ownership, 300-second bounds and durable errors remain intact. No execution `before.json`
or actual `run` folder was created.

The initial preparation snapshot verified **9,771 protected identities**, reconciling only
Git index/branch bytes from the assigned docs-only HEAD `c360a3a556db729cbe18be0ca562b073ba606a7b`.
During final verification another operation changed **369 bin/obj files**, including **nine
runtime DLL pins**. Current client SHA at that sample:
`47D6FD1BEEE55B85DE201474466F784B0BE4B496B96317B7A4703176D75F7BB2`.
The harness intentionally still requires the reviewed `8C8B4529...635234` build and MVID.
No drift outside build outputs was found; source/assets/cache/preferences inventories,
HEAD and the original 25-path staged patch matched at that sample.

An unowned production Godot `--headless --import` process was observed: PID 21212, parent
19668, start 18:23:05.946 local. Root subsequently reported a normal ground-game process,
PID 21924, same parent, start 18:23:11 local. These are external/user-owned; P2 started or
stopped neither. Further writes may follow the sample. `preparation-verification.json`
preserves actual deltas and process metadata; inputs were not rebaselined or restored.
Preparation ownership is released. Coordinator reconciliation and release of the active
play-session hold are required before any scratch-build/native launch.

## Grass native successor reconciliation — 2026-10-17 16:12 UTC

After the external admire/bow chat completed, the coordinator authorized source/hash/
metadata inspection and scratch preparation only. The successor is
`.artifacts/p2-environment/grass-native-prep-r2/`; its `README.txt`, `reconciliation.json`,
`successor-provenance.json`, exact diffs and `proposed-commands.json` form the review package.
No build, test, import, synthesis, native render or fresh guard fixture was executed.

All 102 r1 artifacts and its original handoff hash remain unchanged. Against its 9,771
protected identities, the completed external work changed 371 build outputs, 15 source
files, held-presentations.json, ground-settings.cfg and two Git metadata files: 390 total.
Nine original runtime pins changed; ten of 163 source-freeze entries changed. Seven
source/test files were added, none removed. Asset, import-cache and preference inventories
retain 3,643, 3,498 and four paths respectively. All exact before/after hashes and added
paths are retained. The original staged-patch hash remains `64C26E0E...E29`.

The current client is `ED66A9DE45F13F008C23BDE4936BF498593527D9516AD39456257591A67882C0`,
MVID `2577686e-3f9f-4ba7-8382-f3d4aba9b745`. Metadata-only PE/PDB inspection found matching
IDs for all nine Hollow modules and matching checksums for all 679 source documents
present on disk. The absent 543 Godot-generated paths and one Net generated path remain
an explicit source-verification limit. No assembly was loaded or method invoked.
GrassField.cs, the grass shader, and the selected called dependencies all match the old
validated source. Old DLL bytes were not saved, so no old/new IL-equivalence claim is made.

The successor snapshots 24 small managed DLL/PDB files totaling 9,506,596 bytes and pins
47 runtime/source/resource inputs. Actual calls include production mesh builders,
FloraTextures, FloraLibrary.MeanColour, Painter, NoiseTextures, PlanetGlobals and Core
Rng/Hash64. The full small managed reference closure supports type resolution; world,
physics, persistence and network startup are outside the fixture. Cached GodotSharp is
pinned; native dependency fallback is prohibited. The harness also checks loaded Core
hash/MVID. It reuses r1's ground ctex read-only by absolute path and preserves the reviewed
initial shader TIME baseline correction and all raster/capture logic.

Both guard diffs change only the command argument's r1-to-r2 scratch path. Read-only text
comparison confirms all guard control logic is unchanged. The old eight-group proofs
are retained as provenance; the guards still require fresh matching fixture proofs.
There are no execution before.json files, run directories or new validation.json files.
Final comparison found zero drift during reconciliation across the 9,771 inputs, 102
original artifacts and 47 successor pins; the seven added inputs also still match.

External build-verified.log reports 0 warnings/errors in 12.13 seconds and the verified
tests report 26/26. Root independently reviewed the bow evidence in
`.artifacts/recovery/bow-correction-external-review-20261017.json`. These logs do not embed
the DLL hash; current runtime identity is bound separately by metadata and snapshot hashes.
Historical grass 7/7 and admire 502 unique passes remain historical evidence.

Next reviewable steps are fresh lightweight guard proofs, a reconciled build input
manifest including the seven added files, then one separately granted scratch build.
Native launch needs a separate grant and fresh pins for the built harness and copied
dependencies. Each uses the existing 5.5GiB/80% launch gate, owned-only 2GiB/85% stops,
unknown-competitor stop and 300-second bound. No full solution rebuild is proposed.
Shader/native, root/tip, wind/trample and visual acceptance remain pending. There is no
world/terrain-morph/crease-crossing proof or GPU benchmark. Preparation ownership is
released to the coordinator for review.

## Successor guard proofs and build manifest — 2026-10-17

The coordinator reviewed the successor in
`.artifacts/recovery/grass-native-successor-root-review-20261017.json`, SHA-256
`BD806CB5D9242BDD86B03F412041E63005F94D7FE09F41D7AA06F6CD03B432DF`, and explicitly
authorized the two lightweight fixture runs and build-manifest preparation only.
Both guards ran sequentially with `-Mode Fixtures`; each passed eight groups with
zero failed cases, no heavy process launch and no actual game tests.

| Guard | Exact fresh proof SHA-256 |
| --- | --- |
| build/fixtures/validation.json | `5BFEACC2E0DE3F8BD79C52D4C4A140E685B7AB9C267681F8E45A7819513D23CD` |
| native/fixtures/validation.json | `8B207983195773A582B54D2530464812D4248BE0D57E763084BA4B34F29E1ACA` |

Guard source hashes remain `965AA36A...0EC5` for build and `2C0F88D6...A9D83` for native.
The fresh `grass-native-prep-r2/build/before.json` SHA-256 is
`C925C8173C1EBE76A612C0F85486A5B205879E6DDD8E279A1D6A13891BB2849C`.
It contains 10,048 canonical, case-insensitive absolute input identities with zero
conflicting aliases or observed hash drift, all seven added source/test files, original
evidence, successor/runtime pins, fresh proofs, executable pins and 32 cached Godot
package files. All 896 production build outputs are protected. Source, asset, import-cache
and preference inventories match the reviewed successor; the staged patch is unchanged.
All 63 artifacts in the published successor handoff still match.

The manifest records HEAD `e6818b6f7279ba8e9eb0e4886626c2e3e6018669` and explicitly
requires recording any later HEAD transition before execution. Coordinator-owned pending
KNOWN_PROBLEMS/coordination documentation edits are outside production-source claims;
Git HEAD/index/ref remain pinned. A later docs commit cannot be silently accepted by
this manifest. Preserve and explicitly supersede it after review if those pins change.

The final sample at 19:19:58 Istanbul time reports **4.2504 GiB available, 63% commit**,
54 processes matching the six approved helper command tuples, and zero competing heavy
processes. The 5.5 GiB launch floor is not met. No build/native job was granted, launched
or queued; there is no automatic wait for RAM and no build/run or native/run directory.
Preparation ownership is returned to the coordinator for a separate build decision.

## Granted scratch build and native preparation — 2026-10-17

Root granted one scratch build after headroom recovered, referencing review receipt
`grass-native-successor-fixtures-root-review-20261017.json`, SHA-256
`738050CFBBF3859BECE6DA8929B994B3850C6534A4BF2BA52736B57DDD835584`.
All 10,048 declared input hashes, source/asset/import-cache/preference inventories,
896 production build-output identities, staged bytes and HEAD were rechecked before
invoking the unchanged build guard once with `-Mode Execute`.

The build passed: exit 0, **zero warnings/errors**, 3.37 seconds reported by dotnet.
The guard measured 4.958 seconds for launch/monitoring, collected five resource samples,
and recorded minimum available memory **6.0458 GiB**, maximum commit **61%**.
Its own launch sample was 6.3493 GiB/60%. No guard stop, primary error or secondary
error occurred, and all 10,049 effective pins (declared inputs plus the manifest itself)
remained unchanged. Stderr is empty. All six observed owned process identities have
exited. The immutable build manifest remains `C925C817...2849C`.

Build receipt: `.artifacts/p2-environment/grass-native-prep-r2/build/run/receipt.json`,
SHA-256 `35DC34D6236D19CF5BBBFF19447AEF63153878C54792E4D3A159F63F52AEC1D4`.
Only the scratch project was built; no main solution build, tests, import or native
render was performed. Production source/assets/settings, old evidence and staging
remain preserved.

Metadata-only inspection binds the generated `GrassCheck.dll` to SHA-256
`B526188BE85639F7F1EE1DFF900D80EAF89BEA1E137182D6E55F7BA540CBFCC4`, MVID
`ef8979fc-9cac-4ff5-b719-9b9b3df8b4a8`. Its PDB SHA-256 is
`1F0C817D3784EF4536B44F394B3211DD9E0AEBAEC496767FE398B2AA56F76F47`; PE/PDB IDs
match, as do all four present source checksums. Eight generated virtual source paths
are absent and remain explicitly unverified. No assembly was loaded or invoked.
All 24 runtime/PDB copies and both cached GodotSharp DLLs match their inputs; all nine
production DLL hash/MVID pairs match the reviewed snapshot. The inspection records
18 built managed modules and 49 scratch build outputs.

The prepared native manifest is
`.artifacts/p2-environment/grass-native-prep-r2/native/before.json`, SHA-256
`6B2661382EABC0C8135255467BAFB662620C2E1D5A473C20D61C10E7A3ED90BB`.
It pins 10,115 identities, including the compiled fixture/dependencies, all 49 scratch
outputs, original/successor evidence, build receipts and the exact native guard/proof.
No input or inventory drift occurred during preparation. It retains HEAD
`e6818b6f7279ba8e9eb0e4886626c2e3e6018669` and staged hash `64C26E0E...E29`.
The isolated user directory was absent; native/run and captures-r1 do not exist.

The final resource sample at **19:41:52 Istanbul time** was **6.1826 GiB available /
60% commit**, with 44 exact known helpers and no competing heavy processes. This is
evidence for handoff, not a native grant. Native execution requires separate root review,
fresh input/inventory/resource checks and the existing 300-second ownership/resource
guard. Shader compilation, captures and visual/GPU acceptance remain pending.
The sole build slot and preparation ownership are released; P2 is idle.

## Single native attempt stopped — 2026-10-17

Root granted one native attempt after reviewing all 203 build-handoff artifacts, recorded
in `grass-native-successor-build-root-review-20261017.json`, SHA-256
`5419F905B0E47040EFE5C120CA3186672A830B51B2DF539479F4026C4D514DA6`.
The fresh preflight verified all 10,115 declared inputs, production/scratch inventories,
staged bytes, HEAD, exact guard and fixture hashes, processes and memory before the
unchanged native guard was invoked once with `-Mode Execute`.

**The attempt failed before mesh or shader work.** Godot 4.7.2 initialized Vulkan 1.4.351
Forward+ on the NVIDIA GeForce RTX 2070 SUPER. In `GrassCheck._Ready`, line 62,
`File.ReadAllBytes(typeof(GrassField).Assembly.Location)` received an empty path and threw
`System.ArgumentException`. The failure occurred while evaluating the client hash
assertion, before the loaded client/Core hash and MVID checks. Those loaded-assembly
checks therefore did not pass. This is an observed harness path-lookup failure;
the production mesh/shader behavior was not exercised and cannot be judged from it.

The guard observed the fixture's `FAIL` line and retained status `guard-stopped`, reason
`first test failure observed`, exit 1. That generic reason refers to this fixture
exception; no actual test suite ran. There was no primary or secondary guard error,
no process stop action and no retry. The owned Godot PID 5700 exited. Stderr was empty;
the exception and stack were recorded in stdout and `captures-r1/result.json`.

The fixture result is `passed: false`: 52 earlier checks passed, including all 47 file
pins and the native/Forward+ checks. The client hash assertion never completed.
There are **zero of fourteen planned PNG captures**, zero draws and no mesh-arrays
or texture evidence. Initial shader TIME baseline is null, requested step total is zero,
and the last-time field is its unmeasured default zero. Root/tip, slope/folded roots,
height shrink, wind, trample, litter and retained-litter-shader checks were not reached.

Actual guard preflight was **7.1152 GiB available / 55% commit**. Four samples recorded
minimum available memory **6.6449 GiB** and maximum commit **57%**; launch/monitor duration
was 2.342 seconds. The failure was not a memory gate refusal. All **10,116 effective
input pins** remained unchanged. Post-run verification found zero drift among the
203 reviewed artifacts, production source/assets/import-cache/preferences inventories
and staged bytes. HEAD remains `e6818b6f7279ba8e9eb0e4886626c2e3e6018669`.
The 70 new files in the isolated user directory and owned scratch evidence are inventoried.

Evidence under `.artifacts/p2-environment/grass-native-prep-r2/`:

- `native/run/receipt.json`: SHA-256 `C2FA9AF2C8E8D8CD34843A6C4466A6BD7087B3949C5A7072FBD9CD15D3A2C29A`.
- `captures-r1/result.json`: SHA-256 `20D0F3C3FCA584A787AEEB1792C576BF38CBB5693DEAA9C28F78EFFAE2D5EDFF`.
- `native/post-failure-verification.json`: SHA-256 `93D3DE0D5599949217FC5DBCF37C18E4E43AFBF3165CBC512B2826710EB0A402`.

No source, quality, timeout or binding change was made; no rebuild, import fallback,
extra test or second native attempt occurred. Shader/runtime/visual and GPU acceptance
remain pending. The native slot is released and P2 is idle for root review of the failure.

## Static binding repair successor r3 — 2026-10-17

At root's direction, `.artifacts/p2-environment/grass-native-prep-r3/` contains a minimal
source-only successor. The original failure handoff remains `3C32DFAC...FB2DB3`.
The seal covers 10,205 input/evidence identities, all 218 r2 files and all 70 isolated
user files. Post-preparation comparison found zero hash or inventory drift; all 47
successor runtime pins match. HEAD and the original staged patch remain unchanged.

`GrassCheck.cs.diff` replaces the four client/Core binding assertions with two helper
calls. The helper resolves the explicit `.godot/mono/temp/bin/Debug` file under the
scratch project, requires existence and the unchanged disk SHA-256, then separately
requires the loaded full assembly identity and unchanged MVID. Empty Assembly.Location
is telemetry only. Missing/wrong files, wrong disk hashes, wrong loaded identities and
wrong MVIDs still fail. The result ledger labels disk-file hash and loaded identity
separately and makes no claim to hash loaded in-memory bytes.

Metadata-only inspection of the actual pinned r2 output layout verified both expected
paths, hashes, MVIDs, version1.0.0.0, neutral culture and empty public key. The r3 paths
map the same relative layout under the new directory; future output files do not exist
yet. Source comparison reconstructs r2 exactly after reversing only the binding/helper/
ledger edits, preserving all TIME baseline logic, renderer controls, visual assertions
and tolerances. No negative cases or C# code were executed.

The 33 copied fixture/shader/runtime files are byte-identical; the csproj is unchanged.
Both guard diffs change only their one command-argument line from r2 paths to r3 paths.
The project changes only its isolated user directory. Prior proofs are kept as provenance;
there are no fresh guard proofs, execution manifests, build outputs or capture/run dirs.
`README.txt` and `proposed-commands.json` record exact build/native payloads and unchanged
resource/ownership gates. Root must review the narrow repair before any guard-fixture,
build or native grant. No production edits, build, test, import or retry occurred.

## Conditional r3 scratch build passed — 2026-10-17

Root reviewed the 57 static artifacts and 47 runtime pins, then authorized fresh guard
fixtures, a sealed manifest and one conditional scratch build. Both guards passed all
eight groups sequentially. Fresh proof hashes are build
`D72CA1F417E61266C7081A3A31C7C86B3E4C119D9E3ED15E4C5978AA1DB100A6` and native
`A7104670871896CBA0540AF05745E610912CA33A260B2602B19167AD36C26383`.
The 10,332-input build manifest SHA-256 is
`EBAA5B81D3B13BF73F59E0A7557F1201D2C3A0C953F62C4D38B8AF9673C102F2`.
Manifest and proof hashes were sent to root before the one guarded launch.

The scratch build passed with exit0, zero warnings/errors and empty stderr. Dotnet
reported 3.54 seconds; the guard measured 4.712 seconds with five resource samples,
minimum **6.3308 GiB available**, maximum **57% commit**. Actual guard preflight was
6.6672 GiB/57%. No guard stop or primary/secondary error occurred. All 10,333 effective
pins stayed unchanged; all four observed owned process identities exited.
Receipt SHA-256: `AA830AB5BB9D810427A9D0B4140155F1593A2229DB5EDC9640E971184A442C1E`.

Metadata-only inspection binds the new `GrassCheck.dll` to SHA-256
`709D9DB9D5D15D71DB2F10030D014921251B0BE82C8BD36631A3A1C970336BCC`, MVID
`f4c4abbc-c748-4d39-a132-0cf68cdff3f9`. PDB SHA-256:
`C3FD42DF25D6C18214E5DC082B92FE1E6107F49CAA170C74FAB61CE6BA8EA2C4`.
PE/PDB IDs and all four present source checksums match; eight generated virtual paths
remain absent. All 24 copied runtime/PDB files, two Godot SDK DLLs and nine production
module hash/MVID pairs match. Inspection SHA-256:
`539F3BC6A5CE46A2A1D32C49E7935294F7D4B2F1BB793B7C69667D27E5BEC553`.
No assembly was loaded or invoked during inspection.

`grass-native-prep-r3/native/before.json` now pins **10,394 identities**, including all
49 scratch build outputs and unchanged original/r2 evidence. SHA-256:
`7F24BEE0D21E739EC3CCF70E0D33E9ACB1D7D66421A0644345A134B84CD25427`.
There was zero input or production inventory drift. All 218 r2 files, 70 isolated-user
files, 57 published r3 artifacts, HEAD e6818b6f and staged patch 64C26E0E remain preserved.

The final sample at **19:59:31 Istanbul time** was **7.8671 GiB available / 52% commit**,
with 36 exact known helpers and no heavy competitor. No native/run, captures-r1 or r3
user directory exists. No native render, import, full solution build or game test ran.
The single build slot is released and P2 is idle for root review and a separate native
grant. The repaired loaded-module checks, TIME/mesh/shader/visual assertions and GPU
acceptance remain unexecuted.

## Repaired r3 native fixture passed; visual acceptance open — 2026-10-17

Root independently reviewed the 193 build-handoff artifacts and granted exactly one
native attempt. Its review SHA-256 is
`ED89F184664800D4D2836CF5D1E763BC961C89ED427DD3A7326A8BBD4E33595D`.
Fresh preflight and the unchanged native guard then launched the reviewed fixture once.
Godot 4.7.2 Forward+ / Vulkan ran on the NVIDIA GeForce RTX 2070 SUPER.

The native run passed, exit 0: **287 assertions, 14 original 1600x900 PNGs, 84 draws**.
No guard stop or primary/secondary error occurred. The guard measured 6.438 seconds and
six samples, minimum **5.8218 GiB available**, maximum **59% commit**. Actual guard
preflight was 7.3986 GiB/54%. Stderr is empty; stdout and the isolated user log contain
no warnings/errors. The owned Godot PID 9400/start-time identity exited.
Receipt: `28FBB20F7FDE4453E3449857560FA2E0302DD3092D38496EBD267C41D6489393`.
Result: `E824388A3A33FA3DC4786C007BECE95A2826B4499A32ACBF09DF521701238A40`.

Both client and Core loaded bindings passed despite empty Assembly.Location. Expected
output disk SHA-256 and loaded FullName/MVID match independently; this is not a hash of
loaded memory bytes. The client remains ED66A9DE / MVID 2577686e; Core remains 03725C41 /
MVID b98c7719. Actual compiled production mesh readbacks show 40 tuft vertices, 90 indices
and 40 UV2 values; litter has 24 vertices/54 indices and no UV2. Retained tuft attributes
and litter attributes match, production repeats are deterministic, all five roots have
zero growth and tips retain authored bend. Maximum growth readback error is
2.1073424e-8m; UV2 adds 320 raw bytes. These are ArrayMesh readbacks, not shader-deformed
vertex readbacks. Mesh evidence SHA-256:
`E6821FC0EC5B1359D92C35E77B378B7A126C467CC42B987D97EB6B3E84385295`.

The native TIME probe latched 0.01666259765625s and tracked explicit relative 0/0.5/1s
steps. Maximum capture error was 0.00006103515625s, below the 0.002s tolerance.
Calm/repeat and both litter comparisons have exactly zero changed image-buffer bytes.
Ground-only, wind enable, wind time step and trample comparisons changed 182284,
136851, 144663 and 113504 bytes respectively. These counts are bytes, not pixels.

All fourteen original images were inspected. Grass visibly follows flat/sloped/folded
fixture surfaces in these views; distance shrink shortens blades and trample visibly
flattens/spreads them. No obvious continuous floating band was observed across patch
interiors. Finite patch boundaries expose card bottoms/projections, and individual
root contact is not numerically established. **Broad angular card/triangle silhouettes
and speckled or disconnected-looking tips remain visible in closeups 04/06.** This is
an unresolved visual quality concern for coordinator review, not a diagnosed renderer
fault. The three litter captures match exactly and show sparse tan litter shapes.
Review: `grass-native-prep-r3/native/acceptance-review.json`, SHA-256
`58A1F2B07E83826207021640284EB6BE520761C7C652447DE71848723C6E353B`.

Read-only post-run verification passed: all **10,395 effective inputs**, all 193 previously
published build artifacts, all 218 r2 files and all 70 r2 isolated-user files remain intact.
Source/assets/import-cache/preferences and 896 production output inventory entries have
zero drift. HEAD remains e6818b6f, staged patch 64C26E0E. All 14 PNG hashes match the native
ledger. The new isolated r3 user directory contains 74 files, separately hashed.
Verification: `477EAAD8A94FB7A5D4BB54D0E7940B9413F36FD89514DF059DD3BB896D59C88F`.
Final resource sample at **20:07:49 Istanbul time**: **6.9823 GiB available / 55% commit**,
38 exact known helpers, no heavy competitor and no owned native process still running.

This establishes the bounded production-mesh, TIME and raster controls in the neutral,
16-instance fixture. Full-world appearance, collision, day/night, density/LOD acceptance,
morphed terrain or crease-crossing footprints, exact historical world-baseline replay,
GPU performance and numerical shader-output vertex positions remain untested. This is
not a sealed game-dev CLI adapter run. No production edit, additional build/game test,
import/editor fallback or retry followed the run. The heavy slot and preparation ownership
are released; P2 is idle for independent coordinator review. Native handoff and complete
artifact hashes are in `grass-native-prep-r3/native/native-handoff.json`.


## Read-only closeup cause analysis — 2026-10-17

Root independently accepted the r3 evidence hashes but kept N6 open after reviewing
angular silhouettes and speckled/disconnected-looking tips. Its review SHA-256 is
`CB60FE0D26C9E7116E59396A6F2EC959E912AF2C2F1C85D8FB1A69DDE68A33BB`.
The new note `.artifacts/p2-environment/grass-visual-cause-r1.md` records current/retained
source and frozen fixture references, evidence, unknowns and a proposed later comparison.
Note SHA-256: `DBA960043C87656B2D3F42DCEA02D47327F3FF3340300FA05CA7DEE204FA0AE1`.

The old mesh positions, UV0, indices and three-segment card construction are unchanged;
UV2 is the added attribute. Existing card/strip widths and overlapping silhouettes can
explain broad forms. The current tip taper only removes fragment coverage at the same
UV/seed, and it intersects a separately painted random atlas silhouette. That can sever
connections. Atlas-only coverage mip preservation does not preserve their product.
A second plausible contributor is the interpolated per-tuft seed feeding a sensitive
fragment hash. Neither mechanism has been isolated by intermediate-mask readback, and
r3 contains no retained upright-grass comparison (the retained capture is litter only).

The narrow first candidate is non-interpolated per-tuft seed delivery with all other
shader/mesh/atlas behavior unchanged. A separately granted four-capture comparison
(current/candidate at the same flat/slope root views) can discriminate this hypothesis.
It is not a claimed fix or a grant to execute. If unchanged, reject it as demonstrated
visual improvement and return for direction. Keep the silhouette-intersection branch
separate; do not change AA, lighting, density or the atlas at the same time.

Neutral lighting and closeup framing expose broad shapes. The finite 1.84m ground patch
also explains why outer cards can project beyond its floor; this cannot establish a
world root-contact defect. Full-world appearance, interior root geometry, individual
alpha islands and GPU costs remain unproven. Quality and timeout settings stay unchanged;
any later Maximum run still requires at least 8 GiB available. No heavy slot was granted.

At 20:15:45 Istanbul, all 10,395 input pins, 226 r3 artifact hashes and 74 r3 user-file
hashes remained unchanged; HEAD e6818b6f and staged patch 64C26E0E remained unchanged.
Only this report and the new scratch note were written. No source/assets/preferences,
old evidence, build, test, import, renderer, benchmark, new agent or chat was touched or
launched. P2 is idle for coordinator review; N6 remains open.

## Seed interpolation experiment prepared — scratch only, 2026-10-17

Root authorized preparation of a new successor only. The package is
`.artifacts/p2-environment/grass-seed-prep-r1/`, with review instructions in `README.md`
and complete source/shader/project/guard diffs. The production files and every r1/r2/r3
artifact remain unchanged. The candidate shader adds only `flat` to the `v_seed`
declaration; its SHA-256 is
`546088E1A8F3B9CE36A5A76EC0063B8A4BC9FA73BE4932F93C0A3EC58733843B`.
The current shader stays `6A713E5B…`. Godot's official interpolation documentation
supports this syntax and meaning; installed-renderer compilation remains unexecuted.

The planned four original captures pair current/candidate at exactly the former flat
and slope root views, with unchanged mesh/UV/taper/atlas/lighting/AA/density settings.
Binding verification, mesh readback, texture/global/viewport setup and native TIME
control are byte-identical to r3. The same six draws per capture are retained (24
planned, zero actual); penultimate/final RGB8 snapshots add short-term stability
telemetry without another draw. The package reports changed bytes and changed pixels,
maximum delta and mean absolute delta for full images and fixed same-view interior/root
regions. Counts are descriptive; result visualOutcome explicitly remains unassessed.
Unstable frames or no consistent visible improvement mean no demonstrated fix. Independent
process repeatability is not established by this plan and no automatic follow-up is allowed.

All 33 copied fixture/shader/runtime files match r3, including 24 immutable runtime
DLL/PDB snapshots. There are 48 runtime pins covering both shaders. Each guard differs
only in its command paths; 300s bounds, rendering quality and ownership/resource rules
are unchanged. Historical guard proofs are clearly labelled source-fixture-proof.json;
no fresh proof is claimed. Exact build/native payloads and the separate-grant sequence
are in proposed-commands.json. The custom user directory is GrassSeed-r1.

Static preservation verified all 10,509 sealed identities, original inventories
(103 r1 files, 218 r2 files, 227 r3 files; 70 r2-user and 74 r3-user files), production
source/assets/import-cache/preferences and outputs without drift. HEAD e6818b6f and
staged patch 64C26E0E remain unchanged; the cause-analysis note retains DBA96004.
Preservation verification SHA-256:
`BE7B4A4F27E4F626A5B54E9E3DCFD9701D5CB1BDA141BCE8D0A2F24887298AB6`.
Source reverse reconstruction and PowerShell parse checks are static inspection only.

No guard fixture, build/test/import/render/benchmark ran. No new compiled output,
capture, execution manifest, run folder, fresh validation or isolated user directory
exists. P5's work was untouched. P2 releases preparation ownership and remains idle for
root review; no heavy slot is held. N6, visual quality, full-world and GPU acceptance
remain open. A later Maximum launch still requires at least 8 GiB and unchanged quality/
timeouts. The static handoff is grass-seed-prep-r1/handoff.json.

## Seed experiment scratch build passed; native grant pending — 2026-10-17

Root reviewed all 57 preparation artifacts and 48 runtime pins, then granted the sole
slot for fresh lightweight guard checks and one conditional scratch build. Root review
SHA-256: `59D86CCC510E5C753F016FFC3C11BC816D1F623AB0B44EB602B0531F92846D4C`.
Each current guard ran Fixtures once and passed all eight groups; no game test or native
process ran during these checks. Fresh proof hashes are build
`CA6BF33CCA26EAA5F99F2DB8F01D4A07CD45379A89B5041D968A7A30ABE217FE` and native
`382267ADDA00590919DE4ED919F6EC089035EDCD7562AD4443E93B80EA4AA3FE`.
The 10,637-input build manifest SHA-256 is
`00DA0811AE79E5E83F6EE9566FF75BCAFAE8860A9143E488F46C791B69ABC075`.
Proof and manifest hashes were published to root before the single guarded launch.

The scratch build passed exit 0 with zero warnings/errors and empty stderr. Dotnet
reported 3.41s; the guard measured 4.664692s with five resource samples, minimum
**7.0862 GiB available**, maximum **55% commit**. Actual launch preflight was
7.2726 GiB/54%, with no competitor. No guard stop or primary/secondary error occurred.
All 10,638 effective pins stayed unchanged and all four owned process identities exited.
Build receipt SHA-256:
`53826B93B2EFEB626C6FCBBDF5C78BC866CD7A18122DF19FDA158F74FB864C2E`.

Metadata-only inspection binds GrassCheck.dll to SHA-256
`F7B004CAF829C7D1B7CAEC1DC6A00955E5F3B7BB3B32A92B9012E19C3A4988A6`, MVID
`182e35f2-ce16-4019-84a6-ccfde43d0082`. PDB SHA-256:
`41DA781EB34639F0ABF399D209AAC0D520990FF40F881F4592461776F0A26501`.
PE/PDB IDs and all four present source checksums match; eight generated virtual sources
remain absent. All 24 copied runtime/PDB files, two Godot SDK DLLs and nine production
module hash/MVID pairs match. The inspector read 18 managed modules and 49 scratch
outputs without loading or invoking any assembly. Inspection SHA-256:
`75BFA6AB13974694E8CDDEB56771CB50480B906F41F190C2B16A97EDF77C0728`.

The native manifest in grass-seed-prep-r1/native/before.json pins **10,697 identities**,
including all 49 compiled outputs. SHA-256:
`DB56FC0D3564197F3C9D83FBF6063302471D66681851BF0EEA43C626A87AE88B`.
Its fresh hashing found zero drift. Prior r1/r2/r3 evidence and user-file inventories,
the cause note, all 57 reviewed preparation artifacts, production inputs/inventories,
HEAD e6818b6f and staged patch 64C26E0E remain unchanged.
Post-build verification SHA-256:
`54B5E55CAD905940EDEF30B55707B722216BD7BC160DADCC0DE95A2F342C544F`.

The final sample at **20:33:23 Istanbul time** was **6.7663 GiB available / 57% commit**,
40 exact known helpers, no competitor and no owned build process left running. There
is no native/run, captures-r1 or GrassSeed-r1 user directory. No shader was compiled by
the renderer, and no render, import, full-world run, benchmark or actual game test ran.
The candidate's visual hypothesis remains untested. N6/full-world/GPU acceptance remains
open. The heavy slot and preparation ownership are released; P2 is idle for independent
root review and a separate native grant. Complete hashes are in
`grass-seed-prep-r1/build/build-native-handoff.json`.

## Seed native preflight refused — no launch, 2026-10-17

Root independently reviewed the 191 build artifacts and granted one conditional seed
native attempt. Review SHA-256:
`821AC0787EBB69B1FE2E699432C64A3A39838366C12A30BF190BC69A32984FBA`.
The fresh outer preflight at **20:37:59 Istanbul time** found **5.041248 GiB available**,
below the required **5.5 GiB** launch floor. Commit was 63%, with 50 exact known helpers
and zero competing jobs. Launch was refused. The native guard's Execute mode was never
invoked; no Godot/renderer process, capture or draw was started.

All 10,697 manifest inputs matched. Source/assets/import-cache/preferences, 896 production
outputs and 49 scratch build outputs had no inventory drift. All 191 reviewed build
artifacts matched. HEAD e6818b6f and staged patch 64C26E0E remain unchanged. The missing
resource headroom was the only failed preflight condition.
Preflight evidence: grass-seed-prep-r1/native/granted-preflight.json, SHA-256
`6273C0B54E3592A193E83FA43A6FCBA8790C83D91735E95A5DB4F62BE42736F6`.

No native/run, captures-r1 or GrassSeed-r1 user directory exists. No new native binding,
stability or pixel/byte/visual result can be reported. The compiled seed hypothesis
remains unexecuted, not disproven or validated. N6/full-world/GPU acceptance remains open.
The original preflight evidence is preserved; there was no automatic retry, polling
for more memory, helper/user-app termination, import/editor fallback or threshold,
quality or timeout change. Root was notified and the heavy slot released immediately.
P2 is idle for coordinator direction. The compact refusal handoff is
`grass-seed-prep-r1/native/preflight-refusal-handoff.json`.
