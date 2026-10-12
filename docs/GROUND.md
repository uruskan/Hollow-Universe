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

### 6.3 Building, claims and doors (G3)

Built in two phases. **Phase 1** (this section): the rules, engine-free and tested, their data, the kit's art and a
local sandbox that previews placement with the same code. **Phase 2**, after the survival package: network intents,
persistence, costs taken from the inventory, the claim device's interface.

| Part | Where |
|---|---|
| Rules | `src/Hollow.Simulation/Building/`: `BuildingWorld` (structures, links, stability, collapse; partial files for placement, the hammer, damage), `PieceGeometry` (sockets and snapping), `PieceShapes` (collision), `BuildingPhysics` (Jolt), `Doors`, `ClaimDevice` (rights, upkeep, decay) |
| Numbers | `game/data/ground/building.json`: module sizes, placement, hammer, stability, grades, the 20 pieces with their costs and sockets, the plan wheel's order, doors, locks, the claim device |
| Tests | `tests/Hollow.Tests/BuildingTests.cs` (`dotnet run --project tests/Hollow.Tests -- Building`) |

**Sources.** RustMVP's engine-free rules were ported to C# line by line: `building_rules.hpp` (pieces, sockets,
snapping, support losses, stability, upgrade, repair, the edit window), `building_geometry.hpp` (reach to the nearest
logical surface, aiming at a slab's whole footprint, opening planes, the seams that may overlap) and `damage_rules.hpp`
(damage summed by type, rounded up once). Its spec `docs/architecture/legacy/building_damage.md` gave the costs, the
hit points (the owner's 10 / 200 / 500 / 1,000 / 2,000), the ground check, the placement order, the cupboard and the
door and code-lock rules. The kits' `dimension_contract.json` and `assembly_manifest.json` gave every size and hinge.
hollow-orbit's `construct.js` gave decay times (Rust's 1, 3, 5, 8 and 12 hours), upkeep and the claim radii; Rust gave
its upkeep brackets, the weak side and the key lock. Names are ours.

**Frame.** A structure is the pieces linked to each other through sockets. It has its own frame: its origin is its
first foundation, +Y the planet's up there (`ZoneFrame.UpAt`), turned to the player's view in 15° steps. Pieces snap
in that frame, so a base stays square on a curved world; across a 100 m base the curvature is below a millimetre. All
yaws in a structure are multiples of 30° (squares turn by 90°, triangles by 120°), so they are stored as whole steps
with exact sines and cosines. Positions are doubles in the frame; Jolt and the renderer get floats. Demolishing a
connector splits a structure in two with the same frame; a piece linking two structures of one frame joins them.

**Pieces and sockets** (RustMVP's 20, with their ids): square and triangle foundations; square and triangle floors and
floor frames; wall, half wall, low wall, window, doorway and wall frame; L, U, spiral and triangle spiral stairs, ramp
and steps; square and triangle roofs. The module is 3 m, the storey 3 m, walls 30 cm thick, foundations 1 m deep, floors
20 cm. Sockets: a slab's edges take walls, floors, roofs and (on a foundation) foundations level or half a storey up or
down; its centre takes stairs of the same footprint; a wall's top takes walls, floors and roofs; a staircase's top takes
floors and the next module; a square roof's high edge takes roofs, floors and walls. Each piece's sockets are listed in
the data; the snap formulas are code (`PieceGeometry.Snap`), and a test checks that every pair of neighbouring slabs,
square and triangle, closes its shared edge exactly under a turned parent.

**The plan's aim** (RustMVP's preview): every compatible socket of the pieces near the aim, in every orientation, is
scored by how near the aim passes (a floor by its whole footprint, so a ceiling can be chosen by aiming anywhere on it),
how far it is from what the aim hits, and how close its facing is to the one asked for (a wall's hard side away from
the player, stairs and roofs rising away; R turns it). The best eight are checked and the first allowed wins; if none
is, the best shows with its reason. With no socket near, a foundation goes on the ground: along the aim, at most 3.5 m
across, its top 5 cm above the highest ground under it.

**Placement checks**, in this order, each refusal with a line for the player (`Refusal`, `Outcome.Text`):

| Check | Rule | Refusal |
|---|---|---|
| Size | at most 4,000 pieces in a structure | "This building can't grow any bigger" |
| Reach | 8 m from the eye to the piece's nearest logical surface | "Too far away" |
| Sight | nothing between the eye and that point (35 cm of slack; the ground never hides a foundation) | "Something is in the way" |
| Rights | no claim device the player isn't authorised on covers the spot (2.25 m padding) | "Building blocked: someone else's claim" |
| Spot | no piece of the same kind (slab, wall, stairs, roof) within 1 cm | "Something is already built there" |
| Ground | under every corner and the centre of a foundation: ground within 1 m below its top, at least 5 cm below, with a normal of at least 0.75 up | "…would float", "…would be buried", "The ground is too steep" |
| Collision | a Jolt query of the piece's real hulls: pieces block unless linked through a socket or meeting on a seam (wall on a slab's edge, slabs side by side, walls at a corner); the ground blocks everything but foundations; doors, devices and natural objects block | "It would cut into another piece", "…into the ground", "Something is in the way" |
| Players | no character's capsule reaches into the piece | "A player is in the way" |
| Stability | at least 0.1 % | "Not stable enough (0%)" |
| Claims | it would not link two claimed structures | "That would join two claimed buildings" |
| Price | the twig cost in wood | "Not enough wood" |

**Stability** (RustMVP's): foundations hold 100 %. Along each support link the value drops by the link's loss: nothing
from foundation to foundation or to a wall; 10 % for a staircase on a slab's centre, anything on a wall or staircase,
and a roof; 20 % along any other slab edge. A piece takes its strongest path from the ground, so a loop never holds
itself up. A foundation, four walls and a floor on them: 100, 100, 90 %; floors reaching out past the walls 90 (on the
wall's top), 70, 50, 30 and 10 %, and the next can't be placed. Links come only from exact snaps.

**Collapse.** When a piece goes (demolished, destroyed, decayed), the structure is solved again and the pieces left at
0 % come down in sequence: layer by layer outward from the lost support through their links, one layer every 0.2 s,
announced at once (they stop supporting anything) and removed on the zone's ticks. Over elapsed time (decay) they go at
once.

**Hammer.** Upgrade to any higher grade, paying that grade's cost in its material; the piece keeps its fraction of
health (Rust's rule; RustMVP healed it fully). Rotate (walls flip their soft side, stairs turn 90°, triangle spirals
120°; not slabs, roofs or a frame holding a door) and demolish (no refund) only within 10 minutes of placing, the
boundary included. Repair: +100 health a hit, not within 30 s of damage ("Recently damaged: repair in 20 s"), for
max(1, ⌈cost × 0.2 × restored ÷ max⌉) of the grade's material, worked in whole millionths (a stone wall at 400 of 500:
12 stone). Every hammer action needs reach and building rights.

**Doors.** Deployable items fitted into an opening: single doors into doorways, double doors and the garage door into
wall frames, one per opening, never upgraded. Wooden 200, sheet metal 250, armoured 1,000 health (RustMVP), garage 600
(ours). Their leaves swing on the kit manifest's hinges (single: 56.2 cm left of centre, 17.2 cm out, 100°; doubles
mirrored) and collide open or shut; a turn of 180° on placing swaps the hinge side. A garage door rolls up. Opening a
door needs no rights, only its lock's consent, and its new pose clear of players. Picking a door up needs it open with
no lock on it. **Key lock** (Rust's): its owner opens it without a key; anyone with a key cut for it (its key code)
opens it and it stays locked. **Code lock** (RustMVP's, no guest codes): four digits; setting the code locks it and
leaves only the setter authorised; the right code authorises (up to 64); wrong codes make the player wait 1, 5, 5, 5,
then 30 s; lock, unlock and take off by an authorised player, taking off only when unlocked.

**Claim device** (our tool cupboard): stands on a foundation or floor, one per structure, its owner authorised.
Anyone may authorise at it; only someone on the list may clear it. It covers the points within 16 m of any of its
structure's pieces or 25 m of itself, so the area grows with the base and ends where the base ends; a player builds,
upgrades, rotates, demolishes, repairs and fits doors and locks there only if every covering device lists them
(RustMVP's strict rule: Rust's twig-floor exception for strangers isn't kept). Without a device anyone builds.

**Upkeep and decay.** A day's upkeep is each piece's cost at its grade times Rust's bracket rate: the first 15 pieces
pay 10 %, the next 50 15 %, the next 125 20 %, the rest 33.3 %. The device's store pays each material until it runs out
("protected for" is the shortest); a piece whose material isn't paid, or with no device, loses its full health over its
grade's decay time (twig 1 h, wood 3, stone 5, metal 8, armoured 12). `AdvanceTime` applies any interval in closed form
(ARCHITECTURE.md §10.4): between events everything is linear, and the events are a material running out and a piece
reaching zero (removed, with what it held), after which the rates are worked out again. Work grows with the events,
not the interval: 82 hours of a ten-piece base take 3 ms. A test runs the same base once over 82 hours and in 29,520
steps of 10 s: the same pieces stand, with the same health within a millionth and the same materials taken.

**Damage hook** (for G5's explosives): `Damage(piece, hit)` sums bullet, melee, explosive and heat through the grade's
transmissions and rounds up once (RustMVP's resolver), capped at the health left. A wall hit from its inside (−Z, the
soft side) uses the grade's soft transmissions, Rust's weak side: 100 melee does 5 to a stone wall's hard side and 100
to its soft side. Bullets don't hurt stone and up; explosives hurt every grade alike. A destroyed piece collapses what
it held. Doors take their material's protection.

**Static entities.** Pieces are plain data: nothing runs per tick but a collapse in progress. A world of 1,024 pieces
ticks in 0.007 µs touching none (a test).

**Collision** comes from the module sizes, never from the art, the same on the server and every client: one hull for a
slab or a wall, jambs and lintel around an opening, a frame's border strips, the kit's spiral (twelve 10 cm treads of
15° rising 12.5 cm about a 12-sided column), solid ramps for the ramp, the steps and the first flights of the L and U
stairs, sloped slabs for their second flights and for roofs. Each piece is one static Jolt body (a hull or a compound of
hulls) with an ID from the building range after the ground tiles' (`PhysicsWorld.FirstBuildingBody`). Queries pass the
matrix in Jolt's column-major layout and collide with back faces, so a slab whose middle is underground still cuts the
ground.

**Art** (§9): `art/import/building_kit.py` and `door_kit.py` run in Blender 5.2.1 in background mode and write
`game/assets/building/`:

| File | What |
|---|---|
| `building_<grade>.glb` (twig, wood, stone, metal, armoured) | Every piece under its key, with `_LOD1` and `_LOD2` (the kit's three levels) and its hulls as `<key>_colNN-convcolonly` (Godot's convex collision; the game's collision comes from the rules, not these). The kit's 13 families come from its FBX files; the 7 pieces it lacks (low wall, L and U stairs, ramp, steps, square and triangle roofs with their gables) are built by the same script with the kit's own generator (its `MeshBuilder`, tier surfaces and atlas tiles, and the twig revision's `TwigMesh`), so they share its look and texel density. |
| `doors_wood.glb`, `doors_metal.glb`, `doors_armored.glb` | Single and double doors: frame insert and leaves (each leaf's origin is its hinge), with levels and hulls. |
| `garage_door.glb` | The frame and the skinned curtain with the kit's Open and Close clips. |
| `window_inserts.glb` | Metal and armoured bars, reinforced glass, horizontal and vertical embrasures (art only in phase 1). |
| `textures/` | Each grade's atlas (base colour, OpenGL normal map, ORM) shrunk from 4K to 2K JPEG, stored once: the GLBs carry no images, and the doors share the grades' sets (the door kit's are the same files). Saved as stored, without Blender's view transform; every channel's mean is within 0.006 of the kit's. |

Metres, the kits' axes turned into Godot's by the glTF exporter (a wall's outside is +Z, as in the rules). Godot imports
the GLBs without generating levels of its own (`.import`: `generate_lods=false`) and the textures VRAM-compressed. The
GLBs total 94 MB, nearly all the kit's bevelled finest level.

**The build sandbox** (`--view build`, `game/scripts/Build/`): a local zone on the first world's stand-in surface,
a walking character (the real movement code), the rules above and their art, and the ground interface's belt and
wheels through a view model (`BuildSandboxUi`). The ghost is `BuildingWorld.Plan`'s answer, green when allowed, red
with the reason. Keys: 1 plan, 2 hammer, 3–5 doors, 6 claim device; the right mouse button holds the plan's or the
hammer's wheel (RustMVP's, from §8); the left places, repairs, fits a door or places the device; R turns the piece or
flips a door; E opens and shuts a door; F2 the stability overlay; F5 an hour of upkeep and decay. Removed pieces fall
and sink away, layer after layer. A scene's view is held for screenshots; otherwise the builder stands under it and
walks. `--build-check` plays a round through the sandbox's own input paths (a free foundation, a wall snapped to its
edge, the hammer's upgrade and turn, a doorway and a door aimed into its opening, E, then demolishing the foundation
and watching the rest come down) and quits with exit code 0 when all nine steps pass.

```bash
$GODOT --path game --resolution 1600x900 -- --view build --scene house --shot house.png
# --scene house | ghost-valid | ghost-invalid | stability | stress | empty;  --overlay;  --tool 1–6;  --piece <key>;
# --wheel plan|hammer;  --from x,y,z --at x,y,z (the camera in the scene's frame);  --finest / --lod n (draw one level)
```

**Rendering.** Pieces are batched into one `MultiMeshInstance3D` per piece, grade and level of detail in each 24 m
chunk of the zone; only a batch whose pieces changed is rebuilt. Levels change at 26 m and 64 m, neighbouring levels
overlapping by a metre (Godot's visibility margins are hysteresis: with them a still camera left whole batches drawn by
neither level). Measured on the owner's PC at 1600 × 900, vsync off, shadows and SSAO on, the 1,060-piece block
(`--scene stress`: 10 × 10 rooms, three storeys, every grade) in full view:

| | Frame | Draw calls | Triangles |
|---|---|---|---|
| Levels of detail (as shipped) | 5.0 ms average, 8.9 ms worst over 300 frames (199 fps; 4.4 ms in a quieter run) | 365 | 12.8 M |
| Finest level only | 14.1 ms average (71 fps) | 364 | 49.4 M |

| View | Screenshot |
|---|---|
| A two-storey house in mixed grades: stone foundations and doorway with a sheet-metal door, metal and wood walls, windows, an open wooden double door, wood floors, U stairs, metal roofs with gables, a twig porch | `images/ground/build/house.jpg` |
| The plan's ghost, allowed (a foundation beside the house) and refused (a sixth floor off a balcony: "Not stable enough (0%)") | `ghost-valid.jpg`, `ghost-invalid.jpg` |
| The stability overlay: the house 100 %, the balcony 90, 70, 50, 30, 10 % | `stability.jpg` |
| The 1,060-piece block with its frame time | `stress.jpg` |
| The plan's wheel with the rules' pieces and costs | `wheel.jpg` |

![The house](images/ground/build/house.jpg)

**Phase 2 needs from the survival package:** item definitions for the plan, the hammer, the doors, the key and code
locks, keys and the claim device; an `IBuildingWallet` over the player's inventory; the claim device as a deployable
whose container is its upkeep store (`ClaimDevice.Stock` today); its damage routed to `BuildingWorld.Damage` (melee by
side); the zone calling `BuildingWorld.Tick` each tick and `AdvanceTime` on a slow schedule and once with the elapsed
time on load; typed intents (place by support, socket and orientation; upgrade, rotate, demolish, repair; doors, locks,
codes; claims) and the replication of structures, pieces, doors and devices (static, reliable, when they change); body
IDs replicated with pieces so prediction meets the same contacts; persistence of structures in the body frame.

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

The RustMVP interface is the reference, matched exactly: layout, colours, fonts, spacing, behaviour. It is built
(G2) in `game/scripts/UI` and runs on sample data in the viewer:

```bash
$GODOT --path game --resolution 1600x900 -- --view ui --screen inventory --shot inventory.png
# --screen hud | inventory | loot | corpse | furnace | campfire | crafting | death | wheel | hammer | menu
```

| Screen | Reference (in `D:\GameDev\Projects\RustMVP`) | As built (`docs/images/ground/ui/`) |
|---|---|---|
| HUD, hotbar, vitals | `docs/evidence/ui-restoration-2026-10-01/hud-final.png`, `logs/ui-polish/hud.png` | `hud.jpg` |
| Inventory and loot | `docs/evidence/ui-restoration-2026-10-01/inventory-final.png`, `logs/ui-reference-final/inventory.png`, `loot-circle-preview.png` | `inventory.jpg`, `loot.jpg`, `furnace.jpg` |
| Crafting | `logs/ui-reference-final/crafting.png` | `crafting.jpg` |
| Building and hammer wheels | `docs/evidence/building-2026-10-04/plan-wheel-build4-image-1.png`, `hammer-wheel-build4-image-1.png` | `wheel.jpg`, `hammer.jpg` |
| Death and respawn | Rust's own, `D:\GameDev\References\RustMVP-2026-09-20\20260920-inventory-controls\10-death-respawn-future.png` | `death.jpg` |
| Main menu | `logs/ui-polish/menu.png` | `menu.jpg` |
| Other scales | | `crafting-720p.jpg` (scale 0.8), `inventory-1440p.jpg` (scale 1.3125) |
| Specification | `docs/architecture/legacy/weapons_character_ui.md` | |

- **Numbers** come from RustMVP's final interface, `unreal/Bare/Source/Bare/UI/SBareRoot.cpp`: every position, size,
  colour and font size on its 1600 × 900 design canvas. Where its Godot predecessor (`e569e19`) differs and the
  reference shots agree with it, the Godot one wins: the centred recipe title and table headers, the darker amount
  field.
- **Drawn as RustMVP drew it:** one control, `GroundUi`, paints each screen in immediate mode on the design canvas, in
  partial files per screen. Every design length goes through `Ui.Px`, so at 1600 × 900 each element sits where
  RustMVP put it and at other sizes it is still whole pixels; fonts and icons are rasterised at their final size.
- **Anchoring:** the canvas sits centred on the window's bottom edge, so the belt doesn't move when the inventory
  opens. Vitals, statuses and pickups follow the bottom-right corner; chat and notices the bottom-left; the centre dot,
  prompts and wheels the centre; the inventory and crafting tabs hang from the top edge. The scale is the usual
  `Ui.Scale` (1.0 up to 1080p, ARCHITECTURE.md §15).
- **Style:** `Ui.Ground` in `Ui.cs`, beside the map styles: RustMVP's palette (ink `#E3DFD6`, panel `(.71,.69,.65)`
  at 17 %, selected blue, health, water, food), its type rule (regular Barlow Medium; bold Barlow SemiBold below 26 px
  and Barlow Condensed Bold from 26 px) and its text fields.
- **Fonts:** Barlow and Barlow Condensed (SIL OFL 1.1), from RustMVP, in `game/assets/fonts/Barlow`.
- **Icons:** every screen asks `ItemIcons` (an `IItemIconProvider`). Until the icon package renders them from our
  models, placeholders are flat vector drawings (RustMVP's own placeholder shapes and new ones in their style).
- **The character on the inventory screen is ours.** The reference image shows RustMVP's extracted body, which is
  never used (§9). Until the character package supplies the body, a plain wooden mannequin stands in, rendered into a
  texture the size of its panel.

### 8.1 View model

The screens never talk to the network. They read and act through `IGroundUi` (`game/scripts/UI/Model`), plain C# with
no Godot types, which an adapter over the client API implements. `MockGroundUi` implements it with sample data and
local rules (stacking, swapping, splitting, quick moves, a crafting queue with refunds, bag cooldowns) for the viewer.

| Part | Contract |
|---|---|
| Items | `Item(key)`: name, description, category, stack size, wearable, usable, INFORMATION stats. Unknown keys get a stand-in. Stacks: key, amount, condition (0–1 or none), loaded rounds (or none). |
| Containers | `Slots(container)`: main 24, belt 6, clothing 7, and the open container's. A slot is `(container, index)`. |
| Vitals | Health /100, calories /500, hydration /250 (as RustMVP), body temperature (°C), comfort, bleeding (health per second), wetness, building rights. Cold below 5 °C, hot above 40 °C. |
| Belt | `ActiveBelt` (−1 for none); `Ammo`: loaded, reserve, reloading. |
| Loot | `Loot`: kind, title, slots; for furnaces and campfires also burning, progress and fuel seconds. Slot order per kind: a box any number; a body 0–23 main, 24–29 belt, 30–36 clothing; a furnace 0 fuel, 1–2 input, 3–5 output; a campfire 0 fuel, 1–4 cooking. |
| Crafting | `Categories` (name, count), `Recipes` (result, category, seconds, ingredients, yield, workbench level), `Workbench` in reach, `Have(key)`, `Queue` (ticket, item, count, seconds left, total seconds). |
| Death | `Death`: alive for, killed by (a player or a cause), weapon, distance, place on the map; `Bags`: id, name, cooldown, place on the map. |
| HUD | `Prompt` (key, action, more options). Events: hit confirmed (head shot), damaged (bearing), gathered (item, amount), crafted (item, amount), notice, chat line. |
| Wheels | `Wheel(kind)`: the plan's pieces or the hammer's actions in clockwise order (glyph, title, description, cost line, reason it can't be chosen); `WheelCurrent(kind)`. |
| Actions | `Move(from, to, amount)`, `Split(slot, amount)`, `Drop(slot, amount)`, `QuickMove(slot)`, `Use(slot)`, `Craft(key, amount)`, `CancelCraft(ticket)`, `Respawn(bag or beach)`, `SelectBelt(i)`, `SetBurning(on)`, `ChooseWheel(kind, i)`, `CloseLoot()`, `Say(text)`. Each is a request; the answer is a changed read, or a notice. |

`GroundUi` itself raises the menu's requests (host solo, connect with name, address and port, quit) and tells the game
when it wants the mouse pointer (`WantsCursor`).

**In the game:** the ground mode puts `GroundUi` on a `CanvasLayer` with the client adapter as its `Model`, frees the
mouse while `WantsCursor` is set, and leaves Tab, Q, Esc, 1–6, Enter, H and the right mouse button (with a plan or
hammer held, `HeldWheel`) to it. Until that hook lands the ground mode keeps its placeholder HUD
(`scripts/Ground/GroundHud.cs`).

### 8.2 Behaviour

RustMVP's rules, with Rust's where RustMVP had none:

- **Keys:** Tab opens and closes the inventory (even from a text field); Q opens crafting, and again returns to the
  inventory, closing any open container; Esc closes the wheel, the inventory or crafting, then opens the menu; 1–6
  select the belt; Enter opens chat. A container opened in the world opens the inventory with it.
- **Inventory:** a click selects a slot and shows the item panel (name, description, INFORMATION, ACTIONS, and
  SPLITTING for stacks). Dragging moves the whole stack; middle-dragging half, with left Shift a third (rounded up);
  dragging the split icon moves the slider's amount. Right-click moves a stack across (into the open container and
  back, or between main and belt). Holding H, each filled slot the pointer rests on for 350 ms moves across once,
  with a green ring filling meanwhile. Released on another slot a drag merges, swaps or splits; released on a panel
  it does nothing; released outside every panel it drops the items.
- **Item actions:** USE (medicine, food), WEAR or UNEQUIP (to the first free clothing or main slot), DROP.
- **Quick craft:** what the player can craft now (up to three rows); a click crafts one.
- **Crafting:** categories with their counts, search, the recipe grid (the mouse wheel scrolls it), the details with
  the workbench badge (green when in reach, red when not), time and yield, ingredients with TOTAL for the amount and
  HAVE (short ones in amber), − / amount / + / the most the player can make / CRAFT, and favourites (kept on this
  computer). The queue at the bottom left shows each job's seconds and count, the first one's progress; a click
  cancels and refunds.
- **HUD:** the centre dot; the hit marker (red for a head shot); red arcs pointing to where damage came from; the
  interaction prompt; the belt with the active slot in blue, condition bars and loaded rounds; the ammo counter above
  it; status rows above the vitals (building privilege or blocked, bleeding, cold or hot, wet, comfort); pickups and
  crafts ("+20 Wood") above those, fading after four seconds; chat at the bottom left, fading after twelve; RustMVP's
  one-line notices.
- **Death:** Rust's layout. DEAD, how long the player lived, who killed them, with what and from how far across the
  top; the map with the body and the bags (a ready bag on the map can be clicked); the bags with their cooldowns along
  the bottom, RESPAWN (a random beach) on the right. The map opens centred on the body and pans by dragging; bag
  names are placed by the `Labeller`, so they never cover each other or a marker.
- **Wheels:** held right mouse button with the plan or the hammer; release (or click) chooses, Esc cancels. The plan's
  sectors start at −81° so the top ray falls between the foundation and the triangle roof; the hammer's first sector
  is centred at twelve o'clock.
- **Menu:** PLAY hosts a solo game; CONNECT opens the panel with name, address and port (HOST SOLO or CONNECT); QUIT.
  The name and address are remembered.
- **Check:** `--view ui --ui-check` plays these gestures through Godot's input (click, drag, middle-drag, right-click,
  drop outside and on a panel, split, Q, CRAFT, Tab) against `MockGroundUi` and quits with 0 when all pass.

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
- **As built (G3):** `PSG_BuildingKit_R01` (13 families × 5 grades, three levels each, plus 7 pieces built in its style)
  and `PSG_DoorWindowKit_R01` (doors, the garage door, window bars, glass, embrasures) by `art/import/building_kit.py`
  and `door_kit.py` (§6.3).

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

Decisions taken for the interface (§8) without the owner, to confirm or overturn:

11. **Where RustMVP's references disagree, its final code wins.** The 20 September Godot shots set small bold labels
    in Barlow Condensed with a shadow; twelve minutes later RustMVP switched to "cleaner text" (Barlow SemiBold below
    26 px, no shadow), which both its final Godot and Unreal code keep, so we do too. Likewise the plan wheel's order
    and its −81° start: RustMVP re-matched them to Rust's screenshot on 4 October, after the build-4 capture we have.
12. **No compass:** RustMVP had none (Rust has one at the top); it can come with the map.
13. **Rust's manner where RustMVP had nothing:** the death screen (RustMVP's was a plain card with the same choices),
    damage arcs, status rows, the pickup feed, chat, the furnace, campfire and body loot layouts, dropping outside
    the panels, and the USE / WEAR / DROP actions (RustMVP showed only WEAR, at the bottom of the column).
14. **The ground screens keep `Ui.Scale`** (1.0 up to 1080p) instead of RustMVP's stretch-to-fit; on a 1080p screen
    the inventory stays 1600 × 900 pixels, centred on the bottom edge, and the tabs stay on the top edge.
15. **Our names for items**, plain words or our own (Service Pistol, Frontier Rifle, Refined Metal, Claim Device);
    the RustMVP catalogue's Rust names aren't carried over.
16. **The menu has no picture:** RustMVP's coast image isn't ours to reuse. The world behind is blurred and darkened
    under RustMVP's vignette instead. The menu is English, like the rest of the interface (RustMVP's was Turkish).
17. **Placeholders until their packages land:** item icons (vector drawings), the mannequin, and the death screen's
    map (a fixed-seed coast).

Decisions taken for building (§6.3) without the owner, to confirm or overturn:

18. **Hit points are RustMVP's, the owner's:** twig 10, wood 200 (Rust has 250), stone 500, metal 1,000, armoured 2,000.
19. **Reach is RustMVP's 8 m** to the nearest logical surface (the owner's correction in RustMVP); Rust's feels shorter.
20. **Claim radii are hollow-orbit's** (16 m around every piece, 25 m around the device): RustMVP's 30 m around every
    piece would block a 70 m square around a one-room base.
21. **Upgrades keep the health fraction** (Rust's rule) where RustMVP healed fully; neither has a time window.
22. **Up to 4,000 pieces a structure**: RustMVP's 100 is far below the 1,000-piece bases G3 must carry.
23. **Soft-side and melee transmissions are ours** until G5 tunes raiding: stone's hard side lets 5 % of melee through,
    its soft side all of it; metal 2 % and 50 %; armoured 1 % and 25 %.
24. **Collapse runs at 0.2 s a layer** (ours); RustMVP removed everything in one commit.
25. **The garage door has 600 health** and rolls up; the L and U stairs, ramp, steps, low wall and roofs aren't in the
    kit, so their art and collision are ours (§9).
26. **A key lock starts unlocked**, and a key carries its lock's number; key crafting and keys as items come with phase 2.
27. **Upkeep counts pieces only** for now; Rust also charges for doors and window inserts.
28. **The building art is 94 MB of GLB** in the repository (the kit's bevelled finest level is most of it), as plain
    binary files like the character's. Git LFS would suit it; that is the owner's call.
29. **Textures are 2K JPEG** (normal maps at quality 95), kept beside the GLBs rather than in them, so the doors and the
    grades share one copy.
30. **Levels of detail change at 26 m and 64 m** (ours), and the ghost is the stone piece's second level in green or red.
31. **The sandbox has no network:** it runs the rules in the game process on the stand-in surface; the same rules run on
    the server in phase 2.
