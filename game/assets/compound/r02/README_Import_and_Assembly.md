# PSG Compound & Utilities — R02

## Start here

Open `Source/PSG_Compound_Utilities_R02_GameReady.blend` for the complete 12-asset review gallery. Press Spacebar to play the 180-frame rigid-motion review loop. The gallery layout offsets are for presentation only; individual asset files retain their canonical origins. LOD1/LOD2 and collision helpers are hidden by default.

## Open these files

- `Source/PSG_Compound_Utilities.blend`: revised source geometry, photographic materials and editable surface-aging setup.
- `Source/GameReady/<asset>.blend`: baked, segmented asset with LOD0, two reduced LODs, sockets and convex collision meshes.
- `Exports/GLB/<asset>.glb`: self-contained textured asset; moving-part hierarchy and socket empties retained. No gameplay scripts.
- `Exports/FBX/<asset>/SM_<asset>_<part>.fbx`: individual rigid component, including its named UCX hulls and sockets.
- Matching `_LOD1.fbx` and `_LOD2.fbx`: lower-detail component meshes, aligned to the same component pivot.
- `Textures/Baked/<asset>/`: BaseColor, NormalGL, NormalDX and packed ORM textures.
- `Preview/`: actual renders of the baked assets, not image-generated concepts.
- `Reports/Asset_manifest.json`: component hierarchy, assembly translations, collider definitions and design sockets.
- `Reports/QA/`: actual mesh, bake, LOD, collision and FBX round-trip measurements.
- `Reports/Articulation_QA.json`: rigid pivot checks and declared barricade camera/jump design assumptions.

R01 is preserved in the separate original R01 directory. Archived failed revisions are not part of the R02 production source scene.

## Texture and material settings

**BaseColor**: sRGB on. Do not use as a lighting or ambient-occlusion bake.

**NormalGL**: tangent-space OpenGL (+Y). Used by the Blender and glTF materials. Non-Color in Blender.

**NormalDX**: identical tangent-space normal bake with green channel inverted for DirectX/Unreal. Import as a normal map; sRGB off. Do not invert green a second time.

**ORM**: non-color linear data. Red = ambient occlusion; green = roughness; blue = metallic. In Unreal use a masks/data compression setting and sRGB off; connect the three channels explicitly. FBX automatic material import is not a substitute for these connections.

Source UVs orient individual photographed clay faces, timber grain and manufactured-metal patches to the modeled pieces. Baked assets use xatlas-packed unique 0–1 atlases, checked again from the exported GLB data. Baked assets use a unique 0–1 atlas shared across their rigid components. The second UV layer is a non-overlapping copy available for lightmap repacking. Choose/repack for the actual engine lightmap resolution; the shared texture-atlas padding is not a guarantee for every low-resolution lightmap setting.

Large assets use 4K atlases. Small assets use 2K atlases. Source photographs are retained at their downloaded resolution where used. The normal/roughness/color maps of photographic patches remain aligned.

## Unreal import and assembly

1. Import each LOD0 **component FBX** as a Static Mesh, not a Skeletal Mesh. Do not combine different moving components. Keep imported UCX collision; do not replace it with a single enclosing hull.
2. Import the two matching lower-detail FBXs through that component's LOD import controls. They are automatic edge-collapse reductions, not hand-retopologized replacement models.
3. Create a material from the baked BaseColor, NormalDX and ORM maps described above. Every component of the same asset uses the same atlas/material.
4. Assemble the static-mesh components using `parts[].parent_part` and `parts[].assembly_local_translation_m` from `Reports/Asset_manifest.json`. The manifest values are **meters**; multiply translations by **100** when assigning Unreal centimeter coordinates. Component meshes themselves carry FBX unit metadata; do not blindly add another x100 import scale.
5. Preserve the named pivots and sockets. FBX exports contain one render component per file so socket/collision names match that component exactly. The GLB contains the already assembled hierarchy in meters, with standard glTF Y-up conversion.

Source modeling uses meters, +X forward, +Z up. FBX dimensions are checked by export/reimport into Blender. That check is not a substitute for opening the assets in the target Unreal project and verifying its import configuration.

## Moving components and special requirements

| Asset | Rigid motion / special requirement |
|---|---|
| Large furnace | Open arched mouth, inner throat, grate and ash drawer. `FireFX`, `SmokeFX`, interaction and audio sockets. No fire particles or furnace gameplay included. |
| Short wooden barricade | Highest point about 1.922 m. Design check assumes a 1.65 m standing eye and a 0.55 m jump eye rise; peak clearance is about 0.278 m. These are declared design assumptions, not measurements of your current character controller. |
| Wood/stone compound walls | Placement plane at ground; left/right snap anchors. Barbed wire is visual geometry; add damage triggers in the game. |
| Wood/stone compound gates | Separate frame, left leaf and right leaf. Leaves rotate about their local Z hinge axes; example open angles +105/-105 degrees. Frame collision preserves the portal. |
| Windmill | `YawHead` rotates about local Z; `Rotor` about local X. Power-output and rotation anchors included. No wind/electricity simulation. |
| Orbital SAM | Separate Base, Yaw and Elevation. Yaw local Z, elevation local Y. Six launch sockets and a power input. Fictional visual prop only; no projectiles or targeting logic. |
| Spiked/metal barricades | Convex proxies for structural pieces and damage-center anchors. Damage is game-side. |
| Gunless turret | **No default gun, barrel or ammunition.** Empty receiver cradle with `WeaponMount` and rear guide. Yaw local Z; Pitch local Y. Attach a separate weapon actor; its muzzle belongs to that weapon, not this turret. |
| Wooden ladder | Upright 3.12 m rails, 11 rungs, start/end climbing anchors. Climbing mechanics are game-side. |

## Source material provenance

See `Reports/Texture_sources.json` for the source assets. Photographic surface sets include Poly Haven clay, stone, bark, weathered wood, galvanized/corroded metal, painted steel and reed thatch. Poly Haven asset files are CC0.

The rope uses the downloaded ambientCG Rope001 PBR set. Its metadata identifies its creation method as procedural; it is **not** described here as a photograph. Small utility surfaces such as lettering, dark soot, rubber, lenses and subdued cut-end grain retain authored/derived material data. No ChatGPT image generation was used for these models or their previews.

Baked colors include material tint and localized wear, but not studio illumination. The pack contains original modeled assemblies inspired by the supplied references; it does not contain extracted Facepunch game meshes.

Source licenses and import references:
- https://polyhaven.com/license
- https://ambientcg.com/faq
- https://dev.epicgames.com/documentation/unreal-engine/fbx-static-mesh-pipeline-in-unreal-engine
- https://docs.blender.org/manual/en/latest/render/cycles/baking.html

## Validation boundary

The accompanying reports describe checks actually run: source mesh cleanup, UV bounds, baked image validity, convex collision tests, LOD triangle counts, rigid pivot stability and FBX dimension round trips. The LODs use deterministic automatic simplification. Separate surface labels and small disconnected construction elements are not a Boolean-unified watertight manufacturing mesh; inspect the report's boundary counts rather than assuming a 3D-printing guarantee.

No Unreal runtime, network replication, placement snapping implementation, gameplay balance, damage behavior, actual character jump arc or target-platform performance test is implied. Those depend on the game project. The revised assets are not claimed to have passed an external “AAA” certification.
