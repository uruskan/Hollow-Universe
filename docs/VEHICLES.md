# Ground vehicles: the modular car, the modular motorbike and the vehicle lift (design, 20 October 2026)

Status: **design; the owner answered most of its questions on 20 October 2026 (R13 to R19), four remain (§12). Nothing is
built.** Every rule is marked **Owner** (the owner asked for it on
20 October 2026), **DESIGN.md** (already confirmed there) or is a **proposal** (everything unmarked). Numbers are starting
values for tuning in play, not promises.

The proportion sheet: [images/vehicles/proportions.svg](images/vehicles/proportions.svg) (side elevations to scale; drawn by
`images/vehicles/proportions.py`, `python -I docs/images/vehicles/proportions.py docs/images/vehicles/proportions.svg`).

## 1. What was asked

| | Requirement | Source |
|---|---|---|
| R1 | One modular car and one modular motorbike, in Rust's style and in our own | Owner |
| R2 | They look better than Rust's: Rust's tyres are too small and the vehicles look poor for it | Owner |
| R3 | A **powered** vehicle lift to work on the modular ground vehicles | Owner |
| R4 | Modular in four parts: **chassis**; **engine, engines and engine parts**; **tyres or tracks**; **cabins**: a one-man or a two-man cabin, cabins stacked, or one driver's cabin and the rest of the vehicle made into a portable base with a bed and storage | Owner |
| R5 | Handmade, like the Tin Finch | Owner |
| R6 | The Vehicle Workbench has its own tech tree and no levels | DESIGN.md CRAFT-01, CRAFT-02 |
| R7 | Better variants (an engine, a part) come from blueprints found at monuments, not the trees, and install into the vehicle (the Dune: Awakening sandbike with a rare engine) | DESIGN.md CRAFT-06, CRAFT-07 |
| R8 | Spacecraft modules follow Rust's modular cars: **one module system serves ships and ground vehicles** | DESIGN.md SHIP-01, SHIP-02 |
| R9 | Electricity through the shared graph: data-declared ports, an input and a passthrough on a consumer, the Wire Tool | DESIGN.md ELEC-01 to ELEC-03 |
| R10 | Tap E, hold E for the radial; Rust's interface; a health bar on everything that takes damage | DESIGN.md INT-01 to INT-03, UI-01, UI-03 |
| R11 | Vehicles persist with what they carry; optimisation is a constraint | DESIGN.md WORLD-05 |
| R12 | Names our own; Rust's mechanics, numbers and interface may be used | AGENTS.md rules |
| R13 | The car is the **TOGG**, the motorbike the **Zamazingo** | Owner |
| R14 | Engine parts can be swapped on the road, away from the lift | Owner |
| R15 | Vehicles **decay when they are not in a tool cupboard's zone** | Owner |
| R16 | A bed or sleeping bag on a vehicle is a respawn point | Owner |
| R17 | **Space** gets out of a vehicle (as in Rust and our ship seats) | Owner |
| R18 | Builds too tall for the garage door are accepted; real garage doors for vehicles come later | Owner |
| R19 | Repairs on the road are made with the **hammer**, hit by hit as on building, each costing **metal fragments or refined metal** by what is repaired | Owner |

**What Rust does** (the reference for R1, from Facepunch's own beta post, rust.facepunch.com/blog/vehicles-beta-branch, and
two player guides): three chassis lengths of 2, 3 and 4 module slots; modules for cockpits (plain, with an engine, armoured),
engines, passengers, storage, flatbeds and a tank, each with its own health; five engine parts (carburettor, crankshaft,
pistons, spark plugs, valves) in three qualities, any quality running the engine and better ones going faster and further on
the fuel; a modular car lift drawing **20 power**, needed to add, remove or move modules; a vehicle leaves the lift only with
an engine and a driver's seat; locking at the lift makes a key; cars decay (216 minutes outside, ten times longer under a
roof). We keep that game and change what the owner asked.

## 2. The idea

A vehicle is a **chassis** carrying **modules**. The chassis is the frame, its hubs and a row of **bays**. Modules (cabins, an
engine, a deck, a camper shell, storage) stand in the bays, and some stand **on top of others** (a second level). The
**running gear** (four tyres, or two track units) goes on the hubs, chosen apart from the bays. An **engine** module holds
**engine parts**, which decide its power, its fuel use and how fast it wears. Every module, tyre and part is an **item**: it
comes out of one vehicle and goes into another. The **lift** is where the build changes; on the road you only fuel, load,
repair and swap engine parts.

The car and the motorbike are the same system: the bike's frame has **mount points** (engine, tank, seat, rack, sidecar,
front and rear running gear) in place of bays. Later spacecraft use it too (R8): the Tin Finch's kind becomes a chassis whose
sockets take an engine and a cargo pod. A minicopter session (rotor drive, its own modules) and a motorboat session
(a propeller drive) are building on this same system now; §9 is the part they share.

### Words

| Word | What | An item? |
|---|---|---|
| Chassis | The car's welded ladder frame: four hubs on long-travel arms, a nose mount, three bays, bumpers, a small fuel tank | Yes: crafted, placed on a lift |
| Frame | The motorbike's chassis | Yes |
| Nose mount | The car's engine socket ahead of the bays, under a hood, as on a truck | — |
| Bay | A 1.4 m length of the chassis's deck, its whole width | — |
| Level | 1 on the frame; 2 on the roof of a level-1 module that carries load | — |
| Mount point | A bike frame's named socket for one kind of module | — |
| Module | Anything in a bay or on a mount point | Yes |
| Running gear | Tyres, track units, a ski: what touches the ground | Yes |
| Engine part | Crankshaft, carburettor, pistons, valves, spark plugs, each in three tiers | Yes |
| Build | The list of what is installed where; it decides the vehicle's shape, seats, mass and parts | — |

## 3. The car

**Name.** The car is the **TOGG** (`togg`): **Owner**, 20 October 2026. Its name is stencilled on the cab as the Tin Finch's is.
The name is Turkey's own carmaker's; the owner confirmed it is fine to use (20 October 2026). The car itself is ours, scrap-built
like everything players make, so no badge or body shape is copied from the real one. The motorbike is the **Zamazingo**
(`zamazingo`): **Owner**, 20 October 2026.

### 3.1 The chassis

| | Value | Why |
|---|---|---|
| Length × width | 6.0 × 2.3 m over the tyres (track 1.95 m) | A big pickup's footprint; fits a 6.4 m lift and the 2.7 m garage opening's width |
| Layout, rear to front | Rear bumper with the tank (0.35 m), three bays (4.2 m), the **nose mount** under its hood (1.2 m), front bumper with the lamps | The engine forward, as on a truck, leaves the bays to cabins and the camp (§3.2) |
| Wheelbase | 3.6 m, the front axle under the nose | Stable, and the silhouette of a working truck |
| Tyres | **0.96 m** across, 0.46 m rims, **0.25 m of sidewall**, 0.32 m wide, knobbed | §7: the look the owner asked for |
| Suspension | Independent arms, coil-overs, **0.35 m of travel** | Rough baked terrain (slopes to about 45°) and no roads yet |
| Under the frame | 0.42 m | Crosses the eroded ground without beaching |
| Deck | Top of the frame at 0.98 m; three bays of 1.4 × 2.0 m | |
| Height | A cab: 2.6 m (passes the 2.85 m garage opening); a camper shell 2.95 m and two levels up to **3.9 m** (they do not: accepted, R18; a vehicle garage door comes later) | §3.3 |
| Mass | Chassis 650 kg bare; load at most 1,800 kg | The mass budget is the trade-off between armour, people and a camp |
| Chassis tank | 100 fuel | The fuel is lamp oil, the stand-in for low-grade fuel (as the ships use) |

The bare chassis cannot leave its lift: it has no seat, engine or running gear until they are fitted.

### 3.2 Modules

| Module | Bays | Levels | Load on its roof | What it gives | Mass | Health |
|---|---:|---|---|---|---:|---:|
| **One-seat cab** | 1 | 1 or 2 | Yes | One seat, a door each side, glass. On level 1 its seat drives; on level 2 it is a lookout | 220 kg | 600 |
| **Two-seat cab** | 1 | 1 or 2 | Yes | Two seats side by side, same rule | 300 kg | 750 |
| **Engine** | Nose mount, or 1 bay | 1 | No (hot: stacks and an air filter stand up) | Power from its parts (§5) | 280 kg | 500 |
| **Camp deck** | 1 | 1 or 2 | No | A plank floor with rails: a **build zone** for a bed, a sleeping bag, boxes; people ride standing on it | 120 kg | 400 |
| **Camper shell** | 2 | 1 | Yes | A small room (2.8 × 2.0 × 1.95 m) with a door: a **build zone** inside; the portable base | 450 kg | 1,000 |
| **Storage** | 1 | 1 or 2 | Yes | A 24-slot locker | 150 kg | 500 |
| **Bench** | 1 | 1 or 2 | No | Four open seats facing out; their riders can shoot | 140 kg | 400 |
| **Fuel tank** | 1 | 1 | No | 300 more fuel | 160 kg | 350 |

The owner's examples are all builds of these, with the engine in the nose mount:

- **two men**: a two-seat cab, then two free bays (storage, a bench, a deck);
- **stacked cabins**: a one-seat cab with another on its roof (driver and lookout), or two two-seat cabs stacked for four;
- **one driver's cab and a portable base**: a one-seat cab, then a camper shell over the other two bays with a bed and boxes
  inside (the sheet's panel 2); or a cab, a camp deck and a locker, open to the sky and lighter;
- **more power**: a second engine in a bay, for a heavy build or tracks (the sheet's panel 1).

A cab with an engine in it (Rust's) is left out: the owner listed cabins and engines apart.

Later modules, not in the first delivery: an armoured cab (firing slits, sheet-metal plates), a 2-bay heavy engine, a crane,
a water tank, a roof-mounted gun station (the Iron Wake's stations, IRON_WAKE.md §6).

### 3.3 Rules of a build

1. **Bays.** A module takes its bays in a row. A 2-bay module needs two free neighbouring bays.
2. **Levels.** A level-2 module must stand wholly on level-1 roofs that carry load (the table's column). At most two levels;
   nothing is taller than 3.9 m.
3. **The driver.** The vehicle is driven from the driver's seat of the **front-most level-1 cab**; every other seat is a
   passenger's (one helm, as the ship system requires). A build may have more cabs; it never has two drivers.
4. **Mass.** The installed mass stays under the chassis's load. The build's centre of mass is computed from the modules'; the
   lift shows a tip rating (how far it leans before it rolls), so a tall camp on a short car is a visible choice.
5. **To leave the lift** a build needs a level-1 cab, at least one engine (nose mount or a bay) with every part slot filled, a
   full set of running gear and fuel. (Rust's rule, plus running gear.)
6. **Engines add.** Two engines give twice the power and use twice the fuel; heavy builds need them. The nose mount takes
   only an engine; an empty nose is closed by its hood.
7. **Build zones** (camp deck, camper shell) take deployables as the Iron Wake's decks do (IRON_WAKE.md §8, IW5): placed
   through the one placement system, carried by the vehicle, counted against a limit (proposal: 8 per deck bay, 12 in a
   shell). What may go aboard is the data's (`placement.json` `aboard`): beds, sleeping bags, small and large boxes, a water
   barrel, a workbench level 1, a lamp. Not furnaces or campfires (fire on a moving wooden deck), not building pieces.
8. **Removing** a module needs it empty: a locker emptied, a deck's or shell's deployables picked up, an engine's parts taken
   out. An item never carries another inside it (the game has no nested inventories, and this does not add one).

## 4. The motorbike

| | Value |
|---|---|
| Wheelbase | 1.544 m |
| Tyres | **0.90 m** front (0.53 m rim, 0.15 m wide), **0.86 m** rear (0.46 m rim, 0.20 m wide), tall knobs: an off-road bike's, not a scooter's (the owner found the first 0.80 and 0.76 m too small for off-road, 20 October 2026) |
| Seat height | 1.05 m (0.95 to 1.0 m before the bigger tyres); 0.28 m of travel |
| Mass | Frame 95 kg; about 160 kg ready |
| Tank | 60 fuel (long-range tank 120) |

**Mount points**

| Point | Takes | Required |
|---|---|---|
| Engine cradle | The single (§5) | Yes |
| Tank | Standard tank or long-range tank | Yes |
| Seat | Solo seat (1) or dual seat (rider and a pillion) | Yes |
| Front running gear | Front tyre, or a ski | Yes |
| Rear running gear | Rear tyre, or a short track | Yes |
| Rear | Rack with panniers (12 slots) | No |
| Side | Passenger sidecar (one seat, its rider can shoot) or cargo sidecar (18 slots) | No |

The **sidecar is the bike's cabin**: it adds a seat or a hold and a third wheel. **The snow kit** is the ski and the short track
together (a real conversion, a dirt bike made a snow bike): slower on hard ground, the only bike that crosses deep snow. The
bike uses the same lift (a wheel clamp on its platform) and the same engine parts as the car.

**Riding.** The rider is in the open and can be shot. The bike leans into turns by itself (§6.3); stopped, it stands on its
side stand. A crash above about 8 m/s, or the ground tipping it past 60°, throws the rider off: they fall and take damage by speed (and can
be left wounded), and the bike lies on its side until someone rights it (hold E, two seconds, anyone). With a sidecar it is a
three-wheeler: it does not lean and pulls toward the sidecar under power.

## 5. Engines and engine parts

### 5.1 Engines

| Engine | Where | Part slots | Power with tier-2 parts |
|---|---|---|---|
| **Engine module** (car) | The car's nose mount, or a bay | crankshaft 1, carburettor 1, pistons 2, valves 2, spark plugs 2 | 90 kW |
| **Single** (bike) | The bike's cradle | crankshaft 1, carburettor 1, piston 1, valves 1, spark plug 1 | 35 kW |

Engines are data (`modules.json`, §9): another engine (the 2-bay heavy one, a rare one, a rotorcraft's) is a new entry, not
new code.

### 5.2 Parts and tiers

Five part kinds, as Rust's; three tiers:

| Tier | Name (proposal) | Power | Fuel per kWh | Wear | Where it comes from |
|---|---|---:|---:|---|---|
| 1 | Salvaged | ×0.70 | ×1.25 | Fast | Loot (roadside sites, the Scrapyard, the Fuel Depot); also a cheap Vehicle Workbench recipe |
| 2 | Machined | ×0.85 | ×1.0 | Normal | Vehicle Workbench tree |
| 3 | Precision | ×1.0 | ×0.8 | Slow | **Blueprints found at monuments only** (CRAFT-06, CRAFT-07) |

An engine's power is its base times the **mean** of its parts' power factors (so one tier-3 piston in a tier-1 engine helps a
little); its fuel use is likewise the mean. **Every slot must hold a part with condition left**, or the engine does not start (a
part at zero condition is broken, as Rust's). Parts wear while the engine runs, faster under full throttle; the condition is
the item's own (`ItemStack.Condition`).

**Rare engines** (CRAFT-07, the Dune sandbike's rare engine): an engine module variant from a found blueprint, more power from
the same parts or one more slot. Which ones is content for later; the system needs nothing new for it.

### 5.3 Where parts are changed

- **At the lift**: everything.
- **On the road** (R14): an authorised player opens a stopped vehicle's engine (hold E on it → OPEN ENGINE) and swaps parts
  by hand, so a broken spark plug far from home is a repair and not a walk. Modules and running gear are swapped only at the
  lift; damage anywhere is mended with the hammer (§5.5).

### 5.4 Decay (R15)

- **Outside every tool cupboard's zone** a vehicle decays; **inside one it does not**. The zone is the claim device's area
  (`Building/ClaimDevice.cs`); any cupboard's zone counts, as the owner's words name the zone and not who is authorised on it
  (a reading to confirm). A vehicle on a lift in a base is therefore safe.
- **The rate** (a starting value): Rust's default for a modular car outside, a full-health vehicle reaching zero in
  **216 minutes**. Every module and the chassis lose the same fraction of their health each minute; tyres and parts in an
  engine do not (parts wear by use, §5.2).
- **Not while in use** (proposal): decay waits until nobody has sat in the vehicle for ten minutes, so a long drive costs no
  health.
- **At zero** a module is broken (it stops working until mended with the hammer, §5.5); when the chassis reaches zero the vehicle
  becomes a wreck, as a ship does: a lootable heap holding what its lockers, engine and build zones held.
- Ships keep their own open question on decay (IRON_WAKE.md); the minicopter's design may take this rule.

### 5.5 Repairs with the hammer (R19)

**Nothing here is the vehicles' own: it is the game's one repair system.** The hammer mends everything repairable through
one path: `BuildingSystem.Repair` (the hammer in hand, reach, sight) asks the aimed thing's target family to mend it
(`IRepairFamily`: building pieces, doors, deployables, ship parts each implement it once; GROUND_SYSTEMS.md §5), and pays from
what the player carries. What a thing costs is its data. Vehicles are ships in the code, so the ship family's repair mends their
modules, chassis and running gear, and each one's cost is a line in `modules.json` (the ship parts' `health` block: `RepairMaterial`,
`RepairFull`, the material and the cost of a full repair). No car or bike code decides a repair.

- **A hit** mends a share of the thing's health for its share of the cost, as on building pieces. Anywhere: on the road, in a
  base, on the lift (where REPAIR ALL is every hit at once for the sum).
- **Costs by data** (starting values): **metal fragments** for the chassis, the frame, cabs, decks, the camper shell, lockers,
  the bench, fuel tanks, tyres and the bike's modules; **refined metal** for engine modules, track units and, later, the
  armoured cab. Engine parts are not mended: they wear and are replaced (§5.2).
- **One change to the shared code**: today the ship family refuses to mend a ship's hull (`ShipSystem.RepairPart`, part 0).
  It will mend any part whose data gives it health, the hull included, so the Tin Finch's hull becomes repairable too.
- A broken module mended above zero works again. A wreck is not mended: it is looted.

## 6. Running gear and driving

### 6.1 Tyres and tracks

| Running gear | Fits | Top speed (one engine, light build) | Steepest climb | Grip on snow, sand, mud | Notes |
|---|---|---:|---:|---|---|
| **Car tyre** ×4 | Car hubs | 72 km/h (20 m/s) | 32° | Fair | Can be shot flat |
| **Track unit** ×2 | Car hubs (one per side, on both hubs) | 40 km/h | 40° | Good | +400 kg, turns on the spot, does not go flat, more fuel |
| **Bike tyres** front + rear | Bike | 90 km/h (25 m/s) | 35° | Poor | Can be shot flat |
| **Snow kit** (ski + track) | Bike | 55 km/h | 35° | Best on snow | |

The running gear is all one kind on a car (four tyres or two tracks). **A tyre is a target**: shot to zero it is flat (its
radius falls to the rim's, grip drops, the car pulls to that side and drags) and stays usable, slowly, until mended with the
hammer (§5.5) or replaced at the lift. Tracks only take explosive damage. Earth is 1,200 m in radius and 7.5 km round
(EARTH_TERRAIN.md): these speeds put a lap of it at six to seven minutes by car, close to crossing a Rust map.

### 6.2 The driving model

**A step of our own, not Jolt's vehicle.** The bundled JoltPhysicsSharp 2.23 has Jolt's whole vehicle API (`VehicleConstraint`,
the wheeled, motorcycle and tracked controllers, a gravity override), but using it means dynamic bodies, stepping Jolt with
time, and no way to save and restore its state for the client's replay. Nothing in the game uses Jolt that way: characters and
ships are each a pure function of state and input, stepped at 30 Hz and replayed by the client to the bit (GROUND.md §4.5);
grenades, bullets and gibs have integrators of their own; no body is a dynamic Jolt body. Vehicles stay that way. Ships already do almost everything a car needs: a custom 6-DOF integrator, ray
spring-dampers for their skids with friction solved in passes, a swept compound hull, gravity toward the planet's centre with
the frame turned as the ball curves, sleep and parking, prediction and reconciliation (`ShipMotor`, `ShipCollider`,
`ShipPredictor`).

The ground motor (§9.2):

- **Contacts.** Each wheel is a **sphere cast** of the tyre's radius along its suspension axis (a ray falls into cracks; a
  0.96 m tyre should roll over them) against everything static the ship collider already queries: ground tiles, trees, rocks,
  building, deployables, parked vehicles. A track unit is five such casts (its road wheels); a ski is two short ones.
- **Suspension.** A spring and damper per contact, with travel and a bump stop from the running gear's data.
- **Tyre forces.** Lateral grip from the slip angle on a peaked curve; drive and braking along the wheel; the two limited
  together by a friction circle of μ × load. μ is the running gear's (a table) times the ground's (the surface material at the
  contact: rock, soil, grass, sand, snow, mud, wet). **No wheel-spin state**: traction comes from the engines' power over the
  speed, capped by grip; wheelspin is computed and drawn, not integrated. That keeps the state the ship's plus a steering angle.
- **Tracks** steer by driving one side against the other (skid steer).
- **Gravity** is −up at the body (`ZoneFrame.UpAt`), each substep; the frame turns with the curvature as the ship's does. On a
  world 1.2 km in radius, "down" turns by a degree every 21 m driven.
- **Substeps.** Always 2 (vehicles are always near the ground); 4 above 15 m/s if the measurements say so.
- **The hull** (from the build's modules) is swept as a ship's is; impacts damage the modules nearest the contact, and the
  people inside by the impact's speed.
- **Rolling over.** A vehicle on its side or roof and still for two seconds can be pushed back upright by anyone (hold E,
  three seconds), as in Rust.
- **Water.** Up to the wading depth (0.9 m car, 0.5 m bike) nothing happens; deeper, the engine stalls and the vehicle settles
  on the bottom, where it waits to be recovered.

### 6.3 The motorbike's lean

The bike's roll is not a free axis. The motor sets the lean that balances the turn it is in (lean = atan(v² × curvature / g)),
rate-limited, and turns the body about its contact line; the tyres' forces act as if it were upright (as built: a roll spring
and damper toward that lean, the side grip at the centre of mass's height, §9.2). Only a crash (§4) frees
it, as a body on three points (two wheels and the bars). Most games' bikes work this way: it rides well, it falls only in a
crash, and it replays exactly.

### 6.4 People and vehicles

- **A moving vehicle is solid to people.** Today a ship is a body in the physics only when parked. A driven vehicle keeps one,
  moved to its pose every tick, as the doors' leaves are since D3 (DOORS.md §5); the character controller's penetration
  recovery pushes people out of its way.
- **Running people over.** Above 4 m/s, a vehicle's swept hull hitting a player or animal deals damage by the square of the
  closing speed through the damage pipeline (a new delivery, *impact*), which can down or kill, as in Rust.
- **Riding.** Seats (the ship system's: sit, stand, every way a seat is freed) carry their people. People can **stand on a camp
  deck** while the vehicle drives, as people walk the Iron Wake's decks in flight (its frames and hand-overs, IRON_WAKE.md §4).
  Open seats (bench, pillion, sidecar) may fire held weapons; whether today's seats allow that is to be checked, and if not,
  it is a package of its own.

### 6.5 Controls (Rust's; Space to get out is the owner's, R17)

| Key | Car | Bike |
|---|---|---|
| W / S | Throttle / brake, then reverse | Same |
| A / D | Steer | Steer (it leans by itself) |
| Space | **Get out** (R17), as from every seat | Same |
| Left Ctrl | Handbrake (proposal) | Rear brake (proposal) |
| Left Shift | Overboost while held (§9.10: the TOGGs) | Nothing (no boost in its data) |
| Mouse | Look round (the camera orbits) | Same |
| V | First person or chase camera | Same |
| F | Lamps | Same |
| H | Horn | Same |
| 1–9 | Move to another free seat | 2: pillion or sidecar |

### 6.6 Beds aboard (R16)

A sleeping bag or bed placed in a build zone (a camp deck, a camper shell) is a respawn point like any other, with SURV-04's
cooldowns, and goes wherever the vehicle goes. Proposal: it can be chosen only while its vehicle is still (a respawn onto a
moving deck is refused with its line, the bed listed as moving), and it goes with the vehicle when that is wrecked. When
the vehicle is out of the zone's streaming the bed is still listed, at the vehicle's last saved place.

## 7. The look

**Handmade, as the Tin Finch is (R5, ART-01, LORE.md §4).** The Tin Finch is the measure: pale sky-blue paint worn to rust,
cream tanks, a faded red fuel can, an exposed engine bay, round lamps, stencilled names, panels and windows held by bolted
strips, tube skids. The car and the bike are built by the same hands: welded square tube, salvaged sheet, mismatched panels,
visible hinges, bolts, cables, hoses and exhausts. They look like they work, and like someone keeps them working.

**Wheels that read right** (R2, the sheet's panel 4). The owner finds Rust's tyres too small. A wheel reads small when the body
hangs over a thin tyre with no travel to see; ours follow four rules:

1. **Size.** The tyre is at least 0.53 of a person's height on the car (0.96 m for 1.8 m) and 0.44 on the bike.
2. **Rubber.** At least a quarter of the diameter is sidewall; the knobs stand proud of the carcass and are modelled, not
   painted, at LOD0 and LOD1.
3. **Air.** Open arches that clear full bump and full lock; the wheel's top edge stands above the frame line, so the
   silhouette is wheels first.
4. **Mechanism.** The arms and coil-overs are seen and move (the springs painted the fuel can's red), the steering turns the
   hubs, the tyres compress; a track unit shows its sprocket, idler and road wheels.

**Modules** carry their construction: cabs of square tube with perspex in bolted frames, a roof rack and grab rails; the engine
an open block with an air-filter can, a radiator and two short stacks; the camp deck planks on steel with a tube rail; the
camper shell corrugated sheet and tarp around a salvaged hatch; lockers of riveted steel; the fuel tank a strapped cylinder on
cradles. Every module meets the deck on the same rail and bolt pattern, so any module looks fitted to any bay.

**The lift** is a welded scissor platform of I-beams with salvaged hydraulic rams, an electric pump box with its power port,
a control post with a lever and a lamp, wheel chocks and a bike clamp. Its power port is a cube as every port is (ELEC-02).

**Art contract** (for the production brief, `D:\GameDev\Assets\Hollow_Production_Briefs_R01` style):

| | Contract |
|---|---|
| Units and axes | Metres; Blender +Y forward, +Z up; glTF Y-up |
| Chassis origin | On the ground under the rear axle's centre, so the bays' positions are numbers in the data |
| Sockets | Per hub (`Hub_FL`, `Hub_FR`, `Hub_RL`, `Hub_RR`) with the steering axis; per bay (`Bay_1` to `Bay_3`, origin at the bay's rear centre on the deck); per module roof (`Roof`, level 2); seats and eyes; exhausts, lamps, fuel cap, engine access |
| Moving parts | Tyres, steering knuckles, arms and springs separate, posed by the game from each wheel's compression and steer; track units with a belt whose texture scrolls; cab doors on hinges; the lift's scissor in a clip |
| Collision | Each module's hull boxes exported to the data, as the Iron Wake's are (`art/import/iron_wake.py`) |
| LODs and budgets | LOD0/1/2. Chassis 20–30k triangles, a module 6–15k, a tyre 3–4k (four of them), the bike ready to ride 25–35k, the lift 15–25k |
| Materials | Two to four per family on a shared trim sheet; 2K PBR; the Tin Finch's palette family, each vehicle its own paint |

**Look first.** The owner checks the look before the kit is built: a first sample is the chassis on its four tyres with one
two-seat cab and the nose engine, at player height and at 30 m, in the game's light. Until then the game draws blocks of the right
size (the sheet's), so the driving can be tuned without the art. (On 20 October 2026 the owner asked for the finished models
at once; the first pass is §9.8.)

### 7.1 The sound (the owner, 20 October 2026: "a really good diesel heavy motor sound … suspension sounds, tire sounds based on
where it's driven … AAA level quality")

A vehicle is heard as a machine with a gearbox, not as a pitch that rises with speed:

- **Revs.** The motor drives the wheels by power alone; the sound's gearbox (`VehicleRevs`, presentation) gives the engine its
  revs from what every client sees: its speed along its nose, its throttle, its engine running and its wheels on the ground. It
  slips the clutch to pull away, changes up as the speed grows and down as it falls (each change a clunk and a moment off the
  load), free-revs in neutral and with its wheels in the air, bounces off its limiter, drags on the overrun, and spools a
  turbo by revs and load. Its revs, gear, load and boost are also the HUD's and the exhaust's.
- **The engine** is loops made at several revs, on the throttle and off it (`sounds.json` `engines`): each pitched to the revs
  and crossfaded with its neighbours at equal power, the throttle's loops against the overrun's by the load. The Zamazingo
  is an air-cooled single: its thump at idle, its bark under load, pops and crackle on the overrun. The TOGG is a turbo-diesel
  six: its clatter at idle, the lope of its six cylinders, a deep load through two stacks, the turbo's whistle and its release,
  and only air on the overrun (fuel cut). Starts and stops are their own sounds: the bike kicked, the diesel cranked by its
  starter, cold and loud until it settles.
- **The tyres** play the ground under the vehicle (its physical surface: grass, dirt, gravel, sand, rock, snow, water; wood
  and metal as rock), swelled and pitched by speed, a new ground crossfading in; a skid when it slides sideways or spins its
  wheels pulling away on loose ground; a splash into water.
- **The suspension** knocks when a wheel hits its stop or is struck hard, creaks as it works (people climbing on, the body
  rocking), and a landing slams it.
- Each kind's numbers (idle, redline, gears, flywheel, loops and their levels, tyres' pitch and level, which knock) are its
  `engines` entry, so a new vehicle is a new entry; the monster TOGG is the TOGG's, pitched down and louder.

## 8. The vehicle lift

| | Proposal |
|---|---|
| Item | `vehicle-lift`, a deployable crafted at the Vehicle Workbench, learned in its tree |
| Footprint | 6.4 × 3.0 m platform; on foundations or ground under 5° |
| Power | **20**, as Rust's: a consumer with one input and one passthrough (`PowerStats.Check` requires both). It draws only while a vehicle is on it **and** someone is working on it. Unpowered it cannot raise, lower or edit; fuel and lockers stay usable |
| Raising | Up to 0.6 m when there is room above, less under a low ceiling; the height is for showing the underside and does not gate anything |
| Holds | One car or one bike |

**A new vehicle is born on a lift.** The chassis (or bike frame) is crafted at the Vehicle Workbench as an item (CRAFT-04's
station rule) and placed **onto an empty, powered lift**: the placement ghost snaps it to the platform. Then modules go in.
This replaces the Tin Finch's way (delivered at the workbench's bay) for anything modular; a bare chassis anywhere else would be
useless.

**Working.** Drive onto the platform, step out, tap E at the control post: **EDIT VEHICLE** opens the vehicle screen (Rust's
layout, `docs/CRAFTING_HUD.md` and the loot screen's look):

- left, the player's inventory, as in looting;
- right, the vehicle drawn side-on as slots from its data: the bays in a row, level-2 slots over the roofs that carry load,
  the running gear under the hubs, and the engine's part slots when an engine is picked;
- a panel of what the build makes: mass and load, the tip rating, power, top speed on the flat, fuel use, seats, warnings
  (no driver's cab, a part slot empty, too heavy, too tall);
- buttons: **REPAIR ALL** (its cost shown), **LOCK** (a key lock makes a key item, a code lock asks a code: the doors' locks),
  **NAME** (the `nameable` capability ships already have).

Items move by dragging, as everywhere else. While the screen is open the vehicle is clamped; when it closes, a build that meets
§3.3's rule 5 drives off. Only players authorised on the vehicle (its owner and crew, the ship system's list) may edit it, and
only with nobody seated in it.

## 9. How it is built

### 9.1 One system: vehicles are ships in the code

**No new entity, target family or codec.** The ship system already owns seats and every way out of them, doors, fuel, cargo,
the owner and crew list, locks, damage by part, wrecks, sleep and parking, client prediction and reconciliation, its codec,
and placement. A car is a ship that drives; renaming `Ship` to something wider is a later, separate change, not one to make
while three sessions work in it. What is new:

| Concern | Where | What |
|---|---|---|
| Modules | `src/Hollow.Simulation/Ships/Modules/`, `game/data/ground/modules.json` | `ModuleDef` (what a module adds), the sockets a kind declares (`ships.json` `sockets`: bays and levels, a nose mount, or named mount points, each with tags), the **build** (what is installed where), and the **assembler**: kind + build → one composed `ShipLayout` (seats, parts, doors, hull boxes, containers, build zones), mass, inertia, centre of mass, health, fuel capacity and the motor's inputs. Deterministic and cached by the build. Everything downstream reads the composed layout |
| Engines and parts | `modules.json` `engine` blocks; items with an `enginePart` block (a new `ItemPart`) | An engine's slots are a container of a new kind (`ContainerKind.Engine`) whose slots each take one part kind; power and fuel from §5.2 |
| Running gear | Items with a `running` block: kind (tyre, track, ski), radius, width, travel, grip table, health | Installed in hub sockets; each contact a target part (shot flat) |
| The ground motor | `src/Hollow.Simulation/Ships/GroundMotor.cs` | §6.2, one step for wheels, tracks and skis, chosen by `ships.json` `"motor": "ground"` (§9.2) |
| Lift | A `service` block on the deployable (holds which kinds, power, raise, platform) and a `service` capability with its verbs | The only place a build changes. A rotorcraft's service could be the Vehicle Workbench's bay: the same block on another deployable |
| Editing | One reliable message, `ModuleIntent` (install, remove, move; socket; inventory slot), checked by the server: authorised, still, nobody seated, held by a powered service station, §3.3's rules | Answers through `Outcome` codes and `texts.json` lines |
| Seats | The existing `"exposed": true` seat flag now also means "needs no door" in a ship without an interior (the boat session's 8806f350 on `claude/motorboat`, not on abtest-b yet) | Bench, pillion, sidecar, the rotorcraft's open seat |
| Passengers | A passenger in a vehicle they don't drive is posed from the drawn vehicle (`RemoteShip.TryPoseAt`) in `Client/Modules/ShipModule.cs` `SeatAt` | The same commit, 8806f350 |
| Moving bodies | A driven vehicle's hull is a body moved each tick in the ship body range (as doors since D3) | §6.4 |
| Run-over | An *impact* delivery in `DamageSystem` | §6.4 |
| Decay | Once a minute in the ship system: outside every claim (the claim device's area query), unused for ten minutes, each module and the chassis lose 1/216 of their health | §5.4 |
| Beds aboard | Bags and beds placed in a build zone are ordinary respawn points (SURV-04's cooldowns), carried by the vehicle | §6.6 |
| Network | `ShipNetState` gains the build's number; `ShipDetails` carries the build (modules and each one's health); clients compose the same layout. Remote vehicles' wheel compression is cast by the client in its own physics copy, not sent | Bandwidth stays a ship's |
| Saving | `ShipCodec` version 7: the build, each module's health and container, the running gear with its condition, the engine parts | WORLD-05 |
| Controls | The boat session's data-chosen control schemes (`game/scripts/Ships/ShipControlSchemes.cs`): `car` and `bike` | §6.5 |
| Drawing | `game/scripts/Ships/`: the modular view (module models at their sockets), the wheel rig (compression, steer, spin, the bike's lean, the tracks' belt), the vehicle HUD (speed, fuel, engine state), the lift view; the vehicle screen in `game/scripts/UI` | |
| Sandbox | `--view vehicle` with scenes (`pad`, `lift`, `hill`, `camp`, `bike`) and `--tour` (drive a loop, park on the lift, swap a module, drive off; exit code 0 when every check holds) | As `--view ship` |

### 9.2 Motor and drive (agreed with the motorboat and minicopter sessions, 20 October 2026)

Two levels, two names:

- **Drive** (`ships.json` `"drive": {"kind": ...}`, `IShipDrive`, being landed by the motorboat session): inside today's flight step,
  what the engines ask for in a tick (thrust and turn). Kinds: `thrusters` (today's), `propeller` (the boat), `rotor` (the
  minicopter). The shared step keeps gravity, substeps, the hull's sweep, skids, crashes, sleep and buoyancy.
- **Motor** (`ships.json` `"motor"`, `IShipMotor`): the whole step. The default is today's `ShipMotor.Step` with its drive;
  `ground` is ours (§6.2), which owns its contacts (wheels, tracks, skis in place of skids) and reuses the shared helpers
  (gravity and the curvature turn, the hull's sweep, crash damage, sleep, parking).

### 9.3 What the module system gives the other vehicles

The minicopter session asked for these; the design covers each: per-vehicle mass, inertia, centre of mass, health and fuel
capacity from its modules (the assembler); a module's contribution to the motor or drive (an engine's power; a rotor's lift and
spool time are the rotor's own data in its module); one engine module usable by a car and a rotorcraft (an engine is data: its
slots and base power; a rotorcraft can take the car's engine module or its own); modules swapped only at a service station (the
lift; for aircraft, the same block on the Vehicle Workbench's bay); a module adding one open seat with no door (the seat flag);
a module adding one cargo container (as storage does).

**Item ids.** Items are numbered on the wire. The boat has 239, the minicopter 250–264; this design has 240–249 and needs about
40 in all (the chassis, the frame, the lift, 16 modules, 6 kinds of running gear, 15 parts): proposal 240–249 and **270–299**,
confirmed with the other sessions when the work starts.

### 9.4 Checks

Engine-free tests, by name:

- `GroundMotor`: a vehicle stepped twice from one state and input gives the same bits; a client's replay matches the
  server's; it climbs 32° and not 36° on tyres, 40° on tracks; a flat tyre pulls; it rolls on a tall camp in a hard turn and not
  on a low build; push-to-upright; wading and stalling.
- `Modules`: every rule of §3.3, each refusal's code; the composed layout of each example build; the build round-trips through
  the codec and the network.
- `Lift`: no power, no edit; power drawn only while working; authorisation; nobody seated; a chassis placed only on an empty
  powered lift; a build leaves only when complete.
- `RunOver`, `Impact`: damage by closing speed; nothing below 4 m/s; a person pushed out of a moving hull.
- `Bike`: lean, crash thresholds, the rider thrown, righting.
- `Decay`: outside a claim the health falls at the rate; inside any claim it does not; it waits ten minutes after use; a
  decayed chassis makes a wreck with the contents.
- `Respawn`: a bed on a still vehicle respawns its owner on it; a moving one is refused; a wrecked vehicle's bed is gone.
- Content: every module, part, tyre and recipe valid (`ContentTests`).
- Load: 20 bots driving with `--script drive` for 60 s (tick time, bandwidth), as the ships' `fly` script.
- Pictures for every visual step, and the owner's look approval (§7).

### 9.5 V1 as built (20 October 2026)

On branch `claude/vehicles` (abtest-b with the motorboat's and the minicopter's ship work merged):

- **Data.** `game/data/ground/modules.json` (empty until a vehicle declares its modules); a `ships.json` kind may name
  `sockets` (`key`, `at`, `fits`, `row`, `levels`) and a `buildHeight`; an item names its module (`"module": "<key>"`).
  A module is read by the ships' own layout reader: its seats (optional), doors and parts in its own frame, its first part
  its body with health, met by a ball (the interact key and the hammer aim at it) and by the module's hull boxes (a shot
  and a blast take it). A module's seat is reached through its door or is exposed, every seat, the helm too.
- **Rules** (`ShipAssembler.Check`, `BuildProblem`): a socket that takes the module's tag; a long module along one row;
  level 2 only on roofs that carry load, all at one height; the kind's build height; at most 32 modules. Level 1 is placed
  before level 2. `Sane` makes an untrusted build (a save's, a client's) possible by leaving out what can't be.
- **Composition** (`ShipAssembler.Compose`): the kind's `ShipDef` is copied (so every field another session adds, a drive,
  buoyancy, a rotor's fragile boxes, is carried without the assembler naming it), then each module's seats, doors, parts
  and hull boxes are appended, moved by their socket (sockets don't turn modules); keys become `socket.level.key`. One
  helm: the kind's own, else the first level-1 module's in socket order; any other helm seat rides as a passenger. Mass,
  centre of mass (the moment's sum), inertia (the kind's moved by the parallel axis, each module a solid box of its hull's
  bounds), fuel capacity and water volume add up. Made once per kind and build and kept; `Ship.Def` is the composed one,
  `Def.Kind` and `Def.Build` say what it is made of.
- **Holds.** Each module with a `hold` gets a container on the ship, opened at its part through the shared container verb
  (which now passes the aimed part), kept open by the ship's reach rule, emptied into the wreck.
- **Damage and repair.** `TraceShips` reports the part a box belongs to, blasts reach module bodies by their boxes, and the
  hammer mends hulls through the ship family's one `RepairPart`: a kind's `repair` gives the hull its material and cost
  (the Tin Finch 150, the Iron Wake 3,600 metal fragments: 0.2 a point, 100 a hit after 30 s).
- **Saved and sent.** `ShipCodec` 8 writes the build after the kind (so the layout is known before the seats, doors and parts
  numbered by it) and each hold after the cargo by its module's socket and level; `ShipDetails` carries the build
  (protocol 47) and the client composes the same layout before applying the rest.
- **Not yet:** changing a build at runtime (the lift, V4), engines and their parts (V4), running gear and the ground motor
  (V2), modules on a ship people walk in (refused by the content check for now).

### 9.6 V2 as built (20 October 2026)

- **The motor registry** (`Ships/ShipMotors.cs`): `IShipMotor`, the whole step, by ships.json `motor`: `flight` (today's
  `ShipMotor.Step` and its drive) and `ground` (`GroundMotor`). The server's step and the client's prediction both call
  `def.Motor.Step`. A motor that doesn't fly leaves the flight blocks out of its entry, and it judges its own numbers
  (`Problems`); the skid and drive checks moved into `FlightMotor`. `ShipMotor`'s contact helpers became internal
  statics and the curve carry `CarryOverCurve`, with no change in behaviour.
- **The ground motor** (`Ships/GroundMotor.cs`, §6.2):
  - each wheel a **cylinder cast** of its tyre (radius and width, the tread's shoulders rounded 4 cm; `ShipCollider.CastTyre`)
    from its bump stop down its travel, with a spring, a damper and a stiff bump stop, along the normal where it touches.
    A round tyre meets a kerb's edge in its way and rolls up it, the drive along the edge's slope; the ray it replaced (20
    October 2026) saw nothing ahead of the hub, so the hull met every bump first. What faces less than 0.05 up the body
    (a trunk's side, a wall) is the hull's to meet, not a tyre's;
  - along each loaded tyre:
    - drive (the engines' power over the speed, capped by traction, fading over the last 3 m/s to the top speed);
    - brakes against the motion; engine braking off the throttle (`coast`, a share of the brakes); rolling resistance;
    - held still below 1.5 m/s with no throttle, and by the handbrake on the wheels it holds;
  - across each tyre the sliding is taken out in four passes, the whole within grip × load;
  - the grip acts `gripHeight` up the body, as Unity's wheels' force point does, so the TOGG slides before it rolls in a
    hard turn and a tall build rolls first; the grip across the tyres may act higher (`sideGripHeight`, by default the
    same): the Zamazingo's at its centre of mass, so its balance, not its tyres, sets its lean, while its drive still puts
    its weight on the rear tyre;
  - a two-wheeler (`balance`) has a rider's hands on it: the drive eases off as the front tyre's share of the weight falls
    under a fifth (no wheelies, no loops over backwards), the brakes as the rear's does (no stoppies, no tumbling out of a
    lean);
  - four substeps a tick, the hull swept and pushed out as a ship's, the ground turning under it, rest and sleep.
  - There is no wheel spin in the state: the server's and a client's replays are the same bits.
- **Steering** is in the input: the `car` control scheme turns the wheel toward full lock at 2.5 a second and back at 4,
  so the server and the prediction steer alike. The lock narrows from 34° at a crawl to 8° at top speed.
- **The TOGG** (items 240–242):
  - the kind: 650 kg, four 0.96 m wheels on 0.35 m of travel at 15 kN/m, 4×4, the rear wheels on the handbrake, three
    bays and the nose mount, a fuel cap at the back, the hull mended for 200 metal fragments;
  - two modules: the one-seat cab (220 kg, its seat reached through its door, verb `drive`) and the engine (280 kg,
    90 kW);
  - made with both by default (ships.json `build`);
  - it needs a driver (`needsPilot`): out of the seat, the engine stops and the brakes hold.
- **For the other vehicles:** modules add drive numbers by name (`drive`, summed into `ShipDef.DriveSums`, with `power`),
  read by a drive's `Compose`; a kind's default build is made by `SpawnShip` when a bare kind is spawned.
- **Drawn:** `ShipView` draws a ship with no model as blocks of its hull boxes (steel, a cab pale blue, an engine
  cream) and a tyre and rim at each wheel that ride their springs, steer and roll (`ShipView.Blocks.cs`). The ship
  sandbox takes `--ship togg`: the TOGG on the vehicle workbench's pad, driven with the car scheme; `--scene drive`
  drives it off and turns.
- **Measured** (`GroundMotorTests`, on the game's data with nothing growing on the stand-in ground):
  - 0.04 m from its ride height on four wheels, level;
  - 19.5 m/s after ten seconds;
  - 75° of turn in three seconds at 18 m/s, upright;
  - from 19.4 m/s to rest in 2.2 s and 22 m;
  - reverse; rest and sleep;
  - the same bits twice;
  - 30 m of climb in eight seconds up 20°, none up 42° (it slides back and rolls);
  - 1.1 km round a 2.41 km world on its wheels.
  - On the stand-in's ground with its trees it drives into them and rolls, as it should.
- **Running people over** (`ShipSystem.RunOver.cs`, §6.4): a person whose capsule the hull boxes meet, closing faster than
  `runOverSpeed` (4 m/s), takes (closing − 4)² × `runOverScale` (2) through the characters' receiver, by the driver, and
  is thrown along; nobody in its seats, nobody twice a second. Animals are not run over yet.
- **Not yet:** a moving vehicle is not solid to people below that speed (it passes through them); no hosted drive has been run; the
  42° slope shows the tyres at their limit sliding back rather than holding still under the brakes (μ 1 against
  tan 42° = 0.9), worth a look in play.

### 9.7 In the game, rough (20 October 2026, the owner: "just add them to the game and we will polish them later")

- **Crafted at the Vehicle Workbench**, learned in its tree: the TOGG (200 salvage; 900 metal fragments, 40 refined metal,
  150 wood, 4 gears, 4 pipes, 20 lamp oil, 90 s) and the Zamazingo (125 salvage; 400 metal fragments, 15 refined metal,
  2 gears, 2 pipes, 10 lamp oil, 60 s). They roll out at the workbench's bay as ships do; the admin's `ship <name> togg`
  or `zamazingo` gives one at once. Placeable from the item too (ship placement).
- **The Zamazingo, rough** (item 243): a ground kind on two wheels (0.80 m front, 0.76 m rear), 35 kW, 25 m/s, an open
  saddle (exposed: its rider can be shot), no modules yet (§4's mount points come later). It keeps its balance: a roll
  spring toward the lean that balances its turn (atan of speed × turning over gravity, at most 43°), upright when still
  (`GroundDef.Balance`, §6.3). It has no crash that throws the rider yet.
- **The Zamazingo made rideable** (the owner, 20 October 2026: "not rideable at all … too little power, it gets stuck"):
  its hull's wheel boxes reached to 0.18 m over the ground (`size` is half the box), so a kerb, a rock or a bump stopped it
  dead, and its single rays saw nothing ahead. Its tyres are round now (§9.2), its wheel boxes only their upper halves,
  its grip 1.2 front and 1.3 rear (knobbed tyres), the grip across at its centre of mass (`sideGripHeight` 0.7), no
  wheelies or stoppies. `BikeRideTests` measure it on a test world: 0–50 km/h in 1.6 s (2.2 s before), 87 km/h flat out,
  up a 40° hillside from a standstill (35° before, crawling), over a 25 cm kerb from a crawl (stuck at 15 cm before) and
  a 40 cm one at 8 m/s (stuck at 30 cm before). On Earth in the hosted game (`--tour-steps bike`): 0–50 km/h in about a
  second, 234 m in 20 s weaving across the land, stuck 0.1 s, no damage.
- **Getting in:** E opens the cab's door, E again drives (as the Tin Finch's hatch); the Zamazingo's saddle at once.
- **Drawn as blocks** in the hosted game too (`ShipsView`): the view is made again when a ship's composed layout changes
  (its build arrives after its first sight), and a wheeled ship's wheels ride the ground, roll, and steer by how it turns.
- **Checked:** `GroundMotorTests` 8/8, among them the Zamazingo standing, going (24 m/s), leaning into a right turn
  without falling and stopping upright; and the TOGG over the real network (a loopback server and two clients): the
  server gives it, both clients compose its layout, Ada drives it at 15 m/s with no correction of her prediction, Bo
  sees it go.
- **Still rough:** no lift (modules can't be changed in play yet: each vehicle comes with its default
  build); no engine parts; a slow vehicle passes through people; animals aren't run over; no rider thrown from the bike.

### 9.8 Their art, first pass (V8, 20 October 2026: the owner asked for "AAA models")

Both vehicles are built from code in Blender, as the KA-FA is (no downloaded meshes or pixels), to §7's look and contract,
and replace the blocks:

- **The TOGG** (`art/models/togg.py`): `togg-frame.glb` (ships.json's `model`): a welded ladder frame in black chassis
  paint, a tread-plate deck in the bays and their bolt rails, four corners on double wishbones with red coil-overs, CV
  half-shafts to two differentials and a transfer case, a red fuel drum, a front beam with a winch and a grille guard, round
  lamps, sheet fenders, steps, the battery box and a jerry can; knobbed 0.96 m tyres (the blocks moulded, the sidewall's
  rings and name raised) on cream beadlock steel rims with brake discs. Its modules are their own models, mounted at their
  sockets by the view: `togg-cab-one.glb` (square tube and sea-green sheet, perspex in bolted strips, TOGG stencilled on both
  doors, a cream roof with a rack and spot lamp, a shovel on the back; inside, a bucket seat, the wheel on its column, the
  dash and three gauges, pedals and levers; its left door the `Hatch`, swinging out on its front hinges as door 0 opens)
  and `togg-engine.glb` (an open cream six with a red rocker cover, the air-filter can, six runners and headers to two
  stacks with rain caps, the radiator, fan, belt, hoses and battery). `togg.glb` is the default build, for the item,
  its icon and the placement ghost. Triangles at LOD0: chassis 28.5k, each wheel 7.8k (tyre about 4k, the rest rim and
  disc), cab 15.6k, engine 10k; the whole car 108k, 58k at LOD1 (50 m), 22k at LOD2 (140 m).
- **The Zamazingo** (`art/models/zamazingo.py`, `zamazingo.glb`): a black cradle frame, an air-cooled single (finned
  barrel and head, the header curling to a high silencer), a mustard tank between shrouds with the name across a black
  band, a long ribbed seat, cream plastics (the high fender, the number board with its lamp and a hand-painted 7, the
  tail), a box-section swingarm on a red mono-shock, the chain, wire-spoked wheels on knobbed tyres, drilled discs. 34.8k
  triangles at LOD0 (§7's 25–35k), 18k and 7.6k; with §9.9's bigger tyres 36.2k, 18.8k and 8.0k.
- **The rig** (`ShipView.Rig.cs`): the view poses the model's own nodes for each wheel of the data: `Wheel_<key>` rides its
  spring, its `Steer` turns and its `Spin` rolls; `Arm_<key>_<n>` (wishbones, half-shafts) turn about their pivots so their
  `Tip` follows the hub; `Shock_<key>` points at `ShockLow_<key>`, its `Rod` slides and its `Spring` squeezes; the bike's
  `Fork_front` steers about the raked axis with the wheel sliding up it, and `Swing_rear` carries the rear wheel about its
  pivot. A model without these nodes is drawn by the blocks as before.
- **Lamps** (ships.json): the TOGG's two headlamps and a tail lamp, the Zamazingo's headlamp and tail lamp; their lenses
  are each model's `_Lamps` material, lit with the engine as the other ships' are.
- **Atlases** (`art/models/vehicle_paint.py`): one 2048 px atlas each, painted from noise on the KA-FA's painter, with a
  small stencil font for the names. Shared shapes (tyres, rims, springs, the vehicle frame) are `art/models/vehicle_kit.py`.
- **Checked:** look-dev renders from every side (`-- --preview`), and `art/models/hole_check.py`'s back-face-culled sheets
  of the chassis, cab, door, engine, a wheel and the bike's parts: no holes.
- **Not yet:** the rider's pose on the bike; the bike's side stand down when parked; sounds and effects (§11, V8's rest);
  the other modules' art as they arrive (V5). (The effects are §9.9's; the sounds are the bike session's shared engine model.)

### 9.9 Riding, the engine by hand, the Monster TOGG and the effects (20 October 2026)

The owner's findings on the TOGG, 20 October 2026: "it doesn't even move when I keep jumping in the back of it even though
it has suspensions", "hold E, select engine on but nothing happens ... present in all vehicles", no sounds, and "a monster
version of this with very very high power and bigger tires". Branch `claude/togg-physics`.

- **People on its back (§6.4)** (`ShipSystem.Riders.cs`). A vehicle on wheels keeps its solid body as it moves. People
  standing on its top weigh on its springs, 85 kg each. A landing is a blow and a jump a push, each 0.7 of the person's
  momentum. Walking about moves the weight. Each of these wakes it, and it rests again once its springs settle (GroundMotor
  no longer holds a vehicle still on its wheels while it rocks). The client's physics copy of the body follows the server's
  pose (`ShipModule.MoveBody`, `ShipCollider.MoveParked`), so feet stand on it where it is drawn.
- **ENGINE ON with nobody at the wheel** (`ShipSystem.Engine.cs`). Until now the server dropped it at the next tick: the
  car "needs a pilot". Now it starts the engine by hand (`Ship.HandStarted`), for every kind, and only while the vehicle is
  at rest. It idles with every control let go (on wheels, the handbrake on) and burns its idle fuel. It stops when switched
  off or dry. Someone awake taking the helm takes the engine over.
- **The Monster TOGG** (items 244 and 245, `monster-togg`). It is crafted at the vehicle workbench in 150 s, and its tree
  node (600) comes after the TOGG's. It is the TOGG's chassis lifted 0.75 m on long-travel arms (0.6 m of travel), over
  1.7 m tyres on a 2.84 m track, with a 1.73 m deck. Its engine is the blown six (`monster-togg-engine`), 450 kW, five
  times the TOGG's. It weighs 1,300 kg, its top speed is 34 m/s, it has four-wheel drive and its fuel holds 160. Measured:
  0 to 100 km/h in about 3 s, 120 km/h at the top, and it stands level. Every number is a proposal for the owner. The
  model is `art/models/togg.py -- --monster`, which leaves the TOGG's own files as they were. It wears the TOGG's cab,
  atlas and materials. Its own parts are flotation tyres with deep lugs on 8-stud beadlocks, coil-overs with remote
  reservoirs, a step ladder each side, no fenders, and the six with a supercharger, two carburettors under a scoop and
  taller stacks. It has 110k triangles at LOD0. The hole check (`art/models/hole_check.py`, back faces culled against
  magenta) of its engine, wheel, ladders, arms, knuckle and coil-over showed no holes.
- **Effects** (`ShipsView.Vehicles.cs`, fx.json `exhaust.*`, `dust.tyre`, `spray.tyre`). They are drawn within
  220 m of the camera:
  - **Exhaust.** Every `Exhaust_<n>` point of the model, at the top of each stack (`ShipView.Exhaust.cs`), has two loops:
    a thin blue-grey haze at idle (`exhaust.diesel`) and black soot under load (`exhaust.soot`). Soot comes in within
    0.12 s and clears over 0.7 s. A black puff (`exhaust.puff`) comes on starting, on a stab of the throttle (a rise of
    0.45 over its last 0.3 s, at most one each 0.8 s) and at a gear change. The engine-free rule is
    `Hollow.Client.Effects.VehicleSmoke` (`VehicleSmokeTests`), fed plain inputs: running, throttle, load and shifted.
    Until the shared revs and gear model (`Hollow.Client.Vehicles.VehicleRevs`, the bike session's) lands, the load is the
    throttle and there are no gear changes. Wiring its `Load` and `Shifted` in is one line in `VehicleEffects`.
  - **Tyre dust.** Each tyre on the ground throws dust behind it, by its speed (from 1.2 m/s, full at 15 m/s) and by
    wheelspin pulling away (hard throttle under 6 m/s). The dust is tinted by the ground under it (contact-responses.json
    `dust`, looked at again every 0.5 s), with grit and gravel, and scaled by the tyre's radius over the TOGG's.
  - **Spray.** A tyre in water up to 1.5 of its radius deep throws a rooster tail of drops and mist from the surface.
  - **The effects API** gains `FxDirector.SetDirection` and `SetTint` for loops: a tyre's dust follows its way and the
    ground under it.
- **Righting** (the owner, 20 October 2026: "when it kind of tips over it can come back"). The monster lifted onto two
  wheels in a fast full-lock turn. Rather than never tipping, a vehicle may right itself: ships.json ground `righting`
  (rad/s² per radian) and `rightingDamping` (per rad/s), the monster's 40 and 6. Past 7° off the ground under its tyres
  (or the planet's up with no tyre down), the ground motor springs its roll and pitch back, at most 30 rad/s², damped while
  tipped (`GroundMotor.Righting`, the same on the server and in the client's prediction). Set down 60° or 150° over, it is
  upright after 6 s.
- **Lighter dust.** A bigger tyre's dust grows at most 1.25 times the TOGG's and comes no more often. The puffs are
  fewer and shorter-lived, culled at 160 m: about a seventh of the first pass's see-through dust on the monster.
- **The lag at speed** (the owner: "very unoptimized, it makes it laggy") was the grass, not the monster: changing a grass
  tile's level of detail stalled on the GPU, and a vehicle over about 20 m/s crossed enough tiles to stall every frame
  (docs/WALKING_GROUND.md §18.4). Fixed there; pulling away to 33 m/s now runs at 19–21 ms a frame.
- **Not yet:** seeing a vehicle's parts at the workbench; the owner's look at the monster, its righting and the effects.

### 9.9 The rear wheel, the bike's tyres and the sound (V8, 20 October 2026: the owner's playtest)

The owner rode the Zamazingo: "back tire doesnt spin, sounds are not made and I think tires needs to be bigger for offroad";
and of the TOGG, its tyres don't turn and it should sound like a heavy diesel.

- **Wheels that didn't turn:** Blender keeps object names unique, so a model's second wheel's `Steer` and `Spin` (and its
  arms' `Tip`, its dampers' `Rod`, `Spring` and `SeatLow`) were exported as `Steer.001`, `Spin.001`, which Godot imports as
  `Steer_001`. The rig found only the first wheel's: the bike's rear and three of the TOGG's four wheels neither rolled nor
  steered, and most of the TOGG's arms and dampers stood still. `ShipView.Rig.cs` now finds a part by its name with any such
  number after it, so every model's rig works without renaming.
- **Bigger tyres on the bike** (§4): 0.90 m front and 0.86 m rear (were 0.80 and 0.76), 0.15 and 0.20 m wide (0.12 and
  0.16), the rims as they were so the sidewalls are taller, the knobs taller (22 and 26 mm). The bike stands 0.10 m higher
  (the origin stays on the ground under the rear axle; `LIFT` in `zamazingo.py`), so under the seat it keeps its room at
  full bump; the seat is at 1.05 m and the wheelbase 1.544 m. Around the wheels: the fork's legs out to 0.13 m either side
  with an axle and spacers, a fender curving 3 cm over the tyre at full bump, the down tube and cradle drawn back, the
  header under the crankcase and the silencer out past the tyre, a swingarm flaring from 0.10 m at the pivot to 0.155 at the
  axle, the chain line at 0.13 m, the airbox forward between the carburettor and the shock. Posed at full bump, full droop
  and full lock, no tyre touches anything; the hole check's sheets show none. ships.json's wheels, hull boxes, seat, eye,
  fuel cap, lamps and centre of mass follow.
- **The sound** (§7.1) as built:
  - `art/audio/engines.py` makes everything in code, as the KA-FA's and the motorboat's sounds are (no CC0 recording on
    this PC is an engine). The single's loops are at 1,450 (idle), 2,400, 3,600, 5,000, 6,600 and 8,600 rpm; the six's at
    750 (idle), 1,050, 1,450, 1,950, 2,500 and 3,000. The tyres are grains of the CC0 footstep recordings already used for
    footsteps (NOX's surface banks, BigSoundBank's gravel and water) strewn under the tread's hum and roar, at 10 m/s.
  - `art/audio/vehicle_sounds.py` holds the events (`veh.*`, files under `game/assets/audio/vehicle/`), each kind's
    `engines` entry and its `start` and `shutdown` roles. `python -I art/audio/build.py --only vehicle` makes
    them; sounds.json's `engines` section is written from it (tune the numbers there, not in sounds.json).
  - `Hollow.Client.Vehicles.VehicleRevs` is the gearbox: one per vehicle drawn (`ShipsView`, `RevsOf(netId)` for the HUD and
    the exhaust).
  - `game/scripts/Ships/VehicleSounds.cs` plays it: the engine's loops by revs and load (the loops come in as the start's
    run-up ends), the turbo, gear changes, the tyres by the ground under the vehicle (looked up four times a second), skids,
    the suspension's knocks and creaks from the rig's wheels (`ShipView.SuspensionBump`, `SuspensionWork`,
    `SuspensionBottomed`), a landing only after a quarter of a second in the air (as hard as it fell: the wheels leave and
    meet the ground all the time, so the ships' `touchdown` role isn't a vehicle's), a splash.
  - Checked by `VehicleSoundTests`: the gearbox changing up and down, slipping its clutch, free-revving in the air,
    reversing and running down, a turbo spooling and its release; and every ground vehicle kind's entry (its gears reach its
    top speed, its loops span idle to redline and are loops, tyres on every ground, its start and stop, its suspension's
    knock, creak and landing, and no `touchdown` role).

### 9.10 The TOGG as a heavy truck: overboost, its engine rocking, black smoke (21 October 2026)

The owner, 21 October 2026, asked for the TOGG's diesel to be "super AAA, as if this was a trucking game". That means turbo
sounds, the engine "shaking its head from the torque", heavy black smoke and proper tractor-like diesel sounds when Left Shift
is pressed, gear changes and the turbo spooling. They chose an **overboost** while Shift is held: about +40% power for about
twice the fuel, heavy black smoke, the turbo screaming and the engine rocking harder, with no time limit. They also chose
**recorded CC0 truck sounds** over synthesized ones.

- **Overboost** (`GroundMotor.Overboosting`, `DriveForce(…, boosting)`). The car control scheme sends `ShipButtons.Boost`
  while Left Shift is held (its hints: SHIFT boost). The motor overboosts while the engine runs, the throttle is forward and
  the vehicle can overboost. The power is then `ShipDef.BoostPower` and the drive's traction cap rises by the same factor; each
  tyre's grip still caps its wheel. The fuel burns `FuelBoost` times faster (ships.json `fuel.boost`, the flight boost's own
  field). The ship's `Boosting` flag is set, which goes on the wire as `ShipNetFlags.Boosting`, so every client sees it.
  There is no timer: it lasts while Shift is held. The server's step and the client's prediction call the same motor with
  the same input, so a replay gives the same bits.
- **As data**, not code per vehicle. An engine module's `boost` (modules.json, 1 … 3) is its power's factor overboosted, and
  a kind with its own power takes `ground.boost`. The assembler adds them as it adds the power:
  `BoostPower = Σ power × boost`. The TOGG's and the Monster TOGG's engines are 1.4 (90 → 126 kW, 450 → 630 kW). Both kinds'
  fuel blocks have `boost` 2.0. The Zamazingo has none, so Shift does nothing on it.
- **Measured** (`VehicleBoostTests`): the TOGG reaches 15 m/s in 1.73 s overboosted, against 2.13 s without, and covers
  134 m against 128 m in eight seconds. Its top speed is unchanged: 20 m/s is a cap, not a power limit. Below about 10 m/s
  the tyres' grip, not the power, limits the pull. Over ten seconds flat out it burns 1.6 units of fuel against 0.8, and
  after a minute held it is still overboosting. The same boosted inputs give the same bits. Over the loopback network,
  Ada's prediction overboosts with no correction, and Bo sees the flag.
- **The revs** (`VehicleRevs.Step(…, boosting)`, `Overboost` 0–1, in over 0.25 s and out over 0.35 s). Overboosted, the
  wastegate is held shut: the turbo reaches its full boost from lower revs (the turbo's floor falls from 0.3 to 0.15 of the
  redline) and spools 45% faster. Letting go of Shift on the throttle is a release, like lifting off the throttle. The
  sounds play the turbo 6 dB louder and pitched up by 0.18 overboosted, the release 3 dB louder, and the whole engine
  2.5 dB louder.
- **The engine rocking under torque** (`Hollow.Client.Vehicles.EngineRock`, `ShipView.Rock.cs`). This is presentation
  only. The engine module's model (the first module with power) leans about its crank, the vehicle's length through the
  module's centre of mass. It rides a spring and damper of about 2.5 Hz toward 2.6° (0.045 rad) at full load, 1.7 times
  that overboosted. So a stab of the throttle lurches it past its lean before it settles, and a gear change (the load let
  go and taken up again) swings it back. At idle it shakes with a diesel's lope (two beats from the revs, roughest at
  idle, 2.2 times harder overboosted). Standing still, a rise in the revs (a free rev, or the throttle on the handbrake)
  twists the whole model about its length at the hubs' height, up to 0.8°, as a heavy flywheel does. The exhaust stacks
  are the engine's, so the smoke rocks with it. The hull, the seats and the contacts don't move. Measured
  (`TruckFeelTests`): standing at idle with a shake of 0.005 rad; a stab lurching to 0.056 rad before settling at 0.045;
  0.076 overboosted; a gear change swinging it back 0.063; a free rev twisting the chassis 0.51° standing and 0.03°
  rolling.
- **Black smoke** (`VehicleSmoke.Black`, fx.json `exhaust.black`). Overboosted, each stack pours a heavy column over its
  soot, by the overboost and the load (0.35 + 0.65 × load). It comes in as fast as the soot and clears over 1.1 s. It is
  made of fewer, denser puffs rather than many thin ones (16 + 4 a second, with a spark now and then), because
  see-through smoke is what costs a frame its time. A gear change overboosted throws a puff 1.6 times as big.
- **Tour step** `--tour-steps truck` (`GroundTour.Truck.cs`; `HOLLOW_TOUR_KIND=monster-togg` runs it on the monster;
  `HOLLOW_TOUR_NO_FX=1` runs it without the effects). It idles, revs on the handbrake, pulls away flat out (timed), brakes,
  pulls away overboosted (timed), and lets go of Shift. It prints the revs, gear, turbo, overboost, the engine's lean, the
  chassis's twist and the fuel at each stage.

## 10. Crafting and progression

Everything is learned in the **Vehicle Workbench tree** (R6) and crafted at that workbench, except tier-3 parts and rare
engines (found blueprints, R7). Starting salvage costs, for tuning:

| Node | Salvage | Needs |
|---|---:|---|
| Vehicle lift | 150 | — |
| TOGG chassis | 200 | Lift |
| Zamazingo frame | 125 | Lift |
| Car tyre, bike tyres | 50 | Chassis or frame |
| One-seat cab | 75 | Chassis |
| Two-seat cab | 100 | One-seat cab |
| Engine module, single | 125 | Chassis or frame |
| Tier-1 parts (five) | 25 each | Engine |
| Tier-2 parts (five) | 100 each | Tier-1 part of the kind |
| Storage, camp deck, bench, fuel tank | 50–75 | Chassis |
| Camper shell | 200 | Camp deck |
| Rack, sidecars, long-range tank | 50–100 | Frame |
| Track units | 250 | Engine module |
| Snow kit | 200 | Single |

Recipes use what the game has: metal fragments, refined metal, wood, cloth, rope, rubber if it comes (open: the game has no
rubber; tyres could be cloth and salvage until it does). Tier-1 parts and the odd damaged tyre turn up in loot at the roadside
sites (Stalled Crawler, Container Spill, Fuel Depot) and the Scrapyard.

**Vehicles in the world.** Rust spawns damaged cars along its roads. Earth has no roads yet (WORLD_TERRAIN_PLAN.md, step 8);
wrecks to recover belong with the roads, later.

## 11. Packages

| | What | Checked by |
|---|---|---|
| V0 | This document; the owner's answers to §12 | — |
| V1 (done, §9.5) | The module core: modules.json, sockets, the assembler, holds, the build saved (codec 8) and on the wire (protocol 47), the hull mended with the hammer; **shared** with the minicopter. The motor registry moves to V2 | `ModuleTests` (7) |
| V2 (driving and run-overs done, §9.6) | The ground motor and the TOGG on its wheels, drawn as blocks in the ship sandbox (`--view ship --ship togg`); people run over. Left: a moving vehicle solid to people, animals run over, a hosted drive | `GroundMotorTests` (6); the sandbox pictures |
| V3 | Seats, the car and bike control schemes, prediction of passengers, the HUD; the hosted game | Network tests; a hosted drive with a passenger |
| V4 | The lift: item, power, the vehicle screen, locks; engine parts and tiers; crafting and the tree | `Lift`; `ContentTests`; the screen in `--view ui --screen vehicle` |
| V5 | The car's modules and stacking; build zones aboard (beds and bags as respawn points, boxes); decay outside claims | `Modules`, `Decay`, `Respawn`; a camp built and driven |
| V6 | The motorbike: frame, lean, crash, sidecars | `Bike` |
| V7 | Tracks and the snow kit | `GroundMotor` slopes and snow |
| V8 (models done, §9.8) | Art: the look sample first (§7), then the kit; sounds (engine by load and speed, tyres by ground, suspension), effects (dust, mud, exhaust, lamps) | The owner's approval; pictures |
| V9 | Bots driving, a persistence soak, balance | The load test |

The look sample (V8's first step) can start in parallel with V1–V3: it needs only §7's contract.

## 12. Questions for the owner

1. **One car length or three?** This design has one chassis, as asked ("one modular car"): three bays and a nose mount, which
   hold every example the owner gave. Rust has 2, 3 and 4 slots; other lengths would be data later.
2. **The bike's crash**: thrown off and hurt above 8 m/s (Rust-like, proposed), or more forgiving?
3. **Rare engines**: which ones, and from which monuments (a later content pass)?
4. **Decay's zone** (§5.4): does any tool cupboard's zone stop a vehicle's decay, or only one its owner or crew is authorised on?
