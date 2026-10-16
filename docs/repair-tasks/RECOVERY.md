# Recovery after the computer restart

**23:01 settings-footer visual verification complete:** after RAM recovered above the
documented 5.5 GiB UI threshold, root ran the three prepared native cases sequentially,
without rebuilding or importing. `automatic900-r1`, `automatic720-r1` and `scaled1080-r1`
all exited zero: **38/38, 39/39 and 39/39** assertions respectively. Root inspected all nine
dirty/saved/after-cancel PNGs. The full APPLY/Escape instruction, saved text, GPU line and
all four button labels remain visible without footer overlap at 1600x900 scale 1,
1280x720 scale 0.8125 and 1920x1080 scale 1.25. This closes the reproduced footer defect.

Evidence: `.artifacts/admin-settings-footer/runs/` under those three labels, plus
`.artifacts/recovery/footer-visual-acceptance-20261016-2300.json`. Run durations were
**4.365 / 4.254 / 4.203 seconds**, minimum free RAM **5.548 / 5.698 / 5.810 GiB**, maximum
commit **58 / 56 / 56%**. Each separate launch passed its fresh process/resource guard;
stderr is empty, no guard stopped a job, and real preferences, production binaries and
all 126 frozen inputs remain unchanged. The 25-path staged patch is still byte-identical.
The admin/UI owner has only a report-update request, with source and harness frozen.

The six worker leads had no new implementation results; no heavy slot remains active.
At 22:59 the machine had **6.389 GiB available / 53% commit** and no heavy process. The
Maximum world startup remains below its **8 GiB** floor. Actual UI input was injected
through Godot; physical input and N11 in-world destruction/damage/loot/sound acceptance
remain open, along with the other backlog packages. The coordination schedule stays active.

**22:34 footer build passed; captures blocked before launch:** root reviewed the footer-only
change and isolated capture host. The shared solution build passed zero warnings/errors in
**10.84 seconds**, minimum **5.745 GiB free / 54% commit**; evidence:
`.artifacts/recovery/integrated-checks/20261016-222628-build.*`. The scratch build also passed
zero warnings/errors in **4.242 seconds**, minimum **6.205 GiB / 52%**, with unchanged inputs
and real preferences: `.artifacts/admin-settings-footer/runs/build-r1/`.

The first `automatic900-r1` capture was refused by its 5.5 GiB starting preflight before
creating its output directory or starting Godot. A follow-up sample at 22:34:14 local found
**5.277 GiB free / 58% commit**, no heavy process and no capture directory; retained in
`.artifacts/recovery/footer-capture-preflight-20261016-2230.json`. There are no new screenshots
and no native visual result. This is a resource hold, not a failed native run. No retry or
heavy slot remains outstanding; user applications are untouched.

The 126-input checkpoint is `.artifacts/recovery/source-freeze-20261016-2234.json`; all 125
prior inputs are unchanged, adding only `GroundUi.Settings.cs`. Its hash is
`E6925A498AF88A4F9AB88CEB26D4198B67C2EBC7C8B3EDDBA12596EC5AE7B3F2`, and the built game DLL is
`DE6EAC4B61EE868ADBE9FFE8D6C085210199ECCF779E4F246CE875D441368F8A`. The 25-path staged patch
is still byte-identical. Source and scratch harness are frozen. Once a fresh preflight has
at least 5.5 GiB free and no heavy job, resume the unused `automatic900-r1` case using
`build-r1` and that exact DLL hash, then review before the 720 and scaled-1080 cases.
Do not rebuild unchanged inputs. Commands are in `.artifacts/admin-settings-footer/README.md`.
The separate Maximum world startup still requires 8 GiB. The coordinator remains scheduled.

**22:17 footer repair source grant:** the admin/UI owner confirmed the reproduced overlap
and a one-file remedy. Root grants only the footer hunk in `GroundUi.Settings.cs`: derive
the text column from the unchanged button group with a 16-design-pixel gap, keep the full
compact APPLY/Escape instruction visible, and fit the GPU description to that column.
Font sizes/colors and button positions/actions stay unchanged. The owner may prepare an
isolated scratch capture host, without changing P8's retained harness or production entry
points. Validation must cover 1600x900 automatic, 1280x720 automatic and 1920x1080 at 1.25
UI preference, including dirty and saved/cancel states. No heavy slot is granted yet.
Memory was **7.089 GiB available / 49% commit**, with no heavy process. Root retains shared
build scheduling; other production inputs and the shared staged set remain frozen. The
Maximum startup requirement is still 8 GiB.

**22:10 N11 settings persistence validated — no heavy slot outstanding:** after the reviewed
scratch-only assembly-identity correction, build-r2 passed zero warnings/errors. The actual
production settings panel passed **49/49 APPLY checks** and a distinct native process passed
**36/36 restart checks**. MIN/MAX and clamps, Escape cancellation, saving 30%, and loading
saved 30% after quitting with an unapplied 100% preview all passed. Root and P8 inspected all
six retained 1600x900 captures. Evidence: `.artifacts/p8-gibs/settings-acceptance-runs/`
`{build-r2,apply-r2,restart-r2}/` and [P8's report](../repair-results/P8-performance.md).

The three jobs took 4.256 / 5.652 / 2.839 seconds; native minima were 6.929 / 7.112 GiB free,
at most 51% commit, with empty stderr and no guard stop. All real preferences, frozen DLLs
and the 125 production inputs stayed unchanged; the shared 25-path staged patch remains
byte-identical. Only isolated v2 preference files were saved. The failed r1 receipts and v1
directory remain intact. This is actual UI code with Godot-injected events, not physical
mouse or in-world destruction/damage/loot/sound acceptance; those N11 limits remain open.

Review found a separate existing UI defect: the orange NOT APPLIED sentence extends beneath
the APPLY button. It is recorded in KNOWN_PROBLEMS and routed to the admin/UI owner for
read-only scope review; no production edit/runtime slot is granted for it. At 22:10 memory
was **7.585 GiB free / 49% commit**, with no heavy process. Maximum world startup remains
below its unchanged 8 GiB floor. The repair queue and follow-up schedule remain active.

**22:00 P8 diagnostic failure routed — no heavy slot outstanding:** the isolated settings
harness built successfully (zero warnings/errors, 4.247 seconds, minimum 6.966 GiB free).
Its first native Apply process exited 1 after eight isolation checks, before settings loaded:
Godot's loaded game assembly exposes an empty `Assembly.Location`, and the harness attempted
to hash that empty path. This is a diagnostic-host error, not a gib-setting regression.
The run took 2.856 seconds, minimum 6.938 GiB, with all real preferences/source/binaries
unchanged. Evidence is in `.artifacts/p8-gibs/settings-acceptance-runs/{build-r1,apply-r1}/`.
P8 has a scratch-only correction grant to verify the copied DLL and its loaded module
identity without requiring `Assembly.Location`, and to fix its null console summary.
Original receipts and the v1 isolated directory must stay intact; corrected first use needs
a fresh v2 directory and a reviewed checkpoint. No retry/build/native slot is granted yet.

**21:49 next bounded step — P8 settings persistence:** compact worker checks found all six
leads idle with no new results. Resources were **6.707 GiB available / 48% commit**, with no
Godot, actual Blender, build or Hollow runtime; the Maximum 8 GiB hold is unchanged. P8 now
has a diagnostic-only source grant under `.artifacts/p8-gibs` to prepare verification of the
actual GIB AMOUNT row, APPLY input path, process restart and unapplied-preview cancellation.
It must reuse production code and a provably isolated user-data directory, preserving the
real preference files and capturing the settings UI. No production source change or heavy
job is granted yet. A necessary Main entry point would need a separate scope review. Normal
destruction/gameplay and physical-user-input acceptance must remain distinct from this check.

**21:39 validated layer-lifetime checkpoint — no heavy slot outstanding:** P2's isolated
harness build passed with zero warnings/errors, and its native Forward+ run passed all **48
layers**. Source and uploaded-array bytes/metadata matched; all 48 shader comparisons after
temporary disposal had zero difference and three positive controls passed. The run exited
zero in **8.41 seconds**, minimum **5.188 GiB free**, maximum **57% commit**, with empty stderr.
Evidence: `.artifacts/p2-environment/layer-check-runs/layer-lifetime-r1/` and
[P2's report](../repair-results/P2-environment.md). This proves isolated data/usability, not
an old-versus-new peak-memory saving or full-world startup.

The subsequent shared solution build also passed **zero warnings/errors**, 38.82 seconds,
minimum **5.408 GiB free** / maximum **54% commit**. Evidence:
`.artifacts/recovery/integrated-checks/20261016-213646-build.*`. The new 125-input checkpoint
is `.artifacts/recovery/source-freeze-20261016-2139.json`; all 124 prior inputs are unchanged,
with only the repaired `GroundLayers.cs` added. The shared 25-path staged patch remains
byte-identical to the saved evening patch. No full suite was rerun for this presentation-only
resource-lifetime change; native checks above cover its changed behavior.

At 21:39, available RAM was **6.640 GiB**, commit **50%**, with no actual heavy process.
The **8 GiB Maximum startup floor remains**: the isolated check does not justify lowering it.
P2's material/grass/water candidates and all other production sources remain frozen. The next
world attempt needs fresh resource/process checks and a new label; do not reuse failed r3.
The recurring coordinator remains active because gameplay/visual repairs are still open.

**21:32 P2 isolated validation slot:** the one-file loader repair and scratch harness are
prepared and reviewed, but not validated. P2 now holds the sole heavy slot for a guarded
60-second standalone harness build, followed only on success by one fresh 90-second native
48-layer check. Both require at least 5.5 GiB free / at most 80% commit and retain the
2 GiB / 85% stop guard. No full-world run or import is granted. Root will handle the shared
solution build after that slot is released. Actual Blender processes must block launches;
the unrelated `blender-mcp.exe` connector helpers are not Blender render jobs. All source
edits are frozen during validation. The staged patch still matches its preserved SHA-256.

**21:16 heartbeat — one narrow source step resumed:** compact checks found the five other
repair leads idle with no new results; P2's last hold was unchanged. No Godot, Blender,
dotnet/MSBuild or Hollow runtime was active. Available RAM was **7.251 GiB**, commit **45%**,
so the Maximum world retry remains below its 8 GiB start floor and was not launched.

The coordinator reviewed `GroundLayers.Load` and granted its existing P2 owner a source-only
temporary-lifetime repair: upload one texture-array channel at a time, release readback Images
and containers after upload, handle failed uploads/partial arrays, and preserve all source
bytes, mipmaps, compression, layer order and material metadata. This supersedes the previous
no-source-edits checkpoint **only for that file**, its scratch validation harness/wrapper and
P2's result report. P2 must prepare isolated native verification before any runtime grant.
The wrapper must enforce the 8 GiB Maximum floor and also exclude active Blender jobs.
Other sources and P2's visual candidates remain frozen. No build/import/render/test slot is
outstanding, no LOD work is added, and the shared 25-path staged set remains untouched.

Periodic coordination is now active in this same chat through the app heartbeat
`coordinate-hollow-universe-repairs`, every ten minutes. It checks existing workers/resources,
advances only authorized work under the single-heavy-job limits, and stays quiet on unchanged
holds. This replaces the earlier unmonitored idle gap; it does not grant simultaneous runtimes,
user-app cleanup or a LOD rewrite. The local computer and app must remain running. Stop the
follow-up schedule when the queue is finished or only explicit owner acceptance remains.

**20:50 validated startup repair checkpoint:** the shared build passed zero warnings/errors
(`20261016-203812-build.*`). Transport/loading/architecture checks passed **15/15**
(`20261016-204130-startup.*`), including a real 12-second client-update stall: the manual
control timed out while background transport retained its session; callbacks remained on
the caller, and disconnect/disposal completed. The actual built dilation kernel matched the
retained original **bit-for-bit in 120 cases**, with all four allocation checks passing
(`.artifacts/p8-startup/dilation-result-01.json`). Pixel output is preserved; allocation
volume is measured separately from peak RAM. These changes have not yet passed a native
Maximum world startup. The 124-input source checkpoint is
`.artifacts/recovery/source-freeze-20261016-205044.json`; no inspected text contains NULs.

Another Maximum world run is deferred while idle headroom is around 6 GiB. Its previous
observed available-memory fall was approximately 5.8 GiB, so use **at least 8 GiB free** for
that specific retry while retaining the 2 GiB stop guard. This workload-specific requirement
does not block the smaller validated jobs. No runtime slot remains outstanding; no new
source edits are authorized by this checkpoint. Remaining visual/gameplay checks and the
full suite are still open. Do not label the earlier failed world run an accepted baseline.

**20:35 current queue:** P4's incremental import refreshed only the holo, and both male/female
editor checks passed 16 actions / 372 FPS/TPS samples each. Axe, pickaxe and holo-mount images
were inspected; actual hosted contact/ADS and physical-mouse acceptance remain open. Admin's
full UI check passed 67/67, including crafting's 16 cases; three HUD and three offline clan-base
captures passed inspection. User preferences remained unchanged. See the P4 and admin reports.

P2's Maximum/day baseline began with 7.651 GiB available, but the server timed out during
world loading. The client then received PeerNotFound. A secondary memory observation around
1.8 GiB triggered termination of only the owned process after 49.79 seconds. No ready
screenshots or frame profiles exist. Both the original receipt (sampled minimum 2.034 GiB,
null stop reason) and the explanatory intervention are preserved in
`docs/images/repairs/p2-environment/baseline-max-day-r3/`. No quality improvement is claimed.

P8 now owns a narrow source repair: opt-in background transport for the game while callbacks
remain queued/main-thread and deterministic clients keep manual transport; normal timeouts
remain 10 seconds. It also owns bit-preserving scratch reuse in `FloraTextures.Dilate`.
P2's renderer candidate files remain frozen. These repairs need a fresh build, targeted
regressions and a separate bounded startup capture before the environment route resumes.

**19:22 coordination restart:** the owner asked why coordination had gone idle. The
coordinator had ended its earlier turn while workers were waiting for validation slots;
there was no ongoing monitor. Fresh available memory recovered to 7.436 GiB with no heavy
job active. The new shared build including P5's sequencing repair **passed, zero warnings
and errors**, in 38.95 seconds, minimum 6.245 GiB available / maximum 50% commit. Evidence:
`.artifacts/recovery/integrated-checks/20261016-192147-build.*`. The 122-input source
comparison has only the expected four P5 source/test changes since the prior build.
P5 has the exclusive guarded combat rerun slot; the coordinator remains active to advance
the shared foundation checks and then bounded visual validation. The earlier memory holds
below are historical observations, not the current queue state.

The combat rerun completed **68/68** in 82.9 seconds, including the repaired rifle ordering
case and three new regressions. Evidence: `.artifacts/attack-system/recovery-combat-20261016-1923.*`.
The foundation batch completed **61/64** in 14.8 seconds; two cliff-coverage failures and one
clan-base occupied-site test were returned to their owners. Earth/save, environment-route,
architecture, camera, held-item, loading, streaming and gib catalogue checks passed in that
batch. Evidence: `.artifacts/recovery/integrated-checks/20261016-192939-foundation.*`.
P8's native diagnostic and settings capture ran serially. Settings fit and user preferences
were preserved; five renderer-count assertions failed. P8 corrected the diagnostic to query
the rendering RID rather than resource metadata, preserving the cap/clear assertions; a
new build and native rerun were required. P4's subsequent import/editor slot did not start
because its 5.5 GiB preflight floor was not met; it was returned without launching a process.

**19:55 completed checks:** after the P1 fitting/fixture, clan-base fixture and P8 diagnostic
corrections, the shared build passed again (zero warnings/errors, 16.96 seconds, minimum
4.014 GiB available). The foundation rerun passed **64/64** in 16.0 seconds, including all
three previously failing cases. Evidence: `20261016-194953-build.*` and
`20261016-195242-foundation.*` under `.artifacts/recovery/integrated-checks/`.
The coordinator then ran P8's corrected native diagnostic: **44/44**, zero failures,
2.51 seconds, minimum 4.91 GiB available. The saturated maximum/10-percent/zero renderer
counts now match the live limits; the cleared image was inspected and contains no fragments.
All three user preference files remained byte-identical. Evidence:
`.artifacts/p8-gibs/20261016-02-gibs/`. These are isolated diagnostic passes, not full gameplay
or overall FPS acceptance. P4 import/editor/capture and P2's Maximum route remain queued
pending sufficient headroom for those larger jobs.

**17:18 update:** the owner reported Blender export work complete and explicitly requested
resuming all coordinated development. A fresh check found 9.1 GiB free / 37% commit and no
Blender, build or game process. Seven development leads and the asset-audit chat resumed.
After correcting one missing namespace import in the clan-base tour, the shared incremental
solution build **passed with zero warnings and errors**. The guarded run took 12.87 seconds,
with at least 5.983 GiB available and at most 48% commit. Evidence is in
`.artifacts/recovery/integrated-checks/20261016-171805-build.*`; that 122-input checkpoint
is `.artifacts/recovery/source-freeze-20261016T141805Z.json`.

The initial memory hold below is historical. One-heavy-job scheduling remains active.
P5's guarded focused run then completed **64/65**, revealing a real reordered-gun-input
sequencing failure (nine predicted shots for eight authoritative shots). The owner prepared a
shared sequencing correction and three regressions; these changes invalidate that build
checkpoint and need a fresh completed build/test run. Test evidence:
`.artifacts/attack-system/recovery-combat-20261016-1719.*` (80.6 seconds, no memory-stop).
The coordinator's foundation batch did not launch because its next preflight had 4.902 GiB
free, below the retained 5.5 GiB starting floor. No GPU job has been granted.

The [read-only asset review](../repair-results/Asset-recovery-review.md) is complete. No package
has been imported into the project. New compound GLBs can use Godot's automatic mesh LOD;
authored FBX LODs are optional fallbacks. Extracted Rust content in the recovery inventory is
excluded. See the report for actual files, provenance limitations and useful plant candidates.
No full-suite, visual-quality or performance acceptance follows from a build pass.

P8's bounded 17:31 resource audit found 2.56 GiB available and hundreds of helper processes
whose ancestry leads to live Codex/Claude applications. No sizable safely disposable orphan
tree was established. Summed working sets are not reclaim estimates. No app or helper cleanup
was performed or authorized; further heavy validation still requires restored headroom.
See [P8's report](../repair-results/P8-performance.md). The owner's LOD clarification means
routine existing-LOD verification during asset integration only, not a new Nanite/LOD project.

16 October 2026, 15:18 Europe/Istanbul. The owner requested resuming interrupted chats after
the computer crashed. Windows reports a boot at 15:10. The exact crash cause is not proven.
Prior logs include overlapping full suites/rendering, native failures and memory exhaustion.
The machine has approximately 16 GiB physical memory; the initial recovery check found about
10 GiB available and an already open Blender process. Do not close the owner's applications.

The staged changes, report images and most source/package notes survived. Byte inspection
found NUL-filled P1 `CliffField.cs` and three P2 scratch files; the owners preserved the corrupt
bytes and reconstructed their files from earlier source/history. Presence alone is not an
integrity check. P1 subsequently byte-checked and hashed its ten owned files; P2 confirmed all
thirteen live renderer/shader baseline hashes and rebuilt its scratch route/launcher.
A recovery copy of staged/unstaged binary Git patches and 65 untracked files (about 45 MB) is
at `.artifacts/recovery/2026-10-16T121759Z`. Existing staging belongs to the admin-camera work;
do not commit or unstage it accidentally. The current HEAD is `6746ea3f` at this checkpoint.

## Temporary execution limits

These limits supersede the earlier three-subagents-per-chat plan during recovery.

**19:48 bounded-job refinement:** measured successful runs reduced available RAM by
1.693 GiB for the single-worker incremental build, 1.137 GiB for the foundation batch,
and 0.433 GiB for P8's isolated gib diagnostic. With 4.974 GiB currently available and no
heavy process, the coordinator permits a **4.5 GiB start floor only for these same bounded
jobs**, retaining the 2 GiB / 85% commit stop guard and serialized execution. The root
wrapper samples every second. This is not permission for a full suite, import, character
or world render at that floor; those retain the 5.5 GiB floor. Current workload and memory
are checked again before each job. The purpose is to keep measured small work moving while
the larger jobs remain deferred; the previous blanket floor was unnecessarily restrictive
for these specific observed jobs.

- Resume the existing lead chats and their saved work. Do not spawn or resume subagents yet.
- Parallel light reading/editing is permitted within the existing file claims.
- Only one heavy job group across all chats: solution build, test suite, load test,
  Blender export, Godot import or GPU capture. No build/test/render combinations in parallel.
- Request an explicit slot from coordinator `01a14455-2b1c-7e62-be75-bc958fc1779a` before any
  heavy job. At creation of this recovery note, no runtime slot is granted.
- Prefer single-worker builds and bounded scenarios. Do not run several full suites for
  different packages; obtain one stable integrated snapshot and one serialized suite.
- After the guarded build below, require at least **5.5 GiB available RAM** before requesting
  another runtime/build slot. Stop the owned job below 2 GiB free or above 85% commit.
- Check available RAM before and during large jobs; stop the job you own on escalating
  memory pressure rather than launching another. Do not kill another chat's/user's processes.
- Git staging and commits also need an exclusive coordinator slot because the index is shared.
- Preserve failed/interrupted logs. Resume from source and artifact inspection; a surviving
  DLL or unfinished log is not proof of a completed build or test pass.

## Recovery queue

| Chat | Recovery action |
| --- | --- |
| Investigate weapon hit sync | Resume final source review of cancellation/empty-cannon changes; inspect existing attack test/load-test evidence; request bounded validation |
| Check Earth planet surface area | Recover stable terrain/save revision and interrupted isolated-suite status; provide source hashes; coordinate P1 collision revision and P2 route |
| Add F1 build grade and free crafting | Original admin feature is committed; preserve staged admin-self-camera work; later human request separately authorizes a clan-base preset |
| Coordinate checklist tasks (P1, ending `4a61bf`) | Continue cliff candidate from saved source/baseline; confirm collision revision and requested geology test/review paths |
| Coordinate checklist tasks (P2, ending `9c4dc8`) | Continue staged terrain/grass candidates; owns route capture/selector diagnostics; queue baseline before installation |
| Coordinate checklist tasks (P4, ending `f1d`) | Continue pickaxe/holo/workshop candidate; preserve accepted axe; inspect any pre-crash exported holo before using it |
| Contact review checklist session | P8 completed resource/validation inventory; approved bounded gib-setting source work; no stress/load/GPU jobs |
| Audit building asset library | Already complete; findings are retained, do not rerun the library audit |
| Add disconnect option | Already complete and committed; retain its validation limits, do not duplicate the feature |

After initial acknowledgements, the coordinator serializes finishing the stable source/build
checkpoint, necessary focused tests, then P2/P1 baselines and candidates. Pending work remains
in REPAIR_BACKLOG.md. A resumed chat is not a completed repair.

## Confirmed recovery findings

All six interrupted lead chats acknowledged recovery; P8 also acknowledged its assignment.
See [P8's inventory](../repair-results/P8-performance.md) for timestamped resources and logs.
Windows recorded bugcheck `0x0000003b`; its cause has not been established. The dump was not
analyzed. Historical allocation failures and native test crashes are separate evidence.

P1/P2/Earth/P4/P8/admin and P5 reported coherent source checkpoints. Queue a completed
integrated single-worker build when memory headroom permits, then focused checks covering
attack/held timing, terrain/save/architecture, cliffs/routes, clan-base layout and gibs.
The admin/UI owner has also implemented the directly requested crafting HUD beside the vitals.
Its source checkpoint includes active output counts, produced-item inventory counts and compact
layout. Queue its existing UI-viewer `--view ui --crafting-hud-check` scenario (16 checks), then
separate 1600×900 and narrow-layout captures. The checks have been written, not run. Details
and exact source ownership are in [CRAFTING_HUD.md](../CRAFTING_HUD.md) and
[the admin handoff](../repair-results/Admin-tools-recovery.md). This invalidates earlier
source manifests; capture fresh inputs before the next integrated build.
No previous incomplete full-suite run counts as a pass. GPU scenarios remain separate slots
with fresh memory checks; the user's Blender process remains untouched.

## First integrated recovery build — stopped, not passed

The coordinator committed the merged checklist, prompts and eleven original screenshots in
`86d2d9f1`. The pre-existing 25-path staged admin-camera binary patch remained byte-identical.
A wider scan of 124 changed/new repository text files found no empty or NUL-filled files
after reconstruction. A later common checkpoint hashed 113 changed source/data inputs at
`.artifacts/recovery/integrated-source-freeze.json`.

At 15:35:54 local, the root launched one Debug solution build with one MSBuild worker,
shared compilation/build servers disabled and a 180-second bound. Its memory monitor stopped
the owned process tree after 34.69 seconds: the sampled available RAM reached **1.566 GiB**
(74% commit). The log emitted core/server/test/client DLL paths but has no completed solution
summary. **This is an interrupted build, not successful integrated validation.** No focused
tests, Godot, GPU, import or load job followed; a process check found no remaining dotnet,
MSBuild, compiler-server, Hollow or Godot process. A subsequent sample had about 1 GiB free.

Evidence: [build receipt](../../.artifacts/recovery/integrated-checks/20261016-153554-build.receipt.json),
[stdout](../../.artifacts/recovery/integrated-checks/20261016-153554-build.stdout.log),
[memory samples](../../.artifacts/recovery/integrated-checks/20261016-153554-build.memory.csv).
No automatic retry or heavy slot is granted. Compilation freeze is released for light owned
source/docs work; any change invalidates that source checkpoint. Existing user applications
were left untouched. Restart heavy validation only with the headroom and explicit slot above.
