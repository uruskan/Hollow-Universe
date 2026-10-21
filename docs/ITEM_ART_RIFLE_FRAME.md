# Rifle Frame — original asset R01

`rifle-frame` replaces its refined-metal model and SVG icon with a dedicated
original cosmetic component. ID 162, stack 10, recipe, loot and recycler records
are unchanged. The description now matches its timber-braced housing.

## Reference and direction

Inspected the [official Rust Rifle Body page](https://wiki.facepunch.com/rust/item/riflebody)
and [item image](https://files.facepunch.com/rust/item/riflebody_512.png) before
authoring. Its identifying features are a long dark steel body, short amber wood
section, dark raised ribs and small fasteners, posed diagonally. The compact shape
does not include a full rifle stock or barrel, so the old stock description was
corrected to match the requested reference direction.

Our original layout uses a bevelled stamped housing, raised side panel, top ridge,
amber side braces, ribbed cover plates and shallow cosmetic markings. The shape
and palette distinguish it from Gun Receiver's rusty tube and pale timber. There
is no functional mechanism. No reference game files or pixels entered the project.

## Asset contract

- Source: `art/models/rifle_frame.py`; uses existing `hollow.component_mesh` helpers.
  Build through `art/build.py -- rifle_frame`.
- Approximately 0.378 × 0.086 × 0.095 metres in Blender XYZ, shared pickup pivot.
  Resource-class component, no held pose or weapon sockets.
- Budget 9,000 / 5,000 / 2,500 triangles; actual 5,720 / 2,624 / 996 with a
  48-triangle convex collision hull.
- One external material, one 1024px original procedural albedo/normal/ORM atlas.
  Restrained scratches and warm grain preserve the major steel/wood colour blocks.
- Own 128/256px PNG icons from production IconRenderer, view `[-12, 18, -38]`.

## Validation and limits

Evidence is under `docs/images/item-art-rifle-frame-20261018/`; hashes and origin
are in `game/assets/items/rifle-frame.provenance.json`. The isolated Godot review
uses existing compiled ModelViewer, IconRenderer and CatalogItemIcons. It compares
the SVG with native rendered PNGs at 32–256px without an SVG fallback provider.

Focused review/test builds, catalogue/content checks, mesh attributes/normals,
RGBA icon bounds, all LOD captures and a back view are recorded in validation.json.
The full solution, gameplay suite and live inventory are deferred to keep load low.
These diagnostic slot captures do not claim full-game acceptance.

CPU/RAM-gated jobs run serially below normal priority on two cores. Owned children
exit after each task and the reference tab closes after inspection. Other sessions'
edits and staging remain intact. Artistic acceptance remains with the owner.
