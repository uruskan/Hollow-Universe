# Generator v2: progress notes

Branch `generator-v2`. Implements recommendations 1–9 of the report "Elite galaxy generation vs Hollow"
(`reports/Elite galaxy generation vs Hollow.md` in the main checkout), in the order 1, 3, 4, 2, 5, 7, 6, 8, 9.
Each item is one commit (or a few) that builds and passes the tests. Elite is a reference for how things work;
no Elite tables, names, thresholds or data are used.

Measurements come from `tools/Hollow.GeneratorReport` (Release build, single thread, this PC):

```bash
dotnet run -c Release --project tools/Hollow.GeneratorReport -- bench   # µs per generated system
dotnet run -c Release --project tools/Hollow.GeneratorReport -- stats   # what the systems contain
```

`bench` times `SystemGenerator.Generate` over a fixed sample of 36,110 procedural systems (near Sol and in the
inner disk) and `Galaxy.GetSystem` over the 1,371 real systems within 60 ly. `stats` describes 42,018 procedural
systems.

| | Before (5581c88) |
|---|---|
| Procedural system | 5.9 µs (8.4 bodies, 0.71 µs per body) |
| Real system (GetSystem) | 4.4 µs |

---

## 1. Hot worlds reachable

**Changed.** The planet zone's inner edge moved from `max(0.02 AU, 0.1 AU·√L, 3 R★)` to
`max(0.01 AU, 0.03 AU·√L, 3 R★)`: equilibrium temperatures now reach about 1,600 K (before: at most 881 K, so the
generator's metal-rich and hot-giant branches were dead code). A first try that let planets fill the new inner
slots as often as any other pushed the median planet from 262 K to 353 K, far hotter than real planet statistics.
So slot occupancy is now a weighted random subset: slots beyond `0.1 AU·√L` (about ten-day orbits around the Sun)
weigh 1, closer slots `(a / 0.1 AU·√L)³`, following the steep fall of real occurrence rates at short periods.

**Golden.** Systems golden `0xD88E791D58901CB4` → `0x313E81E347F46464` (every system's slots changed). Cells
golden unchanged.

**Measured** (`stats`, 42,018 systems):

| | Before | After |
|---|---|---|
| Median planet temperature | 262 K | 287 K |
| 99th percentile | 1,001 K | 1,451 K |
| Hottest planet | 1,321 K (rocky world, thick atmosphere) | 2,404 K (metal-rich world) |
| Metal-rich worlds | 0 | 2,914 (2.5% of planets) |
| Gas giants above 1,000 K | 0 | 370 (0.9% of systems; real: 0.5–1% of Sun-like stars) |
| Speed | 5.9 µs per system | 5.4 µs (slightly fewer moons) |

The test `GeneratedSystemsArePlausible` now also requires metal-rich worlds and hot giants in its sample.

**Decisions.** The absolute floor went from 0.02 to 0.01 AU so compact M-dwarf systems (like TRAPPIST-1, whose
innermost planet orbits at 0.0115 AU) can exist. Neutron stars and black holes keep their 0.1 AU inner edge.
The classification and greenhouse rules are unchanged here; item 6 replaces them with physical state.

---

## 3. Per-stage versions, wider goldens, GALAXY.md corrections

**Changed.**
- `GeneratorVersion` (new, `Hollow.Galaxy`): `Galaxy = 1`, `System = 1`, `Surface = 1` (reserved for M4). Each
  stage's seed is `Hash64(universe seed, stage tag, version)` (`GALV`, `SYSV`, `SURV`); `CellGenerator` and
  `SystemGenerator` derive their seed from it. The old `Galaxy.GeneratorVersion = 1`, which nothing read, is gone.
- Goldens: `Golden.Of` (tests) hashes every stored field of an object by reflection, recursively and in name
  order (auto-property backing fields included, computed properties skipped), so fields added in items 5–7 are
  pinned automatically. The procedural systems golden uses it; a new golden covers Sol and 16 real systems
  (Alpha Centauri, Proxima Centauri, Barnard's Star, Sirius, Procyon, Tau Ceti, Epsilon Eridani, 61 Cygni, Vega,
  Capella, Castor, Mizar, Albireo, Polaris, Betelgeuse, Rigel). A new test checks that another galaxy or system
  version changes its stage.
- GALAXY.md: sector and system name examples that the tables can't produce ("Vasken Orrimar", cell code "QX-AB"
  at level 6) replaced with real ones printed by `tools/Hollow.GeneratorReport names`; body designations
  corrected (only the primary has planets, so "B 3 a" can't exist yet; the one belt is `<system> Belt`, not
  "Belt A"); multiplicity by primary mass (the code's thresholds, plus 20% for neutron stars, black holes and
  Wolf–Rayet stars); moons and rings per type as the code does them; the catalogue's real size; a new §14 on
  versions. ARCHITECTURE.md §8.4 names the per-stage versions.

**Golden.** Everything changes because every seed changed: cells `0x9EB9D62229A1F6AC` → `0x595FEAC36CFD361D`;
procedural systems `0x313E81E347F46464` → `0xDEEBD959A57739FB` (now over every field); real systems (new)
`0xD0DADDB204F53B3B`.

**Measured.** No change in speed (the bench varies ±20% between runs on this PC; best of several: 6.4 µs per
procedural system, 4.4 µs per real one).

**Decisions.** Versions stay at 1 during development (ARCHITECTURE.md §8.4: goldens change instead). The
universe seed string "Hollow Universe galaxy, version 1" is kept: it names the main universe and is separate
from the stage versions. Golden hashes skip computed properties so a computed NaN can never make the hash differ
between x64 and ARM64.

---

## 4. A sampler that can't leave the density; galaxy-wide maps

**Maps first.** `tools/Hollow.GeneratorReport maps` renders every level over the whole sector grid (levels 0–2
in full, 3–7 one cell column per 256-ly pixel; about 4 minutes on 12 threads): `docs/images/generator/face-on.png`
(surface density and class mix), `ratio.png` (generated ÷ a fine integral of the density, per 2,048-ly block) and
`edge-on.png` (levels 0–3 through Sol and the Galactic Centre). Looked at before and after the change:

- No density steps between cells or sectors at any level: χ² per block of generated counts against the fine
  integral is 1.00–1.05 everywhere (1 = Poisson noise). (A first measurement gave 1.9–2.7 at levels 3–4; that was
  the map's own sampling comparing one cell column against the whole pixel, fixed by integrating the same
  footprint.)
- One real pattern: at level 0, paired red/blue arcs where the spiral arms fade out (R ≈ 50,000 ly). Counting
  showed the 16-try fallback hit 3,030 of 1.38 million level-0 stars (0.2%) and 76,173 of 78 million at level 1,
  bunched in cells where the arm is a narrow peak over a faint background (acceptance ~13%, so ~10% of those
  stars fell back to a uniform spot in a 1,024-ly cell, off the arm).
- Expected features, not artefacts: the catalogue's holes around Sol (item 2), the hole in the young disk inside
  the bar, the old bar in red giants at level 1.

**Changed.** The density samples of the cell's integration grid (4 × 4 × 4, or 4 × 8 × 4 at levels 0–2) are kept;
a star's position now picks a sub-cell by its component's sampled density (binary search over running sums), then
rejection-samples inside the sub-cell with a bound of 1.3 × the largest sample of the sub-cell and its 26
neighbours, at most 24 tries. A candidate is always inside a sub-cell where the component has stars, so even a
star that runs out of tries stays within the density. Measured: fallbacks 0 at level 0 (was 3,030), 57 at level 1
(was 76,173); the arcs are gone. New test `SamplerFollowsTheDensity`: young-disk stars in 36 level-0 cells follow
the `sech²` profile (5,498 within 165 ly of the midplane; the density says 5,498, a uniform spread 4,696).

**Golden.** Cells `0x595FEAC36CFD361D` → `0x22954A90D19DDA8E` (every star's random draws changed); procedural
systems `0xDEEBD959A57739FB` → `0x5F09B25668332B31` (their primaries changed). Real systems unchanged.

**Measured.** Cell generation cost unchanged within noise (the maps took 218 s before and 239 s after, dominated by
level 2's 3 × 10⁸ stars).
