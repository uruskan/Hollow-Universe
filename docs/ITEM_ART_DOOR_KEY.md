# Door key — original asset R01

The `door-key` item (the key a key lock makes, its condition holding the lock's number) receives a
model and rendered icons of its own, replacing the key lock's model and icon, which it borrowed. Its
ID, stack, description and behaviour are unchanged.

## Direction

A cut steel key as a scrap-age locksmith would hand it over: a round bow, a shoulder, a grooved blade
with three teeth of different depths, on a split ring with a stamped brass tag. It reads as a key at a
glance and cannot be mistaken for the key lock beside it in an inventory. Our own design; no reference
image or other game's file was used.

## Asset contract

- Repeatable source: `door_key` in `art/models/supplies.py` (`art/build.py -- supplies`, or
  `blender -b --factory-startup --python art/models/supplies.py -- door-key`), with the shared
  `hollow.build.Model`.
- About 0.108 × 0.030 × 0.006 metres in Blender XYZ, lying flat with the blade along +X. Bottom pickup
  pivot, a 116-triangle convex hull, no held sockets.
- Three LODs: 1,544 / 772 / 334 triangles, within 2,500 / 1,200 / 600 budgets.
- One material: the shared surface atlas (`game/assets/materials/psg_surface.tres`), as the key lock.
- Production IconRenderer makes 128/256 px RGBA icons, using view `[30, 60, 0]`.

## Validation and limits

`docs/images/item-art-door-key-20261020/` holds native captures of LOD0–2, the back and the icon slots
at 16–256 px beside the key-lock icon it used to borrow, and `validation.json`; hashes are in
`game/assets/items/door-key.provenance.json`. Every capture was taken from resources whose hashes match
the committed files, and a rebuild reproduced the GLB byte for byte. The GLB has finite attributes, unit
normals and external textures; both icons are RGBA with full transparency and unclipped bounds.

`ModelCatalogueTests.DoorKeyHasItsOwnModelAndRenderedIcon` keeps the key on its own model and icon. The
solution build passed with zero warnings and errors, and ModelCatalogue (16/16) and ContentTests (3/3)
passed; the broader suite was not rerun for this asset-only change. The key is slim, so at 32 px it is
small in its slot. Slots are diagnostic, not live gameplay inventory. Artistic acceptance remains with
the owner.
