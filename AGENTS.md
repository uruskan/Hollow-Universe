# Working on Hollow Universe (for AI agents and people)

Read first, in this order:

1. [docs/GAME_DESIGN.md](docs/GAME_DESIGN.md): what the game is.
2. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): how it's built. Its principles (§2) and determinism rules (§8)
   are not optional.
3. [docs/GALAXY.md](docs/GALAXY.md): the universe generator, if you touch `Hollow.Galaxy` or the maps.
4. [docs/GROUND.md](docs/GROUND.md): the Rust-style ground game, if you touch planet surfaces, the server, the
   network or anything players do on foot.

## Rules

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
dotnet run --project src/Hollow.Server -- --port 27015   # the dedicated server (--no-db, --db <path>, --lat, --lon; type help)
dotnet run -c Release --project tools/Hollow.Bots -- --server 127.0.0.1:27015 --count 100   # bots (--script, --lag/--jitter/--loss)
dotnet run -c Release --project tools/Hollow.Bots -- loadtest --count 100 --duration 60   # server + 100 bots: tick time, bandwidth, prediction
dotnet run -c Release --project tools/Hollow.Bots -- loadtest --count 100 --duration 60 --script survive --kit load-test-kit   # G2: bots that gather, craft and fight (GROUND.md §5.2)
dotnet run --project tests/Hollow.Tests -- ContentTests   # checks game/data/ground: references, ranges, every recipe makeable
dotnet run -c Release --project tools/Hollow.GeneratorReport -- bench   # µs per generated star system
dotnet run -c Release --project tools/Hollow.GeneratorReport -- stats   # what generated systems contain
dotnet run -c Release --project tools/Hollow.GeneratorReport -- worlds  # one example of each notable kind of world
dotnet run -c Release --project tools/Hollow.GeneratorReport -- maps    # galaxy-wide maps per level (~4 min)
dotnet run -c Release --project tools/Hollow.GeneratorReport -- surface map|relief|zoom|stats|bench|site|spot|times
                                                 # the first world's surface: maps, speed, landing site, look-dev
                                                 # spots (forest|meadow|beach|mountain), sun and moon by the hour
blender -b --factory-startup --python art/build.py   # every item and world model → game/assets/items, world (GROUND.md §9.1)
dotnet run --project tests/Hollow.Tests -- Building   # the building rules (GROUND.md §6.3)
# The building and door kits into game/assets/building (Blender 5.2.1, D:/GameDev/Tools/blender-5.2.1-windows-x64):
blender --background --factory-startup --python art/import/building_kit.py [-- twig wood stone metal armoured]
blender --background --factory-startup --python art/import/door_kit.py
```

Character assets (GROUND.md §4.8, §9), rebuilt from text by Blender 5.2 in background mode. MPFB 2 lives in its own
Blender user folder, so set `BLENDER_USER_RESOURCES` first (on this PC `D:\GameDev\Toolslender-hollow-config`):

```bash
blender -b --factory-startup --python art/characters/build_body.py        # body → game/assets/characters
blender -b --factory-startup --python art/characters/build_clothing.py    # garments (needs the body's cache)
blender -b --factory-startup --python art/characters/build_animations.py  # retargeted clips (needs the body's cache)
blender -b --factory-startup --disable-autoexec --python art/items/build_items.py   # held items → game/assets/items
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
xvfb-run -a $GODOT --path game --resolution 1600x900 -- --view ground --name Ada --host --shot ground.png
xvfb-run -a $GODOT --path game --resolution 1600x900 -- --view ui --screen inventory --shot inventory.png
xvfb-run -a $GODOT --path game --resolution 1600x900 -- --view ground --name Ada --host --db /tmp/demo.db --demo --shot camp.png
xvfb-run -a $GODOT --path game -- --view icons --out game/assets/items/icons   # render every item icon (GROUND.md §9.1)
xvfb-run -a $GODOT --path game --resolution 1600x900 -- --view models --shot models.png
xvfb-run -a $GODOT --path game --resolution 1600x900 -- --view build --scene house --shot house.png
```

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
- Character viewer: `--view character`; `--outfit <0–4>`, `--item <flint_axe|flint_pick|hand_stone|wren_pistol|kestrel_rifle>`,
  `--motion <idle|walk|jog|sprint|crouch|crouch_walk|strafe_left|strafe_right|back|jump|fall|swim_idle|swim>`,
  `--aim <deg>` (view pitch), `--event <swing|fire|reload|hit|death>` with `--event-at <s>`, `--fp` (first person),
  `--ads`, `--slope` (stand on the ramp), `--face <deg>` (character yaw), `--lod <0–2>`, camera `--yaw`, `--pitch`,
  `--distance`, `--height`; `--debug` prints the character's meshes and hand IK once;
  `--crowd <n>` adds n characters and `--measure <s>` prints frame times (vsync off) and quits.
- Run: `--wait <s>`, `--trace` (prints streaming, exposure and timing once a second).
- Ground (GROUND.md §4.3): `--view ground --name <n>`, then `--connect host:port` or `--host [--port <n>]` (the
  default: a server in the game); `--lag <ms>`, `--jitter <ms>`, `--loss <percent>` (bad network through a local
  relay), `--netgraph` (open the network graph), `--face <name>` (turn toward a player once seen, for screenshots).
  Survival (G2): `--db <path>` (the hosted server's database; the default is `user://hollow-host.db`), `--demo` (host
  only: a lit campfire, a box and a sleeping bag in view, for screenshots). Keys: 1–6 belt, left mouse use, right
  mouse aim, E interact, F place, G drop, H eat or bandage, Tab close loot, B respawn on the beach (dead). Chat lines
  starting with `/` are test commands: `/craft <item> [n]`, `/cancel`, `/put`, `/take`, `/belt`, `/wear`, `/light`,
  and when hosting `/give <item> [n]`, `/kit <loot table>`.
- Server (G2): `--kit <loot table>` gives every new character a loot table's items; admin commands `give`, `kit`,
  `heal`, `kill` besides G1's (type `help`).
- Checks: `--physics-check` runs the physics check inside the client and quits; its line must match the one
  `dotnet run --project tests/Hollow.Tests -- MovementCheck` prints.
- Build sandbox (GROUND.md §6.3): `--view build`, local, on the first world's stand-in surface. `--scene
  house|ghost-valid|ghost-invalid|stability|stress|empty` (a built base; `stress` measures 300 frames and prints the
  frame time), `--overlay` (stability colours), `--tool 1–6` (plan, hammer, three doors, claim device), `--piece <key>`,
  `--wheel plan|hammer` (open the wheel), `--from x,y,z --at x,y,z` (the camera in the scene's frame), `--finest` or
  `--lod n` (draw one level of detail). Keys: right mouse wheels, left places or repairs, R turns, E doors, F2 the
  stability overlay, F5 an hour of upkeep and decay. `--view build --scene empty --build-check` plays a round of
  building through the sandbox's input (place, snap, upgrade, turn, door, demolish and collapse) and quits with exit
  code 0 when every step passes.
- Ground interface viewer (GROUND.md §8): `--view ui --screen <screen>` shows the RustMVP interface on sample data
  (`hud`, `inventory`, `loot`, `corpse`, `furnace`, `campfire`, `crafting`, `death`, `wheel`, `hammer`, `menu`);
  `--pointer x,y` holds the pointer at a point of the 1600 × 900 design canvas. Without `--shot` it's interactive.
  `--view ui --ui-check` drives the inventory and crafting with real input events (drag, middle-drag, split, quick
  move, drop, craft) and quits with exit code 0 when every check passes.
- Models (GROUND.md §9.1): `--view models` stands every catalogue model on the ground (`--lineup items|world`,
  `--pitch <deg>`); `--model <key>` shows one on a turntable (`--stage <n>`, `--lod <n>`, `--yaw <deg>` holds it at an
  angle, `--zoom <f>` moves the camera, `--anim <clip>` plays a clip such as the Tin Finch's `Hatch_Open`, `--attach a,b`
  mounts attachments on a gun, `--upright` shows a held item as held, `--first-person` shows a held item at its
  first-person pose; keys Tab, ←/→, ↑/↓, 0/1/2, A, Space, F, H).
  `--view icons --out <dir>` renders every item icon at 128 and 256 px (`--only a,b` for some) and then shows the
  contact sheet, which `--view icons` alone shows too (`--only` limits it). `--placeholder-icons` keeps the interface's vector drawings.

## Layout

| Path | What |
|---|---|
| `src/Hollow.Core` | Vectors in double precision, `DMath`, `Hash64`, `Rng`, units |
| `src/Hollow.Galaxy` | Density model, addresses, sectors, cells, catalogue, names, star systems |
| `src/Hollow.Surface` | Planet surfaces (GROUND.md §4.6): `ISurface`, `ZoneFrame`, `PlanetSurface` (plates, climate, biomes), cube-sphere tiles, collision, scatter, `SkyEphemeris`, the stand-in `SphereSurface`, `FirstWorld` |
| `src/Hollow.Simulation` | Rules and simulation of the ground game: entity store, zones, Jolt physics world and ground tiles, character movement (GROUND.md §4.5); the survival game (§6.1): game data, items and crafting, natural objects and gathering, vitals, deployables, combat with lag compensation; building in `Building/` (structures, sockets, placement, stability, hammer, doors and locks, the claim device, upkeep and decay; GROUND.md §6.3) |
| `src/Hollow.Net` | Protocol (GROUND.md §4.4): message definitions and their generated serializers, bit packing, snapshot delta coding, relevance grid, bad-network relay |
| `src/Hollow.Persistence` | SQLite storage (GROUND.md §4.7): accounts and characters, versioned schema, one worker thread |
| `src/Hollow.Client` | The client without graphics (GROUND.md §4.3): connection, clock sync, prediction and reconciliation, interpolation, statistics; `LocalPlayer`, the survival API the UI binds to (§6.2) |
| `src/Hollow.Server` | The dedicated server (GROUND.md §4.2): console program and library (Host mode), zone, replication, login, admin |
| `game/` | Godot project: galaxy map, system map, ground mode (`scripts/Ground`), UI, shaders |
| `game/scripts/UI` | The ground game's interface (GROUND.md §8): `GroundUi` (HUD, inventory, loot, crafting, death, wheels, menu), the view model `Model/IGroundUi` with `MockGroundUi`, `ItemIcons`, and the viewer (`Viewer/`) |
| `game/assets/building` | The building and door kits as GLB (three levels of detail, Godot collision names) and their 2K textures (GROUND.md §6.3) |
| `game/scripts/Build` | The build sandbox (`--view build`): batched rendering of structures, the plan's ghost, doors, the HUD lines and the wheels' view model |
| `game/data/ground` | The ground game's numbers; `building.json`: pieces, grades, sockets, costs, doors, locks, the claim device |
| `art/import` | Blender scripts that convert the PSG kits into `game/assets/` |
| `game/assets/fonts` | Inter (SIL OFL 1.1), the interface font; Barlow and Barlow Condensed (SIL OFL 1.1), the ground interface's |
| `game/scripts/Characters` | Player characters (GROUND.md §4.8): `CharacterView` (third person), `ViewmodelView` (first person), animation graph, IK and aim, ragdoll, wardrobe, held items, the `--view character` viewer |
| `game/assets/characters` | Player body, clothing and animations (GLB), their textures and JSON facts, built by `art/` |
| `game/assets/THIRD_PARTY.md` | Every third-party file: source, version, licence, checksum |
| `art/` | Blender Python scripts that build assets from text: the character body, clothing and held-item tests (character package), and the item and world models: `hollow/` (shared code), `import/` (the kits), `models/` (our own), `build.py`; `art/.cache` is scratch (not committed) |
| `game/data/ground` | The ground game's data (GROUND.md §7): items, recipes, natural objects, deployables, loot, survival constants, crafting tabs (JSON, read by server and client) |
| `game/scripts/Surface` | The surface view: terrain LOD, water, sky and air, lights, clouds, plants (`Vegetation`), shader grass (`GrassField`), look-dev camera, time controls |
| `game/scripts/Surface/Flora` | Plants made in code after hollow-orbit's flora: painted atlas and bark (`FloraTextures`, `Painter`), geometry (`FloraGeometry`), species and variants (`FloraLibrary`), impostors baked at start (`ImpostorBaker`) |
| `game/assets/surface/ground` | The eight ground texture layers (Megascans and Poly Haven, `game/assets/THIRD_PARTY.md`), built by `tools/blender/surface_ground.py` |
| `game/scripts/Items` | Item and world models (GROUND.md §9.1): `ModelCatalog` (reads `data/ground/models.json`), `CatalogItemIcons`, the icon renderer (`--view icons`) and the model viewer (`--view models`) |
| `game/assets/items`, `game/assets/world` | Item and world models (GLB with LODs, hulls and sockets) and the item icons (`items/icons/128`, `items/icons/256`), built by `art/` |
| `game/assets/materials`, `game/assets/textures` | The shared Godot materials the models' `.import` files map to, and their textures (2K at most) |
| `tests/Hollow.Tests` | Test runner (no NuGet packages needed) |
| `tools/Hollow.CatalogBuilder` | Converts the HYG star catalogue (or AT-HYG) plus a hand-entered supplement (`supplement.csv`) into `game/data/catalog/hyg41.bin.gz` |
| `tools/Hollow.GeneratorReport` | Measures the generator: speed, contents of generated systems |
| `tools/Hollow.NetGen` | Writes the network serializers (`Messages.g.cs`) from `Messages.cs` |
| `tools/Hollow.Bots` | Headless clients with scripted movement, and the load test (GROUND.md §5.1) |
| `third_party/` | Vendored NuGet packages (LiteNetLib, JoltPhysicsSharp, Microsoft.Data.Sqlite), so the build needs no network; see its README |
