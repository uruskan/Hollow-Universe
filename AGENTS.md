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
  - Icons come from `UiIcons`. Labels drawn over a map go through `Labeller`, so they never overlap each other or
    the panels.
  - Never turn on a stretch mode or MSDF for UI text, and never hard-code pixel sizes.
- **Docs first.** A change to architecture or design updates the matching doc in the same commit.
- **Every change builds and passes the tests before it's handed over.** Anything visual gets a screenshot.

## Commands

```bash
dotnet build HollowUniverse.sln                  # everything, including the Godot client's C#
dotnet run --project tests/Hollow.Tests          # all engine-free tests
dotnet run --project tests/Hollow.Tests -- Galaxy   # only tests whose name contains "Galaxy"
dotnet run -c Release --project tools/Hollow.GeneratorReport -- bench   # µs per generated star system
dotnet run -c Release --project tools/Hollow.GeneratorReport -- stats   # what generated systems contain
dotnet run -c Release --project tools/Hollow.GeneratorReport -- worlds  # one example of each notable kind of world
dotnet run -c Release --project tools/Hollow.GeneratorReport -- maps    # galaxy-wide maps per level (~4 min)
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
```

On Windows or macOS, drop `xvfb-run -a` and run the Godot .NET executable directly (on Windows,
`Godot_v4.7.2-stable_mono_win64.exe`); the options are the same.

Client options (after `--`):
- Camera and selection: `--focus <name>`, `--position=x,y,z` (ly), `--distance <ly>`, `--yaw <deg>`,
  `--pitch <deg>`, `--select <name>`, `--search <text>`.
- System map: `--view system`, `--body <name>`, `--time <UTC>`, `--system-distance`.
- Interface: `--ui-scale <factor>` (preference for this run), `--rescale <factor>` (change it after half a second,
  to test live rescaling).
- Character viewer: `--view character`; `--outfit <0–4>`, `--item <flint_axe|flint_pick|hand_stone|wren_pistol|kestrel_rifle>`,
  `--motion <idle|walk|jog|sprint|crouch|crouch_walk|strafe_left|strafe_right|back|jump|fall|swim_idle|swim>`,
  `--aim <deg>` (view pitch), `--event <swing|fire|reload|hit|death>` with `--event-at <s>`, `--fp` (first person),
  `--ads`, `--slope` (stand on the ramp), `--face <deg>` (character yaw), `--lod <0–2>`, camera `--yaw`, `--pitch`,
  `--distance`, `--height`; `--debug` prints the character's meshes and hand IK once;
  `--crowd <n>` adds n characters and `--measure <s>` prints frame times (vsync off) and quits.
- Run: `--wait <s>`, `--trace` (prints streaming, exposure and timing once a second).

## Layout

| Path | What |
|---|---|
| `src/Hollow.Core` | Vectors in double precision, `DMath`, `Hash64`, `Rng`, units |
| `src/Hollow.Galaxy` | Density model, addresses, sectors, cells, catalogue, names, star systems |
| `src/Hollow.Surface` | Planet surfaces (GROUND.md §4.6): `ISurface`, `ZoneFrame`, the stand-in `SphereSurface`, `FirstWorld` |
| `game/` | Godot project: galaxy map, system map, UI, shaders |
| `game/scripts/Characters` | Player characters (GROUND.md §4.8): `CharacterView` (third person), `ViewmodelView` (first person), animation graph, IK and aim, ragdoll, wardrobe, held items, the `--view character` viewer |
| `game/assets/characters` | Player body, clothing and animations (GLB), their textures and JSON facts, built by `art/` |
| `game/assets/items` | Held items (GLB with grip and sight sockets), built by `art/items` |
| `game/assets/THIRD_PARTY.md` | Every third-party file: source, version, licence, checksum |
| `art/` | Blender Python scripts that build the character and item assets from text; `art/.cache` is scratch (not committed) |
| `game/assets/fonts` | Inter (SIL OFL 1.1), the interface font |
| `tests/Hollow.Tests` | Test runner (no NuGet packages needed) |
| `tools/Hollow.CatalogBuilder` | Converts the HYG star catalogue (or AT-HYG) plus a hand-entered supplement (`supplement.csv`) into `game/data/catalog/hyg41.bin.gz` |
| `tools/Hollow.GeneratorReport` | Measures the generator: speed, contents of generated systems |
