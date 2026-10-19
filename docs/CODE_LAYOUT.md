# The code, file by file (15 October 2026)

Generated from the repository: every C# file, the types it declares (nested ones in brackets), its length and its
own first line of explanation. HOW_IT_WORKS.md says how these run together; this is the map. Regenerate it after
a package with the script in the session that made it, or by hand: the shape is one table per folder.
608 files, 166,994 lines of C#.

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

### src/Hollow.Galaxy (15 files, 2,789 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `CatalogueCompleteness.cs` | 94 | CatalogueCompleteness | How much of the real sky the star catalogue holds, per level and distance from Sol (GALAXY.md §8.3): the |
| `CellGenerator.cs` | 271 | Streams, CellGenerator | Stream tags: every kind of generated object hashes its address with its own tag. |
| `CellKey.cs` | 238 | SectorGrid, CellKey, SystemAddress | The sector grid (GALAXY.md §5). |
| `DensityModel.cs` | 250 | DensityModel [Arm] | Number density of star systems (systems per ly³) in the Milky Way, as the sum of six components |
| `Galaxy.cs` | 257 | SearchResult, Galaxy | A search hit: a system, or a place (a sector or a named region, ) when the text names |
| `GalaxyFrame.cs` | 86 | GalaxyFrame | The galactic frame and the density model's frame (GALAXY.md §2). |
| `GalaxyStar.cs` | 47 | GalaxyStar | The primary star of one star system, as the galaxy knows it: enough to draw it on the map and to |
| `GeneratorVersion.cs` | 39 | GeneratorVersion | Generator versions, one per stage of the universe pipeline (ARCHITECTURE.md §8.4, GALAXY.md §14). Each version is |
| `Names.cs` | 268 | SectorNames, SystemNames | Sector names (GALAXY.md §9): two words from original syllable tables, exactly one name per sector, and |
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

### src/Hollow.Galaxy/Systems (7 files, 2,949 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `BodyPath.cs` | 49 | BodyPath | A body's permanent 32-bit path inside its system (ARCHITECTURE.md §7, GALAXY.md §10.1). With the system address |
| `Orbit.cs` | 113 | Orbit | Keplerian orbit around a parent body, in the system frame (reference plane x–z, +Y north). Angles in |
| `PlanetState.cs` | 227 | Composition, Gases, Volcanism, Tectonics, LiquidKind, SurfaceKind, Hazards, Biosphere, Material, PlanetState | Bulk composition of a body, as mass fractions that add up to 1 (GALAXY.md §10.3). |
| `SolSystem.cs` | 352 | SolSystem [Writer] | The real Solar System: planets with their J2000 orbital elements (ecliptic reference plane), the major moons, |
| `StarSystem.cs` | 267 | BodyKind, PlanetType, AtmosphereType, Body, StarSystem |  |
| `SystemGenerator.cs` | 1031 | SystemGenerator [Companion, Node, Builder, HostContext] | Generates a star system's stars, planets, moons, rings and belts from its primary star (GALAXY.md §10). A pure |
| `WorldBuilder.cs` | 910 | Formation, WorldInput, World, WorldBuilder [ClimateModel, Climate] | How a planet formed: a solid body (which may keep a gas envelope), an ice giant or a gas giant. |

## src/Hollow.Surface

Planets as surfaces: the cube-sphere, terrain, biomes, scatter, the sky, the zone frame.

### src/Hollow.Surface (17 files, 3,569 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Biomes.cs` | 402 | Biome, GroundMaterial, ScatterKind, ClimateSample, BiomeTable | Land biomes (GROUND.md §4.6), picked from the climate: annual mean temperature, moisture and height. |
| `CubeSphere.cs` | 263 | CubeSphere, TileKey | The cube-sphere of a planet surface (GROUND.md §4.6): six faces, each a square of face coordinates |
| `FirstWorld.cs` | 32 | FirstWorld | The ground game's first world (GROUND.md §3): Nolcas Peris CQ-SLY 7-0 B 3, a breathable Earth-like planet |
| `ISurface.cs` | 123 | ISurface, TriangleMesh, PooledTriangles | A body's ground as a pure function (GROUND.md §4.6): the server builds collision from it and the client draws it, |
| `NearTiles.cs` | 71 | NearTiles | TileKey.Around for searches repeated from about the same place every tick (a character's ground, an |
| `Noise.cs` | 232 | NoiseBasis, Noise | The lattice gradients a noise is made of (Perlin's, or the cube's corners for the walking-scale ground), and deterministic 3D gradient noise with its gradient, its octave sums, ridged and eroded noise |
| `PlanetSurface.cs` | 562 | SurfaceSample, VegetationSample, PlanetSurface, MonotoneCurve | What the height function knows about a point, besides its height. |
| `PlanetSurfaces.cs` | 51 | PlanetSurfaces [Entry] | The real surfaces of bodies, built once per process and shared (GROUND.md §4.6): the server's zone, the client's |
| `Plates.cs` | 204 | Plates | Tectonic plates of a surface (GROUND.md §4.6): plate centres on the sphere (a Voronoi partition), each plate's |
| `Scatter.cs` | 369 | ScatterItem, IScatterField, ScatterDensity, ScatterPlan, Scatter [Densities] | One natural object placed by the scatter (GROUND.md §4.6); what the scatter reads from a surface; a surface's scatter level and candidate cells (levels by size); the scatter itself |
| `SkyEphemeris.cs` | 222 | SkyEphemeris | Where everything in a planet's sky is at a universe time (GROUND.md §4.6): the planet's spin turns its body frame |
| `SphereSurface.cs` | 59 | SphereSurface | A plain surface: a sphere with gentle hills from value noise and no sea. Tests use it as a simple |
| `StandInWorld.cs` | 126 | StandInWorld | The stand-in world S3 plays on until S4 makes the real ground at walking scale (docs/WHOLE_WORLD.md §4): a small |
| `SurfaceGrid.cs` | 73 | SurfaceGrid | Ground triangles of a cube-sphere tile from any surface's heights (docs/WHOLE_WORLD.md §6.1): a grid of the face's |
| `TileMesh.cs` | 502 | TileGrid, TileMesh, GridHeights [Grid] | The ground of one tile as a grid of (cells + 1)² vertices (GROUND.md §4.6), which the client draws. On a whole world |
| `ZoneFrame.cs` | 88 | ZoneFrame | The frame a zone's floats are measured in (ARCHITECTURE.md §6, docs/WHOLE_WORLD.md §3.1). A zone uses |
| `WorldGrid.cs` | 140 | WorldGrid [Cell] | The map's grid on a whole world (docs/WHOLE_WORLD.md §11, decision 6): the six faces of the cube-sphere, named A to F, |

### src/Hollow.Surface/Walking (6 files, 1,891 lines)

The walking-scale ground of S4 ([WALKING_GROUND.md](WALKING_GROUND.md)).

| File | Lines | Types | What |
|---|---|---|---|
| `Drainage.cs` | 837 | WalkingLake, DrainageSegment, Drainage | Where water goes, worked out once: a priority flood on a cube-sphere grid of about 30 m, catchments, lakes in hollows with smooth shores, streams and dry valleys from source to the sea or a lake, canyons and gorges; `Shape` cuts them into a height |
| `ReliefProvinces.cs` | 133 | Relief, ReliefProvinces | Plains, hills, tablelands and mountains: provinces spread over the sphere, picked by shares and an uplift field, blended by normalised Gaussian weights |
| `RidgedNoise.cs` | 87 | RidgedNoise | Eroded ridged noise for the walking ground's mountains: every octave turned by its own rotation and crests rounded, so no ridgeline runs straight |
| `WalkingParameters.cs` | 204 | WalkingParameters | The walking-scale ground's numbers in one table |
| `WalkingWorld.cs` | 610 | WalkingSample, WalkingWorld | The ground of a small world at walking scale: an `ISurface` and `IScatterField` from landmasses, provinces, coasts and the drainage; climate at real-equivalent heights, biomes, woods, the landing site |
| `WalkingWorlds.cs` | 20 | WalkingWorlds | The walking-scale grounds of bodies, built once per process and shared |

## src/Hollow.Simulation

The rules of the ground game, engine-free: the zone host, its systems, entities, physics, content, events.

### src/Hollow.Simulation (7 files, 1,377 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Beaches.cs` | 124 | Beaches | Where players arrive and respawn "on the beach", as in Rust (GROUND.md §6.1, docs/WHOLE_WORLD.md decision 8): coast |
| `GroundProbe.cs` | 138 | GroundProbe | The surface's own ground (its heights, not the collision tiles' triangles), asked the same way by the zone and by a |
| `Outcome.cs` | 305 | OutcomeCode, Outcome, OutcomeCodes | The one answer vocabulary (GROUND_SYSTEMS.md §3.2): every action a player asks for is done, or refused with one of |
| `Registry.cs` | 141 | Registry | Handlers found by a list's index (GROUND_SYSTEMS.md §3.5): the list comes from the data (capabilities.json's keys) |
| `Zone.cs` | 396 | Character, Named, Zone | A character's components: its movement state, this tick's input and what its last step did. Its collision is its |
| `ZoneEvents.cs` | 117 | GainReason, IZoneEvent, ZoneEventHandler, ZoneEvents [Channel, Channel] | Why items arrived, for the UI's notifications. |
| `ZoneSpec.cs` | 156 | ZoneSpec | Which surface zone this is (GROUND.md §4.2): the body, what the server and every client need to build the same |

### src/Hollow.Simulation/Animals (7 files, 3,083 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Animal.cs` | 318 | AnimalState, TargetKind, AnimalTarget, Herd, Animal | What an animal is doing (GROUND.md §6.9); others see it (4 bits) and animate by it. |
| `AnimalHitboxes.cs` | 75 | AnimalHitboxes | An animal's hit shapes (GROUND.md §6.9), from its species' size and a pose: a capsule for the body along its length, |
| `AnimalWorld.Brain.cs` | 706 | AnimalWorld [Senses] | The animals' AI (GROUND.md §6.9): what each one notices of players, noises and other animals, and what it does about |
| `AnimalWorld.Spawning.cs` | 498 | SpawnArea, SpawnSlot, AnimalWorld | A cube-sphere tile of the world where animals live (GROUND.md §6.9, docs/WHOLE_WORLD.md §7.3: the level nearest the |
| `AnimalWorld.Steering.cs` | 265 | AnimalWorld | Steering (GROUND.md §6.9): a decision becomes a heading and a speed. Context steering: sixteen headings around the one |
| `AnimalWorld.cs` | 710 | AnimalObserver, AnimalNoise, IAnimalHost, AnimalWorld | A player as the animals see and hear them this tick (the zone fills the list). |
| `NavGrid.cs` | 511 | NavFlags, NavArea, NavGrid | What a cell of the navigation grid holds. |

### src/Hollow.Simulation/Building (14 files, 5,913 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `BuildResults.cs` | 126 | Privilege, IBuildingWallet, Wallet, Capsule, BuildingChange, BuildingEvent, IBuildingPart, Removal | Building rights at a place for a player (GROUND.md §8's privilege status). |
| `BuildingData.cs` | 916 | PieceFamily, PieceShape, Opening, StairsKind, SocketKind, DamageType, SocketDef, PieceDef, Protection, GradeDef, LeafDef, DoorDef, BuildingData [Reader] | What a piece is, structurally (RustMVP's families): it decides its sockets' geometry and its support losses. |
| `BuildingPhysics.cs` | 282 | RayHit, OverlapHit, BuildingPhysics [OnlyGround, NotGround, OnlyBuilding] | What a ray hit: the body, how far along, where and the surface's normal there. |
| `BuildingWorld.Destroyed.cs` | 59 | RemovedPart, BuildingWorld | A piece or door gone from the building as the Destroyed event names it (GROUND.md §6.4.2, §9.4): what it was (its |
| `BuildingWorld.Hammer.cs` | 278 | DamageHit, DamageResult, BuildingWorld | A hit on a piece or a door: the attack's direction in the zone (from the attacker toward the target) and its damage by type. |
| `BuildingWorld.Placement.cs` | 663 | PlacementPlan, BuildingWorld | What a piece's placement would be: the piece, its structure (none yet for a free foundation), its pose in the |
| `BuildingWorld.Replica.cs` | 364 | BuildingWorld, LockKind, BuildPartKind | The building as a copy of someone else's (GROUND.md §6.3, phase 2): a world loaded from storage, and a client's |
| `BuildingWorld.cs` | 659 | BuildingWorld | Building in one zone (GROUND.md §6.3): its structures and pieces, doors and claim devices, and every rule about them, |
| `ClaimDevice.cs` | 500 | ClaimDevice, BuildingWorld | The claim device, our tool cupboard (GROUND.md §6.3): it stands on a piece of a structure, lists the players |
| `Doors.cs` | 653 | LockDef, DoorLock, KeyLock, CodeLock, Door, DoorLeaf, BuildingWorld | A lock as something to place (building.json's locks, GROUND_SYSTEMS.md §6): its key (its item's), name, kind, and the |
| `ItemWallet.cs` | 29 | ItemWallet | Building materials as items in containers (GROUND.md §6.3, phase 2): a player's main and belt slots pay for placing, |
| `PieceGeometry.cs` | 666 | Pose, SocketId, PieceGeometry | A piece's place in its structure's frame: the pivot in metres (Y up) and the yaw in steps of 30°. Every yaw in a |
| `PieceShapes.cs` | 411 | ConvexPart, PieceShapes | One convex part of a piece's collision, in the piece's local metres: its corner points (what Jolt builds a hull |
| `Structure.cs` | 307 | BuildingFrame, Link, Piece, Structure | A structure's frame (GROUND.md §6.3): its origin in the zone and its axes, with +Y the planet's up at its first |

### src/Hollow.Simulation/Capabilities (5 files, 1,077 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Capability.cs` | 108 | CapabilitySet, Viewer, ICapability, ILifetimeCapability, IDependentCapability [Enumerator] | A set of capabilities (GROUND_SYSTEMS.md §2.1): bit i is capabilities.json's entry i. A thing's set |
| `Handlers.cs` | 260 | IOwned, IContainerHolder, IAuthorisations, ILifetime, CapabilityHandlers [Owned, Holds, Works, Burner, Machine, DoorCapability, KeyLockCapability, CodeLockCapability, CupboardCapability, ShipCapability, ExplosiveCapability, Standing, FireCapability, Lifetime] | A thing with an owner: the player who placed or built it (0: none). |
| `Interactions.Building.cs` | 218 | CapabilityHandlers [DoorCapability, KeyLockCapability, CodeLockCapability, CupboardCapability] | The interaction side of the building's capabilities (GROUND_SYSTEMS.md §5, GROUND.md §6.3): doors, their key and code |
| `Interactions.Things.cs` | 235 | VerbAction, VerbActions, CapabilityHandlers [Holds, Pickup, Portable, Burner, Machine, Station] | What a verb does, on the server: the context's zone, doer and thing. |
| `Interactions.World.cs` | 256 | CapabilityHandlers [WoundedCapability, CharacterCapability, DudCapability, SeatCapability, ShipDoorCapability, FuelCapability, ArrowCapability] | The interaction side of people, explosives, ships and spent arrows (GROUND_SYSTEMS.md §5): helping the wounded up, a |

### src/Hollow.Simulation/Combat (4 files, 1,337 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Damage.cs` | 183 | DamageDelivery, DestroyCause, TargetFamilies, TargetFamilySets, DamageAmounts, DamageRequest, DamageOutcome | The damage pipeline's common types (GROUND.md §6.4.2, after RustMVP's damage plan, docs/damage_architecture.md): a |
| `Firearms.cs` | 551 | RecoilState, RecoilSample, Recoil, GunMods, GunState, GunActionKind, GunAction, ShotContext, Shot, FirearmRules, Ballistics | The recoil of the gun in hand (RustMVP weapon_rules.hpp RecoilState): the target the view is kicked to, where |
| `Rockets.cs` | 46 | RocketMotor, RocketFlight | A battery's rocket motor (IRON_WAKE.md §6.4) and the one step the server flies it and clients draw it with. |
| `Geometry.cs` | 198 | Geometry | Ray tests for hits (melee sweeps, arrows, later bullets): against spheres, capsules and oriented boxes, all in the |
| `Hitboxes.cs` | 405 | Pose, HitPose, HitShape, Hitboxes, HitboxHistory [Track, ShipTrack] | How a body lies, for its hit shapes. |

### src/Hollow.Simulation/Content (13 files, 3,228 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Animals.cs` | 257 | Temperament, AttackKind, AttackStats, AnimalSpecies, AnimalSenses, AnimalSpawning, HarvestRates | How an animal meets a threat (GROUND.md §6.9). |
| `CapabilityData.cs` | 144 | CapabilityData | capabilities.json (GROUND_SYSTEMS.md §2.1): the kinds of behaviour a world thing may have, in their append-only order |
| `Explosives.cs` | 209 | BlastStats, ThrowableStats, FireDef, FireRules, RaidAlertSettings | One explosion (explosives.json blasts, GROUND.md §6.6): how far it reaches, how it falls off, what it does to |
| `Batteries.cs` | 377 | BatteryFire, LoaderPer, LoaderEase, RecoilCurve, LaunchProfile, BatteryLaunch, BatteryMount, LoaderMotion, LoaderPhase, LoaderHolder, BatteryLoader, BatteryDef | The crewed batteries' kinds (batteries.json, IRON_WAKE.md §6): launch profiles, mount, recoil, loader phases and holders. |
| `GameContent.cs` | 448 | ContentException, GameContent [Loaded] | The content files have problems; the message lists every one. |
| `Gather.cs` | 137 | GatherRate, SpotRule, LastHitRule, GatherCue, MarkerLook, FallLook, ShakeLook, GatherLook, GatherRules | Which of a tool's gather rates a class of natural objects takes (items.json tree, ore). |
| `Interactions.cs` | 113 | VerbSwitch, VerbDef, InteractionData | Which way a verb turns a device's switch: the loot panel's on/off button finds its verb by this. |
| `ItemDef.cs` | 316 | WearLayer, BodyRegions, HitRegion, MeleeStats, BowStats, AmmoStats, ConsumeStats, WearStats, FuelStats, CookStats, BuildingRole, ItemDef, CharacterTicks | Clothing layers (GROUND.md §6): two worn items may not share a layer, so a shirt and a chest plate go together but |
| `ItemParts.cs` | 253 | ItemPart, HeldUse, ItemParts, ItemPartTable [Enumerator, Row] | A part of an item (GROUND_SYSTEMS.md §7): a block of its data that gives it behaviour (items.json's melee, |
| `JsonFields.cs` | 220 | JsonFields | Reads fields of the content files and notes every problem (a missing field, a wrong type, an unknown name) with |
| `Tables.cs` | 467 | ItemAmount, Recipe, NaturalClass, NaturalKind, DeviceStats, DeployableDef, BayStats, LootTable, Category, SurvivalTuning, LifetimeRestart, LifetimeRule [Entry] | An amount of an item: a recipe's ingredient, a yield. |
| `Texts.cs` | 163 | Texts | The player's lines (GROUND_SYSTEMS.md §3.2, texts.json): one for every OutcomeCode, the one place |
| `Weapons.cs` | 260 | FireMode, RecoilMode, AttachmentSlot, RecoilStep, RecoilStats, SpreadStats, FirearmStats, BulletStats, AttachmentStats | How the trigger works: one shot a press, or shots while held. |
| `World.cs` | 241 | WorldPropKind, PropDef, RecyclerStats, ResearchStats, TechNode, TechTree, LootSpotSettings, WorldData | What a world prop is (world.json props, GROUND.md §6.8). |

### src/Hollow.Simulation/Content/Readers (24 files, 4,733 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AnimalsReader.cs` | 301 | AnimalsReader | Reads animals.json (GROUND.md §6.9) and links its yields and tools to items. |
| `BuildingReader.cs` | 103 | BuildingReader | building.json (GROUND.md §6.3) has a parser of its own (BuildingData); its items are linked here: a door |
| `CapabilitiesReader.cs` | 41 | CapabilitiesReader | capabilities.json (GROUND_SYSTEMS.md §2.1): the capabilities' keys, in their append-only order (at most 64: a thing's set is one word). |
| `CategoriesReader.cs` | 25 | CategoriesReader | categories.json: the crafting tabs, each a key and a name; an item's category is one of them. |
| `ContentReaders.cs` | 36 | ContentReaders | The readers in the order the loader runs them (GROUND_SYSTEMS.md §12.6): each file after the files it refers to |
| `CrossChecks.cs` | 97 | CrossChecks | The checks across the files, last (GROUND.md §7): every recipe can be made from what can be gathered. Starting from |
| `DeployablesReader.cs` | 168 | DeployablesReader | deployables.json: everything placed from the inventory (bags, beds, fires, furnaces, boxes, workbenches, the |
| `ExplosivesReader.cs` | 279 | ExplosivesReader | Reads explosives.json (G5) and links rounds' blasts (weapons.json) to it; checks both against building.json's grades. |
| `BatteriesReader.cs` | 365 | BatteriesReader | Reads batteries.json (IW3) and checks it: profiles for every rocket, the phases against the mount, named phases. |
| `GatherReader.cs` | 368 | GatherReader | gather.json: the natural kinds (trees, outcrops, pickups, bushes, boulders) with their health, yields, stages, |
| `IContentReader.cs` | 89 | IContentReader, ContentReading, ContentChecking | One data file's reader (GROUND_SYSTEMS.md §12.6, decision K12): every file in GameContent.Files and the |
| `InteractionsReader.cs` | 103 | InteractionsReader | Reads interactions.json (GROUND_SYSTEMS.md §5) once survival.json's reaches are known (a verb names one of them). |
| `ItemsReader.cs` | 303 | ItemsReader | items.json: every item with its parts (melee, bow, ammo, consume, wear, fuel, |
| `LootReader.cs` | 79 | LootReader | loot.json: the loot tables (guaranteed entries, then weighted rolls): the respawn kit, the load tests' kits, the |
| `PlacementReader.cs` | 377 | PlacementReader | placement.json (GROUND_SYSTEMS.md §6): the defaults every placement rule starts from, each kind's block over them, the |
| `ReaderHelpers.cs` | 74 | ReaderHelpers | Fields more than one reader shares: lists of numbers as vectors (the deployables' bays, the ships) and a ship's turn axis. |
| `RecipesReader.cs` | 127 | RecipesReader | recipes.json: every recipe (output, amount, seconds, workbench level, ingredients, known from the start, its |
| `ShipInteriorReader.cs` | 285 | ShipInteriorReader | A ship's data file (IRON_WAKE.md §9.1, game/data/ground/ships/), as a ships.json entry's interior block |
| `ShipLayoutReader.cs` | 524 | ShipLayoutReader | A ships.json entry's layout (IRON_WAKE.md §5.1): its seats, doors, target parts, nozzles and |
| `ShipsReader.cs` | 336 | ShipsReader | ships.json (G6): the kinds of ship: mass and inertia, hit points and damage multipliers, thrust and speeds, boost, |
| `SurfacesReader.cs` | 21 | SurfacesReader | surfaces.json (GROUND.md §6.1): the drawn tops of the outcrops' stage models, measured by the model viewer |
| `SurvivalReader.cs` | 190 | SurvivalReader | survival.json: vitals, metabolism, bleeding, burning, the wounded state, temperature, the climate stand-in, the |
| `TextsReader.cs` | 54 | TextsReader | texts.json: the player's lines (GROUND_SYSTEMS.md §3.2), read like the rest but outside the hash (a reworded line or |
| `WeaponsReader.cs` | 317 | WeaponsReader | weapons.json (G4): the firearms with their cadence, ballistics, spread and recoil, the kinds of rounds by calibre |
| `WorldReader.cs` | 436 | WorldReader | Reads world.json (G7) and links it to the items, recipes, loot tables and deployables; then checks it. |

### src/Hollow.Simulation/Diagnostics (1 file, 352 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `TickProfile.cs` | 352 | ProfileSection, TickProfile, TickProfileExtensions [Scope] | The parts of a server tick that TickProfile times (GROUND.md §5.11). |

### src/Hollow.Simulation/Economy (1 file, 99 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Rules.cs` | 99 | Recycling, Research, TechTrees | The recycler's rules (GROUND.md §6.8, Rust's): a loot-only component gives its own list (world.json recycle); |

### src/Hollow.Simulation/Entities (2 files, 272 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `EntityId.cs` | 65 | EntityId, EntityIdAllocator | A persistent entity's permanent 64-bit ID (ARCHITECTURE.md §7): 41 bits of milliseconds since 1 January 2026, |
| `EntityStore.cs` | 207 | Entity, EntityStore, IComponentPool, ComponentPool | A live entity in one EntityStore: its slot and the slot's generation, so a handle kept after the |

### src/Hollow.Simulation/Events (14 files, 412 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Crafted.cs` | 19 | Crafted | To a player: a crafted unit is done. (GROUND_SYSTEMS.md §12.3) |
| `Destroyed.cs` | 53 | Destroyed | To everyone near (GROUND.md §6.4.2): something was destroyed: a deployable or world prop broken, a building piece, door or claim device broken, brough… |
| `Detonated.cs` | 49 | Detonated | To everyone near (G5): an explosion. (GROUND_SYSTEMS.md §12.3) |
| `Died.cs` | 25 | Died | To a player: they died. (GROUND_SYSTEMS.md §12.3) |
| `FireLit.cs` | 36 | FireLit | A fire was lit where a blast left it (IW4, IRON_WAKE.md §7), or fell onto something else when what held it went: the |
| `FireOut.cs` | 19 | FireOut | A fire went out (IW4, IRON_WAKE.md §7): which, where, and why (burnt out, doused, replaced, fell with nothing below). (GROUND_SYSTEMS.md §12.3) |
| `GotUp.cs` | 21 | GotUp | To a player: they got up again. (GROUND_SYSTEMS.md §12.3) |
| `HitConfirmed.cs` | 48 | HitConfirmed | To the attacker: their hit landed, with what it did. (GROUND_SYSTEMS.md §12.3) |
| `Hurt.cs` | 27 | Hurt | To the victim: damage taken, from a direction. (GROUND_SYSTEMS.md §12.3) |
| `ItemsGained.cs` | 26 | ItemsGained | To a player: items gained (gathering, picking up, crafting, looting) or lost (a tool broke, fuel burnt). (GROUND_SYSTEMS.md §12.3) |
| `Notice.cs` | 18 | Notice | To a player: a line to read (a built ship waiting for room, G6). (GROUND_SYSTEMS.md §12.3) |
| `ProjectileEnded.cs` | 26 | ProjectileEnded | To everyone near: a projectile stopped. (GROUND_SYSTEMS.md §12.3) |
| `ProjectileLaunched.cs` | 25 | ProjectileLaunched | To everyone near: a projectile was loosed (an arrow, or a bullet with its shot's sequence number). (GROUND_SYSTEMS.md §12.3) |
| `TurretFired.cs` | 17 | TurretFired | A crewed battery fired (IW3): ship, station, barrel, the shot's number, tick, round, gunner. |
| `WentDown.cs` | 20 | WentDown | To a player: they were knocked down, wounded. (GROUND_SYSTEMS.md §12.3) |

### src/Hollow.Simulation/Interaction (1 files, 242 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Interaction.cs` | 242 | InteractOption, InteractArgs, DoerState, Doer, InteractContext, IPickupFamily, IAuthoriseFamily, IRepairFamily, IInteractCapability, InteractionRules | One thing the E key can do to a target now (GROUND_SYSTEMS.md §5): a verb (interactions.json's number), done or |

### src/Hollow.Simulation/Items (10 files, 2,189 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AttachmentRules.cs` | 70 | AttachmentRules | Putting attachments on firearms and taking them off (GROUND.md §6.3): the server applies these to its containers and |
| `Blueprints.cs` | 61 | BlueprintBook | The blueprints a character has learnt (GROUND.md §6.8, Rust's): recipes not known from the start, unlocked in a |
| `Container.cs` | 314 | ItemStack, ContainerKind, Container | Some of one item in a slot. Empty when Item is 0. A firearm (G4) also carries its magazine and its |
| `CraftQueue.cs` | 225 | CraftJob, CraftQueue | A crafting job: Count units of a recipe left, the front one TicksLeft from done. |
| `CraftingSpeed.cs` | 33 | CraftingSpeed | The workbench's speed (GROUND_SYSTEMS.md §7, the station capability's share of crafting): a job takes its recipe's time |
| `InventoryRules.cs` | 235 | InventoryRules | Moving items between slots (GROUND.md §6, Rust's rules): the server applies these to its containers, and the client |
| `ItemActions.cs` | 403 | ItemActionDef, ItemActionOption, IItemActor, ItemActionContext, IItemAction, ItemActions [Use, Eat, WearIt, Unequip, Learn, Unload, Remove] | One of items.json's actions (GROUND_SYSTEMS.md §7): a button of the item panel's ACTIONS. Its place in the list |
| `ItemRows.cs` | 608 | ItemRow, ItemDescription, IItemDescriber, ItemRows [MeleeRows, BowRows, ArrowRows, ConsumableRows, WearableRows, FuelRows, CookableRows, DeployableRows, ShipRows, FirearmRows, RoundRows, AttachmentRows, ThrowableRows, PlanRows, HammerRows, CupboardRows, LockRows, KeyRows, DoorRows, BlueprintRows, ComponentRows] | A row of an item panel's INFORMATION (GROUND_SYSTEMS.md §7): "Rate of fire" and "450 rpm"; good news or not. |
| `LootRules.cs` | 59 | LootKind, LootRules | What is being looted, which decides what may be put in. |
| `PlayerInventory.cs` | 181 | ContainerRef, PlayerInventory | How an intent names a container: the player's own three, or one of those they are looting. |

### src/Hollow.Simulation/Movement (8 files, 1,276 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `CharacterMotor.cs` | 360 | MoveContext, StepEvents, CharacterMotor | What a movement step needs to know about where it happens: the frame it runs in (IMoveFrame: the |
| `CharacterState.cs` | 151 | Stance, MoveButtons, MoveFlags, MoveCheats, MoveInput, CharacterState |  |
| `CharacterWorld.cs` | 86 | CharacterWorld | A character in the zone's frame, wherever it moves (IRON_WAKE.md §4.2): aboard a ship its state is the ship's frame's, |
| `Facing.cs` | 96 | Facing | Which way things face on a whole world (docs/WHOLE_WORLD.md §5). A zone's frame is the body's own (+Y the north pole), |
| `FreeLook.cs` | 58 | FreeLook | Free look (Left Alt held, RustMVP's; GROUND.md §4.5): the mouse turns the head instead of the view, so the camera |
| `MoveFrames.cs` | 287 | IMoveFrame, ZoneMoveFrame, ShipPose, IShipFrame, MoveFrames | The frame a character moves in (IRON_WAKE.md §4.3): the zone's, or a ship's. It says which way is up, how strong |
| `MovementCheck.cs` | 111 | MovementCheck [Result] | The physics spike of GROUND.md §4.5, kept as a check: a fixed script of inputs walks a character over ground from |
| `MovementTuning.cs` | 127 | MovementTuning | The numbers of on-foot movement (GROUND.md §4.5, §6). Rust's movement, through RustMVP's legacy spec |

### src/Hollow.Simulation/Nature (7 files, 1,746 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `LootSpots.cs` | 403 | LootSite, LootSpot, LootSpots [Patch] | What kind of place a loot cluster stands in (GROUND.md §6.8). |
| `NaturalBodies.cs` | 122 | NaturalBodies | Keeps the trees and outcrops of every ground tile in a PhysicsWorld as static props, so characters |
| `NaturalObjects.cs` | 116 | NaturalObject, INaturalObjects, NaturalShapes | One natural object (GROUND.md §4.6): a tree, an outcrop, a pickup or a bush, generated from the surface, never |
| `NaturalState.cs` | 310 | NaturalOverride, GatherSwing, GatherResult, NaturalState | A player change to a natural object (ARCHITECTURE.md P1: only changes are stored). |
| `ScatterNaturalObjects.cs` | 234 | ScatterNaturalObjects [TileObjects] | The natural objects of a real surface (GROUND.md §6.1): the planet's own scatter (Scatter), the same |
| `SpotSurfaces.cs` | 410 | ISpotSurface, SpotFrames, TrunkSurface, MeasuredSurface | Where a natural object's weak spot can sit (GROUND.md §6.1): on the surface players see, at the object's current |
| `StandInNaturalObjects.cs` | 144 | StandInNaturalObjects | Natural objects of the stand-in world (docs/WHOLE_WORLD.md §7.1), placed by a seeded stream per cube-sphere tile of |

### src/Hollow.Simulation/Physics (5 files, 1,573 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `CharacterBody.cs` | 174 | CharacterBody | A character's collision in a space (GROUND.md §4.5): Jolt's virtual character, a kinematic capsule outside the |
| `JoltRuntime.cs` | 51 | JoltRuntime | The process-wide Jolt runtime (GROUND.md §4.5). Jolt is initialised once per process, by whichever world needs it |
| `PhysicsWorld.cs` | 694 | PhysicsWorld [Prop, Tile, TileBuild] | One zone's Jolt physics system (GROUND.md §4.5): the ground near characters, as static mesh tiles built from the |
| `Props.cs` | 118 | PropLayer, ITileListener, PropKind, PropShape | Who a prop belongs to: its key is only unique within its layer. |
| `ShipSpace.cs` | 536 | SpaceHit, ShipSpace [KindShapes] | What a query through a ship's space met: the part (0 the hull), how far along, where and the surface's normal, in the ship's frame. |

### src/Hollow.Simulation/Placement (9 files, 2,059 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Aboard.cs` | 290 | PlacedSpace, AboardHit, AboardSpot, Aboard | Placing aboard a ship (IRON_WAKE.md §8.1): the free solver's half in the ship's frame, its claim settled there, and the checks' parts aboard (where, corners, sight, room). |
| `Checks.cs` | 215 | IPlacementCheck, Checks [Reach, Sight, Surface, Step, Slope, Support, Water, Rights, Overlap, Limits, Own, Aboard] | One check of the placement pipeline (GROUND_SYSTEMS.md §6), registered by name and run in placement.json's fixed order, |
| `Footprint.cs` | 235 | Footprint, BoxFootprint, HullFootprint [PointFootprint] | What a placement occupies (GROUND_SYSTEMS.md §6's footprint): a box (a deployable's or the claim device's size, or half |
| `Kinds.cs` | 320 | ResolvedPlaceable, IPlacementSite, PlacementSources, PlaceableKind, Kinds, DeployableKind, PieceKind, DoorKind, LockPlacement, CupboardKind, ChargeKind, ShipKind | What a kind finds a placeable's key to name in the game data (its definition and the items it involves). |
| `Placeable.cs` | 200 | PlacementCost, Placeable, PlacementData, PlacementContext, PlacementAim, PlacementClaim, PlacementCandidate | A material a placement costs (a building piece's twig, from the carried slots). |
| `PlacementRule.cs` | 123 | PlacementSurfaces, SupportMode, AlignMode, PrivilegeMode, PlacementRule | What a placement stands on or is fitted to (placement.json's surfaces): the ground, the top of a foundation, floor or |
| `PlacementWorld.cs` | 289 | PlacedThing, PlacedShip, PlacementHit, IPlacementSurroundings, PlacementWorld | A deployable or prop as placement sees it: what it is named, its box (on its base, along up, turned by yaw). |
| `Placer.cs` | 151 | PlacementHandlers, Placer | The placement system's handlers (GROUND_SYSTEMS.md §6, §3.5): the solvers, rotations, checks and kinds, each found by |
| `Rotations.cs` | 134 | IPlacementRotation, Rotations [Free, Steps, Support, Flip, Fixed] | How a placement is turned (GROUND_SYSTEMS.md §6's rotation): from the view and R's presses on the client, from the |
| `Solvers.cs` | 405 | IPlacementSolver, Solvers, FreeSolver, SocketSolver, OpeningSolver, AttachSolver, SweepSolver | Turns an aim into candidates (GROUND_SYSTEMS.md §6), registered by name: free (onto the surface under the aim), socket |

### src/Hollow.Simulation/Players (3 files, 331 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Climate.cs` | 98 | IClimate, PlanetClimate, StandInClimate | The air temperature at a place and time (GROUND.md §6): PlanetClimate on a real surface, |
| `MapMarker.cs` | 14 | MapMarker | A marker a player put on the map (GROUND.md §6.8, Rust's: up to five, theirs alone): a point in the zone frame, a |
| `Player.cs` | 219 | Vitals, ActionButtons, ActionInput, ActionKind, Player | The survival state of a character (GROUND.md §6): health and the needs, all in their own units. |

### src/Hollow.Simulation/Raid (3 files, 617 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Explosive.cs` | 263 | ExplosiveState, LiveExplosive, RaidHit, ExplosiveSave, BlastReport, RaidCounters | Where a thrown or placed explosive is (GROUND.md §6.6). |
| `Fire.cs` | 106 | FireEnd, Fire | Why a fire went out (the FireOut event). |
| `TestBases.cs` | 248 | TestBases [Hut] | Small claimed bases built straight through the building rules (no player, no wallet, no placement checks), for the raid |

### src/Hollow.Simulation/Ships (9 files, 2,797 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Ship.cs` | 284 | ShipDoorState, Ship, ShipLink, ShipSeat | A ship's door as the rules have it (IRON_WAKE.md §5.3): its progress (0 shut … 1 open) at a tick and which way it moves |
| `ShipCollider.cs` | 306 | ShipRayHit, ShipContact, ShipCollider [ExceptBody] | Where a ray from a ship met the world: how far, the point and the surface's normal there. |
| `ShipDef.cs` | 441 | HullBox, TurnAxis, ShipDef, ShipParts | One box of a ship's hull, in the ship's frame: its centre and half extents. |
| `ShipFrame.cs` | 66 | ShipFrame | A ship's frame in a zone (IRON_WAKE.md §4.3): what characters aboard it move in, keyed by the ship's entity ID. Up is |
| `ShipInterior.cs` | 298 | ShipPartKind, ShipPartDef, ShipPiece, BoardingBox, ShipSocket, ShipFixture, PartPose, ShipInterior | How one of a ship's parts moves (the data file's part kinds, IRON_WAKE.md §9.1). |
| `ShipLayout.cs` | 276 | SeatRole, SeatSitters, SeatWorks, DoorOpens, DoorEase, ShipPartHealth, ShipSeatDef, ShipDoorDef, ShipTargetPart, NozzleDrive, ShipNozzle, LightPower, ShipLight | What a seat's occupant does (ships.json seats' role, IRON_WAKE.md §5.2). |
| `ShipMotor.cs` | 793 | ShipContext, ShipMotor [SkidLoad] | What a ship's step needs to know about where it happens. |
| `ShipQueries.cs` | 155 | ShipQueryHit, ShipQueries | What a query of the zone's frame met in a ship's space: the ship's frame, the hit there, and how far along the query it is. |
| `ShipStations.cs` | 605 | StationBarrel, ShipStationDef, StationInput, LoaderState, StationState, StationRules, LoaderPoses [NodePose, HolderView] | A ship's stations (IRON_WAKE.md §6): defs, state, loader records, the mount and fire rules both sides run, the mechanism's poses. |
| `ShipState.cs` | 178 | ShipFlags, ShipButtons, ShipInput, ShipState, ShipEvents | What a ship is doing, besides its motion. Some are found by the last step (outputs), some set by the zone. |

### src/Hollow.Simulation/Systems (31 files, 10,679 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `ActionSystem.cs` | 360 | ActionSystem, ActionSystemAccess [PlayerActor] | Players' actions (ARCHITECTURE.md §11.4: intents): every one checks everything against the server's state and |
| `AnimalSystem.cs` | 400 | AnimalSystem, AnimalSystemAccess [AnimalReceiver] | Animals in the zone (GROUND.md §6.9): the glue between the animal world (Animals/) and the rest of the game. Players |
| `ArrowSystem.cs` | 249 | ArrowSystem, ArrowSystemAccess [ArrowTargets] | Spent arrows (GROUND_SYSTEMS.md §5, the owner's): an arrow that stays whole on a hit sticks where it hit, its tip in, |
| `AutoRespawnSystem.cs` | 42 | AutoRespawnSystem, AutoRespawnSystemAccess | G1's respawn, for a zone without game content (the movement checks, the network tests): a dead character comes back |
| `BlastSystem.cs` | 231 | BlastSystem, BlastSystemAccess [RaidTallies, RaidTally] | What a blast sees, and the building's side of raiding (GROUND.md §6.6): the explosive channel's fractions of a point, |
| `BuildingSystem.cs` | 611 | HammerAction, BuildingSystem, BuildingSystemAccess [BuildingReceiver] | What a hammer does to a piece. |
| `CombatSystem.cs` | 588 | Projectile, TraceHit, CombatSystem, CombatSystemAccess [CharacterReceiver, NatureReceiver, GroundReceiver] | An arrow in flight (later bullets): simulated on the server only (ARCHITECTURE.md §11.5). |
| `DamageSystem.cs` | 361 | RayQuery, BlastQuery, BlastTarget, DamageSystem, DamageSystemAccess [IDamageReceiver, Blow] | A ray's test for what it meets first (a swing, an arrow's or a bullet's step): from where, which way, how far, how |
| `ExplosiveSystem.cs` | 590 | ExplosiveSystem, ExplosiveSystemAccess | Raiding (GROUND.md §6.6, G5): explosives thrown, placed and lit again; their flight, bounces and sticking; fuses and |
| `FireSystem.cs` | 496 | FireCounters, FireSystem, FireSystemAccess [FireSpot, FireTargets] | What fires have done so far (the load test reports it). |
| `GroundSystems.cs` | 49 | GroundSystems | The zone's systems and their order (GROUND_SYSTEMS.md §12.2, K3), the one place that lists them. The host runs them |
| `GunSystem.cs` | 604 | Bullet, GunSystem, GunSystemAccess | A bullet in flight (G4): simulated on the server only (ARCHITECTURE.md §11.5); clients draw their own. |
| `HeldSystem.cs` | 106 | HeldSystem, HeldSystemAccess [IHeldRules, MeleeRules, BowRules, ActsByIntents] | What a held item does in the zone (GROUND_SYSTEMS.md §7): one handler per held behaviour, found by the item's |
| `HistorySystem.cs` | 70 | HistorySystem, HistorySystemAccess | Records every player's pose at the end of the tick (ARCHITECTURE.md §11.5): the second of poses hits are judged |
| `InteractionSystem.cs` | 229 | InteractionSystem, InteractionSystemAccess | Interaction (GROUND_SYSTEMS.md §5): one intent, one path. A player names a target and a verb; the server resolves the |
| `NatureSystem.cs` | 22 | NatureSystem, NatureSystemAccess | The natural objects' own life (GROUND.md §6.1): once a second, what players cut, mined or picked comes back when its |
| `PlacementSystem.cs` | 261 | PlaceRequest, PlacementSystem, PlacementSystemAccess [ZoneSurroundings] | What a player asks to place (PlaceIntent, GROUND_SYSTEMS.md §6): the slot that pays for it (the item used up, or the |
| `PlayerSystem.cs` | 432 | PlayerSystem, PlayerSystemAccess | The survival game's players (GROUND.md §6): their lives, sleep, death into a corpse, and respawn at a bag or on the |
| `ShipSystem.Doors.cs` | 228 | ShipSystem | Doors (IRON_WAKE.md §5.3): a door is a part with a progress, 0 shut to 1 open, moved over its seconds by the ship's rules |
| `ShipSystem.Parts.cs` | 330 | ShipSystem [ShipReceiver] | A ship's crew, lock and parts (IRON_WAKE.md §5.2, §5.5): its list of authorised players (the tool cupboard's verbs at its |
| `ShipSystem.Seats.cs` | 500 | SeatRelease, ShipSystem, ShipSeats | Why a seat's occupant leaves it (IRON_WAKE.md §5.2): every way out goes through ShipSystem.Release. |
| `ShipSystem.Spaces.cs` | 194 | ShipSystem | Ships people walk in (IRON_WAKE.md §4): who is aboard, a parked ship's body in the zone, and the one entry point that |
| `ShipSystem.Stations.cs` | 468 | ShipSystem | Crewed batteries each tick (IW3): the gunner's controls, the mount, the loaders, the trigger and its refusals, the magazine. |
| `ShipSystem.cs` | 656 | ShipEventKind, ShipDamage, ShipEvent, ShipSystem, ShipSystemAccess | What happened to a ship that players should hear about (G6). |
| `SpillSystem.cs` | 235 | SpillSystem, SpillSystemAccess | Loose items on the ground (GROUND.md §6.1). Everything that lands there goes through Spill: a stack dropped from an |
| `SupportSystem.cs` | 117 | SupportSystem, SupportSystemAccess | Support links (GROUND_SYSTEMS.md §3.6): what rests on or is stuck in what. A dependent keeps its own SupportLink (its |
| `TargetSystem.cs` | 1043 | TargetSystem, TargetSystemAccess [GroundTargets, CharacterTargets, AnimalTargets, NaturalTargets, ThingTargets, PieceTargets, DoorTargets, CupboardTargets, ExplosiveTargets, ShipTargets] | Naming world things (GROUND_SYSTEMS.md §3.1): a TargetRef routes to its family, each family registered once here, which |
| `ThingSystem.cs` | 556 | ThingSystem, ThingSystemAccess [DeployableReceiver] | Static things of the survival game (GROUND.md §6): deployables and their devices, dropped items, corpses; warmth and |
| `VitalsSystem.cs` | 153 | VitalsSystem, VitalsSystemAccess | Survival, once a second (GROUND.md §6): metabolism, temperature, bleeding, healing, and the damage of starvation, |
| `WorldSystem.cs` | 703 | PropOverride, WorldStats, WorldSystem, WorldSystemAccess | What is stored about a world prop (GROUND.md §6.8, ARCHITECTURE.md P1: only changes): gone until a time, or standing |
| `WoundedSystem.cs` | 120 | WoundedSystem, WoundedSystemAccess | The wounded state (GROUND.md §6.3, Rust's): a lethal hit that is small enough, and isn't a shot to the head, knocks |
| `ZoneSystem.cs` | 143 | ZoneSystem | One concern of a zone's rules (GROUND_SYSTEMS.md §12.2, K1): its own state, stepped by the host in the host's order. |

### src/Hollow.Simulation/Targets (3 files, 672 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `SupportLink.cs` | 26 | SupportLink | A world thing resting on or stuck in another (GROUND_SYSTEMS.md §3.6): what holds it (its support; the part may name |
| `TargetRef.cs` | 159 | TargetFamily, TargetRef, ViewerFlags, TargetPose, ITargetFamily, ITargetable | The families of world things (GROUND_SYSTEMS.md §3.1): each is one store of the zone with its own lookup, and |
| `Targeting.cs` | 487 | AimRay, AimKind, AimHit, TargetFacts, ITargetView, DoorShapes, ShipShapes, NaturalAim, BuildingAim, ShipAim, Targeting, AimShapes | The ray the interact key looks along (GROUND_SYSTEMS.md §5): from the eye, along the view, as far as the farthest verb |

### src/Hollow.Simulation/Teams (1 file, 323 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `TeamRegistry.cs` | 323 | TeamAction, Team, TeamInvite, TeamRegistry | What a player does with teams (GROUND.md §6.8, Rust's). An invite is a verb on the player invited (GROUND_SYSTEMS.md §5, |

### src/Hollow.Simulation/World (2 files, 434 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `SpentArrow.cs` | 64 | SpentArrow | A spent arrow (GROUND_SYSTEMS.md §5, the owner's): it stays where it stuck, in the ground, a building, a deployable, a |
| `WorldObjects.cs` | 370 | StaticKind, WorldObject, Deployable, WorldProp, DroppedContainer, DroppedBag, Corpse, StaticLink, PlayerLink | What kind of static thing an entity is (characters are not static). |

## src/Hollow.Net

The protocol: messages and their generated serializers, bit packing, snapshot deltas, relevance, the relay.

### src/Hollow.Net/Bits (2 files, 364 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `BitReader.cs` | 161 | BitReader | Reads what a BitWriter wrote. Packets come from outside, so nothing here throws on bad data: reading |
| `BitWriter.cs` | 203 | BitWriter | Packs values into a byte buffer bit by bit, least significant bit first (GROUND.md §4.4). Reuse one per thread or |

### src/Hollow.Net/Commands (2 files, 395 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `ConsoleCatalog.cs` | 210 | CommandSide, ArgKind, ConsoleCommandDef, ConsoleCatalog | Where a console command runs: in the game (the view and its settings) or on the server. |
| `ConsoleCompletion.cs` | 185 | CompletionSources, Completion, ConsoleCompletion, Spawning | What Tab completes from: the game's data, and the players the server says are on. |

### src/Hollow.Net/Protocol (11 files, 7,447 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AnimalNet.cs` | 75 | AnimalLook, AnimalNet | An animal as a client sees it (unpacked from its snapshot state). |
| `Attributes.cs` | 76 | NetMessageAttribute, NetStructAttribute, NetStateAttribute, BitsAttribute, SignedAttribute, VarIntAttribute, AngleAttribute, QuantizeAttribute, MaxLengthAttribute, MaxCountAttribute | The vocabulary of message definitions (Messages.cs). tools/Hollow.NetGen reads the definitions as text and writes |
| `INetMessage.cs` | 24 | INetMessage | A message of the protocol (GROUND.md §4.4): its type byte, its reader and its writers, so a handler registry can |
| `MessageHandlers.cs` | 71 | MessageHandler, MessageHandlers [Entry, Entry] | What a handler does with a message that arrived, in the context it arrived in (a connection, a client, a server). |
| `Messages.cs` | 1733 | Hello, Goodbye, ZoneInfo, Welcome, InputCommand, ShipControls, InputPacket, OwnState, SnapshotHeader, OwnShipState, ShipNetState, CharacterNetState, Spawn, Despawn, ChatSend, ChatLine, ThingSpawn, ThingUpdate, WornItem, CharacterLook, NaturalNet, NaturalStates, ProjectileLaunch, ProjectileEnd, TargetNet, MoveItemIntent, QuickMoveIntent, DropIntent, CraftIntent, CancelCraftIntent, CloseIntent, RespawnIntent, IntentResult, ShipIntent, ShipDetails, ShipPartNet, ShipDoorNet, ShipSeatNet, ShipPartHealthNet, GunNet, SlotNet, ContainerContents, ContainerSlots, LootState, VitalsUpdate, CraftJobNet, CraftQueueState, HitMarker, ItemsChanged, DamageTaken, CraftFinished, BagNet, LifeState, FireIntent, ReloadIntent, AttachIntent, GunUpdate, HammerIntent, BuildFrameNet, BuildFrames, PieceNet, BuildPieces, BuildIdNet, BuildRemoved, DoorNet, BuildDoors, ClaimNet, BuildClaims, ThrowIntent, ExplosiveUpdate, ExplosiveGone, Detonation, FireStarted, FireEnded, RaidAlertNet, RaidAlertMessage, RaidAlertList, TeamIntent, TeamChatSend, MarkerIntent, RecipeNet, BlueprintList, TeamMemberNet, TeamState, TeamInviteNet, TeamInvites, TeammateNet, TeamPositions, MarkerNet, RespawnPointNet, MapState, MachineState, ConsoleRun, ConsoleReply, ServerPlayerNet, ServerState, ZoneClock, InteractIntent, ArrowNet, ArrowStates, ArrowsGone, ThingDestroyed, PoseNet, PlaceIntent, ItemActionIntent, UnloadIntent, DevWatch, DevSectionNet, DevPlayerNet, DevStats, DevInspect, DevLineNet, DevInspected, DevCellNet, DevNav, DevSlotNet, DevAreaNet, DevAreas, DevLog | Every message of the ground game's protocol (GROUND.md §4.4, ARCHITECTURE.md §11). Definitions only: fields with |
| `Messages.g.cs` | 4852 | Protocol, Hello, Goodbye, ZoneInfo, Welcome, InputCommand, ShipControls, InputPacket, OwnState, SnapshotHeader, OwnShipState, ShipNetState, CharacterNetState, Spawn, Despawn, ChatSend, ChatLine, ThingSpawn, ThingUpdate, WornItem, CharacterLook, NaturalNet, NaturalStates, ProjectileLaunch, ProjectileEnd, TargetNet, MoveItemIntent, QuickMoveIntent, DropIntent, CraftIntent, CancelCraftIntent, CloseIntent, RespawnIntent, IntentResult, ShipIntent, ShipDetails, ShipPartNet, ShipDoorNet, ShipSeatNet, ShipPartHealthNet, GunNet, SlotNet, ContainerContents, ContainerSlots, LootState, VitalsUpdate, CraftJobNet, CraftQueueState, HitMarker, ItemsChanged, DamageTaken, CraftFinished, BagNet, LifeState, FireIntent, ReloadIntent, AttachIntent, GunUpdate, HammerIntent, BuildFrameNet, BuildFrames, PieceNet, BuildPieces, BuildIdNet, BuildRemoved, DoorNet, BuildDoors, ClaimNet, BuildClaims, ThrowIntent, ExplosiveUpdate, ExplosiveGone, Detonation, FireStarted, FireEnded, RaidAlertNet, RaidAlertMessage, RaidAlertList, TeamIntent, TeamChatSend, MarkerIntent, RecipeNet, BlueprintList, TeamMemberNet, TeamState, TeamInviteNet, TeamInvites, TeammateNet, TeamPositions, MarkerNet, RespawnPointNet, MapState, MachineState, ConsoleRun, ConsoleReply, ServerPlayerNet, ServerState, ZoneClock, InteractIntent, ArrowNet, ArrowStates, ArrowsGone, ThingDestroyed, PoseNet, PlaceIntent, ItemActionIntent, UnloadIntent, DevWatch, DevSectionNet, DevPlayerNet, DevStats, DevInspect, DevLineNet, DevInspected, DevCellNet, DevNav, DevSlotNet, DevAreaNet, DevAreas, DevLog | Written by tools/Hollow.NetGen from Messages.cs. Don't edit: change Messages.cs, then run |
| `NaturalNet.cs` | 27 | NaturalNet |  |
| `NetConvert.cs` | 186 | NetConvert | Between simulation values and their wire form. The client predicts with ToMoveInput(ToCommand(...)), the |
| `NetStates.cs` | 16 | CharacterNetState, ShipNetState | The entity states snapshots carry, as the replication's generic sections see them (their serializers are generated). |
| `Protocol.cs` | 152 | Protocol, GoodbyeReason, EntityKind, ShipNetFlags, ShipAction, ConsoleReplyKind, ChatKind, TeammateState | Constants of the ground game's protocol (GROUND.md §4.4). |
| `ShipConvert.cs` | 235 | ShipConvert | Between ships' simulation values and their wire form (G6). A pilot's client predicts with |
| `StationConvert.cs` | 126 | StationConvert | Between the batteries' values and their wire form (IW3): a gunner's controls, the stations' records. |

### src/Hollow.Net/Replication (4 files, 691 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `DeltaChannel.cs` | 297 | INetState, IDeltaCandidate, ShipCandidate, DeltaChannel, DeltaKeeper [Sent] | An entity state the generator writes with deltas ([NetState]): what a snapshot section needs of it. |
| `RelevanceGrid.cs` | 154 | RelevanceGrid | Which entities each client hears about (ARCHITECTURE.md §11.3): a uniform grid of cubes in the zone's frame (the |
| `SnapshotReceiver.cs` | 99 | SnapshotReceiver | The client's half of snapshot replication (SnapshotSender): decodes snapshots against the ones it |
| `SnapshotSender.cs` | 141 | SnapshotCandidate, SnapshotSender | An entity offered to a snapshot: its state now and how much it matters to this client. |

### src/Hollow.Net/Transport (3 files, 296 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `ConditionedRelay.cs` | 195 | ConditionedRelay [Datagram] | A local UDP relay that makes a network worse on purpose (GROUND.md §4.4): clients connect to |
| `NetConditions.cs` | 41 | NetConditions | Bad-network settings for testing (GROUND.md §4.4): extra round-trip latency, jitter and packet loss, applied by a |
| `TimerResolution.cs` | 60 | TimerResolution | Waiting precisely. Windows sleeps in 15.6 ms steps unless a process asks for 1 ms timers; a 30 Hz tick and a |

## src/Hollow.Persistence

SQLite storage: the database, one worker thread, one codec per kind of record.

### src/Hollow.Persistence (8 files, 2,443 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AnimalCodec.cs` | 73 | CorpseSave, AnimalCodec | An animal's corpse as stored: its species (key), where it lies, what is left to harvest, its timer. |
| `BuildingCodec.cs` | 332 | BuildingRecord, StoredClaim, ClaimRecord, BuildingCodec | A stored piece, door (with its lock) or claim device (with its list and store): kind and versioned bytes. |
| `ExplosiveCodec.cs` | 147 | ExplosiveCodec | An explosive's stored form (GROUND.md §6.6, G5): a charge stuck or lying in the world with its fuse left survives a |
| `GameDatabase.cs` | 902 | CharacterRecord, ThingRecord, NaturalRecord, TeamRecord, PropRecord, ZoneRecord, ZoneSave, NewerSaveException, ShipRecord, GameDatabase | A stored character (GROUND.md §4.7): where it is on its body, in the body frame (metres from the body's centre, |
| `PersistenceWorker.cs` | 138 | PersistenceWorker | Owns the GameDatabase on one background thread and runs every request in the order it was made |
| `ShipCodec.cs` | 287 | StoredShip, ShipCodec | A ship read back from storage, and who was in each of its seats (the server seats them again if they are there). |
| `TeamCodec.cs` | 50 | TeamCodec | A stored team (G7, GROUND.md §6.8): its leader and members, oldest first. Invites aren't stored. |
| `WorldCodec.cs` | 514 | WorldCodec | The stored form of the survival game's state (ARCHITECTURE.md §13: a compact binary payload with a schema version): |

## src/Hollow.Server

The dedicated server: the host and its modules, sessions, admin commands, notifications.

### src/Hollow.Server (11 files, 2,921 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AdminCommands.cs` | 538 | CommandContext, GameServer | Who runs a console command and where its answer goes (GROUND.md §8.2): a player through the F1 console (their own |
| `CharacterRecords.cs` | 140 | CharacterRecords | How a character is stored and comes back (GROUND.md §4.7): its record from the zone, its survival state from the |
| `DevInspector.cs` | 192 | DevInspector | Describes a target for the F2 panel's LOOK tab (GROUND.md §8.5): what its family resolves it to and every public |
| `GameServer.cs` | 1102 | GameServer [NetEntity] | The dedicated server (GROUND.md §4.2): one zone on the first world, ticking at 30 Hz on one thread, with |
| `NetPresence.cs` | 23 | NetPresence | The server's part of a networked entity: its net ID, view (octahedral, with the head's free look) and owner. |
| `PlayerSession.cs` | 253 | PlayerSession | One connected player: their connection, character, inputs, budgets and what the host has sent them. What a module |
| `Program.cs` | 155 | Program | The dedicated server's console program: |
| `ServerModule.cs` | 286 | ServerModule | One concern of the server (GROUND_SYSTEMS.md §12.5, K10): its own state, its message handlers and event |
| `ServerModules.cs` | 30 | ServerModules | The server's modules and their order (GROUND_SYSTEMS.md §12.5, K10), the one place that lists them. The host calls |
| `ServerOptions.cs` | 117 | ServerOptions | How a server runs. The console program fills it from its command line; the game fills it for Host mode. |
| `TickStats.cs` | 85 | TickStats | How long ticks take (GROUND.md §5: ≤ 10 ms typical with 100 bots). Keeps the last few seconds for the console's |

### src/Hollow.Server/Modules (14 files, 4,999 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AnimalModule.cs` | 205 | AnimalModule, AnimalModuleAccess | Animals on the server (GROUND.md §6.9): the zone's population turned on, animals replicated as entities of their own |
| `ArrowModule.cs` | 128 | ArrowModule, ArrowModuleAccess [ArrowView] | Spent arrows on the server (GROUND_SYSTEMS.md §5): each player is told of the arrows within their range — what holds |
| `BuildingModule.cs` | 640 | BuildingModule, BuildingModuleAccess [BuildView] | Building on the server (GROUND.md §4.4, §6.3, phase 2): players' building intents (on the same budget as every |
| `ConsoleModule.cs` | 827 | ConsoleModule, ConsoleModuleAccess [ConsoleBudget] | The F1 console on the server (GROUND.md §8.2): who is an admin, the console's messages (a line in, the answer's lines |
| `DamageModule.cs` | 48 | DamageModule, DamageModuleAccess | The damage pipeline's replication (GROUND.md §6.4.2): what was destroyed, to the clients that draw it. |
| `DevModule.cs` | 452 | DevModule, DevModuleAccess [Watch] | The F2 developer panel's server side (GROUND.md §8.5): who watches; every half second the stats (the tick's profile |
| `FireModule.cs` | 128 | FireModule, FireModuleAccess [FireView] | Fires on the server (IW4, IRON_WAKE.md §7): each player is told of the fires within their range (reliably) when one is lit |
| `GunModule.cs` | 99 | GunModule, GunBudget, GunModuleAccess | Guns on the server (GROUND.md §6.4, G4): shots, reloads and unloads from the player's own gun budget (a refused one |
| `PlacementModule.cs` | 52 | PlacementModule, PlacementModuleAccess | Placing anything (GROUND_SYSTEMS.md §6): one intent for every placeable, on the general budget, answered by |
| `RaidModule.cs` | 441 | RaidModule, RaidArena, RaidModuleAccess [ExplosiveView] | Raiding on the server (GROUND.md §6.6, G5): throw and place intents (a dud is lit again by a verb); explosives replicated to everyone in range |
| `ShipModule.cs` | 470 | ShipModule, ShipModuleAccess [ShipView] | Ships on the server (GROUND.md §6.7): the pilots' controls into their ships, ship intents, ships in every nearby |
| `SurvivalModule.cs` | 661 | SurvivalModule, GameCounters, SurvivalModuleAccess [OwnState, Looks, PendingRespawn] | The survival game on the server (GROUND.md §4.2, §6): players' intents (inventory, crafting, interaction, closing, |
| `ThingModule.cs` | 405 | ThingModule, ThingSent, ThingModuleAccess [ThingView] | Static things and natural objects on the server (GROUND.md §4.2, §6.1): every static thing gets a net ID and is |
| `WorldModule.cs` | 443 | WorldModule, WorldModuleAccess [WorldView] | The world on the server (GROUND.md §6.8, G7): the world's props placed at start as stored (their public state goes |

### src/Hollow.Server/Notifications (1 file, 369 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Notifications.cs` | 369 | RaidAlert, INotificationService, IPlayerPresence, INotificationStore, IPushChannel, NoPush, NotificationCounters, NotificationService, MemoryNotificationStore, DatabaseNotificationStore, RaidAlertCodec [BaseState, Window] | A raid alert as a player gets it (GROUND.md §6.6): which base (its claim device), where it stands (zone frame), when |

### src/Hollow.Server/Diagnostics (1 file, 355 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `CrashLog.cs` | 355 | CrashLog, CrashLogWriter | Crash reports (GROUND.md §4.3), for the game and the dedicated server alike, without the engine: the last 200 log lines, a report per unhandled exception, a still-running marker per process |

## src/Hollow.Client

The client without graphics: the host and its modules, prediction, the local player, the map, sounds' rules.

### src/Hollow.Client (21 files, 4,909 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `BuildingReplica.cs` | 212 | DoorState, ClaimState, BuildingReplica | What a client knows of a door beyond its geometry: its lock, and what the server tells this player of it |
| `ClientModule.cs` | 148 | ClientModule | One concern of the client (GROUND_SYSTEMS.md §12.5, K11): its own state, its message handlers, and hooks the host |
| `ClientModules.cs` | 30 | ClientModules | The client's modules and their order (GROUND_SYSTEMS.md §12.5, K11), the one place that lists them: the host calls |
| `ClientShipFrame.cs` | 92 | ClientShipFrame | A ship's frame on a client (IRON_WAKE.md §4.3, §4.8), keyed by the ship's net ID as the wire names frames: its space, |
| `ClientStats.cs` | 175 | ClientStats | What the network graph shows (GROUND.md §4.4): round trip, snapshot loss and rate, bandwidth both ways (with UDP |
| `GameClient.cs` | 850 | ClientState, ClientOptions, GameClient | How a client runs. |
| `InputFrame.cs` | 54 | InputFrame | One frame of the player's controls, as the game (or a bot) reads them. |
| `LocalGun.cs` | 629 | ShotEvent, ReloadEvent, UnloadEvent, GunRefusal, LocalGun, RecoilCompensation [Pending] | A shot this client fired, at once (predicted): for the muzzle flash, the sound, the tracer and the kick. |
| `LocalPlayer.Building.cs` | 80 | LocalPlayer | The building intents (GROUND.md §6.3, phase 2): the hammer's. Each returns its sequence number (0 if it couldn't be |
| `LocalPlayer.Interaction.cs` | 32 | LocalPlayer | The one interaction intent (GROUND_SYSTEMS.md §5): a verb done to a target, with a number (a tech tree's recipe) and a |
| `LocalPlayer.Placement.cs` | 115 | PendingPlacement, LocalPlayer | A placement sent and not yet answered (or answered a moment ago): drawn where it will stand in the ghost's pending look |
| `LocalPlayer.Raid.cs` | 65 | LocalPlayer | The player's raiding (GROUND.md §6.6, G5): throwing and placing explosives, and the raid alerts of the bases they are |
| `LocalPlayer.Ships.cs` | 27 | LocalPlayer | A seated player's keys (GROUND.md §6.7, IRON_WAKE.md §5.2): a door from a seat that works them, leaving the seat, the |
| `LocalPlayer.World.cs` | 302 | TeamMemberView, TeamView, TeamInviteView, TeammateView, MarkerView, RespawnPointView, MachineView, LocalPlayer | A member of the player's team as the interface shows them. |
| `LocalPlayer.cs` | 812 | VitalsView, CraftJobView, RespawnOption, LifeView, HitMarkerEvent, ItemsChangedEvent, DamageEvent, IntentAnswer, LocalPlayer [Prediction, Pending] | The player's vitals as the UI shows them. |
| `Predictor.cs` | 284 | Predictor | The player's own character, predicted (GROUND.md §4.3): every tick runs the server's movement code on the client's |
| `RemoteEntity.cs` | 234 | RemoteEntity | Another entity as this client sees it (GROUND.md §4.3): the states snapshots brought, each at its server tick, and |
| `RemoteExplosives.cs` | 90 | ClientExplosive, DetonationEvent, RaidAlertView | An explosive in the world as this client knows it (GROUND.md §6.6): a timed charge, bag charge or tin grenade the |
| `RemoteShip.cs` | 392 | RemoteShip | A ship as this client sees it (GROUND.md §6.7), as RemoteEntity sees a character: the states snapshots |
| `RemoteThings.cs` | 141 | RemoteThing, ClientProjectile | A static thing as this client knows it (GROUND.md §6): a deployable, a bag of dropped items or a corpse, sent reliably |
| `ShipPredictor.cs` | 145 | ShipPredictor | The ship the player flies, predicted (GROUND.md §6.7), as Predictor predicts the character: every tick |

### src/Hollow.Client/Audio (4 files, 924 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AmbienceMix.cs` | 168 | AmbienceInput, AmbienceLevels, AmbienceMix | Where the listener is, for the ambience (GROUND.md §4.9). Distances in metres, angles in degrees. |
| `Loudness.cs` | 238 | LoudnessResult, Loudness [KWeighting] | A sound's loudness (ITU-R BS.1770), as the sound pipeline measures it with ffmpeg's ebur128. |
| `SoundCatalogData.cs` | 261 | SoundCategory, SoundLayer, SoundEventData, SoundCatalogData | How loud the files of one category are made (GROUND.md §4.9): a metric, a target and a range in LUFS. |
| `SoundRules.cs` | 260 | DistanceLayers, FootSurface, FootGait, FootInput, FootEvent, Footsteps, FootstepCadence | Distance layering of loud sounds (GROUND.md §4.9): which layers play at a distance, and when they arrive. |

### src/Hollow.Client/Loading (5 files, 634 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `LoadStage.cs` | 131 | LoadScope, LoadStage | When a stage takes part in a load. |
| `LoadingPipeline.cs` | 271 | LoadPhase, LoadingPipeline | Where a game is in its loading. |
| `LoadingPlan.cs` | 146 | StagePlan, LoadingPlan | One stage of the plan: its words on the loading screen, its weight (the seconds it takes on the reference PC, which |
| `QuietStream.cs` | 38 | QuietStream | A stream that has gone quiet (GROUND.md §4.3.1): the server sends what is around a player that arrives as fast as its |
| `SteadyFrames.cs` | 48 | SteadyFrames | The last stage of the game's start (GROUND.md §4.3.1): a run of frames, each under the frame budget, long enough to |

### src/Hollow.Client/Map (3 files, 628 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `GlobeCamera.cs` | 187 | GlobeCamera | Where a map of the whole world looks (docs/WHOLE_WORLD.md §11): its centre, which way is up on the screen there, and |
| `GlobeProjection.cs` | 155 | GlobeProjection | How the map draws a whole world (docs/WHOLE_WORLD.md §11): a globe seen from afar that becomes a flat chart as it |
| `ZoneMap.cs` | 286 | MapPicture, ZoneMap, ZoneMapImage | The map's picture of a whole world: six square faces of Size × Size pixels, RGBA, one per cube face, stacked in |

### src/Hollow.Client/Modules (15 files, 3,397 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AnimalModule.cs` | 232 | AnimalEventKind, AnimalEvent, ClientAnimal, AnimalModule, AnimalModuleAccess | What happened to an animal in view, for the visual layer and sound (GROUND.md §6.9). |
| `ArrowModule.cs` | 106 | RemoteArrow, ArrowModule, ArrowModuleAccess | A spent arrow as this client knows it (GROUND_SYSTEMS.md §5): what it is, what holds it and its tip's place there, and |
| `BuildingModule.cs` | 66 | BuildingModule, BuildingModuleAccess | Building on the client (GROUND.md §4.3, §6.3, phase 2): the replicated structures to draw, the ghost check with the |
| `ConsoleModule.cs` | 90 | ConsoleAnswer, ZoneDay, ConsoleModule, ConsoleModuleAccess | A line of the server's answer to a console command (its number), or a line it sent by itself (0). |
| `DamageModule.cs` | 71 | DestroyedEvent, DamageModule, DamageModuleAccess | Something in range was destroyed (GROUND.md §6.4.2), for break-apart pieces, debris and the sound of it breaking. |
| `DevModule.cs` | 326 | DevPlayerState, ServerSection, ServerPlayer, DevLine, NavWindow, SpawnSlotView, SpawnAreaView, AnsweredIntent, DevModule, DevModuleAccess | A player's state as the F2 panel lists it. |
| `FireModule.cs` | 158 | ClientFire, FireModule, FireModuleAccess | A fire as this client knows it (IW4, IRON_WAKE.md §7): what left it (the round or explosive: its looks and sounds) and |
| `InteractionModule.cs` | 152 | InteractAim, InteractionModule, InteractionModuleAccess | What the interact key acts on now (GROUND_SYSTEMS.md §5): the target the one query found, what is known of it, and its |
| `LoadingModule.cs` | 89 | LoadingModule, LoadingModuleAccess [Backlog] | The client's part of the loading pipeline (GROUND.md §4.3.1): the connection (connected, welcomed, the character |
| `PlacementModule.cs` | 188 | PlacementModule, PlacementModuleAccess [ClientSurroundings] | Placing on the client (GROUND_SYSTEMS.md §6): the placement world over what this client has (its prediction physics |
| `RaidModule.cs` | 117 | RaidModule, RaidModuleAccess | Raiding in the client (GROUND.md §6.6, G5): the explosives in range as the server has them (drawn on their arcs, with |
| `ShipModule.cs` | 511 | ShipModule, ShipModuleAccess | Ships in the client (GROUND.md §6.7): every ship near, interpolated, with its details (doors, seats, health; IRON_WAKE.md |
| `StationModule.cs` | 535 | StationShotView, StationModule, StationModuleAccess [Pending] | The batteries in the client (IW3): records, others' shots, the gunner's predicted mount and shots matched by number. |
| `SurvivalModule.cs` | 303 | SurvivalModule, SurvivalModuleAccess | The survival game in the client (GROUND.md §4.3, §6): the local player's state and intents (LocalPlayer), static |
| `TargetModule.cs` | 887 | TargetModule, TargetModuleAccess [GroundView, CharacterView, AnimalView, NaturalView, ThingView, PieceView, DoorView, CupboardView, ExplosiveView, ShipView, ArrowView, FireView] | The world's things as this client knows them, named the server's way (GROUND_SYSTEMS.md §3.1, §5): one view per target |
| `WorldModule.cs` | 101 | WorldModule, WorldModuleAccess | The world in the client (GROUND.md §6.8, G7): the world's props in range (barrels, crates, recyclers: things of kind |

## game/scripts

The Godot project (presentation only): the app, the galaxy and system maps, the ground mode and its views, the interface.

### game/scripts/Animals (6 files, 1,691 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AnimalAssets.cs` | 101 | AnimalClip, AnimalModel, AnimalAssets | One of a model's clips, as art/animals/build_animals.py made it. |
| `AnimalSandbox.Scenes.cs` | 431 | AnimalSandbox [TreeGrid] | The sandbox's scenes: each at a clearing near the landing site with trees behind it, found once from the planet's |
| `AnimalSandbox.cs` | 458 | AnimalSandbox, AnimalWorldAngles [Person, Scene] | The animal sandbox (GROUND.md §6.9): --view animals. A zone of the game's rules runs in this process on the |
| `AnimalView.cs` | 488 | AnimalFrame, AnimalView | What an AnimalView draws, given every frame (GROUND.md §6.9): where the animal stands and faces, what it |
| `AnimalWarm.cs` | 15 | AnimalWarm | The animals' part of the renderer's warm-up (GROUND.md §4.3.1, §6.9): every species' model, skinned, as drawn. |
| `AnimalsView.cs` | 198 | AnimalsView | The animals in view (GROUND.md §6.9): one AnimalView per animal, made when it appears, given its frame |

### game/scripts/App (5 files, 715 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Checkout.cs` | 60 | Checkout | The repository this game runs from (the folder above game/): its branch and commit, read from git's own files. For bug |
| `CrashReporter.cs` | 124 | CrashReporter, CrashLogger | The game's crash reports (GROUND.md §4.3), in user://crashes: .NET's and Godot's exceptions, the marker, the menu's notice |
| `GalaxyService.cs` | 43 | GalaxyService | Owns the one Galaxy of the client: loads the real-star catalogue and builds the model. |
| `Main.cs` | 379 | Main | Entry point. Loads the galaxy and switches between the galaxy map and the system map. |
| `UiScaler.cs` | 100 | UiScaler | Keeps Ui.Scale matched to the window (see Automatic), times the player's own |

### game/scripts/Audio (7 files, 1,961 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AmbienceDirector.cs` | 127 | AmbienceDirector [Bed] | The world's ambience around the listener (GROUND.md §4.9). The surface view gives it, every frame or so, where |
| `AudioBuses.cs` | 134 | AudioBuses | The audio bus layout (GROUND.md §4.9), made in code so no .tres has to be kept in step: |
| `AudioDirector.cs` | 646 | SoundHandle, AudioDirector [Voice] | A playing sound (one event; a layered event's layers share it). Default: nothing. |
| `FootstepDriver.cs` | 55 | FootstepDriver | One character's footsteps (GROUND.md §4.9). The ground mode gives it the character's movement every frame with |
| `SoundLibrary.cs` | 131 | SoundLibrary, VariantBag | The sound catalogue in the game (GROUND.md §4.9): res://data/ground/sounds.json read once, its streams |
| `SoundMeter.cs` | 80 | SoundMeter | Loudness of the game's own sound files, measured in the engine: each stream is decoded through Godot |
| `SoundViewer.cs` | 788 | SoundViewer | The audition viewer (--view sounds, GROUND.md §4.9): every event of the catalogue in a list (a click plays |

### game/scripts/Build (11 files, 2,694 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `BuildHud.cs` | 104 | BuildHud | The build sandbox's lines over the ground HUD, in RustMVP's manner (GROUND.md §6.3): under the crosshair the target |
| `BuildSandbox.Check.cs` | 143 | BuildSandbox | --view build --scene empty --build-check: plays one round of building through the sandbox's own input paths (the aim, |
| `BuildSandbox.Joints.cs` | 212 | BuildSandbox | --view build --scene joints: the building's joints in every grade (GROUND.md §6.3, "Fit"), one group a grade on a |
| `BuildSandbox.cs` | 754 | BuildSandbox [Surroundings] | The build sandbox (--view build, GROUND.md §6.3): a local zone on the first world's stand-in surface with one |
| `BuildSandboxUi.cs` | 203 | BuildSandboxUi | The ground interface's view model for the build sandbox (GROUND.md §6.3, §8.1): a belt of building tools, the plan's |
| `BuildingAssets.cs` | 271 | BuildingAssets | The building kit's art in the game (GROUND.md §6.3, §9): the meshes of every piece in every grade at three levels of |
| `BuildingRadials.cs` | 132 | IHammerActions, PlanRadial, BuildingRadials | What the hammer's radial does to the piece aimed at: the game sends intents, the build sandbox applies the rules. |
| `BuildingRenderer.cs` | 430 | BuildingRenderer, DoorView [BatchKey, Batch] | Draws a BuildingWorld (GROUND.md §6.3): pieces batched into one MultiMeshInstance3D per |
| `BuildingWarm.cs` | 67 | BuildingWarm | The building's part of the renderer's warm-up (GROUND.md §4.3.1): every grade's material on its pieces' levels of |
| `CornerPosts.cs` | 161 | CornerPosts [End] | The posts that close the notch walls leave at an outer corner (GROUND.md §6.3, Fit; decision J5). Walls stand centred |
| `DemoBases.cs` | 217 | DemoBases [House] | Bases the sandbox builds for a look or a screenshot (GROUND.md §6.3), placed through the rules' sockets so every pose |

### game/scripts/Characters (16 files, 6,001 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `BowRig.cs` | 268 | BowRig | The bow while it is drawn (GROUND.md §4.8): its model's limbs bend back as the string is drawn (a "draw" blend shape |
| `CharacterAnimator.cs` | 278 | CharacterAnimator | The animation graph every third-person character shares (GROUND.md §4.8). One AnimationNodeBlendTree |
| `CharacterAssets.cs` | 331 | MeshPart, ClipInfo, BodyAssets, CharacterAssets | A skinned mesh of the character files and the levels of detail it is drawn at. |
| `CharacterLab.cs` | 934 | CharacterLab | The character viewer, --view character (GROUND.md §4.8): one character on a patch of ground, with keys to |
| `CharacterMaterials.cs` | 234 | CharacterMaterials | Materials of the character surfaces, by the surface (material) names the art scripts export. Skin and garments |
| `CharacterPoser.cs` | 325 | CharacterPoser | Procedural layer on top of the animation clips, run as the skeleton's modifier (GROUND.md §4.8): |
| `CharacterRagdoll.cs` | 219 | CharacterRagdoll [Joint] | The limp body after death (GROUND.md §4.8): fourteen capsules on the main bones under a |
| `CharacterTypes.cs` | 192 | BodyType, CharacterStance, HeldKind, CharacterEvent, CharacterFrame, BodyRegions | Which body a character has (GROUND.md §4.8): each has its own meshes, clothing and clips. |
| `CharacterView.cs` | 1233 | CharacterView [UseMotion] | A player as others see them (GROUND.md §4.8). Give it a CharacterFrame every frame with |
| `CharacterWarm.cs` | 67 | CharacterWarm | The characters' part of the renderer's warm-up (GROUND.md §4.3.1): each body as others see it and as its own arms in |
| `HandPoses.cs` | 227 | HandPoses [Grip] | Finger poses for holding things (first person; third person takes its fingers from the clips). The fingers of a |
| `HeldItems.cs` | 300 | HeldItemModel, HeldItems, HandFrame | An item that can be held: its item key, its name, how it is held and in which hand. |
| `Looks.cs` | 297 | Look, Looks, LookUniforms | How one player looks (GROUND.md §4.8, looks): skin tone, face, hair style and colour, stubble. The body (male or |
| `ViewmodelMotion.cs` | 124 | ViewmodelMotion [Ease, Key] | The first-person swing and reload as key poses (GROUND.md §4.8): each has an anticipation, an impact and a |
| `ViewmodelView.cs` | 767 | ViewmodelFrame, ViewmodelView | What the first-person view needs each frame (GROUND.md §4.8). |
| `Wardrobe.cs` | 205 | WearSlot, Wearable, Wardrobe | The seven wear slots (GROUND.md §6: 24 main, 6 belt, 7 wear). |

### game/scripts/Common (6 files, 1,045 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Labeller.cs` | 107 | LabelSide, Labeller | Where a label goes relative to the object it names. |
| `MapOverlay.cs` | 97 | MapLabel, MapOverlay [Text, Ring] | How a label on a map looks. |
| `StarColors.cs` | 59 | StarColors | Presentation colours for stars (blackbody-like, tuned to read well on a dark map). |
| `Ui.cs` | 666 | TextStyle, Ui [Ground] | Text styles of the interface. Each one is a Label theme type variation of the same name. |
| `UiIcons.cs` | 85 | UiIcon, UiIcons |  |
| `WarmItem.cs` | 31 | WarmItem, WarmSpot | One thing the game can draw, for the renderer's warm-up (GROUND.md §4.3.1): the files it is made of, loaded on Godot's |

### game/scripts/Fx (20 files, 5,410 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `FxAssets.cs` | 63 | FxAssets | The effects library's textures and meshes in res://assets/fx, loaded once (GROUND.md §9.3). |
| `FxBatch.cs` | 260 | FxBatch, FxOrient, FxBatches | One kind of particle for the whole world (GROUND.md §9.3): a GPUParticles3D in world space that never emits by |
| `FxCatalog.cs` | 436 | FxDirection, FxLayerType, FxLayer, FxLoopDef, FxTracerDef, FxDef, FxCatalog [Flame] | Where a layer's particles fly (GROUND.md §9.3). |
| `FxDecals.cs` | 172 | FxDecals [Entry, Pool] | Decals (GROUND.md §9.3): bullet holes, blood, scuffs and scorch marks projected onto what was hit. Each texture has |
| `FxDirector.cs` | 924 | FxParams, FxSurface, FxDirector, FxSurfaces [Spot] | Optional details of an effect (GROUND.md §9.3). Every field may be left at its default. |
| `FxFlashes.cs` | 201 | FxFlashes [Entry] | Muzzle flashes (GROUND.md §9.3): a pool of small card meshes that sit on a gun's muzzle socket (+X along the barrel) |
| `FxLights.cs` | 122 | FxLights [Entry] | Short lights for flashes and blasts (GROUND.md §9.3): a pool of shadowless omni lights that rise over an attack time |
| `FxLoops.cs` | 108 | FxHandle, FxLoop, FxFlameMeshes | A running loop (a fire, a trail), as FxDirector.Start returns it. |
| `FxRandom.cs` | 76 | FxRandom | A small, fast random source for effects (xorshift); seeded, so screenshots repeat. |
| `FxShake.cs` | 61 | FxShakeEvent, FxShake | A camera shake that started somewhere (GROUND.md §9.3): what the camera hook and other listeners get. |
| `FxStage.Ship.cs` | 421 | FxRocketLaunch, FxStage | How a battery launches its rocket (IRON_WAKE.md §6.4): the speed the gun gives it, how far out its motor lights, the |
| `FxStage.cs` | 361 | FxStation, FxStage | A place in the effects viewer where effects play (GROUND.md §9.3): its props, where and which way an effect plays, |
| `FxTracers.cs` | 178 | FxTracers [Tracer] | Tracers and trails (GROUND.md §9.3): bright streaks that fly the projectile's own path, with the same gravity and |
| `FxViewer.Action.cs` | 363 | FxViewer | The in-action scenes (--action &lt;family&gt;): each family as it would be seen in play, caught at a telling |
| `FxViewer.Gibs.cs` | 301 | FxViewer [GibCell] | Gibs in the effects viewer (GROUND.md §9.4). --gibs lays out every model that breaks (props, deployables, the |
| `FxViewer.Sheet.cs` | 266 | FxViewer [Cell] | The contact sheet (--sheet &lt;family&gt;): every effect of a family in its own cell, all caught at their |
| `FxViewer.Ship.cs` | 225 | FxViewer | The Iron Wake in the effects viewer (IRON_WAKE.md §11): --action ship, its siege battery firing over its deck |
| `FxViewer.Stress.cs` | 333 | FxViewer [Shooter] | The firefight measurement (--stress firefight --measure &lt;s&gt;, GROUND.md §9.3): ten players in two teams |
| `FxViewer.cs` | 504 | FxViewer | The effects viewer (--view fx, GROUND.md §9.3). By default every effect of the catalogue plays in turn on its |
| `FxWarm.cs` | 46 | FxWarm | The effects' part of the renderer's warm-up (GROUND.md §4.3.1, §9.3): every effect of fx.json played once, |

### game/scripts/GalaxyMap (5 files, 2,316 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `GalaxyGlow.cs` | 191 | GalaxyGlow | The galaxy's overall glow: a volume texture of light and dust computed from the density model on a worker |
| `GalaxyMapUi.cs` | 324 | GalaxyMapUi | The galaxy map's interface: where the camera is, search, the selected system, key hints and status. |
| `GalaxyMapView.cs` | 986 | GalaxyMapView | The galaxy map (GALAXY.md §11): the whole Milky Way as a glowing volume, real and generated stars streamed |
| `MapCamera.cs` | 97 | MapCamera | Orbit camera for the galaxy map, in double precision (galactic frame, light-years). Inputs set targets; |
| `StarStreamer.cs` | 718 | StarStreamer [BlockKey, Job, BlockEntry, CellCache, BlockResult, StarBlock] | Streams galaxy cells around the camera focus (GALAXY.md §11). Each level is shown out to a radius that grows |

### game/scripts/Gibs (5 files, 1,399 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `GibCatalog.cs` | 178 | GibMaterial, GibCause, GibCatalog | A material's pieces (gibs.json): how they bounce, slide and spin, their dust (an effect, tinted) and sound. |
| `GibDirector.cs` | 98 | GibBreak, GibDirector | What a break did (GROUND.md §9.4): whether pieces were placed or only dust, the middle of what broke, and the sounds |
| `GibLibrary.cs` | 290 | GibChunk, GibSet, GibLibrary | One piece of a model (GROUND.md §9.4): its mesh (the model's own materials), where it sits in the model |
| `GibWarm.cs` | 35 | GibWarm | The gibs' part of the renderer's warm-up (GROUND.md §4.3.1, §9.4): every gib file the catalogue names, read, and a |
| `GibWorld.cs` | 798 | GibFloor, GibWorld [PieceState, Turn, Piece, BreakFloor, Batch] | Where the floor is under a point: a ray from  along  for |

### game/scripts/Ground (46 files, 14,075 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `ArrowsView.cs` | 81 | ArrowsView | Spent arrows stuck where they hit (GROUND_SYSTEMS.md §5): every one this client knows, placed each frame where its holder |
| `BuildPanels.cs` | 232 | Keypad, RectSnapping | The code lock's keypad (GROUND.md §6.5), in the RustMVP interface's manner: a dark dialog in the middle of the |
| `BuildView.cs` | 387 | BuildView, GodotVectors | Building in the ground game (GROUND.md §6.5, phase 2b): the replicated structures drawn by the building package's |
| `ChildServer.cs` | 188 | ChildServer | A game hosted on this computer in a process of its own (GROUND.md §4.3): the dedicated server (Hollow.Server, built |
| `ClientConsole.cs` | 606 | ViewSettings, ClientConsole | The view's settings the console sets (GROUND.md §8.2), kept on this computer: field of view, mouse sensitivity, the frame rate shown. |
| `ClientDeveloper.cs` | 327 | ClientDeveloper | The F2 panel's model over the client (GROUND.md §8.5): the server's stats, sections and players as the client's dev |
| `ClientGroundUi.Interaction.cs` | 228 | ClientGroundUi, InteractionRadial | The interact key in the view model (GROUND_SYSTEMS.md §5, GROUND.md §8.4): every frame the one targeting query says |
| `ClientGroundUi.World.cs` | 447 | ClientGroundUi | The interface's G7 members over the client (GROUND.md §6.8): the zone's map and what is on it (the player, teammates, |
| `ClientGroundUi.cs` | 669 | ClientGroundUi | The ground interface's view model over the client (GROUND.md §8.1): GroundUi reads the player's own |
| `DevDraw.cs` | 475 | DevDraw | The F2 panel's drawings in the world (GROUND.md §8.5), over the client's dev module's toggles: the hitboxes of every |
| `FiresView.cs` | 137 | FiresView [Shown] | Fires in the ground game (IW4, IRON_WAKE.md §7, GROUND.md §9.3): each pool of burning fuel the server tells of is the |
| `FirstPersonView.cs` | 262 | FirstPersonView | The local player in first person (GROUND.md §4.8): the character package's ViewmodelView holding the |
| `FrameEvents.cs` | 29 | FrameEvents | What happened in this frame that may cost time (for --measure's report of the slowest frames). |
| `GibsView.cs` | 53 | GibsView | Things breaking apart in the ground game (GROUND.md §9.4): whatever the damage pipeline's Destroyed event names |
| `Glow.cs` | 52 | Glow | Light and glow in the planet's own units (GROUND.md §4.6): the sky meters the camera like an eye, a few thousand |
| `GroundAudio.cs` | 402 | GroundAudio, Sounds | The ground game's sound (GROUND.md §4.9), through the audio package: one AudioDirector listening at |
| `GroundChecks.cs` | 20 | GroundChecks | Checks of the ground game that run inside the client process. |
| `GroundCollision.cs` | 76 | GroundCollision | The ground near the player in Godot's own physics (layer 1), for what Godot simulates: ragdolls of the dead and |
| `GroundMeasure.cs` | 182 | GroundMeasure [Probe] | Measures the ground game's frame time where it is played (GROUND.md §5, --measure &lt;s&gt;): once the world |
| `GroundMode.Loading.cs` | 126 | GroundMode | The ground game's start (GROUND.md §4.3.1): one loading pipeline per game, in which the parts that make the game |
| `GroundMode.cs` | 407 | GroundMode [MenuCamera] | The ground game (GROUND.md §4.3). With neither --host nor --connect it opens the main menu over the |
| `GroundModels.cs` | 266 | IGroundModels, GroundModels, CatalogModels, PlaceholderModels | Where the ground game's world models come from (GROUND.md §9): deployables, bags of dropped items, outcrops and the |
| `GroundOverlay.cs` | 226 | GroundOverlay [Crosshair] | What the ground game shows besides the RustMVP interface: other players' names over their heads (placed by |
| `GroundTerrain.cs` | 132 | GroundTerrain [TileArrays] | Placeholder ground: the collision tiles of ISurface.GroundCollision drawn as plain meshes; only the build sandbox draws with it since S3c |
| `GroundTour.cs` | 2487 | GroundTour | The scripted screenshot run of the ground game (GROUND.md §5, --tour &lt;folder&gt;): on the host, a walk |
| `GroundView.cs` | 1031 | GroundView | The ground game in first person (GROUND.md §4.3) on a whole world: the surface view draws the ball in the zone's own frame (docs/WHOLE_WORLD.md §10) |
| `GroundWarmup.cs` | 42 | GroundWarmup | Everything the ground game can draw, for the renderer's warm-up (GROUND.md §4.3.1): each library's items, read from its |
| `GunEffects.cs` | 202 | GunEffects | Shots, hits and guns in hand through the effects library and the model catalogue (GROUND.md §6.4, §9.3): which |
| `InventoryBody.cs` | 51 | InventoryBody | The player's own body on the inventory screen (GROUND.md §8): the character package's CharacterView |
| `ItemIconAliases.cs` | 103 | ItemIconAliases | The ground game's item icons until the assets package renders them from our models (GROUND.md §8): items.json keys |
| `ItemLooks.cs` | 35 | ItemLooks | How items look on a character (GROUND.md §4.8): the held model and pose an items.json key takes in the hands (the |
| `LoadTrace.cs` | 348 | LoadTrace [Late] | The ground game's start, timed (--load-trace [seconds], GROUND.md §4.3.1): from the process's start, when the |
| `MarkerTextures.cs` | 224 | MarkerTextures [Style] | The weak spots' textures (GROUND.md §9.3), painted in code once and kept (our own art): gather.json's |
| `MenuGroundUi.cs` | 53 | MenuGroundUi | The interface's view model before a game exists (the main menu): nothing carried, nothing to show. |
| `NatureMarkers.cs` | 226 | NatureMarkers [Marker] | The weak spots drawn (GROUND.md §6.1, §9.3), one generic marker for every class: gather.json's |
| `NaturePlants.cs` | 65 | NaturePlants | The ground game's plants (docs/WHOLE_WORLD.md §10.3): the zone's natural objects, by their tiles, as ScatterItems of the kind gather.json names |
| `NatureView.cs` | 798 | NatureView [PendingSound, Seen, TreeFall] | The natural objects as the game changed them (GROUND.md §6.1): the plants' changes, outcrops and pickups drawn as models, weak spots, gathering's feedback, falling trees |
| `NetGraph.cs` | 72 | NetGraph | The network graph (GROUND.md §4.4): the last 30 s of round trip, bandwidth down and prediction error, one line each, |
| `PeopleView.cs` | 529 | PeopleView [Person, Corpse] | The other players (GROUND.md §4.8): each a CharacterView fed every frame from what the client |
| `PlacementGhost.cs` | 192 | PlacementGhost | The one placement ghost (GROUND_SYSTEMS.md §6): whatever is being placed, drawn as its own model in one hologram |
| `PlacementView.cs` | 118 | PlacementView | Placing in the game (GROUND_SYSTEMS.md §6): the one placement controller, behind IPlacement for the held |
| `RaidView.cs` | 554 | RaidView, RaidAlertsPanel [Shown, Card] | Raiding in the ground game (GROUND.md §6.6): explosives drawn from the model catalogue on their arcs, stuck to what |
| `RenderWarmup.cs` | 225 | RenderWarmup | The renderer's warm-up (GROUND.md §4.3.1, the loading stage renderer). Every file the game's libraries list |
| `SeatKeys.cs` | 113 | SeatAction, SeatKey, SeatKeys | What a seat's key does (IRON_WAKE.md §5.2): leave it, work a door, the lock, descend from orbit, the camera. |
| `ShipsView.cs` | 206 | ShipsView [Drawn] | Ships in the ground game (G6, GROUND.md §6.7): every ship the client knows drawn by a ShipView, others' |
| `ThingsView.cs` | 403 | ThingsView | Placed and dropped things (GROUND.md §6.1): deployables and bags of dropped items from GroundModels, a lit |

### game/scripts/Ground/Held (4 files, 464 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AmmoRadial.cs` | 111 | AmmoRadial | The gun in hand's rounds (GROUND_SYSTEMS.md §7), R held: every kind of round of its calibre the player carries, with |
| `HeldControl.cs` | 123 | HandPress, HandButtons, HeldContext, IHeldControl, HeldControls | Buttons pressed for the hands since the last frame: attack (left mouse), aim (right mouse), R. |
| `HeldControls.cs` | 209 | MeleeControl, BowControl, FirearmControl, ThrowableControl, PlacerControl, PlannerControl, HammerControl, UsesItsPartControl | The held controls (GROUND_SYSTEMS.md §7), one per held behaviour; registered in HeldControls.Create. |
| `Placement.cs` | 21 | IPlacement | Placing what is in hand (GROUND_SYSTEMS.md §6, package P2's system): the placer's, the planner's and a charge's one |

### game/scripts/Items (6 files, 1,914 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `CatalogItemIcons.cs` | 119 | CatalogItemIcons | Item icons rendered from the models (GROUND.md §9): the catalogue's PNG nearest above the asked size, resampled |
| `IconRenderer.cs` | 401 | IconRenderer, IconSheet | The icon tool (--view icons, GROUND.md §9). With --out &lt;dir&gt; it renders every catalogue model |
| `ModelCatalog.cs` | 534 | Pose, HeldInfo, GripInfo, GibsInfo, ModelEntry, ModelUse, ModelCatalog | A position (metres) and a rotation (degrees, Godot's Euler order YXZ) from the catalogue. |
| `ModelViewer.cs` | 674 | ModelViewer | The model viewer (--view models, GROUND.md §9). By default every catalogue model stands on the ground in |
| `ModelWarm.cs` | 47 | ModelWarm | The model catalogue's part of the renderer's warm-up (GROUND.md §4.3.1): every model of models.json (items, |
| `SurfaceSurvey.cs` | 139 | SurfaceSurvey | Measures the drawn surfaces of the natural objects the ground game draws from catalogue models (the outcrops at |

### game/scripts/Monuments (1 file, 624 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `MonumentViewer.cs` | 624 | MonumentViewer [Monument] | The monument viewer (--view monuments, MONUMENTS.md §8.5): every monument in data/monuments laid out |

### game/scripts/Settings (3 files, 682 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `GraphicsNode.cs` | 60 | GraphicsNode | Applies the graphics settings to the window and everything global whenever they change (GROUND.md §8.6), and draws the sharpening pass |
| `GraphicsSettings.cs` | 470 | QualityPreset, AntiAliasingMode, GraphicsValues, GraphicsSettings [FogLook] | The game's graphics settings (GROUND.md §8.6): the values shown and saved, the presets, one function per thing they reach |
| `SettingRows.cs` | 152 | SettingKind, SettingRow, SettingRows | One row of the settings panel (GROUND.md §8.6): its name, a one-line explanation, and how it reads and changes the values |

### game/scripts/Ships (8 files, 2,748 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `ShipCamera.cs` | 89 | ShipCameraMode, ShipCamera | First person, from the pilot's eye in the cockpit, looking along the nose. |
| `ShipControls.cs` | 104 | ShipControls | The pilot's keyboard and mouse (G6, GROUND.md §6.7), read into a tick of ShipInput. The mouse is a |
| `ShipHud.cs` | 343 | ShipHudData, ShipHud | What the flight HUD shows this frame (the host fills it from the zone or the client's prediction). |
| `StationAmmoRadial.cs` | 94 | StationAmmoRadial | A gunner's rockets (IW3), R held at a station: the kinds and counts; a tap cycles. |
| `StationSight.cs` | 243 | SightBarrel, SightKind, StationSightData, StationSight | The gunner's sight (IW3, IRON_WAKE.md §6.8) in the ground interface's style. |
| `ShipSandbox.Aboard.cs` | 609 | ShipSandbox [Walk] | The ship sandbox for a ship people walk in (IRON_WAKE.md §4, IW1): `--view ship --ship iron-wake`. The ship parked in a |
| `ShipSandbox.Tour.cs` | 215 | ShipSandbox [Leg] | --tour: the sandbox played by a script through the same keys a player presses (Input.ParseInputEvent), to check the |
| `ShipSandbox.cs` | 899 | ShipSandbox [Site] | The ship sandbox (--view ship, GROUND.md §6.7): the Tin Finch on the first world's ground (the stand-in world |
| `ShipSounds.cs` | 468 | ShipSounds | What one ship sounds like (IW6, IRON_WAKE.md §10): every role of its kind's in sounds.json from the replicated state, slow and fast loops crossfaded. |
| `ShipView.cs` | 661 | ShipLook, ShipView [PartNode, Drive, Plume] | What a ship is doing this frame, as a ShipView draws it: plumes, lamps by power, reflection probes, sockets where drawn. |
| `StationsView.cs` | 337 | StationsView [LoaderSeen, SlotModel, StationSeen, ShipSeen] | The batteries drawn and heard (IW3): mounts, recoil, loader pieces by the table, rockets in slots, phases' sounds, shots. |
| `ShipWarm.cs` | 28 | ShipWarm | The ships' part of the renderer's warm-up (GROUND.md §4.3.1, §6.7): every ship of ships.json as |

### game/scripts/Surface (18 files, 4,501 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AerialTable.cs` | 114 | AerialTable | The aerial-perspective table of the surface view (GROUND.md §4.6): the air's in-scattered light and transmittance by direction and distance (400 km on a real planet, 10 km on a whole small world) |
| `AtmosphereModel.cs` | 449 | AtmosphereModel | The physically based air of a planet for drawing (GROUND.md §4.6): Rayleigh scattering from its molecules, Mie |
| `GroundLayers.cs` | 120 | GroundLayers | The ground's sixteen 4K texture layers (WALKING_GROUND.md §8), read from their imported files into three texture arrays every ground material shares: colour, normals, roughness and height; the layers' sizes and normal flips |
| `GrassField.cs` | 561 | GrassField [FaceLattice, FieldData, FaceGrid] | The grass of the surface view (GROUND.md §4.6, docs/WHOLE_WORLD.md §10.3): tufts on cube-face cells (level 13 on a 2.41 km world) placed by the GPU from a lattice of the cells' corners on the drawn ground, one grid a face near a seam |
| `NoiseTextures.cs` | 211 | NoiseTextures | Tiling noise textures for the surface's shaders (presentation only): the cloud field, and the large-scale |
| `PlanetGlobals.cs` | 96 | PlanetGlobals | The planet under everything drawn (docs/WHOLE_WORLD.md §10): its centre, radius and axes as shader globals (surface_planet.gdshaderinc) and for what moves along up (effects, gibs, tracers) |
| `PlantSource.cs` | 37 | IPlantSource, SurfacePlants | Where the surface view's plants come from (docs/WHOLE_WORLD.md §10.3): tiles of ScatterItems; SurfacePlants, a surface's own scatter |
| `RenderFrame.cs` | 116 | RenderFrame | The frame Godot's world is drawn in (ARCHITECTURE.md §6): World, the body's own frame (the ground game's whole world), or a tangent frame of a large planet whose origin follows the camera (look-dev) |
| `SurfaceSky.cs` | 259 | SurfaceSky | The sky of the surface view at a universe time (GROUND.md §4.6): the real directions of the planet's sun, its moon, the other suns and planets, as lights hidden below the drawn world's horizon |
| `SurfaceUi.cs` | 97 | SurfaceUi | The surface view's overlay: where you are, the local time, the sun and moon, and the frame time. |
| `SurfaceView.Game.cs` | 47 | SurfaceView | The surface view driven by a game (GROUND.md §4.3): the game drives the camera and owns the keys; OpenZone opens a real planet in a tangent frame at a point (the main menu's backdrop) |
| `SurfaceView.Loading.cs` | 62 | SurfaceView | The surface view's part of the loading pipeline (GROUND.md §4.3.1): what IsReady waits for, as two stages with their |
| `SurfaceView.cs` | 614 | SurfaceView | The surface view (GROUND.md §4.6): a planet's ground, sea, plants, grass, air and sky; the look-dev view of a real planet (Open) and the ground game's whole world from any ISurface (OpenWorld, docs/WHOLE_WORLD.md §10) |
| `TerrainLod.Loading.cs` | 28 | TerrainLod | What the loading pipeline reads of the terrain (GROUND.md §4.3.1). |
| `TerrainLod.cs` | 679 | TerrainLod, TileMeshData [Node, Job] | Draws a planet's ground out to the horizon from any ISurface (GROUND.md §4.6): a quadtree of tiles on each cube face, the finest level by size, each tile with its face and texture coordinates in metres on it (UV2) |
| `Vegetation.Game.cs` | 151 | Vegetation | What the ground game changed about its plants (GROUND.md §6.1): felled trees, picked ferns and berry bushes; near sets rebuilt on a worker |
| `Vegetation.Loading.cs` | 17 | Vegetation | What the loading pipeline reads of the plants (GROUND.md §4.3.1). |
| `Vegetation.cs` | 848 | Vegetation [LodSet, Block, LodState, NearData, BlockingQueue] | Trees, bushes, berry bushes, ferns and rocks of the surface view (GROUND.md §4.6) from a plant source (IPlantSource), at its tiles' level, ranges from the horizon |

### game/scripts/Surface/Flora (5 files, 2,083 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `FloraGeometry.cs` | 721 | GeometryBuilder, FloraGeometry [Part, Conifer, Broadleaf, Shrub] | Builds plant and rock meshes in code (presentation only), after hollow-orbit's flora: trunks and limbs as tubes |
| `FloraLibrary.cs` | 288 | FloraLibrary [Bands, Variant, Species, Data] | The plants and rocks of the surface view (GROUND.md §4.6), built in code after hollow-orbit's flora: species for a |
| `FloraTextures.cs` | 590 | FloraTextures | The painted textures of the surface view's plants (presentation only), after hollow-orbit's flora: a 4 × 4 atlas of |
| `ImpostorBaker.cs` | 185 | ImpostorBaker [Result] | Renders every tree variant of the flora library from eight sides into two atlases (GROUND.md §4.6): colour with |
| `Painter.cs` | 299 | Painter | A small 2D vector painter for the foliage atlas (presentation only): paths of lines and quadratic curves, filled |

### game/scripts/SystemMap (4 files, 1,598 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `BodyText.cs` | 146 | BodyText | Words for a body's physical state (GALAXY.md §10.3) on the body card (presentation only). |
| `PlanetLook.cs` | 200 | PlanetLook | Colours and surface parameters of planets (presentation only): a base look by type, then oceans, lava, lakes, ice |
| `SystemMapUi.cs` | 566 | SystemMapUi | The system map's interface: system summary, body list, selected body, time and scale controls. |
| `SystemMapView.cs` | 686 | SystemMapView [BodyView] | The system map (GALAXY.md §12): an orrery of one star system, animated by universe time. Distances are |

### game/scripts/UI (20 files, 6,114 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `CharacterPreview.cs` | 133 | CharacterPreview | The figure on the inventory screen: a plain wooden mannequin in its own little world, rendered into a texture the |
| `GroundUi.Console.cs` | 739 | GroundUi | The F1 panel (GROUND.md §8.2): Rust's F1 menu in the ground interface's style, our own drawing. One dark translucent |
| `GroundUi.Crafting.cs` | 239 | GroundUi | The crafting screen (RustMVP's, from SBareRoot.cpp and its Godot predecessor): categories with counts, the |
| `GroundUi.Death.cs` | 193 | GroundUi | The death screen in Rust's layout: DEAD and how it happened across the top, the map with the body and the |
| `GroundUi.Developer.cs` | 404 | GroundUi | The F2 panel (GROUND.md §8.5): the developer's window on the game while playing it, in the F1 panel's shape and |
| `GroundUi.Hud.cs` | 229 | GroundUi | The HUD (RustMVP's, from SBareRoot.cpp): the centre dot, hit marker, damage direction and prompt in the middle; |
| `GroundUi.Inventory.cs` | 447 | GroundUi [DragState] | The inventory (RustMVP's, from SBareRoot.cpp): the character and clothing on the left, the item panel and the |
| `GroundUi.Machines.cs` | 69 | GroundUi | The loot panels of G7's machines (GROUND.md §6.8), in the furnace's manner: the recycler's six inputs and six outputs |
| `GroundUi.Globe.cs` | 797 | GroundUi [Globe, GlobeFrame] | The map control (docs/WHOLE_WORLD.md §11), one for the G screen and the death screen: the whole world as a globe seen |
| `GroundUi.Map.cs` | 273 | GroundUi | The map (G7, GROUND.md §6.8; the whole world since S3d, docs/WHOLE_WORLD.md §11), on G as in Rust: the map control |
| `GroundUi.Menu.cs` | 132 | GroundUi | A small main menu for playtests, in RustMVP's menu style: the title and a column of big condensed items on the |
| `GroundUi.Paint.cs` | 381 | GroundUi | Immediate-mode painting on the design canvas, as RustMVP's SBareRoot painted: rectangles, text, icons and lines at |
| `GroundUi.Radial.cs` | 355 | GroundUi [Trigger] | The radial menu (GROUND_SYSTEMS.md §4; RustMVP's wheels, from SBareRoot.cpp): one control for every source of options. |
| `GroundUi.Settings.cs` | 217 | GroundUi | The settings panel (GROUND.md §8.6), opened by the menu's SETTINGS: GRAPHICS and INTERFACE tabs, a row per setting with its value and a line saying what it does |
| `GroundUi.TechTree.cs` | 214 | GroundUi | The tech tree of the workbench in reach (G7, GROUND.md §6.8), in RustMVP's manner: the tree laid out from its roots |
| `GroundUi.cs` | 692 | GroundScreen, GroundUi [HitKind, Hit, Pickup] | The ground interface's screens; the HUD shows under the inventory and crafting screens too. |
| `ItemIcons.cs` | 216 | IItemIconProvider, ItemIcons, PlaceholderItemIcons, GroundGlyphs | Where item icons come from. The icon package will render them from our models; until then, placeholders. |
| `LoadingScreen.cs` | 199 | LoadingScreen | The loading screen (GROUND.md §4.3.1, §8): a picture of the first world filling the window, the game's name, the |
| `Painter.cs` | 19 | Painter | A full-window layer whose drawing is done by its owner (the ground interface paints in immediate mode). |
| `RadialGlyphs.cs` | 198 | RadialGlyphs | The radial menu's outline glyphs (RustMVP's own perspective outlines, from SBareRoot.cpp, and ours in their manner), by |

### game/scripts/UI/Model (12 files, 2,258 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `ConsoleUiTypes.cs` | 124 | ConsoleTab, ConsoleLineKind, ConsoleLine, ConsoleCompletionView, InfoRow, InfoSection, NetGraphView, ToolButton, ToolGroup, ToolsView, IConsoleModel | The F1 panel's values (GROUND.md §8.2): plain C#, no Godot types, as the rest of the view model. The game's adapter |
| `DeveloperUiTypes.cs` | 91 | DeveloperTab, TickGraphView, SectionRow, PlayerRow, DrawToggle, IDeveloperModel | The F2 developer panel's values (GROUND.md §8.5): plain C#, no Godot types, as the rest of the view model. The game's |
| `GroundUiTypes.cs` | 161 | ContainerId, SlotRef, ItemStack, ItemStat, ItemActionView, ItemInfo, ContainerKind, LootView, Privilege, Vitals, HeldAmmo, Ingredient, Recipe, CraftCategory, CraftJob, RespawnBag, DeathInfo, InteractionPrompt, ChatLine | The values the ground game's interface shows (GROUND.md §8). Plain C#, no Godot types: the client API adapts its |
| `IGroundUi.cs` | 243 | IGroundUi | The ground game's view model (GROUND.md §8): everything the HUD, inventory, crafting, death and radial screens show, |
| `KeyContext.cs` | 34 | KeyContext, KeyContexts | What the keys mean now (KNOWN_PROBLEMS.md finding 28; GROUND.md §6.7): on foot, or seated by the seat's role. The ground |
| `MockConsole.cs` | 161 | MockConsole | The F1 panel on sample data (GROUND.md §8.2), for the interface viewer (--view ui --screen console, and |
| `MockDeveloper.cs` | 152 | MockDeveloper | The F2 panel on sample data (GROUND.md §8.5), for the interface viewer (--view ui --screen developer, and |
| `MockGroundUi.Radials.cs` | 161 | MockGroundUi [SampleDoor] | The sample radials (GROUND_SYSTEMS.md §4): the right mouse opens the held plan's or hammer's; E held on the HUD opens |
| `MockGroundUi.World.cs` | 237 | MockGroundUi | The mock's G7 world (GROUND.md §6.8): the first world's real map round the landing site, a few markers, a team with an |
| `MockGroundUi.cs` | 680 | MockGroundUi | A stand-in for the client API (GROUND.md §8): a survivor with a believable inventory, recipes, a crafting queue, |
| `RadialTypes.cs` | 145 | RadialIcon, RadialOption, IRadialSource, RadialInput, RadialBinding, RadialMenu | The radial menu's contract (GROUND_SYSTEMS.md §4, GROUND.md §8): one control, drawn in RustMVP's look, on any source |
| `WorldUiTypes.cs` | 69 | MapView, MapMarkerKind, MapMarkerView, TechNodeState, TechNodeView, TechTreeView, TeamMemberInfo, TeamInfo, TeamInviteInfo | The values the map, tech tree, team and machine screens show (GROUND.md §6.8, §8, G7). Plain C#, no Godot types: the |

### game/scripts/UI/Viewer (2 files, 412 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `UiBackdrop.cs` | 65 | UiBackdrop | A plain scene behind the interface viewer, so the panels' translucency and blur can be judged against something |
| `UiViewer.cs` | 347 | UiViewer | The interface viewer (--view ui, GROUND.md §8): the ground interface on MockGroundUi over a |

## tools

Tools: the bots and the load test, the message generator, the generator's reports, the catalogue builder, the map export.

### tools/Hollow.Bots (11 files, 3,393 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `BotBrain.cs` | 120 | BotScript, BotBrain |  |
| `BotGround.cs` | 82 | BotGround | The bots' ground on a whole world (docs/WHOLE_WORLD.md §13): the landing site, each bot's home (where it arrived), |
| `BotSwarm.cs` | 199 | BotSwarm | Many headless clients in one process, on one thread (GROUND.md §4.3): each a full GameClient with |
| `BuildBrain.cs` | 579 | BuildBrain [Stage] | A bot that builds a small base (GROUND.md §5.3) through the client's building API, as a player would: it walks to a |
| `FightBrain.cs` | 314 | FightBrain | A bot that fights with guns (GROUND.md §6.3, the G4 load test) through the same client API as a player: it keeps its |
| `FlyBrain.cs` | 159 | FlyBrain | A bot that flies (G6, GROUND.md §6.7): it walks to the hatch of the ship the server gave it (ShipOnJoin), boards |
| `Interacting.cs` | 37 | Interacting | The interact key, as a bot presses it (GROUND_SYSTEMS.md §5): a tap does a target's primary option from where the bot's |
| `Program.cs` | 454 | Program | Headless clients (GROUND.md §4.3). |
| `RaidBrain.cs` | 314 | RaidBrain | A bot that raids (GROUND.md §5.5, G5) through the same client API as a player: its kit (charges, bag charges, |
| `SurvivalBrain.cs` | 378 | SurvivalBrain [Goal] | A bot that plays the survival game (GROUND.md §6) through the same client API as a player: it walks to trees and |
| `WorldBrain.cs` | 757 | WorldBrain [Goal] | A bot that lives off the world's loot (GROUND.md §6.8, G7) through the same client API as a player: it walks to the |

### tools/Hollow.CatalogBuilder (1 file, 948 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Program.cs` | 948 | Program [Row] | Converts a star catalogue (CSV) into Hollow Universe's compact catalogue (GALAXY.md §8). |

### tools/Hollow.GeneratorReport (7 files, 3,070 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `Calibrate.cs` | 80 | Calibrate | The class table against the real neighbourhood: per level, inside the distance where the catalogue is complete |
| `Maps.cs` | 408 | Maps [Layer] | Galaxy-wide maps of what the generator makes, one tile per level (GALAXY.md §7.1): generated stars seen face-on |
| `Png.cs` | 92 | Png | A minimal PNG writer (8-bit RGB, Paeth-filtered rows): enough for the generator's maps. |
| `Program.cs` | 481 | Program | Measurements of the generator that are too slow or too visual for the test suite: generation speed, and later |
| `Shells.cs` | 170 | Shells | Real plus generated stars per level in shells around Sol, against what the density model expects |
| `SurfaceReport.cs` | 564 | SurfaceReport | The first world's surface (GROUND.md §3, §4.6): maps, speed, the landing-site search and the times of day used for |
| `WalkingReport.cs` | 1278 | WalkingReport [Shot, Spot] | The walking-scale ground (WALKING_GROUND.md §15): equal-area maps, globe views, close-ups with the scatter, slope maps, places for close-ups and for the game's ground-level shots, the ground's materials and plants at a point, the stand-in beside it, speed and numbers |

### tools/Hollow.MapExport (4 files, 1,404 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `GalaxyExport.cs` | 522 | GalaxyExport | galaxy.js: a galaxy-wide star sample drawn from the density model and the class table, a face-on glow |
| `LocalExport.cs` | 279 | LocalSystem, LocalData, LocalExport | One system of the local bubble, with its generated star system. |
| `MockExport.cs` | 419 | MockExport | mock.js: a MOCK gameplay layer (stations, bases, danger regions, resources, the player's ship). None of this |
| `Program.cs` | 184 | Program, Js, StarColours | Exports read-only snapshots of the real generator for the galaxy-map UX prototypes in |

### tools/Hollow.NetGen (3 files, 792 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `NetGenerator.Emit.cs` | 319 | NetGenerator |  |
| `NetGenerator.cs` | 416 | NetGenException, NetGenerator [StructKind, FieldKind, Field, StructDef] | A mistake in the message definitions, with the line it is on. |
| `Program.cs` | 57 | Program | Regenerates src/Hollow.Net/Protocol/Messages.g.cs from Messages.cs (ARCHITECTURE.md §11.2). |

## tests/Hollow.Tests

The tests, one file per area (the runner needs no packages).

### tests/Hollow.Tests (57 files, 16,419 lines)

| File | Lines | Types | What |
|---|---|---|---|
| `AnimalNetTests.cs` | 191 | AnimalNetTests |  |
| `AnimalTests.cs` | 552 | AnimalRig, AnimalTests | Animals in a GameRig: the grid built on the test's thread, herds placed by hand. |
| `ArchitectureTests.cs` | 202 | ArchitectureTests |  |
| `BuildingFitTests.cs` | 104 | BuildingFitTests | The building's drawn meshes against their slots (GROUND.md §6.3, Fit), read from the GLBs without Godot: every level of |
| `BuildingNetTests.cs` | 384 | BuildingNetTests | Building over the network (GROUND.md §6.3, phase 2): intents, replication, streaming and locks. |
| `BuildingPersistenceTests.cs` | 151 | BuildingPersistenceTests | The building in storage (GROUND.md §4.7, §6.3, phase 2): a base survives a restart, and decay catches up on load. |
| `BuildingTests.cs` | 735 | BuildingTests [Alone, CliffSurface, RampSurface] | GROUND.md §6.3: the building rules, engine-free. |
| `CharacterAssetTests.cs` | 103 | CharacterAssetTests |  |
| `ConsoleTests.cs` | 321 | ConsoleTests | The F1 console (GROUND.md §8.2): its words, completion, rights, commands over the network, and cheats. |
| `ContentTests.cs` | 131 | ContentFixture, ContentTests | The ground game's data (GROUND.md §7), loaded once for every test that needs it. |
| `CoreTests.cs` | 112 | CoreTests |  |
| `DamageTests.cs` | 153 | DamageTests | The damage pipeline's Destroyed event (GROUND.md §6.4.2), in the zone and over the network. |
| `DevTests.cs` | 63 | DevTests | The F2 developer panel's server side (GROUND.md §8.5), over the network. |
| `EffectsCatalogueTests.cs` | 105 | EffectsCatalogueTests | The effects catalogue's items (GROUND.md §9.3, IW4), checked without Godot: the views play an item's effects by role from |
| `EventTests.cs` | 34 | EventTests |  |
| `FireTests.cs` | 494 | FireRig, FireTests | Rockets and fires in a GameRig (IW4, IRON_WAKE.md §7, GROUND.md §6.6). |
| `FoundationTests.cs` | 492 | FoundationTests | The ground game's shared foundations (GROUND_SYSTEMS.md §3, package P0), engine-free. |
| `GalaxyTests.cs` | 1199 | GalaxyTests |  |
| `GatheringTests.cs` | 392 | GatheringTests |  |
| `GibTests.cs` | 204 | GibTests | Gibs (GROUND.md §9.4): everything that can be destroyed has pieces to break into, declared beside its model in the |
| `Golden.cs` | 66 | Golden | Golden hashes over generated objects. Hashes every stored field (public or private, including the fields behind |
| `GunNetTests.cs` | 498 | GunNetTests |  |
| `GunTests.cs` | 431 | Guns, GunTests | Guns in a GameRig: a loaded gun in hand, shots queued for the coming tick as the server would. |
| `HeldTests.cs` | 403 | HeldTests, Loopback [TestActor] | Held items, item information and inventory actions (GROUND_SYSTEMS.md §7, package P3), engine-free. |
| `InteractionTests.cs` | 554 | InteractionTests [Rogue] | The interaction system (GROUND_SYSTEMS.md §5): what the E key does, one rule a test. |
| `IronWakeBuildTests.cs` | 390 | IronWakeBuildTests | Building aboard the Iron Wake (IRON_WAKE.md §8.1, IW5): placing on its decks, riding it, the refusals, a furnace in flight, a bag aboard, a grenade, a wreck, a restart. |
| `IronWakeNetTests.cs` | 195 | IronWakeNetTests [ShipFacts] | The Iron Wake over the network (IRON_WAKE.md §4.8, §4.11): prediction aboard, and sleepers aboard across a restart. |
| `IronWakeTests.cs` | 634 | IronWakeRig, IronWakeTests | The Iron Wake on the first world's stand-in ground (IRON_WAKE.md §4, IW1): one parked near the landing site, its nose |
| `ItemTests.cs` | 161 | ItemTests |  |
| `LoadingTests.cs` | 240 | LoadingTests [Clock] | The loading pipeline's rules (GROUND.md §4.3.1), without Godot: the game's plan reads and names every stage; the bar |
| `ModelCatalogueTests.cs` | 247 | ModelCatalogueTests | The ground game's model catalogue (GROUND.md §9), checked without Godot: every model file is a GLB with its LOD |
| `MovementTests.cs` | 307 | LevelSurface, Lap, MovementRig, MovementTests | A test surface: a smooth sphere whose ground sits at a fixed height, with an optional sea. |
| `MultiplayerTests.cs` | 471 | LoopbackRig, MultiplayerTests | An in-process server and clients on loopback: on a background thread and the wall clock, or (`simulated: true`) stepped by the rig itself on a simulated clock with a `SimulatedRelay` for a bad line, and `WaitUntilSettled` (clock and ground) before a test counts anything. |
| `NetTests.cs` | 382 | NetTests [Arrivals] |  |
| `PersistenceTests.cs` | 65 | TempDatabase, PersistenceTests | A database file in the temp folder, deleted afterwards. |
| `PhysicsTests.cs` | 126 | PhysicsTests |  |
| `PlacementTests.cs` | 372 | PlacementTests | The placement system (GROUND_SYSTEMS.md §6): one rule per placeable in data; its solvers, rotations, checks and kinds |
| `PlanetGroundTests.cs` | 214 | PlanetGroundTests | The first world's ground (GROUND.md §3, §6.1): the real planet's own scatter as natural objects, and the whole world |
| `PlanetSurfaceTests.cs` | 254 | PlanetSurfaceTests | The first world's real surface (GROUND.md §4.6): determinism, tiles, collision, speed. |
| `RaidAlertTests.cs` | 222 | RigPresence, RaidAlertTests | Who is "online" for a notification service in a GameRig: those awake; what they were sent; what was pushed. |
| `RaidNetTests.cs` | 209 | RaidNetTests |  |
| `RaidTests.cs` | 440 | Raids, RaidTests | Raiding in a GameRig: huts built through the building rules, explosives through the zone (GROUND.md §6.6). |
| `SeatTests.cs` | 166 | SeatTests | Seats, parts and doors (IRON_WAKE.md §5, IW2): who sits where, every way a seat is freed, and ships at rest. |
| `ShipGameTests.cs` | 244 | ShipGameTests | Ships in the survival game (GROUND.md §6.7): built at the vehicle workbench, boarded, flown, left, locked, hurt. |
| `ShipNetTests.cs` | 174 | ShipNetTests | A pilot's controls over a flight: lift off, climb, turn, cruise, boost, flight assist off and on, strafe, roll, |
| `ShipPartsTests.cs` | 384 | ShipPartsTests | IW2 (IRON_WAKE.md §5): the Iron Wake's seats, parts, doors, its helm's list and its parts' health, engine-free and over |
| `BatteryTests.cs` | 590 | BatteryTests | IW3 (IRON_WAKE.md §6): the aim held in the world, alternation and the loader's table, every refusal, a strike, a ripple, prediction, a restart. |
| `ShipPersistenceTests.cs` | 102 | ShipPersistenceTests | Ships in storage (GROUND.md §4.7, §6.7). |
| `ShipTestParts.cs` | 25 | ShipTestParts | A ship kind's parts and seats by what the tests call them (ships.json's data, IRON_WAKE.md §5.1). |
| `ShipTests.cs` | 293 | SlopeSurface, ShipRig, PipeExtensions, ShipTests | A test ground tilted about the north–south line through the landing site: it rises toward the east by |
| `SimulatedRelay.cs` | 125 | SimulatedRelay | `ConditionedRelay` for a stepped test: the same latency, jitter and loss between one client and the server, on the clock its owner passes in and the owner's thread, seeded. |
| `SoundCatalogueTests.cs` | 315 | SoundCatalogueTests [Wav] | The ground game's sounds (GROUND.md §4.9), checked without Godot: every file of the catalogue is there and imported |
| `SurfaceTests.cs` | 125 | SurfaceTests |  |
| `SurvivalNetTests.cs` | 300 | SurvivalNetTests |  |
| `SurvivalTests.cs` | 613 | GameRig, Places, SurvivalTests [FixedClimate] | A survival zone on the first world with players placed by hand, stepped tick by tick. |
| `TestRunner.cs` | 122 | TestAttribute, TestFailure, Assert, Fixtures, Program | Marks a public static method as a test. |
| `WorldNetTests.cs` | 250 | WorldNetTests | G7 World over the network and through a restart (GROUND.md §6.8): protocol 9, schema 6. |
| `WorldTests.cs` | 516 | WorldTests | G7 World, part A (GROUND.md §6.8): loot, the recycler, tech trees and research, teams, the map's markers. |
| `WalkingWorldTests.cs` | 234 | WalkingWorldTests | The walking-scale ground (WALKING_GROUND.md): determinism and its golden hash, sea cover and heights, streams to the sea or a lake, no seams, scatter at the level by size, speed. |
| `WoundedTests.cs` | 107 | WoundedTests |  |

