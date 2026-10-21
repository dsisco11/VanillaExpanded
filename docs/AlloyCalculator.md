# Alloy Calculator: Technical Reference

## Fuel selection

[AlloyFuelDepositService](../VanillaExpanded/src/AlloyCalculator/AlloyFuelDepositService.cs) considers fuel in the backpack, hotbar, supported opened storage containers, and the firepit's queued fuel slot.

Candidates must have a positive burn duration and burn temperature, reach the input's melting point under the firepit's heat modifier, and be available in sufficient quantity to complete the melt within one fuel stack.

Eligible candidates are ranked by:

1. Presence of the native collectible tag `is-fuel-source`.
2. Lowest burn temperature within the same tag-priority group.
3. Smallest required quantity at equal temperature.

Exact ties retain the earlier candidate. Queued fuel is considered before external supplies.

Untagged burnables remain eligible as a fallback when no tagged candidate can complete the melt. The tag is a preference, not an eligibility requirement, and never bypasses temperature, duration, quantity, or inventory-access checks. Deposit does not schedule multiple fuel types; the preview can separately estimate prewarming before a manual fuel switch.

## Fuel-info preview

The dialog calls `AlloyFuelDepositService.CreatePreview` for the selected ingredient stacks, using a detached smelting inventory to obtain the crucible's melting point and duration without altering live inventory. The preview shares fuel selection and simulation with Deposit, including native tag preference and available supply restrictions.

The selected batch is estimated from 20-degree ingredients and zero cooking progress, retaining current furnace heat and the remaining active fuel burn. The count is the total queued fuel needed for that simulated batch, not an additional-to-deposit count; fuel already burning is excluded. The preview does not require possession of the selected ingredients or fuel already in the fuel slot. If no available fuel can complete the batch, it reports insufficient suitable fuel rather than inventing a fuel choice.

The indicator refreshes on calculator changes and deposit attempts, and every half-second while rendering to reflect inventory changes, opened storage, and firepit heat. Text is only recomposed when its value changes. No tick listeners or inventory subscriptions are retained.

The ingredient previews have a centered, localized "Required metals" title. Fuel has a separate panel section with a horizontal divider and centered, localized "Required fuel" title. The preferred fuel is shown in a centered `SlideshowItemstackTextComponent`, matching the ingredient previews' background and quantity overlay. Fuel and prewarming slots retain the standard item tooltip without custom explanatory text. Unavailable or invalid batches clear the item preview and show a status message instead. The preview is only recomposed when the fuel stack or estimated quantity changes.

The excess-metal warning's reserved space and gap collapse when there is no warning. The fuel-status row also collapses when it has no prewarming or availability message. Visibility changes reposition the fuel section and Deposit button and resize the existing composer without resetting alloy selection, ratios, or target units; the button tooltip moves with the button.

### Prewarming

When queued fuel has a positive effective burn duration and temperature below the selected batch's melting point, or an active burn has a positive maximum temperature below that point, the indicator displays a prewarming status. Temperature checks include the firepit's heat modifier.

The simulation consumes the remaining active burn first, then the entire queued low-temperature fuel stack, then switches to a suitable available smelting fuel. The item preview shows the estimated **smelting-fuel count after prewarming**, excluding the prewarming items. Candidate ranking still prefers tagged sources, then cooler sufficient fuels and lower quantities at equal heat. Availability is tested against the reduced requirement.

Savings compare that same selected smelting fuel against an immediate switch from the same current furnace heat and cold selected ingredients, omitting future low-temperature queued and active burns. An active smelting-capable burn is retained in both simulations. Already accumulated heat is credited in both; the indicator does not estimate historical fuel savings.

The display reports whole-item savings, including zero when prewarming does not eliminate a whole fuel item. If prewarming increases the requirement, it reports extra items rather than claiming negative savings. If no available smelting fuel can finish the operation, prewarming status remains visible but no savings number or fuel stack is invented.

The status row uses rich-text item previews: `Prewarming: [queued prewarming fuel stack] saves [saved smelting-fuel stack]`. Text is vertically centered alongside the item previews. Each slot includes its quantity overlay and item tooltip. For an active-only prewarming burn, the engine exposes remaining burn time and temperature but not the consumed item's identity; the row uses "burning fuel" instead of inventing an item icon. The separate fuel preview above continues to show the smelting fuel still required.

This is a read-only preview. It does not light the firepit, consume fuel, or schedule a switch. **Deposit still replaces unsuitable queued fuel immediately**; to realize the previewed savings, let the prewarming fuel burn before switching or depositing fuel.

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
