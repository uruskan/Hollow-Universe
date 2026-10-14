# Working on Hollow Universe (for AI agents and people)

Read first, in this order:

1. [docs/GAME_DESIGN.md](docs/GAME_DESIGN.md): what the game is.
2. [docs/LORE.md](docs/LORE.md): the setting (a vanished human civilisation, scrap technology, no factions, no
   aliens), before you make any model, texture, sound, name, item text or monument.
3. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): how it's built. Its principles (§2) and determinism rules (§8)
   are not optional.
4. [docs/GALAXY.md](docs/GALAXY.md): the universe generator, if you touch `Hollow.Galaxy` or the maps.
5. [docs/GROUND.md](docs/GROUND.md): the Rust-style ground game, if you touch planet surfaces, the server, the
   network or anything players do on foot.
6. [docs/handoff-ground.md](docs/handoff-ground.md) and [docs/progress-ground.md](docs/progress-ground.md): where the
   ground game stands, what to do next, how merges between work packages go, and what happened so far.
7. [docs/GROUND_SYSTEMS.md](docs/GROUND_SYSTEMS.md): the shared systems every ground feature goes through, if you
   touch interaction, placement, held items, item information, damage or anything a player does to a world thing.

## Rules

- **One system per concern.** Behaviour comes from capabilities declared in the data, each handled by one handler
  found in a registry. No if/else or switch chains over item kinds, target families or object types; a new feature
  extends a shared system rather than adding a path (the owner's rule, 13 October 2026; docs/GROUND_SYSTEMS.md).
- **C# only.** No GDScript. C++ only after a profiler proves a hot spot, and only with a note in
  ARCHITECTURE.md.
- **Nothing in `src/` may reference Godot.** Rules and simulation are engine-free; the Godot project in
  `game/` is presentation only. A test enforces this.
- **Generation must be deterministic** (ARCHITECTURE.md §8). In `src/Hollow.Galaxy` and anything else that
  generates the universe: no `System.Random`, `HashCode` (except inside `GetHashCode` overrides),
  `DateTime.Now`, or `Math.Exp/Log/Pow/Sin/Cos/Tan/Atan2`. Use `Hash64`, `Rng` and `DMath` from
  `Hollow.Core`. `ArchitectureTests` scans `src/` for these. Generation must not change when an optional data
  file is missing, and lookups by address must treat the address as untrusted.
- **Changing generated output** (any star, name or planet) changes the golden test values. Update them on
  purpose, in the same commit, and say so in the commit message.
- **No other game's files.** Models, textures, animations, sounds, maps and monuments from Rust, Elite Dangerous,
  No Man's Sky or any other game never enter the repository, not even as placeholders (GROUND.md §9 lists such
  files on this PC). Their mechanics, numbers and UI design may be used: the ground game follows Rust's, and its
  interface matches RustMVP's (owner's decision, 12 October 2026). Names stay our own.
- **Interface: crisp at any resolution** (ARCHITECTURE.md §15).
  - Build the UI with `Ui` (`game/scripts/Common/Ui.cs`): text styles, cards, rows and key hints.
  - Every length goes through `Ui.Px`, and anything that must follow the scale goes through `Ui.Track`, `Ui.Size`
    or `Ui.Place`.
  - Icons come from `UiIcons` (on the ground interface, item icons from `ItemIcons` and HUD glyphs from
    `GroundGlyphs`, both SVG rasterised at their final size). Labels drawn over a map go through `Labeller`, so they
    never overlap each other or the panels.
  - Never turn on a stretch mode or MSDF for UI text, and never hard-code pixel sizes.
- **Docs first.** A change to architecture or design updates the matching doc in the same commit.
- **Every change builds and passes the tests before it's handed over.** Anything visual gets a screenshot.

## Commands

```bash
dotnet build HollowUniverse.sln                  # everything, including the Godot client's C#
dotnet run --project tests/Hollow.Tests          # all engine-free tests
dotnet run --project tests/Hollow.Tests -- Galaxy   # only tests whose name contains "Galaxy"
dotnet run --project tests/Hollow.Tests -- MovementCheck   # the physics check (GROUND.md §4.5)
dotnet run --project tools/Hollow.NetGen      # after changing src/Hollow.Net/Protocol/Messages.cs: rewrites Messages.g.cs
dotnet run --project src/Hollow.Server -- --port 27015   # the dedicated server on the first world's landing site (--no-db,
                                                 # --db <path>, --lat, --lon, --spawn beach|origin, --day-scale <x>,
                                                 # --stand-in for the old test sphere; type help). F1 console admins:
                                                 # --admin <name> (repeatable), admins.txt (or --admins <file>),
                                                 # --dev (everyone is an admin); GROUND.md §8.2
dotnet run --project tests/Hollow.Tests -- Console    # the F1 console: parsing, completion, rights, commands over the network, god and noclip
dotnet run -c Release --project tools/Hollow.Bots -- --server 127.0.0.1:27015 --count 100   # bots (--script, --lag/--jitter/--loss)
dotnet run -c Release --project tools/Hollow.Bots -- loadtest --count 100 --duration 60   # server + 100 bots at the landing site: tick
                                                 # time, bandwidth, prediction (--spawn beach, --stand-in)
dotnet run -c Release --project tools/Hollow.Bots -- loadtest --count 100 --duration 60 --script survive --kit load-test-kit   # G2: bots that gather, craft and fight (GROUND.md §5.2)
dotnet run -c Release --project tools/Hollow.Bots -- loadtest --count 100 --duration 60 --script fight --kit gunfight-kit   # G4: bots in firefights, a kit every life (GROUND.md §5.3)
dotnet run --project tests/Hollow.Tests -- Gun        # G4: ballistics, recoil, damage, armour, refusals, client and server agreeing, a gunfight
dotnet run --project tests/Hollow.Tests -- Wounded    # G4: down, crawling, helped up, finished off, up by themselves or dead
dotnet run --project tests/Hollow.Tests -- Damage     # the damage pipeline's Destroyed event, in the zone and over the network (GROUND.md §6.4.2)
dotnet run -c Release --project tools/Hollow.Bots -- loadtest --count 100 --duration 120 --script build --kit build-kit   # G3: bots that build bases (GROUND.md §5.4)
dotnet run --project tests/Hollow.Tests -- Raid       # G5: raid costs, line of sight, duds, rockets, collapse, alerts, an offline raid, network, restart
dotnet run -c Release --project tools/Hollow.Bots -- loadtest --count 100 --duration 60 --warmup 20 --script raid   # G5: bots raiding claimed huts (GROUND.md §5.5)
dotnet run -c Release --project tools/Hollow.Bots -- loadtest --count 100 --duration 60 --warmup 20 --script world   # G7: bots looting, recycling, researching, in teams (GROUND.md §5.9)
dotnet run --project tests/Hollow.Tests -- ContentTests   # checks game/data/ground: references, ranges, every recipe makeable
dotnet run --project tests/Hollow.Tests -- World      # G7: loot, the recycler, tech trees and research, teams, map markers, over the network and a restart
dotnet run --project tests/Hollow.Tests -- Gather     # gathering: Rust's X and hotspot, the last hit, spots on the drawn surface, hits over the network
dotnet run -c Release --project tools/Hollow.GeneratorReport -- bench   # µs per generated star system
dotnet run -c Release --project tools/Hollow.GeneratorReport -- stats   # what generated systems contain
dotnet run -c Release --project tools/Hollow.GeneratorReport -- worlds  # one example of each notable kind of world
dotnet run -c Release --project tools/Hollow.GeneratorReport -- maps    # galaxy-wide maps per level (~4 min)
dotnet run -c Release --project tools/Hollow.GeneratorReport -- surface map|relief|zoom|stats|bench|site|spot|times
                                                 # the first world's surface: maps, speed, landing site, look-dev
                                                 # spots (forest|meadow|beach|mountain), sun and moon by the hour
blender -b --factory-startup --python art/build.py   # every item and world model → game/assets/items, world (GROUND.md §9.1)
dotnet run --project tests/Hollow.Tests -- Building   # the building rules, over the network and saved (GROUND.md §6.3, §6.5)
dotnet run --project tests/Hollow.Tests -- Ship   # G6: the Tin Finch's flight, boarding, building, prediction, storage (GROUND.md §6.7)
dotnet run -c Release --project tools/Hollow.Bots -- loadtest --count 20 --duration 40 --warmup 15 --script fly   # G6: bots that board their ships and fly loops
dotnet run --project tests/Hollow.Tests -- Animal   # G7: animals' AI, harvest, cooking, spawning, armour, cost, network and a restart (GROUND.md §6.9)
dotnet run -c Release --project tools/Hollow.Bots -- loadtest --count 100 --duration 60 --script survive --kit load-test-kit --animals 200   # G7: 200 animals among the bots (GROUND.md §5.10)
blender -b --factory-startup --python art/animals/build_animals.py [-- deer boar wolf bear] [--preview <png>]   # the animals → game/assets/animals (GROUND.md §6.9)
blender -b --factory-startup --python art/fx/build.py   # the effects' textures, decals and debris → game/assets/fx (GROUND.md §9.3)
blender -b --factory-startup --python art/gibs/build.py   # gibs: every model that breaks, fractured into its pieces → game/assets/gibs
                                                          # (GROUND.md §9.4; -- <keys> for some, -- --list for the plan)
dotnet run --project tests/Hollow.Tests -- Gib            # gibs: everything that can be destroyed has pieces, found as the Destroyed event names it
dotnet run --project tests/Hollow.Tests -- Loading        # the loading pipeline (GROUND.md §4.3.1): the plan, the bar, the hand-over, steady frames, arrivals, a join
# The building and door kits into game/assets/building (Blender 5.2.1, D:/GameDev/Tools/blender-5.2.1-windows-x64):
blender --background --factory-startup --python art/import/building_kit.py [-- twig wood stone metal armoured]
blender --background --factory-startup --python art/import/door_kit.py
# The sounds (GROUND.md §4.9, §9.2; Python's standard library and ffmpeg, HOLLOW_FFMPEG):
python -I art/audio/sources.py --fetch    # the CC0 downloads that are missing, each checked by SHA-256
python -I art/audio/build.py              # sounds whose recipe changed → game/assets/audio, sounds.json, levels.json
                                          # (--only <path prefix>, --force, --credits); after Godot imports new files, run it again
```

Character assets (GROUND.md §4.8, §9), rebuilt from text by Blender 5.2 in background mode. MPFB 2 lives in its own
Blender user folder, so set `BLENDER_USER_RESOURCES` first (on this PC `D:\GameDev\Tools\blender-hollow-config`):

```bash
blender -b --factory-startup --python art/characters/build_body.py        # body → game/assets/characters
blender -b --factory-startup --python art/characters/build_clothing.py    # garments (needs the body's cache)
blender -b --factory-startup --python art/characters/build_animations.py  # retargeted clips (needs the body's cache)
# The female body: the same three with `-- --body female` (human_female, clothing_female, animations_female).
python -I art/lib/import_settings.py                                       # Godot import options, after a first import
```

Each script takes `-- --preview <prefix>` (Eevee renders) or, for the animations, `-- --sheet <png>` (one pose per
clip).

Headless Godot (Linux workspace; `$GODOT` is the Godot 4.7.2 .NET binary; build the solution first):

```bash
$GODOT --headless --path game --import       # first run after a clone
# Render a frame once streaming has finished, save it and quit:
xvfb-run -a $GODOT --path game --resolution 1600x900 -- --shot sol.png
xvfb-run -a $GODOT --path game --resolution 1600x900 -- --position=-26,-21,26673 --distance 1100 --shot core.png
xvfb-run -a $GODOT --path game --resolution 1600x900 -- --view system --focus "Alpha Centauri" --shot acen.png
xvfb-run -a $GODOT --path game --resolution 1600x900 -- --view ground --name Ada --host --spawn origin --hour 10 --shot ground.png
xvfb-run -a $GODOT --path game --resolution 1600x900 -- --view ui --screen inventory --shot inventory.png
xvfb-run -a $GODOT --path game -- --view icons --out game/assets/items/icons   # render every item icon (GROUND.md §9.1)
xvfb-run -a $GODOT --path game --resolution 1600x900 -- --view models --shot models.png
xvfb-run -a $GODOT --path game --resolution 1600x900 -- --view monuments --focus launch-complex --shot lc.png   # every monument at real size (docs/MONUMENTS.md 8.5; view-monuments.cmd on Windows)
xvfb-run -a $GODOT --path game --resolution 1600x900 -- --view build --scene house --shot house.png
xvfb-run -a $GODOT --path game --resolution 1600x900 -- --view ship --scene forest --shot forest.png
xvfb-run -a $GODOT --path game --resolution 1600x900 -- --view ship --tour   # the whole flying loop through the keys; exit code 0 when it lands and steps out
xvfb-run -a $GODOT --path game --resolution 1600x900 -- --view animals --scene wolves --shot wolves.png   # the animal sandbox (GROUND.md §6.9)
xvfb-run -a $GODOT --path game --resolution 1600x900 -- --view sounds --sound rifle.shot --distance 320 --ambience --shot sounds.png
$GODOT --headless --path game -- --view sounds --sound-check   # every sound file loaded and measured in the engine
xvfb-run -a $GODOT --path game --resolution 1600x900 -- --view fx --sheet explosion --shot sheet.png   # effects (GROUND.md §9.3)
xvfb-run -a $GODOT --path game --resolution 1600x900 -- --view fx --stress firefight --measure 8   # what a firefight's effects cost
xvfb-run -a $GODOT --path game --resolution 1600x900 -- --view fx --gibs --at 0.5 --shot gibs.png   # every model breaking (GROUND.md §9.4)
xvfb-run -a $GODOT --path game --resolution 1600x900 -- --view fx --stress gibs --measure 6   # what a raid wall and ten barrels breaking cost
# The ground game's screenshot run (GROUND.md §5.6): a host that takes the pictures, and a second player that joins it.
# Paths are relative to game/, where Godot runs.
$GODOT --path game --resolution 1600x900 -- --view ground --name Ada --host --spawn origin --db /tmp/tour.db --hour 10 --tour ../docs/images/ground/play &
$GODOT --path game --resolution 1280x720 -- --view ground --name Bo --connect 127.0.0.1:27015 --hour 10 --tour guest
# Frame time with a crowd (GROUND.md §5.6): start a server and bots, then measure 20 s once 45 others are in view.
dotnet run -c Release --project src/Hollow.Server -- --spawn origin &
dotnet run -c Release --project tools/Hollow.Bots -- --server 127.0.0.1:27015 --count 50 --script survive --duration 150 &
$GODOT --path game --resolution 1600x900 -- --view ground --name Ada --connect 127.0.0.1:27015 --hour 10 --measure 20 --expect 45
```

Playing the ground game (Windows, from the repository's root; GROUND.md §4.3):

```bat
play-ground.cmd                 :: build, then host a game on this computer (port 27015)
play-ground.cmd join [address]  :: build, then join it as a second player (default 127.0.0.1)
play-ground.cmd menu            :: build, then open the main menu (PLAY, CONNECT, name, body, QUIT)
```

`GODOT` names the Godot executable if it isn't at `D:\Godot_v4.7.2-stable_mono_win64\...`, `PLAYER` the name (Host and
Guest by default); further arguments go to the game (`--body female`, `--mute`, ...). Hosting builds the dedicated
server in Release too and runs it as a process of its own, which stops with the game. A first run after new assets
arrive imports them (`$GODOT --headless --path game --import`).

On Windows or macOS, drop `xvfb-run -a` and run the Godot .NET executable directly (on Windows,
`Godot_v4.7.2-stable_mono_win64.exe`); the options are the same.

Client options (after `--`):
- Camera and selection: `--focus <name>`, `--position=x,y,z` (ly), `--distance <ly>`, `--yaw <deg>`,
  `--pitch <deg>`, `--select <name>`, `--search <text>`.
- System map: `--view system`, `--body <name>`, `--time <UTC>`, `--system-distance`.
- Surface (the first world, GROUND.md §3): `--view surface`, `--lat <deg>`, `--lon <deg>` (default: the landing
  site), `--alt <m above ground>` (1.7 = on foot), `--yaw <deg from north>`, `--pitch <deg>`, `--time <UTC>`,
  `--time-scale <x>` (how much faster than real the planet turns; the client setting `RotationTimeScale`),
  `--clouds <0–1>` (cloud cover, default 0.35), `--hide-ui`. Look-dev: `--flora-lineup <m>` with `--flora-lod 0|1|2`
  (one of each plant at that distance, at one level of detail; 2 = impostors), `--debug-sky <n>`. Screenshots pause
  the clock at `--time`.
- Surface keys: W A S D move, Q E down/up, right mouse look, Shift fast, G walk/fly, `[` `]` time ∓ 1 h (with Shift
  ∓ 6 h), T day speed (1×, 60×, 600×, 3600×), P pause time, H hide the interface, Esc back.
- Interface: `--ui-scale <factor>` (preference for this run), `--rescale <factor>` (change it after half a second,
  to test live rescaling).
- Character viewer: `--view character`; `--body <male|female>`, `--outfit <0–8>`, `--seed <n>` or `--player <name>` (the
  look `Looks.ForPlayer` gives that ID; N / Shift+N the next or previous seed), `--lineup <n>` (n players in a row
  facing the camera, looks from `--seed` on, `--spacing <m>`), `--item <key>` (any item key with a held pose in `data/ground/models.json`, e.g. `stone-hatchet`, `rock`, `pistol`, `rifle`, `bow`),
  `--motion <idle|walk|jog|sprint|crouch|crouch_walk|strafe_left|strafe_right|back|jump|fall|swim_idle|swim|down|crawl|sleep>`
  (down and crawl: wounded; sleep: logged off),
  `--aim <deg>` (view pitch), `--look <yaw,pitch>` (degrees: the head's free look, as others see it),
  `--event <swing|fire|reload|hit|death|helpup|use|eat|bandage|inject|motion:NAME>` with
  `--event-at <s>` (motion:idle from down plays getting up, motion:down from idle the fall), `--partner <male|female>`
  (a wounded player in front, who gets up when the character helps; `--partner-outfit <n>`, `--partner-distance <m>`),
  `--ads` (aim; with the bow, draw), keys U use, G help up, `--fp` (first person), `--fp-sheet <png|jpg>` (every held
  item in first person on one sheet, then quits; `--only a,b` some, `--full` the whole frame),
  `--ads`, `--vm-orbit <yaw,pitch,m>` (look at the first-person hands from another side), `--blink <0–1>` (hold the eyelids), `--slope` (stand on the ramp), `--face <deg>` (character yaw), `--lod <0–2>`, camera `--yaw`, `--pitch`,
  `--distance`, `--height`; `--debug` prints the character's meshes and hand IK once; `--hide-ui` (the picture
  alone); `--on-surface` (everything on the first world's ground and in its light, at the landing site or `--lat`,
  `--lon`, the clock held at `--time <UTC>`, e.g. `2026-10-12T03:00:00Z`; the characters face the sun, turned
  `--stage-yaw <deg>` (default 30) to its left; the meadow at `--lat 44.86225 --lon -170.00150 --stage-yaw 50` is
  clear for the camera);
  `--crowd <n>` adds n characters (every look, outfit, motion including lying and asleep, and held item including a
  drawn bow) and `--measure <s>` prints frame times (vsync off) and quits.
- Sounds (GROUND.md §4.9): `--view sounds` lists every sound event (a click plays it; Space play, W walk away, S stop,
  ↑/↓ the event, A ambience, B biome); `--sound <key>` selects one, `--play` plays it, `--walk` sends the source away
  from the listener (distance layers, arrival delay), `--distance <m>` places it; `--ambience` with `--biome
  Meadow|Forest|Taiga|Beach|Tundra|Alpine|Desert`, `--hour <h>`, `--wind <0–1>`, `--sea <m>`, `--forest <0–1>`,
  `--altitude <m>`, `--storm <0–1>`. `--sound-check` loads and measures every file in the engine and quits with exit
  code 0 when all agree with the pipeline's measurements; `--play-all` plays every event in turn (layered ones at 1, 50
  and 600 m) and quits, a smoke test of the director; `--trace` prints every voice started (layer, distance, dB) and
  the ambience beds' levels once a second.
- Effects (GROUND.md §9.3): `--view fx` plays every effect on its station while the camera turns (`--fx <key>`, with
  `--shot` caught `--at <s>` after it starts; keys ← → effect, ↑ ↓ family, Space replay, P pause, N night, R reload
  fx.json); `--sheet <family>` (contact sheet), `--strip <key> --times a,b,c`, `--action <family>` (a scene in use),
  `--stress firefight --measure <s>` (ten players fighting, effects off and on: prints what they cost and quits),
  `--night`, `--only-batch <name>`. Gibs (GROUND.md §9.4): `--gibs` (every model that breaks, in a grid, breaking at once;
  Space again, C the cause, P pause; `--gib-parts a,b` the building's parts shown), `--gibs raid` (a stone wall blown in,
  ten barrels shot), `--gibs <key>[/<part>]` (one close up, `--yaw`, `--distance`); with `--shot`, held `--at <s>` after
  the break; `--stress gibs --measure <s>` (the raid every 2.5 s, gibs off and on: prints what they cost and quits).
- Run: `--wait <s>`, `--trace` (prints streaming, exposure and timing once a second).
- Ground (GROUND.md §4.3): `--view ground` alone opens the main menu; `--body male|female` (default: the menu's last
  choice); `--mute` (no sound), `--sound-trace` (print every sound started); `--name <n>` with `--host [--port <n>]` (the
  dedicated server started as a process of its own; `--in-process` runs it on a thread of the game) or
  `--connect host:port` starts at once. `--lag <ms>`, `--jitter <ms>`, `--loss <percent>` (bad
  network through a local relay), `--netgraph` (open the network graph), `--face <name>` (turn toward a player once
  seen), `--db <path>` (the hosted server's database; the default is `user://hollow-host.db`), `--spawn origin` (hosted:
  arrive at the landing site rather than on a beach), `--time <UTC>` or `--hour <h>` (show another time of day: a
  universe time, or the next time it is h o'clock at the landing site; this client's sky only), `--tour <folder>`
  with `--tour-steps hud,berries,inventory,crafting,player,chop,mine,gun,build,base,charge,firefight,ship,world,bags,death,night,f1,look,foundations,held,place,interact`
  (the screenshot run; `--tour guest` is its second player, which also joins the firefight, watches the ship and takes
  the team invite; `f1` checks the F1 console in the game, as the host and admin, and pictures its five tabs; `chop` and
  `mine` picture the X, a hit's burst, the fall, the hotspot and a stage breaking, and `--tour-tree <kind>` picks the
  tree, e.g. `pine-tree`; `look` pictures the rock in hand, free look through Alt and the mouse, and a free foundation
  lifted by the aim; `foundations` checks P0's viewer flags and answers: E held on a fresh code lock's door offers its
  owner SET CODE first, and a hut's claim device opened through its wall is refused with its line; `held` pictures P3's
  held items: a gun's slot with its loaded round's icon, its rounds on R held, the AK's, the bag charge's and a blueprint's
  item panels (information and actions, LEARN), and crafting beside a level 2 workbench, faster; `place` pictures P2's
  one placement ghost, as in `docs/images/ground/place` (`--tour ../docs/images/ground/place --tour-steps place`): a
  furnace fitting on a foundation and refused against a doorway's post, a garage door in a doorway, the claim device, a
  code lock on a hut's door, a timed charge on its wall, the Tin Finch's hologram and the ship placed; `interact`
  pictures P1's interaction through the keys: the prompt and E held on a fresh code lock's door (SET CODE first), the
  tool cupboard's options, arrows stuck in a wall (one taken back with E) and in a deer, and a garage door shut through
  its opening),
  `--measure <s> [--expect <n>]` (once n others are in view, turns to them, measures the frame
  time for s seconds with vsync off, prints it with the slowest frames and what they did, and, when hosting, the
  server's tick; then quits), `--load-trace [s]` (GROUND.md §4.3.1: times the start from the process's start to steady
  play, the server ready, the connection, the world streaming, each loading stage, every frame over 33 ms with what
  it built, the warm-up, and quits after s seconds, 45 by default; `--load-shot <file>` saves the loading screen once
  the load is half done; `--arrival-check` teleports the hosting player to the landing site three seconds into play and
  times the arrival). Starting a game shows the loading screen until every part is ready (§4.3.1). Keys, as in Rust: W A S D, Shift sprint, Ctrl crouch, Space jump, mouse look (Left Alt held:
  free look, the camera and head turn while the hands do nothing; in the cockpit too); left mouse attack
  (with anything that places, a deployable, the Tin Finch's item, the building plan's piece, a door, a lock or the tool
  cupboard: place it where its ghost shows, pale blue when it fits, red with the reason when not; with food or medicine:
  use it; with a gun: fire; with the hammer: repair; with an explosive: throw it), right mouse aim or draw (with the
  plan or the hammer, held: its radial; with a charge: put it where its ghost shows; with a grenade: lob it); R reload
  (a gun, tapped: with the loaded kind; held: its rounds and UNLOAD; with something to place: turn it by its rule, a
  piece's facing, a door's side); E interact, a tap doing what the prompt says and held 0.35 s the radial of
  everything the thing aimed at offers, the player walking on meanwhile (GROUND_SYSTEMS.md §5: doors, a code lock's
  keypad, the tool cupboard, a dud to light, a fire or furnace, a crate or recycler, a workbench's tech tree, a player
  down to help up, a player close by to invite into the team one leads, a ship's hatch, seat and tank, spent arrows to
  take back, a deployable to pick up); 1–6 belt; Tab inventory (an attachment dropped on a gun goes onto it; the item panel's ACTIONS
  are the item's own: EAT, USE, WEAR, UNEQUIP, LEARN a blueprint, UNLOAD a gun, REMOVE an attachment, then DROP; a drag
  released anywhere but a slot drops); Q crafting (its TECH TREE tab at a workbench; a workbench above a recipe's level
  makes it faster); G the map (a click
  puts a marker down, a right-click takes it off; the team beside it); Enter chat (`/t ` before a line: to the team
  alone); F1 the console (GROUND.md §8.2: CONSOLE, INFO, NET, ITEMS and TOOLS tabs; Tab completes, Up and Down the
  history, Esc or F1 closes); Esc back, then the menu; F3 network graph; the death screen's buttons respawn. At a ship, E opens its hatch, boards, refuels or opens the cargo. Flying (G6): the mouse is the
  stick, W/S move the throttle lever (X: zero), A/D slide, Space/C rise and sink, Q/E roll, Shift boosts, Z switches
  flight assist, V the camera (chase or cockpit), F steps out, H the hatch, L the lock, J descends from orbit. Chat lines starting with `/` run as console commands
  (`/give rifle`), with the player's rights on any server. A hosted game makes its host the admin.
- Server (G2): `--kit <loot table>` gives every new character a loot table's items; admin commands `give`, `kit`,
  `heal`, `kill` besides G1's (type `help`; every command of the F1 console is in GROUND.md §8.2: `give <item> [amount]
  [player]` (`<item>.blueprint`: its blueprint), `teleport`, `tp2me`, `tpmarker`, `spawn <kind> [count]`, `god`, `noclip`, `time <hour>`, `timescale`,
  `ents`, `whereami`, `players`, Rust's names such as `inventory.give`, `global.teleport`, `env.time`; at the server's
  console a command names its player, and the older `give <name> <item> [count]` forms still work); G7: `learn <name> <item|all>` (blueprints), `world` (props, teams, economy). `--kit build-kit` gives what a base needs (G3). G5: `--raid-bases <n>`
  (and `--raid-radius <m>`; servers without a database) builds claimed huts on a ring for raid tests;
  `raidbase <name> [grade]` builds one in front of a player, listing them.
  `ship <name>` sets a Tin Finch down beside a player, theirs and fuelled (G6).
  G7's animals: `--no-animals` (none), `--animals <n> [--animal-radius <m>]` (a fixed population of n round the landing
  site, always out, for load tests); admin `animals` (how many of each, what they cost) and `spawn <species> <name> [n]
  [m]` (a herd of deer, boar, wolf or bear m metres in front of a player).
- Ship sandbox (GROUND.md §6.7): `--view ship`, local, on the first world's real surface: you start beside your Tin
  Finch on a vehicle workbench's bay. `--scene pad|hover|forest|cockpit|slope|ceiling` (screenshots hold the scene's
  camera), `--hour <h>` (the play clock's hour at the landing site; 9.5 by default) or `--time <UTC>`, `--trace` (the ship's speed, height, attitude and thrust once a second), `--tour` (plays the
  loop through the keys and prints the measurements). Keys on foot: W A S D, mouse look, E hatch and board, G shut the
  hatch, L lock, R refuel. In the seat: the mouse is the stick, W/S throttle lever (X zero), A/D strafe, Space/C up and
  down, Q/E roll, arrows a keyboard stick, Shift boost, Z flight assist, V cockpit or chase camera, F step out, H hatch,
  L lock, J descend from orbit; Esc frees the mouse.
- Animal sandbox (GROUND.md §6.9): `--view animals`, a local zone on the first world's real surface with scenes at
  clearings near the landing site: `--scene herd|wolves|bear|harvest|cooking` (screenshots wait for the scene's moment:
  the pack stalking, the bear rearing, a cut going in), `--hour <h>` (local solar time; each scene has its own),
  `--trace` (the scene's animals once a second), `--sound-trace`, `--mute`, `--hide-ui`. Keys: 1–5 scenes, `[` `]` the
  hour, the wheel zooms, the right mouse drags the view round.
- Checks: `--physics-check` runs the physics check inside the client and quits; its line must match the one
  `dotnet run --project tests/Hollow.Tests -- MovementCheck` prints.
- Build sandbox (GROUND.md §6.3): `--view build`, local, on the first world's stand-in surface. `--scene
  house|ghost-valid|ghost-invalid|stability|stress|empty|joints` (a built base; `stress` measures 300 frames and prints
  the frame time; `joints`: every grade's joints side by side, the twig group's frame for `--from`/`--at`, and with
  `--tour <folder>` (relative to game/) it photographs each grade's nine joints and a walk along a wall's base, counts
  flickering pixels (three pictures a third of a millimetre apart) and saves their maps, then quits), `--overlay`
  (stability colours), `--tool 1–6` (plan, hammer, three doors, claim device), `--piece <key>`,
  `--wheel plan|hammer` (open its radial), `--from x,y,z --at x,y,z` (the camera in the scene's frame, the empty scene's
  too: its first foundation's frame, unbuilt; the plan's aim is the camera's), `--finest` or
  `--lod n` (draw one level of detail). Keys: right mouse wheels, left places or repairs, R turns, E doors, F2 the
  stability overlay, F5 an hour of upkeep and decay. `--view build --scene empty --build-check` plays a round of
  building through the sandbox's input (place, snap, upgrade, turn, door, demolish and collapse) and quits with exit
  code 0 when every step passes.
- Ground interface viewer (GROUND.md §8): `--view ui --screen <screen>` shows the RustMVP interface on sample data
  (`hud`, `inventory`, `loot`, `corpse`, `furnace`, `campfire`, `crafting`, `death`, `wheel`, `hammer`, `menu`, the
  radial on other sources, `options` (a door's options, held E) and `ammo` (the pistol's rounds, held R), and
  G7's `map`, `techtree`, `recycler`, `research`, and the F1 panel's `console`, `console-info`, `console-net`,
  `console-items`, `console-tools` on sample data);
  `--pointer x,y` holds the pointer at a point of the 1600 × 900 design canvas. Without `--shot` it's interactive.
  `--view ui --ui-check` drives the inventory, crafting and the radial with real input events (drag, middle-drag,
  split, quick move, drop, craft; the right mouse on the plan, E tapped, held, chosen and cancelled) and quits with
  exit code 0 when every check passes.
- Models (GROUND.md §9.1): `--view models` stands every catalogue model on the ground (`--lineup items|world`,
  `--pitch <deg>`); `--model <key>` shows one on a turntable (`--stage <n>`, `--lod <n>`, `--yaw <deg>` holds it at an
  angle, `--zoom <f>` moves the camera, `--anim <clip>` plays a clip such as the Tin Finch's `Hatch_Open`, `--attach a,b`
  mounts attachments on a gun, `--upright` shows a held item as held, `--first-person` shows a held item at its
  first-person pose; keys Tab, ←/→, ↑/↓, 0/1/2, A, Space, F, H).
  `--survey-surfaces res://data/ground/surfaces.json` measures the outcrops' stage models for the weak spot (GROUND.md
  §6.1; run it whenever those models change) and quits.
  `--view icons --out <dir>` renders every item icon at 128 and 256 px (`--only a,b` for some) and then shows the
  contact sheet, which `--view icons` alone shows too (`--only` limits it). `--placeholder-icons` keeps the interface's vector drawings.

## Layout

| Path | What |
|---|---|
| `src/Hollow.Core` | Vectors in double precision, `DMath`, `Hash64`, `Rng`, units |
| `src/Hollow.Galaxy` | Density model, addresses, sectors, cells, catalogue, names, star systems |
| `src/Hollow.Surface` | Planet surfaces (GROUND.md §4.6): `ISurface`, `ZoneFrame`, `PlanetSurface` (plates, climate, biomes), cube-sphere tiles, collision, scatter, `SkyEphemeris`, the stand-in `SphereSurface`, `FirstWorld` |
| `src/Hollow.Simulation` | Rules and simulation of the ground game: entity store, zones, Jolt physics world and ground tiles, character movement (GROUND.md §4.5); the survival game (§6.1): game data, items and crafting, natural objects and gathering, vitals, deployables, combat with lag compensation; building in `Building/` (structures, sockets, placement, stability, hammer, doors and locks, the claim device, upkeep and decay; GROUND.md §6.3); G4 (§6.3): firearms (`Combat/Firearms.cs`: recoil, spread, cadence, reload, ballistics), bullets and damage (`Zone.Guns.cs`), the wounded state (`Zone.Wounded.cs`); the damage pipeline (§6.4.2: `Combat/Damage.cs`, `Zone.Damage.cs`: one request, one entry point, a receiver per kind of thing in each domain's file, the Destroyed event); G5 (§6.6): explosives, blasts with line of sight, rockets and explosive rounds, raid hits (`Zone.Raid.cs`, `Zone.Blast.cs`, `Raid/`); G6 (§6.7): ships in `Ships/` (`ShipDef`, `ShipState`, `ShipMotor`, `ShipCollider`) and `Zone.Ships.cs`; G7 (§6.8): world props and where they stand (`Zone.World.cs`, `Nature/LootSpots.cs`, `World/WorldObjects.cs`), the recycler, research and tech trees (`Economy/`, `Items/Blueprints.cs`, `Content/World.cs`), teams (`Teams/`), map markers |
| `src/Hollow.Net` | Protocol (GROUND.md §4.4): message definitions and their generated serializers, bit packing, snapshot delta coding, relevance grid, bad-network relay; `Commands/`: the F1 console's command catalogue, parsing and Tab completion (§8.2) |
| `src/Hollow.Persistence` | SQLite storage (GROUND.md §4.7): accounts and characters, versioned schema, one worker thread; the building's records (`BuildingCodec`, §6.5); explosives (`ExplosiveCodec`) and queued notifications (§6.6); ships (`ShipCodec`, §6.7); teams and world props' overrides (`TeamCodec`, schema 6, §6.8) |
| `src/Hollow.Client` | The client without graphics (GROUND.md §4.3): connection, clock sync, prediction and reconciliation, interpolation, statistics; `LocalPlayer`, the survival API the UI binds to (§6.2); `LocalGun`, the predicted gun and its events for the visual layer (§6.4); `Audio/`: the sound catalogue model, a BS.1770 loudness meter, distance layers, footstep and ambience rules (§4.9); `BuildingReplica`, the building in range and the ghost check, and the building intents on `LocalPlayer` (§6.5); explosives, detonations and raid alerts (`GameClient.Raid.cs`, `LocalPlayer.Raid.cs`, §6.6); ships: `ShipPredictor`, `RemoteShip`, `GameClient.Ships.cs`, the ship intents on `LocalPlayer` (§6.7); G7 (§6.8): blueprints and tech trees, teams, map markers, world props (`LocalPlayer.World.cs`, `GameClient.World.cs`), the zone's map and its picture (`Map/ZoneMap.cs`); `Loading/`: the loading pipeline (§4.3.1: `LoadingPipeline`, `LoadStage`, `LoadingPlan`, `SteadyFrames`, `QuietStream`) and the client's own stages (`GameClient.Loading.cs`) |
| `src/Hollow.Server` | The dedicated server (GROUND.md §4.2): console program and library (Host mode), a host of modules (GROUND_SYSTEMS.md §12.5: `ServerModule`, the list `ServerModules`, one class per concern in `Modules/`: static things and natural objects, the building, placement, ships, raiding with the notification service (`Notifications/`: raid alerts, GROUND.md §6.6), the world's props, teams, blueprints and maps (§6.8), animals, arrows, damage, the survival intents and own state, guns, the F1 console's rights, clock and commands (§8.2)); the host keeps the connection, logins, the tick loop, the snapshots, chat, saving and the console's command switch (`AdminCommands.cs`) |
| `game/` | Godot project: galaxy map, system map, ground mode (`scripts/Ground`), UI, shaders |
| `game/scripts/UI` | The ground game's interface (GROUND.md §8): `GroundUi` (HUD, inventory, loot, crafting, death, the radial menu in `GroundUi.Radial.cs` with its glyphs `RadialGlyphs`, menu, the F1 panel in `GroundUi.Console.cs`), the view model `Model/IGroundUi` with `MockGroundUi` (and `IConsoleModel` with `MockConsole`; the radial's contract in `Model/RadialTypes.cs`), `ItemIcons`, the loading screen (`LoadingScreen`, §4.3.1), and the viewer (`Viewer/`) |
| `game/assets/building` | The building and door kits as GLB (three levels of detail, Godot collision names) and their 2K textures (GROUND.md §6.3) |
| `game/scripts/Build` | The build sandbox (`--view build`): batched rendering of structures, the plan's ghost, doors, the HUD lines; the plan's and the hammer's radial sources (`BuildingRadials.cs`), shared with the game |
| `src/Hollow.Simulation/Animals` | G7's animals (GROUND.md §6.9): `AnimalWorld` (the staggered AI tick, perception, the brain, steering, spawning), `NavGrid` (ground, water, slopes and trunks in 2 m cells, short paths), `AnimalHitboxes`; the zone's side in `Zone.Animals.cs`; the numbers in `game/data/ground/animals.json` |
| `src/Hollow.Simulation/Targets`, `Registry.cs`, `Capabilities/`, `Interaction/`, `Placement/` | The shared systems ([docs/GROUND_SYSTEMS.md](docs/GROUND_SYSTEMS.md)): `TargetRef` and target families (`Zone.Targets.cs`), `Targeting` (one aim query), `SupportLink` (`Zone.Supports.cs`), one reach and sight rule (`Zone.Reach.cs`), lifetimes, capability handlers and their viewer flags and interaction verbs, the placement rules, solvers and checks (`Placer`); the answers' codes (`Outcome.cs`, texts in `texts.json`); the damage pipeline (`Zone.Damage.cs`); held behaviours by item part (`Content/ItemParts.cs`, `Zone.Held.cs`), information rows, item actions and crafting speed (`Items/ItemRows.cs`, `ItemActions.cs`, `CraftingSpeed.cs`) |
| `src/Hollow.Client/Loading` | The loading pipeline (GROUND.md §4.3.1): stages registered by the parts that own them, `loading.json` for titles, weights and tips |
| `game/scripts/Ground/Held`, `ClientGroundUi.Interaction.cs`, `PlacementView.cs`, `PlacementGhost.cs`, `ArrowsView.cs` | The game's side of the shared systems: held-item controls and the ammo radial, the hold-E radial and prompts, the placement controller and hologram ghost, spent arrows drawn where they stuck |
| `game/scripts/Gibs`, `art/gibs`, `game/assets/gibs` | Destroyed things breaking into pieces of our own models (GROUND.md §9.4): the gib runtime (no physics bodies, pooled, batched), the offline fracture script, the pieces; `gibs.json` for their behaviour |
| `game/data/ground` (shared systems) | `capabilities.json` (what world things can do), `interactions.json` (the E verbs), `placement.json` (placement rules and placeables), `texts.json` (every refusal's words), `loading.json`, `gibs.json`, `surfaces.json` (outcrops' drawn tops); lists that go on the wire are append-only |
| `game/scripts/Animals` | Animals drawn (GROUND.md §6.9): `AnimalView` (model, clips, footsteps, voice, blood), `AnimalsView` (the seam: `Client` and `Refresh`), the sandbox (`--view animals`) |
| `art/animals` | `build_animals.py`: the deer, boar, wolf and bear in Blender from text (metaball anatomy, skeleton, clips) → `game/assets/animals` |
| `game/scripts/Ships` | Ships drawn and flown (GROUND.md §6.7): `ShipView` (model, plumes, lights, hatch), `ShipCamera`, `ShipControls`, `ShipHud`, the ship sandbox (`--view ship`, `--tour`) |
| `game/data/ground` | The ground game's numbers; `building.json`: pieces, grades, sockets, costs, doors, locks, the claim device |
| `art/import` | Blender scripts that convert the PSG kits into `game/assets/` |
| `game/assets/fonts` | Inter (SIL OFL 1.1), the interface font; Barlow and Barlow Condensed (SIL OFL 1.1), the ground interface's |
| `game/assets/loading` | The loading screen's picture of the first world and the boot splash (our own renders, GROUND.md §4.3.1) |
| `game/scripts/Ground/RenderWarmup.cs`, `GroundWarmup.cs` | The renderer's warm-up (GROUND.md §4.3.1): every library's `WarmItem`s (`ModelWarm`, `CharacterWarm`, `BuildingWarm`, `GibWarm`, `FxWarm`, `AnimalWarm`, `ShipWarm`, beside each library) loaded from the first frame and shown once under the loading screen |
| `game/scripts/Characters` | Player characters (GROUND.md §4.8): `CharacterView` (third person), `ViewmodelView` (first person), animation graph, IK and aim, ragdoll, wardrobe, held items, the `--view character` viewer |
| `game/assets/characters` | Player body, clothing and animations (GLB), their textures and JSON facts, built by `art/` |
| `game/assets/THIRD_PARTY.md` | Every third-party file: source, version, licence, checksum |
| `art/` | Blender Python scripts that build assets from text: the character body, clothing and held-item tests (character package), and the item and world models: `hollow/` (shared code), `import/` (the kits), `models/` (our own), `build.py`; `art/.cache` is scratch (not committed) |
| `game/data/ground` | The ground game's data (GROUND.md §7): items, recipes, natural objects, deployables, loot, survival constants, crafting tabs, weapons, building, explosives and raid alerts, the world's economy and progression (`world.json`, G7) (JSON, read by server and client); `sounds.json`, the sound catalogue (client only, §4.9); `loading.json`, the loading screen's stages, weights, tips and picture (client only, §4.3.1) |
| `game/scripts/Audio` | The sound (GROUND.md §4.9): `AudioDirector` (pooled voices, limits, priorities, distance layers, occlusion), `AudioBuses`, `FootstepDriver`, `AmbienceDirector`, `SoundLibrary`, `SoundMeter`, the viewer (`--view sounds`) |
| `game/assets/audio` | The sound files (Ogg Vorbis, 48 kHz; interface clicks WAV) and `levels.json`, their measurements; built by `art/audio` |
| `art/audio` | The sound pipeline (GROUND.md §9.2): sources and their checksums, recipes (`events.py`), the build, our 7z reader, sounds made in code, `inspect.py` (waveform and spectrogram pictures) |
| `game/scripts/Surface` | The surface view: terrain LOD, water, sky and air, lights, clouds, plants (`Vegetation`), shader grass (`GrassField`), look-dev camera, time controls |
| `game/scripts/Surface/Flora` | Plants made in code after hollow-orbit's flora: painted atlas and bark (`FloraTextures`, `Painter`), geometry (`FloraGeometry`), species and variants (`FloraLibrary`), impostors baked at start (`ImpostorBaker`) |
| `game/assets/surface/ground` | The eight ground texture layers (Megascans and Poly Haven, `game/assets/THIRD_PARTY.md`), built by `tools/blender/surface_ground.py` |
| `game/scripts/Items` | Item and world models (GROUND.md §9.1): `ModelCatalog` (reads `data/ground/models.json`), `CatalogItemIcons`, the icon renderer (`--view icons`) and the model viewer (`--view models`) |
| `game/scripts/Fx` | The effects library (GROUND.md §9.3): `FxDirector` (the API: `Play`, `Impact`, `Muzzle`, `Tracer`, `Start`/`Stop`, `CameraShake`), world particle batches, muzzle flashes, tracers, decals, lights, fire loops, the catalogue (`data/ground/fx.json`) and the viewer (`--view fx`) |
| `game/assets/fx` | The effects' textures (the PSG C4 VFX kit converted, our own flash atlas, noise and decals), debris meshes and shaders, built by `art/fx/build.py` |
| `game/scripts/Gibs` | Gibs (GROUND.md §9.4): `GibDirector` (the API: `Break` what the Destroyed event names), `GibWorld` (every piece: integrator, floors, batched drawing), `GibLibrary` (the sets by key and grade), `GibCatalog` (`data/ground/gibs.json`); the hookup is `Ground/GibsView.cs` |
| `game/assets/gibs`, `art/gibs` | The pieces things break into (one GLB per model or kit, parts by key), built by `art/gibs/build.py` from the models themselves |
| `game/assets/items`, `game/assets/world` | Item and world models (GLB with LODs, hulls and sockets) and the item icons (`items/icons/128`, `items/icons/256`), built by `art/` |
| `game/assets/materials`, `game/assets/textures` | The shared Godot materials the models' `.import` files map to, and their textures (2K at most) |
| `tests/Hollow.Tests` | Test runner (no NuGet packages needed) |
| `tools/Hollow.CatalogBuilder` | Converts the HYG star catalogue (or AT-HYG) plus a hand-entered supplement (`supplement.csv`) into `game/data/catalog/hyg41.bin.gz` |
| `tools/Hollow.GeneratorReport` | Measures the generator: speed, contents of generated systems |
| `tools/Hollow.NetGen` | Writes the network serializers (`Messages.g.cs`) from `Messages.cs` |
| `tools/Hollow.Bots` | Headless clients with scripted movement, survival and building, and the load test (GROUND.md §5) |
| `third_party/` | Vendored NuGet packages (LiteNetLib, JoltPhysicsSharp, Microsoft.Data.Sqlite), so the build needs no network; see its README |
