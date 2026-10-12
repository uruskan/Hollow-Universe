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
| Starter ship | The Tin Finch (`D:\GameDev\Assets\TinFinch_TF01`, our own Blender model): a single-seat skiff, crafted at the vehicle workbench. It is the first ship of the space part. | Decided |
| Day length | Planet rotation compressed 24 times for play: an Earth day becomes an hour, and the first world's day 87 minutes. Orbits and seasons stay real. Chosen by Claude while the owner is away; the owner may veto it. | Proposal |
| Who runs the work | Claude, on its own, until the whole thing is finished and polished; the owner is busy elsewhere and isn't asked to playtest in between. Progress is logged in `docs/progress-ground.md`. | Decided |
| Character | Our own character system: body from MPFB (MakeHuman), motion from the Quaternius CC0 libraries; downloads approved by the owner. Never RustMVP's extracted Rust character, even as a fallback. | Decided |

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
- **The first zone** sits at a landing site chosen during the surface look-dev: a temperate coast with forest,
  hills and mountains in view.

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
- **As built in G1** (`src/Hollow.Server`): a .NET console program and a library. One zone on the first world at a
  latitude and longitude from the command line (the planet package picks the landing site). Each tick: receive, finish
  logins, run admin commands, give every character its input for this tick, step the zone, send snapshots (20 a
  second), save every 30 s, send. Tick time is measured (average, 99th percentile, maximum).
  - **Login** is by player name for development: the name is the account, one character per name, refused if in use.
  - **Admin console:** `status`, `kick <name> [reason]`, `teleport <name> <x> <z>` or `teleport <name> <other>`,
    `say <text>`, `save`, `quit`. Ctrl+C or `quit` saves everyone, says goodbye to every client and stops.
  - **Host mode:** the game runs the same `GameServer` on a background thread for solo play (`--host`); other
    clients can join it on the same port.
  - Ground near the spawn is built at start, so the first arrival doesn't stall the tick.

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
- **As built in G1** (`src/Hollow.Client`, no Godot): `GameClient.Update(delta, input)` every frame receives, runs
  the ticks that are due (each predicts and sends its input) and advances what is drawn.
  - **Prediction:** a `Predictor` keeps 4 s of inputs and predicted states. A snapshot brings the server's state of
    the character after some tick; if it differs from the prediction for that tick, the predictor takes the server's
    state and replays the newer inputs. Identical inputs give identical results (§4.5), so on a clean line nothing is
    ever corrected.
  - **Smoothing:** a correction under 2 m is added to a visual offset that fades in about 0.1 s; a bigger one (a
    teleport, a respawn) shows at once. The drawn position blends the last two predicted ticks, so movement is smooth
    at any frame rate; the eye height eases between stances (RustMVP's rate 18).
  - **Remote entities** are drawn 100 ms behind a clock that follows the newest snapshot tick, blended between the
    snapshots on either side; if snapshots stop they carry on for up to 4 ticks along their last motion, then hold.
  - **Statistics:** round trip (measured by echo), snapshot rate and loss, bandwidth both ways with UDP and IP
    headers, prediction error (last, mean and worst), input margin, clock rate.
- **The ground mode in Godot** (`game/scripts/Ground/`, G1 placeholders): `--view ground` connects to a server
  (`--connect host:port`) or hosts one in the game for solo play (`--host`, the default). First-person camera at the
  eye, mouse look every frame, W A S D, Shift sprint, Ctrl crouch, Space jump. Other players are capsule mannequins of
  the character's size with a visor and their name (placed by `Labeller`). The ground is drawn from the same collision
  tiles, 448 m across, coloured by slope, under a placeholder sky and haze; the planet package's renderer replaces
  it. F3 shows the network graph, Enter opens chat. Zone-frame metres are Godot's coordinates (no floating origin yet:
  the zone stays within 8.2 km of its origin, where floats are good to half a millimetre).

![Two clients: Bo sees Ada](images/ground/two-players.png)

### 4.4 Network

- LiteNetLib over UDP. Reliable ordered channel: commands, intents, static entities, container contents.
  Unreliable sequenced channel: inputs and snapshots.
- Messages are typed structs, bit-packed by generated code: no reflection or JSON on hot paths.
- Snapshots go out 20 times a second, delta-compressed against the last one the client acknowledged, for the
  entities its relevance grid selects, within a bandwidth budget (≤ 256 kbit/s in a fight).
- Shots: projectiles with drop and travel time are simulated on the server. Hit tests rewind targets to what the
  shooter saw, using one second of hitbox history. Clients never report hits.
- For testing, the transport can add latency, jitter and packet loss, and the client shows a network graph.

**As built in G1** (`src/Hollow.Net`):
- **Transport:** LiteNetLib 2.1 in manual mode (no background threads: the server polls once a tick, a client once a
  frame). Reliable ordered: `Welcome`, `Spawn`, `Despawn`, chat. Sequenced (unreliable, newest wins): inputs and
  snapshots.
- **Connecting:** the connection request carries `Hello` (protocol version, the generated schema hash, the name). A
  refusal carries a `Goodbye` with a reason (protocol mismatch, bad name, name in use, server full; also kick and
  shutdown). An accepted player gets `Welcome`: net and entity IDs, the server tick, the rates, the zone (body, radius,
  gravity, surface seed, latitude, longitude) and the saved view.
- **Clocks:** every input is for a tick. The client runs its tick clock ahead of the server's so inputs arrive in
  time: every snapshot says how early this client's inputs arrived (the least over the last half second). The client
  aims for 2 ticks: 4 % faster below, 3 % slower above 4, and it jumps when inputs arrive late or very early. A jump
  forward runs the extra ticks over the next frames (their inputs still go out); one of over a second skips them (the
  server and the prediction both fill skipped ticks by repeating the last input). A jump back pauses. The first lead
  comes from the connect handshake's round trip.
- **Inputs:** one packet per client tick: the tick's input (move axes in 1/127, yaw and pitch in 1/65,536 of a turn,
  buttons: jump held, jump pressed this tick, crouch, sprint) and the three before it, the snapshot acknowledgements and
  a client timestamp for measuring the round trip. Only four losses in a row lose an input. The server applies each
  tick's input on that tick; without one it repeats the last (without the jump press) and counts a miss.
- **Snapshots:** 20 a second per client (every 1.5 ticks on average). The header has the sequence, the server tick,
  the input margin, the player's own character exactly (floats, for reconciliation) with its tick, and the echo. Then
  the relevant entities, each delta-coded against its newest state the client confirmed (acknowledged by sequence and
  32 bits), highest accumulated priority first, within 1,600 bytes and the path's MTU. Remote positions are on a
  1/256 m grid.
- **Relevance:** a 64 m grid; an entity becomes relevant within 300 m (reliable `Spawn`) and stops beyond 320 m
  (`Despawn`). Everyone's own character is always theirs.
- **Bad network:** `ConditionedRelay`, a local UDP relay below LiteNetLib (whose own simulation is compiled out of its
  release package), adds round-trip latency, jitter and loss in both directions; clients and bots take `--lag <ms>`,
  `--jitter <ms>` and `--loss <percent>`.

### 4.5 Physics and movement

- **Jolt** through JoltPhysicsSharp 2.23 (joltc 1.1), on the server and in the client's prediction. **The G1 spike
  passed (12 October 2026):** a fixed script of 600 ticks of input (walk, sprint while turning, jumps, crouch, strafe)
  walks a virtual character over mesh ground from `ISurface.GroundCollision`, with up from `ZoneFrame.UpAt`, on a
  stand-in planet of the first world's size. A .NET console process and the Godot 4.7.2 client (which also loads
  Godot's own Jolt) print the same final position and the same hash of every state on the way
  (`0x6E7FDE937D5A46FA` on win-x64 with the movement code of 12 October 2026; any change to movement changes it).
  BepuPhysics2 is not needed. The check stays: `dotnet run --project tests/Hollow.Tests -- MovementCheck` and
  `$GODOT --headless --path game -- --physics-check` must print the same line
  (`Hollow.Simulation.Movement.MovementCheck`).
- **One step is a pure function** of the character's state (feet position, velocity, stance, health), one tick of
  input and the static world. Every step first puts Jolt's character exactly at the state and finds its contacts
  afresh, on the server as in prediction, so the client can rewind to any state the server sends and replay its
  inputs to the same bits. Results are repeatable within one build and platform; across platforms they may differ in
  the last bits, which reconciliation absorbs.
- **One lock for Jolt.** joltc's character functions share one temporary allocator inside the native library, so
  `JoltRuntime.Gate` serialises every call into Jolt in a process (a hosted server and the client's prediction share
  the game process). A zone holds it for its movement pass. Zones ticking in parallel (G7) will need joltc's
  per-caller allocators.
- **Characters** use Jolt's virtual character, a capsule (radius 0.32 m, 1.76 m standing, 1.10 m crouched) whose
  position is its feet. "Up" is away from the planet's centre at the character, expressed in the zone's frame
  (`ZoneFrame.UpAt`), so nothing assumes +Y is up; the view's yaw is measured about the zone's +Y and laid onto the
  local horizontal. Characters don't collide with each other in G1 (§11), so prediction depends only on the static
  world and the player's own inputs.
- **Movement** follows RustMVP's numbers (`MovementTuning`, from `weapons_character_ui.md` §2): walk 2.8 m/s,
  sprint 5.5 m/s (straight forward only), crouch 1.3 m/s, acceleration and braking 60 m/s², ground friction 8, air
  control 0.25, jump 7.0 m/s (7.6 crouched), steps up to 0.40 m, slopes up to 46°.
  - **Gravity** is the planet's surface gravity times RustMVP's gravity scale 1.8: 16.2 m/s² on the first world, so a
    standing jump rises 1.51 m.
  - **Fall damage** (RustMVP had none; Rust's rule): landing faster than 15 m/s toward the ground costs
    500 × (speed − 15) / 85 health, rounded up. A fall of about 7 m starts to hurt; about 31 m kills.
  - **Swimming** where `ISurface.SeaRadius` > 0: a character swims once the water reaches 65 % of its height (Rust's
    threshold), steers in the view direction (look down to dive), rises with jump and sinks with crouch, and floats
    at 75 % depth otherwise. 2.0 m/s, 3.0 m/s with sprint. Landing in water never hurts.
- **Collision:** the ground is static mesh tiles of 64 m built from `ISurface.GroundCollision`, identical on both
  sides. A tile's Jolt body ID follows from its grid position (Jolt orders contacts by body ID), so tiles agree where
  they meet whatever else each side holds. Tiles exist within ±8.2 km of the zone's origin. Building parts are boxes
  and convex hulls from data. Deployables and natural objects use simple shapes.
- Godot's own physics is used only for cosmetic things: ragdolls and debris.

### 4.6 Planet surface

- **A pure function** of (body, planet state, point on the sphere, surface generator version), built with `DMath`
  (ARCHITECTURE.md §8), so the server's collision and the client's terrain agree to the bit.
- **Cube-sphere:** six faces, quadtree tiles. Chunk meshes with levels of detail out to the horizon for drawing;
  collision only near players. A zone uses a tangent frame at its origin (ARCHITECTURE.md §6).
- **Height:** continents and oceans that match the ocean cover; mountain ranges and rifts from plate tectonics;
  hills, valleys, coasts and beaches.
- **Climate and biomes:** temperature from latitude, height, season and the planet state; moisture; a biome table
  that picks ground materials and plant sets.
- **Scatter:** trees, bushes, grass, rocks, ore nodes and plants per tile, deterministic. Their IDs are tile + index
  (ARCHITECTURE.md §7).
- **Sky:** scattering from the planet's own air; the real sun, the two white dwarfs, the moon and the other planets
  at their real positions for the universe time; the star field from the galaxy generator.
- **Water:** oceans and lakes, with shorelines.
- **The interface between the two G1 work packages** is `ISurface` in Hollow.Surface: height and normal at a point,
  biome at a point, collision chunk for a tile, scatter for a tile. The network work starts on a simple
  implementation of it and switches to the real one when it lands.

### 4.7 Persistence

SQLite through Hollow.Persistence, as in ARCHITECTURE.md §13:
- what's stored: characters, persistent entities grouped by region, overrides of natural objects, and zone
  metadata (`last_simulated_at`, versions);
- dirty entities are written every 30 s and on shutdown;
- deaths, trades and transfers commit at once.

In G1 (`Hollow.Persistence`): accounts by player name (the development login, case-insensitive) and one character per
account, stored with its position in the **body frame** (metres from the planet's centre, turning with it), so it
comes back at the same place on the planet whatever zone frame the server uses next. Also its view angles, stance and
health. The schema version is SQLite's `user_version`; opening a database migrates it step by step and refuses one
newer than the build. Every database call runs on one worker thread in the order made, so a tick never waits for the
disk and a login always reads the last save. Characters are saved on disconnect, every 30 s and on shutdown.

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
| **G6 Ship** | The vehicle workbench crafts the Tin Finch. Board it through its hatch; first-person cockpit; take off, fly and land anywhere on the first world, and climb to orbit. Other players see it fly. | Two players can fly one to the other side of the planet and land beside a base. |
| **G7 World** | Animals, monuments with loot, guards and radiation, barrels, a recycler, a currency and a tech tree, teams, the map. | |
| **G8 More worlds** | The same game on any landable body; several zones per planet that merge and split (ARCHITECTURE.md §10.1). The ship reaches the moon and the other planets (supercruise, M5). | |

Later: electricity and turrets, farming, traps, miners (our own feature), ground vehicles. Ships (M5) cover what
Rust's helicopters do.

### 5.1 G1 network measurements

Measured on the owner's PC (12 threads, 16 GB) on 12 October 2026 with the stand-in surface. The server runs alone
in one process; 100 bots run in another, each a full client that predicts its character. Bots wander, circle and jump
within 120 m of the spawn, so every bot sees the 99 others (the bandwidth worst case). 60 s measured after an 8 s
warm-up:

```bash
dotnet run -c Release --project tools/Hollow.Bots -- loadtest --count 100 --duration 60
dotnet run -c Release --project tools/Hollow.Bots -- loadtest --count 100 --duration 60 --lag 150 --loss 5
```

| | Clean loopback | 150 ms ping, 5 % loss each way | Target |
|---|---|---|---|
| Server tick, average | 4.25 ms | 4.27 ms | ≤ 10 ms typical |
| Server tick, 99th percentile / worst | 10.2 / 26.9 ms | 9.5 / 24.7 ms | ≤ 33 ms |
| Bandwidth per client, down (99 characters in view, UDP and IP headers included) | 114 kbit/s | 123 kbit/s | ≤ 256 kbit/s |
| Bandwidth per client, up | 18 kbit/s | 17 kbit/s | |
| Snapshots lost | 0 % | 4.6 % | |
| Inputs that missed their tick | 0 of 180,000 | 2 of 180,000 | |
| Prediction: corrections / reconciliations | 0 / 120,000 | 1 / 114,069 | |
| Prediction error: mean / worst | 0 / 0 mm | < 0.001 / 1.9 mm | |

- **Prediction is exact** whenever the server applied the input the client predicted with: the clock keeps inputs
  2 ticks early, and each packet carries four ticks of input, so loss rarely costs one.
- **Ground tiles** cost about 10 ms each to build (3.7 ms sampling the stand-in surface, 6.2 ms for Jolt's mesh tree),
  so tiles two out from every character are built ahead on worker threads; the worst ticks left are a tile a character
  needed before it was ready, and garbage collections.
- A server sharing its process with the bots measured 5.1 / 16.2 / 36 ms (their allocations and collections land on
  its ticks), which is why the load test runs the bots in a child process.
- **With jitter as well** (150 ms ping, ±20 ms jitter, 5 % loss, 30 s): 99.89 % of inputs on their tick, 11
  corrections in 56,211 reconciliations (mean 0.011 mm, worst 14 cm), 17 clock jumps among 100 bots. Jitter
  sometimes makes an input late before the clock moves; the clock then runs the missing ticks quickly rather than
  skipping them, so their inputs still go out. Server ticks in that run were 6.2 ms average, 16.9 ms 99th percentile
  and 48.5 ms worst (two over budget): the server, 100 bots and the relay share one PC, and the worst ticks vary from
  run to run.

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

### 6.1 As built in G2

The rules live in `Hollow.Simulation` (engine-free); the server applies them, the client runs the same code for its
optimistic updates. Numbers are in `game/data/ground` (§7); the source of each is noted there.

**Inventory** (`Items/`). 24 main slots, 6 belt slots, 7 clothing slots (`PlayerInventory`). A stack is an item ID, a
count and, for tools and weapons, a condition (uses left).
- **Move** (drag): onto an empty slot it moves (a split when part of a stack); onto the same item it tops that stack up
  and leaves the rest where it was (Rust; RustMVP refused the whole move); onto another item it swaps, whole stacks
  only.
- **Quick move** (right-click): onto partial stacks of the same item first, then empty slots that take it; what
  doesn't fit stays. Its targets depend on where the item is: from the loot to main then belt; to the open loot;
  clothing to the clothing slots; main to belt and back.
- **What goes where**: clothing slots take clothing only, one item per layer (head, face, torso, torso armour, legs,
  legs armour, hands, feet), so a shirt and a chest plate go together; a campfire's or furnace's fuel slot takes fuel,
  its input slots what it cooks, its output slots nothing; corpses are take only.
- **Gains** (picking up, gathering, crafting, loot): onto stacks of the same item in main and belt, then empty main
  slots, then the belt. What doesn't fit is dropped at the player's feet.
- Every request carries the item the client saw in the slot; if it changed meanwhile (another looter took it), the
  request is refused as stale rather than moving something else.

**Crafting** (`Items/CraftQueue.cs`, RustMVP §7). Up to 8 jobs of up to 999 units. Starting a job checks the recipe,
the workbench level within 4 m and the materials for every unit, then takes them all. Only the front job advances, a
unit every `seconds × 30` ticks; a finished unit goes into the inventory (dropped at the feet if full, as in Rust, rather
than holding the queue as RustMVP did). Cancelling any job gives back the ingredients of every unit left, the one in
progress included; what doesn't fit is dropped. At death the queue's ingredients go into the corpse. A workbench counts
only when the job starts (Rust). Crafting pauses while the player is offline or asleep.

**Gathering** (`Nature/`). Natural objects come from `INaturalObjects`: objects in a rectangle of the zone's tangent
plane, each with a stable 64-bit ID (tile and index), a kind, a base point, up, size and yaw. The planet package's
scatter will provide them; until then `StandInNaturalObjects` places each kind at its density per 64 m tile from a
seeded stream (deterministic: the server and every client place the same objects), keeping 9 m around the zone's origin
clear. `ZoneSpec.CreateNature` is the one place that picks the provider.
- **Trees and outcrops** have health (gather.json × the object's size). A hit takes the tool's tree or ore rate
  (items.json) and gives the share of the object's whole yield that health was, in whole units, so the hits add up to
  exactly the total: a better tool is quicker, not richer. The last hit gives the rest. A 0.94-size pine (206 wood)
  takes 32 hits with a rock, 13 with a stone hatchet, 9 with a metal one.
- **The weak spot** (Rust): it shows with the first hit on a tree or outcrop, on the side facing the hitter (within 50°
  either way), on the trunk at chest height or on the outcrop's upper side. A hit within 0.3 m of it counts double and
  moves it, again to the hitter's side at a new height. Its direction and height are part of the stored change and
  are sent to clients, who draw it. (First placed at random around the object, the spot was on the far side half the
  time: the bots found it on 3 hits in 132; facing the hitter, 91 in 113.)
- **Outcrops** shrink through three stages as they lose health (drawn by the client).
- **Pickups** (piles of wood, stone and ore, fibre plants, wild tubers) and **berry bushes** are taken whole with the
  interact key; a pickup is gone, a bush stays bare, until it grows back.
- **Respawn** on the real clock (Unix milliseconds): trees after 20 minutes, outcrops 10, pickups and bushes 8. Only
  changes are stored (`NaturalState`: health, weak-spot moves, depleted until), and saved.
- **Collision**: trees (capsules) and outcrops (spheres) are static Jolt bodies while their ground tile exists, on the
  server and in every client's prediction (`NaturalBodies`); a felled tree's body goes, and comes back when it does.
  Their body IDs come after every ground tile's, so contacts sort the same way on both sides; two props touched at
  once may sort differently and cost a small correction.

**The zone's game** (`Zone.*.cs`). A zone built with `GameContent` runs the survival game after movement each tick, in
§4.2's order: fall damage into the vitals; actions (held item, swings, shots); projectiles and damage; once a second
survival, natural respawns, despawns and loot reach; devices; crafting; then the hit history. Players' actions are
`Zone` methods (`MoveItem`, `QuickMove`, `DropItem`, `UseItem`, `Craft`, `CancelCraft`, `Open`, `Close`,
`PickUpNatural`, `PickUpItem`, `Toggle`, `Place`, `Respawn`), each checking everything and either doing all of it or
nothing, with a reason (`ActionResult`). Every new persistent thing gets an `EntityId` from the server's allocator.

**Survival** (`Zone.Vitals.cs`, once a second, survival.json). Health 0–100, calories 0–500, hydration 0–250; a new life
starts at 60 / 260 / 160 (hollow-orbit's respawn values).
- Needs fall by 0.07 calories and 0.09 water a second, ×1.8 sprinting, ×1.4 cold. At zero, starvation costs 0.2 health
  a second and thirst 0.3.
- Healing: health owed by food and medicine comes back at 1 a second; well fed (≥ 250 calories, ≥ 120 water) and not
  bleeding, 0.04 a second more, twice that by a fire.
- **Bleeding** (ours): a hit with a blade, spear or arrow adds its damage × the weapon's bleed fraction to a pool, lost
  at 1 health a second while the pool clots by 0.2 a second; bandages and injectors empty it.
- **Temperature**: the air (`IClimate`: the stand-in has the first world's mean 18 °C, ±3.5 °C over the compressed
  87-minute day, −6.5 °C a kilometre up) + clothing insulation + warmth from a lit campfire (up to +20 °C within 4 m)
  or furnace − 10 °C for two minutes after swimming. Below 5 °C felt a character gets cold (fully at −10 °C); cold
  costs calories faster and, past 0.6, 0.15 health a second. The first world is mild, so cold matters on mountains and
  when wet at night.
- Sleepers are left as they are (no hunger, no bleeding) until they wake. Crafting pauses for them too.

**Deployables** (`Zone.Things.cs`, `Zone.Actions.cs`). Placed from an inventory slot at a point within 3 m of the eye,
snapped to the ground below (at most 0.6 m off), refused under water, outside the zone, on ground steeper than the
item allows (20–30°), or overlapping another deployable, a tree, an outcrop or a character (blocked). Boxes, furnaces and
workbenches are solid Jolt boxes on both sides; sleeping bags and campfires are not. Every deployable has hit points;
melee does its `structure` damage, arrows a quarter of theirs; at 0 it breaks and its contents spill on the ground.
- **Sleeping bag**: a respawn point for its owner only, 300 s cooldown from placing and after each use (real clock, as
  in RustMVP, so it runs while the server is down).
- **Campfire and furnace**: lit by hand when they have fuel; a lit one burns a unit of fuel every `fuel.seconds` (wood:
  2 s) leaving its by-product (charcoal) in an output slot, cooks every input slot at once (metal ore 4 s, sulphur ore
  2 s, high-grade ore 8 s in a furnace; wild tubers 15 s on a campfire), waits when the outputs are full and goes out
  when the fuel does. Both give warmth while lit.
- **Workbenches**: a level counts for crafting within 4 m.
- **Boxes**: 18 and 48 slots.

**Dropped items**: a drop lands on the ground in front of the player, lies there for 300 s of world time (it pauses
while the server is down) and is picked up with the interact key, as much as fits.

**Death and respawn** (`Zone.Players.cs`). At 0 health everything carried and worn, and the crafting queue's
ingredients, go into a corpse where the player fell (any number of slots), which anyone may take from but not put into;
it goes when emptied or after 300 s of world time (Rust's corpse lifetime). The dead player chooses: a random beach point
(no wait, as in Rust; survival.json can add one) or one of their own sleeping bags that has cooled down. A new life has
the spawn vitals, a rock and a torch. Until the planet package marks beaches, "the beach" is a random point 15–60 m
from the zone's origin (`Zone.Beaches` takes the real coast).

**Sleepers**: a player who logs off stays where they were, asleep (lying hit shapes), lootable and killable; looting a
sleeper shows their main slots, belt and clothing, take only. A sleeper killed leaves a corpse like anyone. Logging in
wakes them where they lie; a player killed while away logs in dead and chooses a respawn.

**Melee** (`Zone.Combat.cs`). The held item comes from the belt slot each input names; taking an item up takes 0.3 s.
While the primary button is held a melee item swings every `swing` seconds and strikes `strike` seconds after the swing
starts: a ray from the eye along that tick's view, thickened to 0.15 m, out to the item's reach (1.6–2.4 m). The first
thing it meets takes the hit: a character (damage), a tree or outcrop (gathering), a deployable (`structure` damage), or
the ground (nothing). Every hit costs the tool a point of condition; at none it breaks.

**The bow**: holding the secondary button draws it (1 s) while there are arrows; the primary button looses one: from the
eye along the view at 50 m/s, under the planet's surface gravity (9.0 m/s² on the first world; not the characters'
1.8×), in two steps a tick, for up to 4 s. Holding on draws the next. An arrow 20 m to a chest flies 0.4 s and drops
0.7 m; loosed flat from eye height it comes down about 30 m out. An arrow that hits the ground, a tree or a deployable
can be picked up again half the time.

**Damage to characters**: the weapon's damage × the body part (head 2, body 1, limbs 0.5; RustMVP) × (1 − protection),
where protection is the sum of what worn items covering that part give against melee or projectiles (at most 0.8).

**Lag compensation** (ARCHITECTURE.md §11.5). The server keeps every character's pose (feet, up, yaw, standing,
crouching or lying) for 36 ticks (`HitboxHistory`). Each input says how many quarter ticks behind its own tick the
player was drawing everyone else; a swing or an arrow is tested against characters where they were then, interpolated
between ticks as the client draws them, at most 0.25 s back. Trees, outcrops, deployables and the ground are tested as
they are now. An arrow keeps its shooter's delay for its whole flight. Hit shapes: a head sphere (r 0.13 m), a body
capsule (r 0.21 m), leg and arm capsules; crouching scales them down; sleepers lie. Built for guns to reuse (G4).

### 6.2 Network, persistence and the client API (G2)

**Protocol** (version 2, `Messages.cs`). The connect `Hello` carries the hash of the client's game data; other data is
refused (`ContentMismatch`). Each tick's input also carries the belt slot in hand, the attack and aim buttons (attack
pressed is latched like jump) and the view delay in quarter ticks (lag compensation). A character's snapshot state
adds the item in hand, a 4-bit count of swings and shots with the last kind (others animate each), and asleep.
- **Intents** (client → server, reliable ordered, each with a sequence number): move, quick move, drop, use, craft,
  cancel craft, open, close, pick up (a natural object or a dropped item), place, light or put out, respawn. The
  server applies them as they arrive (at the start of a tick) and answers every one with `IntentResult` (accepted, or
  the `ActionResult` reason) after that tick's changes have gone out.
- **Budget** (RustMVP's command budget): 16 at once, 20 a second; every arrival spends one, a bad one too; over it the
  answer is `RateLimited`, and the server counts accepted, refused and rate-limited intents per player.
- **The player's own state** (reliable, to them alone): their three containers in full at joining, then changed slots
  (`ContainerSlots`); what they loot (`LootState`, then the loot's containers in full, then changes, also to every other
  looter); vitals when a whole unit changes; the crafting queue when it changes (ticks left counted from a server
  tick); life (dead, how, by whom, beach and bag respawn choices with their cooldowns). The container, loot and
  crafting messages carry the last intent the server had applied, so the client drops exactly the pending intents its
  copy now includes.
- **Events** (reliable, to whom they concern): hit markers, items gained or lost and why, damage taken (amount, body
  part, from where, with what), a craft finished; projectiles loosed and stopped (to everyone within 300 m), which
  clients draw on their own arc.
- **Static things** (deployables, dropped items, corpses) are sent once when they come within 300 m (`ThingSpawn`; at
  most 48 new ones per snapshot, so a crowded place streams in), again when they change (`ThingUpdate`: health, count,
  lit, the receiver's own bag) and `Despawn`ed when gone or out of range (320 m).
- **Natural objects** are generated on both sides; only changes travel: when a 64 m area comes within range its
  changed objects are sent (health as a fraction, weak-spot moves, depleted), then each change as it happens.
- **Clothing** others wear (`CharacterLook`) is sent with a character's spawn and when it changes.

**Server** (`GameServer.Survival.cs`). The zone runs with the game data; the server sets its real clock each tick,
applies inputs and actions, steps it, then routes its events, sends each player's own state, the container changes,
and the answers; static things and natural changes go with each snapshot. A player who logs off stays as a sleeper
(saved at once); logging in finds their sleeper and wakes it. Admin console: `give <name> <item> [count]`,
`kit <name> <loot table>`, `heal <name>`, `kill <name>` besides G1's.

**Persistence** (schema 2, `GameDatabase`, `WorldCodec`). Characters gain `dead` and `state` (their vitals, inventory,
crafting queue, beach cooldown and last death, in a versioned binary record); new tables hold each body's static things
(`things`: kind and a versioned binary record with the position in the body frame), natural overrides
(`natural_overrides`: health, weak-spot moves, depleted until) and zone metadata (`zones`). The whole zone (every
character, sleeper or not, every thing, and the natural changes since the last save) is written in one transaction
every 30 s, at once on a death, and at shutdown; a character also when they log off. At start the server loads its
body's characters (as sleepers, or dead), things and overrides. World-time timers (corpses, dropped items, a unit of
crafting, a fire's fuel) are saved as what is left and pause while the server is down; real-time ones (bag cooldowns,
respawning nature) keep running. Decoding treats the stored bytes as untrusted: an item the game no longer has is
dropped. A G1 database migrates in place; G1 characters come back with a new life's kit.

**The client** (`Hollow.Client`). `GameClient.Local` (`LocalPlayer`) is the API the UI binds to:

| Member | What |
|---|---|
| `Inventory` (`Main`, `Belt`, `Wear`), `Loot`, `LootBelt`, `LootWear`, `LootKind`, `LootTarget`, `LootTakesDeposits` | Containers as the server has them, with intents not yet answered applied on top. The player's own three are the same objects all session; loot ones are new per open. |
| `Vitals` | Health, calories, hydration (and their maxima), felt temperature, cold, bleeding, healing, comfort, wet. |
| `Crafting` | Jobs front first: recipe, count, progress of the unit in hand, seconds left, still pending. |
| `Life`, `IsDead`, `SecondsUntilReady(option)` | Alive or dead; how, by whom; respawn choices (the beach, each bag) and their countdowns. |
| `HeldSlot`, `HeldItem`, `WorkbenchLevel`, `CanCraft(recipe, count)` | The belt slot in hand (set by the UI; it goes out with every input), the best bench within 4 m, whether a recipe can start. |
| `MoveItem`, `QuickMove`, `Drop`, `Use`, `Craft`, `CancelCraft`, `Open`, `Close`, `PickUpNatural`, `PickUpItem`, `Place`, `Toggle`, `Respawn` | Intents. Each returns its sequence number and, where the outcome is known (moves, drops, uses, crafting, placing, closing), shows at once. |
| `Changed`, `ContainerOpened`, `ContainerClosed`, `HitMarker`, `ItemsChanged`, `DamageTaken`, `CraftFinished`, `IntentAnswered`, `IntentRejected`, `Died`, `Respawned` | Events for the UI. A refused intent has already been rolled back when `IntentRejected` fires. |

Beside it, `GameClient` keeps the static things in range (`Things`, `ThingOf`), the natural objects and their changes
(`Nature`, `Natural`, with trees and outcrops solid in the prediction's physics), projectiles in flight
(`Projectiles`), others' held items, swings, sleep and clothing (`RemoteEntity`), and finds what the interact key would
act on (`Aim(eye, direction)`, `Interact(target)`). `InputFrame` gains the attack and aim buttons.

---

## 7. Data

`game/data/ground/*.json`, read by the server and the client:
- items, recipes and categories;
- building parts, grades, costs and upkeep;
- deployables, loot tables, gather yields;
- weapons, ammo and recoil patterns;
- damage and protection tables, survival constants.

A test checks that every reference resolves and that every recipe can be made from gatherable resources.

**As built in G2** (`Hollow.Simulation.Content`, engine-free): `GameContent.Load(folder)` reads the seven files below,
resolves every reference by key and checks them all; it lists every problem at once (a missing field, an unknown name,
a typo'd field) and refuses to start with any. `GameContent.FindFolder()` takes `HOLLOW_GROUND_DATA`, else
`game/data/ground` above the program. A hash of the files' text goes into the connect handshake, so a client with other
data is refused. Each file starts with a `source` note saying where its numbers came from.

| File | What | Sources |
|---|---|---|
| `items.json` | 42 items: resources, tools, melee weapons, the bow and arrows, medical, food, clothing, deployables. Each has a stable numeric `id` (saves and the wire use it; never reused) and a `key` (data uses it). Blocks: `melee` (damage, reach, swing and strike times, gather rates on trees and ore, damage to deployables, bleeding), `bow`, `ammo`, `consume`, `wear` (layers, body regions covered, protection, insulation), `fuel`, `cook`, `deploys`. | hollow-orbit `items.js`; RustMVP stack sizes; Rust's bow, arrows, spears and berries |
| `recipes.json` | 28 recipes: output, amount, seconds, workbench level, ingredients, known. A recipe's ID is its output's. | hollow-orbit; Rust for spears, bow, arrows; times ours |
| `gather.json` | Natural kinds: trees, three outcrops (three depletion stages), pickups (piles, fibre plants, wild tubers), red and blue berry bushes; health, yields, respawn, shape; the weak spot (radius 0.3 m, ×2). | hollow-orbit `planet.js`; Rust's pickups and weak spot |
| `deployables.json` | Sleeping bag, campfire, furnace, small and large box, workbenches 1–3: hit points, half extents, steepest slope, solid or not, slots, device slots (fuel, input, output), warmth, bag cooldown, workbench level. | hollow-orbit `DEPLOY`; Rust's box sizes; RustMVP's 300 s bag |
| `loot.json` | Loot tables (guaranteed entries, then weighted rolls): the respawn kit (rock and torch), a supply crate for tests, the load test's kit. | Rust |
| `survival.json` | Vitals, metabolism, bleeding, temperature, the climate stand-in, body-part multipliers, rewind limit, corpse and dropped-item lifetimes, reach, action rate limits. | RustMVP, hollow-orbit `player.js` |
| `categories.json` | The crafting tabs. | Rust's grouping |

The checks (`ContentTests`): unique IDs and keys; every reference resolves; sensible ranges; and a fixpoint from what
natural objects yield, through recipes, workbench levels, cooking and fuel by-products, proves every recipe can be made.
Charcoal, for example, comes from wood burnt in a campfire, which is made of wood; the furnace needs lamp oil, made from
charcoal and fibre.

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
  - Megascans and other Fab content: check the licence of each item; some Fab content is Unreal-only.
  - LOLIPOP trees: CC BY 4.0, credit required.
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

1. **Day length:** compressed ×24 is the working default (§1). The owner may switch it back to real rotation.
2. **Downloads for the character:** approved by the owner on 12 October 2026 (Quaternius animation libraries, MPFB),
   with free use of other CC0 and CC-BY sources.
3. **Tech ladder:** Rust's is assumed.

Decisions taken during G1 without the owner, to confirm or overturn:

4. **Character gravity** is the planet's surface gravity times RustMVP's gravity scale 1.8 (§4.5). Without the scale
   RustMVP's 7 m/s jump would rise 2.7 m on the first world; with it, 1.51 m.
5. **Fall damage** uses Rust's rule (safe below 15 m/s, 500 × (speed − 15) / 85), since RustMVP has none.
6. **Swimming numbers** are ours until tuned: swim at 65 % depth, float at 75 %, 2.0 m/s (3.0 with sprint).
7. **Characters pass through each other** in G1. Colliding characters need the other players' positions in
   prediction, which are 100 ms old; it comes back with combat (G4) if it's wanted.
8. **The zone's origin** is 28.4°, −61.7° until the landing site is chosen (the stand-in surface is the same
   everywhere); the server takes `--lat` and `--lon`, and characters are stored in the body frame, so moving the zone
   later keeps them where they were on the planet.
9. **Development login** is the player name alone: no password, one character per name. Real accounts come with the
   account service and connect tokens (ARCHITECTURE.md §11.1).
10. **Placeholders in the client** until their owners land: the ground drawn from collision tiles and a fixed sun
    (planet package), capsule mannequins (character work), a minimal HUD (the RustMVP interface, G2). Day and night
    come with the planet package's sky.
