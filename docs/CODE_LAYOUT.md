# The code, file by file (19 October 2026)

Generated from the repository: every C# file, the types it declares (nested ones in brackets), its length and its
own first line of explanation. HOW_IT_WORKS.md says how these run together; this is the map. Regenerate it after
a package with the script in the session that made it, or by hand: the shape is one table per folder.
1,077 files, 272,593 lines of C#.

## src/Hollow.Core

The foundations: double-precision vectors, deterministic maths and hashing, random numbers, units.

### src/Hollow.Core (7 files, 713 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `DMath.cs` | 296 | DMath | Deterministic maths. Every function here is built only from IEEE 754 operations that are exactly rounded |
| `Hash64.cs` | 43 | Hash64 | Fixed 64-bit hashing for seeds and streams. Unlike System.HashCode and |
| `Quantize.cs` | 142 | Quantize | Turning floats into the integers sent on the wire and back. Every conversion is plain IEEE arithmetic with |
| `Rng.cs` | 119 | Rng | Deterministic random stream (SplitMix64). Seed one per generated object from its address with |
| `Units.cs` | 26 | Units | Physical constants and unit conversions. SI unless the name says otherwise. |
| `UniverseTime.cs` | 23 | UniverseTime | Universe time is real UTC (ARCHITECTURE.md §6). Orbits are expressed relative to the J2000 epoch. |
| `Vector3D.cs` | 64 | Vector3D | A 3D vector in double precision. Used for galactic (light-years) and system (metres) positions. |

## src/Hollow.Galaxy

The 1:1 galaxy: the density model, sectors and cells, the catalogue, names, star systems and their bodies.

### src/Hollow.Galaxy (17 files, 2,940 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `CatalogueCompleteness.cs` | 94 | CatalogueCompleteness | How much of the real sky the star catalogue holds, per level and distance from Sol (GALAXY.md §8.3): the |
| `CellGenerator.cs` | 271 | Streams, CellGenerator | Stream tags: every kind of generated object hashes its address with its own tag. |
| `CellKey.cs` | 238 | SectorGrid, CellKey, SystemAddress | The sector grid (GALAXY.md §5). |
| `DensityModel.cs` | 250 | DensityModel [Arm] | Number density of star systems (systems per ly³) in the Milky Way, as the sum of six components |
| `Galaxy.cs` | 257 | SearchResult, Galaxy | A search hit: a system, or a place (a sector or a named region, ) when the text names |
| `GalaxyFrame.cs` | 86 | GalaxyFrame | The galactic frame and the density model's frame (GALAXY.md §2). |
| `GalaxyStar.cs` | 47 | GalaxyStar | The primary star of one star system, as the galaxy knows it: enough to draw it on the map and to |
| `GameScale.cs` | 34 | GameScale | GameScale: playable planet radii (Earth 1.2 km, cube-root compression, 23.04 km2 area cap), kept separate from the generator's physical values (WORLD-01). |
| `GeneratorVersion.cs` | 39 | GeneratorVersion | Generator versions, one per stage of the universe pipeline (ARCHITECTURE.md §8.4, GALAXY.md §14). Each version is |
| `Names.cs` | 268 | SectorNames, SystemNames | Sector names (GALAXY.md §9): two words from original syllable tables, exactly one name per sector, and |
| `PlayableGalaxy.cs` | 117 | PlayableGalaxy [Entry] | PlayableGalaxy: the finite playable universe of at most 1,000 stars over the unchanged legacy galaxy; map, search and travel resolve through it. |
| `StarCatalogue.cs` | 271 | CatalogueStar, StarCatalogue | One real star from the catalogue. Companions hang off their primary. |
| `StarClass.cs` | 136 | StarKind, Population, GalaxyComponent, StarClass | What kind of object a star is. Stored as a byte; append only, never reorder. |
| `StarClassTable.cs` | 284 | StarClassEntry, StarClassTable | One row of the class table (GALAXY.md §4). |
| `StarCountProfile.cs` | 203 | StarCountProfile | Expected number of stars of each level within any distance of a point. Built by sampling the density model on |
| `StellarEvolution.cs` | 189 | StellarEvolution | Stellar ages and what they decide (GALAXY.md §4.1): each star's age comes from its population, and the age |
| `StellarTables.cs` | 156 | StarPhysics, StellarTables | Physical properties of a star. Temperature in K, others in solar units, magnitude in V. |

### src/Hollow.Galaxy/Authored (2 files, 395 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AuthoredLayer.cs` | 168 | AuthoredPlanet, AuthoredHost, AuthoredSystem, AuthoredLayer | A real planet: what has been measured. Everything else (spin, moons, physical state) is generated. |
| `Regions.cs` | 227 | RegionKind, Region, Regions | What a named region of the galaxy is. |

### src/Hollow.Galaxy/Systems (7 files, 2,955 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `BodyPath.cs` | 49 | BodyPath | A body's permanent 32-bit path inside its system (ARCHITECTURE.md §7, GALAXY.md §10.1). With the system address |
| `Orbit.cs` | 113 | Orbit | Keplerian orbit around a parent body, in the system frame (reference plane x–z, +Y north). Angles in |
| `PlanetState.cs` | 227 | Composition, Gases, Volcanism, Tectonics, LiquidKind, SurfaceKind, Hazards, Biosphere, Material, PlanetState | Bulk composition of a body, as mass fractions that add up to 1 (GALAXY.md §10.3). |
| `SolSystem.cs` | 358 | SolSystem [Writer] | The real Solar System: planets with their J2000 orbital elements (ecliptic reference plane), the major moons, |
| `StarSystem.cs` | 267 | BodyKind, PlanetType, AtmosphereType, Body, StarSystem |  |
| `SystemGenerator.cs` | 1031 | SystemGenerator [Companion, Node, Builder, HostContext] | Generates a star system's stars, planets, moons, rings and belts from its primary star (GALAXY.md §10). A pure |
| `WorldBuilder.cs` | 910 | Formation, WorldInput, World, WorldBuilder [ClimateModel, Climate] | How a planet formed: a solid body (which may keep a gas envelope), an ice giant or a gas giant. |

## src/Hollow.Surface

Planets as surfaces: the cube-sphere, terrain, biomes, scatter, the sky, the zone frame.

### src/Hollow.Surface (34 files, 7,118 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Biomes.cs` | 407 | Biome, GroundMaterial, ScatterKind, ClimateSample, BiomeTable | Land biomes (GROUND.md §4.6), picked from the climate: annual mean temperature, moisture and height. |
| `CliffGeometry.cs` | 87 | CliffGeometry | CliffGeometry: the cliff scans' low-complexity collision meshes and bounds (height, width, gradient range, material family), read once from data. |
| `CliffPlacements.cs` | 809 | CliffPlacement, CliffFaceSample, CliffFormationCacheDiagnostics, CliffPlacements [CacheEntry, FaceSample, WallSection] | CliffPlacements: stable cliff formations on the walking world's steep dry faces (docs/TERRAIN_SCENERY_PLAN.md T2): surveys, placements, hit and hold tests, cache. |
| `CliffRecipe.cs` | 124 | CliffRecipe | CliffRecipe: a cliff formation recipe (docs/TERRAIN_SCENERY_PLAN.md T2, docs/LANDFORM_SURVEY.md): which scans a face takes, group size and rock share on gentle faces. |
| `CubeSphere.cs` | 263 | CubeSphere, TileKey | The cube-sphere of a planet surface (GROUND.md §4.6): six faces, each a square of face coordinates |
| `FirstWorld.cs` | 32 | FirstWorld | The ground game's first world (GROUND.md §3): Nolcas Peris CQ-SLY 7-0 B 3, a breathable Earth-like planet |
| `GroundCollisionLayout.cs` | 122 | GroundCollisionLayout, GroundAreaLimits, GroundAreaPlan | Fixed collision tile layout: grid level by radius, topology limits and validation, and GroundAreaPlan, the radial shadow of a swept box (Around, Along). |
| `GroundCoverMeshFingerprint.cs` | 55 | GroundCoverMeshFingerprint | Versioned exact imported-array identity, not original glTF corner identity. |
| `GroundCoverRootMetadata.cs` | 190 | GroundCoverRoot, GroundCoverRootMetadata | Receipt-bound fern root anchors: GroundCoverRoot and the sidecar metadata that must match a trusted expected digest. |
| `GroundDetailCover.cs` | 32 | GroundDetailCover | Presentation densities from the surface's already biome-, shore- and canopy-adjusted cover. |
| `IPlannedGroundCollision.cs` | 35 | IPlannedGroundCollision, GroundCollisionPreparationBounds | IPlannedGroundCollision, a surface that prepares and builds ground collision from authenticated single-use plans, and its preparation bounds. |
| `ISurface.cs` | 205 | ISurface, GroundCover, TriangleMesh, PooledTriangles | A body's ground as a pure function (GROUND.md §4.6): the server builds collision from it and the client draws it, |
| `MonumentDefinition.cs` | 73 | MonumentPiece, MonumentLoot, MonumentDefinition | MonumentDefinition: the monuments' pieces, loot anchors and collision read from the embedded monuments.json, loot tables mapped to shipped ones. |
| `MonumentPlacements.cs` | 148 | MonumentPlacement, MonumentPlacements | Where monuments stand on a walking world: one per chosen tile from the seed, off water and the landing site, indexed by tile. |
| `NearTiles.cs` | 71 | NearTiles | TileKey.Around for searches repeated from about the same place every tick (a character's ground, an |
| `Noise.cs` | 233 | NoiseBasis, Noise | The lattice gradients a noise is made of (Perlin's, or the cube's corners for the walking-scale ground), and deterministic 3D gradient noise with its gradient, its octave sums, ridged and eroded noise |
| `PlanetBodies.cs` | 52 | PlanetBodies [Entry] | Resolves a planet or moon's physical metadata without generating either kind of terrain. |
| `PlanetSurface.cs` | 516 | SurfaceSample, VegetationSample, PlanetSurface, MonotoneCurve | What the height function knows about a point, besides its height. |
| `Plates.cs` | 204 | Plates | Tectonic plates of a surface (GROUND.md §4.6): plate centres on the sphere (a Voronoi partition), each plate's |
| `Scatter.cs` | 442 | ScatterItem, IScatterField, ScatterDensity, ScatterPlan, ScatterGenerationLimits, Scatter [Densities] | One natural object placed by the scatter (GROUND.md §4.6); what the scatter reads from a surface; a surface's scatter level and candidate cells (levels by size); the scatter itself |
| `SelectedGroundCollision.cs` | 219 | SelectedGroundCollisionLimits, SelectedGroundCollisionPlan, SelectedGroundCollision [Monument] | Selected ground collision: limits, an authenticated single-use plan, and the build of canonical terrain, sealed formations and monuments into one triangle mesh. |
| `SelectedGroundTrace.cs` | 649 | SelectedGroundTraceStatus, GroundPrimitiveKind, GroundPrimitive, SelectedGroundHit, SelectedGroundTraceRequest, SelectedGroundTraceLimits, SelectedGroundTraceDiagnostics, SelectedGroundTraceResult, SelectedGroundTraceWorkspace, ISelectedGroundTraceSurface, SelectedGroundTrace [Use, PredicateMode, Query, ConeCell, FaceRay, TriangleResult, TriangleHit, Evidence, Interval, IntervalVector, ExactVector, Budget] | The selected ground trace: a centreline ray against formations, monuments and terrain, with explicit limits, ordered hits and statuses. |
| `SkyEphemeris.cs` | 222 | SkyEphemeris | Where everything in a planet's sky is at a universe time (GROUND.md §4.6): the planet's spin turns its body frame |
| `SphereSurface.cs` | 74 | SphereSurface | A plain surface: a sphere with gentle hills from value noise and no sea. Tests use it as a simple |
| `StandInWorld.cs` | 137 | StandInWorld | The stand-in world S3 plays on until S4 makes the real ground at walking scale (docs/WHOLE_WORLD.md §4): a small |
| `StartingWorld.cs` | 21 | StartingWorld | StartingWorld: Sol's Earth (system address, body path, default landing site) as the playable starting world, found without the star catalogue (LOOP-01). |
| `SurfaceGrid.cs` | 316 | TerrainCollisionLimits, SurfaceGrid | Ground triangles of a cube-sphere tile from any surface's heights (docs/WHOLE_WORLD.md §6.1): a grid of the face's |
| `TerrainCollisionFootprint.cs` | 126 | TerrainFaceCellRange, TerrainCollisionFootprint, TerrainCollisionMesh, CanonicalRadialShadow | Conservative per-face cell ranges that a collision build uses (TerrainCollisionFootprint), the collision mesh with its footprint, and the shared radial-shadow range. |
| `TerrainCollisionPlan.cs` | 26 | TerrainCollisionPlan | Admitted exact canonical topology, with no terrain samples or source/world reference. |
| `TerrainLattice.cs` | 246 | ITerrainLatticeSurface, TerrainLattice, TerrainLatticeTriangle | TerrainLattice: a body's fixed dyadic terrain grid with canonical vertex identities, cells and triangles shared across cube faces; opt-in through ITerrainLatticeSurface. |
| `TerrainPinSet.cs` | 234 | ITerrainPinnedSurface, TerrainPinnedVertex, TerrainPinSetLimits, TerrainPinSetDiagnostics, TerrainPinSet | Sparse immutable frozen terrain vertices with canonical normals (TerrainPinSet), built from a finalized terrain; opt-in through ITerrainPinnedSurface. |
| `TileMesh.cs` | 519 | TileGrid, TileMesh, GridHeights [Grid] | The ground of one tile as a grid of (cells + 1)² vertices (GROUND.md §4.6), which the client draws. On a whole world |
| `WorldGrid.cs` | 141 | WorldGrid [Cell] | The map's grid on a whole world (docs/WHOLE_WORLD.md §11, decision 6): the six faces of the cube-sphere, named A to F, |
| `ZoneFrame.cs` | 88 | ZoneFrame | The frame a zone's floats are measured in (ARCHITECTURE.md §6, docs/WHOLE_WORLD.md §3.1). A zone uses |

### src/Hollow.Surface/Environment (12 files, 2,185 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `EnvironmentCanonical.cs` | 254 | EnvironmentCanonical [BoundedIdentityHash] | EnvironmentCanonical: versioned little-endian canonical encoding of descriptor properties and the bounded identity hash over it; not a seed source. |
| `EnvironmentDescriptors.cs` | 149 | EnvironmentDescriptors | EnvironmentDescriptors: metadata-only validated resolution of a body to its descriptor; full worlds are acquired separately through a store. |
| `EnvironmentWorldFactory.cs` | 57 | EnvironmentWorldFactory | EnvironmentWorldFactory: builds a world directly from a frozen descriptor, with a cancellation check and an opt-in selected walking path; no cache. |
| `FloraAppearanceCatalog.cs` | 139 | FloraAppearanceWorldKey, FloraAppearancePalette, FloraAppearanceWorldBinding, FloraAppearanceResolutionStatus, FloraAppearanceResolution, FloraAppearanceCatalog | FloraAppearanceCatalog: finite frozen mapping of world keys to flora appearance and palette ids, resolved only with a matching pinned version and digest. |
| `FloraAppearanceContract.cs` | 291 | FloraAppearanceValidationException, FloraAppearanceCapacityException, FloraAppearanceLimits, FloraAppearanceColour, FloraAppearanceImageLayout, FloraAppearanceSchema, FloraAppearanceDependency, FloraAppearanceRow, FloraAppearanceBakeProfile, FloraAppearanceInputContract, FloraAppearancePin, FloraAppearanceCheck | The flora appearance package's schema v1: seven RGBA8 image layouts, input contract and fingerprint, rows, pin, byte limits and validation. |
| `FloraAppearanceManifest.cs` | 186 | FloraAppearanceImageHash, FloraAppearanceManifest [Decoded] | Encodes and decodes appearance.json for a flora package: image hashes, input inventory and blade mean, checked against an expected pin. |
| `FloraAppearancePackageLoader.cs` | 149 | FloraAppearanceImage, FloraAppearancePackage, FloraAppearancePackageLoader | FloraAppearancePackageLoader: authenticates a flora appearance package against an independent pin and returns immutable images; rejects links, no fallback. |
| `PlanetEnvironmentDescriptor.cs` | 112 | EnvironmentSurfaceKind, WorldAddress, PlanetEnvironmentMetadata, PlanetEnvironmentDescriptor | PlanetEnvironmentDescriptor: frozen inputs for one generated environment (surface kind, WorldAddress, copied body metadata), compared by canonical form. |
| `SelectedWalkingWorldLimits.cs` | 108 | SelectedWalkingWorldLimits, SelectedWalkingWorldPlan | SelectedWalkingWorldLimits: opt-in build allowances for a selected walking world (no production defaults) and the plan that carries them. |
| `WorldLease.cs` | 61 | WorldLease | WorldLease: one pin on a session-owned world; Surface is borrowed for the lease's life, Fork makes an independent pin, Dispose drops all references. |
| `WorldStore.cs` | 619 | WorldStore [Entry, Waiter, Disposal] | WorldStore: bounded, session-owned cache of generated worlds keyed by canonical descriptor; coalesced builds, leases never evicted, asynchronous retirement. |
| `WorldStoreOptions.cs` | 60 | WorldStoreOptions, WorldMemoryEstimate, WorldStoreDiagnostics, WorldStoreCapacityException | Finite admission limits for one WorldStore session (builds, waiters, resident and scratch bytes, retained cleanup errors); a request beyond them is refused. |

### src/Hollow.Surface/Formations (35 files, 8,043 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `CliffSupportDefinition.cs` | 420 | CliffPatchBoundary, CliffPatchDomain, CliffContactDomain, CliffLodTriangleMap, CliffLodBinding, CliffSupportLimits, CliffSupportDefinition, CliffSupportValidation | Qualified cliff-support input: patch boundaries and domains, contact chains, LOD bindings and their limits, validated before use (engine-free). |
| `CubeTopology.cs` | 265 | CubeEdge, CubeEdgeTransition, CubeVertexKey, CubeEdgeKey, CubeTopology [FaceBasis, Axis] | Exact cube-face topology: edges, cross-face transitions, canonical dyadic vertex and edge keys, derived from CubeSphere's bases without floating point. |
| `FormationAppearanceData.cs` | 42 | FormationAppearancePins, FormationAppearanceData, FormationAppearanceLod, FormationAppearancePrimitive | Immutable additive appearance data (CUSTOM0 channels) bound to authenticated visual streams, with its authoring-chain pins; grants no material or placement rights. |
| `FormationAppearanceDataLoader.cs` | 201 | FormationAppearanceDataLoader [Pin] | Reads one bounded additive appearance package against the exact immutable visual upload inputs. |
| `FormationCatalog.cs` | 171 | FormationCatalogLimits, FormationCatalog | A bounded, immutable catalogue of qualified cliff support definitions, substrate profiles and formation recipes, with references resolved before it is published. |
| `FormationContactLoop.cs` | 64 | FormationContactLoop, FormationContactLoopBinding | Authored closed physical contact loops of a formation module and their bindings to visual indices, checked against actual mesh edges. |
| `FormationContactSealReceipt.cs` | 55 | FormationTerrainPin, FormationContactSealReceipt, FormationCohort | Evidence that a contact loop was proved buried (frozen terrain pins, FormationTerrainPin) and the FormationCohort that groups accepted pieces. |
| `FormationField.cs` | 194 | IFormationSurface, FormationFieldPieceQuery, FormationFieldCollisionResult, FormationField | FormationField: the sealed physical snapshot of a world's formation pieces, cohorts and rejections, with piece and collision-mesh queries. |
| `FormationFieldBuilder.cs` | 223 | FormationFieldBuilder, FormationFieldWork | Construction-only builder of a formation field: plans pieces, seals each contact loop into the terrain, applies the admission policy; FormationFieldWork keeps the shared work ledger. |
| `FormationFieldContract.cs` | 44 | FormationFieldAdmissionPolicy, FormationFieldContract | The sealed formation field's identity and policy: admission policy (independent pieces or whole formation), algorithm constants and the successful-output contract. |
| `FormationFieldLimits.cs` | 89 | FormationFieldLimits, FormationFieldDiagnostics, FormationFieldRejectionReason, FormationFieldRejection, FormationFieldQueryLimits, FormationFieldCollisionLimits | Caller-supplied limits, diagnostics and rejection records for building, querying and colliding with a formation field; exhaustion refuses the whole field. |
| `FormationFieldTerrain.cs` | 165 | FormationFieldTerrain [Interval, IntervalVector] | FormationFieldBuilder's per-loop terrain: segment-shadow candidate triangles on all six faces, conservatively culled by radial cone; only the exact seal certifies. |
| `FormationGenerationIdentity.cs` | 365 | FormationAlgorithmRevisions, FormationGenerationIdentity [CountingStream] | Algorithm revisions and the canonical identity of decoded formation inputs (physical content, generation, qualification), with their admission estimates. |
| `FormationGeometry.cs` | 197 | FormationValidationException, FormationCapacityException, SupportCapabilities, SupportPatch, GeometryTriangle, FormationGeometryLimits, FormationGeometry, FormationBounds, FormationTransform, FormationInput | Validated physical formation mesh: support patches and capabilities, triangles, bounds, rigid transforms and limits, with the validation and capacity exceptions. |
| `FormationGeometryIndex.cs` | 117 | FormationGeometryIndexLimits, FormationGeometryIndex [Node, BuildTriangle, AxisComparer] | A shareable immutable local-space BVH over one formation geometry, built within triangle, node and byte limits. |
| `FormationGltfDecoder.cs` | 318 | FormationGltfDecoder [Buffer, Primitive, BufferView, Accessor] | Restricted, engine-free glTF 2 reader of a formation's physical coordinates from already authenticated bytes; no transform, repair, I/O or image loading. |
| `FormationJson.cs` | 59 | FormationJson | Bounded strict JSON shared by authenticated formation-package readers. |
| `FormationPackageLoader.cs` | 362 | FormationPackage, FormationVisualAsset, FormationPackageFile, FormationPackageLimits, FormationPackageLoader [FilePin, Admission] | Loads a receipt-pinned formation package: FormationPackage (physical catalog plus visual file bindings), its limits, and FormationPackageLoader, which authenticates every byte. |
| `FormationPlan.cs` | 159 | FormationContourIdentity, FormationPlanDiagnostics, FormationPlanEntry, FormationPlanQueryLimits, FormationPlanQueryResult, FormationPlan [Node] | The finished formation plan: contour identity (packed owner and section), entries, bounded influence-box queries, diagnostics; an immutable snapshot with no terrain retained. |
| `FormationPlanner.cs` | 834 | FormationPlanner [Builder, Sample, Link, Segment, Candidate] | FormationPlanner: surveys the terrain, traces contours, fits recipe pieces by canonical priority and publishes a FormationPlan, all-or-nothing (experimental, not in the live world). |
| `FormationRecipe.cs` | 101 | FormationGeometryChoice, FormationRecipeSlot, FormationRecipe | A formation recipe: geometry choices with their required contact domains, preassigned slots, and the recipe that ties them together. |
| `FormationRecords.cs` | 198 | FormationId, FormationPieceId, PhysicalPrimitiveId, FormationPiece, SupportHit, FormationRejectionReason, FormationRejection, FormationCoverage, FormationRecord | Formation records: canonical formation, piece and physical-primitive ids, pieces, support hits, rejections and coverage, and the finalized FormationRecord. |
| `FormationSiteExclusion.cs` | 65 | IFormationSiteExclusion, FormationBoxExclusion, FormationUnionExclusion, FormationRadialExclusion | Exclusion regions for formation sites: box, union and a radial cone matching a protected chord footprint. |
| `FormationSubstrate.cs` | 271 | SubstrateProfile, SubstrateInfluenceId, SubstrateInfluence, SubstrateInputs, SubstrateSample, HabitatSample, SubstrateEvaluation, FormationSubstrateLimits, FormationSubstrate, SubstrateInput | Immutable synthetic substrate recipe. Material inputs are nonnegative relative weights, normalized on entry. |
| `FormationSupportIndex.cs` | 218 | FormationSupportIndexLimits, FormationSupportIndexDiagnostics, FormationSupportIndex [Instance, Node, AxisComparer] | An immutable world BVH over formation piece instances that share local geometry indexes, with limits and diagnostics of what it retains. |
| `FormationSupportQueries.cs` | 512 | FormationQueryStatus, FormationBackfacePolicy, FormationDiskQualification, FormationRayRequest, FormationSupportQueryLimits, FormationQueryDiagnostics, FormationDiskRequest, FormationDiskResult, FormationRayResult, FormationFootprintRequest, FormationFootprintResult, FormationSupportQueries [ITriangleVisitor, AllHitVisitor, Budget] | Bounded support queries over a FormationSupportIndex: ray casts, whether a disk fits a qualified support, and footprints of probes. |
| `FormationSurfaceChart.cs` | 382 | FormationChartBasis, FormationChartPoint, FormationChartEdge, FormationChartQualificationStatus, FormationChartQualificationLimits, FormationChartQualificationDiagnostics, FormationChartQualificationResult, FormationSurfaceChart, FormationChartMath [Qualification, Stop] | Qualifies a formation's support patch as a piecewise affine chart over an authored local projection (exact predicates), plus the exact-sign arithmetic it relies on. |
| `FormationSurfaceChartQueries.cs` | 343 | FormationChartDiskQualification, FormationChartDiskRequest, FormationChartQueryLimits, FormationChartQueryDiagnostics, FormationChartDiskResult, FormationSurfaceChartQueries [Budget] | Chart-disk queries: whether a disk on the qualified projection around a hit is supported by slope and height spread, within limits. |
| `FormationSurveyRegion.cs` | 205 | FormationIncompleteRegionException, FormationSurveyRegionLimits, FormationSurveyRegionDiagnostics, FormationSurveyRegion [CellComparer] | Sparse immutable cell topology of the planner's survey: core cells, edge-neighbour halo rings, boundary edges and limits; throws when evidence is incomplete. |
| `FormationTerrainSeal.cs` | 363 | TerrainSealTriangle, FormationTerrainSealRejection, FormationTerrainSealLimits, FormationTerrainSealDiagnostics, FormationTerrainSealResult, FormationTerrainSeal [Fraction, Interval, BigVector, Exact, Budget, Stop] | Exact test that a closed loop is buried in the fixed radial terrain triangles it crosses; all-or-nothing, with limits, diagnostics and the covering triangle set. |
| `FormationTransitions.cs` | 45 | FormationTransitions | Validates authored contact chains by visiting every vertex and edge midpoint for burial; never makes a cap or apron. |
| `FormationVisualData.cs` | 228 | FormationVisualLimits, FormationVisualSizing, FormationVisualPreflight, FormationVisualArray, FormationVisualData, FormationVisualLod, FormationVisualPrimitive, FormationVisualImage, FormationVisualSampler, FormationVisualTexture, FormationVisualMaterial | Authenticated CPU-side visual payload of a formation: preflight plan, limits and sizing, LODs, primitives with float32 streams, images and samplers. |
| `FormationVisualDataLoader.cs` | 605 | FormationVisualDataLoader [View, Accessor, Primitive, ImagePlan, LodPlan, Plan, Work, Admission] | Reads a formation's visual files in two steps, preflight (bounded metadata) and load (reauthenticated glTF streams and image headers), without upload or activation. |
| `FormationWorldSelection.cs` | 63 | FormationWorldSelection | Explicit immutable selection for a formation-capable world factory. |
| `IGradedTerrain.cs` | 100 | IGradedTerrain, GradedTerrainFields, FormationProtectedSite, FormationPlannerSettings, FormationPlannerLimits | IGradedTerrain, the finalized radial terrain a planner may read (height, water, raw fields), with protected sites, planner settings and limits. |

### src/Hollow.Surface/Walking (9 files, 2,171 lines)

The walking-scale ground of S4 ([WALKING_GROUND.md](WALKING_GROUND.md)).

| File | Lines | Types | What |
|---|---|---|---|
| `Drainage.cs` | 844 | WalkingLake, DrainageSegment, Drainage | Where water goes, worked out once: a priority flood on a cube-sphere grid of about 30 m, catchments, lakes in hollows with smooth shores, streams and dry valleys from source to the sea or a lake, canyons and gorges; `Shape` cuts them into a height |
| `ReliefProvinces.cs` | 134 | Relief, ReliefProvinces | Plains, hills, tablelands and mountains: provinces spread over the sphere, picked by shares and an uplift field, blended by normalised Gaussian weights |
| `RidgedNoise.cs` | 88 | RidgedNoise | Eroded ridged noise for the walking ground's mountains: every octave turned by its own rotation and crests rounded, so no ridgeline runs straight |
| `WalkingGradedTerrain.cs` | 69 | WalkingGradedTerrain | WalkingGradedTerrain: the walking world exposed as IGradedTerrain for formation construction, with the physical input identity of its generator. |
| `WalkingParameters.cs` | 259 | WalkingParameters | The walking-scale ground's numbers in one table |
| `WalkingWorld.Collision.cs` | 32 | WalkingWorld | WalkingWorld's planned ground collision: prepares and builds a tile's selected collision mesh from the selected bundle. |
| `WalkingWorld.Formations.cs` | 59 | WalkingWorld [SelectedBundle] | WalkingWorld's selected bundle: lattice, formation field, pins and monument collision built together, plus identity matching of descriptor and limits. |
| `WalkingWorld.Trace.cs` | 22 | WalkingWorld | WalkingWorld's TraceGround: the selected ground trace through formations and monuments, unsupported on worlds without selected collision. |
| `WalkingWorld.cs` | 664 | WalkingSample, WalkingWorld | The ground of a small world at walking scale: an `ISurface` and `IScatterField` from landmasses, provinces, coasts and the drainage; climate at real-equivalent heights, biomes, woods, the landing site |

## src/Hollow.Simulation

The rules of the ground game, engine-free: the zone host, its systems, entities, physics, content, events.

### src/Hollow.Simulation (8 files, 1,669 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Beaches.cs` | 125 | Beaches | Where players arrive and respawn "on the beach", as in Rust (GROUND.md §6.1, docs/WHOLE_WORLD.md decision 8): coast |
| `GroundProbe.cs` | 150 | GroundProbe | The surface's own ground (its heights, not the collision tiles' triangles), asked the same way by the zone and by a |
| `Outcome.cs` | 334 | OutcomeCode, Outcome, OutcomeCodes | The one answer vocabulary (GROUND_SYSTEMS.md §3.2): every action a player asks for is done, or refused with one of |
| `Registry.cs` | 141 | Registry | Handlers found by a list's index (GROUND_SYSTEMS.md §3.5): the list comes from the data (capabilities.json's keys) |
| `Zone.cs` | 435 | Character, Named, Zone | A character's components: its movement state, this tick's input and what its last step did. Its collision is its |
| `ZoneEnvironmentOwner.cs` | 133 | ZoneEnvironmentOwner | ZoneEnvironmentOwner: owns one zone's generated source and every reader that must retire before its world lease is released, with rollback. |
| `ZoneEvents.cs` | 117 | GainReason, IZoneEvent, ZoneEventHandler, ZoneEvents [Channel, Channel] | Why items arrived, for the UI's notifications. |
| `ZoneSpec.cs` | 234 | GroundKind, ZoneSpec | Which surface zone this is (GROUND.md §4.2): the body, what the server and every client need to build the same |

### src/Hollow.Simulation/Animals (7 files, 3,189 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Animal.cs` | 318 | AnimalState, TargetKind, AnimalTarget, Herd, Animal | What an animal is doing (GROUND.md §6.9); others see it (4 bits) and animate by it. |
| `AnimalHitboxes.cs` | 75 | AnimalHitboxes | An animal's hit shapes (GROUND.md §6.9), from its species' size and a pose: a capsule for the body along its length, |
| `AnimalWorld.Brain.cs` | 712 | AnimalWorld [Senses] | The animals' AI (GROUND.md §6.9): what each one notices of players, noises and other animals, and what it does about |
| `AnimalWorld.Spawning.cs` | 498 | SpawnArea, SpawnSlot, AnimalWorld | A cube-sphere tile of the world where animals live (GROUND.md §6.9, docs/WHOLE_WORLD.md §7.3: the level nearest the |
| `AnimalWorld.Steering.cs` | 265 | AnimalWorld | Steering (GROUND.md §6.9): a decision becomes a heading and a speed. Context steering: sixteen headings around the one |
| `AnimalWorld.cs` | 735 | AnimalObserver, AnimalNoise, IAnimalHost, AnimalWorld | A player as the animals see and hear them this tick (the zone fills the list). |
| `NavGrid.cs` | 586 | NavFlags, NavArea, NavGrid | What a cell of the navigation grid holds. |

### src/Hollow.Simulation/Authorization (2 files, 65 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AuthorizationSubjects.cs` | 26 | AuthorizationSubjects | Maps a target to the player controlling it (a character itself, a ship's pilot or owner); passengers and crew lists grant no immunity. |
| `DeviceAuthorizationState.cs` | 39 | DeviceAuthorizationMode, DeviceAuthorizationState, DeviceAccessFacts, DeviceAuthorizationPolicies | A device's own authorisation: grants, exclusions and mode (follow the cupboard or manual only), and the named policies that decide who is friendly |

### src/Hollow.Simulation/Building (17 files, 6,329 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `BuildResults.cs` | 126 | Privilege, IBuildingWallet, Wallet, Capsule, BuildingChange, BuildingEvent, IBuildingPart, Removal | Building rights at a place for a player (GROUND.md §8's privilege status). |
| `BuildingBlueprint.cs` | 160 | BuildingBlueprint [NodeSpec, Spec, Node] | BuildingBlueprint: an authored, socket-linked building (nodes, specs) assembled through the ordinary building world, after the caller checks the site. |
| `BuildingBlueprintSpawner.cs` | 90 | BuildingBlueprintSpawner | BuildingBlueprintSpawner: checks an entire prefab site before creating any gameplay objects, then builds it through the normal building world. |
| `BuildingData.cs` | 995 | PieceFamily, PieceShape, Opening, StairsKind, SocketKind, DamageType, SocketDef, PieceDef, Protection, GradeDef, LeafDef, DoorDef, BuildingData [Reader] | What a piece is, structurally (RustMVP's families): it decides its sockets' geometry and its support losses. |
| `BuildingPhysics.cs` | 282 | RayHit, OverlapHit, BuildingPhysics [OnlyGround, NotGround, OnlyBuilding] | What a ray hit: the body, how far along, where and the surface's normal there. |
| `BuildingWorld.Destroyed.cs` | 59 | RemovedPart, BuildingWorld | A piece or door gone from the building as the Destroyed event names it (GROUND.md §6.4.2, §9.4): what it was (its |
| `BuildingWorld.Hammer.cs` | 278 | DamageHit, DamageResult, BuildingWorld | A hit on a piece or a door: the attack's direction in the zone (from the attacker toward the target) and its damage by type. |
| `BuildingWorld.Placement.cs` | 664 | PlacementPlan, BuildingWorld | What a piece's placement would be: the piece, its structure (none yet for a free foundation), its pose in the |
| `BuildingWorld.Replica.cs` | 361 | BuildingWorld, LockKind, BuildPartKind | The building as a copy of someone else's (GROUND.md §6.3, phase 2): a world loaded from storage, and a client's |
| `BuildingWorld.cs` | 662 | BuildingWorld | Building in one zone (GROUND.md §6.3): its structures and pieces, doors and claim devices, and every rule about them, |
| `ClaimDevice.cs` | 509 | ClaimDevice, BuildingWorld | The claim device, our tool cupboard (GROUND.md §6.3): it stands on a piece of a structure, lists the players |
| `Doors.cs` | 694 | LockDef, DoorLock, KeyLock, CodeLock, Door, DoorLeaf, BuildingWorld | A lock as something to place (building.json's locks, GROUND_SYSTEMS.md §6): its key (its item's), name, kind, and the |
| `ItemWallet.cs` | 31 | ItemWallet | Building materials as items in containers (GROUND.md §6.3, phase 2): a player's main and belt slots pay for placing, |
| `LockHosts.cs` | 56 | ILockHost, LockState, LockMounts | ILockHost (a target that carries a lock), LockState (validated restoration) and LockMounts (the authored lock socket), shared by doors and standalone hosts. |
| `PieceGeometry.cs` | 688 | Pose, SocketId, PieceGeometry | A piece's place in its structure's frame: the pivot in metres (Y up) and the yaw in steps of 30°. Every yaw in a |
| `PieceShapes.cs` | 367 | ConvexPart, PieceShapes | One convex part of a piece's collision, in the piece's local metres: its corner points (what Jolt builds a hull |
| `Structure.cs` | 307 | BuildingFrame, Link, Piece, Structure | A structure's frame (GROUND.md §6.3): its origin in the zone and its axes, with +Y the planet's up at its first |

### src/Hollow.Simulation/Capabilities (10 files, 1,367 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Capability.cs` | 108 | CapabilitySet, Viewer, ICapability, ILifetimeCapability, IDependentCapability [Enumerator] | A set of capabilities (GROUND_SYSTEMS.md §2.1): bit i is capabilities.json's entry i. A thing's set |
| `Handlers.cs` | 272 | IOwned, IContainerHolder, IAuthorisations, ILifetime, CapabilityHandlers [Owned, Holds, Works, Burner, Machine, DoorCapability, KeyLockCapability, CodeLockCapability, CupboardCapability, ShipCapability, ExplosiveCapability, Standing, FireCapability, Lifetime] | A thing with an owner: the player who placed or built it (0: none). |
| `Interactions.Building.cs` | 225 | CapabilityHandlers [DoorCapability, KeyLockCapability, CodeLockCapability, CupboardCapability] | The interaction side of the building's capabilities (GROUND_SYSTEMS.md §5, GROUND.md §6.3): doors, their key and code |
| `Interactions.Compound.cs` | 37 | CapabilityHandlers [GateCapability] | The gate capability: open and close verbs for compound deployables, allowed through lock and building rules, run by CompoundInteractionSystem |
| `Interactions.Defense.cs` | 47 | CapabilityHandlers [DefenseCapability] | The defense capability: switch on or off, open the ammunition, authorise or clear oneself or everyone, offered by the viewer's device access |
| `Interactions.Names.cs` | 42 | ICustomNamed, CapabilityHandlers [Nameable] | ICustomNamed and the nameable capability: the owner's rename verb, with the name normalised and the map revision bumped |
| `Interactions.Power.cs` | 48 | CapabilityHandlers [ElectricCapability] | The electric capability: power-enable and power-disable verbs for generators, consumers and storage, by ownership and building privilege (the old connect verbs are retired) |
| `Interactions.ShipEngine.cs` | 43 | CapabilityHandlers [ShipCapability] | The ship capability's engine verbs: engine on or off and open fuel, offered to the authorised at the helm or anywhere aboard |
| `Interactions.Things.cs` | 249 | VerbAction, VerbActions, CapabilityHandlers [Holds, Pickup, Portable, Burner, Machine, Station] | What a verb does, on the server: the context's zone, doer and thing. |
| `Interactions.World.cs` | 296 | CapabilityHandlers [WoundedCapability, CharacterCapability, DudCapability, SeatCapability, ShipDoorCapability, MagazineCapability, FuelCapability, ArrowCapability] | The interaction side of people, explosives, ships and spent arrows (GROUND_SYSTEMS.md §5): helping the wounded up, a |

### src/Hollow.Simulation/Combat (8 files, 1,524 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Attacks.cs` | 69 | AttackDomain, AttackId, AttackRequest, AttackResolution, AttackOrder, AttackJournal | The attack protocol: AttackDomain, AttackId, request and resolution records, AttackOrder (in-order handling) and AttackJournal (bounded decision history, duplicate replay). |
| `ContactGeometry.cs` | 25 | ContactGeometry | Capsule and box surface normals of the same analytic shapes weapon traces use, for contact feedback. |
| `CorpseHitShape.cs` | 15 | CorpseHitShape | The solid shape of a harvestable player body, shared by authority traces and predicted attack targeting. |
| `Damage.cs` | 185 | DamageDelivery, DestroyCause, TargetFamilies, TargetFamilySets, DamageAmounts, DamageRequest, DamageOutcome | The damage pipeline's common types (GROUND.md §6.4.2, after RustMVP's damage plan, docs/damage_architecture.md): a |
| `Firearms.cs` | 551 | RecoilState, RecoilSample, Recoil, GunMods, GunState, GunActionKind, GunAction, ShotContext, Shot, FirearmRules, Ballistics | The recoil of the gun in hand (RustMVP weapon_rules.hpp RecoilState): the target the view is kicked to, where |
| `Geometry.cs` | 198 | Geometry | Ray tests for hits (melee sweeps, arrows, later bullets): against spheres, capsules and oriented boxes, all in the |
| `Hitboxes.cs` | 434 | Pose, HitPose, HitShape, Hitboxes, HitboxHistory [Track, ShipTrack] | How a body lies, for its hit shapes. |
| `Rockets.cs` | 47 | RocketMotor, RocketFlight | A battery's rocket motor (IRON_WAKE.md §6.4) and the one step the server flies it and clients draw it with. |

### src/Hollow.Simulation/Content (22 files, 4,210 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Animals.cs` | 257 | Temperament, AttackKind, AttackStats, AnimalSpecies, AnimalSenses, AnimalSpawning, HarvestRates | How an animal meets a threat (GROUND.md §6.9). |
| `Batteries.cs` | 378 | BatteryFire, LoaderPer, LoaderEase, RecoilCurve, LaunchProfile, BatteryLaunch, BatteryMount, LoaderMotion, LoaderPhase, LoaderHolder, BatteryLoader, BatteryDef | The crewed batteries' kinds (batteries.json, IRON_WAKE.md §6): launch profiles, mount, recoil, loader phases and holders. |
| `CapabilityData.cs` | 150 | CapabilityData | capabilities.json (GROUND_SYSTEMS.md §2.1): the kinds of behaviour a world thing may have, in their append-only order |
| `CompoundCatalog.cs` | 42 | CompoundPart, CompoundSocket, CompoundHull, CompoundAssembly, CompoundCatalog | CompoundCatalog: authored assemblies in local metres (parts with pivots, convex hulls, sockets), found by key and hashed with the gameplay content |
| `CompoundStats.cs` | 81 | CompoundGatePart, CompoundLockSocket, CompoundGateStats, CompoundClimbStats | Gate and climb stats of a compound deployable: reading from deployables.json and checking parts, open yaw, lock socket, span, width and speed |
| `ContactHazardStats.cs` | 34 | ContactHazardStats | ContactHazardStats: data for damage on physical contact with an assembly's authored hulls, independent of item names. |
| `DefenseStats.cs` | 93 | DefenseTargets, DefenseStats | DefenseStats: an automated turret's capability (targets, range, muzzle, one gun slot taking its rounds), its JSON reader and the container-move check. |
| `Explosives.cs` | 209 | BlastStats, ThrowableStats, FireDef, FireRules, RaidAlertSettings | One explosion (explosives.json blasts, GROUND.md §6.6): how far it reaches, how it falls off, what it does to |
| `GameContent.cs` | 467 | ContentException, GameContent [Loaded] | The content files have problems; the message lists every one. |
| `Gather.cs` | 137 | GatherRate, SpotRule, LastHitRule, GatherCue, MarkerLook, FallLook, ShakeLook, GatherLook, GatherRules | Which of a tool's gather rates a class of natural objects takes (items.json tree, ore). |
| `HeldActionTiming.cs` | 52 | HeldActionTiming | HeldActionTiming: durations presentation uses for held actions on the tick grid, with art markers retimed around the gameplay contact tick. |
| `Interactions.cs` | 116 | VerbSwitch, VerbDef, InteractionData | Which way a verb turns a device's switch: the loot panel's on/off button finds its verb by this. |
| `ItemDef.cs` | 329 | WearLayer, BodyRegions, HitRegion, MeleeStats, BowStats, AmmoStats, ConsumeStats, WearStats, FuelStats, CookStats, BuildingRole, ItemDef, CharacterTicks | Clothing layers (GROUND.md §6): two worn items may not share a layer, so a shirt and a chest plate go together but |
| `ItemParts.cs` | 256 | ItemPart, HeldUse, ItemParts, ItemPartTable [Enumerator, Row] | A part of an item (GROUND_SYSTEMS.md §7): a block of its data that gives it behaviour (items.json's melee, |
| `JsonFields.cs` | 220 | JsonFields | Reads fields of the content files and notes every problem (a missing field, a wrong type, an unknown name) with |
| `PhysicalSurfaceData.cs` | 112 | SurfaceDef, PhysicalSurfaceData | PhysicalSurfaceData: the physical materials content declares (SurfaceDef ids and keys, zero is unknown) and the terrain cover found at a contact. |
| `PowerStats.cs` | 95 | PowerStats | PowerStats: shared electrical ratings of a deployable (rates in energy units per second, capacity), with ports and a fallback for old definitions. |
| `Tables.cs` | 487 | ItemAmount, Recipe, NaturalClass, NaturalKind, DeviceStats, DeployableDef, BayStats, LootTable, Category, SurvivalTuning, LifetimeRestart, LifetimeRule [Entry] | An amount of an item: a recipe's ingredient, a yield. |
| `Texts.cs` | 163 | Texts | The player's lines (GROUND_SYSTEMS.md §3.2, texts.json): one for every OutcomeCode, the one place |
| `Weapons.cs` | 260 | FireMode, RecoilMode, AttachmentSlot, RecoilStep, RecoilStats, SpreadStats, FirearmStats, BulletStats, AttachmentStats | How the trigger works: one shot a press, or shots while held. |
| `WiringStats.cs` | 24 | WiringStats | WiringStats: shared data for the held wiring tool; the cable span between endpoints belongs to the electrical graph. |
| `World.cs` | 248 | WorldPropKind, PropDef, RecyclerStats, ResearchStats, TechNode, TechTree, LootSpotSettings, WorldData | What a world prop is (world.json props, GROUND.md §6.8). |

### src/Hollow.Simulation/Content/Readers (27 files, 5,590 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AnimalsReader.cs` | 301 | AnimalsReader | Reads animals.json (GROUND.md §6.9) and links its yields and tools to items. |
| `BatteriesReader.cs` | 366 | BatteriesReader | Reads batteries.json (IW3) and checks it: profiles for every rocket, the phases against the mount, named phases. |
| `BuildingReader.cs` | 103 | BuildingReader | building.json (GROUND.md §6.3) has a parser of its own (BuildingData); its items are linked here: a door |
| `CapabilitiesReader.cs` | 41 | CapabilitiesReader | capabilities.json (GROUND_SYSTEMS.md §2.1): the capabilities' keys, in their append-only order (at most 64: a thing's set is one word). |
| `CategoriesReader.cs` | 25 | CategoriesReader | categories.json: the crafting tabs, each a key and a name; an item's category is one of them. |
| `CompoundReader.cs` | 75 | CompoundReader | Reads compound.json (multi-part assemblies: parts, pivots, collision hulls, sockets) into the CompoundCatalog and checks parents, hulls and sockets. |
| `ContentReaders.cs` | 39 | ContentReaders | The readers in the order the loader runs them (GROUND_SYSTEMS.md §12.6): each file after the files it refers to |
| `CrossChecks.cs` | 97 | CrossChecks | The checks across the files, last (GROUND.md §7): every recipe can be made from what can be gathered. Starting from |
| `DeployablesReader.cs` | 199 | DeployablesReader | deployables.json: everything placed from the inventory (bags, beds, fires, furnaces, boxes, workbenches, the |
| `ExplosivesReader.cs` | 279 | ExplosivesReader | Reads explosives.json (G5) and links rounds' blasts (weapons.json) to it; checks both against building.json's grades. |
| `GatherReader.cs` | 368 | GatherReader | gather.json: the natural kinds (trees, outcrops, pickups, bushes, boulders) with their health, yields, stages, |
| `IContentReader.cs` | 89 | IContentReader, ContentReading, ContentChecking | One data file's reader (GROUND_SYSTEMS.md §12.6, decision K12): every file in GameContent.Files and the |
| `InteractionsReader.cs` | 104 | InteractionsReader | Reads interactions.json (GROUND_SYSTEMS.md §5) once survival.json's reaches are known (a verb names one of them). |
| `ItemsReader.cs` | 312 | ItemsReader | items.json: every item with its parts (melee, bow, ammo, consume, wear, fuel, |
| `LootReader.cs` | 79 | LootReader | loot.json: the loot tables (guaranteed entries, then weighted rolls): the respawn kit, the load tests' kits, the |
| `PhysicalSurfacesReader.cs` | 48 | PhysicalSurfacesReader | Reads the physical-surfaces data file and checks that every terrain, grade, door, deployable, natural object, ship and part has a surface. |
| `PlacementReader.cs` | 393 | PlacementReader | placement.json (GROUND_SYSTEMS.md §6): the defaults every placement rule starts from, each kind's block over them, the |
| `ReaderHelpers.cs` | 74 | ReaderHelpers | Fields more than one reader shares: lists of numbers as vectors (the deployables' bays, the ships) and a ship's turn axis. |
| `RecipesReader.cs` | 127 | RecipesReader | recipes.json: every recipe (output, amount, seconds, workbench level, ingredients, known from the start, its |
| `ShipInteriorReader.cs` | 424 | ShipInteriorReader | A ship's data file (IRON_WAKE.md §9.1, game/data/ground/ships/), as a ships.json entry's interior block |
| `ShipLayoutReader.cs` | 538 | ShipLayoutReader | A ships.json entry's layout (IRON_WAKE.md §5.1): its seats, doors, target parts, nozzles and |
| `ShipsReader.cs` | 445 | ShipsReader | ships.json (G6): the kinds of ship: mass and inertia, hit points and damage multipliers, thrust and speeds, boost, |
| `SurfacesReader.cs` | 21 | SurfacesReader | surfaces.json (GROUND.md §6.1): the drawn tops of the outcrops' stage models, measured by the model viewer |
| `SurvivalReader.cs` | 219 | SurvivalReader | survival.json: vitals, metabolism, bleeding, burning, the wounded state, temperature, the climate stand-in, the |
| `TextsReader.cs` | 54 | TextsReader | texts.json: the player's lines (GROUND_SYSTEMS.md §3.2), read like the rest but outside the hash (a reworded line or |
| `WeaponsReader.cs` | 317 | WeaponsReader | weapons.json (G4): the firearms with their cadence, ballistics, spread and recoil, the kinds of rounds by calibre |
| `WorldReader.cs` | 453 | WorldReader | Reads world.json (G7) and links it to the items, recipes, loot tables and deployables; then checks it. |

### src/Hollow.Simulation/Diagnostics (1 file, 352 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `TickProfile.cs` | 352 | ProfileSection, TickProfile, TickProfileExtensions [Scope] | The parts of a server tick that TickProfile times (GROUND.md §5.11). |

### src/Hollow.Simulation/Economy (1 file, 99 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Rules.cs` | 99 | Recycling, Research, TechTrees | The recycler's rules (GROUND.md §6.8, Rust's): a loot-only component gives its own list (world.json recycle); |

### src/Hollow.Simulation/Electricity (3 files, 412 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `ElectricalGraph.cs` | 280 | ElectricalGraph [Entry, Circuit] | ElectricalGraph: indexed, directed port graph of cables and devices; edits rebuild only touched circuits, and only timed sources and storage are scheduled. |
| `ElectricalRecords.cs` | 65 | PowerPortDirection, PowerTransfer, PowerPort, PortRef, RouteAnchor, CableRecord, PowerPorts | The electrical grid's records: power ports, cables with route anchors, port references, and the shared cube picking and anchor validation (PowerPorts). |
| `PowerTransferRules.cs` | 67 | ElectricalFlow, PowerTransferRules [ITransfer, Generator, Consumer, Storage] | Generator, consumer and storage transfer rules registered once per profile, delivering and settling power flow (ElectricalFlow) without looking at item identity. |

### src/Hollow.Simulation/Entities (2 files, 272 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `EntityId.cs` | 65 | EntityId, EntityIdAllocator | A persistent entity's permanent 64-bit ID (ARCHITECTURE.md §7): 41 bits of milliseconds since 1 January 2026, |
| `EntityStore.cs` | 207 | Entity, EntityStore, IComponentPool, ComponentPool | A live entity in one EntityStore: its slot and the slot's generation, so a handle kept after the |

### src/Hollow.Simulation/Events (16 files, 459 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AttackResolved.cs` | 7 | AttackResolved | The AttackResolved zone event: a player's attack and the AttackResolution it came to. |
| `Crafted.cs` | 19 | Crafted | To a player: a crafted unit is done. (GROUND_SYSTEMS.md §12.3) |
| `Destroyed.cs` | 53 | Destroyed | To everyone near (GROUND.md §6.4.2): something was destroyed: a deployable or world prop broken, a building piece, door or claim device broken, brough… |
| `Detonated.cs` | 49 | Detonated | To everyone near (G5): an explosion. (GROUND_SYSTEMS.md §12.3) |
| `Died.cs` | 25 | Died | To a player: they died. (GROUND_SYSTEMS.md §12.3) |
| `FireLit.cs` | 36 | FireLit | A fire was lit where a blast left it (IW4, IRON_WAKE.md §7), or fell onto something else when what held it went: the |
| `FireOut.cs` | 19 | FireOut | A fire went out (IW4, IRON_WAKE.md §7): which, where, and why (burnt out, doused, replaced, fell with nothing below). (GROUND_SYSTEMS.md §12.3) |
| `GotUp.cs` | 21 | GotUp | To a player: they got up again. (GROUND_SYSTEMS.md §12.3) |
| `HitConfirmed.cs` | 50 | HitConfirmed | To the attacker: their hit landed, with what it did. (GROUND_SYSTEMS.md §12.3) |
| `Hurt.cs` | 27 | Hurt | To the victim: damage taken, from a direction. (GROUND_SYSTEMS.md §12.3) |
| `ItemsGained.cs` | 26 | ItemsGained | To a player: items gained (gathering, picking up, crafting, looting) or lost (a tool broke, fuel burnt). (GROUND_SYSTEMS.md §12.3) |
| `Notice.cs` | 18 | Notice | To a player: a line to read (a built ship waiting for room, G6). (GROUND_SYSTEMS.md §12.3) |
| `ProjectileEnded.cs` | 31 | ProjectileEnded | To everyone near: a projectile stopped. (GROUND_SYSTEMS.md §12.3) |
| `ProjectileLaunched.cs` | 33 | ProjectileLaunched | To everyone near: a projectile was loosed (an arrow, or a bullet with its shot's sequence number). (GROUND_SYSTEMS.md §12.3) |
| `TurretFired.cs` | 25 | TurretFired | A crewed battery fired (IW3): ship, station, barrel, the shot's number, tick, round, gunner. |
| `WentDown.cs` | 20 | WentDown | To a player: they were knocked down, wounded. (GROUND_SYSTEMS.md §12.3) |

### src/Hollow.Simulation/Interaction (2 files, 276 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Interaction.cs` | 243 | InteractOption, InteractArgs, DoerState, Doer, InteractContext, IPickupFamily, IAuthoriseFamily, IRepairFamily, IInteractCapability, InteractionRules | One thing the E key can do to a target now (GROUND_SYSTEMS.md §5): a verb (interactions.json's number), done or |
| `Names.cs` | 33 | Names | Names: validates and normalises player-facing labels (NFC, no control characters, at most 48 characters and 192 bytes); never paths or commands. |

### src/Hollow.Simulation/Items (11 files, 2,320 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AttachmentRules.cs` | 70 | AttachmentRules | Putting attachments on firearms and taking them off (GROUND.md §6.3): the server applies these to its containers and |
| `AttachmentTransfer.cs` | 57 | AttachmentTransfer | AttachmentTransfer: one atomic drag between a gun's attachment socket and an inventory or storage slot, or another socket. |
| `Blueprints.cs` | 61 | BlueprintBook | The blueprints a character has learnt (GROUND.md §6.8, Rust's): recipes not known from the start, unlocked in a |
| `Container.cs` | 349 | ItemStack, ContainerKind, Container | Some of one item in a slot. Empty when Item is 0. A firearm (G4) also carries its magazine and its |
| `CraftQueue.cs` | 230 | CraftJob, CraftQueue | A crafting job: Count units of a recipe left, the front one TicksLeft from done. |
| `CraftingSpeed.cs` | 33 | CraftingSpeed | The workbench's speed (GROUND_SYSTEMS.md §7, the station capability's share of crafting): a job takes its recipe's time |
| `InventoryRules.cs` | 258 | InventoryRules | Moving items between slots (GROUND.md §6, Rust's rules): the server applies these to its containers, and the client |
| `ItemActions.cs` | 403 | ItemActionDef, ItemActionOption, IItemActor, ItemActionContext, IItemAction, ItemActions [Use, Eat, WearIt, Unequip, Learn, Unload, Remove] | One of items.json's actions (GROUND_SYSTEMS.md §7): a button of the item panel's ACTIONS. Its place in the list |
| `ItemRows.cs` | 619 | ItemRow, ItemDescription, IItemDescriber, ItemRows [WiringRows, MeleeRows, BowRows, ArrowRows, ConsumableRows, WearableRows, FuelRows, CookableRows, DeployableRows, ShipRows, FirearmRows, RoundRows, AttachmentRows, ThrowableRows, PlanRows, HammerRows, CupboardRows, LockRows, KeyRows, DoorRows, BlueprintRows, ComponentRows] | A row of an item panel's INFORMATION (GROUND_SYSTEMS.md §7): "Rate of fire" and "450 rpm"; good news or not. |
| `LootRules.cs` | 59 | LootKind, LootRules | What is being looted, which decides what may be put in. |
| `PlayerInventory.cs` | 181 | ContainerRef, PlayerInventory | How an intent names a container: the player's own three, or one of those they are looting. |

### src/Hollow.Simulation/Movement (9 files, 1,366 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `CharacterMotor.cs` | 386 | MoveContext, StepEvents, CharacterMotor | What a movement step needs to know about where it happens: the frame it runs in (IMoveFrame: the |
| `CharacterState.cs` | 154 | Stance, MoveButtons, MoveFlags, MoveCheats, MoveInput, CharacterState |  |
| `CharacterWorld.cs` | 86 | CharacterWorld | A character in the zone's frame, wherever it moves (IRON_WAKE.md §4.2): aboard a ship its state is the ship's frame's, |
| `Climbing.cs` | 55 | ClimbSurface, IClimbSource, ClimbContact, Climbing | Climbing: ClimbSurface spans, the IClimbSource interface and one contact query shared by authoritative movement and client prediction. |
| `Facing.cs` | 96 | Facing | Which way things face on a whole world (docs/WHOLE_WORLD.md §5). A zone's frame is the body's own (+Y the north pole), |
| `FreeLook.cs` | 58 | FreeLook | Free look (Left Alt held, RustMVP's; GROUND.md §4.5): the mouse turns the head instead of the view, so the camera |
| `MoveFrames.cs` | 293 | IMoveFrame, ZoneMoveFrame, ShipPose, IShipFrame, MoveFrames | The frame a character moves in (IRON_WAKE.md §4.3): the zone's, or a ship's. It says which way is up, how strong |
| `MovementCheck.cs` | 111 | MovementCheck [Result] | The physics spike of GROUND.md §4.5, kept as a check: a fixed script of inputs walks a character over ground from |
| `MovementTuning.cs` | 127 | MovementTuning | The numbers of on-foot movement (GROUND.md §4.5, §6). Rust's movement, through RustMVP's legacy spec |

### src/Hollow.Simulation/Nature (9 files, 2,421 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `LootSpots.cs` | 435 | LootSite, LootSpot, LootSpots [Patch] | What kind of place a loot cluster stands in (GROUND.md §6.8). |
| `NaturalBodies.cs` | 186 | NaturalBodies | Keeps the trees and outcrops of every ground tile in a PhysicsWorld as static props, so characters |
| `NaturalCollisionPlan.cs` | 154 | NaturalCollisionLimits, NaturalCollisionReservation, NaturalCollisionShape, NaturalCollisionEntry, NaturalCollisionKind, NaturalCollisionPlan, NaturalCollisionSnapshot | NaturalCollisionPlan: immutable solid-shape inputs for a set of root tiles, with limits, reservations and depletion snapshots for preparing collision off thread. |
| `NaturalObjects.cs` | 176 | NaturalObject, INaturalObjects, IPlannedNaturalCollision, NaturalShapes | One natural object (GROUND.md §4.6): a tree, an outcrop, a pickup or a bush, generated from the surface, never |
| `NaturalSegmentQuery.cs` | 118 | NaturalSegmentQuery | NaturalSegmentQuery: segment broadphase over the six cube-face quadtrees; each leaf visited once, nothing generated until intersected leaves are sorted. |
| `NaturalState.cs` | 326 | NaturalOverride, GatherSwing, GatherResult, NaturalState | A player change to a natural object (ARCHITECTURE.md P1: only changes are stored). |
| `ScatterNaturalObjects.cs` | 374 | NaturalObjectCacheDiagnostics, ScatterNaturalObjects [CacheEntry, TileObjects] | The natural objects of a real surface (GROUND.md §6.1): the planet's own scatter (Scatter), the same |
| `SpotSurfaces.cs` | 410 | ISpotSurface, SpotFrames, TrunkSurface, MeasuredSurface | Where a natural object's weak spot can sit (GROUND.md §6.1): on the surface players see, at the object's current |
| `StandInNaturalObjects.cs` | 242 | StandInNaturalObjects [CacheEntry] | Natural objects of the stand-in world (docs/WHOLE_WORLD.md §7.1), placed by a seeded stream per cube-sphere tile of |

### src/Hollow.Simulation/Physics (10 files, 3,037 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `CharacterBody.cs` | 174 | CharacterBody | A character's collision in a space (GROUND.md §4.5): Jolt's virtual character, a kinematic capsule outside the |
| `CompoundGeometry.cs` | 145 | CompoundGeometry [Shapes] | CompoundGeometry: convex hulls of authored compounds, with a gate's open and closed leaves, driving both movement and weapon traces, with contact face and pose. |
| `GroundAreaCoordinator.cs` | 258 | GroundAreaStatus, GroundAreaRequest, GroundPhysicsOptions, GroundAreaTicket, GroundAreaCoordinator [Entry] | One physics owner's ground area requests: tickets over shared tile builds, a bounded worker pump on the owner thread, adoption, release and drain. |
| `GroundStreamingBudget.cs` | 455 | GroundStreamingLimits, GroundStreamingDiagnostics, GroundWorkState, GroundOwnedBuildFailure, GroundStreamingBudget, GroundStreamingOwner, GroundAreaLease, GroundBuildLease | The engine-independent ledger of ground streaming: limits, reservations for tiles, bodies, workers and bytes, work states, diagnostics, shared by owners. |
| `JoltRuntime.cs` | 88 | JoltRuntime | The process-wide Jolt runtime (GROUND.md §4.5). Jolt is initialised once per process, by whichever world needs it |
| `PhysicsWorld.GroundStreaming.cs` | 43 | GroundNativeEstimates, GroundStreamingEstimates, GroundBuildCost | PhysicsWorld's ground streaming costs: GroundNativeEstimates (versioned, no production defaults) and GroundStreamingEstimates, the inactive build-cost adapter. |
| `PhysicsWorld.SelectedGround.cs` | 318 | GroundPhysicsFailure, GroundNativeStage, GroundPhysicsHooks, PhysicsWorld [SelectedSetupRetirement, SelectedNativeTile, NativeSetup] | PhysicsWorld's inactive selected-ground factory: forks the source pin, reserves setup, builds native tiles, with failure and retirement handling (GroundPhysicsFailure). |
| `PhysicsWorld.cs` | 710 | PhysicsWorld [Prop, Tile, TileBuild] | One zone's Jolt physics system (GROUND.md §4.5): the ground near characters, as static mesh tiles built from the |
| `Props.cs` | 143 | PropLayer, ITileListener, PropKind, PropShape | Who a prop belongs to: its key is only unique within its layer. |
| `ShipSpace.cs` | 703 | SpaceHit, ShipSpace [PiecesOnly, KindShapes] | What a query through a ship's space met: the part (0 the hull), how far along, where and the surface's normal, in the ship's frame. |

### src/Hollow.Simulation/Placement (10 files, 2,578 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Aboard.cs` | 306 | PlacedSpace, AboardHit, AboardSpot, Aboard | Placing aboard a ship (IRON_WAKE.md §8.1): the free solver's half in the ship's frame, its claim settled there, and the checks' parts aboard (where, corners, sight, room). |
| `Checks.cs` | 241 | IPlacementCheck, Checks [Reach, Sight, Surface, Step, Slope, Support, Water, Rights, Overlap, Limits, Aboard, Own] | One check of the placement pipeline (GROUND_SYSTEMS.md §6), registered by name and run in placement.json's fixed order, |
| `Footprint.cs` | 239 | Footprint, BoxFootprint, HullFootprint [PointFootprint] | What a placement occupies (GROUND_SYSTEMS.md §6's footprint): a box (a deployable's or the claim device's size, or half |
| `Kinds.cs` | 338 | ResolvedPlaceable, IPlacementSite, PlacementSources, PlaceableKind, Kinds, DeployableKind, PieceKind, DoorKind, LockPlacement, CupboardKind, ChargeKind, ShipKind | What a kind finds a placeable's key to name in the game data (its definition and the items it involves). |
| `Placeable.cs` | 221 | PlacementCost, Placeable, PlacementData, PlacementContext, PlacementAim, PlacementClaim, PlacementCandidate | A material a placement costs (a building piece's twig, from the carried slots). |
| `PlacementRule.cs` | 132 | PlacementSurfaces, SupportMode, AlignMode, PrivilegeMode, PlacementRule | What a placement stands on or is fitted to (placement.json's surfaces): the ground, the top of a foundation, floor or |
| `PlacementWorld.cs` | 370 | PlacedThing, PlacedShip, PlacementHit, LockMount, IPlacementSurroundings, PlacementWorld | A deployable or prop as placement sees it: what it is named, its box (on its base, along up, turned by yaw). |
| `Placer.cs` | 151 | PlacementHandlers, Placer | The placement system's handlers (GROUND_SYSTEMS.md §6, §3.5): the solvers, rotations, checks and kinds, each found by |
| `Rotations.cs` | 134 | IPlacementRotation, Rotations [Free, Steps, Support, Flip, Fixed] | How a placement is turned (GROUND_SYSTEMS.md §6's rotation): from the view and R's presses on the client, from the |
| `Solvers.cs` | 446 | IPlacementSolver, Solvers, FreeSolver, SocketSolver, OpeningSolver, AttachSolver, SweepSolver | Turns an aim into candidates (GROUND_SYSTEMS.md §6), registered by name: free (onto the surface under the aim), socket |

### src/Hollow.Simulation/Players (4 files, 413 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Breathing.cs` | 35 | Breathing | Breathing: the underwater test at the eye and one breath reserve per player; time without air past the reserve returns suffocation damage. |
| `Climate.cs` | 98 | IClimate, PlanetClimate, StandInClimate | The air temperature at a place and time (GROUND.md §6): PlanetClimate on a real surface, |
| `MapMarker.cs` | 14 | MapMarker | A marker a player put on the map (GROUND.md §6.8, Rust's: up to five, theirs alone): a point in the zone frame, a |
| `Player.cs` | 266 | Vitals, ActionButtons, ActionInput, ActionKind, Player | The survival state of a character (GROUND.md §6): health and the needs, all in their own units. |

### src/Hollow.Simulation/Raid (3 files, 628 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Explosive.cs` | 274 | ExplosiveState, LiveExplosive, RaidHit, ExplosiveSave, BlastReport, RaidCounters | Where a thrown or placed explosive is (GROUND.md §6.6). |
| `Fire.cs` | 106 | FireEnd, Fire | Why a fire went out (the FireOut event). |
| `TestBases.cs` | 248 | TestBases [Hut] | Small claimed bases built straight through the building rules (no player, no wallet, no placement checks), for the raid |

### src/Hollow.Simulation/Ships (12 files, 3,807 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Ship.cs` | 309 | ShipDoorState, Ship, ShipLink, ShipSeat | A ship's door as the rules have it (IRON_WAKE.md §5.3): its progress (0 shut … 1 open) at a tick and which way it moves |
| `ShipAttitudeHold.cs` | 60 | ShipAttitudeHold | ShipAttitudeHold: captures an unattended ship's attitude and holds it; desired rates still pass through the motor's torque, fuel and contact rules. |
| `ShipCollider.cs` | 306 | ShipRayHit, ShipContact, ShipCollider [ExceptBody] | Where a ray from a ship met the world: how far, the point and the surface's normal there. |
| `ShipDef.cs` | 458 | HullBox, TurnAxis, ShipDef, ShipParts | One box of a ship's hull, in the ship's frame: its centre and half extents. |
| `ShipFrame.cs` | 66 | ShipFrame | A ship's frame in a zone (IRON_WAKE.md §4.3): what characters aboard it move in, keyed by the ship's entity ID. Up is |
| `ShipFuel.cs` | 88 | ShipFuel | ShipFuel: a tank's whole fuel units plus the fraction drawn into its engine; only whole units move through inventory; migrates old float saves. |
| `ShipInterior.cs` | 389 | ShipPartKind, ShipPartDef, ShipPiece, BoardingBox, ShipSocket, ShipBuildZone, ShipExclusion, StationBarrelData, StationGeometry, ShipFixture, ShipRoom, PartPose, ShipInterior | How one of a ship's parts moves (the data file's part kinds, IRON_WAKE.md §9.1). |
| `ShipLayout.cs` | 282 | SeatRole, SeatSitters, SeatWorks, DoorOpens, DoorEase, ShipPartHealth, ShipSeatDef, ShipDoorDef, ShipTargetPart, NozzleDrive, ShipNozzle, LightPower, ShipLight | What a seat's occupant does (ships.json seats' role, IRON_WAKE.md §5.2). |
| `ShipMotor.cs` | 827 | ShipContext, ShipMotor [SkidLoad] | What a ship's step needs to know about where it happens. |
| `ShipQueries.cs` | 156 | ShipQueryHit, ShipQueries | What a query of the zone's frame met in a ship's space: the ship's frame, the hit there, and how far along the query it is. |
| `ShipState.cs` | 204 | ShipFlags, ShipButtons, ShipInput, ShipState, ShipEvents | What a ship is doing, besides its motion. Some are found by the last step (outputs), some set by the zone. |
| `ShipStations.cs` | 662 | StationBarrel, ShipStationDef, StationInput, StationAim, LoaderState, StationState, StationRules, LoaderPoses [NodePose, HolderView] | A ship's stations (IRON_WAKE.md §6): defs, state, loader records, the mount and fire rules both sides run, the mechanism's poses. |

### src/Hollow.Simulation/Systems (44 files, 13,266 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `ActionSystem.cs` | 382 | ActionSystem, ActionSystemAccess [PlayerActor] | Players' actions (ARCHITECTURE.md §11.4: intents): every one checks everything against the server's state and |
| `AnimalSystem.cs` | 449 | AnimalSystem, AnimalSystemAccess [AnimalReceiver] | Animals in the zone (GROUND.md §6.9): the glue between the animal world (Animals/) and the rest of the game. Players |
| `ArrowSystem.cs` | 249 | ArrowSystem, ArrowSystemAccess [ArrowTargets] | Spent arrows (GROUND_SYSTEMS.md §5, the owner's): an arrow that stays whole on a hit sticks where it hit, its tip in, |
| `AutoRespawnSystem.cs` | 42 | AutoRespawnSystem, AutoRespawnSystemAccess | G1's respawn, for a zone without game content (the movement checks, the network tests): a dead character comes back |
| `BlastSystem.cs` | 231 | BlastSystem, BlastSystemAccess [RaidTallies, RaidTally] | What a blast sees, and the building's side of raiding (GROUND.md §6.6): the explosive channel's fractions of a point, |
| `BuildingSystem.cs` | 617 | HammerAction, BuildingSystem, BuildingSystemAccess [BuildingReceiver] | What a hammer does to a piece. |
| `CombatSystem.cs` | 787 | Projectile, TraceHit, CombatSystem, CombatSystemAccess [CharacterReceiver, NatureReceiver, GroundReceiver] | An arrow in flight (later bullets): simulated on the server only (ARCHITECTURE.md §11.5). |
| `CompoundInteractionSystem.cs` | 54 | CompoundInteractionSystem | CompoundInteractionSystem: authored compound interactions, gate state and climbable surfaces (IClimbSource), through the shared movement motor. |
| `ContactHazardSystem.cs` | 119 | ContactHazardSystem | ContactHazardSystem: damages characters touching a hazardous assembly's hulls through the existing damage receiver, armour, bleeding and death rules. |
| `DamageSystem.cs` | 379 | RayQuery, BlastQuery, BlastTarget, DamageSystem, DamageSystemAccess [IDamageReceiver, Blow] | A ray's test for what it meets first (a swing, an arrow's or a bullet's step): from where, which way, how far, how |
| `DefenseSystem.cs` | 241 | DefenseSystem, DefenseSystemAccess [MountState, Candidate] | DefenseSystem: authoritative automated mounts: target acquisition and lead, rights, power, magazines and the common projectile pipeline. |
| `DeviceAuthorizationSystem.cs` | 158 | DeviceAuthorizationSystem, DeviceAuthorizationSystemAccess [Binding] | DeviceAuthorizationSystem: binds each registered device to one live tool cupboard; local friendship and administrative rights are independent. |
| `ExplosiveSystem.cs` | 711 | ExplosiveSystem, ExplosiveSystemAccess | Raiding (GROUND.md §6.6, G5): explosives thrown, placed and lit again; their flight, bounces and sticking; fuses and |
| `FireSystem.cs` | 496 | FireCounters, FireSystem, FireSystemAccess [FireSpot, FireTargets] | What fires have done so far (the load test reports it). |
| `GroundSystems.cs` | 55 | GroundSystems | The zone's systems and their order (GROUND_SYSTEMS.md §12.2, K3), the one place that lists them. The host runs them |
| `GunSystem.Attachments.cs` | 22 | GunSystem | GunSystem's attachment moves: between containers and gun mods under the loot rules, bumping the gun's revision when the held gun changes. |
| `GunSystem.Automated.cs` | 36 | GunSystem | GunSystem's automated launch: a mounted gun's bullet enters the ordinary projectile, damage and ProjectileLaunched pipeline. |
| `GunSystem.cs` | 727 | Bullet, GunSystem, GunSystemAccess | A bullet in flight (G4): simulated on the server only (ARCHITECTURE.md §11.5); clients draw their own. |
| `HeldSystem.cs` | 113 | HeldSystem, HeldSystemAccess [IHeldRules, MeleeRules, BowRules, ActsByIntents, WiringRules] | What a held item does in the zone (GROUND_SYSTEMS.md §7): one handler per held behaviour, found by the item's |
| `HistorySystem.cs` | 77 | HistorySystem, HistorySystemAccess | Records every player's pose at the end of the tick (ARCHITECTURE.md §11.5): the second of poses hits are judged |
| `InteractionSystem.cs` | 232 | InteractionSystem, InteractionSystemAccess | Interaction (GROUND_SYSTEMS.md §5): one intent, one path. A player names a target and a verb; the server resolves the |
| `NatureSystem.cs` | 22 | NatureSystem, NatureSystemAccess | The natural objects' own life (GROUND.md §6.1): once a second, what players cut, mined or picked comes back when its |
| `PlacementSystem.cs` | 288 | PlaceRequest, PlacementSystem, PlacementSystemAccess [ZoneSurroundings] | What a player asks to place (PlaceIntent, GROUND_SYSTEMS.md §6): the slot that pays for it (the item used up, or the |
| `PlayerSystem.cs` | 458 | PlayerSystem, PlayerSystemAccess | The survival game's players (GROUND.md §6): their lives, sleep, death into a corpse, and respawn at a bag or on the |
| `PowerSystem.cs` | 117 | PowerSystem, PowerSystemAccess | PowerSystem: the zone's owner of the electrical graph: cable connect and restore, configuration rights, wind variation, and test solve helpers. |
| `ShipSystem.Attitude.cs` | 33 | ShipSystem | ShipSystem's attitude hold: keeps or clears the held target rotation when the ship's engine, autopilot or pilot changes. |
| `ShipSystem.Doors.cs` | 229 | ShipSystem | Doors (IRON_WAKE.md §5.3): a door is a part with a progress, 0 shut to 1 open, moved over its seconds by the ship's rules |
| `ShipSystem.Engine.cs` | 33 | ShipSystem | ShipSystem's engine switch (SetEngine): the crew's on and off through the ship radial, refused without fuel or crew rights. |
| `ShipSystem.Parts.cs` | 333 | ShipSystem [ShipReceiver] | A ship's crew, lock and parts (IRON_WAKE.md §5.2, §5.5): its list of authorised players (the tool cupboard's verbs at its |
| `ShipSystem.Seats.cs` | 523 | SeatRelease, ShipSystem, ShipSeats | Why a seat's occupant leaves it (IRON_WAKE.md §5.2): every way out goes through ShipSystem.Release. |
| `ShipSystem.Spaces.cs` | 218 | ShipSystem | Ships people walk in (IRON_WAKE.md §4): who is aboard, a parked ship's body in the zone, and the one entry point that |
| `ShipSystem.Stations.cs` | 560 | ShipSystem | Crewed batteries each tick (IW3): the gunner's controls, the mount, the loaders, the trigger and its refusals, the magazine. |
| `ShipSystem.cs` | 730 | ShipEventKind, ShipDamage, ShipEvent, ShipSystem, ShipSystemAccess | What happened to a ship that players should hear about (G6). |
| `SpillSystem.cs` | 253 | SpillSystem, SpillSystemAccess | Loose items on the ground (GROUND.md §6.1). Everything that lands there goes through Spill: a stack dropped from an |
| `SupportSystem.cs` | 118 | SupportSystem, SupportSystemAccess | Support links (GROUND_SYSTEMS.md §3.6): what rests on or is stuck in what. A dependent keeps its own SupportLink (its |
| `TargetSystem.cs` | 1085 | TargetSystem, TargetSystemAccess [GroundTargets, CharacterTargets, AnimalTargets, NaturalTargets, ThingTargets, PieceTargets, DoorTargets, CupboardTargets, ExplosiveTargets, ShipTargets] | Naming world things (GROUND_SYSTEMS.md §3.1): a TargetRef routes to its family, each family registered once here, which |
| `ThingSystem.Corpses.cs` | 52 | ThingSystem | ThingSystem's corpses: ray tracing against a body's hit shape and harvesting a body with a tool, advancing it and giving the yield. |
| `ThingSystem.Repair.cs` | 44 | ThingSystem | ThingSystem's hammer repair of a deployable: privilege, damage delay and material cost checked, then health restored. |
| `ThingSystem.cs` | 719 | ThingSystem, ThingSystemAccess [DeployableReceiver] | Static things of the survival game (GROUND.md §6): deployables and their devices, dropped items, corpses; warmth and |
| `VitalsSystem.cs` | 162 | VitalsSystem, VitalsSystemAccess | Survival, once a second (GROUND.md §6): metabolism, temperature, bleeding, healing, and the damage of starvation, |
| `WiringSystem.cs` | 142 | WireOperation, WiringSystem, WiringSystemAccess [Draft] | The wiring tool's authoritative transactions: begin, anchor, complete, cancel and disconnect a cable with reach, sight and rights checks; drafts expire. |
| `WorldSystem.cs` | 707 | PropOverride, WorldStats, WorldSystem, WorldSystemAccess | What is stored about a world prop (GROUND.md §6.8, ARCHITECTURE.md P1: only changes): gone until a time, or standing |
| `WoundedSystem.cs` | 120 | WoundedSystem, WoundedSystemAccess | The wounded state (GROUND.md §6.3, Rust's): a lethal hit that is small enough, and isn't a shot to the head, knocks |
| `ZoneSystem.cs` | 143 | ZoneSystem | One concern of a zone's rules (GROUND_SYSTEMS.md §12.2, K1): its own state, stepped by the host in the host's order. |

### src/Hollow.Simulation/Targets (5 files, 763 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `SupportLink.cs` | 26 | SupportLink | A world thing resting on or stuck in another (GROUND_SYSTEMS.md §3.6): what holds it (its support; the part may name |
| `SurfaceContact.cs` | 22 | SurfaceContact | SurfaceContact: immutable authoritative collision evidence (target, surface, point, normal, anchor) captured before damage can remove its target. |
| `TargetRef.cs` | 163 | TargetFamily, TargetRef, ViewerFlags, TargetPose, ITargetFamily, ITargetable | The families of world things (GROUND_SYSTEMS.md §3.1): each is one store of the zone with its own lookup, and |
| `TargetVitality.cs` | 37 | TargetVitality, AimedVitality, TargetHealthQuery | TargetVitality (optional health of a target), AimedVitality and TargetHealthQuery: a nearby damaged object at the crosshair. |
| `Targeting.cs` | 515 | AimRay, AimKind, AimHit, TargetFacts, ITargetView, DoorShapes, ShipShapes, NaturalAim, BuildingAim, ShipAim, Targeting, AimShapes | The ray the interact key looks along (GROUND_SYSTEMS.md §5): from the eye, along the view, as far as the farthest verb |

### src/Hollow.Simulation/Teams (1 file, 323 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `TeamRegistry.cs` | 323 | TeamAction, Team, TeamInvite, TeamRegistry | What a player does with teams (GROUND.md §6.8, Rust's). An invite is a verb on the player invited (GROUND_SYSTEMS.md §5, |

### src/Hollow.Simulation/World (4 files, 602 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `CorpseLifecycle.cs` | 73 | CorpseTuning, CorpseStage, CorpseLifecycle | CorpseLifecycle: server-owned player-body phase (body, then lootbag), harvest progress and appearance, with CorpseTuning; loot stays outside this state. |
| `HarvestProgress.cs` | 27 | HarvestProgress | HarvestProgress: proportional corpse gathering; tool speed changes the number of hits, never the total yield. |
| `SpentArrow.cs` | 64 | SpentArrow | A spent arrow (GROUND_SYSTEMS.md §5, the owner's): it stays where it stuck, in the ground, a building, a deployable, a |
| `WorldObjects.cs` | 438 | StaticKind, WorldObject, Deployable, WorldProp, DroppedContainer, DroppedBag, Corpse, StaticLink, PlayerLink | What kind of static thing an entity is (characters are not static). |

## src/Hollow.Net

The protocol: messages and their generated serializers, bit packing, snapshot deltas, relevance, the relay.

### src/Hollow.Net/Bits (2 files, 376 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `BitReader.cs` | 161 | BitReader | Reads what a BitWriter wrote. Packets come from outside, so nothing here throws on bad data: reading |
| `BitWriter.cs` | 215 | BitWriter | Packs values into a byte buffer bit by bit, least significant bit first (GROUND.md §4.4). Reuse one per thread or |

### src/Hollow.Net/Commands (2 files, 408 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `ConsoleCatalog.cs` | 221 | CommandSide, ArgKind, ConsoleCommandDef, ConsoleCatalog | Where a console command runs: in the game (the view and its settings) or on the server. |
| `ConsoleCompletion.cs` | 187 | CompletionSources, Completion, ConsoleCompletion, Spawning | What Tab completes from: the game's data, and the players the server says are on. |

### src/Hollow.Net/Protocol (14 files, 9,106 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AnimalNet.cs` | 75 | AnimalLook, AnimalNet | An animal as a client sees it (unpacked from its snapshot state). |
| `Attributes.cs` | 76 | NetMessageAttribute, NetStructAttribute, NetStateAttribute, BitsAttribute, SignedAttribute, VarIntAttribute, AngleAttribute, QuantizeAttribute, MaxLengthAttribute, MaxCountAttribute | The vocabulary of message definitions (Messages.cs). tools/Hollow.NetGen reads the definitions as text and writes |
| `INetMessage.cs` | 24 | INetMessage | A message of the protocol (GROUND.md §4.4): its type byte, its reader and its writers, so a handler registry can |
| `MessageHandlers.cs` | 71 | MessageHandler, MessageHandlers [Entry, Entry] | What a handler does with a message that arrived, in the context it arrived in (a connection, a client, a server). |
| `Messages.cs` | 2088 | Hello, Goodbye, ZoneInfo, EnvironmentIdentity, WorldOffer, WorldAccept, Welcome, InputCommand, ShipControls, StationControls, InputPacket, OwnState, SnapshotHeader, OwnShipState, ShipNetState, CharacterNetState, Spawn, Despawn, ChatSend, ChatLine, ThingSpawn, ThingUpdate, UtilityState, PowerCableNet, CableAnchorNet, WireIntent, CorpseAppearance, WornItem, CharacterLook, NaturalNet, NaturalStates, ProjectileLaunch, ProjectileEnd, SurfaceContactNet, TargetNet, MoveItemIntent, QuickMoveIntent, DropIntent, DropAllIntent, CraftIntent, CancelCraftIntent, CloseIntent, RespawnIntent, IntentResult, ShipIntent, ShipDetails, ShipPartNet, ShipDoorNet, ShipSeatNet, ShipPartHealthNet, ShipStations, StationNet, StationBarrelNet, LoaderNet, MagazineCountNet, TurretShot, GunNet, SlotNet, ContainerContents, ContainerSlots, LootState, VitalsUpdate, CraftJobNet, CraftQueueState, HitMarker, AttackIntent, AttackResult, ItemsChanged, DamageTaken, CraftFinished, BagNet, LifeState, FireIntent, ReloadIntent, AttachIntent, AttachmentMoveIntent, GunUpdate, HammerIntent, BuildFrameNet, BuildFrames, PieceNet, BuildPieces, BuildIdNet, BuildRemoved, DoorNet, BuildDoors, ClaimNet, BuildClaims, ThrowIntent, ExplosiveUpdate, ExplosiveGone, Detonation, FireStarted, FireEnded, RaidAlertNet, RaidAlertMessage, RaidAlertList, TeamIntent, TeamChatSend, MarkerIntent, RecipeNet, BlueprintList, TeamMemberNet, TeamState, TeamInviteNet, TeamInvites, TeammateNet, TeamPositions, MarkerNet, RespawnPointNet, MapState, MachineState, ConsoleRun, ConsoleReply, ServerPlayerNet, ServerState, ZoneClock, InteractIntent, ArrowNet, ArrowStates, ArrowsGone, ThingDestroyed, PoseNet, PlaceIntent, ItemActionIntent, UnloadIntent, DevWatch, DevSectionNet, DevPlayerNet, DevStats, DevInspect, DevLineNet, DevInspected, DevCellNet, DevNav, DevSlotNet, DevAreaNet, DevAreas, DevLog | Every message of the ground game's protocol (GROUND.md §4.4, ARCHITECTURE.md §11). Definitions only: fields with |
| `Messages.g.cs` | 5828 | Protocol, Hello, Goodbye, ZoneInfo, EnvironmentIdentity, WorldOffer, WorldAccept, Welcome, InputCommand, ShipControls, StationControls, InputPacket, OwnState, SnapshotHeader, OwnShipState, ShipNetState, CharacterNetState, Spawn, Despawn, ChatSend, ChatLine, ThingSpawn, ThingUpdate, UtilityState, PowerCableNet, CableAnchorNet, WireIntent, CorpseAppearance, WornItem, CharacterLook, NaturalNet, NaturalStates, ProjectileLaunch, ProjectileEnd, SurfaceContactNet, TargetNet, MoveItemIntent, QuickMoveIntent, DropIntent, DropAllIntent, CraftIntent, CancelCraftIntent, CloseIntent, RespawnIntent, IntentResult, ShipIntent, ShipDetails, ShipPartNet, ShipDoorNet, ShipSeatNet, ShipPartHealthNet, ShipStations, StationNet, StationBarrelNet, LoaderNet, MagazineCountNet, TurretShot, GunNet, SlotNet, ContainerContents, ContainerSlots, LootState, VitalsUpdate, CraftJobNet, CraftQueueState, HitMarker, AttackIntent, AttackResult, ItemsChanged, DamageTaken, CraftFinished, BagNet, LifeState, FireIntent, ReloadIntent, AttachIntent, AttachmentMoveIntent, GunUpdate, HammerIntent, BuildFrameNet, BuildFrames, PieceNet, BuildPieces, BuildIdNet, BuildRemoved, DoorNet, BuildDoors, ClaimNet, BuildClaims, ThrowIntent, ExplosiveUpdate, ExplosiveGone, Detonation, FireStarted, FireEnded, RaidAlertNet, RaidAlertMessage, RaidAlertList, TeamIntent, TeamChatSend, MarkerIntent, RecipeNet, BlueprintList, TeamMemberNet, TeamState, TeamInviteNet, TeamInvites, TeammateNet, TeamPositions, MarkerNet, RespawnPointNet, MapState, MachineState, ConsoleRun, ConsoleReply, ServerPlayerNet, ServerState, ZoneClock, InteractIntent, ArrowNet, ArrowStates, ArrowsGone, ThingDestroyed, PoseNet, PlaceIntent, ItemActionIntent, UnloadIntent, DevWatch, DevSectionNet, DevPlayerNet, DevStats, DevInspect, DevLineNet, DevInspected, DevCellNet, DevNav, DevSlotNet, DevAreaNet, DevAreas, DevLog | Written by tools/Hollow.NetGen from Messages.cs. Don't edit: change Messages.cs, then run |
| `NaturalNet.cs` | 27 | NaturalNet |  |
| `NetConvert.cs` | 267 | NetConvert | Between simulation values and their wire form. The client predicts with ToMoveInput(ToCommand(...)), the |
| `NetStates.cs` | 14 | CharacterNetState, ShipNetState | The entity states snapshots carry, as the replication's generic sections see them (their serializers are generated). |
| `Protocol.cs` | 192 | Protocol, GoodbyeReason, EntityKind, ShipNetFlags, ShipAction, ConsoleReplyKind, ChatKind, TeammateState | Constants of the ground game's protocol (GROUND.md §4.4). |
| `ShipConvert.cs` | 241 | ShipConvert | Between ships' simulation values and their wire form (G6). A pilot's client predicts with |
| `SlotSnapshot.cs` | 33 | SlotSnapshot | SlotSnapshot: converts an item stack, with its gun state, to and from SlotNet: the exact inventory preconditions of an atomic attachment transfer. |
| `StationConvert.cs` | 129 | StationConvert | Between the batteries' values and their wire form (IW3): a gunner's controls, the stations' records. |
| `SurfaceContactNet.cs` | 41 | SurfaceContactNet | SurfaceContactNet: the wire form of a SurfaceContact (target, surface, normal, anchor and local point), decoded defensively against bad numbers |

### src/Hollow.Net/Replication (4 files, 695 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `DeltaChannel.cs` | 297 | INetState, IDeltaCandidate, ShipCandidate, DeltaChannel, DeltaKeeper [Sent] | An entity state the generator writes with deltas ([NetState]): what a snapshot section needs of it. |
| `RelevanceGrid.cs` | 154 | RelevanceGrid | Which entities each client hears about (ARCHITECTURE.md §11.3): a uniform grid of cubes in the zone's frame (the |
| `SnapshotReceiver.cs` | 99 | SnapshotReceiver | The client's half of snapshot replication (SnapshotSender): decodes snapshots against the ones it |
| `SnapshotSender.cs` | 145 | SnapshotCandidate, SnapshotSender | An entity offered to a snapshot: its state now and how much it matters to this client. |

### src/Hollow.Net/Transport (3 files, 296 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `ConditionedRelay.cs` | 195 | ConditionedRelay [Datagram] | A local UDP relay that makes a network worse on purpose (GROUND.md §4.4): clients connect to |
| `NetConditions.cs` | 41 | NetConditions | Bad-network settings for testing (GROUND.md §4.4): extra round-trip latency, jitter and packet loss, applied by a |
| `TimerResolution.cs` | 60 | TimerResolution | Waiting precisely. Windows sleeps in 15.6 ms steps unless a process asks for 1 ms timers; a 30 Hz tick and a |

## src/Hollow.Persistence

SQLite storage: the database, one worker thread, one codec per kind of record.

### src/Hollow.Persistence (10 files, 3,152 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AnimalCodec.cs` | 73 | CorpseSave, AnimalCodec | An animal's corpse as stored: its species (key), where it lies, what is left to harvest, its timer. |
| `BuildingCodec.cs` | 291 | BuildingRecord, StoredClaim, ClaimRecord, BuildingCodec | A stored piece, door (with its lock) or claim device (with its list and store): kind and versioned bytes. |
| `ExplosiveCodec.cs` | 147 | ExplosiveCodec | An explosive's stored form (GROUND.md §6.6, G5): a charge stuck or lying in the world with its fuse left survives a |
| `GameDatabase.cs` | 988 | CharacterRecord, ThingRecord, NaturalRecord, TeamRecord, PropRecord, ZoneRecord, ZoneSave, NewerSaveException, ShipRecord, GameDatabase | A stored character (GROUND.md §4.7): where it is on its body, in the body frame (metres from the body's centre, |
| `LockCodec.cs` | 51 | LockCodec | LockCodec: writes and reads the door-lock payload (kind, id, owner, locked, key or code data), shared by structural doors and compound gate hosts. |
| `PersistenceWorker.cs` | 138 | PersistenceWorker | Owns the GameDatabase on one background thread and runs every request in the order it was made |
| `ShipCodec.cs` | 433 | StoredShip, ShipCodec | A ship read back from storage, and who was in each of its seats (the server seats them again if they are there). |
| `TeamCodec.cs` | 50 | TeamCodec | A stored team (G7, GROUND.md §6.8): its leader and members, oldest first. Invites aren't stored. |
| `TerrainIdentity.cs` | 215 | TerrainIdentity, TerrainMismatchException, TerrainIdentityRequiredException, TerrainSave | TerrainIdentity: a body's heightfield, solids and population revision stamped on a save; mismatch exceptions and TerrainSave's check before migration or restore. |
| `WorldCodec.cs` | 766 | WorldCodec | The stored form of the survival game's state (ARCHITECTURE.md §13: a compact binary payload with a schema version): |

## src/Hollow.Server

The dedicated server: the host and its modules, sessions, admin commands, notifications.

### src/Hollow.Server (15 files, 3,349 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AdminCommands.cs` | 544 | CommandContext, GameServer | Who runs a console command and where its answer goes (GROUND.md §8.2): a player through the F1 console (their own |
| `CharacterPresentation.cs` | 26 | CharacterPresentation | CharacterPresentation.Apply: fills a player's replicated character state (held item, look, last action, sleeping, gun mods, aiming) from accepted simulation state |
| `CharacterRecords.cs` | 140 | CharacterRecords | How a character is stored and comes back (GROUND.md §4.7): its record from the zone, its survival state from the |
| `DevInspector.cs` | 192 | DevInspector | Describes a target for the F2 panel's LOOK tab (GROUND.md §8.5): what its family resolves it to and every public |
| `GameServer.cs` | 1086 | GameServer [NetEntity] | The dedicated server (GROUND.md §4.2): one zone on the first world, ticking at 30 Hz on one thread, with |
| `NetPresence.cs` | 23 | NetPresence | The server's part of a networked entity: its net ID, view (octahedral, with the head's free look) and owner. |
| `PlayerSession.cs` | 260 | PlayerSession | One connected player: their connection, character, inputs, budgets and what the host has sent them. What a module |
| `Program.cs` | 183 | Program | The dedicated server's console program: |
| `ServerModule.cs` | 279 | ServerModule | One concern of the server (GROUND_SYSTEMS.md §12.5, K10): its own state, its message handlers and event |
| `ServerModules.cs` | 32 | ServerModules | The server's modules and their order (GROUND_SYSTEMS.md §12.5, K10), the one place that lists them. The host calls |
| `ServerOptions.cs` | 134 | ServerOptions | How a server runs. The console program fills it from its command line; the game fills it for Host mode. |
| `ServerPersistence.cs` | 16 | ServerPersistence | Opens the world's persistence worker with its canonical terrain identity before the host starts its network. |
| `ServerSessionResources.cs` | 164 | ServerSessionResources | ServerSessionResources: constructs and retires everything one server session owns (world lease, zone, persistence), with rollback that keeps the first error. |
| `ServerWorldHandshake.cs` | 185 | ServerWorldHandshake [PendingWorldJoin, AcceptedWorldJoin] | ServerWorldHandshake: owner-thread admission of pending peers and environment negotiation; the host creates a player session only after a matching acceptance. |
| `TickStats.cs` | 85 | TickStats | How long ticks take (GROUND.md §5: ≤ 10 ms typical with 100 bots). Keeps the last few seconds for the console's |

### src/Hollow.Server/Modules (16 files, 5,506 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AnimalModule.cs` | 205 | AnimalModule, AnimalModuleAccess | Animals on the server (GROUND.md §6.9): the zone's population turned on, animals replicated as entities of their own |
| `ArrowModule.cs` | 128 | ArrowModule, ArrowModuleAccess [ArrowView] | Spent arrows on the server (GROUND_SYSTEMS.md §5): each player is told of the arrows within their range — what holds |
| `AttackModule.cs` | 68 | AttackModule [CancelBudget] | Server module: common attack transport, cancel budget and result route; the domain handlers keep their own simulation rules. |
| `BuildingModule.cs` | 641 | BuildingModule, BuildingModuleAccess [BuildView] | Building on the server (GROUND.md §4.4, §6.3, phase 2): players' building intents (on the same budget as every |
| `ConsoleModule.cs` | 928 | ConsoleModule, ConsoleModuleAccess [ConsoleBudget] | The F1 console on the server (GROUND.md §8.2): who is an admin, the console's messages (a line in, the answer's lines |
| `DamageModule.cs` | 48 | DamageModule, DamageModuleAccess | The damage pipeline's replication (GROUND.md §6.4.2): what was destroyed, to the clients that draw it. |
| `DevModule.cs` | 452 | DevModule, DevModuleAccess [Watch] | The F2 developer panel's server side (GROUND.md §8.5): who watches; every half second the stats (the tick's profile |
| `FireModule.cs` | 128 | FireModule, FireModuleAccess [FireView] | Fires on the server (IW4, IRON_WAKE.md §7): each player is told of the fires within their range (reliably) when one is lit |
| `GunModule.cs` | 108 | GunModule, GunBudget, GunModuleAccess | Guns on the server (GROUND.md §6.4, G4): shots, reloads and unloads from the player's own gun budget (a refused one |
| `PlacementModule.cs` | 52 | PlacementModule, PlacementModuleAccess | Placing anything (GROUND_SYSTEMS.md §6): one intent for every placeable, on the general budget, answered by |
| `RaidModule.cs` | 441 | RaidModule, RaidArena, RaidModuleAccess [ExplosiveView] | Raiding on the server (GROUND.md §6.6, G5): throw and place intents (a dud is lit again by a verb); explosives replicated to everyone in range |
| `ShipModule.cs` | 636 | ShipModule, ShipModuleAccess [ShipView, StationAttacks] | Ships on the server (GROUND.md §6.7): the pilots' controls into their ships, ship intents, ships in every nearby |
| `SurvivalModule.cs` | 724 | SurvivalModule, GameCounters, SurvivalModuleAccess [OwnState, Looks, PendingRespawn] | The survival game on the server (GROUND.md §4.2, §6): players' intents (inventory, crafting, interaction, closing, |
| `ThingModule.cs` | 483 | ThingModule, ThingSent, ThingModuleAccess [ThingView] | Static things and natural objects on the server (GROUND.md §4.2, §6.1): every static thing gets a net ID and is |
| `WiringModule.cs` | 20 | WiringModule | WiringModule: routes WireIntent messages to the zone's wiring system for the sending player's entity |
| `WorldModule.cs` | 444 | WorldModule, WorldModuleAccess [WorldView] | The world on the server (GROUND.md §6.8, G7): the world's props placed at start as stored (their public state goes |

### src/Hollow.Server/Notifications (1 file, 369 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Notifications.cs` | 369 | RaidAlert, INotificationService, IPlayerPresence, INotificationStore, IPushChannel, NoPush, NotificationCounters, NotificationService, MemoryNotificationStore, DatabaseNotificationStore, RaidAlertCodec [BaseState, Window] | A raid alert as a player gets it (GROUND.md §6.6): which base (its claim device), where it stands (zone frame), when |

### src/Hollow.Server/Diagnostics (1 file, 356 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `CrashLog.cs` | 356 | CrashLog, CrashLogWriter | Crash reports (GROUND.md §4.3), for the game and the dedicated server alike, without the engine: the last 200 log lines, a report per unhandled exception, a still-running marker per process |

## src/Hollow.Client

The client without graphics: the host and its modules, prediction, the local player, the map, sounds' rules.

### src/Hollow.Client (34 files, 6,241 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AdminSelfCamera.cs` | 49 | AdminSelfCamera | An admin's orbit camera round their own character: session-only state that captures primary and secondary clicks so they never leak into held actions. |
| `AttackLedger.cs` | 88 | AttackRecord, AttackLedger | AttackLedger: the client's record of each attack by identity, predicted, resolved and presented once, with bounded history and pending limits |
| `BuildingReplica.cs` | 212 | DoorState, ClaimState, BuildingReplica | What a client knows of a door beyond its geometry: its lock, and what the server tells this player of it |
| `ClientModule.cs` | 157 | ClientModule | One concern of the client (GROUND_SYSTEMS.md §12.5, K11): its own state, its message handlers, and hooks the host |
| `ClientModules.cs` | 32 | ClientModules | The client's modules and their order (GROUND_SYSTEMS.md §12.5, K11), the one place that lists them: the host calls |
| `ClientOptions.cs` | 53 | ClientOptions | ClientOptions: how a client runs: name, body, content, bad-network conditions and relay, background transport, and the shared ground, nature and building that bots use. |
| `ClientShipFrame.cs` | 93 | ClientShipFrame | A ship's frame on a client (IRON_WAKE.md §4.3, §4.8), keyed by the ship's net ID as the wire names frames: its space, |
| `ClientStats.cs` | 175 | ClientStats | What the network graph shows (GROUND.md §4.4): round trip, snapshot loss and rate, bandwidth both ways (with UDP |
| `ClientTransport.cs` | 88 | ClientTransport | LiteNetLib side of GameClient: network setup, connecting (through a relay if set), polling updates, and typed and chat sends; holds no state. |
| `ClientWorldHandshake.cs` | 121 | ClientWorldHandshake | The world-offer handshake's state on the owner thread: accepts or rejects a WorldOffer and Welcome by protocol, and times the handshake out; holds no world. |
| `ClientWorldSession.cs` | 177 | ClientWorldSession | Owner-thread lifecycle of the client's generated world and prediction: stages a replacement before retiring the old one, holds the world lease, rolls back a failed open. |
| `ConsoleBindings.cs` | 100 | ConsoleBindings | Saved key-to-command console shortcuts: defaults merged with saved bindings, explicit removals kept, key normalisation; commands still use the normal rights path. |
| `GameClient.cs` | 893 | ClientState, GameClient | How a client runs. |
| `HeldActionQueue.cs` | 46 | HeldActionQueue | A cancellable local wind-up: the callback sends an ordinary validated server intent exactly once, at its commit tick. |
| `InputFrame.cs` | 63 | InputFrame | One frame of the player's controls, as the game (or a bot) reads them. |
| `LocalGun.cs` | 675 | ShotEvent, ReloadEvent, UnloadEvent, GunRefusal, LocalGun, RecoilCompensation [Pending] | A shot this client fired, at once (predicted): for the muzzle flash, the sound, the tracer and the kick. |
| `LocalMelee.cs` | 122 | LocalMelee | Predicted melee swings on input ticks, presentation only: never applies damage, gathering or hit effects; an accepted attempt keeps its identity. |
| `LocalPlayer.Attachments.cs` | 23 | LocalPlayer | LocalPlayer.MoveAttachment: the intent that moves an attachment between slots and weapon sockets, predicted through AttachmentTransfer |
| `LocalPlayer.Building.cs` | 81 | LocalPlayer | The building intents (GROUND.md §6.3, phase 2): the hammer's. Each returns its sequence number (0 if it couldn't be |
| `LocalPlayer.Interaction.cs` | 33 | LocalPlayer | The one interaction intent (GROUND_SYSTEMS.md §5): a verb done to a target, with a number (a tech tree's recipe) and a |
| `LocalPlayer.Placement.cs` | 100 | PendingPlacement, LocalPlayer | A placement sent and not yet answered (or answered a moment ago): drawn where it will stand in the ghost's pending look |
| `LocalPlayer.Raid.cs` | 65 | LocalPlayer | The player's raiding (GROUND.md §6.6, G5): throwing and placing explosives, and the raid alerts of the bases they are |
| `LocalPlayer.Ships.cs` | 31 | LocalPlayer | A seated player's keys (GROUND.md §6.7, IRON_WAKE.md §5.2): a door from a seat that works them, leaving the seat, the |
| `LocalPlayer.Wiring.cs` | 14 | LocalPlayer | LocalPlayer.Wire: sends a WireIntent (operation, target, port, point, revision) for the wiring tool |
| `LocalPlayer.World.cs` | 306 | TeamMemberView, TeamView, TeamInviteView, TeammateView, MarkerView, RespawnPointView, MachineView, LocalPlayer | A member of the player's team as the interface shows them. |
| `LocalPlayer.cs` | 862 | VitalsView, CraftJobView, RespawnOption, LifeView, HitMarkerEvent, ItemsChangedEvent, DamageEvent, IntentAnswer, LocalPlayer [Prediction, Pending] | The player's vitals as the UI shows them. |
| `OrbitCamera.cs` | 46 | OrbitCamera | Presentation-only orbit camera about a player, independent of movement and aim, on any planetary up axis. |
| `PilotSteering.cs` | 34 | PilotSteering | A persistent mouse steering cursor for flight, separate from camera look: no spring-back and no hidden dead zone. |
| `Predictor.cs` | 310 | Predictor | The player's own character, predicted (GROUND.md §4.3): every tick runs the server's movement code on the client's |
| `RemoteEntity.cs` | 240 | RemoteEntity | Another entity as this client sees it (GROUND.md §4.3): the states snapshots brought, each at its server tick, and |
| `RemoteExplosives.cs` | 93 | ClientExplosive, DetonationEvent, RaidAlertView | An explosive in the world as this client knows it (GROUND.md §6.6): a timed charge, bag charge or tin grenade the |
| `RemoteShip.cs` | 405 | RemoteShip | A ship as this client sees it (GROUND.md §6.7), as RemoteEntity sees a character: the states snapshots |
| `RemoteThings.cs` | 313 | RemoteThing, ClientProjectile, Arc | A static thing as this client knows it (GROUND.md §6): a deployable, a bag of dropped items or a corpse, sent reliably |
| `ShipPredictor.cs` | 141 | ShipPredictor | The ship the player flies, predicted (GROUND.md §6.7), as Predictor predicts the character: every tick |

### src/Hollow.Client/Audio (11 files, 1,501 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AmbienceMix.cs` | 168 | AmbienceInput, AmbienceLevels, AmbienceMix | Where the listener is, for the ambience (GROUND.md §4.9). Distances in metres, angles in degrees. |
| `AudioVoiceBudget.cs` | 63 | AudioVoiceBudget | AudioVoiceBudget: the voice pools, update rate and audibility limits read from sounds.json's optional voiceBudget, with validation. |
| `FootstepEnvironment.cs` | 77 | FootstepEnvironment | Picks the FootSurface under a character's feet from a ship's deck, deployables, constructed floors and terrain; WaterDepth counts lakes and streams as well as sea. |
| `FootwearAudioData.cs` | 139 | FootwearAudioData | Footstep profiles from footsteps.json: items name a profile on their wear capability, and worn equipment, local or remote, resolves to one; bare feet use the default. |
| `Loudness.cs` | 238 | LoudnessResult, Loudness [KWeighting] | A sound's loudness (ITU-R BS.1770), as the sound pipeline measures it with ffmpeg's ebur128. |
| `MeleeSwingCue.cs` | 75 | MeleeSwingCue | MeleeSwingCue: one presentation-only swish armed from an attack's start and contact ticks and fired late in its anticipation; never an impact or a result. |
| `RemoteMeleeSwingCue.cs` | 40 | RemoteMeleeSwingCue | The swish for another player's melee swing, timed on the server clock rather than the buffered render clock so it cannot trail the impact. |
| `SoundCatalogData.cs` | 313 | SoundCategory, SoundLayer, SoundSpace, SoundEventData, SoundCatalogData | How loud the files of one category are made (GROUND.md §4.9): a metric, a target and a range in LUFS. |
| `SoundLoopEnvelope.cs` | 54 | SoundLoopEnvelope | Gain, pitch and playhead of a logical loop (fades and stop), advanced whether or not the loop holds an audible voice. |
| `SoundRules.cs` | 250 | DistanceLayers, FootSurface, FootGait, FootInput, FootEvent, Footsteps, FootstepCadence | Distance layering of loud sounds (GROUND.md §4.9): which layers play at a distance, and when they arrive. |
| `SoundVoiceScheduler.cs` | 84 | SoundVoiceCandidate, SoundVoiceScheduler [Ranked] | Chooses which one-shots and logical loops get the bounded voice pool: by loudness and priority, with event limits and a retention margin for voiced loops. |

### src/Hollow.Client/Diagnostics (1 file, 341 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `EarthEnvironmentRoute.cs` | 341 | EnvironmentRouteStop, EnvironmentRouteLeg, EnvironmentRoutePlan, EarthEnvironmentRoute [Cell, Candidate, Grid, Obstacles] | EarthEnvironmentRoute: picks a walkable route on Earth (beach, woodland, clearing, cliff, monument, water) for the hosted environment capture, clear of solid objects |

### src/Hollow.Client/Effects (1 file, 121 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `EffectBudgets.cs` | 121 | EffectBudgetSettings, EffectParticleBudget, EffectLoopCandidate, EffectLoopBudget [Order] | Effect budgets, presentation only: settings record, a per-frame particle counter and a loop budget that picks render slots by distance and priority. |

### src/Hollow.Client/Feedback (5 files, 255 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `ActionCueData.cs` | 65 | ActionSoundCue, ActionCueData, ActionCuePlayer | action-cues.json as ActionCueData (normalised sound cue times per action) and ActionCuePlayer, which fires each cue once as an action runs |
| `ConfirmedFeedback.cs` | 28 | ConfirmedCue, ConfirmedFeedback | ConfirmedFeedback: sound cues held by intent sequence and released only when the server accepts it in time; rejected, late and duplicate answers emit none |
| `ContactResponseData.cs` | 75 | ContactResponse, SurfaceResponse, ContactResponseData | contact-responses.json as one table: per physical surface, its footstep bank, dust and the sound and effect of each action, with optional modifiers. |
| `PresentationPoseRegistry.cs` | 56 | PresentationPoseRegistry [Registration] | Renderer-owned target poses for presentation: no registration falls back to the simulation view, a provider returning null says the rendered target is gone. |
| `RecentContactFeedback.cs` | 31 | RecentContactFeedback | Bounded per-world victim history; one body's projectile feedback cannot suppress a neighbour's flinch. |

### src/Hollow.Client/Loading (5 files, 666 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `LoadStage.cs` | 131 | LoadScope, LoadStage | When a stage takes part in a load. |
| `LoadingPipeline.cs` | 271 | LoadPhase, LoadingPipeline | Where a game is in its loading. |
| `LoadingPlan.cs` | 146 | StagePlan, LoadingPlan | One stage of the plan: its words on the loading screen, its weight (the seconds it takes on the reference PC, which |
| `QuietStream.cs` | 38 | QuietStream | A stream that has gone quiet (GROUND.md §4.3.1): the server sends what is around a player that arrives as fast as its |
| `SteadyFrames.cs` | 80 | SteadyFrames | The last stage of the game's start (GROUND.md §4.3.1): a run of frames, each under the frame budget, long enough to |

### src/Hollow.Client/Map (3 files, 682 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `GlobeCamera.cs` | 189 | GlobeCamera | Where a map of the whole world looks (docs/WHOLE_WORLD.md §11): its centre, which way is up on the screen there, and |
| `GlobeProjection.cs` | 156 | GlobeProjection | How the map draws a whole world (docs/WHOLE_WORLD.md §11): a globe seen from afar that becomes a flat chart as it |
| `ZoneMap.cs` | 337 | MapPicture, ZoneMap, ZoneMapImage | The map's picture of a whole world: six square faces of Size × Size pixels, RGBA, one per cube face, stacked in |

### src/Hollow.Client/Modules (17 files, 4,540 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AnimalModule.cs` | 232 | AnimalEventKind, AnimalEvent, ClientAnimal, AnimalModule, AnimalModuleAccess | What happened to an animal in view, for the visual layer and sound (GROUND.md §6.9). |
| `ArrowModule.cs` | 106 | RemoteArrow, ArrowModule, ArrowModuleAccess | A spent arrow as this client knows it (GROUND_SYSTEMS.md §5): what it is, what holds it and its tip's place there, and |
| `AttackModule.cs` | 29 | AttackModule | Client module: one response route for all predicted attacks; the handlers consume the shared attack ledger. |
| `BuildingModule.cs` | 71 | BuildingModule, BuildingModuleAccess | Building on the client (GROUND.md §4.3, §6.3, phase 2): the replicated structures to draw, the ghost check with the |
| `ConsoleModule.cs` | 102 | ConsoleAnswer, ZoneDay, ConsoleModule, ConsoleModuleAccess | A line of the server's answer to a console command (its number), or a line it sent by itself (0). |
| `DamageModule.cs` | 71 | DestroyedEvent, DamageModule, DamageModuleAccess | Something in range was destroyed (GROUND.md §6.4.2), for break-apart pieces, debris and the sound of it breaking. |
| `DevModule.cs` | 326 | DevPlayerState, ServerSection, ServerPlayer, DevLine, NavWindow, SpawnSlotView, SpawnAreaView, AnsweredIntent, DevModule, DevModuleAccess | A player's state as the F2 panel lists it. |
| `FireModule.cs` | 158 | ClientFire, FireModule, FireModuleAccess | A fire as this client knows it (IW4, IRON_WAKE.md §7): what left it (the round or explosive: its looks and sounds) and |
| `InteractionModule.cs` | 179 | InteractAim, InteractionModule, InteractionModuleAccess | What the interact key acts on now (GROUND_SYSTEMS.md §5): the target the one query found, what is known of it, and its |
| `LoadingModule.cs` | 89 | LoadingModule, LoadingModuleAccess [Backlog] | The client's part of the loading pipeline (GROUND.md §4.3.1): the connection (connected, welcomed, the character |
| `PlacementModule.cs` | 205 | PlacementModule, PlacementModuleAccess [ClientSurroundings] | Placing on the client (GROUND_SYSTEMS.md §6): the placement world over what this client has (its prediction physics |
| `RaidModule.cs` | 118 | RaidModule, RaidModuleAccess | Raiding in the client (GROUND.md §6.6, G5): the explosives in range as the server has them (drawn on their arcs, with |
| `ShipModule.cs` | 588 | ShipModule, ShipModuleAccess | Ships in the client (GROUND.md §6.7): every ship near, interpolated, with its details (doors, seats, health; IRON_WAKE.md |
| `StationModule.cs` | 629 | StationShotView, StationModule, StationModuleAccess [Pending] | The batteries in the client (IW3): records, others' shots, the gunner's predicted mount and shots matched by number. |
| `SurvivalModule.cs` | 522 | SurvivalModule, SurvivalModuleAccess [Terminal] | The survival game in the client (GROUND.md §4.3, §6): the local player's state and intents (LocalPlayer), static |
| `TargetModule.cs` | 999 | TargetModule, TargetModuleAccess [GroundView, CharacterView, AnimalView, NaturalView, ThingView, PieceView, DoorView, CupboardView, ExplosiveView, ShipView, ArrowView, FireView] | The world's things as this client knows them, named the server's way (GROUND_SYSTEMS.md §3.1, §5): one view per target |
| `WorldModule.cs` | 116 | WorldModule, WorldModuleAccess | The world in the client (GROUND.md §6.8, G7): the world's props in range (barrels, crates, recyclers: things of kind |

### src/Hollow.Client/Rendering (1 file, 48 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `LocalShadowSelector.cs` | 48 | LocalShadowCandidate, LocalShadowSelector | Picks at most eight local shadow lights per frame by priority, then proximity, with a ten-percent advantage for incumbents; no allocations. |

### src/Hollow.Client/Streaming (2 files, 42 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `NaturalRenderState.cs` | 28 | NaturalRenderChange, NaturalRenderState [Appearance] | Tracks natural objects that differ from generated, present, stage-zero, and reports whether plants or models need redrawing; a fall hides the plant until it ends. |
| `ScatterRequestSet.cs` | 14 | ScatterRequestSet | Deduplicates scatter jobs by both their complete cube-sphere address and requested contents. |

## game/scripts

The Godot project (presentation only): the app, the galaxy and system maps, the ground mode and its views, the interface.

### game/scripts/Animals (6 files, 1,721 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AnimalAssets.cs` | 101 | AnimalClip, AnimalModel, AnimalAssets | One of a model's clips, as art/animals/build_animals.py made it. |
| `AnimalSandbox.Scenes.cs` | 445 | AnimalSandbox [TreeGrid] | The sandbox's scenes: each at a clearing near the landing site with trees behind it, found once from the planet's |
| `AnimalSandbox.cs` | 468 | AnimalSandbox, AnimalWorldAngles [Person, Scene] | The animal sandbox (GROUND.md §6.9): --view animals. A zone of the game's rules runs in this process on the |
| `AnimalView.cs` | 488 | AnimalFrame, AnimalView | What an AnimalView draws, given every frame (GROUND.md §6.9): where the animal stands and faces, what it |
| `AnimalWarm.cs` | 15 | AnimalWarm | The animals' part of the renderer's warm-up (GROUND.md §4.3.1, §6.9): every species' model, skinned, as drawn. |
| `AnimalsView.cs` | 204 | AnimalsView | The animals in view (GROUND.md §6.9): one AnimalView per animal, made when it appears, given its frame |

### game/scripts/App (6 files, 946 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `CaptureIsolation.cs` | 96 | CaptureIsolation | Keeps a standalone --shot a render job: GUI input, window mode, size and position are held for the capture, so native input and window gestures cannot change it. |
| `Checkout.cs` | 59 | Checkout | The repository this game runs from (the folder above game/): its branch and commit, read from git's own files. For bug |
| `CrashReporter.cs` | 125 | CrashReporter, CrashLogger | The game's crash reports (GROUND.md §4.3), in user://crashes: .NET's and Godot's exceptions, the marker, the menu's notice |
| `GalaxyService.cs` | 45 | GalaxyService | Owns the one Galaxy of the client: loads the real-star catalogue and builds the model. |
| `Main.cs` | 503 | Main | Entry point. Loads the galaxy and switches between the galaxy map and the system map. |
| `UiScaler.cs` | 118 | UiScaler | Keeps Ui.Scale matched to the window (see Automatic), times the player's own |

### game/scripts/Audio (8 files, 2,424 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AmbienceDirector.cs` | 127 | AmbienceDirector [Bed] | The world's ambience around the listener (GROUND.md §4.9). The surface view gives it, every frame or so, where |
| `AudioBuses.cs` | 189 | AudioBuses | The audio bus layout (GROUND.md §4.9), made in code so no .tres has to be kept in step: |
| `AudioDirector.cs` | 853 | SoundHandle, AudioDirector [Voice, LogicalLoop] | A playing sound (one event; a layered event's layers share it). Default: nothing. |
| `AudioLoopCheck.cs` | 149 | AudioLoopCheck | --audio-loop-check: drives the real pooled AudioDirector with 700 retained loop emitters and checks voice caps, virtual gain, event limits and eviction recovery; quits 0 or 1. |
| `FootstepDriver.cs` | 61 | FootstepDriver | One character's footsteps (GROUND.md §4.9). The ground mode gives it the character's movement every frame with |
| `SoundLibrary.cs` | 177 | SoundLibrary, VariantBag | The sound catalogue in the game (GROUND.md §4.9): res://data/ground/sounds.json read once, its streams |
| `SoundMeter.cs` | 80 | SoundMeter | Loudness of the game's own sound files, measured in the engine: each stream is decoded through Godot |
| `SoundViewer.cs` | 788 | SoundViewer | The audition viewer (--view sounds, GROUND.md §4.9): every event of the catalogue in a list (a click plays |

### game/scripts/Build (12 files, 2,834 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `BuildHud.cs` | 104 | BuildHud | The build sandbox's lines over the ground HUD, in RustMVP's manner (GROUND.md §6.3): under the crosshair the target |
| `BuildSandbox.Check.cs` | 143 | BuildSandbox | --view build --scene empty --build-check: plays one round of building through the sandbox's own input paths (the aim, |
| `BuildSandbox.ClanBase.cs` | 22 | BuildSandbox | BuildSandbox's clan scene (--view build --scene clan): places the server's 3x3 clan base preset (data/ground/admin-bases/clan-3x3.json) at --grade 2 by default and aims the camera |
| `BuildSandbox.Joints.cs` | 227 | BuildSandbox | --view build --scene joints: the building's joints in every grade (GROUND.md §6.3, "Fit"), one group a grade on a |
| `BuildSandbox.cs` | 767 | BuildSandbox [Surroundings] | The build sandbox (--view build, GROUND.md §6.3): a local zone on the first world's stand-in surface with one |
| `BuildSandboxUi.cs` | 208 | BuildSandboxUi | The ground interface's view model for the build sandbox (GROUND.md §6.3, §8.1): a belt of building tools, the plan's |
| `BuildingAssets.cs` | 271 | BuildingAssets | The building kit's art in the game (GROUND.md §6.3, §9): the meshes of every piece in every grade at three levels of |
| `BuildingRadials.cs` | 147 | IHammerActions, PlanRadial, BuildingRadials | What the hammer's radial does to the piece aimed at: the game sends intents, the build sandbox applies the rules. |
| `BuildingRenderer.cs` | 500 | BuildingRenderer, DoorView, RenderedTargetFrame [BatchKey, Batch] | Draws a BuildingWorld (GROUND.md §6.3): pieces batched into one MultiMeshInstance3D per |
| `BuildingWarm.cs` | 67 | BuildingWarm | The building's part of the renderer's warm-up (GROUND.md §4.3.1): every grade's material on its pieces' levels of |
| `CornerPosts.cs` | 161 | CornerPosts [End] | The posts that close the notch walls leave at an outer corner (GROUND.md §6.3, Fit; decision J5). Walls stand centred |
| `DemoBases.cs` | 217 | DemoBases [House] | Bases the sandbox builds for a look or a screenshot (GROUND.md §6.3), placed through the rules' sockets so every pose |

### game/scripts/Characters (35 files, 10,937 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `BowRig.cs` | 270 | BowRig | The bow while it is drawn (GROUND.md §4.8): its model's limbs bend back as the string is drawn (a "draw" blend shape |
| `CharacterAnimator.cs` | 293 | CharacterAnimator | The animation graph every third-person character shares (GROUND.md §4.8). One AnimationNodeBlendTree |
| `CharacterAssets.cs` | 331 | MeshPart, ClipInfo, BodyAssets, CharacterAssets | A skinned mesh of the character files and the levels of detail it is drawn at. |
| `CharacterLab.cs` | 940 | CharacterLab | The character viewer, --view character (GROUND.md §4.8): one character on a patch of ground, with keys to |
| `CharacterMaterials.cs` | 234 | CharacterMaterials | Materials of the character surfaces, by the surface (material) names the art scripts export. Skin and garments |
| `CharacterPoser.cs` | 326 | CharacterPoser | Procedural layer on top of the animation clips, run as the skeleton's modifier (GROUND.md §4.8): |
| `CharacterRagdoll.cs` | 223 | CharacterRagdoll [Joint] | The limp body after death (GROUND.md §4.8): fourteen capsules on the main bones under a |
| `CharacterTypes.cs` | 206 | BodyType, CharacterStance, HeldKind, CharacterEvent, CharacterFrame, BodyRegions | Which body a character has (GROUND.md §4.8): each has its own meshes, clothing and clips. |
| `CharacterView.HeldPresentation.cs` | 377 | CharacterView | CharacterView's held-item presentation: plays authored actions (PlayAction, PlayItemAction, previews) on the third-person body, samples keys, skeletal source motion, hand offsets and finger poses |
| `CharacterView.cs` | 1290 | CharacterView [UseMotion] | A player as others see them (GROUND.md §4.8). Give it a CharacterFrame every frame with |
| `CharacterWarm.cs` | 67 | CharacterWarm | The characters' part of the renderer's warm-up (GROUND.md §4.3.1): each body as others see it and as its own arms in |
| `HandPoses.cs` | 259 | HandPoses [Grip] | Finger poses for holding things (first person; third person takes its fingers from the clips). The fingers of a |
| `HeldItemEditor.AdmireChecks.cs` | 125 | HeldItemEditor | HeldItemEditor's --admire-check: steps every held item through the admire action on the first-person renderer and checks the rig stays rooted, grips hold, playback ends and cancels |
| `HeldItemEditor.Automation.cs` | 521 | HeldItemEditor | HeldItemEditor's automation: --vm-orbit and --attachment inspection options, --editor-check (controls, undo, atomic save and reload), contact sheets, animation frame capture, screenshots |
| `HeldItemEditor.BowChecks.cs` | 211 | HeldItemEditor [BowCheckSample, BowHandCheck] | HeldItemEditor's --bow-check: measures the real arrow's axis, screen convergence at 10 m, nock depth and wrist attachment for each bow state and body, and writes bow-check.json |
| `HeldItemEditor.Gizmo.cs` | 118 | HeldItemEditor | HeldItemEditor's grip move gizmo: three coloured axis handles drawn over the first-person preview, dragged with the mouse (Shift fine, Ctrl snaps) as undoable placement edits |
| `HeldItemEditor.Inspector.cs` | 506 | HeldItemEditor, HeldItemEditorTimeline | HeldItemEditor's side panel and edit core: placement, hand, key and action fields, undo and redo, dirty tracking, Save, Reload, close confirmation, and the timeline's key markers |
| `HeldItemEditor.LocomotionChecks.cs` | 368 | HeldItemEditor [LocomotionCheckSample] | HeldItemEditor's --locomotion-check: native first-person samples of walk, sprint, crouch, look sway and action fades for source-bound weapon families, with sheets and a JSON report |
| `HeldItemEditor.RecoilChecks.cs` | 320 | HeldItemEditor [RecoilCheckSample] | HeldItemEditor's --recoil-check: fires at 450 RPM through the real viewmodel with overlapping recovery and checks grip, palm, step size and sight alignment, rendered and reported |
| `HeldItemEditor.SkeletalChecks.cs` | 161 | HeldItemEditor | HeldItemEditor's skeletal checks: save and reload keeps source clips and rig placement, source arm pose matches the clip, curves stay isolated, strike timing maps to gameplay |
| `HeldItemEditor.cs` | 767 | HeldItemEditor | --view held-editor: visual editor of held-item presentation profiles in its own preview viewport; live undoable edits on the real renderers, Save writes the validated catalogue. |
| `HeldItems.cs` | 323 | HeldItemModel, HeldItems, HandFrame | An item that can be held: its item key, its name, how it is held and in which hand. |
| `HeldLocomotion.cs` | 124 | HeldLocomotionProfile, HeldLocomotion [Document] | Held-item locomotion profiles (held-locomotion.json): camera-space walk, sprint and look offsets per item family, their validation, and the loader. |
| `HeldPresentationReloadParts.cs` | 66 | HeldReloadParts | HeldReloadParts: finds a held model's magazine part and moves it between its seat and the support hand during a reload, shared by both character views. |
| `HeldPresentationViewmodel.cs` | 506 | ViewmodelView | ViewmodelView's presentation part: samples the held-presentation profile for first-person actions and previews, the contact target, hand attachment errors and finger poses. |
| `HeldPresentations.cs` | 338 | HeldPresentationPose, HeldPresentationHand, HeldPresentationKey, HeldSkeletalClip, HeldPresentationClip, HeldPresentationProfile, HeldPresentations [Document, VectorConverter] | Held-presentation data (held-presentations.json): poses, hands, keys, clips and profiles in metres and Euler degrees, with the catalogue's reading, validation and Revision. |
| `HeldSkeletalMotion.cs` | 112 | HeldSkeletalMotion [BoundAnimation] | Samples a chosen segment of the retargeted character animation library onto a view's skeleton for held actions; the item then follows the hand. |
| `Looks.cs` | 297 | Look, Looks, LookUniforms | How one player looks (GROUND.md §4.8, looks): skin tone, face, hair style and colour, stubble. The body (male or |
| `ViewmodelMotion.cs` | 126 | ViewmodelMotion [Ease, Key] | The first-person swing and reload as key poses (GROUND.md §4.8): each has an anticipation, an impact and a |
| `ViewmodelView.BowAim.cs` | 39 | ViewmodelView | ViewmodelView's bow aim: turns the nocked arrow about its shelf towards a point 10 m ahead of the camera so the drawn shaft looks at the crosshair, then re-checks the arms' reach |
| `ViewmodelView.Locomotion.cs` | 97 | ViewmodelView | ViewmodelView's skeletal locomotion: walk bob, sprint pose, crouch, look sway and action-weight fades, driven by travelled distance and the item family's HeldLocomotionProfile |
| `ViewmodelView.Recoil.cs` | 50 | ViewmodelView [FirePulse] | ViewmodelView's fire pulses: each shot adds an overlapping authored impulse, so automatic fire keeps unfinished recovery; their samples are summed, finger curls taken from the newest |
| `ViewmodelView.SkeletalMotion.cs` | 70 | ViewmodelView | ViewmodelView's source-animation pose: plays the imported arm clip on the rig, fits the rig placement and locomotion, attaches the item to its wrist and curls the holding fingers |
| `ViewmodelView.cs` | 671 | ViewmodelFrame, ViewmodelView | What the first-person view needs each frame (GROUND.md §4.8). |
| `Wardrobe.cs` | 205 | WearSlot, Wearable, Wardrobe | The seven wear slots (GROUND.md §6: 24 main, 6 belt, 7 wear). |

### game/scripts/Common (7 files, 1,198 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Labeller.cs` | 109 | LabelSide, Labeller | Where a label goes relative to the object it names. |
| `MapOverlay.cs` | 97 | MapLabel, MapOverlay [Text, Ring] | How a label on a map looks. |
| `StarColors.cs` | 59 | StarColors | Presentation colours for stars (blackbody-like, tuned to read well on a dark map). |
| `Ui.cs` | 666 | TextStyle, Ui [Ground] | Text styles of the interface. Each one is a Label theme type variation of the same name. |
| `UiIcons.cs` | 85 | UiIcon, UiIcons |  |
| `WarmItem.cs` | 37 | WarmItem, WarmSpot | One thing the game can draw, for the renderer's warm-up (GROUND.md §4.3.1): the files it is made of, loaded on Godot's |
| `WarmupAssetAdmission.cs` | 145 | WarmupAssetAdmission | Bounds a warm-up catalogue's startup lookahead: admits one prefix window of items, counts their file dependencies until consumed, and hands out files to load. |

### game/scripts/Fx (23 files, 5,949 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `FxAssets.cs` | 63 | FxAssets | The effects library's textures and meshes in res://assets/fx, loaded once (GROUND.md §9.3). |
| `FxBatch.cs` | 262 | FxBatch, FxOrient, FxBatches | One kind of particle for the whole world (GROUND.md §9.3): a GPUParticles3D in world space that never emits by |
| `FxBudgetCatalog.cs` | 14 | FxBudgetCatalog | Reads the renderer's effect resource ceilings from data/ground/fx-budgets.json into EffectBudgetSettings; defaults when the file is missing. |
| `FxBudgetCheck.cs` | 238 | FxBudgetCheck [Check] | --fx-budget-check: saturates the real effect director in a small viewport and checks its limits and lifecycle; no server or saves, optional --shot. |
| `FxCatalog.cs` | 441 | FxDirection, FxLayerType, FxLayer, FxLoopDef, FxTracerDef, FxDef, FxCatalog [Flame] | Where a layer's particles fly (GROUND.md §9.3). |
| `FxDecals.cs` | 186 | FxDecals [Entry, Pool] | Decals (GROUND.md §9.3): bullet holes, blood, scuffs and scorch marks projected onto what was hit. Each texture has |
| `FxDirector.Budgets.cs` | 165 | FxDirector | FxDirector's budgets: per-frame particle count, and admission of looping effects (fires, torches) to pooled flame meshes and lights by distance, cap and priority |
| `FxDirector.cs` | 920 | FxParams, FxSurface, FxDirector, FxSurfaces [Spot] | Optional details of an effect (GROUND.md §9.3). Every field may be left at its default. |
| `FxFlashes.cs` | 213 | FxFlashes [Entry] | Muzzle flashes (GROUND.md §9.3): a pool of small card meshes that sit on a gun's muzzle socket (+X along the barrel) |
| `FxLights.cs` | 136 | FxLights [Entry] | Short lights for flashes and blasts (GROUND.md §9.3): a pool of shadowless omni lights that rise over an attack time |
| `FxLoops.cs` | 115 | FxHandle, FxLoop, FxFlameMeshes | A running loop (a fire, a trail), as FxDirector.Start returns it. |
| `FxRandom.cs` | 76 | FxRandom | A small, fast random source for effects (xorshift); seeded, so screenshots repeat. |
| `FxShake.cs` | 61 | FxShakeEvent, FxShake | A camera shake that started somewhere (GROUND.md §9.3): what the camera hook and other listeners get. |
| `FxStage.Ship.cs` | 421 | FxRocketLaunch, FxStage | How a battery launches its rocket (IRON_WAKE.md §6.4): the speed the gun gives it, how far out its motor lights, the |
| `FxStage.cs` | 362 | FxStation, FxStage | A place in the effects viewer where effects play (GROUND.md §9.3): its props, where and which way an effect plays, |
| `FxTracers.cs` | 205 | FxTracers [Tracer] | Tracers and trails (GROUND.md §9.3): bright streaks that fly the projectile's own path, with the same gravity and |
| `FxViewer.Action.cs` | 363 | FxViewer | The in-action scenes (--action &lt;family&gt;): each family as it would be seen in play, caught at a telling |
| `FxViewer.Gibs.cs` | 301 | FxViewer [GibCell] | Gibs in the effects viewer (GROUND.md §9.4). --gibs lays out every model that breaks (props, deployables, the |
| `FxViewer.Sheet.cs` | 266 | FxViewer [Cell] | The contact sheet (--sheet &lt;family&gt;): every effect of a family in its own cell, all caught at their |
| `FxViewer.Ship.cs` | 225 | FxViewer | The Iron Wake in the effects viewer (IRON_WAKE.md §11): --action ship, its siege battery firing over its deck |
| `FxViewer.Stress.cs` | 333 | FxViewer [Shooter] | The firefight measurement (--stress firefight --measure &lt;s&gt;, GROUND.md §9.3): ten players in two teams |
| `FxViewer.cs` | 504 | FxViewer | The effects viewer (--view fx, GROUND.md §9.3). By default every effect of the catalogue plays in turn on its |
| `FxWarm.cs` | 79 | FxWarm | The effects' part of the renderer's warm-up (GROUND.md §4.3.1, §9.3): every effect of fx.json played once, |

### game/scripts/GalaxyMap (5 files, 1,604 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `GalaxyGlow.cs` | 191 | GalaxyGlow | The galaxy's overall glow: a volume texture of light and dust computed from the density model on a worker |
| `GalaxyMapUi.cs` | 315 | GalaxyMapUi | The galaxy map's interface: where the camera is, search, the selected system, key hints and status. |
| `GalaxyMapView.cs` | 894 | GalaxyMapView | The galaxy map (GALAXY.md §11): the whole Milky Way as a glowing volume, real and generated stars streamed |
| `MapCamera.cs` | 97 | MapCamera | Orbit camera for the galaxy map, in double precision (galactic frame, light-years). Inputs set targets; |
| `StarStreamer.cs` | 107 | StarStreamer [StarBlock] | Streams galaxy cells around the camera focus (GALAXY.md §11). Each level is shown out to a radius that grows |

### game/scripts/Gibs (6 files, 1,789 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `GibBudgetCheck.cs` | 314 | GibBudgetCheck [Result, BatchCount, VisibilitySnapshot] | --gib-budget-check: loads one box's fragments into the real gib runtime and checks live and batch limits and the preference round trip; no terrain, server or saves. |
| `GibCatalog.cs` | 178 | GibMaterial, GibCause, GibCatalog | A material's pieces (gibs.json): how they bounce, slide and spin, their dust (an effect, tinted) and sound. |
| `GibDirector.cs` | 98 | GibBreak, GibDirector | What a break did (GROUND.md §9.4): whether pieces were placed or only dust, the middle of what broke, and the sounds |
| `GibLibrary.cs` | 290 | GibChunk, GibSet, GibLibrary | One piece of a model (GROUND.md §9.4): its mesh (the model's own materials), where it sits in the model |
| `GibWarm.cs` | 36 | GibWarm | The gibs' part of the renderer's warm-up (GROUND.md §4.3.1, §9.4): every gib file the catalogue names, read, and a |
| `GibWorld.cs` | 873 | GibFloor, GibWorld [PieceState, Turn, Piece, BreakFloor, Batch] | Where the floor is under a point: a ray from  along  for |

### game/scripts/Ground (73 files, 20,092 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `ArrowsView.cs` | 81 | ArrowsView | Spent arrows stuck where they hit (GROUND_SYSTEMS.md §5): every one this client knows, placed each frame where its holder |
| `BuildPanels.cs` | 247 | Keypad, RectSnapping | The code lock's keypad (GROUND.md §6.5), in the RustMVP interface's manner: a dark dialog in the middle of the |
| `BuildView.cs` | 458 | BuildView, GodotVectors [LiveBuildingWallet] | Building in the ground game (GROUND.md §6.5, phase 2b): the replicated structures drawn by the building package's |
| `ChildServer.cs` | 195 | ChildServer | A game hosted on this computer in a process of its own (GROUND.md §4.3): the dedicated server (Hollow.Server, built |
| `ClientConsole.Bindings.cs` | 91 | ClientConsole | ClientConsole's key bindings: bind, unbind, binds and noclipspeed, running a bound command (N admires), kept in user://console-bindings.cfg |
| `ClientConsole.cs` | 679 | ViewSettings, ClientConsole | The view's settings the console sets (GROUND.md §8.2), kept on this computer: field of view, mouse sensitivity, the frame rate shown. |
| `ClientDeveloper.cs` | 327 | ClientDeveloper | The F2 panel's model over the client (GROUND.md §8.5): the server's stats, sections and players as the client's dev |
| `ClientGroundUi.Attachments.cs` | 56 | ClientGroundUi | ClientGroundUi's weapon attachments: the slots a gun offers, which are fitted, and moving an attachment between a gun and a container, or quick-moving it to a free slot |
| `ClientGroundUi.Interaction.cs` | 243 | ClientGroundUi, InteractionRadial | The interact key in the view model (GROUND_SYSTEMS.md §5, GROUND.md §8.4): every frame the one targeting query says |
| `ClientGroundUi.World.cs` | 470 | ClientGroundUi | The interface's G7 members over the client (GROUND.md §6.8): the zone's map and what is on it (the player, teammates, |
| `ClientGroundUi.cs` | 720 | ClientGroundUi | The ground interface's view model over the client (GROUND.md §8.1): GroundUi reads the player's own |
| `CompoundView.cs` | 110 | CompoundView [Rig] | Animates articulated deployables from replicated state (leaves, rotor, fitted locks), retaining each rig across state packets; Retire detaches its callbacks. |
| `ContactFeedback.cs` | 22 | ContactFeedback | Loads the contact-response and action-cue tables from data/ground for the Godot side, and turns a surface's dust into a Color. |
| `ContactFeedbackCheck.cs` | 267 | ContactFeedbackCheck [Check] | --contact-feedback-check: native fixture of contact presentation: decal attachment, immediate and delayed, and wood, stone and spark responses from the real table; optional --shot. |
| `DefenseView.cs` | 70 | DefenseView [Rig] | Moves a defence deployable's authored assembly pivots and mounts its replicated installed firearm, without rebuilding the model. |
| `DevDraw.cs` | 492 | DevDraw | The F2 panel's drawings in the world (GROUND.md §8.5), over the client's dev module's toggles: the hitboxes of every |
| `ElectricalView.cs` | 119 | ElectricalView [Rig] | Power ports and cables of electrical deployables: authored port cubes and one tube mesh per routed cable, rebuilt only when its record or poses change. |
| `ElectricityReview.cs` | 60 | ElectricityReview | --electricity-review <folder>: small scene of a windmill, auto turret and SAM site joined by cables, using the production models, ports and cable renderer, for pictures. |
| `EnvironmentRoute.Details.cs` | 41 | EnvironmentRoute | EnvironmentRoute's optional detail views at a checkpoint: ground, looking down at roots and a lifted 9 m view, with the camera, frame cap and vsync restored afterwards |
| `EnvironmentRoute.Performance.cs` | 284 | EnvironmentRoute | EnvironmentRoute's --environment-breakdown: stationary render timing with layers (vegetation, grass, cliffs, water, shadows, SDFGI) removed one at a time, sampled for 8 s each |
| `EnvironmentRoute.cs` | 382 | EnvironmentRoute | --environment-route <folder>: a hosted client walks a real route from spawn to the beach through its normal input, recording checkpoints, trajectory and frame profiles; no teleport or noclip. |
| `FiresView.cs` | 150 | FiresView [Shown] | Fires in the ground game (IW4, IRON_WAKE.md §7, GROUND.md §9.3): each pool of burning fuel the server tells of is the |
| `FirstPersonView.cs` | 495 | FirstPersonView | The local player in first person (GROUND.md §4.8): the character package's ViewmodelView holding the |
| `FrameEvents.cs` | 29 | FrameEvents | What happened in this frame that may cost time (for --measure's report of the slowest frames). |
| `GibsView.cs` | 53 | GibsView | Things breaking apart in the ground game (GROUND.md §9.4): whatever the damage pipeline's Destroyed event names |
| `Glow.cs` | 52 | Glow | Light and glow in the planet's own units (GROUND.md §4.6): the sky meters the camera like an eye, a few thousand |
| `GroundAudio.cs` | 384 | GroundAudio, Sounds | The ground game's sound (GROUND.md §4.9), through the audio package: one AudioDirector listening at |
| `GroundChecks.cs` | 20 | GroundChecks | Checks of the ground game that run inside the client process. |
| `GroundCollision.cs` | 97 | GroundCollision | The ground near the player in Godot's own physics (layer 1), for what Godot simulates: ragdolls of the dead and |
| `GroundMeasure.cs` | 253 | GroundMeasure [Probe] | Measures the ground game's frame time where it is played (GROUND.md §5, --measure &lt;s&gt;): once the world |
| `GroundMode.Loading.cs` | 125 | GroundMode | The ground game's start (GROUND.md §4.3.1): one loading pipeline per game, in which the parts that make the game |
| `GroundMode.cs` | 517 | GroundMode [MenuCamera] | The ground game (GROUND.md §4.3). With neither --host nor --connect it opens the main menu over the |
| `GroundModels.cs` | 278 | IGroundModels, GroundModels, CatalogModels, PlaceholderModels | Where the ground game's world models come from (GROUND.md §9): deployables, bags of dropped items, outcrops and the |
| `GroundOverlay.cs` | 203 | GroundOverlay [ReloadBar] | What the ground game shows besides the RustMVP interface: other players' names over their heads (placed by |
| `GroundTerrain.cs` | 138 | GroundTerrain [TileArrays] | Placeholder ground: the collision tiles of ISurface.GroundCollision drawn as plain meshes; only the build sandbox draws with it since S3c |
| `GroundTour.AdminCamera.cs` | 198 | GroundTour | GroundTour's admin-camera step: checks the F1 third-person self camera through real input (right-drag orbits, clicks never fire or aim) and its hand-back to first person |
| `GroundTour.Admire.cs` | 169 | GroundTour | GroundTour's admire step (--tour-steps admire): real N key, mouse and inventory input against a rifle and a rock, checking cancellations and that admiring changes nothing |
| `GroundTour.Attachments.cs` | 162 | GroundTour | GroundTour's repair-attachments step: a rifle fitted with a holo sight, scope and weapon light through real transactions; checks the optic's eye alignment, lens geometry and beam against a hut. |
| `GroundTour.Attacks.cs` | 255 | GroundTour | GroundTour's attack-launcher and attack-sync steps (--tour-steps attack-sync): rocket launcher presses and Iron Wake station fire, local presentation against the authoritative result. |
| `GroundTour.Audio.cs` | 230 | GroundTour | GroundTour's audio-movement step: walks, runs, crouches and jumps by real prediction input while recording the Master and Footsteps buses, checks footfalls and writes audio-movement.json |
| `GroundTour.Bow.cs` | 137 | GroundTour | GroundTour's bow-alignment step (--tour-steps bow-alignment): real mouse draws and looses arrows, checking the arrow converges on the crosshair, wrist attachment and admire interruption |
| `GroundTour.ClanBase.cs` | 113 | GroundTour | GroundTour's clan-base step: finds a level dry site, runs the F1 spawn raidbase command, checks the stone preset replicated with four doors and a cupboard, and takes four views |
| `GroundTour.Compound.cs` | 298 | GroundTour | GroundTour's compound step: pays for and places walls, gates, windmill, SAM site, auto turret and furnace through the client, fits locks, wires power, loads weapons, checks each |
| `GroundTour.Fitted.cs` | 74 | GroundTour | GroundTour's fitted step: places five windows and both double doors in apertures through the normal client pipeline, checks the fitted mesh and open and close, one view each |
| `GroundTour.MapDrag.cs` | 86 | GroundTour | GroundTour's map-drag step: drags the on-foot map with the real mouse, measures frame time and overlay cost idle and dragging, and checks no marker is added |
| `GroundTour.Repairs.cs` | 335 | GroundTour | GroundTour's repair-ships and repair-corpses steps and their helpers: death and respawn, a fresh ship's fuel inventory and seated mouse gestures, through real input. |
| `GroundTour.Resources.cs` | 167 | GroundTour | GroundTour's resources step: finds stone, metal, sulphur and high-grade nodes in generated scatter, mines each, checks every stage drawn, the hotspot, exact yields and depletion |
| `GroundTour.cs` | 2596 | GroundTour | The scripted screenshot run of the ground game (GROUND.md §5, --tour &lt;folder&gt;): on the host, a walk |
| `GroundView.AdminCamera.cs` | 70 | GroundView | GroundView's admin self camera: the F1 thirdperson orbit around the local character, its input, eligibility, and placement kept clear of walls and the ground (Hollow.Client.AdminSelfCamera) |
| `GroundView.cs` | 1423 | GroundView | The ground game in first person (GROUND.md §4.3) on a whole world: the surface view draws the ball in the zone's own frame (docs/WHOLE_WORLD.md §10) |
| `GroundWarmup.cs` | 43 | GroundWarmup | Everything the ground game can draw, for the renderer's warm-up (GROUND.md §4.3.1): each library's items, read from its |
| `GunEffects.cs` | 119 | GunEffects | Shots, hits and guns in hand through the effects library and the model catalogue (GROUND.md §6.4, §9.3): which |
| `InventoryBody.cs` | 56 | InventoryBody | The player's own body on the inventory screen (GROUND.md §8): the character package's CharacterView |
| `ItemIconAliases.cs` | 103 | ItemIconAliases | The ground game's item icons until the assets package renders them from our models (GROUND.md §8): items.json keys |
| `ItemLooks.cs` | 35 | ItemLooks | How items look on a character (GROUND.md §4.8): the held model and pose an items.json key takes in the hands (the |
| `Keypad.Text.cs` | 86 | Keypad | Keypad's text mode: AskText shows the interaction modal with a LineEdit for a typed label instead of digits, with its own error line and layout. |
| `LoadTrace.cs` | 394 | LoadTrace [Late] | The ground game's start, timed (--load-trace [seconds], GROUND.md §4.3.1): from the process's start, when the |
| `MarkerTextures.cs` | 224 | MarkerTextures [Style] | The weak spots' textures (GROUND.md §9.3), painted in code once and kept (our own art): gather.json's |
| `MenuGroundUi.cs` | 54 | MenuGroundUi | The interface's view model before a game exists (the main menu): nothing carried, nothing to show. |
| `NatureMarkers.cs` | 226 | NatureMarkers [Marker] | The weak spots drawn (GROUND.md §6.1, §9.3), one generic marker for every class: gather.json's |
| `NaturePlants.cs` | 80 | NaturePlants | The ground game's plants (docs/WHOLE_WORLD.md §10.3): the zone's natural objects, by their tiles, as ScatterItems of the kind gather.json names |
| `NatureView.ResourceChecks.cs` | 25 | NatureView | NatureView's TryRenderedNatural: the stage model actually streamed for a natural object id, so hosted acceptance checks can test drawn geometry |
| `NatureView.cs` | 868 | NatureView [PendingSound, Seen, TreeFall] | The natural objects as the game changed them (GROUND.md §6.1): the plants' changes, outcrops and pickups drawn as models, weak spots, gathering's feedback, falling trees |
| `NetGraph.cs` | 72 | NetGraph | The network graph (GROUND.md §4.4): the last 30 s of round trip, bandwidth down and prediction error, one line each, |
| `PeopleView.cs` | 827 | PeopleView [Person, Corpse] | The other players (GROUND.md §4.8): each a CharacterView fed every frame from what the client |
| `PlacementGhost.cs` | 204 | PlacementGhost | The one placement ghost (GROUND_SYSTEMS.md §6): whatever is being placed, drawn as its own model in one hologram |
| `PlacementView.cs` | 161 | PlacementView | Placing in the game (GROUND_SYSTEMS.md §6): the one placement controller, behind IPlacement for the held |
| `PresentationPoseCheck.cs` | 122 | PresentationPoseCheck | Native checks of renderer-owned poses for a door and compound deployables: real leaf meshes, coordinate frames, replacement and retirement; run by ContactFeedbackCheck. |
| `RaidView.cs` | 583 | RaidView, RaidAlertsPanel [Shown, Card] | Raiding in the ground game (GROUND.md §6.6): explosives drawn from the model catalogue on their arcs, stuck to what |
| `RenderWarmup.cs` | 353 | RenderWarmup [Preview, IAssetLoader, GodotAssetLoader] | The renderer's warm-up (GROUND.md §4.3.1, the loading stage renderer). Every file the game's libraries list |
| `SeatKeys.cs` | 131 | SeatAction, SeatKey, SeatKeys | What a seat's key does (IRON_WAKE.md §5.2): leave it, work a door, the lock, descend from orbit, the camera. |
| `ShipsView.cs` | 315 | ShipsView [Drawn] | Ships in the ground game (G6, GROUND.md §6.7): every ship the client knows drawn by a ShipView, others' |
| `ThingsView.cs` | 490 | ThingsView | Placed and dropped things (GROUND.md §6.1): deployables and bags of dropped items from GroundModels, a lit |

### game/scripts/Ground/Held (5 files, 598 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AmmoRadial.cs` | 111 | AmmoRadial | The gun in hand's rounds (GROUND_SYSTEMS.md §7), R held: every kind of round of its calibre the player carries, with |
| `HeldControl.cs` | 124 | HandPress, HandButtons, HeldContext, IHeldControl, HeldControls | Buttons pressed for the hands since the last frame: attack (left mouse), aim (right mouse), R. |
| `HeldControls.cs` | 187 | MeleeControl, BowControl, FirearmControl, ThrowableControl, PlacerControl, PlannerControl, HammerControl, UsesItsPartControl | The held controls (GROUND_SYSTEMS.md §7), one per held behaviour; registered in HeldControls.Create. |
| `Placement.cs` | 21 | IPlacement | Placing what is in hand (GROUND_SYSTEMS.md §6, package P2's system): the placer's, the planner's and a charge's one |
| `WiringControl.cs` | 155 | WiringControl, WiringPreview | Held control for every data-declared wiring tool: aims at power ports, collects cable anchors and sends the intents; WiringPreview draws the cable being laid. |

### game/scripts/Items (10 files, 2,307 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `CatalogItemIcons.cs` | 119 | CatalogItemIcons | Item icons rendered from the models (GROUND.md §9): the catalogue's PNG nearest above the asked size, resampled |
| `GunAttachmentPresentation.cs` | 119 | OpticInfo, AttachmentLightInfo, GunAttachmentPresentation, WeaponAttachmentLight | Presentation of gun attachments from their own models: optic lens and eye socket, light beam origin, TryEye, and WeaponAttachmentLight, in both held views. |
| `IconRenderer.cs` | 401 | IconRenderer, IconSheet | The icon tool (--view icons, GROUND.md §9). With --out &lt;dir&gt; it renders every catalogue model |
| `ModelCatalog.cs` | 554 | Pose, HeldInfo, SupportHandInfo, GripInfo, GibsInfo, ModelEntry, ModelUse, ModelCatalog | A position (metres) and a rotation (degrees, Godot's Euler order YXZ) from the catalogue. |
| `ModelViewer.cs` | 676 | ModelViewer | The model viewer (--view models, GROUND.md §9). By default every catalogue model stands on the ground in |
| `ModelWarm.cs` | 47 | ModelWarm | The model catalogue's part of the renderer's warm-up (GROUND.md §4.3.1): every model of models.json (items, |
| `OpticLensView.cs` | 30 | OpticLensView | Keeps a held optic's lens material in step with the ground camera's exposure, without changing shared material resources. |
| `ScopeLensView.cs` | 128 | ScopeLensView | Magnified scope: one bounded HDR render of a camera-centred crop of the world onto the lens's rear aperture, active only while aiming down sights. |
| `SurfaceSurvey.cs` | 139 | SurfaceSurvey | Measures the drawn surfaces of the natural objects the ground game draws from catalogue models (the outcrops at |
| `WireToolAssetBuilder.cs` | 94 | WireToolAssetBuilder | --build-wire-tool: bakes the editable wire-tool source scene into assets/items/wire-tool.tscn with external materials, as other held items are built. |

### game/scripts/Monuments (1 file, 624 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `MonumentViewer.cs` | 624 | MonumentViewer [Monument] | The monument viewer (--view monuments, MONUMENTS.md §8.5): every monument in data/monuments laid out |

### game/scripts/Settings (6 files, 1,457 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `CrosshairValues.cs` | 57 | CrosshairStyle, CrosshairValues | The player's crosshair preferences: CrosshairStyle and CrosshairValues (size, gap, colour, opacity, dynamic spread), with Normalize bounding stored or previewed values. |
| `GraphicsNode.cs` | 68 | GraphicsNode | Applies the graphics settings to the window and everything global whenever they change (GROUND.md §8.6), and draws the sharpening pass |
| `GraphicsSettings.cs` | 639 | QualityPreset, AntiAliasingMode, GameWindowMode, GraphicsValues, GraphicsSettings [FogLook] | The game's graphics settings (GROUND.md §8.6): the values shown and saved, the presets, one function per thing they reach |
| `LocalShadowBudget.cs` | 115 | LocalShadowBudget [Entry] | Registry of shadow-capable local lights: Track and Untrack, and a per-frame Update that gives a bounded set shadows by priority and proximity to the camera. |
| `SettingRows.cs` | 218 | SettingKind, SettingRow, SettingRows | One row of the settings panel (GROUND.md §8.6): its name, a one-line explanation, and how it reads and changes the values |
| `ShadowBudgetCheck.cs` | 360 | ShadowBudgetCheck [CheckResult] | --shadow-budget-check: smoke check of the shadow settings and the local-light registry in a small world; previews are restored and never saved. |

### game/scripts/Ships (12 files, 4,150 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `ShipCamera.cs` | 90 | ShipCameraMode, ShipCamera | First person, from the pilot's eye in the cockpit, looking along the nose. |
| `ShipControls.cs` | 102 | ShipControls | The pilot's keyboard and mouse (G6, GROUND.md §6.7), read into a tick of ShipInput. The mouse is a |
| `ShipHud.cs` | 391 | ShipHudData, ShipHud | What the flight HUD shows this frame (the host fills it from the zone or the client's prediction). |
| `ShipSandbox.Aboard.cs` | 610 | ShipSandbox [Walk] | The ship sandbox for a ship people walk in (IRON_WAKE.md §4, IW1): `--view ship --ship iron-wake`. The ship parked in a |
| `ShipSandbox.Tour.cs` | 216 | ShipSandbox [Leg] | --tour: the sandbox played by a script through the same keys a player presses (Input.ParseInputEvent), to check the |
| `ShipSandbox.cs` | 898 | ShipSandbox [Site] | The ship sandbox (--view ship, GROUND.md §6.7): the Tin Finch on the first world's ground (the stand-in world |
| `ShipSounds.cs` | 469 | ShipSounds | What one ship sounds like (IW6, IRON_WAKE.md §10): every role of its kind's in sounds.json from the replicated state, slow and fast loops crossfaded. |
| `ShipView.cs` | 663 | ShipLook, ShipView [PartNode, Drive, Plume] | What a ship is doing this frame, as a ShipView draws it: plumes, lamps by power, reflection probes, sockets where drawn. |
| `ShipWarm.cs` | 28 | ShipWarm | The ships' part of the renderer's warm-up (GROUND.md §4.3.1, §6.7): every ship of ships.json as |
| `StationAmmoRadial.cs` | 95 | StationAmmoRadial | A gunner's rockets (IW3), R held at a station: the kinds and counts; a tap cycles. |
| `StationSight.cs` | 244 | SightBarrel, SightKind, StationSightData, StationSight | The gunner's sight (IW3, IRON_WAKE.md §6.8) in the ground interface's style. |
| `StationsView.cs` | 344 | StationsView [LoaderSeen, SlotModel, StationSeen, ShipSeen] | The batteries drawn and heard (IW3): mounts, recoil, loader pieces by the table, rockets in slots, phases' sounds, shots. |

### game/scripts/Surface (79 files, 20,808 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AerialTable.cs` | 115 | AerialTable | The aerial-perspective table of the surface view (GROUND.md §4.6): the air's in-scattered light and transmittance by direction and distance (400 km on a real planet, 10 km on a whole small world) |
| `AtmosphereModel.cs` | 450 | AtmosphereModel | The physically based air of a planet for drawing (GROUND.md §4.6): Rayleigh scattering from its molecules, Mie |
| `AuthoredCoverRootMesh.cs` | 208 | AuthoredCoverRootMesh [Description] | Builds a ground-cover mesh with whole-plant root data (CUSTOM0) from an authored single-surface mesh, verifying the imported vertices and leaving the import shared. |
| `CliffField.Far.cs` | 405 | CliffField [FarChunk, FarSweep] | CliffField's far layer (docs/TERRAIN_SCENERY_PLAN.md T2): formations the near layer has not installed, one MultiMesh per model in each cube-sphere chunk, swept by two low-priority threads, no shadows. |
| `CliffField.cs` | 299 | CliffField [Model, Instance] | CliffField's near layer: scanned geology installed at authoritative placements round the camera, with levels of detail, streamed apart from harvestable nodes. |
| `EnvironmentLifetimeCheck.AuthoredRender.cs` | 370 | EnvironmentLifetimeCheck | EnvironmentLifetimeCheck's authored-cover render check (--environment-authored-cover-render-only): draws the reviewed fern's real PBR with ground contact in eight frames, within a 35 s deadline |
| `EnvironmentLifetimeCheck.AuthoredRoots.cs` | 188 | EnvironmentLifetimeCheck | EnvironmentLifetimeCheck's authored-cover root checks: synthetic mesh augmentation keeps source identity and refuses mismatches, plus the real fern's six meshes against its reviewed roots |
| `EnvironmentLifetimeCheck.AuthoredSpatial.cs` | 332 | EnvironmentLifetimeCheck | EnvironmentLifetimeCheck's authored spatial probe (--environment-authored-spatial-only): moving admission, atlas replacement, capacity refusal, missing support, terrain visibility and retirement |
| `EnvironmentLifetimeCheck.Client.cs` | 235 | EnvironmentLifetimeCheck [ClientProbeSurface] | EnvironmentLifetimeCheck's client step: real GameClient, WorldStore and views on a constant-ground probe surface; checks inventory-preview owners, view retirement leases and that fixture worlds dispose once |
| `EnvironmentLifetimeCheck.Cohorts.cs` | 337 | EnvironmentLifetimeCheck [CohortProbeSurface] | EnvironmentLifetimeCheck's terrain-cohort step (--environment-terrain-cohorts-only): tickets, shared reservations, cube-corner closure, hiding, reanchoring and retirement on a blockable fixture surface |
| `EnvironmentLifetimeCheck.ContactAtlas.cs` | 197 | EnvironmentLifetimeCheck | EnvironmentLifetimeCheck's contact-atlas step: TerrainContactAtlas staging, readiness, same-address replacement, capacity refusal, upload faults and disposal |
| `EnvironmentLifetimeCheck.CoverCompression.cs` | 533 | EnvironmentLifetimeCheck [CoverCompressionInput, CoverCompressionFamily, CoverError, CoverAngle, CoverPackedNormalStrata, CoverLengthDistribution, CoverPackedStratum, CoverPackedWorst] | EnvironmentLifetimeCheck's cover-compression gate (--environment-cover-compression-only): authenticates a receipt, then checks each cover texture's native format, mips, payload and numerical error against its CTEX baseline |
| `EnvironmentLifetimeCheck.CoverCompressionRender.cs` | 361 | EnvironmentLifetimeCheck [CoverCompressionArt] | EnvironmentLifetimeCheck's paired render of one cover family (--environment-cover-compression-render-only): saved CTEX baseline beside the imported material on the production meshes and shader |
| `EnvironmentLifetimeCheck.CoverLossless.cs` | 389 | EnvironmentLifetimeCheck [CoverLosslessPin, CoverLosslessInput] | EnvironmentLifetimeCheck's v2 lossless correction gate (--environment-cover-lossless-only): authenticates the migration receipts, then compares every WebP-lossless mip with the baseline byte for byte |
| `EnvironmentLifetimeCheck.FloraAppearance.cs` | 493 | EnvironmentLifetimeCheck [AppearanceFixtureCpu, AppearanceFixtureOwner, AppearanceFixtureBackend] | EnvironmentLifetimeCheck's flora-appearance session step (--environment-flora-appearance-only): admission, sharing, cancellation, upload and binding faults and quarantine, on a synthetic backend |
| `EnvironmentLifetimeCheck.FloraPixels.cs` | 217 | EnvironmentLifetimeCheck | EnvironmentLifetimeCheck's flora pixel check: FloraTextures dilation and mip output compared bit for bit with an independent full-array reference, on bounded synthetic images |
| `EnvironmentLifetimeCheck.FormationView.cs` | 337 | EnvironmentLifetimeCheck [FormationViewProbeSurface] | EnvironmentLifetimeCheck's formation-view step (--environment-formation-view-only): the view stays hidden until terrain is ready, then publishes one LOD, hides, rebases, rolls back and releases tickets |
| `EnvironmentLifetimeCheck.FormationVisual.cs` | 540 | EnvironmentLifetimeCheck [FormationVisualFixtureState, FormationVisualCleanupProbe, FormationVisualTinyPackage] | EnvironmentLifetimeCheck's formation-visual step (--environment-formation-visual-only): a tiny authenticated package through the native upload session: reservations, sharing, cancellation, cleanup ledger, a capture |
| `EnvironmentLifetimeCheck.Grass.cs` | 196 | EnvironmentLifetimeCheck [GrassProbeSurface] | EnvironmentLifetimeCheck's grass step: GrassField with a blockable surface (stop without waiting, cancelled work, single-use setup, mask upkeep, retirement) and the painted blade atlas's connectivity |
| `EnvironmentLifetimeCheck.GrassRender.cs` | 451 | EnvironmentLifetimeCheck | EnvironmentLifetimeCheck's grass render step (--environment-grass-only): the production grass mesh and shader on tilted ground in a viewport, checking blades, stable shape, wind and missing support |
| `EnvironmentLifetimeCheck.GroundNormals.cs` | 119 | EnvironmentLifetimeCheck [GroundNormalCase] | EnvironmentLifetimeCheck's ground-normal step (--environment-ground-normals-only): the surface_ground_normals shader include run on the GPU against analytic normals for planes, a tilt and rotated axes |
| `EnvironmentLifetimeCheck.ImpostorOwnership.cs` | 171 | EnvironmentLifetimeCheck | EnvironmentLifetimeCheck's impostor ownership step: ImpostorBaker readbacks, cancellation and hand-over of finished textures, so each native owner is released in order |
| `EnvironmentLifetimeCheck.ImpostorRows.cs` | 172 | EnvironmentLifetimeCheck [ImpostorRowCapture] | EnvironmentLifetimeCheck's impostor row step: baking an atlas one row band at a time must give the same bytes and mips as the full-atlas camera, on a real renderer |
| `EnvironmentLifetimeCheck.NativeFloraAppearance.cs` | 376 | EnvironmentLifetimeCheck [NativeAppearanceFixture] | EnvironmentLifetimeCheck's actual flora appearance step (--environment-flora-package-only): the real Earth catalogue under an external approval record and SHA-256 pin: sharing, isolation, readback, retirement |
| `EnvironmentLifetimeCheck.SpatialGrass.cs` | 129 | EnvironmentLifetimeCheck | EnvironmentLifetimeCheck's spatial-grass step: GrassField's compacted cells, tile uploads, staged generations, upload faults and stop with a blocked worker |
| `EnvironmentLifetimeCheck.Surface.cs` | 138 | EnvironmentLifetimeCheck [LeasedProbeSurface] | EnvironmentLifetimeCheck's surface-ownership step: SurfaceView retirement with leased worlds, failed openings and old versus new planet owners, so the surface is disposed exactly once |
| `EnvironmentLifetimeCheck.TerrainAppearance.cs` | 594 | EnvironmentLifetimeCheck [AppearanceExpected, AppearanceQuery] | EnvironmentLifetimeCheck's terrain-appearance step (--environment-terrain-appearance-only): fixed storage, stream upload rejection, retirement and a GPU oracle for the drawn vertex path's interpolants |
| `EnvironmentLifetimeCheck.TerrainContact.cs` | 249 | EnvironmentLifetimeCheck | EnvironmentLifetimeCheck's terrain-contact step: snapshots of the drawn coarse roots, capacity, hidden and restored tiles, morph bounds, rebase, stale grids and both grid diagonals |
| `EnvironmentLifetimeCheck.TerrainContactGpu.cs` | 394 | EnvironmentLifetimeCheck [ContactGpuQuery] | EnvironmentLifetimeCheck's opt-in GPU oracle for terrain contact: the production shader helper's lookups on planar and cube-sphere grids against CPU positions, with the generation guard on reuse |
| `EnvironmentLifetimeCheck.Vegetation.cs` | 126 | EnvironmentLifetimeCheck [ProbePlants] | EnvironmentLifetimeCheck's vegetation step: Vegetation fed by a blockable plant source: stop and retirement cancel queued and layout work, setup is single use, bake cancellation |
| `EnvironmentLifetimeCheck.Warmup.cs` | 270 | EnvironmentLifetimeCheck [WarmupFixtureLoader] | EnvironmentLifetimeCheck's warm-up step: RenderWarmup's bounded preview admission, draw-event counting, deferred deletion and replay of the catalogue on a replacement camera |
| `EnvironmentLifetimeCheck.WorldPhase.cs` | 275 | EnvironmentLifetimeCheck | EnvironmentLifetimeCheck's world-phase step: the production ground_look and wave helpers on small patterned maps: sampler phase, body and render normal frames, absolute double body points |
| `EnvironmentLifetimeCheck.cs` | 293 | EnvironmentLifetimeCheck [Result, ProbeSurface] | --environment-lifetime-check: bounded native regression probe of the surface view's resource lifetimes (terrain reset and stop, grass, flora, formations); the checks sit in its partial files. |
| `FormationFieldView.cs` | 331 | FormationVisualTemplate, FormationViewLimits, FormationFieldView [Part, PieceView, Entry, Diagnostics] | FormationVisualTemplate, FormationViewLimits and FormationFieldView: per-view drawing of formation cohorts from borrowed, admitted meshes; a cohort shows once its terrain is visible. |
| `FormationVisualLoader.cs` | 598 | FormationNativeLimits, FormationMaterialApproval, FormationVisualLodSelection, FormationVisualSurfaceSlot, FormationVisualCustom0Binding, FormationVisualCustom0Profile, FormationMaterialProfile, FormationVisualLoader [TextureVariant, MaterialAddress, NativePlan, UploadJob, Owner, Retirement] | Formation visuals' native plan and upload: material and custom0 mask profiles, preflight of a package, an incremental UploadJob (textures, materials, surfaces) and the Owner that retires them |
| `FormationVisualSession.cs` | 472 | FormationVisualSessionBudget, FormationVisualSession [Snapshot, RequestState, Request, Lease] | FormationVisualSession and its budget: main-thread owner of one approved formation receipt, reserving CPU, native and upload allowances for one CPU task and an incremental upload, with leases. |
| `GeologyReview.cs` | 144 | GeologyReview | Standalone scene (--eye, --target, --rocks, --quality, --shot): renders the real Earth's first cliff or a rock from a given camera, writing a picture and scene facts. |
| `GrassField.Authored.cs` | 207 | GrassField [CoverPlant, CoverMesh, CoverBatch] | GrassField's authored ground cover: families, variants and levels of detail from assets/surface/ground-cover/catalog.json, laid out on the ground lattice in MultiMesh batches |
| `GrassField.AuthoredSpatial.cs` | 895 | GrassField [AuthoredSpatialSource, AuthoredSpatialStatistics, AuthoredSpatialFixtureControl, AuthoredSpatialArt, AuthoredSpatialPoint, AuthoredSpatialTile, AuthoredSpatialIndex, AuthoredSpatialChoice, AuthoredSpatialBatchKey, AuthoredSpatialPlan, AuthoredSpatialBatch, AuthoredSpatialGeneration, AuthoredSpatialStage, AuthoredSpatialState] | GrassField's opt-in tiled authored cover: per-tile plant index, near, middle and far batches, staged generations, bounded scene-thread uploads and retirement, addressed through the contact atlas |
| `GrassField.Contact.cs` | 138 | GrassField | GrassField's drawn-contact binding: the TerrainContactAtlas updated each frame from the terrain, its readiness, the bounds it gives batches, and its retirement |
| `GrassField.Diagnostics.cs` | 165 | GrassField | GrassField's opt-in diagnostics: --grass-diagnostics <json path> writes the production mesh's and atlas's measurements, and the grass-foot check and fixture mesh the lifetime probe uses |
| `GrassField.Jobs.cs` | 174 | GrassWorkMonitor, GrassWorkSnapshot, GrassField [BuildContext, WorkerLifetime] | GrassField's background work: GrassWorkMonitor (jobs started, pending, completed and cancelled, outliving the view) and the worker lifetime that builds field data. |
| `GrassField.LifecycleCheck.cs` | 69 | GrassField | GrassField.SetupLifecycleFixture: a tiny resource setup (small lattice, stub shader, a few batches) for EnvironmentLifetimeCheck, running the production workers and visibility logic |
| `GrassField.Spatial.cs` | 347 | GrassField [SpatialFaceResources, SpatialBatch, SpatialGeneration] | GrassField's spatial submission (--spatial-grass, --spatial-grass-cells 16 or 32): per-tile MultiMeshes, bounded two-generation upload, culling by tile bounds |
| `GrassField.SpatialDiagnostics.cs` | 31 | SpatialGrassDiagnostics, GrassField | SpatialGrassDiagnostics and GrassField.CaptureSpatialDiagnostics: resident and staged tiles, buffer bytes, upload cost and the allocation limit |
| `GrassField.SpatialLifecycleCheck.cs` | 252 | GrassField [SpatialFixtureFault] | GrassField's spatial lifecycle fixture: tiny art-free field with injected faults, checking cells, coverage admission, bounds and native resource retirement |
| `GrassField.SpatialSampling.cs` | 116 | GrassField [SpatialTileData] | GrassField's worker-side spatial tiles: which cells of a lattice may grow grass or litter, built per tile with their bounds, keyed by face and global cell |
| `GrassField.SubmissionDiagnostics.cs` | 107 | GroundCoverLayers, GroundCoverSubmission, GroundCoverDiagnostics, GrassField | GroundCoverLayers mask and GrassField.CaptureDiagnostics: what grass, litter and authored cover submit (batches, instances, vertices, triangles) before culling |
| `GrassField.cs` | 638 | GrassField [FaceLattice, FieldData, FaceGrid] | The grass of the surface view (GROUND.md §4.6, docs/WHOLE_WORLD.md §10.3): tufts on cube-face cells (level 13 on a 2.41 km world) placed by the GPU from a lattice of the cells' corners on the drawn ground, one grid a face near a seam |
| `GroundLayers.cs` | 355 | GroundLayers [Loaded, Texture, Variant, Layer, Catalogue] | The ground's sixteen 4K texture layers (WALKING_GROUND.md §8), read from their imported files into three texture arrays every ground material shares: colour, normals, roughness and height; the layers' sizes and normal flips |
| `MonumentField.cs` | 102 | MonumentField | Draws the live world's monument assemblies at the placements that loot and physics use, streamed round the camera; collision stays with the shared engine-free tiles. |
| `NativeFloraAppearanceApproval.cs` | 240 | NativeFloraAppearanceApproval | Reads a fixture-only approval record for a native flora appearance, pinned by SHA-256: its package directory, pin, runtime fingerprint, catalogue identity and dependency files. |
| `NoiseTextures.cs` | 218 | NoiseTextures | Tiling noise textures for the surface's shaders (presentation only): the cloud field, and the large-scale |
| `PlanetGlobals.cs` | 112 | PlanetGlobals | The planet under everything drawn (docs/WHOLE_WORLD.md §10): its centre, radius and axes as shader globals (surface_planet.gdshaderinc) and for what moves along up (effects, gibs, tracers) |
| `PlantSource.cs` | 38 | IPlantSource, SurfacePlants | Where the surface view's plants come from (docs/WHOLE_WORLD.md §10.3): tiles of ScatterItems; SurfacePlants, a surface's own scatter |
| `RenderFrame.cs` | 117 | RenderFrame | The frame Godot's world is drawn in (ARCHITECTURE.md §6): World, the body's own frame (the ground game's whole world), or a tangent frame of a large planet whose origin follows the camera (look-dev) |
| `RenderMemoryTrace.cs` | 46 | RenderMemoryTrace | RenderMemoryTrace.Mark: opt-in snapshots of engine-accounted renderer memory and process memory at named phase boundaries; no collection or readback. |
| `SurfaceDiagnosticGlobals.cs` | 58 | SurfaceDiagnosticGlobals | Sets neutral global shader parameters (radius, zero, one and noise textures) for isolated render diagnostics; never used with a live SurfaceView. |
| `SurfaceSky.cs` | 261 | SurfaceSky | The sky of the surface view at a universe time (GROUND.md §4.6): the real directions of the planet's sun, its moon, the other suns and planets, as lights hidden below the drawn world's horizon |
| `SurfaceStreamingDiagnostics.cs` | 54 | SurfaceView, TerrainLod, Vegetation, GrassField, CliffField, MonumentField | StreamingStatus strings of SurfaceView, TerrainLod, Vegetation, GrassField, CliffField and MonumentField, queried by capture and profile diagnostics. |
| `SurfaceUi.cs` | 97 | SurfaceUi | The surface view's overlay: where you are, the local time, the sun and moon, and the frame time. |
| `SurfaceView.Game.cs` | 48 | SurfaceView | The surface view driven by a game (GROUND.md §4.3): the game drives the camera and owns the keys; OpenZone opens a real planet in a tangent frame at a point (the main menu's backdrop) |
| `SurfaceView.LightingDiagnostics.cs` | 47 | SurfaceView | SurfaceView.LightingDiagnosticJson: capture metadata (camera, time, sun and moon, exposure, clouds, lights) from the sky that rendered the frame |
| `SurfaceView.Loading.cs` | 62 | SurfaceView | The surface view's part of the loading pipeline (GROUND.md §4.3.1): what IsReady waits for, as two stages with their |
| `SurfaceView.Water.cs` | 49 | SurfaceView | SurfaceView's underwater view: a screen layer and fog that rise with the camera's depth below the sea, a lake or a stream |
| `SurfaceView.cs` | 839 | SurfaceView [ViewLifetime] | The surface view (GROUND.md §4.6): a planet's ground, sea, plants, grass, air and sky; the look-dev view of a real planet (Open) and the ground game's whole world from any ISurface (OpenWorld, docs/WHOLE_WORLD.md §10) |
| `SurfaceWorldPhases.cs` | 133 | SurfaceWorldPhases [Binding] | One view's body-space texture and wave phases for the ground and water materials: only fractional phases reach shaders, bound per material and updated each frame. |
| `TerrainContactAtlas.Appearance.cs` | 148 | TerrainContactAtlas | TerrainContactAtlas's optional appearance arrays: the published grid's materials and normals copied into fixed Texture2DArrays (nine MiB cap), validated, retired |
| `TerrainContactAtlas.cs` | 379 | TerrainContactAtlas [FixtureFault, ConstructionCleanupException] | Atlas of the terrain triangles actually drawn, copied from published grids for contact lookups in shaders; at most two grid uploads a frame, all on the scene thread. |
| `TerrainLod.Cohorts.cs` | 291 | TerrainViewLimits, TerrainCohortAcquireStatus, TerrainCohortState, TerrainCohortTicket, TerrainCohortDiagnostics, TerrainLod [CohortEntry, WorkReservation] | TerrainLod's cohort tickets: TerrainViewLimits reservations, TryAcquireCohort keeping a formation cohort's finest tiles drawn, and the cohort states and diagnostics. |
| `TerrainLod.Contact.cs` | 180 | DrawnTerrainTile, TerrainContactSample, TerrainLod [ContactGridBounds] | TerrainLod's contact queries: DrawnTerrainTile snapshots of the drawn grids near a point, TerrainContactSample on a regular triangle, and ContactRevision. |
| `TerrainLod.Loading.cs` | 28 | TerrainLod | What the loading pipeline reads of the terrain (GROUND.md §4.3.1). |
| `TerrainLod.Workers.cs` | 166 | TerrainLod [BuildSession] | TerrainLod's BuildSession: managed worker threads that build tile grids from a queue with a work reservation, prioritising, cancelling and stopping |
| `TerrainLod.cs` | 699 | TerrainLod, TileMeshData [Node, Job] | Draws a planet's ground out to the horizon from any ISurface (GROUND.md §4.6): a quadtree of tiles on each cube face, the finest level by size, each tile with its face and texture coordinates in metres on it (UV2) |
| `Vegetation.Game.cs` | 179 | Vegetation | What the ground game changed about its plants (GROUND.md §6.1): felled trees, picked ferns and berry bushes; near sets rebuilt on a worker |
| `Vegetation.LifecycleCheck.cs` | 85 | Vegetation [LifecycleGate, LifecycleInput] | Vegetation's lifetime fixture: tiny scatter and layout jobs with gates and faults, checking the production queue and builders without flora art |
| `Vegetation.Lifetime.cs` | 332 | Vegetation [WorkSession, LibraryBuild, Axes, VariantInput, SpeciesInput, PlantInput, AppearanceInput, NearInput, BlockInput] | Vegetation's managed work sessions: scatter and layout worker threads, the flora library build, results, cancellation and a clean stop that holds no Node |
| `Vegetation.Loading.cs` | 17 | Vegetation | What the loading pipeline reads of the plants (GROUND.md §4.3.1). |
| `Vegetation.cs` | 785 | Vegetation [LodSet, Block, LodState, NearData] | Trees, bushes, berry bushes, ferns and rocks of the surface view (GROUND.md §4.6) from a plant source (IPlantSource), at its tiles' level, ranges from the horizon |

### game/scripts/Surface/Flora (11 files, 4,703 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `FloraAppearanceInputs.cs` | 878 | FloraAppearanceInputLimits, FloraAppearanceInputs [MeshReceipt, ImageReceipt, ImportedTextureReceipt, ImportedTreeScene, ImportedNode, HashStream] | FloraAppearanceInputs and its CPU limits: opt-in, main-thread capture of a flora appearance's upload and import inputs (images, meshes) as receipts. |
| `FloraAppearanceSession.cs` | 552 | FloraAppearanceSessionBudget, FloraAppearanceSession [Snapshot, RequestState, Request, Lease, CpuWork, SharedAssets, Operations, Retirement, LoadedAppearance] | FloraAppearanceSession and its budget: main-thread owner of one pinned flora appearance with reserved byte allowances, one CPU job, one native upload and retiring leases. |
| `FloraGeometry.cs` | 741 | GeometryBuilder, FloraGeometry [Part, Conifer, Broadleaf, Shrub] | Builds plant and rock meshes in code (presentation only), after hollow-orbit's flora: trunks and limbs as tubes |
| `FloraImpostorExport.cs` | 279 | FloraImpostorExport | --flora-impostor-export <folder> --flora-seed earth: bakes the impostors with the ordinary library and baker and writes their mip bytes and passes as a diagnostic; no planet or save. |
| `FloraLibrary.Rocks.cs` | 87 | FloraLibrary | FloraLibrary's authored rocks: loads assets/surface/rocks/catalog.json, gives each scanned PBR material surface_scanned_rock.gdshader, builds the rock species' variants and two levels of detail |
| `FloraLibrary.Trees.cs` | 159 | FloraLibrary [ImportedSceneRetirement] | FloraLibrary's authored trees: loads assets/surface/trees/catalog.json into species with surface_authored_tree.gdshader, rejecting unbaked transforms, and retires the imported scene |
| `FloraLibrary.cs` | 491 | FloraLibrary [Bands, Variant, Species, Data, CreationCleanupException, CreationResources, Entry] | The plants and rocks of the surface view (GROUND.md §4.6), built in code after hollow-orbit's flora: species for a |
| `FloraTextures.ImpostorPixels.cs` | 104 | FloraTextures | FloraTextures' impostor finish: dilates and mips an RGBA8 readback in 64-row bands, without expanding the atlas to floats, keeping each cell's coverage |
| `FloraTextures.cs` | 633 | FloraTextures | The painted textures of the surface view's plants (presentation only), after hollow-orbit's flora: a 4 × 4 atlas of |
| `ImpostorBaker.cs` | 480 | ImpostorBaker [Result, OwnershipFixtureHooks] | Renders every tree variant of the flora library from eight sides into two atlases (GROUND.md §4.6): colour with |
| `Painter.cs` | 299 | Painter | A small 2D vector painter for the foliage atlas (presentation only): paths of lines and quadratic curves, filled |

### game/scripts/SystemMap (4 files, 1,603 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `BodyText.cs` | 146 | BodyText | Words for a body's physical state (GALAXY.md §10.3) on the body card (presentation only). |
| `PlanetLook.cs` | 201 | PlanetLook | Colours and surface parameters of planets (presentation only): a base look by type, then oceans, lava, lakes, ice |
| `SystemMapUi.cs` | 570 | SystemMapUi | The system map's interface: system summary, body list, selected body, time and scale controls. |
| `SystemMapView.cs` | 686 | SystemMapView [BodyView] | The system map (GALAXY.md §12): an orrery of one star system, animated by universe time. Distances are |

### game/scripts/UI (23 files, 6,717 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `CharacterPreview.cs` | 138 | CharacterPreview | The figure on the inventory screen: a plain wooden mannequin in its own little world, rendered into a texture the |
| `CrosshairPainter.cs` | 76 | CrosshairPainter [Parts] | Draws the crosshair (dot, bars, ring) for the ground HUD and the settings preview, centred on the half pixel, its gap widened by the weapon's spread cone. |
| `GroundUi.Console.cs` | 764 | GroundUi | The F1 panel (GROUND.md §8.2): Rust's F1 menu in the ground interface's style, our own drawing. One dark translucent |
| `GroundUi.Crafting.cs` | 250 | GroundUi | The crafting screen (RustMVP's, from SBareRoot.cpp and its Godot predecessor): categories with counts, the |
| `GroundUi.Death.cs` | 194 | GroundUi | The death screen in Rust's layout: DEAD and how it happened across the top, the map with the body and the |
| `GroundUi.Developer.cs` | 404 | GroundUi | The F2 panel (GROUND.md §8.5): the developer's window on the game while playing it, in the F1 panel's shape and |
| `GroundUi.Globe.cs` | 860 | GroundUi [Globe, GlobeFrame] | The map control (docs/WHOLE_WORLD.md §11), one for the G screen and the death screen: the whole world as a globe seen |
| `GroundUi.Hud.cs` | 257 | GroundUi | The HUD (RustMVP's, from SBareRoot.cpp): the centre dot, hit marker, damage direction and prompt in the middle; |
| `GroundUi.HudCrafting.cs` | 62 | GroundUi | GroundUi's HUD crafting card: the head of the queue with its output, count owned and progress, placed beside the vitals and clear of the belt |
| `GroundUi.Inventory.cs` | 562 | GroundUi [DragState] | The inventory (RustMVP's, from SBareRoot.cpp): the character and clothing on the left, the item panel and the |
| `GroundUi.Machines.cs` | 69 | GroundUi | The loot panels of G7's machines (GROUND.md §6.8), in the furnace's manner: the recycler's six inputs and six outputs |
| `GroundUi.Map.cs` | 274 | GroundUi | The map (G7, GROUND.md §6.8; the whole world since S3d, docs/WHOLE_WORLD.md §11), on G as in Rust: the map control |
| `GroundUi.MapDiagnostics.cs` | 45 | GroundUi [MapDrawMeasurement] | GroundUi.MeasureMapDrawing: opt-in timing and allocation per frame of the map overlay's drawing (used by GroundTour's map-drag step) |
| `GroundUi.Menu.cs` | 138 | GroundUi | A small main menu for playtests, in RustMVP's menu style: the title and a column of big condensed items on the |
| `GroundUi.Paint.cs` | 421 | GroundUi | Immediate-mode painting on the design canvas, as RustMVP's SBareRoot painted: rectangles, text, icons and lines at |
| `GroundUi.Radial.cs` | 361 | GroundUi [Trigger] | The radial menu (GROUND_SYSTEMS.md §4; RustMVP's wheels, from SBareRoot.cpp): one control for every source of options. |
| `GroundUi.Settings.cs` | 257 | GroundUi | The settings panel (GROUND.md §8.6), opened by the menu's SETTINGS: GRAPHICS and INTERFACE tabs, a row per setting with its value and a line saying what it does |
| `GroundUi.TechTree.cs` | 220 | GroundUi | The tech tree of the workbench in reach (G7, GROUND.md §6.8), in RustMVP's manner: the tree laid out from its roots |
| `GroundUi.cs` | 731 | GroundScreen, GroundUi [HitKind, Hit, Pickup] | The ground interface's screens; the HUD shows under the inventory and crafting screens too. |
| `ItemIcons.cs` | 217 | IItemIconProvider, ItemIcons, PlaceholderItemIcons, GroundGlyphs | Where item icons come from. The icon package will render them from our models; until then, placeholders. |
| `LoadingScreen.cs` | 199 | LoadingScreen | The loading screen (GROUND.md §4.3.1, §8): a picture of the first world filling the window, the game's name, the |
| `Painter.cs` | 19 | Painter | A full-window layer whose drawing is done by its owner (the ground interface paints in immediate mode). |
| `RadialGlyphs.cs` | 199 | RadialGlyphs | The radial menu's outline glyphs (RustMVP's own perspective outlines, from SBareRoot.cpp, and ours in their manner), by |

### game/scripts/UI/Model (14 files, 2,542 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `ConsoleUiTypes.cs` | 136 | ConsoleTab, ConsoleLineKind, ConsoleLine, ConsoleCompletionView, InfoRow, InfoSection, NetGraphView, ToolButton, ToolGroup, ToolsView, IConsoleModel | The F1 panel's values (GROUND.md §8.2): plain C#, no Godot types, as the rest of the view model. The game's adapter |
| `CrosshairView.cs` | 5 | CrosshairView | CrosshairView: presentation state of the ground crosshair (aiming, spread cone half-angle in degrees, field of view, hidden). |
| `DeveloperUiTypes.cs` | 91 | DeveloperTab, TickGraphView, SectionRow, PlayerRow, DrawToggle, IDeveloperModel | The F2 developer panel's values (GROUND.md §8.5): plain C#, no Godot types, as the rest of the view model. The game's |
| `GroundUiTypes.cs` | 185 | ContainerId, SlotRef, AttachmentSlotView, ItemStack, ItemStat, ItemActionView, ItemInfo, ContainerKind, LootView, Privilege, Vitals, HeldAmmo, ProtectionStat, Ingredient, Recipe, CraftCategory, CraftJob, CraftingHudStatus, RespawnBag, DeathInfo, InteractionPrompt, TargetHealthView, ChatLine | The values the ground game's interface shows (GROUND.md §8). Plain C#, no Godot types: the client API adapts its |
| `IGroundUi.cs` | 267 | IGroundUi | The ground game's view model (GROUND.md §8): everything the HUD, inventory, crafting, death and radial screens show, |
| `KeyContext.cs` | 38 | KeyContext, KeyContexts | What the keys mean now (KNOWN_PROBLEMS.md finding 28; GROUND.md §6.7): on foot, or seated by the seat's role. The ground |
| `MockConsole.cs` | 216 | MockConsole | The F1 panel on sample data (GROUND.md §8.2), for the interface viewer (--view ui --screen console, and |
| `MockDeveloper.cs` | 152 | MockDeveloper | The F2 panel on sample data (GROUND.md §8.5), for the interface viewer (--view ui --screen developer, and |
| `MockGroundUi.Attachments.cs` | 107 | MockGroundUi | MockGroundUi's attachments: a sample rifle with sights, muzzle and rail parts, and moves between slots through the real AttachmentTransfer rules |
| `MockGroundUi.Radials.cs` | 161 | MockGroundUi [SampleDoor] | The sample radials (GROUND_SYSTEMS.md §4): the right mouse opens the held plan's or hammer's; E held on the HUD opens |
| `MockGroundUi.World.cs` | 236 | MockGroundUi | The mock's G7 world (GROUND.md §6.8): the first world's real map round the landing site, a few markers, a team with an |
| `MockGroundUi.cs` | 729 | MockGroundUi | A stand-in for the client API (GROUND.md §8): a survivor with a believable inventory, recipes, a crafting queue, |
| `RadialTypes.cs` | 145 | RadialIcon, RadialOption, IRadialSource, RadialInput, RadialBinding, RadialMenu | The radial menu's contract (GROUND_SYSTEMS.md §4, GROUND.md §8): one control, drawn in RustMVP's look, on any source |
| `WorldUiTypes.cs` | 74 | MapView, MapMarkerKind, MapMarkerView, TechNodeState, TechNodeView, TechTreeView, TeamMemberInfo, TeamInfo, TeamInviteInfo | The values the map, tech tree, team and machine screens show (GROUND.md §6.8, §8, G7). Plain C#, no Godot types: the |

### game/scripts/UI/Viewer (4 files, 1,091 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `UiBackdrop.cs` | 65 | UiBackdrop | A plain scene behind the interface viewer, so the panels' translucency and blur can be judged against something |
| `UiViewer.CraftingHud.cs` | 122 | UiViewer | --view ui --crafting-hud-check: drives the HUD crafting card on the mock model (queue, progress, owned count, cancel) and quits with a pass or fail code |
| `UiViewer.Crosshair.cs` | 280 | UiViewer [CrosshairPixels] | --view ui --crosshair-check: renders the HUD's crosshair at several sizes and scales, comparing screenshots (--crosshair-shots), preferences kept in memory |
| `UiViewer.cs` | 624 | UiViewer | The interface viewer (--view ui, GROUND.md §8): the ground interface on MockGroundUi over a |

## tools

Tools: the bots and the load test, the message generator, the generator's reports, the catalogue builder, the map export.

### tools/Hollow.Bots (13 files, 3,647 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AssemblyInfo.cs` | 4 |  | Makes Hollow.Bots' internals visible to Hollow.Tests (InternalsVisibleTo) |
| `BotBrain.cs` | 120 | BotScript, BotBrain |  |
| `BotGround.cs` | 155 | BotGround [HomeState] | The bots' ground on a whole world (docs/WHOLE_WORLD.md §13): the landing site, each bot's home (where it arrived), |
| `BotSwarm.cs` | 237 | BotSwarm | Many headless clients in one process, on one thread (GROUND.md §4.3): each a full GameClient with |
| `BotWorlds.cs` | 135 | BotWorlds, BotWorldContext | One bot session owns its immutable world store and the mutable replicas shared by its clients. |
| `BuildBrain.cs` | 560 | BuildBrain [Stage] | A bot that builds a small base (GROUND.md §5.3) through the client's building API, as a player would: it walks to a |
| `FightBrain.cs` | 316 | FightBrain | A bot that fights with guns (GROUND.md §6.3, the G4 load test) through the same client API as a player: it keeps its |
| `FlyBrain.cs` | 162 | FlyBrain | A bot that flies (G6, GROUND.md §6.7): it walks to the hatch of the ship the server gave it (ShipOnJoin), boards |
| `Interacting.cs` | 37 | Interacting | The interact key, as a bot presses it (GROUND_SYSTEMS.md §5): a tap does a target's primary option from where the bot's |
| `Program.cs` | 466 | Program | Headless clients (GROUND.md §4.3). |
| `RaidBrain.cs` | 316 | RaidBrain | A bot that raids (GROUND.md §5.5, G5) through the same client API as a player: its kit (charges, bag charges, |
| `SurvivalBrain.cs` | 380 | SurvivalBrain [Goal] | A bot that plays the survival game (GROUND.md §6) through the same client API as a player: it walks to trees and |
| `WorldBrain.cs` | 759 | WorldBrain [Goal] | A bot that lives off the world's loot (GROUND.md §6.8, G7) through the same client API as a player: it walks to the |

### tools/Hollow.CatalogBuilder (1 file, 948 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Program.cs` | 948 | Program [Row] | Converts a star catalogue (CSV) into Hollow Universe's compact catalogue (GALAXY.md §8). |

### tools/Hollow.GeneratorReport (10 files, 4,076 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Calibrate.cs` | 80 | Calibrate | The class table against the real neighbourhood: per level, inside the distance where the catalogue is complete |
| `EarthCandidatesReport.cs` | 181 | EarthCandidatesReport [Candidate] | earth candidates <folder> [samples] [width]: compares six layout variants of the Earth profile (dry land, forest, mountains, biomes, connectivity) offline and maps each. |
| `EarthSurvey.cs` | 749 | EarthSurvey [Landform] | earth survey, transect, wall, formations and cliffs-bench: T1 landform survey of Earth with its map (docs/TERRAIN_SCENERY_PLAN.md), a ground transect, a wall profile, and the cliff generator's cost. |
| `FormationPackageReport.cs` | 55 | FormationPackageReport | formation-package <dir> <receipt> [--chart-y-up <patch>]: loads a formation package offline, prints its definitions and optionally times a surface chart qualification. |
| `Maps.cs` | 408 | Maps [Layer] | Galaxy-wide maps of what the generator makes, one tile per level (GALAXY.md §7.1): generated stars seen face-on |
| `Png.cs` | 93 | Png | A minimal PNG writer (8-bit RGB, Paeth-filtered rows): enough for the generator's maps. |
| `Program.cs` | 487 | Program | Measurements of the generator that are too slow or too visual for the test suite: generation speed, and later |
| `Shells.cs` | 170 | Shells | Real plus generated stars per level in shells around Sol, against what the density model expects |
| `SurfaceReport.cs` | 564 | SurfaceReport | The first world's surface (GROUND.md §3, §4.6): maps, speed, the landing-site search and the times of day used for |
| `WalkingReport.cs` | 1289 | WalkingReport [Spot, Shot] | The walking-scale ground (WALKING_GROUND.md §15): equal-area maps, globe views, close-ups with the scatter, slope maps, places for close-ups and for the game's ground-level shots, the ground's materials and plants at a point, the stand-in beside it, speed and numbers |

### tools/Hollow.MapExport (4 files, 1,404 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `GalaxyExport.cs` | 522 | GalaxyExport | galaxy.js: a galaxy-wide star sample drawn from the density model and the class table, a face-on glow |
| `LocalExport.cs` | 279 | LocalSystem, LocalData, LocalExport | One system of the local bubble, with its generated star system. |
| `MockExport.cs` | 419 | MockExport | mock.js: a MOCK gameplay layer (stations, bases, danger regions, resources, the player's ship). None of this |
| `Program.cs` | 184 | Program, Js, StarColours | Exports read-only snapshots of the real generator for the galaxy-map UX prototypes in |

### tools/Hollow.NetGen (3 files, 797 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `NetGenerator.Emit.cs` | 324 | NetGenerator |  |
| `NetGenerator.cs` | 416 | NetGenException, NetGenerator [StructKind, FieldKind, Field, StructDef] | A mistake in the message definitions, with the line it is on. |
| `Program.cs` | 57 | Program | Regenerates src/Hollow.Net/Protocol/Messages.g.cs from Messages.cs (ARCHITECTURE.md §11.2). |

## tests/Hollow.Tests

The tests, one file per area (the runner needs no packages).

### tests/Hollow.Tests (177 files, 43,720 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AdminBuildTests.cs` | 385 | AdminBuildTests | Admin construction and crafting: privileges, prediction, resource accounting and save safety. |
| `AdminCameraTests.cs` | 135 | AdminCameraTests | The admin self camera: eligibility, right drag only turns the orbit, held buttons stay captured, follow and zoom limits, and the thirdperson command. |
| `AdminInputTests.cs` | 91 | AdminInputTests | Noclip speed predicted exactly and bounded, flight speed granting nothing to ordinary players, and console binding of chords and server commands. |
| `AdmireBindingTests.cs` | 62 | AdmireBindingTests | The admire shortcut: N by default as a local non-admin command, merged with old saves, unbound and remapped through a restart. |
| `AdmirePresentationTests.cs` | 77 | AdmirePresentationTests | Every held item has a complete cosmetic admire curve that starts from and returns to its idle pose. |
| `AnimalNetTests.cs` | 191 | AnimalNetTests |  |
| `AnimalTests.cs` | 552 | AnimalRig, AnimalTests | Animals in a GameRig: the grid built on the test's thread, herds placed by hand. |
| `ArchitectureTests.cs` | 243 | ArchitectureTests |  |
| `AttachmentPresentationTests.cs` | 173 | AttachmentPresentationTests | Attachments seen by others: a held optic or light replicates through attach, detach, holster and death; every optic model has a real lens and a clear eye at each LOD. |
| `AttachmentTransferTests.cs` | 153 | AttachmentTransferTests | Moving gun attachments between sockets and containers: swaps and detaches keep the weapon whole, refusals are atomic, storage rights, old saves, intents rolled back. |
| `AttackLifecycleTests.cs` | 130 | AttackLifecycleTests | The AttackLedger's lifecycle: a reconnect starts a new identity space, one presentation per attempt, cancelled attempts stay dead, bounded history, cost and wire size. |
| `AttackMeleeTests.cs` | 386 | AttackMeleeTests [Rig] | Melee attempts through LocalMelee and the zone: reconciliation by identity, duplicate and cancelled requests, cadence, equip cooldown, backlog budgets, a lossy network. |
| `AttackProjectileTests.cs` | 622 | AttackProjectileTests [ProjectileClient] | Gun and rocket attempts on the client: malformed or early endpoints, bounded history, reordered shots and refusals, historical movement for late shots, settling under bad networks. |
| `AttackStationTests.cs` | 422 | AttackStationTests [ClientRig] | Ship attack stations: predicted rounds from separate muzzles, ammunition reservations kept or corrected, refusals, wrong seat, cadence and spam over a lossy link. |
| `AudioLoopBudgetTests.cs` | 118 | AudioLoopBudgetTests | SoundVoiceScheduler's loop budget: nearby sources win, event and family limits hold, silent layers stay virtual, envelopes advance, the budget data is bounded. |
| `AudioRepairTests.cs` | 186 | AudioRepairTests [RollingAudioSurface, LakeSurface] | Sound fixes: dedicated variant sets per action, footsteps by relative velocity on planets and decks, none in the air, real floor and water contacts, one landing sound per jump. |
| `BatteryTests.cs` | 680 | BatteryTests | IW3 (IRON_WAKE.md §6): the aim held in the world, alternation and the loader's table, every refusal, a strike, a ripple, prediction, a restart. |
| `BiomePartitionTests.cs` | 82 | BiomePartitionTests | The walking ground's biome weights: ice and alpine keep their intended shares across climate and height boundaries, and a warm climate keeps its ordinary mixture. |
| `BotEnvironmentLifetimeTests.cs` | 151 | BotEnvironmentLifetimeTests [DisposalProbe] | Bot world contexts: shared per descriptor and session, native users retired before the world, per-client homes and landing sites, no rooting of disposed clients. |
| `BowPresentationTests.cs` | 81 | BowPresentationTests | The bow over a loopback: a draw and an accepted release reach the owner and an observer, with the arrow launched in the aimed zone-space direction. |
| `BuildingFitTests.cs` | 109 | BuildingFitTests | The building's drawn meshes against their slots (GROUND.md §6.3, Fit), read from the GLBs without Godot: every level of |
| `BuildingNetTests.cs` | 385 | BuildingNetTests | Building over the network (GROUND.md §6.3, phase 2): intents, replication, streaming and locks. |
| `BuildingPersistenceTests.cs` | 153 | BuildingPersistenceTests | The building in storage (GROUND.md §4.7, §6.3, phase 2): a base survives a restart, and decay catches up on load. |
| `BuildingTests.cs` | 786 | BuildingTests [Alone, CliffSurface, RampSurface] | GROUND.md §6.3: the building rules, engine-free. |
| `CharacterAssetTests.cs` | 103 | CharacterAssetTests |  |
| `ClanBaseTests.cs` | 209 | ClanBaseTests | The admin clan-3x3 base blueprint: supported floors, protected peeks and doors at every grade, broken blueprints and bad sites refused cleanly, the command over the network and a restart. |
| `ClientEnvironmentLifetimeTests.cs` | 215 | ClientEnvironmentLifetimeTests [ReaderSurface] | The client's world lifetime: Welcome retires maps and physics before replacing the world, unknown kinds refused, failing subscribers, map pixel readers pinned until cancelled. |
| `ClientTransportTests.cs` | 95 | ClientTransportTests | Transport stays alive through a graphical stall without running application callbacks on its workers. |
| `CliffFormationTests.cs` | 250 | CliffFormationTests [EscarpmentSurface, SlopingSurface] | CliffPlacements: deterministic, bounded formations fitted to escarpments and slopes, one material family per face, shared collision transforms, no cliffs on flat or submerged ground. |
| `CliffSupportQualificationTests.cs` | 353 | CliffSupportQualificationTests | Cliff support qualification and the FormationCatalog: exact patch and contact identity, boundary winding, LOD mappings and deviation, malformed receipts, exact-revision lookups, budgets. |
| `ClimbingTests.cs` | 44 | ClimbingTests [Ladder] | The climbing motor on a test ladder: ascends at the set speed, holds without drift, descends, and a jump dismounts away from the ladder. |
| `CompoundGateLockTests.cs` | 81 | CompoundGateLockTests | Locks on the compound gate: a code lock through placement and shared interactions, a key lock sharing the allocator's keys and the gate's moving socket. |
| `CompoundLockIntegrationTests.cs` | 109 | CompoundLockIntegrationTests | Compound locks and wall supports through the thing codec (saved format version 9), and lock changes reaching existing clients through ordinary intents. |
| `ConsoleTests.cs` | 333 | ConsoleTests | The F1 console (GROUND.md §8.2): its words, completion, rights, commands over the network, and cheats. |
| `ContactResponseTests.cs` | 114 | ContactResponseTests | Contact response data: every physical surface resolves to complete audio and effects, new materials need no code, reload cues, contact history and accepted-action cues. |
| `ContentTests.cs` | 144 | ContentFixture, ContentTests | The ground game's data (GROUND.md §7), loaded once for every test that needs it. |
| `CoreTests.cs` | 112 | CoreTests |  |
| `CorpseIntegrationTests.cs` | 229 | CorpseIntegrationTests | Corpses over the network and saves: late-join phase and look, empty and god deaths, support from foundations and boxes, harvest through shared damage, loot kept, aboard a ship. |
| `CorpseLifecycleTests.cs` | 71 | CorpseLifecycleTests | Corpse lifecycle: converts once at its authoritative time, tool speed cannot duplicate yield, and a restart keeps the remaining time and partial yield. |
| `DamageTests.cs` | 153 | DamageTests | The damage pipeline's Destroyed event (GROUND.md §6.4.2), in the zone and over the network. |
| `DefenseTests.cs` | 108 | DefenseTests | Defense mounts in a GameRig: an installed gun needs power, ammunition, rights and sight, then uses ordinary damage; a SAM spends its rocket on a hostile ship. |
| `DeployableRepairTests.cs` | 90 | DeployableRepairTests | Deployable repair with the hammer: rights, raid delay and all materials; utility state through the real thing codec, and invalid energy rejected. |
| `DevTests.cs` | 63 | DevTests | The F2 developer panel's server side (GROUND.md §8.5), over the network. |
| `DeviceAuthorizationTests.cs` | 162 | DeviceAuthorizationTests | Device authorisation: inherited cupboard membership is live and clearing overrides it, manual mode persists, overlapping claims, deny wins, pilots, cupboard loss. |
| `DropAllTests.cs` | 217 | DropAllTests | The inventory's drop-all intent: changed stacks reject the whole drop, a backpack drops into the existing bag, a full one uses one intent, stale prediction rolls back. |
| `DryFireAudioTests.cs` | 147 | DryFireAudioTests [GunAudioRig] | Dry fire: an empty gun clicks once per press without firing or sending intents, and respects equip, reload, cadence and character gates. |
| `EarthLayoutVariantTests.cs` | 32 | EarthLayoutVariantTests | WalkingWorld.LayoutSeed: variant zero keeps the established seed, other variants give repeatable different land under the surface cap without changing the save revision. |
| `EarthTerrainTests.cs` | 178 | EarthTerrainTests | Earth's playable land budget and continent shape, measured from the same surface used by the game. |
| `EffectBudgetTests.cs` | 86 | EffectBudgetTests | The effects' budget: mixed particle sources cannot overshoot in a frame, loops respect separate resources and caps, virtual loops recover, budget data is finite. |
| `EffectsCatalogueTests.cs` | 105 | EffectsCatalogueTests | The effects catalogue's items (GROUND.md §9.3, IW4), checked without Godot: the views play an item's effects by role from |
| `EnvironmentDescriptorTests.cs` | 329 | EnvironmentDescriptorTests | PlanetEnvironmentDescriptor: resolution builds no terrain, canonical identity, no aliasing between profiles, stable snapshots, legacy fields kept, invalid profiles rejected. |
| `EnvironmentDetailTests.cs` | 249 | EnvironmentDetailTests | Earth's authored detail: ground cover with bounded LODs, monuments with local art and shared collision and loot, all four ores, cliff placements, grass and water cover. |
| `EnvironmentHandshakeTests.cs` | 335 | EnvironmentHandshakeTests [ServerFixture, RawPeer] | The environment handshake: WorldOffer and Welcome rebuild legacy descriptors, refuse mismatched identity, bound wire messages, expire pending offers, and publish the descriptor once. |
| `EnvironmentRouteTests.cs` | 95 | EnvironmentRouteTests | EarthEnvironmentRoute: one connected six-stop walking route on Earth (beach, woodland, clearing, water, cliff, monument), dry, graded, clear of solids; refused on legacy worlds. |
| `EventTests.cs` | 34 | EventTests |  |
| `FireTests.cs` | 494 | FireRig, FireTests | Rockets and fires in a GameRig (IW4, IRON_WAKE.md §7, GROUND.md §6.6). |
| `FittedAttachmentTests.cs` | 116 | FittedAttachmentTests | Window attachments fitted to a building piece: shared placement, repair, pickup and persistence, and collision that keeps the authored shooting openings. |
| `FloraAppearanceTests.cs` | 609 | FloraAppearanceTests [PackageFixture] | The flora appearance package loader: seven RGBA mip-chain images, manifest and metadata validation, size limits, authenticated payloads, hosted Earth seed lookup, catalogue snapshots. |
| `FootstepShipContactTests.cs` | 164 | FootstepShipContactTests | Footstep contacts on a parked Tin Finch and on a replicated box aboard: actual client replicas and collision bodies choose the surface across world and ship frames. |
| `FootwearAudioTests.cs` | 145 | FootwearAudioTests | Footwear sounds: shoes follow equip and unequip for local and remote players, new profiles need no item switches, fallbacks stay audible, the registry rejects bad definitions. |
| `FormationAppearanceDataTests.cs` | 237 | FormationAppearanceDataTests [Fixture] | Formation appearance masks: bound to every primitive, immutable copies, refused on bad pins, aliased files, bad weights, layouts or self-granted permissions. |
| `FormationCacheLifetimeTests.cs` | 246 | FormationCacheLifetimeTests [FlatSurface, ControlledFlatSurface, SeamSlope] | The formation cache's bounded budget: recent entries survive eviction, readers coalesce, failed builds retry, arrays and queries survive a cube-face seam. |
| `FormationContactLoopPackageTests.cs` | 169 | FormationContactLoopPackageTests [LoopPackageFixture] | Formation packages' optional contact-loop bindings: loaded and authenticated, refused when missing or malformed, covered by the receipt digest, explicit edges. |
| `FormationContactLoopTests.cs` | 181 | FormationContactLoopTests | Contact loops: ordered indices copied, closure bounded, exact physical and per-LOD closing edges, bindings per LOD sorted by key, explicit seams, accounting. |
| `FormationFieldIdentityTests.cs` | 278 | FormationFieldIdentityTests | The formation field's canonical identity bytes: prior bytes unchanged when extensions are absent, loops, seams and terrain lattice bound to the right scope, bad contracts refused. |
| `FormationFieldTests.cs` | 310 | FormationFieldTests [Fixture, FlatTerrain] | The formation field: only sealed, stable slots published, whole-formation policy, canonical triangles and guard pins across face seams, budgets and cancellation, physical queries. |
| `FormationGenerationIdentityTests.cs` | 444 | FormationGenerationIdentityTests [FixtureOptions, ProfileOptions, SettingsOptions] | The formation generation identity hash: what changes it (physical inputs, recipe, substrate, planner settings, algorithm revisions) and what leaves it alone; bad inputs refused. |
| `FormationGltfDecoderTests.cs` | 383 | FormationGltfDecoderTests [Fixture] | The formation glTF decoder: float positions, triangle order and winding kept; scenes, skins, morphs, bad accessors, duplicate JSON keys, budgets and external URIs refused. |
| `FormationPackageLoaderTests.cs` | 302 | FormationPackageLoaderTests [PackageFixture] | The formation package loader: authenticates source, physical and LOD buffers against the receipt, refuses changed or unlisted files and recipes, qualifies geometry, JSON limits. |
| `FormationPlannerTests.cs` | 625 | FormationPlannerTests [SyntheticTerrain, LocalTerraces] | The formation planner on synthetic terrain: survey of shared seam vertices, fitting chains, coverage, overlaps, regional cores and halos, cross-seam contours, work limits. |
| `FormationSubstrateTests.cs` | 245 | FormationSubstrateTests | The formation substrate: material fractions and habitat units, contact influence, soil and plant permissions, climate and water gates, order-independent reduction, typed capacity failures. |
| `FormationSupportTests.cs` | 448 | FormationSupportTests [Assert] | Formation support queries: shared catalogue storage, disks across triangle edges and holes, shelves, backfaces, canonical ties, immutable inputs, budgets, cancellation, DTO validation. |
| `FormationSurfaceChartTests.cs` | 394 | FormationSurfaceChartTests | Surface charts of a formation: a non-planar crown qualified, holes and split vertex fans kept as exclusions, bad projections refused, queries in world metres, immutable under concurrent reads. |
| `FormationTerrainSealTests.cs` | 231 | FormationTerrainSealTests | The continuous terrain seal under a formation: shared facets, gaps between vertices and midpoints, closing edge, conservative L1 burial, capacity and cancellation fail closed. |
| `FormationTopologyTests.cs` | 266 | FormationTopologyTests | Cube-sphere topology for formations: reciprocal face-edge transitions, tile neighbours at the finest level, vertex keys over corners, ordering independent of query order and threads. |
| `FormationVisualDataTests.cs` | 581 | FormationVisualDataTests [Fixture] | Formation visual data: ordered primitives, exact streams and winding, PBR defaults, no invented tangents or UVs, hash and truncation refusals, budgets before publication, cancellation. |
| `FormationWorldSelectionTests.cs` | 134 | FormationWorldSelectionTests | Explicit formation selection: physical inputs bound without reseeding the legacy world, qualification-only changes keep the save identity, unknown or oversize inputs refused. |
| `FoundationTests.cs` | 499 | FoundationTests | The ground game's shared foundations (GROUND_SYSTEMS.md §3, package P0), engine-free. |
| `GalaxyTests.cs` | 1199 | GalaxyTests |  |
| `GameScaleTests.cs` | 131 | GameScaleTests | The game's world scale: Earth's movement prediction on the scaled walking terrain, every planet within the surface-area budget, bad scales refused, generated physics unchanged. |
| `GatheringTests.cs` | 392 | GatheringTests |  |
| `GeologyPortTests.cs` | 187 | GeologyPortTests | The geology port (TERRAIN_SCENERY_PLAN.md T2): cliff collision receipts, the scanned rock catalogue against real mesh data, cliff formations and chains streaming across cube-face seams. |
| `GibTests.cs` | 204 | GibTests | Gibs (GROUND.md §9.4): everything that can be destroyed has pieces to break into, declared beside its model in the |
| `Golden.cs` | 66 | Golden | Golden hashes over generated objects. Hashes every stored field (public or private, including the fields behind |
| `GroundCollisionPlanTests.cs` | 131 | GroundCollisionPlanTests [CountingSurface] | Ground collision plans: layouts for small and playable radii, deterministic areas across faces, exact seam and corner counts, composite preparation, capacity and cancellation. |
| `GroundCoverRootMetadataTests.cs` | 151 | GroundCoverRootMetadataTests [Fixture] | Ground-cover root metadata: six whole plants with an explicit growth axis, receipt and catalogue bindings required, unknown fields and LOD root mismatches refused, imported identity. |
| `GroundLayerCatalogueTests.cs` | 82 | GroundLayerCatalogueTests | The ground texture catalogue (docs/WALKING_GROUND.md §8, layers.json): sixteen materials first, valid alternatives and selectors, every texture's three files and imports present. |
| `GroundStreamingBudgetTests.cs` | 213 | GroundStreamingBudgetTests [NativeProbe] | The ground streaming ledger: reservations follow limits and survive release, atomic admission, queue and takeover limits, native disposal failures, parked bodies, shared owners. |
| `GunNetTests.cs` | 498 | GunNetTests |  |
| `GunTests.cs` | 431 | Guns, GunTests | Guns in a GameRig: a loaded gun in hand, shots queued for the coming tick as the server would. |
| `HeldActionTimingTests.cs` | 319 | HeldActionTimingTests | Held-item action timing: presentation and authority share the melee contact tick, retimed contact, windup commits once, reach misses, reload cancelled on a switch, hit markers, consumed items. |
| `HeldPresentationTests.cs` | 199 | HeldPresentationTests | Held items' first- and third-person presentation: explicit art per model, continuous action timelines, skeletal bindings on both arms, separate magazine meshes. |
| `HeldTests.cs` | 403 | HeldTests, Loopback [TestActor] | Held items, item information and inventory actions (GROUND_SYSTEMS.md §7, package P3), engine-free. |
| `InteractionTests.cs` | 555 | InteractionTests [Rogue] | The interaction system (GROUND_SYSTEMS.md §5): what the E key does, one rule a test. |
| `IronWakeBuildTests.cs` | 408 | IronWakeBuildTests | Building aboard the Iron Wake (IRON_WAKE.md §8.1, IW5): placing on its decks, riding it, the refusals, a furnace in flight, a bag aboard, a grenade, a wreck, a restart. |
| `IronWakeNetTests.cs` | 196 | IronWakeNetTests [ShipFacts] | The Iron Wake over the network (IRON_WAKE.md §4.8, §4.11): prediction aboard, and sleepers aboard across a restart. |
| `IronWakeTests.cs` | 635 | IronWakeRig, IronWakeTests | The Iron Wake on the first world's stand-in ground (IRON_WAKE.md §4, IW1): one parked near the landing site, its nose |
| `ItemTests.cs` | 161 | ItemTests |  |
| `LoadingTests.cs` | 298 | LoadingTests [Clock] | The loading pipeline's rules (GROUND.md §4.3.1), without Godot: the game's plan reads and names every stage; the bar |
| `LocalFlightTests.cs` | 138 | LocalFlightTests | Local flight (DESIGN.md TRAVEL-01): continuous climb to orbital altitude and back, prediction across it, the limit allowing return, legacy orbit release not teleporting. |
| `LocalShadowTests.cs` | 71 | LocalShadowTests | The local shadow budget: priority, capacity and influence distance, nearby lights stable until another is clearly closer, stable ties, invalid candidates, many lights bounded. |
| `MapInteractionTests.cs` | 47 | MapInteractionTests | The map: a held drag keeps its ground point across frame updates, and the map cache key includes the walking terrain's revision. |
| `MapTests.cs` | 231 | MapTests | The map of a whole world (docs/WHOLE_WORLD.md §11): projection round trip and far side hidden, camera drag and zoom, grid cell names, the picture of the whole world. |
| `MeleeSwingCueTests.cs` | 216 | MeleeSwingCueTests [RemoteCueRig] | The melee swish cue's timing: waits for contact preparation, never plays after contact or a clock rewind, cancellation, remote swishes on the server clock behind interpolated snapshots. |
| `ModelCatalogueTests.cs` | 393 | ModelCatalogueTests | The ground game's model catalogue (GROUND.md §9), checked without Godot: every model file is a GLB with its LOD |
| `MovementTests.cs` | 307 | LevelSurface, Lap, MovementRig, MovementTests | A test surface: a smooth sphere whose ground sits at a fixed height, with an optional sea. |
| `MultiplayerTests.cs` | 472 | LoopbackRig, MultiplayerTests | An in-process server and clients on loopback: on a background thread and the wall clock, or (`simulated: true`) stepped by the rig itself on a simulated clock with a `SimulatedRelay` for a bad line, and `WaitUntilSettled` (clock and ground) before a test counts anything. |
| `NaturalAimSegmentQueryTests.cs` | 154 | NaturalAimSegmentQueryTests [SyntheticNature, FlatSurface] | Aim queries along a segment through natural objects: interaction and attack aim match exhaustive shapes for every content kind, tall endpoints, canonical ties, a bounded 2 km corridor. |
| `NaturalCacheLifetimeTests.cs` | 220 | NaturalCacheLifetimeTests [FlatField, ControlledField] | The natural-object scatter cache: empty tiles share the bounded LRU budget, readers share one generation, failed builds retry, eviction keeps ids, transforms and lookup order. |
| `NaturalCollisionPlanTests.cs` | 245 | NaturalCollisionPlanTests [Field] | Natural-object collision plans: bounded scatter keeps legacy values and order, admission before sampling, no partial results, depletion snapshots, presence clock, empty plans keep capacity. |
| `NaturalRenderTests.cs` | 101 | NaturalRenderTests | Natural objects drawn: health and weak-spot updates keep cues without rebuilding plants, stage changes invalidate only their model, depleted overrides, falling trees hidden. |
| `NaturalSegmentQueryTests.cs` | 257 | NaturalSegmentQueryTests [SyntheticNature, FlatSurface] | Segment queries through natural objects: candidates match an exhaustive search across faces, relief and frames, tall trees and offset rocks hittable, canonical order, long segments refused. |
| `NavigationLifetimeTests.cs` | 306 | NavigationLifetimeTests [TestSurface, ControlledNature] | Lifetimes of navigation and natural-body listeners: stop drains blocked readers, failures abandon nobody, animal system disposal retires its nav world, disposed listeners not retained. |
| `NetTests.cs` | 382 | NetTests [Arrivals] |  |
| `PersistenceTests.cs` | 65 | TempDatabase, PersistenceTests | A database file in the temp folder, deleted afterwards. |
| `PhysicsLifetimeTests.cs` | 218 | PhysicsLifetimeTests [ControlledSurface] | Native physics freed on disposal (T0.3): worlds and ship spaces return their Jolt memory, disposal drains a started tile, a failed tile abandons no worker or resource. |
| `PhysicsTests.cs` | 126 | PhysicsTests |  |
| `PlacementTests.cs` | 461 | PlacementTests | The placement system (GROUND_SYSTEMS.md §6): one rule per placeable in data; its solvers, rotations, checks and kinds |
| `PlanetBodyResolutionTests.cs` | 170 | PlanetBodyResolutionTests | PlanetBodies and EnvironmentDescriptors: metadata without terrain, unknown addresses and bad radii refused, seeds independent, one walking world per lease, no leaked samples |
| `PlanetGroundTests.cs` | 214 | PlanetGroundTests | The first world's ground (GROUND.md §3, §6.1): the real planet's own scatter as natural objects, and the whole world |
| `PlanetSurfaceTests.cs` | 254 | PlanetSurfaceTests | The first world's real surface (GROUND.md §4.6): determinism, tiles, collision, speed. |
| `PlayableGalaxyTests.cs` | 75 | PlayableGalaxyTests | PlayableGalaxy: the 1,000-star budget counts companions, the registry is stable with or without the catalogue, and search and lookup stay inside it |
| `PowerTests.cs` | 201 | PowerTests | Electrical graph: port span and cable routes, one cable per output, cycles and bad directions refused, storage conserving energy, restore sanitising links |
| `PresentationPoseTests.cs` | 83 | PresentationPoseTests | TargetModule presentation poses: a renderer's pose is lazy and never alters the gameplay pose, replacement and part ownership, and disposal |
| `ProgressionTests.cs` | 97 | ProgressionTests | The design's fresh start and separate technology, station and crafting-speed rules. |
| `RaidAlertTests.cs` | 222 | RigPresence, RaidAlertTests | Who is "online" for a notification service in a GameRig: those awake; what they were sent; what was pushed. |
| `RaidNetTests.cs` | 209 | RaidNetTests |  |
| `RaidTests.cs` | 440 | Raids, RaidTests | Raiding in a GameRig: huts built through the building rules, explosives through the zone (GROUND.md §6.6). |
| `RenameTests.cs` | 107 | RenameTests | Naming respawn points (bed, sleeping bag): Unicode normalisation, owner-in-reach only, saved labels and their replication to observers, map and respawn list |
| `RoofPanelTests.cs` | 172 | RoofPanelTests | Roof panels: open sides with solid decks, placing on adjacent roofs through the existing rules, legacy stacking poses fixed, installed deck hulls matching simulation. |
| `SalvagedAxeTests.cs` | 154 | SalvagedAxeTests | The salvaged axe: fells trees faster without more wood, and its loot blueprint unlocks crafting at workbench two, through the shared systems. |
| `SeatTests.cs` | 167 | SeatTests | Seats, parts and doors (IRON_WAKE.md §5, IW2): who sits where, every way a seat is freed, and ships at rest. |
| `SelectedGroundCollisionTests.cs` | 303 | SelectedGroundCollisionTests [Fixture, Terrain] | Selected-lattice terrain collision: sealed raised faces, deterministic order, cube-seam footprints, monuments, and refusal of incomplete or cancelled queries |
| `SelectedGroundTraceTests.cs` | 532 | SelectedGroundTraceTests [Terrain] | Selected ground trace queries: canonical triangles and diagonals, cube seams, nearest formation, pins, monuments, budgets, cancellation and agreement with an exact oracle. |
| `SelectedPhysicsGroundTests.cs` | 443 | SelectedPhysicsGroundTests [Listener, Fixture, PlannedSource] | Selected ground in Jolt: retained entries and reservations, native cleanup after failed or cancelled setup, shared allowances, seams and corners across radii. |
| `SelectedWalkingWorldTests.cs` | 349 | SelectedWalkingWorldTests [OwnedSurface] | Selected walking world construction: input identity, wrong identity refused, default store disabled, one coherent bundle, undercharged or spoofed output refused, cancellation. |
| `ServerTerrainIdentityTests.cs` | 60 | ServerTerrainIdentityTests | The server's persistence opens only on the acquired lease's descriptor: wrong body refused before a file is made, and the stamped identity is read back |
| `ShipAttitudeNetTests.cs` | 137 | ShipAttitudeNetTests | Ship attitude hold over the network: takeover, prediction and rewind stay exact, late snapshots cannot undo ownership, two clients see hold and helm takeover |
| `ShipAttitudePersistenceTests.cs` | 162 | ShipAttitudePersistenceTests | Ship attitude hold in ShipCodec: a save mid-braking keeps its target, legacy versions start inactive, a malformed trailer injects nothing, sleepers keep it |
| `ShipAttitudeTests.cs` | 328 | ShipAttitudeTests | Ship attitude hold: return to release after spin, shortest arc, bounded torque and fuel, target carried with the planet, gates, takeover, landing and sleepers |
| `ShipFuelTests.cs` | 190 | ShipFuelTests | Ship fuel through the inventory: only fuel is taken within tank and carried limits, partial units kept, v5 and v4 restore, client moves match, last fraction |
| `ShipGameTests.cs` | 247 | ShipGameTests | Ships in the survival game (GROUND.md §6.7): built at the vehicle workbench, boarded, flown, left, locked, hurt. |
| `ShipNetTests.cs` | 175 | ShipNetTests | A pilot's controls over a flight: lift off, climb, turn, cruise, boost, flight assist off and on, strafe, roll, |
| `ShipPartsTests.cs` | 385 | ShipPartsTests | IW2 (IRON_WAKE.md §5): the Iron Wake's seats, parts, doors, its helm's list and its parts' health, engine-free and over |
| `ShipPersistenceTests.cs` | 103 | ShipPersistenceTests | Ships in storage (GROUND.md §4.7, §6.7). |
| `ShipRepairTests.cs` | 176 | ShipRepairTests | Ship flight fixes: leaving the seat in flight, cruise kept until engines off or fuel out, mouse steering across frame rates, Iron Wake turn rate, seat orbit |
| `ShipTestParts.cs` | 26 | ShipTestParts | A ship kind's parts and seats by what the tests call them (ships.json's data, IRON_WAKE.md §5.1). |
| `ShipTests.cs` | 293 | SlopeSurface, ShipRig, PipeExtensions, ShipTests | A test ground tilted about the north–south line through the landing site: it rises toward the east by |
| `ShipThrottleTests.cs` | 79 | ShipThrottleTests | The helm's throttle lever on the client: full stroke at any frame rate, a stop at neutral, hold on release, and sub-step frames accumulating into the wire value |
| `SimulatedRelay.cs` | 126 | SimulatedRelay [Datagram] | `ConditionedRelay` for a stepped test: the same latency, jitter and loss between one client and the server, on the clock its owner passes in and the owner's thread, seeded. |
| `SnapshotRepairTests.cs` | 92 | SnapshotRepairTests | Snapshot sizes: EncodedBits and DeltaBits of character and ship states match the bits written at every varint boundary, and pilot states fit the packet budget |
| `SoundCatalogueTests.cs` | 358 | SoundCatalogueTests [Wav] | The ground game's sounds (GROUND.md §4.9), checked without Godot: every file of the catalogue is there and imported |
| `StandInNaturalCacheLifetimeTests.cs` | 213 | StandInNaturalCacheLifetimeTests [FlatSurface, ControlledSurface] | StandInNaturalObjects tile cache: one bounded LRU for empty and full tiles, parallel requests coalesce, failed or evicted builds retry, queries stay stable |
| `StationAimTests.cs` | 158 | StationAimTests | Battery stations' aim: the predicted centre ray matches the server's at corpses and props, barrels converge on the camera target, close and blocked shots |
| `SurfaceContactTests.cs` | 244 | SurfaceContactTests | Surface contacts on hits: kept when the damage destroys the target, in the moving hull's frame, material identity on compounds, animals, wire format, bad payloads |
| `SurfaceStreamingTests.cs` | 45 | SurfaceStreamingTests | ScatterRequestSet: far scatter never blocks a full request across cube faces and levels, and concurrent callers are deduplicated without mixing modes |
| `SurfaceTests.cs` | 125 | SurfaceTests |  |
| `SurvivalNetTests.cs` | 300 | SurvivalNetTests |  |
| `SurvivalRepairTests.cs` | 124 | SurvivalRepairTests [LakeSurface] | Survival fixes: god mode against death and wounds, a wreck freeing its god-mode pilot, breath and drowning, inland water by eye depth, foundations against bites |
| `SurvivalTests.cs` | 620 | GameRig, Places, SurvivalTests [FixedClimate] | A survival zone on the first world with players placed by hand, stepped tick by tick. |
| `TargetHealthTests.cs` | 140 | TargetHealthTests [TestView] | Target health bars: a bounded query that never sees through healthy occluders, one vitality contract across world families, damage and repair replicated |
| `TerrainCollisionMarginTests.cs` | 370 | TerrainCollisionMarginTests [Points, Terrain] | Terrain collision margins: canonical triangles across every face seam and cube corner, two-metre margins, saturation on tiny worlds, capacity, cancellation, frozen pins |
| `TerrainLatticeTests.cs` | 226 | TerrainLatticeTests [PlainSurface, SelectedSurface] | TerrainLattice: canonical resolution and triangles, one position per seam point, render and collision sharing vertices, null opt-in keeping the legacy grid |
| `TerrainPinSetTests.cs` | 251 | TerrainPinSetTests [Terrain, Unpinned] | Terrain pin sets: incident cells at faces and corners, deterministic merging and conflicts, fine and morph geometry, millimetre float bound, limits, no pins |
| `TerrainSaveTests.cs` | 400 | TerrainSaveTests | Terrain identity in saves: cliff and layout revisions fence walking bodies, old saves kept or refused, canonical stamps fail closed, bodies and entity ids bound |
| `TestRunner.cs` | 153 | TestAttribute, TestFailure, Assert, Fixtures, Program | Marks a public static method as a test. |
| `UtilityNetworkTests.cs` | 216 | UtilityNetworkTests | Utilities over a loopback server: authorisation menus and live cupboards reach both clients, and cable routes replicate as deltas and survive a restart |
| `UtilityPersistenceTests.cs` | 99 | UtilityPersistenceTests | Utility records in the building codec: ports, routes and explicit authorisation round-trip, legacy migration keeps items, bounds and invalid routes refused |
| `WalkingGradedTerrainTests.cs` | 357 | WalkingGradedTerrainTests [PlannerTerrain] | Protected radial sites on the graded terrain (whole ray, chord distance, boundary, huge axes) and the walking adapter's raw fields on Earth, full and regional. |
| `WalkingWorldTests.cs` | 235 | WalkingWorldTests | The walking-scale ground (WALKING_GROUND.md): determinism and its golden hash, sea cover and heights, streams to the sea or a lake, no seams, scatter at the level by size, speed. |
| `WiringTests.cs` | 327 | WiringTests | Wiring tool: held capability, forged or remote ports, occlusion, rights at both ends, draft commit and cancellation, no double-occupied output, socket-based reach. |
| `WorldGenerationConsistencyTests.cs` | 243 | WorldGenerationConsistencyTests [SampleSnapshot] | World generation consistency: surface results and shared placement queries agree across worlds, query order and threads for layouts 0 and 5, with explicit shore relief. |
| `WorldNetTests.cs` | 298 | WorldNetTests | G7 World over the network and through a restart (GROUND.md §6.8): protocol 9, schema 6. |
| `WorldStoreTests.cs` | 546 | WorldStoreTests [FailingDisposalSurface, BlockedFactory] | WorldStore: coalesced builds, waiter and build limits, resident and scratch budgets, rollback on failure, cleanup errors, eviction and forks, retirement, independent stores. |
| `WorldTests.cs` | 516 | WorldTests | G7 World, part A (GROUND.md §6.8): loot, the recycler, tech trees and research, teams, the map's markers. |
| `WoundedTests.cs` | 107 | WoundedTests |  |
| `ZoneEnvironmentLifetimeTests.cs` | 310 | ZoneEnvironmentLifetimeTests [TrackingSystem, FailingSystem, ConstructionFailureSystem, ControlledSurface, Barriers] | Zone and server lease lifetimes: the environment is pinned until the last physics reader retires, failed construction or bootstrap returns the lease, borrowed surfaces stay borrowed |
