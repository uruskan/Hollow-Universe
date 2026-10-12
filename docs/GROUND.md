# Hollow Universe — The ground game

Status: draft 1, 12 October 2026. This is the Rust-style game on planet surfaces: what it is, how it's built and in
what order. [GAME_DESIGN.md](GAME_DESIGN.md) §5 says it in short; [ARCHITECTURE.md](ARCHITECTURE.md) holds the
engine-wide rules this follows (principles §2, determinism §8, networking §11, persistence §13).

Every rule is marked **Decided** (the owner said so), **Proposal** (waiting for a yes or no) or **Open**.

---

## 1. Decisions

| Topic | Rule | Status |
|---|---|---|
| Basis | Rust's ground loop, in first person: gather, craft, build, raid, full-loot PvP, sleepers, beds. | Decided |
| What may come from Rust | Its mechanics, its numbers (including the AK recoil pattern) and its UI design. | Decided |
| What may not | Asset files from Rust or any other game: models, textures, animations, sounds, maps, monuments. Not even as placeholders. The list of such files on this PC is in §9. | Decided |
| Names | Our own for items, devices and places. Plain words ("wooden door", "furnace") are fine. | Decided |
| Interface | The RustMVP interface, exactly (§8), built with `Ui` so it stays crisp at any resolution. | Decided |
| Multiplayer | Networked from the first line. Solo play runs the same server inside the game. | Decided |
| First world | A real planet from the generator: Nolcas Peris CQ-SLY 7-0 B 3 (§3). | Decided |
| It must look good | The surface (terrain, plants, water, sky) is part of G1 and is judged by the owner on screenshots. | Decided |
| Tech ladder | Rust's: rock, then stone tools, metal, firearms. Sci-fi only above that. | Proposal |
| Day length | The planet's real rotation (35 h on the first world), or rotation compressed for play (for example ÷ 24: 87 minutes). | Open |
| Character | Our own character system. Base body and motion data from free sources (MakeHuman CC0, CMU motion capture, Quaternius CC0) if the owner allows the downloads. | Open |

---

## 2. Lessons from earlier attempts

| Attempt | What went wrong | What this design does instead |
|---|---|---|
| hollow-orbit (browser, single player) | Game state, meshes and UI mixed in the same objects; random numbers and a variable time step everywhere; no network. | Rules in engine-free C#; fixed ticks; the server owns the state. |
| RustMVP, Godot phase (GDScript client, C++ server) | Every predicted rule written twice, in two languages. | One C# rules library used by the server, the client and the bots. |
| RustMVP, Unreal phase | One generic command call and whole-struct replication (an audit judged it wrong); the installed engine can't build a dedicated server. | Typed messages; per-client delta snapshots; the server is a plain .NET program. |

Their rules, numbers and edge cases are still the best reference we have (§6).

---

## 3. The first world

The first world is a real body of the generated universe, chosen from the planets the generator makes
(GALAXY.md §10.3). Within 400 ly of Sol there are 1,698 worlds with plants and animals; 71 have breathable air;
this is the one closest to an Earth-like place to live.

| | |
|---|---|
| System | Nolcas Peris CQ-SLY 7-0, 331.5 ly from Sol, 5.2 billion years old. `SystemAddress` `0x7810135EB5240000`. |
| Suns | B, an F9.5 V star (5,983 K, 1.46 × the Sun's light), which the planet orbits. A, a cool white dwarf about 486 AU from B. C, a hot white dwarf about 1,800 AU out. From the planet, A and C are the two brightest stars in the sky. |
| Planet | B 3, body path `0x20600000`. Earth-like world, radius 6,435 km, 0.92 g. |
| Orbit and day | 1.255 AU from B (93% of Earth's sunlight), a 489-day year, a 34.9-hour rotation, 23° tilt (seasons). |
| Climate | Mean 18 °C, 13–20 °C between night and day. |
| Air | 2.23 atm; oxygen 0.31 atm, CO₂ 0.08%: breathable. Thick air means a deep blue sky and hazy distances. |
| Surface | Oceans cover 45%, ice 8%. Moving plates (mountain ranges), gentle volcanism. Complex life: vegetation and animals. Hazards: quakes only. |
| Moon | B 3 a, rocky, 250 km radius, 32,036 km away, orbit 16.3 h. It looks about 1.7 times as wide as our Moon. |
| Neighbours | Two hot rocky planets inside, an asteroid belt and three ice giants with moons outside. |

- **Pinned by a test.** A golden test pins the planet's state. If a generator change alters it, the test fails and we
  decide what to do. The ground branch starts from `generator-v2` for this data.
- **The landing site** (the first zone's origin) is 44.8683° N, 170.0010° W (`FirstWorld.LandingLatitude`,
  `FirstWorld.LandingLongitude`): the east shore of a bay, 21 m above the sea, on a meadow at a forest edge
  (birches, oaks, spruces, berry bushes); the bay within 2 km to the west and south-west; forested hills to 365 m
  nearby and to 1,160 m 18 km to the north-east (`surface site` in the generator report). The golden test of the surface includes points around it.
- **Look-dev views** (`docs/images/ground/`, 1600 × 900; commands in §4.6):
  - `a_landing_morning.jpg`: the landing site at 09:25 local time.
  - `b_golden.jpg`: the meadow 700 m south of it at 18:13, looking into the sun (6° up) through the forest edge.
  - `c_night_moon.jpg`: the same meadow at 22:07, the moon 38° up and 68% lit (behind the camera), the galaxy's
    band.
  - `d_hills.jpg`: forested hills 17 km north-east, from 60 m above a 620 m ridge.
  - `e_beach.jpg`: the beach 4 km south, along the coast.
  - `f_forest.jpg`: inside the forest 600 m north, toward the sun.
  - `g_2km.jpg`: from 2 km above the landing site toward the hills, the bay on the left.
  - `compare/`: the meadow and forest before and after the plants and grass were redone (same views and times).

---

## 4. Architecture

### 4.1 Projects

```
src/Hollow.Core          (exists) double vectors, DMath, Hash64, Rng, units
src/Hollow.Galaxy        (exists) galaxy, star systems, planet state
src/Hollow.Surface       planet surfaces: height, climate, biomes, materials, scatter; chunk meshes and collision
src/Hollow.Simulation    entities, components, systems: movement, items, crafting, building, combat, survival
src/Hollow.Net           messages, bit packing, snapshots and deltas, transport (LiteNetLib)
src/Hollow.Client        the client without graphics: connection, prediction, interpolation, client world
src/Hollow.Persistence   SQLite storage
src/Hollow.Server        the dedicated server; also a library the game hosts in-process for solo play
game/                    Godot: views, first person, terrain rendering, UI, audio, input
game/data/ground/        items, recipes, building parts, loot, weapons, survival constants (JSON)
tools/Hollow.Bots        headless clients for tests and load tests
third_party/             vendored packages, so the repository builds without network access
```

Dependencies point one way: `Core ← Galaxy ← Surface ← Simulation ← Net ← Client / Server`, and
`Persistence ← Simulation`. The game references `Client` (and `Server`, for hosting). Nothing in `src/` references
Godot.

```
      both sides run the same rules and data: Hollow.Simulation + game/data/ground
┌─ Godot client ─────────────────┐         ┌─ Hollow.Server (.NET, no Godot) ────────┐
│ Hollow.Client (no Godot):      │   UDP   │ zones: physics (Jolt) + systems, 30 Hz  │
│   prediction, interpolation    │<------->│ replication: who sees what, snapshots   │
│ Godot: views, first person,    │         │ persistence: SQLite                     │
│   UI, audio, input             │         │ sessions, chat, raid alerts, admin      │
└────────────────────────────────┘         └─────────────────────────────────────────┘
  bots = Hollow.Client without a window: 2 players + 100 bots, headless, in tests
```

### 4.2 Server

- One process. A **zone** is one surface region of one body: its entities, one Jolt physics world and the systems
  that run on them, at a fixed 30 ticks per second, on one thread at a time (ARCHITECTURE.md §10).
- **Tick order:** inputs → movement → actions (gather, build, craft, use) → projectiles and damage → survival →
  devices (furnaces, timers) → AI → decay and upkeep (slow) → replication. Dirty entities are saved every 30 s.
- **Static entities** (building parts, deployables, boxes) cost nothing per tick until something happens to them.
- **Natural things** (trees, ore, plants) come from the surface generator. Only changes are stored, such as "this tree
  is cut until T" (ARCHITECTURE.md P1).

### 4.3 Client

- **Hollow.Client** (no Godot) owns the connection and the client's copy of the world:
  - Input commands go out every tick.
  - The player's own character is predicted with Hollow.Simulation's movement code, against a local Jolt world
    built from the surface generator and the replicated static entities.
  - When a snapshot says "input 412 processed, you are here", the client rewinds and replays newer inputs.
  - Other entities are interpolated 100 ms behind the server.
  - Inventory and container contents are mirrored. Intents (build, craft, move an item) show at once in the UI
    and are confirmed or rejected by the server.
- **Godot** draws that world and nothing else: a surface mode with a floating origin, terrain from Hollow.Surface,
  entity views pooled by type, a first-person camera and viewmodel, third-person characters, UI and audio.
  Mouse look is applied every frame, never held back by the tick.
- **Bots** are Hollow.Client with scripted input. Tests run the server and bots headless.

### 4.4 Network

- LiteNetLib over UDP. Reliable ordered channel: commands, intents, static entities, container contents.
  Unreliable sequenced channel: inputs and snapshots.
- Messages are typed structs, bit-packed by generated code: no reflection or JSON on hot paths.
- Snapshots go out 20 times a second, delta-compressed against the last one the client acknowledged, for the
  entities its relevance grid selects, within a bandwidth budget (≤ 256 kbit/s in a fight).
- Shots: projectiles with drop and travel time are simulated on the server. Hit tests rewind targets to what the
  shooter saw, using one second of hitbox history. Clients never report hits.
- For testing, the transport can add latency, jitter and packet loss, and the client shows a network graph.

### 4.5 Physics and movement

- **Jolt** through JoltPhysicsSharp, on the server and in the client's prediction. A G1 spike confirms it runs in both
  processes; the fallback is BepuPhysics2 (pure C#).
- **Characters** use Jolt's virtual character. "Up" is away from the planet's centre, expressed in the zone's frame,
  so nothing assumes +Y is up. Walk, sprint, crouch, jump, swim, fall damage.
- **Collision:** terrain chunks are mesh shapes built from Hollow.Surface, identical on both sides. Building parts
  are boxes and convex hulls from data. Deployables and natural objects use simple shapes.
- Godot's own physics is used only for cosmetic things: ragdolls and debris.

### 4.6 Planet surface

`PlanetSurface` in Hollow.Surface is the real surface of a rocky planet; `SphereSurface` stays as the stand-in.

- **A pure function** of (universe seed, system, body, planet state, `GeneratorVersion.Surface`), built with `DMath`,
  `Hash64` and gradient noise made of + − × ÷ and floor only (ARCHITECTURE.md §8), so the server's collision and the
  client's terrain agree to the bit. A golden test pins heights and biomes at 600 points.
- **Cube-sphere:** six faces, each a square of face coordinates (u, v) ∈ [−1, 1]² mapped to the sphere through
  `tan(t·π/4)`, so tiles stay within a factor of 1.5 of the same size. A `TileKey` is face, level (0–28) and x, y;
  every grid coordinate is a dyadic fraction, exact in double, so parents and children share vertices bit for bit.
  On the first world a level-16 tile is about 154 m across and a level-18 tile about 39 m.
- **Tile grids:** 32 × 32 cells, each quad split along the same diagonal at every level. Vertices also carry their
  position on the parent's grid, so the client morphs between levels without popping, plus eight ground-material
  weights and the climate. **Collision is built from the level-18 grids** (1.2 m spacing), exactly the vertices the
  client draws near the player (`GroundCollision`; a test checks it).
- **Height**, in layers:
  - *Plates:* 14–20 plates (Voronoi on the sphere, seen through a warped direction so their edges bend), each
    continental or oceanic with its own Euler pole. Every plate near a point weighs in softly, and each boundary's
    relief is a function of the distance across it, so the field is continuous at triple junctions.
  - *Continents:* a warped noise plus the plates' bias, run through a crust curve (abyssal plains, continental slope,
    shelf, coast, lowlands, interiors). The coastline threshold is calibrated on 6,144 fixed directions until the full
    height puts exactly `OceanCover` under the sea (45.1% on the first world).
  - *Boundaries:* mountain belts where plates converge on land, trenches and island arcs at sea, rift valleys and
    spreading ridges where they part; their strength varies along each boundary. Old worn ranges in a few regions.
  - *Mountains:* the belts' uplift shaped by eroded ridged noise ("swiss turbulence": octaves displaced along the
    summed slopes), so crests are sharp, valley floors smooth and side valleys branch.
  - *Hills:* regional amplitude (mostly gentle, hilly in places and around ranges) over noise whose octaves are damped
    on steep slopes. Metre-scale ground on top, rougher on stony ground.
  - *Coasts:* a curve that flattens sandy shores into beaches (a regional "beach" field); rocky shores keep their
    slope.
- **Climate:** annual mean temperature from the planet's mean, latitude (42 K equator to pole), noise and a 6 K/km
  lapse rate; the ice line is calibrated so ice covers `IceCover`. Moisture from latitude bands (wet tropics, dry
  subtropics, wet mid-latitudes), distance inland and noise. Seasons are drawn, not generated (the sky moves).
- **Biomes:** ocean, beach, ice sheet, tundra, boreal forest, temperate forest, grassland, shrubland, desert, savanna,
  tropical forest and alpine ground, as soft weights from a Whittaker-like table. They give the eight ground materials
  (grass, forest floor, soil, rock, gravel, sand, snow, moss; slope turns ground to scree and rock) and plant
  densities, thinned by forest clearings, slope, tree line and shore.
- **Scatter:** per level-16 tile, a jittered grid of candidates per kind (trees 3.5 m, bushes 2.4 m, ferns 1.6 m,
  rocks 3.2 m), each kept with the probability its density gives. Trees grow in stands of one kind (fir, pine, birch,
  broadleaf). An object's ID is the tile plus its index: kind in the top byte, candidate cell below (ARCHITECTURE.md
  §7). Grass is a density per vertex, drawn by the client.
- **Speed:** about 3.7 µs per height and 7 µs per drawn vertex (with climate and materials), about 7 ms per tile on
  one core; the client builds tiles on worker threads.
- **Sky:** `SkyEphemeris` turns the planet's body frame in the system frame: the spin axis leans from the system's
  +Y toward −X by the axial tilt and stays fixed (seasons follow the orbit); the body turns 2π·t / period about it,
  prime meridian in the plane of the axis and +X at J2000. Sun, moon, the other suns and planets come from the orbit
  code at the universe time. The client draws physically based scattering for the planet's own air (Rayleigh, Mie,
  ozone, multiple scattering) with the same tables as the aerial perspective on everything under it.
- **Water:** the sea at `SeaRadius` (sea level), drawn on the terrain's own tiles.

**The surface view** (client, `game/scripts/Surface`; presentation only, never deciding what exists):
- **Terrain:** a quadtree of the tiles above around the camera, built on worker threads, morphing toward the parent
  grid with distance (no cracks: skirts), in a floating origin that moves with the camera without turning. Eight
  ground layers (texture arrays) blended by the vertices' material weights and their own heights, triplanar rock on
  cliffs, two scales near and a larger turned copy far so nothing repeats in rows; the grass layer takes the colour
  of the grass blades (below); far forests colour the ground with their canopy where their impostors end.
- **Air and light:** the sun (star B) and the moon at their real directions for the universe time; scattering for the
  thick air (transmittance and multiple-scattering tables, the sky ray-marched at quarter resolution) and the same air
  in front of everything through a 3D aerial-perspective table. Clouds: a layer of fair-weather cumulus (1.6–2.9 km,
  cover 0.35) with cloud shadows. Night: the two white dwarfs, the other planets, a star field and the galaxy's band;
  moonlight on the ground is brightened six times (dark but readable, as in Rust), the moonlit air only 1.5 times;
  the moon's disc is capped a few times above white so it never blooms over the sky. Exposure meters for the light
  falling on the ground, nights a few stops darker than days.
- **Water** reflects the sky and sun with Fresnel, shows the sea floor through water that absorbs red first, glows
  turquoise in the shallows, foams on the shore and moves with Gerstner swell near the camera.
- **Plants** are made in code after hollow-orbit's flora (no plant models): firs (spruce-like), pines, birches,
  broadleaf trees (oak and beech), bushes, red- and blue-berry bushes, ferns and boulders; two to four seeded variants
  per species, each with two meshes. Leaves are cards of a painted atlas (4 × 4 cells, mipmaps that keep the share of
  leaf texels so far foliage does not thin), alpha-tested with alpha to coverage, with vertex-colour occlusion,
  normals pointing out of the crown, hollow-orbit's foliage light (a warm sheen with the sun behind the viewer, light
  through leaves that face away from it) and little specular. Bark: painted plates and fissures with a normal map.
  Boulders: icospheres cut into facets, textured with the ground's rock layer in their own frame, moss on top.
  - *Levels:* trees draw their full mesh to 58 m, then impostors to 2.2 km (eight views of every variant, colour and
    normals, rendered into an atlas when the view opens); the second mesh only casts shadows, to 250 m; past 1.6 km
    forests become canopy colour on the ground. Bushes, ferns and rocks use their second mesh to 75–200 m and shrink
    into the ground at the end. Cross-fades are complementary dithers; distances are the main camera's, also in
    shadow passes.
  - *Wind:* every vertex carries a bend weight and a flutter weight; the plant bends along the wind with gusts that
    roll across the forest, leaves flutter along their normals.
  - Each plant is a scatter object, known by tile and index; berry bushes are their own kinds (`BerryRed` 7,
    `BerryBlue` 8) because they will be food.
- **Grass** is not in the scatter and has no models: `GrassField` draws one MultiMesh of blade tufts on a 0.4 m grid
  that follows the camera (to 42 m; they shrink into the ground from 22 m). Each tuft's place, height, width, turn,
  lean, colour, dryness and kind (grass, meadow grass, dry grass, flowers) come from a hash of its ground cell in
  stable coordinates, so nothing shifts as the grid moves. A field texture (256² at 0.5 m, rebuilt on a worker every
  14 m) holds the ground's height (the finest tiles' own triangles, so tufts sit on the drawn ground), the grass
  density (from the biomes, materials and slope), moisture and flowers. The blades are hollow-orbit's painted blade
  cards spread over each cell, coloured by one function shared with the ground's grass layer
  (`surface_grass.gdshaderinc`), their foot in the ground's colour, normals mostly up so a field lights as one, light
  through the tips toward a low sun; wind bends them in gusts drifting downwind with a ripple; they bend away from the
  walker's feet.
- **Time:** the clock runs at the universe time; `RotationTimeScale` (client setting `[surface]
  rotation_time_scale` in `user://settings.cfg`, or `--time-scale`) turns the planet faster than real without
  moving anything else. Keys: `[` `]` jump the clock an hour (Shift: six), T cycles the day speed 1×, 60×, 600×,
  3600×, P pauses, H hides the interface; a card in the corner shows local solar time, UTC and the speed.
  Screenshots pause at `--time`.
- **Speed** (1600 × 900, RTX 2070 Super, vsync on): 11 ms a frame (90 fps) at the landing site with 4,000 plant
  meshes, 414,000 impostors and 45,000 grass tufts; 13–15 ms (65–77 fps) above dense forest; 6–7 ms from the air.
  Opening the view takes about 4 s (tiles, the painted atlas and the plants, then the impostor bake).
- **Look-dev commands** for the views in §3 (add `--shot <file> --hide-ui` to save them):
  - (a) `--view surface --alt 1.7 --yaw 60 --pitch -2 --time 2026-10-12T03:00:00Z`;
  - (b) `--view surface --lat 44.86225 --lon -170.00150 --alt 1.7 --yaw 280 --pitch 0 --time 2026-10-12T15:50:00Z`;
  - (c) `--view surface --lat 44.86225 --lon -170.00150 --alt 1.7 --yaw 345 --pitch 12 --time 2026-10-18T17:30:00Z`;
  - (d) `--view surface --lat 44.97185 --lon -169.84163 --alt 60 --yaw 45 --pitch -4 --time 2026-10-12T03:00:00Z`;
  - (e) `--view surface --lat 44.83304 --lon -169.99447 --alt 1.7 --yaw 20 --pitch -2 --time 2026-10-12T03:00:00Z`;
  - (f) `--view surface --lat 44.87400 --lon -169.99949 --alt 1.7 --yaw 120 --pitch 6 --time 2026-10-12T03:00:00Z`;
  - (g) `--view surface --alt 2000 --yaw 20 --pitch -6 --time 2026-10-12T03:00:00Z`.
- **The interface between the two G1 work packages** is `ISurface` in Hollow.Surface: height at a point, biome at a
  point (`BiomeAt`), collision for a zone square, scatter for a tile (`ScatterFor`). The last two have default
  implementations, so other `ISurface`s keep compiling.

### 4.7 Persistence

SQLite through Hollow.Persistence, as in ARCHITECTURE.md §13:
- what's stored: characters, persistent entities grouped by region, overrides of natural objects, and zone
  metadata (`last_simulated_at`, versions);
- dirty entities are written every 30 s and on shutdown;
- deaths, trades and transfers commit at once.

---

## 5. Steps

Each step is multiplayer and playable. The owner playtests at the end of each one.

| Step | What you can play | Done when |
|---|---|---|
| **G1 Foundation** | Two players and bots on the first world. Walk, sprint, crouch, jump, swim, fall damage. See each other move. Chat. Day and night. After a server restart you're where you left off. | Two Godot clients, the server and 100 bots run on this PC. Prediction feels instant at a simulated 150 ms ping. Tick time and bandwidth are measured. The owner approves the surface from screenshots. |
| **G2 Survive** | Gather wood, ore, fibre and pickups with tools (bonus for the weak spot). Inventory, hotbar, clothing. Crafting queue and workbench tiers. Furnace, campfire, boxes. Health, food, water, temperature, bleeding. Melee and bow. Death leaves a lootable body; respawn on the beach, at a bag or at a bed; logged-off players sleep in the world. | Two players can play the first hours of a Rust wipe together. |
| **G3 Build** | Foundations, walls, doorways, windows, floors, stairs and roofs, in five grades. Upgrade, rotate, demolish. Stability and collapse. Doors with key and code locks. The claim device: build rights, upkeep, decay. | A base survives a server restart. A big base streams in without a hitch. |
| **G4 Fight** | Guns with drop, recoil (the AK pattern), aiming down sights, attachments and ammo types. Damage by body part, armour, wounded state. | A fight between two players at a simulated 150 ms ping feels fair to both. |
| **G5 Raid** | Explosives, damage by grade and weak side, raid alerts, offline raids. | A base can be raided while its owner is offline, and the owner is alerted. |
| **G6 World** | Animals, monuments with loot, guards and radiation, barrels, a recycler, a currency and a tech tree, teams, the map. | |
| **G7 More worlds** | The same game on any landable body; several zones per planet that merge and split (ARCHITECTURE.md §10.1). | |

Later: electricity and turrets, farming, traps, miners (our own feature), ground vehicles. Ships (M5) cover what
Rust's helicopters do.

---

## 6. Gameplay rules and their numbers

The rules follow Rust. The numbers live in `game/data/ground/` (ARCHITECTURE.md P8), with a note on each table
saying where it came from. Sources, in order of preference:

1. RustMVP's legacy specs: `docs/architecture/legacy/building_damage.md`, `inventory_survival.md`,
   `weapons_character_ui.md` (in `D:\GameDev\Projects\RustMVP`).
2. RustMVP's rule headers `unreal/Bare/Source/Bare/Rules/*.hpp`, including the AK recoil pattern in
   `weapon_definition.hpp`.
3. hollow-orbit's tables (`js/game/items.js`), building rules (`briefs/building.md`) and system contracts
   (`GAME_API.md`), in `D:\GameDev\Projects\hollow-orbit-source\hollow-orbit`.

| Area | Rules |
|---|---|
| Survival | Health, calories, hydration, temperature (planet climate, clothing, fires), bleeding, later radiation and comfort. |
| Gathering | Trees, ore nodes, plants, pickups, animal corpses. Yield grows with the tool; the weak spot gives a bonus. Nodes have depletion stages and respawn. |
| Inventory | 24 main slots, 6 belt, 7 wear. Stacks, splitting, quick move, drag and drop, loot panels. |
| Crafting | Queue with times, ingredients taken up front and refunded on cancel. Workbench tiers 1–3. Blueprints and tech tree in G6. |
| Building | 3 m module and storey. Square and triangle foundations, walls, doorways, windows, frames, floors, stairs, roofs, half and low walls. Five grades. Sockets and snapping, placement checks, stability, upgrade, rotate and demolish within a time window, repair. |
| Claims | The claim device (our tool cupboard): authorised players, build rights in an area, upkeep storage, decay when unpaid. |
| Doors | Wooden, metal and armoured, single and double, garage door. Key locks and code locks. |
| Combat | Melee, bows, firearms. Ballistic projectiles, recoil patterns, spread, aiming down sights, attachments, ammo types, body-part multipliers, armour. |
| Death | Wounded state, lootable body, respawn on the beach, at a sleeping bag or bed (each with a cooldown), sleepers. |
| Raiding | Explosives and explosive ammo, damage per grade, weak sides, raid alerts. |

---

## 7. Data

`game/data/ground/*.json`, read by the server and the client:
- items, recipes and categories;
- building parts, grades, costs and upkeep;
- deployables, loot tables, gather yields;
- weapons, ammo and recoil patterns;
- damage and protection tables, survival constants.

A test checks that every reference resolves and that every recipe can be made from gatherable resources.

---

## 8. Interface

The RustMVP interface is the reference, matched exactly: layout, colours, fonts, spacing, behaviour.

| Screen | Reference (in `D:\GameDev\Projects\RustMVP`) |
|---|---|
| HUD, hotbar, vitals | `docs/evidence/ui-restoration-2026-10-01/hud-final.png`, `logs/ui-polish/hud.png` |
| Inventory | `docs/evidence/ui-restoration-2026-10-01/inventory-final.png`, `logs/ui-reference-final/inventory.png` |
| Crafting | `logs/ui-reference-final/crafting.png` |
| Building and hammer wheels | `docs/evidence/building-2026-10-04/plan-wheel-build4-image-1.png`, `hammer-wheel-build4-image-1.png` |
| Specification | `docs/architecture/legacy/weapons_character_ui.md` |

- **Fonts:** Barlow and Barlow Condensed (SIL OFL 1.1), as in RustMVP (`assets/SourceUI/fonts`).
- **Built with `Ui`:** every length through `Ui.Px`, no stretch mode (AGENTS.md).
- **Icons** are rendered from our own models.
- **The character on the inventory screen is ours.** The reference image shows RustMVP's extracted body, which is
  never used (§9).

---

## 9. Assets

**Allowed sources:**
- Our own Blender scripts. Blender 5.2.1 is at `D:\GameDev\Tools\blender-5.2.1-windows-x64`.
- The PSG kits in `D:\GameDev\Assets`. They were built by Blender Python with Poly Haven CC0 textures:
  - `PSG_BuildingKit_R01`: 13 parts × 5 grades.
  - `PSG_DoorWindowKit_R01`: the source files; R02 is Unreal-only.
  - `PSG_ResourceNodes_R01`: 4 ore nodes × 3 depletion stages, and miner deposits.
  - `PSG_StarterKit_R02`: workbenches, boxes, tools, two guns, the cupboard, a bag and a code lock.
  - `PSG_C4VFX_R01`: explosion flipbooks.
  - Also `RustMVP/assets/PSG_FrontierAK_R05`, `Assets/ResourceNodes` and `TinFinch_TF01`.

  Rename their Rust item names, and redo any shape that copies a Rust model too closely: the ore-node notes call
  them "reconstructions".
- The texture and plant sets in `D:\GameDev\Assets`:
  - Poly Haven: CC0.
  - Megascans and other Fab content: check the licence of each item; some Fab content is Unreal-only. Use only sets
    whose Fab listing is on record (`Inventory`, or a download manifest with the listing URL).
  - LOLIPOP trees: CC BY 4.0, credit required.
- **In the game now:** the ground textures only (six Megascans surfaces and two Poly Haven sets, listed in
  `game/assets/THIRD_PARTY.md`, built by `tools/blender/surface_ground.py`). Plants, grass, rocks and their textures
  are made in code (§4.6); the Megascans and LOLIPOP plant models tried earlier were dropped.
- CC0 libraries and MakeHuman/CMU data, if the owner allows the downloads (§1).

**Never used** (extracted from Rust's game files):
- RustMVP's player body and first-person arms (`Content/Rust/Character`).
- Every animation derived from Rust (`Saved/RustReference`).
- The BareRustWorld maps and their cliffs, rocks, water and ice.
- The gas station monument: `Assets/Oxum-Test`, `RustMVP/assets/SourceMonuments/oxum`, and `client/world/monuments/oxum`
  in RustMVP's git history.
- `Assets/RustMapsResearch`.

**Pipeline:**
- **Models:** Blender Python scripts in the repository export GLB into `game/assets/`.
- **Kit FBX files:** centimetres (scale 0.01); `UCX_` collision renamed to Godot's `-col` / `-convcolonly`; use the
  OpenGL normal maps, or flip the green channel of the DirectX ones.
- **Textures:** 2K by default. The PC has 8 GB of video memory and 16 GB of RAM, and RustMVP ran out of memory on
  bulk loads.
- **Credits:** every third-party file is listed in `game/assets/THIRD_PARTY.md` with its source and licence.

---

## 10. How the work is run

- **Branches:**
  - The `ground` branch lives in `D:\GameDev\Projects\Hollow-wt\ground` and starts from `generator-v2` for the
    planet state.
  - Work packages run on their own branches and worktrees and merge into `ground`.
  - Nothing merges into `main` without the owner.
- **G1 runs as two packages in parallel:** network core (`ground-net`) and planet surface (`ground-planet`). They
  meet at `ISurface` (§4.6).
- **Every package:**
  - docs first;
  - build and tests green;
  - one scripted run and one screenshot per view;
  - a commit after each green step.

---

## 11. Open questions

1. **Day length:** keep each planet's real rotation (the first world's day is 35 hours), or compress rotation for play
   (÷ 24 makes it 87 minutes)? Orbits and seasons would stay real either way. Until decided, the client has one
   setting, `RotationTimeScale` (1 = real), and the time keys of the surface view (§4.6).
2. **Downloads for the character:** MakeHuman for Blender (CC0 human assets), the CMU motion-capture library (free for
   use in commercial games), Quaternius's CC0 animation library. Exact files and sizes will be listed before any
   download.
3. **Tech ladder:** confirm Rust's.
4. **Decisions taken in the surface work** (to confirm):
   - Plants are generated in code in hollow-orbit's style rather than scanned models; trees switch to impostors at
     58 m, which keeps the frame time near 11 ms.
   - Three Megascans surfaces on disk (Mossy Ground `tj4ocdjr`, Tundra Lichened Rock `vjbode1mw`, Tundra Mossy Ground
     `vjulbjjr`) have no listing on record, so they were replaced by listed sets and Poly Haven CC0 sets. The six
     listings used should be checked once for an "Unreal Engine only" note (none is expected for Megascans surfaces).
   - Cloud cover is fixed at 0.35 (fair weather) until weather exists; moonlight on the ground is brightened six
     times.
   - The landing site (§3) can still move: only the surface golden test's landing-area points depend on it.
