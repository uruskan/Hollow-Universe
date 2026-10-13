# Hollow Universe — Monuments

Status: draft 1, 13 October 2026. What monuments are, the whole catalogue, how they are placed on every planet of
the galaxy, the events that spawn at random, and how they are built and brought into the game. It extends
[GROUND.md](GROUND.md) step G7 ("monuments with loot, guards and radiation") and the Surface stage of
[ARCHITECTURE.md](ARCHITECTURE.md) §9. Research behind it: §13.

Every rule is marked **Decided** (the owner said so), **Proposal** (waiting for a yes or no) or **Open**.

---

## 1. Decisions

| Topic | Rule | Status |
|---|---|---|
| What a monument is | A built site on a planet (or in orbit) with special loot, danger and a reason to come back: Rust's monuments, on real planets. | Decided (GAME_DESIGN.md §4) |
| Inspiration | Rust first (the loot ladder, keycards, fuses, radiation, guards, recyclers, safe zones, landmarks by silhouette). Dune: Awakening for the space setting (wrecks, buried sites, sites that renew, events you see from afar). Empyrion for sites on many procedural worlds. Mechanics and numbers may be borrowed; names, layouts, models and art never are (GAME_DESIGN.md §10). | Proposal |
| How many | 66 kinds of monument in eight scales, from a fenced substation to a kilometre-tall tower, plus 10 events that spawn at random (§5, §6). Rust has about 45 kinds, Dune: Awakening about 20. | Proposal |
| Scale | Real scale. The largest monuments are kilometres across or tall and read from 40–60 km away in clear air. Interiors stay human-scale. | Proposal |
| Rarity | By encounter rate, not by count: something small every few minutes on foot, a roadside site near every landing, a large monument on most colonised worlds, a landmark every few systems, wonders only through leads (§4). | Proposal |
| Where | A pure function of the planet (seed, body, history and terrain), so every server and client agrees without storing anything (§9). Only what players change is stored. | Decided (ARCHITECTURE.md §7–8) |
| Random sites | Events (crashes, drops, surges, quakes...) come from a hash of the place and a time window, so they are deterministic too and the server stays authoritative (§6). | Proposal |
| Lore | The ruins of the Seedward Authority, whose automated fleets prepared thousands of worlds for colonists before the Quiet cut the relays three centuries ago (§3). | Proposal |
| Build | Blender scripts in `art/monuments` build every monument from a shared kit and its own geometry, at three levels of detail, with collision and gameplay markers, into GLB and JSON (§8). | Proposal |

---

## 2. What monuments are for

Monuments are where the ground game's progression lives (GAME_DESIGN.md pillar 4, "earned in the world"):

1. **The loot ladder.** Salvage, components and blueprints from barrels and crates at every site; access chips
   (Rust's keycards) that open the next tier's rooms; sealed crates whose best loot exists nowhere else (§7).
2. **PvP magnets.** Contested loot draws players together. A monument's value, timers and noise decide how many fights
   it starts. Pillar 5, "big and quiet, then sudden", happens at monuments.
3. **Landmarks.** Players navigate by silhouettes: a cooling tower, a dish, a broken hull, a tower on the horizon.
   The map names them.
4. **Social places.** Two kinds of safe zone (the Exchange, the Reclaimer Camp) where nobody may shoot: trade,
   recycling, rumours (§5.4).
5. **Reasons to travel.** Landmarks and wonders hold what home doesn't: unique blueprints, ship parts, rare materials.
   They are worth a flight to another planet or system (GAME_DESIGN.md §7, "special loot").
6. **The world's history.** Every ruin says who was here and what went wrong (§3), so worlds differ.

What each source taught us (§13 has the research):

| From | We take | We avoid |
|---|---|---|
| Rust | The ladder of access chips and power cells; contamination as a clock and a gear check; recyclers at monuments; guards and armoured patrols at the big ones; safe zones; landmarks you navigate by; events everyone sees (a crate dropped, a cargo ship arriving). | Its names, layouts and models. |
| Dune: Awakening | A fixed layer players learn plus a layer that renews; each kind of site the main source of one family of parts; wrecks and buried stations; events announced loudly (a column you see for kilometres, an explosion overhead); scale shown on the approach, not inside. | Waves of enemies in locked rooms as the main content; an endgame gated behind PvP. |
| Empyrion | Compound sites from many small pieces; loot rank tied to how hard a place is to reach; site variants by biome. | A placer that retries at random and quietly drops what doesn't fit; the same set of sites on every planet of a type. |
| Others | State overlays (abandoned, occupied, infested, powered); damage and dressing variants; logs that differ per site; trade-able leads; events with a short life near active players. | Hand-made templates repeated without variation (Starfield). |

---

## 3. Lore: why the galaxy is full of ruins (Proposal)

The game is named Hollow for a reason: the galaxy players fly into is full of what someone else left.

- **The Seedward Authority** sent automated seeder fleets ahead of its colonists, to thousands of worlds within a few
  thousand light-years of Sol. The seeders built what colonists would need: power grids, survey relays, landing
  fields, mines, water works, terraforming towers, research stations, and on the best worlds whole colony towns.
- **The Quiet.** About three centuries ago the relay network went silent. Nobody knows why. Colony fleets stopped
  coming; the colonies that existed starved or scattered; ships in transit fell or drifted. The machines kept running
  until they broke, and some still run.
- **Wardens** are Seedward's automated security: turrets, drones, tracked crawlers and gunships that still guard what
  they were told to guard. They are the monuments' machines (Rust's scientists and its armoured patrols, in kind).
- **Reclaimers** are the descendants of stranded colonists: scavenger clans who strip the ruins, wear what protects
  them from the contamination, and fight anyone who competes. They are the monuments' people. One clan keeps a neutral
  market (§5.4).
- **The Bulwark** was Seedward's military arm: garrisons, redoubts, launch sites and the warships that fell.
- **Players** are the second wave, arriving on their own with Tin Finches and nothing else.

**Visual families**, so a ruin says who built it:

| Family | Look | Where |
|---|---|---|
| Seedward civil | Off-white and pale grey plating, safety orange and deep teal bands, rounded corners, stencilled codes, big louvres | Grids, relays, water, power, terraforming, labs |
| Seedward industrial | Concrete (board-formed), painted steel frames, corrugated cladding, yellow rails, hazard stripes | Mines, smelters, yards, ports |
| Bulwark | Olive and grey, armoured concrete, gun ports, revetments, sandbags | Garrisons, redoubts, launch complexes, warships |
| Colonist | Prefab hab cans, domes and greenhouses, solar fields, wind turbines, hand-painted signs | Homesteads, colony towns, camps |
| Reclaimer | Scrap walls, tarps, welded sheet, graffiti, lights strung on cables | Their camps, and their work at every ruin |
| Ships | Panelled hull plating, ribs and decks exposed where it broke, burnt patches | Wrecks of every size |

---

## 4. Scale and rarity (Proposal)

### 4.1 Tiers

| Tier | Footprint | Height | Run | Seen from | Frequency where the world was settled | Rust's nearest |
|---|---|---|---|---|---|---|
| **R** Roadside | 5–50 m | to 30 m | 1–5 min | 0.5–2 km | Every few hundred metres along the old lines; several in sight from any hill | Substations, roadside junk, water wells, cabins |
| **S** Small | 50–150 m | to 40 m | 5–15 min | 2–5 km | Every 1–2 km² | Gas station, supermarket, lighthouse, quarries, fishing village |
| **M** Medium | 150–350 m | to 80 m | 15–40 min | 5–10 km | A few in every 4 × 4 km zone | Dome, satellite dish, harbour, sewer branch, outpost, bandit camp |
| **L** Large | 350–900 m | to 150 m | 40–90 min | 10–20 km | One or two per zone near old settlements | Launch site, power plant, train yard, military tunnels, excavator |
| **X** Landmark | 0.8–5 km (lines to 40 km) | to 900 m | Hours | 40–60 km, and from orbit | One per old region (about every 100 km) on colonised worlds | — (Dune's heighliner wreck, in spirit) |
| **W** Wonder | Any | Any | Sessions | Through leads | A few per planet at most; some planets none | — |
| **O** Orbital | 100–800 m | — | 20–60 min | From the system map | A few per settled system | Oil rigs, cargo ship |
| **E** Event | Any | Any | 15–35 min | Heard and seen from far | Rolling, near active players | Cargo ship, crate drops, patrol helicopter |

A 4 × 4 km zone on a settled world (the size of a Rust map) therefore holds about what a Rust map does: 1–2 large,
3–5 medium, 8–12 small, dozens of roadside sites, and a landmark on the horizon. Wild worlds hold far less (§9.2).

### 4.2 Showing scale

- **Silhouette first.** Every monument from medium up has one shape that reads from far away (a dome, a dish, a
  cooling tower, a broken hull, a tower). Landmarks are visible from 40–60 km and from orbit.
- **The approach.** Debris fields, roads and power lines lead in; scars in the terrain; the wall around the works.
- **Lights at night.** Aviation lamps blink on everything tall (§7.6), so the sky line of a settled world shows at night.
- **Inside, human scale.** Rooms, stairs, catwalks and ladders sized for a person (the Builder's kit uses real
  dimensions: 1.1 m rails, 0.19 m risers, 2.3 m doors).

### 4.3 Planet history decides what exists

Each landable body gets a **history** from its physical state (GALAXY.md §10.3), its distance from Sol and a hash:

| History | Typical bodies | Monuments |
|---|---|---|
| **Colonised** | Earth-like and temperate worlds near the old routes (the first world is one) | Every tier; two or more old regions with towns, industry, power, landmarks |
| **Industrial** | Metal-rich rocky worlds, big moons | Mines, smelters, bores, mass drivers, yards; few towns |
| **Outpost** | Any landable body near a settled system | Roadside and small sites, a landing field, a relay, crash sites |
| **Research** | Ice worlds, odd chemistry, near neutron stars and white dwarfs | Research outposts, ice stations, uplinks, anomalies |
| **Fortress** | Rare; worlds the Bulwark held | Garrisons, redoubts, launch complexes, warship wrecks |
| **Battlefield** | Rare; where the Quiet turned to war | Wrecks everywhere, craters, ruined towns, heavy contamination |
| **Untouched** | Most of the galaxy | Only natural sites, crash sites and events |

This is how GAME_DESIGN.md's rule "rare monuments depend on the type of star and planet" works.

---

## 5. The catalogue

Each entry: footprint (and height), where it stands, what it looks like, what it holds, and its nearest relative in
the games we studied (for mechanics only). "Built" names the Blender script once one exists.

Gameplay terms used below (§7): **chips** amber, cyan and violet (Rust's green, blue and red keycards, in kind);
**cells** (power cells, Rust's fuses); **contamination** 1–5; **crates** (crate, military crate, sealed crate, vault
crate); **guards**: Reclaimers (people) and Wardens (machines).

### 5.1 Roadside (R) — 14

| # | Monument | Size | Where | What | Holds and does | Like |
|---|---|---|---|---|---|---|
| 1 | **Grid Substation** | 40 × 28 m | On old power lines, every few km | A fenced switchyard: two transformers behind a blast wall (one burnt out), breakers, a gantry taking the line in from a pylon, a control house, lamp posts | Barrels, two crates, a locker that opens during a **Surge** (§6). Arcing hazard while powered. *Built: `grid_substation.py`* | Rust's power substations |
| 2 | **Toppled Pylon** | 60 × 20 m | On power lines | A transmission tower fallen across the road, cables snaking through the grass, a line crew's trailer and drum | Copper wire and cable reels to salvage, a crate in the trailer | Roadside junk |
| 3 | **Relay Mast** | 30 m round, 60 m tall | Hilltops, ridges | A guyed lattice mast with dishes, an equipment hut, a fence | Tech scrap, a chance of an amber chip; climbing the mast reveals the zone's sites on the map | — |
| 4 | **Drop Pod** | 12 m crater | Anywhere open | A supply pod half-buried in a scorched crater, its petals open | One military crate. Also an event (§6.2) | Rust's airdrop |
| 5 | **Container Spill** | 40 × 15 m | Roads | A hauler on its side and its freight containers strewn along the verge, some burst | Crates and barrels in and around the containers | Roadside junk |
| 6 | **Stalled Crawler** | 25 × 8 m | Roads, tracks | A six-wheeled crawler and trailer, abandoned mid-route, doors open | Fuel, salvage, a crate in the cab | — |
| 7 | **Survey Marker** | 10 m | Peaks, plains | A survey tripod with a solar panel and a sealed cache box | Map reveal for the area; a small cache once per cycle | — |
| 8 | **Wellhead** | 20 m | Plains, deserts | A pump jack nodding over a well, a tank, a pipe to the road | Refill water (or lamp oil on oil worlds), barrels | Rust's water wells |
| 9 | **Checkpoint** | 30 × 20 m | Roads into regions | A barrier, a guard booth, sandbag walls, a watchtower, a shot-up truck | Military crate, ammo; sometimes Reclaimers | Rust's roadside military |
| 10 | **Bunker Hatch** | 8 m pad | Anywhere | A sealed hatch on a concrete pad with a vent | A cell opens it: a small underground room with a crate and a chance of a cyan chip | — |
| 11 | **Crashed Skiff** | 15 m | Anywhere | A wrecked single-seat ship like the Tin Finch, nose in the dirt | Ship parts, a crate. Also an event (§6.1) | Dune's small crashed ships |
| 12 | **Valve Station** | 25 × 15 m | On pipelines | A valve house where the pipeline surfaces, handwheels, a pig launcher | Pipes, gears, lamp oil | — |
| 13 | **Weather Station** | 15 m | Open ground, coasts | An instrument mast, screens, a solar shed | Tech scrap; predicts storms on the map | — |
| 14 | **Hab Pods** | 20 m | Forest edges, valleys | Two or three colonists' prefab pods, a tarp, a fire pit, a garden gone wild | Food, cloth, a crate, sometimes a bed | Rust's abandoned cabins |

### 5.2 Small (S) — 11

| # | Monument | Size | Where | What | Holds and does | Like |
|---|---|---|---|---|---|---|
| 15 | **Fuel Depot** | 70 × 50 m | Road junctions | A canopy over pumps, a shop, two buried tanks with vent stacks, a tanker trailer | Crates, barrels, an amber chip spawn, a **recycler** | Rust's gas station |
| 16 | **Scrapyard** | 120 × 90 m | Near towns | Piles of wrecked vehicles and hulls, a magnet crane, a crusher, a shed | Salvage piles; the crusher turns vehicles into metal; recycler | Rust's junkyard |
| 17 | **Water Tower** | 60 × 40 m | Towns, farms | An elevated tank on a frame, a pump house, pipes | Water, barrels, a ladder climb to a crate | — |
| 18 | **Homestead** | 120 × 80 m | Farmland, grassland | A colonist farm: two barns, a silo row, a greenhouse, a hab, a wind turbine, fences | Food, seeds, cloth; animals nearby; a stable vendor in the safe variant | Rust's ranch and barn |
| 19 | **Fishing Wharf** | 100 × 60 m | Sheltered coasts | Piers on piles, boathouses, net sheds, a crane | Boats (later); the safe variant has a boat vendor | Rust's fishing villages |
| 20 | **Strip Mine** (stone, metal, sulphur) | 150 × 120 m | Hills, ore-rich ground | A stepped pit with a fuel-powered digger rig, a hopper, a conveyor to an output bin | Fill with fuel to produce ore over time; contested by design | Rust's quarries |
| 21 | **Coast Beacon** | 40 m, 45 m tall | Headlands | A beacon tower with its lamp still turning, the keeper's house, a fog horn | A climb to a military crate at the top, an amber chip spawn | Rust's lighthouse |
| 22 | **Supply Depot** | 80 × 60 m | Towns, roads | A logistics warehouse with racks and loading docks, a forklift | Crates on racks, an amber chip spawn, recycler | Rust's supermarket |
| 23 | **Shuttle Wreck** | 60 × 40 m | Anywhere | A 40 m shuttle crash-landed on its belly, one wing torn off, a furrow behind it | Ship parts, two crates, a sealed crate in the hold | — |
| 24 | **Field Clinic** | 60 × 50 m | Near towns | Prefab wards and tents, a container lab, a helipad | Medical supplies; a medical vendor in rare safe variants | — |
| 25 | **Mining Camp** | 90 × 70 m | Mountains | A shaft headframe over a tunnel, ore bins, workers' huts, a winch house | Ore, gears, an amber chip spawn, recycler | Rust's mining outpost |

### 5.3 Medium (M) — 12

| # | Monument | Size | Where | What | Holds and does | Like |
|---|---|---|---|---|---|---|
| 26 | **Biodome** | 160 m, dome 70 m tall | Plains near towns | A geodesic greenhouse dome, half its panes gone, overgrown inside, with service buildings | A climb up the frame to crates at the top; contamination 2 | Rust's dome |
| 27 | **Uplink Array** | 220 × 160 m | Plateaus | A 60 m dish and three smaller ones, a control block, transformer yard | Cyan room behind an amber chip door; tech; contamination 1 | Rust's satellite dish |
| 28 | **Transit Station** | 200 × 80 m | On rail lines | Platforms under a canopy, a ticket hall, a portal into the tunnel network | The way into the underground (tunnels link stations, §5.9); Reclaimers below | Rust's train stations and tunnels |
| 29 | **Refinery** | 300 × 200 m | Coasts, oil worlds | Distillation columns, tank farm, flare stack, pipe racks | Fuel, gunpowder makings; cyan room; contamination 2 | — |
| 30 | **Research Outpost** | 200 × 150 m | Odd ground: craters, ice, near anomalies | White modular labs on stilts, clean rooms, a test range | An access puzzle through the labs (chips, cells, a code found inside); schematics | Dune's testing stations |
| 31 | **Cargo Port** | 350 × 200 m | Coasts | Quays, two gantry cranes, container stacks, a warehouse, a ship at the quay | Cyan room, container crates, recycler; a boat way in | Rust's harbours |
| 32 | **Garrison** | 250 × 200 m | Strategic ground | Barracks, a motor pool, watchtowers, sandbag walls, an armoury | Reclaimers guard it; military crates, weapons; armoury needs a cyan chip | Rust's military bases |
| 33 | **Landing Field** | 400 × 250 m | Flat ground | Pads, two hangars, a control tower, a fuel farm, a crashed lander | Ship parts and fuel; cyan room in the tower | Rust's airfield |
| 34 | **Drainage Works** | 150 × 100 m (and under) | Below towns | A pumping station over storm drains; a parkour route through the drains | An amber-chip puzzle underground; recycler | Rust's sewer branch |
| 35 | **Detention Block** | 220 × 220 m | Remote | A walled penal compound, cell blocks, guard towers, a yard | Reclaimers; cells as loot rooms; violet room in the warden's office | — |
| 36 | **Reclaimer Camp** (safe) | 200 × 150 m | Swamps, forests | The clans' market in a scrap-walled ruin: stalls, a gambling wheel, a bar | Safe zone; trades, gambling, a recycler; hostile to the hostile (§7.4) | Rust's bandit camp |
| 37 | **Exchange** (safe) | 250 × 200 m | Near the landing site of each zone | A repurposed depot under Warden turrets that enforce the peace | Safe zone; vendors, recycler, research table, workbench, drone market (later) | Rust's outpost |

### 5.4 Large (L) — 10

| # | Monument | Size | Where | What | Holds and does | Like |
|---|---|---|---|---|---|---|
| 38 | **Fusion Plant** | 600 × 400 m, towers 120 m | Rivers, coasts | Reactor hall, turbine hall, two cooling towers, switchyard, chimney | Violet room; contamination 3–4 near the core; recycler | Rust's power plant |
| 39 | **Launch Complex** | 700 × 500 m, tower 140 m | Flat plains, coasts | A rocket on its service tower, an assembly hall, a propellant farm, a bunker | Vault crates at the top of the tower; contamination 3; a **Sentinel Crawler** patrols (§7.3) | Rust's launch site |
| 40 | **Freight Yard** | 700 × 300 m | On rail lines | Rail yard with gantry cranes, sheds, wagons, a signal tower, an engine shed | Cyan room; crates in wagons; recycler | Rust's train yard |
| 41 | **Water Works** | 400 × 300 m | Rivers | Settling basins, filter halls, pump houses, a water tower | Cyan room; recycler | Rust's water treatment plant |
| 42 | **Mountain Redoubt** | 300 m front, tunnels 600 m deep | Cliffs | A Bulwark fortress cut into a cliff: gun ports, blast doors, tunnels, a command bunker | Heavy Reclaimers inside; military and vault crates; violet room | Rust's military tunnels |
| 43 | **Frigate Wreck** | 420 m, two halves | Plains, deserts, seabeds | A Bulwark frigate broken in two, half-buried, burnt; halves 300 m apart | Ship parts (only source of some); Reclaimers nest inside; contamination 2 in the drive section | Dune's named shipwrecks |
| 44 | **Colony Ruins** | 600 × 500 m | Coasts, river valleys | A ruined prefab town: streets, blocks, a plaza, a clinic, burnt cars | Many low-tier crates; contamination 1–3 by street; Reclaimers | Rust's radtown |
| 45 | **Ore Smelter** | 500 × 300 m | Near mines | Crushers, conveyors, blast furnaces, slag heaps, a rail spur | Refined metal makings; cyan room; heat hazard | — |
| 46 | **Bucket-Wheel Pit** | 800 m pit, machine 120 m | Ore-rich plains | A huge bucket-wheel excavator in an open pit, its boom over the face | Fill with fuel and it digs (a slow, loud, contested producer of ore); vault crates on the machine | Rust's giant excavator |
| 47 | **Ice Station** | 400 × 300 m | Ice sheets, poles | Domes and masts on stilts, a hangar, snow vehicles | Cyan and violet rooms; cold as the hazard | Rust's arctic research base |

### 5.5 Landmarks (X) — 8

The huge ones. One per old region at most, each a destination of its own.

| # | Monument | Size | Where | What | Holds and does |
|---|---|---|---|---|---|
| 48 | **Atmosphere Processor** | Works 1.3 km across, tower 860 m | Plains and plateaus of colonised worlds | A terraforming tower on four 330 m buttresses, intake halls at its foot, galleries every 150 m, an elevator spine, a crown of red lamps. A walled works round it | The climb: a vertical dungeon to the crown, with rest galleries; contamination 4 inside; vault crates at the top; the crown reveals every site within 100 km. *Built: `atmosphere_processor.py`* |
| 49 | **Tether Anchor** | Station 1.2 km, cable 40+ km | Equator (elevators need it) | A space elevator's ground station; its snapped tether lies across the land for tens of kilometres, buildings crushed under it | The station (L-size inside) and the cable itself: a line of sites along it; climbing the stump |
| 50 | **Grounded Colony Ship** | 2.4 km, three pieces | Plains, deserts | A colony ship that came down in the Quiet, broken in three, a furrow kilometres long | A dungeon of decks and holds (cryo bays, farms, the bridge); only source of the best schematics |
| 51 | **Deep Bore** | Shaft 1 km across | Mountains of metal-rich worlds | A planetary drill rig over a shaft 1 km across, spiral haul roads down | Descend for ore and the rig's vault; contamination rises with depth |
| 52 | **Mass Driver** | 8 km rail, halls 600 m | Up a mountainside | An electromagnetic launch rail climbing a mountain, capacitor halls at its foot, a cradle on the rail | A line of sites; the capacitor halls are a violet monument; restarting it is a server event |
| 53 | **Great Dam** | 700 m wide, 180 m tall | Mountain valleys | A concrete arch dam with a powerhouse and spillways, the reservoir behind | Powerhouse (L-size), galleries inside the dam, a crawler on the crest |
| 54 | **Arcology** | 600 m base, 400 m tall | Old colony capitals | A ziggurat megablock of the old colony, terraces overgrown | A city in one building: floors of loot, Reclaimer clans by level, the top |
| 55 | **Ring Fall** | Debris field 10 km | Wherever it fell | A kilometre-long piece of an orbital station lying across the land, debris for kilometres | Ship and station parts; a wreck of wrecks |

### 5.6 Wonders (W) — 4

| # | Monument | What |
|---|---|---|
| 56 | **The Vault** | A sealed Seedward archive in a mountain, one per colonised world at most. Its door takes a set of tokens from the planet's landmarks; inside, the best loot of the planet and blueprints found nowhere else. |
| 57 | **Command Spire** | The administrative citadel of an old colony: the place its last records are kept. Logs here point to wonders on other worlds (leads, §7.8). |
| 58 | **Quiet Beacon** | A relay that still transmits after three centuries. Reaching it reveals every monument on the planet's map; the signal can be heard from orbit. |
| 59 | **Anomaly** | Something the Seedward didn't build. **Open**: whether the game has anything not human. |

### 5.7 Orbital (O) — 5

Rust's offshore monuments, for a space game: you reach them by ship.

| # | Monument | What |
|---|---|---|
| 60 | **Derelict Station** | An orbital platform: dock, fight Wardens room by room, crack the sealed crate (15 minutes) while others come. Small and large. (Rust's oil rigs.) |
| 61 | **Drifting Freighter** | An event: a freighter coasts through the system for about an hour, then its orbit decays and it falls (becoming §6.1). (Rust's cargo ship.) |
| 62 | **Belt Platform** | An asteroid mining platform in a belt: ore, fuel, a refinery. |
| 63 | **Dead Satellite** | A small relay satellite: tech scrap, a short visit. |
| 64 | **Moon Outpost** | A sealed base on an airless moon: the vacuum as the hazard. |

### 5.8 Sea (2) and nature (3)

| # | Monument | What |
|---|---|---|
| 65 | **Sea Platform** | An offshore rig on legs, small and large; reached by boat or ship. |
| 66 | **Sunken Hulk** | A wreck on the sea floor (when diving exists). |
| — | **Caves**, **lava tubes**, **impact craters**, **hot springs** | Natural places that may hold a cache, a nest or a rare material; part of the Surface stage, listed here because players treat them as monuments. |

### 5.9 Lines between monuments

Monuments of a settled region are joined, as in Rust:

- **Roads** link towns, depots, the landing field and the big sites, with roadside sites along them.
- **Power lines** run from power plants and dams to towns and industry on lattice pylons; **Grid Substations** stand
  where they branch, every few kilometres. This is where the "leftover electric stations" come from.
- **Pipelines** run from wells and refineries to depots; **Valve Stations** stand along them.
- **Rail** links freight yards, mines and transit stations; tunnels run under hills, with stations underground.

---

## 6. Events: what spawns at random (Proposal)

Events are the renewing layer. Each comes from a hash of (planet, cell, time window), so every server and client
agrees on where and when; the server announces it and stores only what players took. Each is loud (seen or heard
from far), short (15–35 minutes) and contested.

| # | Event | What happens | Draw | Like |
|---|---|---|---|---|
| E1 | **Orbital Fall** | A derelict's orbit decays: a fireball crosses the sky, a sonic boom, an impact. A burning wreck (a skiff, a shuttle or a freighter section) with **sealed cargo pods** that take 15 minutes to crack. | Everyone in the zone sees it fall | Dune's crashing ships; Rust's locked crate |
| E2 | **Supply Drop** | A pod falls on a parachute of fire to a random clearing (or to a player's signal flare). | The trail is visible for kilometres | Rust's airdrop |
| E3 | **Warden Gunship** | An automated gunship patrols between monuments and attacks armed players; shot down, it crashes into an Orbital Fall of its own. | Its sound | Rust's patrol helicopter |
| E4 | **Sentinel Crawler** | A tracked Warden patrols the Launch Complex and other big sites; destroyed, it leaves crates. | Permanent at its sites | Rust's armoured patrol |
| E5 | **Surge** | A dead part of the old grid re-energises: a Grid Substation's lamps come on, arcs crackle, its locker unlocks for 10 minutes; Wardens wake to defend it. | Lights on the horizon at night, a hum | — |
| E6 | **Quake Reveal** | A quake (the first world's hazard) opens a buried site for a few hours: a bunker, a vault corridor, a cave. | The quake itself | Dune's storm-revealed stations |
| E7 | **Storm Front** | On dusty, sandy or snowy worlds a storm crosses the zone; afterwards buried caches and wrecks lie exposed. | The wall of the storm | Dune's sandstorms |
| E8 | **Meteor Fall** | A shower: impact craters hold exotic ore for a day (the rare materials of GAME_DESIGN.md §4 come partly from here). | Streaks in the sky | — |
| E9 | **Trader Convoy** | A Reclaimer convoy crosses the zone selling rare goods; attacked, it fights back. | Its lights and horns | Rust's travelling vendor |
| E10 | **Freighter Drift** | The Drifting Freighter (§5.7): an hour in orbit to board, then it falls as an Orbital Fall. | System-wide broadcast | Rust's cargo ship |

Rules for every event (from the research, §13):

- **Fixed places, random timing** where it makes sense (meteor craters, storm paths reuse their marks, so veterans know
  where but not when).
- **Never on top of players or bases**; never within 300 m of a building piece.
- **Loot never respawns at the same event**; its timers have random spread so nobody can camp it.
- **Planet epochs**: some worlds have a cycle (a storm season, a weekly quake swarm) that re-rolls their buried sites, a
  fixed band near the landing site staying the same (Dune's Deep Desert lesson). **Open**: whether Hollow wants it.

---

## 7. Systems monuments need (Proposal)

### 7.1 The ladder

| Tier | Opens with | Found at | Gives |
|---|---|---|---|
| 1 | Nothing | Roadside and small sites | Barrels, crates, salvage, amber chips |
| 2 | **Amber chip** | Small and medium sites' locked rooms | Military crates, cyan chips, blueprints |
| 3 | **Cyan chip** + **power cell** | Medium and large sites' secure rooms | Sealed crates, violet chips |
| 4 | **Violet chip** + **power cell** | Large sites' vaults | Vault crates: best guns, armour, schematics |
| 5 | Tokens from landmarks | Landmarks, the Vault | Unique blueprints, ship parts |

As in Rust: a chip is used up opening its door (or loses durability), a power cell powers a door's panel for a few
minutes, the rooms' loot respawns on a timer (30–60 minutes with random spread).

### 7.2 Contamination

Levels 1–5 (Rust's radiation tiers in kind), with protection from clothing and suits; each monument lists its zones
(the builder's `zone("radiation", ...)`). On worlds with bad air, the planet's own hazard adds to it (no air, cold,
heat), so the same monument is harder on a harsher world.

### 7.3 Guards

| Guard | What | Where |
|---|---|---|
| **Reclaimers** | Human scavengers in hazard gear with guns; patrol and hold rooms; respawn | Medium and large sites, wrecks, ruins |
| **Reclaimer heavies** | Armoured, with heavy weapons | Redoubts, wrecks, sealed-crate events |
| **Warden turrets** | Automated guns on posts and walls | Seedward sites; the Exchange (peacekeepers) |
| **Warden drones** | Flying, light | Research outposts, landmarks |
| **Sentinel Crawler** | A tracked armoured Warden with a cannon and rockets | Launch Complex, landmarks |
| **Warden Gunship** | A flying gunship | Patrols (E3) |

Reclaimers reuse the player character (§4.8 of GROUND.md) with their own clothing; Wardens are new models.

### 7.4 Safe zones

The Exchange and the Reclaimer Camp: no damage to players inside; anyone who shoots there is marked hostile for a few
minutes and the turrets kill them; vendors, recyclers, a research table and a workbench.

### 7.5 States

Every monument instance has a **state** that changes its dressing and guards without a new model:
abandoned (empty), occupied (Reclaimers moved in: tarps, lights, barricades), infested (animals nest), powered (lamps
lit, doors working, Wardens awake). States come from the planet's history and the event layer.

### 7.6 Lights

Aviation lamps on everything tall blink red at night (landmarks visible far away); powered sites light their flood
lamps (`marker("light")`). Lights stay off in the abandoned state.

### 7.7 What each family gives

Each family of monument is the main source of one family of parts, so every kind is worth visiting (Dune's lesson):
civil and power sites give **electrical parts**; industry gives **mechanical parts**; research gives **tech scrap and
schematics**; military gives **gun parts and armour**; wrecks give **ship parts**; colony sites give **food,
medicine and cloth**. **Open**: the exact items (G7a's components are Rust's set).

### 7.8 Leads

Logs and terminals at monuments point to others: a site on the same planet, a landmark in the next system, a wonder
somewhere in the region. Leads are items, so they can be traded.

---

## 8. How monuments are built

### 8.1 Pipeline

Blender 5.2 scripts in `art/monuments` (§9 of GROUND.md: our own scripts only, no downloaded models):

```bash
blender -b --factory-startup --python art/monuments/build.py                               # every monument
blender -b --factory-startup --python art/monuments/build.py -- grid-substation            # one
blender -b --factory-startup --python art/monuments/build.py -- --surfaces [--force]       # the texture sets
blender -b --factory-startup --python art/monuments/build.py -- grid-substation --preview docs/images/monuments
```

| File | What |
|---|---|
| `monkit/surfaces.py` | The tileable texture sets, made from noise (§8.2) |
| `monkit/godot_mats.py` | The Godot materials on `shaders/monument.gdshader` |
| `monkit/builder.py` | The Builder: primitives, frames, kit instances, markers, zones, pad, collision, export (§8.3) |
| `monkit/kit.py` | Small parts (rails, stairs, ladders, walkways, pipes, fences, insulators, lamps, stripes, windows, block buildings, tanks, lattice towers, barriers, cables, rubble) and the fixed pieces placed as instances |
| `monkit/structures.py` | Large parts (sheds, quonset huts, hab modules, watchtowers, walls, sandbags, cooling towers, chimneys, silos, dishes, cranes, conveyors, rail, roads, solar arrays, wind turbines, ship hulls, rockets) |
| `monkit/preview.py` | Look-dev renders: physical sky, sun, distance haze, ground and pad, a person and a Tin Finch for scale |
| `sites/<name>.py` | One monument each: `KEY`, `TITLE`, `build(b)`, `VIEWS` |

### 8.2 Surfaces

Eleven materials, one shader. Nine are tileable texture sets made by `surfaces.py` from noise (no scans: nothing to
credit, the same file on every build): concrete, board-formed concrete, painted steel, rust, corrugated sheet,
asphalt, bar grating, chain-link, ship hull plating and composite panels; plus glass, lamps and plain glossy and matte
colours. Each set is an albedo (A: where the tint applies, so chips, rust and bare metal keep their colour), an ORM
and a normal map, 1024² and seamless, at a fixed size in metres (concrete 4 m, paint 2 m, hull 8 m...).

`shaders/monument.gdshader` draws every surface: UVs in metres divided by the set's size; the vertex tint (RGB) and
baked occlusion (A); grime and splashed dirt near the ground from UV2 (height above the monument's ground, the
part's wear); a slow variation so long walls don't repeat; per-instance tint for kit pieces and a `lit` switch for
lamps; cloud shadows and the planet's air in front, as on the terrain, so a tower 20 km away sits in the haze.

### 8.3 The Builder

A monument script calls primitives (`box`, `cyl`, `lathe`, `tube`, `extrude`, `slab`, `mesh`, `beam`) inside frames,
places kit pieces (`piece`), adds markers (`marker`: loot with its table, guard, door, ladder, light, locker, landmark),
zones (`zone`: radiation with a level, safe, no-build, arc) and its pad (`set_pad`: shape, ground material, blend).
`export()` writes:

- `game/assets/monuments/<key>.glb`: the monument's own geometry in chunks on a square grid (40 m by default, larger
  for landmarks), three levels per chunk. Levels are rebuilt from the primitives, not decimated: level 1 drops parts
  under half a metre, the bevels and half the sides of round parts; level 2 keeps the big masses. Occlusion is baked
  per vertex by rays (against the pieces too); big faces are subdivided so it has somewhere to land.
- `game/assets/monuments/kit/<piece>.glb`: each kit piece used, once (transformer, breaker, gantry, lamp post,
  container, barrier, drum stack, cable reel...), placed by instance.
- `game/data/monuments/<key>.json`: in the game's frame (metres, +Y up, front toward +Z): bounds, level distances,
  the pieces' instances, collision (boxes, cylinders and convex hulls; stairs as ramps, rails as walls), markers,
  zones and the pad. `game/data/monuments/kit/<piece>.json`: each piece's collision and markers.

The grid substation exports 417 primitives and 12 pieces: 21,410 triangles at level 0, 2.0 MB; the Atmosphere
Processor 6,182 primitives, 120,352 triangles, 15.5 MB. **Open**: how the models ship. A catalogue of 66 at a few MB
each is too much to commit as GLB, so the GLBs are rebuilt from the scripts and not committed for now. The likely
answer (M6): the JSON already lists every primitive, so the game can build the meshes itself from it (a few hundred
KB per monument), with per-site variation for free; only the kit pieces stay GLB.

### 8.4 Variation

A monument script is a template. **Proposal**: each site seeds its own Builder (`seed` from the site's ID), so
damage (missing panels, fallen walls, broken windows), dressing, the loot spots' positions and the guards' differ per
instance while the layout players learn stays. States (§7.5) change the dressing again.

### 8.5 Look-dev images

`docs/images/monuments/`: every monument from its views (§5's "Built" entries), and a scale chart of all of them
beside a person and the Tin Finch.

---

## 9. How monuments are placed (Proposal)

### 9.1 Sites from cells

Placement is local and deterministic, like the scatter (GROUND.md §4.6) and Minecraft's structure grid (§13):

- The cube-sphere's tiles at one level per tier hold at most one **candidate** each: roadside level 15 (about 300 m),
  small 14 (600 m), medium 13 (1.2 km), large 12 (2.5 km), landmark 9 (20 km) on the first world.
- A candidate's position is jittered inside its tile by `Hash64(seed, tier, tile)`; its kind is drawn from the tier's
  table weighted by the planet's history and the ground there (biome, height, slope, coast, river, ore); it is kept if
  the ground suits it (flat enough across its pad, dry, not on ice or cliffs unless it is meant to be) and if no
  bigger monument's pad is near (bigger tiers are decided first, so nothing is ever dropped for lack of room).
- **Lines** (roads, power lines, pipelines, rail) join a region's monuments as a minimum spanning tree plus a few
  extra links, routed along gentle ground; roadside sites are then placed along the lines (substations at junctions,
  every few km).
- A site's **ID** is its tier, tile and index, a natural feature's ID as in ARCHITECTURE.md §7: nothing is stored
  until a player changes something there (a crate looted, a door opened).
- Every zone origin (a landing site) gets an Exchange or a small site with a recycler within a few hundred metres,
  so arrivals always find their footing.

### 9.2 Density by history

Colonised worlds hold the tiers' full density inside their old regions (a few regions per planet, each 50–200 km
across, around a landmark) and a tenth of it between them; outposts and industrial worlds a tenth; untouched worlds
none but crash sites and events. Rarity is per hour of play, not per planet: a player who lands anywhere on a
colonised world finds a roadside site within minutes and a large monument within a short flight.

### 9.3 The pad: terrain under a monument

The planet surface is a pure function (GROUND.md §4.6), so monuments shape it inside that function, and the server's
collision and the client's terrain still agree to the bit:

- `PlanetSurface.Sample` finds the sites near a point (the tiles round it at each tier, cached per tile) and blends
  the natural height toward the site's **pad height** (the mean natural height under its footprint) inside the pad's
  shape, back to nature over its blend distance (10 m for roadside sites, 60 m for landmarks).
- Materials: inside the pad the ground becomes the pad's material (gravel, concrete, asphalt, dirt), blended at the
  edge; plants are cleared there and for the pad's `clear` distance beyond.
- Sites never sit where the blend would need more than a set cut or fill (8 m for roadside, 40 m for landmarks).
- The golden test of the surface (GROUND.md §3) changes where a golden point falls inside a pad; that update is made
  on purpose when this lands, and said so in the commit.

### 9.4 Events

Each zone keeps a list of candidate event spots per kind (from the same cells), and each kind has a period. For
period window `w`, `Hash64(seed, kind, w)` picks whether one fires, where and when inside the window. The server
skips spots near players and building pieces and announces what fires.

---

## 10. How monuments run in the game (Proposal)

| Part | What |
|---|---|
| Surface (`Hollow.Surface`) | `MonumentSites`: sites per tile and tier, pads, lines; `PlanetSurface.Sample` blends pads; materials and vegetation cleared |
| Data (`game/data/monuments`) | Each monument's JSON (§8.3) and `kit/*.json`; a table of kinds per tier and history |
| Simulation (`Hollow.Simulation`) | `World/Monuments.cs`: static bodies from the JSON's collision near players (a physics ID range of their own); markers become world props (crates and barrels with their tables, lockers, doors with chip readers, recyclers); zones (contamination, safe, no-build) |
| Server | Event scheduling (§9.4), guards (later), the safe zones' rules, what players changed (`prop_overrides` already covers looted crates) |
| Client (`game/scripts/Ground`) | `MonumentsView`: loads a monument's GLB and its kit pieces when it comes within its farthest level's distance, places them in the zone frame, switches levels by distance, lights at night; the map names monuments |
| Network | Monuments are generated on both sides, so only their props' state crosses the wire, as world props do now |

---

## 11. Steps

| Step | What | Done when |
|---|---|---|
| **M1 Kit and look** | The surfaces, the shader, the Builder, the kit; the first monuments built and rendered | The owner approves the look from the renders |
| **M2 The catalogue** | Every monument of tiers R, S, M, L and X built and rendered; the scale chart | Every "Built" entry in §5 filled |
| **M3 In the world** | Sites, pads and lines on the first world; the client draws monuments; collision; crates, barrels and recyclers at the markers | Walk into the substation and the processor works on the first world, at their real scale, in the game |
| **M4 The ladder** | Chips, power cells, locked doors, lockers, contamination zones and protection, loot tables per tier | A player runs from roadside sites to a violet room |
| **M5 Guards and events** | Reclaimers and Wardens, the Sentinel Crawler, the Gunship; events E1–E9 | An Orbital Fall draws two teams into a fight over the sealed pods |
| **M6 Variation and states** | Per-site seeds, damage and dressing, states, epochs | No two substations look the same |

---

## 12. Open questions

1. The names (Seedward, the Quiet, Wardens, Reclaimers, chips) are proposals; the owner may rename anything.
2. Is there anything not human in the galaxy (§5.6, the Anomaly)?
3. Planet epochs that re-roll buried sites (§6): wanted, and how long a cycle?
4. Do safe zones exist on every colonised world, or only near the starting region?
5. The exact component families per monument family (§7.7).
6. How deep the underground goes (tunnels between transit stations, the drainage works): its own work package?

---

## 13. Research

Done on 13 October 2026 for this document: Rust's monuments (Facepunch's devblogs and wikis), Dune: Awakening's
points of interest (Funcom's posts, patch notes and the community wiki, to patch 1.5), and Empyrion with No Man's Sky,
Starfield, Star Citizen and others (playfield files, developer guidelines, reviews). The notes, with their sources,
are kept on the owner's PC in `research_notes/Monuments/` of the main checkout (excluded from git, like the Elite
research). What each taught is in §2 and §6.
