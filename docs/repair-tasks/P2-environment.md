# P2 Terrain materials and vegetation

Repair N3/N6 and environmental parts of N5/N12, R2/R6/R10 and L3. Read COMMON.md,
REPAIR_BACKLOG.md, REPAIR_ENVIRONMENT_2026-10-16.md and screenshots 3/4/6/8/9/10.
Screenshot 7 duplicates 6. The owner rejects obvious texture repetition and sunken/flat grass
even at Maximum. Achieve high-fidelity continuous terrain and believable vegetation with
measured cost. Retain supplied high-quality source channels and approved ground UI.

Ownership: terrain/grass/authored-cover/water shaders; `game/scripts/Surface/GroundLayers.cs`,
`GrassField.cs`, `GrassField.Authored.cs`, `Flora/FloraLibrary.Trees.cs`; associated cover/tree
art/import assets and tests. Confirm shader/import paths. Shared `FloraLibrary.cs`, surface
settings, Scatter and EnvironmentRoute require a specific file transfer before editing.
P1 owns scanned rock/cliff shaders/assets/placement. The Earth-area chat owns WalkingWorld
and parameters. Do not change world heights to conceal material/grass problems.

Suggested three-agent split:
1. Terrain texture repetition/seams/material scale, with ground/elevated diagnostics.
2. Grass root placement, orientation, mesh/LOD/density/lighting and owned cover assets.
3. Remaining tree/bush/water/night presentation and cost audit with separate owned files.
Sequence coupled shaders and renderer integration through the lead.

Determine whether screenshot 4 shows periodic UV repetition, chunk seams, masks or multiple
causes. Evaluate multi-scale variation, stochastic sampling/blending and slope treatment only
after diagnosis. Preserve normals/roughness and sensible texel scale; more samples have cost.
For grass inspect actual root/pivot versus height sampling, radial frame, slope, mesh scale,
LOD and draw-distance behavior before simply increasing height. Seek earlier reference images
in available chat/artifacts; do not invent a match if the reference cannot be recovered.

Carry forward broadleaf/bush quality, biome-specific dressing, medium/far foliage consistency,
shoreline foam/refraction and night readability. Coordinate cliff vegetation interfaces with
P1, not duplicate geology. Preserve dense convincing cover while improving its representation.

Acceptance: ordinary hosted walking routes, ground and elevated views, day/night, slopes and
several biomes; no obvious tile grid or sunken roots, plausible silhouettes/material transitions,
clean LODs and readable night terrain. Preserve originals, settings and paired CPU/GPU/p95/p99
samples. Lowering cover density/render scale alone is not an equivalent-quality repair.
Request shared build/import/GPU slot. Write `docs/repair-results/P2-environment.md`.
