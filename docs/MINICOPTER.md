# The KA-FA 1500: our minicopter

Status: **M1–M4 built, 20 October 2026** (§13), on `claude/minicopter`. The owner named it the **KA-FA 1500**; every
other default in §2 waits for the owner's yes or no, and the look (§10) for the owner's approval. Work happens on the branch `claude/minicopter` (worktree `.claude/worktrees/minicopter`). Two other
sessions are building vehicles at the same time, and the three share two seams instead of each inventing its own:
- **The drive** (the boat session, commit 98621ac7, merged here): `ShipMotor`'s thrust-and-turn block sits behind
  `IShipDrive` (`Ships/ShipDrives.cs`), chosen by ships.json's `drive.kind`. The Tin Finch keeps `thrusters`, the boat
  adds `propeller`, and the KA-FA adds `rotor`. Control schemes are chosen the same way (ships.json `controls`,
  `game/scripts/Ships/ShipControlSchemes.cs`).
- **Modules** (the car session, `docs/VEHICLES.md` §9): a vehicle kind declares sockets and a module is an item. An
  assembler composes the kind and its installed modules into one cached build: mass, inertia, centre of mass, seats,
  parts, hull, and the drive's numbers. Engines are data, with typed slots for engine parts. A build changes only at a
  "service" station: the lift for cars, and for aircraft the same block on the Vehicle Workbench's bay. The module core
  (VEHICLES.md's V1) waits for the owner's go on that design.

## 1. What the owner asked for

On 20 October 2026:

> we need to add rust's minicopter equilavent (in our own style & design & modularity/craftability) to our game.

and, the same day, "name the minicopter ka-fa 1500". Read with DESIGN.md:
- **Rust's minicopter** (§3) is the reference for what the vehicle does: a cheap, light, fast rotorcraft for two people,
  the first thing most players fly.
- **Our own style** (ART-01, LORE.md §4): player-made and visibly handmade. The Tin Finch is the measure: a real machine,
  built as if in a scrapyard.
- **Modularity** (SHIP-01, CRAFT-07): Rust's modular cars are the confirmed reference for vehicle modules. A learned and
  crafted rare engine makes a vehicle better without changing what it is (DESIGN.md's Mohandis-engine example).
- **Craftability** (CRAFT-01, CRAFT-04): vehicles are unlocked in the Vehicle Workbench's tree and built at a Vehicle
  Workbench. Rust sells its minicopter at a vendor; ours is built.

## 2. Decisions (defaults the owner may change)

| # | Question | Default | The other way |
|---|---|---|---|
| M1 | Name | **KA-FA 1500** (the owner's). Data key `ka-fa-1500`. | — |
| M2 | Feel | Rust's: it hovers by itself, and it moves by tilting its rotor, nosing down to go and flaring to stop. Ours holds its height exactly with no key held; Rust's sinks slowly. | Rust's slow sink. |
| M3 | Controls (§5.2) | Rust's keys: **W climbs, S (or Ctrl) sinks, A/D yaw, the mouse tilts it** (forward and back pitch, left and right roll). The mouse moves a stick that stays where it is put, so a cruise needs no fight with the mouse. The middle button centres it, and a small tilt left for half a second levels itself, as Rust's does. Space leaves the seat, Alt looks round, V switches chase and cockpit. | A keyboard scheme (W/S pitch, A/D roll, mouse yaw, Shift/Ctrl climb), as a setting later. |
| M4 | Where it flies | In the air only. Its lift falls as the air thins, so on Earth it tops out at 1.35 km over the sea (measured): above the highest peaks (400 m), well below the leaving height (2.4 km). It can't reach orbit. On a world with thinner air it struggles, and with none it can't fly. | Rust's ceiling: a fixed 300 m over the ground under it. |
| M5 | What it is made of (§4) | A **frame** (the vehicle) with five sockets: **engine** and **rotor** (both needed to fly), **tank**, **rear bay** (a passenger seat or a cargo box), **plating**. Every module is an item, crafted and swapped like Rust's car modules (VEHICLES.md). | A fixed craft (Rust's). |
| M6 | Engine parts | The KA-FA's engine is a **flat twin** with the car's part kinds in fewer slots (crankshaft 1, carburettor 1, pistons 2, spark plugs 2). Parts are shared with every vehicle, and their tiers set its power, fuel use and wear (VEHICLES.md §5.2). | An engine with no parts. |
| M7 | Where modules are swapped | Only at a service station: the Vehicle Workbench's bay, by someone allowed there. In the field only fuel, the cargo box and engine parts (VEHICLES.md §5.3). | — |
| M8 | Rare variant (CRAFT-07) | The **tuned twin**: a monument blueprint, not in the tree. More base power for the same parts, so a heavy build still climbs. | Also a rare rotor; later. |
| M9 | Fuel | Lamp oil (our low-grade fuel). Rust's minicopter holds 500 and burns 0.5 a second while the engine runs. Ours: a **jerry tank of 250** or a **long tank of 500**, burning 0.25 a second at idle up to 0.5 at full collective. | Rust's flat 0.5. |
| M10 | Durability (§7) | A 750 hull (Rust's). Crashes hurt as Rust's do: any hit over 5 m/s costs at least 15% of the hull, 30 m/s is fatal, and five times as much lying past 60°. Plating adds 250 hull. A rotor strike (the blades into a tree, rock or wall) breaks the rotor, and the craft falls. | — |
| M11 | Seats | The pilot's seat is part of the frame. The rear seat module adds one passenger, who can use what they hold (Rust's), once seated item use exists (§11). Both seats are open, so both can be shot. | — |
| M12 | Leaving in flight | Allowed, as in Rust. Its engine stops a second after the pilot leaves, and it falls. | Only once landed, as the Tin Finch. |
| M13 | Lock | The owner's lock keeps strangers out of both seats and the cargo box, as the Tin Finch's does. Rust's minicopter has none. | No lock, as Rust. |
| M14 | Decay | The owner's ruling for vehicles (VEHICLES.md R13, 20 October 2026): vehicles decay outside a tool cupboard's claim and not inside one. The rate proposed there is Rust's: 216 minutes from whole to nothing, starting 10 minutes after anyone last sat in it. Repaired with the hammer and metal fragments. | — |
| M15 | Getting one (§6) | Unlock the frame and the standard modules in the Vehicle Workbench's tree, and build at the bench. A first flying machine for an early group, before any spacecraft. | — |
| M16 | Art (§10) | Our own model, built from text by `art/models/ka_fa_1500.py`. Each module is its own model, mounted at its socket. | — |
| M17 | Sound | Synthesised in `art/audio`, like the Iron Wake's machinery: the blades' chop at the rotor's rate over the twin's note, rising with the spool and the collective. | — |

## 3. Rust's minicopter (the reference)

Studied 20 October 2026. Facepunch's own posts, the 2019 decompiled code, plugin sources that read its fields, and the
wiki pages that could be reached. Numbers from the 2019 code may have changed since.

| Topic | Rust |
|---|---|
| Keys | W throttle up and climb (it also starts the engine), S throttle down, A/D yaw, mouse Y pitch, mouse X roll. Space leaves, even in flight. Alt looks round; otherwise the pilot can't look round or use anything. |
| Engine | Starts 5 s after W with fuel. Dies with no fuel, when waterlogged (and won't restart), or 1 s after the pilot leaves. |
| Lift | A hover force of 0.99 of its weight along its up, so with no throttle it sinks slowly. It fades to nothing between 0° and 41° of tilt. The throttle adds lift, and when tilted it adds forward thrust (full at 32° of tilt). Self-levels 0.5 s after the last input. No autorotation: out of fuel it falls. |
| Ceiling | Lift fades over the last 20 m below the ground under it plus a service ceiling (200 to 300 m). |
| Fuel | Low-grade fuel. A 500 tank burns 0.5 a second while the engine runs, idle or not: 16 min 40 s. Vendor copies come with 100. |
| Health | 750 (1,000 at launch). One C4, two rockets, three HV rockets, about 300 rifle bullets. Immune to fire. |
| Crashes | Over 5 m/s: max(InverseLerp(5, 30, speed), 0.15) of the hull; five times that when tilted past 60°. |
| Destroyed | Everyone in it dies. An explosion, gibs and fireballs; the fuel is lost. |
| Seats | Pilot and one passenger. The passenger can use any item; the pilot can't. No storage but the fuel tank. |
| Getting one | Bought from a vendor (750 scrap), not craftable, no world spawns since 2020. |
| Repair, decay | Hammer and metal fragments (250 from empty to full). Decays 8 h outside, 48 h inside, 10 min after last use. No lock. It can be pushed like a rowboat. |
| Other | Dust under it. Its health bar is shown only to the people in it. SAM sites fire at it, and a lock-on warning plays. |

Sources:
- Facepunch: rust.facepunch.com/blog/february-update, /news/qol-update, /news/airborne, /news/maintenance, /news/pivot-or-die.
- Code: unet-dev/Decompiled-Assemblies (BaseHelicopterVehicle.cs, MiniCopter.cs, June 2019), WheteThunger/MiniCopterOptions.
- Wikis and guides: rusthelp.com/world/minicopter, corrosionhour.com (minicopter guide and commands), rustafied.com
  (2019 and 2020 updates), rustwiki.su/Minicopter.

## 4. The KA-FA 1500 as an assembly

### 4.1 The frame

The frame is the vehicle: a ships.json kind with a hull, skids, the pilot's seat, a fuel cap, lamps and five sockets. It
weighs 250 kg. Without an engine and a rotor it is a parked frame people can sit in but not fly; E at the pilot's seat
says why (NO ENGINE, NO ROTOR: new outcome codes).

| Socket | Takes | Needed to fly | Where |
|---|---|---|---|
| `engine` | flat twin, tuned twin | yes | Behind the seat, under the mast; a belt drives the mast |
| `rotor` | rotor | yes | On the mast |
| `tank` | jerry tank, long tank | no (no tank, no fuel) | Under the floor |
| `rear` | rear seat, cargo box | no | On the platform over the tail boom's root |
| `plating` | plating | no | Round the cockpit's floor and the engine |

### 4.2 The modules (proposals)

| Module | Mass | What it adds | Health |
|---|---|---|---|
| Flat twin | 95 kg | Rotor power: lift 9.6 kN at sea level and full spool with tier-2 parts. Part slots: crankshaft 1, carburettor 1, pistons 2, spark plugs 2 | 300 |
| Tuned twin (rare) | 90 kg | Lift 11.5 kN with tier-2 parts; the same slots | 300 |
| Rotor | 45 kg | The blades and hub: without it no lift. Broken by a rotor strike | 200 |
| Jerry tank | 30 kg | Fuel 250 | 150 |
| Long tank | 55 kg | Fuel 500 | 200 |
| Rear seat | 20 kg | A second, open seat facing out to the right | — |
| Cargo box | 35 kg | One container of 12 slots, its single storage place (SHIP-03) | — |
| Plating | 110 kg | Hull +250; the pilot's legs sheltered | — |

A KA-FA with a twin, a rotor and a jerry tank weighs 420 kg; with a pilot, 500. At sea level its 9.6 kN lifts twice that.
A full one (long tank, rear seat and passenger, plating) is about 780 kg and lifts 1.25 times its weight: it still
flies, but it climbs and stops slowly. The tuned twin is for exactly that load. With tier-1 parts the lift is 0.82 of
tier-2's (VEHICLES.md §5.2's power factors).

## 5. Flight: the rotor drive

### 5.1 State

The rotor's **spool** (0 to 1: how fast it turns, as a share of flying speed) is the one new number in `ShipState`. With
the engine running it rises to 1 over 5 s (Rust's start delay); stopped, it falls over 7 s. The rotor's lift is
`lift × spool² × air`, where `air` is `(ρ / 1.225)^0.75`, capped at 1.3. The rest is the shared step: skids, the hull's
sweep, crash damage, the sea, sleep, the ground turning under it.

### 5.2 Controls

| Key | Does | `ShipInput` |
|---|---|---|
| W / S or Ctrl | Climb at up to 7 m/s / sink at up to 6 m/s; neither holds the height | `Vertical` |
| Mouse forward / back | Nose down / up, up to 30° (the stick stays where it is put) | `Pitch` |
| Mouse left / right | Roll left / right, up to 28° | `Roll` |
| A / D | Yaw at up to 100°/s | `Yaw` |
| Middle button | Centre the stick (level) | — |
| Space | Leave the seat, in flight too (M12) | — |
| Alt | Look round | — |
| V | Chase or cockpit camera | — |

The flight keys fill `ShipInput`'s existing fields, so the controls need nothing new on the wire.

### 5.3 How it flies (one tick)

1. **Spool**: as §5.1. The engine runs while someone is at the controls (M12), there is fuel and it isn't flooded.
2. **Attitude**: the stick's pitch and roll become a wanted tilt of the rotor, measured from the ground's up along the
   craft's heading. The craft turns toward it at up to its rates (pitch 100°/s, roll 120°/s) and accelerations. With the
   stick centred, the wanted tilt is level, so it levels itself.
3. **Yaw**: A/D ask for a yaw rate. At speed the tail's fin turns the nose into the air it flies through (weathercocking),
   so a roll becomes a turn, as in Rust.
4. **Collective**: W and S ask for a vertical speed, and neither asks for zero. The rotor gives the thrust that reaches it
   in 0.4 s, counting the tilt, gravity and drag, up to what it has. Near the ground with S held, the descent eases to
   1.5 m/s plus 0.25 m/s per metre of height, as the Tin Finch's landing assist does.
5. **On its skids** with W not held, the rotor stays flat: the craft sits, and the stick doesn't tip it.
6. **Thrust** is along the craft's up only. It goes forward by tilting, so its top speed is where the tilted rotor's pull
   meets the drag. Past 60° of tilt the rotor's pull is useless and the collective lets go.

### 5.4 Numbers (proposals, tuned in play)

| | KA-FA 1500 (twin, rotor, jerry tank, pilot: 500 kg) |
|---|---|
| Inertia | 650 / 820 / 330 kg·m² (pitch, yaw, roll) |
| Lift | 9.6 kN at sea level and full spool (twin, tier-2 parts) |
| Spool | 5 s up, 7 s down |
| Attitude | 30° pitch, 28° roll; rates 100°/s pitch, 120°/s roll, 100°/s yaw |
| Vertical | climb 7 m/s, sink 6 m/s |
| Drag areas | forward 4.5 m², side 6.0, top 9.0 (an open frame, its pilot and the rotor's disc edgewise) |
| Weathercocking | 2.5 rad/s of yaw per unit of sideslip, measured against the heading, from 3 m/s ahead to its whole at 15 |
| Top speed | about 30 m/s (108 km/h) level at full tilt |
| Crash | Rust's rule (M10); the pilot takes a tenth |
| Fuel | 0.25/s idling to 0.5 at full collective |

`RotorTests` measures what these give (M1, 20 October 2026, Earth's air and gravity on level ground):

| Measured | |
|---|---|
| Lift-off | 4.1 s after the engine starts with W held (spool 0.71); then 7.0 m/s up |
| Hover | no drift in 10 s; the collective at 0.49; 0.37 lamp oil a second |
| Full tilt | 29.9° in 2 s; 32 m/s (116 km/h) after 42 s; the height held within 2 cm |
| Let go | from a 28° roll, level within 2° in 0.97 s |
| Yaw | 100°/s; a 4 s banked turn at full speed turns the nose 30° |
| Ceiling | 1,348 m (the leaving height is 2,400 m), from 1 km in 63 s |
| Left in flight at 30 m | down in 1.6 s; the hull at 521 of 750 |
| Landing with S from 25 m | 6.2 s, unhurt; sitting, the stick doesn't tip it |
| Rotor strike | the spool to 0, the hull −35% |
| Ditched in the sea | the engine drowns; no climb |

### 5.5 Rotor strike

The rotor's disc is a thin hull box of its own (two crossed boxes, `rotor: true` in the hull list). A contact on it while
the spool is over 0.2 breaks the rotor module (its health to 0), and with no rotor there is no lift.

## 6. Getting one

| Recipe (Vehicle Workbench) | Ingredients (proposals) | Tree cost |
|---|---|---|
| KA-FA 1500 frame | 500 metal fragments, 30 refined metal, 150 wood | 150 salvage |
| Flat twin | 250 metal fragments, 20 refined metal, then its parts (VEHICLES.md §5.2) | 100 |
| Rotor | 200 metal fragments, 10 refined metal, 100 wood | 75 |
| Jerry tank | 100 metal fragments | 25 |
| Rear seat | 75 metal fragments, 30 cloth | 25 |
| Cargo box | 150 metal fragments, 100 wood | 25 |
| Long tank | 200 metal fragments, 10 refined metal | 75 |
| Plating | 400 metal fragments, 30 refined metal | 100 |
| Tuned twin | 300 metal fragments, 40 refined metal, and its blueprint | not in the tree (monument loot) |

The frame is placed on the bench's bay (the car's frame goes on the lift the same way). Modules are items made at the
bench. The KA-FA's branch of the tree comes before the Tin Finch's: a group flies a KA-FA before it builds a spacecraft.

## 7. Seats, damage, the wreck

- **Seats:** the pilot's (E: FLY) and the rear seat's (E: SIT). Both are open, with no door, and both can be shot: a
  seated player stays in the hit history, as the Iron Wake's exposed gunners do.
- **Damage:** the hull takes melee at 0.25, arrows 0.1, bullets 0.5 and blasts 1, as the Tin Finch's. A blast also
  reaches the modules near it, so a rocket can break the rotor.
- **Destroyed:** everyone aboard dies (Rust's). It burns for two seconds and leaves a wreck holding the cargo box's
  contents, half the scrap of each module, and 120 metal fragments for the frame. The fuel is lost.

## 8. The client

- **Model:** the frame's GLB plus one GLB per module, mounted at the frame's sockets, so the drawn craft is the assembled
  one. The main rotor and the tail rotor turn by the spool; above half spool their blades fade into blurred discs.
- **Camera:** chase by default; V for the cockpit, where the frame's tubes and the windscreen are in view.
- **HUD:** fuel and hull, plus the rotor's spool, the height over the ground, the climb rate and the stick's position. No
  boost line.
- **Rotor wash:** the lift-wash effect under the hub, scaled by spool and nearness to the ground.
- **Sound:** sounds.json roles of `ka-fa-1500`: `start`, `engine` (the chop and the twin), `load` (the collective),
  `shutdown`, `touchdown`, `crash`, `destroyed`.

## 9. Network and saves

- The spool rides in `OwnShipState` (exact, for the pilot's prediction) and in `ShipNetState` (a byte, for everyone else's
  rotor and sound). That is a protocol bump.
- `ShipCodec` stores the spool (a codec version bump), so a craft held in a hover by a sleeping pilot is still hovering
  after a restart. Installed modules are stored by the module core (VEHICLES.md §9.1).

## 10. Art

A two-seat scrapyard machine:
- **Frame:** welded steel tube in oxide-red primer, chipped, with tack welds and taped joints.
- **Cockpit:** a salvaged bucket seat, a bent clear windscreen bolted at its corners, a cyclic stick, a collective lever and
  two round gauges on a strap-iron panel.
- **Engine:** an air-cooled flat twin with a vertical shaft, its pulley belted to the mast's; an exhaust in heat wrap.
- **Rotor:** a teetering two-blade rotor, taped at the tips, over a swashplate and push rods.
- **Tail:** a triangular truss boom, a small tail rotor behind a hazard-taped guard, and a road sign bolted on for a fin.
- **Skids:** bent pipe with rubber feet. **Lamps:** a car headlamp under the nose, a red lamp on the tail.

One 2,048 px atlas of our own pixels (numpy, as the research table's), a glass material and the rotors' blur, three
LODs. The owner approves the look on renders before the client work is finished.

## 11. Packages

| Package | What | Done when |
|---|---|---|
| M0 | This document | the owner's yes, or the changes asked for |
| M1 | The rotor drive, the spool in the state, the network and the codec; the KA-FA as a fixed build (twin, rotor, jerry tank, rear seat) in ships.json, its item and recipe | **Built 20 October 2026** (§13). `RotorTests` 15/15: spool, hover, climb, top speed, levelling, yaw and banked turns, the ceiling, leaving in flight, landing, out of fuel, crashes, a strike, the sea, determinism, storage |
| M2 | The model and the modules' models (`art/models/ka_fa_1500.py`), icons | **Built** (§13); renders the owner approves |
| M3 | The client: the view (rotors, blur, modules), the control scheme, camera, HUD, sound, wash; `--view ship --ship ka-fa-1500` and its tour | **Built** but for the tour (§13); screenshots wait for Godot |
| M4 | Modules on the shared core (VEHICLES.md V1): sockets, module items, the bay as a service station, saves and the network | **Built** but for swapping them (the car session's lift, V4) (§13) |
| M5 | Progression: the tree, recipes, the tuned twin's blueprint in monument loot; decay; SAM sites target it | ContentTests; a hosted game: build, assemble, fly, land, leave |

Seated item use (a passenger firing, M11) is a shared seats feature, not this package's.

## 12. Open questions

1. Rotor wash pushing players and loose things.
2. A rare rotor, and a lighter frame.
3. Flares against SAM sites (Rust added them in 2025).

## 13. As built

### M1 (20 October 2026): the rotor drive and the KA-FA as a fixed build

- **The drive:** `src/Hollow.Simulation/Ships/ShipRotor.cs`, `rotor` in `ShipDrives` (the boat session's seam, 98621ac7). Its
  numbers are ships.json's `drive` block: `lift`, `spoolUp`, `spoolDown`, `airReference`, `airExponent`, `airCap`, `pitch`,
  `roll`, `level`, `climb`, `sink`, `response`, `weathervane`, `tipped`, `floods`. It keeps the spool up to date every tick
  (`Advance`), and a turning rotor never sleeps.
- **Shared step, additive:**
  - `ShipState.Spool`, and `ShipEvents.Strike`.
  - Hull boxes may be `fragile`. A contact on one while the spool is over 0.2 is a strike: the spool goes to 0 and the hull
    loses the crash block's `strike` share. Shots and aim pass through fragile boxes, and a parked body leaves them out.
  - The crash block may take Rust's rule (`fatal`, `least`, `tipped`): `ShipDef.CrashDamage`. The Tin Finch keeps its own.
  - `needsPilot`: the engine runs only with someone awake at the helm (or the zone's autopilot).
  - A point in ships.json may face a way (`{ "at": [x, y, z], "forward": [x, y, z] }`), for the rear seat that looks out
    to the right.
  - A new seat verb, `fly` (interactions.json, appended).
- **Wire and storage:** protocol 46 (`OwnShipState.Spool`, `ShipNetState.Spool` as a byte, `RemoteShip.Spool`); `ShipCodec`
  version 7 appends the spool (`SpoolBytes`). The two attitude and fuel tests that index the codec's trailer count it.
- **Data:**
  - The KA-FA in ships.json (key `ka-fa-1500`, controls `rotor`) and item 250 in items.json.
  - Its recipe at the Vehicle Workbench: 1,050 metal fragments, 60 refined metal, 250 wood, 50 lamp oil.
  - A 250-salvage node in the Vehicle Workbench's tree, a placeable (appended), and `hull` in contact-surfaces.json.
  - The fixed build is today's stand-in for M4's modules: a twin, a rotor, a jerry tank and the rear seat.
- **Not yet:**
  - The model (M2) and everything the client draws (M3). Without a control scheme named `rotor` the client flies it with
    the flight keys, so it can't be flown well yet.
  - Modules (M4, on the car session's V1).
  - Decay, SAM sites, the tuned twin (M5).

### M2 (20 October 2026): the model, from text

`art/models/ka_fa_1500.py` (`art/build.py -- ka_fa_1500`; `-- --preview` renders look-dev pictures to
`.artifacts/itemart/ka-fa-1500/` with `art/models/preview_scene.py`):
- **The frame** (`game/assets/world/ka-fa-1500.glb`, 17,000 triangles at LOD0): primer-red welded tube, bent-pipe skids on
  rubber pads, a bucket seat with a harness, a bent clear windscreen, the stick, lever, pedals and two gauges, the mast with
  its pulley, swashplate and push rods, a truss tail boom with its drive shaft, the tail rotor (`TailRotor`, its
  `TailRotorBlur`) behind a hazard-taped hoop, a stabiliser, a road sign for a fin, the headlamp and tail lamp.
- **The modules**, each its own GLB in its socket's frame (`ka-fa-1500-<module>.glb`): twin, tuned twin, rotor (`Rotor`,
  its `RotorBlur`), jerry tank, long tank, rear seat, cargo box, plating.
- **Materials:** one 2,048 px atlas of our own pixels (`art/models/ka_fa_1500_paint.py`: primer, olive, jerry red, steel,
  dark steel, cast aluminium, rubber, a worn vinyl seat, diamond plate, the blades' grey, hazard tape, the fin's sign, the
  gauges' dials, lenses, rust, exhaust wrap), plus a glass, the rotors' blur and the lamps' lenses (`M_Hollow_KaFa_Lamps`).
- Three LODs, vertex occlusion, sockets (`socket_engine`, `socket_rotor`, `socket_tank`, `socket_rear`, `socket_plating`,
  `socket_engine_audio`, the seats', the fuel cap's, the tail rotor's).
- The catalogue hides every `…Blur` mesh until a view spins it (`ModelCatalog.HideBlurs`), so icons and the viewer show
  the blades.

### M3 (20 October 2026): the client

- **Rotors drawn:** ships.json `rotors` (node, axis, rpm, blur, wash; `ShipRotorLook`). `ShipView` turns each by the
  spool and fades its blades into the blur from 40 to 70 % of working speed. A washing rotor raises the lift-wash dust
  under its hub. `ShipLook` carries the spool: the own ship's predicted, everyone else's from the snapshot.
- **Controls:** `RotorControls` (§5.2): Rust's keys, the mouse a stick that stays where it is put and springs back from a
  small tilt after half a second, the middle button levels, the arrows a keyboard stick.
- **HUD:** `HudGauge.Rotor` (the scheme says): the rotor's spin and lift share in place of the throttle lever, its state
  (STOPPED, SPOOLING, WINDING DOWN) in place of flight assist, no orbit lines.
- **Sandbox:** `--view ship --ship ka-fa-1500 [--scene hover|forest|cockpit|slope]` stands the KA-FA on the workbench's
  pad with its control scheme and HUD.
- **Sound** (M17): `art/audio/rotorcraft.py` models the rotor (the blades' slap at each pass with a thump under it, the
  swelling whoosh, the tail rotor's buzz, the twin's firing, pops and tappets, all geared to the rotor's speed), the
  twin's pull-start and its cut. `art/audio/ka_fa_sounds.py` makes them events (`kafa.rotor.slow/fast/load`,
  `kafa.twin.start/stop`). `ShipSounds` plays the rotor while it turns, engine running or not, crossfaded by the spool,
  the load's slap by the collective.
- **Not yet:** the tour (`--tour`), screenshots and a hosted flight. They wait for a turn on the PC's Godot.

### M4 (20 October 2026): a frame with modules

- **The core:** the car session's (VEHICLES.md §9.5): sockets, modules.json, the assembler, the default build, the drive
  sums.
- **The frame** (ships.json `ka-fa-1500`): 290 kg, its own 10 lamp oil, the pilot's seat, the tail rotor's fragile box.
  Its sockets are `engine`, `rotor`, `tank`, `rear` and `plating`. Its default build (twin, rotor, jerry tank, rear seat)
  makes M1's craft exactly: 480 kg, 250 lamp oil, 9.6 kN.
- **The modules** (modules.json, items 251–258), each with a body part that has health:

  | Module | What it gives |
  |---|---|
  | Twin | drive `lift` 9,600 |
  | Tuned twin | `lift` 11,500 |
  | Rotor | drive `rotor` 1, and the disc's fragile boxes |
  | Jerry tank | 240 fuel |
  | Long tank | 490 fuel |
  | Rear seat | the exposed seat, looking right |
  | Cargo box | a hold of 12 |
  | Plating | boxes that take the shots meant for the cockpit and the engine |

- **The drive composes:** `ShipRotor.Compose` takes the engines' lift while a rotor turns on the craft; without either,
  nothing lifts and the rotor doesn't turn.
- **Drawn:** `ShipView.Modules.cs` mounts each installed module's model at its origin, and `ShipsView` draws a ship again
  when its layout changes.
- **Measured** (`RotorTests` 19/19):

  | Build | Result |
  |---|---|
  | Heavy (long tank, cargo box, plating), 630 kg | climbs 30 m in 5 s on the twin at a collective of 0.67 |
  | The same on the tuned twin | 0.56 |
  | No engine, or no rotor | stays on the ground, its rotor still |

- **Not yet:** swapping modules at the bay (the car session's lift and vehicle screen, V4), and the modules' recipes and
  tree nodes. Until the swap exists, a module item does nothing.

