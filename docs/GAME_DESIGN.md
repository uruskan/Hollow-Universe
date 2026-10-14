# Hollow Universe — Game Design

Status: draft 1, 10 October 2026; the scale (pillar 1, §4) revised on 14 October 2026 ([SCALE.md](SCALE.md)). Owner:
Yusuf. This is what the game is. [ARCHITECTURE.md](ARCHITECTURE.md) says how it's built.

Every rule is marked:

- **Decided**: Yusuf said so.
- **Proposal**: a suggestion waiting for a yes or no.
- **Open**: not decided yet.

---

## 1. The game

Hollow Universe is an online survival game set in the real Milky Way's shape, in a few hundred star systems at its
famous places: real clusters, nebulae and stars.

You fly your ship between real and generated star systems. You land on a planet, claim land, gather
resources, build a base, and fight other players for it. Over weeks and months, one base becomes an empire:
tens of bases scattered across the galaxy, miners working while you sleep, rare loot carried home from
monuments on dangerous worlds, and a ship and gear you've engineered yourself. Anything you own can be taken
from you, online or offline.

Think of Elite Dangerous's galaxy and flight, with the ground game of Rust: base building, raiding and
full-loot PvP. Add the planets and exploration of No Man's Sky and Astroneer, and crafting progression like
Dune: Awakening.

The setting is as simple as Rust's (Decided, [LORE.md](LORE.md)): a great human civilisation spread across the
galaxy and vanished, nobody knows why, and the monuments are what it left. Nobody knows who the players are. No
factions, no aliens, no magic; technology is Rust's a step further, built from scrap.

---

## 2. Pillars

| # | Pillar | Meaning |
|---|---|---|
| 1 | **The real galaxy** (Decided) | The real Milky Way's shape and its famous places, not a stack of procedural galaxies: a few hundred star systems at real clusters, nebulae and stars, on the disc's mid-plane. Not 1:1: the owner replaced the ~120 billion systems on 14 October 2026, because thousands of dots overwhelm ([SCALE.md](SCALE.md)). |
| 2 | **Everything can be taken** (Decided) | Full-loot PvP and raiding, 24/7, including while you're offline. Losing a base is a setback, not the end: you have others. |
| 3 | **An empire you can see** (Decided) | Progress isn't a level number. It's your bases, miners, ships and gear spread across the galaxy. No limit on the number of bases. |
| 4 | **Earned in the world** (Proposal) | Power comes from resources, monuments and engineering, not from a shop. |
| 5 | **Big and quiet, then sudden** (Decided) | Space is huge and encounters with other players are rare. When they happen, they matter. |
| 6 | **Original** (Decided) | Our own names, art and ships. Other games' mechanics, numbers and UI design may be borrowed (the ground game uses Rust's, GROUND.md §1); their files never are. |

---

## 3. Core loops

| Time scale | Loop |
|---|---|
| Minutes | Fly, land, gather, craft, fight. Survive the next encounter. |
| Hours | Build or extend a base. Set up a miner. Raid someone, or defend. Explore a system for something rare. |
| Days | Claim a second world. Supply bases. Run monuments for special loot. Engineer ship and gear upgrades. |
| Weeks to months | Grow the empire across systems. Rebuild after losses. Become the player other players talk about. |

---

## 4. The universe

| Topic | Rule | Status |
|---|---|---|
| Galaxy | The real Milky Way's shape, with a few systems at each landmark: real clusters and nebulae, famous real stars and exoplanet systems, the Galactic Centre and a few far objects. About 200 to 300 systems, all on the disc's mid-plane (owner, 14 October 2026). The list is a proposal: SCALE.md §4. | Decided |
| Travel between systems | Frame shift drive jumps, limited by jump range. A jump is a short hyperspace sequence. | Decided |
| Travel between landmarks | Gaps of hundreds to thousands of light-years. Jump range, drives, routes and the autopilot are decided with the space gameplay (SCALE.md §7). | Open |
| Travel inside a system | Supercruise between bodies; drop to normal space near a target. Planets keep their real orbits (owner, 14 October 2026). | Decided |
| Planets | Procedural spherical planets you can land on, with biomes on worlds that have atmospheres. | Decided |
| Planet size | Small: an Earth-like world is about 2.4 km in radius, about four 4.2 km Rust maps, and other bodies scale from their real size. Their real gravity, air, temperature and day drive play, clamped only at the extremes (owner, 14 October 2026). The size rule and the bands are proposals: SCALE.md §3. | Decided |
| Which bodies are landable | Every body with a solid surface of rock or ice (not giants or global oceans). Its physical state (air, temperature, gravity, life) and hazards (GALAXY.md §10.3) decide what it takes to survive there. | Proposal |
| Where players start | Around Sol. Nothing there is alive any more, as nowhere is (LORE.md §2). | Proposal |
| Rarity | Rare resources and monuments depend on the type of star and planet (for example, rich metals on metal-rich worlds, exotic materials near neutron stars). They're spread across the whole galaxy, not concentrated in a few systems. | Decided |
| Monuments | Special sites on certain planets that hold special loot and are dangerous to run. | Decided |
| Universe time | Real time: planets orbit and rotate continuously, the same for everyone. Whether rotation is compressed so days on the ground are short enough to play is open (GROUND.md §11). | Proposal |

---

## 5. On the ground

Almost the full Rust gameplay loop, on planets. (Decided.) The first world is a real generated planet, Nolcas
Peris CQ-SLY 7-0 B 3. [GROUND.md](GROUND.md) has the details, the build order and the decisions of 12 October 2026.

| Topic | Rule | Status |
|---|---|---|
| Survival | Health, food, water, temperature; the planet's atmosphere, temperature and gravity matter. | Proposal |
| Gathering | Trees, ore, plants and planet-specific materials; tools improve yield. | Decided |
| Crafting | Crafting stations with tiers (like Rust's workbenches). Ground gear crafted and upgraded like Dune: Awakening. | Decided |
| Building | Rust-style building pieces with grades. Doors, locks, storage, deployables. | Decided |
| Claims | A claim device (our own Tool Cupboard equivalent) controls building rights in an area and holds upkeep. | Proposal |
| Decay and upkeep | Bases decay unless upkeep is paid from the claim's storage. | Proposal |
| Number of bases | No limit per player. | Decided |
| Combat | Firearms and melee, full loot on death. | Decided |
| Raiding | Explosives and tools break in. Any base can be raided at any time, online or offline. | Decided |
| Respawn | At a bed or sleeping bag you own, anywhere in the galaxy. Killing yourself (F1) to respawn inside a base that's under attack is allowed. | Decided |
| Respawn cooldown | Each bed has a cooldown after use. | Proposal |
| Raid alerts | Owners get a notification when their base is attacked, so they can respawn there to defend. | Proposal |
| Miners and automation | Machines that extract resources over time while the area is unloaded. | Decided |

---

## 6. In space

| Topic | Rule | Status |
|---|---|---|
| Ships | Ready-made hulls with modules (drives, weapons, shields, cargo, utility), Elite-style. | Decided |
| Ship upgrades | Engineering upgrades crafted from rare materials. | Decided |
| Flight controls | Mouse controls pitch and yaw, Q/E roll, A/D strafe. | Decided |
| More flight controls | W/S throttle, Space/Ctrl up and down. | Proposal |
| Cameras | Third person and a free camera. | Decided |
| Cockpit view | A first-person cockpit camera as well. | Proposal |
| Flight model | Six degrees of freedom with flight assist (on by default), boost. | Proposal |
| Supercruise | Fast travel inside a system. | Decided |
| Supercruise speed | Depends on nearby masses: fast in open space, slow near planets and stars. | Proposal |
| Docking | Landing pads on stations and on player bases. | Proposal |
| Ship combat | Weapons, shields, hull and modules can be damaged. | Proposal |
| Ship loss | A destroyed ship drops its cargo. Whether hulls are insured or lost: | Open |

---

## 7. Progression: the empire

| Topic | Rule | Status |
|---|---|---|
| What progress is | More bases, better bases, miners, better ships and gear. | Decided |
| Special loot | Monuments on certain planets give rare items and materials. | Decided |
| Ground gear | Crafting tiers and upgrades in the style of Dune: Awakening. | Decided |
| Ship enhancement | Engineering upgrades to modules. | Decided |
| What carries over between wipes | | Open |
| Blueprints and unlocks | Learned by finding, researching or experimenting. | Open |

---

## 8. Players and social

| Topic | Rule | Status |
|---|---|---|
| Teams | Small teams that share building rights, map markers and chat. | Proposal |
| Clans | Larger groups. | Open |
| Trading and money | | Open |
| Chat | Local, team, direct; global if a shard is small. | Proposal |

---

## 9. Servers and persistence

| Topic | Rule | Status |
|---|---|---|
| Servers | Shards running 24/7; one shard is one whole universe. | Decided |
| Wipes | Undecided; can be added later. The architecture supports both. | Open |
| Players per shard | Target 1,000 concurrent. | Proposal |

---

## 10. Content rules

- Rust, Elite Dangerous, No Man's Sky, Astroneer and Dune: Awakening are references for feel and mechanics.
  Their mechanics, numbers and UI design may be used (the ground game follows Rust's; owner's decision,
  12 October 2026). Their files (models, textures, animations, sounds, maps, monuments) and names never are.
- Real star data comes from public catalogues, credited under their licence (GALAXY.md §8).
- Every generated name uses our own syllable tables.

---

## 11. Open questions

1. Wipes: never, per season, or per shard type?
2. What survives a wipe (blueprints, cosmetics, nothing)?
3. Ship loss: insurance, or lose the hull?
4. Money: a currency, barter, or both?
5. How blueprints are unlocked.
6. Team and clan size limits.
7. How new players are protected (if at all) in the starting region.
8. Travel between landmarks: jump range, drives, routes, the autopilot (SCALE.md §7).
