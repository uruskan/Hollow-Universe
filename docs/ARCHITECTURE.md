# Hollow Universe — Architecture

Status: foundation plan begun 10 October 2026, with dated AB Test B updates. [DESIGN.md](DESIGN.md) is the
current game design; GAME_DESIGN.md and older scale decisions are historical references. [GALAXY.md](GALAXY.md)
specifies the source generator. Distinguish planned architecture here from the implementation recorded in DEVELOPMENT.md.

Travel revision, **18 October 2026**: normal flight, supercruise, charged interstellar jumps and fuel scooping are the
approved direction under DESIGN.md TRAVEL-01–05. [TRAVEL_IMPLEMENTATION.md](TRAVEL_IMPLEMENTATION.md) proposes the
runtime, fuel, transfer and client work. The space layers and transfer table below are planned architecture; the
running game still has one planetary runtime and its local flight boundary. Historical 2.41 km world measurements
below describe source fixtures; AB Test B's current scale follows IMPLEMENTATION_SCALE.md and DESIGN.md WORLD-01.

AB Test B online combat extension (16 October 2026): [ATTACK_SYSTEM.md](ATTACK_SYSTEM.md)
defines the shared predicted-attack identity, authoritative decisions, ammunition reservations,
projectile reconciliation and bounded network history used by melee, firearms, launchers and cannons.
The current universe design remains [DESIGN.md](DESIGN.md).

AB Test B electrical and device-authorization foundation (**18 October 2026**):
[ELECTRICITY_AND_AUTHORIZATION.md](ELECTRICITY_AND_AUTHORIZATION.md) defines shared
physical ports, directed cables, Wire Tool transactions, indexed circuit evaluation and
separate device friendship/control policies. DESIGN.md ELEC-02–03 and AUTH-01 record the
confirmed 20 m endpoint span and clearing of inherited protection. The runtime now uses
an indexed directed graph, registered transfer handlers and a separate authorization
resolver. Windmill, turret and SAM declare their physical ports in data; shared tool,
network and save paths replace the old source-ID bus and owner/team defense shortcuts.
Routes remain visual, and zero-time topology edits never integrate stored energy.
DEVELOPMENT.md records validation and explicit first-slice limits.

---

AB Test B environment program (**18 October 2026**):
[WORLD_ENVIRONMENT_ARCHITECTURE.md](WORLD_ENVIRONMENT_ARCHITECTURE.md) defines the shared
terrain, formation, support, soil, ecology and rendering boundaries, world ownership and
seed/version contracts. [TERRAIN_SCENERY_PLAN.md](TERRAIN_SCENERY_PLAN.md) contains the
research and delivery gates. Implementation status is explicit; the complete environment
pipeline and multiworld lease store are not yet built.

## 1. What the architecture has to carry

The end state, not the first milestone:

- **A 1:1 Milky Way.** About 120 billion star systems, real stars near Sol, every system reachable,
  every solid body landable.
- **Persistent shards running 24/7.** Players build bases anywhere, with no limit on how many. Bases keep
  existing, and can be raided, while their owners are offline.
- **Two games in one world.** Rust-style survival on planet surfaces (gather, craft, build, full-loot PvP,
  raiding) and Elite-style space (ships, normal flight, supercruise, frame shift jumps).
- **Fast combat** (firearms on foot, ships in space) that feels right at 50–150 ms ping.
- **A tiny team where an AI writes most of the code.** Everything must build, run and be checked from
  text and the command line, without opening an editor.

Scale targets per shard (one universe):

| | First public test | Design target |
|---|---|---|
| Concurrent players | 100 | 1,000 |
| Players in one fight | 30 | 200 |
| Persistent entities (base parts, boxes, items) | 1 million | 50 million |
| Star systems | all of them, generated on demand | same |

---

## 2. Principles

| # | Principle | Consequence |
|---|---|---|
| P1 | **The universe is a function plus a diff.** | Natural things are generated from (galaxy seed, address, generator version). Only player changes are stored. |
| P2 | **The server is the only authority.** | Clients send inputs and intents, never results. |
| P3 | **Rules are engine-free.** | All simulation and rules are plain C#. Godot exists only in the client, for presentation. |
| P4 | **Generation is bit-exact everywhere.** | The same address gives the same result on Windows, Linux, x64 and ARM64, forever (per generator version). |
| P5 | **Simulate only where players are.** | Areas load around players and unload when empty. Elapsed time is applied when an area loads. |
| P6 | **Every persistent object has exactly one owner.** | A zone or the database. Moving an object between owners is a transaction, so nothing duplicates. |
| P7 | **Scale by adding processes, not by rewriting.** | Start as one process. Boundaries (zones, services) talk through messages from day one, so they can move to other processes later. |
| P8 | **Numbers live in data.** | Item stats, recipes, ship modules and generator tables are data. Code reads them. |
| P9 | **Original content.** | Rust, Elite, No Man's Sky and Dune: Awakening are references for feel. No names, assets, data or UI are copied. Real star data is public scientific data, used under its licence. |
| P10 | **Test here first.** | Every change compiles and passes tests in the AI workspace before it reaches the user's PC. Anything visual gets a screenshot. |

---

## 3. Overview

```
                 ┌──────────────────────── Shard (one universe) ─────────────────────────┐
                 │                                                                        │
 Godot client ───┼─UDP─▶ Zone host ──── zones: surface / local space / system space        │
 (C#, Hollow.*   │        (.NET)        each zone = one physics world, one thread           │
  libraries for  │          │                                                             │
  prediction)    │          ├── Coordinator: which zone owns what, transfers, start/stop   │
                 │          ├── Services: accounts, chat, notifications, market (later)    │
                 │          └── Persistence ──▶ database (SQLite, later PostgreSQL)        │
                 └────────────────────────────────────────────────────────────────────────┘
```

**Phase 1** (first public test): everything above is one server process, with zones ticking in parallel
on a worker pool. **Phase 2**: several zone host processes per shard, with a gateway in front so players
don't reconnect when they change zones. **Phase 3**: several shards that share one account service.
Phases 2 and 3 need no rewrite because of P7.

---

## 4. Code layout and dependency rules

```
src/Hollow.Core          math (double vectors, deterministic math), hashing, RNG, IDs, units, time
src/Hollow.Galaxy        galaxy model, addresses, sectors, star generation, catalogue, names, star systems
src/Hollow.Surface       (G1) planet surfaces: height, climate, biomes, scatter, chunk meshes and collision
src/Hollow.Simulation    (G1) entities, components, rules: movement, items, crafting, building, combat
src/Hollow.Net           (G1) protocol, serialization, replication, prediction support, transport adapter
src/Hollow.Client        (G1) the client without graphics: connection, prediction, interpolation, client world
src/Hollow.Persistence   (G1) storage interfaces, SQLite and PostgreSQL implementations
src/Hollow.Server        (G1) the dedicated server program: zone host, coordinator, services
game/                    the Godot client (project.godot, HollowUniverse.csproj)
game/data/ground         (G2) item, recipe, building, loot and weapon tables, read by server and client
tests/Hollow.Tests       test runner for everything engine-free
tools/                   catalogue builder, generator report, bots (Hollow.Bots), load tests
third_party/             vendored packages, so everything builds without network access
```

Dependencies only point down this list: `Core ← Galaxy ← Surface ← Simulation ← Net ← Client / Server`.
Persistence depends on Core and Simulation. The Godot client uses `Hollow.Client`, and `Hollow.Server` when it
hosts a game itself. Bots are `Hollow.Client` without graphics. **Nothing in `src/` references Godot.** A test fails
the build if any `src/` assembly references `GodotSharp`. GROUND.md §4 describes how these fit together for the
ground game.

---

## 5. Languages and tools

| | Choice | Why |
|---|---|---|
| Language | **C# 14 on .NET 10** (LTS, supported to November 2028) for everything | One language for server, client and tools. Fast enough for nearly all of it. Godot 4.7 runs .NET 10 projects (tested). |
| C++ | Only for measured hot spots, as a native library (server) or GDExtension (client) | Every C++ module needs its own native build per platform. Add one only when a profiler shows C# can't do it. |
| GDScript | **Not used** | |
| Engine (client) | **Godot 4.7.2 .NET edition**, Forward+ renderer | Text-based project files, light editor, MIT licence. |
| Server | Plain .NET console program, no engine | P3. Runs on Linux or Windows. |
| Transport | **LiteNetLib** (MIT, pure C#) over UDP | Proven reliable/unreliable channels, fragmentation, MTU discovery. |
| Physics | **Jolt** through JoltPhysicsSharp on server and client (confirmed by the G1 spike: identical results in a console process and in the Godot client, GROUND.md §4.5) | Same engine family as Godot's built-in physics. Its virtual character is a good kinematic controller and isn't part of the solver, so replaying inputs for prediction is cheap. |
| Database | SQLite for development and small shards, PostgreSQL for production | Same repository interfaces. |
| Tests | Plain console runner in `tests/` | Can move to xUnit later. |
| Third-party packages | Vendored in `third_party/packages` as `.nupkg` files; `NuGet.config` maps their IDs to that folder only (`third_party/README.md` lists them) | The AI workspace cannot reach NuGet, so a clone must build offline. |

No reflection or JSON on hot paths. Serializers are generated ahead of time: `tools/Hollow.NetGen` reads the message
definitions (`src/Hollow.Net/Protocol/Messages.cs`) and writes `Messages.g.cs` into the repository, and a test fails
when that file is out of date. A Roslyn source generator would need the compiler packages, which aren't vendored;
a text-to-text tool also keeps working when a definition changes in a way the old generated code can't compile with.

---

## 6. Coordinates, units and precision

Three frames, each with its own type, so they can't be mixed by accident:

| Frame | Used for | Type | Unit | Origin and axes |
|---|---|---|---|---|
| **Galactic** | star positions, galaxy map, jumps | `GalacticPosition` (3 × double) | light-year | Sol. +Z toward the Galactic Centre, +Y toward the north galactic pole, +X toward galactic longitude 90° (the direction of the Sun's orbit). Right-handed, Y up, like Godot. |
| **System** | bodies, stations, ships in supercruise | `SystemPosition` (3 × double) | metre | the root of the system's orbit tree: its only star, or the barycentre of its stars (GALAXY.md §10.1). Reference plane is the system's orbital plane (x–z), +Y is its north. |
| **Local** | physics, rendering, gameplay | `LocalPosition` (3 × float) | metre | the zone's origin. A surface zone uses its world's own frame (`ZoneFrame.World`): origin at the world's centre, the body's axes (+Y towards the north pole, longitude counted eastward from +X), so up is the direction from the centre and changes from point to point. Other zones' origin is close to the players. A tangent frame (`ZoneFrame.At`: origin on the ground, +Y = local up) remains for tools that look at a patch of a large body. |

Rules:

- **Physics only ever sees the local frame.** A surface zone measures its whole world from the centre (§10.1): on
  the first world, 2.41 km in radius, a float steps 0.24 mm at the ground and under 1 mm at 4 world radii, as far as
  a ship may go (WHOLE_WORLD.md §3.1), so Jolt stays single precision with no floating origin. Other zones are at most
  about 20 km across, so float precision stays near 1 mm.
- **Distances in space never reach the physics engine.** Supercruise and jumps work in double precision on the
  system and galactic frames. The engine's world-size limit is irrelevant.
- **The client renders with a floating origin.** The render origin follows the camera. Large-scale things
  (planets, stars) are drawn at scaled-down distances or as impostors.
- **Universe time is real UTC** (as in Elite). Orbits and planet rotation are computed from it, so every
  client and server agrees without synchronising anything but the clock.
- **Server time is ticks** at 30 Hz per zone. Ticks drive simulation, never wall-clock reads.

---

## 7. Identity

| Thing | ID | Stored in the database? |
|---|---|---|
| Star system | `SystemAddress`, 64 bits. Procedural: level + sector + cell + index. Catalogue: flag + catalogue + catalogue ID. Layout in GALAXY.md §6. | No, it's generated |
| Body (star, barycentre, planet, moon, belt) | `BodyId` = SystemAddress + 32-bit body path: node (star or barycentre), orbit slot, moon, feature (GALAXY.md §10.1). Never a list position, so it can't shift. | No |
| Surface tile | BodyId + cube face + quadtree path | Only if players changed it |
| Natural feature (ore node, tree, monument) | tile + index | Only overrides, e.g. "depleted until T" |
| Persistent entity (base part, box, item, ship, character) | `EntityId`, 64 bits, allocated by the shard: 41 bits time + 9 bits allocator + 14 bits sequence | Yes |
| Account | `AccountId` (GUID) from the account service | Yes |

Natural things have IDs without database rows. A row appears only when a player changes something.

---

## 8. Determinism rules

Everything generated (galaxy, systems, terrain, resources, monuments) must give bit-identical results on
every machine, because the server and every client generate the same universe independently.

1. **Never use** `System.Random`, `HashCode`, `string.GetHashCode()`, `Dictionary` iteration order,
   `Parallel` with order-dependent results, `DateTime.Now`, or `Math.Exp/Log/Pow/Sin/Cos/Tan/Atan2`
   in generation code. The `Math` functions call the platform's C library, which differs between Windows
   and Linux in the last bit. `HashCode` is allowed only inside `GetHashCode` overrides for dictionaries.
   A test scans `src/` for all of these.
2. **Use `Hollow.Core`**: `Hash64` (a fixed 64-bit mixer), `Rng` (SplitMix64, seeded from a hash), and
   `DMath` (exp, log, pow, sin, cos, atan2 built only from + − × ÷ and `Math.Sqrt`, which are exactly
   rounded by IEEE 754 and therefore identical everywhere).
3. **Every generated object has its own RNG stream** seeded from its address. Adding a new property to
   one object never shifts another object's results.
4. **Generator versions.** Each pipeline stage (§9) has a version constant (`GeneratorVersion.Galaxy`,
   `.System`, `.Surface`), mixed into that stage's seed, so a stage's output is a function of (seed, address,
   version) and the stages change independently (GALAXY.md §14). Golden tests pin hashes of known outputs,
   every stored field included. While in development the generator may change freely (update the goldens on
   purpose). **Once a shard goes live, its generator versions are frozen.** Changes then ship as a new version
   of the stage for new shards or wipes.
5. **Floats to integers** go through explicit rounding helpers, never casts of values that could sit on a
   boundary.
6. **Never depend on optional data.** Generation must give the same result whether or not a data file
   loaded. For example, procedural stars near Sol are removed in favour of the real-star catalogue even when
   the catalogue is missing. A missing file is an error to report, never a different universe.
7. **Treat addresses from outside as untrusted.** A lookup by an address that doesn't exist (a wrong
   catalogue number, an ID out of range) returns "not found". It never throws or truncates.

Visual-only code (shaders, the galaxy glow, effects) may use normal maths. It must never decide what exists.

---

## 9. Universe generation pipeline

```
Galaxy (density model)
 └ Sector         1024-ly cube, has a generated name
    └ Cell        octree levels 0–7; brighter stars live in bigger cells
       └ Star system   stars, planets, moons, rings, belts            ← M1 stops here
                       and each body's physical state (the surface's inputs)
          └ Surface    terrain, biomes, resources, monuments          ← M4
             └ Points of interest, wrecks, signals                    ← later
```

Each stage is a pure function of (seed, address, version), computed on worker threads and cached (LRU)
on both server and client. The full specification of the first three stages is in [GALAXY.md](GALAXY.md).

Real content (famous exoplanets, real names of stars, named clusters and nebulae) is an **authored layer** laid
over the generated stages (GALAXY.md §15): compiled into `Hollow.Galaxy`, versioned like a stage, and keyed by
catalogue ID and body path for systems and by position for regions, so it replaces exactly what it names and
moves nothing else.

---

## 10. Simulation (server)

### 10.1 Zones

A **zone** is one live simulated area: one physics world, one origin, one thread at a time.

| Zone type | Covers | Physics |
|---|---|---|
| System space | one star system's supercruise layer | none: bodies on rails, ships on simple supercruise kinematics |
| Local space | normal space around a point (a station, a ring, a drop point), up to ~20 km | rigid bodies, ships |
| Surface | a whole world: one body, in its own frame (§6) | characters, buildings, vehicles, terrain |
| Interior (later) | inside a station or a large ship | characters |

**Lifecycle**: requested → loading (generate the natural state, load the diff, apply elapsed time) → active
(ticking) → idle (no players; keeps ticking for 5 minutes so quick returns are instant) → saving → unloaded.

**Zones never touch each other directly.** They exchange messages through the zone host: transfers,
cross-boundary events. This is what lets zones move to other processes later (P7).

**A surface zone is a whole world** (since S3, 14 October 2026: SCALE.md §5.1, WHOLE_WORLD.md). Worlds are small
(the first is 2.41 km in radius, a constant until `GameScale` gives every body its own), so one zone holds all of
one, measured from its centre:

- A world has no zone borders. Two players on the same world are always in the same zone, and surface zones
  don't grow, merge or split. Several zones in one server (the zone host, deferred by the owner) means several
  worlds in one server.
- Everything spatial belongs to the ball. The zone's cube-sphere tiles are chosen by size, the level whose edge is
  nearest a length on that world (`TileKey.LevelFor`), not by level number: the physics tiles of about 64 m (level 6,
  59 m on the first world), the stand-in's natural objects, the nav grids, the animals' spawn areas, the loot cells.
  Spatial indexes (relevance, the building, deployables, natural objects' changes, animals) are grids of cubes in 3D
  with straight-line distances, and natural objects are queried by a sphere.
- Until S4 makes the real ground at walking scale, every zone stands on the stand-in world (`StandInWorld`:
  continents, a sea over 45 % of it, climate bands by latitude). The real `PlanetSurface` is built at its real
  radius, which a zone measured from its centre can't hold.
- A zone has a maximum cost of one thread at 30 Hz. A big raid (200 players) has to fit in one zone's 33 ms
  budget. That's the hardest performance target in the project and is load-tested in M3–M4.
- Far-away bases, beyond a player's relevance (§11.3), are shown from a low-frequency "distant view" feed
  (silhouettes from stored data), not sent in full.

### 10.2 Entities

- An **entity** is a 64-bit ID plus components (data only). **Systems** (logic) run each tick in a fixed
  order. Storage is our own and small (`Hollow.Simulation.Entities`, decided in G1): an entity is a slot with a
  generation and a permanent `EntityId`; each component type has one sparse set (dense arrays to iterate, O(1) add,
  get and remove). Archetype storage can replace the pools if profiling ever asks for it.
- **As built (K0, 14 October 2026):** the zone is a host and each rule is a `ZoneSystem` with its own state, listed once
  in `GroundSystems` and stepped in that order (ships under the physics gate, the slow ones once a second); systems find
  each other by type and the host times each (GROUND_SYSTEMS.md §12.2).
- **Static entities** (building parts, deployables, boxes) change rarely: no per-tick cost, replicated
  reliably when they change.
- **Dynamic entities** (characters, projectiles, vehicles, ships) are simulated and snapshotted every tick.
- Components declare their network and save schema with attributes. Serializers are generated (§5).

### 10.3 Physics

- **Server**: one Jolt physics system per zone. Characters use Jolt's kinematic character controller.
  Building parts are static boxes or convex hulls. Terrain is static mesh tiles, the world's cube-sphere tiles
  (§10.1), built from the same terrain function the client renders.
- **Client**: the same physics library runs a small local copy of the nearby collision world, so the
  player's own character and ship are predicted with exactly the server's code. Godot's built-in physics
  is only for cosmetics (debris, ragdolls).
- **Streaming** (both sides, the same code): the ground and its natural objects exist only where something can
  touch them (a character's surroundings, what a ship's queries can reach this tick), built ahead on worker threads
  and taken in a few a tick; Jolt bodies are kept once made and the broadphase is updated incrementally, so the
  world's churn costs little (GROUND.md §4.5).
- **Native ownership**: JoltPhysicsSharp 2.23.0 leaves physics systems, shape settings and body-creation settings
  without ownership of their native handles, so their `Dispose` frees nothing. Create every settings object with
  `.Owned()` and call `JoltRuntime.HandOverToSystem` before disposing a physics system and its filters; an
  architecture test enforces both. Without them every world leaked its system, bodies and shapes (KNOWN_PROBLEMS.md).

### 10.4 Elapsed time (nothing ticks while unloaded)

Every region stores `last_simulated_at`. When it loads, each system's catch-up function runs once with the
elapsed time:

- decay and upkeep (structures lose health unless upkeep is paid from the claim's storage),
- miners and production (rate × time, capped by storage and fuel),
- furnaces, crops, respawning resource nodes.

Catch-up is closed-form (constant work per object, no replaying ticks). Tests check that catch-up matches
live ticking within rounding.

**Offline raids are live.** Attackers' presence loads the zone, so the base exists in full and its
defences work. The owners get a raid alert from the notification service and can respawn at a bed inside
the base. As built in G5 (GROUND.md §6.6): sleepers die to raids like anyone, upkeep and decay run on the zone's slow
schedule, and alerts wait for the owners' login.

---

## 11. Networking

### 11.1 Connection and transport

- UDP through LiteNetLib. Channels: reliable ordered (commands, events, static entities) and unreliable
  sequenced (inputs, snapshots).
- The graphical client uses LiteNetLib's background transport to keep network progress independent of render/asset
  stalls. Application events remain queued until `GameClient.Update` polls on its caller's thread; prediction and
  modules stay on that thread. Server, bots and tests retain manual transport by default. Background transport never
  runs a concurrent `ManualUpdate`, and is stopped before client simulation resources are disposed. Normal disconnect
  timeouts stay unchanged. The 16 October coordinated startup/loading/architecture batch passed 15/15, including a
  12-second update stall: the manual control timed out, background transport survived, and callbacks/disconnect/disposal
  passed on the caller's thread. Native Maximum world-startup acceptance remains pending
  ([P8 evidence](repair-results/P8-performance.md)).
- **Login flow**: the client logs in to the account service over HTTPS and receives a signed **connect
  token** (shard address, expiry, account ID, session key). It presents the token to the shard. After that,
  every packet is encrypted with the session key (ChaCha20-Poly1305, built into .NET).
- Steam or Epic login and relay networking can be added later behind the same token step.

### 11.2 Messages

- Messages are C# structs with attributes (`[NetMessage(id)]`, `[NetStruct]`, `[NetState]`; fields with `[Bits]`,
  `[Signed]`, `[VarInt]`, `[Angle]`, `[Quantize]`, `[MaxLength]`, `[MaxCount]`). `tools/Hollow.NetGen` writes their
  bit-packed serializers (§5): variable-length integers, quantized floats, and for entity states a delta against a
  baseline (a bit per field, then the difference in as few bits as fit). Readers never throw on bad data; a message
  that reads past its end or breaks a limit is dropped. Each generated message implements `INetMessage<T>` (its type
  byte, its reader and its writers), and `MessageHandlers<TContext>` routes a packet's type byte to the one handler
  registered for it (GROUND_SYSTEMS.md §12.4).
- A protocol version and a hash of every message layout (generated) are checked on connect, so builds that don't
  speak the same protocol refuse each other with a reason.

### 11.3 Replication (server → client)

- **Relevance per client**: a spatial grid in each zone decides what each client receives, by distance and
  entity type. The player's own character, ship and team are always relevant.
- **Static entities** are sent once when they become relevant (rate-limited batches), then only changes.
  A big base streams in over a few seconds instead of arriving as one burst.
- **Dynamic entities** are sent in snapshots at 20 Hz, delta-compressed against what the client acknowledged:
  each entity against its newest state in a snapshot the client confirmed (by sequence and a 32-bit mask), so a lost
  snapshot never breaks a later one. A priority accumulator decides what fits in the client's bandwidth budget
  (target ≤ 256 kbit/s in a fight). GROUND.md §4.4 has the details as built.
- **Events**: shots, impacts and sounds are unreliable when cosmetic and reliable when they change state.

### 11.4 Prediction, reconciliation, interpolation

- The client sends input every frame (move, look, buttons), with the previous few inputs repeated to survive
  packet loss. The server applies each input on its tick.
- **Own character and ship are predicted** with the same simulation code. When a snapshot says "input 412
  processed, you are here", the client rewinds to that state and replays its newer inputs. Small corrections
  are smoothed visually.
- **Everything else is interpolated** about 100 ms in the past, between two snapshots.
- **Actions** (build, craft, loot, use) are intents with sequence numbers. The UI may show them right away;
  the server confirms or rejects them with a reason code.

### 11.5 Combat

- Projectiles and hits are simulated on the server. For each shot, the server rewinds hitboxes to the time the
  shooter saw (up to 250 ms) using a one-second history, then simulates the projectile forward.
- Damage, death and loot exist only on the server.

### 11.6 Cheating

- The server simulates movement itself, so speed and teleport hacks don't work.
- Every command has a rate budget. Invalid or impossible commands are rejected and counted.
- Clients never report hits or results.
- Later: occlusion checks so distant hidden players aren't sent at all (wall-hack resistance), and
  client integrity checks.

---

## 12. Transfers

Planned connected-travel contracts, updated 18 October 2026; current local ascent is documented in LOCAL_FLIGHT.md.
The server must validate drive charging, destination/range and fuel before authorizing a mode change or jump.
Charge and transfer are separate operations: a ready drive does not prove the destination is ready.

| Transfer | Trigger | What happens |
|---|---|---|
| Surface ↔ surface | none since S3 (14 October 2026): a surface zone holds its whole world (§10.1) | Nothing: a world has no zone borders. |
| Local space ↔ surface | descending below the handover altitude (or climbing above it) | Ship and occupants are handed over. The client keeps rendering. |
| Supercruise ↔ local space | drop out / jump to supercruise | Join the local zone at that point, or create one. Join an existing zone if one is within range. |
| Star system → star system | charged drive jump after authoritative validation | Countdown, hyperspace sequence and destination arrival; duration is tuning to be defined, with prepared destination and transactional ownership. |
| Process ↔ process (phase 2) | load balancing, or a jump to a system hosted elsewhere | Serialize and hand over ownership through the coordinator; preserve the logical session, with transport handover covered by the relevant flight/jump transition. |

Ownership handover is transactional (P6). The source marks the object "in transit" and the database
commits the new owner before the target accepts. After a crash, the coordinator resolves every object to
exactly one owner.

The transfer includes the ship, seated/walking/sleeping crew, supported objects and cargo, plus travel state and
committed fuel accounting. Fuel scooping and drive consumption use one server-owned fuel system. Cancellation,
packet retries, stale source saves and crash recovery must not duplicate entities or fuel. See the current travel
plan for implementation order and separate planetary/interstellar validation gates.

---

## 13. Persistence

- **Storage**: SQLite (development and phase 1) → PostgreSQL (production), behind the same interfaces.
- **What's stored**: accounts; characters and where they are; persistent entities grouped by region; region
  metadata (`last_simulated_at`, version); claims; overrides of natural objects (depleted nodes, destroyed
  monuments); global data (chat history, notifications, market later).
- **Entity rows**: ID, region key, type, owner, parent container, version, payload. The payload is a compact
  binary from the same generated serializers, with a schema version for migrations.
- **Writes**: zones keep their state in memory. Dirty entities are flushed every 30 s and on unload, one
  transaction per zone per flush. Critical events (death drops, trades, transfers between zones) commit
  immediately.
- **Crash safety**: at most 30 s of one zone's ordinary changes can be lost. Items can never duplicate
  across zones.
- **Backups**: periodic database snapshots. **Wipes**: a new database, optionally a new galaxy seed.
  Accounts survive wipes. (Whether the game wipes at all is a design decision; the architecture supports
  both.)
- **Schema changes**: versioned payloads plus migration code. Tests load old saves.
- **Playable terrain compatibility (AB Test B, 16 October 2026)**: body-specific walking profiles are
  selected by system/body address, independently of physical planet metadata. Schema 9 records each saved
  body's terrain revision. The server checks it read-only before migrations or restoration; incompatible
  populated saves are preserved and refused. Revisions cover heights and generated solids: cliff generation 2
  advances Earth to save revision 3 and other walking bodies to revision 1 without rerolling their heights.
  Current walking bodies cannot adopt revision-zero spatial state; stand-in ground retains revision zero.
  Default filenames include the Earth revision; protocol 33 rejects peers using earlier coastlines or cliff
  collision. See [EARTH_TERRAIN.md](EARTH_TERRAIN.md).

---

## 14. Global services

| Service | Job | Phase 1 |
|---|---|---|
| Accounts | login, connect tokens, bans | module in the server process |
| Directory | where each player is, which zone host owns which zone | module |
| Chat | global, local, team, direct messages | module |
| Notifications | raid alerts, "your miner is full"; later push to a phone app | module (G5: `INotificationService` in `Hollow.Server/Notifications`: raid alerts in the game or queued in SQLite for login, a push seam; GROUND.md §6.6) |
| Market | trading between players | later |
| Discovery | who first scanned which system or body (optional feature) | later |
| Admin | moderation, server control | console commands first |

Each is an interface from day one, so moving it to its own process later is a deployment change.

---

## 15. Client (Godot)

The shared contact/audio/VFX boundary is specified in [AUDIO_VFX.md](AUDIO_VFX.md).
Authoritative contacts retain target/part, physical surface and actual normal; one
data-driven response table feeds the existing presentation directors. Continuous audio
and visual effects retain logical ownership separately from bounded playback/render
resources. Material identity and cue timing remain engine-free and shared by local and
remote presentation.

- **Layers**: App (modes, settings) → Connection (Hollow.Net client) → World view (entity views pooled by type,
  floating origin, LOD) → Presentation (planets, ships, characters, effects, audio) → UI (Godot controls,
  in C#) → Input (Godot input map → game commands).
- **Modes**: galaxy map, system map, flight, surface, menus. Each is a scene. The world keeps streaming while
  a map is open.
- **Planets**: cube-sphere quadtree terrain with chunk LOD; far bodies as impostors. Near the player, the same
  terrain function as the server gives collision for prediction.
- **Offline tools**: the galaxy and system maps work without a server, because generation is local and
  deterministic.
- **Scenes are thin**: a scene file holds a root node and its script; C# builds the rest. Fewer hand-edited
  scene files means fewer merge conflicts and less editor-only state.
- **Interface**: drawn at the screen's real pixel size, never stretched. Godot's stretch mode is off; our own
  UI scale follows the window (1.0 from 1600 × 900 up to 1080p, growing with the height above that, shrinking
  only to fit a smaller window) times a player preference (Ctrl + / −). Every length goes through `Ui.Px`, so it
  is a whole number of pixels at that scale. Fonts are rasterised at their final size with no distance fields,
  using light hinting and greyscale antialiasing. Icons are SVG, rasterised at their final size. The theme is
  rebuilt when the scale changes. One font family (Inter, variable) with tabular figures for numbers, a step
  heavier than usual (520 for body text, 600 for values) because light text on dark panels reads thin, and body
  text no smaller than 15 px at scale 1. The scale never goes past what fits the window, and labels on the maps
  never overlap each other or the panels. All of this lives in `game/scripts/Common/`: `Ui.cs` (styles, theme,
  helpers), `UiIcons.cs` and `Labeller.cs`.

---

## 16. How we work

- **Build**: `dotnet build HollowUniverse.sln`. **Tests**: `dotnet run --project tests/Hollow.Tests`.
  **Client**: open `game/project.godot` in Godot 4.7.2 .NET.
- **Kinds of tests**: unit tests (rules), golden tests (generation stays identical), integration tests
  (server + headless bot clients, from M2), screenshots (anything visual).
- **Budgets**: client 60 fps on the user's PC; server zone tick ≤ 10 ms typical and ≤ 33 ms in a 200-player
  fight; bandwidth per client ≤ 256 kbit/s in a fight.
- **Style**: nullable reference types on, warnings are errors in `src/` and `tests/`, `.editorconfig` in the
  repo.
- **Docs first**: architecture changes update this file in the same commit. Design changes update
  GAME_DESIGN.md.

---

## 17. Milestones

| | Goal | Done when |
|---|---|---|
| **M0** | Repository, toolchain, docs | Builds and tests pass here; the project opens on the user's PC. |
| **M1** | **Galaxy** | Galaxy map and system map run; real stars near Sol; ~120 billion procedural systems on demand; deterministic; tested. |
| M2 | Network core | .NET server, two Godot clients and 100 bots in a flat test zone; predicted movement; snapshot replication; bandwidth and tick measured. |
| M3 | Zones and persistence | Zones load and unload; place a wall; restart the server; the wall is still there; elapsed-time catch-up works. |
| M4 | Planets | Land on a procedural planet; walk on a curved surface; the whole world is one zone (§10.1). |
| M5 | Ships and travel | Normal flight (6 degrees of freedom, flight assist), supercruise, frame shift jumps between systems, docking. |
| M6 | Survival core | Gather, craft, inventory, Rust-style building, doors, locks, claims, firearms, death, loot, beds. |
| M7 | Raiding | Explosives, structure damage, decay and upkeep, raid alerts. |
| M8 | Empire | Miners and automation, monuments and rare loot, ship engineering, deeper crafting. |

**The ground game runs ahead** (decided 12 October 2026) as steps G1–G7 in GROUND.md §5, on a real planet from the
start:
- M2 and M3 become part of G1–G3;
- M6 and M7 become G2–G5;
- G1 builds one planet's surface ahead of M4;
- M4 finishes with G7 (every landable body; since S3, 14 October 2026, each a whole world in one zone, §10.1);
- M5 (ships) follows.

---

## 18. Open decisions

| Decision | Options | Lean | Decide at |
|---|---|---|---|
| Physics library | Jolt via a C# binding / BepuPhysics2 (pure C#) | **Decided: Jolt** (JoltPhysicsSharp; the G1 spike passed, GROUND.md §4.5) | done |
| Entity storage | own archetype store / Arch | **Decided: own, small** — a sparse set per component type (§10.2) | done |
| Hosting | own machines / rented servers / cloud | rented dedicated servers | M3 |
| Surface tile size and interaction distance | 1–4 km / 2–6 km | **Not needed since S3** (14 October 2026): a surface zone holds its whole world (§10.1) | done |
| Real-star catalogue | HYG 4.1 (~120k stars) / AT-HYG (~2.5M) | HYG now, AT-HYG later | after M1 |
| Wipes, shard size | design | — | GAME_DESIGN.md |

---

## 19. Risks

| Risk | Mitigation |
|---|---|
| Netcode is hard and can sink the project | Built in M2, before any content. Bots and load tests run in the AI workspace. |
| Planet-scale terrain with physics and building | M4 prototype early; precision rules in §6; tiles and zones designed together. |
| Millions of persistent entities | Static/dynamic split, per-region loading, database indexes; load tests with generated bases. |
| Generation differs between Windows and Linux | `DMath`, golden tests run on both. |
| Big fights overload one zone | One thread per zone, measured budgets, static entities cost nothing per tick. |
| Godot's visuals are below Unreal's | Strong art direction; C++ rendering extensions only if needed. |
| Tiny team | Docs first, small milestones, everything tested before it reaches the user. |
