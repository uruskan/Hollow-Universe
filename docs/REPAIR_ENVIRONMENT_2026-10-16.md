# Environment pass — 16 October 2026

This pass is installed in AB Test B. It improves tree models, ground/grass presentation,
water-normal tiling and the night star field. It is an incremental environment pass, not
completion of the game's art direction or the entire repair backlog.

## What changed

- Three authored fir variants, three birches and one pine replace the corresponding
  procedural models. Native UVs, bark, leaf opacity, normals and roughness are used by
  the shared standing/falling tree library. Oak/beech and bushes remain procedural.
- The pine conversion preserves complete branches and leaf cards. The first candidate
  visibly failed: simplification produced giant triangles, sparse crowns and a pinched
  trunk. The final conversion fixes those defects and restores source roughness maps
  and leaf cutoff. See [REPAIR_TREES.md](REPAIR_TREES.md) for sources and measurements.
- Existing authored ground cover remains: four families, twelve variants and its LODs.
  No cells, instance counts, widths or placement rules were removed. The repetitive
  base grass is shorter (0.18–0.32m before existing density/patch multipliers), allowing
  the authored crowns to show. The first, shorter candidate was rejected as too bare.
- Grass terrain previously discarded the scan's RGB and retained only luminance under
  one green tint. Nearby ground now retains 55% of the dry-grass scan colour, gradually
  reaching the existing distant blade match at 42m. Normals, roughness and height maps
  remain native. A full scan-colour candidate was rejected because it made meadows brown.
- Cloud shadows use the current directional light, so moonlight no longer samples the
  sun's shadow direction. This is shared by terrain, vegetation, geology and water.
- The stretched wave-normal field now wraps correctly on both axes. Independent numeric
  reproduction reduced the height seam from 0.482 to floating-point epsilon, with
  interior gradient strength essentially unchanged. Shoreline foam was not redesigned.
- Procedural medium/faint stars are fewer and dimmer. Actual system bodies, galaxy
  presentation, exposure and ground ambient light retain their previous settings.

The earlier geology work is preserved: native scanned boulders and connected Nordic
cliff assemblies with shared collision. Its recovered notes and evidence are in
[REPAIR_GEOLOGY.md](REPAIR_GEOLOGY.md). Local metadata identifies its source chat as
`01a140d9-c7dd-7480-9fe5-c8b1c525ad66`; the app could not retrieve that chat's durable
history or title. This is evidence of surviving work, not proof that the chat was deleted.

## Runtime evidence

The [hosted route](ENVIRONMENT_ROUTE.md) walks the real Earth1200 region
`service-substation/tile 5/3/4/7`, 360.55m plus the normal spawn-ring approach. It uses
normal child-server hosting, shared player input/prediction/collision and isolated fresh
saves. It neither teleports along the route nor substitutes a separate scenic world.

Both baseline and final candidate completed all six ready, grounded checkpoints at
1600×900: High/day and Maximum/night. The actual night sun is below the horizon; time,
camera, resolution, graphics and route are recorded. Final camera differences are at
most 7.5cm and 0.12° from the corresponding baseline, caused by normal movement.
The final monument checkpoint is obstructed by an unchanged broadleaf trunk, so it is
not evidence of monument readability. Nearby cliff and tree views are inspectable.

Original frames, raw timing samples, logs, launch DLL hashes and the comparison are in
[the evidence folder](images/repairs/environment-2026-10-16).

| View | Before | Installed candidate |
|---|---|---|
| Woodland | [baseline](images/repairs/environment-2026-10-16/baseline-route-high2/01-woodland.png) | [candidate](images/repairs/environment-2026-10-16/candidate-route-high2/01-woodland.png) |
| Clearing | [baseline](images/repairs/environment-2026-10-16/baseline-route-high2/02-clearing.png) | [candidate](images/repairs/environment-2026-10-16/candidate-route-high2/02-clearing.png) |
| Cliff approach | [baseline](images/repairs/environment-2026-10-16/baseline-route-high2/04-cliff.png) | [candidate](images/repairs/environment-2026-10-16/candidate-route-high2/04-cliff.png) |
| Night | [baseline](images/repairs/environment-2026-10-16/baseline-route-max-night/02-clearing.png) | [candidate](images/repairs/environment-2026-10-16/candidate-route-max-night/02-clearing.png) |

The separate pine chopping tour verifies accepted hits, the X chain, authoritative
depletion, 125 wood on the final hit, and the new mesh falling/landing. This diagnostic
uses admin setup and is not the walking-route or performance evidence. Its first attempt
failed: Windows child-server stdin decoded degree suffixes incorrectly. The pipe now
uses explicit BOM-free UTF-8, with matching redirected server input and unchanged
interactive-console input. The tour now rejects missing hits, failed teleport and an
undepleted tree instead of writing misleading falling screenshots.

## Cost and checks

The visual improvement has a measurable cost. On this RTX 2070 SUPER, High/day walking
leg medians moved from 14.25–15.50ms to 15.19–17.68ms; the woodland P95 moved from
16.73 to 19.89ms. Maximum/night woodland median moved from 28.04 to 36.20ms, while
water/cliff legs improved. These are single paired runs, each leg sampling at most 20 s;
they are not an overall FPS guarantee. Dense-canopy optimization remains necessary.
See the [complete comparison](images/repairs/environment-2026-10-16/comparison.md).

Validation at the environment checkpoint:

- Solution build: zero warnings/errors. Godot imports: successful, with GPU-compressed
  mipmaps and correct non-colour map settings.
- Full engine-free suite: 389/391; two network-timing tests passed on immediate isolated
  rerun (2/2). This is explicitly not a clean full-suite pass. Final console/gather/route
  focused checks after the pipe repair: 18/18.
- Later shared-checkout suites passed 392/392 (held-animation checkpoint) and
  394/394 (loading/performance checkpoint). Their retained logs document those
  later states; they do not replace the earlier timing failures or visual checks.
- Independent installed-pine audit: 128/128 rays, outward sampled normals/winding,
  unchanged chopping-band positions/UVs/topology, complete source leaf cards, finite
  attributes and matching hashes. Final near/medium meshes: 25,776/7,521 triangles.
- Forced near/medium/far tree lineups rendered successfully with empty stderr.
  Medium birch is sparser and distant foliage brighter; those LOD differences
  remain visible. These are surface-view diagnostics, not gameplay-route captures.
- All four matched route runs stayed connected and completed; one brief airborne sample
  during movement appears in the final High run, with every capture grounded and the
  existing airborne/stuck guards retained.
- Route exits still report seven texture-RID and two ObjectDB leaks. The separate chop
  run reported unfinished gib warm-up after 40 s and fourteen ObjectDB leaks. These are
  recorded limitations, not hidden clean-shutdown claims.

Recompute comparison from retained raw reports:

```text
python tools/compare_environment_routes.py --evidence docs/images/repairs/environment-2026-10-16
```

## Remaining work

Broadleaf/bush art, biome-specific dressing and material balance, shoreline foam/refraction,
night terrain readability and anomalous warm lighting patches, dense-forest cost, and
streaming/exit cleanup still need work. The grass remains an authored-cover/painted-base
hybrid; this pass does not claim Rust/Sons of the Forest production quality. Asset/LOD
captures cannot substitute for longer walking and playtesting across more biomes.

The authorized “Fix loading stall and low FPS” chat now owns loading/shadow-budget
repairs and further isolated performance measurements. Its changes are excluded from
this environment commit. It also accepted responsibility for reopening the normal
AB Test B menu after its GPU tests; the menu stays closed during those measurements.

Audio remained first in scheduling: the coordinated audio chat integrated its ground-contact
and action-audio repair before these runs. [REPAIR_AUDIO.md](REPAIR_AUDIO.md) distinguishes
the verified repair from the remaining sound-library work. Held-item animation work continues
in its own authorized chat and is not claimed as an environment accomplishment.
