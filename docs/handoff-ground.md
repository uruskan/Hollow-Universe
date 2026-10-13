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

  158/158 tests pass, and the protocol is version 10. Nothing has ever been pushed, and nothing merged into `main`
  (that needs the owner).
- **Play it:** `play-ground.cmd` hosts a game; `play-ground.cmd join` adds a second player; `play-ground.cmd menu`
  opens the main menu. Controls and tour steps are in AGENTS.md.

## Branches

Every work package is merged into `ground` (the first session ended with nothing pending):
- `ground-net`, `-planet`, `-character`, `-survive`, `-ui`, `-assets`, `-build`, `-fight`, `-audio`, `-raid`,
  `-fx`, `-ship`, `-world`, `-animals`, `-wardrobe` and `-perf`;
- `ground-integrate`, passes 1–4.

Their worktrees in `D:\GameDev\Projects\Hollow-wt\` can be removed once the owner is happy (`git worktree remove`).

**First jobs for the next session:**
1. **Owner's playtest findings first.** The owner is testing the whole build and will report problems.
2. **Finish the wardrobe hookup:**
   - `Looks.ForPlayer(id, body)` for every remote player and the inventory preview;
   - `Stance = Sleep` for sleepers, and `Crawl` for the wounded;
   - `CharacterFrame.Aiming` for bows;
   - the new events: `HelpUp`, `Eat`, `Bandage`, `Inject`.

   `ItemLooks.Worn` already uses `Wardrobe.ForItem`.
3. **Night bloom:** some glowing models bloom too much at night (the weapon light, perhaps).
4. **Server performance:** next is parallel snapshot writing, since replication takes about 3 ms a tick
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
