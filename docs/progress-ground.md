# Ground game: progress log

On `main`, in `D:\GameDev\Projects\Hollow Universe`, since 14 October 2026 (before that, branch `ground` in
`D:\GameDev\Projects\Hollow-wt\ground`). The plan and the decisions are in
[GROUND.md](GROUND.md). This log says what happened, newest last, so the owner can catch up at a glance.

## 12 October 2026, evening

- **Plan agreed.** It's a Rust-style ground game, networked from the start, with a dedicated .NET server and the
  Godot client. GROUND.md was written, with these decisions from the owner:
  - Rust's UI (as built in RustMVP), Rust's numbers and the AK recoil pattern may be used.
  - Files extracted from Rust may not.
  - The first world is a real planet.
  - The Tin Finch is the starter ship, crafted at the vehicle workbench.
  - Claude runs the work to the end.
- **First world picked: Nolcas Peris CQ-SLY 7-0 B 3**, 331 ly from Sol. Within 400 ly, 71 of the 1,698 worlds with
  plants and animals have breathable air; this is the most Earth-like of them. Details are in GROUND.md §3. A golden
  test pins it.
- **Shared contract committed:** `src/Hollow.Surface` holds `ISurface`, `ZoneFrame` and the stand-in
  `SphereSurface`. 40 of 40 tests pass.
- **Work packages started:**

  | Branch | Work | State |
  |---|---|---|
  | `ground-net` | Dedicated server, network, client prediction, SQLite saves, 100 bots, first-person test mode | running |
  | `ground-planet` | The first world's surface: terrain to the horizon, ocean, sky, clouds, plants, landing site | running |
  | `ground-character` | Player body (MPFB), clothing layers, animation, ragdoll, first-person arms and viewmodel | running |

- **Downloads approved by the owner:** Quaternius animation libraries (CC0) and MPFB (MakeHuman for Blender).

## 12 October 2026, night

- **G1 network core merged** (`ground-net`, 13 commits, about 10,000 lines):
  - a dedicated .NET server;
  - an engine-free client with prediction and interpolation;
  - generated serializers and delta snapshots;
  - SQLite characters, which survive restarts;
  - 100-bot load tests;
  - a first-person ground mode with placeholders (capsule players, flat-coloured ground).

  The Jolt physics check passed in both the server and Godot. Measured with 100 bots:

  | Measure | Result |
  |---|---|
  | Server tick | 4.25 ms average, 10.2 ms at the 99th percentile |
  | Bandwidth per client | 114 kbit/s |
  | Prediction on a clean line | exact |
  | Prediction at 150 ms ping and 5% loss | 1 correction in 114,069 checks |

  Screenshot: `docs/images/ground/two-players.png`. 57 of 57 tests pass.
- **G2 started**, as two packages:
  - `ground-survive`: items, containers, crafting, gathering, survival stats, deployables, death, sleepers, melee and bow, persistence;
  - `ground-ui`: the RustMVP interface rebuilt exactly. That's the HUD, inventory, loot, crafting, death screen, building and hammer wheels, and a main menu.
- **Still running:** `ground-planet` and `ground-character`.
- **G2 interface merged** (`ground-ui`). It is the RustMVP interface rebuilt exactly in Godot:
  - HUD and hotbar;
  - inventory, with loot panels for boxes, bodies, the furnace and the campfire;
  - crafting;
  - death and respawn;
  - the building and hammer wheels;
  - a main menu.

  It talks only to a view model (`IGroundUi`). Screenshots are in `docs/images/ground/ui/`; each matches its reference
  side by side. It gets wired to the real client when `ground-survive` lands.
- **Owner feedback on plants:** the grass clumps made from downloaded plant models are out. Grass becomes a Rust-style
  shader system around the camera. Plants and trees must reach the look of hollow-orbit's vegetation; its berry bushes
  are the bar. Berry bushes become pickable food. Both packages have been told.
- **Started two more packages:**
  - `ground-assets`: converts the PSG starter kit, the ore nodes and the Tin Finch; builds our own models where the
    kits have none; adds a model catalogue and renders item icons for the interface.
  - `ground-build`: phase 1 of G3. It ports Rust's building rules from RustMVP (sockets, stability, upgrade, claims,
    decay, doors) to C#, converts the building and door kits, and adds a local building sandbox. Networking and item
    costs come in phase 2, once `ground-survive` is merged.
- **Characters merged** (`ground-character`):
  - a MakeHuman body, with Quaternius animations retargeted onto it;
  - clothing and armour from our own Blender scripts;
  - aim pitch, arm and foot IK, ragdoll;
  - first-person arms.

  50 characters take 5.7 ms a frame. The clothing, the first-person hands and the melee swing aren't at the Rust bar
  yet, so the same agent is on a polish pass now. That pass also replaces the capsule players in the ground mode with
  real characters. 58 of 58 tests pass.
- **G2 survival merged** (`ground-survive`):
  - data: 43 items, 29 recipes, 13 natural kinds including the berry bushes, and 9 deployables;
  - containers, crafting, gathering with the weak spot, vitals, corpses, sleepers, and melee and bow with lag
    compensation;
  - SQLite schema 2.

  With 100 survival bots the tick is 5.9 ms on average and each client uses 105 kbit/s.
- **The first world's surface merged** (`ground-planet`), with the landing site at 44.87° N, 170.00° W:
  - terrain out to the horizon, thick-air sky, the real sun and moon, stars, clouds and sea;
  - plants generated in code in hollow-orbit's style, including the berry bushes;
  - GPU shader grass.

  A frame takes 11 ms at the landing site. Screenshots: `docs/images/ground/a_landing_morning.jpg` to `g_2km.jpg`.
  A polish pass is running on the forest floor, bark, firs, night, coast and the view from altitude.
- **Integration started** (`ground-integrate`): the real planet on the server and client, natural objects from the
  planet's scatter (felling trees, picking berries, ore outcrops), the real UI bound to the client, real characters
  instead of capsules, a main menu, and a `play-ground.cmd` launcher. 79 of 79 tests pass on `ground`.
- **Planet polish merged:** forest floor litter, natural bark, fuller firs, smooth LOD fades, far forest canopy, a
  better night sky, surf and wet sand. All six Megascans Fab listings were checked: Standard License, none
  Unreal-only. Screenshots in `docs/images/ground/`.
- **G4 fight started** (`ground-fight`): firearms with server-side ballistics and lag compensation, Rust's AK recoil
  pattern from RustMVP (deterministic on client and server), aiming down sights, attachments, ammo types, armour and
  the wounded state. Engine-free for now; the visuals follow after integration.
- **Assets merged** (`ground-assets`):
  - 72 catalogued models: converted kit items, the Frontier rifle, the Tin Finch with its hatch, and our own piles,
    tools, supplies, deployables and outcrops (our own design rather than the kit's Rust-like nodes);
  - 124 item icons in a Rust-like product-shot style (`docs/images/ground/assets/icons.jpg`).

  The bed is a marked stand-in until its model is made. The assets agent is now making the bed, the G4
  attachments and ammo variants, icon fixes and world props.
- **G3 building, phase 1 merged** (`ground-build`):
  - 20 pieces in 5 grades, 7 doors, locks, and the claim device with upkeep and decay;
  - stability and collapse, and hammer actions;
  - the building and door kits converted;
  - a `--view build` sandbox (screenshot `docs/images/ground/build/house.jpg`).

  A 1,060-piece base draws in about 5 ms. In the merge I fixed a physics body ID clash between survival's props and
  buildings. Phase 2a is now running: building over the network, saves, inventory costs, the claim-device store and
  bots that build. 91 of 91 tests pass.
- **Assets pass 2 merged:**
  - a real bed (replacing the stand-in);
  - holo sight, scope, silencer, weapon light and muzzle brake, mounting on the guns' sockets;
  - high-velocity and incendiary ammo boxes;
  - better icons for the spears, arrows and tubers;
  - first-person item placements;
  - loot barrels, crates and a recycler for G7.

  91 of 91 tests pass.
- **Sound started** (`ground-audio`):
  - a catalogue of every sound event: footsteps per ground material, gathering, building, doors, weapons with
    near/far layers, explosions, Rust-like UI clicks, and ambience by biome and time of day;
  - an audio system with pooled 3D voices, distance layering, buses and occlusion;
  - an audition viewer.

  Sources are CC0 recordings with our own processing, checked by loudness and clipping measurements. The owner's
  ears are still needed.
- **G4 fight core merged** (`ground-fight`):
  - the pistol and rifle with RustMVP's definitions and the AK recoil pattern, exactly as given;
  - three rounds per calibre and five attachments;
  - server-side bullets with drop, drag and lag compensation;
  - damage by distance, body part and armour;
  - Rust's wounded state (down, crawl, help up, bleed out).

  With 100 fighting bots the tick is 8.6 ms on average and each client uses 169 kbit/s. The visuals (muzzle flash,
  tracers, aiming and the HUD) follow after integration. Three new items have marked stand-in models for now.
  102 of 102 tests pass.
- **G5 raid started** (`ground-raid`):
  - timed charges, bag charges (they can dud), tin grenades, the rocket launcher and rockets, explosive ammo;
  - damage by grade and side, aiming at Rust's raid costs;
  - line-of-sight shielding;
  - raid alerts to owners, online or offline, and offline raids that work.
- **Character polish merged:**
  - a proper crew-neck shirt with no skin showing in any pose;
  - a riveted cuirass of our own design that reads as armour from a distance;
  - natural first-person hands and grips;
  - a real chop for tools, and weighty swings and reloads;
  - blinking, ragdoll joint limits, no foot sliding;
  - a female body.

  Held items load from the shared catalogue. 102 of 102 tests pass.
- **Effects and the last item models started** (`ground-fx`):
  - muzzle flashes, tracers, impacts and decals per surface, blood, gathering chips;
  - explosions from the PSG C4 flipbooks, and fire;
  - real models for the three stand-ins and the G5 explosives and launcher.
- **Sound merged** (`ground-audio`):
  - 463 files for 138 events, about 13 MB, all CC0 with our own processing;
  - footsteps per ground type, gathering, building, doors, guns with near, mid and far layers, explosions, Rust-like
    interface clicks;
  - ambience by biome and time of day;
  - an audio system and the `--view sounds` viewer.

  Every file was checked by measurement. Gunshot loudness, silenced shots and the generated wind beds need the owner's
  ears. The sounds aren't wired into the ground mode yet. 105 of 105 tests pass.
- **G6 ship started** (`ground-ship`), with the Tin Finch:
  - crafted at the vehicle workbench;
  - hatch and seat, flight assist, hover, landing on its skids;
  - first-person cockpit and chase camera;
  - flight up to an "orbit reached" ceiling;
  - network prediction, saved parked ships;
  - a `--view ship` sandbox over the real planet.
- **Integration pass 1 finished** (`ground-integrate`). G1 and G2 play as one game on the real planet:
  - arriving on real beaches;
  - felling the surface's own trees, picking berries, mining new stone, metal and sulphur outcrops;
  - crafting, clothing, fires, melee and bow, death and respawn, sleepers;
  - real characters and first-person arms;
  - the RustMVP interface with Rust's keys;
  - a ×24 day.

  Launch it with `play-ground.cmd` (host), `play-ground.cmd join`, or `play-ground.cmd menu`. Screenshots are in
  `docs/images/ground/play/`. It runs at 81 fps with 2 players and 50 bots. Integration pass 2 is running now:
  - merging `ground` in;
  - the asset catalogue, characters by worn items with a male or female choice, sound;
  - the gun visual layer;
  - Host as a separate server process, since hosting in-process stalls on shared garbage collection;
  - tick profiling.
- **G3 building phase 2a merged:**
  - building works over the network and survives restarts, with costs paid from the inventory;
  - the claim device's store pays upkeep;
  - code locks and keys.

  With 100 bots building 100 bases the tick is 7.4 ms on average and each client uses 84 kbit/s. A 1,074-piece
  base streams to a newcomer in 1.9 s. The ground-mode hookup (held plan and hammer, ghost, wheels, drawing
  structures) follows the running integration pass. 110 of 110 tests pass.
- **G5 raid core merged** (`ground-raid`):
  - explosives that are thrown, lobbed, placed and stick; bag charges can dud and be relit;
  - the rocket launcher, and explosive rifle rounds;
  - blasts with shielding, matching Rust's raid counts exactly;
  - collapse;
  - raid alerts, in game or waiting until login;
  - offline raids that work.

  With 100 raiding bots the tick is 9.7 ms on average and each client uses 157 kbit/s. Models, sounds and visuals
  for raiding come with the effects package and the next integration pass. 120 of 120 tests pass.
- **Effects merged** (`ground-fx`):
  - 45 effects: muzzle flashes, tracers, impacts and decals per surface, blood, gathering chips;
  - explosions from the PSG C4 VFX kit, fire, world dust;
  - real models for the raid items and the earlier stand-ins.

  A firefight costs about 0.3 ms a frame. 120 of 120 tests pass. The effects get wired into the game in the next
  integration pass.
- **G7b animals started** (`ground-animals`):
  - deer, boar, wolf packs and a rare bear-like predator, with server-side AI (graze, flee, charge, hunt);
  - corpses you harvest for meat, hide, bone and fat;
  - cooking, and lamp oil from fat;
  - animal sounds, and a `--view animals` sandbox.
- **Still running:** integration pass 2, the ship (G6), the wardrobe and looks, and G7a (salvage, recycler, tech
  tree, teams, map).
- **Integration pass 2 finished** (`ground-integrate`), with `ground` merged in (assets, G3 phase 1, G4 core,
  character polish, sound):
  - the asset catalogue through the integration's seams: deployables, outcrops at their three stages, dropped items,
    the dead's backpack (a body becomes its backpack after 150 s), held items and icons; a picked berry bush is the same
    bush, bare;
  - a male or female body chosen in the main menu, kept with the character and seen by everyone; worn items as the
    character package's garments;
  - sound everywhere the game has an event, ambience from the place, footsteps on the ground under each player;
  - the G4 layer: recoil on the camera, zoom, reload on R, muzzle flash, tracers, impacts, others' shots, the ammo
    counter, the hip crosshair, the wounded screen and help-up;
  - hosting runs the dedicated server in a process of its own, and Jolt's broadphase is rebuilt after bodies change:
    with 100 bots on the planet the tick went from 10.3 to 5.0 ms (GROUND.md §5.5).

  Not done, for want of the character contract: sleeping pose, crawling, others' aiming, attachments on guns in hand.
  Screenshots retaken in `docs/images/ground/play/` (a female player, a rifle down the sights). 109 of 109 tests pass.
- **Integration merged into `ground` (3b4703d).** `ground` now holds the playable game together with G3 networking,
  G4, G5, effects and sound. Launch it with `play-ground.cmd` (host), `play-ground.cmd join`, or
  `play-ground.cmd menu`. Pass 2 also brought:
  - real models and icons from the catalogue;
  - worn clothing on characters, with a male or female choice;
  - sound;
  - the gun visuals;
  - Host as its own server process;
  - the broadphase fix that halved the server tick (10.3 to 5.0 ms with 100 bots).

  The protocol is now version 7, and 124 of 124 tests pass. Integration pass 3 is running:
  - a smoke test of the merged game;
  - building in the game (plan, hammer, ghost, doors, code locks, claim device);
  - raid visuals and alerts;
  - real effects everywhere;
  - attachments on guns in hand.
- **Integration pass 3 finished** (`ground-integrate`). The merged game started cleanly, hosted and joined. Then:
  - building in the game: the plan's ghost (green, or red with the reason), the plan's and hammer's wheels, placing,
    upgrading, turning, demolishing and repairing; doors that open and shut with their sounds; code locks with a keypad
    in the interface's style; the claim device's store with authorise, deauthorise and clear;
  - raiding: explosives on their arcs, stuck or lying, a timed charge's lamp blinking faster as it runs down, fuses
    sputtering; Rust's throw and place; blasts with fireball, smoke, debris, scorch and the camera's shake, heard by
    distance; rocket trails; raid alerts at the top right, live and at login;
  - the effects library everywhere: muzzle flames in first and third person, tracers, hits by surface, blood, chips and
    tree-fall dust, campfire, furnace and torch fires. It now follows the camera's exposure, so effects look alike by day
    and night; its heat haze is off in the game, where it drew black (GROUND.md §11 83);
  - attachments on guns in hand, put on by dropping them on the gun and taken off in its panel.

  Hosted play with 2 players and 50 bots: 16.7 ms a frame on average, with another agent's game running on the PC too
  (GROUND.md §5.8). New pictures in `docs/images/ground/play/`: a base being built, a code lock's keypad, a charge on a
  wall, the blast, a firefight. Still waiting for the character contract: sleepers, crawling, others' aiming. 124 of
  124 tests pass.
- **Integration pass 3 merged:**
  - building in the game: plan ghost, wheels, place, upgrade, rotate, demolish, repair, doors, code-lock keypad,
    claim device;
  - raid visuals: explosives with blinking fuse lamps, fireballs, debris, scorch, shake, rocket trails, raid alerts;
  - real effects everywhere;
  - attachments on guns in hand.

  Screenshots: `docs/images/ground/play/build.jpg`, `base.jpg`, `charge.jpg`, `explosion.jpg`, `firefight.jpg`.
- **G6 ship merged** (`ground-ship`). The Tin Finch is built at the vehicle workbench; you board it through the
  hatch and fly with flight assist. It hovers, boosts, lands on slopes, takes crash damage and burns fuel, and the
  pilot's prediction matches the server exactly. There's an orbit marker at 15 km, and a `--view ship` sandbox with a
  scripted `--tour`. 134 of 134 tests pass. Integration pass 4 is putting the ship into the game now.
- **G7a world finished** and being merged. It adds:
  - salvage and components;
  - barrels and crates on coasts and forest edges;
  - recyclers;
  - tech trees and a research table with blueprints;
  - teams of up to 8;
  - the map with markers.
- **Next: server performance.** Ships streaming ground as they fly cost the server a lot on the real planet.

## 13 October 2026, morning

- **Integration pass 4 finished** (`ground-integrate`):
  - the Tin Finch in the game: crafted at the vehicle workbench, it rolls out onto the bay; you board it through the
    hatch and fly it from the cockpit or the chase camera, with the flight HUD in place of the belt; it hums, burns,
    boosts, lands and crashes with its own sounds, and a wreck burns;
  - G7a in the game: the map on G, on the zone's own parchment picture, with markers, teammates, bags and the last
    death (the death screen draws it too); a workbench's tech tree on E; the recycler's and research table's panels;
    barrels, crates and recyclers drawn, a running recycler's lamp and hum, a barrel breaking; blueprints learnt by
    using them; teams, with the invite prompt and teammates' names in green at any distance; notices for blueprints
    learnt, invites and team changes.

  New pictures in `docs/images/ground/play/`: the ship's recipe, boarding, cockpit, chase camera and landing; a team
  invite, a teammate far off, the props, a crate, the recycler, a workbench, its tech tree, the research table, the map
  and the death screen. 145 of 145 tests pass.

## 13 October 2026, the F1 console

- **The F1 console** (`ground-console`, GROUND.md §8.2), for the owner's playtest. F1 opens Rust's F1 menu in our
  interface's style, five tabs:
  - CONSOLE: a log in colours (typed lines, answers, things done, refusals), history on Up and Down kept between runs,
    Tab completion of commands, item keys, players, loot tables and things to spawn;
  - INFO: the game and its build, this computer, the world (planet, landing site, local time, the day's speed), the
    server;
  - NET: ping, loss, bandwidth, snapshots, prediction error, input margin, and a graph of the last 30 seconds;
  - ITEMS: every item with its icon, in categories with a search; an admin's click gives 1, 10, 100 or 1,000;
  - TOOLS: god, noclip, heal, kill, a time-of-day slider and the time scale, spawn buttons (animals, the Tin Finch,
    barrels and crates, every deployable, a raid base), teleport to a player or the map marker, entity counts, save.
- **Commands:** for everyone `help`, `find`, `kill` (Rust's: dead at once, the respawn screen opens), `players`,
  `whereami`, `clear`, `quit`, `fps`, `netgraph`, `fov`, `sensitivity`; for admins `give`, `kit`, `learn`, `teleport`
  (to a player, metres or latitude and longitude), `tp2me`, `tpmarker`, `spawn`, `heal`, `kill <player>`, `god`,
  `noclip`, `time`, `timescale`, `status`, `kick`, `say`, `save`, `ents`, `clearinv`, `ship`, `raidbase`, `admin`;
  Rust's names work too (`inventory.give`, `global.teleport`, `env.time`, `global.god`, `global.noclip`,
  `entity.spawn`, `server.save`).
- **Rights:** the server checks every line. A hosted game's host is its admin; a dedicated server takes `--admin <name>`,
  `admins.txt` and `--dev`. Others are told plainly, and everyone keeps `kill`. Five lines a second at most.
- **God mode and noclip** ride in the player's own state, so the prediction flies exactly as the server does: no
  corrections while flying. **time** and **timescale** turn this zone's sun only; universe time stays as it is.
- Checked once in the hosted game: help, give rifle, spawn wolf 2, teleport, noclip up and down, time 21 (10 of 10).
  Pictures: `docs/images/ground/play/f1-console.jpg`, `f1-info.jpg`, `f1-net.jpg`, `f1-items.jpg`, `f1-tools.jpg`.
  Protocol 11; the console's tests (parsing, completion, a non-admin refused, give, teleport, spawn, kill and time over
  the network, god and noclip against the prediction) pass with the rest.

## 13 October 2026, dropped items lie in bags (owner's playtest)

- **Bags** (`ground-drop`, GROUND.md §6.1, decisions U1–U7), for the owner's finding that a dropped Tin Finch lay on the
  ground as a full-size ship. Everything that lands on the ground now lies in a small bag: one function
  (`Zone.Spill`) packs whatever lands within 1.5 m of a bag with room into it, else starts a bag where it comes to rest,
  and every source calls it: drops, crafts, refunds and gifts that don't fit, broken boxes, furnaces, barrels and claim
  devices, a broken research table's work, spent arrows. A bag has 48 slots, lasts 300 s from its newest item, rests on
  the floor it lands on (not the terrain under a base) and goes once emptied; E takes a bag of one stack whole and opens
  one holding more (take only, the rows it fills). Others see only a bag and its number of stacks.
- **Our own model** (`dropped-bag`): a burlap sack gathered into a neck with a wound cord, two corners standing out of
  the knot, a leather strap with a buckle round its belly. Pictures: `docs/images/ground/drop/bag.jpg`, `bag-open.jpg`,
  `bag-model.jpg` (new screenshot step `--tour-steps bags`).
- **Shared code made generic on the way:** bodies and bags are one `DroppedContainer`; what clients are told of a thing
  (health, count, burning, whose, name) is the thing's own; how a container is looted follows its kind; the copies of
  "give, drop the rest" are one `GiveOrDrop`. Bots open the bags they find and take what they want.
- Protocol 12, the things' record version 3 (old dropped stacks load as bags; no SQL step), survival.json's `bag`;
  models.json's unused `drop.scale` went. 166 of 166 tests pass (three new: packing, taking or opening, and what
  another player sees over the network).
- **Not done:** deploying the Tin Finch from its item (the other half of this package) was stopped by the owner's new
  rule: placement (deployables, doors, locks, the claim device, charges, building pieces and the ship) will be one
  system, designed with the owner first. No ship-deploy code was written.

## 13 October 2026, gathering's feedback

- **The owner's playtest: "no feedback other than the sound"** when hitting trees and outcrops (`ground-gather`,
  GROUND.md §6.1, §9.3, decisions H1–H14). In the real game the effects did play, but at a guessed point 1.1 m up on the
  side facing the local player, mostly away from the crosshair, with small chips thrown at the camera; the weak spot was
  a small orange ball that floated beside broken outcrops; a felled tree paid nothing for falling; and walking into an
  area replayed its old hits, felled trees falling again.
- **One data-driven rule and look per class**: gather.json's `classes` (tool rate, weak spot, last hit, look; kinds
  replace parts of the look), read by `NaturalState.Hit` and the client's `NatureView` and `NatureMarkers`.
- **Trees: Rust's X.** A red painted X (a decal on the bark, our own texture) shows with the first hit, facing the
  player near the hit's height; hits on it in a row pay and take 1.125, 1.25 … up to 2 times and move it; a miss ends
  the run and leaves it. The tree's crown shakes at each hit; felled, it tips away, lands in its dust with the crash
  in time, lies and sinks, and pays half its wood as it falls (Rust, November 2017).
- **Outcrops: Rust's hotspot.** A warm glow with a pulsing glint and sparks on the rock itself at every stage (the
  stage models' tops measured by the new `--view models --survey-surfaces` into surfaces.json); hits on it pay 150%,
  200%, 250%, at most 300%; the outcrop gives the same ore in under half the hits; the last hit with a pickaxe pays 20%
  more (Devblog 166). Each stage breaks with a burst and its sound.
- **Every hit plays where it landed**, for everyone in range: protocol 13 sends the point where the swing met the
  drawn surface, the run of spot hits and how fresh the hit is, so old hits never replay.
- Checked once in the hosted game (the tour's `chop` and `mine` steps on a pine and a metal outcrop): wood per hit
  rising with X hits, the felling hit 84 wood, the outcrop at its last stage in 4 hotspot hits. The nature view cost
  52 µs a frame on average. Pictures: `docs/images/ground/gather/`. 173 of 173 tests pass with `ground` merged (seven
  new: the X, the hotspot, the fall's share, the pickaxe's bonus, spots on the drawn rock, the looks' references, hits
  over the network). Protocol 13.

## 13 October 2026, the recoil and the damage code (`ground-damage`)

After the owner's first playtest: "the AK doesn't use the recoil pattern exactly from RustMVP", and is the weapon and
damage code as modular as RustMVP's foundation planned?

- **The recoil is RustMVP's exactly** (GROUND.md §6.4, D1–D5). Checked field by field against RustMVP's
  `weapon_definition.hpp`, `weapon_rules.hpp` and `recoil_compensation.hpp`. Two things were ours, both gone:
  - a random part on every kick (hollow-orbit's ±12% and ±0.66° on the pistol, ±0.06° a step on the AK);
  - a smaller kick aimed (×0.78) and crouched (×0.85). RustMVP's aiming narrows the spread only.

  The 30 steps, their order, the 0.1 s kick, 0.2 s hold, 0.08 s smoothstep return, the reset after 0.4 s, the limits,
  the zero tail, the 60/450 s cadence and sampling before the kick were already RustMVP's. The countersteer credit now
  runs in RustMVP's order each frame (settle, mouse, camera; before, it came a frame late), and a shot or a correction
  settles it first. The rocket launcher keeps its own kick, deterministic too; muzzle attachments still scale it.
  A new test sprays the AK twice (at the hip; aimed and crouched): every view before a shot is on RustMVP's table, on
  the client's prediction and the server, to the bit.
- **One damage pipeline** (GROUND.md §6.4.2, D6–D11), RustMVP's plan and the owner's rule (no switch that grows with
  every new target):
  - every delivery (swing, arrow, bullet, blast, an animal's attack) builds one request and calls `Zone.Damage`;
  - each kind of thing is a receiver, registered in one place, that finds its own things along a ray and in a blast and
    applies its own defences and consequences;
  - a new damageable thing is one receiver and one line.

  Every number stayed the same: no test changed for it.
- **The Destroyed event**: whatever breaks, collapses, decays, is demolished or wrecked is reported once, from one
  place, with its kind, ID, model key (a piece's grade), where it stood, the blow's direction, the cause, who and with
  what. It reaches clients in range as `GameClient.Destroyed` (message 165, protocol 14), for the break-apart pieces
  `ground-gibs` is making. The building rules now say why they took a piece away.
- **The audit** against RustMVP's FOUNDATION.md and damage plan is in GROUND.md §6.4.2: what is done, what is ours on
  purpose (engine-free rules shared by server and client instead of Unreal and GAS), what is left (one channel an
  attack, environmental damage straight to health, no armour wear or penetration, the network's message dispatch).
- Load tests before and after, back to back on the same busy PC: gunfight tick 6.35 → 5.49 ms on average, raid 7.17
  → 7.75 ms with a fifth more bullets in the second run; a bullet's step 29.0 → 27.4 µs and 16.6 → 16.5 µs, blasts 0.095
  → 0.087 ms a tick. No regression beyond the PC's noise (four other agents were working on it).
- No Godot run: nothing drawn changed (the camera's recoil angles are computed in the same frame, in a new order). 166
  tests pass: the 163 that existed (the recoil test updated for D1) and three new.

## 13 October 2026, the playtest's looking, hands and foundations

- **Free look** (`ground-look`, GROUND.md §4.5, L2–L6): hold Left Alt and the mouse turns the camera and the head, up
  to 120° either side and within ±85° of level, while the view, the walk and the aim stay put; letting go, or any screen
  that shows the cursor, turns the camera back at once. The hands do nothing while it lasts (no shots, swings,
  placing, throwing, reloading or turning a piece): one place now decides what the item in hand does (`GroundView.Hands`,
  from the item's data, `ItemDef.Hands`). The arms stay with the body, so they slide out of the picture as the head
  turns. Others see the head turn and ease back (two bytes on every input and on a character's state; the server bounds
  them and lets go after 0.8 s without input). In the Tin Finch's cockpit Alt turns the pilot's head. Protocol 15.
- **The rock in hand** (§4.8, L7–L10): it was held like a hatchet handle, the fist closed inside the rock and the palm a
  pale blob behind it. It now lies in the right hand at the lower right, the fingers round its far side, and swings from
  the wrist. How a hand holds an item became data (`held.grip` in models.json); the bandage and the grenade are held the
  same way and food lies on the open palm. A contact sheet of all 40 held items showed food, locks, the plan, charges and
  spears sitting below the bottom of the picture (their poses were set for a wider view): all are in view now.
  Pictures: `docs/images/ground/look/fp-rock-before.png`, `fp-rock-after.png`, `game-rock.jpg`, `fp-held-items.jpg`.
- **Free foundations rise with the aim** (§6.3, L1), as RustMVP's did: within 3.5 m the foundation sits where the aim
  meets the ground; aimed further, the aim's height 3.5 m out lifts its top, up to its depth over the lowest ground
  under it, always valid where the slope leaves room. The ghost and the server work it out from the same point.
  Pictures: `docs/images/ground/build/foundation-lifted.png`, `docs/images/ground/look/game-foundation-lifted.jpg`.
- Checked once each: the sandbox's build check (9 of 9), the hosted game's new `look` tour step (rock, free look
  through Alt and the mouse, a lifted foundation) and the character viewer (a head turned, every held item). New
  tests: free look over the network, free foundations on a slope, the catalogue's grips; with `ground` merged (protocol
  13 after the bags' 12), 168 of 168 tests pass.

## 13 October 2026, things breaking apart (`ground-gibs`)

- **Nothing destroyed just vanishes** (GROUND.md §9.4, X1–X12). Barrels, boxes, furnaces, campfires, workbenches, beds
  and bags, the research table, the vehicle workbench, the claim device, doors and every building piece of every grade
  break into their own pieces where they stood: thrown along the blow, tumbling, bouncing and settling on the ground,
  lying a few seconds, then sinking away, with dust in their material's colour. A collapse lets its pieces drop, an
  explosion throws them. An outcrop's last remnant crumbles the same way when it is mined out.
- **Made from our own models** by one Blender script (`art/gibs/build.py`): Voronoi cuts capped with the material's
  inside, the barrels and the recycler hollow so they break into curved sheets, small parts (bolts, bricks, logs) left
  whole. 30 files, 1,106 pieces, 278,000 triangles, 18.1 MB, rebuilt in 40 s with the same bytes.
- **All of it data.** A model's gibs are declared beside it in models.json (the building's grades and the doors as
  kits), how the pieces fly and go in gibs.json (materials, causes, limits), the dust in fx.json's `gibs` family. One
  hookup breaks whatever the damage pipeline's Destroyed event names, by its key and grade; no code names a kind of
  thing. The building's old falling-and-sinking pieces and the props' and collapses' bursts made way for it. The
  Destroyed event now gives a door's turn as it was drawn (half round when it was fitted the other way).
- **Cheap**: no physics bodies, a small integrator against the ground under the pieces, one draw call per piece shape
  in sight, at most 640 pieces (the oldest recycled first), pieces within 70 m and dust to 170 m. A stone wall blown
  in and ten barrels shot every 2.5 s cost 0.22–0.30 ms a frame (the GPU 0.18–0.21 ms of it, the gibs' own CPU work
  0.06–0.08 ms), under the 0.5 ms asked.
- To look at it: `--view fx --gibs` (everything breaking in a grid; `--gibs raid`; `--gibs <key>`), `--stress gibs
  --measure 6`; the tour shoots a barrel and an outcrop breaking. Pictures: `docs/images/ground/gibs/sheet.jpg`,
  `raid.jpg`, `wall.jpg`, `barrel.jpg`, `barrel-rest.jpg`, `outcrop.jpg`.
- Left for later: pieces don't hit each other or walls sideways (X3); an open door breaks from its shut pose (X9);
  the building's own materials are still made in code beside the gib build's files (X11).
- One new test (everything that can be destroyed has gibs, and its data and files check out); with `ground` merged,
  179 of 179 tests pass.

## 13 October 2026, one radial menu (`ground-radial`, P5)

GROUND_SYSTEMS.md §4; GROUND.md §8.4 and decisions Q1–Q9.

- **One radial control for every menu of options**, drawn exactly as RustMVP's wheel at any interface scale. It takes a
  source of options, never a kind: each option has a glyph or an item's icon, a title, a description, a cost line and,
  when it can't be chosen, why. The building plan and the hammer are now two such sources, written once for the game and
  the build sandbox (each had its own copy). `WheelKind`, `WheelChoice`, the four wheel members of the view model and
  the hammer's choice by its index are gone. The glyphs have names (a building piece's key, `door`, `keypad`, ...), so
  data can name them.
- **One way to open it:** a held input. The view model lists its bindings (the right mouse with the plan or hammer
  today; E and R next); a press asks for a source, a hold opens the radial, the release chooses, a quick tap chooses the
  current option (E's primary action, R's reload), Esc or the right mouse cancels.
- **Proved on the interface viewer** with sample sources: E held opens a door's options with a fresh code lock (OPEN
  DOOR, SET CODE, LOCK disabled until a code is set, PICK UP), R held with the pistol its rounds as item icons. The UI
  check now also plays the radial (the plan on the right mouse; E tapped, held, refused, chosen, cancelled by Esc and by
  the right mouse): 19 of 19 pass. The plan's and the hammer's radials look as before. Pictures:
  `docs/images/ground/ui/radial/` (`plan.jpg`, `hammer.jpg`, `options.jpg`, `ammo.jpg`).
- With `ground` merged, 179 of 179 tests pass (no engine-free code changed) and the build sandbox's check 9 of 9 (its
  hammer acts through the shared radial).
- **Next:** P1 moves hold-E onto it (and deletes the vertical options list), P3 adds R's ammo radial; GROUND_SYSTEMS.md
  §4 says how.

## 13 October 2026, the loot bag remade (`ground-bagart`)

- **The bag dropped items lie in** (`dropped-bag`, GROUND.md §9.1, U4) now reads like the owner's picture: a strapped
  bundle, not a sack. Only the idea was taken; the design is our own. A khaki canvas tarp is wrapped round a box-like
  load, its ends folded like a parcel's and a grommeted hem across the front, with two crossed leather straps and iron
  buckles. 0.42 × 0.32 × 0.30 m; LODs of 6,242, 3,120 and 1,366 triangles. No game code changed; with `ground`
  merged, 179 of 179 tests pass. Pictures: `docs/images/ground/drop/bag-model.jpg`, `bag.jpg`, `bag-open.jpg`.

## 13 October 2026, the shared foundations (P0, `ground-p0`)

The first package of [GROUND_SYSTEMS.md](GROUND_SYSTEMS.md), after the owner's rule (each concern built once, no switch
that grows with every new thing). There is nothing new to play with: these are the foundations the interaction (P1),
placement (P2) and held-item (P3) packages build on. The playtest's code-lock dead end is fixed on the way.

- **One name for anything in the zone** (§3.1): `TargetRef` (family, ID, part) and ten families, each answering for its
  own things: whether one exists, where it or a part of it is, its nearest point, its model, its capabilities, and
  taking it out of the world. The four lookups and the damage code's field per kind of thing are gone, and the Destroyed
  event names what broke by its `TargetRef`.
- **One answer for every intent** (§3.2): one code list (`Outcome`), with the lines in `texts.json`. The building's own
  answers are gone; every refusal shows the same way, with the one error sound.
- **What each player is told** (§3.3): one byte of viewer flags per thing, made by its capabilities (`capabilities.json`,
  one handler each). **The code lock is fixed:** holding E on a fresh lock's door offers its owner SET CODE first, and
  LOCK only once it has a code.
- **Reach and sight** (§3.4): every action on a thing measures from the eye to its nearest surface (survival.json
  `reach`) and needs it in sight. Boxes and the cupboard can no longer be opened through walls, and no prompt offers it.
- **Support links** (§3.6): a charge stuck on a door swings with its leaf and falls when its support goes. It's one rule,
  which the next packages' arrows and deployables will use.
- **Lifetimes** (§3.7): bags, bodies, duds and carcasses go by one step and one table (survival.json `lifetimes`).
  Nothing scans every static thing for them any more, nor for the furnaces.
- Checked once in a hosted game (`--tour-steps foundations`): `docs/images/ground/p0/set-code.jpg` (SET CODE first on a
  fresh code lock) and `refusal.jpg` (the cupboard opened through its wall: "Something is in the way"). Protocol 16;
  decisions E1–E13 in GROUND.md §11. Seven new tests; with `ground` merged, 186 of 186 pass.

## 13 October 2026, held items, item information and inventory actions (P3, `ground-p3`)

The third package of [GROUND_SYSTEMS.md](GROUND_SYSTEMS.md) (§7), after the owner's rule: what an item does in the hands,
what it says about itself and what its inventory buttons are now come from its parts, each part's behaviour one handler in
a registry; nothing asks what kind of item it is.

- **What the player sees:** every item's panel says what it is. The AK: damage, auto at 450 rounds a minute, magazine,
  reload, aim cone at the hip and aimed, range, recoil, what is loaded and fitted. The bag charge (the owner's example):
  its fuse and duds, blast, and how many break each wall and door. Rounds, attachments, deployables, doors, locks, the
  cupboard, the plan, the hammer, ships, blueprints and components too; melee, food and clothing say more.
- **Actions:** the panel's buttons are the item's own: EAT food, USE medicine, WEAR and UNEQUIP clothing, LEARN a
  blueprint (it had none), UNLOAD a gun, REMOVE each attachment, then DROP; a refused one is muted and says why. One
  `ItemActionIntent` replaced `UseIntent` and `DetachIntent`; the server does an action only when its handler offers it,
  and the client predicts it with the same handlers.
- **Guns:** a gun's slot shows its loaded round's icon at the bottom left. R tapped reloads with the loaded kind; held,
  it opens the gun's rounds (every kind of its calibre carried, with how many) and UNLOAD. Unloading the gun in hand
  rides the gun's own timeline. The gun's answers joined the one answer list.
- **Dropping:** a drag released anywhere but a slot drops, over panels and buttons too; on its own slot it is put back.
- **Workbench speed:** a workbench one level above a recipe's halves its time, two or more quarter it (recipes.json),
  fixed when the job starts; the crafting panel shows the time here.
- **Underneath:** `ItemPart` and one table give every item its parts and its one held behaviour (two are refused when the
  data is read); the zone's tick asks the held item's handler, and the client's hands ask its control (mouse, R, the
  radials, the HUD's ammo, the arms). First person picks nothing by item key any more: the torch's flame and the guns'
  pose are model catalogue fields. The console's `give` takes `<item>.blueprint`.
- Pictures from a hosted game (`--tour-steps held`): `docs/images/ground/held/ak-info.jpg`, `bag-charge-info.jpg`,
  `ammo-icon.jpg`, `ammo-radial.jpg`, `blueprint-learn.jpg`, `crafting-workbench.jpg`. `--ui-check` passes 21 of 21.
  Protocol 17; decisions V1–V12 in GROUND.md §11. Six new tests; 192 of 192 pass.

## 13 October 2026, the loading screen (`ground-loading`)

GROUND.md §4.3.1; decisions O1–O16.

- **The owner's question answered.** "Is it shaders compiling?" Partly. Measured on the owner's own game (hosted, 1600 ×
  900): the character arrived after 7.2 s and play began there, in a world still opening. The window then froze for
  8.8 s (the world view, the spare bodies, the sound, reading files, and 4.5 s of Godot loading the variants of every
  material's shader on first use), twice for 2.7 s (the impostor atlas finished on the main thread) and stuttered until
  24 s. Godot's shader and pipeline caches are on and persist; no shader was compiled from source. What costs is loading
  thousands of cached shader variants the first time each material draws.
- **One loading pipeline.** Each part registers the stage it owns (the hosted server's start, the connection, the
  world's things, the land, the forest, the renderer's warm-up, a run of steady frames); their words, weights and
  numbers are data (`loading.json`). The screen draws the total and the current stage; play (input, mouse look, the
  keys) starts only when every stage is done, then the screen fades. Neither knows which parts exist. A respawn or a far
  teleport runs the arrival's stages again, behind a short version of the screen when they take longer than 0.25 s.
- **The screen,** Rust-like in RustMVP's style: our own first world's golden meadow (rendered for it at 2560 × 1421, and
  the boot splash too), HOLLOW, where the game goes, the stage's words ("Starting the world", "Connecting", "Receiving the
  world", "Painting the land", "Growing the forest", "Warming up the renderer", "Arriving"), one bar with its percentage,
  and a tip. Crisp at any size through `Ui`. Picture: `docs/images/ground/loading/loading-screen.jpg`.
- **Everything the game can draw is warmed under it**, from the catalogues, not lists: every model at every stage and
  level of detail, both bodies in every garment and as first-person arms, every held item in first person, every grade
  and door of the building and the plan's ghost, every gib set, every effect of fx.json, the animals, the Tin Finch with
  its engines lit. Their files load from the first frame (6 at a time, kept for the game, sounds too). In play after the
  hand-over no pipeline is built; no frame went over 33 ms (with Godot's caches emptied, two in two minutes: 46 and
  118 ms).
- **Faster where it was blocking:** the impostor atlas on a worker, the planet's air and surface prepared while the server
  starts, the terrain putting more tiles a frame behind the screen, the hosted server at a higher priority, the game's
  C# optimized in Debug builds.
- **Timeline** (seconds from launch): before, playable at 7.2 in a frozen world, steady at 24.1; now, the screen from the
  first moment, play handed over at 20.1 on smooth frames (26.4 with Godot's caches emptied). An arrival of 826 m took
  1.05 s.
- Left: the world's opening still holds one frame for about 6 s under the screen (shader variants of the world and the
  characters); opening the world while a hosted server starts would hide more of it, but it needs the zone predicted
  and was not done. `--load-trace` (with `--load-shot`, `--arrival-check`) measures a start.
- Seven new tests (`LoadingTests`); with `ground` merged (P0, P3), 199 of 199 pass.

## 13 October 2026, placement (P2, `ground-p2`)

GROUND_SYSTEMS.md §6; GROUND.md §6.10 and decisions Z1–Z12.

- **One way to place anything.** Every placeable has one rule in data (`placement.json`'s defaults, its kind's block,
  and its own block next to its definition): where it may stand, how the aim finds its spot, its footprint, what must
  hold it up, slope, step, water, how R turns it, reach, building rights, limits. The solvers, rotations, checks and kinds
  are handlers found by name. The client's ghost and the server's verdict are the same code, and the server takes a
  placement only within 30 cm of its own. One `PlaceIntent` replaces six messages (protocol 18).
- **One ghost** for everything placed, in a hologram: pale blue when it fits, red with the reason under the crosshair,
  breathing until the server answers. Doors, the garage door, locks, the claim device, charges and the Tin Finch had
  none. P3's held controls place through it.
- **The playtest's fixes:** furnaces, boxes, campfires and bags stand on foundations and floors, and break when what
  they stand on goes (after a restart too); the Tin Finch's item is placed (with 50 of lamp oil); reach, sight, water and
  building rights are one rule for everything.
- Checked in a hosted game (`--tour-steps place`): `docs/images/ground/place/` (`furnace-fits.jpg`,
  `furnace-refused.jpg`, `garage-door.jpg`, `cupboard.jpg`, `code-lock.jpg`, `charge.jpg`, `tin-finch.jpg`,
  `tin-finch-placed.jpg`). A ghost costs 32 µs a frame (a wall on sockets 80 µs). The build sandbox's check passes 9 of 9.
  Nine new tests, one a rule; with `ground` merged (P3, the loading screen), 208 of 208 pass.

## 13 October 2026, interaction (P1, `ground-p1`)

[GROUND_SYSTEMS.md](GROUND_SYSTEMS.md) §5; GROUND.md decisions I1–I17. Everything the E key does is one system now: the
verbs are data, each thing's kinds of behaviour offer and do their own, one query finds what is aimed at, and one intent
carries every verb.

- **E as in Rust.** A tap does what the prompt says ("E  OPEN WOODEN DOOR", "E  SET CODE", "E  PICK UP ARROW", or why
  not); holding E for 0.35 s opens the radial of everything the thing offers, a disabled option saying why. The player
  walks on while a radial is open (every radial: E's, R's, the plan's, the hammer's). A code verb asks the keypad first.
- **The playtest's dead ends are fixed:** a fresh code lock offers its owner SET CODE first; the garage door shuts again
  (E through its rolled-up opening); the tool cupboard's options appear (authorise first for a stranger, its store for
  those on its list). The claim device is called the Tool Cupboard on screen.
- **New on E:** a deployable is picked back up as its item when empty and where the player may build; a dud is lit
  again; a wounded player is helped up (held E loots them); a team leader invites a player looked at; a workbench opens
  its tech tree; a ship's hatch, seat and tank by the part looked at. An open doorway no longer hides what is behind it.
- **Spent arrows stay where they stuck** (the owner's): in the ground, a wall, a door, a box, a tree, an animal or a
  player, riding a body's bone as it moves; taken back with E, from the living too; gone after 5 minutes, or falling
  when what holds them goes. No more arrows in bags. Every client draws them in one draw call.
- **Gone:** six intents and the building's, team's and ship's on-foot actions (one `InteractIntent` instead), the
  client's five pickers and its interaction kinds, the held-E options list and GroundView's hold-E code.
- Checked once in a hosted game through the keys (`--tour-steps interact`), pictures in
  `docs/images/ground/interact/`: `prompt.jpg` (E SET CODE on a fresh code lock, the hold hint), `code-lock.jpg` (E
  held: SET CODE current, OPEN, LOCK disabled, REMOVE LOCK), `cupboard.jpg` (the tool cupboard's radial), `arrow-wall.jpg`
  and `arrow-taken.jpg` (two arrows in the hut's wall, one taken back with a tap), `arrow-deer.jpg` (an arrow in a deer
  running off, small at that distance), `garage-open.jpg` and `garage-closing.jpg` (CLOSE GARAGE DOOR through the
  opening, the curtain coming down).
- Protocol 19 (P3 took 17, P2 18). Nine new tests (`InteractionTests`); with `ground` merged (P3, the loading screen,
  P2's placement), 217 of 217 pass. A frame's query and options cost 23 µs in the test's fan of views.
- **Merged with P2's placement:** placing is P2's `PlaceIntent` and E's verbs are P1's, so nothing is left of the door,
  lock and claim intents (messages 92–94 free); the ground is P2's one probe for both (P1's own copy went); the answers
  Seated and NotEmpty moved to 78 and 79, P1's capabilities after P2's `standing`. The tour's code lock and garage door
  go on through P2's ghost.

## 13 October 2026, the building's joints (`ground-fit`)

GROUND.md §6.3 (Fit) and decisions J1–J9. The owner: "Can't you really spot that gap between all building elements? …
textures/meshes glitching constantly, almost like flickering". Measured, then fixed at each cause:

- **Gaps: twig's art stopped short of its slots** (its slab edges 3–4.5 cm in, their tops 9–28 mm low, walls floating
  1–3 cm, the doorway's sticks 2–11 cm off the floor). Every drawn mesh of every piece, grade and level is now measured
  face by face at import and moved onto its slot (`art/import/building_fit.py`); twig overlaps its joints by 8 mm, and its
  doorway and wall frame got the rail and reveal the other twig walls have.
- **Flicker: the other grades stopped exactly on their slots,** so neighbours' faces lay in one plane (26.2 m² over 185
  joints: floors on wall tops, walls' tops and bases at corners and T's, roofs' beams, a wall's end and a floor's side).
  Walls are drawn 3 mm lower, sinking a little more toward their ends, their ends leaning a little; our roofs and stairs
  keep out of their neighbours' planes. 0.10 m² is left, out of sight or rare (an armoured foundation's bolts inside the
  slot between two, a roof turned back over another).
- **Corners:** walls' square ends left a 15 cm notch at every outer corner; a post shaped for the corner's angle fills it.
- **Levels of detail:** two levels of a batch were drawn at once over a metre at each switch; now one, with a metre of
  hysteresis. The stone floor frames' broken coarsest level is dropped. Depth precision was not a cause (D32F, reverse-Z).
- The building's gibs are fractured again from the fitted art.
- Pictures (`docs/images/ground/build/fit/`): every grade's nine joints before and after, the flicker maps, a walk along a
  wall's base. Flicker in the flat-faced grades' joints: 47,638 changing pixels before, 12,188 after (edges' aliasing).
  The stress block: 400 draw calls before, 405 after (the corner posts); 16.85 M and 16.79 M triangles; frame time
  within this PC's run-to-run swing (another game was running). One new test (`BuildingFitTests`: 298 meshes fill their
  slots); 218 of 218 pass; the sandbox's build check passes 9 of 9.

## 14 October 2026, the kernel (`ground-systems`)

GROUND_SYSTEMS.md §12 and decisions K1–K14. The owner asked whether the code was "a mess or a proper architecture",
heard that four things grew with every feature and every feature was wired through all four (`Zone`, 24 partial files
and 8,800 lines calling each other freely; `GameServer`, 12; `GameClient`, 14; `GameContent` with a 1,200-line reader
inside; one event struct of 17 fields for 12 kinds; a switch or chain for every message on both sides), and decided:
one dedicated server for now, no zone host and no transfers yet, but the rest fixed now, by applying §2's registry rule
one level up. Built in five packages, each landing with every old path moved and deleted:

- **K0, the zone is a host:** `Zone` keeps what every rule shares (the entities, the physics, the natural objects, the
  pose history, the clock, the content, the events) and is 360 lines; every rule is a `ZoneSystem` of its own in
  `src/Hollow.Simulation/Systems` (24 of them), listed once in `GroundSystems` in the tick's order, initialised in
  `InitOrder`, timed by the host, found by its accessor (`zone.Players()`, `zone.Building().World`, `zone.Ships().All`).
  A movement-only zone is the same host with `AutoRespawn`, `Ships` and `Damage`. The conversion was scripted (each
  member's new home chosen by its file; 575 call sites outside the simulation moved to the accessors; what was private
  and used by another system became internal, 154 members); three overloads that clashed across files were renamed
  (`TryLinkPose`, `ResolveContainer`, `DropExplosive`); `SetCheats` and the ground helpers are the host's. The 68
  `Content == null` paths went. Test: `ArchitectureTests.TheZoneIsAHost`.
- **K1, events are types:** twelve records in `Events/` (`HitConfirmed`, `Hurt`, `ItemsGained`, `Crafted`, `Died`,
  `ProjectileLaunched`, `ProjectileEnded`, `WentDown`, `GotUp`, `Detonated`, `Notice`, `Destroyed`); `Zone.Events`
  keeps them by kind and tells each subscriber the moment one is added; the server's 23-case route and its counters are
  subscriptions made once at start, the animals hear noises as they happen. Test: `EventTests.EventsAreTypedAndHeard`.
- **K2, messages find their handler:** every generated message implements `INetMessage<T>`; `MessageHandlers<TContext>`
  routes a packet's type byte to the one handler registered for it, without allocating; the schema hash is unchanged.
  Test: `NetTests.MessageHandlersRouteByTypeByte`.
- **K4, one reader per data file:** `Content/Readers`, nineteen readers in `ContentReaders.All`, the checks with the
  file they check and the cross-file ones last; `GameContent.cs` from 1,675 lines to 400, `Validation.cs` gone, the
  content hash and every problem text unchanged. Test: `ContentTests.EveryDataFileHasOneReader`.
- **K3a, the server is a host:** `GameServer` keeps the connection, logins, the tick loop, the snapshots of dynamic
  entities, chat, saving and shutdown (1,079 lines, no partial left but `AdminCommands.cs`); twelve `ServerModule`s in
  `src/Hollow.Server/Modules` (things, building, placement, ships, raid, world, animals, arrows, damage, survival, guns,
  console), listed once in `ServerModules`, each registering its intents in the handler table and hooked by the core
  (`Restore`, `Start`, `Joined`, `Left`, `BeforeStep`, `Input`, `AfterStep`, `Prepare`, `Describe`, `AtRest`, `Seen`,
  `Snapshot`, `Replicate`, `Finish`, `Save`); the per-player state a module keeps lives in `session.State<T>()` (the thing,
  build, explosive, arrow, ship and world views, the own state, the looks, the budgets), so the weak tables and the
  per-feature dictionaries are gone; the wire order of a tick is unchanged, and a 20-bot load test ticks in 0.7 ms as
  before. Test: `ArchitectureTests.TheServerIsAHost`.
- **K3b, the client is a host:** `GameClient` keeps the connection, the clock, the input, the prediction, the snapshots,
  the remote entities and chat (837 lines, no partial left); thirteen `ClientModule`s in `src/Hollow.Client/Modules`
  (survival, targets, world, building, placement, interaction, raid, ships, animals, arrows, console, damage, loading),
  listed once in `ClientModules`, each registering its messages in the handler table and hooked by the core (`Welcome`,
  `Spawned`, `Despawned`, `Seated`, `BeforePredict`, `AfterSnapshot`, `Sample`, `Render`, `Update`), found by accessor
  (`client.Raid().Explosives`, `client.Ships().Piloting`, `client.Building().World`); 385 call sites moved in the
  presentation, the bots and the tests; a packet nobody handles is counted (`Stats.UnhandledMessages`). Test:
  `ArchitectureTests.TheClientIsAHost`.

224 tests pass (218 before). The game runs as before (a frame rendered on the first world after each package, the
server a child process, the saved world loaded); 100 survival bots tick in 5 to 7 ms as before (§5.11). Deferred by the
owner: the zone host, transfers, the lifecycle with catch-up (ARCHITECTURE.md §10.1, §10.4, §12) and P6; nothing above
has to be redone for them.

## 14 October 2026, night: the developer panel (F2)

The owner, after the architecture tour: a human needs a window on the machine, not a console. So the F2 panel
(GROUND.md §8.5), in the F1 panel's shape: SERVER (the tick as a graph of the last minute against the budget, the
profile's sections as its tree, what the zone holds, the game's counters, the process), PLAYERS (everyone on: state,
where, ping, bandwidth both ways, inputs on time, in view), LOOK (what the crosshair is on from the same aim E uses,
as this client sees it and as the server describes it: every public member of the object its family resolves it to,
by reflection, so no kind of thing needs a line of its own; and the last intents answered), DRAW (hitboxes, the
ground tiles' collision mesh, the relevance circle, the animals' nav grid and spawn areas, drawn in the world and
staying on when the panel closes) and LOG (the server's lines, which a hosted game otherwise hides). The server
answers admins only and sends nothing while nobody watches: `DevModule` on both sides, `DevInspector`, six messages
(protocol 20), a window on the tick profile. The tour's step `f2` pictures it on a hosted game
(`docs/images/ground/play/f2-*.jpg`); the interface viewer shows it on sample data (`--screen developer...`).
Test: `DevTests.TheServerAnswersAWatchingAdmin`. 225 tests pass.

**Known problems in one place** (`docs/KNOWN_PROBLEMS.md`): the owner asked what else is known to be wrong besides
the chop desync and whether there is a plan. There was one, proposed at 3:49 am after the second playtest and never
approved, so none of the 30 findings was fixed while the kernel, the merges and the panel were built. The file lists
all 30 with their causes and packages, the gaps GROUND.md admits, the deferred structure, the decisions waiting on the
owner, and the plan; AGENTS.md and the handoff point to it.

## 14 October 2026, night: one place

The owner was tired of the game living in two places, so `ground` was merged into `main` with the scale proposal
(`docs/SCALE.md`, the owner's scale answers of the same day, waiting for approval). Conflicts in AGENTS.md, README.md
and GAME_DESIGN.md took `ground`'s side; AGENTS.md now says the whole game is on `main` and work happens there. Before
the merge, the developer panel's five missing Godot `.uid` files were committed on `ground` (0352bf5), and a
whitespace-only re-indent of `building.json` left in the old worktree was stashed (`git stash list`). The `scale`
worktree and branch were then removed; the old `ground` worktree is unused and goes once the Godot editor open on it is
closed. The merged checkout builds, its assets were imported, and the tests pass.
