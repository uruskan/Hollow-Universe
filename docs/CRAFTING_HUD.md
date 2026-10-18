# Crafting on the ground HUD

The owner requested an ongoing crafting indicator to the left of health, food and
water on 16 October 2026. The active queue entry shows its produced item's icon
and name, `CRAFTING ×4` (remaining output units), `IN INVENTORY 5`, and progress
toward the next recipe completion. Counts follow the client's existing predicted
queue and inventory; the HUD does not create a second crafting timer or queue.
A recipe yielding two arrows decreases the remaining count by two per completion.
The owned count uses the produced item's key, which need not equal the recipe key.

The card follows the bottom-right HUD anchor, left of the vitals with a gap. It
narrows where possible; at narrow widths or larger interface scales it moves above
the hotbar to keep both readable. It uses the existing scaled text, icons and HUD
palette. The card disappears when the queue empties, the player dies, another
screen opens, or the flight HUD replaces the ground HUD. Cancelling the active
job reveals the next job. There are no extra click targets on the normal HUD.

Preview: `--view ui --screen hud-crafting --shot <path>` shows four bandages
remaining; `--screen hud-crafting-next` shows the same inventory after one finishes.
The UI check covers output quantities, owned counts, progress, cancellation,
completion, visibility and layout at normal/narrow/scaled sizes. Its 16 checks are
also available alone through `--view ui --crafting-hud-check`. They reuse the
viewer rather than loading another HUD, character or map. The mock clock is held
during checks and advanced explicitly, so a slow machine cannot finish a sample
craft between unrelated assertions. Interactive previews still advance normally.

The fresh integrated Debug build passed on 16 October at 19:49 local with zero
warnings/errors. The full UI run then passed 67/67, including all 16 crafting-HUD
checks. Three screenshots were inspected: four remaining/five owned, three
remaining/six owned, and the clear narrow layout at 1024×768. Settings remained
unchanged. See [the validation evidence](repair-results/Admin-visual-validation.md).
