# Salvage — original asset R01

The `salvage` resource receives its own scrap assortment model and rendered icons,
replacing the metal-fragments model and SVG. ID 150, stack 1000, description and
all recycler, research, workbench, loot and currency bindings stay unchanged.

## Reference and direction

Inspected the [official Rust Scrap page](https://wiki.facepunch.com/rust/item/scrap)
and [item image](https://files.facepunch.com/rust/item/scrap_512.png) before authoring.
The reference combines a large toothed wheel, stout spring, corrugated sheet,
hollow pipes and two loose nuts in a compact brown, oxidized steel pile.

Our geometry, proportions, arrangement and texture marks are original. The main
gear and coil, folded metal and open pipe ends retain those recognition cues.
A broken bracket and small wire loop complete our assortment. No Rust pixels,
models or other game files enter the repository; only reference links are kept.

## Asset contract

- Repeatable source: `art/models/salvage.py`, with the unchanged original
  `art/hollow/component_mesh.py` helper. Build via `art/build.py -- salvage`.
- Approximately 0.369 × 0.319 × 0.111 metres in Blender XYZ. Shared bottom pickup
  pivot, convex collision, no held sockets or animations.
- Three LODs: 7,752 / 4,972 / 3,184 triangles, within 10,000 / 6,500 / 4,000 budgets.
  The convex hull has 42 triangles. Gear bore, all four pipe ends and both nut holes
  stay open at every LOD (ray check on the exported GLB). The front pipe's centre line
  is clear end to end, but the gear's teeth and a trough of the corrugated sheet pass
  through its upper wall into the middle of the bore, 22 mm from its right end at
  LOD1 at the closest. This is hidden from outside in every capture and left as authored.
- One material and one external 1024px albedo/normal/ORM atlas. Original rust,
  worn steel and scratches; an eight-sample vertex occlusion pass separates the
  overlapping parts without a texture bake or runtime cost.
- Production IconRenderer makes 128/256px RGBA icons, using view `[24, 58, -8]`.
  The catalogue uses this asset directly, with no icon alias or stand-in.

## Validation and limits

Native screenshots and executed checks are recorded in
`docs/images/item-art-salvage-20261019/validation.json`; source and asset hashes
are in `game/assets/items/salvage.provenance.json`.

Review uses the production ModelViewer, IconRenderer and CatalogItemIcons from
existing compiled game assemblies in a small isolated project. It covers all
three LODs, the back and diagnostic icon slots at 16–256px with no SVG fallback.
The 16px check represents the tech-tree cost icon's logical size. Diagnostic
slots do not establish full live gameplay inventory acceptance. The captures were
rendered from resources whose hashes match the committed assets. Both icons are
RGBA with full transparency and unclipped bounds.

The validation record also holds the GLB check (finite attributes, unit normals,
external textures) and the opening rays: inside each opening every ray misses
within the part's own depth, and control rays through the part's wall all hit.

The full solution build passed with zero warnings and errors, and all 13/13 focused
catalogue/content checks passed. The broader suite was not rerun for this
asset-only change. Artistic acceptance remains with the owner.
