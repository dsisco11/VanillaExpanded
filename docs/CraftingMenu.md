# Crafting-menu filtering and ordering

This guide describes the implemented filtering and ordering systems. Classification providers remain deferred until concrete consumers need them.

## Components and authority

- [CraftingMenuSession](../VanillaExpanded/src/CraftingMenu/CraftingMenuSession.cs) owns one [RecipeFilterSet](../VanillaExpanded/src/CraftingMenu/RecipeFilterSet.cs) and explicitly constructs the feature controllers. There is no global registry, discovery, or public extension API.
- [RecipeSort](../VanillaExpanded/src/CraftingMenu/RecipeSort.cs) owns a session-local immutable collection of ordering providers and the selected provider. Availability and Name are isolated `IRecipeOrder` implementations, independent of filter registration; the current fixed choices need no additional registration framework.
- [CraftingCatalogueView](../VanillaExpanded/src/CraftingMenu/CraftingCatalogueView.cs) resolves automatic or explicitly pinned recipe alternatives before filtering. The catalogue metadata is immutable for the loaded world.
- Providers implement [IRecipeFilter.Matches](../VanillaExpanded/src/CraftingMenu/IRecipeFilter.cs) over an already evaluated selection. All registered predicates must match; no registrations means no restriction. Predicates cannot rely on registration order or invocation count, and must not manufacture or reorder rows.
- Inventory allocation remains authoritative. Providers must not capture inventories, allocate, dispatch transfers, read GUI state or live stacks, or mutate the catalogue, evaluation, alternative, or another feature's criteria.

| Controller / predicate | Registration identity | Contribution |
| --- | --- | --- |
| [RecipeSearch / SearchFilter](../VanillaExpanded/src/CraftingMenu/RecipeSearch.cs) | `vanillaexpanded:search` | Normalize terms once per update; every term must match the selected recipe's immutable search text. Empty search removes this restriction. |
| [RecipeCategory / CategoryFilter](../VanillaExpanded/src/CraftingMenu/RecipeCategory.cs) | `vanillaexpanded:category` | Match the selected recipe's category snapshot, including Other. All removes this restriction. |
| [CraftableOnly / CraftableFilter](../VanillaExpanded/src/CraftingMenu/CraftableOnly.cs) | `vanillaexpanded:craftable-only` | Read `Evaluation.CanCraft`, not `CanPlace`, border color, relevance, or ingredient counts. One predicate instance belongs to the feature/session and is registered only while enabled. |

`CanPlace` means supported placement and allows partial ingredient placement; it is not proof of full craftability. Displayed eligibility never authorizes a transfer: [CraftingTransferCoordinator](../VanillaExpanded/src/CraftingMenu/CraftingTransferCoordinator.cs) captures and allocates again, validates the transfer plan and live inventory, then uses native transfers.

## Registration and updates

`Register(id, filter)` returns an owned `RecipeFilterSet.Registration`. Duplicate identities fail before mutation. Keep the handle in its feature controller; `Replace` changes only that registration atomically, and idempotent `Dispose` releases only its own registration, not a later registration reusing the identity.

Construct immutable criteria before registering or replacing them. Update the controller's state only after the registry mutation succeeds, then invoke the session's common change callback. Criteria changes project existing allocations without capturing inventory. An inventory refresh captures once, evaluates the catalogue once, and projects the refreshed selections.

The set enforces its owning client thread and rejects registry mutations or reentrant filtering during an eager projection pass. Predicates and criteria remain read-only during that pass. Registration errors and predicate exceptions propagate; never catch them to publish unfiltered or empty success-shaped results.

An active registration can call `FilterOthers(selections)` to evaluate every other registered predicate without removing or replacing itself. This eager diagnostic pass has the same thread, mutation, reentrancy, lifetime, and error guarantees as normal filtering; a stale handle cannot exclude a newer registration using the same identity.

To insert a feature:

1. Implement its pure selection predicate and a controller that owns its criteria and registration handle.
2. Explicitly construct the controller in the session with the existing set and common change callback. Add no provider-identity checks to filtering or reconciliation and no feature flags to catalogue query methods.
3. Bind native controls to that controller's state. Explicit filter changes reset the viewport through the existing tile reset path; recomposition restores controls without registering again.
4. Add predicate, registration/failure, composition, allocation-reuse, interaction, and native lifetime tests using the existing fixtures.

## Projection, interaction, and lifetime

The view filters resolved selections, applies the selected ordering provider, and always adds ordinal recipe stable identity as the final tie-breaker. Availability remains the default: supported first, then craftable, descending relevance, ascending missing units, and culture-aware case-insensitive display name. Name uses only the culture-aware case-insensitive display name before the common stable identity tie-breaker, regardless of supported/craftable status or stock.

The localized native Sort by dropdown sits beneath Craftable only, before tiles in tab order. Its label, both option names, and arrow/padding space contribute to minimum layout measurements. Native mouse and arrow-key selection use the same controller path. An explicit sort change cancels pending tile presses, resets scroll, ends the old interaction freeze, and projects existing allocations immediately. It preserves search, category, craftable-only, selected output, and pinned alternative. Selecting the already-active mode does nothing; unknown identities fail before changing selection or publishing rows. Sort changes neither capture inventory nor authorize transfers.

During interaction, the session refreshes evaluations for the exact displayed recipes and keeps still-eligible rows in relative order. Ineligible automatic alternatives use the current automatic candidate only if it passes every filter. Explicit pins are never substituted to satisfy a filter. Newly eligible groups append in canonical order without duplicates; explicit variant/Auto actions take effect immediately. Ending interaction restores canonical order and automatic selection. Only a completed projection replaces the published row snapshot.

A hidden pin survives search and craftable-only changes, subject to normal deselection/category behavior. Turning craftable-only off can expose it again; Auto is available only through a visible selected row. Hidden selections have disabled footer controls and no displayed tooltip.

The session publishes an empty-state diagnosis with its row snapshot. For empty results, the craftable-only controller evaluates the resolved selections through every other registered filter, including search and category. Surviving selections mean craftability is the blocker; otherwise an active search gets the search-specific message. A truly empty catalogue gets the general unavailable message without a misleading clear action. This pass reuses current evaluations and allocations and respects pinned alternatives; it neither captures inventory nor changes registrations. Ordering does not alter eligibility or empty-state diagnosis. Diagnostic failures leave the prior rows and diagnosis published and propagate normally.

The localized empty-state message and its one relevant action are centered horizontally and vertically as a group in the recipe viewport, with centered text, measured wrapping, and sufficient minimum space for translated labels. Without an action, the message alone is centered. Clear search uses the existing search input path, returns keyboard focus to search, and keeps craftable-only/category unchanged. Show uncraftable removes only the craftable restriction through its controller and synchronizes the toggle without firing a second handler. Both actions reset scroll through the existing safe reset path. Hidden actions are disabled, unfocused, and not rendered; live refresh, recomposition, and reopening update the message and action from session state.

[GuiDialogCraftingMenu](../VanillaExpanded/src/CraftingMenu/GuiDialogCraftingMenu.cs) uses native controls with measured localized layout. Search stays first in tab order. The native craftable-only toggle uses Enter; recipe tiles also accept Space and keypad Enter. Mouse activation requires the same output and recipe identities as the left-button press. Removal/rebinding cancels the press, including remove/reintroduce before release. Scroll and scrollbar positions clamp together after membership shrinks, even during a press; ordinary live refresh does not reset scroll unconditionally.

[GuiElementCraftingSearch](../VanillaExpanded/src/CraftingMenu/GuiElementCraftingSearch.cs) keeps native text editing in a narrower editable area inside a shared full-width field frame, reserving room for a filled-circle x on the inner right edge. The native input's text/caret clipping cannot overlap the icon. The icon and localized Clear search hint appear only for a nonempty enabled input, including whitespace-only text. A completed left click clears through the existing input callback, resets scroll, and retains input focus without changing other criteria or pins. Edits, focus loss, disabling, and release outside the icon cancel a pending clear. The icon adds no separate tab stop; normal native keyboard editing remains available. Recomposition disposes its texture and hover hint with the input.

The footer's Handbook button resolves the currently visible selected recipe at activation time, obtains a cloned output preview, and invokes the registered native `handbook` link protocol. Page codes use the collectible's `IHandBookPageCodeProvider` when present, otherwise `GuiHandbookItemStackPage.PageCodeForStack`, preserving relevant stack attributes. This action neither calls the placement coordinator nor changes selection, pins, or browsing criteria. No selection, hidden selection, or unresolved preview disables the button and removes its focus; an unavailable native protocol produces a localized in-game error. Layout measurement reserves space for the translated button label. The button uses ordinary native mouse/Enter activation; there is no added keyboard binding. The inventory and crafting menu stay open; the opened handbook receives focus, including when it was already open. The crafting menu shares the native handbook/inventory draw layer (`0.2`) so focus can bring the handbook in front.

Recomposition keeps the session, criteria, checked state, registrations, selected ordering provider, and scroll position; old tile subscriptions are removed with the old composer. Dialog disposal captures an immutable [CraftingMenuBrowsingState](../VanillaExpanded/src/CraftingMenu/CraftingMenuBrowsingState.cs) before tearing down the composer and disposing the session's filter set. The client system retains only category, exact search text, selected sort identity, and GUI-unscaled scroll offset from successfully opened dialogs. A fresh dialog restores the criteria and sort through their existing controllers, evaluates the current inventory, and then restores/clamps scroll in that ordering. No allocations, selected outputs, pins, focus, hover, presses, or registrations survive the old session.

The first menu in a world starts with All, empty search, Availability sorting, and zero scroll. Closing/reopening the inventory, parent-composer replacement, and explicit menu close/book-tab reopen retain browsing state. World exit, catalogue reinitialization, and system disposal clear it; nothing is written to disk. Craftable-only still starts off in each new dialog. The existing preference to reopen the menu with the inventory remains independent of browsing state. Failed opens do not replace the last successfully captured state.

## Tooltip ingredient coverage

[CraftingMenuTooltip](../VanillaExpanded/src/CraftingMenu/CraftingMenuTooltip.cs) adds one compact ingredient-name and have/need row per requirement beneath the native recipe grid. Exact ingredient names come from cloned resolved stacks and are rendered as literal richtext components, not parsed markup. Wildcard/tag alternatives use the localized "Matching ingredient" label rather than a misleading single-variant name or raw matching expression; the native grid still cycles acceptable choices. Names wrap to the measured tooltip width. Short count pairs use the native error-text red; satisfied pairs retain the normal text color.

Counts describe coverage for **one craft**, not total stock or Shift-placement quantities. Each requirement's have count is its required quantity minus the matching deficit in the selection's authoritative evaluation. Shared/repeated requirements therefore cannot double-count stock, and reusable tools contribute one only when usable under the allocator's durability rules. Unsupported or unevaluated selections omit counts and retain their existing warning; no separate inventory scan or diagnostic prose is added.

[GuiElementCraftingRecipeTooltip](../VanillaExpanded/src/CraftingMenu/GuiElementCraftingRecipeTooltip.cs) caches the semantic summary with the selected recipe identity and unsupported reason. Inventory refresh republishes the inspected selection through the existing tile callback. Changed counts rebuild and dispose the old popup; equivalent summaries retain the popup and native description resources even when allocations are newly captured or surplus stock changes.

## Verification scope

Provider, catalogue-view, interaction, transfer, lifecycle, and rendering regressions live in [the CraftingMenu unit tests](../VanillaExpanded.Tests/Unit/CraftingMenu/). Run the regression group from the repository root:

```powershell
dotnet test .\VanillaExpanded.Tests\VanillaExpanded.Tests.csproj --no-restore --filter "FullyQualifiedName~CraftingMenu" --verbosity minimal
```

These tests include native GUI fixtures, not a running game. Automated results and source review do not certify live control/layout behavior, inventory transitions in a running world, multiplayer, or recipe/modpack compatibility. Live-game validation of the provider-based filtering and craftable-only control remains pending; the earlier wooden-planks smoke test covered placement before these additions.
