# Door items — art from the corrected door kit (D4)

The four door items `wooden-door`, `armoured-door`, `double-door` and `garage-door` stop being stand-ins: each is now the
door the building draws, made from the corrected kit doors of [DOORS.md](DOORS.md) §2 (D1: symmetric leaves lying inside a
frame centred in the 0.3 m wall), with matching inventory icons. Item IDs, keys, names, recipes, placement and the doors'
building data are unchanged. This is the D4 package of [DOORS.md](DOORS.md) §9, following the pattern of
[ITEM_ART_SALVAGE.md](ITEM_ART_SALVAGE.md) as far as it applies to a placed item (no held pose, no new textures).

## What each item is

| Item | Kit file | Parts |
|---|---|---|
| `wooden-door` | `doors_wood.glb` | `Wood_SingleDoor_Frame` and `Wood_SingleDoor_Left`, shut |
| `armoured-door` | `doors_armored.glb` | `Armored_SingleDoor_Frame` and `Armored_SingleDoor_Left`, shut |
| `double-door` | `doors_metal.glb` | `Metal_DoubleDoor_Frame`, `_Left` and `_Right`, shut |
| `garage-door` | `garage_door.glb` | `GarageDoor_Frame` and the curtain in its rest pose, which is shut |

`double-door` is the *sheet metal* double door (`items.json`: its name and its `door` key `sheet-metal-double-door`; the recipe
is metal fragments), so it takes the kit's Metal grade, not Wood. The wooden and armoured double doors are separate items
(`wooden-double-door`, `armoured-double-door`) with their own models and are not part of this package. The garage curtain is
skinned in the kit; its rest pose equals the end of the kit's Close clip (largest difference 0.7 mm, measured), and the
item's curtain is that pose as a plain mesh.

## Asset contract

- Repeatable source: `art/models/doors.py`, run by `art/build.py -- doors`. It imports the committed kit GLBs with Blender's
  glTF importer, copies the kit's frame and leaf meshes (and the garage's curtain) into the item's frame, and exports
  `game/assets/world/<key>.glb` with the Godot `.import` beside it. No geometry is authored; nothing from outside the kit
  enters, and a rebuild writes byte-identical files (checked by hashes). The earlier stand-in builders were removed from
  `art/models/deployables.py`, whose build would otherwise have overwritten these files.
- Frame and leaves are placed as the building places them: each leaf at its `building.json` hinge in the frame, the wall's
  middle plane at z = 0, the outside on +Z. Pivots are the old models': a single or double door hangs from its lower left hinge
  corner at the origin and closes across +X; the garage door's origin is the middle of its bottom edge. Sizes (Godot axes, from
  the model viewer): 1.21 × 2.30 × 0.29 m (single doors), 2.71 × 2.85 × 0.29 m (double), 2.72 × 2.85 × 0.52 m (garage; its housing
  is wider than the wall on purpose).
- Nodes: `Body_lod0..2` (the frame), the moving part under an empty at its pivot (`Leaf`, `Left` and `Right`, `Curtain`) with
  `<Part>_lod0..2`, one convex hull `Body_hull0-convcolonly` around the whole door, and sockets `socket_lock` (where
  `Doors.LockOffset` puts the lock on the first leaf: 0.86 of the leaf's width from the hinge, 1.05 m up, on the leaf's outer
  face), `socket_use` (in front, 1.1 m up) and, for the single doors, `socket_hinge`.
- Materials: the building kit's own, `Building_wood`, `Building_armoured`, `Building_metal` and `Door_garage`, mapped by the
  `.import` to `game/assets/materials/building_*.tres` and `door_garage.tres`. The GLBs carry no images and add no textures.
- Triangles per level, measured on the exported GLBs (budgets in brackets):

| Item | LOD0 | LOD1 | LOD2 | Hull |
|---|---|---|---|---|
| `wooden-door` | 16,000 (16,000) | 4,888 (6,000) | 1,000 (1,500) | 32 |
| `armoured-door` | 16,000 (16,000) | 5,112 (6,000) | 888 (1,500) | 32 |
| `double-door` | 23,999 (24,000) | 12,000 (12,500) | 2,096 (3,000) | 30 |
| `garage-door` | 10,232 (16,000) | 4,624 (6,000) | 1,395 (1,500) | 42 |

  The kit's own levels are used as they are: LOD1 and LOD2 are the kit's `_LOD1` and `_LOD2` frames and leaves. The kit's
  full-detail leaves are heavy (wood single 16,312, armoured 16,200, metal double 22,660 and 21,340 triangles), so only LOD0
  reduces them: Blender's collapse decimation takes the leaves to 0.78 (single doors) and 0.47 (the double door) of their
  triangles, leaving the frame as it is. The garage curtain has no levels in the kit (4,292 triangles, 26 slats): LOD0 keeps
  it whole, LOD1 and LOD2 are 0.7 and 0.3 of it. The reduced levels were looked at beside the kit's own in Workbench
  renders with the real textures; no seams, holes or shading breaks show at icon or model-viewer size.
- Icons: 128 and 256 px RGBA from the production `IconRenderer` (`--view icons --out game/assets/items/icons --only
  wooden-door,armoured-door,double-door,garage-door`), all four at view `[28, 12, 0]` (the door class's default, now written
  in `models.json`): from the right and a little above, so the left jamb's hinge straps and the handle plate both show.
  `models.json` loses the four `standIn` notes and names the kit as `source`.

## Validation and limits

Run in this worktree: `dotnet build HollowUniverse.sln` 0 warnings, 0 errors; `ContentTests` 3/3, `Model` 18/18 (the
catalogue check reads the new GLBs, their `.import` mappings and all 274 icons) and `Icon` 13/13. The four GLBs have finite
positions, unit normals, no images and one material each. The eight PNGs are RGBA with transparent corners; each door fills
0.86 of the square's longer side (the renderer's fill), the single doors being tall and narrow. The model viewer
(`--view models --model <key>`) shows each model with its real materials in the game.

Not done: no live inventory, drop or placement check in a running game (the door items are placed with the kit's own parts,
which this package does not touch), no UI-scale review of the icons at slot size, no full test suite. The kit-subset models of
`wooden-double-door` and `armoured-double-door` (`game/assets/items/fitted/`) were copied from the kit before D1 and still show
its asymmetric leaves; they are outside this package. The garage curtain's three dark bands are the kit's own art. The doors
here are built from the kit as committed with D1; rebuild them whenever the kit changes. Artistic acceptance remains with the
owner.
