# Alloy Calculator: Technical Reference

## Fuel selection

[AlloyFuelDepositService](../VanillaExpanded/src/AlloyCalculator/AlloyFuelDepositService.cs) considers fuel in the backpack, hotbar, supported opened storage containers, and the firepit's queued fuel slot.

Candidates must have a positive burn duration and burn temperature, reach the input's melting point under the firepit's heat modifier, and be available in sufficient quantity to complete the melt within one fuel stack.

Eligible candidates are ranked by:

1. Presence of the native collectible tag `is-fuel-source`.
2. Lowest burn temperature within the same tag-priority group.
3. Smallest required quantity at equal temperature.

Exact ties retain the earlier candidate. Queued fuel is considered before external supplies.

Untagged burnables remain eligible as a fallback when no tagged candidate can complete the melt. The tag is a preference, not an eligibility requirement, and never bypasses temperature, duration, quantity, or inventory-access checks. This selection policy does not implement a separate preheating stage or a mixed-fuel schedule.

## Fuel-info preview

The dialog calls `AlloyFuelDepositService.CreatePreview` for the selected ingredient stacks, using a detached smelting inventory to obtain the crucible's melting point and duration without altering live inventory. The preview shares fuel selection and simulation with Deposit, including native tag preference and available supply restrictions.

The selected batch is estimated from 20-degree ingredients and zero cooking progress, retaining current furnace heat and the remaining active fuel burn. The count is the total queued fuel needed for that simulated batch, not an additional-to-deposit count; fuel already burning is excluded. The preview does not require possession of the selected ingredients or fuel already in the fuel slot. If no available fuel can complete the batch, it reports insufficient suitable fuel rather than inventing a fuel choice.

The indicator refreshes on calculator changes and deposit attempts, and every half-second while rendering to reflect inventory changes, opened storage, and firepit heat. Text is only recomposed when its value changes. No tick listeners or inventory subscriptions are retained.

Fuel has a separate panel section with a horizontal divider and localized title. The preferred fuel is shown in a centered `SlideshowItemstackTextComponent`, matching the ingredient previews' background, item tooltip, and quantity overlay. Unavailable or invalid batches clear the item preview and show a status message instead. The preview is only recomposed when the fuel stack or estimated quantity changes.

## Vanilla fuel tags

[fuelsources.json](../VanillaExpanded/assets/vanillaexpanded/patches/fuelsources.json) adds `is-fuel-source` to:

- Firewood and aged firewood.
- Peat bricks.
- Charcoal and coke.
- Contaminated coal.
- Lignite, bituminous coal, and anthracite.

The coal ores share an asset definition with non-fuel ores. Their tags are assigned through `tagsByType`, so other ores in that definition are not tagged.

## Modded fuel integration

Add `is-fuel-source` to the fuel's native collectible `tags` array, preserving any existing tags:

```json
{
  "tags": ["is-fuel-source"]
}
```

This is a top-level collectible tag, not an entry in `attributes`. For assets with both fuel and non-fuel variants, use `tagsByType` to tag only the intended variants.

## Tests

[AlloyFuelDepositServiceTests](../VanillaExpanded.Tests/Unit/AlloyCalculator/AlloyFuelDepositServiceTests.cs) covers tagged-fuel priority, unsuitable or insufficient tagged-fuel fallback, temperature-based ranking within the tagged group, and vanilla patch coverage.
