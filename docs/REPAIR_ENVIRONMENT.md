# Environment repair — 15 October 2026

Authoritative request: `DESIGN.md` and `reports/current-17-issues.md`, especially 2, 6, 10 and 11.
`reports/reported-shore.png` is the actual reported frame. The legacy reports were read as evidence;
their proposed plans and old completion labels are not proof of current behavior.

## Findings verified in the initial audit

- Hosted Earth is the 1,200 m `WalkingWorld` routed through `StartingWorld` and `ZoneSpec`; it is not
  accidentally drawing the real Earth radius. The inherited walking generator does contain relief provinces,
  coasts, drainage, lakes, rivers and biome cover. Their appearance on the smaller Earth still needs visual tuning.
- All sixteen scanned ground layers are already loaded through `GroundLayers`, including normal and roughness maps.
  Replacing this with another texture directory would not fix the observed material issues.
- Woodland grass was suppressed three times: `WalkingWorld.Vegetation` already reduces grass beneath canopy;
  `GrassField` additionally removed up to 80% for forest-floor material and up to 75% for tree shade. The separate
  collectible fern meshes survived, explaining the sparse ferns over almost bare ground in the report.
- Terrain wetness forced every low material to roughness 0.16, including non-shore materials. It also painted
  procedural white residue lines on the terrain. The near scan contributed only a minority of the blended detail.
- Water read the normal map's blue channel as foam/breakup noise, although that channel stores normal Z and is
  nearly constant at one. Its thresholds consequently produced solid bands. Foam also depended on view-ray depth
  and included a compulsory white edge. The emission-only water body used
  a different moonlight multiplier than the ordinary lighting used by foam/terrain; night water could therefore
  be nearly black beside strongly lit white contours. Underwater presentation was absent.
- No actual cliff-scan placement/render/collision component existed. Rocky terrain was texture plus small scatter.
- Stone, metal and sulphur outcrops already exist in the shared scatter, at a base density of one per 3,000 m²
  before shore/forest/slope suppression. High-grade ore is yielded by metal nodes; the authored high-grade node
  model is explicitly unused in `models.json`. Fuel comes from fuel barrels and crafting, not a fuel ore node.
- Monument meshes exist under `game/assets/monuments`, with a model viewer, but no authoritative monument layout
  system places them in the live world. Current live sites are clusters of barrels/crates and recyclers.

## First repair batch

- `GroundDetailCover` now consumes the surface's already adjusted grass density once, preserving woodland grass
  alongside litter, herbs and ferns. Bare sand/rock, steep cliffs and submerged ground remain suppressed.
  Water rejection uses `ISurface.WaterLevelAt`, so lakes and streams agree with the sea. Existing atlas varieties
  are retained; meadow and flower frequency is increased modestly without introducing downloaded foliage.
- Terrain retains more of the near photographic scan, limits wetness to shore substrates, and uses roughness 0.48
  for wet grains rather than a polished 0.16 finish. Artificial white residue contours were removed.
- Water now packs independent wave-height noise into the normal texture's alpha channel and uses it for breakup.
  Foam uses bed depth and broken patches. Refraction rejects foreground samples and limits grazing-depth
  artifacts. The water body now receives the same scene lighting as its surroundings, with modest roughness.
- `SurfaceView.Water` samples the actual camera against sea/lake/stream water and applies underwater fog plus a
  subtle refractive/color transition on a canvas below the HUD. It is deliberately separate from the shared
  breathing/drowning rule being repaired by the root task.
- `EnvironmentDetailTests` checks woodland and meadow cover plus sand, cliff and submerged exclusions.

## Cliff asset preparation and shared placement

Two local Poly Haven scans were converted with the existing local Blender 5.2.1 executable. No downloaded tool,
paid generation service, Rust-extracted mesh, or Rust-extracted texture was used. RustMVP's local-cliff conversion
code was inspected for the principle of preserving PBR bindings and fitting geology to slopes; its extracted
reference geometry and placement transforms were not copied.

| Scan | Local source | License / source |
|---|---|---|
| Coastal Cliff 04 | `D:/GameDev/Assets/coastal_cliff_04_4k.gltf` | [Poly Haven asset](https://polyhaven.com/a/coastal_cliff_04), [CC0](https://polyhaven.com/license) |
| Namaqualand Cliff 02 | `D:/GameDev/Assets/namaqualand_cliff_02_4k.gltf` | [Poly Haven asset](https://polyhaven.com/a/namaqualand_cliff_02), [CC0](https://polyhaven.com/license) |

`tools/blender/environment_cliffs.py` is the reproducible conversion. Each formation has a 12,000-triangle near
mesh, a 2,500-triangle distant mesh, 2K PBR textures, and a 700-triangle collider derived from the same scan.
The four GLBs total approximately 8.4 MB. `game/assets/surface/cliffs/provenance.json` records original and output
SHA-256 hashes, dimensions and triangle counts; originals are untouched. Collision geometry is engine-free data,
so server, client prediction and Godot's cosmetic collision can share placement and physical shape.

`CliffPlacements` uses an independent deterministic stream on approximately 48 m candidate cells, accepting dry
steep slopes and embedding each formation into them. Existing terrain heights, harvestable IDs and loot IDs are
unchanged. `WalkingWorld.GroundCollision` adds the shared scan colliders to the terrain tiles; client prediction,
server movement and cosmetic ragdolls all reach this same interface. `CliffField` streams the matching models
within 340 m, switches detail at 95 m and instantiates at most two formations per frame. An integration check scans
the whole Earth, compares independent placements, verifies radial bases/bounds and asserts that the authoritative
collision API includes the scans. The coordinated build and focused integration test passed, finding 80 formations
and confirming shared collision. The live cliff capture and its remaining art limitations are recorded below.

## Resources and live monuments

- The parallel asset integration replaces all twelve stone/metal/sulphur/HQ node stages from
  `D:/GameDev/Assets/PSG_ResourceNodes_R01` at existing model paths, retaining four authored LODs, PBR materials,
  metre scale and geometric depletion. Measured weak-point surfaces were regenerated from these models.
  The package contains original geometry and CC0 Poly Haven rock detail, with no extracted commercial assets.
- HQ is appended to the natural-kind and scatter enums, preserving existing saved indices. It uses the shared
  harvesting, weak-point, stage, depletion and refill systems. Its 600 health, 60 high-grade/80 metal ore yield,
  900-second refill and 7.5% extra candidate density are provisional balance. The inherited metal node still
  produces its existing HQ byproduct. Walking-world outcrop density is multiplied by 2.2 through the configurable
  `WalkingParameters.ResourceDensity`; existing candidate streams and IDs remain stable.
- `D:/GameDev/Assets/ResourceNodes`: another original three-node package, not the four-resource PSG delivery.
- No dedicated fuel-node model was found in either node package. Fuel barrels and fuel-depot monument models
  exist. Generated wellheads now provide fuel barrels through the existing loot system; no fuel ore was invented.
- `Hollow_Production_Briefs_R01/ASSET_INVENTORY.json` indexes the owner's newer Salvage, LootContainers, PowerKit,
  WreckModules, RuinDressing and Substation packages. Root owns the direct item/container integration. WreckModules
  and further dressing remain available for later site expansion.

Five original archive assemblies are now live: Weather Station, Crashed Skiff, Hab Pods, Wellhead and Relay Mast.
`MonumentPlacements` chooses dry, gentle sites in independent approximately 300-metre cells, with deterministic
candidate retries, footprint separation and a 90-metre landing clearance. Spacing/probability are configurable.
This is planet-size-dependent small-site generation; the archive's kilometre-sized landmarks are not silently
squeezed onto this Earth, and this repair does not claim all 55 archive types are live.

Every site grades a bounded tangent-plane pad, returning smoothly to the original heightfield over eight metres.
Both rendering and collision read `HeightAt`/`Sample`; raw `Fields` retains geological fields and drainage. Trees,
grass, ore and clutter are excluded at actual scatter candidates as well as the density samples. Beach arrivals
skip the structures. Existing ground changes only within these new footprints; prior constructions in old saves
could overlap a newly introduced site, so repair playtests use isolated saves.

`tools/blender/environment_monuments.py` compiles original Hollow collision metadata, including kit pieces, into
embedded engine-free triangles. Server movement, prediction and cosmetic physics use those shapes through the
same ground collision interface as cliffs. `MonumentField` streams assemblies within 850 metres with LODs.
`LootSpots.ForMonuments` turns authored loot markers into authoritative world props: existing network, storage,
depletion and refill behavior applies. A separate C8 ID namespace preserves existing loot IDs. Archive-only
specialist labels temporarily map to shipped crate/military-crate tables. Unique specialist loot, power activation,
NPCs, puzzles and marker-driven no-build zones are not implemented by this repair.

## Supplied R01 industrial landmark

A sixth type, Service Substation, uses the owner's complete five-part `Hollow_Substation_R01` service court at its
authored transforms: transformer, switchgear, cable reel and barrier. An adjacent compact switch bay uses the
`Hollow_PowerKit_R01` breaker, open disconnect and termination pedestal. A single dismantling spot and oil seep
from `Hollow_RuinDressing_R01` provide localized wear with an obvious source, leaving the walking route clear.
The equipment is abandoned scenery with loot, not a claim that electrical gameplay is implemented.

`tools/blender/environment_substation.py` verifies the source manifests and vendors only the 69 required native
scene/material/texture dependencies. Resource paths are remapped under `game/assets/revised`, original sources
remain untouched, and native PBR and exclusive LOD bands are retained. Its parser reads actual native convex hulls
and hierarchical transforms, producing 396 shared collision triangles for the combined landmark. Native Godot
collision layers are disabled in the visual instances to avoid a second collision authority.
`game/assets/revised/substation-integration.json` records selected source/output SHA-256s and assembly transforms.
The packages contain original owner-controlled geometry, Poly Haven CC0 scans and original texture derivatives;
RuinDressing's painted-steel atlas also documents an earlier ImageGen-derived source. No remote generation was
run here. No Rust-extracted geometry, texture, map or placement data entered the project.

## First real-render feedback and correction

The daytime stream capture `.artifacts/repair-shore-day.png` showed grass restored, scanned ground visible and no
old solid white water border. It also exposed unacceptable dark plant-card silhouettes in the stream and solid
rectangular grass bases. That frame was not accepted as finished art.

The fine ground lattice now also reads cached terrain water levels to reject all cover layers at submerged
vertices; coarse climate interpolation can no longer plant grass across a stream narrower than one climate cell.
The same fine check clears monument footprints, preventing interpolated tufts from leaking into buildings.
Opaque discard cutouts keep grass color and depth in the same screen snapshot used by water. The previous
ALPHA/scissor path could supply depth without matching plant color. Procedural blade strips break solid card
bases, flowers keep their own silhouette, and grass tint is reduced. The same camera must verify these changes.

## Further authored vegetation inventory

The local `wild_grass_vlkhcbxia_ue_high`, `cotton_grass_wlykecdia_ue_high` and
`tundra_grass_wk2oaeyqx_ue_high` packages contain respectively eight, nine and nine authored variants, with four,
four and five LODs. Their `standard/*_nonUE.gltf` scenes declare alpha-mask, two-sided materials; sampled base/opacity
PNG alpha spans 0–255. Node scale is 0.01, converting the centimetre mesh data to metres. These are real UV-mapped
plant meshes, not drop-in replacements for the painted atlas's cells. The subsequent integration below preserves
their UVs and opacity, uses bounded near geometry and far LODs, and retains stream/monument clearing.

## Validation status

Root coordinates builds and renderer ownership. Blender conversion and selective manifest verification completed.
Root's build 5 passed with zero warnings. The later focused run (`.artifacts/repair-focused4.log`) passes all five
`EnvironmentDetailTests`, including six monument types and 77 reachable art dependencies: 61 sites, 217 stable
loot anchors, all four harvestable node families and 80 scanned cliff formations. Tests include shared collision,
tangent pads, cleared scatter and safe beach arrivals. The 20:12 client/test assembly also passes all five checks
without rebuilding; it prints external review cameras for the cliff and substation and a shallow underwater camera.
`ContentTests.BrokenDataIsReported` passes after the test fixture removes every high-grade ore gathering source
and refined-metal recycling yield; its original invalid dependency assertion is unchanged.

The coordinated 20:26 asset import completed successfully. An initial capture exposed a live fuel-data/assembly
mismatch (`interaction verbs: "open-fuel" has no handler`); root's coherent build 7 resolved it. Five sequential
1600×900 live-world captures then completed on fresh isolated saves. Every run had no startup/shader errors, no
warm-up warnings and no hashed runtime-input changes; none used the 100-second screenshot fallback. The capture harness records
SHA-256 identities for runtime DLLs, ground JSON and surface shaders so the evidence can be tied to a build.

| Capture under `.artifacts/` | Elapsed | Observed result |
| --- | ---: | --- |
| `environment-stream-day.png` | 46.8 s | Dark submerged plant-card silhouettes and rectangular grass roots are gone; scanned stream bed and clean waterline are visible. |
| `environment-underwater.png` | 41.3 s | Camera immersion tint/fog works and HUD stays untinted. Broken bright horizon lines reveal missing/coarse underside rendering; this remains a concrete shader issue. |
| `environment-cliff.png` | 40.9 s | Real scanned rock formations are grounded in the live slope. The central formation is modest relative to the hill; legacy rounded rocks and trees still limit the art quality. |
| `environment-substation.png` | 41.8 s | Supplied R01 machinery, court, switch bay, loot and graded clearing render together with clear walking space. |
| `environment-stream-night.png` | 39.1 s | Night stream is dark but readable without the old white shore band. The existing star field is excessively dense and bright. |

The reusable local scripts are `.artifacts/Run-EnvironmentCapture.ps1` and `Get-EnvironmentCapture.ps1`; each run
has matching `.run.json`, `.log` and `.error.log` files. The GPU was handed back after those five captures.

The painted grass/flower cards remain visibly uniform and yellow-green, below the requested art standard. Root
approved a next bounded pass using the supplied WildGrass, CottonGrass, TundraGrass and BrackenFern meshes, with
measured LOD/draw budgets and an actual before/after capture. This is active work, not an accepted final environment.

## Authored ground cover batch

`tools/blender/environment_ground_cover.py` verifies SHA-256 files against the supplied ZIP extraction receipts,
then preserves authored UVs, opacity, metre conversion and shape. Wild Grass (`vlkhcbxia`), Cotton Grass
(`wlykecdia`), Tundra Grass (`wk2oaeyqx`) and Bracken Fern (`seEoW`) contribute three variants each. Near grass
uses authored LOD1 instead of the original meshes that can exceed 40,000 triangles; middle uses authored LOD2/3,
and far uses the original 3–8 triangle billboard. Bracken's native near meshes already have only 58–88 triangles.
All 20 embedded maps are verified 2048×2048 PNG derivatives; originals and archive receipts stay untouched.
The four GLBs total about 89 MiB before engine texture compression. `catalog.json` records source/output hashes,
mesh counts, source nodes and measured metre dimensions. These are owner-supplied Quixel/Fab plant assets, not CC0
or assets extracted from Rust; the project already admits the owner's Fab assets under its existing licence basis.

`GrassField.Authored.cs` draws those native meshes in at most 36 instanced batches. Near/middle/far bands are 8/22 m
and the existing quality-dependent grass reach. A strict 18,000 visible-instance cap bounds the field. Visible
candidates within 22 metres are prioritized by distance, then distant candidates by a stable hash with distance
as the tie-breaker. This avoids dropping an ordered tail of spatial rows when the budget is reached. Smooth,
stable density/species patches use the existing fine grass lattice; every wet or cleared monument corner rejects
upright plants. Cotton grass is a sparse wet-meadow accent, tundra tufts mix in drier patches, and bracken is sparse
under canopy. The dense, fine blade underlayer remains, with native accents above it; the old oversized flower and
plant cards are disabled, while flat forest litter remains. The custom material keeps
scanned colour and opaque-pass cutouts, with restrained wind and transmitted light. First field installation logs
actual LOD counts, draw batches, submitted triangles and CPU batching time as `[cover]` for the next capture review.

The underwater water surface is now two-sided, flips the ripple normal below the surface and measures absorption
from submerged eye to surface. The live `.artifacts/environment-underwater-underside.png` capture verifies that the
broken horizon lines are replaced by a continuous rippled underside, with the HUD still untinted.

## Vegetation review and correction

The first authored-only frame, `.artifacts/environment-stream-day-authored.png`, was rejected. It exposed too much
foreground ground and made distant grass look like green mounds. Source and exported texture inspection confirmed
real alpha ranges of 0–255; opacity was not lost. The correction uses fuller Wild Grass B/C/A variants, disables
Godot's second automatic mesh simplification on already authored LODs, reduces accent density, and restores the
fine blade underlayer. These changes were reviewed together; no isolated experiment attributes all mound artifacts
to automatic LOD generation alone.

The user's direction was to preserve the old satisfying coverage while making it more diverse and realistic.
The corrected `.artifacts/environment-stream-day-diverse.png` restores that coverage without the rejected bare
foreground or distant clumps. Root accepted this as a coverage correction, not final realistic vegetation: the
base blades remained too uniformly lime green. At the same stream origin the native accents logged 11,523 visible
plants, LOD counts 313/2,492/8,718, 27 batches, 2,638,050 submitted triangles, and 10.5 ms initial batching. These
counts exclude the fine blade underlayer and are not whole-frame draw or performance claims.

A separate 15-second measurement of the earlier rejected authored-only version recorded 84 FPS average,
11.92 ms average frame time, 19.76 ms P99 and 31.51 ms worst on the RTX 2070 SUPER at 1600×900. It does not measure
the restored-underlayer candidate. Current performance still requires a comparable measurement after visual review.

## Dry-ground material audit and bounded candidate

CPU inspection of the packed roughness/height arrays confirmed that roughness is R and height is G, with no sRGB
conversion on that array. The sixteen mean roughness values match their metadata; soil is approximately 0.920,
rock 0.904 and forest floor 0.838. Soil's P1/P50/P99 is 0.894/0.918/0.957. The maps themselves are not polished
surfaces, and no incorrect roughness-channel import was found. Evidence is
`.artifacts/terrain-roughness-inspection.log`.

The terrain's custom direct-light function did not read the fragment's `SPECULAR` material value. Its
`SPECULAR_AMOUNT` input belongs to the light, so lowering fragment `SPECULAR` had not reduced the direct sun's
white lobe. See the [Godot spatial shader reference](https://docs.godotengine.org/en/4.7/tutorials/shaders/shader_reference/spatial_shader.html).
The current candidate applies a documented `dry_reflection_weight` of 0.25 to both custom direct reflection and
sky reflection, with wet shore material and ice retaining the stronger response. The actual roughness
textures and wet-grain roughness remain intact. A separate local grass patch tint and reduced blade transmission
target lime coloring without changing cover density, plant placement, dimensions or LODs.

A subsequent source review corrected the base reflectance: Godot uses `0.16 * SPECULAR^2`, so `SPECULAR` is now
0.35355339 to match the custom direct lobe's F0 of 0.02 before both receive the dry multiplier. The initial candidate's
0.5 would have increased the sky's base F0 to 0.04. The `RADIANCE` override scales the sky path, not later SSR or
reflection-probe contributions; High disables SSR/SDFGI and terrain disables GI. This is a deliberately scoped
material response, not a claim of universal indirect-light control. The wetness mask covers sandy/shingle shore
substrates and does not add a general wet-soil simulation.

Terrain UV.x formerly stored height above the ocean even beside inland water. It now stores each vertex's height
above its cached local water level; the only consumer is terrain wetness. Water's separate depth coordinates,
terrain simulation heights and collision do not change. The default stream bank is several metres above the sea,
so this defect was real but did not explain the broad white dry-bank reflection by itself.

Build 13 compiled this candidate cleanly. `--hide-ground-cover` is a presentation-only diagnostic: it hides the
grass node without changing generation or readiness, allowing the exposed bank to be reviewed. Previous shader
copies are retained under `.artifacts/material-before-v3`, and candidate copies are kept separately for restoration.

The initial `environment-stream-day-bare-before.png` and `environment-stream-day-bare-after.png` attempts are
**invalid comparison evidence**. The first frame is 2560×1377 looking across the stream; the second is 1600×900
looking along it. The latter log also records a timeout followed by a menu-triggered rehost as Survivor. Runtime
`HollowUniverse.dll` hashes differ between the runs. Shader/input manifests preserve these facts; both processes
closed and candidate shaders were restored. The after frame still has a broad grazing-angle bright soil region,
so those attempts could not establish acceptance. No visual improvement percentage or controlled before/after
claim is made from these frames. The isolated build-14 comparison below supersedes them.

Latest broad validation reported by root is 376/376 engine-free checks passing in 418.2 seconds before this
material-only pass; build 14 subsequently compiled with zero warnings and errors.

## Accepted implementation checkpoint: controlled build-14 evidence

Build 14 isolates screenshot input, prevents GUI rehosting, fixes window size, avoids saving screenshot preferences,
and records actual camera/ground state. Three sequential fresh-save captures then passed: all 1600×900, Windowed,
FOV 75, ground state Playing, yaw/pitch zero, loading false, ready true, one host, and no startup/shader errors,
warm-up warnings, timeouts, reconnects or runtime-input hash drift. No screenshot used the fallback timeout.
The player position is identical; the camera position differs by less than 0.2 millimetres from float settling.

| Preserved image under `docs/images/abtest-b/repairs/` | Capture source under `.artifacts/` | Wall time | Review |
| --- | --- | ---: | --- |
| `environment-stream-covered.png` | `environment-stream-day-material-locked.png` | 38.3 s | Dense coverage retained; green is more restrained and native plant accents remain visible. |
| `environment-soil-before.png` | `environment-stream-day-bare-before-locked.png` | 37.2 s | Previous shaders produce a broad pale reflection across the exposed right bank. |
| `environment-soil-after.png` | `environment-stream-day-bare-after-locked.png` | 37.8 s | Candidate significantly reduces that white wash and reveals the soil detail; a grazing highlight remains. |

All runtime DLLs, ground JSON, cover assets and import settings match across the pair. Only the three intended
terrain/grass shader files differ. Runtime `HollowUniverse.dll` SHA-256 is
`65761054515F539CC649349845D6629BAEEF4D067AEC517AE1984565873EF977`. The restored terrain candidate SHA-256 is
`EE3B7E475EDDF12EF14AB7175EE516158E9E23B0B6F96185038BACD3F2F00C86`. Full input identities, camera telemetry,
warnings and session state remain in matching `.run.json`, `.log`, `.error.log` and
`environment-material-controlled-results.json` files. Shader swaps happened only after the previous process
closed; candidate files were restored after the baseline shot. This is a material comparison on the same compiled
local-water UV behavior, not a before/after measurement of the UV change itself.

Root reviewed all three and accepted this as the implemented material/vegetation checkpoint. Native accent count
and LOD distribution remain 11,523 plants, 313/2,492/8,718 LOD instances and 27 batches, confirming that the color
change did not hide missing coverage. Large flat legacy base blades remain obvious and prevent a final photorealism
claim. Coarse tree bark/crowns, legacy rounded rocks and the excessive night star field also remain visible limits.
Further art iteration is intentionally frozen at this accepted checkpoint while gameplay validation finishes.

Earlier reviewed evidence is also preserved without image alteration as `environment-underwater.png`,
`environment-substation.png`, `environment-cliff.png` and `environment-stream-night.png` in the same folder.
Each copy's SHA-256 was compared with its original screenshot. `environment-evidence.json` preserves these image
hashes, the three controlled cameras/session outcomes, common runtime-input hashes and per-run shader identities.
The night image documents the remaining star-field
limitation; the underwater image shows the accepted continuous underside repair. Current-density frame-time
measurement remains separate and pending; the earlier authored-only timing must not be presented as its result.
Monument puzzles, power activation and specialist loot remain outside the implemented environment scope.

## Remaining visual gate preparation

The offline `.artifacts/Run-RemainingVisualGates.ps1` batch discovers no invented scene flags: it uses the actual
AnimalSandbox `wolves`/`herd` scenes on the first planet, nine explicit R01 model catalogue keys, and the building
sandbox's `joints` tour. The latter is nine joint cameras across five grades, plus flicker and walking evidence:
73 JPEG files in total. Separate bounded processes, readiness/exit/error checks, runtime-input hashes, exact image
inventories/dimensions and diagnostic contact sheets keep failed captures from being presented as acceptance.
The installed bundled Pillow runtime is used without a new installation; original screenshots are not altered.

This audit exposed additional flat-world assumptions in the building capture fixture. `BuildSandbox` and its
joints setup now settle absolute planet-frame points directly instead of treating their X/Z components as offsets
from the landing site. Site search compares radial heights; spawn offsets, placement headings and camera-follow
up vectors use the local frame. Diagnostic lines record builder/joint origins, up vectors and ground clearance.
These are sandbox-only corrections and do not change the authoritative world generator or building asset geometry.
Root owns the coordinated build and actual visual gate; preparation alone is not validation.

Build16 ran the prepared batch in `.artifacts/visual-gates-20261015-201609-4476d695`.
The herd and all nine model processes completed at the locked resolution with no runtime errors or hash drift.
The herd is visibly grounded and readable; its inherited smooth animal models remain an art-quality limitation.
The wolf image is rejected: three animals had reached the dummy while a fourth was 42m away, allowing a mean-distance
predicate to declare the pack approaching. The narrow sandbox repair requires every wolf to be chasing 8–24m away,
frames the whole group from an elevated three-quarter camera, and holds the achieved simulation moment for a
screenshot so a minimum wait cannot miss it. Interactive animal simulation is unchanged. The final rerender
result is recorded below.

The joints tour produced no images and reached its 180s external deadline. Diagnosis found 81 requested ground tiles,
including `4/6/44/58` whose centre lies 334.26m from the first camera: `TileKey.Around` samples a square, while the old
terrain renderer evicted every tile beyond 320m, including that still-requested corner. It rebuilt and evicted the
same tile forever, preventing readiness. The renderer now retains requested tiles until the view moves; it does
not expand the request range. Readiness component diagnostics and a 45s explicit failure replace the silent wait.
The planet-frame builder starts 0.05m above ground, 8.49m from the site; all five foundation groups start 0.61–0.654m
above ground. The corrected joints tour completed on build17 in 18.1s with all 73 expected images, exit 0,
no runtime errors and no input-hash drift. See `REPAIR_BUILDING.md` for the actual visual review,
nonzero twig change-map result, preserved evidence and scope limits.

The first held wolf moment revealed a second fixture issue: it happened before the minimum one-second
simulation age, so holding simulation also held that age below readiness. Readiness now permits the held
valid moment while still requiring the world to be ready and the live all-wolf predicate to remain true.
Build18 captured at elapsed 10.07s (22.6s wall time), ready=True, exit 0, at the locked 1600×900 resolution.
All four wolves report visible Chase at 15.7–17.9m from the dummy; the complete dummy and pack fit the image.
Two wolves overlap in this particular view. One nonfatal ObjectDB instance leak warning at process exit
is retained in the evidence, rather than being described as a warning-free run. No fallback capture or
runtime input drift occurred. The accepted wolf18 and herd16 frames, exact source paths, hashes and run
telemetry are preserved unaltered under `docs/images/abtest-b/repairs/animals/`. They establish upright,
grounded actors and readable planet-frame scene composition, not final animal-model art quality.
