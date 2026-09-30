# Crafting Menu Popout Proposal

Status: proposed; no implementation is implied by this document.

## Purpose

Add a searchable crafting glossary as a popout attached to the player's crafting grid panel. Players can browse outputs, see which recipes their inventory supports, and place available ingredients into the grid with one click. The existing crafting output interaction remains responsible for actually crafting items.

This document specifies proposed behavior. Engine integration points and repository conventions have not yet been verified because local workspace reads were unavailable when drafting it.

## Scope

The initial glossary includes all enabled crafting-grid recipes available to the player in the current world, including compatible mod recipes. Recipes must fit the player's grid and satisfy the game's recipe visibility and access rules.

“All craftable items” means all outputs craftable through this panel. Cooking, smithing, knapping, clay forming, and other workstation or process recipes are outside this initial scope. A broader glossary could link to those systems later, but cannot fulfill their recipes by populating this grid.

## Player experience

A crafting-menu button beside the crafting grid toggles the attached popout. It contains a search bar and a scrollable tile grid of output item icons. The popout follows the parent panel, closes with it, and adapts its attachment side to available screen space. On small screens it must remain usable without covering the crafting grid or extending beyond the viewport.

Each tile shows an output icon and the recipe's output quantity. Fully available recipes appear normally. Unavailable recipes are greyed out but retain hover, focus, and click interactions. An additional unavailable marker avoids relying on color alone.

Search filters by localized output name, with item-code matching as a secondary convenience. Matching ignores case and trims whitespace; multiple words must all match. Search filters the catalogue without replacing inventory relevance ordering. Empty searches show the full catalogue, and an empty result set displays a clear message.

| Interaction | Result |
| --- | --- |
| Hover or keyboard-focus a tile | Show the output name, recipe ingredients, availability, and interaction hints. |
| Hover an unavailable tile | Explicitly list every missing ingredient and its missing quantity for one recipe execution. |
| Click a tile | Arrange the ingredients available for one recipe execution in the crafting grid. Missing ingredients remain absent. |
| Shift-click a tile | Arrange available ingredients and maximize the usable quantities placed in the grid, within inventory and slot limits. |
| Select an alternative recipe | Update the ingredient preview, missing list, and placement action to that recipe. |

Clicks only move ingredients. They do not take the output, consume ingredients, or repeatedly craft items.

## Outputs and alternative recipes

Show one tile per distinct resolved output, preserving attributes that materially distinguish the resulting item. Multiple recipes for the same output are grouped behind that tile, with an alternative-recipe indicator and a compact chooser or cycle control.

By default, select the alternative with the highest ingredient relevance. Preserve an explicitly selected alternative while that tile remains selected; do not silently switch the recipe under the player's cursor. Tile availability, tooltip, relevance, and click behavior must all describe the same selected recipe. Releasing an explicit selection restores automatic choice.

Do not enumerate arbitrary hypothetical attribute combinations. Where a mod recipe has a dynamic output that cannot be represented accurately before ingredient selection, use an honest generic preview and resolve it through the owning recipe logic when possible. Unsupported recipes must be identified explicitly rather than shown as reliably fillable.

## Inventory and ingredient matching

Proposed ingredient sources are the player's normal carried storage, including the hotbar, backpack storage, and ingredients already in the crafting grid. Exclude the crafting output slot, cursor-held stack, equipment slots, remote containers, and inventories the player cannot currently access for normal item transfers. Exact inventory identifiers require source verification.

Use the game's existing recipe matching and item-transfer rules. Matching must preserve wildcard constraints, repeated ingredient requirements, stack attributes, tool requirements, durability constraints, and any relationships between ingredient choices. A generic material match must not override a more specific recipe constraint.

Build one allocation for the complete recipe against a snapshot of eligible stacks. A physical item cannot satisfy two simultaneous requirements. Resolve overlapping alternatives globally so a flexible requirement does not take the only item that can satisfy a restricted requirement. Equivalent choices should use a stable inventory/slot order unless the game already defines a preferred policy.

Tools and other non-consumed requirements must follow the recipe's actual semantics. Do not treat their durability or presence as ordinary consumable quantities. Ingredients from incompatible stacks must not be merged merely because both stacks separately match a recipe predicate.

## Relevance and availability

For ordinary counted ingredients, let R be the total units required for one execution and A be the maximum number of those units that can be allocated simultaneously from eligible inventory. Relevance is A / R. A presence-only tool contributes one required unit, subject to its actual recipe constraints. Recipes with exceptional requirements need an explicit supported adapter rather than an invented numerical approximation.

Sort fully satisfiable recipes first, then incomplete recipes by descending relevance. Break ties by fewer missing units, localized output name, and a stable output/recipe identifier. This is a proposed quantity-based definition of “how many required ingredients the player has.” It favors substantial progress toward a recipe rather than merely owning many ingredient types.

For example, a recipe requiring four boards and two sticks has relevance 4/6 when the player has three boards and one stick. Its missing list shows one board and one stick. Having a large surplus of boards cannot compensate for missing sticks or raise the score above 1.

Availability requires a complete valid allocation for one execution, including recipe constraints. Ingredient sufficiency and the ability to rearrange a full inventory are separate: a tile may have all ingredients while its placement action fails because displaced grid contents cannot be returned. Explain that failure when clicked.

Recompute affected availability and ordering after relevant inventory or recipe changes. Keep a stable selection and avoid moving a hovered tile immediately before a click; defer the visible reorder until the interaction finishes while revalidating the action against current inventory.

## Ordinary click placement

1. Resolve the selected recipe and a valid layout for the current grid. Preserve shaped arrangements; use a stable legal layout for shapeless recipes.
2. Plan how existing grid contents will be reused, repositioned, or returned to eligible carried storage. Count existing grid ingredients only once.
3. Allocate up to the required quantity for one execution in each recipe position. Populate available requirements even if other requirements remain missing.
4. Validate the complete transfer plan, including destination acceptance, stack compatibility, and capacity for displaced items.
5. Execute through the game's authoritative inventory mechanisms and refresh the preview from the resulting state.

If a requirement needs several units and only some are available, place those units. A requirement with no available ingredient remains empty. Missing positions may have non-interactive preview outlines if the existing UI supports them; preview content must never become real inventory stacks.

If displaced items cannot be returned safely, leave the grid unchanged and show a concise reason. Do not drop items automatically. Clicking with no usable ingredients should not clear an unrelated grid merely to produce an empty layout; report that no matching ingredients are available.

## Shift-click placement

Shift-click uses the same selected recipe, matching rules, and transfer safety as an ordinary click, but fills ingredient positions as far as available inventory and slot limits allow.

For repeated or overlapping requirements, “maximize each stack” needs a shared allocation policy: independently filling the first slot could starve every later slot. Proposed policy:

1. First allocate the best available single-execution coverage, spreading scarce shared ingredients across required positions rather than filling only the first position.
2. Next maximize the number of complete recipe executions supported by the placed consumable quantities, accounting for each position's required quantity and stack limit.
3. Finally distribute remaining matching items into compatible required positions up to their limits, preserving that coverage and execution count. Prefer the least-filled equivalent position, with stable grid-position tie-breaking.

This also applies when a different ingredient is entirely missing: available positions are still filled. A missing ingredient must not reduce all other placements to zero. Surplus ingredients may remain in the grid after the limiting ingredient runs out during later manual crafting; this follows the requested maximum-stack behavior.

For a recipe requiring the same one-unit ingredient in two positions, 20 available units yield 10 in each position. With 127 units and 64-unit limits, the result is 64 and 63. A non-stackable tool remains one tool, and shift-click does not automatically replace it or craft beyond its durability.

The allocation objective applies only to legal matching stacks. A recipe position can hold only one compatible stack; incompatible variants cannot be combined to claim a larger achievable batch.

## Missing-ingredient tooltip

Derive missing quantities from the same one-execution allocation used for relevance and availability. Aggregate repeated identical requirements where doing so preserves their constraints. For alternatives, explain the accepted choice, such as “Missing 2 of: oak board or pine board,” rather than incorrectly listing every alternative as individually required.

Include tools and special requirements with an accurate reason when unsupported or insufficient. Refresh the tooltip after inventory changes. Shift-click does not change the tooltip into a deficit for an arbitrary maximum batch; it continues to explain what is missing for the first execution.

## Proposed implementation boundaries

Keep panel integration limited to attachment, visibility, and event wiring. Separate catalogue construction, search/order computation, ingredient allocation, transfer planning, and popout rendering by responsibility. Reuse the same allocation result for ranking, tooltips, and placement to prevent contradictory behavior.

The existing recipe system remains the authority for recipe semantics. The existing inventory system remains the authority for movement, merging, restrictions, and synchronization. The menu must not introduce a parallel crafting engine or mutate client inventory stacks directly.

Before implementation, inspect the actual crafting-dialog lifecycle, loaded recipe registry, recipe matching APIs, inventory ownership, and transfer protocol. Determine whether existing transfers support a validated multi-slot operation. If they do not, the design needs an authoritative operation that revalidates and safely commits the plan; sequential optimistic client moves are insufficient to guarantee the unchanged-on-failure behavior above.

In multiplayer, validate recipe identity, eligible source slots, destination slots, quantities, and current inventory state on the authority responsible for inventory changes. Treat stale previews as expected: reject or recompute stale requests without duplication, loss, or unauthorized inventory access. Suppress duplicate pending requests and reconcile the UI with the confirmed result.

## Catalogue lifecycle and performance

Build the recipe/output catalogue when loaded recipes are available, and rebuild when the world or recipe registry changes. Cache normalized search text and immutable recipe metadata. Recalculate inventory-dependent allocation when eligible inventory changes, not every render frame.

Render only visible tiles for large catalogues, reuse the game's item-icon rendering, and release GUI/render resources when the panel or world closes. Any deferred computation must use safe snapshots and discard stale results; game-owned mutable inventory data must remain on its owning thread.

Performance targets should be set after measuring representative vanilla and heavily modded catalogues. No runtime cost or compatibility claim is established by this proposal.

## Acceptance criteria

- Every eligible resolved grid-crafting output is discoverable through browsing and localized-name search; alternatives remain accessible.
- Inventory changes update relevance, missing quantities, and availability consistently, including quantities already in the grid.
- Unavailable tiles remain selectable and expose all missing requirements for one execution.
- Ordinary clicks populate available ingredients for one execution, respecting shaped and shapeless layouts.
- Shift-click maximizes legal placement under the shared-allocation policy, including incomplete recipes and repeated ingredients.
- Wildcards, restricted alternatives, attribute-sensitive stacks, tools, non-stackable items, and output variants retain native semantics.
- A full inventory or stale request cannot lose, duplicate, drop, or incorrectly consume items; unsafe rearrangements leave the original state intact.
- Existing output-taking and manual crafting behavior continues to work after placement.
- Search focus, scrolling, tooltip display, panel closing, UI scale, and small-screen placement are usable in the live client.
- Multiplayer behavior is checked with authoritative inventory updates and competing inventory changes.

Future implementation should use focused automated tests for allocation and transfer invariants, followed by user-run live-client checks for layout, interaction, and synchronization. This proposal itself changes no code and establishes no tested runtime behavior.

## Decisions to revisit before implementation

- Confirm that the initial glossary should cover grid recipes only; broader crafting processes require a separate browsing and navigation design.
- Confirm quantity-based relevance versus weighting each ingredient type equally.
- Verify the exact carried inventories eligible for sourcing and returning items.
- Confirm the alternative-recipe control and the handling of dynamic mod outputs after inspecting existing UI conventions.
- Verify authoritative transfer support before committing to an implementation mechanism.
