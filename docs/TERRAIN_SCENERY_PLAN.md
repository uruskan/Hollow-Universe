# Playable terrain, cliffs and vegetation plan

18 October 2026. Research and implementation sequence. T0 work and validation are recorded
in DEVELOPMENT.md; the full scenery repair is not complete. The module contract is
[WORLD_ENVIRONMENT_ARCHITECTURE.md](WORLD_ENVIRONMENT_ARCHITECTURE.md).
DESIGN.md remains authoritative, including Subsistence as the atmosphere reference, C#,
standard Godot 4.7.2 .NET, deterministic shared simulation, the 23.04 km² total planet
surface cap and preserved ground UI. This plan covers landforms, geological formations,
soil/vegetation transitions, ground cover, rendering, streaming and their validation.

## Decision

Retain the spherical heightfield and adaptive terrain tiles. Add a shared description of
complete geological formations and their environmental influence. Render near cliffs from
authored pieces with consistent materials and contact transitions; preserve their silhouettes
at distance. Rebuild grass submission around spatial tiles, compact instance lists and one
coverage policy. Use existing licensed assets first and identify missing shapes through review.

Mesh welding is an optional local asset/batching technique. Combining the current misplaced
pieces would preserve their gaps and repetition. A voxel-engine rewrite or custom GPU pipeline
is not the starting dependency. The first implementation uses engine-free C# rules and standard
Godot rendering facilities; a measured bottleneck must justify a more specialized backend.

## Evidence and corrections to the previous audit

- The audited Earth baseline is layout 5, terrain/save revision 4, radius 1,200 m: 18.0956 km² total,
  10.616 km² dry and 6.186 km² sampled open/gentle ground. Those are geography proxies,
  not approved scenery or guaranteed building sites. See [EARTH_TERRAIN.md](EARTH_TERRAIN.md).
- Cliffs are independent near/far scan instances with shared simplified collision. Current
  placement creates up to three columns by three tiers in roughly 48 m candidate cells;
  per-piece fitting does not establish full exposed-face coverage. Terrain materials and
  vegetation do not currently consume a coherent cliff-contact/soil description.
- The three 18 October placement failures have a concrete test-input mismatch. Production
  `WalkingWorld` applies `LayoutSeed(baseSeed, LayoutVariant)` before constructing cliffs
  and monuments. `EnvironmentDetailTests` and `GeologyPortTests` construct their independent
  comparison objects from the unmodified base seed. With layout 5 they compare different
  seeds. This is strong source evidence for a fixture bug, not a demonstrated generator race.
  The corrected production-input comparisons now pass; see the implementation checkpoint
  in DEVELOPMENT.md. The original failure did not establish a generator race.
- A separate defect existed: `WalkingWorld.Compute` read stack-allocated relief weights
  on the offshore path without explicitly initializing them. These are now cleared and
  complete samples are checked. A stricter sample test also exposed biome weights exceeding
  one when ice and alpine overlap; normalization is repaired under Earth save revision 5
  (other walking worlds 2), preserving the terrain seed and height golden.
- The latest broad suite also aborted inside Jolt mesh-shape creation. Investigate its
  lifetime/concurrency context separately; no evidence yet assigns the crash to cliff art.
- [GRASS_INVESTIGATION.md](GRASS_INVESTIGATION.md) identifies compounded coverage reduction,
  authored-root contact problems, conflicting silhouettes/palettes, excessive normal smoothing,
  absent direct specular in custom lighting and whole-face geometry submission. Its Maximum
  estimate of 3.28 million grass/litter vertices per active face is a source count, not a GPU
  timing. Upward-normal blending itself is a valid technique; its strength and animation need work.
- The audited saved Low settings compound appearance problems. Comparisons use explicit
  per-run settings and retain the user's preferences. No preset change can count as a repair.

## Production lessons and their limits

| Primary source | Documented practice | Application here |
| --- | --- | --- |
| [Rust World Update 2.0, Facepunch, 2024](https://rust.facepunch.com/news/world_update_2) | Distinct coastal and inland cliffs, deliberately traversable ledges/slopes, larger formations and new landscape features. | Compose formation families around location and traversal, then vary them procedurally. |
| [Rust Devblog 62, Facepunch, 2015](https://rust.facepunch.com/news/devblog-62) | Rock/terrain contact blending, limits on where that blend applies, terrain/grass tint corrections and simpler rock shaders where blending is unnecessary. | Bound contact shading and share environmental colors. This is historical implementation evidence, not a description of every current Rust shader. |
| [Procedural World Generation of Far Cry 5, Ubisoft/GDC, 2018](https://www.gdcvault.com/play/1025557/Procedural-World-Gen) and [terrain rendering talk](https://www.gdcvault.com/play/1025480) | Procedural tools coordinate biomes, terrain textures, water and cliffs; the terrain renderer supports heightfields plus additional geometry. | Keep our useful heightfield while giving cliffs, soil and plants a common rule set. |
| [GPU-Based Procedural Placement in Horizon Zero Dawn, Guerrilla, 2017](https://www.guerrilla-games.com/read/gpu-based-procedural-placement-in-horizon-zero-dawn) | Artist-authored placement rules feed runtime environment generation. | Put artistic controls in reusable data; seed variation operates inside those controls. |
| [No Man's Sky Worlds Part I, Hello Games, 2024](https://www.nomanssky.com/worlds-part-i-update/) | Environmental-object rendering moved to a GPU-based system; distance detail and terrain meshing were improved. | Animation in a vertex shader is only one part of performance. Visibility, submitted geometry, LOD and streaming need independent budgets. The page does not identify the exact culling algorithm. |
| [No Man's Sky Worlds Part II, Hello Games, 2025](https://www.nomanssky.com/worlds-part-ii-update/) | Reduces repetition within planets and introduces new terrain settings in new systems while preserving existing terrain/bases. | Judge within-world variety and explicitly version physical generation changes. |
| [Ghost of Tsushima production account, Sucker Punch](https://blog.playstation.com/?p=345372) | Procedural grass and modeled accents use coordinated wind, also shared with trees and bushes. | Retain a procedural ground layer and authored accents with common habitat, palette and wind inputs. |
| [Godot MultiMesh guidance](https://docs.godotengine.org/en/stable/tutorials/performance/using_multimesh.html) | One MultiMesh has no individual-instance frustum culling; multiple area-bounded batches are recommended. | Use compact local vegetation tiles with accurate bounds instead of huge whole-face batches. |

These are transferable production principles. The design below is our proposal, not a claim
to reproduce proprietary internals. No reference-game assets enter the repository. Subsistence
remains the intended atmosphere; the other games inform engineering and composition.

## One generator for many planets

Planet identity, generator revision and layout determine a stable seed. Physical/gameplay
parameters determine permitted environments: radius, temperature, liquids, atmosphere,
composition and biosphere. Shared geology/ecology profiles convert those inputs into landform families,
substrates and plausible vegetation. Seeded sampling chooses arrangements and variations.

For example, a wet temperate profile with suitable life can support weathered cliffs, soil
pockets, moss and grasses; a dry profile can use exposed bedrock and debris; a frozen or airless profile should
not inherit Earth's grass merely because its ground is gentle. These are profile examples,
not newly approved planet biology. Exact profile mappings must follow available planet data.
Galaxy data already distinguishes biosphere levels and water/ammonia/hydrocarbon/magma
liquids, while `WalkingWorld.For` currently forwards only ocean cover, ice and temperature.
Carry the relevant eligibility data into shared surface rules: wet and temperate alone must
not populate a sterile world with Earth plants, and non-water liquids need appropriate exclusions.

Earth's explicit layout selection is an override, not the universal generation strategy.
No per-planet hand tuning is required for ordinary planets. Use named deterministic substreams
for terrain, formations, sites and decorative variation so one concern does not arbitrarily
reroll the others. Graphical quality and missing optional visual assets must not change
authoritative geometry, persisted identities or shared placement.

Generate compact planet data when a world is needed; stream fine terrain, collision and
vegetation around active players. The earlier `WalkingWorlds` static full-world dictionary
has now been removed. `WorldStore` provides explicit ownership, estimated admission budgets
and inactive-world eviction, with simulation, prediction and pending readers pinning their
data. Cliff and natural-object caches now retain at most 384 entries each, including pending
and empty results; this does not independently cap caller-owned executing work. Reopening
an evicted world must reproduce it exactly; current focused eviction/revisit checks pass.
Keep player changes in persistence independently of generated caches. Connected interplanetary
travel remains its separate implementation; exercise this lifetime contract in a harness now.

## Work packages and completion gates

### T0 — Establish correct generation and a trustworthy baseline

1. Compare two independently constructed `WalkingWorld.For` worlds with identical profiles
   in the failing tests, including variants 0 and 5. Do not duplicate seed plumbing or alter
   production geography to satisfy the old fixtures.
2. Initialize offshore relief fields explicitly. Check finite/intended values and stable
   heights, water, materials, sites and formations across cold/warm instances, tile order,
   parallel requests, poles, cube seams and shorelines. Run sensitive checks in Release too.
3. Reproduce/classify the Jolt abort with bounded physics-streaming/lifetime checks. A build
   alone is not completion, and an unrelated native abort cannot be hidden by a green subset.
4. Pin source/assets and record a current normal hosted route. Independently toggle procedural
   grass, litter and authored plants; report submitted instances/triangles, visible tiles,
   generation/upload time, upload bytes, memory and CPU/GPU frame timing for each workload.

Gate: corrected focused checks pass, unresolved broad-suite failures are explicitly attributed,
and at least one current hosted baseline completes loading and the route without a native crash.
There is no assumption that the three fixture corrections alone resolve all generation issues.

Status 19 October: item 3 is classified and fixed. The Jolt abort was an unchecked native allocation
failure after the binding leaked every physics system, body and shape (about 16 MiB per world); see
KNOWN_PROBLEMS.md. Items 1 and 2 were completed on 18 October. Item 4, the measured hosted baseline
with separate grass, litter and authored-plant ablations, is next.

### T1 — Survey landforms and define the visual target

Use the whole current Earth survey to locate coastal scarps, inland slopes, plateaus, mountain
faces, wooded edges and shores. Inspect large silhouettes first, vegetation masses second,
ground detail third. Diagnose the regularly scalloped soil-wall report at reproduced coordinates
before deciding whether its cause is relief shape, lake rim, LOD skirts or cliff placement.

Define a continuous normal-play benchmark through meadow, forest edge, water bank, inland rock
and coast, with extra sites where one route cannot cover all landforms. Retain open construction
areas, navigable approaches, visible resources and monument access. Select three initial formation
recipes: coastal scarp, inland outcrop and plateau/mountain face. Freeze macro layout for the art
comparison; propose a deliberate versioned shape change only if the survey demonstrates a need.

Gate: reference targets, current before-images, site identities and the problems each target
addresses are recorded. A chosen scenic camera cannot substitute for normal movement through it.

### T2 — Generate complete cliff formations

Replace unrelated local rock stacks with deterministic formation records describing crest,
exposed face, toe/base apron, usable ledges, material family and exclusion footprint. Canonical
tile ownership and bounded neighbor queries avoid duplicates or broken formations at cube seams.
Trace landform regions into bounded sections; evaluate the whole face rather than each piece alone.

Compose larger authored masses first, then secondary breaks and restrained rubble. Vary profiles,
offsets and compatible shapes without visible regular rows or arbitrarily stretched textures.
Use the licensed scan library, adding original transition/cap/base pieces only where needed.
Each recipe has an exposed-rock target: some slopes legitimately contain soil, so coverage does
not mean covering every steep square metre in rock. Failed fitting must report an uncovered
intended region rather than silently claiming success.

Keep render and simplified collision transforms paired. Add formation-level far geometry or
proxies where silhouettes matter beyond the existing 340 m range; set their distance from screen
size and measured cost. Local merged assets may help batching, but retain useful culling units.

Gate: complete crown/face/base compositions, believable scale, correct contact and walkable
approaches at inspected sites; stable IDs/collision across seams; no hard silhouette disappearance
when walking or flying through the near/far boundary.

### T3 — Make rock, soil and vegetation share environmental data

Extend the existing cover/material system with formation influence: exposed bedrock, debris,
soil pockets, moisture and plant support. Establish a one-way dependency:

`base relief/drainage → site pads/exclusions → formations/support → materials and scatter → rendering`

Formation fitting reads finalized terrain/site exclusions, not vegetation or a material query
that recursively asks for formations. Rules use radial up and body-space coordinates correctly.

Build transitions appropriate to the profile: rock face → broken stone → stony soil → patchy
plants → established cover. Place soil in plausible ledges/depressions; keep steep faces exposed.
Use explicit mesh support/contact metadata or cached shared surface queries for ledge plants;
a terrain height alone cannot establish support on a cliff mesh or beneath an overhang.

Reuse existing triplanar steep-rock and material-height blending. Match terrain and cliff substrate
scale, color, normal strength and roughness; bound blending to relevant contact zones instead of
painting soil indiscriminately over rock tops. Share moisture/canopy palette with grass and authored
plants while preserving species albedo. Ground litter and moss are habitat choices, not a uniform
green fade. Trees, shrubs, deadwood, stones and clearings should form readable groups and gaps.

Gate: contact looks connected in daylight, grazing sun and shade; plants have real support;
water, rock faces and site pads have correct exclusions; resources and routes remain readable.

### T4 — Repair grass coverage, contact and lighting

- Apply habitat coverage once. Separate occupied area, species mix, blade shape and artistic patch
  variation rather than multiplying several density reductions into unintended bare ground.
- Make both procedural roots and authored crowns follow the same terrain triangle convention.
  Remove unconditional deep burial; fit wide root zones and reject/rescale incompatible creases.
  Keep stems plausibly upright. Validate against the actually rendered, morphed terrain.
- Use one deliberate silhouette policy; eliminate severed tips from conflicting masks and inspect
  mipmapped cutouts in motion. Preserve depth-writing cutouts and verify shore/refraction behavior.
- Let near normals follow blade bending, with restrained cluster/up smoothing at distance.
  Implement bounded direct specular/transmission in custom lighting, with roughness that prevents
  glitter. Upward smoothing is tunable, not categorically wrong.
- Share environmental tint, wind and terrain-to-grass distance handoff across both plant layers.
  LOD reduces segments and complexity while preserving apparent coverage; it should not flatten
  the field into a shrinking-height ring. Test stable subsets and bounded width compensation.

Gate: roots sit correctly, tips remain connected, moving populations are stable, and meadow,
forest and rocky transitions remain distinct. Check both High and the user's Low settings.

### T5 — Bound rendering and generation cost

Build compact surviving-instance lists on C# workers per stable vegetation tile. Compare roughly
8 m and 16 m tiles before selecting a size. Pool tile/species/LOD MultiMeshes, use conservative
bounds including authored mesh extent, maximum wind and interaction bending, and upload only
entering/invalidated tiles. LOD/preset changes, terrain/support revisions and origin rebasing
must update the relevant buffers/transforms: prefer prebuilt LOD buffers and visibility changes
or bounded rebucketing. Version jobs so stale results cannot publish after tile/world changes.
Keep camera selection
separate from deterministic placement. Audit authored middle LODs for meaningful simplification.
Preserve non-overlapping cell ownership at face seams and retain coherent far terrain coloration.

Use nested stable LOD subsets to prevent churn, avoid unnecessarily drawing two dense LODs at
once and cap work per frame with explicit readiness. Worker cancellation, main-thread upload
and unload must share lifecycle ownership. Add bounded world/formation caches described above.

Gate: meet habitat-specific coverage targets at equal-or-lower total vegetation GPU cost and no p95
streaming regression, using confidence from repeated runs. The identified local GPU is an RTX
2070 SUPER; start at 1600×900 native High and separately check Low, then stress Maximum.
These are benchmark settings, not changed preferences or a promised FPS. Record a numerical
budget after T0 and before candidate comparison; report total frame cost as well as layer cost.
Rocky slopes and forest floors retain deliberate gaps; more covered pixels alone is not the goal.
Use three warmed route runs per version plus cold-start/turn/tile-crossing observations. Restore
baselines between layer ablations. A custom compute backend is a later option if compact C#
tiles still leave a measured submission/upload bottleneck.

### T6 — Roll out beyond the showcase and across planets

Use an initial fixed regression matrix of eight environment cases × six seeds (48 small,
bounded runs), including inhabited wet temperate, sterile temperate, dry, cold, high-relief,
water-dominated, airless and non-water-liquid cases
supported by current planet data. These are test cases, not six final planet categories. Include
small/maximum radii, poles, cube seams and boundary conditions; expand only for a found defect.
Reserve some seeds from tuning for an independent final check. Sequential processes and released
worlds prevent test-cache growth from becoming an unrelated machine-wide memory failure.

Survey Earth globally for formation exposure, repeated layouts, unsupported plants, route/access
obstruction and ecological mismatches. Human-review representative ordinary-play clips from
multiple regions and planet profiles. Compare repeated independent worlds, eviction/revisit,
client/server collision, save round trips, actual resource interaction and low-altitude flight.
Test two active world handles to establish lifetime isolation without claiming interplanetary
travel is implemented. Quality presets must not change collidable terrain or harvestable IDs.

Gate: required build/full suite passes; otherwise the work remains incomplete, with failures recorded.
current hosted screenshots and motion clips pass visual review; the planet matrix, memory and
frame-time reports pass. Report remaining unsupported planet appearances honestly. A good Earth
shot is the first benchmark, not completion of reusable world generation.

## Ownership, saves and integration

| Owner | Exclusive scope after interface agreement |
| --- | --- |
| Generation | WalkingWorld/parameters, formation/support/cover contracts, deterministic placement and engine-free tests. |
| Geology art | Cliff shapes, transition pieces, materials, export metadata and provenance; coordinate collision changes with generation. |
| Vegetation renderer | GrassField parts, procedural/authored shaders, compact tiles, LOD and diagnostics; consume the frozen shared contract. |
| Root/integration | Route harness, lifecycle/cache integration, protocol/save boundaries, documentation, serialized builds/imports/Godot runs and final review. |

T0/T1 precede art changes. Agree T2/T3 data contracts before parallel renderer/asset edits.
T4/T5 develop together on the same captured sites; T6 is mandatory before declaring the system
complete. Heavy jobs remain serialized and use fresh resource checks, retaining existing user
apps and saves. Coordinate around current owners; do not overwrite the mixed working checkout.

Fixture-only and purely cosmetic fixes need no terrain reset. Changes to physical heights,
collidable cliffs, monument pads or persisted spatial identities require deliberate goldens,
terrain revisions for every affected body and compatible network identity. Incrementing the
cliff class's generation constant alone is insufficient. Seed-generation version and save
compatibility revision have separate roles: changing `TerrainRevision` alone does not reseed
the landscape. Existing saves remain preserved and
incompatible spatial state is refused. New diagnostic runs use explicit fresh database paths.

Every package records source identity, evidence and remaining limitations in DEVELOPMENT.md.
Technical checks establish reproducibility and runtime correctness; visual acceptance still
requires inspecting normal play in motion. This research pass changed documentation only and
ran no build, tests, asset export/import, game or benchmark.

## Concealment bushes investigation — 18 October 2026

The owner supplied four Rust references and requested bushes, especially around monuments,
so players can hide inside them. That need is confirmed intent. The implementation and
tuning below are proposals; this investigation does not add bushes to the running game.
Subsistence remains the environment/atmosphere reference in DESIGN.md ART-02.

### Meshes, shaders and gameplay are separate concerns

A bush can be mesh geometry, have animated foliage materials, have no solid collision and
have no chopping action. Leaf cards are small polygons carrying cutout leaf textures; they
are still meshes. A shader supplies their shading, transparency cutout and wind motion.
Removing an instance when a foundation occupies it does not imply shader-only vegetation.

Primary Facepunch sources establish the relevant Rust behavior:

- [Devblog 198](https://rust.facepunch.com/news/devblog-198) describes taller-than-player
  willow and spicebush vegetation explicitly intended for hiding inside its canopy.
- [Vehicle Update](https://rust.facepunch.com/news/vehicle-update) describes foliage shaders,
  tree/bush wind animation, material changes and distant billboards. Together these support
  a geometry-plus-material interpretation, not identification of every current asset's internals.
- [World Revamp, 6 May 2021](https://rust.facepunch.com/news/world-revamp) documents removing
  bushes when building over them and replenishing them at suitable map locations using the
  tree population system. It also records biome-dependent monument foliage and bush billboards.
  This is published behavior, not proof of today's exact clearing shape, delay or save format.
- [Above-ground Trains Update](https://rust.facepunch.com/news/above-ground-trains-update)
  describes larger bush clusters beside roads/rails with deliberate dense and open areas.
- [Stay Alert, 1 August 2024](https://rust.facepunch.com/news/stay-alert) documents nearby
  players hearing bush rustles, with slower movement producing quieter sounds.

The supplied pictures demonstrate the desired leafy volume, undergrowth and sightline breakup.
They cannot establish collision, server entities or rendering technique. The unchoppable
behavior is the owner's observation; this audit did not independently playtest Rust.

### What is missing in our active ground route

| Finding | Current source evidence | Consequence |
| --- | --- | --- |
| Ordinary bushes already exist in generation and presentation. | `src/Hollow.Surface/Scatter.cs:208` generates `ScatterKind.Bush`; `game/scripts/Surface/Flora/FloraLibrary.cs:144` registers three variants; `FloraGeometry.cs:518` constructs stems and leaf-card geometry. | This is partly a missing connection, not an entirely absent vegetation system. |
| Normal ground play drops that scenery. | `game/scripts/Ground/GroundView.cs:423` chooses `NaturePlants`; its `Tile` at line 49 enumerates only `INaturalObjects`. `src/Hollow.Simulation/Nature/ScatterNaturalObjects.cs:15` explicitly excludes unmapped scenery; `gather.json` maps berries but no ordinary bush. | Surface look-dev can show bushes while normal ground play cannot get them through this provider. Berry bushes are a different case. |
| Monument clearance is coarse. | `src/Hollow.Surface/MonumentPlacements.cs:119` returns zero vegetation within `At(direction, 2)`; the definition's radius is already its mesh bounding circle plus a margin (`MonumentDefinition.cs:65`). | Entire monument circles lose generated vegetation, including otherwise usable exterior pockets. No authored planting-pocket contract was found in this path. |
| Buildings have no bush-clearance connection. | `NatureView.cs:176` through its frozen draw filter handles depleted/falling nature, not foundation occupancy. `GrassField.cs:491,542` excludes water/monuments; `BuildingSystem.OnBuildingChanged` does not publish foliage clearance. | Restoring bushes alone will not implement foundation removal. Ground-cover clearance should share the eventual policy. |
| The existing bush LOD policy cannot guarantee concealment. | `FloraLibrary.cs:144` uses 30/120 m bands with no impostor. `Vegetation.cs:34,604` multiplies these by `PlantDistance` and shrinks the final mesh over the last 18% of range. | Low preset bushes disappear at 90 m; Maximum at 156 m. If restored unchanged, observers could see different concealment. |

The current procedural shrub uses H=1.25 m as a shape parameter, 13 near leaf cards and
four far cards; the cards extend around their centers and scatter scales vary. H is not
the measured final mesh height. Neither this geometry nor its independently generated
far silhouette has been validated with a player hiding in it. Simply increasing density,
enabling the existing mesh or raising the user's graphics settings would not complete this feature.

Two integration traps need explicit handling:

- Do not register decorative bushes as the existing gather class `bush`: that class is
  pickable (`Content/Tables.cs:121`), and an empty yield still does not make it scenery.
  `NaturalState.Pick` can deplete it; `NatureView.Picked` keeps only berry variants as bare
  bushes and hides other kinds. Existing `PlanetGroundTests` intentionally excludes plain
  bushes from the resource provider.
- Do not blindly append raw scatter items to `NaturePlants`. Its item index is an index
  into the filtered natural-object array, and `Id` resolves it that way. `SurfacePlants`
  uses scatter kind/candidate indices instead. A composed plant source needs explicit,
  stable source/ID ownership so chopping a tree cannot hide unrelated scenery and no
  harvestable is drawn twice.

### Proposed integration with T3–T5

1. Extend the shared deterministic plant-source contract to expose scenery alongside
   authoritative resources, preserving existing resource identities and renderer ownership.
   Keep biome/planet eligibility and placement engine-free. Use data to distinguish
   decorative detail, concealment vegetation and harvestable plants; do not create fake
   gather targets or a new switch chain for each bush species.
2. Start with a few leafy shrub/thicket shapes tall and dense enough for the intended
   standing/crouching concealment. Use clustered placements at woodland edges, rock bases,
   appropriate banks and monument approaches, with open routes and deliberate sightlines.
   Give monument definitions local planting regions and hard exclusions for structures,
   doors, loot access and paths, rather than relaxing the whole site exclusion indiscriminately.
3. Give concealment foliage a shared minimum visibility/coverage policy across presets.
   Lower settings may simplify geometry, shadows and wind; they must preserve comparable
   silhouettes/opacity while the hidden player remains observable. Evaluate scopes and
   distant views too. Keep near/far coverage consistent rather than shrinking bushes away
   at arbitrary quality-dependent ranges. The required range and budget remain to be measured.
4. Add one shared scenery-clearance policy using foundation shapes, rotation and vertical
   bounds. Elevated floors or a ship flying overhead must not clear ground vegetation by
   an unbounded vertical projection. Derive suppression from authoritative building state;
   update dirty vegetation/grass tiles through immutable snapshots on placement, removal,
   replication and reload. Clearing data must reach every observer who can see the foliage,
   even if the associated building renderer is culled. Keep this separate from harvested
   nature's depletion timer. Return after demolition versus delayed/local or relocated
   regrowth is an open rule, not an automatically adopted Rust population mechanism.
5. Recommended gameplay: ordinary concealment bushes are passable and non-harvestable;
   leaves obscure vision but do not stop bullets. Physical obstacles remain separate.
   Any future AI/turret vegetation sensing must be an explicit shared rule, not an accidental
   solid collider. Movement-sensitive rustles audible to nearby players are a useful optional
   stealth tradeoff and should use the existing audio/network event systems.

[The local asset audit](ASSET_LIBRARY_AUDIT.md#4-environment-palette-available-locally)
already identifies downy willow, dwarf birch and other shrub candidates. Their dimensions,
canopy coverage, wind setup, LODs and applicable asset entitlements need checking before
selection. No asset was imported or selected here; original procedural meshes remain a
useful integration fixture. No Rust assets are needed.

Acceptance requires a normal hosted scene, not just the surface viewer: two players on
different presets, standing/crouching inside foliage, observer views from several directions
and distances, scopes, daylight/backlight and movement. Check square/triangle/rotated/raised
foundations, demolition, late join and save reload; verify bullets, walking and resource
interactions. Exercise stable seeds, tile seams and planet eligibility. Measure geometry,
alpha overdraw, shadows, CPU/GPU time and tile-update cost before expanding populations.
Run the solution build and relevant/full tests for implementation, and retain screenshots
and clips. This source investigation ran none of those runtime validations and makes no
performance or visual-completion claim.
