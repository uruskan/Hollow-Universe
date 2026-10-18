# Hollow Universe — Game Design

## Core Concept

A full sandbox survival game with space travel, 24/7 online play,
full-loot PvP, base building, and base raiding. Set in the 3000s after
humanity's near-extinction, it combines extremely advanced technology
with player-made, craftable equipment and structures whose handmade
construction remains apparent.

The primary gameplay foundations are **Rust** and **Empyrion - Galactic
Survival**. The world must also offer meaningful, long-term play when
only one player is online, supported by exploration, production, and
other renewable activities alongside PvP.

## Game Title

The chosen game title is **Hollow Universe**.

### Meaning Behind Hollow

The creator envisions an enormous galaxy. The appeal of **Hollow** comes
from the feeling that, against this vast scale, everything is ultimately
empty, with a somewhat nihilistic undertone. This is the philosophical
motivation behind the preferred name.

## Confirmed Features

- Full sandbox survival.
- Long-term replayability and engaging play even with only one player
  online, with additional gameplay inspirations from Dune: Awakening,
  Elite Dangerous, Empyrion - Galactic Survival, Starfield, Star Citizen,
  Astroneer, and Satisfactory.
- New players start on **Earth** with only **one torch and one rock**,
  progressing from basic gathering and stone tools into base building,
  resource control, spacecraft, and interplanetary exploration and PvP.
- Hunger, thirst, and a shared breathing/suffocation system supported
  by oxygen-providing equipment.
- Workbench 1, 2, and 3 with tier-specific tech trees and crafting-speed
  benefits; separate, unlevelled Vehicle and Engineering Workbenches.
- Sequential technology unlocks using salvage in the standard workbench
  trees, and recipe-specific workbench requirements independent of speed.
- An alternative learning path through the Research Bench: research an
  obtained item using salvage, consuming the item to produce a learnable blueprint.
- Rare item blueprints absent from the normal tech trees can be found
  at some monuments or other spawn locations.
- Higher-performance variants of existing item types can be learned
  from blueprints found at monuments or loot spawns and crafted; these
  variants are absent from the normal tech trees. A faster-smelting
  furnace retains the basic furnace identity and function.
- Spacecraft technology unlocks through the Vehicle Workbench, which
  is also required for crafting spacecraft.
- Spacecraft customization through defined modules, inspired by Rust's
  modular cars; learned and crafted rare engines or weapons can be
  installed to make a ship more powerful than standard options.
- Small spacecraft can carry one or a few people with a single cargo
  storage location; large spacecraft have walkable interiors where players
  can deploy storage boxes. Ships can be raided and captured like bases.
- Pilot-operated cockpit weapons and separate player-operated cannon
  stations; the Iron Wake's largest cannons are operated by players at
  their individual stations. Aiming uses a crosshair and readable shot feedback.
- Space travel.
- Elite Dangerous-inspired space travel: normal flight, supercruise
  within a star system, and charged frame shift drive jumps between
  systems, with fuel scooping and travel times that are not excessively long.
- 24/7 online play.
- Full-loot player-versus-player combat.
- Rapid progression through defeating better-equipped players or offline
  raiding is possible, including for newer players using simple weapons.
- Without a Sleeping Bag or Bed respawn point, players respawn on a
  random beach on the planet where they died. With such points, they can
  choose their own bag or bed on any planet or spacecraft, subject to
  cooldowns in the **1–5 minute** range.
- Inventory items dropped on the ground form lootbags; nearby drops
  combine into one lootbag.
- Base building.
- Base raiding.
- Bases and spacecraft persist in the world, with optimization required
  to support persistent structures, vehicles, and their contents.
- Rust provides the default building and raiding rules, subject to the
  specific Hollow Universe requirements recorded here.
- Rust-based structural building using Building Paper and a wooden-looking
  hammer, with a Tool Cupboard for claim coverage and upkeep storage.
- Reliable snapping and colliders for deploying items inside player-built bases.
- Deployable placement within a Tool Cupboard's claim requires authorization
  on that cupboard, with an exception for ladders placed on walls.
- An Auto Turret that consumes 10 energy, fires its installed weapon and
  ammunition at enemies, and automatically protects players authorized on
  its associated Tool Cupboard, subject to per-device clearing in AUTH-01.
- A Rust-based electricity system with solar panels and wind turbines
  for generation, batteries for storage, and cables supplying devices
  such as Auto Turrets.
- A Wire Tool connecting physical cube ports, with visual cable-routing
  anchors and a maximum of 20 m between endpoint ports; generators have
  one output, batteries one input/one output, and Auto Turrets/SAM sites
  one input/one passthrough each.
- Shared defense authorization: automatic Tool Cupboard protection by
  default, with per-device self-authorization, self-clear, clear-all and
  enable/disable actions. Clearing removes inherited protection too.
- Electricity and technology systems inspired broadly by Rust,
  Dune: Awakening, Astroneer, and Empyrion - Galactic Survival.
- Player-made, craftable technology with a visibly handmade character.
- Subsistence as a direct reference to emulate for the world's appearance,
  atmosphere, and feel during play.
- Sandbox gameplay without an authored storyline.
- A galaxy with at most 1,000 stars and a presentation combining Stellaris
  references with the previous Hollow Universe project; matching the real
  galaxy is optional.
- Planet, orbit, composition, and planet-type generation reused from the
  previous project, with planet sizes scaled to a maximum total surface
  area of approximately 23.04 km².
- Planet-size-dependent, randomized, configurable monument generation.
- Realistic planetary biomes, fauna, and resource distribution, with
  gatherable nodes and mineable ore veins.
- Changing planetary weather, including rainy, foggy, snowy, and sunny conditions.
- Continuous ship travel in both directions between a planetary surface,
  orbit, and space, without loading screens during descent or ascent.
- First-person player UI matching Rust exactly, reused unchanged from
  the previous project's ground UI.
- A shared interaction system using tap-E actions, hold-E pickup progress,
  and radial menus for multiple actions.
- A simple Rust-style health bar showing a damaged target's remaining
  health, consistently supported by every damageable target.

## Player Journey and Core Gameplay Loop

The following describes an example player journey through the sandbox.
Activities overlap and can recur throughout play; the stages describe
progression rather than a mandatory sequence of objectives or unlocks.

### LOOP-01 — Starting on Earth

The player initially spawns on the starting planet, **Earth**, carrying
only **one torch** and **one rock**, following the Rust-style starting
experience.

This defines the initial start. Respawning after death follows SURV-04.

The rock is a basic **gathering tool**. It can harvest trees and resource
nodes, but gathering with it is slow and inefficient. Harvesting trees
provides **wood**, and harvesting stone nodes provides **stone**. The
player uses these initial resources to craft a **Stone Axe** and
**Stone Pickaxe**, then continues developing their equipment.

### LOOP-02 — Early Survival, Looting, and a First Base

While gathering and crafting, the player explores monuments, smaller
monuments, and spawned loot. They establish a base, learn and craft
better tools, and hunt animals. They may need to flee animals when
unarmed or otherwise unable to handle the encounter.

Other players can become opponents during this early progression.
Crafted weapons such as a **bow** or **spear** allow early PvP fights
alongside gathering, looting, hunting, and building.

### LOOP-03 — Expansion and Resource Control

As the player progresses, they expand their base, place **autominers**,
and establish control over nearby areas and **ore veins**. Deploying
**Auto Turrets** helps defend their bases and resource operations.
These activities give players assets and resource access to compete
over and defend.

### LOOP-04 — First Spacecraft and Interplanetary Activity

The player eventually crafts a spacecraft and gains access to other
planets. They can raid other players' bases there, including bases
still in an early stage of development, or establish additional mining
operations on another planet. They transport gathered resources and
loot back to their main base.

They can also venture farther into space, explore new monuments,
collect loot, and learn technologies and blueprints. The established
crafting, module, and advanced-variant rules govern those upgrades.

### LOOP-05 — Continuing Competition and Progression

Throughout this journey, players can fight other players, raid their
bases, or have their own bases attacked and raided. Gathering, crafting,
building, resource control, exploration, hauling, and PvP remain
connected activities as the player's reach expands across planets.

New players can accelerate their progression by defeating better-equipped,
established players even with a simple crafted weapon and taking their
loot. **Offline raiding is possible:** players can attack and raid a base
while its owner is offline. These opportunities are intentional routes
to rapid progression within the sandbox. The presence of established
players and their territory on Earth is accepted within this competitive
design, rather than treated as a problem that must be removed.

The recurring loop is **gather and loot → craft and build → expand
resource access → explore and compete → bring resources and loot home
→ improve equipment and infrastructure**, with player attacks and raids
creating ongoing risk along the way.

## Lore and World History

### LORE-01 — Humanity Before the Collapse

The game takes place in the 3000s. Before the catastrophe, humanity and
its technology had become extraordinarily advanced, and human civilization
had spread throughout the universe.

### LORE-02 — The Hollow Men and the Catastrophe

Humanity was betrayed by the **Hollow Men**, artificial intelligences whose
conceptual inspiration is the Men of Iron from Warhammer 40,000.
The resulting catastrophe wiped out almost all of humanity.
During the same catastrophe, the Hollow Men were also completely destroyed.

The precise year, the time elapsed since the catastrophe, the reasons for
the betrayal, and the circumstances of the Hollow Men's destruction have
not yet been specified.

### LORE-03 — The Survivors

The players are among the surviving humans. They use extremely advanced
technology while making and assembling their own equipment and structures.
Space travel and other advanced capabilities coexist with visibly
handmade construction. Advanced function and player craftsmanship are
central to the intended technological mix.

The intended condition of humanity is desperate. In the creator's
Warhammer 40,000 comparison, humanity has no Emperor-equivalent figure
and is in an even worse predicament.

### LORE-04 — Planetary Monuments

On planets, players can encounter monuments and remnants left behind by
the former human civilization. Very rarely, they can also encounter
monuments left by the Hollow Men. These are remnants of their former
presence following their complete destruction.

Specific monument types, contents, and gameplay interactions have not
yet been defined.

### LORE-05 — A Secretive Surviving Human Faction

A small human faction also survived the collapse of human civilization.
This faction is extremely secretive and seeks to retain control of
monuments throughout the universe.

This is established background lore. If monument NPCs are added in the
future, this faction provides the lore basis for their presence and
their role in holding those sites. The creator's reference is the role
of Rust's scientists guarding some monuments.

Adding monument NPCs remains a possible future feature. The faction's
name and further details have not been defined.

### LORE-06 — Narrative Approach

Hollow Universe will have no authored storyline. Its lore provides an
internal foundation for the world and its inhabitants, including the
possible future monument NPCs.

The creator does not currently expect to explain this background
directly to players. The faction can support consistent world design
without requiring a player-facing story about it.

## Visual Direction

### ART-01 — Advanced Technology with Handmade Construction

The guiding principle for equipment, structures, vehicles, and technology
is that they are player-made and craftable. Their appearance should
communicate that people made and assembled them, even when their
functionality is extremely advanced.

An enormous spacefaring ship can embody sophisticated technology while
still visibly reflecting the work of its builders. Handmade construction
is the defining visual quality.

Rust, scrap materials, damage, and rough finishing are optional visual
choices for individual assets, not requirements for every asset.
Clean, well-maintained, and carefully crafted objects also fit this
direction. Materials, surface condition, and finishing are defined
for each asset within the shared handmade construction principle.

### ART-02 — World Appearance and Feel: Subsistence

Use **Subsistence** as a direct reference to emulate for how the world
**looks and feels to the player**, including its environmental atmosphere.

The creator finds that Subsistence produces a distinctive feeling despite
its older, non-AAA visual presentation. That distinctive experience is
the quality to capture in Hollow Universe's world. The reference
establishes a target for the world's atmosphere and feel; it does not
set a ceiling on graphical quality.

Apply this direction alongside ART-01's advanced, handmade technology
and the established planetary and biome requirements. The particular
visual and experiential qualities responsible for the desired feeling
remain to be identified in more detail through reference examples.

### ART-03 — Tool Handling and Detailed Gameplay References

On 17 October 2026, the creator accepted the metal hatchet and metal pickaxe
**holding poses shown in their screenshots** as the local baseline, while
rejecting the angle/position of other tools. This acceptance does not cover
the complete equip, walk, run, strike and recovery performances.

The creator supplied [HOW TO chop trees in RUST](https://www.youtube.com/watch?v=JTTJlCEwfUE)
(VID-TOOLS-01, especially 25.73–36.60 seconds for chopping and movement) and
[Rust Official Trailer](https://www.youtube.com/watch?v=LGcECozNXEw)
(VID-WORLD-01, especially 7.47–21.28 seconds for tool handling; detailed world
and action references throughout) for close study. Use these to inform hand
choreography, movement, contact feedback, sound and world presentation,
alongside ART-02 and the existing ground UI requirement.

[Tool handling and world presentation study](REFERENCE_STUDY_TOOLS_WORLD.md)
records the accepted captures, timestamped observations, implementation audit,
proposed repairs and remaining audio/validation limits. Its proposed details
do not automatically become confirmed mechanics or authorize importing the
reference game's assets. Every tool must be evaluated for its own geometry
and task; a shared implementation must still support distinct performances.

### Reference Intent

The creator describes the broad premise as Rust-like survival and
reconstruction extended into space. Rust is a reference for player-built
survival and construction; the visual reference concerns the sense of
making and assembling things. It does not require every asset to copy
Rust's materials or weathering.

Warhammer 40,000 is the conceptual reference for humanity's lost
technological peak, an AI betrayal, and the coexistence of advanced
technology with crude or manual practices after a catastrophic decline.
The creator's example of spacecraft alongside manually loaded cannons
illustrates this technological mix. Player-operated cannon stations are
defined in SHIP-05; their specific loading/reloading process remains
to be specified.

## Galaxy and System Presentation

### GAL-01 — Galaxy Layout and Star Count

The galaxy may draw on the real galaxy, but **matching the real galaxy
is optional**. Its layout and scale must support the intended travel
experience in TRAVEL-02. It will contain **a maximum of 1,000 stars**,
with Stellaris as the creator's scale and presentation reference.

The specific layout, star selection, and extent of real-galaxy data use
remain to be specified within these requirements.

### GAL-02 — Galaxy, Star System, and Planet Presentation

Stellaris is the primary reference for how the galaxy, stars, star
systems, and planets are presented, including the galaxy map and its
UI/UX. The creator values the clarity and usability of these views as
the direction for simplifying the new game's space presentation.

The final design will combine this reference with selected elements of
the previous Hollow Universe project's galaxy-map approach. The exact
combination is still to be developed into the game's own map design.

### GAL-03 — Previous Project as a Reference

Reference project: `D:\GameDev\Projects\Hollow Universe`.

The previous project drew inspiration from Elite Dangerous. The creator's
earlier dissatisfaction with its scale, travel and galaxy map led to the
initial direct-flight design. On **18 October 2026**, the creator chose
Elite Dangerous-inspired travel again, specifically including frame shift
drive charging and fuel scooping, as defined in TRAVEL-02 through TRAVEL-05.
Galaxy-map presentation continues to follow GAL-02. The older project's
travel plans are reference material, not evidence of implemented travel.

The previous project's star counts and implementation choices are
reference material; the choices for this design will be specified here.
Map dimensionality and detailed map interactions remain to be defined.
Physical travel between planets and systems follows TRAVEL-02.

## Star Systems and Planetary Worlds

### SYS-01 — Reuse Existing System Generation

The previous Hollow Universe project already provides the generation of
planets within star systems, their orbits, material composition, and
planet types. Reuse that existing work as the basis for these systems.
Apply the planet-size requirements in WORLD-01 to the new game's scale.
The reuse decision covers this generation work. The playable space
scale, travel experience, and map presentation follow the new design
in GAL-02, GAL-03, and TRAVEL-02.

Existing implementation references:

- [Galaxy generation documentation](../Hollow%20Universe/docs/GALAXY.md).
- [SystemGenerator.cs](../Hollow%20Universe/src/Hollow.Galaxy/Systems/SystemGenerator.cs)
  and [Orbit.cs](../Hollow%20Universe/src/Hollow.Galaxy/Systems/Orbit.cs):
  system generation and orbits.
- [PlanetState.cs](../Hollow%20Universe/src/Hollow.Galaxy/Systems/PlanetState.cs)
  and [WorldBuilder.cs](../Hollow%20Universe/src/Hollow.Galaxy/Systems/WorldBuilder.cs):
  planetary composition, atmosphere, climate, geology, biosphere, and type.

### WORLD-01 — Planet Size

Planet sizes will be scaled relative to their real sizes. The largest
planet must have a total surface area of approximately **23.04 km²**, the
area of a **4.8 km × 4.8 km** map. This is the upper limit; other planets
may be smaller, and no planet may exceed it.

The creator explicitly confirmed that this limit refers to the planet's
**entire surface area**. For a spherical planet, the equivalent radius
is approximately **1.354 km**, and its diameter is approximately
**2.708 km**, derived from `surface area = 4 × pi × radius²`.

The precise scaling function relating real planet sizes to game sizes
has not yet been defined.

On 16 October 2026, the creator approved increasing Earth's land proportion
and generating larger connected landmasses, while retaining the **23.04 km²
maximum total surface area**. Earth's playable geography may therefore differ
from its physical ocean-cover metadata. The exact land percentage and terrain
parameters are implementation tuning, to be measured and playtested.

### WORLD-02 — Monument Generation

Monuments will be distributed on planets according to planet size and
random variation. Monument generation must also support explicit
configuration and overrides.

Use the existing monument archive from the previous project. Its asset
definitions and previews are reference material for selecting monuments.
The historical human and rare Hollow Men origins are described in
LORE-04; these remain part of their worldbuilding context.

#### Existing Monument Archive

The archive contains **55 built monument types represented by 57 GLB
model variants**, with matching JSON definitions, plus **64 shared
modular GLB parts**. Examples include Mining Camp, Research Outpost,
Launch Complex, Frigate Wreck, Colony Ruins, and Atmosphere Processor.

| Reference | Contents |
| --- | --- |
| [Monument catalogue](../Hollow%20Universe/docs/MONUMENTS.md) | Monument descriptions, design categories, and production notes |
| [Model archive](../Hollow%20Universe/game/assets/monuments/) | Built monument GLBs and the shared `kit/` |
| [Monument definitions](../Hollow%20Universe/game/data/monuments/) | JSON metadata for the built models |
| [Preview archive](../Hollow%20Universe/docs/images/monuments/) | 177 JPG previews and scale images |
| [All-monument scale overview](../Hollow%20Universe/docs/images/monuments/scale-all.jpg) | Visual index of the existing models |
| [Source scripts](../Hollow%20Universe/art/monuments/) | Existing monument production scripts |

These are available source assets; the final selection and dimensions
must be evaluated against WORLD-01's planet-size limit. The old
catalogue's placement section is a proposal. Size-dependent, randomized,
configurable placement is a requirement for this design, not a verified
completed feature inherited with the asset archive.

### WORLD-03 — Biomes, Fauna, and Resources

Planetary biomes and fauna should be realistic. Resource distribution
must be informed by real-world knowledge and the planet's characteristics,
including its type and composition.

Existing work to consult includes
[PlanetSurface.cs](../Hollow%20Universe/src/Hollow.Surface/PlanetSurface.cs),
[Biomes.cs](../Hollow%20Universe/src/Hollow.Surface/Biomes.cs), and
[WalkingWorld.cs](../Hollow%20Universe/src/Hollow.Surface/Walking/WalkingWorld.cs)
for terrain and biomes;
[Scatter.cs](../Hollow%20Universe/src/Hollow.Surface/Scatter.cs) for resource
placement; and
[animals.json](../Hollow%20Universe/game/data/ground/animals.json) with
[AnimalWorld.Spawning.cs](../Hollow%20Universe/src/Hollow.Simulation/Animals/AnimalWorld.Spawning.cs)
for existing fauna and spawning.

The old fauna implementation covers the first world's deer, boar, wolf,
and bear, and the resource scatter uses terrain and biome conditions.
The full requirement for biome, fauna, and resource distribution across
planet types is not assumed to be complete in that implementation.

### WORLD-04 — Changing Weather

Weather on planets can change over time. Conditions can include rain,
fog, snow, and sunshine. The weather types available on each planet,
their frequency and transitions, and any gameplay effects remain to
be defined.

### RES-01 — Gathering and Ore Veins

Resources include nodes that can be gathered by hand and ore veins that
can be extracted using **autominers**, **mining ships**, or **tools**.
Ore veins must support placing autominers on them for extraction.

Early gathering includes wood from trees and stone from stone nodes,
initially using the starting rock before crafting stone tools (LOOP-01).
The complete resource catalogue, extraction rates, depletion, and
regeneration rules remain to be specified.

### TRAVEL-01 — Continuous Planetary Descent and Ascent

Players must be able to travel in their ship through both of these
continuous routes:

- Space → orbit → planetary surface.
- Planetary surface → orbit → space.

Both descent and ascent must be continuous and realistic, with no
loading screen during either transition. The player can leave the
surface in their ship, reach orbit, and continue into space.

The travel modes in TRAVEL-02 may change during the journey, with visible
entry/exit effects and appropriate flight controls. The player stays with
the same ship and crew. This continuity requirement does not require one
flight speed or one physics region for the whole journey. An interstellar
hyperspace sequence is part of travel, rather than planetary descent/ascent.

### TRAVEL-02 — Normal Flight, Supercruise and Interstellar Jumps

**Confirmed revision, 18 October 2026:** the creator requested an
Elite Dangerous-inspired travel experience, including frame shift drive
charging and fuel scooping. This replaces the former requirement for
direct compressed-distance flight without supercruise.

The travel model has three modes:

| Mode | Purpose |
| --- | --- |
| **Normal flight** | Ship handling, combat, close manoeuvring, planetary takeoff and landing. |
| **Supercruise** | Pilot-controlled fast travel between planets and other destinations within the same star system, with entry from and exit to normal flight. |
| **Interstellar jump** | A charged drive jump to a selected star system, shown through a hyperspace travel sequence and arrival in that system. |

Travel must have readable preparation, movement and arrival, with journey
times that are **not excessively long**. System distances, supercruise
speed and acceleration, jump range and route lengths must be tuned together.
Neither real astronomical distances nor the former compressed-flight scale
are mandated by this revision. Map diagram coordinates are not flight distances.

The galaxy remains limited to **1,000 stars**, planets to **23.04 km² total
surface area**, and initial spawn remains Earth with one rock and one torch.
GAL-02 continues to govern map presentation and UI-01 the ground interface.
Adopting this travel model does not restore all historical design decisions.

### TRAVEL-03 — Drive Charging and Jump Experience

Drive charging is an explicit part of the player's flight experience.
The pilot selects the intended travel mode/destination, initiates the drive,
sees charging and readiness feedback, and then enters supercruise or commits
to an interstellar jump. An interstellar jump has a readable countdown,
departure, hyperspace sequence and arrival. The interface must explain why
activation cannot proceed and allow cancellation before commitment.

Ship travel consumes fuel; interstellar jumps have a fuel cost and range
limit. The ship, crew, cargo and deployed contents remain the same persistent
objects throughout the journey. The reference term **frame shift drive
(FSD)** is used in these design notes; the final in-universe equipment name
and its crafting/unlock details remain to be chosen under SHIP-01/02.

Exact charge, countdown and cooldown durations, jump range, fuel formula,
arrival position and speed, and the rules for interruption are open. A
visible hyperspace sequence may cover destination preparation; its duration
and failure behaviour must be defined and tested, not hidden in a claim
that a map selection already transports the player.

### TRAVEL-04 — Fuel Scooping

**Fuel scooping is a confirmed feature.** Players can replenish spacecraft
fuel by flying near eligible stars with suitable ship equipment, supporting
exploration and return journeys. The flight interface must show whether
scooping is available/active, fuel gained and remaining tank capacity.

Proposed handling baseline: a fitted scoop collects while the pilot remains
in a suitable stellar region in supercruise; closer approaches trade higher
collection rate for heat exposure. The pilot manages position, fuel and
temperature, with clear warnings before dangerous exposure. This baseline
is a proposal for implementation and playtesting, not fixed balance.

Eligible star classes, scoop fitting/unlocks, collection rates, heat and
damage rules, and the fuel commodity remain open. The existing local-flight
fuel inventory is not proof of scoop support. Its relationship to scooped
fuel must be specified, with one authoritative fuel accounting system.

### TRAVEL-05 — Navigation, Handling Proposals and Completion

Navigation must support selecting bodies within a system and plotting
interstellar routes subject to drive range and fuel. It should expose
reachable destinations and refuelling opportunities. Charging, arrival,
scooping and warnings need clear visual/audio feedback using our own assets;
ship travel displays belong in the ship/map interface while preserving UI-01.

Reference-inspired proposals to resolve: mass-based limits on drive
activation, alignment/throttle requirements, slower supercruise near large
bodies, safe disengagement windows, emergency drops, heat damage and drive
cooldowns. Their exact rules, including effects on PvP escape, are not yet
confirmed. Interdiction, automatic route flying/scooping, neutron boosting
and supercruise overcharge are also undecided.

Reference: Frontier's [Elite Dangerous Player's Guide](https://hosting.zaonce.net/elite/website/assets/English-PlayersGuide_v2.00-Horizons.pdf),
flight manual pp. 68–69, describes the three travel modes and drive charging.
It informs this direction; its full equipment catalogue, numerical balance
and other mechanics are not automatically requirements for Hollow Universe.

**Implementation status:** the galaxy generator and map viewers exist, and
ships can cross the local orbital-altitude marker. The current four-radius
flight boundary remains. Connected space, supercruise, interstellar jumps,
drive charging and fuel scooping are not implemented by this documentation
change. [TRAVEL_IMPLEMENTATION.md](TRAVEL_IMPLEMENTATION.md) proposes the
integration and validation work; [LOCAL_FLIGHT.md](LOCAL_FLIGHT.md) records
the existing foundation.

## Player Interface

### UI-01 — First-Person Player UI

The normal first-person player interface will use Rust's UI exactly.
The creator considers the previous Hollow Universe project's ground UI
nearly completely successful at implementing this and wants that existing
implementation reused unchanged.

Source project: `D:\GameDev\Projects\Hollow Universe`.
Relevant existing files include `game/scripts/Ground/ClientGroundUi.cs`,
`game/scripts/Ground/ClientGroundUi.Interaction.cs`, and
`game/scripts/Ground/InventoryBody.cs`.

This requirement applies to the normal FPS/player interface. The galaxy
map follows its own design direction in GAL-01 through GAL-03, and ship
weapon aiming follows UI-04.

On **18 October 2026**, the creator requested a **DROP ALL** button in the
inventory. It drops every backpack stack onto the ground while keeping
the six **hotbar** slots (called the belt internally) and all equipped
clothing/armor. The creator explicitly confirmed backpack contents only.
Dropped items follow the shared lootbag rules in LOOT-01.

On 17 October 2026, the creator requested a held-item **admire** action,
bound to **N** by default: the player raises and turns the item to inspect it.
This is a cosmetic first-person action; ordinary item use takes priority.

### UI-02 — Radial Menus

Most menus in the game will be radial. Interactable objects with multiple
options use a radial menu, as described in INT-03.

### UI-03 — Health Feedback for Damageable Targets

Every damageable target must support visible health feedback. When a
player deals damage to a target, a simple Rust-style health bar appears
and shows its remaining health. Further damage is reflected in that bar.

The bar remains visible for a period after damage or until the target
is destroyed. The exact display duration and the choice between timed
visibility and persistence until destruction have not yet been specified.

Examples include hitting a loot barrel with a rock and shooting a
building wall with explosive ammunition. Both should reveal the target's
remaining health through the same general feedback system. These examples
illustrate a rule for all damageable targets, rather than separate
per-object UI implementations.

### UI-04 — Ship Weapon Aiming and Shot Readability

Ship cannons and weapons must provide robust, responsive aiming with
a **crosshair/aiming reticle**. The player should be able to understand
where the weapon is aimed and read its projectile flight, drop where
applicable, and impact feedback well enough to judge and correct shots.

The intended skill expression comes from deliberate aiming and reading
the shot, with clear controls and feedback. The absence of a crosshair
or unnecessarily obscure trajectory/impact feedback is not part of the
desired challenge. This requirement applies to both cockpit weapons
and independently operated gun stations in SHIP-05.

Reticle design, projectile behavior, and the exact feedback presentation
remain to be specified. Automatic lead/impact prediction, aim assistance,
and hitscan behavior have not been selected by this requirement.

### UI-05 — Weapon Aim and Optic Presentation

On 17 October 2026 the creator requested a consistent screen-centre aim reference
for projectile weapons, including player-operated gun stations. The holosight dot
must show that reference; the 4× scope image and reticle must stay synchronized
with the camera while the scope remains rigidly mounted to the gun. Repeated shots
must not teleport the weapon back to the start of its recoil animation.

Attachment magnification may change zoom but must not automatically change mouse
sensitivity. Visual recoil is separate from the authoritative recoil pattern.
The implementation retains spread, projectile drop and physical muzzle obstruction;
screen centre is the aiming reference, not a promise that every projectile hits it.
Autonomous turrets aim at their selected target rather than a player's camera.

### UI-06 — Customizable Ground Crosshair

On **18 October 2026**, the creator requested one customizable HUD crosshair,
replacing the separate centre dot and gun spread crosshair. It must sit at the
exact screen centre, and the creator's preferred/default style is a dot.
Players must be able to choose other crosshair shapes through the game's settings.
This is an explicit extension to UI-01's inherited ground interface.

The implementation offers dot, cross, ring and combined shapes, an Off choice,
colour and dimension controls, opacity and outline, a live preview, and optional
visibility while aiming or expansion with weapon spread. These controls are
implementation choices supporting the request; physical optic reticles and
UI-05's weapon simulation rules remain separate.

## Base Building

### Admin Construction Tools — confirmed 16 October 2026

The F1 admin TOOLS panel provides BGrade selection: new structural pieces are built directly
at the selected tier (Twig, Wood, Stone, Metal or Armoured). Admins can build and craft without
resources or crafting prerequisites. These privileges are limited to admins; ordinary players
continue to use the survival building and crafting rules.

Implementation controls: FREE BUILD starts on for an admin session and can be switched off;
BGRADE starts off (normal twig placement). Free crafting keeps the normal queue and duration.
The building paper, hammer or placeable item still selects the action, and placement still
requires valid space, support, reach and cupboard authorization.

### BUILD-01 — Foundation and Research Direction

Rust's **building and raiding rules** are the default baseline. Specific
Hollow Universe requirements recorded in this document take precedence
where they define different behavior. Because Hollow Universe is also
a space game, evaluate Empyrion - Galactic Survival,
Astroneer, No Man's Sky, and Dune: Awakening alongside Rust to develop
a combined system suited to this game's needs.

BUILD-02 through BUILD-05 and BUILD-10 record confirmed requirements and
the adopted Rust baseline. BUILD-06 through BUILD-09 contain research and
recommendations; their additional mechanics remain open where not
subsequently confirmed elsewhere in this document.

### BUILD-02 — Building Paper and Hammer

There are two building tools:

| Item | Confirmed purpose |
| --- | --- |
| **Building Paper** | Crafted with a very small amount of wood; opens the radial build menu for selecting construction pieces |
| **Hammer** | A wooden-looking hammer used for repair, upgrading building levels/grades, breaking or demolishing building pieces, and related building operations |

The exact Building Paper recipe and the detailed costs, permissions,
and timing of hammer actions have not yet been specified.

### BUILD-03 — Build Radial Reference

![IMG-BUILD-01: Rust building radial showing construction-piece icons around a ring and the selected Foundation's details in the centre](media/build-01-rust-radial.png)

**IMG-BUILD-01 — Creator-supplied Rust UI reference.** This image supports
BUILD-02's Building Paper radial menu and UI-01's Rust UI requirement.
The ring contains construction-piece icons, highlights the selected
segment, and shows that piece's icon, name, description, and material
cost in the centre. The screenshot's recipe numbers and visible pieces
do not independently define Hollow Universe's complete parts catalogue
or crafting costs.

### BUILD-04 — Tool Cupboard, Claim, and Storage

The **Tool Cupboard** is the base's land-claim item, following the Rust
reference. Its claim coverage is intended to grow in direct proportion
to the base's size. The exact measure of base size, coverage geometry,
and calculation remain to be specified.

The Tool Cupboard can store the base's upkeep materials, ordinary
resources, and building tools. Detailed upkeep rates and inventory
capacity have not yet been defined.

Auto Turrets follow the shared deployable placement authorization rule
in BUILD-05. Auto Turrets and SAM sites automatically protect players
authorized on the associated Tool Cupboard, subject to the explicit
per-device clearing rules in AUTH-01.

### BUILD-05 — Structural Building and Deployable Items

Placeable/deployable items are separate from the structural building
system, but their placement must work properly with player-built bases.
Doors, workbenches, storage boxes, furnaces, and the game's other
deployable items must be accommodated in those bases.

**Shared placement authorization:** A player cannot place a deployable
within a Tool Cupboard's claim area unless they are authorized on that
Tool Cupboard. This applies to Auto Turrets as well as other deployables.

**Exception — wall-placed ladders:** Ladders placed on walls can be
placed within a Tool Cupboard's claim area without authorization on
that cupboard. This exception concerns the TC authorization requirement;
the normal placement and collision rules still apply.

Snapping and colliders must allow practical, coherent base layouts.
The sizes and placement behaviour of building pieces and deployables
must work together, including fitting items into rooms and attaching
items such as doors to the appropriate building parts. Rust is the
creator's reference for this integration.

### BUILD-06 — Comparative Research

Research snapshot: **15 October 2026**. Sources below are developer
documentation, developer announcements, or the Facepunch-hosted Rust
wiki. Older release notes establish the described design approach in
that version; their numerical balance and restrictions are not assumed
to be today's rules or Hollow Universe requirements.

#### Rust — Structural Building, Privilege, and Deployable Alignment

Rust's Building 3.0 explanation connects building privilege to the
connected structure and describes upkeep paid from cupboard resources.
It also discusses sockets and half-height construction. These are useful
references for a coherent structural kit and ownership that follows a
base's shape. [Facepunch: Devblog 189, December 2017](https://rust.facepunch.com/news/devblog-189)

The Facepunch wiki describes building-material upgrades with the hammer
and pickup of deployed objects while equipped. Rust's July 2025 update
adds deployable snapping to walls, corners, and suitable nearby objects.
This directly supports treating deployables as separate items while
making interior layouts easy to arrange.
[Hammer](https://wiki.facepunch.com/rust/item/hammer) ·
[Friends with Benefits](https://rust.facepunch.com/news/friends-with-benefits)

Rust's September 2026 update also changes upkeep through a group-size
surcharge. Exact Rust costs, authorization rules, and taxes must therefore
be specified explicitly before reuse; BUILD-04 is the confirmed Hollow
Universe requirement.
[Breach and Clear](https://rust.facepunch.com/news/breach-and-clear/)

**Recommended contribution:** the structural kit, grades, tool roles,
claim/upkeep loop, and practical deployable alignment. Select balance
values for Hollow Universe's planet sizes and PvP design.

#### Empyrion — Structures with Functional Space Equipment

Empyrion's official construction overview describes block-based bases
and spacecraft whose equipment affects their function. Its developer
gameplay FAQ describes oxygenated compartments using power, an oxygen
tank, a ventilator, and an airtight enclosure. That 2017 explanation
uses an oxygenated/not-oxygenated room model and describes air loss
when a room is breached.
[Building and construction](https://empyriongame.com/building-and-construction/) ·
[Developer gameplay FAQ](https://empyriononline.com/threads/read-first-gameplay-faq.10344/)

Its Alpha 8 repair-to-template system restores missing ship blocks from
a saved layout using materials. Version 1.12 describes field repair
with conditions including no incoming attack, active shield, or firing
turrets. These distinguish repairing damage, reconstructing lost parts,
and repairing during combat.
[Repair to Template, June 2018](https://empyriononline.com/threads/alpha-8-repair-to-template.38180/) ·
[v1.12, March 2025](https://empyriononline.com/threads/v1-12-release.102735/)

**Recommended contribution:** separately placed equipment that gives a
structure capabilities, and an understandable power/life-support layer
for hostile environments. Whether to include sealed rooms and automatic
repair remains a Hollow Universe decision. Spacecraft customization
follows SHIP-01, with interior use defined in SHIP-03; Empyrion's vehicle
classes and exact block dimensions are not prescribed.

#### Astroneer — Readable Modules, Connections, and Automation

Astroneer's developer base-building update separates movable platforms
from attached modules and describes visible power-network connections.
Its automation guide shows filtered Auto Arms with visible pickup/drop
areas, extractors placed at deposits, storage integration, and sensors
for storage and power conditions.
[System Era: Basebuilding Update](https://steamcommunity.com/games/361420/announcements/detail/1670144303988869060) ·
[System Era: Automation Guide](https://blog.astroneer.space/p/megatech-automation-guide/)

**Recommended contribution:** readable mount points, connection types,
machine states, and local resource movement. A platform can be one
mounting option alongside floors and walls. The game need not require
every device to occupy an Astroneer-style platform. Resource rates,
network reach, and automated item transfer remain design proposals.

#### No Man's Sky — Placement Tools and Resource Infrastructure

Frontiers documents snapping structural sets, context-sensitive visual
variants, moving and duplicating parts, and a separate free-placement
mode. That mode permits intersections and unsupported placement, so its
editing convenience needs different placement validation for a raid game.
[Frontiers](https://www.nomanssky.com/frontiers-update/)

Beyond describes generators, solar panels, batteries, wiring, switches,
and mineral/gas/energy surveying, with extraction linked to storage.
Synthesis adds terrain flattening/restoration and discusses the capacity
used to preserve base terrain edits. Terrain modification is therefore
a separate persistence and building decision.
[Beyond](https://www.nomanssky.com/beyond-update/) ·
[Synthesis](https://www.nomanssky.com/synthesis-update/)

**Recommended contribution:** clear snap previews, convenient adjustment,
and an ore-site-to-extractor-to-storage workflow. For functional structures
and deployables, free positioning should retain support, collision, and
permission checks. Terrain editing and transfers of completed bases are
not confirmed features.

#### Dune: Awakening — Planning, Claim Feedback, and Base Management

Funcom's building overview describes a sub-fief console with claim
boundaries, permissions, and base information; advanced claims can grow
horizontally and vertically. Solido blueprints project layouts that
players complete piece by piece using materials, including cooperative
construction. This provides a reference for planning a base before all
its resources are available.
[Building in Dune: Awakening, April 2025](https://duneawakening.com/news/building-in-dune-awakening-claim-your-piece-of-arrakis/)

Later systems must be distinguished from that material-funded blueprint
workflow: the reconstruction tool stores and restores a completed base
and its contents, while Chapter 3 removes base taxes but retains power
requirements. These are separate economy and protection choices.
[Reconstruction tool, December 2025](https://duneawakening.com/news/major-new-feature-and-a-free-surprise/) ·
[Chapter 3, February 2026](https://duneawakening.com/uk/news/announcement/chapter-3-free-update-raiders-of-the-broken-lands-dlc-out-now/)

**Recommended contribution:** visible claim feedback, clear base status,
and optional material-funded construction plans. Fixed land parcels,
shield protection, and storing a completed base with its loot would
require their own full-loot PvP design decisions.

### BUILD-07 — Proposed Combined System

**Status: research-based recommendation, not yet confirmed.** These
proposals preserve BUILD-02 through BUILD-05 and the existing Rust player
UI direction. The main recommendation is a Rust structural kit with
separately crafted functional devices and a shared placement system.

1. **Keep the two-tool construction loop.** Building Paper selects
   structural pieces through the existing radial presentation; the
   hammer handles the approved modification actions. New equipment
   remains an inventory item deployed through the common placement flow.
   Additional device capabilities can use the existing interaction
   patterns without requiring a different visual UI style.

2. **Share validation while allowing different placement modes.**
   Structural pieces use compatible sockets; doors use openings; floor
   devices can rotate and align to walls, corners, or neighboring devices;
   mounted devices use suitable attachment points. Each item declares
   supported surfaces, snap points, footprint, support needs, and
   clearance. A shared pipeline evaluates these rules and permissions.
   The placement preview and server validation should agree.

3. **Design interior fit as part of the kit.** Physical collision shapes
   should represent occupied space accurately. Door opening sweeps,
   container access, machine interaction areas, and passage/headroom
   need separate consideration from decorative mesh bounds. Show why
   placement fails and offer predictable snap candidates. Support an
   uncluttered but well-packed base without clipping, unreachable
   controls, or accidentally blocked doors.

4. **Fit construction to small spherical planets.** Consider one local
   construction frame per connected base, with foundations handling the
   terrain beneath it. This would keep adjacent floors and deployables
   aligned while the planet curves away below. Maximum footprint,
   terrain penetration, and foundation limits need evaluation against
   WORLD-01; no numerical grid or footprint limit is selected here.

5. **Make the Tool Cupboard's reach and upkeep understandable.** Derive
   the base-size measure from defined structural membership, make claim
   coverage visible, and show upkeep needs and available supplies. Decide
   how separated structures, extensions, and destroyed pieces affect
   coverage. Upkeep consumption should be clear alongside ordinary
   resources and building tools stored in the same item. Apply the confirmed
   deployable placement authorization rule and ladder exception in BUILD-05.
   Further authorization details, raid damage, and destruction of a
   cupboard require explicit rules.

6. **Add space functionality through devices.** Solar and wind generation,
   battery storage, and wired electricity distribution are now confirmed
   in ELEC-01. Sealed habitats and oxygen devices remain candidates from
   the space-game research; their need can depend on the environment.
   Devices should share the game's placement and interaction capabilities.
   Spacecraft customization follows the module-based design in SHIP-01.
   Orbital bases and detailed pressure/temperature simulation remain
   separate scope decisions.

7. **Connect extraction to physical infrastructure.** Build on RES-01's
   confirmed ore veins and autominers with a proposed local chain:
   ore vein → autominer → storage → processing. Show input/output,
   power demand, and full or blocked storage. Distinct power and material
   connections can keep the system understandable. Transfer range,
   throughput, damage, theft, and offline operation remain undecided.

8. **Use blueprints as plans if adopted.** Save layout and required
   materials, show a construction projection, and build through the same
   placement and resource rules as normal construction. Copying a design
   should not itself duplicate resources, inventories, or completed
   equipment. Whole-base relocation and reconstruction during raids
   need their own decisions before implementation.

### BUILD-08 — Existing Implementation to Reuse or Extend

The previous project already contains a shared placement pipeline with
registered solvers and checks, data-driven placement rules, and the
common radial UI. These are concrete starting points for evaluating
the proposed system:

| Existing source | Relevance |
| --- | --- |
| [Ground systems documentation](../Hollow%20Universe/docs/GROUND_SYSTEMS.md) | Section 4: shared radial; section 6: shared placement |
| [Placer.cs](../Hollow%20Universe/src/Hollow.Simulation/Placement/Placer.cs) | Candidate selection and shared placement validation |
| [placement.json](../Hollow%20Universe/game/data/ground/placement.json) | Surface, socket, rotation, support, and placement rules |
| [building.json](../Hollow%20Universe/game/data/ground/building.json) | Existing building pieces, doors, locks, and cupboard data |
| [BuildingRadials.cs](../Hollow%20Universe/game/scripts/Build/BuildingRadials.cs) | Existing construction and hammer radial sources |

This inspection located reusable code and data; it did not playtest the
new requirements. Legacy numeric limits and placement exceptions still
need comparison with the new design. UI-01's unchanged UI reuse remains
the confirmed presentation requirement.

### BUILD-09 — Proposed Validation and Implementation Order

First establish the confirmed structural kit, Building Paper, hammer,
Tool Cupboard, and dependable interior placement using the existing UI.
Then develop the confirmed electricity system and evaluate environmental
equipment. Add automation and
optional blueprint planning after the placement and ownership rules
are consistent.

Useful acceptance scenarios for the eventual implementation:

- Fit workbenches, boxes, and a furnace into a representative small
  base; the player can move through it and use every item.
- Open a door fully beside stored items; test its sweep and handle
  access independently of the closed-door collider.
- Place objects on upper floors and near walls or stairs; the selected
  support and the final placement agree with the preview.
- Remove or destroy a supporting piece and apply one defined rule to
  its dependent structure and deployables.
- Expand and shrink a base; claim coverage and upkeep update according
  to the selected base-size rule.
- Have two players attempt a conflicting placement; the server resolves
  it consistently without duplicated items or resource loss.
- Build across sloping terrain on a small planet; the connected floor
  grid remains usable and the foundations satisfy the chosen limits.
- Verify power-loss behavior once defined and show device states clearly.
  If sealed habitats are adopted, also verify the effects of a hull breach.

### BUILD-10 — Adopted Building and Raiding Baseline

Use the following Rust-derived rules as the building and raiding baseline.
Hollow Universe's explicit requirements take precedence, including TC
coverage, placement authorization, the wall-ladder exception, interaction
controls, and Auto Turret authorization.

- **Building grades:** Twig, Wood, Stone, Sheet Metal, and Armored.
  The Hammer upgrades building pieces between the applicable grades.
  Recipes, health, and resistance values remain to be documented.
  [Facepunch: Hammer](https://wiki.facepunch.com/rust/item/hammer) ·
  [Facepunch: Tool Cupboard and building grades](https://wiki.facepunch.com/rust/the_tool_cupboard)
- **Structural support:** Ground structures require sufficient support.
  Unsupported portions can collapse, including after supporting pieces
  are destroyed. The mapping of structural damage to spacecraft remains
  part of SHIP-04's detailed design.
  [Facepunch: Structural Integrity](https://rust.facepunch.com/news/devblog-35)
- **Upkeep and decay:** The TC holds and consumes building materials
  to maintain the base. Maintenance costs grow with the structure;
  insufficient upkeep allows affected building pieces to decay.
  Exact rates and timing remain to be defined.
  [Facepunch: Building Upkeep](https://rust.facepunch.com/news/devblog-189) ·
  [Facepunch: Tool Cupboard](https://wiki.facepunch.com/rust/the_tool_cupboard)
- **Doors and locks:** Doors are deployable defenses with key/code locks.
  Door materials and layouts affect how difficult it is to breach a base;
  airlocks and layers of defenses provide additional obstacles.
  [Facepunch: Wooden Door](https://wiki.facepunch.com/rust/item/door.hinged.wood) ·
  [Facepunch: Building Terminology](https://wiki.facepunch.com/rust/Building-terminology)
- **Directional vulnerability:** Applicable building pieces distinguish
  stronger exterior faces from weaker interior faces. This rule must be
  defined per piece and does not automatically apply to doors or every
  damageable item.
  [Facepunch: Building Protection](https://rust.facepunch.com/news/devblog-104) ·
  [Facepunch: Door damage changes](https://rust.facepunch.com/news/devblog-120)
- **Raiding:** Attackers breach physical doors and structures to reach
  defenders and stored loot. Building material, weapon/damage type, and
  the route through defensive layers determine the effort required.
  Offline raiding is confirmed under LOOP-05; ship capture follows SHIP-04.
  [Facepunch: Door vulnerability](https://wiki.facepunch.com/rust/item/door.hinged.wood) ·
  [Facepunch: Layered defenses](https://wiki.facepunch.com/rust/Building-terminology)

These sources establish the adopted behavior. Exact Rust numerical
balance, later version-specific additions, and special server-mode rules
are not independently specified Hollow Universe requirements.

## Interaction Architecture

### INT-01 — Shared Interactable System and Tap E

The system first determines whether an object is interactable. A single
press of **E** performs its contextual interaction. For example, E closes
an open door or opens a closed door.

Interactivity and actions belong to a shared, reusable system. Actions
are determined from what the target supports, its current state, and
relevant attached components. The examples below illustrate this system;
they are not instructions to hardcode separate action lists for each
object type or complete specifications of those objects.

### INT-02 — Hold E to Remove and Pick Up an Object

For an interactable object that can be removed and put into inventory,
holding **E** displays a progress bar. The intended hold duration is
approximately **1-2 seconds**. Once the progress completes, the object
is removed and added to the player's inventory. If there is no inventory
space, it drops on the ground as loot.

### INT-03 — Hold E for Multiple Options

If an interactable object has multiple options, holding **E** opens its
radial menu. The options reflect the object's current state, including
the state of attached components such as locks.

The following menus are illustrative and non-exhaustive:

| Example state | Example radial options |
| --- | --- |
| Closed door with an attached, locked lock | Unlock the lock; Change lock code; Open door |
| Closed door with an attached, unlocked lock | Open door; Lock; Remove lock |
| Closed door with no lock attached | Remove door; Open door |

Unlocking a lock leaves it attached to the door. Removing the lock is a
separate action. These examples show how options change with state;
omitted options do not establish additional restrictions or define an
object's full feature set.

### INT-04 — Interaction Details Still to Define

The precise handling of a hold when an object supports both pickup and
multiple actions remains to be specified, including how removal selected
from a radial menu uses progress and further input. Exact hold thresholds,
cancellation behavior, action permissions, and lock validation rules are
also still to be defined. Listing an action in an example menu does not
define the conditions under which that action succeeds.

## Character Survival and Equipment

### SURV-01 — Hunger and Thirst

The character has hunger and thirst needs. Their values, consumption
rates, and consequences have not yet been specified.

### SURV-02 — Breathing and Oxygen Equipment

When the character enters an environment without breathable air, such
as being submerged in the sea or standing on a moon without oxygen,
they begin to suffocate unless suitable equipment supplies oxygen.

An oxygen tank item provides oxygen for underwater use. Clothing and
equipment for planetary environments will similarly support an oxygen
supply. The oxygen available from these items is part of their equipment
state; capacities, consumption rates, and refill rules remain to be
defined.

### SURV-03 — Shared Environmental Survival Logic

Breathing is handled through one common system: the environment provides
its breathing conditions, and equipped items provide their oxygen-supply
capabilities and current supply. These determine whether the character
can breathe or begins to suffocate.

The main character must not contain separate hardcoded behaviours for
the sea, a particular moon, or other individual environments. The same
rules work with the environmental conditions and the equipment in use.
The detailed timing and damage progression of suffocation remain to be
specified.

### SURV-04 — Death and Respawning

The initial starting planet is Earth (LOOP-01). After death, respawn
location follows the player's **Sleeping Bag** and **Bed** respawn points:

| Situation | Respawn behavior |
| --- | --- |
| The player has no Sleeping Bag or Bed respawn point | Respawn at a **random beach on the same planet where they died** |
| The player has Sleeping Bag or Bed respawn points | Choose their own bag or bed on **any planet or spacecraft**, subject to its cooldown |

Sleeping Bag and Bed respawn points have **cooldowns of 1–5 minutes**.
The exact duration for each type, cooldown triggers, and whether nearby
or related points share a cooldown remain to be specified.

Death in open space, planets without a valid beach spawn location, and
the case where all existing respawn points are on cooldown remain to
be defined. The confirmed random-beach rule applies when the player
has no Sleeping Bag or Bed respawn point.

## Inventory and Loot

### LOOT-01 — Dropped Items and Lootbag Merging

When an item is dropped from the inventory onto the ground, it becomes
a lootbag containing that item. If multiple items are dropped close
together at the same location, they combine into a single lootbag
containing the dropped items and their quantities.

The exact proximity threshold for merging has not yet been specified.

## Crafting and Technology Progression

The broader technology design references are recorded in TECH-01.
The following sections define the confirmed progression rules.

### CRAFT-01 — Workbench Types

| Workbench | Level | Tech tree and role |
| --- | --- | --- |
| **Workbench 1** | 1 | Standard level-1 tech tree and crafting support |
| **Workbench 2** | 2 | Standard level-2 tech tree and crafting support |
| **Workbench 3** | 3 | Standard level-3 tech tree and crafting support |
| **Vehicle Workbench** | No levels | Its own tech tree, including spacecraft unlocks; required for crafting spacecraft |
| **Engineering Workbench** | No levels | Its own tech tree; the specific technologies and recipes remain to be defined |

### CRAFT-02 — Learning Technology

The standard workbenches provide tech trees appropriate to their levels.
Players spend **salvage** to learn technologies in sequence through these
trees. The exact nodes, prerequisite connections, and salvage costs
remain to be specified.

Vehicle and Engineering Workbenches each have their own tech tree.
They do not have workbench levels. The costs and progression details
of these specialized trees have not yet been specified.

Tech trees are not the only learning path. Players can also learn from
item blueprints obtained through research or found as loot (CRAFT-05 and CRAFT-06).

### CRAFT-03 — Crafting Speed and Workbench Requirements

Crafting speed increases with the level of the standard workbench.
Exact speed bonuses have not yet been defined.

Independently of crafting speed, some items require a workbench, or a
specific workbench level, in order to be crafted. Such an item cannot
be crafted without satisfying that requirement, even when its technology
has already been learned.

The crafting system must treat technology unlocks, required workbench
type/level, and crafting speed as distinct rules. The exact applicability
of speed bonuses and compatibility between workbench levels remain to
be specified.

### CRAFT-04 — Spacecraft Unlocking and Crafting

The Vehicle Workbench is used to unlock spacecraft through its tech
tree. Crafting a spacecraft also requires a Vehicle Workbench. Learning
the technology does not remove that crafting-station requirement.

Spacecraft customization follows SHIP-01 and SHIP-02. The detailed
assembly process, materials, and station-access conditions remain to
be defined.

### CRAFT-05 — Research Bench and Item Blueprints

An item obtained through any gameplay source, such as monument loot,
can provide an alternative route to learning its technology through
the **Research Bench**:

1. Obtain the item to research.
2. Research it at a Research Bench by spending **salvage**.
3. The research process consumes and destroys the original item and
   produces a **learnable blueprint** for that item.
4. Learn the resulting blueprint to unlock the corresponding technology.

Producing a blueprint and learning it are separate steps. Learning a
technology through a blueprint still leaves its crafting-station
requirements in effect, as described in CRAFT-03.

These are item/technology blueprints, distinct from the optional
building-layout plans discussed in the base-building research.
Researchable items, salvage costs, and research timing remain to be specified.

### CRAFT-06 — Rare Blueprint Loot

Some monuments and other spawn locations can contain **rare blueprints**
for items or technologies that are **not available in the normal tech trees**.
Finding and learning these blueprints provides access to technologies
outside the normal tree progression.

The improved item variants in CRAFT-07 follow this rule: their blueprints
are found at monuments or loot spawns, and the variants are not available
through the normal tech trees.

The rare blueprint list, spawn distribution, and whether their associated
items can also be researched at a Research Bench remain to be specified.

### CRAFT-07 — Item Variants and Performance Progression

The progression principle illustrated by rare spacecraft engines and
weapons also applies broadly to the game's other items. An improved
variant retains its **base item type and purpose** while offering
better performance.

**Blueprint acquisition:** Improved item variants are **not available
in the normal tech trees**. Their blueprints can be found at **monuments
or loot spawns**, then learned to unlock crafting of the corresponding
variant.

For example, the game has a basic **furnace**. Players can find a
blueprint for a **faster-smelting furnace**, learn it, and craft that
variant. It remains a furnace, with the same fundamental role of
smelting; its improved characteristic is smelting speed.

For modular equipment, a learned and crafted improved component can
be installed in the relevant vehicle, as described in SHIP-01 and
SHIP-02. The creator's Dune: Awakening reference is a sandbike fitted
with a "Mohandis engine" to become faster; this illustrates the desired
performance improvement within an existing vehicle type.

The furnace example defines a craftable item variant; module installation
follows the design of the relevant equipment. The variant catalogue,
specific blueprint spawn locations and drop rates, recipes, and exact
performance differences remain to be specified.

## Spacecraft and Customization

### SHIP-01 — Modular Spacecraft

Spacecraft have defined module types, including **engine modules** and
other types still to be specified. Players customize their ships by
crafting and installing modules and equipment, with **Rust's modular
cars** as the reference for this approach.

Engines and weapons are confirmed customization examples. The complete
module catalogue, installation process, and compatibility rules remain
to be defined.

### SHIP-02 — Rare Engines and Weapons

A player who learns a **rare weapon type** or **rare engine** from
content obtained at a monument can craft it and install it on their
spacecraft. These rare options can make the ship more powerful than
one using standard options.

This is an application of the broader item-variant progression in CRAFT-07.

The confirmed progression is: **find the blueprint at a monument or
loot spawn → learn it → craft the item → install it on the ship**.
These improved variants are not available through the normal tech trees,
following CRAFT-06 and CRAFT-07.

Exact performance differences and module-specific crafting-station
requirements remain to be specified. The existing Vehicle Workbench
requirement for crafting spacecraft remains in effect (CRAFT-04).

### SHIP-03 — Ship Sizes, Interiors, and Cargo

Spacecraft function as vehicles. Designs can include both of these forms:

| Ship form | Confirmed characteristics |
| --- | --- |
| **Small spacecraft** | Carries one or a few people and has a single cargo/loot storage location; its inventory capacity remains to be defined |
| **Large spacecraft** | Has a walkable interior where players can deploy storage boxes and store loot |

The creator's references for large spacecraft are Rust's tugboats and
player-made ships: vehicles that also provide usable physical space
inside. The modular customization rules in SHIP-01 and SHIP-02 apply
alongside these interior and cargo capabilities. Sleeping Bags and Beds
on spacecraft follow SURV-04.

### SHIP-04 — Raiding and Capture

Spacecraft can be attacked and raided like player bases. A raid can
involve destruction of the ship or its doors. If the occupants cannot
resist the attackers, the ship and its contents can fall into enemy
hands, as their remaining condition permits.

Attackers can gain control of a surviving ship and access its equipment
and stored loot. The exact damage, total-destruction, wreckage, and
ownership-transfer mechanics remain to be specified within this
base-raiding model.

### SHIP-05 — Pilot Weapons and Player-Operated Cannon Stations

Ship weapons support distinct control roles:

| Weapon role | Confirmed operation |
| --- | --- |
| **Cockpit weapons** | The pilot controls these weapons from the cockpit on small and large spacecraft |
| **Dedicated cannon stations** | A player goes to the cannon's station, takes control, aims, and fires it directly; these provide separate gunner roles on large spacecraft |

The **Iron Wake's largest cannons** are operated separately by players
at their gun stations. They are distinct from the pilot's cockpit
weapons. The previous project's [Iron Wake documentation](../Hollow%20Universe/docs/IRON_WAKE.md)
identifies the existing crewed-carrier reference; this section defines
the weapon-control requirements for the new design.

**Strict reference scope:** Use **SAND: Raiders of Sophie** and
**Sea of Thieves** as references for a player personally occupying and
operating a mounted cannon. Their other combat, camera, aiming, or
ballistic conventions are not adopted by this reference. In particular,
crosshair-free or hard-to-read aiming must not be copied into Hollow
Universe. Follow UI-04's robust aiming and shot-readability requirements.

Reference titles: [SAND: Raiders of Sophie — official site](https://www.sand-game.com/) ·
[Sea of Thieves — official site](https://www.seaofthieves.com/).

Gun-station controls, firing arcs, ammunition, loading/reloading, and
the weapon assignments for individual ship designs remain to be specified.

## World Persistence and Performance

### WORLD-05 — Persistent Bases and Spacecraft

Player bases and spacecraft are **persistent world objects**, including
their installed equipment and stored contents. Their persistent state
must reflect gameplay changes such as building, moving, looting,
damage, destruction, and capture.

**Optimization is a required design constraint** for this persistent
world. The implementation must account for accumulated bases, ships,
deployables, and inventories. Simulation, streaming, networking, and
saving strategies and measurable performance targets remain to be
defined. Server organization and wipe policy are separate decisions.

## Electricity and Technology Direction

### TECH-01 — Reference Games

Electricity follows **Rust** as its primary system reference. More broadly,
the game's electricity and technology systems will draw inspiration from
**Rust**, **Dune: Awakening**, **Astroneer**, and
**Empyrion - Galactic Survival**.

These references guide the overall design. Specific mechanics are defined
by the confirmed requirements in this document.

### ELEC-01 — Generation, Storage, and Wired Supply

Players can generate electricity, store it, and supply electrical devices
through cables.

| Component | Confirmed role |
| --- | --- |
| **Solar Panel** | Generates electricity |
| **Wind Turbine** | Generates electricity |
| **Battery** | Stores generated electricity |
| **Cables** | Connect electrical devices to an electricity supply |
| **Electrical devices** | Receive electricity through wired connections; the Auto Turret is one confirmed example |

The Auto Turret's consumption remains **10 energy**, as specified in
ITEM-01. Generation rates, battery capacities, energy units and time basis,
and storage dispatch rules remain to be specified. The confirmed initial
wiring rules follow in ELEC-02–03.

### ELEC-02 — Physical Ports and the Wire Tool

Confirmed by the owner on **18 October 2026**:

| Device role | Electrical ports |
| --- | --- |
| Wind Turbine and every other generator | Exactly one output |
| Battery | Exactly one input and one output |
| Auto Turret and SAM site | Exactly one input and one passthrough output |

Each port is a small physical cube on the item itself. Players use a
**Wire Tool** to click an output, optionally click surfaces to attach
visual cable-routing points, and finally click the receiving input.
The maximum connection span is **20 m between the physical endpoint
ports**, explicitly confirmed by the owner; it is not a limit on total
routed cable length or on each decorative segment. Routing points are
for cable management only and do not become electrical junctions.

The architecture must support later branches, splitters and combiners
without per-item hardcoded paths. Those additional devices are outside
the initial delivery. Batteries are a required architectural role; this
confirmation does not imply a playable battery item already exists.

### ELEC-03 — Shared Electrical Architecture and Scope

Electricity uses shared, efficient, modular, scalable systems with
data-declared ports and behavior. Power generation, distribution and
consumption must not be separately hardcoded for each item. Physical
ports, cable editing and presentation use the shared interaction and
held-tool foundations, preserving the ground UI.

The current request is to work on **architecture and general logic first**.
[Electricity and device authorization](ELECTRICITY_AND_AUTHORIZATION.md)
records the source audit, proposed directed graph, wire transactions,
permission boundaries, persistence/migration and validation sequence.
That document is a plan, not an implementation-completion claim.
Disabled-device passthrough, partial-power handling, wire removal controls,
port dimensions, routing limits, costs and balance remain proposals/open
details unless separately confirmed.

### AUTH-01 — Automatic and Per-Device Defense Authorization

Confirmed by the owner on **18 October 2026**: when an Auto Turret or
SAM site is placed in a base where the placer has Tool Cupboard building
privilege, the placer and everyone authorized on the associated cupboard
are automatically protected by default.

Each device also offers these actions through its own **hold-E radial**:
authorize yourself, clear your authorization, clear all authorization,
and enable/disable the device. The owner explicitly clarified that
clearing authorization must remove **inherited Tool Cupboard protection
as well**, on that device. Clearing yourself must therefore override your
cupboard-derived protection; clearing all must stop automatic inheritance
for that device. It does not clear the cupboard's building-privilege list
or authorization on other devices.

The proposed architecture separates target friendship from permission
to configure the device. It uses local grants/exclusions and an explicit
inheritance mode so clearing is not silently undone by a later power,
cupboard, reconnect or restart event. Exact management/enrollment rights,
overlapping-cupboard association, an optional restore-inheritance action,
and how SAMs classify a ship with different crew members remain proposed
details in the architecture document.

## Items and Deployable Equipment

### ITEM-01 — Auto Turret

The **Auto Turret** is a deployable automated defense item.

| Property | Confirmed behavior |
| --- | --- |
| Energy consumption | **10 energy**; the unit and time basis have not yet been specified |
| Electricity supply | Connected to an electricity supply by cable, following ELEC-01 |
| Weapon and ammunition | Automatically fires at enemies using the weapon and ammunition placed inside it |
| Placement | Follows the shared deployable rule in BUILD-05: placement within a Tool Cupboard's claim area requires authorization on that cupboard |
| Authorization | Automatic Tool Cupboard protection, with explicit per-device authorization and clearing under AUTH-01 |

The Tool Cupboard placement rule refers to its claim area. Targeting
range, weapon compatibility, and further targeting and power-system
rules remain to be specified.

## Long-Term Replayability and Low-Population Gameplay

### PLAY-01 — Confirmed Design Goal and References

The game should support **indefinite replayability as a design goal**
and remain engaging when **only one player is online on the server**.
Meaningful activities and progression opportunities must therefore be
available independently of encounters with other human players.

Rust and Empyrion - Galactic Survival remain the primary gameplay
foundations. Broader gameplay inspiration includes **Dune: Awakening,
Elite Dangerous, Empyrion - Galactic Survival, Starfield, Star Citizen,
Astroneer, and Satisfactory**. Satisfactory is specifically included
as a reference for factory production, automation, and optimization.

This goal applies to the existing online sandbox. Full-loot PvP, offline
raiding, physical cargo transport, persistent bases and ships, the travel
model in TRAVEL-01–05, and the absence of an authored storyline remain in effect.

**Status:** PLAY-01 records the creator's confirmed direction. PLAY-02
is source research. PLAY-03 through PLAY-06 are proposed additions and
design recommendations; specific new mechanics there are not yet
confirmed requirements.

### PLAY-02 — Research from the Reference Games

Research snapshot: **15 October 2026**. Sources establish particular
mechanisms, not a wholesale adoption of another game's balance, world
reset rules, travel, economy, or progression. Historical updates and
limited events are identified where relevant.

- **Dune: Awakening:** Chapter 3 introduced repeatable testing-station
  challenges and a wider mix of exploration, combat, gathering, and
  crafting activities. Update 1.5's solo-specific vendor and simulated
  Landsraad participation illustrate ways to support activities without
  a player-populated market. Hollow Universe would adapt these ideas
  to its shared online world.
  [Chapter 3, 3 February 2026](https://duneawakening.com/news/dune-awakening-chapter-3-patch-notes/) ·
  [Update 1.5, 17 September 2026](https://duneawakening.com/news/dune-awakening-console-release-patch-notes/)
- **Elite Dangerous:** Exploration, mining, and trade provide distinct
  occupations. Frontier's 2018 developer recaps describe signal scanning,
  discovery, several mining approaches, and local scenarios involving
  ships and installations. The transferable idea is learning about a
  location and choosing an activity and equipment to suit it.
  [Publisher overview](https://store.steampowered.com/app/359320/Elite_Dangerous/?l=english) ·
  [Frontier developer recaps, 2018](https://store.steampowered.com/news/posts/?appids=359320&enddate=1543838399)
- **Empyrion - Galactic Survival:** Different resource locations and
  environments motivate expeditions; plundering and dismantling
  structures make found sites useful for production. Version 1.19's
  beacon-and-detector treasure hunts add a search activity between
  receiving a lead and reaching the reward. Their renewal schedule is
  not established by these sources.
  [Mining and resources](https://empyriongame.com/mining-and-resource-gathering/) ·
  [Official gameplay overview](https://empyriongame.com/press/) ·
  [Version 1.19, 4 August 2026](https://empyriononline.com/threads/v1-19-11th-anniversary.103144/)
- **Starfield:** Planet resource scans and surface surveying inform
  landing and extraction-site choices. Mission boards and bounty scanning
  offer examples of optional activities outside an authored story quest.
  [Starmap](https://help.bethesda.net/app/answers/detail/a_id/61656/~/starmap---starfield) ·
  [Resource gathering](https://help.bethesda.net/app/answers/detail/a_id/61082/~/resource-gathering---tips-%26-tricks---starfield) ·
  [Update 1.12.30, 9 June 2024](https://bethesda.net/en-US/news/starfield-update-1-12-30-june-9-2024)
- **Star Citizen:** Documented playable Alpha activities include mining,
  refining, hauling, wreck recovery, and contracts. Alpha 3.24's generated
  hauling jobs vary pickup/delivery arrangements and cargo size. These
  are references for physical work and logistics, not assumptions about
  a future finished release.
  [Daily Life in the Verse](https://support.robertsspaceindustries.com/hc/en-us/articles/16444523494423-Daily-Life-in-the-Verse) ·
  [Alpha 3.24 patch notes](https://robertsspaceindustries.com/en/comm-link/Patch-Notes/20087-Star-Citizen-Alpha-3240)
- **Astroneer:** Auto Arms, filters, sensors, and resource transport
  support networks that players can design and refine. The 2025 A.M.P.P.
  event connects production to shipments and rewards, but was a limited
  event; a permanent demand system would be a Hollow Universe adaptation.
  [Automation guide](https://blog.astroneer.space/p/megatech-automation-guide/) ·
  [A.M.P.P. 2025, 2 June 2025](https://blog.astroneer.space/p/how-to-complete-automated-mass-production-protocol-event-2025/)
- **Satisfactory:** Its central activities combine resource expeditions
  with building and optimizing factories. Conveyors, pipes, and transport
  connections link production sites. Large production projects offer
  milestones, while improving layouts provides player-directed goals.
  Its authored Project Assembly narrative and final completion are
  separate from the proposed renewable systems below.
  [Official gameplay overview](https://www.satisfactorygame.com/) ·
  [Coffee Stain: 1.0 launch, 10 September 2024](https://coffeestain.com/news/satisfactory-1-0-launch/)

### PLAY-03 — Proposed Gameplay Additions

#### PLAY-A — Surveying, Prospecting, and Signal Hunting

Equip a scanner, detect a lead, narrow its location, and investigate
on the surface or in space. Findings can include resource prospects,
buried caches, wrecks, and monument access opportunities. The player
decides whether to recover cargo immediately, return with different
equipment, or establish a nearby operation.

Different signal quality, terrain, access routes, and environmental
conditions create preparation and search decisions. Temporary leads
can provide new opportunities in already visited systems; permanent
planet discoveries alone provide a finite exploration checklist.

**References:** Elite Dangerous, Starfield, and Empyrion in PLAY-02.

#### PLAY-B — Wreck Recovery and Dismantling

Find a wreck, assess access and hazards, recover its cargo and useful
components, then dismantle selected parts for materials and haul the
results home. Tools, cargo room, and the value of recoverable components
create choices about what to extract and what to leave behind.

World-generated wrecks provide opportunities even without player ship
losses. Existing player wrecks can also participate once SHIP-04's
destruction rules are defined. Renewing generated sites must preserve
player-owned and captured ships. This activity is called **wreck recovery**
here to distinguish it from the **salvage** resource used for research.
Recovered-item research remains subject to CRAFT-05/06 eligibility.

**References:** Star Citizen and Empyrion in PLAY-02.

#### PLAY-C — Repeatable Monument Operations

Use the existing monument archive for sites with varied functional
challenges: restore a power circuit, repair an access mechanism, locate
a secured chamber, or extract cargo from a hazardous section. Different
layouts, access conditions, equipment requirements, and loot pools can
make preparation and return visits meaningful.

Human defenders from the existing secretive faction are a possible
additional challenge if monument NPCs are adopted. This proposal does
not require an authored storyline or surviving Hollow Men. Opportunities
appropriate for a lone player should coexist with optional harder sites.
Rare variant blueprints remain physical monument/loot discoveries under
CRAFT-06/07.

**References:** Dune: Awakening and Empyrion in PLAY-02.

#### PLAY-D — Local Factories and Production Engineering

Extend autominers, furnaces, electricity, and storage into configurable
local production chains: **extract → sort → process → assemble → store**.
Candidate additions include conveyors or item-transfer connections,
splitters, filters, production machines, and sensors controlling supply
and power. The final connection and machine catalogue remains a proposal.

Players can identify bottlenecks, balance inputs against power and
throughput, rearrange layouts, and expand production to supply their
next base, ship, expedition, or industrial project. New resource sites,
recipes, and projects change what an efficient factory looks like.
Engineering Workbench could provide the relevant ordinary technology
tree; its exact scope still needs a separate definition.

Begin with automation inside bases and local mining operations. Preserve
the confirmed activity of physically transporting cargo between planets.
Factory equipment and stocks remain part of the raidable world. Growth
must fit WORLD-05's performance requirement; unlimited factory size or
continuous full-detail simulation is not implied.

**References:** Satisfactory and Astroneer in PLAY-02.

#### PLAY-E — Planet Specialization and Active Extraction

Make planetary composition and environmental conditions inform which
resources and operations a location supports. Surveying then leads to
equipment selection, a mining expedition or outpost, extraction,
processing, and a cargo trip home. Different material needs give players
reasons to maintain more than one location.

Active extraction with tools or mining ships could offer skill-based
choices in targeting, timing, or managing extraction conditions, while
autominers serve ongoing production. The exact mechanic and resource
depletion/replenishment policy remain to be designed. Renewable
opportunities and continuing material uses must be considered together.

**References:** Empyrion, Elite Dangerous, Starfield, and Star Citizen
in PLAY-02; this extends RES-01 and the confirmed LOOP-04 hauling loop.

#### PLAY-F — Optional Recovery, Survey, and Supply Requests

Offer short, renewable objectives such as recovering physical cargo,
surveying a location, repairing a facility, or supplying specified
materials. Requests can vary by location, goods, access conditions, and
hazards. They can provide a clear goal for a short session even when
there are no other players to trade with.

The issuing terminal, customer, or faction and the reward economy need
definition before adoption. Existing materials and salvage are possible
rewards; no new currency or reputation system is required by this proposal.
Advanced variant blueprints remain monument/loot finds, not routine
contract unlocks. An ongoing supply market would need bounded demand
and material consumption to avoid unlimited risk-free profit.

**References:** Star Citizen, Starfield, Dune: Awakening's solo-specific
support systems, and Astroneer's shipment event in PLAY-02.

### PLAY-04 — Renewal in a Persistent World

The proposed long-term structure combines **changing expedition
opportunities**, **player-chosen production projects**, and **physical
resources and cargo with continuing uses**. Completing a standard tech
tree should leave useful activities available.

- Renew temporary signals, wrecks, caches, and site conditions through
  defined placement and lifecycle rules. Renewal must not overwrite
  player bases, ships, claims, or occupied sites.
- Keep the same activities in the shared world when more players join:
  discoveries, cargo, extraction sites, and factories naturally provide
  opportunities for competition and raids.
- Support varied routes, tools, layouts, and practical rewards. An endless
  sequence of stronger item tiers or mandatory daily tasks is not required
  to meet the replayability goal.
- Separate ongoing demand from new unlocks. Construction, expeditions,
  and existing upkeep provide uses for materials; optional requests can
  add further demand once their economy is designed.
- Define limits and scheduling for generated sites and industrial networks
  so persistent progression remains compatible with WORLD-05.

These renewal rules are proposed Hollow Universe design choices. For
comparison, Dune's multiplayer Coriolis cycle renews Deep Desert content
through a reset; that reset is not adopted for Hollow Universe's
persistent player structures.
[Funcom: Coriolis Storm](https://funcom.helpshift.com/hc/en/4-dune-awakening/faq/46-what-is-a-coriolis-storm/)

### PLAY-05 — Recommended Initial Feature Set

First develop a connected version of **surveying + wreck recovery +
local production + repeatable monument operations**, using the existing
gathering, electricity, storage, modular spacecraft, and blueprint rules.
Add deeper extraction, environmental variations, and request-based
economic systems after that loop supports meaningful choices.

An example solo session could be: detect a wreck signal, prepare the ship,
fly there, overcome access hazards, choose cargo to recover, bring it
home, process it, and improve a production line or prepare for a harder
monument. Each step should provide an action or decision of its own.

### PLAY-06 — Design Validation Scenarios

- A player alone on the server can gather, explore, build, and pursue
  useful rewards without waiting for another player to appear.
- After standard technologies are learned, that player can still find
  worthwhile expeditions and choose meaningful industrial projects.
- Known systems can offer fresh opportunities without deleting persistent
  player property or requiring a galaxy-wide reset.
- When another player joins, the same loot, cargo, factories, and
  objectives still obey full-loot PvP and raid rules.
- A functioning factory reduces routine handling while leaving active
  exploration, engineering choices, and interplanetary hauling valuable.
- A prototype with many machines, containers, and active sites establishes
  performance limits before committing to their production-scale counts.

## Detailed Gameplay Design

Further gameplay systems will be developed from subsequent inputs.

## Images and Videos

Store reference files in the `media/` folder. Place each image or video link
beside the design text it explains, with a unique reference ID and a caption
describing what matters. For videos, include the relevant timestamps.

| Reference ID | File | Related design |
| --- | --- | --- |
| IMG-BUILD-01 | [Rust build radial screenshot](media/build-01-rust-radial.png) | BUILD-02, BUILD-03, UI-01 |
| IMG-TOOLS-01 / 02 | [Accepted Hollow metal-tool captures](REFERENCE_STUDY_TOOLS_WORLD.md#references-and-review-coverage) | ART-03 |
| VID-TOOLS-01 | [Continuous axe gathering reference](https://www.youtube.com/watch?v=JTTJlCEwfUE&t=25s) | ART-03; carry, chopping, recovery and traversal |
| VID-WORLD-01 | [Detailed trailer reference ledger](REFERENCE_STUDY_TOOLS_WORLD.md#trailer-lessons-by-concern) | ART-02, ART-03; tools, terrain, lighting, materials and actions |

## Open Decisions

- Exact timeline and further details of the catastrophe.
- Specific technology, equipment, and art details within the established handmade direction.
- Specific Subsistence reference examples and the world-appearance and
  atmosphere qualities to reproduce under ART-02.
- Detailed crafting scope and recipes.
- Workbench tech-tree nodes, prerequisite paths, salvage costs, crafting-speed
  bonuses, station-access rules, and recipe-specific workbench requirements.
- Engineering Workbench technologies and specialized tech-tree progression.
- Researchable items, Research Bench salvage costs and timing, and rare
  blueprint contents, spawn distribution, and research eligibility.
- Item variant catalogue, specific blueprint spawn locations and drop
  rates, recipes, and performance differences, including the faster-smelting
  furnace variant.
- Spacecraft module catalogue, installation and compatibility rules,
  module crafting requirements, and performance differences between
  standard and rare engines or weapons.
- Spacecraft handling, cargo capacities, damage and wreckage behavior,
  and the mechanics of gaining control of a raided ship.
- Cockpit/gunner weapon assignments, gun-station controls, firing arcs,
  ammunition and reloading, and the reticle/projectile feedback details
  supporting UI-04 and SHIP-05.
- Building Paper recipe, building-grade costs and statistics, and detailed
  hammer action permissions and timing under the adopted Rust baseline.
- Tool Cupboard claim geometry, base-size measure, upkeep, and storage capacity.
- Auto Turret targeting range, weapon compatibility, further targeting
  rules, and the energy unit and time basis for its consumption of 10.
- Electricity generation rates, battery capacities, energy units and time
  basis, storage dispatch, disabled/undersupplied-device passthrough,
  wire-removal controls, port dimensions and Wire Tool costs. The 20 m
  endpoint span and initial port counts are settled in ELEC-02.
- Defense management/enrollment rights, overlapping-cupboard association,
  optional restoration of inherited protection, and aircraft identity for
  SAM targeting. Clearing inherited protection is settled in AUTH-01.
- Final selection of space-oriented building extensions from the research.
- Selection and detailed rules for the proposed low-population gameplay
  additions in PLAY-03, including surveying, wreck recovery, repeatable
  monument operations, local factories, active extraction, and requests.
- Temporary-site renewal, resource replenishment, continuing material
  demand, and performance limits supporting long-term replayability.
- Monument selection, size-dependent distribution rules, configuration,
  contents, and interactions.
- Whether to add monument NPCs and, if added, their behavior.
- Initial Earth spawn distribution, progression pacing through the first
  spacecraft, and further camera/perspective details.
- Sleeping Bag/Bed cooldown details, all-points-on-cooldown behavior,
  respawning after death in space or on planets without valid beaches,
  and other death consequences and recovery after losing a ship or base.
- Population distribution across the galaxy.
- Hold-E behavior when pickup and multiple actions overlap, radial-menu removal flow,
  and detailed interaction validation and timing.
- Galaxy layout and star selection within the 1,000-star maximum,
  optional use of real-galaxy data, map dimensionality, and detailed map UI/UX.
- Planet-size scaling function within the 23.04 km² total-surface-area limit.
- Specific biome, fauna, and resource rules; ore extraction and replenishment.
- Hunger/thirst rules, equipment oxygen capacities and consumption/refilling,
  and suffocation timing and damage.
- Playable space scale, supercruise speeds and handling, drive charge/cooldown
  times, jump ranges/costs, fuel type, scoop equipment/star eligibility/rates,
  heat and emergency-drop rules; implementation of continuous planetary
  descent/ascent and persistent transfers under TRAVEL-01–05.
- Further survival, combat, and loot rules; detailed application of the
  Rust building/raiding baseline and explicit Hollow Universe adaptations.
- Server structure, wipe policy, and the character's state while offline.
- Persistent-world simulation, streaming, networking, saving, and
  performance targets for bases, ships, deployables, and inventories.
