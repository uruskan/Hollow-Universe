# Ground game: handoff to the next session

Written 13 October 2026, morning, when the first session filled its context; the kernel of 14 October added on top. Read this first, then
[GROUND.md](GROUND.md) (design and everything as built) and [progress-ground.md](progress-ground.md) (what happened).
Memory files `ground-game-plan`, `earlier-rust-attempts-and-assets`, `grass-is-a-shader-system` and
`minimal-automated-testing` hold the owner's standing decisions.

## 14 October 2026, night: one place (read this first)

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
