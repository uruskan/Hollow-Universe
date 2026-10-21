# PSG Resource Nodes R01

## Open the delivery

Open `Unreal/PSGResources.uproject` with Unreal Engine 5.8.3. Its startup level is `/Game/PSG_ResourceNodes/Maps/L_PSG_Nodes_Overview`. The second level is `/Game/PSG_ResourceNodes/Maps/L_PSG_MinerDeposits_Overview`.

Open `Source/PSG_ResourceNodes_Master.blend` with Blender 5.2.1 LTS. Scene `01_RESOURCE_NODES` shows all twelve node-stage meshes; `02_MINER_DEPOSITS` shows the four extractor deposits. These scenes use the actual baked LOD0 game meshes, not high-poly render substitutes. `START_HERE.html` is the offline gallery and inventory.

This is a separate asset project. The existing RustMVP/game project was not edited or overwritten.

## Contents

Four resources: Stone, Metal, Sulfur and HQMetal. Each node has three separate, closed depletion stages. Four additional ground deposits provide a central miner attachment point. Every primary asset has LOD0-LOD3, one custom convex collision hull, two UV channels and one material section per LOD.

The package contains **16 primary assets, 64 authored LOD meshes, 48 baked 4096 x 4096 texture maps, 16 collision hulls and 4 miner sockets**. Sixteen individual source `.blend` files include high-resolution source geometry, all four game LODs, collision and deposit sockets. The clean Blender overview is the seventeenth source file. Source generation, baking, import and validation scripts are included.

The gallery includes ten Cycles images of the baked game meshes and two screenshots captured using the native Unreal Engine 5.8.3 Direct3D12 renderer.

These are newly modeled, reference-based reconstructions, not extracted Rust or Satisfactory game files. They are not claimed to be byte-for-byte or pixel-exact copies of the original assets. The numerical checks document technical readiness; visual art approval remains subjective.

## Use in the game

In the delivered Unreal project, select `/Game/PSG_ResourceNodes` and use **Asset Actions > Migrate**, targeting the existing game's `Content` folder. Alternatively, with both editors closed, copy the entire delivered `Unreal/Content/PSG_ResourceNodes` directory into the target `Content` directory, preserving its internal paths. Do not copy only meshes and omit the material/texture dependencies.

The native files target UE5.8.3. They already have materials, texture settings, collision, custom LODs and sockets configured. Use the FBX/texture sources for a different engine version. Review maps contain lighting, captions, cameras and 100 cm rulers; those are not part of the resource meshes.

For a fresh FBX import, use import scale **1.0**, import normals and tangents, and disable automatic collision generation so the supplied UCX hull is used. Import the `_LOD1`, `_LOD2` and `_LOD3` files into the respective base mesh. Each main FBX contains one render mesh, named UCX collision and, for deposits, an attachment empty.

## Scale, pivots and depletion stages

Blender source units are metres. Exported FBX vertex coordinates and native Unreal assets are in centimetres, with identity object scale. All shipping node pivots are ground-centred at `(0,0,0)` and consistent across S01-S03. A buried underside prevents a paper-thin terrain edge. Display transforms in the overview scenes are not source pivots.

S01 is intact. S02 removes an inclined section and exposes fresh fracture surfaces. S03 is the lower remnant. These are geometric cuts, not uniform scale-downs; retained stratification and texture detail stay tied to local space. The game can hide/remove the mesh after S03 without needing an empty fourth mesh.

Deposits are approximately 6.2-6.3 m wide and 5.0-5.2 m deep. Their native attachment is named **Miner**, at local `(0,0,14.5)` cm, rotation zero and scale one. The central flat drill footprint is 110 cm in diameter. Place the miner relative to this socket, not the top of the coarse convex collision hull. The opaque closed skirt should sit slightly inside the terrain. Vertex alpha supplies an optional edge-blend mask; the delivered material does not require a terrain runtime virtual texture.

## PBR maps and UVs

Each primary asset has three baked 4K maps. `BC` is RGB8 base color, imported as sRGB/BC7. `ORM` is RGB8 linear data: red = ambient occlusion, green = roughness, blue = metallic, imported as Masks. `N_DX` is a RGB16 PNG tangent-space **DirectX** normal map, imported as Normalmap with **Flip Green Channel disabled**. Blender preview nodes invert green only for Blender's convention; do not reproduce that inversion in Unreal.

The master material `M_PSG_Resource` has `BaseColor`, `NormalDX` and `ORM` texture parameters. Every mesh has its own `MI_...` instance with explicit texture overrides. Geological variation does not require extra material sections. Editable high-poly source material graphs retain the original scans and procedural masks.

UV0 is the unique bake atlas; UV1 is the lightmap channel. Native meshes use lightmap coordinate index 1 and full-precision UVs. Texture bakes have 32-pixel dilation. Node charts reserve the bounds of all four LODs, not just LOD0. Vertex colors survive FBX/native import: R = mineral, G = weathering, B = fresh fracture; node A = 1 and deposit A = terrain-edge fade. The shipped appearance is already baked and does not require evaluating these masks at runtime.

## LODs and collision

The native LOD screen-size settings are 1.0, 0.50, 0.20 and 0.065. Nanite is deliberately disabled so the supplied LOD chain remains active and inspectable. Enabling Nanite later is an engine-side performance choice, not a package requirement.

Each primary mesh has one low-complexity convex UCX hull. It is a simple movement/interaction hull rather than a per-groove approximation. Use the desired complex trace or a separate weak-point component when gameplay requires exact surface hits. These are static meshes, not skeletal meshes or Chaos fracture collections.

## Measured inventory

| Asset | LOD0 | LOD1 | LOD2 | LOD3 | Dimensions (cm) |
|---|---:|---:|---:|---:|---|
| PSG_Node_HQMetal_S01 | 27,202 | 13,526 | 5,980 | 2,176 | 229.2 x 200.4 x 185.9 |
| PSG_Node_HQMetal_S02 | 19,914 | 9,942 | 4,380 | 1,592 | 227.7 x 200.5 x 174.6 |
| PSG_Node_HQMetal_S03 | 12,266 | 6,126 | 2,698 | 980 | 225.6 x 194.9 x 81.8 |
| PSG_Node_Metal_S01 | 25,444 | 12,700 | 5,596 | 2,034 | 243.5 x 202.1 x 182.8 |
| PSG_Node_Metal_S02 | 18,596 | 9,284 | 4,090 | 1,486 | 242.8 x 202.4 x 171.7 |
| PSG_Node_Metal_S03 | 11,410 | 5,704 | 2,510 | 912 | 242.6 x 198.2 x 83.1 |
| PSG_Node_Stone_S01 | 23,626 | 11,774 | 5,194 | 1,890 | 252.3 x 216.8 x 166.3 |
| PSG_Node_Stone_S02 | 17,202 | 8,586 | 3,784 | 1,376 | 251.1 x 217.1 x 162.9 |
| PSG_Node_Stone_S03 | 10,542 | 5,270 | 2,318 | 842 | 250.7 x 212.0 x 86.0 |
| PSG_Node_Sulfur_S01 | 23,728 | 11,854 | 5,220 | 1,898 | 252.1 x 214.7 x 168.5 |
| PSG_Node_Sulfur_S02 | 17,224 | 8,610 | 3,788 | 1,376 | 251.2 x 214.5 x 164.3 |
| PSG_Node_Sulfur_S03 | 10,550 | 5,274 | 2,320 | 844 | 247.8 x 212.9 x 84.4 |
| PSG_Vein_HQMetal_01 | 31,104 | 13,056 | 5,952 | 1,632 | 624.1 x 502.0 x 55.8 |
| PSG_Vein_Metal_01 | 31,104 | 13,056 | 5,952 | 1,632 | 617.6 x 495.5 x 55.3 |
| PSG_Vein_Stone_01 | 31,104 | 13,056 | 5,952 | 1,632 | 633.6 x 516.2 x 51.7 |
| PSG_Vein_Sulfur_01 | 31,104 | 13,056 | 5,952 | 1,632 | 627.1 x 517.8 x 50.8 |

## Verification

`Reports/PSG_*.json` records counts, bounds, volume, topology and UV checks for every LOD. All 64 game meshes passed the documented manifold, degenerate-triangle, positive-volume and UV-bound checks. No triangle-interior UV overlap was detected at the 2048-square test raster. Raster testing does not mathematically exclude every subpixel overlap.

`Reports/bake_validation.json` records PNG dimensions/bit depths, sampled atlas interiors, black bake-miss checks, normal-vector checks and ORM ranges. All sixteen texture sets passed their stated thresholds; this is not an exhaustive proof of every pixel matching the high-poly source.

`Reports/unreal_validation.json` records actual imports in **5.8.3-58210709+++UE5+Release-5.8**. All sixteen assets have four LODs, source-matching triangle counts, dimensions within 1 mm, one section per LOD, vertex colors and one convex hull. All four Miner sockets were checked. `Reports/unreal_final_audit.json` reloads the saved native files and verifies two UV channels per LOD, resolved material texture bindings, color spaces and DirectX normal settings. Both native review levels were saved, and both were captured using the real renderer.

No health thresholds, resource loot, harvesting logic, network replication, weak-point sparkle, miner machine model or mining animations have been added. Those remain game-side work as requested. This verification is not an FPS benchmark, network/stress test, packaged-build certification or console validation in the full game.

## Source workflow

`Scripts/psg_resources.py` is the geological geometry/material generator. `Scripts/psg_pipeline.py` builds LODs, UVs, bakes, FBXs and reports. Run it in a separate Blender background process, for example:

    blender -b --factory-startup --python Scripts/psg_pipeline.py -- --all --skip-complete

The pipeline resets its background scene; do not run it inside an unsaved working Blender session. Texture-source images are included under `ThirdParty/PolyHaven` so an offline rebuild does not require a paid asset library or online generation service.

`ue_import_resources.py` requires Unreal's full `-ExecutePythonScript` editor environment, not the commandlet-only Python runner. The included review, capture and audit scripts reproduce the staging workflow. Their outputs belong to this isolated project; manually targeting another project changes that destination.

Historical work-in-progress files are retained on the workstation in `_Work` but excluded from the release ZIPs. Release archives exclude Unreal Saved/Intermediate/derived-data caches and Blender backup files. Every archive is CRC-tested and has a SHA-256 sidecar. `Reports/release_checksums.json` records individual release-file hashes.

## Credits

Photographic rock detail uses the Poly Haven **rock_boulder_cracked** and **rock_08** CC0 texture sets. The geometry, geological masks, depletion surfaces, UVs, LODs, collision, bakes and project assembly were created for this package. See `ThirdParty/THIRD_PARTY_NOTICES.md` for provenance and source URLs. Rust and Satisfactory are visual/design references, not package dependencies or endorsements; no original game files or reference screenshots are redistributed here.
