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
  - G6 Tin Finch flight, which works in the `--view ship` sandbox but isn't yet in the ground mode;
  - G7a (salvage, recycler, tech tree, research, teams, map), merged at the end of the first session; its UI isn't
    wired into the ground mode yet (§6.8);
  - effects, sound, characters and assets.

  145/145 tests pass, and the protocol is version 9. Nothing has ever been pushed, and nothing merged into `main`
  (that needs the owner).
- **Play it:** `play-ground.cmd` hosts a game; `play-ground.cmd join` adds a second player; `play-ground.cmd menu`
  opens the main menu. Controls and tour steps are in AGENTS.md.

## Unfinished branches (each in `D:\GameDev\Projects\Hollow-wt\<branch>`)

Their agents ran in the first session. Whatever they committed is safe; check `git log ground..<branch>`.

| Branch | State when the session ended | Next step |
|---|---|---|
| `ground-world` (G7a) | **Merged into `ground`** (protocol 9, schema 6). | Nothing. Integration wires its UI (§6.8), and its new item keys need real models (stand-ins or missing: see §6.8). |
| `ground-integrate` (pass 4) | Just started: Tin Finch in the ground mode (§6.7 hook-up list), then the G7a UI (§6.8). | Redo pass 4 from the hook-up lists. |
| `ground-perf` | Just started: server cost of flying ships streaming ground, broadphase rebuilds, respawn hitches. | Profile and fix (targets ≤ 10 ms average and ≤ 33 ms 99th percentile with 100 bots + 20 flying), then GROUND.md §5.10 (§5.9 is G7a's load test). |
| `ground-wardrobe` | 3 commits ahead: sleeping, wounded, help-up and bow animations, probably more. | Finish: hood, gloves and metal mask worn; per-player looks; then merge. Integration then wires sleepers, crawl and aim pose. |
| `ground-animals` (G7b) | 4 commits ahead: animals with AI, harvesting, cooking. | Finish and merge; integration then wires `AnimalView`. |

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
