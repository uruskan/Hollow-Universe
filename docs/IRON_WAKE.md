# The Iron Wake: a crewed carrier

Status: **approved by the owner on 15 October 2026, with all fourteen defaults of §2.** The art, sound and effects
(§13: IW-A, IW-S, IW-F) are being built on `main` in their own worktrees. The rules (IW1–IW6) follow on top of S3a
(WHOLE_WORLD.md), which was built on `whole-world` on 15 October 2026.

---

## 1. What the owner asked for

On 14 October 2026 the owner gave the Iron Wake asset (`D:\GameDev\Assets\IronWake_Buildable_R01`) to the game:

> Add this as described to our game. This ship really is going to be the benchmark right now. Make the cannons fire
> rocket, HV rocket and incendiary rocket for now. Loading should be done visually, animated, fully realistic. I want
> this ship and every mechanic and gameplay element about it done at the highest quality possible, at AAA studio
> level: no shortcuts, nothing low quality.

"As described" means the asset's `README.md` and `Documentation/SHIP_FEEL.md`, written with the owner while the asset
was made:
- **The decks:** an unfurnished 58 m carrier whose decks players furnish with crafted boxes, furnaces and
  workbenches, as on Rust's tugboats and player-built boats.
- **The cannons:** crewed by hand. A player walks to a station, sits, aims with the mouse and fires.
- **The feel:** a working vessel's weight: its engines spool, its hull strains, its guns kick and settle.

The owner's rule of 13 October applies in full: one shared system per concern, behaviour from capabilities declared in
data, no chains of special cases (GROUND_SYSTEMS.md §1). The Iron Wake therefore adds no ship-specific code path. It
is the first ship to need things the Tin Finch never did:
- a deck to walk on while it flies;
- more than one seat;
- doors;
- guns;
- things built aboard.

Each of these becomes a shared system the next ship reuses.

## 2. Decisions (defaults the owner may change)

| # | Question | Default | The other way |
|---|---|---|---|
| IW1 | Where do people aboard live? | In the ship's own frame, inside the zone (§4). Outside and inside see, hear and shoot each other through the open ramp. | A separate "interior" zone (ARCHITECTURE.md §10.1). That needs transfers, which the owner deferred, and it would cut the inside off from the world. |
| IW2 | Which way is down aboard? | The deck's down, with the ground's gravity. The ship's turns and braking never push people about (deck gravity). | The planet's real down. People would slide whenever the ship tilts. |
| IW3 | How does the Iron Wake fly? | Heavy and stately (§5.4). Flight assist holds it level, within 20° of pitch and 12° of roll. With assist off it may tumble. | Free six-axis flight like the Tin Finch's. |
| IW4 | Can a seated crew member be shot? | Gunners sit in open stations and can be shot; pilots on the glazed bridge are sheltered like the Tin Finch's pilot. | Everyone seated is sheltered, as today. |
| IW5 | The gunner's controls | E sits. Mouse aims, held in the world as the ship turns. Left mouse fires; right mouse zooms. R tapped reloads with the chosen rocket; R held picks the rocket. Tab opens the magazine. The seat is left with whatever key wave 1's seats package settles (Space proposed). | — |
| IW6 | Loading | Automatic, from the station's magazine, shown as the full mechanism (§6.6). The breech opens and the ready rack indexes. A tray swings the rocket to the breech, a rammer drives it home, and the breech closes. One timing table in the data drives both the rules and the animation. There is no loading minigame (SHIP_FEEL.md). | A second crew member loads by hand. |
| IW7 | Ammunition | The same `rocket`, `rocket-hv` and new `rocket-incendiary` items the shoulder launcher fires. The batteries launch them faster and further (§6.4); the blast is each item's own. | Separate ship ammunition. |
| IW8 | The two kinds of gun | The siege batteries (twin, bow and dorsal) alternate barrels: a heavy shot about every 2.3 s, long range. The defense mounts (triple, four on the sides) ripple three rockets, then swap a cassette: about every 3 s, short range, fast traverse. | — |
| IW9 | Incendiary rocket and fire | Rust's recipe. A small blast and a 3.5 m fire that burns 18 s. It burns people, animals, wooden deployables and twig or wood building pieces; stone and better don't burn. Fire doesn't spread. | Spreading fire (later, if wanted). |
| IW10 | Building aboard | Deployables only, inside the asset's build zones, with entrances, stairs and routes to the stations kept clear. The owner authorises builders at the helm; the ship is its own tool cupboard. Up to 80 deployables. A bed aboard is a spawn point. Everything aboard breaks with the ship. | — |
| IW11 | Durability and raiding | Hull 18,000. Each battery 1,500: disabled at 0, repaired with the hammer. The ramp and airlock lock against strangers and break open like armoured doors (1,000 each). A wreck leaves loot. | — |
| IW12 | How a player gets one | Open (§15.1). Until decided: admin `ship <name> iron-wake` and the F1 panel's spawn. | — |
| IW13 | Sound | Built from the CC0 libraries already on this PC and from synthesis (§10). No voice lines yet. | Add the Sonniss GDC bundles (royalty-free for games) for a richer mix; downloading them needs the owner's word. |
| IW14 | Order of work | The art, sound and effects start now on `main`. The rules (§4–§8) are built on top of S3a once it is green, because both rewrite characters, ships, the network and saves. | Build the rules on `main` now and merge them into `whole-world` by hand. |

## 3. The asset

**As delivered on 14 October 2026** (`IronWake_Buildable_R01`, made with ChatGPT from the owner's Iron Wake; original
geometry and textures, Poly Haven CC0 materials; no other game's files):

| | |
|---|---|
| Size | 58.1 × 21.0 × 17.6 m (native +X forward, +Z up, metres) |
| Detail | 622,060 triangles in 327 draw calls at LOD0; 331,740 / 63,360 / 51,736 at LOD1–3, still 291–321 draw calls each |
| Spaces | Deck A, the open construction deck: 30 × 10.8 m, 3.8 m headroom. A 7 × 8.2 m fabrication bay inside it, 7.5 m tall. The upper mezzanine: 14 × 8.9 m. The bridge with two flight seats. Weather decks and walkways to the stations |
| Moving parts | The cargo ramp (two leaves, `CargoRamp_Open`/`Close`), the side airlock door, 6 turrets (yaw and pitch nodes), 16 barrels each with its own recoil node and `*_Fire_*` clip (the curves in `weapon_motion.json`) |
| Stations | 6 gunner stations: seat, eye, hand-grip, entry, exit and interaction sockets; occupied volumes and access routes in `gunner_station_layout.json`, validated at the parked yaw |
| Sockets | 91, now with orientation: exhausts, lift nozzles, pilot and copilot seats and eyes, ramp hinge, boarding exit, cargo entry, a third-person camera, the centre-of-mass hint |
| Collision | 358 convex pieces (`03_CONVEX_COLLISION`; the manifest lists 475 colliders), grouped by part so the ramp's, the door's and each turret's move with them |
| Layout data | `asset_manifest.json`: rooms, 4 build zones with their exclusions, the ramp's hinges and angles, the stations, the routes |

**Gaps in the asset:**
- **Materials.** The README says the 4K Poly Haven materials are active, but the exported GLB and glTF files (21:11–21:15)
  and the `.blend` (21:20) are older than them (21:48–21:56) and still use the earlier 1,254 px generated textures.
  The import (§9.1) binds the new derivatives itself. A re-export by the asset's author would make the asset's own
  files agree.
- **Uncrewed weapons.** The 12-cell launch bank on the roof and the keel lance under the bow have no stations. They stay
  fittings (§15.4).

**Made here, because the asset has none of it:**
- the loading mechanisms (breeches, loader arms and trays, rammers, ready racks and the defense mounts' cassettes;
  §9.5);
- the incendiary rocket's model;
- seated poses for pilots and gunners (§9.6);
- the interior's lights and reflection probes (§9.4);
- every sound (§10);
- the effects (§11).

## 4. Walking aboard: ship spaces

### 4.1 What exists

From the surveys of 14 October 2026:
- **Characters.** They move with Jolt's `CharacterVirtual` (`CharacterBody`, `CharacterMotor.Step`) in the zone's
  frame. Every physics body is static and the world is never stepped. Nothing reads a ground velocity: standing on
  something that moves is not supported.
- **The one seat.** A ship carries exactly one seated pilot by setting the character's position to the seat each tick
  (`ShipSystem.CarryPilot`, `Predictor.Seat`).
- **Parked hulls.** A parked hull is a static body. It is removed the moment the ship wakes (a hatch, a refuel, a hit),
  so people standing on it lose it.
- **Deployables** can be supported only by building pieces.
- **What already works.** Support links follow moving supports: arrows stuck in a hull already ride it.

### 4.2 The rule

**A character is either in the zone's frame or aboard a ship, in the ship's frame.** Aboard, its position, velocity
and view are relative to the ship. It moves by the same `CharacterMotor.Step` against the ship's own collision, its
*space*, with the ship's up as up. The ship's flight never moves an aboard character relative to its deck. So:
- nobody jitters on a turning deck;
- a passenger's prediction stays exact while someone else flies;
- precision stays fine, because positions are within 60 m of the ship's origin.

The same holds for anything else that moves aboard (§4.10). Seats are places in a ship's frame (§5.2), so the Tin
Finch's pilot becomes an aboard character at seat 0, and `CarryPilot` goes.

### 4.3 A ship's space

A ship whose `ships.json` entry has an `interior` block gets a space when it is made: a small Jolt physics system of its
own, separate from the zone's, with its collision in the ship's frame.
- **The hull.** The hull's static pieces form one compound body, built once per kind and shared.
- **Moving parts.** The ramp's leaves, the airlock door and each turret's platform are one body each, re-posed when they
  move, as doors are (`BuildingPhysics.Move`).
- **Deployables aboard.** Each is a box, as solid deployables are in the client's prediction world.
- **Characters** are not in it; they pass through each other as everywhere (GROUND.md §11 7).

The collision comes from the import (§9.1), not from Godot: `game/data/ground/ships/iron-wake.json` holds the convex
pieces by part, the rooms, the boarding volume, the build zones and exclusions, and the sockets. It is hashed with the
rest of the content, so server and client agree. A space has a few hundred bodies: tens of kilobytes, made in a
millisecond from the cached shapes. The server keeps one per ship with an interior. A client keeps one for each such
ship within 300 m, for its prediction and its aim.

`PhysicsWorld` stays the zone's. Its surface, frame and 148,544-body table don't fit a ship. `ShipSpace` is the small
class beside it. Its queries (`CastRay`, `CastShape`, `CollideShape`) are what `CharacterBody`, the aim and the shots
already ask of the zone. `CharacterBody` takes the space it is bound to, and each space has its own body for the
characters stepping in it. `MoveContext` gets its up, gravity and water from a frame object (`IMoveFrame`) instead of
`ZoneFrame`:
- **the zone's frame:** up from the planet, sea from the surface;
- **a ship's frame:** +Y up, the zone's character gravity, no sea.

### 4.4 Deck gravity (IW2)

Aboard, "down" is the ship's −Y, with the strength characters have on the ground (the planet's gravity × 1.8, as
`CharacterMotor` uses). The ship's acceleration and turning are not felt; the decks have their own gravity. People walk,
jump and fall aboard exactly as on the ground, at whatever attitude the ship holds. What the crew feels of the ship's
motion is cosmetic (§9.7): a little camera sway when it banks hard or stops short, which the reduced-motion option
turns off, and the hull's sound. Flight assist keeps the Iron Wake within 20° of level (IW3).

### 4.5 Boarding and leaving on foot

Transfers happen at the end of a character's step and keep its pose and velocity in the world exactly. Its velocity
gains or loses the ship's velocity at that point (centre-of-mass velocity + ω × r).
- **On.** A character in the zone's frame moves aboard when the body under its feet is the ship's (its parked body,
  §4.6), or when its feet are inside the ship's *boarding volume*: the rooms, the ramp and the weather decks, from the
  ship's data.
- **Off.** An aboard character returns to the zone's frame when nothing of the ship is under its feet and its feet are
  more than 0.3 m outside the boarding volume. The margin keeps someone on the ramp's edge from flickering between
  frames.
- **In flight.** Stepping off the open ramp of a hovering ship is a fall from that height, with the ship's velocity.
  Jumping onto a flying ship's deck is not possible, because a flying ship has no body in the zone (§4.6).

### 4.6 Parked and flying

While the Iron Wake is parked (asleep, nobody at the helm), its whole collision is a static body in the zone's physics,
as the Tin Finch's hull is today. People outside walk round its legs and up its ramp, and both frames agree, so the
hand-over on the ramp is seamless. When it wakes, the zone's body goes and the space carries everyone aboard. A
waking cause such as a refuel or a hit no longer costs anyone their footing, because people aboard never stood on the
zone's body.

### 4.7 Moving parts with people on them

- **The ramp and doors** refuse to move into a character, as building doors do. "RAMP BLOCKED" is the answer to whoever
  worked them.
- **A turret's platform** may turn into someone standing next to it; the character is pushed out on its next step. The
  seated gunner turns with it.

### 4.8 Prediction and drawing

- **Prediction.** The local player predicts its character in its frame. Aboard, the ship's motion doesn't enter the
  prediction at all. The camera is the ship as drawn × the character's predicted eye.
- **The pilot** predicts the ship too, as today.
- **Everyone else aboard** draws the ship interpolated 100 ms behind, like any remote ship, and places their own
  predicted character on it. They see themselves steady on the deck, and the deck moves under the sky.
- **Others aboard** are interpolated in the ship's frame and placed on the ship as drawn. A lerp in the zone's frame
  would cut the corner of a turning ship's arc (survey: `RemoteEntity` and `RemoteShip` interpolate separately).

### 4.9 Through the frame: aim, reach, shots and blasts

Every query in the zone's frame also enters each ship space its segment or sphere crosses (bounding sphere first). The
ray is turned into the ship's frame and tested against the space, the characters aboard and the things aboard. This
covers four queries:
- the aim (`Targeting.Aim`, its backdrops);
- reach and sight (`Zone.Reach`);
- the bullets' and arrows' traces (`DamageSystem.Trace`);
- a blast's line of sight (`BlastSystem`).

So:
- A shot from outside meets the hull's outside. Through the open ramp it meets whoever stands inside.
- A rocket that bursts on the deck hurts what is on the deck and in its sight there.
- One that bursts against the hull's outside hurts nobody inside.
- Someone aboard aims at a box on the deck, a gunner's station or a player on the shore with the same query.

The ship receiver stops treating the hull as six boxes. A shot meets the space's actual pieces, and the ship's own
shots ignore its own hull (a ship can hit itself today).

The pose history (`HistorySystem`) records aboard characters in their ship's frame and every ship's pose for the last
second. A late shot is then judged where the shooter saw both the ship and the people on it: someone aboard shooting
out, someone outside shooting in, or two people on the same deck. Projectiles keep flying in the zone's frame, with the
shooter's ship velocity added at launch (today nothing adds the shooter's velocity).

### 4.10 Other things aboard

- **Charges** stuck to the ship's surfaces already ride it: their support link names the ship.
- **Thrown grenades and charges** thrown aboard fly and bounce in the ship's frame, under deck gravity, until they leave
  the boarding volume.
- **Bags, bodies and dropped items** that come to rest aboard are linked to the ship and ride it.
- **Spent arrows** stick to the ship's parts by the same link, as they do today.
- **Fires** (§7) on a deck are linked to it.
- **Bullets, arrows and rockets in flight** stay in the zone's frame. They are fast, and leave the ship within a few
  ticks.

### 4.11 Network and saves

- **Input.** `InputCommand` carries the view in the frame the client believed its character was in, with that frame's
  number. On a transfer the server turns a view given in the other frame through the ship's pose.
- **Character state.** `CharacterNetState` carries the frame (none, or the ship's net number) and, aboard, the
  position and velocity in the ship's frame on the 1/256 m grid.
- **Relevance.** Aboard characters are relevant when their ship is.
- **Saves.** A character logged off aboard sleeps aboard, in the ship's frame, and comes back with the ship. Things
  linked to a ship are saved with their pose in its frame (`WorldCodec` keeps only piece supports today).

### 4.12 What this changes in the code

| Where | Change |
|---|---|
| `Hollow.Simulation/Physics` | `ShipSpace` (the small system, its queries and its bodies); `CharacterBody` bound to a space |
| `Movement/` | `IMoveFrame` (zone or ship: up, gravity, water, space); `CharacterState.Frame`; the transfer step |
| `Systems/ShipSystem` | Spaces made and dropped with ships; parked bodies of the whole collision; moving parts re-posed |
| `Systems/HistorySystem` | Ship poses; aboard characters in their frame; rewinds through both |
| `Systems/DamageSystem`, `BlastSystem`, `TargetSystem`, `Targeting` | Queries enter crossed ship spaces |
| `Hollow.Client` | Ship spaces near the player; the predictor in its frame; aboard characters interpolated in the ship's frame |
| `Hollow.Net` | The frame in the input and in `CharacterNetState` |
| `Hollow.Persistence` | Sleepers and things aboard in the ship's frame |

## 5. Ships with many seats, parts and doors

### 5.1 `ships.json` grows

Today one ship kind with one seat and four fixed parts is wired through every layer (survey: `ShipParts`, `Ship.Pilot`,
`ShipSeat`, `OwnShipState`, `ShipIntent`, `ShipCodec`, `Predictor.Seat`, `ShipView`). Spawning always makes
`Ships[0]`, so `spawn <ship>` names one kind and makes another. The Iron Wake's entry adds these blocks, each read by
`ShipsReader` with its check and each handled by one system. The Tin Finch keeps its current numbers, expressed in the
same blocks.

| Block | What | Used by |
|---|---|---|
| `model` | The catalogue key and the generated data file (§9.1) | The views; the space |
| `seats` | Seat id, role (`helm`, `passenger`, `station:<id>`), the socket it sits at (a turret's seat rides the turret), eye, exits, `exposed` (IW4), who may sit (`authorised`) | §5.2 |
| `parts` | Named targets with their aim shapes and capabilities, replacing the fixed hull, hatch, fuel cap and cargo; their order is their wire number (a byte) | Interaction, damage |
| `doors` | The ramp and the airlock: part, motion (the clip, or hinge and angles), seconds, when they may move, health | §5.3 |
| `interior` | The space's data file, the boarding volume, deck gravity | §4 |
| `stations` | The batteries (§6) | §6 |
| `build` | Zones, exclusions, the deployable limit | §8 |
| `assist` | `level`: the most pitch and roll flight assist allows (IW3) | `ShipMotor` |
| `nozzles` | Sockets by name and plume sizes (today the sizes are fixed per drive in `ShipView`) | The view |
| `lights` | Interior and exterior lamps by socket, with the power state they need | The view |

The sockets come from the generated file, not hand-copied numbers. The Tin Finch's seat, hatch and exits were copied
by hand into `ships.json`.

### 5.2 Seats

`Ship.Pilot` becomes one occupant per seat. `ShipSeat` gets the seat's index.
- **Sitting** is an interaction verb, `sit`, on the seat's part. Its gates: free, within reach, authorised for helm and
  station seats, the ship not locked against the sitter. The old `board` verb is `sit` on the Tin Finch's seat.
- **The seated character is aboard at its seat.** Its frame is the ship; its pose is the seat socket through whatever
  carries it: a turret's yaw for a gunner.
- **What a seat lets its occupant do** comes from its role:
  - `helm`: the flight controls (the pilot);
  - `passenger`: nothing;
  - `station:<id>`: that station's controls.

  The copilot's seat is a passenger seat that also works the doors and the lights.
- **Exposure.** An `exposed` seat keeps its occupant in the hit history at the seat's pose and in blasts' reach.
  Gunners can be shot (IW4); pilots on the bridge cannot, as today.
- **Leaving** puts the character at the first clear exit in the ship's frame, tested in the ship's space (a clear
  capsule on a floor). A gunner stands up onto the walkway beside the station, in flight too. A ship without an
  interior (the Tin Finch) keeps its exits on the ground and its rule that it must be still.
- **When nothing is clear**, the occupant stays seated and is told "EXIT BLOCKED".
- **Death and disconnect** free the seat. A dead gunner's body slumps from the seat onto the deck, linked to the ship.
- **Logging off** keeps the seat: the character sleeps in it.
- **Today's exemptions.** Seated characters are exempt from the hit history, blasts, wounding, interactions and guns.
  These become the seat role's rules: a gunner may use the station's verbs and its gun, and anyone seated may use the
  verbs marked `seated` in `interactions.json`.

### 5.3 Doors: the ramp and the airlock

A ship's door is a part with a progress (0 shut, 1 open), driven by the ship's rules and drawn by sampling the model's
clip at that progress, as the Tin Finch's hatch is today.

| | Time | When it may move | Who | Health |
|---|---|---|---|---|
| Cargo ramp | 6.0 s | Ship still, or slower than 5 m/s within 30 m of the ground | Anyone, unless locked; from the helm or the copilot's seat; from a switch at the ramp's top | 1,000 (armoured) |
| Airlock door | 1.5 s | Any time | As the ramp | 1,000 |

- A moving door refuses to move into a character (§4.7).
- **Locked doors** stay shut to anyone the owner hasn't authorised.
- **Broken doors** (a raid) jam open until repaired with the hammer (IW11).
- **The ramp's motion has phases** with their own sounds: release, actuator travel, contact and lock (§10).
- The ramp leaf is its own part, moving with the main leaf's hinge from the manifest.

### 5.4 The Iron Wake's numbers (proposals, tuned in play)

| | |
|---|---|
| Mass | 420 t. Centre of mass at the asset's hint, 1 m aft of the origin and 5.5 m up |
| Thrust | Main 2.1 MN (two main drives and two auxiliaries), retro 1.3 MN, lateral 0.85 MN. Lift 7.6 MN on the four lift engines (2.0 × its weight at 9.0 m/s², as the Tin Finch). Down 0.8 MN |
| Flight assist's speeds | Forward 110 m/s, reverse 25, lateral 18, up 15, down 12. Boost ×1.5 thrust to 150 m/s for 6 s, then 12 s to recharge |
| Turn rates, acceleration | Pitch 9°/s and 6°/s². Yaw 8°/s and 5°/s². Roll 7°/s and 5°/s². Flight assist holds pitch within 20° and roll within 12° of level (IW3) |
| Drag areas | Forward 190 m², side 950, top 1,150 |
| Landing legs | Four pads at the gear's feet. 6.3 MN/m, 1.1 MN·s/m, 0.35 m of travel, friction 0.6. Lands on up to 12° |
| Crash | Above 3 m/s, (speed − 3)² × 60 of the hull: 10 m/s costs 2,940 |
| Hull, fuel | 18,000 (IW11); 6,000 lamp oil, burning 0.5 a second idling and up to 5 at full thrust |
| Ceiling | The world's leaving height (WHOLE_WORLD.md §6.5). In orbit the crew stays aboard and may walk about; the ship waits at its orbit marker |

Five motor limits break at this size and become data or are fixed:
- the water band (±1.2 m round the centre of mass);
- buoyancy from the hull boxes' volume, which would float a ship whose hull is solid boxes;
- at most 8 skid loads and 4 overlap contacts;
- the reader's caps;
- the hull for the world's sweeps.

For the sweeps, ten boxes fitted to the model at import (body, bow, superstructure, bridge, two drive pods, two
outriggers, two batteries) sweep the world as the Tin Finch's six do, with the legs' four pads as skids.

### 5.5 Damage, the wreck and raiding a ship

- **The hull.** Hits on the hull go to the ship receiver as today, with the shares by delivery (explosives 1).
- **Batteries.** A hit on a battery's parts damages the battery. At 0 it is disabled: it can't fire or turn, it smokes,
  and its gunner is put out. The hammer repairs it at the cost of its metal.
- **Doors** take damage as armoured doors do.
- **A wrecked ship:**
  - everyone aboard takes its explosion by distance and is put into the zone's frame with the ship's velocity;
  - everything built aboard breaks and spills;
  - the wreck holds the stations' magazines, half the fuel and its salvage.

  Gibs come later, with their art.

### 5.6 Getting one

Open (§15.1). Until the owner decides, an admin spawns it (`ship <name> iron-wake`, the F1 panel's spawn). This also
fixes the spawn that always made `Ships[0]`.

## 6. Crewed batteries

### 6.1 A station

A station is a seat, a mount, a battery and a magazine, named in the ship's `stations` block:
- **the seat**, with role `station:<id>`, on the turret's yaw;
- **the mount:** the yaw and pitch nodes, with their limits and motors;
- **the battery:** the barrels with their muzzles and recoil nodes, the firing order, the loader;
- **the magazine:** a container that takes only the battery's ammunition.

Two kinds of battery are defined in data, `siege` and `defense`. Each station names one; nothing tests which. A
station has health (§5.5).

### 6.2 The mount: aim held in the world

- **What the gunner asks for** is a direction in the world: the input's view, which S3 makes a direction vector. Each
  tick the server turns it into the mount's yaw and pitch through the ship's pose, clamps them to the mount's arcs, and
  slews towards them at the motor's rates. It accelerates and brakes so it neither overshoots nor creeps.
- **Stabilisation.** A turning ship doesn't drag the aim away, within the motor's rates (SHIP_FEEL.md). It is not
  target tracking: the gunner still leads a moving target by hand.
- **Saturation.** When the ship turns faster than the motor can follow, or the aim is beyond the arcs, the actual barrel
  direction lags or stops. The sight shows both: where the gunner points and where the barrels are (§6.8).
- **Alignment.** A barrel may fire when it points within the battery's tolerance of the asked direction.

### 6.3 Barrels and shots

- **Firing order.**
  - The siege battery fires one barrel per press, the next loaded one in turn.
  - The defense mount ripples while the trigger is held: its three barrels a quarter second apart, each as soon as it is
    loaded.
  - A release, a loss of focus, leaving the seat or losing the seat stops the fire at once. No refused press is kept to
    fire later.
- **The shot.** It leaves from the barrel's muzzle: the ship's pose at the shot's tick × mount × yaw × pitch × barrel ×
  muzzle socket. It carries the barrel's direction × the launch speed (§6.4) plus the ship's velocity at the muzzle.
  It is the projectile system's own rocket, so blasts, raid hits, alerts and replication are what they are for the
  shoulder launcher. It also names its station and barrel, so the shooter is the gunner.
- **Recoil.** The asset's curves (`weapon_motion.json`: siege 0.48 m over 0.9 s, defense 0.19 m over 0.32 s) are copied
  into the battery's data and drive the barrel's recoil node. They are presentation. The rules know only that a barrel
  is *recovering* for that long, and the loader waits for it.

### 6.4 Ammunition and launch profiles (IW7)

A battery accepts the rocket items listed in its data. For each it has a launch profile:
- the speed the gun gives it at the muzzle;
- the motor that lights a few metres out, with its acceleration and burn time;
- gravity during and after the burn;
- drag;
- lifetime.

The shoulder launcher's numbers (18 and 40 m/s) don't change. The projectile reader's 5 s lifetime cap and the client's
8 s cut-off rise to 15 s.

| Proposal | Muzzle | Motor | Top speed | Lifetime | Reach on the flat (approx.) |
|---|---|---|---|---|---|
| Siege: rocket, incendiary | 70 m/s | +60 m/s² for 1.2 s | 142 m/s | 12 s | 900 m |
| Siege: HV rocket | 120 m/s | +100 m/s² for 1.4 s | 260 m/s | 12 s | 1,350 m |
| Defense: rocket, incendiary | 35 m/s | +40 m/s² for 1.0 s | 75 m/s | 7 s | 400 m |
| Defense: HV rocket | 60 m/s | +60 m/s² for 1.2 s | 132 m/s | 7 s | 650 m |

During the burn gravity is 0.15 × the planet's (the launcher's rule); after it, the whole of it. Drag is 0.0015 a metre.
On S3's 2.4 km worlds the horizon is 380 m away from 30 m up and 690 m from 100 m, so the siege battery's reach matters
from height. HV still flies flatter and faster, as the owner's playtest direction asks; its bear-killing damage is
wave 1's fix (KNOWN_PROBLEMS.md, finding 26).

### 6.5 The magazine and the ready racks

- **The magazine** is a container at the station: 12 slots for a siege battery (36 rockets), 6 for a defense mount
  (18).
- **Filling it.** It is opened on foot at the station's interaction point (E, "OPEN MAGAZINE") or from the seat (Tab),
  by the ship's authorised players and the gunner. Rockets are put in as into a box.
- **The ready racks** are the magazine made visible. Each siege barrel's rack shows the next three rockets of the chosen
  kind; a defense mount shows its cassette and the one waiting.
- **The chosen kind** is the station's, picked by holding R (the ammo radial, P3's, with the kinds the magazine holds
  and how many). Loads take it. A tap of R cycles: rounds of another kind already chambered are drawn back into the
  magazine, by the same mechanism in reverse, and the chosen kind is loaded.

### 6.6 Loading, step by step (IW6)

Each battery kind has a loader: an ordered list of phases with durations, in `ships.json`. The rules step through them
per barrel (siege) or per mount (defense). A barrel counts as loaded at the end of its last phase. The view samples
each phase's progress to pose the mechanism's parts (§9.5), so what is seen is what the rules are doing. The
mechanism's nodes and their motions (which node, which axis, how far, with what easing) are named in the same table,
so the art and the rules can't drift apart.

**Siege battery, per barrel** (each barrel has its own loader; the battery alternates):

| Phase | s | What is seen and heard |
|---|---|---|
| recover | 0.90 | The barrel recoils and runs back to battery (the asset's curve) |
| breech open | 0.45 | The sliding breech block drops; a puff of smoke from the chamber |
| fetch | 0.80 | The loader arm, pivoting on the trunnion axis beside the barrel, swings the empty tray down to the ready rack; the rack's drum indexes one place |
| transfer | 0.40 | The rack's pusher slides the next rocket onto the tray |
| present | 0.80 | The arm swings up to the barrel's line at its current elevation (it shares the trunnion, so any elevation works) |
| ram | 0.50 | The chain rammer extends and drives the rocket into the chamber; the rocket is seen to go in |
| retract | 0.40 | The rammer folds back; the tray swings clear |
| breech close | 0.35 | The block rises and locks |

That is 4.6 s from shot to loaded, per barrel; alternating, the battery fires about every 2.3 s sustained. With an
empty rack the loader waits after "breech open": the breech stays open and the sight says EMPTY.

**Defense mount, per mount** (a three-round cassette):

| Phase | s | What is seen and heard |
|---|---|---|
| recover | 0.32 | The third barrel's recoil |
| release | 0.30 | The spent cassette's latches open |
| eject | 0.45 | It slides back and drops into the chute under the mount |
| raise | 0.70 | A filled cassette rises from the hoist behind the breech face |
| seat | 0.45 | It slides forward; the three rockets enter their tubes |
| lock | 0.30 | The latches close |

That is 2.5 s after the third shot. With the ripple, a defense mount fires three rockets about every 3 s. A cassette
holds what the magazine has of the chosen kind, up to three.

### 6.7 Why a shot is refused

The sight says why, in this order, and the press does nothing:

| Line | When |
|---|---|
| DISABLED | The station's health is 0 |
| EMPTY | No barrel is loaded and the magazine has none of the chosen kind |
| LOADING | No barrel is loaded yet; the loader is working (its progress shows) |
| LIMIT | The asked direction is beyond the mount's arcs |
| TRAVERSING | No loaded barrel is within the alignment tolerance yet |
| HULL BLOCKED | The barrel's line, from the breech to 6 m past the muzzle, meets the ship's own space |

### 6.8 Controls, camera and the sight (IW5)

- **Sitting.** E at the station's interaction point sits. The prompt reads "E  MAN FORWARD BATTERY", or why not:
  occupied, not authorised, disabled. Hold E for the radial: sit, open magazine.
- **Seated:**
  - the mouse aims;
  - left mouse fires;
  - right mouse zooms (×3 siege, ×2 defense, with its own sensitivity);
  - R reloads or opens the ammo radial;
  - Tab opens the magazine;
  - the seat is left with wave 1's key.

  Keys are rebindable. Free look (Alt) turns the head without moving the aim.
- **The camera** is at the station's camera socket. It turns with the yaw and pitches with the view; the gunner's own
  hands rest on the grips (§9.6). Recoil and the ship's motion shake the camera a little (a cosmetic layer, off with
  reduced motion). It never moves the aim and never drifts the cursor.
- **The sight** is in the ground interface's style:
  - the cross where the gunner points;
  - a ring where the barrels point (the two separate while the mount slews or is at a limit);
  - range marks for the chosen rocket, from its launch profile, in 100 m steps;
  - each barrel's state: loaded (the rocket's icon), loading (its progress) or empty;
  - the chosen kind and how many the magazine holds of each;
  - the refusal line (§6.7);
  - the arcs as a small dial when the aim is near or past them.

  The ship's heading and speed are small at the side.

### 6.9 Prediction without doubled shots

- **The aim.** The gunner's client predicts its mount from its own input and the ship as it draws it, and reconciles
  with the server's yaw and pitch. Corrections are smoothed, never snapped, because the cross is the input's and never
  moves.
- **A predicted shot.** A press the client believes can fire plays at once: the recoil, the flash, the sound and a
  predicted rocket, tagged with a shot number.
- **Confirmation.** The server's shot (ship, station, barrel, number, tick, kind) replaces the predicted rocket with the
  real one and plays nothing again.
- **A refused shot** stops the predicted rocket and shows the reason. Mispredictions are rare, because the client runs
  the same rules on the same loader timeline.
- **Others** see the shot from the server's event and the rocket from the projectile messages, deduplicated by the
  station, barrel and number (the asset's contract).

### 6.10 The numbers (proposals)

| | Siege battery (2) | Defense mount (4) |
|---|---|---|
| Barrels | 2, alternating, a press each | 3, rippled while held, 0.25 s apart |
| Yaw | arcs from the hull's clearance at import; 24°/s, 40°/s² | ±100° outboard; 80°/s, 240°/s² |
| Pitch | −8° to +40°; 16°/s, 30°/s² | −15° to +65°; 60°/s, 180°/s² |
| Alignment tolerance | 0.5° | 1.5° |
| Loader | 4.6 s a barrel | 2.5 s a cassette |
| Magazine | 12 slots (36) | 6 slots (18) |
| Health | 1,500 | 1,500 |
| Zoom | ×3 | ×2 |

## 7. The incendiary rocket and fire

**The item.**
- `rocket-incendiary`, "incendiary rocket", stack 3.
- Recipe (Rust's): workbench 2, 2 metal pipes, 150 gunpowder and 75 lamp oil (our low-grade fuel). Researchable like the
  rocket.
- It fires from the launcher (calibre `rocket`) and from both batteries.
- Its model: the rocket's body with a red band and a blunt, frangible nose cap.

**Its burst** (`explosives.json` blast `rocket-incendiary`, proposals) is a small blast:
- radius 2.5 m;
- 60 to flesh at the centre, 60 to deployables;
- building pieces: twig 500, wood 20, stone 5, metal 2, armoured 1;
- doors: wooden 30, sheet metal 5, armoured 2.

The fire then does the work.

**The fire** (a new `FireSystem`, IW9):

| | Proposal |
|---|---|
| Size and time | A patch of 3.5 m radius on what the rocket hit, burning 18 s |
| People | 8 a second at the centre, nothing at the edge, less their clothing's protection, plus 6 of `Burning` a second while in it (the existing vital: 2 a second until it runs out or they swim) |
| Animals | 8 a second at the centre |
| Deployables | 5 a second to those whose data says they burn: wooden boxes, doors and benches. Metal ones don't |
| Building | 6 a second through the building rules' heat channel, which `building.json` already sets to 1 for twig and wood and 0 for stone and up. A wooden wall (200) under one fire loses 108; two fires break it |
| Water | A fire on water or in the sea goes out at once |
| Count | At most 64 fires in a zone; the oldest goes first |

- A fire is a world thing with a support link to what it burns on, so a fire on the Iron Wake's deck rides with it.
- It damages through the damage pipeline: a new delivery, `heat`, on the existing `Heat` channel, which nothing
  delivers today.
- Every half second it hurts what is within its radius and in its sight, by distance. It doesn't spread.
- **The network:** `FireStarted` and `FireEnded` reliably to everyone within 300 m. Fires aren't saved (the longest
  lasts 18 s).
- **Drawn as** `fire.burning`, scaled to the radius, with a flickering light, crackle and roar by size, scorch decals
  where it burned, and smoke.

## 8. Building aboard (IW10)

- **What may stand aboard:** every floor-standing deployable whose placement rule allows the `ship` surface. That is
  the default for deployables; `aboard: false` turns it off for some:
  - the tool cupboard, since the ship is its own;
  - the vehicle workbench;
  - anything whose rule says so.

  Placement already has a `Ship` surface that nothing maps.
- **Where:**
  - inside the asset's build zones: the fabrication bay, the lower forward and aft decks, the mezzanine;
  - with every corner on a deck within 15 cm;
  - with the footprint clear of the space's pieces, doors and other things aboard;
  - outside the exclusions: the ramp's top and its swing, the airlock's approach, stair footprints with 1 m landings,
    the stair void, the bridge, 1.2 m lanes along the asset's routes to the stations, the stations' access and exit
    volumes.
- **Tall things** (the large furnace and anything taller than 3.6 m) only in the 7.5 m bay.
- **How:** the one placement pipeline (GROUND_SYSTEMS.md §6):
  - the `free` solver runs in the ship's frame (its down);
  - the support is the ship's deck part;
  - the placed deployable keeps a support link to the ship with its pose in the ship's frame;
  - it goes into the ship's space as a box.
- **Who:** the ship's authorised players.
  - The owner authorises at the helm with the cupboard's verbs: authorise, deauthorise, clear. The ship carries the
    `cupboard` capability.
  - No upkeep or decay aboard.
  - 80 deployables per ship.
  - A bed or sleeping bag aboard is a spawn point aboard.
- **What happens to it:**
  - Things aboard open, burn and work as anywhere: a furnace smelts in flight.
  - They are saved with the ship.
  - When the ship is wrecked they break and spill (Rust).
  - Vibration is sound and a little cosmetic sway of loose parts, never their pose (SHIP_FEEL.md).

## 9. How it looks

### 9.1 The import

`art/import/iron_wake.py` reads the asset's `.blend`, as `tin_finch.py` reads the Tin Finch's:
- the collections `01_GAME_LOD0`, `04_GAME_LOD1_2_3`, `03_CONVEX_COLLISION` and `05_ATTACHMENT_POINTS`;
- the axes turned so the nose faces Godot +Z.

It writes `game/assets/ships/iron-wake.glb` and the data file the simulation reads.

**The model's structure** follows the house conventions (`art/hollow/__init__.py`):
- the static hull as `Body_lod0..4`;
- every moving part under an empty at its pivot: ramp main and leaf, the airlock door, six yaw and six pitch nodes,
  sixteen recoil nodes, and the loader's parts (§9.5);
- `socket_*` empties with their orientation (the asset's now carry it);
- the collision as `-convcolonly` hulls by part, for the views' own use (feet, decals; §9.8).

**The data file** `game/data/ground/ships/iron-wake.json` holds, in the ship's frame:
- the convex pieces by part;
- the rooms, the boarding volume, the build zones and exclusions, the routes;
- the sockets;
- the ten hull boxes fitted for the world's sweeps (§5.4).

`ShipsReader` checks it: every station's sockets, parts and nodes named, every seat's exits inside the space.

The model catalogue entry, `iron-wake` (`kind` world, `class` vehicle), carries the LOD distances (§9.3) and later its
`gibs`.

### 9.2 Materials and textures

- **The sixteen finishes.** The asset's sixteen surface definitions (`material_revision.json`) become shared Godot
  materials (`ORMMaterial3D`, `.tres` under `game/assets/materials`): paints, dark and brushed steel, gunmetal, machined
  parts, deck tread, grates, louvres, rubber, seat fabric, warning yellow. Their textures are the asset's 4K Poly Haven
  derivatives, filtered to 2K by the house tool (`art/hollow/textures.py`; the 2K rule, GROUND.md §9). The UVs carry
  the metric repeats (paint every 2.5 m, steel every 2 m), so 2K gives about 800 px a metre on the paint.
- **Ambient occlusion.** Every material turns its ambient occlusion on. No `.tres` does today, so the ORM's occlusion is
  read nowhere.
- **Special materials:**
  - the markings atlas;
  - the instrument display atlas (emissive, lit only when the ship has power);
  - the glass: transparent, low roughness, the interior seen through it;
  - the lamps (emissive, scaled by exposure as the Tin Finch's).
- **Contact darkening** is baked into the vertex colour at import (Blender, 40 rays, as our own models have it), mixed
  as the kits' are. Joints, deck edges and the undersides of fittings then read solid without a texture bake.
- **Texture memory:** about 150 MB at 2K with mipmaps (block compressed), against about 850 MB the 4K maps would hold
  for the whole game.

### 9.3 Detail levels and draw calls

The asset's LODs keep their triangles but not their draw calls: 291 at LOD3. The import merges the static hull's
meshes by material at each level; moving parts stay their own.

| Level | Shown | Triangles | Draw calls (static + moving) |
|---|---|---|---|
| LOD0 | to 70 m | 622k | about 20 + 45 |
| LOD1 | to 180 m | 332k | about 20 + 45 |
| LOD2 | to 450 m | 63k | about 16 + 22 |
| LOD3 | to 1,200 m | 52k | about 12 + 10 |
| LOD4 (made by the import: decimated, one material, no interior) | to 4 km | about 8k | 2 |

Shadows draw the same meshes again from the sun, so the hull's LODs also bound its shadow cost. The interior's
fittings, from the outside, are hidden by occluders made from the hull's own boxes (Godot's occlusion culling), and the
outside is hidden from the inside the same way, except through the open ramp and the glass.

### 9.4 Light inside the ship

The game's renderer has no global illumination, reflection probes, screen-space reflections or fog (survey). For a hull
interior that matters twice:
- the inside would be lit only by the sky through the openings;
- its metal would reflect the open sky.

The Iron Wake therefore brings its own:
- **Lamps.** A light at each of the asset's light fixtures (omni or spot by shape). Four on Deck A and the bridge's
  two cast shadows; the rest don't. They are powered with the engines or by the helm's light switch; dim red
  emergency lamps light the deck without power.
- **Reflection probes.** Six reflection probes in the box shapes of the rooms (Deck A, the bay, the mezzanine, the
  bridge, the two weather decks), in interior mode with their own dark ambient. They are captured once at the start,
  with the ship's own lamps on, and move with the ship. Metal inside reflects the inside.
- **Exposure.** The sky's metering keeps working: inside, the eye opens up as it does in a shaded spot, and the glare
  through the open ramp reads as daylight.

A lighting test in this setup is also the engine question's test (memory: the visual fidelity bar): scanned materials,
bevels, standard PBR and real lamps, judged on the owner's screen.

### 9.5 The loading mechanisms (new art)

Made in Blender by our own script, `art/ships/iron_wake_loaders.py`:
- in the asset's materials and manufacturing language: welded steel plate, hydraulic cylinders, chain, bolts, cable
  looms, stencils;
- attached under the asset's turret nodes;
- every moving piece under an empty at its pivot.

| Battery | Parts |
|---|---|
| Siege (×2, per barrel ×2) | The **sliding breech block** at the barrel's rear, under the recoil node so it recoils with the barrel. The **loader arm** with its **tray**, pivoting on the trunnion axis beside the barrel. The **chain rammer** (telescoping links with a head) on the tray. The **ready rack**: a drum with three rocket cradles and a pusher, on the yaw platform beside the gunner's seat, so the gunner sees both loaders work at either side. A **hydraulic damper** and cable runs |
| Defense (×4) | The **breech face** behind the three tubes. The **cassette**: a steel frame holding three rockets, with latches. The **hoist** rising from the mount's base. The **chute** the spent cassette drops into |

- **The rockets in the racks and cassettes** are the items' catalogue models, three kinds told apart by their bands
  (yellow, blue, red) and noses, so a glance at a rack says what it holds.
- **The parts' clearance** is checked in Blender against the asset's housings and the seated gunner's volumes. The arm's
  swing must not cross the seat's occupied volume or the barrel's recoil path at any elevation.
- **Review renders.** A contact sheet of the phases and a short render of a whole cycle (`docs/images/iron-wake/`) come
  before the mechanism is wired to the rules.
- **The motion lives in the loader table**, not in clips: the phases name a node, an axis, a travel and an easing, and
  the view poses them. If clips are wanted for the model viewer, the script bakes them from the same table.

### 9.6 People in seats

- **The clips.** The character contract has no sitting stance; a retargeted `sit_idle` clip (Quaternius UAL1, CC0) is
  built but unused. A `Seated` stance is added to the animation graph with three poses:
  - **pilot:** hands on the helm's controls;
  - **gunner:** hands on the station's two grips, feet on the footrests;
  - **passenger:** `sit_idle`.

  The pilot's and gunner's poses are authored from `sit_idle` by the character scripts (`authored_clips.py`). The two
  hands reach their grip sockets by the existing two-bone IK, and the head and spine follow the view.
- **Who is drawn.** Seated characters are drawn instead of hidden: everyone at their seat in the ship's frame. That
  closes GROUND.md §11 87 for every ship, the Tin Finch's pilot included.
- **The gunner's own view.** In first person the gunner sees the station, its display and their own hands on the
  grips: the first-person arms with their hands on the grips' sockets.

### 9.7 Cameras

| Who | Camera |
|---|---|
| Pilot | At the pilot's eye socket, the cockpit view; or the chase camera, behind and above by the ship's size (the asset's third-person socket: 41 m back, 19 m up), following the heading as `ShipCamera` does |
| Gunner | At the station's camera socket (§6.8) |
| Anyone aboard | The usual first-person camera in the ship's frame. When the ship banks or brakes hard, a little sway and lag from the ship's acceleration (cosmetic, capped, off with reduced motion) |

### 9.8 Feet, decals and gibs

- **Feet.** Today the feet's IK rays meet only terrain (Godot physics layer 1). The view keeps the ship's collision
  hulls as Godot bodies on layer 1, under the ship's node, so feet stand on decks, stairs and the ramp.
- **Decals.** Bullet holes and scorch marks land on the hull and move with it.
- **Gibs.** A wreck's pieces follow when the model gets its `gibs` entry. The fracture script handles parts by
  material, and the Iron Wake's materials join its table.

## 10. How it sounds

The sound follows SHIP_FEEL.md: the simulation's events, not ship speed, drive the layers. Today's director has pooled
voices, distance layers with the speed of sound's delay, occlusion and loops on moving nodes, but no runtime pitch. Its
room reverb (`AudioBuses.SetSpace`) is never called. The Iron Wake adds three things:
- **The listener's space.** Aboard, the deck's connected reverb is the Deck A, bay and mezzanine volume. The bridge is
  insulated and drier. Outside, open air.
- **Interior transmission.** A battery's shot is heard aboard as a muffled structural thump and rattle as well as
  through the air.
- **Load-driven loops.** The drives' gain and filter follow spool, thrust and load. With no runtime pitch, a slow and a
  fast loop are crossfaded.

| Events | What |
|---|---|
| Drives | Start: contactors, pumps, a 6 s spool. Idle. Load, by thrust. Release, when thrust falls. Shutdown. Lift engines by lift thrust. Manoeuvring puffs. The boost's strain layer and its release |
| Hull | Strain creaks on hard turns, braking and heavy hits, by the simulation's event. Touchdown on the legs. Ventilation and machinery beds inside, by room |
| Doors | The ramp's release, actuator travel loop, contact and lock. The airlock's slide and seal |
| Siege battery | Mechanism attack (near). The exterior report in near, middle and far layers, with the delay. Interior transmission. Recovery. The latch |
| Loader | Breech open and close, the arm's servo, the rack's index ratchet, the rammer's chain, the cassette's latch, eject and lift. Each timed to its phase |
| Defense mount | Its own sharper report and ripple, not a small copy of the siege gun's (SHIP_FEEL.md) |
| Turret | Traverse motor loops (slow and fast, crossfaded by slew rate), the stop at a limit |
| Rockets | Motor ignition, a proper flight loop (today `arrow.flight` stands in), the incendiary's burst, fire by size |
| Alarms | Hull critical, fire aboard. Sparse; no voice (IW13) |

- **Sources.** The CC0 packs already on this PC (`D:\GameDev\Assets\Downloads`: BigSoundBank's 251 recordings, Kenney's
  impact and sci-fi packs, the free firearm library and others), layered and processed by the recipe pipeline
  (`art/audio`). Synthesis (`synth.py`) supplies impulse responses and motors.
- **Mixing.** Every event is measured (`levels.json`). The mix is judged in the playable check (§14) from the bridge,
  the empty deck and outside, with and without built equipment aboard.

## 11. Effects

| Effect | What |
|---|---|
| `muzzle.siege` | A heavy flash, a shock ring, a smoke ring, lingering gun smoke, dust and debris kicked off the deck below the muzzle, a light. A cosmetic camera kick for the gunner and those near |
| `muzzle.defense` | A smaller, sharper flash and smoke, per barrel |
| `ignite.rocket` | The motor lighting a few metres out, then the existing `trail.rocket` |
| `breech.smoke` | A puff from the open breech after each shot |
| Plumes | The ship's nozzles from `ships.json`, sizes from data. Main, auxiliary and lift drives, each growing with its thrust |
| `wash.lift` | Dust and flattened vegetation on the ground under the lift engines within 25 m of it |
| `explosion.incendiary`, `fire.patch` | The burst and the burning patch (§7) |
| `impact.hull` | Sparks and a decal on the hull's metal |
| `smoke.damaged` | Smoke from a disabled battery or a hull below a quarter |

Everything goes through `FxDirector` and its budgets (1,500 particles a frame; the firefight test's +0.29 ms against 2
ms). The siege muzzle stays within one heavy effect's share.

## 12. Network, saves and budgets

- **Protocol** (after S3's 21):
  - the frame in the input and in characters' state (§4.11);
  - ship state by kind: doors' progress, flags, and each station's yaw, pitch, occupant, health and each barrel's
    chamber and loader phase with its progress, delta-coded and sent only when changed;
  - `TurretShot`;
  - `FireStarted` and `FireEnded`;
  - the station's input (fire, zoom, cycle) and intents (the chosen kind);
  - the seats' verbs through `InteractIntent`.
- **Saves:**
  - `ShipCodec` version 2: the seats' occupants, doors, stations (aim, health, chambers, magazines), the authorised
    list;
  - things and sleepers aboard in the ship's frame;
  - a schema step after S3's 7.
- **Budgets** (measured once each, principle 6):
  - **Server.** One Iron Wake with twelve aboard adds under 1 ms to the tick: its step, its space's character steps,
    six stations, the queries through its space.
  - **Client.** The ship near, inside or out, costs under 3 ms on the owner's PC at 1600 × 900, its shadows and lamps
    included.
  - **Network.** The ship and its stations cost under 2 kbit/s to a watcher when idle and under 8 kbit/s in a
    firefight.

## 13. Order of work

S3 is being built on `whole-world` now (WHOLE_WORLD.md §14). Its first step, S3a, rewrites the code §4–§8 change:
- the character's state and motor, and the view;
- the physics world;
- the ship system;
- the history, the messages, the protocol and the saves.

About thirty of those files were already open there on 14 October. Building the rules on `main` meanwhile would mean
merging them into S3 by hand, line by line. So (IW14):

**Now, on `main`** (new files, nothing S3 touches):

| Package | What | Done when |
|---|---|---|
| IW-A, art | The import (§9.1), materials (§9.2), LODs and merging (§9.3), the loading mechanisms (§9.5), the incendiary rocket's model, the seated poses (§9.6), the catalogue entry | `--view models --model iron-wake` renders it; the loader's contact sheet and cycle render; the seated poses in `--view character` |
| IW-S, sound | The events of §10 built by the recipe pipeline | `--view sounds` plays each; `--sound-check` passes |
| IW-F, effects | The effects of §11 | `--view fx` shows each |

**After S3a is green**, on a branch from `whole-world`:

| Package | What | Needs |
|---|---|---|
| IW1 | Ship spaces: frames, transfers, deck gravity, prediction, queries through frames, history, network, saves (§4) | S3a |
| IW2 | Ship kinds, seats, parts and doors; the Iron Wake's flight; spawning by kind (§5). Together with wave 1's seats package, which also changes the seat keys | IW1 |
| IW3 | Crewed batteries (§6) | IW2 |
| IW4 | The incendiary rocket and fire (§7) | S3a |
| IW5 | Building aboard (§8) | IW1 |
| IW6 | The playable slice: the ship sandbox scene, lights and probes, the sound and effects hooked up, the measurements, the pictures, the owner's playtest | All |

IW4 can run beside IW1. Each package lands with every old path moved and deleted, its tests green and one check of its
own, as the kernel's did.

## 14. Checks

**Engine-free tests** (`IronWakeTests`):
- Walk from the ground up the ramp. Take off. The character's place on the deck doesn't move while the ship climbs,
  turns and brakes.
- Jump on the deck. Step off the open ramp at 10 m and land hurt.
- A passenger predicted while another player flies: 0 corrections.
- A shot through the open ramp hits a player inside; with the ramp shut it meets the hull.
- A rocket bursting on the deck hurts those aboard in its sight.
- Stations:
  - sit;
  - the aim held while the ship yaws;
  - fire, and the loader's timeline phase by phase;
  - every refusal of §6.7;
  - HULL BLOCKED across the bridge;
  - no doubled shot over a lagged loopback.
- An incendiary fire burns a wooden box, a wood wall and a player, and spares a stone wall.
- A box placed on the deck rides the ship; one placed in the stair's exclusion is refused.
- A restart with the crew asleep aboard, boxes and a furnace on the deck, magazines half full and a barrel loaded.

**In the game**, once (memory: minimal testing):
- `--view ship --ship iron-wake --tour`:
  - board up the ramp and walk to the forward battery;
  - sit and fire at a test hut while the ship turns, with another player at the helm;
  - leave the seat, walk to the bridge, land, step down the ramp.
- The pictures from it: `docs/images/iron-wake/`.
- Then the owner plays it (the milestone's playtest), from the bridge, the empty deck and outside, then with things
  built aboard. The acceptance list is SHIP_FEEL.md's.

## 15. Open questions

1. **How players get an Iron Wake** (IW12). Possibilities:
   - built at a shipyard monument from a great deal of salvage and refined metal;
   - a derelict found at a monument and repaired;
   - a new "large vehicle bay" deployable.

   It is the most valuable thing in the game, so its cost sets its place in the economy.
2. **Voice alerts.** None now. A local, original voice is possible later with the open text-to-speech model on this PC
   (`D:\GameDev\Tools\kokoro`): "Forward battery ready", "Hull integrity critical". Sparse, subtitled, with its own
   volume (SHIP_FEEL.md).
3. **Richer sound sources.** The Sonniss GDC bundles (royalty-free for games), if the owner wants them downloaded.
4. **The uncrewed weapons.** The roof's 12-cell launch bank and the keel lance: later, fired from the helm or the
   copilot's seat?
5. **Ships and decay.** Do ships and what is built aboard need upkeep, as bases do? (Default: no.)
6. **Walking inside other ships.** The ship spaces work for any ship with an `interior` block. Should the Tin Finch get
   a walkable cabin, or new ships be designed for it?
