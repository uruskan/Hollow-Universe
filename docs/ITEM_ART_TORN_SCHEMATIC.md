# Torn Schematic — original asset R01

The `torn-schematic` component receives its own paper model and rendered icons,
replacing the building-plan model and SVG. ID 163, stack 20, description and
workbench requirements stay unchanged.

## Reference and direction

Inspected the [official Rust Basic Blueprint Fragment page](https://wiki.facepunch.com/rust/item/basicblueprintfragment)
and [item image](https://files.facepunch.com/rust/item/basicblueprintfragment_512.png)
before authoring. It is a blue graph-paper fragment with a large missing corner,
jagged tears and fine cyan grid. Those features guide the silhouette and palette.

Our outline, curl, folds and diagrams are original. The fragment carries part of
an invented service drawing: a housing with circular insert and connectors. It
has no real manufacturing dimensions. No reference game images or meshes are
stored in the repository; the browser tab closes after inspection.

## Asset contract

- Repeatable source: `art/models/torn_schematic.py`, with original diagram marks
  from `art/hollow/diagram_texture.py`. Build via `art/build.py -- torn_schematic`.
- Approximately 0.236 × 0.012 × 0.278 metres in Blender XYZ. Thin opaque paper,
  gently curled surface, shared pickup pivot and simplified convex collision.
- Triangle budgets 4,000 / 1,500 / 400; actual counts 2,880 / 828 / 140, with a
  34-triangle convex hull. Exact bounds are in the provenance geometry record.
- One material and a 1024px original albedo/normal/ORM atlas with separate front
  and back regions. No alpha-cutout silhouette or double-sided rendering shortcut.
- Production IconRenderer creates 128/256px PNGs with view `[-18, 8, -5]`.

## Validation and limits

`docs/images/item-art-torn-schematic-20261019/validation.json` records the executed
checks and native Godot captures. `game/assets/items/torn-schematic.provenance.json`
records origin and hashes. Review uses the existing compiled ModelViewer,
IconRenderer and CatalogItemIcons in a small isolated project, showing 32–256px
icons without SVG fallback. All LODs and the reverse side receive captures.

Focused builds and catalogue/content tests check identity and dependencies; mesh
checks inspect attributes/normals and PNG checks inspect transparency and bounds.
The full solution build passed with zero warnings/errors, and all 11 focused
catalogue/content tests passed. The broader isolated suite did not start because
free RAM fell to about 2 GiB, below its 3.5 GiB launch gate. Live inventory remains
unchecked.
Diagnostic slot images are not full gameplay acceptance.

The first build found Blender 5.2's tessellator returning triangle indices instead
of point vectors. The source now handles both formats, and the successful rebuild
uses `--python-exit-code 1` so script errors cannot masquerade as a successful job.
The captured viewer uses zoom 1.2 to keep the paper below its diagnostic header.

CPU/RAM gates keep the serial two-core jobs from starting during memory pressure.
Only owned children and tabs are closed. Other sessions' edits and staging are
preserved. Artistic acceptance remains with the owner.
