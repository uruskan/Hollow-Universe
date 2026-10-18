# Earth terrain recovery checkpoint — 16 October 2026

Earth's heightfield remains at the pre-crash checkpoint. The coordinator then
authorized save compatibility for P1's frozen cliff-collision candidate; those
source changes are recorded below. No post-restart build, test, Godot run, import,
staging or commit has been started by this owner.
The shared index contains the separate admin-camera changes and remains untouched.
P2 owns the environment-route implementation and tests; this owner does not edit them.

## Stable source and evidence

| File | SHA-256 |
| --- | --- |
| `src/Hollow.Surface/Walking/WalkingParameters.cs` | `C02E0F8DE80BD255AC67A1FCA878D54250167DFA3A8D6103C8758698667D28E9` |
| `src/Hollow.Surface/Walking/WalkingWorld.cs` | `B4B9CFCF0CE4EF934C62D95ED1C4CC5041A7E3916900E76274E2863DB957C768` |
| `src/Hollow.Persistence/TerrainIdentity.cs` | `7F7B26BEECC385877057A449D6FAB9B7D8BFDA346506FC416CB1DBD35FC44E9C` |
| `tests/Hollow.Tests/EarthTerrainTests.cs` | `053B7A418AB7D7F141D8EACAE02C8F7DE25E46EF066649E37BFEBC7AE6627286` |
| `tests/Hollow.Tests/TerrainSaveTests.cs` | `7B9CFB6ED87ED07BB723DCDE8CEFC93A25F7FB9DCEA370706D9B5830FF6619ED` |

Height/water/biome golden: `3D5174066B119948`. Radius 1,200 m, total sphere
18.0956 km², sampled dry ground 10.64472 km²; 99.9% of sampled dry ground is connected.
These measurements and accepted Low-quality ground/orbit captures remain valid for
this heightfield. They do not validate a subsequent cliff-placement candidate.

Pre-crash solution Debug/server Release builds succeeded without warnings or errors.
Focused terrain/environment checks passed 20/20; save/architecture passed 10/10 after
the server persistence helper resolved the host's line-count failure. Six tests
repeated from the rejected shared-output full run passed 6/6 on copied binaries.
The isolated full run was interrupted: 255 starts, 253 passes, one failure
(`GunNetTests.GunfightOverTheNetwork`, "reloading at once"), and unfinished
`IronWakeNetTests.APassengerIsPredictedExactlyWhileAnotherFlies`. There is no final
summary or successful exit. Logs are retained in `.artifacts/earth-land-tuning/`
and the relevant copies in `docs/images/earth-land/validation/`.

Remaining requested checks on the coordinator's stable integrated build:
`EarthTerrain`, `TerrainSave`, `Architecture`, and the failed `GunfightOverTheNetwork`.
Use the coordinator's single integrated full suite rather than another duplicate.

## Screenshot 5: bounded static diagnosis

The original image and manifest were inspected. No coordinate, camera state, terrain
revision, seed override or quality setting accompanies it. A precise site cannot be
recovered from this evidence alone. No location or root cause is claimed as confirmed.

The image shows one continuous soil-colored face with regularly repeated narrow
columns and a coherent crest. That appearance is consistent with a heightfield or
its tessellation, rather than proof that the separately placed scanned cliff meshes
made the wall. The following source mechanisms need discrimination at a reproduced site:

- `WalkingWorld.Tableland` uses narrow escarpment bands: the plateau's steep share
  rises across a noise-field interval of 0.0108, the mesa's across 0.0084. Those are
  field units, not metres; actual physical width depends on the local noise gradient.
  A narrow transition sampled onto `TileMesh`'s fixed diagonal grid can expose
  repeated facets. The land-profile change did not modify these functions.
- `Drainage.LakeShape` raises low terrain outside a lake's support to the lake level,
  plus a lip, while terrain inside its water bounds remains lower. The support is a
  sum of compact kernels on drainage-grid cells. A low patch near the support edge
  can therefore create a sharply raised rim, and its repeated cell structure warrants
  inspection. This is a source-based possibility, not a match established for image 5.
- Rendered LOD skirts copy tile borders down by `0.03 * tile edge + 3 m`.
  A rendered wall absent from sampled heights would point toward mesh/LOD handling
  rather than a reason to change the procedural height function.

At a reproduced site, compare uncarved height, carved fields, final monument-adjusted
height and rendered mesh at top/face/base; then toggle cliff dressing and inspect LOD
boundaries. This identifies which layer creates the shape before altering a saved
world. No speculative height smoothing, golden update or location claim was made.

## P1 collision compatibility integration

P1's candidate changes placement/orientation and shared collision even when heights
stay identical. Height golden stability does not imply save or network compatibility:
characters, structures or ships can occupy newly added hulls, and old peers would
predict a different collision world. Protocol 32 separated pre-v2 Earth; it could
not distinguish two different collision implementations both labeled 32.

The coordinator authorized integration of P1's frozen generation-2 candidate. Earth
now selects save revision **3** and every other walking body selects **1**. Default
host/server filenames derive from the Earth revision and therefore select
`earth-v3`. Read-only preflight rejects revision-2 Earth plus revision-zero or
unversioned populated saves for other walking bodies. Stand-in ground remains
revision zero. Refusals explicitly explain that land shape and solid cliff
placement are both part of the identity; preserved files require their matching
build or a new save path. Schema remains 9: no new data migration is needed.

P5 owns the protocol update, now observed at **33**; this owner did not edit that
file. The global seed version, all height parameters and heightfield golden remain
unchanged. Three additional `TerrainSaveTests` cover all-body identity selection,
Earth v2 rejection, and both unversioned and revision-zero other-body rejection
without modifying their bytes. These are **written but not run**; the eight save
tests and five Earth terrain checks remain queued on the integrated build.

The eight owned source/test files were checked for NUL bytes and SHA-256 recorded
in `.artifacts/earth-land-tuning/collision-integration-hashes.json`. P1 metadata hash
matches `233858e2fc095f5577b6c9e68b301c78db0bebb990810901bfe363a16295a3f5`.
The obsolete pre-recovery commit script was disabled to prevent staging protocol
32 over P5's newer work. No Git index change occurred. This compatibility change
does not establish final visual acceptance or a passing integrated runtime check.

## Coordinator build stopped by memory guard

The integrated attempt beginning at 15:35 Europe/Istanbul was terminated by the
coordinator's memory guard after 34.69 seconds: available RAM reached 1.566 GiB,
with commit at 74%. Only its owned build process tree was stopped. The stdout log
emitted several DLLs, including server, tests and game, but no successful solution
summary. This is **not a build pass**, and partial outputs are not used as fresh
validation. No follow-on tests or GPU captures ran. Receipt, memory samples and
logs are in `.artifacts/recovery/integrated-checks/20261016-153554-build.*`.

The terrain source hashes above remain unchanged. Runtime work is held until
the coordinator grants one job with at least 5.5 GiB available RAM; the active
job must stop below 2 GiB available or at 85% commit. No automatic retry,
new agents or changes to the user's applications are authorized. These limits
are recovery coordination requirements, not a claimed cause of the earlier crash.
