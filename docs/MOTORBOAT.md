# Motorboat

Status 20 October 2026: built on branch `claude/motorboat` at the owner's request ("a small motorboat craftable").
The request is confirmed; every number, the recipe, its place in the tech tree, the controls and the look below are
proposals, open to the owner's tuning.

## 1. What it is

A small open boat for the coasts, lakes and rivers of a world: a welded hull of salvaged sheet, a car's steering wheel
on a post, cabled to an outboard motor on the transom, a bench for one more, a crate in the bow. It seats a driver and
one passenger, carries eight slots of cargo and burns lamp oil. It is crafted at a level 1 workbench and put on the
water from its item, like a deployable: there is no bay to roll out onto.

It is a ship kind like the Tin Finch and the Iron Wake (`game/data/ground/ships.json`, key `motorboat`, item 239), built
on three shared pieces added for it:

- **A drive** (`IShipDrive`, ships.json `drive`; GROUND.md §6.6): what the engines give each tick. The boat's is the
  `propeller`; the Tin Finch and the Iron Wake keep `thrusters`, the default.
- **A hull that floats on its shape** (`ShipBuoyancy`, ships.json `buoyancy`).
- **A control scheme** (ships.json `controls`, `ShipControlSchemes` on the client): `boat`.

The minicopter session adds a `rotor` drive the same way.

## 2. Numbers

| What | Value | Why |
|---|---|---|
| Size | 4.4 m long (transom to stem), 1.64 m beam, 0.8 m deep amidships (1.02 m at the bow) | a small skiff |
| Mass | 390 kg; inertia 670, 730, 115 kg·m² (pitch, yaw, roll) | hull, motor, fittings |
| Centre of mass | 0, 0.34, −0.35 m (the motor pulls it aft) | |
| Health | 400 | Tin Finch 750 |
| Damage taken | melee 0.3, arrow 0.2, bullet 0.6, explosive 1.0 | thin sheet |
| Thrust | 2,200 N ahead, 900 N astern | about a 10 hp outboard |
| Speed | 8 m/s ahead (15.5 kn, 29 km/h), 2.5 m/s astern | |
| Turning | 40°/s at speed, 90°/s² | a 12 m turning radius at 8 m/s |
| Steering | full at 4 m/s through the water; the wash adds 2.5 m/s at full throttle | turns slowly from rest |
| Fuel | lamp oil, 100 tank, 0.01/s idle, 0.12/s full | about 14 minutes at full throttle |
| Placed with | 20 lamp oil (the recipe's) | |
| Cargo | 8 slots, the bow crate | |
| Crash | over 4 m/s: (speed − 4)² × 3; the driver takes a tenth | |
| Recipe | 60 s at a level 1 workbench: 300 metal fragments, 200 wood, 2 gears, 1 metal pipe, 4 rope, 20 lamp oil | |
| Learnt | level 1 workbench tree, 125 salvage | |
| Wreck | 120 metal fragments, 80 wood, 1 gear | |

## 3. On the water

**Buoyancy** (`src/Hollow.Simulation/Ships/ShipBuoyancy.cs`). The hull is six columns, 0.8 m tall (the gunwale
amidships), from the bottom at three stations (stern, middle and bow, port and starboard), together displacing 2.4 m³
when wholly under. Each column
lifts by how far it is under the water, where it is, so the hull rides level, pitches and rolls back and settles at
its draft: 18 cm at rest, 13 cm at speed with the bow 0.8° up. In the water it drags little ahead, much sideways (its
keel: under 1° of slip at full speed, under 3° turning) and up and down, and its turning is damped.

**The water** is the sea, a lake or a stream: `ISurface.WaterLevelAt` under the centre of mass, once a tick (the
walking-scale ground has computed that point for the ground's height already). Ships without a `buoyancy` block keep
the band round their centre of mass on the sea alone, as before.

**The propeller** (`ShipPropeller.cs`) pushes only while it is under the water (0.27 m at rest), along the nose, from
where it is, so the bow lifts a little under power. The throttle asks for a speed ahead or astern, and the motor works
toward it within its thrust. At zero the motor idles and the boat coasts: from 8 m/s it is below 0.2 m/s in 20 s.
The leg steers by the water past it, the boat's way plus the wash, so going astern the wheel turns it the other way.
Nothing lifts, strafes, pitches or rolls.

**Ashore** the propeller bites nothing. The boat rests on three keel points (its skids) and goes nowhere. Pushing a
beached boat back into the water is not built (§9).

**At rest** afloat and slow, it sleeps as a landed ship does. While asleep it stays supported as long as a column is
in the water.

## 4. Crafting and placing

- The recipe names no station, so a crafted ship becomes its item (`CraftQueue`); ships made at a station still roll
  out onto its bay.
- Placement (`placement.json`'s ship rule, the boat's own block in ships.json): `"surfaces": ["water"], "water":
  true`, within 8 m of the eye.
  - The water is a placement surface (`PlacementSurfaces.Water`). The aim meets it only for a rule that allows it:
    `PlacementWorld.Ray` with the rule's surfaces walks the ray against `GroundProbe.WaterCrossing`, which uses the
    ground's own water level.
  - On land the answer is "Put it on the water" (`OutcomeCode.NotOnWater`, 124).
  - The hull's footprint must still be clear; water too shallow for it blocks.
- A placed boat drops onto its draft and settles in about two seconds.

## 5. Driving

Controls (`BoatControls`):

| Input | Does |
|---|---|
| W, held | ahead |
| S, held | astern |
| Let go | the motor idles and the boat coasts |
| A, D (or the arrows) | the wheel |
| Mouse | looks round (no steering cursor) |
| V | the orbit camera, or the seat's view |
| SPACE | leave |

Flight assist stays on, and there is no boost.

The HUD (`ShipHud`, `Flies` false) shows:
- speed in m/s and knots, and the heading;
- the throttle;
- fuel and hull;
- AGROUND when out of the water.

It hides altitude, climb, flight assist, LANDED, PULL UP and the orbit's lines.

ShipView draws no flight glows on it (ships.json `lights.nav` false). A bow lamp, a spot of 45 m, lights while
someone is at the helm.
In the water and moving it throws a wake at its stern: ships.json `wake`, the effect `wake.boat` in fx.json
(spray up and back, foam left on the water, rings spreading), full at 7 m/s (`ShipsView.Wake`).

## 6. Seats

- **The helm** (take the helm; sitters: anyone while unlocked, the crew while locked) and **the bench** (sit; anyone).
  Both are in the open: `exposed`, so their occupants can be shot. A seat in the open on a ship people only sit in
  needs no door.
- **Leaving.** Over water deeper than a metre, the occupant goes over the side at the seat's authored exit and swims.
  In the shallows, or ashore, they stand as on land.
- **A passenger on the client** rides the boat someone else drives: `ShipModule.SeatAt` takes it from its states,
  carried past the newest, and `SeatEye` puts the passenger's eye at the bench's eye on the boat as drawn. Before
  this, a passenger stayed where they sat while the ship drove off.

## 7. Model and sound

**The model** is original, built from code (`art/models/motorboat.py`; `blender -b --factory-startup --python
art/build.py -- motorboat`; `-- --preview <prefix>` renders Eevee look-dev views with back faces culled as the game culls
them, and `-- --holes <prefix>` renders against a magenta sky with no water from every side, from below and into the
bow, where sky seen through the hull is a hole):
- a hard-chined hull of welded panels in faded teal over steel, chipped and rust-streaked, with a primer-red patch and
  a dark bottom; its sides meet on the centreline at the stem, closed, with a steel stem bar outside;
- a plank deck 0.26 m over the keel, above the waterline (about 0.15 m), because the sea is one plane that nothing
  masks inside a hull: below the deck nothing shows. The deck runs a centimetre into the inner walls, which go 5 cm
  below it, and it closes to the keel at the bow, so no crack shows the sea;
- two thwarts with canvas cushions at the seats' height (0.62 m);
- a welded console with a car's steering wheel where ShipView's helm grips put the driver's hands (0.32 m over the
  seat, 0.42 m ahead), a throttle lever, cables aft;
- a weathered outboard on a transom clamp (its propeller where the drive pushes from, 0.02 m under the keel);
- a red fuel can with its hose, a lashed crate in the bow (the cargo), a foredeck with a coil of rope, cleats, a
  spare paddle and the bow lamp on its post.

One 2048 px atlas and material (`M_Hollow_Motorboat`). Three hand-built levels: 13,531 / 6,033 / 1,915 triangles, and
a 68-triangle convex hull. Every open panel is turned to face where it is seen from (the shared mesh helper turns an
open panel as if it were a closed solid, and the game culls back faces). The engine sound plays from `socket_engine_audio` on the powerhead. Icons are rendered by
`--view icons --only motorboat`; provenance is in `game/assets/world/motorboat.provenance.json`. ShipView draws it like
any ship without an interior, its lamp lit while someone is at the helm.

**The sound** is made in code (`art/audio/machines.py` `outboard_*`, `events.py` `motorboat()`; `python -I
art/audio/build.py --only boat/`), because no recording on this PC is an outboard. It models a small twin two-stroke:
- each firing rings the leg's exhaust passages (a buzz at 190, 460 and 1,150 Hz);
- its gas burbles out under the water;
- the crank's orders, the gears' whine, the reeds' chatter and the intake's hiss;
- a firing that misses now and then, more often at idle.

Loops: idle (900 rpm), part throttle (2,900 rpm) and full (5,300 rpm), which ShipSounds crossfades by the motor's
thrust. Also a pull start (one to three pulls) and the kill switch. They were checked as waveforms and spectrograms
only, not by ear: the owner's ear decides.

## 8. Tests and the game

`dotnet run --project tests/Hollow.Tests -- Motorboat`: ten tests.
- Its kind.
- Afloat at its draft, level and asleep.
- Righting from a 25° heel.
- Full ahead, turning and coasting.
- Astern.
- No way on land.
- A lake.
- Put on the water and not on land.
- Crafted at a workbench as its item.
- A driver and a passenger: boarding from the water, riding, getting out over deep water.

In the game, the tour step `boat` (`--view ground --host --tour <folder> --tour-steps boat`, `GroundTour.Boat.cs`) runs
from a beach:
- it finds water 1.5 m deep and sets the time to 11:00 there;
- it shows the boat's ghost on the water from the shore, places the boat and photographs it afloat;
- it swims to the helm, takes it and drives (orbit camera, then the helm's view);
- it coasts, and its driver goes over the side.

Its step `bay` photographs the vehicle workbench's bay (GROUND.md §6.7). The admin's `ship <player> motorboat` puts a
boat on the nearest water a metre deep within 55 m.

## 9. Open

- **Tuning.** Every number in §2 waits for the owner's feel in the game.
- **No pushing.** A beached boat can't be pushed back into the water (Rust's push).
- **No waves.** The simulation's water is flat, while the sea's shader swells up to about 0.34 m. A low hull may show
  the swell through its sides near the camera.
- **Helm hands.** The driver's hands go to ShipView's fixed helm grips: the wheel is placed there.
- **Moving parts.** The propeller doesn't spin, and the wheel and motor don't turn: ShipView animates parts only for
  ships with an interior file.
- **The view from the helm** looks over the wheel, which sits below the eye's field of view.
