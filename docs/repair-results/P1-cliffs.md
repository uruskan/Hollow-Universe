# P1 cliff formations and geology — work in progress

16 October 2026. Assigned by the repair coordinator. Visual acceptance remains open.

## Scope and ownership

- Placement: `CliffPlacements.cs` and new `CliffFormationTests.cs`.
- Renderer: `CliffField.cs`, `FloraLibrary.Rocks.cs`, scanned-rock shader.
- Assets: geology export/import scripts and existing cliff/rock assets; source library read-only.
- Integration: `CliffGeometry.cs`, shared cliff metadata, corresponding geology tests and this handoff.

WalkingWorld/WalkingParameters, terrain-save identity, terrain/grass/water shaders, harvestable
nodes and master repair/development documents belong to the other active owners. Shared build,
Godot, GPU and Git execution is queued through the coordinator.

## Evidence and diagnosis

Inspected original screenshots 1, 2, 5 and 6 under
`docs/images/reports/2026-10-16-playtest/`. Screenshots show repeated scan grids with large bare
terrain areas. Screenshot 5 shows a continuous, regularly scalloped soil wall; its heightfield
cause is a separate terrain-owner investigation, not established as a cliff-mesh defect.

The existing five-family scan library, collision data, recipes and receipts survive. No new
assets, downloads or purchases are required by the current investigation. Current placement uses
fixed 13.5 m columns, up to five repeated rows, a single family per group and only centre/toe
height fitting. The broad scan is approximately 24 m wide, 13 m high and 21 m deep. Its burial
can hide most of the face. The tall scan's upper geometry leans toward local +Z while its lower
geometry leans -Z, opposite the other families; authored facing is being checked and corrected.
The maximum accepted slope of 3.5 also excludes the steepest terrain from scan dressing.

The renderer assigns transforms/visibility every frame and holds its whole pending queue behind
the first unfinished model load. LOD changes abruptly at 95 m from the buried placement origin.
These are source findings; frame-time improvement has not yet been measured.

## Validation state

Initial source baseline: `6746ea3f35a57247db29ca9e390a0f6576cf7d6d`, plus the active shared checkout
changes. Owned baseline source copies are retained in `.artifacts/P1-cliffs/baseline-source`.
The shared recovery solution build is now complete; P1 tests, Godot runs, asset regeneration and
commits remain unperformed. Actual hosted top/face/base, close/medium/far,
different slopes/biomes, collision/raycast, LOD and matched-cost checks remain required.

The coordinator's 16 October 15:35 integrated recovery build was stopped by its memory guard
after **34.69 seconds**, with **1.566 GiB available RAM** and **74% commit usage**. Its log at
`.artifacts/recovery/integrated-checks/20261016-153554-build.stdout.log` lists emitted DLLs,
including Surface, Tests and the game, but has no completed solution summary. This is an
**incomplete build**, not a pass; partial outputs are not fresh validation. No tests or GPU
jobs followed. P1 did not launch another process or retry. Runtime remains ungranted: a new
exclusive job needs at least 5.5 GiB available RAM and must stop below 2 GiB or at 85% commit.
User applications, including Blender, remain untouched. This update changes only this result
document; P1 implementation source is unchanged from its recorded candidate checkpoint.

The subsequent coordinator-owned **17:18 shared incremental build passed with zero warnings
and zero errors**. The completed summary is retained in
`.artifacts/recovery/integrated-checks/20261016-171805-build.stdout.log`; its receipt records
12.87 seconds, minimum free RAM 5.983 GiB and maximum commit 48%. Source identity is recorded
in `.artifacts/recovery/source-freeze-20261016T141805Z.json`. All ten P1-owned files matched the
saved candidate hashes before this build. WalkingParameters, Protocol and TerrainIdentity had
changed under their separate owners and were reported to the coordinator. This successful
build does not replace the earlier incomplete attempt or establish test/visual acceptance.
P1 remains frozen while P5 and then the foundation batch run in exclusive slots.

## Recovered candidate checkpoint

Following the computer crash and the coordinator's recovery instruction, work resumed lead-only.
The current `CliffField.cs` was 13,842 NUL bytes: file presence was not valid recovery evidence.
The corrupt bytes are retained at `.artifacts/P1-cliffs/recovery-corrupt/game/scripts/Surface/CliffField.cs`.
The renderer candidate was reconstructed from complete pre-crash tool-output source, with the
already identified failure/lifetime fixes completed. No other owned source file contained
NUL bytes. The admin-camera index/staged changes were not touched.

Saved candidate changes, included in the successful shared build but still awaiting runtime checks:

- `CliffPlacements.cs`: unchanged seeded candidate gate/direction; at most nine larger,
  staggered members; footprint/silhouette fitting with bounded samples; authored facing;
  data-driven slope selection; compatible material family within each formation. Removes
  the old unconditional slope-above-3.5 exclusion. Exposes `Downslope` separately from mesh
  `Forward` and `GenerationRevision = 2` for the terrain/save owner's compatibility work.
- `CliffGeometry.cs` / `Data/cliffs.json`: authored facing, slope ranges/weights and material
  family, plus collision bounds. Vertex/index/height arrays have identical values to the
  retained baseline. The tall scan receives a proper half-turn; no mirrored winding.
- `CliffField.cs`: fair background family loading, bounded instantiation, cached transforms,
  visual-AABB LOD selection with hysteresis, failure-aware readiness and teardown handling,
  and diagnostic counters. Original PBR maps and two-sided materials remain intact.
- `environment_cliffs.py` / cliff `provenance.json`: preserve placement policy on future
  exports and record the metadata-only collision-file hash change. No asset re-export ran.
- `CliffFormationTests.cs`, geology portions of `GeologyPortTests.cs` and
  `EnvironmentDetailTests.cs`: deterministic candidate/transform, bounded composition,
  synthetic slope/whole-scan fitting, dry-ground exclusion, exact shared-collision inclusion,
  material-family/receipt/orientation checks, and reproducible per-model camera coordinates.
- `GeologyReview.cs`: supports explicit body-frame `--eye x,y,z --target x,y,z` for matched
  comparisons, or `--tile`, `--part`, `--distance`, `--eye-height`, `--target-fraction`.
  Writes settings, camera, seed/time, module identity, readiness and cliff counters beside
  its screenshot. This remains a standalone renderer diagnostic, not hosted acceptance.

Ten candidate files and dependency hashes are recorded in
`.artifacts/P1-cliffs/candidate-source/manifest.json`. This is an owned-source checkpoint,
not a runnable isolated project or proof of a completed build. No baseline was restored into
the shared checkout. `git diff --check` passed for candidate sources. The later shared build is
recorded above; no engine-free test, Godot capture or performance result is claimed yet.

The requested geology checks are `CliffFormation GeologyPort EarthCliffs Architecture`, queued
with the coordinator's foundation batch after the shared build. No parallel heavy jobs,
full suite, Godot or GPU work is included. Paired hosted captures and cost measurement follow
only after a stable integrated source checkpoint and a separate slot.

## Compatibility handoff

Shared placement/collision changes affect every `WalkingWorld` that enables `CliffPlacements`,
not only Earth. No per-body scope filter was added. Heights and their golden hashes are
unchanged by P1; the set of transformed solid cliff triangles changes. Earth/P5 own the required
terrain/save and protocol integration. Do not load existing saves onto this candidate until
that integration is complete. `GenerationRevision = 2` describes P1's candidate; it does not
itself implement save rejection or network negotiation. The metadata/collision-file SHA-256 is
`233858e2fc095f5577b6c9e68b301c78db0bebb990810901bfe363a16295a3f5`.
Placement source hashes are in the candidate manifest; an output-placement golden is pending
the coordinated focused test run and must be recorded intentionally after fitting settles.

## Foundation failures and narrow source repair

The coordinator's 19:29 foundation batch completed **61/64**, without a memory stop. Evidence:
`.artifacts/recovery/integrated-checks/20261016-192939-foundation.stdout.log`. Six of eight
geology-specific checks passed; two failed. This is not a clean validation checkpoint.

1. `CliffFormationFitsTheWholeScanAndKeepsSteepFacesEligible` stopped at nominal gradient
   0.75. The fixture used mean radius 1,200 m to define height change, but the actual sample
   origin was at radius 1,500 m (300 m terrain height). Its physical grade at the origin was
   therefore `0.75 × 1200 / 1500 = 0.60`, below the unchanged 0.62 candidate threshold.
   The fixture now uses ground radius and explicitly checks its grade with the same physical
   five-metre offsets. All formation-count, steep-slope, exposed-area and crest assertions remain.
2. `CliffChainsHaveDistinctStablePartsAndStreamAcrossCellEdges` failed the requirement for
   at least twelve descending formations among the first thirty groups. Source review found
   that one full-mesh-height stride could terminate a column after overshooting its remaining
   face; a valid but non-descending burial also stopped selection before another compatible
   model could be considered. Tier placement now tries at most three distances along the same
   descent (full stride, 70%, 40%). Each retains the original slope, fit, water, monument,
   material-family, bounds and strict descending-radius checks. Model selection tries the
   remaining compatible scans when one fails that radius check. No extra rendered members are
   added beyond the existing nine-part cap. This is a bounded source correction; the next
   coordinated run must establish its actual Earth result.

Both tests now print measured counts before the final count assertion, so any remaining failure
retains useful evidence. No coverage threshold was reduced and no actual Earth assertion was
removed. Placement output changes, but the cliff vertex/index data and heightfield are unchanged;
the in-progress generation-2 compatibility fence remains owned by Earth/P5.

Changed source: `CliffPlacements.cs`, `CliffFormationTests.cs`, `GeologyPortTests.cs`.
The repair copies/hashes are in `.artifacts/P1-cliffs/foundation-repair/manifest.json`;
the earlier candidate snapshot remains intact. Source-only `git diff --check` passed.
No build, test, GPU job, agent or Git mutation was launched by P1. Source is frozen again
for the coordinator's rebuild and focused rerun; these two failures are not claimed resolved
until that run passes.

## Pending dependencies

The terrain owner must provide stable terrain source hashes and any recovered screenshot-5
coordinate. The environment owner owns material continuity and vegetation pockets. A corrected
placement algorithm alone does not establish complete exposed-face coverage or owner approval.
