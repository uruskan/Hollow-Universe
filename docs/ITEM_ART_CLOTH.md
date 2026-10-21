# Cloth Item Art

First replacement from [the item art plan](ITEM_ART_REPLACEMENT_PLAN.md).
The owner requested one item at a time with low machine load and cleanup of owned
processes. Cloth is a loose folded rag of rough woven plant fibre, using its existing
`cloth` item key and inventory behavior.

## Reference and direction

The owner confirmed Rust as the main inspiration and requested checking the actual
item image on the web before authoring. On 18 October 2026 we inspected
[Facepunch's cloth entry](https://wiki.facepunch.com/rust/item/cloth) and its
[official item image](https://files.facepunch.com/rust/item/cloth_512.png).
The visual cues are broad draping folds, an uneven frayed outline, warm natural
fabric and localized wear. Our first neat four-panel stack was revised into a looser
folded piece with modeled edge notches and larger wrinkles in response.

Those links are reference only. The mesh, procedural weave, material maps and final
rendered icons are authored locally; no Rust asset files enter the project.

## Specification

- Original authored folded-sheet geometry, approximately 31 × 30 × 6.5 cm.
- Natural muted flax colour, soft folds, visible fabric thickness, narrow selvedges
  and sparse frayed threads. No new wrapping, label, recipe or equipment function.
- Three authored detail levels, one material surface per level and a simple convex
  pickup envelope. No held-item sockets are required by the current catalogue.
- Closest/medium/distant detail: 5,916 / 2,356 / 968 triangles, within the
  7,000 / 3,000 / 1,100 budgets; 54-triangle convex proxy. The detail levels
  preserve the folds, with less edge detail at distance. Simplify the sheet
  before adding thickness to avoid underside artifacts in the distant mesh.
- One original 512 px tiling weave set: colour, OpenGL normal and packed ORM, with
  neutral ambient occlusion and no metallic response. Four centimetre texture repeat.
- Dedicated transparent 128/256 px icons from the selected Godot model.

## Production

`art/models/cloth.py` authors the geometry directly, avoiding a cloth simulation,
texture baking or large source downloads. Run Blender in background mode with two
threads through the shared asset-job mutex, at below-normal process priority.
Wait for acceptable CPU and available memory before starting each job. Keep jobs
serial, use timeouts, and close only child processes owned by this task.

The script writes cloth-specific resources and a reproducible editable Blender
snapshot plus geometry report under `.artifacts/cloth-r01/`. Review uses a separate
small Godot project containing only this item's resources. No world, server or user
save needs to be loaded for this asset review.

## Status

Implemented on 18 October 2026. The catalogue selects `assets/items/cloth.glb` and
cloth's own transparent 128/256 px PNGs; its fibre stand-in annotation is removed.
The existing item identity, recipes, quantities, inventory UI and lootbag behavior
are unchanged. Artistic acceptance remains with the owner.

Validation completed:

- Blender export and finite geometry/unit-normal checks passed; GLB has all three
  detail levels, the convex proxy, one external material and no embedded images.
- Godot 4.7.2 imported the material, textures and model. Native model captures at
  LOD0 and LOD2 and the production `IconRenderer` run completed without engine errors.
  LOD1 was structurally checked and imported, not separately captured after the final
  reference revision.
- Both `ModelCatalogueTests` passed: cloth's dedicated identity plus 163 catalogue
  models, 171 model files and 252 icons. Tests ran from an isolated copy of a newer
  existing build that included the cloth regression test.
- The small isolated C# review project built with zero warnings/errors. No full
  solution rebuild or full test-suite run was launched by this task, respecting the
  owner's request to keep load low while other sessions work.
- Both PNG sizes have transparent, unclipped edges. The size comparison checks
  32/48/64/96/128 px icons and a 256 px detail view. It composes source image files
  with Pillow and is explicitly **not** a live inventory screenshot.

The live inventory/provider check is **deferred**, not passed. Two final icon-import
attempts were stopped by the memory guard; later idle waiters were closed before
launch. Available system memory repeatedly fell below 1 GB. The runtime review used
only this item's resources and snapshotted production renderer assemblies; no world,
server, player save or full inventory session was loaded. Recheck the live inventory
at representative UI scales when machine capacity is available.

Evidence and provenance:

- [Model close view](images/item-art-cloth-20261018/cloth-lod0.png) and
  [distant mesh enlarged for inspection](images/item-art-cloth-20261018/cloth-lod2.png).
- [Before/after icon sizes](images/item-art-cloth-20261018/icon-sizes.png).
- [Validation receipts and input hashes](images/item-art-cloth-20261018/validation.json).
- [Installed asset hashes and reference notes](../game/assets/items/cloth.provenance.json).

The editable Blender snapshot remains at `.artifacts/cloth-r01/cloth-source.blend`;
the committed Python authoring source reproduces it. Review caches and duplicate
runtime files are disposable; retain the source snapshot, screenshots and logs.
All owned processes and the reference browser tab are closed. Automatic approval
review blocked recursive deletion of the temporary review caches, so those files
remain locally under `.artifacts/cloth-r01/`.
