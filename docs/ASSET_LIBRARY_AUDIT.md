# Asset library audit — 16 October 2026

The remembered window covers exist. Five window assemblies are already imported into this
project, but they were never connected to playable items and placement. The wider audit found
additional unused doors, solar-panel and ore-deposit assets, scenery, animations and animal
models. Several older notes incorrectly describe assets that have since been integrated.

This is an inspection report, not an integration or art-approval pass. No game assets, code,
settings, running processes or saves were changed. The original Hollow Universe project was
read only. Existing unrelated working-tree changes were preserved.

## Scope and evidence

- Enumerated **12,721 local files** in `D:/GameDev/Assets`, grouped into 87 populated
  top-level groups (including root files). This includes backups, engine packages and excluded
  reference material; it is not a count of distinct usable assets.
- Inspected authored package manifests, importers, GLB contents, data catalogues, runtime
  consumers, provenance and delivery/review notes. Compared original and current model paths.
- The saved Fab inventory has **1,264 ownership observations**, including 1,249 Quixel entries.
  Its September metadata explicitly says whole-account completeness is unverified. It is not a
  fresh online inventory, proof of local download, or a universal licence clearance.
- All **142 base model-catalogue file references** resolve. This check alone does not establish
  that every variant, texture or dynamically generated path is valid or that every model is
  accessible in normal play.
- All **121 monument GLBs** (57 monument variants and 64 modular parts) are byte-identical to
  the original project's copies. They were not lost during the project copy.
- The original has 299 model/scene files versus 336 here. Its only six original-only model files
  are legacy salvage GLBs; all six have newer native-scene replacements in the active catalogue.
  Of the other 172 shared non-monument models/scenes, 146 match and 26 have changed through the
  existing building, weapon, resource-node and loot repairs. No unexplained missing model/scene
  was found in this comparison. Original-only audio/textures were not covered by that comparison.

The [machine-readable evidence](reports/asset-library-audit-2026-10-16.json) records directory
counts, source inventory metadata, window mesh names, all monument bounds/selection flags,
monument hashes, package-manifest checks and installed receipt checks. The local reproduction
utility is `.artifacts/asset-library-audit/audit_inventory.py`.

## 1. Building assets missing from normal play

| Asset | What exists | Why it is absent / required work |
|---|---|---|
| Metal window bars | Imported mesh, two lower LODs and collision source | No item, recipe, model-catalogue entry or window-attachment gameplay entity |
| Armoured window bars | Same | Same |
| Reinforced glass window | Frame, pane and wire meshes, each with lower LODs | Same; three meshes form one window option |
| Horizontal embrasure | Imported mesh, lower LODs and collision source | No playable window attachment |
| Vertical embrasure | Imported mesh, lower LODs and collision source | No playable window attachment |
| Wooden double door | Frame and two leaves; door rules and renderer support | No inventory item, recipe, progression or placement-catalogue entry |
| Armoured double door | Frame and two leaves; door rules and renderer support | Same |
| Glass surface map | `PSG_DoorWindowKit_R01/Textures/T_PSG_Glass_Surface.png` | Not consumed; current glass material uses uniform colour, transparency and roughness |

The source is `D:/GameDev/Assets/PSG_DoorWindowKit_R01`. Its `assembly_manifest.json`
lists **12 assemblies: seven doors and five window options**. The window opening is 1.8 × 1.2 m
with a 0.9 m sill. No wooden hinged shutter was found in this inspected kit; the matches to
"window covers" are bars, reinforced glass and embrasures.

`game/assets/building/window_inserts.glb` contains seven base mesh groups, their LOD1/LOD2
meshes and collision meshes. `game/scripts/Build/BuildingAssets.cs:61` includes that file in
the normal building asset loader. `docs/GROUND.md:2394` explicitly calls these inserts
**"art only in phase 1."** Reimporting the file will not make them playable.

Window support needs a shared attachment rule for the window aperture, placement validation,
authoritative collision/damage, persistence/network state, removal/repair interactions and
item/recipe/progression data. Existing door opening handling only admits Door/Double:
`src/Hollow.Simulation/Building/BuildingData.cs:669`, `Doors.cs:235`, and
`BuildingWorld.Placement.cs:605`. Extend the shared systems; do not add per-window special paths.

The two inaccessible double doors are a smaller integration task. They already have definitions
in `game/data/ground/building.json:146` and `:158`; the five normal player door choices are in
`items.json:198` and `placement.json:69`. Their historical deferral is recorded at
`docs/GROUND.md:5439`.

**Not missing:** all 65 original structural meshes (13 shapes × five grades) are represented.
The importer adds seven shape families, providing 20 shapes × five grades. All 23 static
door/window source parts plus the garage curtain and Open/Close clips are present. The omitted
stone floor-frame LOD2 meshes are intentional (`GROUND.md:2391`). R02_GameReady is Unreal
packaging of this kit, not another set of missing Godot models.

## 2. Authored equipment and scenery

Across the six `Hollow_*` art packages, **18 of 27 unique designs are already used**.
The unused remainder is **nine designs**, plus **seven ruin decals** and the variants below.

| Package | Already used here | Available but unused |
|---|---|---|
| Hollow_Salvage_R01 | All six salvage designs | No missing design |
| Hollow_LootContainers_R01 | All three intact containers | Six open/damaged presentation variants; not six additional designs |
| Hollow_Substation_R01 | All five assets in Service Substation | No missing design |
| Hollow_PowerKit_R01 | Breaker, open disconnect, termination pedestal | Gantry column, crossbeam, insulator; closed disconnect variant |
| Hollow_RuinDressing_R01 | Hardware cluster and oil-seep decal | Bent sheet, broken cable tray, concrete break; oil-pool, water-runoff, soot-fan, paint-abrasion, hazard-band, keep-clear and isolate-04 decals |
| Hollow_WreckModules_R01 | None | Pressure hatch, thruster/service cartridge, rib/deck corner |
| PSG_StarterKit_R02 | 17 of 19 designs converted | SolarPanel; old AK intentionally superseded by Frontier rifle |
| PSG_ResourceNodes_R01 | Four node families × three depletion stages | Four miner deposits: Stone, Metal, Sulfur and HQMetal |

The **solar panel** is especially relevant to confirmed electricity requirements. Source:
`D:/GameDev/Assets/PSG_StarterKit_R02/Assets/SolarPanel/asset.json`; editable Blender/FBX,
three LODs, three hulls and a `PowerOut` socket exist. `art/import/starter_kit.py:6` explicitly
says it is not an item yet. It still needs conversion, a deployable definition and the functional
electricity system. The PowerKit substation props are industrial scenery, not that system.

The **four ore deposits** support the confirmed vein/autominer direction. They have four LODs,
collision hulls, PBR maps and four `Miner` sockets. The supplied pack does not include an
autominer machine or extraction gameplay. Keep the existing twelve gatherable node stages.

Native Godot scenes, materials, authored LODs and editable source make the remaining Power/Ruin
parts practical scenery candidates. They still need intentional placement and collision/loot
integration. Some small ruin props have no collision; the larger proxies are broad approximations.
Decal rendering evidence covers Forward+ only. Open/damaged loot variants need state routing;
current containers/barrels do not acquire opening or damage animations merely by copying them.

The **wreck modules are a revision-2 resubmission awaiting review**, not a complete spacecraft.
`Hollow_WreckModules_R01/CHANGELOG.md` describes corrections to the earlier rejected submission.
The old `Hollow_Production_Reviews/WreckModules_R01/REVIEW_DECISION.json` refers to earlier
handoff/manifest hashes and must not be treated as a fresh rejection of revision 2. The moving
hatch hierarchy/collision must be preserved. Current package integrity also needs attention below.

Integration evidence: `game/data/ground/models.json`, `game/assets/revised/integration.json`,
`game/assets/revised/substation-integration.json`, and `tools/blender/environment_substation.py:11`.
These are more current than the external production inventory's "not integrated" notes.

**Not missing:** Tin Finch and Iron Wake Buildable R01 are integrated; IW09 is an older version,
and the Benchmark folder is an independent test project. Tin Finch's fourth source LOD is unused,
not a missing ship. C4 smoke/fire flipbooks, flash, pressure, distortion, dust, sparks, scorch and
debris already enter the FX pipeline. HeldTools, Workstations and Waterworks R01 briefs describe
planned deliveries, not completed asset packages located by this audit.

## 3. Most archived monuments are not selected by the live world

The archive contains **55 types / 57 model variants**. The live selection currently includes
only **weather-station, crashed-skiff, hab-pods, wellhead and relay-mast**, plus the newer
**service-substation** assembly. Thus **52 archive model variants are outside the live selection**.
They remain available in the monument viewer and on disk.

The selection is explicit in `tools/blender/environment_monuments.py:13`, compiled into
`src/Hollow.Surface/Data/monuments.json`, and consumed by `MonumentDefinition`,
`MonumentPlacements` and `game/scripts/Surface/MonumentField.cs`. This is an integration/selection
gap, not a missing-file problem.

Small candidates for an initial expansion, using authored bounds rather than guessed scale:

| Candidate | Approximate footprint | Caveat |
|---|---|---|
| Survey Marker | 8.6 × 8.2 m | Reveal marker is metadata; not an implemented reveal mechanic |
| Bunker Hatch | 8.0 × 8.0 m | Requires checking underground access, hatch and ladder behavior |
| Drop Pod | 23.3 × 25.3 m | Needs terrain placement, collision and loot validation |
| Stalled Crawler | 32.0 × 10.7 m | Uses ten modular parts; check traversal/collision |
| Container Spill | 48.1 × 19.7 m | Uses seventeen modular parts; check placement/loot |
| Checkpoint | 19.9 × 34.1 m | Guard markers do not provide NPC gameplay |

Larger Mining Camp, Research Outpost, wrecks and industrial sites also exist. Their footprints,
terrain treatment and density need evaluation against the new planet-size limit. Existing
placement rules reject wet/steep sites and overlapping footprints; do not add all 57 blindly.
`MonumentDefinition.cs` currently consumes loot markers, while many archive interaction/light/
guard/fuse/reveal markers remain descriptive metadata.

## 4. Environment palette available locally

**Already integrated:** seven tree variants (three fir, three birch, one pine), twelve authored
ground-cover variants (four families), nine rock variants, five cliff shapes and sixteen ground
material layers. The specific catalogs and `docs/REPAIR_ENVIRONMENT_2026-10-16.md` supersede
the stale general statement that all vegetation/rocks are procedural.

Ten extracted plant families have standard GLTF/textures/LODs and extraction provenance but
are not selected by the current cover importer:

| Source directory under D:/GameDev/Assets | Useful role |
|---|---|
| bilberry_vldufeiha_ue_high | Forest understory / berry-bush appearance |
| crowberry_xeolbhcja_ue_high | Low ground cover |
| alpine_bearberry_xb0gdazqx_ue_high | Alpine/tundra cover |
| downy_willow_wknlfiuqx_ue_high | Streamside shrubs |
| dwarf_birch_xblnaabqx_ue_high | Tundra thickets |
| wood_sorrel_wcwmchzja_ue_high | Forest floor |
| rhazya_xdjmfeqqx_ue_high | Desert shrubs |
| dead_shrubs_vdknafgha_ue_high | Dry/exposed areas |
| dry_grass_tbbqejqr_ue_high | Dry grass palette |
| nordic_coastal_groundcover_vlqkdeaja_ue_high | Coastal cover |

These are integration candidates, not licence-cleared shipping additions. The `_ue_high` suffix
alone is not a licence determination: these folders contain `standard/*_nonUE.gltf` files.
Crowberry, coastal groundcover and wood sorrel have exact-name ownership records; willow and
dwarf birch also have local verified download receipts. Confirm each applicable entitlement.
`GrassField.Authored.cs:24` currently assumes four families and three variants, so adding a
catalog row alone is insufficient. Preserve shared scatter/gather behavior for interactable plants.

Further local variety:

- Fir source has nine near-tree models, of which three are selected; birch has five families,
  of which three are selected. Additional variants need the same LOD/material validation.
- `Bare-World-Prepared/extras/pine_forest/polyhaven_pine_fir_forest.blend` includes saplings,
  dead trees, roots, fallen trunks, branches, fern varieties and moss. Its 299 scene objects
  include components/variants, not 299 unique asset designs.
- `Bare-World-Prepared/extras/namaqualand/Namaqualand.blend` contains dry-biome plants including
  quiver trees, iceplant, didelta, othonna, searsia and dead branches. Its 81 scene objects are
  likewise not unique-asset counts. Local records identify these two packs as Poly Haven sources.
- 45 of 51 loose root-level GLBs have no filename reference in active import/runtime/data source.
  This is a discovery shortlist, not proof of unique unused content. It includes desert cliff/rock
  sets, ice cliffs, snow piles/embankments and fallen/dead trees/roots. Preparation is still needed.

Twenty extracted texture directories are absent from the current sixteen-layer source plan:
`beach_sand_uinladeo_4k`, `beach_sand_with_pebbles_vl0fdilo_4k`,
`cracked_nordic_coastal_rock_ukohaj0mw_4k`, `desert_outback_ground_rocky_06_xiqgafr_4k`,
`desert_sand_sfilxqh_4k`, `desert_sand_sfiodwh_4k`, `desert_sand_wkjgche_4k`,
`desert_western_ground_gravel_coarse_02_xglmbct_4k`,
`desert_western_ground_sand_fine_01_xgmpdek_4k`, `forest_floor_wgyhdgy_4k`,
`fresh_windswept_snow_uehnaazn_4k`, `ice_wall_uebjabcr_4k`,
`icy_snow_on_dry_grass_sjziebea_4k`, `melting_snow_uexjbdudy_4k`,
`sleet_covered_gravel_tb5oaf2s_4k`, `snow_cliff_uhqncfpdy_4k`,
`windswept_snowy_stones_tdloaf1r_4k`, `tundra_gravel_ground_vjepchymw_4k`,
`tundra_lichened_rock_vjbode1mw_4k`, `tundra_rocky_ground_vi2mahnmw_4k`.
These are material alternatives, not missing gameplay. Choose biome mappings intentionally;
the current layer layout is fixed and texture-memory cost matters.

## 5. Animation, animals and audio

The installed Quaternius Standard sources contain **89 animation entries** (46 + 43).
The game uses 37 source clips plus six authored clips. The other 52 source entries include
duplicate reference poses and clips irrelevant to this game; they are not 52 missing features.
Useful unused CC0 clips include `Driving_Loop`, `Fixing_Kneeling`, `Sitting_Enter`,
`Sitting_Exit`, `Sitting_Talking_Loop`, `Push_Loop`, `Chest_Open`, `ClimbUp_1m_RM`,
`Farm_Harvest`, `Farm_PlantSeed` and `Farm_Watering`. Each needs retargeting and a corresponding
gameplay state. The starter weapons' component animations are not character-hand/body clips.

`D:/GameDev/Assets/Animated Animals 1.0.zip` contains seven nested animal packs: **Bear, Deer,
Doe, Donkey, Moose, Pronghorn and Elk**, with Blender/FBX models and animations. Bear includes
90 animation FBXs. None is used by the current procedural deer/boar/wolf/bear builder. No
licence/readme/credits was found in the outer or seven inner ZIPs and no corroborating provenance
was located. These are valuable candidates pending source/licence confirmation, not cleared assets.

`D:/GameDev/Assets/Audio` is empty; audio source packs are in `Downloads`. The existing
`docs/reports/audio-action-checklist.md` records 235 events / 775 unique files and unused recordings.
Two integration candidates are more concrete than acquiring more sounds: `gun.shell` has four
OGG variants and weapon sound roles, and `build.repair` has a hammer sound role, but no successful
gameplay playback consumer was found for either in this inspection. This is a source-trace
finding, not an audible runtime reproduction. CC0 gravel/leaves/twigs/mud/snow alternatives are
also available for audition; current animal voices use authored synthesis.

## 6. Integrity and limitations

Every recorded file entry in six source manifests was freshly hashed:

| Source package | Matching / recorded entries |
|---|---|
| LootContainers | 125 / 125 |
| PowerKit | 171 / 171 |
| RuinDressing | 233 / 233 |
| Salvage | 256 / 256 |
| Substation | 88 / 88 |
| WreckModules | **200 / 204** |

Wreck's differences are `project.godot` and the three `Textures/Wreck_*.png.import` files.
Its recorded geometry, scenes, maps and other entries match. The exact expected/actual hashes
are in the evidence JSON. Reconcile these changes and refresh validation before integration;
do not treat the historical "pass" receipt as current whole-package integrity.

The active integration receipts also match **all 118 source/installed file pairs**: Salvage 34,
Loot 15, Substation 45, Power 15 and Ruin 9. Installed paths can intentionally differ from source
bytes after resource-path remapping; each was compared with its respective recorded hash.

These are legacy project-specific manifests. `game-dev` was not found on PATH; no canonical
package admission, migration or catalogue rebuild was performed. Hash checks establish recorded
byte integrity, not closed-roster certification, source authorship, runtime behavior or art approval.
No source licences were refreshed online, no paid assets were acquired, and no assets were copied.
Rust-extracted research/Oxum content and explicitly engine-restricted Epic samples are excluded
from the usable recommendations. Legitimately documented sources within mixed staging folders
were assessed individually rather than approving those folders wholesale.

No build, simulation suite or Godot playtest was run: the deliverable is documentation/data
inspection and no executable game content changed. Existing screenshots/review reports were
treated as historical evidence, not newly reproduced validation.

## Recommended integration order (proposal)

1. Expose wooden/armoured double doors and implement the five window attachments through the
   shared building systems. The artwork already exists; verify snapping, damage, bullets through
   openings, save/network behavior, crafting and icons in real gameplay.
2. Expand small-monument selection and evaluate the remaining ruin/power scenery to improve
   exploration variety. Reconcile/review the revised wreck package before using its modules.
3. Use the existing solar panel and four deposits when implementing the confirmed electricity
   and vein/autominer systems. Do not mistake their sockets for completed functionality.
4. Add selected shrubs, tree variants and relevant Quaternius clips with measured rendering cost.
   Resolve the animal pack's provenance before considering a replacement of the procedural animals.

These priorities are recommendations from this audit, not new confirmed design requirements.
