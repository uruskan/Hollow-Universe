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
| Wheelbase | 1.52 m |
| Tyres | **0.80 m** front (0.53 m rim), **0.76 m** rear (0.46 m rim), knobbed: an enduro's, not a scooter's |
| Seat height | 0.95 to 1.0 m; 0.32 m under the frame; 0.28 m of travel |
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

- **The standard hammer**, as on building pieces: aim at the damaged thing, hit, and each hit mends a share of its health for
  its share of the cost, taken from the inventory. Anywhere: on the road, in a base, on the lift (where REPAIR ALL does every
  hit at once for the sum).
- **What it mends**: every module, the chassis or frame, and the running gear (a flat tyre comes back up). Engine parts are
  not mended: they wear and are replaced (§5.2).
- **The cost depends on what is repaired**, from each one's data (the ship parts' `RepairMaterial` and `RepairFull`: the
  material and the cost of a full repair). Starting values: **metal fragments** for the chassis, the frame, cabs, decks, the
  camper shell, lockers, the bench, fuel tanks, tyres and the bike's modules; **refined metal** for engine modules, track
  units and, later, the armoured cab.
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
rate-limited, and turns the body about its contact line; the tyres' forces act as if it were upright. Only a crash (§4) frees
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
size (the sheet's), so the driving can be tuned without the art.

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
| V1 | The module core and the motor registry: modules.json, the assembler, the build in the state, codec 7 and the network; **shared**, built by whichever of the three sessions starts first | `Modules` tests |
| V2 | The ground motor on a fixed build of blocks: driving the baked Earth in `--view vehicle`; moving bodies; run-over. First, `ShipMotor`'s shared helpers (gravity and the curvature turn, the hull's sweep, crash damage, sleep) pulled out as statics: the drive seam (98621ac7 on `claude/motorboat`) leaves them inside the step | `GroundMotor`, `RunOver`; the sandbox tour |
| V3 | Seats, the car and bike control schemes, prediction of passengers, the HUD; the hosted game | Network tests; a hosted drive with a passenger |
| V4 | The lift: item, power, the vehicle screen, locks; engine parts and tiers; crafting and the tree | `Lift`; `ContentTests`; the screen in `--view ui --screen vehicle` |
| V5 | The car's modules and stacking; build zones aboard (beds and bags as respawn points, boxes); decay outside claims | `Modules`, `Decay`, `Respawn`; a camp built and driven |
| V6 | The motorbike: frame, lean, crash, sidecars | `Bike` |
| V7 | Tracks and the snow kit | `GroundMotor` slopes and snow |
| V8 | Art: the look sample first (§7), then the kit; sounds (engine by load and speed, tyres by ground, suspension), effects (dust, mud, exhaust, lamps) | The owner's approval; pictures |
| V9 | Bots driving, a persistence soak, balance | The load test |

The look sample (V8's first step) can start in parallel with V1–V3: it needs only §7's contract.

## 12. Questions for the owner

1. **One car length or three?** This design has one chassis, as asked ("one modular car"): three bays and a nose mount, which
   hold every example the owner gave. Rust has 2, 3 and 4 slots; other lengths would be data later.
2. **The bike's crash**: thrown off and hurt above 8 m/s (Rust-like, proposed), or more forgiving?
3. **Rare engines**: which ones, and from which monuments (a later content pass)?
4. **Decay's zone** (§5.4): does any tool cupboard's zone stop a vehicle's decay, or only one its owner or crew is authorised on?
