# Clothing items — the worn garments, R01

Six clothing items are now the garments players wear, so the inventory icon and the dropped item
show exactly what a character puts on. They replace the stand-in models of `art/models/clothing.py`
and the icons rendered from them, which looked nothing like the worn garments (a flat brown shirt
where the worn one is cream, rust-stained plating where the worn breastplate is bright steel).
Item IDs, stacks, wear slots, protection and recipes are unchanged.

| Item | Garment in `clothing.glb` | LOD triangles | Hull |
|---|---|---|---|
| `woven-hood` | `woven_hood` | 8,374 / 3,517 / 1,339 | 42 |
| `woven-shirt` | `woven_shirt` | 5,906 / 2,479 / 944 | 42 |
| `woven-trousers` | `woven_trousers` | 3,230 / 1,354 / 515 | 47 |
| `woven-gloves` | `woven_gloves` | 6,844 / 2,872 / 1,092 | 42 |
| `work-boots` | `laced_boots` | 4,768 / 2,002 / 762 | 54 |
| `chest-plate` | `riveted_breastplate` | 9,576 / 4,020 / 1,512 | 44 |

## How they are made

- Repeatable source: `art/models/garment_items.py` (`art/build.py -- garment_items`). It reads the
  character package's `game/assets/characters/clothing.glb` (built by
  `art/characters/build_clothing.py`) and copies each garment's three LOD meshes, unskinned, in their
  bind pose. Nothing is decimated or re-modelled; the item's LODs are the garment's.
- The male garments are used: `clothing.glb` is the default body's, and the female garments are fitted
  versions of the same pieces.
- Pose: upright with the front toward −Y (Godot +Z), as the clothing items have always lain. The gloves,
  which hang fingers-down at the A-pose's hands, are turned fingers-up with their backs to the front,
  each leaning 12° outward, side by side. The boots are brought together to a 4 cm gap. Bottom pickup
  pivot and a convex hull of at most about 50 triangles.
- Material: each garment's own, as `CharacterMaterials.Garment` builds it for the worn piece: the
  character cloth shader (`game/shaders/character_cloth.gdshader`, drawn from both sides, the inside
  darker) with the garment's albedo, normal and ORM textures and its tiled weave, knit, twill, leather
  or metal detail. Written as one shared `game/assets/materials/garment-<garment>.tres` per garment,
  using the textures in `game/assets/characters/textures/` rather than copies.
- Icons: the production IconRenderer at 128 and 256 px with the catalogue's existing views (the boots
  at `[65, 22, 0]`, the rest front-on).

`art/models/clothing.py` now builds only the metal mask's item, whose model is its own; it rebuilt
byte-identical after the stand-ins were removed.

## Validation and limits

Per item, `docs/images/item-art-<item>-20261020/` holds native captures of LOD0–2, the back and the
icon slots at 16–256 px beside the stand-in icon, and `validation.json`; source and asset hashes are
in `game/assets/items/<item>.provenance.json`. The review uses the production ModelViewer, IconRenderer
and CatalogItemIcons from this worktree's compiled game in an isolated project. Every capture was
taken from resources whose hashes match the committed files; a rebuild of each item reproduced its
GLB byte for byte. Each GLB has finite attributes, unit normals and external textures; each icon is
RGBA with full transparency and unclipped bounds.

`ModelCatalogueTests.ClothingItemsAreTheWornGarments` holds each item to its garment's material and
textures and keeps it off the stand-in list. The solution build passed with zero warnings and errors,
and ModelCatalogue (15/15) and ContentTests (3/3) passed; the broader suite was not rerun for this
asset-only change. Slots are diagnostic, not live gameplay inventory.

The gloves are close-fitting knit, so at small icon sizes they read almost as bare hands; that is the
worn garment's own look. Artistic acceptance remains with the owner.
