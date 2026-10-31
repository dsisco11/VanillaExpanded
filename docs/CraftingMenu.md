# Crafting-menu filtering

This guide describes the implemented filtering system. The [approved proposal](CraftingMenuProvidersProposal.md) and [implementation plan](CraftingMenuProviders.todo) retain the delivery contract; ordering and classification provider systems remain deferred until concrete consumers need them.

## Components and authority

- [CraftingMenuSession](../VanillaExpanded/src/CraftingMenu/CraftingMenuSession.cs) owns one [RecipeFilterSet](../VanillaExpanded/src/CraftingMenu/RecipeFilterSet.cs) and explicitly constructs the feature controllers. There is no global registry, discovery, or public extension API.
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

To insert a feature:

1. Implement its pure selection predicate and a controller that owns its criteria and registration handle.
2. Explicitly construct the controller in the session with the existing set and common change callback. Add no provider-identity checks to filtering or reconciliation and no feature flags to catalogue query methods.
3. Bind native controls to that controller's state. Explicit filter changes reset the viewport through the existing tile reset path; recomposition restores controls without registering again.
4. Add predicate, registration/failure, composition, allocation-reuse, interaction, and native lifetime tests using the existing fixtures.

## Projection, interaction, and lifetime

The view filters resolved selections and retains the default order: supported first, then craftable, descending relevance, ascending missing units, culture-aware case-insensitive display name, and ordinal recipe stable identity.

During interaction, the session refreshes evaluations for the exact displayed recipes and keeps still-eligible rows in relative order. Ineligible automatic alternatives use the current automatic candidate only if it passes every filter. Explicit pins are never substituted to satisfy a filter. Newly eligible groups append in canonical order without duplicates; explicit variant/Auto actions take effect immediately. Ending interaction restores canonical order and automatic selection. Only a completed projection replaces the published row snapshot.

A hidden pin survives search and craftable-only changes, subject to normal deselection/category behavior. Turning craftable-only off can expose it again; Auto is available only through a visible selected row. Hidden selections have disabled footer controls and no displayed tooltip.

[GuiDialogCraftingMenu](../VanillaExpanded/src/CraftingMenu/GuiDialogCraftingMenu.cs) uses native controls with measured localized layout. Search stays first in tab order. The native craftable-only toggle uses Enter; recipe tiles also accept Space and keypad Enter. Mouse activation requires the same output and recipe identities as the left-button press. Removal/rebinding cancels the press, including remove/reintroduce before release. Scroll and scrollbar positions clamp together after membership shrinks, even during a press; ordinary live refresh does not reset scroll unconditionally.

The footer's Handbook button resolves the currently visible selected recipe at activation time, obtains a cloned output preview, and invokes the registered native `handbook` link protocol. Page codes use the collectible's `IHandBookPageCodeProvider` when present, otherwise `GuiHandbookItemStackPage.PageCodeForStack`, preserving relevant stack attributes. This action neither calls the placement coordinator nor changes selection, pins, or browsing criteria. No selection, hidden selection, or unresolved preview disables the button and removes its focus; an unavailable native protocol produces a localized in-game error. Layout measurement reserves space for the translated button label. The button uses ordinary native mouse/Enter activation; there is no added keyboard binding. The inventory and crafting menu stay open; the opened handbook receives focus, including when it was already open. The crafting menu shares the native handbook/inventory draw layer (`0.2`) so focus can bring the handbook in front.

Recomposition keeps the session, criteria, checked state, registrations, and scroll position; old tile subscriptions are removed with the old composer. Dialog disposal captures an immutable [CraftingMenuBrowsingState](../VanillaExpanded/src/CraftingMenu/CraftingMenuBrowsingState.cs) before tearing down the composer and disposing the session's filter set. The client system retains only category, exact search text, and GUI-unscaled scroll offset from successfully opened dialogs. A fresh dialog restores the criteria through their existing controllers, evaluates the current inventory, and then restores/clamps scroll. No allocations, selected outputs, pins, focus, hover, presses, or registrations survive the old session.

The first menu in a world starts with All, empty search, and zero scroll. Closing/reopening the inventory, parent-composer replacement, and explicit menu close/book-tab reopen retain browsing state. World exit, catalogue reinitialization, and system disposal clear it; nothing is written to disk. Craftable-only still starts off in each new dialog. The existing preference to reopen the menu with the inventory remains independent of browsing state. Failed opens do not replace the last successfully captured state.

## Verification scope

Provider, catalogue-view, interaction, transfer, lifecycle, and rendering regressions live in [the CraftingMenu unit tests](../VanillaExpanded.Tests/Unit/CraftingMenu/). Run the regression group from the repository root:

```powershell
dotnet test .\VanillaExpanded.Tests\VanillaExpanded.Tests.csproj --no-restore --filter "FullyQualifiedName~CraftingMenu" --verbosity minimal
```

These tests include native GUI fixtures, not a running game. Automated results and source review do not certify live control/layout behavior, inventory transitions in a running world, multiplayer, or recipe/modpack compatibility. Live-game validation of the provider-based filtering and craftable-only control remains pending; the earlier wooden-planks smoke test covered placement before these additions.
