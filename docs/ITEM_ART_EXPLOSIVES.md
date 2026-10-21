# Explosives — original asset R01

The `explosives` resource receives its own model and rendered icons, replacing the
sulphur model and the sulphur icon alias. ID 120, stack 100, description, recipe and
every raid recipe that uses it stay unchanged.

## Reference and direction

Inspected the [official Rust Explosives page](https://wiki.facepunch.com/rust/item/explosives)
and [item image](https://files.facepunch.com/rust/item/explosives_512.png) before authoring.
The reference shows two off-white, slightly lumpy putty blocks, one leaning on the
other, each with a tan paper band and a twine tie, and no lettering. At small size it
reads by the pale blocks and the dark band across them.

Ours is a fictional industrial compound, packaged differently: three dense grey-white
blocks, two bound side by side in one kraft band printed with a red-orange ring and
black chevrons, the third laid across them in a plain band; each band is tied with
twine and a small bow. The pale compound and the printed band keep it distinct from
the yellow sulphur and the dark gunpowder. No Rust pixels, models or other game files
enter the repository; only reference links are kept.

## Asset contract

- Repeatable source: `art/models/explosives.py`, with the unchanged `art/hollow/component_mesh.py`
  and the new `art/hollow/rounded.py` (rounded boxes and thin sleeves round them).
  Build via `art/build.py -- explosives`.
- Approximately 0.189 × 0.178 × 0.111 metres in Blender XYZ. Bottom pickup pivot,
  convex collision (46 triangles), no held sockets or animations.
- Three LODs: 7,272 / 3,616 / 1,740 triangles, within 8,000 / 4,500 / 2,500 budgets,
  each built from the same shapes at fewer segments rather than decimated.
- One material and one external 1024 px albedo/normal/ORM atlas: compound with pores
  and faint grime, fibrous kraft paper, the printed band and twisted twine. An
  eight-sample vertex occlusion pass separates the blocks, bands and twine.
- Production IconRenderer makes 128/256 px RGBA icons, using view `[28, 34, 0]`.
  The catalogue uses this asset directly, with no icon alias or stand-in.

## Validation and limits

Native screenshots and executed checks are recorded in
`docs/images/item-art-explosives-20261019/validation.json`; source and asset hashes
are in `game/assets/items/explosives.provenance.json`.

Review uses the production ModelViewer, IconRenderer and CatalogItemIcons from this
worktree's compiled game assemblies in a small isolated project. It covers all three
LODs, the back, and diagnostic icon slots at 16–256 px with no SVG fallback, beside the
sulphur icon the item used to borrow. Both icons are RGBA with full transparency and
unclipped bounds; the GLB has finite attributes, unit normals and external textures.

The loose block's band and twine pass about 2 mm into the top of the pair where the
block rests; this is hidden in every capture. The solution build passed with zero
warnings and errors, and the ModelCatalogue (11/11) and ContentTests (3/3) filters
passed. The broader suite was not rerun for this asset-only change. Artistic
acceptance remains with the owner.
