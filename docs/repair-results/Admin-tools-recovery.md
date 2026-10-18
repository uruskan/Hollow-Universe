# Admin tools recovery — 16 October 2026

The BGrade and resource-free building/crafting feature is committed as `b80e804e`.
The camera's 25-file staged set remains intact after the crash; it has not yet
been committed. The separate 3×3 clan-base request is now being implemented in
owned new files and narrow F1 hooks, coordinated with the recovery lead.

## Camera evidence

- Pre-crash isolated solution build: zero warnings/errors.
- Integrated primary solution build: zero warnings/errors; focused camera/seat
  checks: 7/7. Six camera lifecycle, input and command checks are new.
- Rendered Godot UI checks at 1600×900: 51/51.
- Real-input `admin-camera` gameplay tour: passed, with six inspected JPGs under
  `docs/images/admin-self-camera`.
- The full engine-free suite was interrupted by the restart, with no final summary.
  `HeldActionTimingTests.AcceptedUseRetainsConsumedItemForObserversAndRefusalDoesNotReplay`
  failed at “valid use sent” and failed once in a separate rerun. The combat owner
  and coordinator are handling it in the shared recovery validation.

The successful camera capture temporarily raised the isolated client/server
transport timeout from 10 to 120 seconds after three startup stalls. Those changes
were restored before the last build/tests and are not staged. The capture logged
texture decode/GPU allocation failures and shutdown resource warnings; these are
retained as startup/resource findings, not described as repaired by this feature.
The earlier headless UI attempt used the wrong viewport and failed; the correctly
sized rendered UI run passed.

All available camera `.log`/`.exit` files have been copied from the isolated
worktree into primary `.artifacts/admin-self-camera`. The managed worktree remains
attached while validation is incomplete. No post-restart heavy job has been run
by this chat. No other chat's staged or unstaged changes have been altered.

## Clan base checkpoint

See [the layout and research](../ADMIN_BASE_PRESET.md). The corrected draft has
228 socket-linked pieces and is wired to F1's five grade buttons. New loader,
whole-site spawner and five focused tests are at a complete checkpoint for the coordinated build.
Both source files were checked for null bytes after the recovery corruption report:
zero found. Static review moved the four peek frames into the open triangular
recesses between honeycomb bays. They now have exterior window cover and adjacent
solid standing tiles; a physics-ray check guards against looking into sealed
honeycomb instead of open ground. The entrance uses a triangular airlock so its
roof also stays outside the front peek-down recess. The 19 camera-staged text working copies were
also checked for null bytes: zero found.

Remaining: focused structure/site/network/save checks, actual visual inspection,
serialized integrated validation and scoped commits. Existing hut tours explicitly
use `raidbase hut`; default `raidbase` and the F1 button spawn the clan layout.

## Post-restart execution limit

The coordinator's single-worker solution build at 15:35 was stopped by its memory
guard after 34.69 seconds (1.566 GiB available, 74% commit). It emitted several
project DLLs but no successful solution summary. No test or GPU run followed.
That is not a build pass. The revised blueprint, five tests, offline `clan` scene
and hosted `clan-base` tour were completed after the temporary source freeze was
released, so they require a new completed build.

Runtime work now requires at least 5.5 GiB available plus an explicit coordinator
slot; an observed later read was 3.46 GiB. This chat has not stopped user apps or
started another runtime. The previously described held-use rerun log is now filled
with null bytes; the pre-crash observed failure is retained as a finding, but that
file cannot serve as fresh test evidence. The full-suite partial log still ends at
`RUN ShipNetT`.

## Crafting HUD — additional owner request

The owner requested crafting status at the bottom right, left of health/food/water,
with remaining output and inventory counts. The recovery coordinator granted the
narrow HUD/model/viewer ownership; no build/test/Godot/Git slot was granted. See
[the UI behavior and previews](../CRAFTING_HUD.md).

The implementation follows the first live queue entry: produced-item icon/name,
remaining output units (`CRAFTING ×4`), carried output count and progress toward
the next completion. The adapter preserves the produced item separately from the
recipe key and uses the existing `job.Count * recipe.Amount` conversion once.
An empty queue, death, another screen or the flight HUD hides the card. Normal
layout fits between hotbar and vitals; a narrow window or larger UI preference
lifts the card above the hotbar and its ammo/seat hints.

The mock now finishes one recipe at a time, including multi-item output and
elapsed time crossing queue entries. Two frozen preview scenes show four and
three remaining bandages. Sixteen focused assertions exercise the production
adapter, remaining/owned changes, progress, cancellation, completion, visibility
and four size/scale layouts. `--view ui --crafting-hud-check` runs just those;
the full `--ui-check` includes them too. They reuse the existing HUD and hold the
mock clock, avoiding a second character/map load and wall-clock timing races.

Exact touched source paths for this additional request:

- `game/scripts/UI/GroundUi.HudCrafting.cs` (new renderer/layout)
- `game/scripts/UI/GroundUi.Paint.cs` (paint hook)
- `game/scripts/UI/Model/GroundUiTypes.cs` (produced-item/progress fields and card snapshot)
- `game/scripts/UI/Model/MockGroundUi.cs` (incremental preview crafting)
- `game/scripts/UI/Viewer/UiViewer.cs` (preview/check hooks, deterministic check clock)
- `game/scripts/UI/Viewer/UiViewer.CraftingHud.cs` (new previews/checks)
- `game/scripts/Ground/ClientGroundUi.cs` (existing owned adapter seam; its camera staging preserved)

Documentation: `docs/CRAFTING_HUD.md` and this owned handoff. No edits to the shared
root trackers/design/development log were made. New source requires a fresh
integrated checkpoint. Static review and scoped whitespace/byte checks are not a
build or runtime pass. Compilation, the 16 checks, full UI regression and actual
1600×900/narrow screenshots remain pending the coordinator's safe validation slot.

Static checkpoint: all nine listed source/document files are nonempty with zero
NUL bytes; scoped `git diff --check` is clean. Their SHA-256 manifest is
`.artifacts/crafting-hud/source-checkpoint.json`. No staging or commit was performed;
the existing 25 camera-staged paths remain present.

## Resumed shared build — 17:18 local

After the human reported the Blender job finished, the coordinator observed
9.1 GiB available and resumed serialized validation. The first completed build
reported eight errors from one missing `using Hollow.Client;` in the new clan-base
capture tour. That import was the only source change before refreezing.

The subsequent integrated incremental build **passed with zero warnings/errors**
in 12.87 seconds, with 5.983 GiB minimum free RAM and 48% peak commit. Evidence:
`.artifacts/recovery/integrated-checks/20261016-171805-build.stdout.log` and its
`.receipt.json`; frozen inputs are in
`.artifacts/recovery/source-freeze-20261016T141805Z.json`. This compiles the camera,
228-piece base, five base tests, HUD and its 16 checks together. Behavior tests
and GPU evidence remain separate pending gates; no private runtime or Git job
was started by this chat. The coordinator is serializing combat tests first,
then foundation/admin/base checks, followed by explicit visual slots.

## Foundation result and fixture repair — 19:29 local run

The shared fresh Debug build at 19:21 passed with zero warnings/errors; the
coordinator reports combat 68/68. The foundation run completed 61/64 with no memory
guard stop (6.436 GiB minimum free). All six camera tests passed. Four of five
clan-base tests passed: all-tier support/layout/doors, invalid blueprint refusal,
occupied-site atomicity/clear peek-down rays, and wet/steep site refusal.
The remaining clan test failed before replication/save assertions: F1 correctly
reported its requested footprint occupied.

The fixture had used default arrival positions and assumed 26 m ahead was clear.
`StandInNaturalObjects.ClearRadius` only guarantees 9 m around the landing point;
the network fixture also had not explicitly moved the guest clear. Only
`tests/Hollow.Tests/ClanBaseTests.cs` was repaired: it computes the actual command
target, depletes solid nature in a 20 m fixture area through the established
`NaturalState.Apply` path, deliberately puts the guest there and asserts F1's
occupied refusal plus zero partial pieces, then moves the guest 30 m aside and
continues the original replication/restart assertions. No spawner, production
collision, terrain or game-data rule changed. The rerun reports the number of
solid natural objects cleared to make fixture preparation visible.

Evidence: `.artifacts/recovery/integrated-checks/20261016-192939-foundation.stdout.log`
and `.receipt.json`. The fixture repair is source-reviewed and refrozen, **not
runtime-validated**. Its updated hash is in
`.artifacts/admin-clan-base/source-checkpoint.json`. No private runtime, build,
new agent, staging or commit was started; the 25 camera-staged paths remain.
[Exact queued commands](Admin-validation-commands.md) cover the focused checks,
full UI regression, normal/narrow HUD screenshots, three clan-base sandbox views
and the optional separately granted hosted F1 tour with an isolated save.

## Completed foundation and standalone visual validation — 20:28 local

The coordinated 19:49 build passed zero warnings/errors; the 19:52 foundation run
passed 64/64, including all five clan-base and six camera tests. The fixture repair
is now validated through admin authorization, occupied-site refusal without partial
pieces, replication to two clients and save/restart. Evidence is under
`.artifacts/recovery/integrated-checks/20261016-194953-build.*` and
`20261016-195242-foundation.*`.

The exclusive visual slot then completed the full UI check once: **67/67**,
including the 16 crafting checks. Three HUD and three offline clan-base images
were captured sequentially and inspected. All seven jobs exited 0, kept at least
5.029 GiB available (preflights at least 5.900 GiB), peaked at 59% commit and took
4.31–8.07 seconds each. All checked settings files and three recorded DLLs remained
unchanged; no settings restoration was needed. No build/import/hosted tour/Git
job ran in this slot. No runtime process remained afterward.

[The visual report](Admin-visual-validation.md) records the exact DLL hash, receipts,
images and inspection findings. Summary: `.artifacts/admin-validation/summary.json`.
The three base captures each logged 30 leaked ObjectDB instances on exit, retained
as an open cleanup finding despite successful images and exits. No allocation or
texture-decode errors occurred. Hosted gameplay navigation and its F1 captures
remain unrun; no inference of a hosted-tour pass is made. The heavy slot is released;
existing staging remains untouched and commits await a coordinator Git slot.

## N11 settings-footer overlap — read-only review and proposed repair

The coordinator requested inspection only; implementation and runtime validation
are **not granted** in this follow-up. The existing P8 input/APPLY/restart result
remains a behavioral pass, but does not establish a clean footer layout.

Inspected evidence:
`.artifacts/p8-gibs/settings-acceptance-runs/apply-r2/02-thirty-unapplied.png`
(1600×900), SHA-256
`7833C23F2739D2D9F34AE3B0E2977A99426C1138CD566AEC559A0788AEACD532`.
The orange dirty-state sentence visibly runs under APPLY; the ending about Esc
is obscured. This is confirmed in that image, not inferred from the numeric
acceptance result. The same dirty branch supplies the other preview states.

Cause: `GroundUi.Settings.cs`, `PaintSettings`, paints unbounded `Text` at
`SetX + 36 = 506`, `foot + 2 = 756`. The four footer buttons begin with APPLY at
x934, y754, leaving only 428 design pixels before the button, or **412** with a
16-pixel gap. The full sentence exceeds that space. The adapter/default-preset
description at y780 is unbounded too and can overlap on longer GPU names.
`TextIn` in `GroundUi.Paint.cs` aligns but does **not** clip, wrap or constrain
the drawn string. `Paragraph`/`WrapLines` support width-bounded multiline text,
but adding a second status line at the existing positions would collide with
the adapter row. The existing `Fit` helper in the `GroundUi.Console.cs` partial
uses `Measure` and an ellipsis to bound a single line; the settings partial can
reuse it without changing either shared helper file.

Proposed narrow repair, awaiting the coordinator's implementation grant:

- Assign this admin/UI chat only the footer hunk of
  `game/scripts/UI/GroundUi.Settings.cs`. P8 retains its gib logic/settings rows
  and acceptance harness; no `GraphicsSettings`, persistence, input, or shared
  paint-helper change is proposed.
- Derive a footer text column from the existing APPLY left edge, with a 16-design-
  pixel gap, instead of letting the text extend through the button group. Keep
  existing button positions, sizes and actions.
- Use compact dirty copy, proposed as
  `NOT APPLIED: APPLY saves; Esc reverts.` The full APPLY/Esc instruction must be
  visible, not accepted merely because it has been clipped or ellipsized.
  Use the existing measured single-line fitting for the footer text bounds and
  for the GPU/default-preset line; an ellipsis is appropriate on the latter when
  necessary. Preserve the saved-state message and existing font sizes/colors.
- Keep all dimensions in the existing design coordinate/scaled drawing path.
  No new layout framework, panel expansion or global typography change is needed.

Proposed validation after a separate build/runtime grant: reuse the existing
real-input settings acceptance path, preview MIN/30%/MAX, inspect dirty footer
text at 1600×900 and normal UI scale, then inspect the saved state after APPLY
and after an Esc cancellation. Confirm both APPLY and Esc instructions remain
fully visible, the GPU line ends before the 16-pixel gap, and all four buttons
remain unobstructed and clickable. Include 1280×720 automatic scaling and
1920×1080 with a 1.25 UI preference to catch font-rounding/layout issues. Preserve
settings with the existing isolated/backup-restore acceptance workflow. A measured
fit assertion may supplement these captures, but cannot replace image inspection.
Do not rerun the gib stress workload for this text-only fix.

Read-only source baseline: `GroundUi.Settings.cs` SHA-256
`5BBF04E94F649AA941DF625F14EC92C17812731DDF0F7A35755BE28D31B7B8D5`.
Only this report was edited for the follow-up. No production source, existing
capture, staged content or other working change was altered; no build, runtime,
screenshot rerun, new agent or new chat was started. The overlap is still open.

## N11 footer implementation prepared — awaiting coordinated build/captures

The coordinator subsequently granted the footer hunk and this report, plus an
owned scratch capture host. That narrow implementation is now prepared and frozen.
`GroundUi.Settings.cs` retains the four button positions, actions, dimensions,
fonts and colors. The button cursor supplies the actual APPLY left edge; the text
column ends 16 design pixels before it (412 pixels wide). Dirty copy is the full
`NOT APPLIED: APPLY saves; Esc reverts.` with no truncation. The saved message is
unchanged. The GPU/default-preset description uses existing `Fit`/`Measure` to
stay inside the column. No persistence, shared paint helper, or other UI source
was changed for this repair.

Production source SHA-256:
`E6925A498AF88A4F9AB88CEB26D4198B67C2EBC7C8B3EDDBA12596EC5AE7B3F2`.
Exact diff: `.artifacts/admin-settings-footer/source.diff`.

Prepared scratch project: `.artifacts/admin-settings-footer/`. Its README records
the exact bounded commands and acceptance criteria. It reuses P8's diagnostic
structure in this separate directory; P8's original files and evidence are
untouched. Only the new C# host compiles, referencing the already-built production
game. Read-only resource/source-path junctions have been prepared; the scratch
project must never be opened in the editor or imported. No production diagnostic
entry point or implementation-mirroring unit tests were added.

The host uses actual GroundUi, GraphicsSettings, SettingRows and UiScaler. Its
native userdata is verified as
`%APPDATA%/HollowUniverseABTestB-AdminSettingsFooter-20261016-v1`, disjoint from
the real game's preferences. The first case requires a fresh directory. Subsequent
cases require the previous successful receipt and unchanged isolated settings.
Only the starting scale preference is seeded in scratch. Mouse/key events select
the real row, change it, click APPLY, then change it again and cancel with Escape.
Coordinates use `CanvasPoint`, subtracting its X origin because settings uses
LeftFrame, while retaining scale and the bottom anchor. Both wrapper and host
hash real settings before/after. The loaded production module is checked against
the copied and workspace DLL hashes and MVIDs.

Queued separately after the coordinator's shared solution build:

1. One scratch-only build referencing the approved resulting DLL hash, using one
   worker without shared compilation/build servers.
2. 1600×900 automatic: dirty, saved after APPLY, saved after Escape cancellation.
3. 1280×720 automatic (production rounded scale 0.8125): the same three captures.
4. 1920×1080 with 1.25 preference: the same three captures.

Each job requires a separate slot, has a 60-second limit, checks at least 5.5 GiB
available and at most 80% commit before starting, and stops only its own process
tree below 2 GiB or at 85% commit. Every capture requires image inspection for the
complete APPLY/Esc instruction, GPU text stopping before the gap and unobstructed
button labels. This small UI host starts no world/server/stress workload; the
separate Maximum-world 8 GiB gate still applies. No automatic retry is prepared.

Completed preparation checks are limited to PowerShell syntax parsing, project
XML parsing, source diff/whitespace review and hashes. **No build, import, Godot
run or new screenshot has run**, so the visual finding remains open pending
those captures. Source/checkpoint and command plan are frozen; no new agent/chat,
staging or commit was performed, and the existing 25 staged paths remain.

## N11 coordinated builds passed; first capture blocked before launch

The coordinator reviewed the footer diff and scratch host, then ran the builds
serially. Shared solution `20261016-222628-build` passed with zero warnings/errors
in 10.84 seconds, minimum available memory 5.745 GiB and maximum commit 54%.
Receipts and logs are under `.artifacts/recovery/integrated-checks/` with that
prefix. The resulting game DLL SHA-256 is
`DE6EAC4B61EE868ADBE9FFE8D6C085210199ECCF779E4F246CE875D441368F8A`.

Scratch `build-r1` also passed with zero warnings/errors in 4.242 seconds,
minimum 6.205 GiB available and maximum commit 52%. Its receipt at
`.artifacts/admin-settings-footer/runs/build-r1/receipt.json` confirms no source,
production-binary or real-preference changes during that job. The unchanged
inputs do not need another build before resuming captures.

The first `automatic900-r1` capture was **blocked by the resource preflight before
creating its result directory or starting Godot**. This is neither a failed
native run nor a visual pass; no footer screenshots or native capture receipt
exist. The follow-up sample at 22:34:14 local recorded 5.277 GiB available, 58%
commit, no heavy processes and `captureOutputExists=false`. Evidence is retained
in `.artifacts/recovery/footer-capture-preflight-20261016-2230.json` and the
coordinator's original tool output.

Only this report was updated in this follow-up. The footer source and scratch
host/wrapper hashes still match the preparation checkpoint. No build, runtime,
source-edit or Git slot is held or granted to this chat. The coordinator will
resume `automatic900-r1` after a fresh resource check reaches at least 5.5 GiB,
then inspect it before subsequent cases. All existing evidence and user apps
remain untouched; the visual finding stays open pending the captures.

## N11 footer captures passed — narrow footer scope complete

After fresh resource/process guards, the coordinator resumed the prepared native
captures without changing or rebuilding the frozen inputs. All three runs passed:

| Run | Viewport / actual UI scale | Seconds | Minimum free GiB | Maximum commit |
| --- | --- | ---: | ---: | ---: |
| [automatic900-r1](../../.artifacts/admin-settings-footer/runs/automatic900-r1/receipt.json) | 1600×900 / 1 | 4.365 | 5.548 | 58% |
| [automatic720-r1](../../.artifacts/admin-settings-footer/runs/automatic720-r1/receipt.json) | 1280×720 / 0.8125 | 4.254 | 5.698 | 56% |
| [scaled1080-r1](../../.artifacts/admin-settings-footer/runs/scaled1080-r1/receipt.json) | 1920×1080 / 1.25 | 4.203 | 5.810 | 56% |

Each directory retains `01-dirty.png`, `02-saved.png`, `03-after-cancel.png`,
`result.json` with capture hashes and native checks, `receipt.json`, launch
arguments, memory samples and stdout/stderr. Every process exited 0, with empty
stderr, no stop reason, no native errors and zero changes to production/harness
inputs, production/harness binaries or real preferences. This chat verified those
stored outcomes; the coordinator performed the visual inspection of all nine PNGs.

The coordinator confirmed that the complete APPLY/Esc instruction and GPU line
remain clear of all four footer button labels in dirty, saved and canceled states
at every requested resolution/scale. The full dirty instruction is visible without
ellipsis. The normal saved text also remains clear after both APPLY and Escape.
This closes the narrow footer overlap finding. The earlier resource rejection
remains retained as a pre-launch event, not a failed native capture.

Only this report was changed for this completion update. Source, harness, README,
checkpoint and staged paths remain frozen; no job was launched by this chat.
The coordinator owns the corresponding RECOVERY/BACKLOG/KNOWN_PROBLEMS/DEVELOPMENT
updates. These captures establish native UI layout and injected-input behavior;
physical-input and broader in-world checks remain outstanding.
