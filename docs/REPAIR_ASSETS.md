# Revised owner assets integrated

The nine direct replacements from `D:/GameDev/Assets/Hollow_Salvage_R01` and
`Hollow_LootContainers_R01` are now used by the game catalogue:
gears, springs, metal pipe, sheet metal, tech scrap, rope, wooden loot crate,
reinforced equipment crate and the standard loot barrel. The fuel barrel remains
its separate model. Six salvage items also use the supplied 128/256-pixel icons.

`tools/integrate_revised_assets.py` verifies the complete SHA-256 rosters supplied
with both packages, then copies the native Godot scenes, shared PBR materials and
runtime textures. Resource paths are remapped under `game/assets/revised/`.
`integration.json` there records source and installed hashes and the exact copied
files. Provenance, handoff, README and source manifest are preserved beside each
package. These are owner-controlled original packages, with recorded CC0 timber
derivatives in the loot containers. This is a project-specific local integration,
not a receipt from the unavailable `game-dev` CLI.

Using the supplied `.tscn` files preserves their textured material overrides,
three authored LODs and closed convex collider proxies. Importing the plain GLBs
alone would not have reproduced the intended materials. Catalogue validation now
checks native scene LOD names, collision and recursive material/texture references
as well as the existing GLB pipeline. Authoritative world-prop half extents match
the revised measured closed bounds.

The build16 client capture set now covers all nine catalogue keys at authored LOD0,
with a fixed turntable angle, 1600×900 windowed resolution and isolated input.
Every process exited 0, reported ready=True, produced its expected image, and had
no runtime errors or input-hash drift. The nine native frames and labeled diagnostic
contact sheet are preserved under `docs/images/abtest-b/repairs/assets/`;
`assets-evidence.json` records source paths, image hashes, runtime identities and
per-capture telemetry. Copies were checked against their original SHA-256 values.
The root reviewed all nine and accepted their actual integration geometry and
materials. This is not an all-LOD, performance or final art-quality certification.

Existing destruction gibs are retained;
the new packages do not supply matching fracture meshes, and no new destruction
animation is claimed. Open/damaged inspection variants have not been substituted
for gameplay states that the current containers do not have.

The four resource-node families are integrated separately by
`tools/integrate_resource_nodes.py`: twelve depletion-stage meshes, authored LODs,
new PBR maps and measured hotspot surfaces. The live Godot surface survey ran
successfully after import. See the resource-node integration report for source
hashes and conversion details.

Environment kits need real placements, collision, clearance and loot integration.
Their existence on disk is not counted as a populated world. Held tools,
workstations and waterworks remain unproduced in the supplied R01 inventory.
