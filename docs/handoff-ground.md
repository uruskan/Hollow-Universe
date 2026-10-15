# Ground game: handoff to the next session

Written 13 October 2026, morning, when the first session filled its context; the kernel of 14 October added on top. Read this first, then
[GROUND.md](GROUND.md) (design and everything as built) and [progress-ground.md](progress-ground.md) (what happened).
Memory files `ground-game-plan`, `earlier-rust-attempts-and-assets`, `grass-is-a-shader-system` and
`minimal-automated-testing` hold the owner's standing decisions.

## 15 October 2026, afternoon: S3c, S3d and S4 on `main`

The owner said "finish S3c, S3d and S4 right now". Three packages were built in worktrees under `Hollow-wt\` (`s3c`,
`s3d`, `s4`) and merged into `main` the same day (8d62fe5, dc233cc, dad661c, 73c273a), beside the Iron Wake session's
IW4 and IW2. Near the end of the week's usage the owner asked for fewer tests: the build is green on every merge and the
engine-free suite passes on the final `main` with IW2 (268 of 268, 73c273a), but most Godot checks were skipped (list below), to run once the limits
reset, after the owner's own playtest.
- **S3c** (WHOLE_WORLD.md §14, "S3c as built"): the hosted game draws the whole world as a ball from any `ISurface`
  (terrain, sea, the planet's sky and the ball's horizon, plants from the zone's natural objects, grass on the cube
  faces, things falling toward the centre, a 15 km far plane). `ISurface` has a drawing contract with defaults
  (`HeightBounds`, `Sample`, `Cover`).
- **S3d** (WHOLE_WORLD.md §14, "S3d as built"): one map for G and the death screen, a globe that becomes a flat chart,
  the grid "C K12" shared with `whereami` and `teleport <cell>`. Finding 1 is done.
- **S4** ([WALKING_GROUND.md](WALKING_GROUND.md)): the design (16 decisions waiting for the owner), the generator
  `WalkingWorld` and its look-dev renders; **S4c**: the game draws it with 16 ground layers built at 4K from the owned
  scans, lakes and streams as water. It is **off by default**: `play-ground.cmd --ground walking`. Protocol 24 (IW2 may
  have taken the next).
- **The owner's word on assets (15 October):** everything in `D:\GameDev\Assets` is paid for and fully licensed; use it
  at full quality, no licence checks (files extracted from other games stay out, as ever).
- **Left, in order:** the owner's playtest and their verdict on WALKING_GROUND.md's decisions; S4b (the generator's
  speed: 1.75 µs a height and 10.5 ms a physics tile against 1 µs and 5 ms; the scatter cache; the server's 10 s start;
  the 100-bot load tests on it); S4d (the scanned cliff and rock meshes along cliffs, sea cliffs and canyons); S4e (going
  live: the default ground, the landing site, a save wipe, arrivals off lakes and plants, the nav grid's water); S5
  (clouds from above, the thin air from space, clouds drawing over the near ground on the small world).
- **Seen in S4c's shots, to fix:** grass tufts on snow fields and frozen lakes, white dashes along flat beaches' waterline,
  bare grey mountain faces until S4d, a player arriving inside a fern.
- **Skipped checks, for the review after the limits reset:** Godot's `--physics-check` against `MovementCheck`; a
  measured frame time with a crowd; the ground tour; the ship (Tin Finch and Iron Wake) and animal sandboxes and the
  look-dev view after S3c's changes; the main menu; the stand-in and the real planet drawn with 16 layers; the
  100-bot load tests on the walking ground; Godot `.uid` files for S3c's new scripts (`PlanetGlobals.cs`,
  `PlantSource.cs`, `NaturePlants.cs`, `surface_planet.gdshaderinc`); a native Jolt crash seen once during a test run
  beside three Godot games (KNOWN_PROBLEMS.md).

## 15 October 2026: the Iron Wake finished, unverified in play

Every Iron Wake package is on `main`:
- the art fixes (closed hull, whole barrels into real breeches, new paint and tread);
- IW2: seats, doors, the helm's crew list, keys;
- IW3: the crewed batteries;
- IW4: the incendiary rocket and fire;
- IW5: building aboard;
- IW6: sounds, effects, interior light, people in seats.

Near the week's usage limit the owner asked for the least testing: each package built and ran its own tests once, and
nothing was looked at or heard in Godot. **For the review after the reset** (each package's "As built" note in
IRON_WAKE.md lists its own gaps):
- the new surface shader `iron_wake_surface.gdshader`, never compiled;
- the loading mechanisms posed in play;
- the gunner's sight and keys: R tapped or held, Tab;
- sound levels and the reverb presets;
- lamp brightness, the probes and occlusion culling through open doors;
- the seated poses (hips on the seat, hands on the grips);
- the reworked rocket trails and fires;
- a gunner firing while the pilot turns hard, and under packet loss.

Not built:
- the helm's light switch;
- authored pilot and gunner poses (everyone uses `sit_idle`);
- first-person hands on the grips;
- `iw.alarm.fire`.

## 14 October 2026, late night: the Iron Wake designed

The owner gave the game its benchmark ship, the Iron Wake (`D:\GameDev\Assets\IronWake_Buildable_R01`): a 58 m carrier
with walkable decks that players furnish, six crewed rocket cannons with a visible loading mechanism, and a new
incendiary rocket. The design is [IRON_WAKE.md](IRON_WAKE.md), **approved by the owner on 15 October 2026** with all
fourteen defaults (§2). On 15 October 2026 its import (model, materials, LODs, the data file), its sounds and its
effects were merged into `main` (IW-A part 1, IW-S, IW-F), then the loading mechanisms, the battery table
(`batteries.json`) and the incendiary rocket's model (IW-A part 2, 2c7677b). IW1 (ship spaces: walking aboard) was
built on `iw-spaces`, cut from `whole-world` with `main` merged in, and fast-forwarded into `whole-world` (1f7b89a, 239
tests): people aboard move in the ship's frame, protocol 22, schema 8 (IRON_WAKE.md §4.13). **On 15 October 2026 the
owner had `whole-world` merged into `main`** (S3a, S3b and IW1), so the game has the Iron Wake: the host gets one with
`ship iron-wake` (F1), on the plain stand-in world until S3c. IW2–IW6, S3c and S3d follow on `main`.

## 14 October 2026, night: one place (read this first)

**Known problems live in [KNOWN_PROBLEMS.md](KNOWN_PROBLEMS.md)** since 14 October 2026, night: the second playtest's
30 findings with their causes and packages (none fixed yet; the three-wave plan waits for the owner's go), the gaps
GROUND.md admits, the deferred structure and the decisions waiting on the owner. A finding goes there the day it is
found.

At the owner's request the `ground` branch was merged into `main`, together with the scale proposal
([SCALE.md](SCALE.md), waiting for the owner's approval). The whole game is on `main`, in
`D:\GameDev\Projects\Hollow Universe`. The `scale` branch and worktree were removed; the old `ground` worktree is no longer
used and goes as soon as nothing has it open (a Godot editor did). Below, "`ground`"
means the line of work that is now `main`. New work happens on `main`; a package that needs isolation gets a
short-lived branch and worktree under `D:\GameDev\Projects\Hollow-wt\`, merged back into `main` when green.

## State on 14 October 2026, evening: the kernel

- **`ground` holds the kernel** (GROUND_SYSTEMS.md §12, packages K0–K4, merged from `ground-systems` on 14 October
  2026): the zone is a host of `ZoneSystem`s, events are types, messages find their handler in a table, the server and
  the client are hosts of modules, and every data file has one reader. 224 tests pass. The game runs as before (a frame
  rendered on the first world, the saved world loaded; 100 bots tick in 5 to 7 ms). The packages' branches (`ground-k2`,
  `ground-k4`, `ground-k3a`, `ground-k3b`, `ground-systems`) are merged; their worktrees under `Hollow-wt\` can go.
- **The F2 developer panel** (GROUND.md §8.5, 14 October 2026, night; branch `dev-panel`, merged): F2 in the game opens
  the developer's window: SERVER (the tick's graph and profile sections, what the zone holds, the game's counters, the
  process), PLAYERS, LOOK (what the crosshair is on as both ends see it; the server describes any object by reflection),
  DRAW (hitboxes, ground tiles, the relevance circle, the nav grid, the spawn areas, drawn in the world) and LOG (the
  server's lines). The server answers admins only (a hosted game's host is one); protocol 20. `DevModule` on both
  sides, `DevInspector`, `GroundUi.Developer.cs`, `ClientDeveloper`, `DevDraw`, `MockDeveloper`; the tour's step `f2`
  takes `docs/images/ground/play/f2-*.jpg`. The owner asked for it after asking what the architecture is and how to
  start the game; next on that list: the front door (the menu as the default start, a Developer page listing the
  viewers, a desktop shortcut) and the merge of `ground` into `main`, which needs the owner's word.
- **Where things go now** (the rule of §12 is the rule of §2 one level up):
  - a rule of the game: a `ZoneSystem` in `src/Hollow.Simulation/Systems`, listed once in `GroundSystems` (its position
    is its place in the tick; `InitOrder` if its `Init` must follow another's); its file ends with its accessor, so
    callers write `zone.Guns()`, `zone.Building().World`, `zone.Ships().All`; what it needs of another system it finds
    once in `Init` and keeps (`Zone.Require<T>()`);
  - something that happened: a record in `src/Hollow.Simulation/Events`, added with `Events.Add(new X(...))`; whoever
    cares subscribes once (`Events.On<X>`): the server's route, a system's hearing, a test reads `Events.Of<X>()`;
  - a message: its struct in `Messages.cs` as before, and one `handlers.On<X>(...)` in the module that owns it, on
    each side; nothing else routes;
  - the server's or the client's part of a concern: a `ServerModule` in `src/Hollow.Server/Modules` or a
    `ClientModule` in `src/Hollow.Client/Modules`, listed once in `ServerModules` / `ClientModules`, with its accessor
    (`server.Raid()`, `client.Ships()`); per-player state a server module keeps goes in `session.State<T>()`;
  - a data file: its reader in `src/Hollow.Simulation/Content/Readers`, listed in `ContentReaders.All`, with its check.
  - The checks that keep this shape: `ArchitectureTests.TheZoneIsAHost`, `TheServerIsAHost`, `TheClientIsAHost`,
    `ContentTests.EveryDataFileHasOneReader`, `NetTests.MessageHandlersRouteByTypeByte`, `EventTests`.
- **Renamed in K0** (the names a reader of older notes will miss): the supports' `TryPose(in SupportLink)` is
  `TryLinkPose`; the actions' `Resolve(Player, ContainerRef)` is `ResolveContainer`; the explosives' `Drop(LiveExplosive)`
  is `DropExplosive`; `Zone.Building` is `zone.Building().World`, `Zone.Animals` is `zone.Animals().World`,
  `Zone.Placement` is `zone.Placement().World`, `Zone.Ships`/`Arrows`/`Explosives` are `zone.Ships().All` and so on;
  `Zone.GameEvents` is `Zone.Events`; the event kinds `DamageTaken`, `CraftFinished`, `Wounded` and `Recovered` are the
  records `Hurt`, `Crafted`, `WentDown` and `GotUp`. On the client (K3b): `client.Animals`, `Ships` and `Arrows` are
  `client.Animals().All` and so on, `client.Building` is `client.Building().World`, `client.BuildingReplica` is
  `client.Building().Replica`, `client.Placement` is `client.Placement().World`; everything else keeps its name behind
  its module's accessor (`client.Survival().Things`, `client.Raid().Explosives`, `client.Ships().Piloting`). On the
  server (K3a): a concern's members sit behind its module's accessor (`server.Raid().Notifications`,
  `server.Console()`, `server.Survival()`); what a module remembers per player is `session.State<T>()`.
- **Deferred by the owner (14 October 2026):** the zone host (many zones in one server), transfers between zones, the
  lifecycle with catch-up for unloaded time, and P6 (one model for every world thing). A second zone is a second host
  with the same lists; nothing of the kernel has to be redone for them.
- **Left, in order:** the owner's playtest of all of this (the shared systems of 13 October and the kernel); the
  second playtest's 30 findings (the wave plan in `ground-game-plan`, awaiting the owner's go); the rename from claim
  device to tool cupboard in the code; P6 and P7, to plan with the owner; the deferred questions below.
## State on 13 October 2026, evening (what the kernel was built on)

- `ground` holds everything below plus the owner's first playtest's fixes and the **shared systems** of
  [GROUND_SYSTEMS.md](GROUND_SYSTEMS.md) (approved by the owner): P0 foundations (targets, outcomes and `texts.json`,
  capabilities and viewer flags, reach and sight, support links, lifetimes), P5 the radial, P1 interaction (verbs in
  `interactions.json`, one targeting query, `InteractIntent`, hold-E radial, stuck arrows), P2 placement
  (`placement.json`, solvers and checks, one ghost, `PlaceIntent`, the Tin Finch from its item), P3 held items (parts,
  information rows, item actions, ammo icon and hold-R radial, drop anywhere, workbench speed), the damage pipeline
  with the Destroyed event, gibs, the loading screen, and building pieces fitted to their slots (no gaps or flicker
  between pieces: `art/import/building_fit.py`, `building_fit.json`). **218 tests pass; protocol 19.**
- **The repository is on GitHub:** `origin` is https://github.com/uruskan/Hollow-Universe; `main` shows the README with
  a pointer to `ground`. **The owner pushes, from GitHub Desktop; sessions only commit** (never push). Never merge into
  `main` without the owner.
- Another session works on monuments (G7c) in worktree `D:\GameDev\Projects\Hollow-wt\monuments`, branch
  `ground-monuments`, and fast-forwarded `ground` to it once (486716d). Merge with care; don't rewrite its files.
- **Left, in order:** the owner's playtest of all of this; the code identifiers' rename from claim device to tool
  cupboard (the visible name is done; deferred so the monuments session isn't disturbed); P6 (one model for every
  world thing) and P7 (characters and held items), to plan with the owner; the deferred questions below (the
  half-wall floor, the foundation height range); smaller leftovers in GROUND.md §11 (gibs don't collide with each
  other or walls, an open door breaks from its shut pose, the pistol's support hand, an outcrop's collision taller than
  its broken stages, own hits not predicted, the world's opening freezes 6 s behind the loading screen).

## Where things are

- **`main`** (`D:\GameDev\Projects\Hollow Universe`; until 14 October 2026 the `ground` branch, branched from
  `generator-v2`) holds the playable game. It covers:
  - G1 and G2 on the real first world;
  - G3 building;
  - G4 guns;
  - G5 raiding;
  - G6 Tin Finch flight, in the game (craft it at the vehicle workbench, board with E);
  - G7a (salvage, recycler, tech tree, research, teams, map), merged at the end of the first session, its UI wired in
    (map on G, tech tree from a workbench, recycler, research, teams);
  - effects, sound, characters and assets.

  163/163 tests passed then, at protocol version 11 (the F1 console). The whole ground game was merged into `main` on
  14 October 2026, at the owner's request.
- **Play it:** `play-ground.cmd` hosts a game; `play-ground.cmd join` adds a second player; `play-ground.cmd menu`
  opens the main menu. Controls and tour steps are in AGENTS.md.

## Branches

Everything is on `main` since 14 October 2026: every work package, the monuments, the kernel (K0–K4), the developer
panel, the galaxy map's web prototypes (`prototypes/map-ux`, waiting for the owner's choice), the space concept
(`docs/SPACE_CONCEPT.md`) and the scale proposal (`docs/SCALE.md`). New work goes on `main`, or on a short-lived branch
in its own worktree under `D:\GameDev\Projects\Hollow-wt\`, merged back into `main` when green.
**The owner's first playtest (13 October 2026) and where each finding stands:**
- Done and merged into `ground`: Left Alt free look; the hands on held items (the rock and 40 others); free-foundation height
  by aim; the AK and pistol recoil exactly RustMVP's; one damage pipeline with the Destroyed event; Rust's X on trees and
  hotspot on outcrops with hits that read; dropped items in bags.
- Running: gibs (`ground-gibs`); the bag's model remade as a strapped bundle (`ground-bagart`).
- By the shared systems the owner approved ([GROUND_SYSTEMS.md](GROUND_SYSTEMS.md)): P0 foundations (`ground-p0`, running)
  and P5 the radial (`ground-radial`, running), then P1 interaction (`ground-p1`, built 13 October 2026: hold-E radial,
  code lock's set code, garage door closing, the cupboard's menu, the rename to tool cupboard on screen, spent arrows that
  stay stuck and are taken with E, from living bodies too; GROUND_SYSTEMS.md §5, decisions I1–I17), P2 placement (deployables on foundations and floors, a ghost for every placeable, the Tin Finch from its
  item) and P3 held items (information for every item, the ammo icon and hold-R ammo radial, unload, the blueprint's
  LEARN, dropping over the interface, workbench speed).
- Later: P7, characters and held items (third-person grips and clipping, Rust's closed grip on the rock, swings whose
  contact meets the server's hit), to plan with the owner.
- To check once the shared systems are merged (the owner, 13 October 2026): building textures and the plan's ghost looked
  "super bright, broken" in one of their runs after the first merges (the sandbox rendered them normally afterwards: maybe
  a half-done import; look again in the game); and free foundations rise too little (today at most the foundation depth,
  1 m, RustMVP's number; more needs a taller foundation model so nothing floats).
- Deferred, to ask the owner: building stability. They wrote: "floors can hang out if there's a half wall or something
  that they are snapped to, and in a triangle foundation one wall and one half wall and a triangle floor on top shouldn't
  break when the half wall is broken since that triangle floor would be attached to the wall; however the half wall
  allows it to be built." Today a piece is linked only to pieces whose sockets it sits on exactly
  (`BuildingWorld.Link`, `Supports`), never to pieces it merely touches; a floor on a half wall sits at 1.5 m, against the
  full wall's face, while a full wall's floor socket is at its top (3 m). Ask whether the floor meant is at half-wall
  height against the wall (then touching would have to count as support) or at the full wall's top.

**First jobs for the next session:**
1. **Owner's playtest findings first.** The owner is testing the whole build and will report problems.
2. **Finish the wardrobe hookup:**
   - `Looks.ForPlayer(id, body)` for every remote player and the inventory preview;
   - `Stance = Sleep` for sleepers, and `Crawl` for the wounded;
   - `CharacterFrame.Aiming` for bows;
   - the new events: `HelpUp`, `Eat`, `Bandage`, `Inject`.

   `ItemLooks.Worn` already uses `Wardrobe.ForItem`.
3. **Night bloom:** some glowing models bloom too much at night (the weapon light, perhaps).
4. **Watch Jolt native crashes under low memory:** two test runs crashed inside Jolt while the PC was out of
   GPU and system memory (Elite Dangerous running); unconfirmed, so reproduce before trusting it.
5. **Server performance:** next is parallel snapshot writing, since replication takes about 3 ms a tick
   (GROUND.md §5.11).

## What's left after that

- **G7c:** monuments with loot, NPC guards and radiation; then the economy's props get real models.
- **G8:** more zones per planet (merge and split), the moon and other planets through supercruise (M5), and the
  orbit hook `Zone.OrbitTransfer`.
- **Polish:** stand-in models (the `standIn` flags in `models.json`), sound mixing by ear, effects that still look
  cheap (§9.3), the 40–60 ms ragdoll hitch, and Linux server testing.

## How the merges went (patterns that repeat)

- **Protocol and NetGen:** packages bump the protocol independently. On merge, take the highest + 1 and rerun
  `dotnet run --project tools/Hollow.NetGen` for `Messages.g.cs`.
- **Schema steps** must be chained, never duplicated.
- **GROUND.md §11:** each package has its own decision prefix (B, R, S, W, N, C, F, M, A, P) or a numbered run.
  Keep both sides.
- **Model catalogue:** new item keys need entries in `models.json` (`ModelCatalogueTests`). Use a marked
  `standIn: true` entry until a model exists.
- **Physics body-ID ranges** are carved per system (props, buildings, ships). Check for overlaps.
- **Merge order:** ask an agent to merge `ground` into its own branch before it finishes. Then the merge into
  `ground` is clean.
- **Concurrency:** 4–5 agents at once is fine on budget. RAM (16 GB) is the limit when several run Godot.

## Owner's standing decisions (summary)

- **Style:** Rust's mechanics, numbers, UI design and the AK recoil pattern are allowed. Files extracted from Rust
  are never used, not even the RustMVP character as a fallback.
- **The first world** is a real planet that must look good. Grass is a shader system, and vegetation follows
  hollow-orbit's look.
- **The Tin Finch** is the starter ship, built at the vehicle workbench.
- **Day length:** ×24 compressed is the working default (owner may veto).
- **The owner** works on other things. Don't ask them to test between milestones; work autonomously, and offer a
  playtest only at major milestones.
