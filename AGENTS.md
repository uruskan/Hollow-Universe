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
dotnet run --project tests/Hollow.Tests -- MovementCheck   # the physics check (GROUND.md §4.5)
dotnet run -c Release --project tools/Hollow.GeneratorReport -- bench   # µs per generated star system
dotnet run -c Release --project tools/Hollow.GeneratorReport -- stats   # what generated systems contain
dotnet run -c Release --project tools/Hollow.GeneratorReport -- worlds  # one example of each notable kind of world
dotnet run -c Release --project tools/Hollow.GeneratorReport -- maps    # galaxy-wide maps per level (~4 min)
```

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
- Run: `--wait <s>`, `--trace` (prints streaming, exposure and timing once a second).
- Checks: `--physics-check` runs the physics check inside the client and quits; its line must match the one
  `dotnet run --project tests/Hollow.Tests -- MovementCheck` prints.

## Layout

| Path | What |
|---|---|
| `src/Hollow.Core` | Vectors in double precision, `DMath`, `Hash64`, `Rng`, units |
| `src/Hollow.Galaxy` | Density model, addresses, sectors, cells, catalogue, names, star systems |
| `src/Hollow.Surface` | Planet surfaces (GROUND.md §4.6): `ISurface`, `ZoneFrame`, the stand-in `SphereSurface`, `FirstWorld` |
| `src/Hollow.Simulation` | Rules and simulation of the ground game: entity store, zones, Jolt physics world and ground tiles, character movement (GROUND.md §4.5) |
| `game/` | Godot project: galaxy map, system map, UI, shaders |
| `game/assets/fonts` | Inter (SIL OFL 1.1), the interface font |
| `tests/Hollow.Tests` | Test runner (no NuGet packages needed) |
| `tools/Hollow.CatalogBuilder` | Converts the HYG star catalogue (or AT-HYG) plus a hand-entered supplement (`supplement.csv`) into `game/data/catalog/hyg41.bin.gz` |
| `tools/Hollow.GeneratorReport` | Measures the generator: speed, contents of generated systems |
| `third_party/` | Vendored NuGet packages (LiteNetLib, JoltPhysicsSharp, Microsoft.Data.Sqlite), so the build needs no network; see its README |
