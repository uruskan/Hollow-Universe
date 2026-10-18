# Gun Receiver — original asset R01

The `receiver` component now uses original geometry and rendered PNG icons instead
of the refined-metal ingots and flat SVG. Its ID 161, stack 10, recipes, loot and
recycling values remain unchanged. The description follows the visible construction.

## Reference and direction

Inspected the [official Rust Semi Automatic Body page](https://wiki.facepunch.com/rust/item/semibody)
and its [512px item image](https://files.facepunch.com/rust/item/semibody_512.png)
before authoring. The reference reads as a long rusty housing with pale timber
alongside it, dark metal clips and tan bindings. These large material blocks and the
diagonal inventory pose matter more than tiny mechanism details.

Our version follows that construction and palette with a separately authored
profile, brace ends, clips, bindings and surface wear. It is cosmetic fictional
game geometry with no functional mechanism. No reference image, mesh or texture
was downloaded into the project. The reference tab was closed after inspection.

## Asset contract

- Source: `art/models/receiver.py`, with `art/hollow/component_mesh.py` for small
  atlas-mapped presentation parts. Build through `art/build.py -- receiver`.
- Bounds: approximately 0.367 × 0.089 × 0.111 metres in Blender XYZ. Bottom-centre
  pickup convention; all three LODs share one pivot. No held pose or weapon sockets.
- Budget: at most 9,000 / 5,000 / 2,500 triangles. R01 has 4,704 / 2,200 / 984,
  plus a 46-triangle convex collision hull.
- One material and one original 1024px atlas with albedo, OpenGL normal and ORM
  maps. Wood grain, cracks, oxide, bare-metal scratches and cord fibres are generated
  deterministically. Texture dependencies remain external to the GLB.
- Production IconRenderer supplies 128px and 256px transparent PNGs using
  catalogue view `[-12, 16, -34]`. CatalogItemIcons handles smaller slots.

## Validation and limits

Native Godot captures, checks and final file hashes are recorded in
`docs/images/item-art-receiver-20261018/validation.json` and
`game/assets/items/receiver.provenance.json`. The isolated review uses the existing
compiled production ModelViewer, IconRenderer and CatalogItemIcons. Its display
compares the old SVG with new PNGs at 32–256px without a fallback provider.

The focused catalogue/content build and tests cover model identity, external
materials, LODs, collision, icons and data references. Geometry checks inspect
finite attributes and normals; PNG checks inspect alpha and clipping. Screenshots
cover front, back and all LODs. The full solution, gameplay suite and live inventory
are deferred to keep resource use low; this is not a full-game acceptance claim.

Jobs run serially, below normal priority on two CPU cores, after CPU/RAM gates.
Only owned child processes and browser tabs are closed. Other sessions' staging
and edits are preserved. Artistic acceptance remains with the owner.
