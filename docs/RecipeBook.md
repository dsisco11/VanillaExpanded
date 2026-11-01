# Recipe Book design

This guide describes the Recipe Book's component ownership, evaluation pipeline, and client UI lifecycle. It is intended for developers maintaining the feature. Player-facing usage is covered in the [README](../README.md).

## Components and authority

- [RecipeBookSession](../VanillaExpanded/src/RecipeBook/RecipeBookSession.cs) owns filtering, sorting, selection, and the published recipe rows for one dialog.
- [CraftingCatalogueView](../VanillaExpanded/src/RecipeBook/CraftingCatalogueView.cs) resolves recipe alternatives and projects evaluated selections. Catalogue metadata is immutable for the loaded world.
- [CraftingAllocator](../VanillaExpanded/src/RecipeBook/CraftingAllocator.cs) evaluates ingredient coverage, craftability, placement support, and grid relevance from captured inventory.
- [GuiDialogRecipeBook](../VanillaExpanded/src/RecipeBook/GuiDialogRecipeBook.cs) binds controls to the session and owns their composition and interaction lifecycle.
- [CraftingTransferCoordinator](../VanillaExpanded/src/RecipeBook/CraftingTransferCoordinator.cs) independently validates and dispatches placement through native inventory transfers.

```mermaid
flowchart LR
    Inventory[Accessible inventory] --> Snapshot[Inventory snapshot]
    Snapshot --> Allocation[Evaluate recipe alternatives]
    Allocation --> Resolve[Resolve automatic or pinned alternatives]
    Resolve --> Filter[Apply all registered filters]
    Filter --> Order[Order eligible outputs]
    Order --> Rows[Publish rows and empty-state diagnosis]
    Rows --> Dialog[Recipe Book UI]
    Dialog -->|Placement request| Transfer[Recapture, allocate, and validate placement]
    Transfer --> Native[Native inventory transfers]
```

Displayed eligibility does not authorize a transfer. `CanPlace` permits supported partial ingredient placement; `CanCraft` means the ingredients for one craft are available. Placement always recaptures inventory and validates against live state.

## Filtering and ordering

### Filter ownership

The session explicitly constructs its feature controllers and one [RecipeFilterSet](../VanillaExpanded/src/RecipeBook/RecipeFilterSet.cs). Each controller owns its immutable criteria and registration handle. Filtering has no global registry, discovery mechanism, or public extension API.

| Controller / predicate | Registration identity | Contribution |
| --- | --- | --- |
| [RecipeSearch / SearchFilter](../VanillaExpanded/src/RecipeBook/RecipeSearch.cs) | `vanillaexpanded:search` | Every normalized search term must match the selected recipe's immutable search text. Empty search removes the restriction. |
| [RecipeCategory / CategoryFilter](../VanillaExpanded/src/RecipeBook/RecipeCategory.cs) | `vanillaexpanded:category` | Match the selected recipe's category snapshot, including Other. All removes the restriction. |
| [CraftableOnly / CraftableFilter](../VanillaExpanded/src/RecipeBook/CraftableOnly.cs) | `vanillaexpanded:craftable-only` | Match `Evaluation.CanCraft`. The controller registers its predicate only while enabled. |

All registered [IRecipeFilter](../VanillaExpanded/src/RecipeBook/IRecipeFilter.cs) predicates must match. Predicates read evaluated selections; they cannot capture inventories, allocate ingredients, dispatch transfers, read live stacks or GUI state, mutate other criteria, or reorder rows. Registration order and invocation count have no semantic meaning.

### Registration and updates

`Register(id, filter)` returns an owned registration. Duplicate identities fail before mutation. Its `Replace` operation changes only that registration atomically; idempotent disposal cannot remove a later registration that reuses the identity.

Controller updates commit the registration before changing controller state and invoking the session's common callback. Evaluation and projection have separate responsibilities:

```text
criteria change:
    construct immutable criteria
    commit registration and controller state
    project using existing evaluations

inventory change:
    capture accessible inventory once
    evaluate recipe alternatives once
    project using refreshed evaluations

projection succeeds:
    publish rows and empty-state diagnosis together
```

The filter set enforces its owning client thread and rejects registry mutation or reentrant filtering during an eager projection. Registration and predicate failures propagate without publishing a fallback result. `FilterOthers` evaluates every predicate except the calling registration under the same guarantees; stale handles cannot exclude a newer registration.

To add an internal filtering feature:

1. Implement a pure selection predicate and a controller that owns its criteria and registration.
2. Construct the controller explicitly in the session with the common change callback.
3. Bind controls to the controller. Explicit criteria changes reset recipe scrolling; recomposition restores controls without registering again.
4. Cover predicate behavior, registration failures, allocation reuse, interactions, and resource lifetime with the existing test fixtures.

### Ordering policies

[RecipeSort](../VanillaExpanded/src/RecipeBook/RecipeSort.cs) owns a session-local immutable collection of ordering providers and the selected provider. Each provider defines automatic alternative selection and output-row ordering independently of filter registration. Both orderings finish with ordinal recipe stable identity as a deterministic tie-breaker.

| Policy | Automatic alternative selection | Output-row ordering |
| --- | --- | --- |
| Availability (default) | Supported status, descending grid-kind matches, descending inventory coverage ratio, ascending missing units, stable identity. | Supported status, descending grid-kind matches, craftability, descending coverage ratio, ascending missing units, culture-aware case-insensitive display name, stable identity. |
| Name | Supported status, descending coverage ratio, ascending missing units, stable identity. | Culture-aware case-insensitive display name, stable identity. |

Filtering applies to the resolved alternative, not every alternative in an output group. Availability can therefore select a grid-relevant but incomplete alternative that Hide uncraftable excludes, even when another alternative is craftable. Explicit pins are never substituted to satisfy a filter.

An explicit sort change cancels pending tile presses, ends the interaction freeze, resets scrolling, and reprojects existing allocations. It preserves other criteria, selected output, and pinned alternative. Selecting the active policy is a no-op; unknown identities fail before mutation.

### Interaction reconciliation and empty results

While a recipe is being interacted with, refresh updates evaluations for the displayed recipes while retaining still-eligible rows in relative order. Newly eligible groups append in canonical order. Automatic alternatives may be substituted only when the replacement passes every filter; pins remain explicit. Variant and Auto actions take effect immediately. Ending interaction restores canonical ordering and automatic selection.

Only a completed projection replaces the published rows and empty-state diagnosis. The diagnosis resolves alternatives through the active ordering policy and uses the craftable-only controller's `FilterOthers` pass to distinguish craftability restrictions from search/category restrictions without recapturing inventory.

The empty-state UI binds to the published diagnosis. Its Clear search and Show uncraftable actions update only their owning controllers, preserving unrelated criteria. Clearing search restores search focus. An empty catalogue has no criteria-clearing action.

Hidden selections have disabled footer controls and no recipe tooltip. A hidden pin can survive search and craftable-only changes, subject to normal deselection and category behavior.

## Grid-item relevance

[CraftingInventorySnapshot](../VanillaExpanded/src/RecipeBook/CraftingInventorySnapshot.cs) exposes immutable `GridStacks` from the nine verified crafting input slots. These are references to captured source objects already included in the available supply, not a second inventory read or additional supply.

[GridRelevance](../VanillaExpanded/src/RecipeBook/GridRelevance.cs) groups inputs by native stack identity with quantity normalized to one, preserving meaningful persistent attributes and all source variants within a kind. Ingredient matching uses cloned stacks normalized to the requirement quantity and the same native predicates as allocation.

Relevance is the maximum bipartite matching between distinct input kinds and recipe requirements. Each kind and requirement contributes at most one point. Repeated stacks, grid position, and supplied quantity do not inflate the score.

The allocator publishes immutable `CraftingAllocation.GridMatches` after supported evaluation and required native validation. Availability uses the displayed alternative's own score; Name ignores relevance. A zero score gives no grid-relevance ranking advantage. Relevance is a display hint, not proof of craftability or placement permission.

Criteria changes reuse allocation scores; grid changes refresh through the normal inventory and interaction paths. Evaluation failures leave the prior published rows intact.

## Dialog lifecycle and state

The book belongs to its inventory dialog and closes with that parent. [GuiElementRecipeBookButton](../VanillaExpanded/src/RecipeBook/GuiElementRecipeBookButton.cs) is anchored to the crafting output slot and reflects the book's actual open state. Explicit opening requests search focus; automatic restoration opens without taking focus from the inventory. An unfocused book does not capture keyboard input or acquire focus during resizing.

The native titlebar provides two positioning modes:

- **Fixed** uses adaptive inventory-relative placement with a two-screen-pixel docking gap.
- **Movable** uses native dragging and remembered manual coordinates, clamped to the window.

Bounds use window-relative coordinates in both modes. The absence of a native saved manual position means Fixed; choosing Fixed clears that position. Fixed placement is computed from layout, not stored as a separate coordinate.

Recomposition retains the current session, criteria, selection, and scroll while replacing GUI resources. Dialog replacement instead captures immutable [RecipeBookBrowsingState](../VanillaExpanded/src/RecipeBook/RecipeBookBrowsingState.cs) and restores category, exact search text, sort identity, and GUI-unscaled scroll into a fresh session evaluated against current inventory.

| State | Lifetime |
| --- | --- |
| Category, search, sort, and scroll | Retained across dialog reopening/replacement within the loaded world; cleared on world exit or catalogue reinitialization. |
| Allocations, selected outputs, pins, focus, hover, presses, and filter registrations | Current session/dialog only. |
| Craftable-only | Off in each fresh dialog. |
| Reopen with inventory | Native client boolean setting `vanillaexpanded-recipebook-remember-open`, default true. |
| Movable position | Native per-dialog position persistence. |

The first dialog in a world starts with All, empty search, Availability, and zero scroll. Actual open state and browsing state are not persisted across client sessions. Failed opens do not replace the last successfully captured browsing state.

Automatic reopening is independent of browsing-state restoration. Disabling reopening leaves an open dialog and its criteria intact; explicitly closing the book also disables the reopening preference.

## Controls and layout

The dialog contains category tabs and a framed content panel with the selected category's localized heading, filtering controls, search, a recessed recipe list, and footer controls. The panel has eight-GUI-pixel inner padding.

Composition and minimum-size measurement use shared named dimensions for spacing, margins, and control chrome. Minimum width accommodates the complete category strip and localized headings and controls; minimum height reserves room for the heading and empty-state text. GUI-scaled dimensions are distinct from physical-pixel borders and docking gaps. [RecipeBookLayout](../VanillaExpanded/src/RecipeBook/RecipeBookLayout.cs) owns placement and preferred dialog dimensions; the options component owns popup-width measurement.

The [shared visual helpers](../VanillaExpanded/src/Gui/GuiControlVisuals.cs) expose opt-in palette, frame drawing, control measurement, and host-relative pixel snapping. Category tabs and their panel use the shared palette, scaled outlines, and snapping rules; tab measurements also govern strip sizing. The separate [visual dimensions](../VanillaExpanded/src/RecipeBook/RecipeBookVisualMetrics.cs) are not yet used by dialog composition. Other control appearance and layout remain governed by their existing components.

Search is the first keyboard tab stop. Native control focus and input routing are preserved, with the category strip acting as one outer tab stop.

### Category tabs

[GuiElementIconTabs](../VanillaExpanded/src/Gui/GuiElementIconTabs.cs) is a crafting-independent icon tab strip owned by a native child composer. Its 30-unit tabs have four-unit inter-tab gaps and no end spacing; the first tab is flush with the panel edge. [GuiElementIconTab](../VanillaExpanded/src/Gui/GuiElementIconTab.cs) retains native toggle interaction and renders rounded-top folder tabs. Selected tabs are 36 units high with a scaled upper accent and an open bottom edge joining the framed panel; inactive tabs are 32 units high. Idle, hover, and selected states have distinct background and icon tints without changing icon geometry.

All appears first. Present standard categories follow Construction, Decorative, Mechanics, Terrain, Tools, Clothing, and Items. Additional categories follow those standards, ordered by localized name with an ordinal category-code tie-breaker; Other appears last when present. This presentation order does not change catalogue membership, filtering, or code-based browsing restoration.

Category selection updates its controller and panel heading and resets recipe scrolling. Localized tooltips and native focus highlighting provide category identification. The Recipe Book reserves width for every tab; the shared component also supports selection wrapping and overflow navigation in narrower hosts.

[RecipeBookCategoryIcons](../VanillaExpanded/src/RecipeBook/RecipeBookCategoryIcons.cs) maps standard categories to monochrome [SVG assets](../VanillaExpanded/assets/vanillaexpanded/textures/gui/recipe-book-categories/); unknown categories use a generic tag. The client owns icon registrations, while each composer owns its rendered textures.

### Search, filtering, and options

[GuiElementRecipeBookSearch](../VanillaExpanded/src/RecipeBook/GuiElementRecipeBookSearch.cs) reserves an inner-right clear icon outside the native text/caret area. Its clear action uses the normal search callback and preserves input focus.

Hide uncraftable and Sort by bind directly to their feature controllers. Their localized tooltips explain craftability and ordering. Recomposition synchronizes controls without invoking change handlers.

[GuiElementRecipeBookOptions](../VanillaExpanded/src/RecipeBook/GuiElementRecipeBookOptions.cs) binds the titlebar's sliders control to the persisted reopening preference. Its dropdown owns its composer and textures, receives input before underlying controls, and dismisses on Escape, outside clicks, or focus loss.

### Recipes and handbook

[GuiElementRecipeBookTiles](../VanillaExpanded/src/RecipeBook/GuiElementRecipeBookTiles.cs) uses one viewport for tile layout, virtualization, clipping, hit testing, and scrolling. The viewport reaches the recessed list frame's inner vertical edges, and scroll/scrollbar positions clamp together as membership changes.

Recipe activation validates the output and recipe identities retained by the press; removal or rebinding cancels it. Mouse and keyboard activation share the session's placement path.

Handbook resolves the visible selected output through the native `handbook` link protocol, preserving relevant stack attributes. It does not place ingredients or change recipe selection. Missing selection or preview disables the button; unavailable protocol handling reports a localized error. The inventory and book stay open while the handbook receives focus.

## Rendering and tooltips

### Texture ownership and clipping

Category icons use identical integer raster/display sizes and pixel-snapped positions with nearest magnification. Hover and selection change tint without moving or resizing icons. Tab backgrounds share the panel's snapped baseline. Composers dispose their textures when replaced or closed.

Recipe tile backgrounds, icons, and output counts render inside the shared viewport scissor. Scissor scopes unwind on failure. Tile quads and scissors use matching pixel bounds; availability borders occupy the outer edge. Recipe overlays render below dropdowns and tooltips.

[RecipeBookIconCache](../VanillaExpanded/src/RecipeBook/RecipeBookIconCache.cs) is world-owned. It records missing previews during GUI preparation and submits baking requests in the Before render stage through native `RenderItemStackToAtlas`. The cap of four outstanding requests includes both pending and natively queued work. Native atlas rendering owns framebuffer and shader operations; previews can remain pending for two GUI frames.

Texture reload invalidates cached work, and late callbacks release results whose owners no longer exist. Submission failures propagate and leave the request retryable. World cleanup unregisters the renderer and releases its resources.

### Ingredient coverage and inspection

[RecipeBookTooltip](../VanillaExpanded/src/RecipeBook/RecipeBookTooltip.cs) combines the native recipe grid with one compact `have/need name` row per requirement. Coverage describes one craft and comes from authoritative evaluation deficits, so repeated/shared requirements cannot double-count stock. Reusable tools count only when usable under allocation rules.

Exact names use cloned resolved stacks rendered as literal richtext. Wildcard/tag requirements use the localized Matching ingredient label while the grid cycles accepted choices. Counts below requirement use the native error tint. Unsupported or unevaluated selections omit coverage counts.

[GuiElementRecipeBookTooltip](../VanillaExpanded/src/RecipeBook/GuiElementRecipeBookTooltip.cs) caches the semantic summary. Changed counts replace the popup; equivalent summaries retain it. Pointer movement takes precedence over retained tile focus, so leaving a tile hides inspection. Keyboard navigation restores focused-tile inspection until the next pointer movement.

## Verification

[Recipe Book unit tests](../VanillaExpanded.Tests/Unit/RecipeBook/) cover filtering, ordering, allocation, transfer, lifecycle, and rendering. [GUI unit tests](../VanillaExpanded.Tests/Unit/Gui/) cover the shared category tab component. From the repository root:

```powershell
dotnet test .\VanillaExpanded.Tests\VanillaExpanded.Tests.csproj --no-restore --filter "FullyQualifiedName~RecipeBook|FullyQualifiedName~GuiElementIconTabsTests" --verbosity minimal
```

Native GUI fixtures run without a live game or GPU context. Live-world layout, inventory transitions, multiplayer behavior, and recipe/modpack compatibility require separate in-game validation.
