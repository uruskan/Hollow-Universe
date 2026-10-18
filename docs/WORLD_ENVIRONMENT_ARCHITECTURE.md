# Playable world environment architecture

18 October 2026. Implementation contract for the authorized terrain/scenery work.
This describes the target and migration; it does not claim that the target is built.
[DESIGN.md](DESIGN.md) is authoritative. Research, source citations, reference limits and
the T0–T6 delivery sequence are in [TERRAIN_SCENERY_PLAN.md](TERRAIN_SCENERY_PLAN.md).

## Requirements and decisions

**Confirmed:** at most 1,000 stars; at most 23.04 km² total surface per playable planet;
Earth start; existing ground UI; C# and engine-free deterministic simulation; standard
Godot 4.7.2 .NET; natural cliffs and soil/vegetation transitions; repaired grass;
concealment bushes, especially around monuments. Subsistence remains the atmosphere
reference. Other games inform techniques; their assets do not enter this project.

**Technical decisions for implementation:** retain the spherical heightfield and add
authored physical formations; use one shared environment description; separate physical
identity from cosmetic variation; own worlds through explicit session lifetimes; use
data-driven rules and renderer capabilities; validate the normal hosted gameplay path.
The same generator serves eligible planets through resolved metadata, not Earth defaults.

**Candidates requiring measurement or visual review:** survey resolution, formation
length/piece limits, vegetation tile size, LOD ranges, contact tolerances, memory/work
budgets and selected recipes. Initial values in the plan are experiments, not measured
limits. Biology beyond available design/planet data and post-demolition bush regrowth
remain open rules. No voxel rewrite or custom GPU backend is an initial dependency.

## Current implementation and target

| Concern | Current / work in progress | Target / completion evidence |
| --- | --- | --- |
| Terrain | `WalkingWorld` heightfield, drainage and monument pads; Earth layout 5. Biome normalization advances save revision to 5 (other walking bodies 2), without a landform reseed. | Preserve approved geography unless a documented defect needs a versioned change. |
| Correctness | T0 fixture seed corrections, explicit offshore relief initialization and normalized biome weights are implemented; independent construction/order/thread checks pass. | Broad validation and remaining failures recorded in DEVELOPMENT. |
| Planet lookup | `PlanetBodies` resolves metadata without constructing terrain. `WorldStore` and explicit leases replace static full-world caches; server, client, views and admitted jobs own their pins. | Integrated lifetime checks pass; native/resource memory remains separately accounted and under investigation. |
| Retention | `WalkingWorld` thread-local sample cache now retains an identity token without a world back-reference; failed body lookups are not retained. | No hidden owner keeps an inactive world alive. |
| Terrain lifetime | `TerrainLod` owns a separate build session per setup; stop/reset retire jobs and resources. Native probe covers reset, stop, required failures and discarded late failures. Physics disposal drains all pending workers before reporting failures. | Native probe and controlled physics lifetime checks pass; this does not establish long-session memory bounds. |
| Cliffs | Live generation still uses up to nine fitted scan pieces per candidate and simplified collision. Inactive formation records, qualified support geometry and substrate contracts have synthetic checks; actual scan qualification and planning are in progress. | Activate complete formations only after asset correspondence, fitting, coverage, collision and visual validation. Soil transitions remain unfinished. |
| Grass | Whole-face baseline remains default. Opt-in spatial batches compact conservative survivors with stable cell identities and bounded staging; native lifecycle checks pass. | Measure baseline/candidate rendering, then finish coherent coverage, LOD, contact and lighting before promotion. |
| Bushes | Raw scatter has ordinary bushes; hosted `NaturePlants` exposes only mapped natural objects. | Stable composed plant sources, concealment policy and building clearance: not implemented. |
| Compatibility | Terrain save checks and protocol compatibility already exist. | Full environment descriptor/wire negotiation and separate revision domains: not implemented. |

Implemented rows describe the delivered foundation, not scenery acceptance. Exact
build/test/capture results and remaining failures belong in [DEVELOPMENT.md](DEVELOPMENT.md).

## Identity, metadata and contexts

Use the following named contracts; names describe responsibilities before APIs settle:

| Contract | Contents and invariant |
| --- | --- |
| `WorldAddress` | System address + body path; validated as untrusted input. Names never select identity. |
| `PlanetEnvironmentDescriptor` | Universe seed, address, game radius, physical planet metadata, resolved profile IDs, explicit layout override, seed schema/revisions, required physical-content fingerprint and gameplay-visibility signature. Immutable, canonically serialized. |
| `GenerationContext` | Descriptor plus required immutable rule/geometry catalogs and deterministic domain seeds. No Godot, camera, quality, saved graphics settings or wall clock. |
| `EnvironmentSession` | Session-owned store, world leases, work queues, budgets and diagnostics. Server/client roles may share a store only through explicit ownership. |
| `PresentationContext` | World lease, render origin, quality, appearance revision, visibility and upload budgets. It selects representations without changing the world. |

A metadata resolver validates address, body type and requested radius, reads generated
system/body metadata and returns a descriptor without constructing `PlanetSurface`,
drainage, terrain tiles or render assets. Invalid addresses return a bounded failure;
negative caching must also have a limit. Do not maintain an unbounded global object graph.

Carry atmosphere, relevant composition, liquid kind and biosphere eligibility as well
as temperature/ocean/ice. Wet temperate sterile worlds must not acquire Earth plants.
Water, ammonia, hydrocarbons and magma are not interchangeable moisture inputs. Profiles
declare their supported metadata and reject unsupported combinations explicitly.
Diagnostic overrides are immutable descriptor inputs, visibly identified in evidence;
environment variables must not silently change one peer after descriptor resolution.

## Seeds, revisions and persistence

Derive stable named domains through `Hash64`, `Rng` and `DMath`: raw terrain, drainage,
sites, geology provinces, formation layout, physical piece fit, gameplay plants,
concealment plants and cosmetic detail. Document numeric domain tags in one registry.
Keep legacy domains unchanged while migrating an unchanged stage. Include a stage
revision only when intentionally changing that stage; never reuse a cosmetic seed for
physical placement. Preserve today's Earth seed mapping as schema 1; including universe
seed in new streams requires a deliberate new schema, goldens and compatibility decision.
Derive each candidate/slot stream from its stable key, not the
number of previously accepted objects. Catalog iteration is by stable explicit key.

Distinguish revisions even if the first compatible release stores a composite value:

- **Terrain/physical:** heightfield, site grading, solid transforms/collision, support
  geometry and physical exclusion policy. Changes invalidate affected spatial state.
- **Population/gameplay:** persistent resource and concealment identities/placement,
  occupancy behavior and related rules. Require explicit compatibility/migration policy.
  Concealment silhouettes, alpha coverage and minimum LOD/visibility-distance policy
  belong here even when implemented by a render asset or shader.
- **Appearance:** cosmetic tint/lighting maps, shading, wind and detail/LOD that do not
  change physical behavior or required concealment. No physical reroll.
- **Schema/protocol:** representation and negotiation changes, separate from generation.

The physical fingerprint covers canonical required recipes, collision/support data and
their revisions; exclude timestamps, absolute paths and texture bytes. Missing required
data fails validation. Optional missing visual assets cannot alter physical placement.
The separate gameplay-visibility signature covers approved concealment representations,
including alpha-coverage assets and minimum distance/LOD policy. Peers with matching
placement but incompatible concealment must not silently join as compatible clients.
Validate this policy across presets; ordinary cosmetic texture changes remain independent.
The future server descriptor is authoritative; clients verify it before prediction.
Until that wire contract exists, use the existing compatibility fence and explicit shared
build/profile inputs. Never claim matching local settings establish a negotiated identity.

Persist player changes independently of generated caches. Preserve incompatible saves
and refuse them clearly; do not silently reproject buildings, ships or sleepers. A later
migration must specify each affected state. Fresh diagnostics use explicit save paths.

## Session store, leases and budgets

`WorldStore.Acquire(descriptor)` returns a disposable lease over one immutable generated
world per descriptor identity. Active simulation, prediction, render consumers and jobs
hold leases. A pending job acquires its lease before dispatch and releases it exactly once
on completion, failure, cancellation or scheduling rejection. A lambda's `finally` alone
is insufficient if cancellation prevents the lambda from starting. Sharing a raw world
reference is not ownership transfer.

Track resident generated bytes, reserved job bytes, pending result bytes, tile caches,
native/render allocations and peak usage separately. Account for build scratch memory,
not only retained output. Define configurable session limits for each, concurrent jobs,
queued work and main-thread uploads; establish numbers from T0 measurements before rollout.

Only zero-lease inactive worlds may be evicted. Use deterministic content regeneration;
LRU eviction order need not affect output. Under pressure: stop speculative requests,
evict unpinned caches, then defer work. Do not free active collision or silently switch
physical detail. Report an explicit capacity/readiness failure if essential work cannot
be admitted. Never let a rejected job reserve memory indefinitely.

Session shutdown closes admission, increments its epoch, cancels work, awaits retirement,
then releases dependent native resources and the store. Physics shape/world removal must
follow the physics owner's safe phase. A node exiting the scene is not proof that worker
or native users are finished. Metadata-only lookup, two-world coexistence, cancellation,
eviction/revisit and repeated session teardown are required lifetime checks.

The source ownership audit is retained in
`.artifacts/environment-architecture/worker-t0/worldstore/WORLDSTORE_LEASE_HANDOFF.txt`
with 72 source hashes. Migration must include landing bootstrap, navigation, all vegetation
jobs, abandoned map jobs, retained client map/nature references and keyed bot sharing.
Do not activate integrated eviction until those readers hold leases or have proven owner
retirement barriers. Disposed lease handles must clear their world-bearing references.
Until authoritative descriptor negotiation exists, multiplayer must reject variants that
cannot be reconstructed from the current wire data and shared build. Store construction
support alone does not authorize a new multiplayer generation identity.

## One-way generation stages

```text
descriptor + required catalogs
  → raw terrain / drainage / climate / geological provinces
  → protected sites and graded heightfield
  → formation plan → physical pieces, caps and aprons
  → support-surface queries
  → substrate and ecology
  → gameplay + concealment populations / cosmetic detail
  → collision consumers / presentation and LOD
```

`Fields()` remains a raw field query; `HeightAt()` remains the graded radial heightfield.
Monuments and landing selection read raw suitability/forest potential, not final ecology.
Formation fitting reads the graded terrain and protected sites, never `Cover()` or final
scatter. Support sees finalized terrain/formations. Substrate may describe apparent soil
depth, but cannot add physical geometry after support has been published. A physical soil
bank belongs in the formation stage and participates in collision/versioning.

Do not redefine `HeightAt()` as the highest cliff hit: overhangs have multiple surfaces.
The new support query is explicit. No lazy callback may point backward through this DAG;
caches memoize completed pure stages and do not decide acceptance from arrival order.

## Geology, formations and source geometry

Separate geological/material family, formation type and ecological weathering. Current
labels `coastal`, `sandstone`, `nordic` and `forest` mix those concerns. A source filename
is not evidence of lithology; retain an honest appearance-family label until verified.
Planet profiles select compatible families across coherent provinces, not unrelated
materials independently per small cell. Biome affects weathering, not rock identity.

`FormationRecord` contains canonical ID/owner, recipe/family keys, crest/face/toe guides,
bounded physical pieces, contact/support regions, conservative bounds and diagnostics.
`FormationPiece` has a stable role/slot, geometry key and body-frame transform. Rejected
slots do not renumber survivors. Array position is not persistent geometry identity.

Candidate planning is finite. A whole-planet coarse graph is a practical option at our
surface cap, followed by bounded local refinement and lazy realization. Level 6 contains
24,576 cells (~29.5 m nominal edge on Earth); this is a candidate, not a proven cost.
Measure cold construction, scratch/resident bytes and cancellation before selecting it.
If it exceeds budget, partition with fixed halos and canonical boundary ownership while
retaining the same plan semantics; never allow recursive unbounded neighbour generation.

Use exact cross-face edge adjacency and canonical cell/edge keys. Survey exposed bands,
coasts and site constraints; refine narrow features; trace and split bands into bounded
sections with stable branch/tie rules. Compose large masses, corners, ledges and toes,
then secondary breaks/debris. Resolve conflicts in stable plan order before publication.
Use bounded attempts, pieces, probes and spatial extent from each recipe. Do not force
bad fits to satisfy density. Record uncovered intended face regions for asset/recipe repair.

Index actual formation bounds, including offset pieces and influence reach. Every query
deduplicates and sorts canonical IDs. `TileKey.Around()` is an approximate streaming aid,
not a completeness proof for physical discovery. Test exact seams, corners and poles.
Formation far representations preserve meaningful silhouettes; mesh merging is local
batching/authoring, not a requirement to weld the planet into one object.

Required asset metadata includes provenance, stable key/revision, compatible families,
local basis/scale, collision mesh, crest/toe/contact regions, permitted support patches,
all-LOD bounds and visual-to-collision deviation measurements. Current scan import creates
24,000/3,500 triangle visual budgets and ~700 collision triangles; those budgets do not
prove shelf/contact accuracy. Missing shapes are authored from allowed sources, then
validated with their recipe. Preserve actual PBR detail and appropriate texture scale.

## Support, substrate and terrain contact

`SupportHit` identifies source/formation/piece, primitive/patch, point, geometric normal,
barycentric coordinates, substrate and capability flags. Bounded deterministic ray/triangle
queries use the same transformed physical geometry as collision; source/primitive keys
break ties. Support policy selects the intended shelf or ground beneath an overhang,
checks inclination, clearance, footprint and permitted substrate, and can return no hit.

Terrain roots cannot establish support on cliff shelves. A scan's bounding box or one
radial height is insufficient. Validate wide authored root zones against several support
samples; reject or fit incompatible creases within recipe limits. Trees/resources use
authoritative support. Collision and visual meshes share transforms, but geometric error
must still be checked at traversed/support surfaces.

Cosmetic roots also follow the *drawn* terrain triangle and its current morph/LOD. Store
stable authoritative anchors plus a bounded presentation correction, using the terrain
tile's exact triangulation/morph contract; origin rebasing is a coordinate change only.
This correction never moves gameplay collision or identities. If contact error exceeds
the measured tolerance, repair the LOD/support geometry rather than burying the plant.

`SubstrateSample` exposes bedrock, loose stone/talus, soil-depth proxy, wetness, litter and
plant suitability for that support. Recipes produce rock → rubble → stony soil → sparse
plants → established cover, with pockets and gaps. Share these inputs among terrain,
cliff contact shading, grass and authored plants. Limit soil blending to appropriate
contact/shelf regions; preserve rock faces and species albedo. Climate and substrate
produce habitat coverage once; later renderer stages do not repeatedly thin it.

## Vegetation identity, recipes and rendering

Data distinguishes **cosmetic detail**, **concealment vegetation** and **harvestable plants**.
Concealment can be non-solid and unharvestable while still affecting play. Do not register
ordinary bushes as a gather class merely to draw them. A composed plant-source contract
uses `(source namespace, world identity, canonical candidate key)` and explicit state
ownership. `NaturePlants` filtered indices and `SurfacePlants` scatter indices are different
domains; blindly appending them can hide the wrong object or duplicate a resource.

Recipes declare stable IDs, version, eligibility, habitat weights, support/clearance,
density in instances/m² or occupied-area fraction (explicitly identified), dimensions
and distances in metres, angles in radians, normalized weights, species mix and renderer
capability keys. Validate units/ranges/references at load. Add species as data using a
registered blade, clump, shrub, tree or debris renderer; no species/type switch chains.
One handler owns each capability. Novel geometry needs a handler, not a second scatter path.

Preserve blade silhouette through LOD; avoid collapsing height into a flat fade ring.
Use stable nested instance subsets, contact-correct roots, wind-following normals and
controlled specular/transmission. Shared wind/palette inputs coordinate procedural and
authored layers. Simplification must reduce work and retain intended apparent coverage.

Concealment silhouette/opacity must remain comparable across presets while a hidden
player can be observed, including scoped/distant views. Define that range from gameplay
and measured budget. Low may simplify geometry/shadows/wind, not remove necessary cover.
Monument planting pockets and structure/path exclusions are a target, not implemented;
replace blanket circular clearing only after explicit regions preserve doors/loot/routes.
Building clearance must use authoritative rotated shapes and vertical bounds; an elevated
floor or flying ship must not erase ground plants. Publish immutable dirty-region snapshots
to all relevant observers, independent of building draw culling. Regrowth is a separate rule.

## Tile and job lifecycle

Stable vegetation tiles contain compact surviving instances grouped by renderer/LOD;
compare the plan's 8 m/16 m candidates before selecting size. Keep their ownership grid
independent of adaptive terrain LOD. Bounds include mesh, maximum wind and interaction.
Workers produce plain immutable data; Godot creation/upload/disposal stays with its owner.

Each request carries world identity, session epoch, tile key, tile generation, source
revision and presentation revision. States are Requested → Building → Ready → Uploading
→ Resident → Retiring → Released, with explicit Failed/Cancelled outcomes. Every publish
checks all keys and current ownership. Cancelling never permits a stale result to upload.
Dispose rejected results and release reservations/leases exactly once. Failure is visible.

Unload stops admission for that tile, invalidates its generation, retires work and then
releases/pools resources safely. Re-entry receives a new generation. Rebase updates local
transforms without reseeding. Quality/LOD updates replace bounded representations; they
do not duplicate two full populations indefinitely. Support/occupancy changes invalidate
the correct data stages; wind changes normally update uniforms. Pending outputs are bounded.

## Packages, ownership and removal of old routes

| Package | Primary ownership; integration responsibility |
| --- | --- |
| T0 correctness | External worker: comparison fixtures and `WalkingWorld.Compute`; coordinate shared-file hunks with generation owner. |
| Lifetime repair | Root: `game/scripts/Surface/TerrainLod*.cs`, native probe and integrated validation. |
| Metadata/retention | Generation owner: metadata-only resolver, `PlanetSurfaces`/`WalkingWorlds` call boundary and `WalkingWorld` thread-static retention. |
| Grass plumbing | Grass owner: `GrassField*.cs` job retirement and diagnostic layer mask only. This does not deliver spatial grass tiles. |
| Architecture | This pass: this document only. Root integrates contracts and records actual implementation status. |
| Session/descriptor | Subsequent assigned owner: store/lease contracts, server/client descriptor negotiation, persistence identity and lifetime tests. |
| Formation/support | Subsequent assigned owner: `CliffPlacements`, `CliffGeometry`, required recipes/import metadata, support/substrate contracts and formation tests. |
| Ecology/populations | Subsequent assigned owner: `WalkingWorld` cover/density integration, scatter/plant-source contracts, concealment and occupancy; shared edits scheduled explicitly. |
| Spatial rendering | Subsequent assigned owner: vegetation tiles, renderer registry, terrain contact/morph integration, `CliffField` far silhouettes and materials. |

Assign exclusive files/hunks before parallel work; root serializes builds and Godot runs.
Introduce adapters only with a removal gate. After consumers switch and equivalence checks
pass, remove the static full-world cache route, legacy cliff stack generator, duplicate
cover decisions and whole-face detail submission. Test the hosted `GroundView` provider,
not only `SurfaceView`. No permanent Earth-only or look-dev-only implementation bypass.
An extension adds a validated profile/recipe, compatible assets and renderer capability
references, then enters the existing pipeline and regression matrix; it adds no new loop.

## Validation and acceptance

### Session ownership implementation in progress — 18 October 2026

`PlanetEnvironmentDescriptor` freezes canonical inputs without using its identity digest as a terrain seed.
`WorldStore` has explicit resident/build estimates, bounded admissions and coalesced construction; a
`WorldLease` pins output until its reader retires. The estimates are admission limits, not measured process
memory. The current wire protocol reconstructs only the existing default-universe profiles; unsupported
descriptors are refused until a descriptor negotiation protocol exists.

The server acquires before its landing search, then transfers ownership to the zone. Clients retire the
previous modules and physics before publishing a replacement. Presentation receives its own pins: terrain,
grass, vegetation, cosmetic ground collision and falling-plant jobs finish before those pins are released.
Maps fork a pin for each admitted CPU render; native image creation and texture upload stay on the render
thread. A view's shutdown does not block on a render-frame callback. Shared bot replicas are scoped to their
swarm and descriptor, including physics, natural-state listeners, buildings and landing-dependent plots.

`Stop` closes admission without pretending that cancellation has interrupted an arbitrary source callback.
Retirement observes every admitted job, including faults, before releasing the world. Shader globals use a
view ownership stamp so an exiting old view cannot clear a newer planet. Standalone diagnostic surfaces
remain explicitly owned by their callers. Fresh Debug and Release builds pass with zero warnings/errors;
103 focused checks across sixteen classes pass in both configurations. A headless Godot probe passes
70 native lifecycle checks, including the production reconnect coordinator and spatial-grass staging,
failure and retirement. See `.artifacts/environment-ownership/` and DEVELOPMENT.md for receipts.

The first spatial grass implementation is explicitly opt-in for comparison. It compacts conservative
survivors into local 16/32-cell batches with stable cell identities and per-generation textures; authored
plants and the existing coverage/lighting rules are unchanged. Formation topology, immutable records,
triangle support and substrate rules have 28 passing synthetic tests but are not connected to the live
generator. The asset audit measures material support-to-visible-mesh disagreement and identifies the
required qualified patches/contact geometry before activation. These checks do not establish finished
cliffs, soil transitions, final vegetation, GPU culling/performance, or visual acceptance. Full-suite and
hosted-render validation remain separate gates; memory headroom currently constrains the capture.

The subsequent F3 authoring slice has strict receipt-authenticated package loading and actual near/far
triangle correspondence. `FormationPackageLoader` accepts a caller-pinned SHA and bounds the aggregate
source/output files and bytes, JSON, physical geometry, domains and decoded LOD buffers. It verifies
the source manifest and dependency closure; images are hashed without decoding. An authenticated
package is not automatic content approval or a generation identity. Current packages contain no
active recipes. Their receipt hashes must not be substituted for a physical-content fingerprint.

Curved patch charts qualify an explicit projection while retaining physical triangles and all holes
and pinches. Continuous disk support uses those exact boundaries and conservative slope/height
checks; it does not promise overhead clearance. Qualification is prepared once per owned geometry,
not per instance. Actual forest prototype qualification passed; query indexing/allocation still
needs optimization before vegetation use. The inactive planner consumes only finalized graded
terrain and raw fields, traces canonical cube-edge contours, fits keyed contacts and estimates face
coverage with declared physical-triangle sample rays. Site/conflict boxes are conservative refusals,
not exact clearance. Generated caps/aprons, final ecology, content fingerprints and live activation
remain unfinished. DEVELOPMENT records synthetic failures, corrections and actual package evidence.

Use the plan's initial 8 environmental cases × 6 fixed seeds, with held-out seeds for
final review. Include Earth variants 0/5, sterile temperate, dry/cold/high-relief/wet,
airless/non-water-liquid eligibility, smallest/maximum supported radii, seams and poles.
Compare independent production descriptors, request orders, cold/warm caches, parallel
requests, cancellation, eviction/revisit, two active leases and repeated teardown.
Assert stable IDs/transforms/support/collision; validate finite complete samples, bounds,
physical-content failures and save/protocol refusal. Intentional output changes update
goldens and compatibility in the same change, with an explicit explanation.

Capture ordinary hosted walking and low flight through meadow, forest edge, bank, coast,
inland cliff and monument approach at daylight/grazing light/shade. Check roots, exposed
faces, soil transitions, repeated formations, readable resources, collision/traversal,
building clearance, concealment across presets and distant silhouettes in motion.
Shots supplement clips and player-scale inspection; visual acceptance is not a test count.

Before candidate tuning, record hardware/build/assets, resolution, preset, route/seed/time,
cold readiness, CPU/GPU frame distribution, p95 streaming, layer ablations, submitted and
visible geometry, generation/upload time/bytes and peak resident/scratch memory. Establish
numeric budgets from this baseline, then compare three warmed runs plus cold/turn/crossing
cases. Target intended habitat coverage at equal-or-lower vegetation cost without p95
streaming regression; report uncertainty and total frame cost. No measured budget or
performance gain is established by this document. A native abort or incomplete run remains
an unresolved result; focused passes cannot be reported as a complete suite or art approval.

### Drawn terrain contact and grass coverage — implementation under validation

Terrain now exposes snapshots of only its currently drawn regular 33×33 grids, their actual
instance transforms and stored morph ranges. Grass contact uses those fine and parent-morph
positions, applying the terrain shader's camera distance independently at each triangle vertex
before barycentric interpolation. It does not sample a second heightfield to invent support.
Whole grass cards require both footprint endpoints; this does not yet prove clearance over
every interior point of a wide card crossing a crease. Skirts never provide support.

The scene-thread contact atlas admits at most 128 tiles and two grid pairs per update, with
a 0.5 ms soft admission target checked between uploads. Native upload duration is not bounded
by that target. It has fixed fine/morph arrays and a dense nearest-filtered address texture
at terrain resolution, capped at level 9. Generation-tagged addresses reject retired slots;
removal is published before reuse, and a partially uploaded pair remains unavailable. CPU
snapshots retain immutable grid arrays only, with no world lease or scene-node reference.
The dense address texture is about 96 KiB at level 6 and 6 MiB at level 9; the two grid arrays
total about 4.25 MiB before driver overhead. Upload and shading costs still require measurement.

Procedural grass now has one habitat/patch/quality occupancy decision. Surviving cards retain
their dimensions; stable cell/card ranks remove entire cards at distance. The atlas alone
defines their connected cutout silhouettes. Fragment normals follow the deformed geometry,
with restrained specular response. Actual drawn-grid bounds replace lattice bounds for
contact-enabled spatial grass. Spatial batching remains opt-in until hosted visual, motion
and performance comparisons pass. Authored plants and the full ecology transition are still
separate unfinished work. Source implementation is not visual acceptance.

### Finalized grading input for formation construction

`WalkingGradedTerrain` is the formation producer's restricted input. Construction first
finishes the existing monument placement snapshot. It exposes the graded radial height,
water, and raw climate/forest potential, without calling cover, scatter, cliff support or
final habitat. Each protected site combines its collision mesh box with its actual chord
footprint projected along every radial height. This is necessary because a finite box
around the monument's plane does not cover the blended raw terrain in its grading ring.
Constant-work enclosing-sphere/cone intersection conservatively rejects candidates; it
does not grant support. Sites are never region-filtered using just their finite mesh box.
The adapter contract is an explicit physical identity input. It is constructed
within the world's admitted build and discarded when its immutable formation plan is
complete. This seam does not activate a formation recipe by itself.

### Canonical terrain and formation contact — implementation in progress

Selected formation worlds use one engine-free `TerrainLattice`: the finest render tile
level plus five defines the canonical vertex level (32 cells per tile). Tile rendering,
terrain collision and contact validation consume that descriptor. Canonical cube vertex
identities also determine one direction at face edges and corners. Legacy worlds keep
their existing sampling; no current production descriptor activates the new capability.
The selected contract and finest level must enter physical generation identity before activation.

The cliff interface is a closed additive module with a complete outer soil/apron
contact loop shared exactly by physical, near and far geometry. Continuous segment coverage
against the actual canonical terrain triangles replaces sparse probes for the final seal.
Accepted contacts pin a bounded band of terrain morph vertices and require a finite cohort
of finest terrain tiles before the cliff becomes visible. The underlying terrain remains
intact during streaming.

The inactive `FormationField` now selects complete formations against finalized grading,
validates their exact seals without changing or promoting the selection afterward, and
records stable rejected slots. Its contract binds terrain identity, closed contact loops,
canonical lattice and seal policy. Candidate segment shadows are conservatively enumerated
on all six faces within explicit cell/work limits; only the exact seal grants acceptance.
The frozen pin set includes two guard rings and all incident finest-tile copies at face
edges and corners. Tile generation uses the same fixed positions and normals for fine and
morph data. The focused field/lattice/seal/pin/root/architecture bundle passed 58 checks
across the initial run and a corrected fixture rerun; this is not live-world acceptance.

Per-view cohort tickets are being qualified separately. Their intended readiness is the
actual visible finest terrain instances, including sibling transactions needed to reach
them. Shared tickets retain shared demand. A renderer hides the formation synchronously
before releasing its ticket; failed hiding retains that demand. Current, queued, completed
and retiring work share one admission ledger, and setup refusal preserves the old coherent
view. The native cohort fixture passed 35 checks, including hidden terrain and hidden
ancestors: readiness uses effective visibility in the scene tree. Production activation
remains outstanding. Selected collision margins cross cube-face edges through canonical
vertex-neighbor rings under explicit cell/vertex/scratch/output limits. Nine focused
checks passed, including full-sphere saturation and the zero-margin invalid-position/final-cancellation regression.
An additional exact-byte-budget boundary regression is prepared for the next build.

The forest scan's revised closed module passes complete near/physical self-intersection
and manifold checks under the original work limits. Exact repeated triangle-pair results
are reused within one bounded run; every candidate pair is still enumerated. Its far
triangles equal the physical triangles. The common added closure preserves the original
certified symmetric surface-error upper bound of 0.149989 m at scale one. This says
nothing about signed clearance, contact normals, terrain fit or material quality. The
six offline PBR views have been inspected; closure faces retain explicit review colors.
The added soil apron has visible triangle facets and a straight exposed outer wall in isolation.
Metric UV charts preserve all original geometry and scan UV bytes, with no collapsed or mirrored
added faces and certified linear distortion at most two. Native tangent-basis verification is still
being completed before smooth soil normals and material blending. The module remains unadmitted. No successful
mathematical seal test substitutes for these asset gates.
Soil/apron materials, final substrate/population integration, production publication and
hosted visual/performance acceptance remain unfinished.

Terrain substrate appearance is being shared through `surface_ground_look.gdshaderinc`.
The first extraction preserves the original uniform defaults and material function exactly
(source reconstruction was byte-identical). Sealed soil will consume the same layer arrays,
metre scales, normal-axis convention, rough-height channels and world texture phase. This
extraction alone does not qualify a formation material or its boundary interpolation; native
shader/render verification and actual contact blending remain required.

Ground normal composition now uses the surface-gradient frame in
`surface_ground_normals.gdshaderinc`: each sampled tangent normal becomes a slope,
its own UV rotation or triplanar projection moves it into world axes, and the
component along the geometric normal is removed before material blending. The
former raw-RGB triplanar blend used one unrelated cube-face basis. The encoded
normal Z denominator has a 0.1 floor to keep quantized grazing texels finite.
Normal channel flips apply before coordinate rotation. Neutral maps leave all
signed faces unchanged; rotated samples follow the texture's rotation. This is
based on the [surface-gradient formulation](https://jcgt.org/published/0009/03/04/).
An isolated native analytic fixture and hosted visual verification are required
before accepting its appearance. Texture phase, albedo and habitat do not change.

Selected walking construction reserves finalized monument grading, field and fixed pins before
dispatch and publishes a single immutable bundle after final cancellation checks. A selected store
rejects unqualified/custom output; operational construction limits remain separate from physical
identity. The focused constructor/store/field/pin/lattice/descriptor/architecture bundle passed
82 tests, followed by 52 walking/identity/seal/substrate/handshake checks. The constructor fixture
intentionally produces an empty complete field; nonempty playable integration remains required.

The formation package loader retains immutable geometry/revision/LOD-to-relative-asset mappings and
all authenticated output-file pins. A renderer can bind approved near/far resources without
reconstructing paths from names. These presentation records have a separate retained-byte estimate;
they neither alter physical generation nor authenticate an unchecked engine import.

The visual-coordinate decoder accepts a bounded list of static indexed primitives and
external buffers. Canonical ordinals concatenate primitive declarations, then each
primitive's original vertices/triangles, without welding material seams. Each primitive
owns distinct accessors; indices remain local until one explicit rebase. Aggregate limits
are 32 primitives, eight buffers totalling 32 MiB, 128 views/accessors, 60,000 vertices and
50,000 triangles. Metadata, ranges, alias checks and their four-million-step work allowance
are admitted before any dependency read. Images/material appearance are authenticated by
the separate visual-data contract; this geometry reader grants no support permissions.

Material and normal seams may duplicate visual corner indices without separating their
positions. A contact binding can therefore supply two explicit actual-edge endpoint
indices for each ordered segment, in addition to its representative loop vertices.
Every endpoint must equal its physical loop coordinate exactly; every segment must still
be an actual triangle edge, including closure. Physical loops remain strict indexed
cycles. Missing endpoint maps preserve the old strict visual-cycle contract and identity
bytes. Explicit endpoint arrays are counted in retained/scratch/reference admission and
included in qualification identity. No approximate positional welding is used.

Additive formation appearance is a separate authenticated payload. Its four
float32 channels describe scan-to-substrate, substrate-to-drawn-terrain, cap and
toe weights. Native upload uses CUSTOM0 RGBA_FLOAT; source positions, normals,
tangents, UV0 and triangle order are unchanged. The engine-free reader binds every
primitive to ordered vertex/corner P/N/UV, tangent and local-index hashes and checks
both vertex and corner mask hashes. It rejects missing LODs, aliased streams,
nonfinite weights, cap+toe above one and source-atlas sampling on closure geometry.
Caller-supplied module, tangent and recipe pins are independent of package claims.
One load requires a declared 64 MiB CPU reservation, at most two LODs, 32 primitives,
60,000 vertices and 50,000 triangles per LOD, five files and two million validation
steps. Retained masks are accounted separately; the borrowed visual data and native
upload remain separate owner charges. This reader grants no art or root permission.
The implementation and seven focused tests await build/native integration.

### Finite shared appearance packages — implementation in progress

World placement identity and reusable flora appearance are separate contracts. A world
seed chooses deterministic positions and variants; it must not require a newly baked
texture library for every visited planet. A separately versioned finite catalog will
select an admitted appearance and palette without consuming placement RNG. Its mapping
is immutable within a visual version; adding catalog entries must not silently remap
existing worlds. The first qualification package preserves Earth's exact legacy recipe
and identity palette. Other worlds need an explicit versioned migration, not an implicit
fallback to Earth.

Each shared package binds the near painted atlas, bark/birch textures and normals,
`BladeMean`, both complete finished impostor mip chains, and the exact geometry/material
dependency and row mapping. The present row contract is four broadleaf, three fir, three
birch and one pine variants; authored rows occupy [4,11). Near/far palettes must agree;
palette choices do not produce more baked atlases. Source pixels and immutable payloads
can be shared, while mutable materials and world bindings stay with their view.

An isolated exporter currently produces evidence with `runtimeCacheAdmission=false`.
It is not a production cache. The engine-free seven-image contract/catalog/loader passed
15 tests. It requires independent input and manifest pins, complete RGBA8 mip chains,
exact row semantics and explicit byte limits; its immutable payload totals 87,381,292
bytes. Admission still requires the actual export/runtime input adapter, atomic native
row/pass publication and lifetime verification.
Missing packages cannot silently trigger runtime baking. Resident, loading, staging and
retiring bytes must all fit the eventual cache budget; measured limits remain to be set.
