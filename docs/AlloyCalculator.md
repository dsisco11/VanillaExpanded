# Alloy Calculator: Technical Reference

## Ingredient eligibility

Ingredient availability and concrete-form planning use the crucible cooking slots' native
`CanHold` checks, including storage flags, tags, insertion locks, and content dimensions.
Matching a metal's smelted output alone is not sufficient: ordinary ingots are too large
for the vanilla crucible and must not displace usable nuggets in a deposit plan.
Execution rechecks eligibility from live supplies. Planning does not use `CanTakeFrom`
against current cooking contents because those contents may be removed before insertion.

The stack planner snapshots source stacks and groups merge-compatible forms per ingredient,
retaining stable smallest-source-stack preference. Each form records its available quantity
and effective capacity (the lower of the common cooking-slot limit and the item's stack limit).
Forms with nonpositive capacity are skipped without excluding usable alternatives.
During execution-time resolution, a concrete layout is validated before greedy selection.
Valid layouts retain their exact forms, quantities, and slot assignments even if smaller
source stacks have become available. Validation checks unique in-range destinations,
positive quantities, capacity, ingredient identity, and aggregate supply per form.
Infeasible layouts fall through to reselection; invalid indices or nonpositive requests
are rejected. Selection prefers the existing greedy result when it fits. If greedy
selection exceeds the available slot budget, a read-only backtracking search tries
smaller contributions from preferred forms, then skips those forms as needed. It can
revisit an earlier ingredient's choices to leave space for later ingredients.
The first feasible selection is passed to the existing spare-slot balancing logic.
Execution-time resolution remains restricted to the plan's destination indices.
Search reserves at least one slot for each later ingredient and rejects states whose
remaining supply or optimistic capacity cannot meet the current request. Failed states
are memoized by ingredient index, form index, remaining quantity, and remaining slots.
The cache is local to one detached supply snapshot, assuming disjoint supplies between
ingredients; no attempt limit truncates the search. See
[the planner proposal](AlloyDepositPlannerProposal.md) for the broader design.

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

Prewarming candidates come from withdrawable backpack, hotbar, and supported opened-storage slots, plus the firepit's queued fuel. Each distinct stack type with positive effective burn duration and temperature below the selected batch's melting point is considered, including untagged burnables. Candidates need not already be in the fuel slot. Temperature and duration checks include the firepit's modifiers.

If any useful prewarming options have the native `is-fuel-source` tag, only those tagged options appear in the slideshow. Untagged burnables are a fallback only when no tagged option passes the temperature and whole-item-savings checks; merely possessing a tagged fuel does not suppress the fallback.

For each candidate, the preview simulates the remaining active burn and then whole prewarming items until both the furnace and crucible contents reach that fuel's temperature ceiling. Contents within one degree count as reaching the ceiling, matching the game's temperature-step tolerance and avoiding an asymptotic exact-equality requirement for stacked inputs. The displayed amount is the smallest whole-item count reaching that threshold, not the first count saving an item. Counts may exceed available quantity or stack capacity: they describe a requirement, not possession.

The normal smelting fuel is selected independently using the existing tag preference, temperature ranking, and availability checks. Its "Required fuel" amount excludes optional queued prewarming. Each candidate's full prewarming amount is tested against that same fuel from the same current furnace heat, cold selected ingredients, and remaining active burn. Only candidates reducing the normal requirement by at least one whole smelting-fuel item survive. Already accumulated heat and active burning fuel are credited in both simulations.

The surviving item types and their individual required counts cycle in one `SlideshowItemstackTextComponent`. The row reads `Prewarming: [cycling prewarming options] saves [1x selected smelting fuel]`: one saved item is the guaranteed benefit, even if an option saves more. Its text is vertically centered alongside the slots, with standard item tooltips and quantity overlays. The separate "Required fuel" preview stays at its normal amount rather than changing with the slideshow.

If there are no useful options, the prewarming row is hidden and its space collapses without a no-savings message. An active burn alone does not invent a candidate item identity. If no available smelting fuel can finish the normal batch, the usual insufficient-fuel message is shown without prewarming options.

This is a read-only preview. It does not light the firepit, consume fuel, or schedule a switch. **Deposit still replaces unsuitable queued fuel immediately**; to realize the previewed savings, burn the displayed count of one chosen prewarming option before switching or depositing fuel.

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
