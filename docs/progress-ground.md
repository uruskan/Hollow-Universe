# Ground game: progress log

Branch `ground`, in worktree `D:\GameDev\Projects\Hollow-wt\ground`. The plan and the decisions are in
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
