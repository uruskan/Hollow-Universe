# P8 — recovery inventory, performance, gibs and reliability

16–17 October 2026, Europe/Istanbul. Recovery inventory followed by an authorized N11 source
candidate and serialized validation, recorded below. **Latest N11 evidence: the coordinator's actual
settings UI APPLY check passed 49/49 and its separate restart check passed 36/36, with six inspected
screenshots and unchanged real preferences. Actual APPLY/restart is covered by native injected input.
The earlier gib-budget diagnostic passed 44/44, shared build had zero warnings/errors, and foundation
batch passed 64/64. N11 remains open for destruction/loot/audible-sound gameplay acceptance; physical
human input has not been exercised. N12 remains open.** The original inventory was read-only apart
from this report. Later source
changes and native runs used explicit coordinator ownership and slots, as recorded chronologically.
This latest update reviews retained results/screenshots and changes only this report. No source,
harness, build, test or native run was performed by P8 during that evidence review.

Separate startup follow-up: P2's failed `baseline-max-day-r3` remains retained. After the transport,
dilation and terrain-array lifetime repairs, **Maximum in-process day route r4 completed with
normal timeouts, exit 0, technical checks passed and 18 ready captures**. No Timeout/PeerNotFound
appears in that run; five clock-correction notices remain. This establishes one successful native
startup/route, not general startup reliability or a measured memory/FPS improvement. Six stationary
baseline means were 24.93–30.20 ms (GPU 24.36–29.63 ms). Shutdown still reports **seven Texture RIDs
and two ObjectDB instances**, distinct from the roof viewer's **30 ObjectDB** warning. Visual
quality acceptance remains false in route JSON. Earlier focused startup checks passed 15/15 and
the dilation comparator passed 120 bitwise cases plus four allocation cases. Source stays frozen;
verbose lifetime diagnosis remains queued with no P8 heavy slot.

Read RECOVERY.md, P8-performance.md, COMMON.md, the current design/backlog and the relevant
performance, loading, environment, development and known-problem records. The saved work and
staged admin-camera changes are preserved. The recovery snapshot records HEAD `6746ea3f` at
`.artifacts/recovery/2026-10-16T121759Z`; active owners may have changed files after that snapshot.

## Resource and crash evidence

Read-only CIM snapshot at **15:21:35 local / 12:21:35 UTC**:

| Observation | Value |
| --- | --- |
| Physical RAM | 15.92 GiB total; 5.73 GiB free (performance counter: 5,847 MiB available) |
| System committed memory | 15.68 GiB of 31.84 GiB commit limit |
| Last boot | 15:10:54 local |
| Blender PID 9900 | 1,875.1 MiB working set; 5,787.3 MiB private bytes; created 15:12:18 |
| Largest ChatGPT process, PID 10952 | 1,714.7 MiB working set |
| Codex PID 11148 | 803.0 MiB working set |
| Runtime/build processes | No Godot, Hollow, dotnet or MSBuild process observed at this instant |

These are instantaneous observations, not peaks or evidence of process leaks. Private bytes,
resident memory and GPU memory are different measures. Blender and the owner's applications
were left untouched. The initial recovery note's approximately 10 GiB free is an earlier snapshot;
the difference alone does not identify its cause. No GPU allocation measurement was collected.

A bounded read of Windows System events for the preceding six hours found:

- Event 1001, Windows Error Reporting, 15:11:03: reboot from **bugcheck 0x0000003b**,
  parameters `(0x00000000c0000005, 0xfffff8021caf6425, 0xffffe78b35d4c600, 0)`.
  Recorded dump: `C:\Windows\Minidump\101626-7281-01.dmp`.
- Event 6008 records the previous unexpected shutdown at **14:52:14 local**.
- Event 41 confirms an unclean reboot. No event 2004 was returned by this bounded query.

The dump was not opened or copied. This establishes a system bugcheck, **not its cause**.
Prior allocation failures and overlapping workloads justify serialized recovery, but do not
prove that memory pressure caused the Windows crash. Native Jolt access violations in game-test
logs are separate events; no causal link to the system bugcheck has been established.

## Retained validation inventory

The paths below are retained evidence, not new validation. A terminal summary establishes only
that historical run; it does not certify today's changing source. Missing summaries, NUL-padded
output, a surviving DLL and isolated reruns must not be promoted into completed suite passes.

| Evidence | Observed completion and limitation |
| --- | --- |
| [Loading full suite](../../.artifacts/loading-performance/full-tests-final.log) | Completed **397/398 in 418.3 s**; authoritative melee hit-marker test failed. [Isolated rerun](../../.artifacts/loading-performance/melee-isolated-rerun.log) passed 1/1 in 4.8 s. Full-suite checkpoint remains failed. |
| [Attack focused run 11](../../.artifacts/attack-system/attacks11.log) | Completed **32/32 in 34.3 s**. Later cancellation/empty-cannon work needs its own final validation. |
| [Attack full suite](../../.artifacts/attack-system/full.log) | Ends after starting `BuildingPersistenceTests.DecayCatchesUpOnLoad`; no final suite summary. [Persistence rerun](../../.artifacts/attack-system/persistence-recheck.log) passed 2/2 in 5.1 s. |
| [Attack build 12](../../.artifacts/attack-system/build12.log) | Observed output ended after test-DLL emission with NUL padding and without `Build succeeded`. Not a confirmed completed build at inspection. Initial inventory returned differing views of `attacks12.log` (empty metadata, then partial content); this did not establish a live process. P5 confirmed 677 bytes, last write 14:53:09 local **before** the 15:10 boot, three passes then an unfinished fourth. Treat it as incomplete historical evidence, not a post-recovery run. |
| [Attack 100-bot log](../../.artifacts/attack-system/load100.log) | Contains a complete-looking measured report: Fight, 10 s warmup, 30 s measured, 902 ticks; 9.32 ms mean/16.6 ms p99/30.3 ms worst; 100 playing, zero disconnected; machine memory maximum 80%. It labels the surface **stand-in ball**, not current Earth. No client rendering FPS or clean process exit is established by this report. Do not repeat this load during recovery. |
| [Earth full suite](../../.artifacts/earth-land-tuning/full-tests.log) | Five assertion failures (melee attempt settlement, station shots, battery shots, held marker, protocol round trip), then **0xC0000005 in `JPH_PhysicsSystem_Create`** during `RaidAlertTests`. No final summary. Concurrent source/build activity prevents clean attribution. |
| [Earth isolated suite](../../.artifacts/earth-land-tuning/isolated-full-tests.log) | `GunNetTests.GunfightOverTheNetwork` failed (“reloading at once”); ends during `IronWakeNetTests.APassengerIsPredictedExactlyWhileAnotherFlies`. No final summary. [Prior-failure rerun](../../.artifacts/earth-land-tuning/isolated-prior-failures.log) passed 6/6 in 66.0 s; does not close the incomplete full run. |
| [Admin integrated build](../../.artifacts/admin-self-camera/integrated-build.log) and [focused tests](../../.artifacts/admin-self-camera/integrated-focused-tests.log) | Build terminal summary: zero warnings/errors, 6.47 s. Focused camera/seat checks: **7/7 in 0.8 s**. Respect the admin owner's staged files and separate runtime evidence. |
| [Disconnect focused checks](../../.artifacts/disconnect-check/focused-tests.log) | Completed **20/20 in 27.9 s**. [Lifecycle](../../.artifacts/disconnect-check/lifecycle.log) reports PASS at 32.2 s for host disconnect, remote join/disconnect and host again; world rendering was suppressed per DEVELOPMENT.md. |
| [Disconnect full suite](../../.artifacts/disconnect-check/full-tests.log) | No terminal summary; includes generated-serializer and player-save-version failures and ends in TargetHealth tests. DEVELOPMENT.md reports out-of-memory termination after 381 successful tests. Distinguish the owner's recorded termination from what this stdout tail alone proves. |
| P1/P2 artifacts | Saved cliff baseline sources and P2 material/water sampling/normal-correlation reports survive. These are source/data checks, not new hosted rendering or performance passes. P2 owns the new terrain route. |

Startup/cleanup evidence is independent of the repaired 97% steady-frame wait:

- [Earth child-host startup](../../.artifacts/earth-land-tuning/ground-child.stdout.log) and
  [disconnect cold startup](../../.artifacts/disconnect-check/cold-start.log) show server Timeout
  followed by client PeerNotFound while loading. The first diagnostic should retain normal timeouts.
- [Disconnect renderer errors](../../.artifacts/disconnect-check/godot-errors.log) include null
  allocation parameters and a native fatal error. The headless attempt additionally reports wrong
  and uninitialized RIDs; it is not an accepted renderer substitute.
- [Earth beach cleanup](../../.artifacts/earth-land-tuning/beach.stderr.log) and globe cleanup retain
  Texture RID/ObjectDB warnings. A warning does not establish which owner retained a resource.
- Admin runtime used a temporary 120-second transport timeout after normal startup failed; the
  source timeout edits were restored. That success validates the camera scenario, not startup liveness.

## Existing performance evidence and its limits

[Loading summary](../../.artifacts/loading-performance/summary.json), RTX 2070 SUPER, 1600×900:

| Historical stationary sample | Mean | Median | p95 | p99 | GPU | Render CPU |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Custom, baseline start | 16.401 ms | 16.531 ms | 17.779 ms | 18.411 ms | 15.739 ms | 2.256 ms |
| Custom, restored baseline | 16.429 ms | 15.337 ms | 24.661 ms | 36.589 ms | 15.943 ms | 2.124 ms |
| Low | 5.406 ms | 5.348 ms | 6.312 ms | 7.104 ms | 4.483 ms | 1.088 ms |

Custom submitted 1,561 draws/7,293,569 primitives and reported 4,675.9 video MiB; Low submitted
443 draws/1,703,985 primitives and 4,161.7 video MiB. This compares different quality settings,
not a quality-preserving speedup. Even the restored Custom baseline has materially different
tail latency: do not overinterpret a small mean change from a single pass.

[Environment evidence](../REPAIR_ENVIRONMENT_2026-10-16.md) records High/day walking medians
14.25–15.50 → 15.19–17.68 ms, woodland p95 16.73 → 19.89 ms; Maximum/night woodland median
28.04 → 36.20 ms. These are historical geography and single paired runs. Current Earth revision 2
has a different, not-yet-driven 786.5 m route. P2 must establish its baseline before comparison.

[Gathering evidence](../REPAIR_PERFORMANCE.md) already verifies removal of redundant plant uploads
and outcrop rebuilds on seven nonterminal tree hits, preserving the final fall/depletion update.
Both timing comparisons overlapped other imports/rendering and cannot establish overall FPS gains.
First-hit setup and worst frames without gather events remain unexplained. Preserve this fix.

No evidence presently attributes normal-play FPS loss to gibs. N11 is a requested cosmetic budget
control; implementing it must not be presented as solving N12's steady-state environment cost.

## Initial bounded N11 implementation proposal

Read-only source findings:

- `game/data/ground/gibs.json` limits 640 simultaneous pieces, pieces within 70 m, dust within
  170 m, busy threshold 0.75 and maximum lifetime 14 s. `GibCatalog` permits data caps up to 8,192.
- `GibWorld._Ready` allocates the catalogue-size piece array once. `Place` emits dust before its
  near-distance piece gate; busy mode drops below-median-volume fragments. `Claim` recycles a
  non-flying piece preferentially, otherwise a ring slot. Batches retain capacity after use.
- `GibsView` consumes authoritative Destroyed events and plays the break sound independently
  of `GibBreak.Pieces`. Changing fragment presentation need not change damage, loot or world collision.
- Graphics settings already have saved/current values, Preview/Changed, Save and Revert; the
  effects section has no gib-specific row. No current diff was observed in Gibs or Settings files.

Propose a **GIB AMOUNT** control explicitly described as the maximum number of cosmetic fragments
alive at once, with minimum 0 and maximum the existing 640-piece catalogue ceiling (a bounded
percentage may be used internally). Default/missing-save behavior stays at today's maximum.
No automatic lowering of other quality settings or near/far distances. Minimum keeps destruction,
loot, dust and break sounds; maximum preserves current fragment presentation. This is an explicit
user tradeoff, not an invisible optimization.

Live reductions must evict excess cosmetic pieces and update visible batch counts immediately,
including while paused; increases permit future fragments without recreating vanished debris.
Use stable eviction order, retain existing full-budget behavior, and clamp malformed preference
values. A zero budget must avoid piece/floor work without suppressing dust. Subscribe once and
unsubscribe on exit so reconnects do not accumulate settings callbacks. Do not grow beyond the
catalogue ceiling or regenerate assets. P7 owns roof fracture geometry and its future regeneration.

First production file claims (subsequently approved by the coordinator):

1. `game/scripts/Gibs/GibWorld.cs` — effective budget, live trimming and settings subscription.
2. `game/scripts/Settings/GraphicsSettings.cs` — saved cosmetic amount and normalization.
3. `game/scripts/Settings/SettingRows.cs` — clear minimum/maximum control and description.

Runtime diagnostics additionally need a new `game/scripts/Gibs/GibBudgetCheck.cs` and a
narrow `game/scripts/App/Main.cs` dispatch seam; these were also approved before editing. Prefer
a small real-engine check for live batches/preview/revert/cleanup over tests that only duplicate
an arithmetic implementation. Persistence checks must use isolated config paths or restore and
verify the exact original bytes; no silent writes to the owner's saved graphics preferences.

Inspected SHA-256:

| File | Hash |
| --- | --- |
| GibWorld.cs | `74929E9812E67834A05BAEAE53A8D682FF503ECF086EC62664D5AB9E56A07889` |
| GibCatalog.cs | `AC681C943B7EF1D71F8DF53D32609C6D7DD2FA6D7FCFF35CA976EE60F09BE562` |
| GraphicsSettings.cs | `7D18B5ADE129B572CABF2E300962190EBEB391DF4AB796F30D0FD899141F8E74` |
| SettingRows.cs | `573EE3960159F2958C646B18D83BACB1138AED70DF4C38DBDA7D5A076A1ACAFC` |

## Smallest next jobs and remaining work

All are proposals requiring an exclusive coordinator slot. Do not launch them together.

1. **Stable-source checkpoint first:** coordinator freezes protocol/generator/source and schedules
   one single-worker solution build; use that same output for subsequent checks. A completed
   historic build is not sufficient for today's modified source.
2. **First requested P8 runtime slot:** one no-build engine-free process, filtered to `GibTests`,
   `LoadingTests` and `SurfaceStreamingTests`, maximum 60 s wall time, no server/GPU/import.
   Command after the coordinated Debug build:
   `dotnet tests/Hollow.Tests/bin/Debug/net10.0/Hollow.Tests.dll GibTests LoadingTests SurfaceStreamingTests`.
   Retain output and exit code; if interrupted, retain partial output without rerunning automatically.
   This checks retained catalogue/loading foundations, not the unimplemented N11 behavior.
3. Attack owner finishes focused checks and investigates its timing fixture; Earth owner resolves
   source/hash consistency. Only then schedule one integrated full suite. Do not launch another
   P8 copy or reproduce the 100-bot run during recovery.
4. After source claims and N11 implementation, build once with other ready work; run one bounded
   gib scene with minimum → maximum → lower-live-budget → revert. Verify live/visible caps,
   preserved dust/sound, subscription cleanup, malformed/missing/save-reload preferences and one
   authoritative destruction/loot regression. Capture outside the measurement interval.
5. Before broad profiling, one ordinary child-host startup with a fresh Earth-v2 diagnostic save,
   fixed 1600×900 High/day settings and normal timeouts. Cap the whole attempt at 120 s and stop
   on the first loading disconnect/allocation error. Preserve phase, memory and process evidence.
   This is a liveness check, not a performance benchmark; do not conceal failure by extending timeouts.
6. Reuse P2's new ready route baseline before any current-Earth performance claim. Then a single
   eight-second stationary sample (plus two-second settle), and separately idle/first-hit/repeated-hit
   windows at the same ready pose. Route traversal, firefight/destruction, host/guest map and Vehicle
   Workbench coverage remain later serialized acceptance jobs.

For each approved runtime job, take a fresh resource/process snapshot; record source/DLL hashes,
exit status and memory during the run. Proposed conservative stop thresholds for this 16 GiB
machine: stop only the owned diagnostic if available RAM falls below 2 GiB or system commit exceeds
85% of the limit; never stop other applications. These are recovery guardrails, not measured hardware
limits. Do not start a second job to investigate a failing first job. Keep duration and sampling
bounded, preserve failed evidence, and obtain a new slot for retries.

Coordinator owns root DEVELOPMENT/KNOWN_PROBLEMS/backlog updates. Report this inventory there;
do not close N11, N12, startup liveness, exit-resource cleanup or full-suite reliability from it.

## N11 source checkpoint — awaiting coordinated build/runtime

The user explicitly requested continuing coordination and implementing after ownership confirmation.
The coordinator approved the three production files, then the two diagnostic files. No subagents
or heavy jobs were started. The claimed production files contained no NUL bytes and matched the
initial inventory hashes before editing; no other owner's source changes were present in them.

- [GraphicsSettings.cs](../../game/scripts/Settings/GraphicsSettings.cs): `GibAmount` is an integer
  0–100 percentage of the catalogue's live-fragment cap, saved as `graphics/gib_amount`. Missing
  keys preserve 100%; numeric bounds clamp; non-numeric/non-finite values default to 100. Preview
  clamps the amount. Existing presets preserve the player's gib preference; defaults restore 100.
- [SettingRows.cs](../../game/scripts/Settings/SettingRows.cs): GIB AMOUNT runs MIN → 10–90% → MAX.
  The explanatory line says this is the live debris limit and minimum retains destruction dust/sound.
  It replaces the duplicate Graphics shadow-distance row, keeping 24 rows and the existing Shadows
  tab's shadow-distance control intact. No ground UI layout or scaling code changed.
- [GibWorld.cs](../../game/scripts/Gibs/GibWorld.cs): loads the catalogue before allocating the
  bounded array, derives the effective piece limit, retains dust before the zero-budget gate, and
  uses that limit for busy thinning and claims. Lowering the limit trims settled pieces first in
  stable ring order, then flying pieces, in at most two linear passes; visible batch counts update
  immediately even while paused. Increasing the limit admits future pieces without resurrecting
  removed ones. Empty worlds skip fragment floor/integration work. Entry/exit subscribes/unsubscribes
  settings updates. Damage, loot, authoritative collision, near/far distances, assets and sound
  routing remain unchanged. This does not claim to fix the existing general exit-resource warnings.
- [GibBudgetCheck.cs](../../game/scripts/Gibs/GibBudgetCheck.cs) and the narrow
  [Main.cs](../../game/scripts/App/Main.cs) dispatch: `--gib-budget-check <report.json>` prepares an
  actual small-box gib fixture in a 960×540 independent viewport. No terrain/server/save is loaded;
  workloads above a 1,024-piece catalogue cap are refused. It checks shared preference serialization
  using a separate diagnostic `.cfg`, malformed/missing inputs, row endpoints, preset preservation,
  minimum dust, full-break fragments, floor integration, cap saturation/recycling, live reductions,
  immediate visible counts, increases, zero cleanup, detach/reentry/free and shared Revert. It hashes
  the player's three preference files before/after and never calls their Save path. Optional
  `--shot <prefix.png>` writes `<prefix>-minimum.png` and `<prefix>-maximum.png` outside measurement;
  these are diagnostic images, not hosted combat/loot evidence or a performance comparison.

**Initial validation:** source/diff review, byte/NUL inspection and `git diff --check` only.
The later coordinated build passed as recorded below. Engine checks, screenshots, saved-settings
UI interaction and gameplay are not yet completed.
No pass count is claimed for the new diagnostic. Full application APPLY/restart behavior and audible
destruction cues still need the coordinated runtime scenario; shared serializers alone do not prove it.

Requested subsequent serialized runtime jobs, after the coordinator's integrated build succeeds:

1. The previously queued engine-free gib/loading/streaming filters (coordinator may include them in
   its foundation test group rather than launch another process).
2. Native Godot `--path game --resolution 1600x900 -- --gib-budget-check ABSOLUTE_REPORT.json
   --shot ABSOLUTE_GIB.png`, outer wall-clock limit 60 seconds, no other GPU/build/test work.
3. Separate existing UI viewer `--view ui --screen settings --shot ABSOLUTE_SETTINGS.png` at
   1600×900 to inspect the actual new row and retained two-column layout. Additional resolution
   checks only if the first result exposes a layout concern.

Source was handed to the coordinator for the shared freeze/build. Runtime approval remains pending.

### Integrated build interrupted by memory guard

The coordinator's 15:35:54 integrated build ran for 34.69 seconds before its guard stopped the owned
build process tree. Available RAM reached **1.566 GiB**, below the 2 GiB floor; commit was approximately
74%. No tests or GPU jobs followed. [Build stdout](../../.artifacts/recovery/integrated-checks/20261016-153554-build.stdout.log)
contains DLL-emission lines including the game, but **no completed solution summary**. The stderr file
is empty, and no warning/error line names the P8 files. This is **not a build pass** and emitted DLLs
are not a fresh validated checkpoint. Retain the [receipt](../../.artifacts/recovery/integrated-checks/20261016-153554-build.receipt.json)
and [memory samples](../../.artifacts/recovery/integrated-checks/20261016-153554-build.memory.csv).

P8 did not retry, run partial outputs, start tests/rendering, or alter source in response. This report
update is documentation only. The coordinator released the compilation freeze for light owned work,
but a new runtime job requires **at least 5.5 GiB available RAM and a fresh explicit one-job grant**.
The stop thresholds remain below 2 GiB available RAM or above 85% commit. The owner's applications,
including Blender, remain untouched. N11 build/runtime/visual validation and N12 remain open.

### Successful integrated build after resource recovery

After the human reported their Blender job had finished, P8 confirmed all five source hashes still
matched the frozen checkpoint and all five files were NUL-free. The coordinator's single-worker
incremental build at **17:18:05 local** completed successfully: **zero warnings, zero errors** and
exit code 0. The compiler reports 10.90 seconds; the wrapper receipt records 12.87 seconds elapsed,
5.983 GiB minimum available RAM and 48% maximum commit. Evidence:
[build stdout](../../.artifacts/recovery/integrated-checks/20261016-171805-build.stdout.log),
[receipt](../../.artifacts/recovery/integrated-checks/20261016-171805-build.receipt.json), and
[frozen source manifest](../../.artifacts/recovery/source-freeze-20261016T141805Z.json).

This confirms compilation of the gib candidate with the coordinated source. It does not supersede
the failed earlier attempt or establish behavioral/visual validation. P5's focused tests and root's
foundation batch are serialized before any P8 GPU grant. Source/data remain frozen; P8 launched no
build, tests, GPU job or Git operation. N11 runtime/visual acceptance and N12 remain open.

## Existing automatic LOD — routine asset-integration verification only

The human asked whether the project should generate its own LODs rather than require imported
manual LOD levels, and whether a Nanite-like system already exists. The coordinator's source review
found no custom virtualized geometry renderer. P8 checked the following existing mechanisms:

- `GraphicsSettings.ApplyGlobal` sets `viewport.MeshLodThreshold` from Model Detail. This controls
  Godot's automatic mesh LOD selection; it does not prove that every imported mesh contains usable
  generated LODs.
- `TerrainLod` generates and selects a quadtree of cube-face terrain tiles, including morphing and
  skirts. This is the existing custom terrain system.
- `Vegetation` selects authored LOD0/LOD1 meshes and impostors with transition state.
- `CliffField` selects Near/Far meshes. Current authored cliff `.gltf.import` files explicitly set
  `meshes/generate_lods=false`. Some older cliff `.glb.import` files set it to true, so **do not
  generalize the current authored-path setting to every cliff import**.

The human subsequently clarified that **no new LOD/Nanite project is needed**. The earlier proposal
for a separate automatic-LOD performance workstream is withdrawn. Verify the existing engine path
only as a routine part of actual asset integration; no optimization/rewrite project is authorized.
The newly reported PSG_Compound_Utilities_R02 kit's twelve LOD0 GLBs are candidates after the
asset owner's read-only audit; absence of manually named LOD nodes is not itself a defect.

When integrating an authorized rigid-prop asset, check its existing/generated LOD selection at fixed quality,
resolution, camera, lighting and distances; inspect silhouette, thin parts, material boundaries,
and visible transitions, alongside frame-time tails, submitted geometry and memory. Retain an
authored fallback where automatic simplification fails the visual comparison. Keep interaction
sockets, placement rules and authoritative collision independent of presentation simplification.
Coordinate mesh/import ownership with P1/P2 and the asset owner; do not bulk-reimport the library.

No LOD source, architecture, assets, import settings or runtime parameters changed for this note.
No separate baseline campaign or runtime slot is requested for LOD. The pending P8 runtime work
remains recovery validation and the existing gib diagnostic/settings captures. This note does not
claim a measured performance improvement.

## Bounded helper-process lineage inventory — 17:31–17:32 local

At the coordinator's request, P8 took one read-only resource/process snapshot at **17:31:45 local**
and one targeted ancestry follow-up at **17:32:26**. No continuous polling, full command-line reads,
tokens, process termination, app closure/restart, source edits, tests or GPU work. Observed 765
processes; 15.92 GiB physical RAM, **2.56 GiB available**, 20.75/33.41 GiB system commit (~62%).
No dotnet/MSBuild/Hollow/Godot/Blender executable was present in the targeted follow-up.

Process-family totals below are **sums of per-process counters**, not unique physical memory or
predicted reclaimable RAM. Working sets can count shared pages more than once; private bytes are
commit, not necessarily resident memory. Do not add these totals to explain the machine's used RAM.

| Family | Count | Summed working set | Summed private bytes |
| --- | ---: | ---: | ---: |
| node.exe | 115 | 5,286.2 MiB | 5,715.5 MiB |
| brave.exe | 19 | 3,043.1 MiB | 2,739.6 MiB |
| claude.exe | 12 | 2,153.3 MiB | 1,750.8 MiB |
| conhost.exe | 233 | 1,842.0 MiB | 1,443.7 MiB |
| ChatGPT.exe (Codex desktop executable) | 15 | 1,802.3 MiB | 2,486.2 MiB |
| codex.exe | 5 | 1,610.1 MiB | 1,499.1 MiB |
| python.exe | 44 | 1,178.1 MiB | 943.5 MiB |
| node_repl.exe | 82 | 791.3 MiB | 215.3 MiB |

### Ownership evidence

Parent IDs were checked against the same process snapshot and parent creation times; a later
parent creation would indicate PID reuse. None of the node/node_repl/claude/conhost processes had
a missing or reused **direct** parent in the first snapshot. Parent liveness proves attachment,
not that every child is actively useful or tied to a currently running chat.

- **Current Codex engine PID 11148**, started 15:11:49 local, is a child of current Codex desktop
  `ChatGPT.exe` PID 8328, started 15:11:42. It owns ancestry for 507 of the selected helper-family
  processes: 114 node, 82 node_repl, 221 conhost, 36 Python, 18 blender-mcp, 32 StudioMCP and four
  codex processes including itself. Most helper pressure is attached to this live app, not a dead
  top-level owner. The executable is under `AppData/Local/OpenAI/Codex/bin/.../codex.exe`.
- Concrete current-app chains include `node.exe:15992 <- cmd.exe:13832 <- codex.exe:11148`
  (node started 17:20:24) and
  `node.exe:23888 <- codex-command-runner:21384 <- codex.exe:11184 <- node_repl.exe:14416
  <- node.exe:25760 <- codex.exe:11148` (leaf started 17:09:56).
  The nodes use the Codex `runtimes/cua_node/.../bin` executable. Overall, 86 nodes and all 82
  node_repl executables came from that runtime; 28 other nodes used `D:/GameDev/Tools/Node-v22.23.2`.
- A connector example is `python.exe:26792 <- blender-mcp.exe:26436 <- codex.exe:11148`,
  started 17:04:22. `StudioMCP.exe:2364 <- cmd.exe:11840 <- codex.exe:11148` uses the installed
  Roblox Studio MCP executable. Executable identity and live parentage do **not** establish
  which chat owns each connector or whether that connector can be stopped safely.
- **Claude desktop PID 17740**, started 17:22:29 and parented by Explorer, owns 24 selected
  processes: 12 Claude processes, two blender-mcp, four Python, five conhost and one StudioMCP.
  This is a separate current user application, not a Hollow workload or P8-owned job.
- Some browser helper chains reach live `brave.exe:19424 <- explorer.exe:5228`. Explorer's
  original launcher PID 5208 is absent, which is not evidence that the browser/helper tree is
  abandoned. Similarly, `conhost.exe:19008` belongs to live `goodbyedpi.exe:21228`, whose earlier
  launcher PID 12060 is absent. Do not classify these as disposable or interfere with networking.

### One detached Python tree; no authorized cleanup

`python.exe` **PID 3992**, started **15:15:49.641 local**, has missing direct parent PID 5656.
Its executable is `D:/GameDev/MCP/venv/Scripts/python.exe`. Its observed descendants were
`conhost.exe:7984` and Python 3.12 `python.exe:4784`, started within 15 ms of it. Their combined
working set in the ancestry snapshot was **10.5 MiB** and private bytes **30.4 MiB**. This is
concrete evidence of a detached tree, not proof that a service/client no longer uses it. No
command-line or socket/session inspection established its purpose or ownership.

**No cleanup is already authorized by this audit.** The current instruction explicitly forbids
stopping anything; no inspected persistent helper was proven to belong to a completed P8 job.
The tiny detached tree cannot recover the ~3 GiB additional available RAM needed to meet the
5.5 GiB runtime-start gate. Terminating current Codex/Claude/browser helper trees by executable
name or count risks live sessions and tools and is not a safely scoped cleanup.

Any later action needs the human's choice of an app/session to close or an app-supported unused
session cleanup, after preserving its work. Mapping helpers to inactive Codex sessions would need
additional app-level evidence; parentage alone is insufficient. If a specific PID tree is later
approved, revalidate its creation times/parentage before acting because PIDs are reusable. P8
performed **no cleanup**, changed no source/hash, and left all validation held.

## GPU readiness checkpoint after the 19:21 build

The shared Debug build beginning **19:21:47 local** completed with zero warnings/errors, exit 0:
[stdout](../../.artifacts/recovery/integrated-checks/20261016-192147-build.stdout.log) and
[receipt](../../.artifacts/recovery/integrated-checks/20261016-192147-build.receipt.json).
Compiler time was 38.42 seconds; wrapper time 38.95 seconds, minimum available RAM 6.245 GiB,
maximum commit 50%. The coordinator reported combat 68/68 and started the foundation batch;
that does not grant P8 a parallel runtime slot.

Read-only readiness verification found all five P8 source files identical to the frozen hashes
and NUL-free. Current Debug game DLL SHA-256:
`A465875BF92C350C1B771479C2C0D60EDA0ED398DD4CAA51F017E35D8AE7CFA8`.

Located the console executable without launching it:
`D:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe`.
Windows file metadata reports 4.7.2. The matching regular executable is beside it; `GODOT` is
unset and `play-ground.cmd` points to that directory. Do not use `play-ground.cmd` for this check,
because it can build/import/host beyond the granted diagnostic scope.

Prepared [Run-GibValidation.ps1](../../.artifacts/p8-gibs/Run-GibValidation.ps1) outside compilation
source. PowerShell parsed it with zero syntax errors; it has **not been executed**. It requires
an explicit slot flag, at least 5.5 GiB available RAM and no competing build/game/Blender process.
It records source/DLL hashes, arguments, memory, stdout/stderr, exit/stop reason and preference
hashes. It stops its own process tree below 2 GiB available/above 85% commit or at the bounded
deadline (shutdown begins at 58 seconds, reserving two seconds). It never terminates another job,
retries automatically, builds, imports, stages Git or restores/overwrites the user's preferences.

Exact prepared invocation **only after the coordinator grants that job**:

```powershell
& 'D:\GameDev\Projects\HollowUniverseABTestB\.artifacts\p8-gibs\Run-GibValidation.ps1' -Check gibs -RunId '20261016-01' -CoordinatorSlotGranted
```

This launches Godot with:

```text
--windowed --path D:\GameDev\Projects\HollowUniverseABTestB\game --resolution 1600x900 -- --gib-budget-check D:\GameDev\Projects\HollowUniverseABTestB\.artifacts\p8-gibs\20261016-01-gibs\report.json --shot D:\GameDev\Projects\HollowUniverseABTestB\.artifacts\p8-gibs\20261016-01-gibs\gibs.png
```

Outputs are `report.json`, `gibs-minimum.png`, `gibs-maximum.png`, an isolated generated preference
fixture, and `launch.json`, `receipt.json`, `memory.csv`, `stdout.log`, `stderr.log` in that folder.
The diagnostic viewport is 960×540; the application window is 1600×900. Inspect the original
images and all failed check/error lines before declaring success.

Separate settings capture, **only after the first job releases the slot and this job is granted**:

```powershell
& 'D:\GameDev\Projects\HollowUniverseABTestB\.artifacts\p8-gibs\Run-GibValidation.ps1' -Check settings -RunId '20261016-01' -CoordinatorSlotGranted
```

Arguments: `--windowed --path D:\GameDev\Projects\HollowUniverseABTestB\game --resolution 1600x900
-- --view ui --screen settings --placeholder-icons --shot
D:\GameDev\Projects\HollowUniverseABTestB\.artifacts\p8-gibs\20261016-01-settings\settings.png`.
This uses the existing settings viewer/capture isolation; placeholder item icons avoid unnecessary
icon assets on a screen which does not display them. Outputs and guard receipts are in the separate
`20261016-01-settings` directory. Existing evidence folders are refused rather than overwritten.

No engine launch, tests, import, GPU capture, configuration change or Git operation was performed
during readiness preparation. Compilation source/data remains frozen. Await the explicit GPU grant.

## First authorized native gib diagnostic and settings capture

The coordinator granted two sequential native jobs from the existing Debug build, each bounded to
60 seconds, after its foundation batch completed **61/64**. All GibTests, LoadingTests and
SurfaceStreamingTests in that [foundation log](../../.artifacts/recovery/integrated-checks/20261016-192939-foundation.stdout.log)
passed; the three other failures were routed to the cliff and clan-base owners. No complete-suite
pass is implied. P8 ran only the two granted jobs, without build/import/retry/source edits.

Both jobs used game DLL SHA-256 `A465875BF92C350C1B771479C2C0D60EDA0ED398DD4CAA51F017E35D8AE7CFA8`.
Their launch manifests record the same five owned source hashes as the frozen checkpoint. Native
logs identify **Godot 4.7.2 .NET, Vulkan Forward+, RTX 2070 SUPER**.

| Job | Result | Wrapper elapsed | Available RAM start / minimum | Maximum commit |
| --- | --- | ---: | ---: | ---: |
| Gib diagnostic, 19:35:16 local | **37/42 passed, five assertion failures; exit 1** | 4.19 s | 6.125 / 5.692 GiB | 55% |
| Settings capture, 19:35:43 local | Ready capture, inspected; exit 0 | 4.01 s | 6.353 / 5.378 GiB | 58% |

Neither memory/deadline guard triggered. Both stderr logs are empty. No Godot/Hollow server/test
process remained at the cleanup check. No user application was closed or stopped.

### Gib checks: retain the five failures

[Report](../../.artifacts/p8-gibs/20261016-01-gibs/report.json),
[stdout](../../.artifacts/p8-gibs/20261016-01-gibs/stdout.log),
[stderr](../../.artifacts/p8-gibs/20261016-01-gibs/stderr.log),
[launch/hash manifest](../../.artifacts/p8-gibs/20261016-01-gibs/launch.json),
[receipt](../../.artifacts/p8-gibs/20261016-01-gibs/receipt.json),
[memory](../../.artifacts/p8-gibs/20261016-01-gibs/memory.csv).

Passing checks include missing/malformed/clamped preferences, isolated save/load serialization,
row endpoints and 24-row fit, preset preservation, minimum dust with no fragment-floor work,
all six small-box fragments at maximum, retained floor integration, future-only budget increases,
exit/reentry/free settings lifecycle, Revert and unchanged user preferences.

The failures all include the diagnostic's visible-instance readback:

| Check | Live piece count | Reported visible count | Limit |
| --- | ---: | ---: | ---: |
| Maximum saturated | 640 | 1,152 | 640 |
| Live reduction while paused | 64 | 1,152 | 64 |
| Repeated breaks at reduced cap | 64 | 1,152 | 64 |
| Zero clears batches | 0 | 1,152 | 0 |
| Reentered world breaks once | 6 | 1,152 | 640 |

These are **not accepted passes**. Source review shows `GibWorld.Upload` writes visibility through
`RenderingServer.MultimeshSetVisibleInstances`, while the new diagnostic reads the `MultiMesh`
resource's `VisibleInstanceCount`, treating -1 as its allocated capacity. The constant 1,152 is
consistent with reading resource-side capacity rather than the server-side count, but this remains
a **diagnostic hypothesis**, not proof that the rendering is correct. The installed Godot API
exposes `RenderingServer.MultimeshGetVisibleInstances` for directly querying the same RID.

Proposed narrow next step: correct the diagnostic to query the RenderingServer RID count, retain
the original resource value as context, and capture a cleared saturated scene to check for ghost
fragments. Do not weaken the live/visible cap assertions. This requires a coordinated source/build
checkpoint and another bounded runtime grant. No such correction or rerun was performed in this slot.

### Original images inspected

- [Minimum](../../.artifacts/p8-gibs/20261016-01-gibs/gibs-minimum.png), 960×540: destruction dust
  and small FX specks on the neutral floor; no box fragment meshes visible.
- [Maximum](../../.artifacts/p8-gibs/20261016-01-gibs/gibs-maximum.png), 960×540: recognizable
  fractured box panels and dust. This is a single break before budget saturation, so it does not
  settle the later 1,152-count failures or prove zero-budget cleanup after saturation.
- [Settings](../../.artifacts/p8-gibs/20261016-01-settings/settings.png), 1600×900: GIB AMOUNT
  label, MAX endpoint, slider and explanatory text are readable in the right column. The existing
  two-column panel and footer fit without clipping or overlap. The retained SHADOWS tab is visible;
  shadow-distance control presence is covered by the diagnostic's row check, not this screenshot.

Settings evidence: [stdout](../../.artifacts/p8-gibs/20261016-01-settings/stdout.log),
[stderr](../../.artifacts/p8-gibs/20261016-01-settings/stderr.log),
[launch/hash manifest](../../.artifacts/p8-gibs/20261016-01-settings/launch.json),
[receipt](../../.artifacts/p8-gibs/20261016-01-settings/receipt.json),
[memory](../../.artifacts/p8-gibs/20261016-01-settings/memory.csv).

### Preference preservation and release

Both launcher receipts confirm byte-identical user preferences before/after; the gib diagnostic's
independent in-engine hash check agrees. SHA-256 values remained:

- `graphics.cfg`: `11131498A9A52C3E37C611F185057EC87F48BE64B85F16792BD5980244E8DFDC`
- `ground-settings.cfg`: `8045B9C414CA63166F4334911755B173FE410C7EBB9DE2C9A885316DFF51C683`
- `settings.cfg`: `3D88ECE560546D31B8B1DF10B7DCD93F42FE8864475F4A2D1F98DD9B862928AB`

**The P8 exclusive heavy slot is released.** Only the report changed after the two jobs. N11 remains
open for the five readback failures and final gameplay/settings APPLY/restart acceptance; audible
break sounds and authoritative destruction/loot were not exercised by this isolated fixture. N12
performance, startup transport liveness and general exit-resource cleanup remain open. No overall
FPS or memory optimization claim follows from these diagnostic runs.

## Renderer-count diagnostic correction; source refrozen

After the two native jobs, the coordinator explicitly authorized a narrow source correction within
the five owned files, with no runtime, build, tests, Git operations or new agents. Only
`game/scripts/Gibs/GibBudgetCheck.cs` changed in this correction; the production gib/settings/UI
code and dispatch remain byte-identical to the first native checkpoint.

The readback defect is the mismatch between `GibWorld.Upload`, which writes visible-instance counts
through `RenderingServer.MultimeshSetVisibleInstances(batch.Rid, ...)`, and the diagnostic, which
read the `MultiMesh.VisibleInstanceCount` resource property. Its fallback translated the resource's
all-instances sentinel into allocated capacity. This explains the observed constant-capacity
measurement pattern; the next run must still establish the actual renderer counts and prove the
feature's cap/clearing behavior. The installed Godot 4.7.2 .NET XML API documents both
`MultimeshGetVisibleInstances(Rid)` and `MultimeshGetInstanceCount(Rid)` for reading server state.

The correction reads the same renderer RID, handles exactly the documented `-1` all-instances
sentinel, and keeps all five live/visible assertions. It additionally rejects out-of-range renderer
counts. Each checkpoint now records every batch's allocated capacity, raw renderer visible value
and raw resource visible value, with the aggregate live count and budget, in the JSON report. This
will distinguish a resource metadata mismatch from a real renderer visibility failure.

The immediate saturated-to-zero assertion is retained. A second assertion checks that the scene
stays empty across three process frames, followed by a new `gibs-cleared.png` capture. Only the
independent FX director is cleared for that image; no second gib clear is called, so residual gib
meshes cannot be hidden by diagnostic cleanup. The original failed report and captures are retained.

Refrozen SHA-256 values:

| Owned file | SHA-256 | Change in this correction |
| --- | --- | --- |
| `game/scripts/Gibs/GibBudgetCheck.cs` | `2BD9F5F41E6D0B1DD6A5B90AC69591BD5196B5D26C8FDA81E16976E767248F6F` | Renderer readback, batch evidence and cleared-scene check/capture |
| `game/scripts/Gibs/GibWorld.cs` | `2972E2E5499883A5B18B38E0DF1389D54C05BD866B342BCA637B5644CB9F4B2E` | Unchanged |
| `game/scripts/Settings/GraphicsSettings.cs` | `890EB8CA007B94682CC7B5CBCD97AC5053F4B42FDAA0B41279C2D65233D1B128` | Unchanged |
| `game/scripts/Settings/SettingRows.cs` | `E66C43B811346731ACB38508B3D4865B7B574F487543624B6879B293E66D1224` | Unchanged |
| `game/scripts/App/Main.cs` | `5B76A7A6F9CD25B0BF6FDE20A6415AEA0FFF936209571A1E85E85778FB3BC879` | Unchanged |

All five files were read back and checked for NUL bytes (zero found). **This source correction is
not compiled or runtime-validated.** The prior DLL does not contain it. The coordinator owns the
next combined build and must explicitly grant any later native rerun. Suggested new evidence ID
after that grant: `20261016-02-gibs`; use the existing guarded launcher with `-Check gibs -RunId
'20261016-02' -CoordinatorSlotGranted`. No settings recapture is needed for this diagnostic-only
change unless the coordinator changes that UI. N11 remains open pending the rerun and the previously
listed gameplay/APPLY/restart acceptance checks.

## Corrected native rerun passed; remaining acceptance unchanged

The coordinator completed the combined build and foundation checks, then ran the corrected native
gib diagnostic at **19:56:01 local**. P8 subsequently read the existing logs, manifest and receipt
and inspected the cleared image. P8 performed no new runtime, build, tests, Git operations or agent
launches for this evidence update, and changed only this report.

| Coordinator-run check | Result | Elapsed | Minimum available RAM | Maximum commit |
| --- | --- | ---: | ---: | ---: |
| `20261016-194953-build` | Exit 0, zero warnings/errors | 15.85 s compiler / 16.96 s wrapper | 4.014 GiB | 60% |
| `20261016-195242-foundation` | **64/64 passed**, exit 0 | 16.0 s tests / 17.34 s wrapper | 4.13 GiB | 60% |
| `20261016-02-gibs` | **44/44 passed**, zero failures, exit 0 | 2.51 s wrapper | 4.91 GiB | 54% |

Build evidence: [completed log](../../.artifacts/recovery/integrated-checks/20261016-194953-build.stdout.log)
and [receipt](../../.artifacts/recovery/integrated-checks/20261016-194953-build.receipt.json).
Foundation evidence: [completed log](../../.artifacts/recovery/integrated-checks/20261016-195242-foundation.stdout.log)
and [receipt](../../.artifacts/recovery/integrated-checks/20261016-195242-foundation.receipt.json).
The foundation batch resolves its three prior failures; it is not a complete-suite run.

Native evidence: [44-check report](../../.artifacts/p8-gibs/20261016-02-gibs/report.json),
[stdout](../../.artifacts/p8-gibs/20261016-02-gibs/stdout.log),
[stderr](../../.artifacts/p8-gibs/20261016-02-gibs/stderr.log),
[launch/hash manifest](../../.artifacts/p8-gibs/20261016-02-gibs/launch.json),
[receipt](../../.artifacts/p8-gibs/20261016-02-gibs/receipt.json) and
[memory samples](../../.artifacts/p8-gibs/20261016-02-gibs/memory.csv).
The manifest records the five refrozen source hashes above and game DLL SHA-256
`6E9F640EB671E2A84002B3DE500D4DE213141BA5193BD677F86778A4AE7EC407`.
The native log identifies Godot 4.7.2 .NET, Vulkan Forward+, RTX 2070 SUPER.

The paired readbacks confirm the diagnostic defect: all six resource properties remain `-1`,
which the previous reader expanded into their total allocation of 1,152. The renderer RID counts
change with the live pieces and budget:

| Checkpoint | Live pieces | Renderer visible | Budget |
| --- | ---: | ---: | ---: |
| Maximum saturated | 640 | 640 | 640 |
| Live reduction while paused | 64 | 64 | 64 |
| Repeated breaks at reduced cap | 64 | 64 | 64 |
| Zero immediately after saturation | 0 | 0 | 0 |
| Zero after three frames | 0 | 0 | 0 |
| Reentered world breaks once | 6 | 6 | 640 |

Every per-batch range check passes. P8 inspected the original 960×540
[cleared image](../../.artifacts/p8-gibs/20261016-02-gibs/gibs-cleared.png): only the neutral floor
and background remain, with no fragment meshes. The new run also saved
[minimum](../../.artifacts/p8-gibs/20261016-02-gibs/gibs-minimum.png) and
[maximum](../../.artifacts/p8-gibs/20261016-02-gibs/gibs-maximum.png) captures; P8's new visual
inspection here was specifically of the added cleared capture. The original failed evidence is
retained rather than relabelled as passing.

The native receipt has no stop reason, stderr is empty, and both the receipt and in-engine check
confirm byte-identical `graphics.cfg`, `ground-settings.cfg` and `settings.cfg`. Their hashes match
the preceding preference-preservation table. The measured start was 5.765 GiB free; minimum was
4.91 GiB. Per the coordinator's bounded-job refinement in [RECOVERY.md](../repair-tasks/RECOVERY.md),
the ignored launcher now permits a 4.5 GiB starting floor **only for `-Check gibs`**; settings keeps
5.5 GiB. The 2 GiB / 85% commit stop guard and explicit slot requirement remain. P8 did not alter
the launcher or broaden that authorization during this report update.

**N11 diagnostic cap/clear verification is now passed, but N11 is not closed.** The isolated
serializer round trips and preserved player files do not replace actual settings APPLY followed
by process restart. Audible destruction sounds, authoritative destruction/loot and normal gameplay
acceptance also remain outstanding. **N12 remains open:** no overall FPS/memory improvement,
startup transport-liveness or general exit-cleanup acceptance follows from this small fixture.
The source stays refrozen; further heavy work requires a new coordinator slot, currently held by
P2 for the world baseline.

## Fresh P2 startup failure: source diagnosis and proposed repair

The coordinator authorized light source/log diagnosis only. P8 changed only this report; no game,
build, test, import, Git operation or subagent was started. Proposed paths below require ownership
assignment before editing; the existing five-file gib checkpoint remains frozen.

### What the new evidence establishes

The run in [baseline-max-day-r3](../images/repairs/p2-environment/baseline-max-day-r3/) started at
**20:18:54 local**, using Maximum quality, the initial Earth beach, 1600×900, and an in-process
server on port 27182. [stdout](../images/repairs/p2-environment/baseline-max-day-r3/stdout.log)
shows the server started, accepted the player, and sent the welcome. The failure therefore happened
**after joining, while the client's world renderer was loading**, not while waiting for the server
to start. Ground-layer arrays took **3,490 ms**, followed by character-library messages and an animal
draw. The server then reported `Timeout`; the client later reported `PeerNotFound`. Terrain and
vegetation eventually became idle, but the client remained Disconnected/loading and never reached
a ready checkpoint. No screenshots, route report or CPU/GPU frame profiles were produced.

The [watchdog receipt](../images/repairs/p2-environment/baseline-max-day-r3/receipt.json) records exit
−1 after 49.79 seconds, sampled minimum 2.034 GiB free, maximum 83% commit, and a null stop reason.
The separate [intervention receipt](../images/repairs/p2-environment/baseline-max-day-r3/intervention.json)
explains that a secondary observation found about **1.80 GiB free** between watchdog samples and
stopped only this run's verified Godot process tree. Those observations must stay together: null
`stopReason` does not mean a successful or unguarded completion. The run started at 7.651 GiB free;
the final post-stop watchdog sample returned to 7.429 GiB. These are **system-wide** available-RAM
and commit readings, not process-private, managed-heap or GPU allocations. They cannot attribute
the approximately 5.6 GiB sampled reduction to any particular asset or cache.

For comparison, the earlier Earth beach [successful log](../images/earth-land/validation/beach.stdout.log)
reported 2,942 ms for these arrays and reached ready at 9.15 seconds, while the earlier inland
[failed log](../images/earth-land/validation/ground.stdout.log) reported 3,972 ms and `PeerNotFound`.
These are different historical checkpoints, not controlled before/after measurements. The 3.49-second
array call is a measured contributor, not sufficient proof of the entire ten-second silence.

### Concrete transport starvation path

| Source | Finding |
| --- | --- |
| `src/Hollow.Client/GameClient.cs:127–135, 393, 438` | The client uses `StartInManualMode(0)`. Receive polling and network updates happen only inside `GameClient.Update`. Its disconnect timeout is 10,000 ms. |
| `game/scripts/Ground/GroundView.cs:405–449, 481, 504` | `OpenWorld()` runs synchronously before `Client.Update` in `_Process`. It opens the surface, attaches the arms and creates eight spare bodies before returning. |
| `game/scripts/Surface/SurfaceView.cs:245–307` and `GroundLayers.cs:61–99` | Surface opening requests all 48 ground-layer textures synchronously, reads their images back, and creates all three arrays in one call. Noise/material/world setup follows on the same thread. |
| `game/scripts/Surface/Vegetation.cs:159–190` and `Flora/FloraLibrary.cs:216–284` | Completion of worker geometry triggers synchronous `FloraLibrary.Create` from the main-thread vegetation update. It loads/instantiates all authored tree and rock scenes and duplicates meshes/materials. `FloraLibrary.Trees.cs:31` and `FloraLibrary.Rocks.cs:17` use `GD.Load<PackedScene>`, not threaded requests. |
| `game/scripts/Surface/GrassField.Authored.cs:39–44` | Ground-cover family scenes are also loaded and instantiated synchronously when authored cover is initialized. |
| `game/scripts/Ground/RenderWarmup.cs:113–123` | The 12 ms limit is checked **between** whole `ShowItem` calls. A single model/character creation can exceed it; later first-draw shader/driver work is outside that CPU budget. |
| `src/Hollow.Server/GameServer.cs:114, 296, 347, 422` | The server has its own thread, continues polling/updating, and retains the normal 10,000 ms timeout while the Godot thread is blocked. |

Thus a long world-open, asset-create or first-draw interval prevents the manual client from receiving
or sending transport traffic, including keepalives, while the server continues its clock. Moving
one `Client.Update` call earlier would only move the beginning of that gap. Polling gameplay from
a new worker would violate the client's single-threaded simulation/callback contract.

This is a source-confirmed vulnerability consistent with the observed sequence. The fresh log has
neither per-line timestamps nor `--load-trace` output, so it does **not** identify which individual
call or combination produced the full timeout interval. Historical `GROUND.md §4.3.1` already
documents multi-second opening and first-draw stalls, but those old timings are not measurements
of this run. The existing `LoadTrace` can report open phases, CPU/draw time, GC pause and warm-up
costs in a separately granted bounded startup diagnostic.

### Smallest liveness repair for root review

Use the installed LiteNetLib 2.1.4 background transport for the **graphical client only**, with all
unsynchronized-event options explicitly false. Keep message dispatch, prediction, module updates
and Godot work on the caller's thread via the existing `PollEvents`. Keep manual mode as the default
for bots/tests. The package's pinned [manager source](https://raw.githubusercontent.com/RevenantX/LiteNetLib/4d3de1e93abaead30199bf572f4a3363f854e14b/LiteNetLib/LiteNetManager.cs)
confirms that normal-mode events queue until `PollEvents`, while manual mode has no background
transport progress. `Stop` joins the logic thread. The local NuGet XML documents the same contracts;
the package version and repository commit come from `litenetlib.nuspec`.

Requested ownership for this first patch:

1. `src/Hollow.Client/GameClient.cs`: add an explicit `ClientOptions.BackgroundTransport` opt-in,
   start through `Start(0)` when enabled, leave `StartInManualMode(0)` otherwise, and use one helper
   for all three update/flush sites (`Update`, `Disconnect`, `Dispose`). In background mode trigger
   a send update rather than calling `ManualUpdate` concurrently. Keep `PollEvents` on `Update` and
   stop the transport before disposing simulation modules. Preserve both 10-second timeouts.
2. `game/scripts/Ground/GroundMode.cs`: enable that option on the one normal graphical `GameClient`.
3. New `tests/Hollow.Tests/ClientTransportTests.cs`: a loopback regression that joins, withholds
   client updates for longer than the unchanged timeout while the server keeps running, and checks
   continued server membership and successful resumption. Queue an application event during the
   pause and assert it is delivered only when the caller polls, on that caller's thread. Cover
   explicit disconnect/disposal too. A manual-mode control can share the same pause window.
4. `docs/GROUND.md §4.4` (currently describes every client as manual) and `docs/ARCHITECTURE.md §11.1`:
   document transport-only worker ownership and main-thread gameplay dispatch. Root may own these
   shared-doc edits plus `DEVELOPMENT.md` rather than assigning them to P8.

This preserves scene contents, Maximum settings, simulation data and timeouts. It fixes the network
dependency on frame progress; it does not make asset construction faster or prove that the large
world now fits memory. Queued application events during a stall remain a validation concern, so
the regression must verify bounded recovery rather than merely observe a connected transport.

### Countable avoidable allocations; separate narrow patch

`FloraTextures.Dilate` (`game/scripts/Surface/Flora/FloraTextures.cs:294–338`) clones the entire
float RGBA array inside every pass. The algorithm reads the previous pass from that copy and writes
into `px`; allocating one scratch array outside the loop and copying `px` into it before every pass
preserves the operation order and output exactly. It does not reduce resolution, passes, coverage,
texture formats or visual settings.

Source-derived payload arithmetic for the current data (excluding array headers):

| Caller | Buffer | Old clone allocations | One reusable scratch | Avoided cumulative allocation |
| --- | ---: | ---: | ---: | ---: |
| `FloraTextures.Atlas`: 2048×2048×4 floats, six passes | 64 MiB | 384 MiB | 64 MiB | **320 MiB** |
| Each impostor pass: 2048×2816×4 floats, four passes | 88 MiB | 352 MiB | 88 MiB | **264 MiB** |
| Colour and normal impostor passes together | — | 704 MiB | 176 MiB | **528 MiB** |

The 11 impostor rows are seven authored variants (fir 3, birch 3, pine 1) plus four retained
procedural broadleaf variants. `ImpostorBaker` uses eight 256-pixel views and a 256-pixel row.
The combined **848 MiB is avoidable managed allocation churn**, not measured peak savings, live
heap size or an explanation for the observed system-wide memory loss. Reuse still needs a full
previous-pass buffer, so the minimum simultaneous working set of `px` plus its copy is unchanged.
The launch manifest's `FloraTextures`, `FloraLibrary`, `ImpostorBaker` and `GroundLayers` hashes match
the source inspected here. No implementation or allocation benchmark has been run.

Exact proposed additional path: `game/scripts/Surface/Flora/FloraTextures.cs`, limited to `Dilate`.
It must be reassigned from the frozen environment ownership before editing. Validate by comparing
the existing and revised algorithm byte-for-byte for the same small deterministic input, including
multiple passes, transparent borders and opaque texels, then inspect the unchanged Maximum assets
in the next authorized capture. The measured allocation delta should be recorded separately from
any later process working-set result.

Two other source-backed allocation candidates are recorded but are **not needed for that smallest
patch**:

- `GroundLayers.Load` holds all 48 readback `Image` objects while making the three texture arrays
  and never explicitly disposes the images/collections. `Read` already disposes each source texture,
  and `GroundWarmup` deliberately lists no layer files, so a second permanent warm-up-held copy is
  **not** established. A subsequent one-file `GroundLayers.cs` repair could create one map array at
  a time and dispose its temporary images/collection in `finally`, after upload. Actual native bytes
  depend on the imported image formats and must be measured, not inferred from JPEG/file size.
- `FloraLibrary.Build` creates procedural fir, pine and birch geometry that `Create` skips after
  finding those kinds in the authored catalogue: nine variants, two LOD geometries each, **18 unused
  geometry builders**. Skipping their construction could avoid work, but needs a shared catalogue
  decision before the worker starts and is broader than scratch reuse. No byte or timing estimate
  is claimed for these builders. Warm-up also retains its preview nodes until completion and its
  loaded resources for the game intentionally; this run has no per-resource footprint measurement.

### Handoff and validation boundary

Root review can approve the two-source-file transport change first, its dedicated regression and
matching docs, plus the independent one-function dilation change once P2 releases that path.
No timeout, quality preset, texture resolution, spawn data or loading completion rule needs changing.
After a fresh coordinated build and focused checks, a separate guarded Maximum startup run should
capture load timing and process-private/working-set plus system-memory samples. The prior no-ready
run remains rejected. Neither the transport proposal nor the calculated allocation reduction alone
closes N12, establishes GPU memory attribution or guarantees another full world run is safe.

## Approved startup repairs implemented; validation pending

The coordinator granted exclusive narrow ownership of `GameClient.cs`, the client-option assignment
in `GroundMode.cs`, new `ClientTransportTests.cs`, and only `Dilate` in `FloraTextures.cs`. P5 and P2
were frozen. It also permitted isolated small transport-doc edits. P8 applied those changes and
updated only the transport paragraphs in `ARCHITECTURE.md §11.1` and `GROUND.md §4.4`, explicitly
marking validation pending. **No build, test, game/native process, import, Git operation or agent
was launched.** Ground-layer disposal was not changed.

### Final source behavior

- `ClientOptions.BackgroundTransport` defaults to false. The normal graphical client opts in;
  bots and existing engine-free tests keep manual transport. In background mode the library starts
  with `Start(0)`; all three unsynchronized-event flags are explicitly false. Application handlers
  remain dispatched by the existing `PollEvents` in `GameClient.Update` on its caller's thread.
- One `UpdateTransport` helper covers frame updates, explicit disconnect and disposal. It calls
  `TriggerUpdate` for background mode and `ManualUpdate` only for manual mode. Disposal still stops
  the network before releasing simulation modules/physics. No timeout, gameplay/attack logic,
  data, preset, resource quality or loading-completion rule changed.
- `Dilate` allocates one scratch buffer for positive pass counts and copies the entire previous-pass
  input into it at each pass. The pixel loop, alpha rules, arithmetic and order are untouched.
  Nonpositive pass counts still return without touching the input or allocating a buffer.

Before-edit copies of the three shared source files are retained under
`.artifacts/p8-startup/source-before/`. A read-only inverse-replacement comparison reconstructed
each original file exactly after line-ending normalization, confirming that unrelated P5/P2 work
was preserved and only the approved plumbing/option/allocation edits were made. All checkpointed
files are NUL-free and have no trailing whitespace. These source checks are not compilation or
behavioral validation.

### Regression and actual-kernel comparison prepared, not run

`ClientTransportTests.BackgroundTransportSurvivesAFrameStallAndDispatchesOnTheCaller` uses the
existing stand-in loopback fixture, one background client and one default manual client. Both
withhold `Update` for **12 real seconds** while the server keeps its production 10-second timeout.
The test requires the manual control to disappear from the server and the background session to
remain, then verifies queued server chat and a client-chat round trip exactly once, callback thread
identity, resumed snapshots, explicit disconnect, rejoining with the same name, and disposal of a
connected client without another client update. The disconnected/disposed sessions must disappear
within a five-second bound, below the transport timeout. Disposal is called twice to exercise
idempotence. This is a behavior regression, not an assertion of the option's value.

The ignored standalone comparator in `.artifacts/p8-startup/dilation-check/` loads the **actual
built `HollowUniverse.dll` method** by reflection and invokes its managed `FloraTextures.Dilate`
kernel. Its oracle is the verbatim original method extracted from the saved pre-edit source, not
a reimplementation of the candidate. It prepares **120 bitwise comparisons** across thin/odd grids,
clipped edges, transparent/opaque/partial-alpha values, the 0.01 alpha boundary, signed zero,
deterministic mixed colours and −1/0/1/2/4/6 passes. Float bits, including alpha, must all agree.

After warming both methods, the comparator measures actual per-thread allocated bytes over eight
calls at 128×96 for 0/1/4/6 passes, with inputs/delegates prepared outside the counters. It requires
the predicted removal of repeated scratch allocations. Loading the game assembly should not call
Godot native code: the target kernel and its static tuple fields are managed. **This runner itself
has not been compiled or executed**, so that hosting assumption remains to be validated. It writes
the tested DLL hash and results to a fresh report path and refuses to overwrite existing evidence.

The earlier **848 MiB** calculation remains cumulative full-size allocation churn for the current
atlas/bake workload, **not a measured peak/system/GPU saving**. No native visual acceptance or world
memory result follows from preparing this comparator.

### Frozen checkpoint

| File | SHA-256 |
| --- | --- |
| `src/Hollow.Client/GameClient.cs` | `20264719071E5CF311A2BC3C4A678A6E61E053689259CBD1F80280EC5B9899D2` |
| `game/scripts/Ground/GroundMode.cs` | `FB770081C6EC3E3292E3F64BD1B1F7DAB61A43CE020C6D4CC8DBF419E5507096` |
| `tests/Hollow.Tests/ClientTransportTests.cs` | `F640C3E5FA62ACFEE922954D72FC51A85712471B9AF8D8E39FE59CA2A38B7B0A` |
| `game/scripts/Surface/Flora/FloraTextures.cs` | `8D827CB84E523FEE1FC2DC90D92D57D99BEDCE7F1986E00F88A34670CB07A6DF` |
| `docs/ARCHITECTURE.md` | `59E684E8D73B071C56C760BE010C656CD913A3163E0A83CA905713C8C3039572` |
| `docs/GROUND.md` | `D9F7AFCB38631EDDFCD7E705FD402EC16C07F276042E446C9F801C6C5DC210C4` |
| `.artifacts/p8-startup/dilation-check/DilationCheck.csproj` | `4B41A5BA11B27647B9157EAC9A66FD991CAC4917A0606CF66E316B0FC7663312` |
| `.artifacts/p8-startup/dilation-check/Program.cs` | `1C89A7B8CE6738FFD72C3D1DD594ECC8A74D60938D53EA22CB91FAA0FFE61A08` |
| `.artifacts/p8-startup/dilation-check/OriginalDilation.cs` | `88F8C8BA251298284014535BFFB7033ED5407E83B4B7AE15C60992A8496A390B` |

The retained original full `FloraTextures.cs` hash is
`52DE4C6D8E2E8D59346C1571918B4BCE824535D5EF6E177191F5754159EEEED2`.
The native gib checkpoint is unchanged; its prior pass does not validate this new transport change.

### Next checks, only in explicit coordinator slots

Root owns the next integrated build. After it succeeds, the focused engine-free filter is:

```powershell
dotnet run --no-build --project tests/Hollow.Tests -- ClientTransportTests LoadingTests
```

The dedicated new test contains one 12-second pause; allow a bounded 60-second job for join,
resumption and cleanup rather than treating that pause as a hung test. Existing `LoadingTests`
exercise the unchanged default manual path. Keep source/DLL manifests and the normal memory guard.

The separate small comparator command (it builds its own ignored runner, so needs a build/test slot):

```powershell
dotnet run --project .artifacts/p8-startup/dilation-check/DilationCheck.csproj -- D:\GameDev\Projects\HollowUniverseABTestB\game\.godot\mono\temp\bin\Debug\HollowUniverse.dll D:\GameDev\Projects\HollowUniverseABTestB\.artifacts\p8-startup\dilation-result-01.json
```

No import, full asset load or renderer is requested by that comparator. If loading the assembly
requires native initialization unexpectedly, retain its failure and return it for review rather
than substituting a copied candidate kernel.

Only after these checks and adequate measured headroom should root grant a separate bounded
Maximum startup/route run. Preserve P2's baseline settings/scene and use its guarded launcher;
capture load timing (a separate `--load-trace` startup job if necessary), process-private/working-set
and system-memory samples, readiness and callback/disconnect behavior. A real world capture is
still required. **Source is refrozen; N12 remains open and no startup or memory improvement is
claimed as validated.** Root retains shared DEVELOPMENT/backlog updates.

## Startup and dilation bounded validation passed

Root completed the following serialized jobs. P8 inspected their existing logs/receipts and the
comparator JSON, then updated only this report and the small pending-validation notes in
`ARCHITECTURE.md` and `GROUND.md`. No further build, test, native/game run, source change, import,
Git operation or agent was launched by P8. GroundLayers remains unchanged.

| Root job | Result | Elapsed | Minimum available RAM | Maximum commit |
| --- | --- | ---: | ---: | ---: |
| `20261016-203812-build` | Exit 0, zero warnings/errors | 18.28 s compiler / 18.69 s wrapper | 5.046 GiB | 58% |
| `20261016-204130-startup` | **15/15 passed**, exit 0 | 17.6 s tests / 18.50 s wrapper | 5.300 GiB | 57% |
| `20261016-204641-dilation-build` | Comparator built; exit 0, zero warnings/errors | 2.33 s compiler / 2.86 s wrapper | 5.488 GiB | 55% |
| `20261016-204650-dilation` | **120 bitwise comparisons and four allocation cases passed**, exit 0 | 1.43 s wrapper | 5.629 GiB | 55% |

All four receipts have null stop reasons and completed successful logs. Evidence:
[shared build log](../../.artifacts/recovery/integrated-checks/20261016-203812-build.stdout.log),
[build receipt](../../.artifacts/recovery/integrated-checks/20261016-203812-build.receipt.json),
[startup test log](../../.artifacts/recovery/integrated-checks/20261016-204130-startup.stdout.log),
[startup receipt](../../.artifacts/recovery/integrated-checks/20261016-204130-startup.receipt.json),
[comparator build log](../../.artifacts/recovery/integrated-checks/20261016-204641-dilation-build.stdout.log),
[comparator build receipt](../../.artifacts/recovery/integrated-checks/20261016-204641-dilation-build.receipt.json),
[comparator run receipt](../../.artifacts/recovery/integrated-checks/20261016-204650-dilation.receipt.json),
and [actual-kernel results](../../.artifacts/p8-startup/dilation-result-01.json).

The 15-check batch contains five architecture checks, the new client-transport regression and
nine loading checks. During the **12.00-second** caller pause, the unchanged manual control timed
out while the background client kept its server session. The two queued/resumed chat callbacks
ran on caller thread 2, with exact-once checks passing. Snapshots resumed, explicit disconnect
released the name for rejoin, and connected/idempotent disposal removed the replacement session
without another client update. The regression took 15.803 seconds. These results validate the
bounded transport behavior and unchanged engine-free/default-manual loading tests; they are not
a full-suite pass or a successful Godot world startup.

The comparator invoked `HollowUniverse.Surface.Flora.FloraTextures.Dilate` from actual game DLL
SHA-256 **`FEE5F46497ED41A3F4C8279FAD7D00B42DE892FB043880C9DC246E53B2A4B4AD`**. Its prepared
managed-only hosting path succeeded. All 120 cases were bit-identical to the retained old kernel,
including the edge/alpha/multiple-pass cases. Actual caller-thread allocation totals for eight
128×96 invocations were:

| Passes per invocation | Original allocated bytes | Revised allocated bytes | Saved bytes |
| ---: | ---: | ---: | ---: |
| 0 | 0 | 0 | 0 |
| 1 | 1,573,056 | 1,573,056 | 0 |
| 4 | 6,292,224 | 1,573,056 | 4,719,168 |
| 6 | 9,438,336 | 1,573,056 | 7,865,280 |

The reductions include array headers and meet the predicted payload savings. These are measured
**cumulative managed allocations** in the bounded comparator. The full-size 848 MiB payload
calculation is still a workload extrapolation, not a measured process/system/peak/GPU reduction.
Texture dimensions, dilation passes and pixel output are preserved; native Maximum appearance and
the whole-world memory footprint have not been revalidated.

Root deferred another Maximum world startup: the failed r3 run reduced system available memory
from 7.651 GiB to about 1.80 GiB at the secondary guard observation (roughly 5.8 GiB), while root
reported current idle availability around 5.6 GiB. This is system-memory evidence, not per-asset
attribution. Root requires roughly **8 GiB available for that larger rerun**, or another separately
justified allocation repair demonstrating lower need. It does not authorize a retry or a new repair
here, and it does not change the measured small-job policy. The earlier r3 failure remains rejected.

**The transport/dilation source checkpoint is validated by these bounded checks and stays frozen.**
N12 and native Maximum startup/FPS acceptance remain open. N11 still needs its previously listed
actual settings APPLY/restart and gameplay acceptance. No new source work, including GroundLayers
disposal, is approved by this evidence update. Root owns further scheduling and shared backlog/
DEVELOPMENT updates; no repeat of the completed focused checks is needed without a new change or
unresolved failure.

## N11 actual settings APPLY/restart — isolated harness prepared, not run

Root next granted diagnostic source preparation only under `.artifacts/p8-gibs`, against the
21:36 shared build that includes P2's GroundLayers lifetime repair. Production source remains
frozen. P8 prepared [the scratch native harness](../../.artifacts/p8-gibs/settings-acceptance/README.md)
without editing Main.cs or any production UI/settings implementation. No build, test, import,
Godot process, Git operation, or new agent was run for this preparation. PowerShell scripts were
parsed (zero syntax errors), the project XML was read, and filesystem aliases/hashes were prepared;
these are preparation checks, not compilation or runtime acceptance.

The harness references actual built `HollowUniverse.dll` and its managed dependencies; it has no
ProjectReference and explicitly compiles only its diagnostic entry. Actual `GroundUi`,
`MockGroundUi`, `UiScaler`, `SettingRows` and `GraphicsSettings` supply the UI, model and behavior.
Source-path, assets and imported junctions reuse existing resources without an import. **Whether
Godot registers all referenced assembly scripts correctly is not yet validated.** The first native
job must retain and report any error, not replace this with a copied implementation.

The fixed, previously nonexistent native userdata namespace is
`C:\Users\uruskan\AppData\Roaming\HollowUniverseABTestB-P8-GibSettings-20261016-v1`.
The wrapper refuses first-stage reuse. Before any production settings load, the native script
asserts `OS.GetUserDataDir()` and every `user://` preference path against that exact namespace,
separate from real game userdata. Both wrapper and native script hash all real top-level `.cfg`
files, explicitly including graphics, ground-settings, settings and ground-ui, with absent-file
markers. No preference file is seeded: the real APPLY button must create the isolated files.

The proposed sequence uses actual `Input.ParseInputEvent` keyboard/mouse events through the
production panel, not calls to its Save/Preview/Revert or row mutators:

1. Fresh default MAX=100; mouse selects GIB AMOUNT; left arrows reach and clamp MIN=0. Escape
   cancels, restoring 100 with no saved files.
2. Reopen, set 30%, click the actual APPLY hit region, require the existing UI click callback,
   Current/Saved=30 and Dirty=false, and read `graphics.cfg` back through ConfigFile.
3. Preview and clamp MAX=100, cancel to saved 30, and require saved file bytes unchanged. Leave
   another unapplied 100 preview at process exit.
4. A separately launched native process must load Current/Saved=30 with Dirty=false, capture the
   actual panel, and cancel a new MIN preview back to 30 without altering saved bytes. The wrapper
   requires a successful matching Apply receipt, identical inputs/binaries, and a distinct PID.

Five first-process and one restart 1600×900 captures are requested. The production UI uses scale 1
and a solid backdrop; no world, server, flora, gib fixture or physics simulation starts. This is
native injected-input integration coverage, **not physical human mouse input, destruction gameplay,
authoritative loot, sound playback, Maximum world memory or FPS acceptance**.

[Run.ps1](../../.artifacts/p8-gibs/settings-acceptance/Run.ps1) keeps Build, Apply and Restart as
separate jobs requiring distinct coordinator grants and fresh receipt labels. Each uses the
ordinary small-job start guard (at least 5.5 GiB available / at most 80% commit), rejects other
heavy processes, has a 60-second bound, samples once per second, and stops only its own tree below
2 GiB available or at 85% commit. Build is single-worker with build servers/shared compilation
disabled. No automatic rerun, import or quality change is permitted. Runtime errors, missing or
failed result JSON, changed inputs/binaries, or changed real preferences reject a run. Each phase
must be reviewed before granting the next.

Prepared SHA-256 checkpoint:

| Input | SHA-256 |
| --- | --- |
| Actual game DLL | `D0CAD90603DCFE808826025EE38AF93D3BF124C76B0C9A8F6DFAD3F3393D139F` |
| SettingsAcceptance.cs | `22FAD84F7D3794F2960B0E8AA4FB6BAC8A4660E98FAD8CDAAC19BFBDCD77E185` |
| SettingsAcceptance.csproj | `5D23D5B4C0EC3CFB2638C0CC622D03A9D8DAF0F0D2DCE163033B04CDBB4113CD` |
| project.godot | `40D14363346E947E13AD88699ED6FE14E37EC302D1E747EC94406749BFC9B8A3` |
| Run.ps1 | `115368FA6D356AC56C03B7F41445F01018DAE173B9144C98C49D861C06872495` |
| checkpoint.json | `63EF9B96029A72758D7B0F794CB89579FA6290E7CFF95150C536C8DF857FDE8B` |

The full [r1 checkpoint manifest](../../.artifacts/p8-gibs/settings-acceptance-checkpoints/checkpoint-r1.json) includes
the source files, referenced DLLs and real-preference baseline. The three previously recorded
real preference hashes are unchanged. The newly included real `ground-ui.cfg` hash is
`948EBF41CB1C11C8919FEDBD8A6D941DC07F1E15116DD2E4F6BDDA52375D46A2`.

**N11 remains open.** The diagnostic source is ready for coordinator review; no build or native
execution slot has yet been granted for this harness. Shared DEVELOPMENT/backlog scheduling stays
with root, and the larger Maximum-world gate remains separate.

## N11 r1 harness result and r2 identity correction

Root reviewed and ran the first two jobs serially. P8 read the retained receipts and logs:

| Job | Result | Wrapper elapsed | Minimum available RAM | Maximum commit |
| --- | --- | ---: | ---: | ---: |
| `build-r1` | Passed; exit 0, zero compiler warnings/errors | 4.247 s | 6.966 GiB | 49% |
| `apply-r1` | Harness failed before settings load; exit 1 | 2.856 s | 6.938 GiB | 49% |

Both receipts report null watchdog stop reasons and zero changed inputs, real preferences or
binaries. [Build log](../../.artifacts/p8-gibs/settings-acceptance-runs/build-r1/stdout.log),
[build receipt](../../.artifacts/p8-gibs/settings-acceptance-runs/build-r1/receipt.json),
[Apply result](../../.artifacts/p8-gibs/settings-acceptance-runs/apply-r1/result.json),
[Apply receipt](../../.artifacts/p8-gibs/settings-acceptance-runs/apply-r1/receipt.json), and
[Apply error log](../../.artifacts/p8-gibs/settings-acceptance-runs/apply-r1/stderr.log) remain intact.

The native Apply attempt passed eight initial phase/isolation/path checks, then the diagnostic
called `File.OpenRead` on `typeof(GroundUi).Assembly.Location`, which is empty in Godot's
stream-loaded assembly host. This is a **harness identity-check defect, not a game or settings
failure**. The loader, panel, input events and captures were never reached; all four isolated
preference files remained absent. No APPLY or restart acceptance is claimed.

Root granted a scratch-only correction, now prepared without another build/test/native run:

- Resolve the copied game DLL explicitly under the harness's `.godot/mono/temp/bin/Debug` and the
  frozen workspace DLL under `game/.godot/mono/temp/bin/Debug`. Read each file's SHA-256 and PE
  metadata module MVID from the same open stream, without loading another assembly.
- Require both hashes to equal frozen game hash
  `D0CAD90603DCFE808826025EE38AF93D3BF124C76B0C9A8F6DFAD3F3393D139F`; require both on-disk MVIDs
  to equal the actual loaded `GroundUi` type's nonempty module MVID; require production assembly
  names and the same actual `GraphicsSettings` module/assembly. Record the identities in result
  JSON. No identity check was bypassed; `Assembly.Location` is evidence only.
- Use fresh `HollowUniverseABTestB-P8-GibSettings-20261016-v2` consistently in the native script,
  project and launcher. V2 does not yet exist. V1, its cache and every r1 receipt/log remain intact.
- Cast the launcher's ordered receipt dictionary to `PSCustomObject` before console
  `Select-Object`; this fixes null console summary fields. The saved receipt JSON was already sound.

The original checkpoint is retained with its original SHA-256 at
[checkpoint-r1.json](../../.artifacts/p8-gibs/settings-acceptance-checkpoints/checkpoint-r1.json).
PowerShell syntax parsing again reports zero errors; real preference hashes remain identical to
the r1 preparation baseline. These are source-preparation checks, not runtime validation of r2.

| Corrected input | SHA-256 |
| --- | --- |
| SettingsAcceptance.cs | `C02FA019BB97BE988832933AA26FF2A70157B638C4D62527A841DFD038DC354B` |
| Run.ps1 | `2616CC4BC85B8A0E62566779D14C081A3C4B0B0CA0C1D92BA33C1218609FA977` |
| project.godot | `1CDC00C2F0D9B4822B0897AB9523DDB92B0A06DE2C9B2BD919F2414DA209CAF4` |
| checkpoint.json | `6331DC6670A26556FBA660F08ACD455AD1C7BAAF6097A39E8C08199D36B56CC1` |

The [current checkpoint](../../.artifacts/p8-gibs/settings-acceptance/checkpoint.json) and
[updated plan](../../.artifacts/p8-gibs/settings-acceptance/README.md) are ready for root review.
The proposed next job is **build-r2 only**, after a separate root grant; Apply-r2 and Restart-r2
still need subsequent grants and fresh preflight. All production source, isolation guards,
memory/time limits and the actual input-path assertions are unchanged. **N11 remains open.**

## N11 actual settings APPLY/restart passed — r2 retained evidence

Root reviewed the r2 correction and executed these three jobs serially. P8 then read all receipts,
result JSON and log tails, inspected all six existing PNGs, and matched their hashes to the results.
This update edits only the report; no source/harness edit, rerun, build, test, import, native process,
Git operation or agent was launched by P8. Root released all execution slots.

| Root job | Result | Wrapper elapsed | Minimum available RAM | Maximum commit |
| --- | --- | ---: | ---: | ---: |
| `build-r2` | Exit 0; zero warnings/errors | 4.256 s | 6.974 GiB | 50% |
| `apply-r2` | **49/49 checks passed**, exit 0 | 5.652 s | 6.929 GiB | 51% |
| `restart-r2` | **36/36 checks passed**, exit 0 | 2.839 s | 7.112 GiB | 50% |

The compiler itself reported 2.68 s. Every receipt has a null stop reason and zero changed input,
binary or real-preference counts. All three stderr files are empty; native receipts contain no
errors. Evidence: [build log](../../.artifacts/p8-gibs/settings-acceptance-runs/build-r2/stdout.log),
[build receipt](../../.artifacts/p8-gibs/settings-acceptance-runs/build-r2/receipt.json),
[APPLY result](../../.artifacts/p8-gibs/settings-acceptance-runs/apply-r2/result.json),
[APPLY receipt](../../.artifacts/p8-gibs/settings-acceptance-runs/apply-r2/receipt.json),
[restart result](../../.artifacts/p8-gibs/settings-acceptance-runs/restart-r2/result.json), and
[restart receipt](../../.artifacts/p8-gibs/settings-acceptance-runs/restart-r2/receipt.json).

The loaded production module MVID was `82955e4e-830d-408d-b3f8-ed38f9035dc6` in both processes,
matching both explicitly located DLLs. Both DLL hashes matched the frozen game SHA-256
`D0CAD90603DCFE808826025EE38AF93D3BF124C76B0C9A8F6DFAD3F3393D139F`. `GroundUi` and
`GraphicsSettings` shared that verified production assembly/module. Its empty Location was
recorded successfully; the corrected identity check did not bypass verification. Referenced-script
creation and actual panel rendering now worked in the bounded scratch host.

The first process (PID 4656) loaded default MAX=100, selected the real GIB AMOUNT row, and used
keyboard input past the lower limit to verify MIN=0. Escape restored 100 without creating settings
files. After reopening and setting 30%, actual mouse input on the APPLY rectangle reached the
production click handler, changed Saved to 30, cleared Dirty, and created readable settings with
`graphics/gib_amount=30`. A preview past the upper limit clamped at MAX=100; Escape returned to 30
without changing saved bytes. That process then exited with a further unapplied MAX preview.

The separate restart process (PID 28964) loaded Current=Saved=30 with Dirty=false, proving that
the unapplied 100 preview did not replace the applied value. It also previewed MIN and canceled
back to 30 without changing saved files. P8 independently compared the retained Apply and Restart
preference manifests: they are identical, including the still-absent isolated `ground-ui.cfg`.
The four real preferences remained byte-identical to their pre-run baselines. Native checks and
wrapper receipts bind these operations to the unique v2 userdata directory. V1 and r1 evidence
remain preserved.

All six actual 1600×900 panel screenshots were inspected and match their recorded SHA-256 hashes:

| Capture | Observed state |
| --- | --- |
| [Minimum preview](../../.artifacts/p8-gibs/settings-acceptance-runs/apply-r2/01-minimum-preview.png) | GIB AMOUNT MIN, left endpoint, unapplied warning and enabled APPLY |
| [30% before APPLY](../../.artifacts/p8-gibs/settings-acceptance-runs/apply-r2/02-thirty-unapplied.png) | GIB AMOUNT 30%, unapplied warning and enabled APPLY |
| [30% applied](../../.artifacts/p8-gibs/settings-acceptance-runs/apply-r2/03-thirty-applied.png) | GIB AMOUNT 30%, Saved footer, disabled APPLY/REVERT |
| [Maximum preview](../../.artifacts/p8-gibs/settings-acceptance-runs/apply-r2/04-maximum-preview.png) | GIB AMOUNT MAX, right endpoint, unapplied warning |
| [30% after cancel](../../.artifacts/p8-gibs/settings-acceptance-runs/apply-r2/05-thirty-after-cancel.png) | GIB AMOUNT 30%, Saved footer |
| [30% after restart](../../.artifacts/p8-gibs/settings-acceptance-runs/restart-r2/06-thirty-after-restart.png) | GIB AMOUNT 30%, Saved footer in the new process |

**Separate observed UI defect:** at 1600×900 the long orange NOT APPLIED footer sentence extends
under the APPLY button and its ending is obscured. This is visible in the MIN, unapplied 30% and
MAX captures. It does not invalidate the verified gib setting input/persistence results. Root is
routing the existing footer layout issue separately; no UI fix or additional run is authorized here.

**N11's actual settings APPLY/restart requirement is now validated through production code and
Godot-injected input.** The earlier 44/44 gib fixture remains separate evidence for bounded cosmetic
piece behavior and dust at minimum. The following limits remain: no physical human keyboard/mouse
session, no in-world destruction at both settings, no authoritative damage/loot comparison, and no
audible break-sound playback acceptance. The UI-only harness does not establish world rendering,
Maximum startup, memory reduction or FPS improvement. **N11 gameplay acceptance and N12 remain
open.** Root owns shared backlog/DEVELOPMENT updates and any further scheduling; no repeat of these
passing checks is needed without a new change or unresolved concern.

## 17 October — P7 twig fracture budget review (read-only evidence)

Root requested a bounded review of P7's actual, unaccepted twig fracture calibration. P8 read
the current recovery instructions, frozen exporter/catalogue/runtime source, the calibration
receipt and GLB JSON metadata from the installed and candidate files. Only this report changed;
no source/asset edit, generation, build, test, import, render, engine process, cleanup or agent
was performed. Nothing was installed. P7 separately owns the reported 5.22 mm / 3.14 mm bounds
investigation; this review does not accept those deviations or the candidate.

**The catalogue's `budget: 1000` is a soft pre-fracture source target, not a hard exported
triangle cap.** `art/gibs/build.py:167` chooses the finest LOD within the target, otherwise the
coarsest. `limit()` at line 274 skips reduction within 110% of the target and otherwise uses
`max(0.5, budget / tris)`: at most half the source triangles are removed, explicitly to retain
thin parts. Fracture then splits faces and adds caps; there is no post-fracture triangle cap.
GROUND §9.4's numbered pipeline describes these same semantics. Its shorter historical X6
phrase "500 triangles a piece before the cuts; twig's sticks 1,000" is imprecise shorthand,
not evidence that the implementation enforces a hard cap. Root should clarify that wording
in its documentation scope rather than treating a lower-quality decimation as a bug fix.

Runtime `GibsInfo` contains only File, Material and Grade; the loader does not consume the
exporter's triangle target. `GibWorld` renders the supplied meshes in per-mesh MultiMesh batches,
with the separate 640 live-piece catalogue ceiling and the player's 0–100% amount setting.
Nine fragments per roof remain nine; the 1,000 source target is not a runtime triangle limiter.

The [calibration receipt](../../.artifacts/p7-roofs/twig-gibs-r1/roof-gibs-twig.glb.receipt.json)
records LOD2, unchanged nine requested/actual fragments, and seeds 1867159080 / 3860711873:

| Part | Selected source triangles | After source limit | Receipt fragment triangles | Final GLB triangles |
| --- | ---: | ---: | ---: | ---: |
| roof | 2,905 | 1,452 | 3,682 | 3,682 |
| roof-triangle | 1,994 | 1,000 | 2,588 | **2,582** |

The square's 1,452 follows the retained 50% floor. The final triangle count distinction is real:
`fragmentTriangles` is calculated before `chunk_object()` / `me.validate()` and final glTF export.
P8 counted final GLB TRIANGLES primitive index accessors divided by three, for all nine direct
children of each named group. Validation or exporter triangulation could account for the six
triangle difference; the exact substage was not measured. The receipt value must be described as
pre-object/export fracture output, not asserted as the final exported count. No receipt was edited.

Comparison with installed `game/assets/gibs/building-twig.glb`, using the same GLB metadata method:

| Part | Installed triangles | Candidate triangles | Difference | Installed → candidate POSITION entries |
| --- | ---: | ---: | ---: | ---: |
| roof | 3,862 | 3,682 | −180 (−4.66%) | 6,656 → 6,728 |
| roof-triangle | 2,843 | 2,582 | −261 (−9.18%) | 4,917 → 4,566 |
| Both groups | 6,705 | 6,264 | −441 (−6.58%) | 11,573 → 11,294 |

Both files contain nine fragments per group. Counts are sums over mesh primitives, not a claim
about unique welded vertices. The square's POSITION count rises by 72 despite fewer triangles,
so even this static comparison is not uniformly cheaper. Full installed-pack bytes cannot be
compared with a roof-only scratch file as a size saving. No frame-time, peak-memory, quality or
gameplay improvement has been measured.

The installed GLB SHA-256 is
`5F04DB748BBE88C90DE4A487C93538AE60FC018031D71138222A05F1070D5C14`, matching the receipt's
protected baseline; the candidate is
`E356F506B3F873F85E07A2B6207565898AED5E41F84162433BCB005D2918E530`, matching its output hash.
Reviewed exporter hash: `2BF044E8CB60BE75B4DD077BBAEAC51D1F3BC38245D764513F0A6EEF7637D579`;
receipt hash: `E46CC3EB76897E202C09E6210B0954750E706AB65040BBD160CCBA2346510441`.

**Finding:** exceeding 1,000 final triangles exposes existing exporter semantics; it is not a
new total-triangle regression against the installed roofs. **Smallest follow-up:** retain the
current target, decimation floor, seeds and fragment counts; report source, post-limit fracture
and final GLB counts separately; continue P7's bounds/geometry diagnosis before any merge or
acceptance. If root later authorizes a telemetry correction, derive final exported counts from
the completed GLB without changing mesh quality. A hard exported cap would be a new quality/
performance policy requiring separate evidence and approval, not a correction justified here.

## 17 October — native roof viewer shutdown warning triage

Root assigned one light read-only review after P7's sole native capture job finished. P8 read
existing logs/receipts and relevant disposal/source paths; only this report changed. No build,
test, rerun, import, render, engine/process-cleanup operation, production edit or subagent was
performed. No new heavy slot is granted. Shared known-problem/backlog updates remain with root.

### Observed evidence and historical comparison

P7's retained [stdout](../../.artifacts/p7-roofs/native-r2/capture/stdout.log) reports
`ready=True` at **2.01 s**, followed by saving the requested **1600×900** metal-square LOD0
image. Its [receipt](../../.artifacts/p7-roofs/native-r2/capture/receipt.json) records native
exit 0, no guard stop and 7.932 s wrapper duration. The
[stderr](../../.artifacts/p7-roofs/native-r2/capture/stderr.log) then reports **30 ObjectDB
instances leaked at exit**, with no object classes/IDs/resource paths, and requests `--verbose`
for details. No Texture-RID or Jolt error appears in this capture's two-line stderr. The image's
successful save and process exit do not establish clean shutdown.

This warning class predates the roof capture:

- [Disconnect lifecycle stderr](../../.artifacts/disconnect-check/lifecycle-errors.log) also
  reports **30** ObjectDB instances. Its accompanying lifecycle log reaches PASS after host,
  remote join/disconnect and host-again checks. That is a different scenario/checkpoint, not a
  matched baseline or proof that the same 30 objects were retained.
- [Second direct-start stderr](../../.artifacts/disconnect-check/second-direct-start-errors.log)
  reports **35**; the historical mixed-build error log reports **33**.
- [Beach](../../.artifacts/earth-land-tuning/beach.stderr.log),
  [globe](../../.artifacts/earth-land-tuning/globe.stderr.log) and
  [cold-start](../../.artifacts/disconnect-check/cold-start-errors.log) stderr each report
  **two ObjectDB instances plus seven Texture RIDs**. These are different signatures and must
  not be conflated with the current ObjectDB-only warning.

There is no evidence here attributing this to roof geometry, an FPS regression, increasing
in-session memory, or the earlier machine-memory crisis. Object counts do not quantify bytes.

### Ownership already handled, and concrete source candidates

`BuildSandbox._ExitTree` (`game/scripts/Build/BuildSandbox.cs:751`) already disposes its
`BuildingWorld` and `Zone`. `BuildingRenderer._ExitTree` detaches its world Changed handler;
`GroundUi._ExitTree` detaches model events and `Ui.ScaleChanged`; `GibWorld._ExitTree` removes
its graphics-settings subscription. Child nodes are attached to the scene tree. The GLB reader
in `BuildingAssets.Read` explicitly frees each temporary instantiated root, and its shared
`Loaded` cache is a **WeakReference**, not a static strong owner. These are not demonstrated
missing-root-free or missing-zone-dispose bugs.

Two specific lifetime paths warrant attribution before editing:

1. **Static director/callback retention:** `GibDirector._world` is a static strong reference
   (`GibDirector.cs:24`); `BuildSandbox` assigns its instance `Floor` method to that world's
   `Floor` delegate (`BuildSandbox.cs:147`). Neither the sandbox exit nor the gib-world exit
   clears that callback or releases the director's reference. The resulting managed chain
   can retain the sandbox and its fields, including `BuildingAssets`' meshes/materials, after
   scene removal. The director checks native validity when reused, but that is not explicit
   release on exit. This is a concrete retention candidate, **not proof that it produces the
   30 native ObjectDB survivors** or that Godot's final shutdown leaves the whole chain alive.
2. **Captured Image lifetime:** shared `Main.Capture` (`game/scripts/App/Main.cs:392–395`)
   obtains an `Image`, saves it and immediately requests quit without a `using`/explicit
   disposal. That leaves release to the managed/native lifetime machinery. An identified
   leaked Image would justify a very small shared-capture ownership correction; the source
   alone cannot assign the current count, and this path is not roof-specific.

Do not indiscriminately dispose shared asset materials, clear global caches, force collection,
or add arbitrary exit delays to make the warning disappear. Those actions would change
ownership or timing before identifying the actual survivors.

### Smallest diagnostic next step (not granted or run)

Request **one** separately granted repeat of the same pinned BuildSandbox capture with engine
`--verbose` added **before** the user-argument `--`, using fresh evidence/output paths and the
same source/DLL/import/configuration checks, quality/camera, 2+90 s readiness and 120 s external
bound. Retain the existing general 5.5 GiB / 80% start and owned-only 2 GiB / 85% stop guards;
give competing user jobs priority. No build/import/source change is needed for this diagnostic.

Capture leaked object classes, instance IDs and any resource paths from both logs, then map
them to the ownership paths above. If verbose output still lacks enough detail, return that
specific gap for a narrower instrumentation grant. Do not expand automatically to an empty
scene comparison, full world, second rerun or code fix. This current review grants none of
those jobs. P7's existing image evidence remains intact while shutdown acceptance stays open.

Reviewed source hashes: `BuildSandbox.cs`
`C273414A2C09D4F852911F5E22ADD308AB6644CE2E48E5689AFF893C0C9B052D`;
`BuildingAssets.cs` `C83D9740F9996CD41FAF4E8DA5DE469580B8B73FE8DE6465F178AEC246657F5A`;
`GibDirector.cs` `09A58AC33ED00FF3FCB612DEF2B2C05EEC25DCC5057A89EEDBF6FFE7CE36600F`;
`GibWorld.cs` `2972E2E5499883A5B18B38E0DF1389D54C05BD866B342BCA637B5644CB9F4B2E`;
`Main.cs` `5B76A7A6F9CD25B0BF6FDE20A6415AEA0FFF936209571A1E85E85778FB3BC879`.
Current stderr SHA-256:
`5438A054ADF903B56A809396458310840637359FF88EEAD77323F34F846C3D92`.

## 17 October — completed Maximum day route r4: baseline and separate exit signature

Root supplied P2's completed native route for light evidence integration. P8 read the existing
route JSON, logs and receipt and inspected the route capture's Image ownership; only this report
changed. No production/harness edit, build/test/import/game/render, process cleanup, new chat or
subagent was performed. Root retains shared RECOVERY/BACKLOG/DEVELOPMENT/KNOWN updates and the
158-input / 25-staged-path freeze. No execution slot was granted to P8.

### Concrete completion and scope

[Route JSON](../images/repairs/p2-environment/baseline-max-day-r4/environment-route.json) records
`technicalChecksPassed=true`, `failure=null`, **18/18 ready 1600×900 captures**, and
`visualQualityAccepted=false`. The command used Maximum, render scale 1, hour 10, clouds 0.25,
normal timeouts, an isolated r4 database, port 27182, and `--in-process`. Effective VSync was
disabled and frame cap zero. All six stationary profiles have `diagnosticAblation=false` and no
disabled layer. The runtime module MVID is `83a3a1a0-1c91-410f-a6d6-c93c12de1772`; code-version
text alone is not a substitute for the coordinator's pinned input/DLL evidence.

[Receipt](../../.artifacts/p2-environment/baseline-max-day-r4/run/receipt.json): native exit **0**,
no guard stop or secondary error, InputsUnchanged=true, reported duration **350.475 s**. Route
JSON elapsed time is **339.952 s**. Preflight available memory was 8.976 GiB / 38% commit; across
267 samples, minimum available memory was **3.336 GiB**, maximum commit **75%**. Those are system
snapshots for this run, not a per-process allocation measurement or a matched memory improvement.

[Stdout](../../.artifacts/p2-environment/baseline-max-day-r4/run/stdout.log) contains no Timeout
or PeerNotFound occurrence, reaches technical route completion, then records the player's normal
connection close and server save/stop. Five `clock moved` notices remain: 16, 13, 11, 11 and 13
ticks. No timing-correction cause or separate latency regression was established here. This is a
successful normal-timeout native in-process startup/route after the built repairs, not evidence
that every repair independently caused the success, that child-process hosting is covered, or
that repeated startup/late join/reconnect/full-suite reliability is complete. R3 stays failed.

### Stationary baseline — no before/after improvement claim

These are the six `*-ground` profiles' nested `timing` measurements, approximately eight seconds
each, with 265–321 frames. They are distinct from the six movement-leg profiles in the same JSON.

| Stationary stage | Frames | Mean ms | p95 ms | p99 ms | GPU mean ms | Render CPU mean ms | Mean draws | Mean primitives |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Beach | 307 | 26.06 | 36.91 | 66.02 | 25.53 | 2.09 | 2,204 | 11,148,673 |
| Woodland | 265 | 30.20 | 31.30 | 32.47 | 29.63 | 2.36 | 2,561 | 15,217,836 |
| Clearing | 279 | 28.77 | 30.14 | 31.03 | 28.22 | 2.22 | 2,423 | 14,469,914 |
| Water | 321 | 24.93 | 26.74 | 27.05 | 24.36 | 2.06 | 2,196 | 8,295,918 |
| Cliff | 318 | 25.24 | 27.22 | 27.78 | 24.67 | 2.10 | 2,235 | 8,265,494 |
| Monument | 320 | 25.03 | 26.72 | 27.43 | 24.47 | 2.10 | 2,326 | 11,745,902 |

The closeness of GPU time to frame time is consistent with a GPU-limited stationary workload,
but does not attribute cost to terrain, vegetation, shadows, gibs or any other pass. Render CPU
time is not total CPU/simulation time, and CPU/GPU work overlaps. The beach sample has a larger
tail than the other stationary samples; its cause is unmeasured. Renderer-reported peak video
memory spans 6,014.96–6,087.18 MiB across the six profiles; it is not the size of the leaked RIDs
or evidence of a leak slope. No comparison with the older unmatched Low/Custom samples is valid.

The movement-leg profile named `cliff` has **zero frames and zero measured seconds**. Its zero
timings are missing sample evidence, not excellent performance; the separate stationary
`04-cliff-ground` profile above is valid. Image readiness is recorded, but P8 did not independently
inspect these 18 images in this review and does not promote them to visual acceptance.

### Shutdown remains open under a distinct signature

R4 [stderr](../../.artifacts/p2-environment/baseline-max-day-r4/run/stderr.log) reports **seven
Texture RIDs plus two ObjectDB instances**. This matches the counts/types retained in historical
beach/globe/cold-start logs, not proof of identical objects or cause. It differs from P7 native-r2's
**30 ObjectDB-only** warning. Neither warning is made clean by exit 0 or route completion, and no
causal link between them or to steady-state FPS/memory pressure is established.

The route uses `EnvironmentRoute.Capture` (`game/scripts/Ground/EnvironmentRoute.cs:322`), which
already wraps its captured Image in `using`. The earlier `Main.Capture` undisposed-Image suspect
belongs to the other screenshot path and must not be transferred to r4 without evidence.
R4's ordinary stderr provides neither leaked instance classes/IDs nor Texture-RID creation sites;
it cannot identify the retained owner or justify disposal changes to shared resources.

**Next diagnostic:** retain the separately queued, unchanged BuildSandbox `--verbose` capture to
identify the 30-instance signature; it still has no P8 heavy slot. R4's two-instance/seven-Texture
signature needs its own source-matched verbose evidence in a later explicitly granted Maximum
route/exit job with the existing 8 GiB preflight and ordinary guards, not an automatic repeat or
reuse of the roof viewer's survivors as attribution. If verbose output omits Texture-RID owners,
return that precise gap for a bounded allocation/lifetime-instrumentation proposal. For performance,
retain r4 as the baseline; any subsequent GPU-pass attribution must preserve pose/quality and keep
diagnostic ablations separate from accepted visual results. No optimization patch or job is granted
by this report. **General exit cleanup, N11 gameplay acceptance and N12 improvement remain open.**

Evidence SHA-256: route JSON
`E3EC122D86A5BEA8515B44CB506F0FBB9D5E18447AF011E90719CB1014331A64`;
stdout `818A1C78EB5F0900DDEFB656FEB059A53D4D2C2E29F39BE8228DC73D32F80869`;
stderr `4A1D34103CC845825852893DEB32F6B7CF04171A5A58642F886C0102DAAC3CD7`.
