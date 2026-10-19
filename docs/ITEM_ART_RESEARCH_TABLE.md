# Research Bench — original asset R01

The `research-table` deployable (the Research Bench) receives its own model, gibs and
rendered icons, replacing the engineering-workbench model and gibs it borrowed and the
SVG drawing. ID 171, name, recipe, health, three slots, pickup, research rules and its
placement bounds stay unchanged.

## Reference and direction

Inspected the [official Rust Research Table page](https://wiki.facepunch.com/rust/item/research.table)
and [item image](https://files.facepunch.com/rust/item/research.table_512.png) before authoring.
The reference is a tilted drafting board on a blue steel trestle, with a crank wheel at
its side and a magnifier on an arm over a blueprint, in worn paint with rust. It reads
at small size by the inclined top and the steel frame.

Ours is a flat dismantling bench in the plan's terms: inspection tools, a work surface
and sample storage. A blue-grey painted steel trestle with side braces carries a timber
top in angle steel. On it a green cutting mat lies under a magnifier on a clamped arm,
with a device taken apart (its case, circuit board and lid), screws, a gear and a
screwdriver beside it. A salvage tray full of scrap stands at the left end, a bench vise
at the front right, and the drawing is clipped to an inclined drafting board at the back.
Underneath are two sample drawers, three lidded bins, a wire coil and a plate. The
drafting board and the magnifier keep Rust's research cues in our own arrangement. No
Rust pixels, models or other game files enter the repository.

## Asset contract

- Repeatable source: `art/models/research_table.py`, with the unchanged `art/hollow/component_mesh.py`,
  `art/hollow/rounded.py` and `art/hollow/diagram_texture.py`. Build via `art/build.py -- research_table`,
  then its gibs via `art/gibs/build.py -- research-table`.
- 1.465 × 0.782 × 0.892 metres (Blender XYZ), inside the deployable's half extents
  0.75 × 0.45 (up) × 0.40 m in `deployables.json`, which place it, collide with it and
  destroy it on the server. The engineering-workbench stand-in was larger than those
  bounds; the drawn bench now agrees with them. A ModelCatalogueTests check reads the
  GLB's bounds against them.
- Faces Blender -Y (Godot +Z) with its footprint centred on the origin at ground level;
  `socket_use` at the front of the work surface; one convex hull (56 triangles).
- Three LODs: 14,412 / 6,588 / 2,788 triangles, within 16,000 / 7,500 / 3,200 budgets,
  each built from the same parts at fewer segments rather than decimated.
- One material and one external 2048 px albedo/normal/ORM atlas: chipped paint, bare
  steel, timber, the cutting mat, the drawing, bin plastic, rubber, brass, the lens and
  rusty scrap. An eight-sample vertex occlusion pass grounds the parts.
- Gibs: `game/assets/gibs/research-table.glb`, six pieces (2,274 triangles) fractured
  from LOD2 with the bench's own material, `metal` as before.
- Production IconRenderer makes 128/256 px RGBA icons, using view `[-30, 30, 0]`.

## Validation and limits

Native screenshots and executed checks are recorded in
`docs/images/item-art-research-table-20261019/validation.json`; source and asset hashes
are in `game/assets/world/research-table.provenance.json`.

Review uses the production ModelViewer, IconRenderer and CatalogItemIcons from this
worktree's compiled game assemblies in a small isolated project. It covers all three
LODs, the back, a close view of the top, and diagnostic icon slots at 16–256 px with no
SVG fallback, beside the previous SVG. Both icons are RGBA with full transparency and
unclipped bounds; the GLB has finite attributes, unit normals and external textures.

Placement on foundations, reach, pickup, storage and research run on the unchanged
bounds; the World tests (79/79) cover them, the GibTests (5/5) the new gibs. The bench
was not placed in a live hosted game for this change, and its gibs were not captured
breaking. The solution build passed with zero warnings and errors; ModelCatalogue
(13/13) and ContentTests (3/3) passed. Artistic acceptance remains with the owner.
