# Recovery after the computer restart

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
