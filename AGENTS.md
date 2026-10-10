# Working on Hollow Universe (for AI agents and people)

Read first, in this order:

1. [docs/GAME_DESIGN.md](docs/GAME_DESIGN.md): what the game is.
2. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): how it's built. Its principles (§2) and determinism rules (§8)
   are not optional.
3. [docs/GALAXY.md](docs/GALAXY.md): the universe generator, if you touch `Hollow.Galaxy` or the maps.

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
- **Original content only.** No names, assets, data or UI copied from Rust, Elite Dangerous, No Man's Sky or
  any other game.
- **Docs first.** A change to architecture or design updates the matching doc in the same commit.
- **Every change builds and passes the tests before it's handed over.** Anything visual gets a screenshot.

## Commands

```bash
dotnet build HollowUniverse.sln                  # everything, including the Godot client's C#
dotnet run --project tests/Hollow.Tests          # all engine-free tests
dotnet run --project tests/Hollow.Tests -- Galaxy   # only tests whose name contains "Galaxy"
```

Headless Godot (Linux workspace; `$GODOT` is the Godot 4.7.2 .NET binary; build the solution first):

```bash
$GODOT --headless --path game --import       # first run after a clone
# Render a frame once streaming has finished, save it and quit:
xvfb-run -a $GODOT --path game --resolution 1600x900 -- --shot sol.png
xvfb-run -a $GODOT --path game --resolution 1600x900 -- --position=-26,-21,26673 --distance 1100 --shot core.png
xvfb-run -a $GODOT --path game --resolution 1600x900 -- --view system --focus "Alpha Centauri" --shot acen.png
```

Client options (after `--`): `--focus <name>`, `--position=x,y,z` (ly), `--distance <ly>`, `--yaw <deg>`,
`--pitch <deg>`, `--select <name>`, `--view system`, `--body <name>`, `--time <UTC>`, `--system-distance`,
`--wait <s>`, `--trace` (prints streaming and exposure state once a second).

## Layout

| Path | What |
|---|---|
| `src/Hollow.Core` | Vectors in double precision, `DMath`, `Hash64`, `Rng`, units |
| `src/Hollow.Galaxy` | Density model, addresses, sectors, cells, catalogue, names, star systems |
| `game/` | Godot project: galaxy map, system map, UI, shaders |
| `tests/Hollow.Tests` | Test runner (no NuGet packages needed) |
| `tools/Hollow.CatalogBuilder` | Converts the HYG star catalogue into `game/data/catalog/hyg41.bin.gz` |
