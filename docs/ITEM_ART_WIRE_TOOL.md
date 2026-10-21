# Wire Tool — review and original asset R01

The replacement plan asked for the Wire Tool's model and icons to be reviewed and
improved only where needed. ID 238, recipe, wiring rules and its held presentation
stay unchanged.

## Review

The previous asset was Codex's primitive probe: a box grip, a box head, a flat cyan
screen slab and a cone, baked by `WireToolAssetBuilder` from `wire-tool-source.tscn`
into `wire-tool.tscn` with one level of detail and three flat materials. Codex's own
record called it functional foundation art, not a polished tool. At icon size it read
as a pale block with a stick. Verdict: replace the model and re-render the icons.

## Reference and direction

Inspected the [official Rust Wire Tool page](https://wiki.facepunch.com/rust/item/wiretool)
and [item image](https://files.facepunch.com/rust/item/wiretool_512.png). The reference is a
glossy black power-connector plug with a moulded strain relief and a long cable looping
away; it reads by the dark plug against the thin cable.

Ours keeps the existing tool's identity, a handheld wiring probe, and borrows only the
idea of wire on the tool. It has a charcoal rubber grip with finger grooves and a
lanyard loop, and a scuffed safety-yellow housing with a black hazard band and rubber
bumper. On the front is a lit screen (a trace, a grid and a reading bar, no lettering)
in a bezel, with two buttons below it. On the right side is a wire reel with copper
winding, and a red lead runs from it to the steel nozzle, brass collar and probe. A
belt clip sits on the back. No Rust pixels, models or other game files enter the
repository.

## Asset contract

- Repeatable source: `art/models/wire_tool.py`, with the unchanged `art/hollow/component_mesh.py`
  and `art/hollow/rounded.py`. Build via `art/build.py -- wire_tool`.
- The old frame, so `held-presentations.json` still fits: the length up Godot +Y, the
  screen toward Godot +Z, `socket_grip_r` at the origin and `socket_wire_tip`, the
  presentation's contact socket, at the point 0.275 m up. A ModelCatalogueTests check
  reads both sockets from the GLB.
- Approximately 0.123 × 0.066 × 0.381 metres in Blender XYZ; the reel widens the
  right side to 0.073 m (the old head reached 0.055 m). Convex collision (48 triangles).
- Three LODs: 8,088 / 4,148 / 1,928 triangles, within 9,000 / 4,500 / 2,200 budgets.
- One material and one external 1024 px albedo/normal/ORM atlas, plus an emission map
  that lights only the screen's marks.
- Production IconRenderer makes 128/256 px RGBA icons, using view `[-35, 20, -30]`.
- Removed with the old asset: `wire-tool.tscn`, `wire-tool-source.tscn`, the three
  `items/materials/wire-tool-*.tres`, `WireToolAssetBuilder` and Main's `--build-wire-tool`.

## Validation and limits

Native screenshots and executed checks are recorded in
`docs/images/item-art-wire-tool-20261019/validation.json`; source and asset hashes
are in `game/assets/items/wire-tool.provenance.json`.

Review uses the production ModelViewer, IconRenderer and CatalogItemIcons from this
worktree's compiled game assemblies in a small isolated project. It covers all three
LODs, the back, and diagnostic icon slots at 16–256 px with no SVG fallback, beside the
previous icon. Both icons are RGBA with full transparency and unclipped bounds; the GLB
has finite attributes, unit normals and external textures.

The first- and third-person held views were not captured in the game for this change;
the held presentation is unchanged and the sockets are where it expects them. The
solution build passed with zero warnings and errors; ModelCatalogue (14/14),
ContentTests (3/3), HeldPresentation (4/4) and Wiring (9/9) passed. Artistic acceptance
remains with the owner.
