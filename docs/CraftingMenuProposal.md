# Crafting Menu Popout Proposal

Status: proposed; no implementation is implied by this document.

## Purpose

Add a searchable crafting glossary as a popout attached to the player's crafting grid panel. Players can browse outputs, see which recipes their inventory supports, and place available ingredients into the grid with one click. The existing crafting output interaction remains responsible for actually crafting items.

This document specifies proposed behavior, not implemented functionality. Source-backed engine integration decisions, repository conventions, and remaining validation obligations are recorded in [CraftingMenu.todo](./CraftingMenu.todo), Phase 1.

## Scope

The initial glossary includes all enabled crafting-grid recipes available to the player in the current world, including compatible mod recipes. Recipes must fit the player's grid and satisfy the game's recipe visibility and access rules.

For this design, recipes, player recipe visibility, access rules, and grid dimensions are immutable while the world is loaded. Eligibility is evaluated when the world's catalogue is built; inventory changes affect ingredient availability, not catalogue eligibility. See Catalogue lifecycle and performance for world-loading boundaries.

“All craftable items” means all outputs craftable through this panel. Cooking, smithing, knapping, clay forming, and other workstation or process recipes are outside this initial scope. A broader glossary could link to those systems later, but cannot fulfill their recipes by populating this grid.

## Player experience

A crafting-menu button beside the crafting grid toggles the attached popout. It contains a search bar and a scrollable tile grid of output item icons. The popout follows the parent panel, closes with it, and adapts its attachment side to available screen space. On small screens it must remain usable without covering the crafting grid or extending beyond the viewport.

Use the game's composer, native bounds hierarchy, clipping, containers and controls for layout, focus and input. Custom GUI behavior should be limited to attachment-side policy, bounded tile virtualization and recipe-specific presentation/actions. Recipe previews are non-transferable controls, not synthetic inventory slots.

Each tile shows an output icon and the recipe's output quantity. Fully available recipes appear in full color. Missing ingredients use a dim grayscale icon and greyed button background; unsupported recipes use a near-black icon and blacked-out background. Do not use status annotation characters such as `!` or `?`. All states retain hover, focus and inspection; unsupported recipes still disable placement. Tooltips explain the state in text. The alternative-recipe indicator is separate from availability status.

Fit each icon to the smaller button dimension using the native inventory-slot item/slot size ratio. Clip tile contents to the button bounds with a stacking scissor, preserving the scroll viewport's clip; GUI scaling must not enlarge icons beyond their slots.

Search filters by localized output name, with item-code matching as a secondary convenience. Matching ignores case and trims whitespace; multiple words must all match. Search filters the catalogue without replacing inventory relevance ordering. Empty searches show the full catalogue, and an empty result set displays a clear message.

| Interaction | Result |
| --- | --- |
| Hover or keyboard-focus a tile | Show the output name, recipe ingredients, availability, and interaction hints. |
| Hover an unavailable tile | Explicitly list every missing ingredient and its missing quantity for one recipe execution; for an unsupported recipe, explain what cannot be evaluated instead of inventing deficits. |
| Click a tile | For a supported recipe, arrange the ingredients available for one recipe execution in the crafting grid. Missing ingredients remain absent. Unsupported recipes remain inspectable but cannot trigger placement. |
| Shift-click a tile | For a supported recipe, arrange available ingredients and maximize the usable quantities placed in the grid, within inventory and slot limits. Unsupported recipes cannot trigger placement. |
| Select an alternative recipe | Update the ingredient preview, missing list, and placement action to that recipe. |

Clicks only move ingredients. They do not take the output, consume ingredients, or repeatedly craft items.

## Outputs and alternative recipes

Show one tile per distinct resolved output, preserving attributes that materially distinguish the resulting item. Multiple recipes for the same output are grouped behind that tile, with an alternative-recipe indicator and a compact chooser or cycle control.

By default, select the supported alternative with the highest ingredient relevance. If no alternative is supported, select one by stable recipe identifier. Preserve an explicitly selected alternative while that tile remains selected; do not silently switch the recipe under the player's cursor. Tile availability, tooltip, relevance, and click behavior must all describe the same selected recipe. Releasing an explicit selection restores automatic choice.

Do not enumerate arbitrary hypothetical attribute combinations. Where a mod recipe has a dynamic output that cannot be represented accurately before ingredient selection, use an honest generic preview and resolve it through the owning recipe logic when possible. Unsupported recipes must be identified explicitly rather than shown as reliably fillable.

Unsupported recipes remain discoverable through browsing and search, and accessible in the alternative-recipe control. Mark their availability as unknown/unsupported, show the reason in the tooltip, and disable ingredient placement for both click modes without disabling inspection or alternative selection. Do not assign a numerical relevance or fabricate missing quantities for requirements that cannot be evaluated. See Relevance and availability for their ordering.

## Inventory and ingredient matching

Proposed ingredient sources are the player's normal carried storage, including the hotbar, backpack storage, and ingredients already in the crafting grid. Exclude the crafting output slot, cursor-held stack, equipment slots, remote containers, and inventories the player cannot currently access for normal item transfers. Exact inventory identifiers require source verification.

Use the game's existing recipe matching and item-transfer rules. Matching must preserve wildcard constraints, repeated ingredient requirements, stack attributes, tool requirements, durability constraints, and any relationships between ingredient choices. A generic material match must not override a more specific recipe constraint.

Do not inspect collectible method overrides or private recipe-event subscriptions to certify compatibility. Collectible behaviors, overrides, and custom event subscriptions are not blanket reasons to reject a recipe or transfer. Invoke native matching and transfer APIs normally; retain actual trait access, explicit recipe-semantic constraints, native winner validation, and slot/merge/heat/capacity checks. Ingredient placement does not invoke crafting consumption, durability mutation, or output-creation callbacks.

Potential overlap with another recipe is not a reason to disable placement, and an unrelated custom recipe must not disable ordinary entries. Validate the actual native winner for complete planned layouts. Incomplete layouts remain placeable from available ingredients without claiming a resolved native output.

Build one allocation for the complete recipe against a snapshot of eligible stacks. A physical item cannot satisfy two simultaneous requirements. Resolve overlapping alternatives globally so a flexible requirement does not take the only item that can satisfy a restricted requirement. Among equally optimal allocations, equivalent choices should use a stable inventory/slot order unless the game already defines a preferred policy. For shift-click, apply this tie-break only after satisfying the objectives in Shift-click placement.

Tools and other non-consumed requirements must follow the recipe's actual semantics. Do not treat their durability or presence as ordinary consumable quantities. Ingredients from incompatible stacks must not be merged merely because both stacks separately match a recipe predicate.

## Relevance and availability

For ordinary counted ingredients, let R be the total units required for one execution and A be the maximum number of those units that can be allocated simultaneously from eligible inventory. Relevance is A / R. A presence-only tool contributes one required unit, subject to its actual recipe constraints. Recipes with exceptional requirements need an explicit supported adapter rather than an invented numerical approximation.

Sort fully satisfiable recipes first, then incomplete recipes by descending relevance. Break ties by fewer missing units, localized output name, and a stable output/recipe identifier. This is a proposed quantity-based definition of “how many required ingredients the player has.” It favors substantial progress toward a recipe rather than merely owning many ingredient types.

Tiles whose selected recipe is unsupported sort after all supported recipes, by localized output name and stable output/recipe identifier. Their relevance remains unknown rather than zero. An explicitly selected unsupported alternative retains that state even when the tile has another, supported alternative.

For example, a recipe requiring four boards and two sticks has relevance 4/6 when the player has three boards and one stick. Its missing list shows one board and one stick. Having a large surplus of boards cannot compensate for missing sticks or raise the score above 1.

Availability requires a complete valid allocation for one execution, including recipe constraints. Ingredient sufficiency and the ability to rearrange a full inventory are separate: a tile may have all ingredients while its placement action fails because displaced grid contents cannot be returned. Explain that failure when clicked.

Recompute availability and ordering after relevant inventory changes. A newly loaded world gets a fresh catalogue and selection state. Keep a stable selection and avoid moving a hovered tile immediately before a click; defer the visible reorder until the interaction finishes while revalidating the action against current inventory.

## Ordinary click placement

1. Resolve the selected recipe and a valid layout for the current grid. Preserve shaped arrangements; use a stable legal layout for shapeless recipes.
2. Plan how existing grid contents will be reused, repositioned, or returned to eligible carried storage. Count existing grid ingredients only once.
3. Allocate up to the required quantity for one execution in each recipe position. Populate available requirements even if other requirements remain missing.
4. Preflight the complete transfer plan, including destination acceptance, stack compatibility, and capacity for displaced items, against the current client snapshot.
5. Execute through the native client inventory operations and standard synchronization, then refresh the preview from the resulting state.

If a requirement needs several units and only some are available, place those units. A requirement with no available ingredient remains empty. Missing positions may have non-interactive preview outlines if the existing UI supports them; preview content must never become real inventory stacks.

If preflight finds that displaced items cannot be returned safely, leave the grid unchanged and show a concise reason. Do not drop items automatically. Clicking with no usable ingredients should not clear an unrelated grid merely to produce an empty layout; report that no matching ingredients are available.

Preflight refusal and native execution refusal are different states. If inventory changes during dispatch and a later native transfer is locally refused, accepted moves may remain. Stop issuing further placement moves when the refusal or competing inventory change is observed, let already-submitted operations resolve through native synchronization, explain that placement was interrupted, and refresh from current client inventory. After dispatch, the game owns authoritative reconciliation; do not infer server acceptance or rejection. Do not restore an old snapshot, blindly replay the whole placement, or claim that a client bulk action is an all-or-nothing transaction.

## Shift-click placement

Shift-click uses the same selected recipe, matching rules, and transfer safety as an ordinary click, but fills ingredient positions as far as available inventory and slot limits allow.

For repeated or overlapping requirements, “maximize each stack” needs a shared allocation policy: independently filling the first slot could starve every later slot. Optimize the complete allocation jointly under these lexicographically ordered objectives; a later objective must preserve the optimum achieved for every earlier objective:

1. First allocate the best available single-execution coverage, spreading scarce shared ingredients across required positions rather than filling only the first position.
2. Next maximize the number of complete recipe executions supported by the placed consumable quantities, accounting for each position's required quantity and stack limit.
3. Finally distribute remaining matching items into compatible required positions up to their limits, preserving that coverage and execution count. Prefer the least-filled equivalent position, with stable grid-position tie-breaking.

These are optimization priorities, not irreversible transfer steps. Reconsider preliminary ingredient and variant choices when doing so improves a later objective without reducing an earlier one. Apply stable inventory/slot ordering only among equally optimal allocations. For example, if one position accepts either oak or pine boards, one oak board earlier in inventory must not lock that position when a separate stack of 64 pine boards supports more executions with the same single-execution coverage.

This also applies when a different ingredient is entirely missing: available positions are still filled. A missing ingredient must not reduce all other placements to zero. Surplus ingredients may remain in the grid after the limiting ingredient runs out during later manual crafting; this follows the requested maximum-stack behavior.

For a recipe requiring the same one-unit ingredient in two positions, 20 available units yield 10 in each position. With 127 units and 64-unit limits, the result is 64 and 63. A non-stackable tool remains one tool, and shift-click does not automatically replace it or craft beyond its durability.

The allocation objective applies only to legal matching stacks. A recipe position can hold only one compatible stack; incompatible variants cannot be combined to claim a larger achievable batch.

## Missing-ingredient tooltip

Derive missing quantities from the same one-execution allocation used for relevance and availability. Aggregate repeated identical requirements where doing so preserves their constraints. For alternatives, explain the accepted choice, such as “Missing 2 of: oak board or pine board,” rather than incorrectly listing every alternative as individually required.

Include tools and special requirements with an accurate reason when unsupported or insufficient. For unsupported recipes, follow the unknown-state contract in Outputs and alternative recipes rather than presenting an unevaluated requirement as a known deficit. Refresh the tooltip after inventory changes. Shift-click does not change the tooltip into a deficit for an arbitrary maximum batch; it continues to explain what is missing for the first execution.

## Proposed implementation boundaries

Keep panel integration limited to attachment, visibility, and event wiring. Separate catalogue construction, search/order computation, ingredient allocation, transfer planning, and popout rendering by responsibility. Reuse the same allocation result for ranking, tooltips, and placement to prevent contradictory behavior.

The existing recipe system remains the authority for recipe semantics. The existing inventory system remains the authority for movement, merging, restrictions, and synchronization. The menu must not introduce a parallel crafting engine or mutate client inventory stacks directly.

Ingredient placement is a client-side bulk inventory operation built on the game's existing inventory APIs and normal synchronization. Catalogue construction, recipe selection, allocation, and transfer planning stay on the client. Do not introduce a custom server-side placement operation, server-owned catalogue, or feature-specific request/result protocol.

Before implementation, inspect the actual crafting-dialog lifecycle, client-loaded recipe registry, recipe matching APIs, inventory ownership, and native bulk-transfer protocol. Identify the exact native API and its validation, partial-failure, and synchronization behavior. Preflight the complete rearrangement and revalidate the client snapshot before invoking it; never replace normal inventory operations with direct stack/attribute writes.

In multiplayer, the game remains responsible for accepting inventory transfers and synchronizing authoritative state through its existing protocol. Treat stale previews as expected, suppress overlapping local placement execution, and reconcile with native inventory updates. The user approved a local execution guard: another click is allowed after the current client placement burst has been dispatched, without waiting for a whole-batch acknowledgement. Server reconciliation may still be pending and can affect an earlier placement. Do not equate release of the guard with authoritative success.

The user-approved failure contract is unchanged state on local preflight refusal, and ordinary native partial progress plus game-owned reconciliation on a later transfer refusal; there is no whole-arrangement rollback guarantee. Notify interruption for locally observed native execution refusal or competing changes during dispatch. Use native inventory notifications to invalidate previews without distinguishing local changes from server-applied changes. Do not patch packet processing, retain post-dispatch projection histories, classify server corrections, or claim confirmed batch outcomes.

Use the native inventory open/transfer/synchronize lifecycle. Reuse inventories already opened by the parent panel and do not close the crafting grid merely to flush changes; its normal closing behavior may return or drop contents. Native manager transfer calls return operation packets, which the client sends through the normal inventory network API. Closing an inventory is a lifecycle operation, not a commit of arbitrary direct stack edits; no unsupported flush or update-pausing semantics are required.

## Catalogue lifecycle and performance

Build the recipe/output catalogue at world finalization, when loaded recipes are available, and clear it on world exit. A newly loaded world gets a new catalogue. The user explicitly selected world-loading-only rebuilds: recipes are immutable while loaded, so do not observe in-world registry reloads or add recipe-change checks. Cache normalized search text and immutable recipe metadata for that world.

On opening the popout, compute inventory relevance from a fresh eligible-inventory snapshot: union the eligible recipes that accept any inventory item, deduplicate that candidate list, then perform exact allocation/availability evaluation on the candidates. Broad matching must not collapse attribute-sensitive stacks into an item-code-only identity, require full ingredient quantities, or claim craftability. Keep recipes outside that attention list in the searchable catalogue; handle zero-ingredient and exceptional recipes conservatively rather than excluding them through the ingredient union.

The user selected complete recomputation from a fresh snapshot when eligible inventory changes while the popout is open. Do not maintain an incremental inventory-result/dependency cache between changes or compute every render frame. Native immutable ingredient indexes may be reused; per-computation deduplication is not a long-lived inventory cache. Later integration should coalesce notifications belonging to one inventory action and revalidate clicks against current state.

Recipe visibility, access rules, and grid dimensions are immutable during that loaded-world lifetime under the Scope assumption, so no independent player-eligibility refresh is required.

Native slot notifications schedule a coalesced refresh from current inventory while the popout is open, including notifications caused by placement itself. They are invalidation signals, not server acknowledgements. The installed crafting-grid full-contents synchronization path bypasses both SlotModified and MarkedDirty notifications. A feature-owned GUI element observes the native output dirty-slot flag before native slot-grid finalization consumes it and schedules the same refresh. This is native GUI lifecycle integration, not packet or rendering-method interception; it neither clears native flags nor evaluates recipes every frame. Controlled installed-native tests cover contents replacement and the native output element clearing the flag; live-client coverage remains a separate validation obligation.

Render only visible tiles for large catalogues. Use native item rendering to lazily bake static previews into a world-owned cache, retaining every created icon until atlas invalidation or world exit; animated previews are not required. Do not evict previews for capacity, scrolling, menu closure or GUI scale changes. Retain pixel-size variants so returning to an earlier scale also reuses its icons. Bake up to four new icons per render frame by default, request only currently visible misses and allow at most four outstanding deferred bakes. Pending tiles still render their state backgrounds and remain interactive. Use the game's ItemStack.Equals and collectible equality implementation for stack equivalence throughout catalogue grouping, preview sharing, ingredient lookup, allocation grouping and transfer planning. Do not add custom attribute recursion, type-identity checks or temporary-attribute distinctions beyond native equality. Exact quantity checks still validate live transfer state and planned amounts, separately from native stack-kind equality. Assign shared client-local preview IDs once during world-catalogue construction and cache textures by preview ID and pixel size; do not serialize, encode or fingerprint stacks to manufacture cache keys. Normalize quantity to one for the native icon bake and render quantities separately using shared visible quantity-label textures. Ingredient-lookup memoization uses detached stack snapshots and native equality, and lasts for one evaluation only; its item-class/ID cache buckets do not restrict the native ingredient predicates searched. Draw cached previews in a shared shader pass, with one activation for all visible icons rather than switching programs per tile. The dedicated CraftingIconShaderProgram derives from the game's ShaderProgram and owns the mod's vertex and fragment stages rather than using the generic shader-program factory. All shader uniforms, including the texture sampler, are accessed through typed properties on the shader rather than string-based calls in the renderer. Preserve per-button and scroll-viewport clipping, output quantities, alternative indicators and native button input/focus. Panel disposal releases its meshes, text textures and shaders but not cached atlas allocations. Texture reloads invalidate the world-owned previews; shader reloads do not. World exit or mod-system disposal releases all owned atlas allocations, including late deferred completions. This presentation cache is not an inventory-result cache. Any deferred computation must use safe snapshots and discard stale results; game-owned mutable inventory data must remain on its owning thread.

The user removed mandatory vanilla/heavily modded benchmarking and measurement-derived performance thresholds. Verify bounded visible-tile rendering and change-driven inventory recomputation through focused tests; representative timing measurements are not an acceptance requirement. Do not infer untested runtime-cost or modpack-compatibility claims.

## Acceptance criteria

- Every eligible resolved grid-crafting output is discoverable through browsing and localized-name search; alternatives remain accessible.
- Inventory changes update relevance, missing quantities, and availability consistently, including quantities already in the grid.
- Unavailable supported tiles remain selectable and expose all missing requirements for one execution.
- Unsupported recipes remain discoverable and inspectable, show an explicit unknown/unsupported reason without invented scores or deficits, sort deterministically after supported recipes when selected, and cannot trigger placement.
- Ordinary clicks on supported recipes populate available ingredients for one execution, respecting shaped and shapeless layouts.
- Shift-click on supported recipes maximizes legal placement under the joint, lexicographic shared-allocation policy, including incomplete recipes, repeated ingredients, and reconsideration of preliminary variant choices before inventory-order tie-breaking.
- Wildcards, restricted alternatives, attribute-sensitive stacks, tools, non-stackable items, and output variants retain native semantics.
- A failed local preflight, including insufficient space for displaced contents, leaves the original state intact. Native execution cannot lose, duplicate, automatically drop, or incorrectly consume items; if later transfers are locally refused, earlier accepted moves may remain. The UI refreshes from current inventory and the game owns authoritative reconciliation without feature-level server-outcome tracking.
- Existing output-taking and manual crafting behavior continues to work after placement.
- Search focus, scrolling, tooltip display, panel closing, UI scale, and small-screen placement are usable in the live client.
- Multiplayer behavior is checked with authoritative inventory updates and competing inventory changes.

Future implementation should use focused automated tests for allocation and transfer invariants, followed by user-run live-client checks for layout, interaction, and synchronization. This proposal itself changes no code and establishes no tested runtime behavior.

## Pre-implementation decisions

- The user confirmed grid-only scope and quantity-based relevance.
- Eligible carried slots and exclusions are recorded against native sources in [CraftingMenu.todo](./CraftingMenu.todo), Phase 1; runtime permissions and capacity still require action-time validation.
- The user selected a compact alternative chooser with Auto or tile deselection releasing an explicit choice. Dynamic outputs use honest generic identities/previews, with placement unsupported unless native semantics or a verified adapter establish safe support.
- Placement uses native client inventory manager operations and sends their returned standard packets. The user approved partial-progress reconciliation and a local execution guard, not a custom server transaction or whole-batch acknowledgement. Phase 1 records the integration mechanism; later phases must implement and test it.
