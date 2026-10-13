# Ground game: handoff to the next session

Written 13 October 2026, morning, when the first session's context filled up. Read this first, then
[GROUND.md](GROUND.md) (design and everything as built) and [progress-ground.md](progress-ground.md) (what happened).
Memory files `ground-game-plan`, `earlier-rust-attempts-and-assets`, `grass-is-a-shader-system` and
`minimal-automated-testing` hold the owner's standing decisions.

## Where things are

- **`ground`** (worktree `D:\GameDev\Projects\Hollow-wt\ground`, branched from `generator-v2`) holds the playable
  game. It covers:
  - G1 and G2 on the real first world;
  - G3 building;
  - G4 guns;
  - G5 raiding;
  - G6 Tin Finch flight, in the game (craft it at the vehicle workbench, board with E);
  - G7a (salvage, recycler, tech tree, research, teams, map), merged at the end of the first session, its UI wired in
    (map on G, tech tree from a workbench, recycler, research, teams);
  - effects, sound, characters and assets.

  163/163 tests pass, and the protocol is version 11 (the F1 console is in). Nothing has ever been pushed, and nothing merged into `main`
  (that needs the owner).
- **Play it:** `play-ground.cmd` hosts a game; `play-ground.cmd join` adds a second player; `play-ground.cmd menu`
  opens the main menu. Controls and tour steps are in AGENTS.md.

## Branches

Every work package is merged into `ground` (the first session ended with nothing pending):
- `ground-net`, `-planet`, `-character`, `-survive`, `-ui`, `-assets`, `-build`, `-fight`, `-audio`, `-raid`,
  `-fx`, `-ship`, `-world`, `-animals`, `-wardrobe` and `-perf`;
- `ground-integrate`, passes 1–4.

Their worktrees in `D:\GameDev\Projects\Hollow-wt\` can be removed once the owner is happy (`git worktree remove`).

**The owner's first playtest (13 October 2026) and where each finding stands:**
- Done and merged into `ground`: Left Alt free look; the hands on held items (the rock and 40 others); free-foundation height
  by aim; the AK and pistol recoil exactly RustMVP's; one damage pipeline with the Destroyed event; Rust's X on trees and
  hotspot on outcrops with hits that read; dropped items in bags.
- Running: gibs (`ground-gibs`); the bag's model remade as a strapped bundle (`ground-bagart`).
- By the shared systems the owner approved ([GROUND_SYSTEMS.md](GROUND_SYSTEMS.md)): P0 foundations (`ground-p0`, running)
  and P5 the radial (`ground-radial`, running), then P1 interaction (hold-E radial, code lock's set code, garage door
  closing, the cupboard's menu, the rename to tool cupboard, spent arrows that stay stuck and are taken with E, from living
  bodies too), P2 placement (deployables on foundations and floors, a ghost for every placeable, the Tin Finch from its
  item) and P3 held items (information for every item, the ammo icon and hold-R ammo radial, unload, the blueprint's
  LEARN, dropping over the interface, workbench speed).
- Later: P7, characters and held items (third-person grips and clipping, Rust's closed grip on the rock, swings whose
  contact meets the server's hit), to plan with the owner.
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
