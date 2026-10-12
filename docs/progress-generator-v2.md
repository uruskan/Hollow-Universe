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
