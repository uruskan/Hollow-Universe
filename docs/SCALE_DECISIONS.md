# Scale decisions, 14 October 2026 (handoff for the next session)

The owner made two scale decisions on 14 October 2026 and asked for a new session to carry them out, after
consolidating the repository. This file records exactly what he decided, what is still open, and what it touches.
**Architecture first:** turn this into a design proposal and get his approval before building anything big.

---

## 1. What the owner decided

1. **Planets are far too big.** Target size: about an Astroneer planet, or a world the size of a 4,200 m Rust map.
2. **The 1:1 galaxy isn't needed.** Thousands of dots on every view confuse and overstimulate; it kills the feeling.
   Keep the whole real galaxy (the real Milky Way's shape), but bring the number of stars down drastically, for
   example a few stars at each landmark: named nebulae, clusters and other notable places.
3. **Consolidate before more development**: one line of work, fewer copies (status in §4).

## 2. Settled vs open

Settled: much smaller planets; far fewer stars; the real galaxy's shape stays.

Open, ask him before deciding (see the "agreement is not every detail" rule: confirm specifics separately):
- **Exact planet size.** A sphere with the area of a 4.2 km × 4.2 km Rust map has a radius of about 1.2 km
  (circumference about 7.4 km); Astroneer-style worlds are of that order. Confirm which he means.
- **How many systems in total**, and **which landmarks**. Candidates already in the generator's authored layer:
  39 real clusters and nebulae, famous exoplanet systems (TRAPPIST-1, Proxima, 51 Pegasi, Kepler-186…), bright
  named stars near Sol.
- **3D or a plane.** He asked whether star systems share the same height. Today none do: every star has its own 3D
  position, and the disc is thin overall (most stars within a few hundred light-years of the mid-plane) but locally
  heights differ by up to hundreds of light-years. With few systems, putting them on or near one plane would make the
  map 2D and much easier to read: an option to offer him.
- **Travel between landmarks** (gaps of thousands of light-years: jump range, autopilot, drones).
- **What physics means at game scale**: keep the generator's real values for information (gravity, day length,
  temperature) or derive game values from the small radius.

## 3. What it changes

| Area | Today | Impact |
|---|---|---|
| GAME_DESIGN.md pillar 1, "The real galaxy" (Decided) | "A 1:1 Milky Way… ~120 billion systems" | **Reversed by the owner on 14 Oct.** Update the doc first (docs-first rule). |
| Galaxy generator (`src/Hollow.Galaxy`, GALAXY.md) | Density model, octree cells and catalogue merge for ~1.2×10¹¹ systems | Replaced or bypassed by a curated landmark layer; the system generator (bodies and their physics) still makes each system. Golden values change. |
| Planet scale | Real radii (thousands of km) | A game-scale world (~1 km radius class). Touches the ground game's first world (Nolcas Peris CQ-SLY 7-0 B 3), its terrain generation and LOD, and the "world needs local features" problem (a small world makes features dense). Owned by the `ground` line: coordinate. |
| Galaxy map | Prototypes in `prototypes/map-ux/` (round 3: `concept-known`), leads in `prototypes/map-ux/LEADS.md`, concept in `docs/SPACE_CONCEPT.md` | Far fewer stars removes most of the map problem; the leads (concept-ab frame, a shape per star, the knowledge map, drones, free camera tilt, no True scale) still apply. |
| System map (Godot) | Compressed only since 14 Oct (True scale removed) | Unchanged. |

## 4. Repository situation on 14 October 2026

| Where | What | State |
|---|---|---|
| `D:\GameDev\Projects\Hollow Universe` (`main`) | Milestone M1 | `ground` is 449 commits ahead; `main` has 4 doc commits (AGENTS.md, LORE.md) not in `ground`. |
| `Hollow-wt\ground` (`ground`) | The active line: ground game, generator overhaul, monuments, kernel, map prototypes, space concept | Active; another session works here. |
| `Hollow-wt\dev-panel` (`dev-panel`) | Branch made from `ground` on 14 Oct by another session | Purpose unknown to this session: ask before touching. |
| `Hollow Universe\EliteClone` | The Elite feasibility test (own local git repo, git-excluded from Hollow) | Finished; keep-or-discard pending. |
| `Hollow Universe\EliteClone-wt` (controls, maps, ui) | Its three phase-2 worktrees, all merged into EliteClone `main` | Removable (2.5 GB). |
| `Hollow Universe\research_notes`, `reports` | The Elite-generation research (git-excluded) | Keep; reference only (Elite data must never enter Hollow). |
| `D:\GameDev\Projects\Hollow-LAN` | A LAN build of the ground game (folder + zip) | Build output. |
| `RustMVP`, `Frontier-Drift-v0.2-Complete`, `Proper-Space-Game-v0.2-Complete`, `SphericalFrontierLab`, `DevSandbox`, `hollow-orbit-source` (+ zips) | Earlier attempts and labs | Archive candidates; RustMVP holds Rust-extracted files that must never enter Hollow. |
| `EDAPGui`, `EliteCompanion`, `EliteNexus`, `RoutePilot`, `sharex-capture-panel` | Separate tools, not the game | Leave alone. |
