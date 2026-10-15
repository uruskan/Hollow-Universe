# How Hollow works, end to end (as built, 14 October 2026)

This is the guided tour of the ground game's code: what the layers are, what runs where, what happens in one tick,
which classes exist and how they talk to each other. ARCHITECTURE.md says what was designed; GROUND.md says what each
feature does; GROUND_SYSTEMS.md §12 says why the code has the shape below. Read this one when you want to know how it
actually runs.

## 1. The layers

Everything is C#. Nothing under `src/` references Godot (a test enforces it). An arrow means "may use".

```
Hollow.Core ──► Hollow.Galaxy ──► Hollow.Surface ──► Hollow.Simulation ──► Hollow.Net ─────┐
 vectors,        the 1:1 galaxy,     planets' terrain,   the rules of the     messages, bit      │
 Hash64, Rng,    stars, names,       biomes, the sky,    ground game: the     packing, snapshot   ├──► Hollow.Server
 DMath, units    star systems        the zone frame      zone and its         deltas, relevance  │     the dedicated server
                                                         systems, physics,    grid, the relay    ├──► Hollow.Client
                                                         entities, events,    Hollow.Persistence ┘     the client without
                                                         content (data)       SQLite, codecs            graphics (prediction)
                                                                                                             │
                                                                                                             ▼
                                                                                                   game/ (Godot, C#):
                                                                                                   drawing, sound, input, UI
```

The simulation knows nothing about the network; the server and the client both run the same simulation code (the
server authoritatively, the client to predict its own character and ship). The Godot project only draws what the
client knows and feeds it input. Tools (`tools/`) sit beside: `Hollow.Bots` (headless clients that play, and the load
test), `Hollow.NetGen` (writes the message serializers), `Hollow.GeneratorReport`, `Hollow.CatalogBuilder`.

## 2. Processes and threads

| Process | Threads | What runs on them |
|---|---|---|
| The server (`Hollow.Server`) | the tick thread (30 Hz, above normal priority) | receiving, the zone's step, the modules, replication, sending |
| | the persistence thread (`PersistenceWorker`) | every SQLite transaction (saves every 30 s, logins, notifications) |
| | the console thread | reads stdin, queues commands for the tick |
| | worker tasks | ground tiles and nav grids built ahead (`PhysicsWorld.Prefetch`, `NavGrid`) |
| The game (Godot) | the main thread | rendering, input, `GameClient.Update` every frame (its ticks, prediction, the modules) |
| | worker tasks | the client's own ground tiles, the map's picture |
| | a child process | when you host: the dedicated server, started with `--parent` (`ChildServer`), so its garbage collections never stall your frames |

The physics library (Jolt) is used through one process-wide gate (`JoltRuntime.Gate`): the zone's step holds it for the
movement pass, and every other call into Jolt takes it, because the native binding shares one scratch allocator. That
is the one thing standing between the zone and a second thread (§9).

## 3. One tick of the server

`GameServer.Tick`, 30 times a second. The numbers are the load test with 100 survival bots at the landing site of the
whole world (GROUND.md §5.12).

| Step | What happens | Who | ms |
|---|---|---|---|
| 1. Receive | UDP packets in; each one's first byte picks its handler in the table (`MessageHandlers<PlayerSession>`): inputs and chat to the host, every intent to the module that registered it | `GameServer`, the modules' `Init` | 0.6 |
| 2. Logins and respawns | characters whose ground is ready come in (`FinishLogins`, the modules' `BeforeStep`) | `GameServer`, `SurvivalModule` | 0.0 |
| 3. Commands | the console's queued commands and the load test's inspections run | `AdminCommands`, `ConsoleModule` | |
| 4. Inputs | each player's input for this tick goes into their `Character` (the one received, or the last one again); pilots' controls to the ship | `PlayerSession.InputFor`, `ShipModule.Input` | |
| 5. The zone's step | §4 below: movement, then every system in order; events fire as they happen | `Zone.Step` | 1.7 |
| 6. After the step | the modules' `AfterStep` in list order: ship notices, raid alerts, the world, each player's own state (vitals, containers, crafting, loot), then the answers to this tick's intents | the modules | 0.1 |
| 7. Replicate | 20 times a second: what each player should know (§6) | `GameServer.Replicate`, the modules' `Prepare`/`Snapshot`/`Replicate`/`Finish` | 3.6 |
| 8. Save | every 30 s, or within a second of a death: one transaction with everything that changed | the modules' `Save`, `PersistenceWorker` | 0.0 |
| 9. Send | the transport writes the packets | LiteNetLib | 0.5 |

Everything in the table runs on the tick thread, one after another; nothing in the game's rules is concurrent.

## 4. The zone: a host and its systems

`Zone` (`src/Hollow.Simulation/Zone.cs`, 377 lines) owns what every rule shares and nothing else:

- `Frame`, `Surface` and `Landing`: the frame is the world's own (`ZoneFrame.World`: metres from the planet's centre,
  +Y the north pole), so one zone holds a whole world (the first world's radius is 2,410 m); up at a point is its
  direction from the centre (`Frame.UpAt`). Until S4 brings the real ground at walking scale, the server's zone stands
  on the stand-in world (`StandInWorld`). `Landing` is the ground at the spec's latitude and longitude;
  `GroundAt(east, south)` lays places out from it along the ground.
- `Entities`: the entity store (an ID with a generation, plus components in sparse sets: `Character`, `Named`,
  `PlayerLink`, `StaticLink`, `NetPresence` on the server).
- `Physics`: the Jolt world of this zone (ground tiles within 64 m of anyone, built ahead to 128 m: the world's
  cube-sphere tiles, about 59 m on the first world; props, building bodies, characters' capsules).
- `Nature` and `Natural`: the world's trees, outcrops, pickups and bushes (on the stand-in world, a seeded stream per
  cube-sphere tile), generated where you look, and what players did to them (cut, mined, respawning).
- `History`: a second of everyone's poses, for hits judged as the shooter saw them.
- `Content`: the game's data (`GameContent`, §8). `Climate`: the air temperature. `UnixMilliseconds`: the clock the
  server sets before each step. `Events`: §5.
- its systems, found by type (`zone.Get<T>()`) or by the accessor each one gives the zone (`zone.Guns()`).

`Zone.Step`: the ground under every character is taken in (tiles built on workers earlier), then, under the physics
gate, the broadphase is updated and every living, unseated character moves (`MoveFrames.Step`: the same code the client
predicts with), then the systems that move bodies (`ShipSystem`); then the ground nobody needs is released; then the
other systems, in `GroundSystems`' order. A system steps every tick or once a second (`Every`); the host times each.

**Frames** (IRON_WAKE.md §4). A character moves in the zone's frame or aboard a ship in the ship's frame
(`CharacterState.Frame`). `MoveFrames.Step` finds the frame (`ZoneMoveFrame`, or a ship's `ShipFrame` with its own small
physics, a `ShipSpace`), runs `CharacterMotor.Step` there unchanged, and at the end hands the character over between
frames when it steps onto a parked ship or off a ship's ramp, keeping its pose and velocity in the world. The ship's own
flight never enters a step aboard. What a character does in the world asks where its ship carries it
(`CharacterWorld.WorldOf`, `AttackLine`), and every query of the world also enters the ship spaces it crosses
(`ShipQueries`).

| System | Steps | What it is |
|---|---|---|
| `TargetSystem` | no | names anything in the zone by a `TargetRef` (family, ID, part); the families, capabilities and viewer flags; reach and sight |
| `DamageSystem` | no | the one damage pipeline: a request, a receiver per family (character, animal, nature, deployable, ship, building, ground), the `Destroyed` event |
| `HeldSystem` | no | what an item does when held (melee, bow, gun, placing, throwing, eating): one rule per `HeldUse` |
| `SupportSystem` | no | what rests on or sticks in what (a box on a floor, a charge on a door, an arrow in a bear) |
| `InteractionSystem` | no | the verbs (`interactions.json`): what E offers on a thing, gated and ranked; one `Interact` |
| `PlacementSystem` | no | placing anything: kinds, checks, solvers, the ghost's verdict (the client runs the same) |
| `SpillSystem` | no | dropped items into bags on the ground |
| `BlastSystem` | no | an explosion's reach and line of sight, splash shares, raid tallies |
| `ShipSystem` | yes, under the gate | ships: flight (`ShipMotor`), seats, hatches, fuel, cargo, parking, wrecks |
| `PlayerSystem` | yes | players' lives: spawn, sleep, death into a corpse, respawn at a bag; fall damage |
| `CombatSystem` | yes | held items' actions each tick (swings, bows), arrows in flight, hits on characters, nature and ground |
| `GunSystem` | yes | gun actions queued by tick (fire, reload, unload, attach), bullets with drop and drag, lag compensation |
| `ExplosiveSystem` | yes | thrown and placed charges, bounces, sticking, fuses, duds, detonations |
| `FireSystem` | yes | fires a blast leaves (IW4): where the fuel rests, what each burns every half second (the heat delivery), falling when what holds it goes |
| `WoundedSystem` | yes | down, crawling, helped up, got up, bled out |
| `AnimalSystem` | yes | the animals' world: spawning, AI (`AnimalWorld.Brain`), attacks, harvest; it hears the zone's noises |
| `VitalsSystem` | once a second | calories, water, temperature, bleeding, healing; starving, thirst and cold |
| `NatureSystem` | once a second | cut trees and mined outcrops come back when their time is up |
| `ThingSystem` | yes | static things: deployables in cells, burners and machines; loose things' lifetimes (bags, corpses, duds) |
| `ArrowSystem` | once a second | spent arrows stuck in things and bodies, taken back with E |
| `WorldSystem` | once a second | world props (barrels, crates, recyclers), loot spots, recycling, research, teams, map markers |
| `ActionSystem` | yes | containers and looting, inventory moves, item actions, the loot check; craft queues |
| `BuildingSystem` | yes | the building (`BuildingWorld`: structures, sockets, stability), the hammer, doors, locks, the cupboard; decay and upkeep |
| `HistorySystem` | yes, last | records everyone's pose for the next tick's hits |
| `AutoRespawnSystem` | movement-only zones | G1's respawn, for the physics checks |

A system keeps what it needs of another in a field it fills once (`Zone.Require<T>()`), and calls it as a plain method:
`Players.PlayerOf(entity)`, `Damage.Damage(request)`, `Things.AddStatic(box)`. Nothing reads another system's fields.

**Registries.** Behaviour that varies by kind is a handler found by a number, never a switch: the target families
(11), the capabilities (`capabilities.json`, `CapabilityHandlers`), the verbs (`interactions.json`, `InteractionRules`),
the damage receivers (by family), the held rules (by `HeldUse`), the item describers (by `ItemPart`), the item actions,
the placeable kinds, checks, rotations and solvers. Each is a `Registry<T>` filled at start and sealed: an entry without
a handler, or a handler without an entry, stops the server from starting.

## 5. Events

Something that happened is a record struct in `src/Hollow.Simulation/Events` (`HitConfirmed`, `Hurt`, `ItemsGained`,
`Crafted`, `Died`, `ProjectileLaunched`, `ProjectileEnded`, `WentDown`, `GotUp`, `Detonated`, `Notice`, `Destroyed`, and IW4's
`FireLit` and `FireOut`; the fires light from `Detonated`, which carries the blast). A
system adds one (`Events.Add(new Died(...))`); every subscriber of that type hears it at once, on the tick thread
(`Events.On<Died>(...)`): the server's routes turn it into messages for the players it concerns, the counters count it,
the animals turn a shot into a noise. The lists keep the tick's events until the server clears them after the step; a
test reads them by kind. A new kind of event is one record and its subscribers.

## 6. The server: a host and its modules

`GameServer` (`src/Hollow.Server/GameServer.cs`, 1,093 lines) keeps the connection and the sessions, logins and spawns,
the tick loop, the snapshots of the dynamic entities (characters, animals, ships: §7), chat, saving and shutdown. A new
character arrives on a beach anywhere on the world (`Beaches.Find`), or at the landing site with `--spawn origin`. Each
concern is a `ServerModule` in `Modules/`, listed once in `ServerModules`, reached as `server.Raid()`:

| Module | Intents it handles | What it replicates and keeps |
|---|---|---|
| `ThingModule` | | static things and natural objects by relevance (spawn once, then changes); their save and load |
| `BuildingModule` | hammer | the building by 64 m cubes within a byte budget; frames, pieces, doors, claims; its save |
| `PlacementModule` | place | |
| `ShipModule` | the pilot's keys | ships in the snapshot's ship section; crashes, wrecks; their save |
| `RaidModule` | throw | explosives in range with their arcs and fuses; detonations; raid alerts (`Notifications`, queued for the offline) |
| `FireModule` | | fires in range (IW4): lit or come into range, fallen, ended |
| `WorldModule` | team, team chat, marker | blueprints, teams, invites, teammates' positions, the map, machines |
| `AnimalModule` | | animals as snapshot entities (their kind on spawn, at rest when still) |
| `ArrowModule` | | spent arrows and their poses |
| `DamageModule` | | the `Destroyed` event to everyone who draws the thing |
| `SurvivalModule` | move item, quick move, drop, item action, craft, cancel, interact, close, respawn | each player's own state (containers, vitals, crafting, loot, life), looks, the event routes, the counters, respawns waiting for ground, kits |
| `GunModule` | fire, reload, unload, attach | the gun's state and the shot's answers |
| `ConsoleModule` | console lines | admins' rights, replies, the server's state and the zone's clock for F1 |
| `DevModule` | watch, inspect | the F2 panel (GROUND.md §8.5): the stats every half second to watching admins, a target described, the nav grid and spawn areas, the log |

The host calls the modules through hooks named for what they do, in a fixed order: `Init` (register intents and
subscribe to events), `Restore` (the saved world), `Start`, `Joined`/`Left`, `BeforeStep`, `Input`, `AfterStep`,
`Prepare`, `Describe`/`AtRest`/`Seen`/`Snapshot` (the snapshot path), `Replicate`, `Finish`, `Save`, `Dispose`. What a
module remembers per player (what it already sent them, their view) sits in a typed slot on the session
(`session.State<ThingView>()`), never on the host.

**An intent** (anything a player asks for) is one message with a sequence number. The module's handler spends from the
player's budget (RustMVP's rates), calls the zone (`zone.Guns().QueueGunAction(...)`), and the zone answers with one
`Outcome` (a code and a detail, the words in `texts.json`); the answer goes out after this tick's changes, so the
client sees the change first and the verdict second. The client may have predicted the result; the answer confirms or
corrects it.

## 7. The wire

`src/Hollow.Net/Protocol/Messages.cs` defines every message as a struct with attributes saying how it is packed
(`[Bits(4)]`, `[Angle]` for a 16-bit angle, `[VarInt]`, `[Quantize]`, `[MaxCount]`); `tools/Hollow.NetGen` writes the
readers and writers (`Messages.g.cs`, 76 messages), a schema hash that both sides compare at connect, and the
`INetMessage<T>` each message implements, so `MessageHandlers<TContext>` can route a packet's type byte to the one
handler registered for it. A view travels as a direction in the zone's frame, two 16-bit octahedral numbers
(`Quantize.Octahedral`), and a ship's turn as a smallest-three quaternion (`Quantize.SmallestThree`): unlike angles from
local north, neither breaks at a pole (protocol 21). UDP through LiteNetLib, four kinds of delivery:

| What | Channel | Rate |
|---|---|---|
| Inputs (client to server) | unreliable, sequenced; each packet carries the last four ticks' inputs | every tick (30 Hz) |
| Snapshots (server to client) | sequenced; each entity's state is a delta against the newest state that client acknowledged (a bit per unchanged field), fitted into 1,600 bytes by priority (nearer first); the player's own state and ship ride in the header | 20 Hz |
| Intents, answers, static things, building, own state | reliable, ordered | when something changes |
| Cosmetic events (bullets flying, explosive rounds) | unreliable | as they happen |

Relevance: a grid of cubes per zone; a player hears about what is within 300 m in a straight line (ships 2 km); what
leaves is despawned. Static things are sent once when they become relevant, then only on change; a big base streams in
over a few seconds. Measured with 99 characters in view (the worst case): 105 to 192 kbit/s down, 18 to 24 kbit/s up,
against the budget of 256.

**Prediction.** The client runs its own character every tick with the same `CharacterMotor` on its own copy of the
nearby physics, sends the input, and when a snapshot says "input 412 processed, you are here", rewinds to that state
and replays its newer inputs (`Predictor`); small corrections are eased out. Ships are predicted the same way
(`ShipPredictor`); guns too (`LocalGun`: recoil and spread from the same seed as the server's). Everyone else is drawn
100 ms in the past, between two snapshots. The client steers its clock so inputs arrive two ticks early. Aboard a ship
the prediction runs in the ship's frame through the client's copy of its space (`ClientShipFrame`, kept by the
`ShipModule` for ships within 300 m), and the character is drawn on the ship as drawn; others aboard are interpolated in
their ship's frame and placed on it the same way.

## 8. The client and the game

`GameClient` (`src/Hollow.Client/GameClient.cs`, 859 lines) keeps the connection, the clock, the input, the prediction,
the snapshots, the remote entities and chat; `LocalPlayer` is the player's own state (inventory, belt, wear, crafting,
vitals, the gun) and the intents it sends. Each concern is a `ClientModule` in `Modules/`, listed in `ClientModules`,
reached as `client.Raid().Explosives`: survival (things, nature, projectiles), targets (the families' views, the same
`TargetRef`s the server uses), world, building (the replica of the structures), placement (the ghost, with the server's
rules), interaction (what E would do, from the same rules), raid, fires, ships, animals, arrows, console, damage, loading, dev
(the F2 panel, GROUND.md §8.5). The
hooks: `Init` (its messages), `Welcome` (the zone is known), `Spawned`/`Despawned`, `BeforePredict`, `AfterSnapshot`,
`Sample`, `Render`, `Update`, `Dispose`.

The Godot project (`game/scripts`) draws it: `GroundMode` starts the client (and the child server when hosting),
`GroundView` is the scene (the terrain, `PeopleView`, `ThingsView`, `NatureView`, `BuildView`, `ArrowsView`, `GibsView`,
the first-person view and view model, `FxDirector`, `AudioDirector`), and the interface is `GroundUi` (HUD, inventory,
crafting, map, death screen, radials, console) drawing an `IGroundUi` model that `ClientGroundUi` fills from the client
(and `MockGroundUi` fills for screenshots without a server). Every length goes through `Ui.Px`. The map (G) and the
death screen share one map control of the whole world (`GroundUi.Globe.cs`, docs/WHOLE_WORLD.md §11): its view is a
`GlobeCamera`, a canvas shader draws the six faces' picture through `GlobeProjection`, and the overlay places the grid
(`WorldGrid`), markers and labels with the same projection and reads clicks with its inverse. The world is drawn by
the surface view (`SurfaceView.OpenWorld`, docs/WHOLE_WORLD.md §10): the zone's surface, whole, in the zone's own frame
(origin at the centre), its terrain from `ISurface` (`TileMesh.Build`, `ISurface.Cover`), its plants the zone's natural
objects (`NaturePlants`), grass on the cube faces' cells, the sky of the planet the world stands for; `PlanetGlobals`
tells every shader and the effects where the planet's centre is, so up, heights and falling follow the ball.

**The data.** 23 JSON files in `game/data/ground` (items, recipes, deployables, weapons, building, animals, world,
interactions, placement, capabilities, texts...). `GameContent.Load` runs one reader per file (`Content/Readers`,
`ContentReaders.All`), then each file's checks, then hashes the text; a client with other data is refused at connect.

## 9. Three journeys

**You chop a tree.** Godot reads the mouse and keys into an `InputFrame`; `GameClient.RunTick` packs it into the tick's
`InputCommand` (the view as a direction, attack held, the hatchet's belt slot) and sends it. The server puts it into
your `Character`; `Zone.Step` moves you; `CombatSystem.StepActions` asks `HeldSystem` what a hatchet does when swung
(`StepMelee`), which traces the swing through `DamageSystem.Trace` (every receiver tests its own things;
`NatureReceiver` finds the tree in `Natural`), then `CombatSystem.Gather` works out the yield (`gather.json`, Rust's X
and the hotspot) and gives it to your inventory; the events `HitConfirmed` and `ItemsGained` fire; the server's routes
send you `HitMarker` and `ItemsChanged`; `ThingModule` sends the tree's new state (`NaturalStates`) to everyone near. On
your client `SurvivalModule` updates the tree and `LocalPlayer` your inventory; `NatureView` redraws the tree,
`GroundUi` shows the marker and the gain.

**You place a box.** `PlacementModule` (client) runs `Placer.Plan` on the client's own `PlacementWorld` every frame:
the ghost is red or green by the same rules the server will apply. On click, `PlaceIntent` goes out. The server's
`PlacementModule` spends your budget and calls `PlacementSystem.Place`: `Placer.Settle` (the kind's checks and solvers,
on foundations and floors, not in water, not where a player stands), then `ThingSystem.AddStatic` (a physics body, its
cell, its support link to the floor), and answers `Done`; `ThingModule` sends `ThingSpawn` to everyone within 300 m.
`ThingsView` draws it; your own client had already stopped drawing the ghost.

**You shoot someone.** `LocalGun.Tick` fires on your predicted state (the recoil pattern and spread from a seed the
server shares) and sends `FireIntent` with the tick, a sequence number and the view. The server's `GunModule` queues it
(`GunSystem.QueueGunAction`); at that tick `StepGun` reads the view as a heading and pitch where you stand, as your
client did, and makes the `Bullet`; `StepBullets` flies it two substeps a tick with drop and drag, each step tracing
through `DamageSystem.Trace` with the world rewound to what you saw (`History`, up to 250 ms back); `CharacterReceiver`
takes the hit through the victim's armour (`PlayerInventory.Protection`) and `PlayerSystem.Hurt` applies it (a lethal
small hit knocks down instead, `WoundedSystem`). The events: `HitConfirmed` to you (`HitMarker`), `Hurt` to them
(`DamageTaken`), `ProjectileLaunched` and `ProjectileEnded` to everyone near, unreliable, for the tracer and the impact.
`GunEffects` and `FxDirector` draw them.

## 10. Persistence

SQLite (`src/Hollow.Persistence/GameDatabase.cs`), one worker thread, one transaction per save: accounts and characters
(where they are, their inventory and state), things, natural overrides (what players changed), building, ships, teams,
prop overrides, the zone's meta, notifications. Each module adds its records to the `ZoneSave` (`BuildingCodec`,
`ShipCodec`, `ExplosiveCodec`, `TeamCodec`, `WorldCodec`, the character records); on start the host restores the
character records and each module its own. Deaths commit within a second; the rest every 30 s and at shutdown. A player
who leaves stays in the world asleep (aboard a ship, in its frame: it comes back with the ship).

## 11. Performance and what runs in parallel

Measured on the owner's PC on the whole world (GROUND.md §5.12, S3b): with 100 survival bots at the landing site the
server ticks in 6.6 ms on average, 11.4 at the 99th percentile, none over the 33.3 ms budget; the zone's step is 1.7 ms
of it (movement 1.5, the game's rules 0.1), replication 3.6 (snapshots 1.6, things 0.4, nature 0.06), receiving 0.6,
sending 0.5. The same bots arriving on beaches all round the world tick in 4.5 ms (each has one other in range or none),
with about 3,000 ground tiles held all over the ball. Before S3, on the flat patch: 5.2 ms (GROUND.md §5.11). A client
draws at 86 to 108 fps on an RTX 2070 at 1600×900 (measured before S3).

What runs off the tick thread today: the ground tiles and nav grids built ahead (worker tasks), every database write
(the persistence thread), the console; the hosted server as a whole (its own process). What is serial: the zone's step
(by design: one zone, one thread, the same order every tick, so the client can predict it), and replication, which
is the largest part of the tick and does not have to be serial: each player's packet is written from state nobody
changes during replication, so one thread per player (or per batch) is the obvious next step; a 6-core PC should cut
its 3 ms to under 1. Movement could follow (Jolt's queries are thread-safe) once the binding has a scratch allocator
per thread instead of the gate. The animals' thinking (0.4 ms for 200) is independent per animal and could go the same
way. None of these change the rules' order or results.

## 12. Where to look

| You want to | Look at |
|---|---|
| add a rule of the game | a `ZoneSystem` in `src/Hollow.Simulation/Systems`, listed in `GroundSystems` |
| add something that happens | a record in `src/Hollow.Simulation/Events`, `Events.Add`, subscribers `Events.On<T>` |
| add a message | `Messages.cs`, `dotnet run --project tools/Hollow.NetGen`, one `handlers.On<T>` in the module that owns it on each side |
| add a server or client concern | a module in `src/Hollow.Server/Modules` or `src/Hollow.Client/Modules`, listed in `ServerModules` / `ClientModules` |
| add a data file | a reader in `src/Hollow.Simulation/Content/Readers`, listed in `ContentReaders.All` |
| change how a thing behaves | its capability block in the data and the handler in `Capabilities/`; a verb in `interactions.json` |
| change what the player sees | `game/scripts/UI` (the interface) or `game/scripts/Ground` (the world's views) |
| measure | `dotnet run -c Release --project tools/Hollow.Bots -- loadtest --count 100 --duration 30` (the tick's profile), `--view ground --measure` (frames) |
| check the shape still holds | `dotnet run --project tests/Hollow.Tests -- Architecture` |
