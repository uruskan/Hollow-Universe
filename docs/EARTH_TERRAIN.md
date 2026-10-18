# Earth layout variant 5 / save revision 5 — 18 October 2026

Revision **5** corrects overlapping ice/alpine biome weights and fences generated
population changes. The layout seed, landforms, drainage and terrain-height golden remain
unchanged. Default save names use `earth-v5`; revision-4 saves are preserved and refused,
not migrated or overwritten. Other walking bodies advance to revision 2; protocol 37
prevents older peers from mixing populations. See [WORLD_ENVIRONMENT_ARCHITECTURE.md](WORLD_ENVIRONMENT_ARCHITECTURE.md).
The measurements and images below describe the layout selected on 17 October; biome/tree
coverage statistics need a fresh survey after the normalization correction.

## Layout selection / save revision 4 — 17 October 2026

The owner requested broad, varied land with open building areas, forests and mountains.
The current `WalkingParameters.Earth` selects deterministic layout variant **5**, chosen
from six candidates with the same terrain profile. The 1,200 m radius and **18.0956 km²**
total surface are unchanged. Variant zero preserves the earlier generation domain.

The candidate measurement finds **10.616 km² dry ground**, **7.428 km² gentle ground**,
and **6.186 km² open/gentle ground**. The largest connected dry component contains
**99.986%** of sampled dry land. These are terrain suitability proxies, not guarantees
of valid building placement. Dense tree cover occupies 11.70% of sampled dry ground;
the combined forest biome weights cover 47.87%. Mountain relief occupies 7.86%, with
a sampled maximum height of 261.6 m.

The deliberate Earth golden is now **`3FD0EDEC0D0C72A5`**, with 10 lakes and 532 drainage
pieces. Save revision **4** selects `earth-v4` default save filenames. Existing saves
remain intact and are refused when their recorded terrain revision does not match.
The on-foot globe cache includes this terrain revision. Holding a drag now suspends
north-settling, avoiding competing camera motion while the pointer is down.

The selected [biome map](images/content-integration/earth-layout-5-biomes.png),
[relief map](images/content-integration/earth-layout-5-relief.png) and
[measurements](images/content-integration/earth-layout-5.json) record this choice.
Current native validation is recorded in [DEVELOPMENT.md](DEVELOPMENT.md).

## Historical layout 2 / save revision 3 — 16 October 2026

The measurements and captures below describe the previous layout.

The owner requested more dry ground and larger connected landmasses while retaining
the 23.04 km² limit on the entire planet. Earth's radius remains **1,200 m**, giving
**18.0956 km² total surface area**, including oceans, lakes and streams.

## Implemented profile

`WalkingParameters.Earth` selects the profile by Sol's system address and Earth's
body path. Server, client and diagnostic reports use the same walking terrain.

| Parameter | Inherited Earth | Revision 2 |
| --- | ---: | ---: |
| Ocean target | 0.71 | 0.35 |
| Continent wavelength | 2,600 m | 3,200 m |
| Island field share | 0.32 | 0.12 |
| Coast detail | 0.035 | 0.02 |

The longer continent wavelength and reduced island/coast detail favor broad land
connections. Lakes, rivers, relief, biomes and generated placement systems still
operate on that terrain. Earth's physical Solar System metadata retains its 0.71
ocean reference; the playable surface has a separate target. Other bodies retain
their existing profiles, and explicit diagnostic parameter overrides still work.
This is procedural game geography, not a reconstruction of Earth's real continents.

## Measured result

Equal-area Fibonacci sampling at **160,000 directions** measures the actual height
and water surfaces. Dry ground excludes inland water as well as the ocean.

| Surface | Before | Revision 2 |
| --- | ---: | ---: |
| Dry ground | 5.12297 km² (28.31%) | **10.64472 km² (58.825%)** |
| Ocean | — | 6.31750 km² |
| Lakes and streams | — | 1.13335 km² |
| Entire sphere | 18.0956 km² | 18.0956 km² |

Dry ground increases by about **5.52 km²**, or **2.08 times**. A separate area-weighted
cube-sphere measurement at level 7, approximately 15 m cells, finds about **10.64 km²**
in the largest dry component: **99.9% of sampled dry ground**. Four-edge adjacency
joins face seams and poles without joining cells that touch only at a corner.
This establishes connected land at the sampling resolution; it does not establish
that every point is walkable, buildable or free of small water channels and cliffs.

The intentional revision-2 terrain golden hash is **`3D5174066B119948`**.
Raw [baseline measurements](images/earth-land/validation/baseline-area.log),
[final measurements](images/earth-land/validation/final-area.log), build/test logs and
accepted/rejected capture logs are retained in `images/earth-land/validation/`.

## Saves and multiplayer

Coastline changes also move terrain-dependent objects and invalidate old world
positions. After P1's changed solid cliff placement, default hosted saves include
`earth-v3` in their filename; the dedicated server defaults to
`hollow-server-earth-v3.db`. Existing save files are
retained rather than overwritten or silently restored onto the new landscape.

Database schema **9** records terrain revisions per system/body. A read-only check
rejects mismatched revisions and unversioned saves containing spatial state for
the requested body before migration or restoration. Empty or unrelated-body saves
can be adopted. Earth now uses save revision **3**; all other walking bodies use
revision **1**, fencing P1's cliff generation **2** on every affected world.
Their heightfields are unchanged by that collision integration. Revision zero remains
available for stand-in ground and matching legacy builds, but is no longer selected
by a current walking body. Revision-2 Earth and revision-zero or unversioned populated
walking saves are preserved and refused. The error explains that terrain revisions
include land shape and solid cliff placement. Reopening an old world requires its
matching build. No automatic conversion of placed objects, ships, characters or
terrain overrides is provided. P5's network protocol **33** rejects older peers so
clients and servers cannot disagree about coastlines and collision.

The save integration does not reroll the heightfield: the base generator version,
Earth's land parameters and golden `3D5174066B119948` remain unchanged. The images
below predate P1's candidate and are evidence for the land layout, not visual
acceptance of the new cliffs. The three additional compatibility regression tests
and the integrated build remain queued through recovery coordination.

## Reproduction and evidence

Run from this repository after building. The `earth` report selects actual playable
Earth; the `walking` report continues to select the inherited first world at 2,410 m.
Clear any `HOLLOW_WALKING` diagnostic override before reproducing the shipped profile.

```powershell
dotnet build HollowUniverse.sln
dotnet run --project tests/Hollow.Tests -- EarthTerrain
dotnet run --project tests/Hollow.Tests -- TerrainSave
dotnet run -c Release --project tools/Hollow.GeneratorReport -- earth stats
dotnet run -c Release --project tools/Hollow.GeneratorReport -- earth biomes docs/images/earth-land/earth-v2-map.png 2048
dotnet run -c Release --project tools/Hollow.GeneratorReport -- earth globe docs/images/earth-land/earth-v2-report-globe.png 700
```

The [baseline](images/earth-land/baseline-map.png) and
[revision-2 biome map](images/earth-land/earth-v2-map.png) have been inspected.
Actual Godot [ground](images/earth-land/earth-v2-ground.png),
[orbital](images/earth-land/earth-v2-globe.png), and
[system-map](images/earth-land/earth-v2-system.png) screenshots were also inspected at 1600×900.
Ground/orbit used Low quality, normal child-server hosting, fresh diagnostic databases,
and the generated route beach at latitude -30.486486, longitude 43.059333.
The orbital camera adds `--lift 3600 --pitch -85 --yaw 0`. Both logs confirm
Playing, ready, loading complete, and a clean disconnect; the existing seven Texture
RID/two ObjectDB shutdown warnings remain. Two High-quality inland-start captures
timed out while assets loaded and are rejected. The report globe above is separate
from the in-engine globe screenshot. The system-map globe remains a generic shader;
its surface description and ocean parameter now use the playable 35% ocean target.

The initial focused terrain/environment checks passed 20/20 and the latest
save/architecture checks passed 10/10. A shared-output full run was rejected after
concurrent source/build changes and a native process failure. Rebuilding and copying
the test binaries into an isolated directory made all six previously failed or
interrupted checks pass (6/6). The subsequent isolated full run was interrupted by
the computer restart: its log contains 253 passes, one failure in
`GunNetTests.GunfightOverTheNetwork` ("reloading at once"), and one unfinished test.
It has no completion summary and is **not a full-suite pass**. The preserved logs
are `isolated-prior-failures.log` and `isolated-full-tests.log` in the validation folder.
Further integrated checks are serialized through the recovery coordinator.
The coordinator's 15:35 recovery build was stopped by its memory guard after
34.69 seconds at 1.566 GiB available RAM (74% commit). Some DLLs were emitted,
but there is no completed solution summary, so this is **not a build pass**.
No follow-on tests or GPU captures ran. The new collision/save compatibility
tests remain unrun; partial build outputs are not fresh validation evidence.
See [DEVELOPMENT.md](DEVELOPMENT.md) and the
[recovery checkpoint](repair-results/Earth-terrain-recovery.md) for limitations.
Historical environment-route coordinates refer to the earlier terrain; use a newly
generated route for revision 2.

## Choosing a layout

Procedural generation can produce repeatable candidates from different seeds and
profiles. The owner can compare maps and choose a favorite while keeping the same
surface cap and land/connectivity requirements. The current game selects one fixed
Earth seed; there is no player-facing reroll or candidate-selection screen yet.
Once a layout is chosen, its seed, settings and generator revision must stay fixed
for that saved world. A later geography change needs a new compatible world/save
identity or an explicit migration. This revision is a reviewed candidate, not a
claim that the owner has selected Earth's final geography.
