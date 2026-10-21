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
