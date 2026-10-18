# Sealed Schematic — original asset R01

The `sealed-schematic` component receives its own sleeved drawing model and
rendered icons, replacing the building-plan model and SVG. ID 164, stack 20,
description, loot and workbench requirements remain unchanged. It is a crafting
component, separate from the learnable `blueprint` item.

## Reference and direction

Inspected the [official Rust Advanced Blueprint Fragment page](https://wiki.facepunch.com/rust/item/advancedblueprintfragment)
and [item image](https://files.facepunch.com/rust/item/advancedblueprintfragment_512.png)
before authoring. The reference shows a nearly complete blue gridded sheet,
irregular edges and broad curls. Blue paper, fine grid and the complete silhouette
guide this asset. The protective sealed sleeve follows our existing item text.

Our outline, curl, rim, scuffs and service drawing are original. The front depicts
an invented finned cassette with an inset view, registration bars and drawing
table; there are no functional manufacturing dimensions. The reverse has sparse
archival marks. No Rust pixels, meshes or other game files enter the repository.

## Asset contract

- Repeatable source: `art/models/sealed_schematic.py`, using the existing original
  `art/hollow/diagram_texture.py` helper. Build via `art/build.py -- sealed_schematic`.
- Approximately 0.242 × 0.023 × 0.302 metres in Blender XYZ; thickness includes
  the curl. Sleeve wall thickness is 1.2 mm. Shared bottom pickup pivot and convex
  collision, with no held sockets or animations.
- LOD triangle counts: 2,432 / 656 / 188; convex hull: 34 triangles.
- One opaque material, one external 1024px albedo/normal/ORM atlas, separate front
  and reverse regions. Surface roughness and a sealed rim suggest laminate without
  overlapping transparent surfaces or alpha sorting.
- Production IconRenderer creates 128/256px RGBA icons with view `[-18, 8, -5]`.
  The catalogue uses this item's own model and icons, with no alias or stand-in.

## Validation

Executed checks and screenshots are recorded in
`docs/images/item-art-sealed-schematic-20261019/validation.json`; origin and asset
hashes are in `game/assets/items/sealed-schematic.provenance.json`.

Native review uses the production ModelViewer, IconRenderer and CatalogItemIcons
from existing compiled game assemblies in an isolated project. It checks all
three LODs, the back and diagnostic slots at 32–256px with no SVG fallback. These
slot captures do not establish full live gameplay inventory acceptance.

The full solution build passed with zero warnings/errors, and all 12/12 focused
catalogue/content checks passed. Broader suite: 979/985 checks passed across all 170 classes. Six failures
in shared SelectedPhysicsGroundTests concern terrain-pin ownership, native
resource/completion expectations and a tile allowance. They do not load item art
and lie outside this change; they are recorded as current failures, not a blanket
suite pass. No unrelated physics code was changed.

Jobs run serially, below normal priority and limited to two CPU cores, behind
CPU/RAM gates. Only this task's children and reference tab are closed. Artistic
acceptance remains with the owner.
