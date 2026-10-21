# P7 / N10 roof review — 17 October 2026

## Residual diagnosis: inherited near-coincident detail; candidate can advance to visual review

**17 October, 16:50:43 local:** the single read-only `metal-residual-r1` Python diagnosis
completed, native exit **0**, guarded duration **5.444 seconds** (runner **4.107 s**).
It made **zero repair calls**, executed no exporter/inspection `main`, and wrote no geometry.
The prior parser's constructors/math were reused without invoking its pipeline. Stderr is
empty; no guard error, stop or process termination occurred, and no owned process remains.
**Slot released.**

**Decision:** the inspected residuals do not justify another geometry correction before
controlled engine visual review. The scratch candidate can progress with these inherited
limits documented, under a separate grant. This is not installation approval or visual
acceptance: installed packs are unchanged, and no conclusion about visible z-fighting is
possible from these numbers alone. N10 remains open.

### The 93 tolerance hits are 92 distinct near-coincident face pairs

The previous per-node sample counts were reproduced exactly. The 93 sample locations
refer to **92 distinct triangle pairs** (one triangular LOD1 pair occurs at two samples).
All 92 pairs have a positive *projected* intersection, but **none is exactly parallel or
coplanar** in the stored binary coordinates. Therefore none has a positive-area coplanar
3D intersection; the positive areas below are projections onto the first face's plane.
The faces can cross along a line or lie very close. These findings refine the earlier
10-micrometre tolerance result rather than declaring 93 coplanar duplicates.

Coplanarity was evaluated with integer scalar triples of the exact represented float
coordinates. Triangle intersections were clipped with exact rational arithmetic after
one common projection; area conversion back to the reference plane uses its unit normal.
Recorded per-pair normals, plane constants and both sets of signed vertex-to-plane distances
make the projection and numerical separation explicit.

Across the 92 pairs:

- Normal angles: **0.000090878–0.001361081 degrees**.
- Maximum vertex-to-opposite-plane distance per pair: **0.026948–38.611912 micrometres**.
- Positive projected intersection areas: **0.000015529660–0.000166886008 m²**
  (about **15.53–166.89 mm²**).
- Eight pairs are within both **1 micrometre** and **0.001 degree** over their vertices;
  84 exceed at least one of those descriptive bounds. A sample point can be within the
  earlier 10-micrometre query tolerance while a farther vertex has a larger separation.

| Node | Sample locations / unique pairs | Pairs with both exact raw faces | Pairs with both exact old faces |
|---|---:|---:|---:|
| roof | 12 / 12 | 11 | 9 |
| roof_LOD1 | 15 / 15 | 13 | 14 |
| roof_LOD2 | 19 / 19 | 17 | 16 |
| roof-triangle | 8 / 8 | 7 | 8 |
| roof-triangle_LOD1 | 26 / 25 | 19 | 25 |
| roof-triangle_LOD2 | 13 / 13 | 13 | 13 |
| Total | 93 / 92 | 80 | 85 |

Exact matches compare all position triples with winding preserved up to cyclic order.
All **92** pairs have equivalent raw surfaces at the inspected region; **90** have both
local surfaces in the old repaired subset. For cases without identical triangles, local
equivalence requires positive projected intersection, <=1 micrometre maximum plane
separation and <=0.001 degree between the candidate face and a raw/old face covering the
first sampled region. This is deliberately a local provenance claim, not proof that an
entire triangulated surface is identical. Detailed exact IDs and local matches are retained.

### Why square LOD0 changed from 11 to 12

The change is **two added sample locations and one removed**, with ten shared samples.
Both added locations use face pairs whose positions are **exactly unchanged from raw**;
the old faulty repair had removed one member at each region:

| Raw sample triangle | Candidate pair | Exact raw pair | Old surviving face | Normal angle | Max vertex-plane separation | Projected intersection |
|---|---|---|---|---:|---:|---:|
| 4207 | 4207 / 9590 | 4207 / 9571 | 4208 | 0.000188129° | 1.651914 µm | 0.000015529660 m² |
| 9942 | 4399 / 9962 | 4395 / 9942 | 10004 | 0.000507259° | 4.539314 µm | 0.000022626630 m² |

For the first pair, normals are approximately
`(0.000001683095,0.707104719845,0.707108842520)` and
`(-0.000000057421,0.707106688569,0.707106873804)`.
For the second they are
`(0.000000057421,0.707106791477,0.707106770896)` and
`(0.000006732516,0.707110903918,0.707102658399)`.
The added detections thus reflect restored raw geometry, not newly moved or invented
surfaces. The removed old-only LOD0 sample is raw triangle **11035** at
`(0.6469744966,3.0319921131,-1.3622867222)`; old faces 5042/11120 become one candidate
interior hit, 5037. Square LOD1 and LOD2 lose 17 and two old-only sampled multiple hits
respectively, with no new-only ones. Triangle-roof sample sets are unchanged.

### All three tiny faces are exactly inherited zero-area triangles

The three candidates are **exactly zero area**, not merely near the previous threshold:
each repeats one position. All positions, winding and full per-vertex attribute tuples
match an old repaired face exactly. None is an exact triangle in raw.

| Node | Candidate triangle | Exact old triangle | Cross-product length / area | Nonzero edge length |
|---|---:|---:|---:|---:|
| roof_LOD1 | 1082 | 1083 | 0 / 0 | 0.040007969 m |
| roof_LOD2 | 593 | 592 | 0 / 0 | 0.040007969 m |
| roof-triangle | 8363 | 8363 | 0 / 0 | 0.112023834 m |

The square LOD1/2 triangles both repeat
`(1.498400092125,3.084852695465,-1.415147066116)`, with their other vertex at
`(1.499199986458,3.113137006760,-1.386862874031)`.
The triangle roof repeats `(-1.500265717506,0.078768596053,0.951264262199)`, with its
other vertex at `(-1.502505660057,0.005446501542,0.866599082947)`.
No pruning was performed. These inherited zero-area render faces do not change the
separately preserved collision hulls, and are not a new regression from the axis correction.

### Evidence, guard and limits

[Full diagnosis](../../.artifacts/p7-roofs/metal-residual-r1/analysis/diagnosis.json) records
all 92 measured pairs, all 93 sampled regions, exact/raw/old provenance, the LOD0 transitions
and all three full-attribute matches. The
[compact summary](../../.artifacts/p7-roofs/metal-residual-r1/run/inspection-summary.json)
and [receipt](../../.artifacts/p7-roofs/metal-residual-r1/run/receipt.json) bind the outcome.

Eight adapted guard fixture groups passed before the sole run. Actual preflight was
**9.034 GiB / 39% commit**, no competing job. Six samples record minimum **8.804 GiB /
maximum 40%**. The **60-second** ceiling, 5.5/80 launch, 2/85 owned stop, exact helper
classifier, PID/start identities and durable primary/secondary recording were retained.
All **6,347 pinned path/hash entries** match, including the 158 freeze, raw/old/candidate,
unchanged repair tool, imports/caches/DLLs/preferences and **647 prior roof-evidence files**.
Root's intervening commit `60906edaa42d96c31c116b0c635a5f9a0fa127f6` changes only
`docs/REPAIR_BACKLOG.md` and `docs/repair-tasks/RECOVERY.md` relative to the prior grant.
All 25 staged destination blobs still match the retained patch; current index SHA-256 is
`A2422E94C0B09CB5277CD3E4BC42AE0E79AB216B501BC9A598795F2838E42BEC`.
No Git command or index write occurred.

Runner SHA-256: `DE26C224D38E24CB42105B8244A0187C73A5A3085C2FB4FF2A508A8326179F49`.
Guard SHA-256: `3D24223DFECC52B060748BC3A33165A6B9742CA2EB1A4CBD199A49C7D8284057`.
Candidate remains `2BD9275628251C9A8C27195B8E2DAC91A7090639B9844876CEC71F39BF33E8F0`.

Only fresh diagnostic scratch files and this P7 report changed. No repair, regeneration,
installation, source/asset mutation, Blender, fracture, build, import, render or new job
followed. The recommendation is bounded to the classified evidence; unsampled geometry,
engine appearance, other grades and broader N10 acceptance remain unproven.

---

## Scratch metal roof candidate repaired once; backing restored in bounded geometry inspection

**17 October, 16:27:45 local:** the granted `metal-clip-r1` Python process completed one
explicit-destination call to the unchanged corrected repair tool, then inspected the
result. Native exit **0**, guarded duration **5.461 seconds**, repair-call time **1.247 s**;
the runner's repair-plus-inspection clock was **5.054 s**. Stderr is empty, receipt and
finalization agree, and no guard error/stop or process termination occurred. No owned
process remained. **The slot is released. The candidate is not installed.**

Candidate: [roof-source-metal.glb](../../.artifacts/p7-roofs/metal-clip-r1/candidate/roof-source-metal.glb)
— **2,210,956 bytes**, SHA-256
`2BD9275628251C9A8C27195B8E2DAC91A7090639B9844876CEC71F39BF33E8F0`.
It contains only the existing six metal roof render nodes and two hulls. The all-building-pack
CLI was never invoked, and no old repaired asset was regenerated for comparison.

### Geometry and coverage results

All six render nodes passed finite attribute, accessor-range, declared min/max, index,
primitive and material-reference validation. Attribute channel names are retained. Scene,
node hierarchy/names/transforms, mesh/primitive metadata and materials match raw and old
repaired input exactly. Both square and triangular hulls retain exact attribute values
and indices, with 12 and 8 triangles respectively. Every render node's live bounds match
raw exactly (maximum observed coordinate-bound change **0 m**). This does not substitute
for checking textures or materials in the engine.

The representative backing point `(0.5934874,1.2601899,0.3953736)` is covered by raw
triangle **11378**, absent from the old repaired subset, and covered by candidate triangle
**11437**. The formerly misprojected pair is raw 4177/11378. Across **4,608 deterministic
surface probes** (768 per render node), the candidate had **zero missing samples**. The old
subset missed three square LOD0 samples; all three are covered now. No sampled point
covered by the old subset became missing in the candidate.

| Render node | Raw triangles | Old repaired | Candidate | Old/new missing samples | Candidate multiple-interior samples |
|---|---:|---:|---:|---:|---:|
| roof | 11,500 | 11,639 | 11,564 | 3 / 0 | 12 |
| roof_LOD1 | 2,812 | 2,840 | 2,847 | 0 / 0 | 15 |
| roof_LOD2 | 1,264 | 1,338 | 1,317 | 0 / 0 | 19 |
| roof-triangle | 8,628 | 8,734 | 8,734 | 0 / 0 | 8 |
| roof-triangle_LOD1 | 2,108 | 2,131 | 2,131 | 0 / 0 | 26 |
| roof-triangle_LOD2 | 948 | 981 | 981 | 0 / 0 | 13 |

The corrected repair reports **480 clipped input triangles** (old report: 456), with no
exact same-winding duplicate removal and no foundation changes. Counts describe clipping
work, not the number of holes repaired. Triangle-roof counts match the old subset; this
inspection does not assert byte-identical render buffers from equal counts alone.

### Remaining overlap/degeneracy limits

The candidate still has **93 sampled locations with multiple same-facing triangle
interiors within the numerical tolerance**, compared with 111 in the old repaired subset
and 180 in raw. Square LOD0 alone has 12 versus the old subset's 11. This is not a claim
that every overlap is eliminated or that each sampled overlap produces visible z-fighting.
The point/triangle examples are retained in the inspection JSON, including the first
square LOD0 example at `(-1.1202235005,0.4021588974,1.3806837519)` with candidate faces
4207/9590. Plane tolerance is **10 micrometres**, minimum same-facing normal dot is
**0.99999**, and barycentric tolerance is **1e-7**.

Three **near-zero-area triangles** remain by cross-product length `<1e-10`: one each in
square LOD1, square LOD2 and triangular LOD0. The old repaired subset has one in each of
those nodes too; raw has zero. Equal counts do not prove they are identical faces. No
additional pruning or repair was attempted. No candidate same-winding duplicates were
found by the inspector's 1-micrometre rounded coordinate signature.

Samples use one interior barycentric point `(0.237,0.319,0.444)` in each of 768 evenly
selected nondegenerate raw triangle indices per node. They are **not uniform area samples
or an exhaustive surface-union/watertightness proof**, and small unsampled gaps can remain.
The stored summed triangle areas are not union areas. LOD0 square summed area rises from
old **55.793664 m²** to candidate **63.048602 m²**, against raw **63.084633 m²**; this is
consistent with restored backing, but is not a standalone coverage measurement. Visual,
other-grade, hidden-join, gameplay collision and fracture acceptance remain open.

### Guard and preservation

All eight adapted guard fixture groups passed before launch, including the **120.1-second**
timeout probe. The guard differs from validated coplanar-r2 only in command/output labeling
and the granted **120-second** limit. Exact pinned helper classification, ownership/error
handling and runtime competition checks remain unchanged. Actual preflight was
**8.989 GiB / 38% commit**, with no competing job. Six samples show minimum
**8.737 GiB / maximum 39% commit**. No helpers or user processes were stopped.

All **6,295 pinned path/hash entries** match after execution and documentation preparation,
including the **158-input freeze**, raw/old/tool, installed building/gib collections,
**1,723 unique production import settings**, **3,498 imported cache files**, 35 actual
build-output files including the game/dependency DLLs, saved user preferences, and **597
previous roof-evidence files**. The staged 25 destination blobs were checked directly
against the retained patch without Git commands. Root's current HEAD is `484d5886`; index
SHA-256 remains `8FEFA6C47D2F4ED1A21123889F3E94A08B1D808306554C637435B92C655D35DC`.

| Bound input | SHA-256 |
|---|---|
| Preserved raw metal subset | `604B26A8558959A70ED4BC0810912A898F8EE91067715A52A9A2E24DF95729F7` |
| Preserved old repaired subset | `CBD82107E852B7A1E31924098181856DAEF2BE2F2D9D9BDDDD0588A7927A201F` |
| Unchanged corrected repair tool | `BD15CEE2778C8A15E7F56A28EB6392056F74B938342E1C15B4101F157CA09F89` |
| Scratch repair/inspection runner | `4F807B3C94EF9FE25C3D67100B3553C4E208B89A9B52F51A8566770AE7EBC86F` |
| Guard | `92BD4AA5D7D17D4840AA198E6D56DF7ABA24C879E89D55CC6C500ABC494F43D1` |

[Full inspection](../../.artifacts/p7-roofs/metal-clip-r1/candidate/inspection.json),
[compact run summary](../../.artifacts/p7-roofs/metal-clip-r1/run/inspection-summary.json),
[guard receipt](../../.artifacts/p7-roofs/metal-clip-r1/run/receipt.json) and the guard diff
retain the actual data. Only fresh scratch files and this P7 report were written. Installed
assets, runtime/source code, the frozen regression, imports, settings, quality, existing LOD
choices, collision/socket declarations, source library, original project and Git index were
not changed. No Blender, fracture, build, import, render or repeated regression ran.

**Next:** root reviews this candidate and the residual overlap/near-zero-area findings
before any installation or further job. The known backing point is restored in this
candidate; N10 and visual acceptance remain open. No further action was taken.

---

## Coplanar correction passes the focused regression; assets remain unchanged

**17 October, 15:21:02 local:** the separately granted `coplanar-r2` execution passed
**all five regression groups**, native exit **0**, in **1.445 seconds** of guarded process
time. This was one Python invocation containing **36 tiny repair calls**: one retained-old
negative control and candidate cases. The largest input was **three triangles / 2,120 bytes**.
No full asset pack was repaired. Stderr is empty; no primary/secondary guard error, stop
or process termination occurred. Receipt and finalization agree. **Slot released.**

### Actual correction evidence

The unchanged focused script invoked the real `repair(source,destination)` implementations
using preserved old bytes and the new tool. For extracted raw triangles **4177/11378**:

| Result | Preserved old tool | Corrected tool |
|---|---:|---:|
| Backing-point coverage at `(0.5934874,1.2601899,0.3953736)` | 0 | 1 |
| Input / output triangles | 2 / 3 | 2 / 2 |
| Reported falsely clipped triangles | 1 | 0 |

The old implementation reproduced the observed defect as the required negative control.
The candidate retained the pair's exact attributes/positions/winding and its area.
Twelve pair variants also passed across order changes, cyclic index changes and reversed
winding. Twenty genuine-overlap variants passed for each dominant axis and exact Y/Z and
X/Y/Z ties, in both input orders and windings: subtraction retained union area and expected
point coverage without duplicate interior coverage. Interpolated UVs, normals, vertex
colors and tangents, plus metadata/node transforms/materials/mode, passed their assertions.
The coincident reversed face survived while a same-facing duplicate was removed. Repeated
repair of the same tiny input produced byte-identical output.

[Regression results](../../.artifacts/p7-roofs/coplanar-r2/check/results.json) — SHA-256
`7D56B9EBF573003D0BF1F097A9F090A8B5A48CE9D6440B924EAC2711194D22F5`.
[Run logs and inspection](../../.artifacts/p7-roofs/coplanar-r2/run/inspection.json) retain
exact source/output hashes, sizes, bounds and receipt details. The production correction
remains the one-expression change from per-face unrounded normal to rounded plane-key
normal when choosing the projection axis. No further repair/test source edits occurred.

### Reviewed guard correction and preservation

The [r2 guard diff](../../.artifacts/p7-roofs/coplanar-r2/guard-source.diff) adds only the
reviewed exact helper classifier, its pinned allowlist/fixtures and fresh output path.
The **same classifier** runs at preflight and during execution. Both executable and complete
command-line strings must match one of root's six tuples with ordinal equality; no trimming,
wildcards, substring matching or blanket Python exclusion is used. Unknown/null/unreadable
Python metadata still blocks. Native Blender/Godot/dotnet/build/game processes block even
with helper-like metadata. Existing owned PID/start-time handling remains unchanged.

All **eight guard fixture groups passed**, including six exact recognized tuples,
same-executable/different-command, different executable, missing metadata, native heavy
processes, exact owned exclusion and reused-PID rejection. The original seven groups and
60-second limit remain. Both actual classification snapshots recognized **22 helper
processes** and found **zero competitors**; this is the observed count at this run, distinct
from root's earlier 24-process snapshot. Helpers were never stopped.

Actual preflight was **7.672 GiB / 43% commit**. Three durable samples recorded minimum
**7.662 GiB / maximum 43%**, within the unchanged 5.5 GiB/80% start and 2 GiB/85% stop rules.
All **2,137 pinned input hashes** match after execution: all 2,090 prior-run inputs, the
root allowlist, all 45 r1 evidence files, current tool/test/guard and added manifests.
This includes the 156 frozen inputs, 1,723 import settings, preserved assets/caches/DLLs,
user configurations, frozen manifest and staged 25-path patch. The index remains
`29022FA0F217B693D99C45E4B4906167377EB6EC3FC18C7CFD46711959EFEC7E`.
No Git command or index write occurred.

| Input | SHA-256 |
|---|---|
| Unchanged corrected repair tool | `BD15CEE2778C8A15E7F56A28EB6392056F74B938342E1C15B4101F157CA09F89` |
| Unchanged regression script | `8F3489074C061FBC36B527D64AE2C83A8FE00E3C9E74521DB077419788F419D3` |
| r2 guard | `B80B0E93E1E3E5F8744C5CF9B9ECFC7F5E4DD6F7A271242A1FAF4AC7D48E4FD7` |
| Root exact-service allowlist | `B1B00FD4E3F4CF40DCB8E3D804AC65E49EFEF92CCD112D9A22228A6F1D36408B` |

### Scope and next step

This validates the narrow repair correction on the extracted defect and bounded synthetic
coverage. It does **not** establish full-pack coverage, visual acceptance or a fix already
installed in the game: the current roof GLBs are unchanged and retain the diagnosed slit.
No asset regeneration/replacement, Blender, Godot, build, import, capture, new LOD or
collision/socket work occurred. The next bounded step for root's review is a separate
candidate repair of the preserved raw metal roof source, followed by geometry/coverage
verification before any installation or render. No such run is part of this grant.
N10 remains open; the inherited apex profile and P8's exit-warning investigation are separate.

---

## Coplanar projection correction prepared; regression refused before launch

**17 October, 15:12:34 local:** the one granted check attempt stopped at preflight.
Available memory was **8.147 GiB / 41% commit**, but the adapted guard matched **26
existing Python processes**. The proposed regression did **not** launch, has no native
exit code or result, and its `check/` output directory does not exist. Wrapper exit was 1.
This is a competing-process classification refusal, not a regression failure or test pass.
No retry, process termination, filter exemption or delayed launch followed. **Slot released.**

### Narrow change and prepared check

`tools/repair_building_glbs.py` changes one expression: the dropped projection axis is
chosen from the group's rounded `key` normal instead of each face's unrounded normal.
`max(range(3), ...)` retains its deterministic first-axis tie break. Plane grouping,
winding handling, subtraction, attribute interpolation and overlap tolerances are unchanged.
The [two-line diff](../../.artifacts/p7-roofs/coplanar-r1/repair-tool.diff) includes the
explanatory comment. No asset regeneration or production asset replacement occurred.

The old tool's exact bytes were preserved **before** editing in
`coplanar-r1/repair_building_glbs.before.py`. The new stdlib-only
`tools/check_coplanar_repair.py` is syntax-parsed but **unexecuted**. It is designed to
call the actual old/new `repair(source,destination)` on tiny GLBs of at most three
triangles, never on a full pack. Its five intended groups cover:

- Extracted raw triangles 4177/11378 and the representative backing point; retained old
  implementation must reproduce the missing coverage, while the candidate must retain it.
- Twelve order/cyclic-index/reversed-winding variants of that near-tied actual pair.
- Twenty genuine-overlap variants across all dominant axes and exact Y/Z and X/Y/Z ties,
  checking union area, interior/outside coverage, winding and interpolated UV/normal/color/tangent.
- Coincident opposite-winding faces retained while a same-facing duplicate is removed.
- Byte-identical repeated output. Every tiny repair also checks metadata, node transforms,
  primitive material/mode, attribute keys and preservation of its input file.

These are planned assertions, **not successful validation results**. No build, full test
suite, Blender, Godot, import, capture, full-pack repair or gameplay change was performed.

| File | SHA-256 |
|---|---|
| Preserved old repair tool | `BBEFA37EE862285067D2C897FFD26E431E6B9F56E9C515AA2FCA7F846A2D9AE8` |
| Candidate repair tool | `BD15CEE2778C8A15E7F56A28EB6392056F74B938342E1C15B4101F157CA09F89` |
| Focused regression script | `8F3489074C061FBC36B527D64AE2C83A8FE00E3C9E74521DB077419788F419D3` |
| Adapted guard | `5065EDEBB9C97653F9D9B33F9028699FDA222D230E65AF8D1E07FE9539B67917` |

### Guard evidence and why it refused

The native-r2 guard was narrowly adapted to the exact Python command, fresh `coplanar-r1`
paths, flat protected-input manifest and a **60-second** limit. The 5.5 GiB/80% launch
bounds, 2 GiB/85% run stops, durable records and PID/start-time-owned stopping remain.
Python/pythonw names were added to the competing-process filter so Python work would
also receive priority. That name-only extension also classifies helper processes as
competitors; it is too broad to distinguish helpers from actual queued work.

All **seven lightweight guard fixture groups passed** before the sole execution attempt,
including the changed 60.1-second timeout probe. Fixtures did not run the regression.
The refused attempt records two memory samples, minimum **8.133 GiB / maximum 41%**,
zero owned processes/stops and no secondary guard errors. All **2,090 pinned input hashes**
match afterward, including all 156 frozen inputs, 1,723 import settings, asset/cache/DLL
inputs, four saved configurations, the retained old tool and the new source/test/guard.

Read-only inspection found **24 of the 26 matched PIDs still present**: 20 Blender MCP
helpers, two FabCatalog browser helpers and two unclassified Python processes. The other
two were no longer present when inspected; their workloads were not established. Helper
classification does not prove idleness. No process was stopped or reclassified to bypass
the refusal. The [receipt](../../.artifacts/p7-roofs/coplanar-r1/run/receipt.json),
[inspection](../../.artifacts/p7-roofs/coplanar-r1/run/inspection.json), fixture validation
and guard diff retain this evidence.

### Preparation and preservation limits

The inherited native-r2 index hash first rejected root's newer documentation checkpoint.
Read-only parsing of Git objects/index verified commit
`2212a1f066b888f8149c8420e6027e7dbb74ef65` changes only `docs/REPAIR_BACKLOG.md` and
`docs/repair-tasks/RECOVERY.md`; all 25 staged destination blobs still match the retained
patch, including its rename (26 distinct old/new path names). The preparatory verifier
initially rejected that rename because it required equal a/b names; it was corrected to
check the destination blob and absence of the old path before any fixture/check execution.
Both preparation stops are retained in `before.json`; no test retry occurred.

The patch remains SHA-256
`64C26E0E8840CF961EE77CCFFB4A3800F84C2AD90A7D2C4ED68A4B8FE89A7E29`;
the refreshed index remains
`29022FA0F217B693D99C45E4B4906167377EB6EC3FC18C7CFD46711959EFEC7E`.
No Git command or index write occurred. Only the authorized repair tool, focused check,
fresh scratch evidence and two owned notes changed. All previous evidence is intact.

**Next:** root reviews the narrow patch, unrun regression and process classification.
A separately granted attempt would first need a reviewed distinction between known helper
processes and active competing work; no automatic exemption or retry is proposed here.
The fix is unvalidated, installed roofs still contain the previously diagnosed slit, and
N10 remains open. P8 retains ownership of the unrelated exit-warning investigation.

---

## Light geometry diagnosis: inherited apex profile and pre-existing backing slits

**17 October, follow-up to native-r2; read-only geometry inspection only.** The visible
apex profile comes from unchanged roof-deck coordinates. At least one representative
bright slope slit corresponds to a backing face already missing in the preserved
pre-removal metal roof. Its raw-export face exists: the existing coplanar repair uses
inconsistent projection axes inside one plane group and falsely subtracts that surface.
This is concrete evidence of an inherited repair defect, not evidence that the authored
metal roof deliberately has an opening there. Removing the gables makes the underside
available to this camera; there is no pre-removal render proving the old on-screen appearance.
No Godot, Blender, build, test, import, capture or repair function was run for this diagnosis.
P8 owns the separate exit-warning investigation.

### Inputs and method

The comparison reads the actual `roof` POSITION/index accessors directly with Python's
standard library, without loading an exporter or repair module:

| Input | SHA-256 | LOD0 node / mesh / POSITION / indices |
|---|---|---|
| Preserved pre-removal `integration-r1/backups/game/assets/building/building_metal.glb` | `1874b79ee030342bd3c4751e9986a8b8c048202189398b2361ba1091592a1488` | node 59 / mesh 59 / 327 / 330 |
| Installed `game/assets/building/building_metal.glb` | `afbfac081121185cd716bab394c7ffaa5e58ef0de0d11d654790a644c89065d5` | node 124 / mesh 136 / 453 / 456 |
| Preserved `metal-source-r1/roof-source-metal.raw.glb` | `604b26a8558959a70ed4bc0810912a898f8ee91067715a52a9a2e24df95729f7` | node `roof` / mesh 0 |

Indices in this section are zero-based. Triangle ordinals refer to consecutive index
triples in the primitive. The raw GLB is the preserved output before the separate
`repair_building_glbs.py` polygon-clipping pass; it is not a newly generated asset.
The complete old/new LOD0 primitive has 12,202 / 11,639 triangles, so an assertion that
only gable triangles disappeared would be inaccurate. Retriangulation changes 5,871 old
and 5,308 new triangle coordinate sets (6,331 match at 1 micrometre rounding).

For bounded camera probes, the documented house poses were reconstructed from
`DemoBases.TwoStorey`: four roofs, x offsets 0/3, y=6, north rotated 180 degrees and
south at z=3. Projection uses the recorded local eye `(-8,7.5,1.5)`, target
`(0,7.2,1.5)`, vertical FOV 75 and 1600x900 viewport. These are numerical geometry
probes, not a new render or full visibility/material comparison. To inspect the old
*deck* without its gables masking it, faces with a vertex below
`h=(y+z-1.5)/sqrt(2) < -0.00001 m` were excluded from that query only. No GLB was edited.
That retains 11,676 old deck triangles; all 11,639 current and 11,500 raw triangles remain.

### Apex/ridge: unchanged authored section, newly exposed view

The old, current and raw deck all contain the same highest-edge coordinate, for example
`(-0.758023918, 3.139764071, -1.358578682)` metres in the roof's local game axes.
For the two opposed house slopes, the high surface edges are at scene z
`1.358578682` and `1.641421318`: **0.282842636 m apart at that top height**, matching
the existing offset of two 20 cm sloping decks. This is the top V profile, not a claim
of a uniform 28 cm gap through the whole deck thickness. The collision slab is unchanged.
Selected apex rays `(800,282)`, `(800,291)`, `(800,305)` encounter no deck face in the
old-deck, raw or current geometry, while `(785,285)` and `(810,285)` encounter the same
edge surfaces within sub-micrometre numerical differences.

Read-only AST comparison against the original project's reference exporter confirms
`deck`, `perimeter`, `sloped`, `eave` and `roof_hull` are identical. `roof` and
`roof_triangle` are identical after excluding only their two old `gable(...)` calls.
The reference file SHA-256 is
`2a8db57e7686115441401a129d04bf4b7788ee49c349529b9fc3e6d74b45f335`.
No original-project file was modified. The visible notch therefore does not warrant
inventing a cap or changing sockets/slab thickness as part of gable removal. Its desired
ridge finish remains a separate visual-contract decision.

### Representative slope slit: existing coplanar projection defect

The source library's `make_buildings.py:281–289` authors metal plates with narrow seams
*over a continuous backing prism* (`-.09` to `-.04` in deck coordinates). It does not
specify the large backing cutout visible at this representative location.

The numerical ray through pixel `(650,447)` intersects raw `roof` **triangle 11378**
at local point approximately **`(0.5934874, 1.2601899, 0.3953736)`**. The triangle is:

- `(1.494200110, 1.577781796, 0.077781647)`
- `(1.494200110, 1.203488946, 0.452074528)`
- `(-1.497799993, 1.203489065, 0.452074528)`

This is the underside backing plane, at `h≈0.11 m`. Neither the preserved old nor the
current roof has a same-facing triangle covering that point on this plane. The old-deck
ray has no triangle hit at all; the current ray reaches only an opposite-facing surface
farther away. Several neighbouring probes still hit identical old/current screw or
plate surfaces; this is not a claim that every bright pixel has the same cause.

`tools/repair_building_glbs.py:160–188` computes a rounded plane key but chooses its
projection axis from each triangle's *unrounded* normal. On a 45-degree roof, tiny
floating-point differences switch the dominant component between Y and Z. Both faces
below enter the same key `(-0.0,-0.70711,-0.70711,-1.17066)`:

| Raw triangle | Unrounded normal (approximately) | Dropped axis / stored 2D coordinates |
|---|---|---|
| 11378 | `(-2.8173e-8,-0.7071068093,-0.7071067530)` | Y / `(x,z)` |
| Earlier 4177 | `(-1.4087e-8,-0.7071067319,-0.7071068305)` | Z / `(x,y)` |

Triangle 4177 has vertices
`(1.494200110,0.454903126,1.200660229)`,
`(-1.497799993,0.080610283,1.574953079)`,
`(-1.497799993,0.454903185,1.200660229)`.
The backing point is **inside its mismatched `(x,y)` polygon when interpreted as `(x,z)`**,
but **outside its actual polygon when both faces use the same projection**. There are
13 earlier faces in this plane group: eight drop Y and five drop Z. The subtraction loop
compares those incompatible polygons without storing/checking their axis. The raw/current
comparison and missing old backing establish an inherited defect in this repair path;
the gable-removal change did not originate this representative opening.

### Smallest warranted next step and preservation

Propose a separate narrowly scoped change that chooses **one deterministic projection
basis for each rounded coplanar group**, then verifies this exact triangle pair and backing
coverage before any asset regeneration. Do not fill the gable, alter roof collision or
add ridge geometry to conceal this defect. Any regenerated candidate still needs separate
bounded asset/hash validation and visual acceptance; this diagnosis does not close N10.
Other bright pixels, grades, LODs, joins and gameplay/fracture coverage remain unproven.

Only this P7 report and its current owned repair note changed. All 156 source-freeze
inputs matched before and after, as did the frozen manifest, the old/raw GLBs, native-r2
PNG, original exporter, library generator, four saved user configurations and Git index.
The index remains `962ae8c301977aa901f9b89e58c2392e7b2e69cc0ece64f556d524dd60a995d6`.
No production/settings/library/original-project file or previous evidence was written.
The light diagnosis is finished; no heavy slot was acquired.

---

## Native metal-square LOD0 image captured and viewed; shutdown warning retained

**17 October, 14:38:48 local:** the newly granted `native-r2` attempt launched once through
the normal renderer and saved the requested **1600×900 PNG**, with **ready=True at 2.01 s**
and native exit **0**. Guarded process time was **7.932 seconds**. No build, import, rerun or
source/art edit occurred. The earlier `native-r1` resource refusal and all 44 evidence
files remain intact.

[Actual capture](../../.artifacts/p7-roofs/native-r2/metal-square-gable-lod0.png) —
SHA-256 `468AA171A4A6B71530C0648BE8FED043F2399DEBFD6722BD3D20A87E926AB37D`,
1,688,185 bytes. PNG chunk CRCs, deflate data, scanline structure and dimensions were
validated, and the original image was viewed with the image tool.

### What this image shows

The front triangular gable opening is visibly open to the sky; no full-height gable panel
closes that opening. Both roof slopes/undersides and their edge trim render above the house
front, with the existing ground HUD present. The ridge meeting line and a small apex notch
remain visible, as do narrow bright slits/patches along both slopes. Those details require
review; this one view cannot determine whether they are intentional authored detail or
a join/detail defect. No geometry was changed to address them.

The pinned house scenario contains four metal square roofs, but this angle mainly examines
the front gable and undersides. Hidden rear joins, other grades, triangular roofs, LOD1/2,
hosted placement, walking/shot collision and fracture behavior are not established by this
capture. It supports the visible removal of gable panels in this specific metal LOD0 view.

### Runtime, camera and warning

The log reports **Vulkan 1.4.351, Forward+, NVIDIA GeForce RTX 2070 SUPER**; this was not a
headless/dummy capture. CaptureIsolation reported isolated input and unsaved preferences.
Requested local camera was `from=-8,7.5,1.5`, `at=0,7.2,1.5`. The runtime capture line
records window/viewport **1600×900**, Windowed mode, Camera position
`(858.7924, 1226.295, 1906.8007)`, full basis and **75° FOV**.
The exact command pins Maximum quality and LOD0. The unchanged Maximum preset uses render
scale 1.0; no separate numeric runtime quality/render-scale telemetry is emitted, so that
setting is evidenced by the command and pinned code/binaries rather than an independent
runtime measurement.

**The logs are not clean:** Godot reports **30 ObjectDB instances leaked at exit**.
There is no error line, and the image had been saved successfully, but this warning remains
unresolved. Its cause has not been established. No verbose rerun or leak fix was authorized
or attempted. The guard's successful exit does not erase this post-capture warning or
establish clean-log/native acceptance.

### Guard and preservation

The r2 guard changes only the screenshot output path from the reviewed r1 guard.
SHA-256: `9A6F4A8200290A985649419F017FB6A6E4CB6D10F92468C22DC2839ED61BAEBB`.
The [diff](../../.artifacts/p7-roofs/native-r2/guard-source.diff), seven passing fixtures and
[preparation record](../../.artifacts/p7-roofs/native-r2/preparation.json) bind that copy.
The normal 2+90-second readiness window and 120-second guard ceiling were preserved.

Actual preflight was **6.312 GiB / 47% commit**, with no competing job. Eight samples show
minimum **5.045 GiB available / maximum 53% commit**, within the unchanged running stop
bounds. Receipt and final snapshot agree; no primary/secondary guard error or stop occurred.

All **2,034 input hashes** still match, including 156 frozen inputs, 19 actual game/dependency
DLLs, 20 imported cache files, four saved user configurations including `graphics.cfg`,
production settings and all previous evidence. The index snapshot was refreshed only for
root's documentation commit `9893cc0fe39eb004b08a521f94ec3f0a1da583b4`; the original staged
25-path patch remained identical. Current whole-index SHA-256 is
`962AE8C301977AA901F9B89E58C2392E7B2E69CC0ECE64F556D524DD60A995D6`.
No matching heavy process remained after the run.

[Inspection, logs and camera evidence](../../.artifacts/p7-roofs/native-r2/capture/inspection.json)
retain the warning and bounded visual observations. **The sole capture slot is released.**
N10 remains open for root's image review, the shutdown warning disposition and broader
visual/gameplay/fracture acceptance.

---

## Native metal-roof capture refused at preflight; no image; slot released

**17 October, 14:17 local:** the single authorized capture attempt stopped **before launching
Godot**. Its actual launch preflight was **5.493 GiB available / 49% commit**, approximately
**6.977 MiB below the required 5.5 GiB floor**. No competing heavy process was present.
The second recorded sample was lower, giving a sampled minimum of **5.484 GiB**; that
minimum is distinct from the launch-preflight value.

The guard recorded `preflight refused; no process launched`, wrapper exit 1, no native
process/exit code, no owned processes, no stopped processes and no secondary errors.
Its primary refusal includes script position and stack, and the receipt/finalization snapshot
agree. **No retry, delayed launch, resource-floor change or process termination followed.**

### Prepared scope and preserved inputs

The requested normal-renderer command was pinned exactly:

`--path game --resolution 1600x900 -- --view build --scene house --quality maximum --lod 0 --from=-8,7.5,1.5 --at=0,7.2,1.5 --shot D:/GameDev/Projects/HollowUniverseABTestB/.artifacts/p7-roofs/native-r1/metal-square-gable-lod0.png`

The fresh [capture guard](../../.artifacts/p7-roofs/native-r1/Run-RoofCapture-r5.ps1) has
SHA-256 `999BDD1FB110740F028AAC67D9546CD6ABAF550BB7F4370A118534BDD645E4D9`.
All seven lightweight fixture groups passed, including the **120-second** external deadline.
The existing game readiness window remains its normal **2 + 90 seconds**. The
[source diff](../../.artifacts/p7-roofs/native-r1/guard-source.diff) and
[preparation manifest](../../.artifacts/p7-roofs/native-r1/preparation.json) record the narrow
capture/path/timeout changes and byte-exact reversal. The preparation note also retains the
in-memory draft proof adjustment made before creating the guard or running fixtures.

The [before snapshot](../../.artifacts/p7-roofs/native-r1/before.json) pins all **19 actual
game/dependency DLLs** from the built game directory, 20 imported scene/checksum cache files,
156 frozen inputs, source/import settings, engine binaries, four user configuration files,
the staged index/checkpoint and previous evidence. Actual `HollowUniverse.dll` SHA-256 is
`8513A23DC2300AE397AF64CC491E5CF121CB61D24283047BE8ACE856C01E90E2`.

All **1,989 recorded input hashes** still match after refusal, including the user's
`graphics.cfg`, the other three saved configuration files, imported roof resources and
the index. No build, import, source/art/settings edit or Git/index operation occurred.

### Result and limitation

[Inspection and refusal evidence](../../.artifacts/p7-roofs/native-r1/capture/inspection.json)
confirm that the target PNG is absent and no stdout/stderr logs were created by a native
process. **There is no image to view and no visual observation to report.** Actual viewport
resolution, camera pose, quality, readiness and appearance were not measured in this slot.
The requested scope remains only metal square roofs at LOD0, without claims about other
grades/shapes or gameplay.

**The exclusive native slot is released.** P7 is stopped after the resource refusal; any
future capture requires a new coordinator disposition and fresh evidence. Earlier successful
roof/regression tests and the ten-pack incremental import remain unchanged. N10 visual and
normal gameplay/fracture acceptance remain open.

---

## Incremental Godot import passed for all ten roof/debris packs; slot released

**17 October, 13:53:36 local:** the single granted normal incremental import completed in
**11.902 seconds**, exit **0**, with no primary error, secondary error, guard stop, competing
process, warning/error line or stderr output. Godot identified itself as
`4.7.2.stable.mono.official.ed1daf0bf`. The existing console launcher ran:

`--path game --headless --editor --import`

The [stdout log](../../.artifacts/p7-roofs/integration-r1/guard-roof-import-r4/stdout.log)
records exactly ten scene imports: the twig, wood, stone, metal and armoured building packs
and their five corresponding gib packs. Each scene import completed. The post-reimport
operations are recorded separately; they do not represent ten additional asset imports.

### Guard, resource and pre-import evidence

The new [Run-RoofImport-r4.ps1](../../.artifacts/p7-roofs/integration-r1/Run-RoofImport-r4.ps1)
has SHA-256 `0ADE8E80DA7BDB78D4C47ECB57666D1032433BCD2779447F02923816386C2703`.
Its [exact diff](../../.artifacts/p7-roofs/integration-r1/guard-import-r4-source.diff) changes
only the executable/arguments, import labels/directories and the established **60-second**
import limit, including its 60.1-second timeout fixture. Reversing those replacements
reproduces r3 byte-for-byte. All seven lightweight fixture groups passed before launch.

The [before snapshot](../../.artifacts/p7-roofs/integration-r1/import-r4-before.json) records
all ten GLB hashes, import settings/UIDs, old destination resources/MD5 records, all 1,723
import-settings hashes, the 156 frozen inputs, engine binaries, previous evidence, and
3,498 imported-cache file metadata records. Before import, all ten cache source MD5s
differed from their current integrated GLBs, as expected.

The root-owned 25-path staged checkpoint matched SHA-256
`64C26E0E8840CF961EE77CCFFB4A3800F84C2AD90A7D2C4ED68A4B8FE89A7E29`.
Read-only parsing of the existing binary index matched all 25 checkpoint object IDs; no Git
command or index write was used. Whole-index SHA-256 before and after is
`95E1E04C69D519F044B7C693F5B258024767BD41A72EC7A5D2AE8FB55E16C210`.
The exact index hash is retained in the inspection and before snapshot.

Fresh launch preflight was **7.761 GiB / 39% commit**, with no competing job.
Eleven durably flushed samples recorded minimum **6.279 GiB available**, maximum
**45% commit**. These are sampled bounds. The 5.5 GiB / 80% start and 2 GiB / 85% owned-job
stop rules remained unchanged; the import finished within its 60-second ceiling.

### Actual imported-cache and preservation results

The [inspection](../../.artifacts/p7-roofs/integration-r1/guard-roof-import-r4/inspection.json)
verifies all ten target resources individually:

- Each recorded source MD5 now equals the corresponding current GLB's MD5.
- Each destination MD5 equals the actual newly written scene-cache bytes.
- All ten scene caches are nonempty RSCC resources with changed hashes and newer timestamps.
- The imported-cache inventory remains 3,498 files; exactly **ten scene caches and ten MD5
  records** changed. No cache file was added or removed.
- All ten UIDs, all 1,723 import-settings files, 156 frozen inputs, 228 guard inputs, original
  source/art, index checkpoint and previous failure/test evidence remain unchanged.
- Guard receipt and finalization snapshot agree, output hashes match, and no matching heavy
  process remained on read-only inspection.

Raw stdout includes ANSI color sequences. An initial raw filename extraction therefore
returned zero matches; read-only ANSI normalization then identified the ten imports above.
Original log bytes are preserved. This inspection adjustment did not rerun the importer.

The current validation paragraph in `docs/REPAIR_BUILDING.md` now records the already-passed
4/4 roof and 20/20 regression tests and this import result. Only that validation text was
updated there; historical guard failures remain linked and retained.

**The sole import slot is released.** No forced/full reimport, cache deletion, build, new LOD
work, source/art edit, render, gameplay session, retry or Git/index operation was performed.
This establishes successful incremental import and checksum preservation, **not visual,
normal placement/walking/shot or fracture acceptance**. N10 remains open for that later stage.

---

## Remaining building regression batch passed 20/20; slot released

**17 October, 13:29:34 local:** the one granted remaining batch completed successfully using
the same compiled test DLL. **20/20 tests passed in 15.9 seconds**; the guarded process stage
took **16.800 seconds**, exited **0**, and recorded no primary error, secondary error,
guard stop or competing process. Stderr is empty.

| Suite | Passed |
| --- | ---: |
| BuildingNetTests | 3 |
| BuildingPersistenceTests | 2 |
| BuildingTests | 14 |
| GibTests | 1 |
| **Total** | **20** |

The actual command was:

`dotnet tests/Hollow.Tests/bin/Debug/net10.0/Hollow.Tests.dll BuildingTests BuildingPersistenceTests BuildingNetTests GibTests`

### Exact guard copy and preflight

[Run-FocusedRoofCheck-r3.ps1](../../.artifacts/p7-roofs/integration-r1/Run-FocusedRoofCheck-r3.ps1)
has SHA-256 `AD912F2DB64DEB656C4589356E356448FE6DCA3427AE311C99C057013CE3F83B`.
The [source diff](../../.artifacts/p7-roofs/integration-r1/guard-r3-source.diff) contains only
the approved test-filter replacement and three directory-label occurrences. Reversing those
four string replacements reproduces the r2 guard bytes exactly; functions and limits are
otherwise unchanged. Its inherited function/Kind names remain unchanged; the actual Command
field records this four-suite regression batch.

The existing r1-operation probe was copied with a matching hash, and all
[seven r3 fixture groups passed](../../.artifacts/p7-roofs/integration-r1/guard-r3-fixtures/validation.json)
before execution. Guard bytes were pinned after fixtures. Fresh launch preflight was
**7.758 GiB available / 40% commit**, with no competing heavy process. The 300-second ceiling,
5.5 GiB / 80% launch limits, 2 GiB / 85% owned-job stop limits and user-game priority were retained.

### Durable result and preservation

The [receipt](../../.artifacts/p7-roofs/integration-r1/guard-building-regression-r3/receipt.json)
and finalization snapshot agree exactly. Fifteen durably flushed resource samples report
**minimum 6.969 GiB available / maximum 42% commit**. Those are sampled bounds, not peak
measurements. The process-exit event was saved before finalization. The
[stdout](../../.artifacts/p7-roofs/integration-r1/guard-building-regression-r3/stdout.log) has
twenty matching RUN/ok pairs and the complete 20/20 summary, with no FAIL line.

The [inspection](../../.artifacts/p7-roofs/integration-r1/guard-building-regression-r3/inspection.json)
rechecks **all 228 input hashes**, output hashes, sample counts/extrema, the seven fixture
groups and the exact permitted source diff. All prior 144 frozen inputs, the other 53
protected files, installed ten packs, two integration source targets, expected binaries and
old freeze remain unchanged. Previous r1/build failure evidence is intact. Read-only
inspection found no remaining matching heavy process.

| Source/binary | Verified SHA-256 |
| --- | --- |
| PieceShapes.cs | `7FFD62DE51CA4D06089AB87F6C9247814C843CA603C154827615598240B63DAA` |
| RoofPanelTests.cs | `B1749B51CFA3BAF0E90592B85EEC1CF7E53BB2B0415C1B1A045D3D8CD44B587D` |
| Hollow.Tests.dll | `857D5E9CB5BD71A3CCE70F0582CA788B087ECB6594D1F568F1F4B764DB331B03` |
| Hollow.Simulation.dll | `3D96B112C3274C482CBBAEB44AF214E1B771970A8699B4758CD5EE3EF5A73634` |

The earlier dedicated r2 run separately passed **4/4 RoofPanelTests** in 0.5 seconds, with
exit 0, no guard errors, three samples (minimum 8.122 GiB / maximum 39%) and 228 unchanged
inputs: [r2 inspection](../../.artifacts/p7-roofs/integration-r1/guard-roof-only-r2/inspection.json).
The five architecture checks and one building-fit check had individually passed in the
earlier incomplete r1 attempt; its wrapper failure remains recorded honestly below.
Those passed roof/architecture/fit checks were not rerun in r3.

**The sole heavy slot is released.** No rebuild, production implementation edit, import,
render, extra test batch, retry or Git/index operation occurred. Focused regression results
are now recorded; Godot import and visual/placement/fracture acceptance still require their
own granted stage. **N10 remains open pending that acceptance and root's checkpoint.**

---

## Guard r2 prepared and fixture-validated; no execution grant used

**17 October — preparation only:** a new scratch revision,
[Run-FocusedRoofCheck-r2.ps1](../../.artifacts/p7-roofs/integration-r1/Run-FocusedRoofCheck-r2.ps1),
is ready for root review. SHA-256:
`D29821FB062AB21DD4037198AA6029464B96279930D596EF2A731E4DE54017EF`.
The r1 script and all failed evidence are preserved unchanged. No build, game test, import,
render or game process was launched in this preparation.

### Reproduced failure mechanisms

A lightweight in-process fixture kept a writable stdout file open with read sharing.
The r1 `File.ReadAllText` operation threw a sharing violation; the new explicit
`FileShare.ReadWrite | FileShare.Delete` reader succeeded while that same writer remained
open, and saw a subsequently appended synthetic FAIL line. This operation occurs after r1's
first running sample and before its missing next receipt update. It is a **reproduced earlier
failure mechanism**, not proof of r1's actual cause: the original primary exception was lost.

The r1 ordered-dictionary `Measure-Object AvailableGiB` aggregation also reproduced its
strict-mode exception independently. Raw probe errors, source positions and stacks are in
[r1-operation-probes.json](../../.artifacts/p7-roofs/integration-r1/guard-r2-fixtures/r1-operation-probes.json).

### Guard changes and actual fixture coverage

The new guard normalizes samples to fixed objects and aggregates their numeric values in
explicit loops; zero samples yield null extrema. Each sample is appended and flushed
immediately. Primary exceptions retain type/message, HResult, script line/position and stack
in a dedicated file, event and receipt **before** the stop/cleanup callback. Exit codes and
guard-stop reasons are persisted as soon as known. Independent finalization stages record
secondary errors separately without replacing the primary outcome, error or exit code; a
separate finalization snapshot survives a final receipt-save failure.

All **seven fixture groups passed**, exercising the actual receipt and finalization functions:

| Fixture group | Verified result |
| --- | --- |
| Zero samples | Count 0; null extrema; successful final receipt |
| One sample | Count 1; 8 GiB / 39%; two PID/start records round-trip |
| Three samples | Count 3; minimum 6 GiB / maximum 41%; two PID/start records round-trip |
| Primary plus five secondary failures | Primary position/stack durable before stop; primary outcome preserved despite failures in final sample, summary, input check, output hashes and final receipt save |
| Exit plus secondary failure | Exit code 23 and failed process outcome preserved |
| Guard stop followed by exit | Stop reason saved immediately and retained after exit code 137 |
| Live stdout and first-sample path | Sharing violation reproduced, new reader succeeds, appended FAIL detected; empty/null/nonempty competing arrays and memory/commit/time guards work under strict mode; subsequent Save-Receipt/finalization succeeds |

The [fixture validation](../../.artifacts/p7-roofs/integration-r1/guard-r2-fixtures/validation.json)
and [preparation manifest](../../.artifacts/p7-roofs/integration-r1/guard-r2-preparation.json)
bind these results to the exact script hash and individual evidence files. These are guard
fixtures, not execution of RoofPanelTests or evidence of game behavior. Real process launch,
ownership and native-test behavior remain unexecuted for this revision.

### Proposed next command — not executed

`dotnet tests/Hollow.Tests/bin/Debug/net10.0/Hollow.Tests.dll RoofPanelTests`

After a separate grant, the prepared guard's `-Mode Execute` path would run only that command
in fresh `integration-r1/guard-roof-only-r2/` evidence. That directory is currently absent.
It retains the **300-second ceiling**, **>=5.5 GiB / <=80%** launch requirements, and the
**2 GiB / 85%** owned-job stop limits; competing user/game/build work stops only verified
owned processes. Passed architecture/building-fit checks are not included in this proposed run.

All previous 144 frozen inputs, the other 53 protected files, installed source/art, expected
test/simulation binaries and old freeze still match. R1 and earlier build-failure evidence
also match. No production implementation or Git/index changes were made. **Preparation is
complete; P7 is idle awaiting root disposition, with no heavy execution grant. N10 remains open.**

---

## Focused-test attempt incomplete; guard finalization failed; slot released

**17 October, 13:03:47 local:** the separately granted test attempt used the existing compiled
DLL; no rebuild occurred. The persisted guard is
[Run-FocusedRoofCheck.ps1](../../.artifacts/p7-roofs/integration-r1/Run-FocusedRoofCheck.ps1),
SHA-256 `0292CE183C1C85E0E3B75E4D85AB60153B095366C303D594A7DE69969BACBF73`.

Before launch, a representative final receipt with two PID/start records, input maps,
exit/failure/secondary-error fields and output metadata successfully serialized and round
tripped. That [validation](../../.artifacts/p7-roofs/integration-r1/guard-tests-r1/serialization-validation.json)
launched no heavy process. It verified the previous dictionary-key correction but did not
exercise live resource-summary aggregation.

The single attempt ran exactly:

`dotnet tests/Hollow.Tests/bin/Debug/net10.0/Hollow.Tests.dll RoofPanelTests BuildingTests BuildingFitTests BuildingPersistenceTests BuildingNetTests ArchitectureTests GibTests`

Fresh preflight was **8.015 GiB / 39% commit**, with no competing heavy process. Source,
installed assets, test/simulation binaries, all prior 144 frozen inputs, the other 53 protected
files and the previous build-failure evidence matched before launch.

### Actual outcome

The [stdout log](../../.artifacts/p7-roofs/integration-r1/guard-tests-r1/stdout.log) records
**six passing tests**: all five ArchitectureTests and BuildingFitTests.EveryPieceFillsItsSlot
(which inspected 298 building meshes). It then records the start of
BuildingNetTests.PlacingGoesThroughTheServer without a result. There is no FAIL line and no
completed batch summary. **RoofPanelTests were not reached.** This incomplete attempt does
not establish that the network test failed.

The wrapper exited **1** with a finalization exception at guard line 248:
`Measure-Object AvailableGiB -Minimum` rejected the property argument while aggregating the
ordered-dictionary sample objects under StrictMode. The exception prevented final receipt
persistence. Any preceding in-memory error/stop reason and the test-process exit code were
not retained, so their values are **unknown**, not reconstructed. The original receipt still
says `running` and is preserved as evidence.

The corrected per-sample flushing did preserve
[three resource records](../../.artifacts/p7-roofs/integration-r1/guard-tests-r1/resources.jsonl):
preflight, running and finished, spanning **0.733 seconds**. Their minimum observed available
memory was **7.909 GiB**, maximum commit **39%**. These are observations from the incomplete
attempt, not full-suite resource coverage. Stderr is empty. Read-only inspection found no
remaining matching heavy process.

Following the first-failure rule, there was **no retry, guard-source repair, rebuild, import,
render or additional test invocation**. A separate
[post-run inspection](../../.artifacts/p7-roofs/integration-r1/guard-tests-r1/post-run-inspection.json)
records the known results, durable samples and exact evidence hashes without rewriting either
failed attempt's receipt. All 144 prior inputs, the other 53 protected paths, installed ten
packs, two integration source targets and expected binaries still match; previous build
failure evidence is unchanged.

**The sole test slot is released; P7 is stopped pending coordinator disposition.** The
building-fit and architecture checks passed in this attempt, but the focused batch and N10
native/visual acceptance remain incomplete. No production implementation, freeze or Git/index
change was made in this follow-up. Root owns any further execution grant and checkpoint.

---

## Integration installed; guard receipt failed after build; slot released

**17 October, 12:54–12:56 local:** all five reviewed building packs and matching fragment
packs are installed with the minimal simulation collision change. The roof branch no longer
adds gable collision; its unused helper/constant were removed. The private `Roof` method is
byte-identical. Footprints, Top poses, placement/support/catalogue and save/wire behavior are
unchanged. No parked broader socket/geometry changes were restored.

Four new `RoofPanelTests` compiled: real Jolt rays through square/triangle side openings and
against solid decks; adjacent supported square-roof placement and occupied-position refusal;
legacy square Top poses and absent triangle apex socket; installed hull/LOD agreement across
all five grades. **None of these or the granted existing focused suites ran in this slot.**

### Integration evidence

- [Before manifest](../../.artifacts/p7-roofs/integration-r1/before.json): ten final candidate
  hashes, twenty verified stage receipt chains, and backups of eleven existing targets plus
  ten import settings.
- [Promotion journal](../../.artifacts/p7-roofs/integration-r1/promotion.json): twelve completed
  writes (ten packs, collision source, new test), after a fresh no-heavy-process check.
- [After manifest](../../.artifacts/p7-roofs/integration-r1/after.json): exactly ten of 63
  protected paths changed; the other 53, all import/material settings and all 144 prior frozen
  inputs remained unchanged. The old freeze manifest was preserved.
- [Collision diff](../../.artifacts/p7-roofs/integration-r1/collision.diff) and
  [post-run inspection](../../.artifacts/p7-roofs/integration-r1/guard-build/post-run-inspection.json):
  exact source/binary hashes and repeated preservation checks after the build.

A preparation assertion initially assumed CRLF; it stopped before any scratch-source or
production write. Inspection confirmed the existing LF file and unchanged hash, and preparation
was corrected to preserve those bytes. No build, asset-generation or promotion retry occurred.
The [preparation note](../../.artifacts/p7-roofs/integration-r1/preparation-note.json) records this.

### Build result and validation stop

Exactly one build was launched:

`dotnet build HollowUniverse.sln --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false -nodeReuse:false -v:minimal`

The launch receipt records **12:54:00 local**, **8.276 GiB / 39% commit**, no competing heavy
process, a 180-second ceiling and 2 GiB / 85% stop limits. Retained
[stdout](../../.artifacts/p7-roofs/integration-r1/guard-build/stdout.log) reports
**Build succeeded; 0 warnings; 0 errors; 37.55 seconds**. Stderr is empty.

**The wrapper failed while serializing its final receipt.** Its
`OwnedProcessIdentities` Hashtable used integer process-ID keys, whereas `ConvertTo-Json`
requires string keys. The wrapper exited **1**. The preserved original receipt still says
`running`; it has not been rewritten to imply a successfully sealed run. Only the initial
memory sample was saved. In-memory samples and final guard/exit fields were lost, so there
is no claim of full resource coverage, sampled minimum/maximum or separately recorded dotnet
exit code. The post-run inspection records known facts without fabricating a successful receipt.

Following the failure rule, **the focused DLL test batch was not launched**. No retry, guard
rerun, additional build, Godot import/render/screenshot, hosted run or unrelated fix followed.
Read-only inspection found no remaining heavy process; a separate final observation was
**8.690 GiB / 36% commit**, not a measurement of the build window.

| Installed/built file | SHA-256 |
| --- | --- |
| PieceShapes.cs | `7FFD62DE51CA4D06089AB87F6C9247814C843CA603C154827615598240B63DAA` |
| RoofPanelTests.cs | `B1749B51CFA3BAF0E90592B85EEC1CF7E53BB2B0415C1B1A045D3D8CD44B587D` |
| Hollow.Tests.dll | `857D5E9CB5BD71A3CCE70F0582CA788B087ECB6594D1F568F1F4B764DB331B03` |
| Hollow.Simulation.dll | `3D96B112C3274C482CBBAEB44AF214E1B771970A8699B4758CD5EE3EF5A73634` |

**The sole heavy slot is released; P7 is stopped pending coordinator disposition. N10 remains
open.** Matching source/art is integrated and compiler output is successful, but focused
tests and Godot/visual/fracture acceptance remain. Root owns a new freeze/checkpoint and any
new execution grant. The original project/library and Git index were not edited or operated
on. Historical staging and twig diagnostic evidence below remain intact. Retained obsolete
mesh data still makes these packs larger, with no performance-improvement claim.

---

## Four-grade serial staging complete: 16/16 stages passed; slot released

**17 October, 12:33 local:** wood, stone, metal and armoured completed in the granted
order. Each grade ran source generation, building merge, fragment generation and gib merge
once, with successful artifact/provenance inspection before proceeding. All **16 processes
exited 0**, all structural/preservation inspections passed, and there was **no retry or
resource/concurrency stop**. Together with twig, all five grades now have inspected scratch
building/gib candidates. This is staged asset readiness, not installed or visual/gameplay
acceptance.

The first stage's preflight was **12:28:55 local**, the last process finished **12:33:03**:
**248.049 seconds** including intervening inspections, comfortably inside the 30-minute
slot. Guarded process time totaled **87.733 seconds**. All stages used fresh output and
matching guard directories. Every preflight met **>=5.5 GiB, <=80% commit and no competing
heavy job**; sequential start/finish timestamps confirm no overlap. Blender stages kept
their **600-second** ceilings and merges **120 seconds**, plus the **2 GiB / 85%** owned-job
stop rule. Across **91 samples**, minimum available memory was **5.868 GiB**, maximum commit
**46%**; these are sampled bounds, not peak measurements.

### Stage results and evidence

All values below are guarded elapsed seconds; each links to that stage's actual inspection.
Its sibling `receipt.json`, `resources.json`, `stdout.log` and `stderr.log` contain the exact
command, preflight, input/output hashes, resource samples and logs.

| Grade | Source | Building merge | Fragments | Gib merge | Min GiB / max commit |
| --- | ---: | ---: | ---: | ---: | --- |
| Wood | [10.703](../../.artifacts/p7-roofs/guard-wood-source-r1/inspection.json) | [3.592](../../.artifacts/p7-roofs/guard-wood-merge-r1/inspection.json) | [8.347](../../.artifacts/p7-roofs/guard-wood-gibs-r1/inspection.json) | [1.215](../../.artifacts/p7-roofs/guard-wood-gibs-merge-r1/inspection.json) | 5.868 / 46% |
| Stone | [6.278](../../.artifacts/p7-roofs/guard-stone-source-r1/inspection.json) | [2.391](../../.artifacts/p7-roofs/guard-stone-merge-r1/inspection.json) | [7.153](../../.artifacts/p7-roofs/guard-stone-gibs-r1/inspection.json) | [1.198](../../.artifacts/p7-roofs/guard-stone-gibs-merge-r1/inspection.json) | 6.292 / 45% |
| Metal | [9.550](../../.artifacts/p7-roofs/guard-metal-source-r1/inspection.json) | [3.873](../../.artifacts/p7-roofs/guard-metal-merge-r1/inspection.json) | [9.489](../../.artifacts/p7-roofs/guard-metal-gibs-r1/inspection.json) | [1.213](../../.artifacts/p7-roofs/guard-metal-gibs-merge-r1/inspection.json) | 6.256 / 45% |
| Armoured | [9.564](../../.artifacts/p7-roofs/guard-armoured-source-r1/inspection.json) | [3.608](../../.artifacts/p7-roofs/guard-armoured-merge-r1/inspection.json) | [8.346](../../.artifacts/p7-roofs/guard-armoured-gibs-r1/inspection.json) | [1.213](../../.artifacts/p7-roofs/guard-armoured-gibs-merge-r1/inspection.json) | 6.399 / 44% |

The [batch summary](../../.artifacts/p7-roofs/guard-armoured-gibs-merge-r1/batch-summary.json)
records **all 16 actual output hashes/sizes**, exact commands, receipt paths, geometry
measurements and resource totals. No new runner/diagnostic source file was written: the
guard and read-only inspection were executed from the session, with only scratch
receipts/candidates persisted. The existing three tool hashes were checked before every
stage. All direct input hashes, **144 frozen inputs** and **63 protected production files**
remained unchanged throughout. No Git/index operation was performed; root's staged patch
was not independently re-audited here. The only stderr messages were the four source
exporters' existing `Material.use_nodes` deprecation warning; other stage stderr was empty.

### Geometry and preservation findings

Each source contains exactly **six roof render LOD meshes and two 20 cm slab hulls**, with
identity roots, the existing grade material name and original deterministic seeds. The
full-height gable hulls/panels are absent. All final fragment packs contain the two expected
roof groups with **nine fragments each**, retaining the existing grade recipes and seeds.
Positions/attributes/indices are finite/valid, and node transforms/material bindings passed
inspection. Actual source and fragment slope bands remain at approximately **0–0.20 m**;
the largest fragment upper band is wood triangle's **0.200407 m**, not a full-height panel.

Every building/gib merge preserves its original BIN prefix, mesh/accessor/view records,
materials, unrelated nodes and hierarchy. Replacement payloads and transforms match the
partial candidate exactly after index/offset remapping. Building packs preserve **124
unrelated nodes** for wood/metal/armoured and **122** for stone. Each gib pack preserves
**18 unrelated groups / 180 nodes**. Twelve obsolete roof mesh records remain in each
building pack and eighteen in each gib pack without live node references; no optimizer or
compaction ran.

| Grade | Selected LOD square / triangle | Triangles after gather/limit | Actual exported fragment triangles | Largest AABB excess from selected original LOD |
| --- | --- | --- | --- | --- |
| Wood | 2 / 2 | 841 / 714 | 1,827 / 1,112 | 0.170 mm |
| Stone | 2 / 1 | 273 / 420 | 507 / 652 | 0.000437 mm |
| Metal | 2 / 2 | 659 / 500 | 878 / 841 | 0.000746 mm |
| Armoured | 2 / 2 | 653 / 498 | 832 / 723 | 0.000577 mm |

These preserve the catalogue's **500-triangle source target** and existing limiter behavior;
the target is not a final-fragment triangle ceiling. Receipts separately retain source,
post-limit, pre-export and actual exported counts; differing pre-export/export totals are
not silently conflated. Small measured bounds differences are diagnostic observations,
not an invented acceptance threshold. Twig's original flag and measured limiter explanation
remain intact. No grade's geometry, budgets, caps or transforms were rewritten to force a
comparison to pass.

### Final scratch candidates

| Grade / pack | Bytes | Growth from retained old data | Actual SHA-256 |
| --- | ---: | ---: | --- |
| [Wood building](../../.artifacts/p7-roofs/wood-merge-r1/building_wood.glb) | 21,415,892 | 16.955% | `CCF7D28FDCC577F21212E6936394CE692905B33E831A9201DA41C1DF513E5D74` |
| [Wood gibs](../../.artifacts/p7-roofs/wood-gibs-merge-r1/building-wood.glb) | 2,542,580 | 8.782% | `272D2B2DCE318BA15CCC399F00A9C0EBB28DBA53C77088D369ED54C67FF0CFA3` |
| [Stone building](../../.artifacts/p7-roofs/stone-merge-r1/building_stone.glb) | 13,651,748 | 8.136% | `5849776C003D2267FC5D5BD5AE54FCED4499EBD1AB7D7517528A85C488701C4C` |
| [Stone gibs](../../.artifacts/p7-roofs/stone-gibs-merge-r1/building-stone.glb) | 1,470,844 | 6.490% | `FB4B93547F127DBD106F4D14DD51C68748D89FC2755D411FE5764FA1CEBDF93B` |
| [Metal building](../../.artifacts/p7-roofs/metal-merge-r1/building_metal.glb) | 29,115,196 | 8.245% | `AFBFAC081121185CD716BAB394C7FFAA5E58EF0DE0D11D654790A644C89065D5` |
| [Metal gibs](../../.artifacts/p7-roofs/metal-gibs-merge-r1/building-metal.glb) | 2,891,512 | 4.769% | `2A63EBC643F882055D5BA592162D98D1E930FEAD3A3A05B7F01C334F748D1F1B` |
| [Armoured building](../../.artifacts/p7-roofs/armoured-merge-r1/building_armoured.glb) | 20,353,372 | 12.260% | `E23BBEAC1FA10FD4CB75B6B245C89B2957D547626A325FB9F71935BED9404AFE` |
| [Armoured gibs](../../.artifacts/p7-roofs/armoured-gibs-merge-r1/building-armoured.glb) | 2,362,208 | 5.523% | `9F1E024CEFD7A362E3F1B256BF71E0D623A70FE9428B31C4C158E20890D5B795` |

**The sole global staging slot is released.** These larger preserved-data candidates make
no size, memory or performance improvement claim. Installed assets, collision/gameplay
source and the original project/library are unchanged. No installation, Godot import,
render, runtime test, new diagnostic project or additional stage followed. Normal joins,
visual fracture quality and matching gameplay collision still require separate integration
and acceptance. Only the granted scratch candidates/receipts and this P7 report changed.

---

## Actual twig scratch gib merge passed preservation checks; slot released

**17 October, 12:18 local:** the one separately granted `twig-gibs-merge-r1` run exited
**0** in **1.227 seconds**, with empty stderr. It used the pinned helper
`A6E3AB7FD0A0983AAD0E62B76B26FBCA7DA334A0CD0A6B7F5CB11CD9113DC535` and fragment patch
`E356F506B3F873F85E07A2B6207565898AED5E41F84162433BCB005D2918E530`:

```powershell
python -I tools/repair_roof_assets.py merge --kind gibs --grade twig --patch .artifacts/p7-roofs/twig-gibs-r1/roof-gibs-twig.glb --out .artifacts/p7-roofs/twig-gibs-merge-r1/building-twig.glb
```

Fresh preflight was **6.630 GiB available / 43% commit**, no competing heavy job and fresh
output/guard directories. The two samples recorded minimum **6.577 GiB / maximum 43%
commit**; these are sampled values, not peak-memory measurements. The hidden owned process
had the **120-second** ceiling and existing **2 GiB / 85%** stop guard. No guard triggered;
Python PID 19712 exited and is absent. All **11 direct input hashes**, **144 frozen inputs**
and **63 protected production hashes** remain unchanged. No Git/index operation occurred.

Read-only inspection of the actual output established:

- The source-generation, building-merge, fragment-generation and gib-merge receipts all
  match their files, input hashes, grade/mode and protected snapshot. The output hash and
  size also match its guard inventory.
- All **18 unrelated groups / 180 unrelated nodes**, their hierarchy and scene ordering
  are unchanged. The pack remains **200 nodes** overall.
- The entire original **4,360,688-byte BIN prefix**, **180 original mesh records**, **720
  accessor records**, **720 buffer-view records**, and material records are retained
  unchanged. Appended fragment payloads and their remapped records match the patch exactly.
- Both roof root groups retain their exact patch transforms and nine direct fragments
  each. Every fragment transform, mesh payload and material binding is preserved: square
  roof **3,682 triangles / 6,728 vertices**, triangle roof **2,582 / 4,566**. The unchanged
  `Building_twig` material record is used. No geometry was regenerated by this merge.
- All **18 obsolete roof mesh records** remain without live node references. The pack
  grows from **4,542,888 to 4,956,292 bytes**: **+413,404 bytes / +9.100%**. Retaining old
  buffers preserves unrelated data; it is not a size or performance optimization.

Merged gib candidate SHA-256:
`CE34135FCC86ACB8AFA72B532CCA585269B8B8DCA067DCE5C404B36888EC6C76`.

Evidence:

- [Guard receipt, command, hashes and generated inventory](../../.artifacts/p7-roofs/guard-twig-gibs-merge-r1/receipt.json)
- [Resource samples](../../.artifacts/p7-roofs/guard-twig-gibs-merge-r1/resources.json)
- [stdout](../../.artifacts/p7-roofs/guard-twig-gibs-merge-r1/stdout.log) and [stderr](../../.artifacts/p7-roofs/guard-twig-gibs-merge-r1/stderr.log)
- [Merged gib receipt](../../.artifacts/p7-roofs/twig-gibs-merge-r1/building-twig.glb.receipt.json)
- [Read-only receipt/payload/group/transform preservation inspection](../../.artifacts/p7-roofs/guard-twig-gibs-merge-r1/inspection.json)
- [Merged scratch gib candidate](../../.artifacts/p7-roofs/twig-gibs-merge-r1/building-twig.glb)

**The sole execution slot is released; source remains frozen.** The original LOD2 bounds
flag and its measured explanation remain intact: both small extent changes arise in the
unchanged limiter before fracture. This merge changes neither that geometry nor any
threshold and does not grant normal visual/collision/fracture acceptance. No simplifier,
cap or transform repair was made. No installation, regeneration, other-grade job, import,
render, source edit or retry followed. Installed art and matching gameplay source are
unchanged. Only the permitted scratch output/evidence and this report were written.

---

## Diagnostic completed: existing limiter accounts for both extrema; slot released

**17 October, 12:13 local:** the single granted square-roof gather/limit measurement run
completed with exit **0**, empty stderr and **8.329 seconds** guarded elapsed. It used the
exact prepared driver SHA-256
`E27B82AFD34B35D90C569D38E1903F67F9A833BADF6651381CDFD3A58B4FCAE3` and command below,
including `--python-exit-code 1`. The AST loader successfully verified the pinned source
and setup, removing only the final `main()` expression at line 930. Measurement stopped
after `limit`, before fracture or export.

**The two recorded millimetre-scale excesses already exist after the unchanged limiter.**
Gathering leaves the original bounds and triangle count unchanged. The limiter path
(existing COLLAPSE modifier and cleanup) creates the expanded extrema before any cap,
fragment recentering or fragment export can occur. Re-decoding the retained fragment
vertices and comparing their actual positions to the limited mesh gives:

| Extremum | Before / after gather (m) | After limit (m) | Expansion introduced by limit | Final fragment distance to matching limited vertex |
| --- | ---: | ---: | ---: | ---: |
| Minimum Y | -0.103617638 | -0.108839095 | 5.221456 mm | 0.000279 mm |
| Minimum Z | -1.431473017 | -1.434610605 | 3.137589 mm | 0.000328 mm |

The matched limited vertices are indices **905** and **68**, respectively. Their retained
final counterparts are `roof_c00` vertex 0 and `roof_c08` vertex 183. This localizes the
specific reported excesses to the existing limit stage; a cap-stage explanation is not
needed for these two points. It does not assert that caps never move other vertices or
separately instrument the modifier versus its cleanup operations.

| Measurement stage | Vertices | Triangles |
| --- | ---: | ---: |
| Before gather, imported `roof_LOD2` | 3,836 | 2,905 |
| After gather | 1,827 | 2,905 |
| After unchanged limit | 1,077 | 1,452 |

The measurements retain all coordinates and explicit model-space transforms. Independent
read-only inspection recomputed their AABBs/slope bands from those coordinates, verified
the Blender-to-Godot conversion and compared the two target positions against the actual
retained fragment GLB. All measurement-integrity checks passed. The existing 1,000 target,
50% reduction clamp, weld settings and all production code remained unchanged. The prior
evidence excluding full-height gables remains valid.

Fresh preflight was **6.119 GiB available / 44% commit**, no competing heavy process and
fresh output/guard labels. Eight samples recorded minimum **5.837 GiB / maximum 45%
commit**. No timeout/resource/concurrency stop triggered under the retained **600-second**
ceiling and **2 GiB / 85%** guard. Owned hidden Blender PID 11368 exited and is absent.
All **12 recorded direct inputs**, **144 frozen inputs**, **63 protected production files**
and the driver remain hash-identical; the post-run read-only inspection independently
confirmed the frozen/protected hashes. No Git/index operation was performed.

Evidence:

- [Measurements and exact loader/input provenance](../../.artifacts/p7-roofs/twig-limit-diagnostic-r1/bounds.json)
- [Guard receipt, exact command and hashes](../../.artifacts/p7-roofs/guard-twig-limit-diagnostic-r1/receipt.json)
- [Resource samples](../../.artifacts/p7-roofs/guard-twig-limit-diagnostic-r1/resources.json)
- [stdout](../../.artifacts/p7-roofs/guard-twig-limit-diagnostic-r1/stdout.log) and [stderr](../../.artifacts/p7-roofs/guard-twig-limit-diagnostic-r1/stderr.log)
- [Independent read-only measurement/fragment comparison](../../.artifacts/p7-roofs/guard-twig-limit-diagnostic-r1/inspection.json)

`bounds.json` is **1,507,357 bytes**, SHA-256
`F641A572C10D1E9A619744C37B6B7E2C0052BCD02A25E08CA57590A458EA64A4`.

**The sole execution slot is released.** This resolves the origin of the observed extent
change; it does not turn the 2 mm diagnostic comparison into an art requirement, grant
acceptance or justify a new repair scope. The fragment candidate remains unaccepted pending
root review. No production/diagnostic source edit, fracture, asset export, material write,
Godot import, installation, other-grade job or retry followed. Only the permitted measurement
and guard evidence plus this report were written. The dated preparation and uncertainty
records below are superseded by these measured findings where applicable.

---

## Square gather/limit diagnostic source prepared and frozen; no execution slot

**17 October, preparation after root's 11:50 source-only grant:** one isolated driver is
prepared at
[square_limit_bounds.py](../../.artifacts/p7-roofs/diagnostic-preparation-r1/square_limit_bounds.py).
It is **14,633 bytes / 258 lines**, SHA-256
`E27B82AFD34B35D90C569D38E1903F67F9A833BADF6651381CDFD3A58B4FCAE3`.
This source is now frozen for root review. **No Python parse/compile, diagnostic, Blender,
generator, test, build, import or native job was executed.** Verification consists only of
source inspection, file hashes and the preparation/output path checks. All **144 frozen
inputs** still match; no production source, helper, asset or setting changed.

### Prepared loading and measurement boundaries

- `art/gibs/build.py` is pinned to
  `2BF044E8CB60BE75B4DD077BBAEAC51D1F3BC38245D764513F0A6EEF7637D579`.
  The driver will hash its bytes **before parsing**, require an exact final zero-argument
  `main()` expression, verify the ordered function/class names, and compare every remaining
  module-level setup statement to an explicit AST allowlist. It rejects definition
  decorators, annotations, nonliteral defaults and unexpected class execution. It removes
  **only that final expression** from the AST before compiling the definitions. It never
  directly imports or runs the script as a module and never invokes its `main`.
- Manual review of the surviving module setup found imports, constants/path construction,
  in-memory material maps, bytecode suppression and an art-directory `sys.path` insertion;
  no material, import-setting or asset write occurs there. The imported `hollow`,
  `hollow.export` and `hollow.godot` modules have imports/constants/function definitions at
  module level. Their writer functions are only defined. Their source hashes and canonical
  loaded paths are pinned; a preloaded `hollow` namespace is rejected. Blender executable,
  version and bundled NumPy path/version/metadata are also checked. This is static review
  of the exact sources, not a demonstrated runtime sandbox or full runtime-library audit.
- The driver reuses the **unchanged original** `import_glb`, `source_objects`, `gather`
  and `limit` implementations. It reads only the pinned twig catalogue row, retaining the
  existing 1,000 target and recipe; the usual selector must choose the one square
  `roof_LOD2` with 2,905 triangles. No limiter/seed/weld/cap/acceptance parameter is
  replaced. The whole merged pack is imported into Blender as the existing importer does,
  but only the square roof enters measurement and simplification. It does not call
  `gib_sets`, `build_set`, `fracture`, `export` or any material/import writer.
- JSON will record Blender and Godot coordinates, source transform, triangle/vertex counts,
  AABBs, square-roof slope bands and extremal vertices **before gather, after gather and
  after limit**. It also records nearest limited vertices to the two retained final-fragment
  extrema identified below. The coordinate conversion is explicit: Blender `(x,y,z)` to
  Godot `(x,z,-y)` in metres. It makes no pass/fail acceptance decision or threshold change.
- The merged pack/receipt, source inspection receipt, catalogue, shared art modules and
  freeze file have pinned hashes. All 144 frozen inputs and the 63 protected production
  hashes are checked before and after measurement. Input/output locations must match this
  checkout exactly. The only driver output is a new `bounds.json` under the exact fresh
  `twig-limit-diagnostic-r1` directory, created only after all measurement/hash checks.
  Bytecode writes are disabled. Any unexpected hash, AST, path, runtime, recipe or source
  selection is a failure, not permission to fall back or continue.

### Exact proposed command and guard plan — do not execute yet

From `D:\GameDev\Projects\HollowUniverseABTestB`:

```powershell
& 'D:\GameDev\Tools\blender-5.2.1-windows-x64\blender.exe' --background --factory-startup --python-exit-code 1 --python .artifacts/p7-roofs/diagnostic-preparation-r1/square_limit_bounds.py -- --source .artifacts/p7-roofs/twig-merge-r1/building_twig.glb --out .artifacts/p7-roofs/twig-limit-diagnostic-r1/bounds.json
```

The output directory and proposed guard directory
`.artifacts/p7-roofs/guard-twig-limit-diagnostic-r1/` are both absent at preparation time.
Root must separately review/grant this exact driver hash and command. The wrapper must
recheck **>=5.5 GiB available, <=80% commit and no competing build/test/Blender/Godot/server**
immediately before launch, use a hidden owned process and **600-second ceiling**, sample
resources, and stop only that process/tree below **2 GiB**, above **85% commit**, or if the
user's game/build starts. Retain command/dependency/source/output hashes, stdout/stderr,
exit/duration and samples in the new guard directory. `--python-exit-code 1` makes a Python
exception visible as failure; output/receipt inspection is still required afterward.

The preparation-time resource sample was **5.049 GiB / 47% commit**, below the execution
floor; it is not a launch authorization. No guard was started. Any later failure ends that
one granted run with evidence retained, no retry or source fix. The driver has no internal
resource monitor and therefore **must not run without the external guard**.

Limitations remain explicit: source-only review has not established that the AST guards,
Blender API calls or JSON output work. This diagnostic would localize gather/limit extent
changes; it would not assess caps, fracture appearance, matching gameplay collision,
visual joins or installation readiness. The existing fragment candidate remains
unaccepted. Only the new scratch driver and this report were written; no new execution
slot, source edit outside that scope or later job is implied.

---

## Read-only bounds diagnosis: correct comparison, missing intermediate evidence

**17 October, follow-up diagnosis:** the square-roof excess is a real difference from
the original **LOD2 render mesh**, not evidence of an axis/transform mix-up. It is not yet
localized to simplification versus cutting/capping. The fragment candidate remains
unaccepted; the 2 mm diagnostic threshold and all production source/settings are unchanged.

### What the retained bytes establish

The comparison uses **metres in the roof's glTF/Godot model space, +Y up**. Source
`roof_LOD2` is an identity root in merged candidate
`0CAE9522B4CF9F55CEF07FF6CDCDA997F8F3E3AE21A13E3D47A2FE46F947B1E7`.
Each fragment's position is reconstructed from its local vertices and stored node transform,
then its identity `roof` parent. There is no second Blender-to-Godot axis conversion in
the inspection. The baseline is the **2,905-triangle LOD2 before `gather`/`limit`**, not the
1,452-triangle limited mesh supplied to `fracture`, and not the full-detail roof.

A separate read-only calculation using quaternion cross products instead of the original
rotation-matrix expression reproduced every square-fragment bound within **4.44e-16 m**.
Normalizing the stored fragment quaternions changed reconstructed positions by at most
**1.73e-7 m (0.000173 mm)**. That serialization detail cannot explain a 5.222 mm excess.

The two square extrema are:

| Extremum | Final fragment / primitive / vertex | Reconstructed X, Y, Z (m) | Excess from source LOD2 |
| --- | --- | --- | --- |
| Minimum Y | `roof_c00` / 0 / 0 | -1.489789156, -0.108839301, 1.533416821 | 5.222 mm below |
| Minimum Z | `roof_c08` / 0 / 183 | -1.328168434, 2.959047207, -1.434610736 | 3.138 mm beyond |

The same square fragments fit inside the **LOD0 and LOD1 AABBs** (apart from negligible
1.6e-9 m X rounding). LOD0 minima are Y **-0.119110733 m**, Z **-1.436996341 m**;
LOD1 minima are Y **-0.114125744 m**, Z **-1.436996341 m**. This identifies which baseline
the failure concerns; it does **not** replace the LOD2 comparison, establish exact surface
containment or clear the candidate. Triangle fragments still have the separately recorded
1.937 mm maximum-Y expansion, including against LOD0.

**Obsolete full-height gables are excluded from this partial fragment candidate's geometry.**
All 18 meshes belong to the two expected groups. Every transformed vertex remains in the
thin source slope band: square **[-0.095678, +0.191010] m**, triangle **[-0.079106,
+0.191001] m**. Signed distance to that plane is affine across each triangle, so their
interiors cannot hide a full-height under-roof panel outside those vertex bounds. This
conclusion is independent of the small change along the roof's extent. It does not prove
fracture appearance, watertightness or runtime acceptance.

### Pipeline trace and remaining uncertainty

The relevant existing code is [art/gibs/build.py](../../art/gibs/build.py):

- `source_objects` (line 177) selects the named LOD2. `gather` (233) applies source object
  transforms, welds at **1e-5 m** and dissolves degenerate geometry. The original unused
  roof/gable buffers in the merged pack are not the selected named source objects.
- `limit` (274) applies Blender's **COLLAPSE** modifier and gathers its resulting mesh,
  followed by another weld/degenerate cleanup. There is no original-AABB constraint or
  retained-coordinate comparison. This is the first substantial geometry-changing stage
  after the inspected baseline and the leading stage to measure; the receipt establishes
  its triangle reduction, but contains no resulting vertex positions/bounds. Attribution
  of these particular extrema to the decimator remains a hypothesis.
- `cut` (544) bisects the mesh with 1e-6 m tolerance. `dress_cap` (511) explicitly shifts
  cap vertices by `v.co -= no * d`; `describe_islands` (429) sets **d = 0–0.6 mm per cap
  operation**. Those shared boundary vertices can move along any model axis, and a vertex
  can be affected by successive cuts. Thus cap creation also prevents an assumption of
  exact source-AABB preservation. Its contribution to these two extrema is not recorded.
  This catalogue entry has no hollow option, so the optional inner-wall extrusion is off.
- `chunk_object` (694) recentres/rotates fragments into principal axes and stores the
  inverse placement in their object transform. Algebraically, `R * (R^T * p - m) + R*m`
  recovers `p` for an orthonormal R; it does not intentionally enlarge the model. The
  independent reconstruction rules out the reported inspection transform error. Exact
  pre-/post-recentring numeric drift was not captured, so it is not directly measured.
- The saved receipt records counts/seeds only at intermediate stages. There is no limited
  mesh or pre-export fragment snapshot in the retained run, and `chunk_object` removes the
  `cap`/`island` attributes before export (727). Final bytes therefore cannot identify
  whether an extremal vertex was moved by collapse or a cap operation.

### Smallest next diagnostic — proposed only

A separately granted **square-roof-only Blender diagnostic** can import the same merged
candidate, select `roof_LOD2`, and call the unchanged `gather` and `limit` once. Capture
vertex counts, AABB/slope extrema and their positions **before gather, after gather and
after limit**, plus distance from the two final extremal positions above to the limited
vertex set. Stop there: no fracture, cap change, export, merge, material/import write or
installation. That determines whether the excess already exists before fracture without
repeating the full calibration or changing its budget. If it does not, a later explicit
grant would be needed for a cut/cap-stage trace; it is not an automatic follow-up.

No repair is justified before that localization. This diagnosis used only existing code,
receipts and GLB bytes, with read-only standard-library calculations. **No Blender,
generator, import, build, test, source edit or retry ran; only this P7 report changed.**
No new execution slot is claimed, and no simplification/cap budget or acceptance tolerance
has been altered.

---

## Twig fragments exported; bounds inspection flagged; execution slot released

**17 October, 11:41 local:** the one granted `twig-gibs-r1` Blender calibration exported
its GLB/receipt with exit **0**, empty stderr and **8.338 seconds** guarded elapsed.
The read-only inspection then flagged one bounds comparison, so this candidate is **not
accepted** and no further job followed. The result and exact failed comparison are retained.

The exact command was:

```powershell
& 'D:\GameDev\Tools\blender-5.2.1-windows-x64\blender.exe' --background --factory-startup --python art/gibs/build.py -- --roof-only --grade twig --source .artifacts/p7-roofs/twig-merge-r1/building_twig.glb --out .artifacts/p7-roofs/twig-gibs-r1/roof-gibs-twig.glb
```

Fresh preflight was **6.498 GiB available / 40% commit**, no heavy job, fresh labels and
the required merged-source hash. Eight samples recorded minimum **6.219 GiB / maximum
41% commit**. The hidden-process guard retained the **600-second** ceiling and **2 GiB /
85%** stop thresholds; no guard triggered. Owned Blender PID 1520 exited and is no longer
present. All **144 frozen inputs** and **15 recorded direct inputs/dependencies** remained
unchanged, including the Blender executable, bundled NumPy 2.3.4 metadata, shared art
modules, catalogues and source/receipt chain. No Git/index operation was performed.

### Actual fragments and bounds finding

The GLB contains exactly **two identity root groups**, `roof` and `roof-triangle`, each
with **nine direct named fragment meshes** (`_c00` through `_c08`): **20 nodes, 18 meshes,
6,264 exported triangles**. Material names/bindings, position/normal/UV attributes, finite
transformed positions and index ranges passed inspection. The receipt chain and all **63
protected production hashes/file names** match. The file has no external media, animation,
skin or extension dependencies.

| Roof part | Selected LOD / triangles | After existing limit | Pre-export fragment triangles | Actual exported triangles | Seed |
| --- | --- | ---: | ---: | ---: | ---: |
| Square | LOD2 / 2,905 | 1,452 | 3,682 | 3,682 | 1867159080 |
| Triangle | LOD2 / 1,994 | 1,000 | 2,588 | 2,582 | 3860711873 |

Both retained the configured **1,000-triangle target and nine requested fragments**, with
no override. The existing limiter refuses reduction beyond 50%, leaving the square source
at 1,452 triangles; that target is not a hard final-fragment ceiling. Fracture adds cap
geometry (928/681 recorded cap faces). The triangle's exported count is six below its
pre-export count; both values are retained without assuming why export cleanup differs.

The inspection applies each fragment's glTF transform and its parent transform before
comparing geometry. Square fragments remain within **-0.095678 to +0.191010 m** of the
intended sloping underside plane; triangle fragments remain within **-0.079106 to +0.191001
m**. Both match their source LOD2 slope bands within 2 mm, excluding obsolete full-height
gable panels. This does not establish visual fracture quality.

**The flagged comparison is `roof:withinSourceBounds`:** square fragments reach
Y = **-0.1088393 m**, versus source minimum **-0.1036176 m** (**5.222 mm lower**), and
Z = **-1.4346107 m**, versus source minimum **-1.4314730 m** (**3.138 mm farther**).
Those exceed this inspection's **2 mm diagnostic tolerance**. That tolerance is a
read-only comparison threshold, not an approved gameplay/art acceptance specification.
The triangle remains inside the same tolerance (its maximum-Y expansion is 1.937 mm).
The cause and acceptability of the square extent change are unresolved; no tolerance was
relaxed, source changed, extra diagnostic job launched or generation retried.

The isolated candidate is **417,308 bytes**, SHA-256
`E356F506B3F873F85E07A2B6207565898AED5E41F84162433BCB005D2918E530`.

Evidence:

- [Guard receipt, exact command, source/dependency hashes and output inventory](../../.artifacts/p7-roofs/guard-twig-gibs-r1/receipt.json)
- [Resource samples](../../.artifacts/p7-roofs/guard-twig-gibs-r1/resources.json)
- [stdout](../../.artifacts/p7-roofs/guard-twig-gibs-r1/stdout.log) and [stderr](../../.artifacts/p7-roofs/guard-twig-gibs-r1/stderr.log)
- [Exporter receipt with actual budgets/counts](../../.artifacts/p7-roofs/twig-gibs-r1/roof-gibs-twig.glb.receipt.json)
- [Transform-aware inspection, failed comparison and per-fragment bounds](../../.artifacts/p7-roofs/guard-twig-gibs-r1/inspection.json)
- [Unaccepted partial fragment candidate](../../.artifacts/p7-roofs/twig-gibs-r1/roof-gibs-twig.glb)

**The sole slot is released; source remains frozen.** No gib merge, installation, other
grade, build, Godot import/render/native run or retry followed. Only this report and
isolated scratch artifacts changed. Installed roofs/gibs and their gameplay collision are
unchanged; matching collision integration, visual joins and fracture acceptance remain
outstanding. Root must review this concrete finding before granting any next action.

---

## Actual twig scratch merge passed; execution slot released

**17 October, 11:36 local:** the sole granted `twig-merge-r1` run exited **0** in
**3.584 seconds**, with empty stderr. Fresh preflight was **6.687 GiB available / 39%
commit**, with no heavy process and absent output/guard labels. Four samples recorded
minimum **6.638 GiB / maximum 39% commit**; no timeout, resource or competing-job stop
triggered. The hidden owned Python process (PID 12276) exited and is no longer present.
The guard enforced the **120-second** ceiling and existing **2 GiB / 85%** stop thresholds.

Exactly one merge ran from the AB Test B root:

```powershell
python -I tools/repair_roof_assets.py merge --kind building --grade twig --patch .artifacts/p7-roofs/twig-source-r1/roof-source-twig.glb --out .artifacts/p7-roofs/twig-merge-r1/building_twig.glb
```

The expected source candidate hash was verified before launch. All **144 frozen inputs**
matched root's freeze before and after the job, and all **nine direct recorded inputs**
(including the helper, Python executable, baseline pack, patch and source receipt) remained
unchanged. Root's already-reviewed staged patch was not independently rechecked here;
this job performed no Git/index operation. The actual merge receipt chain, including both
input hashes, modes/grade, output hash/size and the source receipt hash captured by the
guard, passed the subsequent read-only inspection.

That inspection confirmed:

- The original **14,720,296 BIN bytes**, all **124 original mesh records**, all **409
  accessor records**, all **409 buffer-view records**, and the exact baseline material
  records remain unchanged. Appended payloads/records match the twig patch with only the
  necessary index/offset remapping.
- All **112 unrelated nodes**, their hierarchy and scene order remain unchanged. The
  merged pack has **120 nodes**, down from 124: the four gable collider nodes are absent.
  Its selected roof roots are exactly the six render LODs and two slab colliders, with
  their original identity transforms and the correct `Building_twig` material binding.
- All **63 protected production files** still match their receipt hashes, and the
  protected file set is unchanged. Installed packs, imports, materials, catalogues and
  gameplay source were not modified.
- All **12 old roof mesh records** remain retained without live node references. The
  candidate is **16,411,848 bytes**, compared with the baseline's **14,817,820 bytes**:
  **+1,594,028 bytes / +10.758%**. Obsolete buffers are intentionally retained; this is
  preservation evidence, not a size, memory or performance optimization.

Merged candidate SHA-256:
`0CAE9522B4CF9F55CEF07FF6CDCDA997F8F3E3AE21A13E3D47A2FE46F947B1E7`.

Evidence:

- [Guard receipt, command, hashes and output inventory](../../.artifacts/p7-roofs/guard-twig-merge-r1/receipt.json)
- [Resource samples](../../.artifacts/p7-roofs/guard-twig-merge-r1/resources.json)
- [stdout](../../.artifacts/p7-roofs/guard-twig-merge-r1/stdout.log) and [stderr](../../.artifacts/p7-roofs/guard-twig-merge-r1/stderr.log)
- [Merged receipt](../../.artifacts/p7-roofs/twig-merge-r1/building_twig.glb.receipt.json)
- [Independent read-only preservation/receipt inspection](../../.artifacts/p7-roofs/guard-twig-merge-r1/inspection.json)
- [Merged scratch candidate](../../.artifacts/p7-roofs/twig-merge-r1/building_twig.glb)

**The sole execution slot is released; source remains frozen.** The actual twig pack
merge and preservation checks passed. No fracture, generation, other grade, installation,
import, build, engine run, source change or retry followed. Roof fracture quality, matching
gameplay collision and visual joins remain unverified. The candidate is only in scratch;
installed art and gameplay collision still retain their matching old gables. Only this
report and isolated scratch artifacts changed. Further work requires a separate root grant.
The dated records below remain as historical evidence.

---

## Twig roof-source calibration passed; execution slot released

**17 October, 11:27 local:** the one granted `twig-source-r1` Blender calibration completed
with exit code **0** in **9.469 seconds**, producing the partial roof-source GLB and its
receipt. The nine resource samples recorded a minimum **6.933 GiB available** and maximum
**37% commit**. No resource, timeout or competing-job stop triggered. The owned Blender
process (PID 4744) exited and is no longer present. This is a source-generation calibration,
not an installed repair or visual acceptance.

The exact command, run from the AB Test B root, was:

```powershell
& 'D:\GameDev\Tools\blender-5.2.1-windows-x64\blender.exe' --background --factory-startup --python art/import/building_kit.py -- --roof-only --grade twig --out .artifacts/p7-roofs/twig-source-r1/roof-source-twig.glb
```

Fresh preflight recorded **7.495 GiB / 35% commit**, no heavy job, absent candidate/guard
labels, and the four prepared tool sources matching root's freeze. The hidden-process guard
retained the **600-second** ceiling, **2 GiB / 85%** stop thresholds and competing-job stop
rule. All 13 recorded direct input/dependency hashes remained unchanged. The before/after
inventory of **1,187 scoped files** was identical by path, length and modification time;
that inventory is not a byte-level proof for un-hashed files. A separate read-only inspection
rehashed all **63 protected production files** in the exporter receipt and found no change.
These cover the installed building/gib packs and imports, building textures/materials,
catalogues and fit report. No Git/index operation or broad asset-library scan was performed.

### Actual output inspection

- Exactly **eight meshes/nodes**, all direct scene roots with identity transforms: six
  render meshes (`roof` and `roof-triangle`, each with `_LOD1` and `_LOD2`) plus only their
  `_col00-convcolonly` slabs. No gable collider nodes or unrelated meshes were exported.
- Render meshes retain position, normal and UV attributes and the sole `Building_twig`
  material name. This partial file intentionally has a plain placeholder material; a later
  merge must preserve the installed pack's real material record. There are no images,
  textures, external buffer references, animations, skins or glTF extensions in this output.
- Decoded vertex positions are finite and indices are in range. Across all six render
  meshes, signed distance from the intended sloping underside plane stays between
  **-0.108 m and +0.199 m**, consistent with the retained twig deck/eave and excluding
  full-height vertical gables. Each collider spans **0.000–0.200 m** above that plane.
  This geometric inspection does not establish normal in-game joins or visual quality.
- Original deterministic seeds are retained: square roof **7686**, triangle roof **7823**.
  The existing coplanar repair reports **184 clipped triangles**, all on square-roof LODs,
  and zero duplicate removals. Its subdivision explains why final square-roof triangle
  counts exceed the pre-export generated counts; the triangle-roof counts are unchanged.

| Piece | Final LOD0 triangles | Final LOD1 | Final LOD2 | Slab triangles |
| --- | ---: | ---: | ---: | ---: |
| Square roof | 13,745 | 5,891 | 2,905 | 12 |
| Triangle roof | 8,508 | 3,832 | 1,994 | 8 |

The repaired partial is **1,594,840 bytes**, SHA-256
`79CD9457B9F5E2484EB9F1104B2B336BAA13A53E97B432908BD4D31F984BEA78`;
the saved receipt's hash/size match. The raw export is **1,415,432 bytes**, SHA-256
`6AE76E9C76D1378BFDE72653738B2E6A9AEBFF30B94878D9D03223912204103C`.
There was one nonfatal Blender deprecation warning for `Material.use_nodes`; no traceback
or export error appeared. No source fix or retry was attempted.

Evidence:

- [Guard receipt, exact command, dependency hashes and generated-file inventory](../../.artifacts/p7-roofs/guard-twig-source-r1/receipt.json)
- [Resource samples](../../.artifacts/p7-roofs/guard-twig-source-r1/resources.json)
- [stdout](../../.artifacts/p7-roofs/guard-twig-source-r1/stdout.log) and [stderr](../../.artifacts/p7-roofs/guard-twig-source-r1/stderr.log)
- [Exporter receipt and protected-file hashes](../../.artifacts/p7-roofs/twig-source-r1/roof-source-twig.glb.receipt.json)
- [Read-only GLB/receipt inspection](../../.artifacts/p7-roofs/guard-twig-source-r1/inspection.json)
- [Partial candidate](../../.artifacts/p7-roofs/twig-source-r1/roof-source-twig.glb)

**The sole execution slot is released; all source remains frozen.** This run establishes
that the prepared twig source exporter works with the installed Blender and produces the
expected bounded geometry. It does not validate a full-pack merge, roof fractures, any other
grade, engine imports, matching gameplay collision, placement or visual joins. No merge,
fracture, install, Godot run, build, test or other-grade job followed. The observed resource
samples are not peak-memory measurements or permission to reduce later workload floors.
Installed art and gameplay source still retain their matching old gables. Only isolated
scratch evidence and this report were written; further execution needs a separate root grant.
The dated records below are historical and their pending-calibration statements are now
superseded by this result.

---

## Synthetic controls passed; execution slot released

**17 October, 02:22 local:** the sole granted `controls-r1` run completed successfully.
All **41 synthetic controls passed**, exit code **0**, empty stderr. The guard measured
**0.321 seconds** from process launch through completion (the control script's own core
timer was 0.0146 seconds). Two resource samples recorded a minimum **7.130 GiB available**
and maximum **51% commit**, with no concurrent heavy process. No timeout/resource guard
triggered; the owned Python process exited and is no longer present. These brief samples
are not a peak-memory measurement or a performance forecast for real asset generation.

Evidence:

- [Control results](../../.artifacts/p7-roofs/preparation-r1/runs/controls-r1/result.json)
- [Guard receipt and exact before/after input hashes](../../.artifacts/p7-roofs/preparation-r1/guard-controls-r1/receipt.json)
- [Resource samples](../../.artifacts/p7-roofs/preparation-r1/guard-controls-r1/resources.json)
- [stdout](../../.artifacts/p7-roofs/preparation-r1/guard-controls-r1/stdout.log) and
  [stderr](../../.artifacts/p7-roofs/preparation-r1/guard-controls-r1/stderr.log)

Immediately before launch, memory/process checks met the granted >=5.5 GiB, <=80% commit
and no-heavy-job requirements; the run and guard output labels were absent. The wrapper
enforced the 30-second ceiling, 2 GiB/85% stop thresholds and concurrent-heavy-job stop rule.
The four prepared source files matched root's 144-input freeze before launch and their
individual hashes remained unchanged afterward. Root had verified the shared 25-path staged
patch; this job made no Git/index operations and did not independently re-audit that patch.

The result covers the **synthetic merge core and scratch guards only**: preserved original
payloads/materials/hierarchy and replacement transforms for building/gib layouts, GLB
roundtrips, and the listed malformed-input/output refusals. Production IO is deliberately
blocked in those controls. It does **not** validate actual building/gib files, the production
receipt chain, Blender compatibility, roof geometry, collision, visuals, fracture quality,
runtime cost or gameplay. No production library scan, launcher, build, other test, generator,
import or render ran. No retry was needed or attempted.

**The sole execution slot is released. Source remains frozen.** Installed assets and gameplay
source are unchanged, so no P7 source/art mismatch exists. One twig roof-source calibration
is still a separate, ungranted step requiring the documented resource/time guards; no merge,
fracture, import or other grade may follow automatically. Only this result report was updated
after the run. The preparation and historical records below remain as dated context.

---

## Narrow asset-tooling preparation ready; execution remains held

**Current status after the 01:17 grant:** only the two permitted exporters and new merge
helper have been prepared from the verified parked copies. A small synthetic-control source
file was added in the granted scratch directory. No structural socket/support/persistence/
wire code was restored. Preparation is frozen for root review, not validated or installed.

Fresh read-only hashes confirm `PieceShapes.cs`, `BuildingData.cs` and `PieceGeometry.cs`
still match their restored pre-grant hashes. Production `building.json` remains
`A8DFA0653A14E675CE309C35E97465B4E3E17998CA4A292C274B6AD04BE029C5`.
**No gameplay source/art mismatch is active.** Installed meshes, gibs, import settings and
materials were not modified; gameplay collision still matches their old gables. Only root
may later integrate a verified geometry/fracture candidate with its matching collision patch.

| Prepared path | SHA-256 | Bytes |
| --- | --- | ---: |
| `art/import/building_kit.py` | `BDB10DA6D8B147C879DE885C9CB862277FB3F1E4ADF673009E26E29114406E44` | 33,000 |
| `art/gibs/build.py` | `2BF044E8CB60BE75B4DD077BBAEAC51D1F3BC38245D764513F0A6EEF7637D579` | 39,724 |
| `tools/repair_roof_assets.py` | `A6E3AB7FD0A0983AAD0E62B76B26FBCA7DA334A0CD0A6B7F5CB11CD9113DC535` | 21,044 |
| `.artifacts/p7-roofs/preparation-r1/merge_controls.py` | `12D16468000EB637C6261087C3FBC384C36C0B389D0BA7304FEBAD0FDCDD09B5` | 12,686 |

This report is the only changed document. The original parked copies and historical hashes
below remain intact. No build, test, syntax check, candidate validator, generator, Blender,
Godot, import, staging, commit, other-chat message or subagent was run. Source text comparisons,
file copies within the exact grant, static inspection and the small listed hash checks are
the only verification performed. No production-library scan or synthetic control was run.

### Prepared behavior and static review limits

- The building exporter removes only generated gables and their imported hulls. Deck/eave,
  seam lean, original pivots, UV/normal generation and all three authored LOD names remain.
  `--roof-only --grade ... --out ...` generates six meshes/two hulls for one grade; original
  grade and GENERATED indices still determine seeds. It bypasses FBX imports, texture writes
  and whole-grade fit/report writes. Roof fit has no face/cut rules. It reuses the existing
  duplicate/coplanar repair; that module's already-existing scratch directory is required
  before loading it. Raw and repaired output paths must be new files in `.artifacts/p7-roofs`.
- The normal exporters retain their existing CLI/default output paths and import/material
  behavior, apart from the intended new roof geometry. **Do not run those broad default
  commands for this task.** Bounded gib mode requires an explicit **merged staged building
  input and matching receipt**; it cannot silently fracture the old installed gables. Only
  roof/roof-triangle are selected, preserving nine requested fragments, crc32 seeds and the
  existing 1000/500 budgets. Bounded mode suppresses material and `.import` writes. Receipts
  will record chosen LOD, selected/limited/exported triangles, fragment counts and bytes.
- The merger has a pure core for synthetic controls. It appends replacement payloads, retains
  original mesh/accessor/view/material records and original BIN bytes, remaps only selected
  roof nodes, and compares unrelated nodes/hierarchy/scene order before writing. Material
  names must match the one grade material; the original material record wins. Roof gib
  groups remain direct roots with direct named mesh fragments and original fragment transforms.
- Validation rejects unsupported layouts rather than inventing conversions: animations,
  skins, images/external buffers, extensions/compression, sparse accessors, invalid references,
  nonfinite attributes, out-of-range indices, missing render attributes/materials, malformed
  hierarchy and displaced roof/group pivots. Output guards require new scratch files with
  exact basenames. Source/production/import/material hashes accompany receipts.
- **These are code-inspection findings, not passing checks.** The synthetic source exercises
  both building/gib merges with unrelated shared meshes/hierarchy/materials, roundtrips,
  transforms, rejected malformed inputs and output guards. It prevents its pure-core cases
  from reaching production IO. It does not establish the real CLI receipt chain, actual
  exporter output, visual joins, collider agreement or fracture quality.
- Original roof buffers remain unreferenced in merged packs to preserve unrelated payloads.
  The candidate may be larger and import unused data; **no file-size, memory or runtime-cost
  saving is claimed**. Compaction/LOD migration is outside this scope.

### Minimal next commands — prepared only, separate grants required

From `D:\GameDev\Projects\HollowUniverseABTestB`, root should first review and grant the
light synthetic control job, with a fresh output label and a 30-second bound. This command
loads only the helper source and tiny synthetic documents; it writes a new result receipt
under the preparation directory. It has **not** run:

```powershell
python -I .artifacts/p7-roofs/preparation-r1/merge_controls.py --label controls-r1
```

After reviewing those results, a **different grant** can calibrate one twig source batch:

```powershell
& 'D:\GameDev\Tools\blender-5.2.1-windows-x64\blender.exe' --background --factory-startup --python art/import/building_kit.py -- --roof-only --grade twig --out .artifacts/p7-roofs/twig-source-r1/roof-source-twig.glb
```

Use root's single-job resource wrapper: **at least 5.5 GiB available at start**, stop the
owned job below **2 GiB** or above **85% commit**, and retain the existing **600-second**
generation ceiling, stdout/stderr, source hashes, elapsed time and memory samples. Verify
the executable path and a fresh label at that time. The last delegated resource snapshot
was only 3.720 GiB / 65% commit; there is no execution slot or automatic retry. Do not stop
user applications. No synthetic result justifies changing the generation/world memory floors.

The twig job produces only a partial roof-source GLB/receipt, not a full pack or installed
asset. Actual merge, fracture from that merged input, gib merge, inspection, imports and
any other grade each need later grants; none is chained into these commands. The applicable
later interfaces are `repair_roof_assets.py merge --kind building|gibs --grade twig --patch
<partial-with-receipt> --out <new-full-pack>` and `art/gibs/build.py -- --roof-only --grade
twig --source <merged-staged-building_twig.glb> --out <new-roof-gibs-twig.glb>`.
Do not invoke them yet. No whole-grade regeneration or unrelated asset work is proposed.

All actual control results, Blender compatibility, asset validation, measurements and native
acceptance remain pending. No runtime failure was observed because no job was launched.

---

Historical parking and review records follow; the narrow preparation status above supersedes
their earlier blanket source hold, while gameplay-source and execution holds remain active.

## Candidate parked; production source restored

**Current status — 17 October, 00:59 local:** root preserved all six exact candidate files
under `.artifacts/recovery/p7-paused-source-20261017/`, verified their hashes, restored the
five tracked files to their pre-grant bytes, and removed the new merge helper from `tools/`
only after verifying its identical backup. The
[parking manifest](../../.artifacts/recovery/p7-paused-source-20261017/manifest.json)
records the candidate and restored hashes, all **138** prior frozen inputs matching again,
the unchanged production catalogue, and zero heavy jobs.

**No P7 source/art mismatch remains.** The installed art and restored collision source both
retain the old gables. The repair is still pending; this is preservation and restoration,
not implementation or acceptance. Shared/staged changes were excluded from root's parking.
Only this report's top section was updated afterward; all source and execution work remains
stopped. A later geometry-only repair needs a separate scope; no new sockets, structural
connectivity or migration is authorized.

Root relayed the user's clarification that roofs should visibly join automatically when
placed side by side. **Source work stopped immediately; no further source/asset edits or
helper expansion followed.** The earlier lateral-socket proposal below is now historical,
pending scope review, and is not required for the concrete case of roofs on adjacent supports.

**Smallest change:** use the existing placement rules to place equally oriented roofs on
neighbouring foundations/floors/wall tops. Their sloping decks already meet along the 3 m
module. Remove the full-height generated gables, matching authoritative/imported gable
collision and obsolete fragments, while preserving decks, eaves, pivots and existing Top
stacking. Then verify visible joins and collision through normal placement. **No new socket,
support solver, structural link, save migration or wire behavior is necessary for that case.**
The source review never established that extending roofs unsupported from another roof was
needed. A separate conditional rule retaining exterior gables and removing only shared
interior gables would be a different visual contract; do not infer it from the Rust analogy.

Six source paths changed before the hold. These **historical candidate hashes** now identify
the parked copies, not active production edits:

| Path | Classification and frozen SHA-256 |
| --- | --- |
| `art/import/building_kit.py` | Geometry: removed four gable calls, unused gable builders and hull additions. Also contains unexecuted roof-only staging mode. `C2161A26B87E3BCAE640E8C50ABDD400DD6024418D547DD19A8D307843896DF5` |
| `src/Hollow.Simulation/Building/PieceShapes.cs` | Geometry/collision only: roof slab retained, gable parts/helper removed. `7FFD62DE51CA4D06089AB87F6C9247814C843CA603C154827615598240B63DAA` |
| `art/gibs/build.py` | Asset tooling only: staged candidate input, roof-only parts/output, optional import suppression and fragment statistics; no fractures generated. `6386D2B38F89DBE122AFD43A45D71D18A73E6D1F946C052ED470411D17E4FACF` |
| `tools/repair_roof_assets.py` (new) | Asset tooling only: bounded scratch GLB validation/merge and receipts; unexecuted/unvalidated. `BA33B562D5B133F594D4E40BBB3F865248F07C76FCE228E5F6ED96A047E7B846` |
| `src/Hollow.Simulation/Building/BuildingData.cs` | **Optional structural expansion to set aside:** edge-index declarations/parser/validation. `993F7FF8618F42EDD7666AE6AF084A9FD04179CE06EECE4123A8A892512C50EE` |
| `src/Hollow.Simulation/Building/PieceGeometry.cs` | **Optional structural expansion to set aside:** restricted edge enumeration/acceptance, roof-side snaps/orientations/anchors. `1F45935B936AA3EE211B4DCC34455560061B6C20D63E2AF9F71BE0D8776C09CF` |

`game/data/ground/building.json` is byte-identical to the pre-edit receipt:
`A8DFA0653A14E675CE309C35E97465B4E3E17998CA4A292C274B6AD04BE029C5`.
Thus the optional new side sockets are **not enabled**. Neither proposed test file exists:
`RoofBuildingTests.cs` and `RoofAssetTests.cs` were not created before the hold. No persistence,
BuildingWorld, claim, protocol, admin or other source was edited. No assets/import settings,
shared materials, staging or commits were changed. No build, test, syntax execution, Blender,
Godot, generation or installation was run. Only read-only hashes/inspection followed the hold,
plus this report update. A first hash command had a PowerShell parse error and did not execute;
the corrected read-only command produced the hashes above.

**Historical mismatch resolved by parking:** before restoration, candidate collision source
lacked gables while installed meshes and gibs retained them. Those candidate sources are now
outside production and remain uncompiled/unexecuted. Preserve them for selective reuse only;
do not restore the optional socket expansion with the geometry work. The legacy two-cupboard
restore risk belongs to that optional connectivity expansion; the geometry-only fix does not
introduce those new links.

---

The following is the earlier read-only review, retained as context. Its proposed structural
extension is superseded by the scope hold above; its historical no-source-edit statement
describes that review stage. The parked-candidate/restored-production status above is current.

**Status: source review and proposal only. No roof repair has been implemented or validated.**
The unwanted sides are generated gable panels, present in both roof shapes at every grade and
authored detail level. Remove their visible geometry and both collision representations, then
regenerate their fractures. Direct side-to-side snapping also needs a small shared socket change:
the current catalogue provides no lateral roof sockets. Existing square-roof stacking already
has a shared rule and should retain its poses.

This review reads the current checkout, the recovery/common/P7 instructions, DESIGN's confirmed
building rules, REPAIR_BUILDING, the backlog and the original
[screenshot 11](../images/reports/2026-10-16-playtest/screenshot-11.png). It reuses the completed
[asset audit](../ASSET_LIBRARY_AUDIT.md). Only this result document was created. No production,
test, asset, script, other document or staged path was changed; no Git, subagent, other-chat
message, build, test, import, Blender or Godot operation was run. The delegated resource snapshot
was 5.080 GiB free / 63% commit; that was not resampled here. Maximum startup still requires
8 GiB. Production and the shared 25-path staged set remain outside this grant.

## Confirmed source findings

### Which geometry to remove

`art/import/building_kit.py` generates these roofs from the existing owned PSG kit's mesh
builders and atlas. They are not missing FBX roof assets to acquire or recover.

- `roof()` (around line 299): remove its **two `gable(...)` calls**, on the square's left and
  right sloping boundaries. In game coordinates these are at x = −1.5 and +1.5, from z = +1.5,
  y = 0 to z = −1.5, y = 3. They fill the upright triangles below the slope.
- `roof_triangle()` (around line 313): remove its **two `gable(...)` calls**, from each base
  corner `(±1.5, 0, +0.8660254)` to the apex `(0, 3, −1.7320508)` in game coordinates.
- `gable()` and `GABLE_FOOT` (around lines 268–290) then become unused. The helper produces a
  6 cm thick panel extending 1 cm below the pivot and, for twig, additional upright branches.
  Those panel-specific branches should disappear with the panel.
- **Retain** `sloped()`, the sloping deck and its perimeter/edge thickness, the twig low-edge
  `eave()`, UV/material assignments, pivot and slope, and the existing `DECK_LEAN = .02` seam
  treatment. The thin rim of the actual deck is not the unwanted full-height side panel.
  Do not remove wall pieces independently placed beneath or beside a roof.

Screenshot 11 shows the large upright panels below the sloping deck. The source establishes
their construction; the screenshot alone does not establish selected entity IDs or grade.

### Collision and damage must agree

`PieceShapes.For` at `src/Hollow.Simulation/Building/PieceShapes.cs:161` adds both `Roof(...)`
and `Gables(...)`. Remove the latter addition, and its now-unused helper/constant at roughly
354–396. Keep `Roof()`'s sloping slab and the configured floor thickness. These engine-free
parts supply actual Jolt bodies through `BuildingWorld.ShapeOf` / `BuildingPhysics.ShapeFor`,
on the server and client replica. They also supply the renderer's missing-art fallback.

The exporter independently mirrors these shapes: `generated_hulls()` around lines 505–512
returns `roof_hull(...) + gable_hulls(...)`. Return only the sloping hull for both keys and
remove the unused gable-hull helper. Replacing GLB collision nodes alone would not change the
game's authoritative collision. Conversely, changing only PieceShapes leaves misleading
imported collision and unchanged panels/gibs.

Fresh **read-only GLB JSON inspection** found, in each of the five grade packs:

- `roof`, `roof_LOD1`, `roof_LOD2`, and the same three names for `roof-triangle`;
- three hull nodes per roof: `*_col00-convcolonly` is the sloping slab; `*_col01-convcolonly`
  and `*_col02-convcolonly` are the sides;
- corresponding gib groups `roof` and `roof-triangle`, with nine fragments each, in each
  `game/assets/gibs/building-<grade>.glb`.

That is **30 roof render meshes, 10 retained slab hulls, 20 removable side hulls and 10 gib
groups currently containing 90 fragments**, across twig, wood, stone, metal and armoured.
This establishes stored node/triangle metadata, not fresh visual or physics acceptance.

### What snapping and support already do

| Case | Current source behavior | N10 implication |
| --- | --- | --- |
| Roof on foundation/floor edge or wall top | Both square and triangular roof children are accepted; roof orientation count is two | Keep these placements and their existing orientation IDs |
| Neighbouring roofs placed on separate neighbouring supports | Each can be placed from its own support; no lateral roof-to-roof link is created | Removing panels can open the join, but does not let a player extend/support the next roof directly from its neighbour |
| Square roof extending upward from another square roof | `building.json:119–121` supplies Top, accepting `piece:roof`, floor and wall | Preserve this already-supported stack |
| Direct roof side attachment | Neither roof definition has Edge sockets; `BuildingData` validation currently rejects edge sockets on non-slabs | Needs catalogue, validator and shared geometry changes, not just meshes |
| Triangular roof as parent | `building.json:122–124` has an empty socket list | No roof/floor/wall can currently snap from its sides or apex |

For a square parent at pivot `(0,0,0)`, yaw 0, the current Top→square-roof rule gives
`(0,3,−3), yaw 0` for orientation 0 and `(0,3,0), yaw 6` for orientation 1 (yaw units are 30°).
These are source-derived poses, not results of a run. The latter folds the next slope back
above the parent; it is not a same-height ridge. Do not silently replace either legacy pose.

`PieceGeometry` owns socket enumeration/acceptance, orientation, snapping and aim anchors
(roughly lines 126–307). Roof Top currently assumes a square high edge at z = −width/2.
Adding a Top socket to the triangle unchanged would be geometrically wrong: its high point
is an apex at z = −width/√3, with no full-width bearing edge.

`BuildingWorld.Supports` (`BuildingWorld.cs:241`) derives support by enumerating these same
snap poses; `Link`, `StabilityAt`, claim merging and restored structures use that shared
relation. The special `WallBearsFloor` repair only applies to floors, not roofs. Roof links
currently cost 100/1000 stability. The strongest path must still reach grounded foundations.

`CheckCollision` / `Joined` (`BuildingWorld.Placement.cs:443–496`) already exempt pieces
linked through those valid sockets. The existing wall/tile and tile/tile seam helpers do not
generally apply to roofs. A valid lateral snap can supply both structural support and its
intended contact exemption without adding a second support solver or broadly ignoring roof
collisions. New poses must reject overlapping footprints and mismatched edge heights before
they can become such an exemption.

## Proposed minimal behavior

Implement the panel removal and shared **sloping-side attachment** together, while retaining
the existing Top behavior. This is a proposal for root review, not a new source grant.

1. Add an optional edge-index restriction to the existing socket declaration. Declare roof
   side sockets accepting the roof family: square edges **1 and 3**, triangle edges **1 and 2**.
   Other socket declarations keep their existing default of all footprint edges. Parse and
   validate the restriction, including untrusted out-of-range/undeclared edge claims. Permit
   ordinary Edge sockets for these roof declarations; do not thereby permit roof EdgeUp/Down.
2. Extend shared `PieceGeometry` with a roof boundary profile derived from `Footprint`,
   `RoofSpan`, width and height. Match a parent's declared sloping side to a compatible child
   sloping side with a rigid yaw/translation, checking both endpoint heights, matching edge
   length, opposite footprint interiors and non-overlap. Use the existing 30° yaw lattice.
   Use the actual 3D side midpoint as the aim anchor, not the roof's Top anchor or y = 0.
   Orientation encoding for the new Edge path can enumerate receiving edges deterministically;
   leave the existing Top/slab/wall orientation encoding intact.
3. Route candidates, client previews and authoritative claims through that same shared rule.
   No special client placement path, forced placement, new protocol payload, free-floating
   roof or unconditional roof collision exemption is proposed. Existing `Supports` then
   discovers both neighbours and alternate paths, including on restoration.
4. Keep current square-to-square upward/folded stacking and place lateral neighbours at each
   such tier. Triangular roofs can join the sloping sides of that structure, or be placed on
   existing walls/floors at higher tiers. **An unsupported vertical column of triangular
   apex-to-base roofs is not implied or proposed.** If that is the intended meaning of
   stacking, root must define its bearing contract separately; copying square Top is unsafe.

The side profiles are geometrically compatible in this kit: both square and triangular
sloping boundary edges have a 3 m horizontal length and a 3 m rise. Thus their 3D lengths
match at √18 m; mixed joins need yaw alignment rather than a new pitch/roll state. This is
geometric reasoning, not proof of the final deck-thickness seam, collision or cursor behavior.
Square→square, square↔triangle and triangle→triangle need explicit acceptance cases.

The new side connections remain within the existing 4.5 m link search radius. No increase to
that radius, stability strength, grade costs, build reach or cupboard permissions is proposed.
No new general deployable snapping, window insert feature, double door content or Give to
friend workflow is included in this N10 grant.

## Exact proposed ownership and dependencies

| File / group | Narrow proposed hunk or responsibility |
| --- | --- |
| `art/import/building_kit.py` | The four gable calls, unused gable helpers, two roof hull returns; narrowly bounded roof-only generation/output selection if approved. Preserve original GENERATED indices/seeds when filtering |
| `src/Hollow.Simulation/Building/PieceShapes.cs` | Roof case and unused Gables/GableThickness only; no stair, wall, door or foundation changes |
| `src/Hollow.Simulation/Building/BuildingData.cs` | Optional socket edge restriction, its reader, roof Edge validation; no item/grade/admin semantics |
| `src/Hollow.Simulation/Building/PieceGeometry.cs` | Restricted edge enumeration/acceptance, roof-side orientation/snap/anchor helper; preserve existing branches and Top poses |
| `game/data/ground/building.json` | Only socket declarations of IDs 18/19; preserve IDs, costs, height, shape, existing square Top and all other pieces |
| `art/gibs/build.py` | Optional bounded part/output selection and suppression of unrelated material rewrites; use current fracture settings/seeds |
| Proposed new `tools/repair_roof_assets.py` | Only if root chooses targeted generation: validated GLB subtree/buffer replacement for the exact roof nodes/groups, with an unrelated-content comparison; no new importer/LOD system |
| Five `game/assets/building/building_<grade>.glb` | Replace six roof render meshes and roof collision nodes per grade; all other content must compare unchanged |
| Five `game/assets/gibs/building-<grade>.glb` | Replace only roof and roof-triangle groups; preserve every other piece and material binding |
| Proposed new `tests/Hollow.Tests/RoofBuildingTests.cs` and `RoofAssetTests.cs` | Meaningful N10 geometry/authority/collision/support/restore and asset regressions; avoid editing currently shared test files where helpers can be reused |

Paths above are **requested future ownership**, not edits made in this review. New helper/test
names are proposed and must be reserved before creation. Root owns matching updates to
REPAIR_BUILDING/GROUND/backlog/development and the frozen-input manifests.

**Admin dependency:** coordinate the BuildingData, PieceGeometry and building.json hunks
before granting them. Preserve `PlacementPlan.Grade`, BGrade/free-build checks, reach/sight,
claim checks, occupied-socket exclusion, pending-ghost handling and current clan-base preset.
No changes to `BuildingWorld.Placement.cs`, GroundView, admin UI, protocol/generated messages
or staged admin tests are currently justified. If normal input exposes a candidate scoring
problem, report it and request that exact extra hunk. A roof Edge claim fits existing socket
codes 0–3; verify network bounds and data agreement before claiming no wire work is needed.

**P8 dependency:** P7 owns changed fracture assets; P8 retains GibRenderer, graphics settings,
gib budgets/slider, shared materials and the retained diagnostic harness. Keep catalogue
budgets (twig 1000, other grades 500), requested fragment counts (9), and destruction
event key/grade mapping unchanged. New fracture output may have fewer pieces after sliver
merging; do not increase runtime caps. Queue all native work through root's single heavy slot.

**LOD scope:** these existing building packs deliberately have `meshes/generate_lods=false`
and use named LOD0/1/2 meshes selected by `BuildingRenderer` (26 m / 64 m thresholds) with
fallback in `BuildingAssets`. Update all 30 roof meshes in that established path. Godot's
automatic mesh LOD remains available for other imports; missing authored LODs would not by
itself block a new rigid prop. N10 needs neither a Nanite-like system nor a migration of this
working building renderer/import configuration.

## Bounded generation, import and validation plan

1. **Before source work:** root reserves the precise hunks and records current input hashes
   and the staged-patch receipt. Capture a baseline manifest of non-roof node/accessor/buffer,
   material/image and import-setting content. Keep original-library sources read-only.
2. **Prepare source and focused checks; freeze for root review.** No heavy job is implicit.
   Include all four roof-pair combinations, rotations around the yaw lattice, old Top poses,
   rejected arbitrary/forged sockets, occupancy, alternate supports, unsupported cycles and
   removal of the last grounded support. Exercise the actual physics path: a ray/character
   can cross the old gable space while the sloping slab still blocks and supports correctly.
3. **Preferred generation:** after a separate slot, generate only two roof keys × three LODs
   × five grades, and their ten slab hulls, into isolated scratch outputs. Reuse current
   deck/eave/material/repair code; preserve deterministic seeds by filtering after original
   index selection. Merge only the named roof subtrees into copies of the current packs.
   Any node/buffer/material drift outside the allowlist rejects the candidate. This merge
   utility does not currently exist; review it before relying on it. Preserve glTF index
   references, transforms, UVs, normals, material slots and untouched payload bytes.
4. **Fracture only ten roof groups**, from the staged candidate packs, at existing budgets,
   targeting up to the current 90 fragments. Merge into copies of the five existing gib
   packs. Do not run the unmodified grade command in place: `gib_sets()` selects all twenty
   building pieces per grade, and `main()` also rewrites all building/garage materials.
   The current building converter likewise rewrites a whole grade, textures and fit reports.
5. **Workload bounds:** five serialized grade batches, each producing six render meshes,
   two hulls and two gib groups. Start with twig as a calibration; request a guarded maximum
   ten-minute generation slot for that one grade, stop rather than retry automatically,
   and retain elapsed time/RAM/commit samples before scheduling the remaining four. Peak RAM
   and duration are unmeasured; this review cannot justify a lower starting-memory floor.
   Root applies the existing >=5.5 GiB general floor and 2 GiB / 85% stop guard, with a higher
   floor if the measured workload requires it. No CPU generation and GPU import/run overlap.
6. **Fallback only after review:** if a safe roof-only merge is disproportionate, stage full
   grade exports one at a time and accept only with unchanged non-roof semantic content.
   That expands work to **195 source FBX imports and 105 generated mesh builds** across five
   grades before fitting/corners/repair, plus up to **100 gib groups** if the unmodified
   fracture selection is used. Do not silently choose that larger workload or publish its
   unrelated changes. Atlas/fit/material outputs are not authorized by the narrow proposal.
7. **Installation/import:** root reviews candidate comparisons before replacing the ten GLBs.
   Keep existing import UIDs/settings and shared materials. Queue one bounded incremental
   Godot import separately; no all-library reimport or icon generation. Re-freeze inputs.
8. **Serialized checks:** one shared build, then focused Roof/BuildingFit/Building/Gib tests
   as selected by root, plus normal placement network/restart cases and admin regressions.
   Existing BuildingFit validates walls/slabs/spiral bounds but has no roof opening check;
   existing tests whose local variable is named `roof` often use the flat `floor` piece and
   are not N10 evidence. Add actual sloped-roof cases rather than relying on those names.
9. **Native acceptance:** guarded asset/build viewer first, then normal hosted placement with
   an isolated save, and a separately scheduled guest. Inspect five grades at three authored
   levels; square pairs, mixed pairs, triangular pairs, both stack orientations and rotated
   joins; walk/shot passage through opened sides; slope hit/contact; dismantling and alternate
   support; restart/late join; gibs at zero/default/maximum using P8's existing setting. Record
   screenshots and normal build-input sequences, refusal reasons, source/settings/pose/time
   and performance samples. A full Maximum world run retains the **8 GiB** startup floor.

## Preservation risks and still-unverified items

- Existing saves retain roof IDs 18/19 and poses; changing built-in gable collision changes
  the physical enclosure of those existing roofs immediately. Do not synthesize replacement
  walls. Check sleepers, placed objects and attachments near removed side surfaces on an
  isolated restored save; this review has not established whether any existing attachment
  can depend on those surfaces.
- New lateral support links can join previously separate same-frame structures. Normal
  `JoinsClaims` must still refuse two active cupboards on new placement. Restoration and
  existing adjacent claimed structures need an explicit regression/migration decision;
  additive socket data is not proof that all old saves remain semantically unchanged.
- A support relation also exempts collision between linked pieces. Match complete boundary
  profiles and reject overlapping interiors; a permissive approximate match could admit
  intersecting roof volumes or give stability through a corner.
- Retain the old foundation-depth, floor/window coplanar and twig seam repairs. Rebuilding
  an entire pack can accidentally overwrite them even if the roof code change is tiny.
  Twig shimmer and other P7 carryovers remain open.
- Removing gables must remove their fragments too. Current GibTests establishes catalogue
  coverage/materials/counts, not absence of obsolete panel fragments; compare reconstructed
  roof fragment geometry and inspect an actual roof break.
- Contact along pitched mixed-shape seams, slope walkability, aiming at new sockets, network
  predictions, building aboard ships, existing-save behavior and runtime cost are **not
  verified**. No screenshot, passing test or performance result for a repaired roof exists
  from this review, and N10/P7 remain open.
