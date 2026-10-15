# Resource-node asset integration — 15 October 2026

The owner-controlled `D:/GameDev/Assets/PSG_ResourceNodes_R01` pack supplies all four resource types:
Stone → `stone-node`, Metal → `metal-node`, Sulfur → `sulphur-node`, HQMetal → `high-grade-node`.
S01–S03 replace the existing stages 1–3. Each stage preserves the authored ground-centred metre pivot,
closed geometric depletion cut, four LOD meshes, UV atlas and convex hull. The inherited fractured gibs
remain assigned; they are functional fragments, not new fractures generated from these replacement meshes.

`tools/blender/resource_nodes.py` reads only the five game/collision objects from each individual source
blend. It verifies source SHA-256 against the delivery roster and checks every LOD triangle count against
the source QA report. It stages GLBs without embedded textures. Geological vertex masks are excluded
from final albedo because their appearance is already baked in the maps.

`tools/integrate_resource_nodes.py` prepares 2K RGB8 texture derivatives. Albedo downsamples in linear
light before returning to sRGB. ORM retains linear AO/roughness/metalness channels. DirectX normal maps
invert green for OpenGL and renormalize after filtering. The shipped 4K sources remain untouched. Native
ORM materials use mipmapped high-quality VRAM compression and anisotropic filtering. The 2K choice
limits memory across twelve distinct stage materials while retaining close-range geological texture.

The installer preserves existing model keys and world paths. Explicit LOD distances 14/35/90/260 metres
activate all four levels; the previous three-distance default would give the fourth LOD an empty interval.
Harvest rewards, native-resource kind order, scattering and simulation dimensions belong to gather data,
coordinated separately with the environment work.

Mining hotspots depend on the actual mesh. The Blender conversion measures all LOD0 stages using the
same four rings and 32 directions as `SurfaceSurvey`, with glTF's `(x,z,-y)` axis conversion. It updates
only these four kinds in `surfaces.json`. The final game-side survey after Godot import verifies the
rendered geometry and rewrites the authoritative measured file.

`game/assets/revised/PSG_ResourceNodes_R01/integration.json` records selected source hashes, geometry
counts, texture transformations and output hashes. The pack's README, release checksum roster and
third-party notices are retained. Geometry and assembly are owner-authored; photographic detail uses
Poly Haven CC0 textures. No original Rust or Satisfactory assets are included in the supplied pack.
The optional `game-dev` CLI was unavailable; this is a verified local integration receipt, not a canonical
Game Development Studio package. Static checks do not by themselves establish visual approval or FPS.
