# Item Art Replacement Plan

Replace the remaining flat placeholder icons and borrowed item models with distinct,
recognizable assets. Each finished model supplies its own inventory icon so that the
two share the same shape, materials and construction.

**Status, 18 October 2026:** the catalogue audit is complete and the replacement work
is planned. The owner requested this plan and its audit evidence be saved and committed.
Cloth now has its own original model and rendered PNGs; see
[its specification, evidence and validation limits](ITEM_ART_CLOTH.md). Its live
inventory check and owner visual acceptance remain pending. Sewing Kit now has its
own spool-and-needle model and rendered icons; see [its asset record](ITEM_ART_SEWING_KIT.md).
Metal Blade now has a dedicated tapered steel model and rendered icons; see
[its asset record](ITEM_ART_METAL_BLADE.md). Sign Plate now has overlapping salvaged
signs and rendered icons; see [its asset record](ITEM_ART_SIGN_PLATE.md).
Gun Receiver now has its own timber-braced housing and icons; see
[its asset record](ITEM_ART_RECEIVER.md). Rifle Frame now has a dark housing with amber bracing and rendered icons; see
[its asset record](ITEM_ART_RIFLE_FRAME.md). Torn Schematic now has a dedicated ragged paper model and icons; see
[its asset record](ITEM_ART_TORN_SCHEMATIC.md). Sealed Schematic now has a dedicated complete sleeved drawing and icons; see
[its asset record](ITEM_ART_SEALED_SCHEMATIC.md). Salvage now has a dedicated rusty scrap assortment and icons; see
[its asset record](ITEM_ART_SALVAGE.md). Explosives now have a bundled, banded compound model and icons; see
[its asset record](ITEM_ART_EXPLOSIVES.md). Blueprint now has a folded cyanotype sheet and icons, while taught
stacks keep their item-on-blue look; see [its asset record](ITEM_ART_BLUEPRINT.md). The Research Bench now has its own
bench, gibs and icons inside its unchanged placement bounds; see [its asset record](ITEM_ART_RESEARCH_TABLE.md).
The Wire Tool review replaced its primitive probe with a modelled one in the same frame and sockets, and re-rendered
its icons; see [its asset record](ITEM_ART_WIRE_TOOL.md). All thirteen scoped entries now have their own assets;
live in-game review of them remains. Batch 4 (below) covers the clothing items, the door key and the doors.
Artistic acceptance remains with the owner.
[DESIGN.md](DESIGN.md), especially ART-01 and the ground UI requirement, remains authoritative.

## Current assets

The audited working tree contains 137 items: 14 components, 17 resources and 106 items
in other categories. All item keys have model mappings. Six components already use the
revised `Hollow_Salvage_R01` textured scenes and their supplied 128/256 px icons:
**gears, springs, metal pipe, rope, tech scrap and sheet metal**.

Eight components still use flat SVG drawings in the inventory and unrelated models.
The SVGs scale cleanly; their simple artwork is the problem. Increasing icon resolution
alone would preserve the same appearance.

![Current component and salvage icons](images/item-art-audit-20261018/components-current.png)

The initial audit found 123 item entries with dedicated PNGs, two PNG aliases and twelve
fallbacks. Eleven entries lack icon metadata; Wire Tool referenced a missing PNG and
has no dedicated fallback drawing. These counts describe the working tree at the audit,
including concurrent uncommitted features, rather than a release inventory.

During document preparation, concurrent work supplied both Wire Tool PNG sizes. A fresh
diagnostic run now finds 124 dedicated PNG entries, two aliases and eleven fallbacks.
The images preserve the initial audit baseline. Wire Tool's remaining task is to review
its current art and confirm the model/icon match, rather than generate missing files.

## Replacement scope

Thirteen item entries need work. The appearance descriptions below are proposed art
directions grounded in the current item descriptions; detailed geometry and materials
remain subject to visual review.

| Batch | Item key | Current model or icon gap | Proposed appearance |
|---|---|---|---|
| 1 | `cloth` | Replaced: original model and rendered PNGs | A loose folded rag of rough woven plant fibre, with visible thickness, folds and restrained fraying. |
| 1 | `sewing-kit` | Replaced: original model and rendered PNGs | Brown thread on a worn fibre core with a tucked steel needle; replaces the rejected green wooden-spool version. |
| 1 | `metal-blade` | Replaced: original model and rendered PNGs | Broad tapered steel blade, one mounting hole, ground cutting edge and small chips; based on visual reference study. |
| 1 | `sign-plate` | Replaced: original model and rendered PNGs | Overlapping red stop, yellow arrow and warning plates; shallow bends, mounting holes and original localized wear. |
| 1 | `receiver` | Replaced: original model and rendered PNGs | Rusty tubular housing, pale timber braces, dark clips and tan bindings, based on visual reference study. |
| 1 | `rifle-frame` | Replaced: original model and rendered PNGs | Long dark housing, short amber timber brace and ribbed steel covers; distinct from the gun receiver and based on visual reference study. |
| 1 | `torn-schematic` | Replaced: original model and rendered PNGs | Torn blue drafting paper with a large missing corner, slight curl, fine grid and original partial diagram. |
| 1 | `sealed-schematic` | Replaced: original model and rendered PNGs | Complete blue service drawing in a thin sealed sleeve, with curled edges and an original diagram. |
| 1 | `salvage` | Replaced: original model and rendered PNGs | Compact rusty assortment: toothed gear, coil, corrugated offcut, hollow pipes, loose nuts and wire. |
| 2 | `explosives` | Replaced: original model and rendered PNGs | Three grey-white compound blocks in printed and plain kraft bands tied with twine; distinct from sulphur and gunpowder. |
| 2 | `blueprint` | Replaced: original model and rendered PNGs | A cyanotype sheet in three zig-zag panels with an original drawing, for generic views; the learned-item presentation below is kept. |
| 2 | `research-table` | Replaced: original model, gibs and rendered PNGs | A dismantling bench: magnifier over a cutting mat, a device taken apart, vise, salvage tray, drafting board, sample drawers and bins. |
| 3 | `wire-tool` | Reviewed and replaced: original model and rendered PNGs | A yellow probe with a lit screen, rubber grip, wire reel and lead; the held presentation's frame and sockets are kept. |
| 4 | `woven-hood` | Replaced: the worn garment and rendered PNGs | The character package's hood, its drape and dark face opening, upright and front-on. |
| 4 | `woven-shirt` | Replaced: the worn garment and rendered PNGs | The worn cream woven shirt in its bind pose, sleeves out. |
| 4 | `woven-trousers` | Replaced: the worn garment and rendered PNGs | The worn dark olive trousers with their belt. |
| 4 | `woven-gloves` | Replaced: the worn garment and rendered PNGs | The worn knit gloves, fingers up and backs to the front, side by side. |
| 4 | `work-boots` | Replaced: the worn garment and rendered PNGs | The worn laced leather boots, brought together. |
| 4 | `chest-plate` | Replaced: the worn garment and rendered PNGs | The worn riveted steel breastplate with its straps and shoulder guards. |
| 4 | `door-key` | Borrows the key lock's model and icon | A key of its own, distinct from the key lock. |
| 4 | `wooden-door`, `armoured-door`, `double-door`, `garage-door` | Our early door models and their icons | Waits for the door-system rebuild: icons and dropped models from the corrected door kit. |

Batch 1 is nine assets: the eight unfinished components plus salvage. Batch 2 covers
three further borrowed identities. Wire Tool needs a model/icon review, rather than
an assumed replacement of its existing model. Batch 4 retires the last stand-in notes
in the catalogue. The six clothing items are now the worn garments themselves, so their
icons and dropped items show what is worn ([their asset record](ITEM_ART_CLOTHING.md)).
The door key gets a model of its own. The four door items wait for the door-system
rebuild, which makes both faces of a door match; their icons follow the corrected kit.

## Visual standard

Follow the game's advanced technology with visible handmade construction. Materials
may be clean, maintained or worn as appropriate; rust is not a universal requirement.
The six revised components provide a starting point for consistency, not a ceiling on
quality or evidence of final owner acceptance.

**Owner direction, 18 October 2026:** Rust is the main visual inspiration. Before
authoring each replacement, look up and inspect the actual corresponding Rust item
image on the web, preferably on Facepunch's wiki. Record the reference URL and the
observed construction, silhouette, material and small-slot readability. Use those
observations to author our own asset. Keep reference game images and models out of
the repository; retain links and notes. Work on one item at a time, wait for machine
resources, and close only processes and browser tabs created by this work.

Start with **cloth, gun receiver and salvage** to establish fabric, machined metal and
mixed-material examples. Review their silhouettes, material response and inventory
readability before applying the approach to the rest of the batch. Avoid adding detail
that only appears in an enlarged studio render.

Use deliberate silhouettes, sensible thickness, bevels, smooth shading where appropriate
and convincing material variation. More polygons alone do not correct weak forms or
shading. The existing revised pickups use roughly 1,500–4,400 triangles at their closest
level of detail, three detail levels and shared 2K texture sets. These are references
for small items, not fixed budgets for paper, fabric or the larger Research Bench.
Set each asset's dimensions, geometry budget and texture allocation before production.

## Production and integration

1. **Prepare the asset specification.** Confirm the item identity, dimensions, intended
   views, construction, material treatment and detail budget. Keep the existing item key.
2. **Author in Blender.** Reuse the original owner-controlled source and shared material
   conventions where useful. Save editable sources and repeatable exports in a new
   asset workspace. Preserve the existing revised package. Other games supply no files.
3. **Prepare game assets.** Supply the appropriate detail levels, UVs, material maps and
   resource paths. Keep metre scale, orientation and pivots consistent with the current
   importer. Add collision or held-item sockets where the catalogue and runtime require
   them. Use textured Godot scenes when the plain GLB does not retain material bindings.
4. **Render matching icons.** Use the existing Godot `IconRenderer` on the exact model
   selected by the catalogue. Render transparent 128 and 256 px PNGs through the existing
   supersampled workflow. Adjust per-item framing and angle for a readable silhouette.
5. **Integrate narrowly.** Update each item's model path and icon entry in
   `game/data/ground/models.json`; remove its stand-in annotation only after replacement.
   Keep IDs, keys, recipes, quantities and inventory behavior intact. Record asset source,
   provenance and installed hashes without overwriting earlier integration receipts.
6. **Review in the game.** Compare the model viewer, inventory grid and item details.
   Check representative UI scales and actual small slot sizes, not only 256 px images.
   Correct geometry, materials or framing where those views disagree.

The current local pipeline already supplies Blender authoring helpers, a catalogue,
model viewer and icon renderer. The original revised component sources are under
`D:/GameDev/Assets/Hollow_Salvage_R01/Source`. Their availability does not mean the
missing twelve item identities have already been authored. The optional `game-dev`
CLI was unavailable during the audit; the existing project tools support the proposed
local production route.

## Gameplay details to preserve

**Blueprints:** inventory stacks with `Teaches` show the taught item's icon on a blue
background in `GroundUi.Paint.cs`. Keep that behavior. A new generic blueprint asset
serves generic catalogue and detail views; it must not replace every taught-item icon
with the same paper picture.

**Dropped items:** inventory drops form lootbags. Replacing component art does not
introduce individually scattered physical component drops. Validate actual presentation
paths that use the models and retain the shared bag behavior.

**Research Bench:** its visual footprint, authoritative placement bounds and collision
must agree. Check placement on foundations, interaction reach, pickup, storage and
destruction. If the geometry changes, update the shared bounds and matching destruction
assets deliberately; the engineering-workbench debris must not silently remain the
claimed finished replacement.

**Wire Tool:** preserve its existing held presentation and grip requirements if its
model changes. Render only after the selected model is ready, so the icon does not
immediately become stale.

## Acceptance checks

Every replacement needs a recognizably distinct model and a matching rendered icon.
The intended rendered path must not silently fall back to the SVG. Check both icon
sizes, transparency, resource references, material bindings, detail levels, pivots
and required collision or sockets.

Run the existing catalogue/content checks and extend targeted coverage for these
identities where needed: dedicated model and icon mappings, no remaining stand-in
annotation, and no accidental reference to the previously borrowed asset. A file's
existence cannot establish visual quality; retain before/after model and inventory
screenshots for that review.

For implementation handover, run `dotnet build HollowUniverse.sln` and
`dotnet run --project tests/Hollow.Tests`, along with targeted catalogue checks and
Godot import/runtime inspection. Coordinate builds and Godot sessions with the root
agent. Record actual results and any remaining failures in `DEVELOPMENT.md`.

Completion means all thirteen scoped entries meet their applicable checks, icons are
readable at gameplay size, and the Research Bench and Wire Tool retain correct behavior.
Structural checks and successful imports do not substitute for artistic acceptance.

## Audit evidence

- [Component and salvage comparison](images/item-art-audit-20261018/components-current.png)
- [Resource icons](images/item-art-audit-20261018/resources-current.png)
- [All item icons page 1](images/item-art-audit-20261018/all-items-1.png) and
  [page 2](images/item-art-audit-20261018/all-items-2.png)
- [Catalogue snapshot](images/item-art-audit-20261018/inventory.json)
- [Revised model captures](images/abtest-b/repairs/assets/models-r01-contact-sheet.png)
- [Existing revised-asset integration record](REPAIR_ASSETS.md)
- [Audit sheet generator](../tools/audit_item_art.py)

The four icon sheets compose actual selected PNGs and source SVG drawings rasterized
outside Godot. They are diagnostic sheets, not new gameplay captures. The model images
are the existing Godot captures. The audit checked all 171 catalogue model/stage paths
and matched all 34 salvage-package and 15 container-package files against their installed
hash receipts. No replacement asset, runtime build or new gameplay validation is claimed.

To regenerate diagnostic sheets, use Python with Pillow and Node.js with Sharp:

```text
python tools/audit_item_art.py --output .artifacts/item-art-review --node <node-executable> --sharp <sharp-module-path> --font <ttf-path> --date YYYY-MM-DD
```

Generate into a fresh diagnostic directory. Treat the committed images and inventory
as the dated baseline; later catalogue changes can legitimately produce different output.
