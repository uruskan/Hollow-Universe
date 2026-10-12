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
