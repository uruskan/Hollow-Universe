# Blueprint — original asset R01

The `blueprint` item receives its own model and rendered icons, replacing the
building-plan model and the SVG drawing. ID 170, name, description and every research
and learning binding stay unchanged.

This is the generic picture of a blueprint, for the model catalogue and item details.
A stack that teaches an item still draws that item's icon on blueprint blue
(`GroundUi.Paint.cs`), as the replacement plan requires; this asset does not replace
those taught-item icons.

## Reference and direction

Inspected the [official Rust Blueprint page](https://wiki.facepunch.com/rust/item/blueprintbase)
and [item image](https://files.facepunch.com/rust/item/blueprintbase_512.png) before authoring.
The reference is a flat-shaded blue sheet folded in three zig-zag panels, with pale
outline and plan lines; it reads at small size by the blue and the folded outline.

Ours is a modelled cyanotype sheet, 0.30 × 0.21 m unfolded and 0.6 mm thick, folded
in three uneven zig-zag panels with rounded folds, a gentle sag and one lifted corner.
Its original drawing shows three views of an invented pump housing (front, side and
plan with an exploded bolt), dimension lines, callouts, a construction grid and a
ruled title block with no lettering. Creases and edges are rubbed paler. The fold and
the deep blue keep it distinct from the flat, sleeved Sealed Schematic and the torn
sheet. No Rust pixels, models or other game files enter the repository.

## Asset contract

- Repeatable source: `art/models/blueprint.py`, with the unchanged
  `art/hollow/component_mesh.py` and `art/hollow/diagram_texture.py`.
  Build via `art/build.py -- blueprint`.
- Approximately 0.273 × 0.210 × 0.048 metres in Blender XYZ. Bottom pickup pivot at
  the sheet's centre, convex collision (44 triangles), no held sockets or animations.
- Three LODs: 2,232 / 696 / 236 triangles, within 3,000 / 1,000 / 400 budgets. Each is
  one closed sheet (print, back and cut edges) with samples gathered at the folds.
- One material and one external 1024 px albedo/normal/ORM atlas: the printed face,
  the paler back and the edges. An eight-sample vertex occlusion pass darkens the
  inside of the folds.
- Production IconRenderer makes 128/256 px RGBA icons, using view `[18, 42, 0]`.
  The catalogue uses this asset directly, with no icon alias or stand-in.

## Validation and limits

Native screenshots and executed checks are recorded in
`docs/images/item-art-blueprint-20261019/validation.json`; source and asset hashes
are in `game/assets/items/blueprint.provenance.json`.

Review uses the production ModelViewer, IconRenderer and CatalogItemIcons from this
worktree's compiled game assemblies in a small isolated project. It covers all three
LODs, the back, and diagnostic icon slots at 16–256 px with no SVG fallback, beside the
previous SVG. Both icons are RGBA with full transparency and unclipped bounds; the GLB
has finite attributes, unit normals and external textures. The taught-item blueprint
slots in a live inventory were not captured; their code path is unchanged.

The solution build passed with zero warnings and errors, and the ModelCatalogue and
ContentTests filters passed. The broader suite was not rerun for this asset-only
change. Artistic acceptance remains with the owner.
